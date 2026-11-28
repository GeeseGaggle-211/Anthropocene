using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// Read-only extraction from complete native saves, with no replay or overrides.
internal static class CivicFeedbackEvidence
{
    private static string Group(CivicTurnReport c) => c.ComplexityAfter <= .05 ? "incipient" :
        c.StratificationAfter < -.25 ? "egalitarian" : c.StratificationAfter > .25 ? "stratified" : "mixed";
    public static void Run(string[] args)
    {
        string Arg(string key,string fallback) => args.FirstOrDefault(a=>a.StartsWith(key+"="))?.Substring(key.Length+1) ?? fallback;
        string baseline=Arg("--baseline","TestResults/CivicPlaythrough"), treatment=Arg("--treatment","TestResults/CivicFeedback");
        var runs=new List<object>();
        foreach(int seed in new[]{11,23,47,101,1729})
        foreach(string variant in new[]{"linear","feedback"})
        {
            string dir=seed==101 || seed==1729 ? "b" : "a";
            string root=variant=="linear" ? baseline : treatment;
            var w=SaveCodec.Load(File.ReadAllBytes($"{root}/{dir}/seed-{seed}-annual-final.anthro"));
            var initial=SaveCodec.Load(File.ReadAllBytes($"TestResults/CivicAudit/baseline/seed-{seed}-checkpoint.anthro"));
            var history=w.History.Where(r=>r.StartDays>=initial.Days-1e-7).ToArray();
            if(Math.Abs(history.Sum(r=>r.Duration)-(w.Days-initial.Days))>1e-5) throw new Exception("Incomplete history");
            var exposure=new Dictionary<string,double>(); var splits=new Dictionary<string,int>();
            var lastPole=new Dictionary<int,(string group,double year,double p)>();
            var reversals=new List<object>(); var fissions=new List<object>();
            foreach(var r in history)
            {
                double year=(r.StartDays+r.Duration-initial.Days)/w.Config.DaysPerYear;
                var civics=r.CivicReports.ToDictionary(c=>c.Camp);
                foreach(var c in r.CivicReports)
                {
                    string g=Group(c);
                    exposure.TryGetValue(g,out double time); exposure[g]=time+r.Duration/w.Config.DaysPerYear;
                    if(g=="incipient") { lastPole.Remove(c.Camp); continue; }
                    if(g=="mixed") continue;
                    if(lastPole.TryGetValue(c.Camp,out var previous) && previous.group!=g)
                        reversals.Add(new {camp=c.Camp,from=previous.group,to=g,fromYear=previous.year,year,fromP=previous.p,p=c.StratificationAfter});
                    lastPole[c.Camp]=(g,year,c.StratificationAfter);
                }
                foreach(var f in r.Fissions)
                {
                    string g=Group(civics[f.ParentCamp]); splits.TryGetValue(g,out int n); splits[g]=n+1;
                    fissions.Add(new {year,group=g,f.ParentCamp,f.ParentBefore,f.Daughter,f.Trigger});
                }
            }
            runs.Add(new {seed,variant,turns=history.Length,exposure,splits,reversals,fissions});
            Console.WriteLine($"history seed={seed} variant={variant} reversals={reversals.Count} fissions={fissions.Count}");
        }
        var cfg=new SimulationConfig { Civic=new CivicGovernanceSettings {Enabled=true,StratificationFeedbackStrength=.2} };
        var probes=new List<object>();
        foreach(double start in new[]{-.4,-.02,.02,.4})
        foreach(double shock in new[]{0.0,-.35,.35})
        {
            double p=start;
            for(int year=0;year<=300;year++)
            {
                probes.Add(new {start,shock,year,p});
                p=CivicGovernance.AdvanceStratification(cfg,p,year<150 ? 0 : shock,1);
            }
        }
        string output=Arg("--output","TestResults/CivicFeedback/report/history.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output,JsonSerializer.Serialize(new {runs,probes},new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));
    }
}
