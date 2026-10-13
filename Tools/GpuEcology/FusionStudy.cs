using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Anthropocene.Simulation;
using ILGPU.Runtime;

// Research only: isolated production fusion. All other candidate backends stay off.
static class FusionStudy
{
    static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    static readonly string[] Names={"Standard80","Huge240"};
    static string Output;
    static void Save(string name,object value)=>File.WriteAllText(Path.Combine(Output,name),JsonSerializer.Serialize(value,Json));
    static string Snapshot(string name,int turn)=>Path.Combine(Output,$"{name}-before-{turn}.anthro");
    sealed class Errors
    {
        public long Count;public double Max,Sum,Squares;public long DiscreteMismatches;
        public double Mean=>Count==0?0:Sum/Count;public double Rms=>Count==0?0:Math.Sqrt(Squares/Count);
        public void Add(double a,double b,string label,bool discrete=false)
        {
            if(!double.IsFinite(a)||!double.IsFinite(b))throw new Exception("Nonfinite "+label);
            double e=Math.Abs(a-b);Count++;Max=Math.Max(Max,e);Sum+=e;Squares+=e*e;
            if(discrete&&a!=b)DiscreteMismatches++;
            if((discrete&&a!=b)||e>1e-8+1e-10*Math.Max(Math.Abs(a),Math.Abs(b)))throw new Exception($"{label}: {a:R} != {b:R}");
        }
    }
    static void Compare(FusionCase c,double[] state,FusionReceipt[] receipts,Errors errors)
    {
        for(int i=0;i<state.Length;i++)errors.Add(c.Expected[i],state[i],$"camp{c.Camp}/state{i}",i==83||i==84);
        for(int i=0;i<c.Commands.Length;i++)
        {
            var a=c.ExpectedReceipts[i];var b=receipts[i];
            errors.Add(a.Amount>1e-12?1:0,b.Amount>1e-12?1:0,$"camp{c.Camp}/receipt{i}/exists",true);
            errors.Add(a.Amount,b.Amount,$"camp{c.Camp}/receipt{i}/output");errors.Add(a.Used,b.Used,"labor");
            errors.Add(a.Wood,b.Wood,"wood");errors.Add(a.Clay,b.Clay,"clay");errors.Add(a.Hand,b.Hand,"hand");errors.Add(a.Tools,b.Tools,"tools");
        }
    }
    static void Configure(string mode)
    {
        CandidateBridge.Mode="off";CandidateBridge.Capture=false;CandidateBridge.Check=false;CandidateBridge.ResolutionOnly=true;
        FusionBridge.Mode=mode;FusionBridge.Counters.Clear();CandidateBridge.Reset();FusionBridge.Cases.Clear();
        if(mode=="per-call"){FusionBridge.Mode="off";CandidateBridge.Mode="Production";}
    }
    public static void Run(string[] args)
    {
#if !RESEARCH_HOOKS
        throw new Exception("Fusion study requires TurnProbe research hooks");
#else
        if(args.Length<2)throw new ArgumentException("--fusion-study validate|bench|matched|trajectory <output> [backend run-id]");
        Output=args[1];Directory.CreateDirectory(Output);
        var setup=Stopwatch.StartNew();using var gpu=new FusionDevice(args[0]=="bench"?16384:1);FusionBridge.Device=gpu;
        Save(args[0]+"-setup.json",new{gpu=gpu.Gpu.Name,setupMs=setup.Elapsed.TotalMilliseconds,availableManagedBytes=GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,commandBytes=Marshal.SizeOf<FusionCommand>(),stateDoubles=FusionKernel.Stride,receiptBytes=Marshal.SizeOf<FusionReceipt>()});
        if(args[0]=="device")return;
        if(args[0]=="boundary"){Save("boundary-checks.json",FusionBoundaryChecks.Run());return;}
        if(args[0]=="validate")Validate();else if(args[0]=="bench")Bench();else if(args[0]=="matched")Matched();else if(args[0]=="compact-matched")Matched(true);
        else if(args[0]=="trajectory")Trajectory(args[2],args[3]);else throw new ArgumentException("Unknown fusion study");
#endif
    }
    static void Validate()
    {
        var results=new List<object>();var all=new List<FusionCase>();
        foreach(var name in Names)foreach(int turn in new[]{25,40,50})
        {
            Configure("capture");var w=SaveCodec.Load(File.ReadAllBytes(Snapshot(name,turn)));
            var m=WholeTurnProbe.Step(w,4);
            using var baseline=JsonDocument.Parse(File.ReadAllText(Path.Combine(Output,"native-cpu-50-turns.json")));
            string expected=baseline.RootElement.EnumerateArray().Single(x=>x.GetProperty("world").GetString()==name&&x.GetProperty("turn").GetInt32()==turn).GetProperty("measurement").GetProperty("Fingerprint").GetString();
            if(m.Fingerprint!=expected)throw new Exception("Capture hook changed CPU state");
            var cases=FusionBridge.Cases.ToArray();if(cases.Length==0)throw new Exception("No eligible fusion cases");
            var counts=new Dictionary<string,long>(FusionBridge.Counters);all.AddRange(cases);
            foreach(string mode in new[]{"packed-cpu","resident","fused","fused-compact"})
            {
                var errors=new Errors();
                foreach(var c in cases)
                {
                    var state=(double[])c.Initial.Clone();int transferred=mode=="fused-compact"?c.Commands.Length:FusionKernel.MaxCommands;var commands=new FusionCommand[transferred];c.Commands.CopyTo(commands,0);var receipts=new FusionReceipt[transferred];
                    if(mode=="packed-cpu")for(int j=0;j<c.Commands.Length;j++)receipts[j]=FusionCpu.Execute(state,0,commands[j]);
                    else if(c.Commands.Length>0)FusionBridge.Device.Run(state,commands,new[]{c.Commands.Length},receipts,mode);
                    Compare(c,state,receipts,errors);
                }
                results.Add(new{world=name,turn,mode,cases=cases.Length,counts,errors});Save("validation.json",results);
                Console.WriteLine($"VALID {name} {turn} {mode}: {cases.Length} camps, max {errors.Max:E3}");
            }
            w=null;expected=null;GC.Collect();
        }
        Save("cases.json",all);
    }
    static double Median(double[] a){var b=(double[])a.Clone();Array.Sort(b);return b[b.Length/2];}
    static void Bench()
    {
        var cases=JsonSerializer.Deserialize<FusionCase[]>(File.ReadAllText(Path.Combine(Output,"cases.json")),Json);
        var rows=new List<object>();var device=FusionBridge.Device;
        foreach(int n in new[]{1,16,64,256,1024,4096,16384})
        {
            var initial=new double[n*FusionKernel.Stride];var commands=new FusionCommand[n*FusionKernel.MaxCommands];var counts=new int[n];
            for(int i=0;i<n;i++){var c=cases[i%cases.Length];c.Initial.CopyTo(initial,i*FusionKernel.Stride);c.Commands.CopyTo(commands,i*FusionKernel.MaxCommands);counts[i]=c.Commands.Length;}
            var state=(double[])initial.Clone();
            foreach(string mode in new[]{"packed-cpu","packed-cpu4","packed-cpu4-dense","resident","fused","fused-dense"})
            {
                int stride=mode.EndsWith("-dense")?counts.Max():FusionKernel.MaxCommands;
                var modeCommands=new FusionCommand[n*stride];var modeReceipts=new FusionReceipt[n*stride];
                for(int i=0;i<n;i++)Array.Copy(commands,i*FusionKernel.MaxCommands,modeCommands,i*stride,counts[i]);
                var total=new double[7];double upload=0,kernel=0,download=0;
                for(int rep=-1;rep<7;rep++)
                {
                    Array.Copy(initial,state,initial.Length);var clock=Stopwatch.StartNew();
                    if(mode.StartsWith("packed-cpu4"))Parallel.For(0,n,new ParallelOptions{MaxDegreeOfParallelism=4},i=>{for(int j=0;j<counts[i];j++)modeReceipts[i*stride+j]=FusionCpu.Execute(state,i*FusionKernel.Stride,modeCommands[i*stride+j]);});
                    else if(mode=="packed-cpu")for(int i=0;i<n;i++)for(int j=0;j<counts[i];j++)modeReceipts[i*stride+j]=FusionCpu.Execute(state,i*FusionKernel.Stride,modeCommands[i*stride+j]);
                    else device.Run(state,modeCommands,counts,modeReceipts,mode);
                    if(rep>=0)total[rep]=clock.Elapsed.TotalMilliseconds;
                }
                if(!mode.StartsWith("packed-cpu"))
                {
                    var uploads=new double[5];var kernels=new double[5];var downloads=new double[5];
                    for(int rep=0;rep<5;rep++)
                    {
                        Array.Copy(initial,state,initial.Length);var clock=Stopwatch.StartNew();device.Upload(state,modeCommands,counts);device.Gpu.Synchronize();uploads[rep]=clock.Elapsed.TotalMilliseconds;
                        using var start=device.Gpu.DefaultStream.AddProfilingMarker();device.Launch(n,mode,counts.Max());using var end=device.Gpu.DefaultStream.AddProfilingMarker();end.Synchronize();kernels[rep]=end.MeasureFrom(start).TotalMilliseconds;
                        clock.Restart();device.Download(state,modeReceipts);downloads[rep]=clock.Elapsed.TotalMilliseconds;
                    }
                    upload=Median(uploads);kernel=Median(kernels);download=Median(downloads);
                }
                // Check every lane after timing against the actual CPU-captured receipts/state.
                var errors=new Errors();for(int i=0;i<n;i++)Compare(cases[i%cases.Length],state.Skip(i*FusionKernel.Stride).Take(FusionKernel.Stride).ToArray(),modeReceipts.Skip(i*stride).Take(stride).ToArray(),errors);
                rows.Add(new{independentCampSequences=n,mode,medianMs=Median(total),samplesMs=total,uploadMs=upload,kernelMs=kernel,downloadMs=download,stagedTransferFraction=mode.StartsWith("packed-cpu")?0:(upload+download)/(upload+kernel+download),
                    sequencePerSecond=1000*n/Median(total),commands=counts.Sum(),commandStride=stride,launches=mode.StartsWith("packed-cpu")?0:mode=="resident"?counts.Max():1,bytes=(long)n*(FusionKernel.Stride*8+stride*Marshal.SizeOf<FusionCommand>()+4+FusionKernel.Stride*8+stride*Marshal.SizeOf<FusionReceipt>()),errors});
                Save("phase-benchmark.json",rows);Console.WriteLine($"BENCH {n} {mode} {Median(total):F4} ms");
            }
        }
    }
    static void Matched(bool compactOnly=false)
    {
        using var perCall=new CandidateDevice(1);CandidateBridge.Device=perCall;var rows=new List<object>();
        foreach(var name in Names)foreach(int turn in compactOnly?new[]{50}:new[]{25,40,50})
        {
            byte[] input=File.ReadAllBytes(Snapshot(name,turn));
            foreach(int repeat in new[]{-1,0,1,2})
            {
                var modes=compactOnly?new[]{"off","fused","fused-compact"}:new[]{"off","packed-cpu","per-call","resident","fused"};if(repeat%2==1)Array.Reverse(modes);
                foreach(string mode in modes)
                {
                    Configure(mode);GC.Collect();var w=SaveCodec.Load(input);GC.Collect();long before=GC.GetTotalAllocatedBytes(false);
                    var m=WholeTurnProbe.Step(w,4,false);long allocated=GC.GetTotalAllocatedBytes(false)-before;
                    var state=new{campPopulation=w.Camps.Sum(c=>(long)c.Population),camps=w.Camps.Count,technologyUnlocks=w.Societies.Sum(s=>s.NeolithicTechnologies.Count)};
                    if(repeat>=0){m.Fingerprint=SaveCodec.Fingerprint(w);rows.Add(new{world=name,turn,repeat,mode,measurement=m,allocatedBytes=allocated,state,counts=new Dictionary<string,long>(FusionBridge.Counters),perCallCounts=CandidateBridge.Counts.ToDictionary(x=>x.Key,x=>x.Value)});Save(compactOnly?"compact-matched.json":"matched.json",rows);}
                    Console.WriteLine($"MATCHED {name} {turn} {repeat} {mode} {m.TotalMs:F2} ms");w=null;
                }
            }
        }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Warm(byte[] opening) { WholeTurnProbe.Step(SaveCodec.Load(opening),4,false); }
    static void Trajectory(string mode,string run)
    {
        var rows=new List<object>();
        foreach(var name in Names)
        {
            byte[] opening=File.ReadAllBytes($"Tools/GpuEcology/whole-turn-results/{name}-opening.anthro");
            Configure(mode);Warm(opening);GC.Collect();var w=SaveCodec.Load(opening);
            for(int turn=1;turn<=50;turn++)
            {
                Configure(mode);GC.Collect();long before=GC.GetTotalAllocatedBytes(false);var m=WholeTurnProbe.Step(w,4,false);long allocated=GC.GetTotalAllocatedBytes(false)-before;
                m.Fingerprint=SaveCodec.Fingerprint(w);
                rows.Add(new{world=name,turn,mode,run,measurement=m,allocatedBytes=allocated,counts=new Dictionary<string,long>(FusionBridge.Counters),campPopulation=w.Camps.Sum(c=>(long)c.Population),camps=w.Camps.Count,technologyUnlocks=w.Societies.Sum(s=>s.NeolithicTechnologies.Count),production=w.History.Last().Production.Sum(p=>p.Output)});
                Save($"trajectory-{mode}-{run}.json",rows);Console.WriteLine($"TRAJECTORY {mode}/{run} {name} {turn}/50 {m.TotalMs:F2} ms");
                GC.Collect();
            }
            w=null;GC.Collect();
        }
    }
}
