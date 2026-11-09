using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// Experimental harness only. All production Simulation/*.cs remain unchanged.
internal static partial class Program
{
    private sealed class SoilRow
    {
        public int Seed, Center;
        public string Fixture, Kind, Arm, Cadence, Staffing, Integrator;
        public double Q, MatureCapacityGain, TargetMoisture, NominalSupply;
        public double YearStart, YearEnd, DeltaYears, DeliveredFraction, SoilStart, SoilEnd, SoilMean;
        public double FoodYear, FoodPopulation, TargetFoodYear, UnchangedFoodYear, CropAttributedFoodYear;
        public double HarvestWorkers, MaintenanceWorkers, FarmWorkers, AllocatedFixedWorkers;
        public double TargetCapacityMean, TargetBiomassEnd, TargetFoodEnd, CumulativeFood;
        public double WaterRequested, WaterWithdrawal, WaterDelivered, WaterLoss;
        public double SourceWaterStart, SourceWaterEnd, BackgroundWater, WaterRecharge, WaterOverflow;
    }
    private sealed class SoilCell
    {
        public EcologyView State;
        public double Food, HarvestWork, CropFood, Maintenance, Capacity;
    }
    private static readonly List<SoilRow> SoilRows = new List<SoilRow>();
    private static readonly Dictionary<string,Cell[]> SoilInitial = new Dictionary<string,Cell[]>();
    private static long SoilCases;
    private static double MaxWaterResidual, MaxSoilPartitionError, MaxReferenceFoodError;
    private static double MaxQuarterReferenceFoodError;

    private static double SoilNext(double a, double rate, double years) => Math.Min(1, a + Math.Max(0,rate) * years);
    private static double SoilAverage(double a, double rate, double years)
    {
        if (rate <= 0 || years <= 0) return a;
        double ramp = Math.Min(years, Math.Max(0, (1-a)/rate));
        return ((a + SoilNext(a,rate,ramp))*.5*ramp + (years-ramp))/years;
    }
    private static Tile SoilOverlay(WorldState w, Tile original, double sigma, double target, double a, double gain)
    {
        var t=Copy(original); var cfg=w.Config;
        for(int s=0;s<4;s++)
        {
            var b=original.Seasons[s];
            double m=b.Moisture + sigma*Math.Max(0,target-b.Moisture);
            t.Seasons[s].Moisture=(float)m;
            double waterOnly = Math.Max(b.MaxPotentialFood, FoodEcology.LandBiomassBase*cfg.LandBiomassMultiplier*FoodEcology.LandProductivity(t,t.Seasons[s],cfg));
            // Explicit uncalibrated sensitivity: mature capacity is 1+gain times
            // water-only capacity. a is persistent; CURRENT WATER still sets the base.
            // No Food/PotentialFood inventory, natural MaxFood or rainfall is gifted.
            t.Seasons[s].MaxPotentialFood=(float)(waterOnly*(1+gain*a));
            if(sigma>0 && m>b.Moisture)
            {
                double warmth=Numbers.Clamp((b.Temperature+8)/30.0,.015,1);
                double fraction=cfg.LandFoodRegenerationInitialized?cfg.LandFoodRegeneration:cfg.FoodRegeneration;
                double rate=EcologyMath.DailyRate(fraction*(.2+m)*warmth,cfg.BaseDays);
                t.Seasons[s].FoodRecoveryPerDay=(float)Math.Max(b.FoodRecoveryPerDay,rate);
                t.Seasons[s].BiomassRecoveryPerDay=(float)Math.Max(b.BiomassRecoveryPerDay,rate);
            }
        }
        return t;
    }
    private static SoilCell SoilAdvance(WorldState w, Tile original, EcologyView opening,
        double q,double years,double sigma,double target,double a0,double aRate,double gain,
        double? fixedWorkers,double referenceStepYears)
    {
        var cfg=w.Config;double e=Math.Pow(q/cfg.ReferenceIntensity,2);
        var saved=w.Tiles[original.Id];
        try
        {
            var op=SoilOverlay(w,original,sigma,target,a0,gain);
            op.CultivationCapital=e;op.CultivationOperator=0;op.CultivationOperatingFraction=1;
            w.Tiles[op.Id]=op;
            double share=CropShare(w,op,opening),cost=WorkPerFood(w,share);
            double requested=Math.Max(0,opening.Food);
            if(fixedWorkers.HasValue)requested=Math.Min(requested,fixedWorkers.Value*years/cost);
            var v=opening;double harvest=0,elapsed=0,capacity=0;
            // Reference-only subdivisions: harvest ONCE at the player boundary.
            // No repeated intra-turn claims, policy changes, or extra workers.
            while(elapsed<years-1e-10)
            {
                double segment=referenceStepYears>0?Math.Min(referenceStepYears,years-elapsed):years;
                double at=SoilNext(a0,aRate,elapsed),mean=SoilAverage(at,aRate,segment);
                var t=SoilOverlay(w,original,sigma,target,mean,gain);
                t.CultivationCapital=e;t.CultivationOperator=0;t.CultivationOperatingFraction=1;
                w.Tiles[t.Id]=t;
                var claim=new Demand{Tile=t.Id,Entity=0,Kind="Cultivation",Food=elapsed==0?requested:0,ImpactMultiplier=1};
                var r=EcologicalPressure.Integrate(w,t.Id,new[]{claim},v.Days,segment*cfg.DaysPerYear,
                    averageCultivationCapital:e,endingCultivationCapital:e,operatingFraction:1,projectWood:false,openingEcology:v);
                Balance(r);Check(Rel(r.View.CultivationCapital,e)<1e-12,"Soil run changed q");
                harvest+=r.Transfers.Sum(x=>x.Food);capacity+=MeanB(t)*segment/years;
                v=r.View;elapsed+=segment;
            }
            Check(harvest<=requested+1e-7,"Subdivided reference created harvest");
            var end=SoilOverlay(w,original,sigma,target,SoilNext(a0,aRate,years),gain);
            end.CurrentVegetation=v.CurrentVegetation;
            CultivationEconomy.Factors(cfg,end,out _,out double factor);
            double maintenance=cfg.Neolithic.MaintenanceWorkersPerTileE(cfg.MaintenanceCoefficient)*factor*CultivationEconomy.EffortForCapital(cfg,e);
            return new SoilCell{State=v,Food=harvest,HarvestWork=harvest/years*cost,CropFood=harvest*share,Maintenance=maintenance,Capacity=capacity};
        }
        finally{w.Tiles[original.Id]=saved;}
    }
    private static Cell[] InitialSoilCells(WorldState w,Fixture f,double q,string cadence)
    {
        string key=f.Name+":"+q+":"+cadence;
        if(!SoilInitial.TryGetValue(key,out var cells))
        {
            double years=cadence=="fixed10"?10:20;
            cells=f.Fields.Select(t=>Steady(w,Copy(t),q,years)).ToArray();
            SoilInitial.Add(key,cells);
        }
        return cells;
    }
    private static void SoilTrajectory(WorldState w,Fixture f,double q,double gain,string cadence,
        string arm="continuous",double nominalSupply=1,double target=.5,bool fixedStaff=false,double referenceStep=0)
    {
        var cfg=w.Config;var initial=InitialSoilCells(w,f,q,cadence);
        var states=initial.Select(x=>x.End).ToArray();for(int k=0;k<states.Length;k++)states[k].Days=0;
        var fixedBudgets=initial.Select(x=>Math.Max(x.HarvestWorkers,x.MaintenanceWorkers)).ToArray();
        var source=w.Tiles[f.Source];var water=new EcologyView(source){Days=0};
        double sourceRate=source.Seasons.Average(s=>.8*(double)s.MaxFreshwater*s.WaterRecoveryPerDay);
        Check(sourceRate>0,"No finite source");water.Freshwater=source.Seasons.Average(s=>(double)s.MaxFreshwater)*.5;
        var targetFields=f.Fields.Where(t=>f.Dry.Contains(t.Id)).ToArray();
        double maxDeficit=targetFields.Sum(t=>t.Seasons.Sum(s=>Math.Max(0,1-s.Moisture)));
        double deficit=targetFields.Sum(t=>t.Seasons.Sum(s=>Math.Max(0,target-s.Moisture)));
        double demandScale=maxDeficit>1e-12?deficit/maxDeficit:0;
        double year=0,a=0,cumulative=0;
        SoilCases++;
        while(year<600-1e-9)
        {
            double years=cadence=="fixed10"?10:year<200?20:year<350?15:10;
            years=Math.Min(years,600-year);double dt=years*cfg.DaysPerYear;
            bool active=arm!="no_irrigation" && !(arm=="shutdown350_450"&&year>=350&&year<450);
            // Same explicitly assumed source-relative demand law for every arm.
            // Higher target moisture costs proportionally more delivered water.
            double fullWater=.4*sourceRate*dt*demandScale;
            const double transmission=.8;
            double canalRequest=active?nominalSupply*fullWater/transmission:0;
            double backgroundRequest=.2*sourceRate*dt;
            var claims=new[]{new Demand{Tile=source.Id,Entity=100,Kind="Background water",Freshwater=backgroundRequest},new Demand{Tile=source.Id,Entity=101,Kind="Irrigation intake",Freshwater=canalRequest}};
            var wr=EcologicalPressure.Integrate(w,source.Id,claims,water.Days,dt,
                averageCultivationCapital:0,endingCultivationCapital:0,operatingFraction:0,projectWood:false,openingEcology:water);
            Balance(wr);
            double withdrew=wr.Transfers.Where(x=>x.Entity==101).Sum(x=>x.Freshwater);
            double delivered=withdrew*transmission,loss=withdrew-delivered;
            double sigma=fullWater>1e-12?Math.Min(1,delivered/fullWater):0;
            double waterError=Rel(withdrew,delivered+loss);MaxWaterResidual=Math.Max(MaxWaterResidual,waterError);
            Check(waterError<1e-12&&withdrew<=canalRequest+1e-8,"Water duplication");
            // Maintained cultivation O=1 in this fixed-field diagnostic.
            // A sediment-poor control keeps water but supplies no qualifying soil service.
            double aRate=arm=="sediment_poor"?0:sigma*Math.Min(1,q/.4)/500;
            if(gain==0)aRate=0;
            double next=SoilNext(a,aRate,years),mean=SoilAverage(a,aRate,years);
            double partition=SoilNext(SoilNext(a,aRate,years*.37),aRate,years*.63);
            MaxSoilPartitionError=Math.Max(MaxSoilPartitionError,Math.Abs(next-partition));
            Check(Math.Abs(next-partition)<1e-12,"Soil clock depends on partition");
            double food=0,targetFood=0,work=0,maint=0,crop=0,cap=0,biomass=0,stock=0;
            for(int k=0;k<7;k++)
            {
                bool irrigated=f.Dry.Contains(f.Fields[k].Id);
                var result=SoilAdvance(w,f.Fields[k],states[k],q,years,irrigated?sigma:0,target,
                    irrigated?a:0,irrigated?aRate:0,irrigated?gain:0,
                    fixedStaff?(double?)fixedBudgets[k]:null,referenceStep);
                states[k]=result.State;food+=result.Food;work+=result.HarvestWork;maint+=result.Maintenance;crop+=result.CropFood;
                if(irrigated){targetFood+=result.Food;cap+=result.Capacity;biomass+=result.State.PotentialFood;stock+=result.State.Food;}
            }
            cumulative+=food;
            SoilRows.Add(new SoilRow{Seed=w.Seed,Center=f.Center,Fixture=f.Name,Kind=f.Kind,Arm=arm,Cadence=cadence,
                Staffing=fixedStaff?"fixed_opening_workers":"harvest_ceiling",Integrator=referenceStep==0?"mean_soil_per_turn":referenceStep==1?"annual_reference":"quarter_reference",
                Q=q,MatureCapacityGain=gain,TargetMoisture=target,NominalSupply=nominalSupply,
                YearStart=year,YearEnd=year+years,DeltaYears=years,DeliveredFraction=sigma,SoilStart=a,SoilEnd=next,SoilMean=mean,
                FoodYear=food/years,FoodPopulation=food/years/PhysicalDemography.FoodPerPersonYear(cfg),TargetFoodYear=targetFood/years,UnchangedFoodYear=(food-targetFood)/years,
                CropAttributedFoodYear=crop/years,HarvestWorkers=work,MaintenanceWorkers=maint,FarmWorkers=Math.Max(work,maint),
                AllocatedFixedWorkers=fixedStaff?fixedBudgets.Sum():0,TargetCapacityMean=cap,TargetBiomassEnd=biomass,TargetFoodEnd=stock,CumulativeFood=cumulative,
                WaterRequested=canalRequest,WaterWithdrawal=withdrew,WaterDelivered=delivered,WaterLoss=loss,
                SourceWaterStart=water.Freshwater,SourceWaterEnd=wr.View.Freshwater,
                BackgroundWater=wr.Transfers.Where(x=>x.Entity==100).Sum(x=>x.Freshwater),WaterRecharge=wr.Balance.WaterRecharge,WaterOverflow=wr.Balance.WaterOverflow});
            a=next;water=wr.View;year+=years;
        }
        Check(year==600,"Wrong campaign duration");
        if(arm=="continuous"&&gain>0&&nominalSupply==1&&q>=.4)Check(Math.Abs(a-1)<1e-10,"Full-service field did not mature");
        if(arm=="no_irrigation"||arm=="sediment_poor"||q==0)Check(a==0,"Unfunded soil progress");
    }
    private static Fixture SoilControlled(WorldState w,Tile reference)
    {
        var ids=new[]{reference.Id}.Concat(w.Map.Neighbors(reference.Id).OrderBy(id=>id)).ToArray();
        var fields=ids.Select(id=>SnapshotProfile(w.Tiles[id],reference,false,1,1)).ToArray();
        var dry=ids.Skip(4).ToArray();
        for(int k=0;k<7;k++)
        {
            fields[k].Relief=reference.Relief;
            if(!dry.Contains(fields[k].Id))continue;
            var t=fields[k];t.Environment=EnvironmentKind.Desert;t.RiverEdges=null;
            t.NaturalVegetationPotential=t.CurrentVegetation=VegetationTier.Low;
            t.FreshwaterSource=FreshwaterSource.None;t.Freshwater=0;
            for(int s=0;s<4;s++)
            {
                var p=t.Seasons[s];p.Moisture=.1f;p.RegionalMoisture=.1f;p.MaxFreshwater=0;
                p.MaxPotentialFood=(float)(FoodEcology.LandBiomassBase*w.Config.LandBiomassMultiplier*FoodEcology.LandProductivity(t,p,w.Config));
                p.MaxFood=p.MaxPotentialFood*(float)FoodEcology.GatherableFraction(t,w.Config);
                double warmth=Numbers.Clamp((p.Temperature+8)/30.0,.015,1);
                double fraction=w.Config.LandFoodRegenerationInitialized?w.Config.LandFoodRegeneration:w.Config.FoodRegeneration;
                p.FoodRecoveryPerDay=p.BiomassRecoveryPerDay=(float)EcologyMath.DailyRate(fraction*(.2+p.Moisture)*warmth,w.Config.BaseDays);
                t.Seasons[s]=p;
            }
        }
        return new Fixture{Name="controlled_dry_m0.10",Kind="controlled_dry",Center=reference.Id,Source=reference.Id,
            Fields=fields,Wet=ids.Skip(1).Take(3).ToArray(),Dry=dry,DryMoisture=.1,DryBiomassRatio=fields.Skip(4).Average(MeanB)/fields.Skip(1).Take(3).Average(MeanB)};
    }
    private static void SoilRun(WorldState w,List<Fixture> original,Tile reference,string output,int seed,int eligible)
    {
        var fixtures=original.Where(f=>f.Kind=="generated").ToList();
        fixtures.Insert(0,SoilControlled(w,reference));
        foreach(var f in fixtures)
        {
            SaveProfile(f);
            foreach(string cadence in new[]{"fixed10","dynamic20_15_10"})
            {
                foreach(double q in new[]{.2,.4,.5})
                {
                    SoilTrajectory(w,f,q,0,cadence,"no_irrigation");
                    foreach(double g in new[]{0.0,.25,.5,1.0})SoilTrajectory(w,f,q,g,cadence);
                }
                foreach(double g in new[]{0.0,.5})
                {
                    SoilTrajectory(w,f,.4,g,cadence,"continuous",.5);
                    SoilTrajectory(w,f,.4,g,cadence,"shutdown350_450");
                    SoilTrajectory(w,f,.4,g,cadence,"continuous",1,.5,true);
                    SoilTrajectory(w,f,.4,g,cadence,"continuous",1,1);
                }
                SoilTrajectory(w,f,.4,.5,cadence,"sediment_poor");
                SoilTrajectory(w,f,0,0,cadence);SoilTrajectory(w,f,0,.5,cadence);
            }
            Console.WriteLine("SOIL_FIXTURE "+seed+" "+f.Name+" cases="+SoilCases);
        }
        // Slow numerical reference only for seed-11 controlled and median sampled site.
        // It holds the same player harvest boundaries while refining capacity forcing.
        if(seed==11)
        foreach(var f in new[]{fixtures[0],fixtures[fixtures.Count/2]})
        foreach(string cadence in new[]{"fixed10","dynamic20_15_10"})
        foreach(double g in new[]{.5,1.0})
        {
            SoilTrajectory(w,f,.4,g,cadence,referenceStep:1);
            SoilTrajectory(w,f,.4,g,cadence,referenceStep:.25);
            var c=SoilRows.Where(r=>r.Fixture==f.Name&&r.Cadence==cadence&&r.Q==.4&&r.MatureCapacityGain==g&&r.TargetMoisture==.5&&r.Arm=="continuous"&&r.NominalSupply==1&&r.Staffing=="harvest_ceiling").ToArray();
            foreach(var group in c.GroupBy(r=>r.YearEnd))
            {
                var fast=group.Single(r=>r.Integrator=="mean_soil_per_turn");
                var annual=group.Single(r=>r.Integrator=="annual_reference");
                var quarter=group.Single(r=>r.Integrator=="quarter_reference");
                MaxReferenceFoodError=Math.Max(MaxReferenceFoodError,Rel(fast.FoodYear,quarter.FoodYear));
                MaxQuarterReferenceFoodError=Math.Max(MaxQuarterReferenceFoodError,Rel(annual.FoodYear,quarter.FoodYear));
            }
        }
        // q=0 and sediment-poor controls cannot receive a soil gain.
        foreach(var f in fixtures)foreach(string c in new[]{"fixed10","dynamic20_15_10"})
        {
            var control=SoilRows.Where(r=>r.Fixture==f.Name&&r.Cadence==c&&r.Q==.4&&r.MatureCapacityGain==0&&r.TargetMoisture==.5&&r.Arm=="continuous"&&r.NominalSupply==1&&r.Staffing=="harvest_ceiling"&&r.Integrator=="mean_soil_per_turn").ToDictionary(r=>r.YearEnd);
            foreach(var r in SoilRows.Where(r=>r.Fixture==f.Name&&r.Cadence==c&&r.Arm=="sediment_poor"))Check(Rel(r.FoodYear,control[r.YearEnd].FoodYear)<1e-12,"Sediment-poor water acquired fertility");
        }
        Csv(Path.Combine(output,"trajectories.csv"),SoilRows);
        File.WriteAllText(Path.Combine(output,"profiles.json"),JsonSerializer.Serialize(Profiles,J));
        var ends=SoilRows.Where(r=>r.YearEnd==600&&r.Integrator=="mean_soil_per_turn").ToArray();
        var summary=new{baseline=Baseline,seed,eligible,sites=fixtures.Count,rows=SoilRows.Count,
            checks=new{SoilCases,Integrations,MaxMassError,MaxStartDifference,MaxTailRange,MaxWaterResidual,MaxSoilPartitionError,MaxReferenceFoodError,MaxQuarterReferenceFoodError},
            model="a'=min(1,a+dt_years*I*O*min(1,q/.4)/500), O=1. Soil matures only on three target fields. B=B_water*(1+gain*a), gains .25/.5/1 are uncalibrated mature-capacity SENSITIVITIES, not selected coefficients. B_water and r use native moisture-responsive potential/renewal; wild MaxFood profiles and q remain fixed. Current water remains necessary. No existing biomass or food inventory is injected. The main transition uses the exact mean of the capped-linear a over each locked horizon, not its endpoint. A quarter-year recovery-only reference checks this approximation without adding harvest events; these slow substeps are not a proposed production architecture.",
            scope="600-year fixed-field food-capacity trajectories; NOT live population, canal economics, explicit sediment mass transport, terrain routing or calibrated hydrology. Three wet neighbors plus unchanged center, three dry targets. Native pulse harvest remains. Fixed10 isolates soil time; dynamic uses 20 years to 200,15 to350,10 to600. Irrigation starts at0; shutdown350_450 pauses soil progress and removes water benefit. Full-water demand=.4 of source-rate reference times normalized deficit relative target1; transmission .8, background=.2. Finite source is actually debited, but that demand calibration is an assumption. Fixed-workforce lanes hold opening per-field max(harvest,maintenance) workers. No Plough, q_k, mortality or civic modifications. Initial fields are mature fixed-q but unirrigated stocks are converged before t=0. The same endpoint snapshots and totals are not observations of a settled town.",
            sourceHashes=Directory.GetFiles("Assets/_Anthropocene/Simulation","*.cs").OrderBy(p=>p).ToDictionary(p=>p,p=>Sha(p)),openingConfigHash=Sha("Assets/_Anthropocene/Resources/OpeningConfig.json"),
            endpointGroups=ends.GroupBy(r=>new{r.Kind,r.Arm,r.Q,r.MatureCapacityGain,r.Cadence,r.TargetMoisture,r.NominalSupply,r.Staffing}).Select(g=>new{g.Key,foodPopulation=Dist(g.Select(r=>r.FoodPopulation)),targetFood=Dist(g.Select(r=>r.TargetFoodYear)),soil=Dist(g.Select(r=>r.SoilEnd)),farmWorkers=Dist(g.Select(r=>r.FarmWorkers))})};
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summary,J));
        Console.WriteLine("SOIL_SUMMARY "+JsonSerializer.Serialize(new{seed,sites=fixtures.Count,rows=SoilRows.Count,summary.checks},J));
    }
}
