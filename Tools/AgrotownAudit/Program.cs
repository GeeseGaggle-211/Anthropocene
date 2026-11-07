using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private const string Baseline = "07f3501c2d5e047e44d61e85cc0d4613b54572d6";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static readonly List<Row> Rows = new List<Row>();
    private static readonly List<object> Worlds = new List<object>();
    private static long NativeIntegrations, FlowReferenceSteps;
    private static double MaxMassError, MaxFlowCompositionError, MaxNativeTailRange;
    private sealed class Site { public int Id; public double Score, Suitability; }
    private sealed class Cell
    {
        public double AnnualFood, HarvestWorkers, MaintFactor, TailRange, FlowMinStockFraction;
        public double[] SeasonalHarvestWorkers = new double[4];
    }
    private sealed class Row
    {
        public int Seed, Center, Radius, Tiles;
        public string Cohort, Model;
        public double Q, KneeQ, DecisionYears, TravelPerExtraStep;
        public double FoodPerYear, FoodOnlyPopulation, PopulationWith25PercentHeadroom;
        public double HarvestWorkersAtCapacity, MaintenanceWorkers, MinimumFarmWorkersAtCapacity;
        public double FarmerFractionAtCapacity, Target5000FarmWorkers, Target5000FarmerFraction;
        public bool Has5000FoodHeadroom, Target5000LaborFeasible;
    }
    private static double Quantile(IEnumerable<double> values, double p)
    {
        var a = values.OrderBy(v => v).ToArray(); double x = (a.Length - 1) * p;
        int i = (int)Math.Floor(x); return a[i] + (a[Math.Min(i + 1, a.Length - 1)] - a[i]) * (x - i);
    }
    private static object Dist(IEnumerable<double> values)
    {
        var a = values.ToArray(); return new { n = a.Length, min = a.Min(), p25 = Quantile(a, .25), median = Quantile(a, .5), p75 = Quantile(a, .75), max = a.Max() };
    }
    private static double Error(double a, double b) => Math.Abs(a-b)/Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b)));
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static double Effort(SimulationConfig c, double q, double knee)
    {
        // Diagnostic application of the already-existing model9-native knee law.
        // Physical ReferenceIntensity is unchanged. No equipment service is gifted.
        double q0=c.ReferenceIntensity, e=q*q/(q0*q0), ek=knee*knee/(q0*q0);
        if (q<=knee) return e;
        double beta=Math.Log(c.Neolithic.CultivationMarginalEffortMultiplierPerTenth)/.1;
        return ek+2*knee/(q0*q0*beta)*(Math.Exp(beta*(q-knee))-1);
    }
    private static Dictionary<int,int> Reach(WorldState w,int center,int radius)
    {
        var d=new Dictionary<int,int>{{center,0}}; var queue=new Queue<int>(); queue.Enqueue(center);
        while(queue.Count>0) { int i=queue.Dequeue(); if(d[i]>=radius) continue;
            foreach(int n in w.Map.Neighbors(i)) if(!d.ContainsKey(n)&&w.Tiles[n].TraversableLand) {d[n]=d[i]+1;queue.Enqueue(n);} }
        return d.Where(kv=>CultivationEconomy.Cultivable(w.Tiles[kv.Key])).ToDictionary(kv=>kv.Key,kv=>kv.Value);
    }
    private static Cell Native(WorldState w,int id,double q,double years)
    {
        var t=w.Tiles[id]; var c=w.Config; double e=Math.Pow(q/c.ReferenceIntensity,2), dt=years*c.DaysPerYear;
        double oldE=t.CultivationCapital,oldO=t.CultivationOperatingFraction; int oldOwner=t.CultivationOperator;
        t.CultivationCapital=e;t.CultivationOperatingFraction=1;t.CultivationOperator=0;
        try {
            var v=new EcologyView(t){Food=0,PotentialFood=0,CultivationCapital=e,LandCondition=1,Days=0};
            int count=Math.Max(40,(int)Math.Ceiling(800/years)); double food=0,work=0,min=double.PositiveInfinity,max=0;
            for(int k=0;k<count;k++) {
                var cap=new EcologyCapacity(w,t,v,(int)Math.Floor(v.Days/c.BaseDays)%4,1);
                double share=cap.Food>0?Math.Max(0,cap.Food-cap.WildFood)/cap.Food:0;
                var claim=new Demand{Tile=id,Entity=0,Kind="Cultivation",Food=v.Food,ImpactMultiplier=1};
                var r=EcologicalPressure.Integrate(w,id,new[]{claim},v.Days,dt,averageCultivationCapital:e,endingCultivationCapital:e,operatingFraction:1,projectWood:false,openingEcology:v);
                NativeIntegrations++; var b=r.Balance;
                double er=Math.Max(Error(b.StartingFood+b.FoodRecharge,b.FoodHarvest+b.FoodOverflow+b.EndingFood),Error(b.StartingBiomass+b.BiomassRecharge,b.BiomassHarvest+b.BiomassOverflow+b.EndingBiomass));
                MaxMassError=Math.Max(MaxMassError,er);Check(er<1e-8,"Native conservation");
                Check(r.View.Food>=-1e-8&&r.View.PotentialFood+1e-7>=r.View.Food,"Native stock bounds");
                double receipt=r.Transfers.Sum(x=>x.Food);
                if(k>=count-8) {double annual=receipt/years;food+=annual; min=Math.Min(min,annual);max=Math.Max(max,annual);
                    work+=annual*(1-share+share/c.FarmerProductivity)/(c.GatherPerPerson*c.DaysPerYear/c.BaseDays);}
                v=r.View;
            }
            CultivationEconomy.Factors(c,t,out _,out double factor);
            double tail=(max-min)/Math.Max(1,max);MaxNativeTailRange=Math.Max(MaxNativeTailRange,tail);Check(tail<1e-6,"Native no convergence");
            return new Cell{AnnualFood=food/8,HarvestWorkers=work/8,MaintFactor=factor,TailRange=tail};
        } finally {t.CultivationCapital=oldE;t.CultivationOperatingFraction=oldO;t.CultivationOperator=oldOwner;}
    }
    private static Cell FlowEnvelope(WorldState w,int id,double q)
    {
        // Experimental fixed seasonal harvest schedule, NOT the production resolver.
        // h_s = .8*r_s*K_s + M_s; h never exceeds sustainable zero-stock forcing.
        // Four profiles and one affine annual map; Repeat() is independent of elapsed years.
        // Initial capacity clamps are included in each profile map. Receipts are h_s*days.
        var t=w.Tiles[id];var c=w.Config;var v=new EcologyView(t){CultivationCapital=Math.Pow(q/c.ReferenceIntensity,2),LandCondition=1};
        var caps=Enumerable.Range(0,4).Select(s=>new EcologyCapacity(w,t,v,s,1)).ToArray();
        var h=caps.Select(p=>.8*p.FoodRate*p.Food+p.ManagedFlow).ToArray();
        var annual=SeasonalAffine.Identity; var nonfood=SeasonalAffine.Identity;
        for(int s=0;s<4;s++) {
            var p=caps[s];double a=Math.Exp(-p.FoodRate*c.BaseDays);
            double b=p.FoodRate>1e-18?(p.Food+(p.ManagedFlow-h[s])/p.FoodRate)*(1-a):(p.ManagedFlow-h[s])*c.BaseDays;
            Check(b>=-1e-8,"Candidate negative intercept");
            annual=annual.Then(new SeasonalAffine(1,0,p.Food)).Then(new SeasonalAffine(a,Math.Max(0,b),p.Food));
            nonfood=nonfood.Then(new SeasonalAffine(1,0,p.Biomass-p.Food)).Then(SeasonalAffine.Recovery(p.Biomass-p.Food,p.BiomassRate,c.BaseDays));
        }
        double f=0,n=0,minFraction=double.PositiveInfinity;
        for(int y=0;y<200;y++) for(int s=0;s<4;s++) {
            var p=caps[s];var ff=EcologyMath.ManagedStock(f,p.Food,p.FoodRate,p.ManagedFlow,h[s],c.BaseDays);
            var nn=EcologyMath.Stock(n,p.Biomass-p.Food,p.BiomassRate,0,c.BaseDays);FlowReferenceSteps++;
            double er=Error(f+ff.Recharge,ff.Stock+ff.Realized+ff.Overflow);MaxMassError=Math.Max(MaxMassError,er);Check(er<1e-8,"Flow conservation");
            Check(Error(ff.Realized,h[s]*c.BaseDays)<1e-10,"Flow was supply limited");
            f=ff.Stock;n=nn.Stock;Check(f>=-1e-8&&n>=-1e-8&&f+n<=p.Biomass+1e-6,"Flow stock bounds");
            if(y>=192&&p.Food>0) minFraction=Math.Min(minFraction,f/p.Food);
        }
        double closed=annual.Repeat(0,200);double error=Error(f,closed);MaxFlowCompositionError=Math.Max(MaxFlowCompositionError,error);Check(error<1e-10,"Flow repeat/reference mismatch");
        foreach(int horizon in new[]{1,5,20}) {
            double state=0;for(int y=0;y<200;y+=horizon)state=annual.Repeat(state,horizon);
            error=Error(state,closed);MaxFlowCompositionError=Math.Max(MaxFlowCompositionError,error);Check(error<1e-10,"Flow horizon partition");
        }
        var result=new Cell{AnnualFood=h.Sum()*c.BaseDays,FlowMinStockFraction=minFraction};
        CultivationEconomy.Factors(c,t,out _,out result.MaintFactor);
        for(int s=0;s<4;s++) {double share=caps[s].Food>0?Math.Max(0,caps[s].Food-caps[s].WildFood)/caps[s].Food:0;
            result.SeasonalHarvestWorkers[s]=h[s]*(1-share+share/c.FarmerProductivity)/(c.GatherPerPerson/c.BaseDays);}
        result.HarvestWorkers=result.SeasonalHarvestWorkers.Average();return result;
    }
    private static void Add(WorldState w,Site site,string cohort,int radius,double q,double knee,double years,string model,double travel,Dictionary<int,int> routes,Func<int,Cell> get)
    {
        double food=0,harvest=0,maintenance=0;var peaks=new double[4];
        foreach(var pair in routes.Where(kv=>kv.Value<=radius)) {var cell=get(pair.Key);double cost=1+travel*Math.Max(0,pair.Value-1);
            food+=cell.AnnualFood;harvest+=cell.HarvestWorkers*cost;
            maintenance+=w.Config.Neolithic.MaintenanceWorkersPerTileE(w.Config.MaintenanceCoefficient)*cell.MaintFactor*Effort(w.Config,q,knee)*cost;
            for(int s=0;s<4;s++)peaks[s]+=cell.SeasonalHarvestWorkers[s]*cost;}
        if(model=="candidate_seasonal_flow")harvest=peaks.Max();
        double p=food/PhysicalDemography.FoodPerPersonYear(w.Config),workers=Math.Max(harvest,maintenance);
        double targetWork=Math.Max(maintenance,harvest*5000/Math.Max(p,1e-12));
        Rows.Add(new Row{Seed=w.Seed,Center=site.Id,Cohort=cohort,Radius=radius,Tiles=routes.Count(kv=>kv.Value<=radius),Q=q,KneeQ=knee,DecisionYears=years,TravelPerExtraStep=travel,Model=model,
            FoodPerYear=food,FoodOnlyPopulation=p,PopulationWith25PercentHeadroom=p/1.25,HarvestWorkersAtCapacity=harvest,MaintenanceWorkers=maintenance,MinimumFarmWorkersAtCapacity=workers,
            FarmerFractionAtCapacity=workers/Math.Max(p,1e-12),Target5000FarmWorkers=targetWork,Target5000FarmerFraction=targetWork/5000,Has5000FoodHeadroom=p>=6250,Target5000LaborFeasible=p>=5000&&targetWork<=5000});
    }
    private static void Run(int seed,string output)
    {
        var cfg=JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"),Json);
        var rg=JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"),Json);
        cfg.Width=1024;cfg.Height=512;cfg.Players=80;cfg.Shape=WorldShape.Continents;
        Console.WriteLine("GENERATING "+seed);var w=new WorldGenerator().Generate(seed,cfg,rg);
        var eligible=new List<Site>();
        foreach(var t in w.Tiles.Where(t=>t.River&&CultivationEconomy.Cultivable(t))) {
            var ids=w.Map.Radius(t.Id,1).Where(id=>CultivationEconomy.Cultivable(w.Tiles[id])).ToArray();if(ids.Length!=7)continue;
            double suit=ids.Average(id=>w.Tiles[id].Seasons.Average(s=>EcologyMath.CropSuitability(cfg,s.Moisture)));
            if(suit>=.8)eligible.Add(new Site{Id=t.Id,Suitability=suit,Score=ids.Sum(id=>w.Tiles[id].Seasons.Average(s=>s.MaxPotentialFood))});
        }
        double threshold=Quantile(eligible.Select(s=>s.Score),.75);var fertile=eligible.Where(s=>s.Score>=threshold).OrderBy(s=>s.Score).ThenBy(s=>s.Id).ToArray();
        var selected=Enumerable.Range(0,16).Select(i=>(cohort:"fertile",site:fertile[(int)Math.Round((fertile.Length-1.0)*i/15)])).ToList();
        if(seed==11)selected.Add(("desert_preset",new Site{Id=PlaytestScenarios.Tile(w,1)}));
        var cache=new Dictionary<string,Cell>();
        Cell Get(int id,double q,double years,string model) {string key=id+":"+q+":"+years+":"+model;if(!cache.TryGetValue(key,out var value)){value=model=="native_pulse"?Native(w,id,q,years):FlowEnvelope(w,id,q);cache.Add(key,value);}return value;}
        foreach(var sel in selected) {
            var routes=Reach(w,sel.site.Id,4);
            foreach(double years in new[]{1.0,5.0,20.0})Add(w,sel.site,sel.cohort,1,.4,.4,years,"native_pulse",0,routes,id=>Get(id,.4,years,"native_pulse"));
            foreach(double q in new[]{.4,.5,.6})foreach(int radius in new[]{1,2,3,4})foreach(double knee in new[]{.4,.52,.62})foreach(string model in new[]{"native_pulse","candidate_seasonal_flow"})foreach(double travel in new[]{0.0,.15})
                Add(w,sel.site,sel.cohort,radius,q,knee,20,model,travel,routes,id=>Get(id,q,20,model));
        }
        Worlds.Add(new{seed,moist=eligible.Count,fertile=fertile.Length,selected=selected.Select(s=>new{s.cohort,s.site.Id,s.site.Score,s.site.Suitability}),cachedCells=cache.Count});
        File.WriteAllText(Path.Combine(output,"rows-"+seed+".json"),JsonSerializer.Serialize(Rows.Where(r=>r.Seed==seed),Json));Console.WriteLine("DONE "+seed+" cells="+cache.Count);
    }
    private static string Sha(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    private static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;string output=args.Length>0?args[0]:"TestResults/AgrotownAudit";Directory.CreateDirectory(output);
        foreach(int seed in new[]{11,23,47,101,1729}){Run(seed,output);GC.Collect();}
        var cfg=JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"),Json);
        var mortality=new[]{1000,3000,5000,10000,50000,100000}.Select(n=>{var r=PhysicalDemography.Rates(cfg,n,1,1,n);return new{population=n,r.Birth,r.Exposure,r.Crowding,r.Disease,Net=r.Birth-r.Death,EqualMitigationForZeroGrowth=(r.Birth-r.Exposure)/(r.Crowding+r.Disease)};}).ToArray();
        var groups=Rows.GroupBy(r=>new{r.Cohort,r.Model,r.Radius,r.Q,r.KneeQ,r.DecisionYears,r.TravelPerExtraStep}).Select(g=>new{g.Key,foodPopulation=Dist(g.Select(r=>r.FoodOnlyPopulation)),populationWithHeadroom=Dist(g.Select(r=>r.PopulationWith25PercentHeadroom)),tiles=Dist(g.Select(r=>(double)r.Tiles)),maintenance=Dist(g.Select(r=>r.MaintenanceWorkers)),targetFarmers=Dist(g.Select(r=>r.Target5000FarmWorkers)),headroomPass=g.Count(r=>r.Has5000FoodHeadroom),laborPass=g.Count(r=>r.Target5000LaborFeasible)}).ToArray();
        var summary=new{baseline=Baseline,checks=new{NativeIntegrations,FlowReferenceSteps,MaxMassError,MaxFlowCompositionError,MaxNativeTailRange},worlds=Worlds,mortality,groups,
            scope="Diagnostic only; mature fixed fields, full operation, no tools, no demography in food trials, no nonfood goods, household water, settlement competition, transport capacity or stockpile closures. 15% extra work per land step beyond radius one is a disclosed sensitivity, not a calibrated route rule. Candidate flow h_s=.8*r_s*K_s+M_s is a fixed seasonal throughput envelope; exact source ManagedStock reference agrees with bounded annual affine composition, but the proposal is NOT wired to the authoritative shared-claim resolver. It does not prove a live town of 5000. Food headroom is capacity/1.25, not excess consumption. Candidate farm labor uses the peak summed seasonal requirement; native pulse labor uses the existing horizon convention. q_k variation changes cost only using the already existing model9 law, not physical q or biomass.",
            sourceHashes=Directory.GetFiles("Assets/_Anthropocene/Simulation","*.cs").OrderBy(x=>x).ToDictionary(x=>x,x=>Sha(x)),openingConfigHash=Sha("Assets/_Anthropocene/Resources/OpeningConfig.json")};
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summary,Json));
        var fields=typeof(Row).GetFields();using(var wr=new StreamWriter(Path.Combine(output,"rows.csv"))){wr.WriteLine(string.Join(",",fields.Select(f=>f.Name)));foreach(var r in Rows)wr.WriteLine(string.Join(",",fields.Select(f=>Convert.ToString(f.GetValue(r),CultureInfo.InvariantCulture))));}
        Console.WriteLine("SUMMARY_BEGIN");Console.WriteLine(JsonSerializer.Serialize(new{summary.baseline,summary.checks,mortality,
            central=groups.Where(g=>g.Key.Cohort=="fertile"&&g.Key.TravelPerExtraStep==0&&g.Key.Q==.4&&g.Key.KneeQ==.4),
            advanced=groups.Where(g=>g.Key.Cohort=="fertile"&&g.Key.TravelPerExtraStep==.15&&g.Key.DecisionYears==20&&g.Key.KneeQ==.52&&g.Key.Q==.6)},Json));Console.WriteLine("SUMMARY_END");
    }
}
