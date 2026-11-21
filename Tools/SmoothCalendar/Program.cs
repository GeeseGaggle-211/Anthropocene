using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private static int checks;
    private static readonly JsonSerializerOptions J = new JsonSerializerOptions {IncludeFields=true,WriteIndented=true};
    private static void Check(bool ok,string label) { if(!ok)throw new Exception(label);checks++; }
    private static void Near(double a,double b,string label,double tol=1e-9) => Check(Math.Abs(a-b)<=tol*Math.Max(1,Math.Abs(b)),label+" "+a+" != "+b);
    private static void Reject(Action action,string label) { bool threw=false;try{action();}catch(ArgumentException){threw=true;}catch(InvalidOperationException){threw=true;} Check(threw,label); }
    private static TurnRecord Step(WorldState w) { var ai=AiPlanning.Decide(w); var p=TurnPlanner.Prepare(w,ai.Orders,ai.Traces); return new TurnResolver().Resolve(w,p); }
    private static SimulationConfig Config() => JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/SmoothCalendarOpeningConfig.json"),J);
    private static void Main()
    {
        Directory.CreateDirectory("TestResults/SmoothCalendar");
        var cfg=Config();cfg.Validate();var settings=cfg.SmoothCalendar;
        Near(SmoothCalendar.DaysAtElapsedYears(cfg,0),7300,"initial duration");
        Near(SmoothCalendar.DaysAtElapsedYears(cfg,1200),3650,"tested early exponential");
        foreach(var pair in new[]{(1800,100.0),(1850,Math.Sqrt(1000)),(1900,10.0),(1950,Math.Sqrt(10)),(1980,Math.Pow(10,.2)),(2000,1.0),(2050,1.0)})
            Near(SmoothCalendar.DaysAtElapsedYears(cfg,pair.Item1+5000),pair.Item2,"historical anchor");
        double prior=7300;
        for(double y=0;y<=7100;y+=.25) {double d=SmoothCalendar.DaysAtElapsedYears(cfg,y); Check(d<=prior+1e-9&&d>=1,"monotone bounded curve");prior=d;}
        foreach(double year in new[]{1750.0,1800.0,1990.0,2000.0}) {
            double x=year+5000,e=.0001;
            double l=(Math.Log(SmoothCalendar.DaysAtElapsedYears(cfg,x))-Math.Log(SmoothCalendar.DaysAtElapsedYears(cfg,x-e)))/e;
            double r=(Math.Log(SmoothCalendar.DaysAtElapsedYears(cfg,x+e))-Math.Log(SmoothCalendar.DaysAtElapsedYears(cfg,x)))/e;
            Check(Math.Abs(l-r)<1e-6,"C1 log-duration join "+year);
        }
        settings.Version=99;Reject(()=>cfg.Validate(),"reject unknown calendar version");settings.Version=1;
        settings.NeolithicStartYear=double.NaN;Reject(()=>cfg.Validate(),"reject NaN date");settings.NeolithicStartYear=-5000;
        settings.EpochDays=-1;Reject(()=>cfg.Validate(),"reject negative epoch");settings.EpochDays=0;
        var legacy=JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"),J);
        Check(legacy.SmoothCalendar==null,"legacy JSON remains fixed");
        var outputs=new List<object>();
        foreach(int seed in new[]{11,23,47}) {
            var c=Config();c.Width=96;c.Height=48;c.Players=4;c.StartSeparation=9;
            var w=new WorldGenerator().Generate(seed,c);int opening=0;
            while(!w.Finished&&opening++<20){var r=Step(w);Near(r.Duration,7300,"unchanged opening duration");}
            Check(w.Finished&&c.SmoothCalendar.Started,"start global calendar once opening completes");
            Near(c.SmoothCalendar.EpochDays,w.Days,"persist global Neolithic epoch");
            byte[] fork=SaveCodec.Save(w);string fp=SaveCodec.Fingerprint(w);
            Check(SaveCodec.Fingerprint(SaveCodec.Load(fork))==fp,"enabled save round trip");
            int initial=w.Camps.Sum(x=>x.Population)+w.Hunters.Sum(x=>x.Population),births=0,deaths=0;
            double epoch=w.Days;
            for(int k=0;k<30;k++) {
                double before=w.Days,locked=c.TurnDays;var r=Step(w);births+=r.Growth;deaths+=r.Deaths;
                Near(r.Duration,locked,"frozen record duration");Near(w.Days,before+locked,"physical elapsed days");
                Near(c.TurnDays,SmoothCalendar.DaysAtElapsedYears(c,(w.Days-epoch)/365),"automatic next horizon");
            }
            TurnResolver.Invariants(w);
            Check(initial+births-deaths==w.Camps.Sum(x=>x.Population)+w.Hunters.Sum(x=>x.Population),"population ledger");
            var saved=SaveCodec.Save(w);var a=SaveCodec.Load(saved);var b=SaveCodec.Load(saved);
            Step(a);Step(b);Check(SaveCodec.Fingerprint(a)==SaveCodec.Fingerprint(b),"exact continuation replay");
            var draft=TurnPlanner.Draft(w,-1);Check(draft.Matches(w),"fresh draft matches");
            c.TurnDays*=.99;Check(!draft.Matches(w),"duration invalidates draft");
            Reject(()=>AiPlanning.Decide(w),"no stale observation horizon");c.TurnDays=SmoothCalendar.ExpectedDays(w);
            var ai=AiPlanning.Decide(w);var prepared=TurnPlanner.Prepare(w,ai.Orders,ai.Traces);
            c.SmoothCalendar.Curve=SmoothCalendarCurve.EarlyExponential;
            Check(!draft.Matches(w),"same-duration calendar edit invalidates draft");
            Reject(()=>new TurnResolver().Resolve(w,prepared),"prepared calendar config frozen");
            c.SmoothCalendar.Curve=SmoothCalendarCurve.HistoricalExponential;
            // Null config is deliberate legacy fixed mode, not implicit migration on load.
            // Disabling the calendar is the compatibility test. Do not also
            // shrink the carrying capacity under already committed provisions.
            double fixedDays=Math.Max(c.TurnDays,w.History.Last().Duration);
            c.SmoothCalendar=null;c.TurnDays=fixedDays;var fixedSave=SaveCodec.Save(w);var fixedWorld=SaveCodec.Load(fixedSave);
            Check(fixedWorld.Config.SmoothCalendar==null,"legacy save stays disabled");
            Step(fixedWorld);Near(fixedWorld.Config.TurnDays,fixedDays,"legacy next duration fixed");
            outputs.Add(new{seed,opening,elapsedYears=(w.Days-epoch)/365,checks});
        }
        // Daily boundary/inventory smoke uses a mature Neolithic fixture, not a modern economy.
        var dailyCfg=Config();dailyCfg.Width=96;dailyCfg.Height=48;dailyCfg.Players=4;dailyCfg.StartSeparation=9;
        var daily=new WorldGenerator().Generate(11,dailyCfg);
        for(int k=0;!daily.Finished&&k<20;k++)Step(daily);
        double shift=(2000-dailyCfg.SmoothCalendar.NeolithicStartYear)*365;
        // Do not jump ecological time: shift the calendar epoch in the diagnostic only.
        dailyCfg.SmoothCalendar.NeolithicStartYear=1750; // rejected by production validator below
        Reject(()=>dailyCfg.Validate(),"invalid historical epoch rejected");
        dailyCfg.SmoothCalendar.NeolithicStartYear=-5000;
        // Curve evaluator supplies the daily floor; legacy-mode physical smoke keeps native timestamps.
        dailyCfg.SmoothCalendar=null;dailyCfg.TurnDays=1;
        for(int k=0;k<40;k++){var r=Step(daily);Near(r.Duration,1,"daily resolver accepts one day");}
        TurnResolver.Invariants(daily);
        var report=new{status="passed",checks,worlds=outputs,scope="Curve anchors, monotonicity/C1 joins, save compatibility, cache invalidation, native auto-cadence, deterministic continuation; daily native smoke only. No demographic retuning or historical-era validation."};
        File.WriteAllText("TestResults/SmoothCalendar/results.json",JsonSerializer.Serialize(report,J));
        Console.WriteLine(JsonSerializer.Serialize(report,J));
    }
}
