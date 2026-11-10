"""Cloud diagnostic; only creates an experiment harness. Native engine files are unchanged."""
from pathlib import Path
import hashlib, json, subprocess, sys

root = Path.cwd()
seed = sys.argv[1] if len(sys.argv) > 1 else "11"
treatment = sys.argv[2] if len(sys.argv) > 2 else "baseline"
assert treatment in {"baseline", "scaled", "zero"}
source = root / "Tools/SmoothCadence100/Program.cs"
src = source.read_text()
original_hash = hashlib.sha256(source.read_bytes()).hexdigest()

def replace(old, new):
    global src
    if src.count(old) != 1:
        raise RuntimeError(f"Expected one replacement, got {src.count(old)}: {old[:100]}")
    src = src.replace(old, new)

replace('    private const string Baseline =', '''    private static string Treatment = "baseline";
    private static HashSet<int> FoundingCamps;
    private static Dictionary<int, double> FoundedAt, LastSplit;
    private const string Baseline =''')
replace('        w.Config.TurnDays = years * w.Config.DaysPerYear;', '''        w.Config.TurnDays = years * w.Config.DaysPerYear;
        // Change only the planner tradeoff, never hazards or food production.
        // The native opening is identical across treatments.
        w.Config.ElderSplitLexTolerance = Arm == "opening" || Treatment == "baseline" ? .05
            : Treatment == "zero" ? 0 : 1 - Math.Pow(.95, years / 20.0);''')
replace('        Rows.Add(row);', '        Rows.Add(row);\n        InspectCamps(w, record);')
replace('        Stage="load "+arm; Arm=arm; Current=SaveCodec.Load(checkpoint);', '''        Stage="load "+arm; Arm=arm; Current=SaveCodec.Load(checkpoint);
        // Prior regression already covered replay; this attribution does not re-run it.
        exactReplay = false;
        FoundingCamps = new HashSet<int>(Current.Camps.Select(c => c.Id));
        FoundedAt = Current.Camps.ToDictionary(c => c.Id, c => 0.0);
        LastSplit = new Dictionary<int, double>();''')
replace('        Seed=args.Length>0?int.Parse(args[0]):11;', '''        Seed=args.Length>0?int.Parse(args[0]):11;
        Treatment=args.Length>3?args[3]:"baseline";''')
start = src.index('            if(mode!="production" && Seed==11) {')
end = src.index('            var summary=new {status="complete"', start)
src = src[:start] + src[end:]
replace('            var summary=new {status="complete",Seed,mode,baseline=Baseline,openingTurns,openingHash,', '''            var summary=new {status="complete",Seed,mode,treatment=Treatment,baseline=Baseline,openingTurns,openingHash,
                attribution="Identical source and opening, same 1333 years, only ElderSplitLexTolerance differs: baseline .05; scaled 1-.95^(dt/20); zero 0. Not a recommended policy patch. Pure fully supplied demography probes test fixed-law partition consistency separately. Per-camp records distinguish pre-social births/deaths from post-social populations, original founders and settlement ages. No new production save/replay acceptance.",''')
replace('            Write("initial-config.json",cfg);', '''            Write("initial-config.json",cfg);
            PureDemographyProbes(cfg);''')
src = src.replace('Write("checks.json",Checks);', 'Write("checks.json",Checks);Write("diagnosis-camps.json",CampRows);')
methods = r'''
    private static void InspectCamps(WorldState w, TurnRecord record)
    {
        if (record == null || FoundedAt == null) return;
        double elapsed = Years(w);
        foreach (var f in record.Fissions) { FoundedAt[f.DaughterCamp] = elapsed; LastSplit[f.ParentCamp] = elapsed; }
        var reports = record.Demographics.ToDictionary(x => x.Camp);
        foreach (var c in w.Camps.Where(c => c.Population > 0).OrderBy(c => c.Id))
        {
            reports.TryGetValue(c.Id, out var r);
            double py = r?.PersonYears ?? 0;
            double nutrition = r?.Nutrition ?? c.FoodNeedsMetRatio;
            double fertility = py > 0 ? r.ContinuousBirths / py : 0;
            double mortality = py > 0 ? r.ContinuousDeaths / py : 0;
            double g = py > 0 && nutrition > 0 ? fertility / (w.Config.BaseBirthRate * nutrition) : 0;
            CampRows.Add(new {
                Seed, Treatment, Arm, Turn = w.Turn, Year = elapsed, DeltaYears = record.Duration / w.Config.DaysPerYear,
                Camp = c.Id, c.SocietyId, FirstTechnology = c.Technology.ToString(), c.Population,
                IsFounder = FoundingCamps.Contains(c.Id), AgeSinceTest = elapsed - (FoundedAt.TryGetValue(c.Id, out var born) ? born : 0),
                SinceSplit = LastSplit.TryGetValue(c.Id, out var split) ? elapsed - split : -1,
                HasDemographicReport = r != null, StartingPopulation = r?.StartingPopulation ?? 0,
                PreSocialEndingPopulation = r?.EndingPopulation ?? c.Population,
                Births = r?.ContinuousBirths ?? 0, Deaths = r?.ContinuousDeaths ?? 0, PersonYears = py,
                BirthRate = fertility, DeathRate = mortality, FertilityFraction = g,
                nutrition, Clothing = r?.ClothingCoverage ?? c.ClothPerCapita,
                Housing = r?.HousingCoverage ?? c.HousingPerCapita,
                ExposureDeaths = r?.ExposureDeaths ?? 0, CrowdingDeaths = r?.CrowdingDeaths ?? 0,
                DiseaseDeaths = r?.DiseaseDeaths ?? 0, StarvationDeaths = r?.StarvationDeaths ?? 0,
                FoodProduced = r?.FoodProduced ?? 0, FoodConsumed = r?.FoodConsumed ?? 0,
                InitialReserves = r?.InitialReserves ?? 0, FinalReserves = r?.FinalReserves ?? c.Food,
                ManufacturingFood = r?.ManufacturingFood ?? 0, DiscardedFood = r?.DiscardedInventory ?? 0,
                Emigrated = r?.Emigrated ?? 0, Immigrated = r?.Immigrated ?? 0, SplitOff = r?.SplitOff ?? 0,
                c.QoL, c.FoodNeedsMetRatio, c.ClothPerCapita, c.HousingPerCapita,
                FarmerCount = CultivationEconomy.Workers(c, WorkforceRoles.Farmer),
                MeanFieldQ = FootprintQ(w,c), c.Food,
                PlannerTolerance = w.Config.ElderSplitLexTolerance
            });
        }
    }
    private static void PureDemographyProbes(SimulationConfig cfg)
    {
        var probes = new List<object>();
        // Fully provided fixed physical services: same autonomous hazard ODE,
        // no economic planner, no resource scarcity, no social movement or rounding.
        foreach (double initial in new[] {100.0, 500.0, 900.0, 1500.0})
        foreach (double step in new[] {20.0, 10.0, 1.0, 10.0/365, 1.0/365})
        {
            double n = initial, elapsed = 0, births = 0, deaths = 0, personYears = 0;
            while (elapsed < 20-1e-10)
            {
                double dt = Math.Min(step,20-elapsed);
                var r = PhysicalDemography.AdvanceFixed(cfg,n,dt,1,1,100000,0,false);
                n = r.FinalPopulation; elapsed += dt; births += r.Births; deaths += r.Deaths; personYears += r.PersonYears;
            }
            probes.Add(new {type="full_service_partition", initial, step, elapsed, population=n,births,deaths,personYears});
        }
        // Illustrate whether the unchanged 5% person-year gate treats an
        // otherwise identical 20% reduction in fertility as affordable at each dt.
        foreach (double years in new[] {20.0,15.0,10.0,5.0,1.0,10.0/365,1.0/365})
        {
            var full = PhysicalDemography.AdvanceFixed(cfg,500,years,1,1,5000,0,false,0,1);
            var reduced = PhysicalDemography.AdvanceFixed(cfg,500,years,1,1,5000,0,false,0,.8);
            double gap=1-reduced.PersonYears/full.PersonYears;
            probes.Add(new {type="lex_gate_witness",years,gap,admittedByFivePercent=gap<=.05,
                scaledTolerance=1-Math.Pow(.95,years/20), fullEnd=full.FinalPopulation,reducedEnd=reduced.FinalPopulation,
                fullPY=full.PersonYears,reducedPY=reduced.PersonYears});
        }
        // Fully supplied death rate depends on density, not decision length.
        foreach (double n in new[] {500.0,642.0,775.0,976.0,1000.0})
        { var r=PhysicalDemography.Rates(cfg,n,1,1,n);probes.Add(new {type="rates_at_equal_state",n,r.Birth,r.Death,r.Net,r.Exposure,r.Crowding,r.Disease}); }
        Write("pure-demography-probes.json",probes);
    }
'''
replace('    private static void Main(string[] args)', methods+'\n    private static void Main(string[] args)')
# This inherited metadata described the old one-year test, not the current experiment.
start=src.index('                shortSchedule=')
end=src.index('\n                earlyElapsedYears=',start)
src=src[:start]+'                shortSchedule="Not run here. Pure full-service hazard and lex-gate probes are separate from the whole-economy ablation.",'+src[end:]
work = root/'TestResults'/'CadenceDemographyCause'/f'{seed}-{treatment}'
work.mkdir(parents=True,exist_ok=True)
(work/'Program.cs').write_text(src)
proj=work/'Cause.csproj'
proj.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings><Nullable>disable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9</LangVersion></PropertyGroup><ItemGroup><Compile Include="'+str(root/'Assets/_Anthropocene/Simulation/*.cs')+'"/><Compile Include="Program.cs"/></ItemGroup></Project>')
(work/'derivation.json').write_text(json.dumps({'original_harness_sha256':original_hash,'derived_harness_sha256':hashlib.sha256(src.encode()).hexdigest(),'treatment':treatment,'seed':int(seed),'engine_changed':False},indent=2))
subprocess.run(['dotnet','run','-c','Release','--project',str(proj),'--',seed,'small',str(work),treatment],check=True)
