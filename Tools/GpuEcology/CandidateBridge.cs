using System.Collections.Concurrent;
using Anthropocene.Simulation;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Algorithms;

sealed record CandidateTrace(CandidateKind Kind,CandidateInput Input,CandidateOutput Expected);
sealed class CandidateDevice:IDisposable
{
    readonly Context context;
    public readonly Accelerator Gpu;
    readonly Action<Index1D,ArrayView<CandidateInput>,ArrayView<CandidateOutput>>[] kernels;
    MemoryBuffer1D<CandidateInput,Stride1D.Dense> input;
    MemoryBuffer1D<CandidateOutput,Stride1D.Dense> output;
    public CandidateDevice(int capacity=1)
    {
        context=Context.Create(b=>b.Cuda().EnableAlgorithms().Profiling());Gpu=context.CreateCudaAccelerator(0);
        kernels=new[]{
            Gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<CandidateInput>,ArrayView<CandidateOutput>>(CandidateKernels.PopulationKernel),
            Gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<CandidateInput>,ArrayView<CandidateOutput>>(CandidateKernels.ProductionKernel),
            Gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<CandidateInput>,ArrayView<CandidateOutput>>(CandidateKernels.HerdKernel),
            Gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<CandidateInput>,ArrayView<CandidateOutput>>(CandidateKernels.CapitalKernel),
            Gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<CandidateInput>,ArrayView<CandidateOutput>>(CandidateKernels.StockKernel)};
        Ensure(capacity);
    }
    public void Ensure(int n){if(input!=null&&input.Length>=n)return;input?.Dispose();output?.Dispose();input=Gpu.Allocate1D<CandidateInput>(n);output=Gpu.Allocate1D<CandidateOutput>(n);}
    public void Upload(CandidateInput[] p)=>input.View.SubView(0,p.Length).CopyFromCPU(p);
    public void Launch(CandidateKind kind,int n)=>kernels[(int)kind](n,input.View,output.View);
    public void Download(CandidateOutput[] p){output.View.SubView(0,p.Length).CopyToCPU(p);Gpu.Synchronize();}
    public CandidateOutput[] Run(CandidateKind kind,CandidateInput[] p){if(p.Length==0)return Array.Empty<CandidateOutput>();Ensure(p.Length);var result=new CandidateOutput[p.Length];Upload(p);Launch(kind,p.Length);Download(result);return result;}
    public void Dispose(){output?.Dispose();input?.Dispose();Gpu.Dispose();context.Dispose();}
}
static class CandidateBridge
{
    public static string Mode="off";
    public static bool Capture,Check;
    public static HashSet<CandidateKind> Qualified=new();
    public static bool ResolutionOnly,InResolution;
    public static CandidateDevice Device;
    public static readonly List<CandidateTrace> Traces=new();
    static readonly int[] Kept=new int[5];
    public static readonly ConcurrentDictionary<string,long> Counts=new();
    static readonly object Gate=new();
    public static bool Enabled(CandidateKind kind)=>(!ResolutionOnly||InResolution)&&((Mode=="all"&&Qualified.Contains(kind))||Mode==kind.ToString());
    public static void Reset(){Traces.Clear();Counts.Clear();Array.Clear(Kept);}
    public static CandidateOutput Apply(CandidateKind kind,CandidateInput input,Func<CandidateOutput> reference)
    {
        Counts.AddOrUpdate(kind.ToString(),1,(_,n)=>n+1);
        if(!Enabled(kind))
        {
            var cpu=reference();if(Capture)lock(Gate){if(Kept[(int)kind]++<10000)Traces.Add(new(kind,input,cpu));}return cpu;
        }
        CandidateOutput value;lock(Gate)value=Device.Run(kind,new[]{input})[0];
        if(Check){var error=new CandidateError();error.Add(reference(),value);error.Require(kind.ToString());}
        return value;
    }
    public static CandidateInput PackPopulation(WorldState w,Camp camp,TurnNeeds needs,double years)=>new(){
        A=needs.ConsumptionRequired,B=needs.ConsumptionTarget+needs.ReserveFillTarget,C=camp.Food,D=needs.OpeningCampFood,
        E=needs.PlantClothingFeedstock,F=camp.PlantFood,G=needs.AnimalClothingFeedstock,H=camp.AnimalFood,I=needs.ReserveTarget,
        J=QoLMortality.MaximumRate(w,camp),K=years,L=needs.PlantFeedstock,M=needs.AnimalFeedstock,N=camp.GrowthRemainder,Count=needs.FoodConsumers};
    public static CandidateOutput PackGrowth(GrowthPlan plan,double remainder)=>new(){A=plan.Required,B=plan.ConsumptionRequired,C=plan.GrowthFood,D=plan.ReproductivePopulation,E=plan.FinalPopulation,F=plan.ManufacturingInput,G=plan.PlantManufacturingInput,H=plan.AnimalManufacturingInput,Discrete=(int)Math.Floor(remainder+Math.Max(0,plan.FinalPopulation-plan.ReproductivePopulation))};
    public static GrowthPlan UnpackGrowth(CandidateOutput p)=>new(){Required=p.A,ConsumptionRequired=p.B,GrowthFood=p.C,ReproductivePopulation=p.D,FinalPopulation=p.E,ManufacturingInput=p.F,PlantManufacturingInput=p.G,AnimalManufacturingInput=p.H};
    public static CandidateInput PackProduction(Camp c,CraftRecipe r,double amount)=>new(){A=amount,B=c.Inventory.Stone,C=r.Stone,D=c.Inventory.Clay,E=r.Clay,F=c.Inventory.Wood,G=r.Wood,H=c.PlantFood,I=r.PlantFood,J=c.AnimalFood,K=r.AnimalFood,L=c.Inventory.AnimalFabric-c.TradeHeldForExport(CraftGood.AnimalFabric),M=r.AnimalFabric,N=c.Inventory.HousingMaterial-c.TradeHeldForExport(CraftGood.HousingMaterial),O=r.HousingMaterial};
    public static double Breed(int animals,double remainder,double rate,double breeding,double years,double nutrition)
    {
        if(!Capture&&!Enabled(CandidateKind.Herd))return (animals+remainder)*Math.Exp(rate*breeding*years*nutrition);
        var p=new CandidateInput{Count=animals,A=remainder,B=rate,C=breeding,D=years,E=nutrition};
        return Apply(CandidateKind.Herd,p,()=>{double target=(animals+remainder)*Math.Exp(rate*breeding*years*nutrition);return new(){A=target,B=target-Math.Floor(target),Discrete=Math.Max(0,(int)Math.Floor(target)-animals)};}).A;
    }
    public static double FisheryCapital(WorldState world,FisherySite site,double elapsedYears,double operation)
    {
        if(!Capture&&!Enabled(CandidateKind.FisheryCapital))return NeolithicCapital.Decay(world.Config,site.Capital,operation,elapsedYears);
        var p=new CandidateInput{A=site.Capital,B=operation,C=elapsedYears,D=-Math.Log(2)/world.Config.Neolithic.DecayHalfLifeYears};
        return Apply(CandidateKind.FisheryCapital,p,()=>new(){A=NeolithicCapital.Decay(world.Config,site.Capital,operation,elapsedYears)}).A;
    }
    public static StockFlow AquaticStock(double initial,double capacity,double rate,double request,double days)=>AquaticManagedStock(initial,capacity,rate,0,request,days);
    public static StockFlow AquaticManagedStock(double initial,double capacity,double rate,double inflow,double request,double days)
    {
        if(!Capture&&!Enabled(CandidateKind.FisheryStock))return inflow>0?EcologyMath.ManagedStock(initial,capacity,rate,inflow,request,days):EcologyMath.Stock(initial,capacity,rate,request,days);
        var p=new CandidateInput{A=initial,B=capacity,C=rate,D=request,E=days,F=inflow};
        var result=Apply(CandidateKind.FisheryStock,p,()=>PackStock(inflow>0?EcologyMath.ManagedStock(initial,capacity,rate,inflow,request,days):EcologyMath.Stock(initial,capacity,rate,request,days)));
        return new StockFlow(result.A,result.B,result.C,result.D,result.E,result.F);
    }
    public static CandidateOutput PackStock(StockFlow s)=>new(){A=s.Stock,B=s.Realized,C=s.Unmet,D=s.Recharge,E=s.Overflow,F=s.DepletionDays};
}
sealed class CandidateError
{
    public long Values,Failures,Discrete,Nonfinite;
    public double Max,Sum,Squares;
    public double Mean=>Values==0?0:Sum/Values;
    public double Rms=>Values==0?0:Math.Sqrt(Squares/Values);
    public void Add(CandidateOutput a,CandidateOutput b)
    {
        var x=new[]{a.A,a.B,a.C,a.D,a.E,a.F,a.G,a.H};var y=new[]{b.A,b.B,b.C,b.D,b.E,b.F,b.G,b.H};
        for(int k=0;k<x.Length;k++)
        {
            Values++;if(double.IsPositiveInfinity(x[k])&&double.IsPositiveInfinity(y[k]))continue;
            if(!double.IsFinite(x[k])||!double.IsFinite(y[k])){Failures++;Nonfinite++;continue;}
            double e=Math.Abs(x[k]-y[k]);Max=Math.Max(Max,e);Sum+=e;Squares+=e*e;
            if(e>1e-8+1e-10*Math.Max(Math.Abs(x[k]),Math.Abs(y[k])))Failures++;
        }
        if(a.Discrete!=b.Discrete)Discrete++;
    }
    public void Require(string name){if(Failures+Discrete>0)throw new Exception($"{name}: {Failures} float failures, {Discrete} discrete mismatches");}
}
namespace Anthropocene.Simulation
{
    public partial class TurnResolver
    {
        internal static CandidateOutput CandidatePopulationReference(CandidateInput p)
        {
            var w=new WorldState{Config=new SimulationConfig{AnnualHumanGrowthRate=p.J,QoLMortalityEnabled=false}};
            var camp=new Camp{Population=p.Count,PlantFood=p.F,AnimalFood=p.H,AquaticFood=p.C-p.F-p.H,DemographicGrowthMultiplier=1,GrowthRemainder=p.N};
            var needs=new TurnNeeds{FoodConsumers=p.Count,ConsumptionRequired=p.A,ConsumptionTarget=p.B,OpeningCampFood=p.D,PlantClothingFeedstock=p.E,AnimalClothingFeedstock=p.G,ReserveTarget=p.I,PlantFeedstock=p.L,AnimalFeedstock=p.M};
            return CandidateBridge.PackGrowth(PlanGrowth(w,camp,0,p.K,needs),p.N);
        }
        internal static Dictionary<int,GrowthPlan> CandidatePopulationPlans(WorldState w,double intervals,double years,Dictionary<int,TurnNeeds> needs)
        {
            var camps=w.Camps.OrderBy(c=>c.Id).ToArray();var p=camps.Select(c=>CandidateBridge.PackPopulation(w,c,needs[c.Id],years)).ToArray();
            var result=new Dictionary<int,GrowthPlan>();
            if(CandidateBridge.Enabled(CandidateKind.Population))
            {
                var outputs=CandidateBridge.Device.Run(CandidateKind.Population,p);
                for(int i=0;i<camps.Length;i++)
                {
                    if(CandidateBridge.Check){var e=new CandidateError();e.Add(CandidateBridge.PackGrowth(PlanGrowth(w,camps[i],intervals,years,needs[camps[i].Id]),camps[i].GrowthRemainder),outputs[i]);e.Require("Population");}
                    result[camps[i].Id]=CandidateBridge.UnpackGrowth(outputs[i]);
                }
                CandidateBridge.Counts.AddOrUpdate("Population",camps.Length,(_,n)=>n+camps.Length);
            }
            else for(int i=0;i<camps.Length;i++)
            {
                var c=camps[i];var original=PlanGrowth(w,c,intervals,years,needs[c.Id]);
                CandidateBridge.Apply(CandidateKind.Population,p[i],()=>CandidateBridge.PackGrowth(original,c.GrowthRemainder));result[c.Id]=original;
            }
            return result;
        }
    }
}


