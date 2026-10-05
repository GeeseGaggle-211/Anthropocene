"""Prepare a second experimental phase. Patches only the isolated test runner."""
from pathlib import Path
p = Path(__file__).with_name('FishingSourceSweep') / 'Program.cs'
s = p.read_text()
marker = '// DEMAND_CAPPED_PHASE_TWO'
if marker not in s:
    old = '            var ids=new HashSet<int>(chosen.Select(x=>x.Tile));'
    new = '''            // DEMAND_CAPPED_PHASE_TWO: accepted food is capped at fixed-cohort meals.
            // This is a steady-state service test, not a forecast of growth or reserve rebuilding.
            if (family.StartsWith("demand_capped") || family.StartsWith("upstream_budget"))
            {
                double remainingMeals=population*w.Config.Consumption*w.Config.TurnDays/w.Config.BaseDays;
                for(int j=0;j<chosen.Length;j++)
                {
                    var p=chosen[j];
                    double rate=w.Config.FishPerPerson*w.Config.DaysPerYear/w.Config.BaseDays*Math.Pow(retention,p.Distance-1)*gear;
                    chosen[j]=Candidate(w,p.Tile,p.Distance,Math.Min(p.Work,Math.Max(0,remainingMeals)/rate),qs[p.Tile],retention,bonus,gear,sigma,scale);
                    remainingMeals=Math.Max(0,remainingMeals-chosen[j].Catch);
                }
            }
            var ids=new HashSet<int>(chosen.Where(x=>x.Work>1e-9).Select(x=>x.Tile));'''
    assert s.count(old)==1
    s=s.replace(old,new)
    s=s.replace('foreach(var p in chosen)\n            {','foreach(var p in chosen.Where(x=>x.Work>1e-9))\n            {')
    old='var w=new WorldGenerator().Generate(seed,Config());worlds.Add(w);sites.AddRange(Sites(w));'
    new='var ro=JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"),Json); var w=new WorldGenerator().Generate(seed,Config(),ro);worlds.Add(w);sites.AddRange(Sites(w));'
    assert s.count(old)==1
    s=s.replace(old,new)
    old='            Costs(worlds[0]);'
    new='''            foreach(var s in sites.Where(x=>x.Name.EndsWith("_p50")))
            {
                foreach(int n in new[]{24,120,300})foreach(double bonus in new[]{0.0,.25})foreach(double gear in new[]{1.0,1.5})
                    Run(s,n,.5,.75,bonus,gear,.2,40,"demand_capped_primary");
                foreach(int n in new[]{24,120})foreach(double scale in new[]{160.0,400.0,800.0})
                    Run(s,n,.5,.75,.25,1.5,.2,scale,"demand_capped_maturation");
                // Fixed half-resident food-chain envelope INCLUDING upstream tool replacement.
                // Ratios use the unchanged source rates. Material stock availability remains separate.
                foreach(int n in new[]{120,300})
                {
                    Run(s,n,.5/1.25,.75,.25,1.5,.2,40,"upstream_budget_generalists");
                    Run(s,n,.5/1.046875,.75,.25,1.5,.2,40,"upstream_budget_specialists_regular");
                }
            }
            Costs(worlds[0]);'''
    assert s.count(old)==1
    s=s.replace(old,new)
    old='            File.WriteAllText("SweepResults/manifest.json",'
    new='''            hashes["Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json")));
            File.Copy("Assets/_Anthropocene/Resources/OpeningConfig.json","SweepResults/OpeningConfig.json",true);
            File.Copy("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json","SweepResults/ResourceGenerationConfig.json",true);
            File.Copy("Tools/FishingSourceSweep/Prepare.py","SweepResults/Prepare.py",true);
            File.Copy("Tools/FishingSourceSweepPhaseTwo.py","SweepResults/FishingSourceSweepPhaseTwo.py",true);
            File.WriteAllText("SweepResults/manifest.json",'''
    assert s.count(old)==1
    s=s.replace(old,new)
    s=s.replace('"Potential catch is not capped by meal/storage acceptance"','"Primary physics cases measure uncapped harvest potential; demand_capped/upstream_budget families cap at fixed resident meals"')
    s=s.replace('"Equipment supplied for physics runs; all upstream replacement costs reported separately"','"Equipment supplied in physics; upstream_budget families reserve source-derived replacement labor but do not execute finite material-stock production"')
    s=s.replace('"Whole-share bounded ranking heuristic, not production DemandControl"','"Whole-share bounded ranking heuristic, not production DemandControl", "Generated geology uses shipped ResourceGenerationConfig; reported local quantities are surface-accessible, not all possible deposits"')
    p.write_text(s)
print('Demand-capped experiment phase prepared; production source unchanged')
