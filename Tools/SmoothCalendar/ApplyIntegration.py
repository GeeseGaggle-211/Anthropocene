#!/usr/bin/env python3
"""One-shot branch-only materializer; CI removes this file after committing actual sources."""
from pathlib import Path
import hashlib, json, uuid
ROOT=Path(__file__).resolve().parents[2]
def put(path,text):
 p=ROOT/path;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text,encoding='utf-8')
def replace(path,old,new):
 p=ROOT/path;s=p.read_text(encoding='utf-8')
 if s.count(old)!=1: raise RuntimeError('Ambiguous integration anchor: '+path+' '+old[:80])
 p.write_text(s.replace(old,new),encoding='utf-8')
S='Assets/_Anthropocene/Simulation/'
put(S+'SmoothCalendar.cs',r'''using System;
using System.Runtime.Serialization;

namespace Anthropocene.Simulation
{
    // Save identity: a null SimulationConfig.SmoothCalendar retains fixed TurnDays.
    public enum SmoothCalendarCurve { EarlyExponential = 1, HistoricalExponential = 2 }

    [Serializable]
    public sealed class SmoothCalendarSettings
    {
        public int Version = 1;
        public SmoothCalendarCurve Curve = SmoothCalendarCurve.HistoricalExponential;
        // Presentation/calibration epoch, not a technology unlock or real-world date claim.
        public double NeolithicStartYear = -5000;
        public bool Started;
        public double EpochDays;
        [OnDeserializing]
        private void Defaults(StreamingContext context)
        { Version = 1; Curve = SmoothCalendarCurve.HistoricalExponential; NeolithicStartYear = -5000; }
        public void Validate(SimulationConfig config)
        {
            if (Version != 1 || (Curve != SmoothCalendarCurve.EarlyExponential && Curve != SmoothCalendarCurve.HistoricalExponential))
                throw new ArgumentException("Unsupported smooth-calendar version/curve.");
            if (!SmoothCalendar.Finite(EpochDays) || EpochDays < 0 || !SmoothCalendar.Finite(NeolithicStartYear))
                throw new ArgumentException("Invalid smooth-calendar epoch.");
            if (config.DaysPerYear != 365 || config.MinDays > 1 || config.MaxDays < 20 * config.DaysPerYear)
                throw new ArgumentException("Smooth calendar v1 requires 365 days/year and bounds covering 1 day to 20 years.");
            if (NeolithicStartYear >= 1750 || NeolithicStartYear < -50000)
                throw new ArgumentException("Neolithic calendar epoch must precede the industrial join.");
            if (Curve == SmoothCalendarCurve.HistoricalExponential)
            {
                double first = Math.Log(20 * config.DaysPerYear) - SmoothCalendar.EarlyRate * (1750 - NeolithicStartYear);
                double last = Math.Log(100.0);
                double secant = (first - last) / 50.0;
                // The matching endpoint tangents must define a monotone Hermite join.
                if (!(secant > 0) || SmoothCalendar.EarlyRate / secant + SmoothCalendar.ModernRate / secant > 3)
                    throw new ArgumentException("Calendar epoch does not admit the monotone industrial join; recalibrate its anchors explicitly.");
            }
        }
    }

    // Value snapshot, not object identity: edits to an existing config invalidate plans.
    public readonly struct CalendarBoundaryStamp : IEquatable<CalendarBoundaryStamp>
    {
        private readonly bool enabled, started;
        private readonly int version, curve;
        private readonly double epoch, startYear, duration, yearDays;
        internal CalendarBoundaryStamp(SimulationConfig c)
        {
            var s = c.SmoothCalendar; enabled = s != null; started = s != null && s.Started;
            version = s == null ? 0 : s.Version; curve = s == null ? 0 : (int)s.Curve;
            epoch = s == null ? 0 : s.EpochDays; startYear = s == null ? 0 : s.NeolithicStartYear;
            duration = c.TurnDays; yearDays = c.DaysPerYear;
        }
        public bool Equals(CalendarBoundaryStamp b) => enabled == b.enabled && started == b.started &&
            version == b.version && curve == b.curve && epoch == b.epoch && startYear == b.startYear &&
            duration == b.duration && yearDays == b.yearDays;
        public override bool Equals(object other) => other is CalendarBoundaryStamp b && Equals(b);
        public override int GetHashCode() => version ^ curve ^ duration.GetHashCode() ^ epoch.GetHashCode();
    }

    public static class SmoothCalendar
    {
        public static readonly double EarlyRate = Math.Log(2.0) / 1200.0;
        public static readonly double ModernRate = Math.Log(10.0) / 100.0;
        internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        public static CalendarBoundaryStamp Stamp(WorldState world) => new CalendarBoundaryStamp(world.Config);
        private static double Hermite(double x, double x0, double x1, double y0, double y1, double m0, double m1)
        {
            double h = x1-x0, t = (x-x0)/h, t2=t*t, t3=t2*t;
            return (2*t3-3*t2+1)*y0+(t3-2*t2+t)*h*m0+(-2*t3+3*t2)*y1+(t3-t2)*h*m1;
        }
        // Pure O(1) calendar evaluation. No world tick, stock mutation, RNG or era scan.
        public static double DaysAtElapsedYears(SimulationConfig config, double elapsedYears)
        {
            var s = config.SmoothCalendar;
            if (s == null) return config.TurnDays;
            if (!Finite(elapsedYears) || elapsedYears < 0) throw new ArgumentOutOfRangeException(nameof(elapsedYears));
            s.Validate(config);
            double early = Math.Log(20 * config.DaysPerYear) - EarlyRate * elapsedYears;
            if (s.Curve == SmoothCalendarCurve.EarlyExponential) return Math.Max(1, Math.Exp(early));
            double year = s.NeolithicStartYear + elapsedYears;
            double logDays;
            if (year <= 1750) logDays = early;
            else if (year < 1800)
            {
                double left = Math.Log(20 * config.DaysPerYear) - EarlyRate * (1750-s.NeolithicStartYear);
                logDays = Hermite(year,1750,1800,left,Math.Log(100),-EarlyRate,-ModernRate);
            }
            else if (year <= 1990) logDays = Math.Log(10) - ModernRate * (year-1900);
            else if (year < 2000) logDays = Hermite(year,1990,2000,ModernRate*10,0,-ModernRate,0);
            else return 1;
            return Math.Max(1, Math.Min(20*config.DaysPerYear, Math.Exp(logDays)));
        }
        public static double ExpectedDays(WorldState world)
        {
            var s = world.Config.SmoothCalendar;
            if (s == null) return world.Config.TurnDays;
            if (!Finite(world.Days) || world.Days < 0 || (s.Started && world.Days < s.EpochDays))
                throw new InvalidOperationException("World precedes its smooth-calendar epoch.");
            return DaysAtElapsedYears(world.Config, s.Started ? (world.Days-s.EpochDays)/world.Config.DaysPerYear : 0);
        }
        public static double CalendarYear(WorldState world)
        {
            var s = world.Config.SmoothCalendar;
            if (s == null) throw new InvalidOperationException("Fixed-time worlds have no calendar epoch.");
            return s.NeolithicStartYear + (s.Started ? (world.Days-s.EpochDays)/world.Config.DaysPerYear : 0);
        }
        public static void RequireCurrentBoundary(WorldState world)
        {
            if (world.Config.SmoothCalendar == null) return;
            double expected = ExpectedDays(world);
            if (Math.Abs(expected-world.Config.TurnDays) > 1e-9*Math.Max(1,expected))
                throw new InvalidOperationException("Smooth-calendar duration is stale or was edited outside the committed boundary.");
        }
        // Called ONCE after the old horizon has committed, including social and knowledge passes.
        // It prepares next-turn duration; it never reinterprets the completed TurnRecord.
        internal static void CommitNextBoundary(WorldState world)
        {
            var s = world.Config.SmoothCalendar;
            if (s == null) return;
            if (!s.Started && world.Finished) { s.Started = true; s.EpochDays = world.Days; }
            world.Config.TurnDays = ExpectedDays(world);
        }
    }
}
''')
replace(S+'Model.cs','[System.Runtime.Serialization.OptionalField] public NeolithicConfig Neolithic = new NeolithicConfig();','[System.Runtime.Serialization.OptionalField] public NeolithicConfig Neolithic = new NeolithicConfig();\n        // Absent/null is legacy fixed timing. Enabled only by an explicit new-game profile.\n        [System.Runtime.Serialization.OptionalField] public SmoothCalendarSettings SmoothCalendar;')
replace(S+'Model.cs','var defaults = new SimulationConfig(); Neolithic = defaults.Neolithic;','var defaults = new SimulationConfig(); Neolithic = defaults.Neolithic;\n            SmoothCalendar = null;')
replace(S+'Model.cs','        public void Validate()\n        {','        public void Validate()\n        {\n            SmoothCalendar?.Validate(this);')
replace(S+'TurnPlanning.cs','        public readonly TurnHorizon Horizon;','        public readonly TurnHorizon Horizon;\n        internal readonly CalendarBoundaryStamp CalendarStamp;')
replace(S+'TurnPlanning.cs','            Horizon = TurnHorizon.Next(world);','            Horizon = TurnHorizon.Next(world);\n            CalendarStamp = SmoothCalendar.Stamp(world);')
replace(S+'TurnPlanning.cs','        public readonly double Days;','        public readonly double Days;\n        private readonly CalendarBoundaryStamp calendarStamp;')
replace(S+'TurnPlanning.cs','World = world; Turn = world.Turn; Days = world.Days; ExcludedPlayer = excludedPlayer; Ai = ai;','World = world; Turn = world.Turn; Days = world.Days; ExcludedPlayer = excludedPlayer; Ai = ai;\n            calendarStamp = SmoothCalendar.Stamp(world);')
replace(S+'TurnPlanning.cs','ReferenceEquals(World, world) && Turn == world.Turn && Days == world.Days;','ReferenceEquals(World, world) && Turn == world.Turn && Days == world.Days && calendarStamp.Equals(SmoothCalendar.Stamp(world));')
replace(S+'TurnPlanning.cs','            using var profile = PhaseProfile.Scope("Prepare");','            SmoothCalendar.RequireCurrentBoundary(world);\n            using var profile = PhaseProfile.Scope("Prepare");')
replace(S+'TurnResolver.cs','Math.Abs(w.Config.TurnDays - prepared.Horizon.Days) > 1e-8)','Math.Abs(w.Config.TurnDays - prepared.Horizon.Days) > 1e-8 || !prepared.CalendarStamp.Equals(SmoothCalendar.Stamp(w)))')
replace(S+'TurnResolver.cs','            using var profile = PhaseProfile.Scope("Resolve");','            SmoothCalendar.RequireCurrentBoundary(w);\n            using var profile = PhaseProfile.Scope("Resolve");')
replace(S+'TurnResolver.cs','            LastMetrics.TotalMilliseconds = resolutionTimer.Elapsed.TotalMilliseconds; return record;','            SmoothCalendar.CommitNextBoundary(w);\n            LastMetrics.TotalMilliseconds = resolutionTimer.Elapsed.TotalMilliseconds; return record;')
replace(S+'PhaseExecution.cs','            using var profile = PhaseProfile.Scope("Decide");','            SmoothCalendar.RequireCurrentBoundary(world);\n            using var profile = PhaseProfile.Scope("Decide");')
replace(S+'SaveCodec.cs','                world.Config.Validate();','                world.Config.Validate();\n                SmoothCalendar.RequireCurrentBoundary(world);')
# Human observers must see the same frozen horizon as AI and planner.
replace(S+'Agents.cs','            Days = w.Days;','            SmoothCalendar.RequireCurrentBoundary(w);\n            Days = w.Days;')
profile=json.loads((ROOT/'Assets/_Anthropocene/Resources/OpeningConfig.json').read_text())
profile['SmoothCalendar']={'Version':1,'Curve':2,'NeolithicStartYear':-5000,'Started':False,'EpochDays':0}
put('Assets/_Anthropocene/Resources/SmoothCalendarOpeningConfig.json',json.dumps(profile,indent=2)+'\n')
for name in [S+'SmoothCalendar.cs','Assets/_Anthropocene/Resources/SmoothCalendarOpeningConfig.json']:
 guid=uuid.uuid5(uuid.NAMESPACE_URL,'anthropocene:'+name).hex
 ext='MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' if name.endswith('.cs') else 'TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
 put(name+'.meta','fileFormatVersion: 2\nguid: '+guid+'\n'+ext)
put('Tools/SmoothCalendar/Anthropocene.SmoothCalendar.csproj','<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings><Nullable>disable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9</LangVersion></PropertyGroup><ItemGroup><Compile Include="../../Assets/_Anthropocene/Simulation/*.cs"/><Compile Include="Program.cs"/></ItemGroup></Project>\n')
put('Tools/SmoothCalendar/Program.cs',r'''using System;
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
            c.SmoothCalendar=null;c.TurnDays=3650;var fixedSave=SaveCodec.Save(w);var fixedWorld=SaveCodec.Load(fixedSave);
            Check(fixedWorld.Config.SmoothCalendar==null,"legacy save stays disabled");
            Step(fixedWorld);Near(fixedWorld.Config.TurnDays,3650,"legacy next duration fixed");
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
''')
put('Docs/SmoothCalendar.md','''# Smooth calendar integration v1\n\nStatus: isolated implementation PR; not a demographic rebalance.\n\n## Authority and activation\n`SimulationConfig.SmoothCalendar == null` preserves fixed `TurnDays`, including old saves and the shipped `OpeningConfig.json`. The new `SmoothCalendarOpeningConfig.json` is an explicit new-game profile. Set the config before generation; do not mutate the clock during observation/planning. Normal construction retains the existing fixed behavior unless the profile is chosen. The global epoch is committed once all founding societies finish the opening; this is not a separate clock for each society.\n\n## Curve\nThe first portion is the measured early exponential, `20 exp(-ln(2)*elapsedYears/1200)` years. Historical mode uses a provisional, serialized Neolithic epoch of -5000 CE on a continuous mathematical year axis. This date is a configurable pacing assumption, not a historical claim or technology gate. A monotone C1 cubic in log duration joins the early curve at 1750 to the modern exponential at 1800. Modern duration is `10 exp(-ln(10)*(year-1900)/100)` days: 100 days at 1800, 31.6228 at 1850, 10 at 1900, 3.1623 at 1950, and 1.5849 at 1980. Another C1 log-duration join over 1990–2000 reaches a one-day floor. The floor remains positive through 2050 and beyond; this module does not impose an end-game victory date. Version 1 requires the engine's existing 365-day-year convention.\n\nAlternative curve `EarlyExponential` retains the equal-years experiment's early equation, with a one-day lower bound. It is for controlled attribution, not a finalized alternate historical campaign.\n\n## Boundary contract\nAll systems within one turn see the same immutable duration. `TurnResolver` computes the next duration only after the previous economy, demography, social and knowledge passes have committed. `TurnRecord` retains the actual old start/duration. `PreparedTurn` and `PlanningDraft` carry a value snapshot of the calendar and duration; changed settings invalidate reuse and reject prepared resolution. AI observation/planning and save loading reject stale smooth horizons rather than silently replacing a prepared plan. No mutation occurs in pure forecasts.\n\n## Compatibility and scope\nLegacy missing/null settings load fixed; the new profile is opt-in and creates no automatic migration. BaseDays, DaysPerYear, PracticeDays, material-life units, births/deaths, migration/fission rules, harvest kernel and RL schemas are unchanged. The earlier causal-demography investigation and 512-MiB historical save-header issue are deferred, not fixed or claimed as this PR's acceptance gates. Large-save reload failure remains independently documented. Existing running RL environments, checkpoints and branches are untouched.\n\n## Validation\nRun `dotnet run -c Release --project Tools/SmoothCalendar/Anthropocene.SmoothCalendar.csproj` from repository root. Focused checks cover numeric anchors, monotonicity and smooth joins, invalid config, native opening, three-seed automatic scheduling, records/stock/population invariants, exact save continuation, duration/config cache invalidation, legacy behavior and a daily numerical smoke. Run the registered headless suites independently. These tests do not establish a complete industrial economy, global daily performance, or timestep-independent gameplay.\n\n## Design deliverables\nIrrigation and Plough are separate implementation designs, not code effects bundled into this scheduler PR. Their accepted mechanisms do not alter q_k or the demographic model.\n''')
# Link rather than replace the current authoritative contract.
p=ROOT/'Docs/CurrentSimulationContract.md'
s=p.read_text();s+='\n\n## Smooth calendar (opt-in integration)\nSee [SmoothCalendar.md](SmoothCalendar.md) for the explicit new-game profile, legacy-save boundary and one shared post-opening horizon. Demographic balance diagnosis is deferred; no mortality or planner objective is changed.\n';p.write_text(s)
# The materialized source, not this bootstrap, is the reviewable PR payload.
Path(__file__).unlink()
