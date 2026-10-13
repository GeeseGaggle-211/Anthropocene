using System.Diagnostics;
using System.Text.Json;
using Anthropocene.Simulation;

static class Program
{
    static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Warm(byte[] bytes) { WholeTurnProbe.Step(SaveCodec.Load(bytes),4,false); }
    static void Main(string[] args)
    {
        if(args.Length<2)throw new ArgumentException("baseline <output> | --fusion-study <mode> <output> [backend run]");
#if RESEARCH_HOOKS
        if(args[0]=="--resident-study"){ResidentStudy.Run(args.Skip(1).ToArray());return;}
        if(args[0]=="--fusion-study"){FusionStudy.Run(args.Skip(1).ToArray());return;}
#endif
        if(args[0]!="baseline")throw new ArgumentException("Unknown native command");
        string output=args[1];Directory.CreateDirectory(output);var rows=new List<object>();
        Directory.CreateDirectory("Tools/GpuEcology/whole-turn-results");
        foreach(var spec in new[]{(Name:"Standard80",Width:1024,Height:512,Players:80),(Name:"Huge240",Width:2048,Height:1024,Players:240)})
        {
            string opening=$"Tools/GpuEcology/whole-turn-results/{spec.Name}-opening.anthro";
            if(!File.Exists(opening))
            {
                Console.WriteLine("Generating "+spec.Name);var created=WholeTurnProbe.Generate(spec.Width,spec.Height,spec.Players);
                File.WriteAllBytes(opening,SaveCodec.Save(created));created=null;GC.Collect();
            }
            byte[] bytes=File.ReadAllBytes(opening);Warm(bytes);GC.Collect();var w=SaveCodec.Load(bytes);
            for(int turn=1;turn<=50;turn++)
            {
                bool checkpoint=turn==25||turn==40||turn==50;
                if(checkpoint)File.WriteAllBytes(Path.Combine(output,$"{spec.Name}-before-{turn}.anthro"),SaveCodec.Save(w));
                GC.Collect();
                long before=GC.GetTotalAllocatedBytes(false);var m=WholeTurnProbe.Step(w,4,false);long allocated=GC.GetTotalAllocatedBytes(false)-before;
                m.Fingerprint=SaveCodec.Fingerprint(w);
                if(checkpoint)File.WriteAllBytes(Path.Combine(output,$"{spec.Name}-after-{turn}.anthro"),SaveCodec.Save(w));
                rows.Add(new{world=spec.Name,turn,measurement=m,allocatedBytes=allocated,managedBytes=GC.GetTotalMemory(false),workingSetBytes=Process.GetCurrentProcess().WorkingSet64,campPopulation=w.Camps.Sum(c=>(long)c.Population),camps=w.Camps.Count,technologyUnlocks=w.Societies.Sum(s=>s.NeolithicTechnologies.Count),production=w.History.Last().Production.Sum(p=>p.Output)});
                File.WriteAllText(Path.Combine(output,"native-cpu-50-turns.json"),JsonSerializer.Serialize(rows,Json));
                GC.Collect();
                Console.WriteLine($"CPU {spec.Name} {turn}/50 {m.TotalMs:F2} ms");
            }
            w=null;bytes=null;GC.Collect();
        }
    }
}
