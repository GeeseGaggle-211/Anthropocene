using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// Source-linked experiment only. Production Simulation/*.cs is compiled unchanged.
// Population dynamics use the current food-bucket, growth-food, QoL, starvation and
// social-cohesion equations. Fishing capital/ecology are an experimental adapter.
internal static class Program
{
    const string Baseline = "86cba712dfd76673bd9637052543bde85f810e0b";
    const int Turns = 40, Bands = 32, StartPopulation = 24;
    static readonly int[] Seeds = { 0, 1, 2, 3, 4, 11, 15, 17, 41, 42, 83, 1729, 2718, 9001 };
    static readonly double[] SampleQuantiles = { .05, .15, .25, .35, .50, .65, .75, .85, .95 };
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

    sealed class Site
    {
        public int Seed, CampTile;
        public string WaterClass, CountClass;
        public Dictionary<int,int> Distance = new Dictionary<int,int>();
        public double NaturalScore;
    }
    sealed class GroundState { public int Tile, Distance; public double Capital; public EcologyView Ecology; }
    sealed class GroundEval
    {
        public int Tile, Distance; public double WorkerYears, EffectiveWork, MaintenanceWorkerYears, OperatingFraction;
        public double StartE, AverageE, EndE, QAverage, QEnd, Request, Catch, ConditionEnd, FoodEnd, BiomassEnd;
        public EcologyView EndView;
    }
    sealed class CaseResult
    {
        public string Id, Mode; public Site Site; public int SlotLimit;
        public int P200, P500, P800, FinalPopulation, MaxPopulation, DeficitTurnsTotal, Deaths, Births;
        public double MeanFishers, MeanQ, MaxQ, FinalQoL, FinalFood, MeanCatch, MeanTarget, MeanUtilization;
    }

    static SimulationConfig Config()
    {
        var c = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"), Json);
        c.Width = 128; c.Height = 64; c.Players = 8; c.PlateCount = 12; c.StartSeparation = 8;
        c.OpeningCompatibilityEffects = false;
        c.FissionEnabled = false; c.ImmigrationEnabled = false; c.TradeEnabled = false;
        c.Validate(); return c;
    }

    static Dictionary<int,int> Access(WorldState w, int origin, int range)
    {
        var result = new Dictionary<int,int>();
        if (!w.Tiles[origin].TraversableLand) return result;
        var queue = new Queue<int>();
        foreach (int n in w.Map.Neighbors(origin).OrderBy(x => x))
            if (w.Tiles[n].Environment == EnvironmentKind.Shallows || w.Tiles[n].Environment == EnvironmentKind.Lake)
            { if (!result.ContainsKey(n)) { result[n] = 1; queue.Enqueue(n); } }
        while (queue.Count > 0)
        {
            int u = queue.Dequeue(), d = result[u];
            if (d >= range) continue;
            var env = w.Tiles[u].Environment;
            foreach (int v in w.Map.Neighbors(u).OrderBy(x => x))
                if (!result.ContainsKey(v) && w.Tiles[v].Environment == env)
                { result[v] = d + 1; queue.Enqueue(v); }
        }
        return result;
    }

    static bool Warm(Tile t) => t.Seasons.Min(s => (double)s.Temperature) > 0;
    static double DistanceRetention(int d) => Math.Pow(.75, Math.Max(0, d - 1));

    static double NaturalSafeAmount(WorldState w, int id)
    {
        var t = w.Tiles[id]; var v = LazyEcology.ProjectEcology(w, id, w.Days);
        var rep = LazyEcology.MeanView(w, t, v, w.Config.TurnDays);
        var h = new HorizonEcologyCapacity(w, t, rep, w.Days, w.Config.TurnDays);
        return h.SafeRate * w.Config.TurnDays;
    }

    static List<Site> EnumerateSites(WorldState w)
    {
        var sites = new List<Site>();
        foreach (var land in w.Tiles.Where(t => t.TraversableLand).OrderBy(t => t.Id))
        {
            var access = Access(w, land.Id, 3);
            if (access.Count == 0) continue;
            // Primary calibration excludes every catchment containing a <=0C seasonal ground,
            // rather than silently dropping cold alternatives and misclassifying a site's true count.
            if (access.Keys.Any(id => !Warm(w.Tiles[id]))) continue;
            string water = access.Keys.All(id => w.Tiles[id].Environment == EnvironmentKind.Shallows) ? "Shallows"
                : access.Keys.All(id => w.Tiles[id].Environment == EnvironmentKind.Lake) ? "Lake" : "Mixed";
            string count = access.Count == 1 ? "exact1" : access.Count == 2 ? "exact2" : "3plus";
            double score = access.OrderByDescending(p => NaturalSafeAmount(w,p.Key) * DistanceRetention(p.Value))
                .Take(3).Sum(p => NaturalSafeAmount(w,p.Key) * DistanceRetention(p.Value));
            sites.Add(new Site { Seed=w.Seed, CampTile=land.Id, WaterClass=water, CountClass=count, Distance=access, NaturalScore=score });
        }
        return sites;
    }

    static List<Site> SampleSites(List<Site> all)
    {
        var result = new List<Site>();
        foreach (var g in all.GroupBy(s => (s.CountClass,s.WaterClass)).OrderBy(g => g.Key.CountClass).ThenBy(g => g.Key.WaterClass))
        {
            var a = g.OrderBy(s => s.NaturalScore).ThenBy(s => s.Seed).ThenBy(s => s.CampTile).ToArray();
            foreach (double q in SampleQuantiles)
            {
                if (a.Length == 0) continue;
                int i = (int)Math.Round(q * (a.Length - 1));
                var s = a[Math.Max(0,Math.Min(a.Length-1,i))];
                if (!result.Any(x => x.Seed==s.Seed && x.CampTile==s.CampTile)) result.Add(s);
            }
        }
        return result;
    }

    readonly struct ManagedCapacity
    {
        public readonly double Food, Nonfood, Biomass, FoodRate, BiomassRate, ManagedFlow;
        public ManagedCapacity(double food,double nonfood,double biomass,double fr,double br,double flow)
        { Food=food; Nonfood=nonfood; Biomass=biomass; FoodRate=fr; BiomassRate=br; ManagedFlow=flow; }
    }

    static (double B,double G,double Rb,double Rf,double Managed) Season(WorldState w, Tile t, EcologyView v, int season, double q, double operation)
    {
        var natural = new EcologyCapacity(w,t,v,season);
        double b = Math.Max(0,natural.Biomass), g0 = Math.Min(b,Math.Max(0,natural.Food));
        double g = g0 + Math.Max(0,b-g0) * q * q;
        double managed = Math.Max(0,w.Config.CultivationRegenFlux) * q * b * Math.Max(0,natural.BiomassRate) * Numbers.Clamp(operation,0,1);
        return (b,Math.Min(b,g),natural.BiomassRate,natural.FoodRate,managed);
    }

    static ManagedCapacity Horizon(WorldState w, Tile t, EcologyView v, double q, double operation, double start, double days)
    {
        var exposure = new SeasonExposure(start,days,w.Config.BaseDays);
        double foodDays=0, nonfoodDays=0, biomassDays=0, fr=0,br=0,foodSource=0,nonfoodSource=0,managed=0;
        for(int s=0;s<4;s++)
        {
            double span=exposure.At(s), weight=days>0?span/days:0; if(weight<=0)continue;
            var p=Season(w,t,v,s,q,operation); double non=Math.Max(0,p.B-p.G);
            foodDays += span*p.G; nonfoodDays += span*non; biomassDays += span*p.B;
            fr += weight*p.Rf; br += weight*p.Rb;
            foodSource += weight*p.Rf*p.G; nonfoodSource += weight*p.Rb*non; managed += weight*p.Managed;
        }
        double food=fr>0?foodSource/fr:(days>0?foodDays/days:0);
        double nonfood=br>0?nonfoodSource/br:(days>0?nonfoodDays/days:0);
        return new ManagedCapacity(food,nonfood,days>0?biomassDays/days:food+nonfood,fr,br,managed);
    }

    static void Reclassify(WorldState w, Tile t, ref EcologyView v, double q0, double q1)
    {
        if (Math.Abs(q0-q1)<=1e-12 || v.PotentialFood<=0) return;
        int season=(int)Math.Floor(v.Days/w.Config.BaseDays)%4;
        var a=Season(w,t,v,season,q0,0); var b=Season(w,t,v,season,q1,0);
        double oldShare=a.B<=0?0:Numbers.Clamp(a.G/a.B,0,1), newShare=b.B<=0?0:Numbers.Clamp(b.G/b.B,0,1);
        double non=Math.Max(0,v.PotentialFood-v.Food);
        if(newShare>oldShare && oldShare<1) v.Food += non*(newShare-oldShare)/(1-oldShare);
        else if(newShare<oldShare && oldShare>0) v.Food *= newShare/oldShare;
        v.Food=Numbers.Clamp(v.Food,0,v.PotentialFood);
    }

    static GroundEval EvaluateGround(WorldState w, GroundState g, double workerYears, double years, double toolMultiplier=1)
    {
        var t=w.Tiles[g.Tile]; double start=g.Ecology.Days, days=w.Config.TurnDays;
        double d=DistanceRetention(g.Distance), effective=Math.Max(0,workerYears)*d;
        double startE=Math.Max(0,g.Capital);
        double maintenanceWorkers=w.Config.Neolithic.MaintenanceWorkersPerTileE(w.Config.MaintenanceCoefficient)
            * CultivationEconomy.EffortForCapital(w.Config,startE);
        double maintenanceWY=maintenanceWorkers*years;
        double operation=startE<=1e-12 ? (effective>0?1:0) : Math.Min(1,effective/Math.Max(1e-12,maintenanceWY));
        double decayed=CultivationEconomy.Decay(w.Config,startE,operation,years);
        double build=Math.Max(0,effective-maintenanceWY);
        double effort=CultivationEconomy.EffortForCapital(w.Config,decayed)+build/w.Config.Neolithic.BuildWorkerYearsPerTileE;
        double endE=Math.Min(Math.Pow(1/w.Config.ReferenceIntensity,2),CultivationEconomy.CapitalForEffort(w.Config,effort));
        double decayRate=Math.Log(2)/w.Config.Neolithic.DecayHalfLifeYears*(1-Numbers.Clamp(operation,0,1));
        double meanDecay=decayRate*years>1e-10 ? startE*(1-Math.Exp(-decayRate*years))/(decayRate*years) : startE;
        double avgE=Math.Max(0,meanDecay+.5*(endE-decayed));
        double q0=CultivationEconomy.Intensity(w.Config,startE), qavg=CultivationEconomy.Intensity(w.Config,avgE), qend=CultivationEconomy.Intensity(w.Config,endE);

        var v=g.Ecology;
        Reclassify(w,t,ref v,q0,qavg);
        var h=Horizon(w,t,v,qavg,operation,start,days);
        double rate=w.Config.FishPerPerson*w.Config.DaysPerYear/w.Config.BaseDays*d*toolMultiplier;
        double request=rate*Math.Max(0,workerYears);
        var nf=EcologyMath.Stock(Math.Max(0,v.PotentialFood-v.Food),h.Nonfood,h.BiomassRate,0,days);
        var ff=EcologyMath.ManagedStock(v.Food,h.Food,h.FoodRate,h.ManagedFlow,days>0?request/days:0,days);
        double catchAmount=ff.Realized;
        v.Food=ff.Stock; v.PotentialFood=v.Food+nf.Stock;

        // Current source pressure law, but using the same q-managed capacities as this experiment.
        double realizedRate=days>0?catchAmount/days:0, weighted=0;
        var exposure=new SeasonExposure(start,days,w.Config.BaseDays);
        for(int s=0;s<4;s++)
        {
            double weight=days>0?exposure.At(s)/days:0;if(weight<=0)continue;
            var p=Season(w,t,v,s,qavg,operation);
            double safe=EcologyMath.SustainableHarvestRate(p.B,p.Rb,w.Config.SustainableProductionFraction,p.Managed);
            double finite=p.B/Math.Min(w.Config.BaseDays,days)+p.B*p.Rb+p.Managed;
            double seasonal=Math.Min(realizedRate,finite); if(seasonal<=0)continue;
            double util=seasonal/Math.Max(1e-12,safe);
            weighted += weight*Math.Pow(Math.Max(0,util-w.Config.SafeUseRatio),w.Config.PressureExponent);
        }
        double damage=w.Config.AquaticUseDamagePerYear*weighted/w.Config.DaysPerYear;
        double recovery=AquaticEcology.ConditionRecoveryPerDay(t);
        v.AquaticCondition=EcologyMath.Condition(v.AquaticCondition,1,recovery,damage,days);
        v.CurrentVegetation=Vegetation.Update(t.NaturalVegetationPotential,v.CurrentVegetation,v.AquaticCondition,w.Config);
        v.Days=start+days;
        Reclassify(w,t,ref v,qavg,qend);
        int endSeason=(int)Math.Floor(v.Days/w.Config.BaseDays)%4;
        var cap=Season(w,t,v,endSeason,qend,0);
        v.PotentialFood=Numbers.Clamp(v.PotentialFood,0,cap.B);
        v.Food=Numbers.Clamp(v.Food,0,Math.Min(v.PotentialFood,cap.G));

        return new GroundEval { Tile=g.Tile,Distance=g.Distance,WorkerYears=workerYears,EffectiveWork=effective,
            MaintenanceWorkerYears=maintenanceWY,OperatingFraction=operation,StartE=startE,AverageE=avgE,EndE=endE,
            QAverage=qavg,QEnd=qend,Request=request,Catch=catchAmount,ConditionEnd=v.AquaticCondition,
            FoodEnd=v.Food,BiomassEnd=v.PotentialFood,EndView=v };
    }

    static (List<GroundEval> evals,double catchAmount,double fisherYears) PlanAndCommit(WorldState w,List<GroundState> grounds,int slots,double target,int residents,double years)
    {
        double available=Math.Max(0,residents*years); if(available<=0||grounds.Count==0) return (new List<GroundEval>(),0,0);
        var work=grounds.ToDictionary(g=>g.Tile,g=>0.0); var current=grounds.ToDictionary(g=>g.Tile,g=>EvaluateGround(w,g,0,years));
        var touched=new HashSet<int>(); double step=available/Bands, used=0;
        int lastTile=-1;
        for(int band=0;band<Bands && used<available-1e-9;band++)
        {
            int best=-1; double bestMarginal=0;
            foreach(var g in grounds)
            {
                if(!touched.Contains(g.Tile)&&touched.Count>=slots)continue;
                double candidateAdd=Math.Min(step,available-used);
                var next=EvaluateGround(w,g,work[g.Tile]+candidateAdd,years);
                double marginal=next.Catch-current[g.Tile].Catch;
                if(best<0||marginal>bestMarginal+1e-10||Math.Abs(marginal-bestMarginal)<=1e-10&&(g.Distance<grounds.First(x=>x.Tile==best).Distance||g.Distance==grounds.First(x=>x.Tile==best).Distance&&g.Tile<best))
                {best=g.Tile;bestMarginal=marginal;}
            }
            if(best<0||bestMarginal<=1e-9)break;
            double chosenAdd=Math.Min(step,available-used); work[best]+=chosenAdd;used+=chosenAdd;touched.Add(best);lastTile=best;
            var gs=grounds.First(x=>x.Tile==best); current[best]=EvaluateGround(w,gs,work[best],years);
            double total=current.Where(p=>touched.Contains(p.Key)).Sum(p=>p.Value.Catch);
            if(total>=target-1e-7)break;
        }
        // Reduce only the last increment by bisection to avoid intentionally harvesting beyond useful demand.
        if(lastTile>=0)
        {
            double others=current.Where(p=>touched.Contains(p.Key)&&p.Key!=lastTile).Sum(p=>p.Value.Catch);
            double need=Math.Max(0,target-others), hi=work[lastTile], lo=Math.Max(0,hi-step);
            var gs=grounds.First(x=>x.Tile==lastTile);
            for(int i=0;i<40;i++){double mid=(lo+hi)/2;var e=EvaluateGround(w,gs,mid,years);if(e.Catch<need)lo=mid;else hi=mid;}
            work[lastTile]=hi;current[lastTile]=EvaluateGround(w,gs,hi,years);
        }

        var selected=current.Where(p=>work[p.Key]>1e-8).Select(p=>p.Value).OrderBy(e=>e.Tile).ToList();
        foreach(var e in selected)
        {
            var state=grounds.First(g=>g.Tile==e.Tile);
            state.Capital=e.EndE; state.Ecology=e.EndView;
        }
        // Unworked candidates still evolve through the SAME fishing-capital-aware
        // ecology adapter: q decays, accessibility reclassifies accordingly, and
        // natural aquatic stock/condition recover. Do not hand them to the source
        // q=0 lazy projector, because Fishery Capital is sparse experiment state.
        var selectedIds=new HashSet<int>(selected.Select(e=>e.Tile));
        foreach(var g in grounds.Where(g=>!selectedIds.Contains(g.Tile)))
        {
            var idle=EvaluateGround(w,g,0,years);
            g.Capital=idle.EndE; g.Ecology=idle.EndView;
        }
        return (selected,selected.Sum(e=>e.Catch),selected.Sum(e=>e.WorkerYears));
    }

    static int StarvationLosses(int population,double deficitFraction,double mortality,double generationScale,ref double remainder)
    {
        if(population<=0||deficitFraction<=0||generationScale<=0)return 0;
        double progress=remainder+population*Numbers.Fraction(mortality,generationScale)*Numbers.Clamp(deficitFraction,0,1);
        int losses=Math.Min(population,(int)Math.Floor(progress)); remainder=progress-Math.Floor(progress); return losses;
    }

    static double GrowthRate(WorldState w,Camp c,double available,double populationBase,double years)
    {
        double max=c.DeficitTurns>0?0:QoLMortality.CeilingRate(w.Config,c.QoL)*c.DemographicGrowthMultiplier;
        if(max<=1e-12||available<=1e-12)return 0;
        if(TurnResolver.GrowthFood(populationBase,max,years)<=available+1e-8)return max;
        double lo=0,hi=max;for(int i=0;i<64;i++){double mid=(lo+hi)/2;if(TurnResolver.GrowthFood(populationBase,mid,years)<=available)lo=mid;else hi=mid;}return lo;
    }

    static CaseResult RunCase(byte[] original,Site site,int slots,string mode,double maxFisherShare,StreamWriter turns)
    {
        var w=SaveCodec.Load(original); var c=w.Camps.OrderBy(x=>x.Id).First();
        w.Camps=new List<Camp>{c};w.Hunters.Clear();w.Herds.Clear();w.FixedHousing.Clear();
        c.Tile=site.CampTile;c.Population=StartPopulation;c.Workforce=new PopulationLedger();c.Moves=0;c.StationaryTurns=0;c.StationaryYears=0;
        c.DeficitTurns=0;c.DeficitSeverity=0;c.DeathRemainder=0;c.GrowthRemainder=0;c.DemographicGrowthMultiplier=1;
        c.Inventory=new CampInventory{Cloth=1e9,Pots=1e9,HandTools=1e9,Tools=1e9};
        w.FixedHousing.Add(new FixedHousingStock{CampId=c.Id,Tile=c.Tile,Units=1e9,LastUpdatedDays=w.Days});
        c.PlantFood=0;c.AnimalFood=0;c.AquaticFood=StartPopulation*w.Config.Consumption*w.Config.GenerationIntervals*w.Config.StartingReserveGenerations;
        c.FoodNeedsMetRatio=1;c.WaterNeedsMetRatio=1;c.QoL=1;c.RudimentaryExpectation=w.Config.Neolithic.RudimentaryExpectationFloor;
        var grounds=site.Distance.OrderBy(p=>p.Key).Select(p=>new GroundState{Tile=p.Key,Distance=p.Value,Capital=0,Ecology=new EcologyView(w.Tiles[p.Key])}).ToList();
        double years=w.Config.TurnDays/w.Config.DaysPerYear, intervals=w.Config.GenerationIntervals, generationScale=w.Config.TurnDays/w.Config.PracticeDays;
        var result=new CaseResult{Id=$"{mode}_s{site.Seed}_t{site.CampTile}_k{slots}",Mode=mode,Site=site,SlotLimit=slots,MaxPopulation=c.Population};
        double sumFishers=0,sumQ=0,sumCatch=0,sumTarget=0,sumUtil=0;int observations=0;
        for(int turn=1;turn<=Turns&&c.Population>0;turn++)
        {
            int startPop=c.Population; double openingFood=c.Food;
            double populationBase=startPop*w.Config.Consumption*intervals;
            double desiredReserve=NeolithicProduction.DesiredReserveFood(w,startPop,intervals);
            double storage=NeolithicProduction.StorageCapacityAfterPotDepreciation(w,c,intervals,years);
            double reserve=Math.Min(desiredReserve,storage);
            TurnResolver.SplitFoodBuckets(populationBase,openingFood,reserve,out double consumptionIncome,out double reserveFill);
            double maxRate=c.DeficitTurns>0?0:QoLMortality.CeilingRate(w.Config,c.QoL)*c.DemographicGrowthMultiplier;
            double maxGrowth=TurnResolver.GrowthFood(populationBase,maxRate,years);
            double target=consumptionIncome+reserveFill+maxGrowth;
            var plan=PlanAndCommit(w,grounds,slots,target,(int)Math.Floor(startPop*maxFisherShare+1e-9),years);
            c.AquaticFood += plan.catchAmount;
            double consumed=Math.Min(populationBase,c.Food);c.Food= Math.Max(0,c.Food-consumed);c.LastConsumed=consumed;c.FoodDeficit=populationBase-consumed;
            c.FoodNeedsMetRatio=populationBase>0?consumed/populationBase:1;
            int births=0,deaths=0;double growthFood=0;
            if(c.FoodDeficit>1e-8){c.DeficitTurns++;c.DeficitSeverity+=c.FoodDeficit/Math.Max(1,populationBase)*generationScale;result.DeficitTurnsTotal++;}
            else
            {
                c.DeficitTurns=0;c.DeficitSeverity=0;
                double availableGrowth=Math.Max(0,c.Food-reserve);
                double rate=GrowthRate(w,c,availableGrowth,populationBase,years);
                growthFood=TurnResolver.GrowthFood(populationBase,rate,years);
                c.Food=Math.Max(0,c.Food-growthFood);
                double final=startPop*Math.Exp(rate*years);
                double progress=c.GrowthRemainder+Math.Max(0,final-startPop);
                births=(int)Math.Floor(progress);c.GrowthRemainder=progress-births;
            }
            // Current cohort needs/QoL resolve before deaths and births.
            NeolithicProduction.ResolveNeeds(w,c,years,startPop);
            if(c.FoodDeficit>1e-8 && c.DeficitTurns>w.Config.StarvationGraceTurns)
                deaths=StarvationLosses(startPop,c.FoodDeficit/Math.Max(1e-9,populationBase),w.Config.Mortality,generationScale,ref c.DeathRemainder);
            c.Population=Math.Max(0,c.Population-deaths+births);c.Workforce.Normalize(c.Population);
            // Keep only the ordinary operating reserve; the fishing allocator never deliberately overharvests for surplus.
            c.Food=Math.Min(c.Food,reserve);
            w.Days+=w.Config.TurnDays;
            // Fishery ecology is carried in GroundState because production Tile
            // has no Fishery Capital field yet. Every ground must remain on the
            // same authoritative experiment clock.
            if(grounds.Any(g=>Math.Abs(g.Ecology.Days-w.Days)>1e-7))
                throw new InvalidOperationException("Fishing ground clock drift.");
            result.Births+=births;result.Deaths+=deaths;result.MaxPopulation=Math.Max(result.MaxPopulation,c.Population);
            if(turn==10)result.P200=c.Population;if(turn==25)result.P500=c.Population;if(turn==40)result.P800=c.Population;
            double qmean=plan.evals.Count==0?0:plan.evals.Average(e=>e.QAverage),qmax=plan.evals.Count==0?0:plan.evals.Max(e=>e.QEnd);
            double fisherHeads=years>0?plan.fisherYears/years:0;
            sumFishers+=fisherHeads;sumQ+=qmean;sumCatch+=plan.catchAmount;sumTarget+=target;sumUtil+=startPop>0?fisherHeads/startPop:0;observations++;
            turns.WriteLine(string.Join(",",new object[]{result.Id,mode,site.Seed,site.CampTile,site.WaterClass,site.CountClass,site.Distance.Count,slots,turn,w.Days/w.Config.DaysPerYear,startPop,c.Population,
                plan.evals.Count,string.Join(";",plan.evals.Select(e=>e.Tile)),fisherHeads,plan.catchAmount,target,c.FoodNeedsMetRatio,births,deaths,c.QoL,c.Food,qmean,qmax,
                plan.evals.Count==0?0:plan.evals.Average(e=>e.ConditionEnd),string.Join(";",plan.evals.Select(e=>e.QEnd.ToString("F4",CultureInfo.InvariantCulture))),
                string.Join(";",plan.evals.Select(e=>e.Distance)),growthFood}.Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture))));
        }
        result.FinalPopulation=c.Population;result.FinalQoL=c.QoL;result.FinalFood=c.Food;
        result.MeanFishers=observations>0?sumFishers/observations:0;result.MeanQ=observations>0?sumQ/observations:0;result.MaxQ=grounds.Count==0?0:grounds.Max(g=>CultivationEconomy.Intensity(w.Config,g.Capital));
        result.MeanCatch=observations>0?sumCatch/observations:0;result.MeanTarget=observations>0?sumTarget/observations:0;result.MeanUtilization=observations>0?sumUtil/observations:0;
        return result;
    }

    static double Percentile(IEnumerable<double> xs,double p)
    {
        var a=xs.OrderBy(x=>x).ToArray();if(a.Length==0)return double.NaN;double z=p*(a.Length-1);int lo=(int)Math.Floor(z),hi=(int)Math.Ceiling(z);return lo==hi?a[lo]:a[lo]+(a[hi]-a[lo])*(z-lo);
    }

    static void Main()
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;Directory.CreateDirectory("SweepResults");
        var cfg=Config();var ro=JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"),Json);
        var worlds=new Dictionary<int,byte[]>();var all=new List<Site>();
        foreach(int seed in Seeds)
        {
            Console.WriteLine("GENERATE "+seed);var c=JsonSerializer.Deserialize<SimulationConfig>(JsonSerializer.Serialize(cfg,Json),Json);
            var w=new WorldGenerator().Generate(seed,c,ro);worlds[seed]=SaveCodec.Save(w);all.AddRange(EnumerateSites(w));
        }
        using(var sw=new StreamWriter("SweepResults/site_counts.csv")){sw.WriteLine("countClass,waterClass,n");foreach(var g in all.GroupBy(s=>(s.CountClass,s.WaterClass)))sw.WriteLine($"{g.Key.CountClass},{g.Key.WaterClass},{g.Count()}");}
        var sample=SampleSites(all);
        using(var sw=new StreamWriter("SweepResults/sites.csv")){sw.WriteLine("seed,campTile,waterClass,countClass,candidates,naturalScore,grounds");foreach(var s in sample)sw.WriteLine($"{s.Seed},{s.CampTile},{s.WaterClass},{s.CountClass},{s.Distance.Count},{s.NaturalScore.ToString("R",CultureInfo.InvariantCulture)},{string.Join(";",s.Distance.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value))}");}
        using var turns=new StreamWriter("SweepResults/turns.csv");
        turns.WriteLine("case,mode,seed,campTile,waterClass,countClass,naturalCandidates,slotLimit,turn,year,startPopulation,endPopulation,workedGrounds,groundIds,fisherHeads,catch,target,foodRatio,births,deaths,qol,endReserve,meanQ,maxQ,meanCondition,qEnds,distances,growthFood");
        var results=new List<CaseResult>();
        foreach(var s in sample)
        {
            int naturalSlots=s.CountClass=="exact1"?1:s.CountClass=="exact2"?2:3;
            results.Add(RunCase(worlds[s.Seed],s,naturalSlots,"natural",1.0,turns));
            results.Add(RunCase(worlds[s.Seed],s,naturalSlots,"labor80",0.80,turns));
            results.Add(RunCase(worlds[s.Seed],s,naturalSlots,"labor60",0.60,turns));
            if(s.CountClass=="3plus"){results.Add(RunCase(worlds[s.Seed],s,1,"paired",1.0,turns));results.Add(RunCase(worlds[s.Seed],s,2,"paired",1.0,turns));}
            else if(s.CountClass=="exact2") results.Add(RunCase(worlds[s.Seed],s,1,"paired",1.0,turns));
            turns.Flush();
        }
        // One-ground risk audit: run EVERY warm exact-one-ground site, not just
        // percentile samples, both as an ecological ceiling and with 40% resident
        // labor reserved from Fishing for the rest of the economy.
        foreach(var s in all.Where(x=>x.CountClass=="exact1").OrderBy(x=>x.Seed).ThenBy(x=>x.CampTile))
        {
            results.Add(RunCase(worlds[s.Seed],s,1,"all1-natural",1.0,turns));
            results.Add(RunCase(worlds[s.Seed],s,1,"all1-labor60",0.60,turns));
            turns.Flush();
        }
        using(var sw=new StreamWriter("SweepResults/summary.csv"))
        {
            sw.WriteLine("case,mode,seed,campTile,waterClass,countClass,naturalCandidates,slotLimit,p200,p500,p800,finalPopulation,maxPopulation,births,deaths,deficitTurns,meanFishers,meanFisherShare,meanQ,maxQ,finalQoL,finalReserve,meanCatch,meanTarget");
            foreach(var r in results)sw.WriteLine(string.Join(",",new object[]{r.Id,r.Mode,r.Site.Seed,r.Site.CampTile,r.Site.WaterClass,r.Site.CountClass,r.Site.Distance.Count,r.SlotLimit,r.P200,r.P500,r.P800,r.FinalPopulation,r.MaxPopulation,r.Births,r.Deaths,r.DeficitTurnsTotal,r.MeanFishers,r.MeanUtilization,r.MeanQ,r.MaxQ,r.FinalQoL,r.FinalFood,r.MeanCatch,r.MeanTarget}.Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture))));
        }
        using(var sw=new StreamWriter("SweepResults/distribution.csv"))
        {
            sw.WriteLine("mode,siteClass,waterClass,slotLimit,n,p10_500,p25_500,median_500,p75_500,p90_500,p10_800,p25_800,median_800,p75_800,p90_800,medianMaxPopulation,medianFisherShare,medianMaxQ,deathCases");
            foreach(var g in results.GroupBy(r=>(r.Mode,r.Site.CountClass,r.Site.WaterClass,r.SlotLimit)).OrderBy(g=>g.Key.Mode).ThenBy(g=>g.Key.CountClass).ThenBy(g=>g.Key.WaterClass).ThenBy(g=>g.Key.SlotLimit))
                sw.WriteLine(string.Join(",",new object[]{g.Key.Mode,g.Key.CountClass,g.Key.WaterClass,g.Key.SlotLimit,g.Count(),
                    Percentile(g.Select(x=>(double)x.P500),.1),Percentile(g.Select(x=>(double)x.P500),.25),Percentile(g.Select(x=>(double)x.P500),.5),Percentile(g.Select(x=>(double)x.P500),.75),Percentile(g.Select(x=>(double)x.P500),.9),
                    Percentile(g.Select(x=>(double)x.P800),.1),Percentile(g.Select(x=>(double)x.P800),.25),Percentile(g.Select(x=>(double)x.P800),.5),Percentile(g.Select(x=>(double)x.P800),.75),Percentile(g.Select(x=>(double)x.P800),.9),
                    Percentile(g.Select(x=>(double)x.MaxPopulation),.5),Percentile(g.Select(x=>x.MeanUtilization),.5),Percentile(g.Select(x=>x.MaxQ),.5),g.Count(x=>x.Deaths>0)}.Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture))));
        }
        // Paired 3+ sites isolate the slot-count effect from geography.
        using(var sw=new StreamWriter("SweepResults/paired_3plus.csv"))
        {
            sw.WriteLine("seed,campTile,waterClass,p500_k1,p500_k2,p500_k3,p800_k1,p800_k2,p800_k3");
            foreach(var g in results.Where(r=>r.Site.CountClass=="3plus").GroupBy(r=>(r.Site.Seed,r.Site.CampTile,r.Site.WaterClass)))
            {
                int V(int k,bool y800){var x=g.FirstOrDefault(r=>r.SlotLimit==k);return x==null?0:(y800?x.P800:x.P500);}
                sw.WriteLine($"{g.Key.Seed},{g.Key.CampTile},{g.Key.WaterClass},{V(1,false)},{V(2,false)},{V(3,false)},{V(1,true)},{V(2,true)},{V(3,true)}");
            }
        }
        File.WriteAllText("SweepResults/manifest.json",JsonSerializer.Serialize(new{baseline=Baseline,seeds=Seeds,turns=Turns,bands=Bands,startPopulation=StartPopulation,
            primaryFilter="all radius-3 connected Shallows/Lake grounds have minimum seasonal temperature > 0C",
            distanceRetention="0.75^(d-1), d=1..3",capitalModel="CultivationEconomy Intensity/EffortForCapital/CapitalForEffort/Decay; open build/maintenance factors=1; effective on-ground effort is worker-years times route retention; maintenance first, residual effort builds capital; same fishing effort also harvests",
            accessibility="G=G0+(B-G0)q^2",managedRenewal="CultivationRegenFlux*q*B*BiomassRate*operatingFraction",nonFoodNeeds="fully supplied Cloth/Housing/Pots to isolate fishing ecology + current QoL/social-cohesion demography; labor80/labor60 reserve 20%/40% of residents from Fishing as economic-headroom sensitivities",
            caveats=new[]{"resident Fishing is an experiment adapter, not production integration","no Fishing Tools multiplier in central sweep","no trade immigration fission","cold-water catchments excluded because of known source condition bug","nonfood production labor/cost is not charged; sizes are ecological-demographic ceilings, not final live-economy census"}},Json));
        Console.WriteLine($"DONE sites_all={all.Count} sampled={sample.Count} cases={results.Count}");
    }
}
