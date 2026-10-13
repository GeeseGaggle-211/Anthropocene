using System.Collections.Concurrent;
using System.Diagnostics;
using Anthropocene.Simulation;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Algorithms;

sealed class ResidentDevice:IDisposable
{
    const int Capacity=131072;
    readonly Context context;readonly Accelerator gpu;
    readonly MemoryBuffer1D<ResidentLane,Stride1D.Dense> lanes;
    readonly MemoryBuffer1D<ResidentProfile,Stride1D.Dense> profiles;
    readonly MemoryBuffer1D<ResidentCycle,Stride1D.Dense> cycles;
    readonly Action<Index1D,ArrayView<ResidentLane>,ArrayView<ResidentProfile>,ResidentConfig> fused;
    readonly Action<Index1D,ArrayView<ResidentLane>,ArrayView<ResidentProfile>,ResidentConfig,int> split;
    readonly Action<Index1D,ArrayView<ResidentProfile>,ArrayView<ResidentCycle>,ResidentConfig,int> buildCycles;
    readonly Action<Index1D,ArrayView<ResidentLane>,ArrayView<ResidentProfile>,ArrayView<ResidentCycle>,ResidentConfig> coefficientFused;
    WorldState boundWorld;SimulationConfig boundConfig;string boundMode;
    public readonly Dictionary<int,int> Slots=new();
    public double UploadMs,KernelMs,DownloadMs;public long Bytes;
    public ResidentDevice()
    {
        context=Context.Create(b=>b.Cuda().EnableAlgorithms().Profiling());gpu=context.CreateCudaAccelerator(0);
        lanes=gpu.Allocate1D<ResidentLane>(Capacity);profiles=gpu.Allocate1D<ResidentProfile>(Capacity*4);
        cycles=gpu.Allocate1D<ResidentCycle>(Capacity);
        fused=gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<ResidentLane>,ArrayView<ResidentProfile>,ResidentConfig>(ResidentKernel.Fused);
        split=gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<ResidentLane>,ArrayView<ResidentProfile>,ResidentConfig,int>(ResidentKernel.Split);
        buildCycles=gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<ResidentProfile>,ArrayView<ResidentCycle>,ResidentConfig,int>(ResidentKernel.BuildCycles);
        coefficientFused=gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<ResidentLane>,ArrayView<ResidentProfile>,ArrayView<ResidentCycle>,ResidentConfig>(ResidentKernel.CoefficientFused);
    }
    public void Reset(){Slots.Clear();boundWorld=null;boundConfig=null;boundMode=null;}
    public void Bind(WorldState w,string mode)
    {
        if(boundWorld==null){boundWorld=w;boundConfig=w.Config;boundMode=mode;}
        if(!ReferenceEquals(boundWorld,w)||!ReferenceEquals(boundConfig,w.Config)||boundMode!=mode)throw new InvalidOperationException("Reset resident profiles before replacing a world/config/backend. Static forcing must remain immutable during the run.");
    }
    public int Slot(WorldState w,int tile,List<ResidentProfile> added)
    {
        if(Slots.TryGetValue(tile,out int slot))return slot;
        if(Slots.Count>=Capacity)return -1;
        slot=Slots.Count;Slots.Add(tile,slot);var t=w.Tiles[tile];var v=new EcologyView(t){CultivationCapital=0};
        for(int j=0;j<4;j++)
        {
            var k=new EcologyCapacity(w,t,v,j);
            added.Add(new(){Food=k.Food,Biomass=k.Biomass,Water=k.Water,FoodRate=k.FoodRate,BiomassRate=k.BiomassRate,WaterRate=k.WaterRate,Moisture=t.Seasons[j].Moisture,Salt=t.Seasons[j].MaxSaltwater});
        }
        return slot;
    }
    public void Run(ResidentLane[] input,ResidentProfile[] added,int firstSlot,ResidentConfig cfg,string mode)
    {
        UploadMs=KernelMs=DownloadMs=0;Bytes=0;
        if(input.Length==0)return;
        var clock=Stopwatch.StartNew();
        if(added.Length>0)profiles.View.SubView(firstSlot*4,added.Length).CopyFromCPU(added);
        lanes.View.SubView(0,input.Length).CopyFromCPU(input);gpu.Synchronize();UploadMs=clock.Elapsed.TotalMilliseconds;
        using var begin=gpu.DefaultStream.AddProfilingMarker();
        if(mode=="gpu-coeff"&&added.Length>0)buildCycles(added.Length/4,profiles.View,cycles.View,cfg,firstSlot);
        if(mode=="gpu-split")for(int phase=0;phase<3;phase++)split(input.Length,lanes.View,profiles.View,cfg,phase);
        else if(mode=="gpu-coeff")coefficientFused(input.Length,lanes.View,profiles.View,cycles.View,cfg);
        else fused(input.Length,lanes.View,profiles.View,cfg);
        using var end=gpu.DefaultStream.AddProfilingMarker();end.Synchronize();KernelMs=end.MeasureFrom(begin).TotalMilliseconds;
        clock.Restart();lanes.View.SubView(0,input.Length).CopyToCPU(input);gpu.Synchronize();DownloadMs=clock.Elapsed.TotalMilliseconds;
        Bytes=(long)input.Length*System.Runtime.InteropServices.Marshal.SizeOf<ResidentLane>()*2+(long)added.Length*System.Runtime.InteropServices.Marshal.SizeOf<ResidentProfile>();
    }
    public void Dispose(){cycles.Dispose();lanes.Dispose();profiles.Dispose();gpu.Dispose();context.Dispose();}
}
static class ResidentBridge
{
    public static string Mode="off";public static ResidentDevice Device;public static bool Validate;
    static WorldState world;static double query;
    static readonly ConcurrentDictionary<int,byte> known=new();
    sealed class Entry{public EcologyView Before,After;public double Operation;public bool Gpu;}
    static readonly ConcurrentDictionary<int,Entry> cache=new();
    public static long Hits,GpuHits,Misses,Queries,Prefetched,NewProfiles,Checked,Values;
    public static double PrefetchMs,MaxError,ErrorSum,ErrorSquares;
    public static string Stage="";
    public static readonly ConcurrentDictionary<string,long> StageHits=new();
    public static void Reset(){world=null;known.Clear();cache.Clear();Device?.Reset();MaxError=ErrorSum=ErrorSquares=0;Checked=Values=0;}
    // Validation loads multiple full-world clones. Release the prior clone while
    // retaining its query IDs as an explicit, untimed coverage fixture.
    public static void DetachWorld(){world=null;cache.Clear();Device?.Reset();}
    static bool Eligible(Tile t)=>t.Land&&t.LandCondition==1&&t.AquaticCondition==1&&t.CurrentVegetation==t.NaturalVegetationPotential&&!t.Fishery;
    static bool Same(EcologyView a,EcologyView b)=>a.Food==b.Food&&a.PotentialFood==b.PotentialFood&&a.Freshwater==b.Freshwater&&a.Saltwater==b.Saltwater&&a.Days==b.Days&&a.CultivationCapital==b.CultivationCapital&&a.LandCondition==b.LandCondition&&a.AquaticCondition==b.AquaticCondition&&a.CurrentVegetation==b.CurrentVegetation;
    public static void Begin(WorldState w)
    {
        if(Mode=="off")return;
        world=w;query=w.Days;cache.Clear();Hits=GpuHits=Misses=Queries=Prefetched=NewProfiles=0;StageHits.Clear();
        var clock=Stopwatch.StartNew();
        if(Mode.StartsWith("gpu"))
        {
            Device.Bind(w,Mode);
            int firstSlot=Device.Slots.Count;var added=new List<ResidentProfile>();var ids=new List<int>();var input=new List<ResidentLane>();
            // Predict only from prior actual query IDs. New/unpredicted reads take CPU fallback.
            foreach(int id in known.Keys.OrderBy(x=>x))
            {
                var t=w.Tiles[id];if(!Eligible(t)||t.LastEcologyUpdateDays>query)continue;
                int slot=Device.Slot(w,id,added);if(slot<0)continue;
                ids.Add(id);input.Add(new(){Slot=slot,Food=t.Food,Biomass=t.PotentialFood,Water=t.Freshwater,Days=t.LastEcologyUpdateDays,Capital=t.CultivationCapital,Operation=t.CultivationOperatingFraction,Query=query});
            }
            var packed=input.ToArray();var c=w.Config;
            Device.Run(packed,added.ToArray(),firstSlot,new(){BaseDays=c.BaseDays,DaysPerYear=c.DaysPerYear,HalfLife=c.Neolithic.DecayHalfLifeYears,ReferenceIntensity=c.ReferenceIntensity,Displacement=c.CultivationVegetationDisplacementStart,MoistureReference=c.CropMoistureReference,MoistureExponent=c.CropMoistureExponent,Flux=c.CultivationRegenFlux},Mode);
            for(int i=0;i<ids.Count;i++)
            {
                int id=ids[i];var t=w.Tiles[id];var before=new EcologyView(t);var s=packed[i];var after=before;
                after.Food=s.Food;after.PotentialFood=s.Biomass;after.Freshwater=s.Water;after.CultivationCapital=s.Capital;after.Days=s.Days;after.Saltwater=s.Salt;
                if(Validate)Compare(after,LazyEcology.ProjectEcologyReference(w,id,query,true,null));
                cache[id]=new(){Before=before,After=after,Operation=t.CultivationOperatingFraction,Gpu=true};
            }
            Prefetched=ids.Count;NewProfiles=added.Count/4;
        }
        PrefetchMs=clock.Elapsed.TotalMilliseconds;
    }
    public static bool Try(WorldState w,int tile,double days,out EcologyView result)
    {
        result=default;if(Mode=="off"||!ReferenceEquals(w,world)||days!=query)return false;
        Interlocked.Increment(ref Queries);var t=w.Tiles[tile];if(!Eligible(t))return false;
        known.TryAdd(tile,0);
        if(cache.TryGetValue(tile,out var e)&&e.Operation==t.CultivationOperatingFraction&&Same(e.Before,new EcologyView(t)))
        {
            result=e.After;Interlocked.Increment(ref Hits);if(e.Gpu)Interlocked.Increment(ref GpuHits);
            StageHits.AddOrUpdate(Stage,1,(_,n)=>n+1);return true;
        }
        Interlocked.Increment(ref Misses);return false;
    }
    public static void Record(WorldState w,int tile,double days,EcologyView before,EcologyView result)
    {
        if(Mode=="off"||!ReferenceEquals(w,world)||days!=query||!Eligible(w.Tiles[tile]))return;
        cache[tile]=new(){Before=before,After=result,Operation=w.Tiles[tile].CultivationOperatingFraction};
    }
    static void Compare(EcologyView a,EcologyView b)
    {
        if(a.Days!=b.Days||a.CurrentVegetation!=b.CurrentVegetation)throw new Exception("Resident discrete mismatch");
        foreach(var pair in new[]{(a.Food,b.Food),(a.PotentialFood,b.PotentialFood),(a.Freshwater,b.Freshwater),(a.CultivationCapital,b.CultivationCapital),(a.Saltwater,b.Saltwater)})
        {
            double error=Math.Abs(pair.Item1-pair.Item2);if(!double.IsFinite(error)||error>1e-8+1e-10*Math.Max(Math.Abs(pair.Item1),Math.Abs(pair.Item2)))throw new Exception($"Resident float mismatch {pair.Item1:R} / {pair.Item2:R}");
            MaxError=Math.Max(MaxError,error);ErrorSum+=error;ErrorSquares+=error*error;Values++;
        }
        Checked++;
    }
    public static object Counters()=>new{Queries,Hits,GpuHits,Misses,Prefetched,NewProfiles,PrefetchMs,uploadMs=Mode.StartsWith("gpu")?Device.UploadMs:0,kernelMs=Mode.StartsWith("gpu")?Device.KernelMs:0,downloadMs=Mode.StartsWith("gpu")?Device.DownloadMs:0,bytes=Mode.StartsWith("gpu")?Device.Bytes:0,stageHits=StageHits.ToDictionary(x=>x.Key,x=>x.Value),Checked,MaxError,meanError=Values>0?ErrorSum/Values:0,rmsError=Values>0?Math.Sqrt(ErrorSquares/Values):0};
}
