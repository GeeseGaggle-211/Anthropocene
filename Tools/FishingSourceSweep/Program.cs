using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

// Experiment only. All production Simulation/*.cs files are compiled unchanged.
// The proposed resident allocator/intensity/equipment are an adapter, NOT an implemented game mode.
internal static class Program
{
    const string Baseline = "86cba712dfd76673bd9637052543bde85f810e0b";
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    static readonly Dictionary<string, StreamWriter> Writers = new Dictionary<string, StreamWriter>();
    static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo DamageMethod = typeof(EcologicalPressure).GetMethod("SeasonalDamageRate", BindingFlags.Static | BindingFlags.NonPublic);
    static double maxParity, maxConservation;
    static int parityChecks, runs;
    sealed class Site { public string Name; public WorldState World; public int CampTile; public int[] Grounds; public Dictionary<int,int> Distance; public double Score; }
    sealed class Flow { public EcologyView View; public double Catch, ClosingCapacity, Residual; }
    sealed class Probe { public int Tile, Distance; public double Work, Catch, QEnd, QMean, Score; public Flow Flow; }
    static void Csv(string name, params object[] values)
    {
        if (!Writers.TryGetValue(name, out var w)) { w = new StreamWriter(Path.Combine("SweepResults", name)); Writers[name] = w; }
        string Cell(object o) { string s = o is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : o?.ToString() ?? ""; return "\"" + s.Replace("\"", "\"\"") + "\""; }
        w.WriteLine(string.Join(",", values.Select(Cell)));
    }
    static SimulationConfig Config()
    {
        var c = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"), Json);
        if (c == null) throw new Exception("Opening configuration failed to load");
        c.Width = 128; c.Height = 64; c.Players = 8; c.PlateCount = 12; c.StartSeparation = 8;
        // Isolated physical tests, not a full-system population calibration.
        c.OpeningCompatibilityEffects = false; c.FissionEnabled = false; c.ImmigrationEnabled = false; c.TradeEnabled = false;
        return c;
    }
    static WorldState CopyWorld(WorldState original)
    {
        return new WorldState { Seed = original.Seed, Config = original.Config, Generation = original.Generation,
            Tiles = original.Tiles.Select(t => (Tile)CloneMethod.Invoke(t, null)).ToArray(), Days = original.Days };
    }
    static Dictionary<int,int> Access(WorldState w, int origin, int range)
    {
        var d = new Dictionary<int,int>(); var queue = new Queue<int>();
        if (!w.Tiles[origin].TraversableLand) return d;
        foreach (int n in w.Map.Neighbors(origin).OrderBy(x => x))
            if (w.Tiles[n].Aquatic) { d[n] = 1; queue.Enqueue(n); }
        while (queue.Count > 0)
        {
            int u = queue.Dequeue(); if (d[u] >= range) continue;
            foreach (int v in w.Map.Neighbors(u).OrderBy(x => x))
                if (!d.ContainsKey(v) && w.Tiles[v].Environment == w.Tiles[u].Environment)
                { d[v] = d[u] + 1; queue.Enqueue(v); }
        }
        return d;
    }
    static double NaturalFlow(WorldState w, int id)
    {
        var t = w.Tiles[id]; var v = LazyEcology.ProjectEcology(w, id, w.Days);
        var rep = LazyEcology.MeanView(w, t, v, w.Config.TurnDays);
        var k = new HorizonEcologyCapacity(w, t, rep, w.Days, w.Config.TurnDays);
        return k.Food * k.FoodRate * w.Config.DaysPerYear;
    }
    // Zero grazing aquatic specialization of the source kernel. Calls the actual source's
    // representative forcing, bounded Stock solver, damage estimator and condition updater.
    // Only multiplier != 1 is proposed. Natural condition recovery/damage coefficients are NOT multiplied.
    static Flow Physical(WorldState w, int id, double request, double multiplier, bool parity = false)
    {
        var t = w.Tiles[id]; double start = w.Days, days = w.Config.TurnDays;
        var opening = LazyEcology.ProjectEcology(w, id, start);
        var rep = LazyEcology.MeanView(w, t, opening, days);
        var k = new HorizonEcologyCapacity(w, t, rep, start, days);
        var f = EcologyMath.Stock(opening.Food, k.Food, k.FoodRate * multiplier, request / days, days);
        var b = EcologyMath.Stock(Math.Max(0, opening.PotentialFood - opening.Food), k.Nonfood, k.BiomassRate * multiplier, 0, days);
        var water = EcologyMath.Stock(opening.Freshwater, k.Water, k.WaterRate, 0, days);
        double damage = (double)DamageMethod.Invoke(null, new object[] { w, t, rep, start, days, f.Realized, 0.0, 1.0, null });
        var end = opening; end.Food = f.Stock; end.PotentialFood = f.Stock + b.Stock; end.Freshwater = water.Stock;
        LazyEcology.RecoverCondition(w, t, ref end, days, damage);
        end.CultivationCapital = CultivationEconomy.Decay(w.Config, opening.CultivationCapital, t.CultivationOperatingFraction, days / w.Config.DaysPerYear);
        end.Days = start + days;
        var closing = new EcologyCapacity(w, t, end, (int)Math.Floor(end.Days / w.Config.BaseDays) % 4);
        double residual = Math.Abs(opening.Food + f.Recharge - f.Realized - f.Overflow - f.Stock);
        residual = Math.Max(residual, Math.Abs(opening.PotentialFood + f.Recharge + b.Recharge - f.Realized - f.Overflow - b.Overflow - end.PotentialFood));
        maxConservation = Math.Max(maxConservation, residual);
        if (end.Food < -1e-8 || end.PotentialFood + 1e-8 < end.Food || residual > 1e-6 * Math.Max(1, opening.PotentialFood + f.Recharge + b.Recharge))
            throw new Exception("Aquatic conservation failure");
        if (parity && multiplier == 1)
        {
            var source = EcologicalPressure.Integrate(w, id, new[] { new Demand { Tile = id, Entity = 1, Kind = "Fishing", Food = request, ImpactMultiplier = 1 } }, start, days, projectWood: false);
            double Delta(double a, double z) => Math.Abs(a-z)/Math.Max(1, Math.Abs(z));
            double error = new[] { Delta(end.Food, source.View.Food), Delta(end.PotentialFood, source.View.PotentialFood),
                Delta(end.Freshwater, source.View.Freshwater), Delta(end.AquaticCondition, source.View.AquaticCondition),
                Delta(f.Realized, source.Transfers.Sum(x => x.Food)) }.Max();
            maxParity = Math.Max(maxParity, error); parityChecks++;
            if (error > 1e-9 || end.CurrentVegetation != source.View.CurrentVegetation) throw new Exception("Source-kernel parity failure " + error);
        }
        return new Flow { View = end, Catch = f.Realized, ClosingCapacity = Math.Min(closing.Food, end.PotentialFood), Residual = residual };
    }
    static (double end,double mean) Intensity(double q, double work, double years, double scale, double upkeep, double halfLife)
    {
        if (years <= 0) return (q,q);
        double required = upkeep * q * years, m = required <= 0 ? 1 : Math.Min(1,work/required);
        double build = Math.Max(0,work-required)/(scale*years), decay = Math.Log(2)/halfLife*(1-m), k=build+decay;
        if (k<=0) return (q,q); double eq=build/k;
        return (eq+(q-eq)*Math.Exp(-k*years),eq+(q-eq)*EcologyMath.MeanExponential(k,years));
    }
    static Probe Candidate(WorldState w, int id, int distance, double quota, double q, double retention, double bonus, double tool, double sigma, double scale)
    {
        double years=w.Config.TurnDays/w.Config.DaysPerYear;
        double rate=w.Config.FishPerPerson*w.Config.DaysPerYear/w.Config.BaseDays*Math.Pow(retention,distance-1)*tool;
        Probe At(double work)
        {
            var iq=Intensity(q,work,years,scale,1,80); double mult=1+bonus*iq.mean;
            var f=Physical(w,id,rate*work,mult); var zero=Physical(w,id,0,mult);
            return new Probe { Tile=id, Distance=distance,Work=work,Catch=f.Catch,QEnd=iq.end,QMean=iq.mean,Flow=f,
                Score= Math.Min(sigma*f.ClosingCapacity,zero.View.Food) };
        }
        bool Safe(Probe p) => p.Catch+1e-7>=rate*p.Work && p.Flow.View.Food+1e-7>=p.Score;
        var full=At(quota);
        if (!Safe(full))
        {
            double lo=0,hi=quota;
            for(int i=0;i<32;i++){double mid=(lo+hi)/2; if(Safe(At(mid)))lo=mid;else hi=mid;}
            full=At(lo);
        }
        // The guard guarantees useful work; do not award intensity for an unmet catch.
        if (full.Work>0 && full.Catch/rate+1e-6<full.Work) throw new Exception("Fictional productive work");
        full.Score=quota>0?full.Catch/quota:0;
        return full;
    }
    static List<Site> Sites(WorldState w)
    {
        var result=new List<Site>();
        foreach(var type in new[]{EnvironmentKind.Shallows,EnvironmentKind.Lake})
        {
            var all=new List<Site>();
            foreach(var t in w.Tiles.Where(t=>t.TraversableLand))
            {
                if(!w.Map.Neighbors(t.Id).Any(id=>w.Tiles[id].Environment==type))continue;
                var d=Access(w,t.Id,3).Where(x=>w.Tiles[x.Key].Environment==type).ToDictionary(x=>x.Key,x=>x.Value);
                if(d.Count==0)continue;
                double score=d.Keys.Select(id=>NaturalFlow(w,id)).OrderByDescending(x=>x).Take(3).Sum();
                all.Add(new Site{World=w,CampTile=t.Id,Distance=d,Grounds=d.Keys.OrderBy(x=>x).ToArray(),Score=score});
            }
            all=all.OrderBy(s=>s.Score).ThenBy(s=>s.CampTile).ToList();
            foreach(double p in new[]{.2,.5,.8})
            {
                if(all.Count==0)continue;
                var s=all[(int)Math.Round(p*(all.Count-1))];
                // Copy wrapper because small candidate sets can select the same underlying site more than once.
                result.Add(new Site{Name=$"seed{w.Seed}_{type}_p{p*100:0}",World=w,CampTile=s.CampTile,Grounds=s.Grounds,Distance=s.Distance,Score=s.Score});
            }
            Csv("site_populations.csv",w.Seed,type,all.Count);
        }
        return result;
    }
    static void Run(Site site,int population,double share,double retention,double bonus,double gear,double sigma,double scale,string family)
    {
        runs++; string key=$"R{runs:D4}";var w=CopyWorld(site.World);w.Days=0;
        double years=w.Config.TurnDays/w.Config.DaysPerYear;
        var qs=site.Grounds.ToDictionary(id=>id,id=>0.0);var previous=new HashSet<int>();
        double total=0,late=0,workSum=0,toolUnits=0;int switches=0,firstMature=-1;
        for(int turn=1;turn<=25;turn++)
        {
            double quota=population*share*years/Math.Min(3,site.Grounds.Length);
            var probes=site.Grounds.Select(id=>Candidate(w,id,site.Distance[id],quota,qs[id],retention,bonus,gear,sigma,scale)).ToList();
            // Explicit bounded whole-share heuristic, not the production DemandController and not a global optimum.
            // A 2.5% continuity preference is a ranking tie margin; it does not force rotation.
            var chosen=probes.OrderByDescending(p=>p.Score*(previous.Contains(p.Tile)?1.025:1)).ThenBy(p=>p.Distance).ThenBy(p=>p.Tile).Take(3).ToArray();
            var ids=new HashSet<int>(chosen.Select(x=>x.Tile));
            int changed=turn==1?0:ids.Count(id=>!previous.Contains(id));switches+=changed;
            double catchSum=chosen.Sum(p=>p.Catch),wy=chosen.Sum(p=>p.Work);
            // Physical carrying-capacity experiment: requested harvest is not capped by meals or storage.
            // Surplus is reported as potential receipts, NEVER booked as free population growth.
            foreach(var p in chosen)
            {
                if(turn==1)Physical(w,p.Tile,p.Catch,1,true);
                p.Flow.View.Commit(w.Tiles[p.Tile]);qs[p.Tile]=p.QEnd;
                Csv("ground_turns.csv",key,turn,p.Tile,p.Distance,p.Work,p.Catch,p.QMean,p.QEnd,p.Flow.View.Food,p.Flow.ClosingCapacity,p.Flow.View.AquaticCondition);
            }
            foreach(int id in site.Grounds.Where(id=>!ids.Contains(id)))qs[id]=Intensity(qs[id],0,years,scale,1,80).end;
            double qWeighted=catchSum>0?chosen.Sum(p=>p.QEnd*p.Catch)/catchSum:0;
            if(firstMature<0&&qWeighted>=.5)firstMature=turn;
            double meals=population*w.Config.Consumption*w.Config.TurnDays/w.Config.BaseDays;
            double tools=gear>1?wy/20:0;
            total+=catchSum;workSum+=wy;toolUnits+=tools;if(turn>20)late+=catchSum;
            Csv("turns.csv",key,turn,turn*years,catchSum,meals,catchSum/meals,wy,tools,qWeighted,changed,string.Join(";",chosen.Select(p=>p.Tile)));
            previous=ids;w.Days+=w.Config.TurnDays;
        }
        double lateAnnual=late/(5*years),perPerson=w.Config.Consumption*w.Config.DaysPerYear/w.Config.BaseDays;
        Csv("summary.csv",key,family,site.Name,site.World.Seed,site.CampTile,site.Grounds.Length,population,share,retention,bonus,gear,sigma,scale,
            total/(25*years),lateAnnual,lateAnnual/perPerson,workSum/(25*years),toolUnits/(25*years),switches,firstMature,
            qs.Values.Max(),qs.Values.Average());
    }
    static void Costs(WorldState w)
    {
        var c=w.Camps.First();var recipes=NeolithicProduction.Recipes(w.Config);double annual=w.Config.DaysPerYear/w.Config.BaseDays;
        foreach(bool specialist in new[]{false,true})foreach(bool regular in new[]{false,true})
        {
            foreach(string tech in NeolithicTech.FirstTier)if(!c.People.NeolithicTechnologies.Contains(tech))c.People.NeolithicTechnologies.Add(tech);
            var fishRecipe=recipes.First(r=>r.Good==CraftGood.FarmTools); // Proposed FishingTools matches this Carpenter output reference.
            var generic=recipes.First(r=>r.Good==(regular?CraftGood.Tools:CraftGood.HandTools));
            if(regular&&!specialist)continue; // Regular Tools require a Toolmaker.
            string craftRole=specialist?WorkforceRoles.Carpenter:WorkforceRoles.Craftsperson;
            string toolRole=specialist?WorkforceRoles.Toolmaker:WorkforceRoles.Craftsperson;
            double outputs=regular?ToolCoverageBudget.OutputsPerTool:ToolCoverageBudget.OutputsPerHandTool;
            double craftYears=1/(SpecialistProductivity.CraftRate(w,c,fishRecipe,craftRole)*annual)
                +1/outputs/(SpecialistProductivity.CraftRate(w,c,generic,toolRole)*annual);
            double stone=1+generic.Stone/outputs,wood=1+generic.Wood/outputs;
            double stoneRate=SpecialistProductivity.ExtractionRate(w,c,MaterialId.Stone,specialist?MaterialEconomy.Specialist(MaterialId.Stone):WorkforceRoles.Gatherer)*annual;
            double woodRate=SpecialistProductivity.ExtractionRate(w,c,MaterialId.Wood,specialist?MaterialEconomy.Specialist(MaterialId.Wood):WorkforceRoles.Gatherer)*annual;
            double gatheringYears=stone/stoneRate+wood/woodRate;
            Csv("equipment_costs.csv",specialist?"matching specialists":"generalists",regular?"Regular Tools":"Hand Tools",stone,wood,craftYears,gatheringYears,(craftYears+gatheringYears)/20,
                1.5/(1+(craftYears+gatheringYears)/20));
        }
        Csv("task_rates.csv","wild PlantFood",w.Config.GatherPerPerson*annual,"food per worker-year; unchanged wild-food rate");
        Csv("task_rates.csv","generalist material",w.Config.Neolithic.SpecialistMaterialExtractionRate*w.Config.Neolithic.GeneralistTaskEfficiency*annual,"material units per worker-year");
        Csv("task_rates.csv","specialist material",w.Config.Neolithic.SpecialistMaterialExtractionRate*annual,"material units per worker-year at default zero scale bonus");
    }
    static void CurrentHunterControl(Site site)
    {
        // Actual ordinary Fish orders and full production resolver, isolated first-turn controls.
        var original=site.World;var w=SaveCodec.Load(SaveCodec.Save(original));
        var c=w.Camps.First();w.Camps.RemoveAll(x=>x.Id!=c.Id);w.Hunters.Clear();w.Herds.Clear();c.Tile=site.CampTile;
        c.People.Technologies[(int)Technology.Watercraft]=true;c.People.Technology=Technology.Watercraft;c.People.Progression.Era=Era.Neolithic;
        c.People.OpeningTransitionProcessed=true;c.People.OpeningTransitionProcessedInitialized=true;
        c.Population=16;c.Food=24*w.Config.Consumption*w.Config.TurnDays/w.Config.BaseDays;
        var h=new Hunter{Id=w.NextId++,Tile=c.Tile,SourceCamp=c.Id,Population=8,Player=c.Player,SocietyId=c.SocietyId};w.Hunters.Add(h);
        Ownership.Bind(w);
        int target=w.Map.Neighbors(c.Tile).Where(id=>w.Tiles[id].Aquatic).OrderByDescending(id=>NaturalFlow(w,id)).First();
        var order=new Order(OrderKind.Fish,h.Id,target);
        var resolver=new TurnResolver();string rejection=resolver.Validate(w,c.Player,order);
        if(rejection!=null)throw new Exception("Source Hunter order rejected: "+rejection);
        var record=resolver.Resolve(w,new Dictionary<int,List<Order>>{{c.Player,new List<Order>{order}}});
        var demo=record.Demographics.First(x=>x.Camp==c.Id);
        Csv("hunter_controls.csv",site.Name,target,demo.StartingPopulation,demo.EndingPopulation,demo.Additions,demo.Deaths,
            record.Transfers.Where(x=>x.Kind=="Fishing").Sum(x=>x.Food),demo.FoodProduced,demo.FoodConsumed,demo.FinalReserves);
    }
    static void Main()
    {
        Directory.CreateDirectory("SweepResults");
        try
        {
            Console.WriteLine("PINNED SOURCE "+Baseline);AquaticChecks.Run();
            Csv("site_populations.csv","seed","type","eligible shoreline tiles");
            Csv("sites.csv","site","seed","camp_tile","ground","distance","environment","food_capacity_s0","food_rate_s0","natural_annual_flow","upwelling","local_stone","local_wood","local_clay");
            Csv("summary.csv","run","family","site","seed","camp_tile","candidates","residents_fixed","max_fisher_share","retention","max_renewal_bonus","tool_multiplier","stock_guard","work_scale","mean_annual_catch","late_annual_catch","late_meal_support_equivalents","mean_productive_fishers","mean_tool_replacement_units_per_year","ground_replacements","first_weighted_q_half_turn","max_q_end","mean_candidate_q_end");
            Csv("turns.csv","run","turn","year","potential_catch","resident_meal_need","meal_coverage","productive_fisher_years","equipment_units_worn","catch_weighted_q","grounds_changed","selected_tiles");
            Csv("ground_turns.csv","run","turn","tile","distance","productive_work","catch","q_mean","q_end","food_end","food_capacity_end","aquatic_condition_end");
            Csv("equipment_costs.csv","workforce","generic_equipment","stone_per_fishing_tool","wood_per_fishing_tool","craft_worker_years","extraction_worker_years","upstream_workers_per_covered_fisher","net_productivity_ratio_before_ecological_cap");
            Csv("task_rates.csv","task","annual_rate","units");
            Csv("hunter_controls.csv","site","target","population_before","population_after","births","deaths","hunter_catch","all_food_produced","food_consumed","final_reserves");
            var sites=new List<Site>();var worlds=new List<WorldState>();
            foreach(int seed in new[]{41,83,1729})
            {
                Console.WriteLine("Generating source world "+seed);var w=new WorldGenerator().Generate(seed,Config());worlds.Add(w);sites.AddRange(Sites(w));
            }
            foreach(var s in sites)
            {
                foreach(int id in s.Grounds)
                {
                    double Material(MaterialId m)=>s.World.Map.Radius(s.CampTile,1).Where(t=>s.World.Tiles[t].TraversableLand).Sum(t=>s.World.Resources?.SurfaceAccessible(t,m)??0);
                    var p=s.World.Tiles[id].Seasons[0];
                    Csv("sites.csv",s.Name,s.World.Seed,s.CampTile,id,s.Distance[id],s.World.Tiles[id].Environment,p.MaxFood,p.FoodRecoveryPerDay,NaturalFlow(s.World,id),AquaticEcology.UpwellingStrength(s.World,s.World.Tiles[id]),Material(MaterialId.Stone),Material(MaterialId.Wood),Material(MaterialId.Clay));
                    foreach(double load in new[]{0.0,.5,1.0,2.0})Physical(s.World,id,NaturalFlow(s.World,id)*20*load,1,true);
                }
                foreach(int n in new[]{24,120,300})foreach(double bonus in new[]{0.0,.25})foreach(double gear in new[]{1.0,1.5})
                    Run(s,n,.5,.75,bonus,gear,.2,40,"primary");
            }
            foreach(var s in sites.Where(x=>x.Name.EndsWith("Shallows_p50")))
                foreach(double share in new[]{.25,.5,.75})foreach(double retention in new[]{.65,.75,.85})foreach(double guard in new[]{0.0,.2})
                    Run(s,120,share,retention,.25,1.5,guard,40,"allocation_sensitivity");
            foreach(var s in sites.Where(x=>x.Name.EndsWith("_p50")))
                foreach(double scale in new[]{20.0,80.0})Run(s,120,.5,.75,.25,1.5,.2,scale,"intensity_sensitivity");
            Costs(worlds[0]);
            foreach(var s in sites.Where(x=>x.Name.EndsWith("_p50")))CurrentHunterControl(s);
            var hashes=Directory.GetFiles("Assets/_Anthropocene/Simulation","*.cs").OrderBy(x=>x).ToDictionary(x=>x,x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
            hashes["Assets/_Anthropocene/Resources/OpeningConfig.json"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("Assets/_Anthropocene/Resources/OpeningConfig.json")));
            File.WriteAllText("SweepResults/manifest.json",JsonSerializer.Serialize(new{baseline=Baseline,experimentCommit=Environment.GetEnvironmentVariable("GITHUB_SHA"),runtime=Environment.Version.ToString(),runs,sites=sites.Count,parityChecks,maxRelativeParityError=maxParity,maxConservationResidual=maxConservation,
                limitations=new[]{"Resident feature is test adapter, not production integration","Fixed cohorts: no population growth in ecology sweep","Known catchments; no rival claims or knowledge uncertainty","Potential catch is not capped by meal/storage acceptance","Equipment supplied for physics runs; all upstream replacement costs reported separately","Source-selected 128x64 world catchments, not Standard-map start distributions","Whole-share bounded ranking heuristic, not production DemandControl"},sourceHashes=hashes},Json));
            File.Copy("Tools/FishingSourceSweep/Program.cs","SweepResults/Program.cs",true);
            File.Copy("Tools/FishingSourceSweep/Anthropocene.FishingSourceSweep.csproj","SweepResults/Anthropocene.FishingSourceSweep.csproj",true);
            Console.WriteLine($"DONE runs={runs} sites={sites.Count} parity={parityChecks} maxParity={maxParity:R} massResidual={maxConservation:R}");
        }
        finally{foreach(var w in Writers.Values)w.Dispose();}
    }
}
