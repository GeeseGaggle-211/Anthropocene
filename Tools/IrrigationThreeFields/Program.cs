using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private const string Baseline = "07f3501c2d5e047e44d61e85cc0d4613b54572d6";
    private static readonly JsonSerializerOptions J = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static readonly List<Row> Rows = new List<Row>();
    private static readonly List<StepRow> Steps = new List<StepRow>();
    private static readonly List<object> Profiles = new List<object>();
    private static long Integrations;
    private static double MaxMassError, MaxStartDifference, MaxTailRange;
    private static void Check(bool ok, string msg) { if (!ok) throw new InvalidOperationException(msg); }
    private static double Rel(double a, double b) => Math.Abs(a-b)/Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b)));
    private static double MeanB(Tile t) => t.Seasons.Average(s => (double)s.MaxPotentialFood);
    private static double MeanM(Tile t) => t.Seasons.Average(s => (double)s.Moisture);
    private static Tile Copy(Tile t)
    {
        var z = new Tile();
        foreach (var f in typeof(Tile).GetFields(BindingFlags.Instance|BindingFlags.Public))
        { var v=f.GetValue(t); f.SetValue(z,v is Array a ? a.Clone() : v); }
        return z;
    }
    private sealed class Fixture
    {
        public string Name, Kind;
        public int Center, Source;
        public Tile[] Fields;
        public int[] Dry, Wet;
        public double DryMoisture, DryBiomassRatio;
    }
    private sealed class Cell
    {
        public double AnnualFood, HarvestWorkers, MaintenanceWorkers, CropAttributedAnnualFood, TailRange;
        public EcologyView End;
    }
    private sealed class Row
    {
        public int Seed, Center, IrrigatedFields;
        public string Fixture, Kind, Staffing;
        public double Q, Years, Coverage, TargetMoisture, DryMoisture, DryBiomassRatio;
        public double BaselineFoodYear, FoodYear, AddedFoodYear, RelativeGain, NeighborFoodYear, CenterFoodYear;
        public double FoodPopulation, AddedFoodPopulation, CropAttributedFoodYear;
        public double HarvestWorkers, MaintenanceWorkers, FarmWorkers, BaselineFarmWorkers, AddedFarmWorkers;
        public double ExtraNonfarmHeadcountBeforeOtherNeeds, After15CanalWorkers, After30CanalWorkers;
        public double EndingBiomass, EndingFood;
    }
    private sealed class StepRow
    {
        public int Seed, Center;
        public string Fixture, Arm;
        public double Q, YearStart, YearEnd, DeltaYears, WaterRequirementRatio, RequestedCoverage;
        public double CanalIntakeCapacityRatio, Transmission, SuppliedCoverage;
        public double ReferenceSourceWaterYear, RequestedDeliveredWater, SourceWaterStart, SourceWaterEnd;
        public double WaterRecharge, WaterOverflow, CanalWithdrawal, BackgroundWithdrawal, DeliveredWater, ConveyanceLoss;
        public double FoodYear, FoodPopulation, CenterFoodYear, NeighborFoodYear, HarvestWorkers, CropAttributedFoodYear;
    }
    private static void Balance(TileEcologyResult r)
    {
        Integrations++;
        var b=r.Balance;
        double er=Math.Max(Rel(b.StartingFood+b.FoodRecharge,b.FoodHarvest+b.FoodOverflow+b.EndingFood),
            Math.Max(Rel(b.StartingBiomass+b.BiomassRecharge,b.BiomassHarvest+b.BiomassOverflow+b.EndingBiomass),
                Rel(b.StartingWater+b.WaterRecharge,b.WaterHarvest+b.WaterOverflow+b.EndingWater)));
        MaxMassError=Math.Max(MaxMassError,er);
        Check(er<1e-8,"Stock mass balance: "+er);
        Check(r.View.Food>=-1e-7&&r.View.PotentialFood+1e-6>=r.View.Food&&r.View.Freshwater>=-1e-7,"Stock bounds");
    }
    private static Tile WetOverlay(Tile original, double coverage, double target)
    {
        var t=Copy(original);
        for(int s=0;s<4;s++) t.Seasons[s].Moisture=(float)(original.Seasons[s].Moisture+coverage*Math.Max(0,target-original.Seasons[s].Moisture));
        // Only crop moisture changes. No regenerated biomass, rainfall, terrain or fresh-water stock.
        return t;
    }
    private static double CropShare(WorldState w, Tile t, EcologyView v)
    {
        int phase=((int)Math.Floor(v.Days/w.Config.BaseDays)%4+4)%4;
        var c=new EcologyCapacity(w,t,v,phase,1);
        return c.Food>0?Math.Max(0,c.Food-c.WildFood)/c.Food:0;
    }
    private static double WorkPerFood(WorldState w,double share)
    {
        double mix=w.Config.CropHarvestingOnly ? 1-share+share/w.Config.FarmerProductivity : 1/w.Config.FarmerProductivity;
        return mix/(w.Config.GatherPerPerson*w.Config.DaysPerYear/w.Config.BaseDays);
    }
    private static Cell Iterate(WorldState w, Tile t, double q, double years, bool empty, double? workers)
    {
        var original=w.Tiles[t.Id]; w.Tiles[t.Id]=t;
        try
        {
            var c=w.Config; double e=Math.Pow(q/c.ReferenceIntensity,2),dt=years*c.DaysPerYear;
            t.CultivationCapital=e;t.CultivationOperatingFraction=1;t.CultivationOperator=0;
            var v=new EcologyView(t){Days=0,CultivationCapital=e,LandCondition=1};
            var cap=new EcologyCapacity(w,t,v,0,1);
            v.Food=empty?0:cap.Food;v.PotentialFood=empty?0:cap.Biomass;
            int turns=Math.Max(40,(int)Math.Ceiling(800/years));
            double food=0,crop=0,work=0,low=double.PositiveInfinity,high=0;
            for(int turn=0;turn<turns;turn++)
            {
                double share=CropShare(w,t,v),cost=WorkPerFood(w,share);
                double request=workers.HasValue?Math.Min(Math.Max(0,v.Food),workers.Value*years/cost):Math.Max(0,v.Food);
                var claim=new Demand{Tile=t.Id,Entity=0,Kind="Cultivation",Food=request,ImpactMultiplier=1};
                var r=EcologicalPressure.Integrate(w,t.Id,new[]{claim},v.Days,dt,averageCultivationCapital:e,endingCultivationCapital:e,operatingFraction:1,projectWood:false,openingEcology:v);
                Balance(r);Check(Rel(r.View.CultivationCapital,e)<1e-12,"Fixed q lost");
                double annual=r.Transfers.Sum(x=>x.Food)/years;
                if(turn>=turns-8){food+=annual;crop+=annual*share;work+=annual*cost;low=Math.Min(low,annual);high=Math.Max(high,annual);}
                v=r.View;
            }
            t.CurrentVegetation=v.CurrentVegetation;
            CultivationEconomy.Factors(c,t,out _,out double factor);
            double maintenance=c.Neolithic.MaintenanceWorkersPerTileE(c.MaintenanceCoefficient)*factor*CultivationEconomy.EffortForCapital(c,e);
            return new Cell{AnnualFood=food/8,CropAttributedAnnualFood=crop/8,HarvestWorkers=work/8,MaintenanceWorkers=maintenance,End=v,TailRange=(high-low)/Math.Max(1,high)};
        }
        finally{w.Tiles[t.Id]=original;}
    }
    private static Cell Steady(WorldState w, Tile t, double q, double years, double? workers=null)
    {
        var a=Iterate(w,Copy(t),q,years,true,workers);var b=Iterate(w,Copy(t),q,years,false,workers);
        double er=Rel(a.AnnualFood,b.AnnualFood);MaxStartDifference=Math.Max(MaxStartDifference,er);
        MaxTailRange=Math.Max(MaxTailRange,Math.Max(a.TailRange,b.TailRange));
        Check(er<1e-6&&a.TailRange<1e-6&&b.TailRange<1e-6,"Steady yield not converged");return a;
    }
    private static void Sweep(WorldState w,Fixture f)
    {
        var cache=new Dictionary<string,Cell>();
        Cell Get(int k,double q,double years,double sigma,double target,double? cap=null)
        {
            string key=k+":"+q+":"+years+":"+sigma+":"+target+":"+(cap?.ToString("R")??"all");
            if(!cache.TryGetValue(key,out var value)){value=Steady(w,WetOverlay(f.Fields[k],sigma,target),q,years,cap);cache.Add(key,value);}return value;
        }
        void Evaluate(double q,double years,double sigma,int count,double target,bool fixedStaff)
        {
            var baseCells=Enumerable.Range(0,7).Select(k=>Get(k,q,years,0,.5)).ToArray();
            var wetIds=new HashSet<int>(f.Dry.Take(count));
            var cells=Enumerable.Range(0,7).Select(k=>Get(k,q,years,wetIds.Contains(f.Fields[k].Id)?sigma:0,target,
                fixedStaff?(double?)Math.Max(baseCells[k].HarvestWorkers,baseCells[k].MaintenanceWorkers):null)).ToArray();
            double food=cells.Sum(x=>x.AnnualFood),baseFood=baseCells.Sum(x=>x.AnnualFood),c=w.Config.Consumption*w.Config.DaysPerYear/w.Config.BaseDays;
            double farm=Math.Max(cells.Sum(x=>x.HarvestWorkers),cells.Sum(x=>x.MaintenanceWorkers));
            double baseFarm=Math.Max(baseCells.Sum(x=>x.HarvestWorkers),baseCells.Sum(x=>x.MaintenanceWorkers));
            double extra=(food-baseFood)/c-(farm-baseFarm);
            Rows.Add(new Row{Seed=w.Seed,Center=f.Center,Fixture=f.Name,Kind=f.Kind,Staffing=fixedStaff?"baseline_per_field_workers":"harvest_ceiling",
                Q=q,Years=years,Coverage=sigma,TargetMoisture=target,IrrigatedFields=count,DryMoisture=f.DryMoisture,DryBiomassRatio=f.DryBiomassRatio,
                BaselineFoodYear=baseFood,FoodYear=food,AddedFoodYear=food-baseFood,RelativeGain=baseFood>0?food/baseFood-1:0,
                NeighborFoodYear=cells.Skip(1).Sum(x=>x.AnnualFood),CenterFoodYear=cells[0].AnnualFood,FoodPopulation=food/c,AddedFoodPopulation=(food-baseFood)/c,
                CropAttributedFoodYear=cells.Sum(x=>x.CropAttributedAnnualFood),HarvestWorkers=cells.Sum(x=>x.HarvestWorkers),MaintenanceWorkers=cells.Sum(x=>x.MaintenanceWorkers),
                FarmWorkers=farm,BaselineFarmWorkers=baseFarm,AddedFarmWorkers=farm-baseFarm,ExtraNonfarmHeadcountBeforeOtherNeeds=extra,After15CanalWorkers=extra-15,After30CanalWorkers=extra-30,
                EndingBiomass=cells.Sum(x=>x.End.PotentialFood),EndingFood=cells.Sum(x=>x.End.Food)});
            Check(Rel(cells[0].AnnualFood,baseCells[0].AnnualFood)<1e-8||fixedStaff,"Irrigation changed center");
        }
        foreach(double q in new[]{.4,.5})foreach(double years in new[]{20.0,15.0,10.0})
        {
            foreach(double sigma in new[]{0.0,.25,.5,.75,1.0})Evaluate(q,years,sigma,3,.5,false);
            Evaluate(q,years,1,1,.5,false);Evaluate(q,years,1,2,.5,false);
            Evaluate(q,years,1,3,.5,true);
        }
        if(f.Kind=="controlled")
        {
            Evaluate(.4,10,1,3,.35,false);Evaluate(.4,10,1,3,.4,false);Evaluate(.4,10,1,3,.6,false);
            var a=Enumerable.Range(0,7).Select(k=>Get(k,0,10,0,.5).AnnualFood).Sum();
            var b=Enumerable.Range(0,7).Select(k=>Get(k,0,10,f.Dry.Contains(f.Fields[k].Id)?1:0,.5).AnnualFood).Sum();
            Check(Rel(a,b)<1e-7,"q=0 irrigation should not boost wild ecology in crop-only model");
        }
    }
    private static Tile SnapshotProfile(Tile original,Tile profile,bool dry,double moisture,double bRatio)
    {
        var t=Copy(original);t.Environment=profile.Environment;t.CurrentVegetation=profile.CurrentVegetation;t.NaturalVegetationPotential=profile.NaturalVegetationPotential;
        t.Seasons=(SeasonData[])profile.Seasons.Clone();
        if(dry)for(int s=0;s<4;s++) {t.Seasons[s].Moisture=(float)moisture;t.Seasons[s].MaxFood*=(float)bRatio;t.Seasons[s].MaxPotentialFood*=(float)bRatio;}
        return t;
    }
    private static void SaveProfile(Fixture f)
    {
        Profiles.Add(new{f.Name,f.Kind,f.Center,f.Source,f.Dry,f.Wet,f.DryMoisture,f.DryBiomassRatio,
            fields=f.Fields.Select(t=>new{t.Id,t.Environment,t.Relief,t.River,t.Elevation,t.CurrentVegetation,t.NaturalVegetationPotential,
                profiles=t.Seasons.Select(s=>new{s.Moisture,s.Temperature,s.MaxFood,s.MaxPotentialFood,s.FoodRecoveryPerDay,s.BiomassRecoveryPerDay,s.MaxFreshwater,s.WaterRecoveryPerDay})})});
    }
    private static void DynamicWater(WorldState w,Fixture f,double ratio,double transmission,double intakeCap,bool shutdown)
    {
        const double q=.4;var cfg=w.Config;double e=Math.Pow(q/cfg.ReferenceIntensity,2);
        var states=f.Fields.Select(t=>Steady(w,Copy(t),q,20).End).ToArray();for(int k=0;k<7;k++)states[k].Days=0;
        var source=w.Tiles[f.Source];var water=new EcologyView(source){Days=0};
        double sourceRate=source.Seasons.Average(s=>.8*(double)s.MaxFreshwater*s.WaterRecoveryPerDay);
        Check(sourceRate>0,"No source water");water.Freshwater=source.Seasons.Average(s=>(double)s.MaxFreshwater)*.5;
        string arm=ratio==0?"no_canal":shutdown?"canal_200_to_400":"canal_from_200";
        double year=0;
        while(year<500-1e-8)
        {
            double years=year<200?20:year<350?15:10,dt=years*cfg.DaysPerYear;
            bool on=ratio>0&&year>=200&&(!shutdown||year<400);
            double fullDelivery=ratio*sourceRate*dt;
            double canalRequest=on?Math.Min(fullDelivery/transmission,intakeCap*sourceRate*dt):0;
            double domestic=.2*sourceRate*dt;
            var claims=new[]{new Demand{Tile=source.Id,Entity=100,Kind="Background water",Freshwater=domestic},new Demand{Tile=source.Id,Entity=101,Kind="Canal intake",Freshwater=canalRequest}};
            var wr=EcologicalPressure.Integrate(w,source.Id,claims,water.Days,dt,averageCultivationCapital:0,endingCultivationCapital:0,operatingFraction:0,projectWood:false,openingEcology:water);
            Balance(wr);double withdrew=wr.Transfers.Where(t=>t.Entity==101).Sum(t=>t.Freshwater),background=wr.Transfers.Where(t=>t.Entity==100).Sum(t=>t.Freshwater);
            double delivered=withdrew*transmission,sigma=fullDelivery>0?Math.Min(1,delivered/fullDelivery):0;
            Check(withdrew<=canalRequest+1e-7&&Rel(delivered+withdrew*(1-transmission),withdrew)<1e-12,"Water copy");
            double food=0,centerFood=0,work=0,crop=0;
            for(int k=0;k<7;k++)
            {
                var t=WetOverlay(f.Fields[k],f.Dry.Contains(f.Fields[k].Id)?sigma:0,.5);var original=w.Tiles[t.Id];w.Tiles[t.Id]=t;
                try
                {
                    t.CultivationCapital=e;t.CultivationOperatingFraction=1;t.CultivationOperator=0;
                    var v=states[k];double share=CropShare(w,t,v);
                    var claim=new Demand{Tile=t.Id,Entity=0,Kind="Cultivation",Food=Math.Max(0,v.Food),ImpactMultiplier=1};
                    var r=EcologicalPressure.Integrate(w,t.Id,new[]{claim},v.Days,dt,averageCultivationCapital:e,endingCultivationCapital:e,operatingFraction:1,projectWood:false,openingEcology:v);
                    Balance(r);double receipt=r.Transfers.Sum(z=>z.Food)/years;food+=receipt;if(k==0)centerFood=receipt;
                    work+=receipt*WorkPerFood(w,share);crop+=receipt*share;states[k]=r.View;
                }
                finally{w.Tiles[t.Id]=original;}
            }
            Steps.Add(new StepRow{Seed=w.Seed,Center=f.Center,Fixture=f.Name,Arm=arm,Q=q,YearStart=year,YearEnd=year+years,DeltaYears=years,WaterRequirementRatio=ratio,
                RequestedCoverage=on?1:0,CanalIntakeCapacityRatio=intakeCap,Transmission=transmission,SuppliedCoverage=sigma,ReferenceSourceWaterYear=sourceRate*cfg.DaysPerYear,
                RequestedDeliveredWater=on?fullDelivery:0,SourceWaterStart=water.Freshwater,SourceWaterEnd=wr.View.Freshwater,WaterRecharge=wr.Balance.WaterRecharge,WaterOverflow=wr.Balance.WaterOverflow,
                CanalWithdrawal=withdrew,BackgroundWithdrawal=background,DeliveredWater=delivered,ConveyanceLoss=withdrew-delivered,FoodYear=food,FoodPopulation=food/(cfg.Consumption*cfg.DaysPerYear/cfg.BaseDays),
                CenterFoodYear=centerFood,NeighborFoodYear=food-centerFood,HarvestWorkers=work,CropAttributedFoodYear=crop});
            water=wr.View;year+=years;
        }
        Check(year==500,"Schedule length");
    }
    private static double Quantile(IEnumerable<double> values,double p)
    {var a=values.OrderBy(x=>x).ToArray();Check(a.Length>0,"Empty quantile");double x=(a.Length-1)*p;int i=(int)x;return a[i]+(a[Math.Min(i+1,a.Length-1)]-a[i])*(x-i);}
    private static object Dist(IEnumerable<double> values)
    {var a=values.ToArray();return new{n=a.Length,min=a.Min(),p25=Quantile(a,.25),median=Quantile(a,.5),p75=Quantile(a,.75),max=a.Max()};}
    private static void Csv<T>(string path,IEnumerable<T> rows)
    {
        var fields=typeof(T).GetFields();using var writer=new StreamWriter(path);writer.WriteLine(string.Join(",",fields.Select(f=>f.Name)));
        foreach(var row in rows)writer.WriteLine(string.Join(",",fields.Select(f=>Convert.ToString(f.GetValue(row),CultureInfo.InvariantCulture))));
    }
    private static string Sha(string p){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
    private static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        int seed=args.Length>0?int.Parse(args[0]):11;string output=args.Length>1?args[1]:"TestResults/IrrigationThreeFields/"+seed;
        Directory.CreateDirectory(output);var timer=Stopwatch.StartNew();
        var cfg=JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"),J);
        var resources=JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"),J);
        cfg.Width=1024;cfg.Height=512;cfg.Players=80;cfg.Shape=WorldShape.Continents;
        Console.WriteLine("GENERATING "+seed);var w=new WorldGenerator().Generate(seed,cfg,resources);
        var rich=w.Tiles.Where(t=>t.River&&CultivationEconomy.Cultivable(t)&&t.Seasons.All(s=>s.Moisture>=.5)&&t.Seasons.Average(s=>s.Temperature)>=10
            &&w.Map.Neighbors(t.Id).Count(id=>CultivationEconomy.Cultivable(w.Tiles[id]))==6&&t.Seasons.Any(s=>s.MaxFreshwater>0))
            .OrderBy(t=>MeanB(t)).ThenBy(t=>t.Id).ToArray();
        Check(rich.Length>0,"No controlled reference");var reference=rich[(int)((rich.Length-1)*.875)];
        var neighbors=w.Map.Neighbors(reference.Id).OrderBy(id=>id).ToArray();
        var fixtures=new List<Fixture>();
        foreach(double moisture in new[]{.2,.3,.4})foreach(double b in new[]{.25,.5,1.0})
        {
            var ids=new[]{reference.Id}.Concat(neighbors).ToArray();var dry=neighbors.Skip(3).ToArray();
            fixtures.Add(new Fixture{Kind="controlled",Name="controlled_m"+moisture+"_b"+b,Center=reference.Id,Source=reference.Id,Dry=dry,Wet=neighbors.Take(3).ToArray(),
                DryMoisture=moisture,DryBiomassRatio=b,Fields=ids.Select(id=>SnapshotProfile(w.Tiles[id],reference,dry.Contains(id),moisture,b)).ToArray()});
        }
        var candidates=new List<(int center,int[] wet,int[] dry,double score)>();
        foreach(var t in w.Tiles.Where(t=>t.River&&CultivationEconomy.Cultivable(t)))
        {
            var ns=w.Map.Neighbors(t.Id).ToArray();if(ns.Length!=6||ns.Any(id=>!CultivationEconomy.Cultivable(w.Tiles[id])))continue;
            var wet=ns.Where(id=>w.Tiles[id].River).ToArray();var dry=ns.Where(id=>!w.Tiles[id].River).ToArray();
            if(wet.Length!=3||dry.Length!=3)continue;
            if(wet.Any(id=>w.Tiles[id].Seasons.Average(s=>EcologyMath.CropSuitability(cfg,s.Moisture))<.8||w.Tiles[id].Seasons.Average(s=>s.Temperature)<8))continue;
            if(dry.Any(id=>w.Tiles[id].Seasons.Average(s=>EcologyMath.CropSuitability(cfg,s.Moisture))>=.8))continue;
            candidates.Add((t.Id,wet.OrderBy(id=>id).ToArray(),dry.OrderBy(id=>id).ToArray(),wet.Average(id=>MeanB(w.Tiles[id]))));
        }
        var fertile=candidates.Count>0?candidates.Where(c=>c.score>=Quantile(candidates.Select(z=>z.score),.5)).OrderBy(c=>c.dry.Average(id=>MeanM(w.Tiles[id]))).ThenBy(c=>c.center).ToArray():candidates.ToArray();
        int sample=Math.Min(8,fertile.Length);
        for(int i=0;i<sample;i++)
        {
            var c=fertile[sample==1?0:(int)Math.Round((fertile.Length-1.0)*i/(sample-1))];
            var ids=new[]{c.center}.Concat(c.wet).Concat(c.dry).ToArray();
            fixtures.Add(new Fixture{Kind="generated",Name="generated_"+c.center,Center=c.center,Source=c.center,Dry=c.dry,Wet=c.wet,Fields=ids.Select(id=>Copy(w.Tiles[id])).ToArray(),
                DryMoisture=c.dry.Average(id=>MeanM(w.Tiles[id])),DryBiomassRatio=c.dry.Average(id=>MeanB(w.Tiles[id]))/c.wet.Average(id=>MeanB(w.Tiles[id]))});
        }
        Console.WriteLine("FIXTURES controlled=9 generated="+sample+" eligible="+candidates.Count);
        foreach(var f in fixtures){SaveProfile(f);Sweep(w,f);}
        foreach(var f in fixtures.Where(f=>f.Name=="controlled_m0.2_b1"||f.Name=="controlled_m0.2_b0.25").Concat(fixtures.Where(f=>f.Kind=="generated").Take(1)))
        {
            DynamicWater(w,f,0,.8,1,false);
            foreach(double ratio in new[]{.25,.5,1.0,2.0,4.0})DynamicWater(w,f,ratio,.8,1,false);
            DynamicWater(w,f,.5,.8,1,true);
            DynamicWater(w,f,1,.8,.5,false);
            DynamicWater(w,f,1,1,1,false);
        }
        Csv(Path.Combine(output,"rows.csv"),Rows);Csv(Path.Combine(output,"water-trajectories.csv"),Steps);
        File.WriteAllText(Path.Combine(output,"profiles.json"),JsonSerializer.Serialize(Profiles,J));
        var summary=new{baseline=Baseline,seed,referenceTile=reference.Id,referenceBiomass=MeanB(reference),eligibleGenerated=candidates.Count,richGenerated=fertile.Length,sampledGenerated=sample,
            checks=new{Integrations,MaxMassError,MaxStartDifference,MaxTailRange,elapsedSeconds=timer.Elapsed.TotalSeconds},
            groups=Rows.GroupBy(r=>new{r.Kind,r.Q,r.Years,r.Coverage,r.IrrigatedFields,r.TargetMoisture,r.Staffing}).Select(g=>new{g.Key,foodPopulation=Dist(g.Select(x=>x.FoodPopulation)),addedPopulation=Dist(g.Select(x=>x.AddedFoodPopulation)),relativeGain=Dist(g.Select(x=>x.RelativeGain)),addedFarmWork=Dist(g.Select(x=>x.AddedFarmWorkers))}),
            scope="Cloud native C# kernel diagnostic, not a full live village. Center plus six fields are mature fixed-q. Exactly three target fields receive crop-only effective moisture; no biomass/rainfall/terrain/stock grant. Controlled cases replicate a generated fertile river profile; dry neighbors have specified moisture and retained biomass-potential ratio, held fixed within each pair. Generated fixtures retain their true profiles; river adjacency is a floodplain proxy, not an alluvial soil class or constructed hydraulic route. Most rows condition on delivered irrigation coverage; water scenarios separately debit one native finite source with background withdrawal and lossy capacity-limited service. Water-to-moisture units are uncalibrated: requirement ratio is an explicit sensitivity relative to 80% of source gross renewal, not evidence that physical irrigation is adequately funded. Water scenarios use horizon-average fulfillment, no soil storage or drought hydrograph. No canal construction or maintenance labor is paid in kernel rows; 15/30-worker deductions are diagnostic opportunity costs, not implemented industry. Existing harvest/maintenance work overlaps; no demographic, clothes, housing, trade, fission or plough effects are claimed.",
            sourceHashes=Directory.GetFiles("Assets/_Anthropocene/Simulation","*.cs").OrderBy(p=>p).ToDictionary(p=>p,p=>Sha(p)),openingConfigHash=Sha("Assets/_Anthropocene/Resources/OpeningConfig.json")};
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summary,J));
        Console.WriteLine("SUMMARY "+JsonSerializer.Serialize(new{seed,referenceTile=reference.Id,eligibleGenerated=candidates.Count,sampledGenerated=sample,rows=Rows.Count,steps=Steps.Count,summary.checks},J));
        Console.WriteLine("CENTRAL "+JsonSerializer.Serialize(Rows.Where(r=>r.Q==.4&&r.Years==10&&r.Staffing=="harvest_ceiling"&&r.IrrigatedFields==3&&r.TargetMoisture==.5&&(r.Coverage==0||r.Coverage==1)),J));
    }
}
