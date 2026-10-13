using System.Text.Json;
using Anthropocene.Simulation;

static class ResidentStudy
{
    static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    public static void Run(string[] args)
    {
        string output=args[1];Directory.CreateDirectory(output);
        CandidateBridge.Mode="off";FusionBridge.Mode="off";
        if(args[0]=="trajectory"){Trajectory(output,args[2],args[3]);return;}
        if(args[0]=="validate"){Validate(output);return;}
        if(args[0]!="profile")throw new ArgumentException("Unknown resident study mode");
        using var profileBaseline=JsonDocument.Parse(File.ReadAllText("Tools/GpuEcology/fusion-results/native-cpu-50-turns.json"));
        var rows=new List<object>();
        foreach(string name in new[]{"Standard80","Huge240"})foreach(int turn in new[]{25,40,50})
        {
            GC.Collect();var w=SaveCodec.Load(File.ReadAllBytes($"Tools/GpuEcology/fusion-results/{name}-before-{turn}.anthro"));
            GC.Collect();PhaseProfile.Reset();PhaseProfile.Enabled=true;
            WholeTurnProbe.Measurement m;
            try{m=WholeTurnProbe.Step(w,1,false);}finally{PhaseProfile.Enabled=false;}
            m.Fingerprint=SaveCodec.Fingerprint(w);
            string expected=profileBaseline.RootElement.EnumerateArray().Single(x=>x.GetProperty("world").GetString()==name&&x.GetProperty("turn").GetInt32()==turn).GetProperty("measurement").GetProperty("Fingerprint").GetString();
            if(m.Fingerprint!=expected)throw new Exception("Profile replay changed authoritative CPU state");
            rows.Add(new{world=name,turn,workers=1,measurement=m,scopes=PhaseProfile.Rows(),counters=PhaseProfile.Counters()});
            File.WriteAllText(Path.Combine(output,"profile.json"),JsonSerializer.Serialize(rows,Json));
            Console.WriteLine($"PROFILE {name}/{turn} {m.TotalMs:F2}ms");w=null;GC.Collect();
        }
    }
    static void Validate(string output)
    {
        using var device=new ResidentDevice();ResidentBridge.Device=device;var rows=new List<object>();
        using var baseline=JsonDocument.Parse(File.ReadAllText("Tools/GpuEcology/fusion-results/native-cpu-50-turns.json"));
        foreach(string name in new[]{"Standard80","Huge240"})foreach(int turn in new[]{25,40,50})
        {
            byte[] bytes=File.ReadAllBytes($"Tools/GpuEcology/fusion-results/{name}-before-{turn}.anthro");
            ResidentBridge.Reset();ResidentBridge.Mode="cpu-cache";var w=SaveCodec.Load(bytes);
            var cpu=WholeTurnProbe.Step(w,4);w=null;ResidentBridge.DetachWorld();GC.Collect();
            string expected=baseline.RootElement.EnumerateArray().Single(x=>x.GetProperty("world").GetString()==name&&x.GetProperty("turn").GetInt32()==turn).GetProperty("measurement").GetProperty("Fingerprint").GetString();
            if(cpu.Fingerprint!=expected)throw new Exception("CPU shared cache changed authoritative CPU state");
            foreach(string mode in new[]{"gpu-split","gpu-fused","gpu-coeff"})
            {
                DeviceResetForValidation();
                w=SaveCodec.Load(bytes);ResidentBridge.Mode=mode;ResidentBridge.Validate=true;
                var m=WholeTurnProbe.Step(w,4);
                rows.Add(new{world=name,turn,mode,cpuFingerprintEqual=cpu.Fingerprint==m.Fingerprint,counters=ResidentBridge.Counters()});
                File.WriteAllText(Path.Combine(output,"validation.json"),JsonSerializer.Serialize(rows,Json));
                Console.WriteLine($"VALID {name}/{turn} {mode}: {ResidentBridge.Checked} projections, error={ResidentBridge.MaxError:E3}, exact={cpu.Fingerprint==m.Fingerprint}");
                w=null;ResidentBridge.DetachWorld();GC.Collect();
            }
            ResidentBridge.Validate=false;
        }
        ResidentBridge.Reset();ResidentBridge.Mode="off";
    }
    static void DeviceResetForValidation()=>ResidentBridge.Device.Reset();
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Warm(byte[] opening){ResidentBridge.Mode="off";WholeTurnProbe.Step(SaveCodec.Load(opening),4,false);}
    static void Trajectory(string output,string mode,string run)
    {
        var setup=System.Diagnostics.Stopwatch.StartNew();
        using var device=mode.StartsWith("gpu")?new ResidentDevice():null;ResidentBridge.Device=device;
        File.WriteAllText(Path.Combine(output,$"{mode}-{run}-setup.json"),JsonSerializer.Serialize(new{setupMs=setup.Elapsed.TotalMilliseconds},Json));
        using var cpu=JsonDocument.Parse(File.ReadAllText("Tools/GpuEcology/fusion-results/native-cpu-50-turns.json"));
        var baseline=cpu.RootElement.EnumerateArray().ToDictionary(x=>(x.GetProperty("world").GetString(),x.GetProperty("turn").GetInt32()),x=>x.Clone());
        var rows=new List<object>();
        foreach(string name in new[]{"Standard80","Huge240"})
        {
            byte[] opening=File.ReadAllBytes($"Tools/GpuEcology/whole-turn-results/{name}-opening.anthro");
            Warm(opening);ResidentBridge.Reset();GC.Collect();var w=SaveCodec.Load(opening);
            ResidentBridge.Mode=mode;ResidentBridge.Validate=run=="validate";
            int turns=run=="validate"?3:50;
            for(int turn=1;turn<=turns;turn++)
            {
                GC.Collect();var m=WholeTurnProbe.Step(w,4,false);m.Fingerprint=SaveCodec.Fingerprint(w);
                var b=baseline[(name,turn)];
                rows.Add(new{world=name,turn,mode,run,measurement=m,counters=ResidentBridge.Counters(),cpuFingerprintEqual=m.Fingerprint==b.GetProperty("measurement").GetProperty("Fingerprint").GetString(),population=w.Camps.Sum(c=>(long)c.Population),camps=w.Camps.Count,populationDrift=w.Camps.Sum(c=>(long)c.Population)-b.GetProperty("campPopulation").GetInt64(),campDrift=w.Camps.Count-b.GetProperty("camps").GetInt32()});
                File.WriteAllText(Path.Combine(output,$"trajectory-{mode}-{run}.json"),JsonSerializer.Serialize(rows,Json));
                Console.WriteLine($"RESIDENT {mode}/{run} {name} {turn}/{turns} {m.TotalMs:F2}ms prefetch={m.PrefetchMs:F2} hits={ResidentBridge.Hits} gpu={ResidentBridge.GpuHits} checked={ResidentBridge.Checked} error={ResidentBridge.MaxError:E3}");GC.Collect();
            }
            w=null;ResidentBridge.Reset();GC.Collect();
        }
    }
}
