using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Anthropocene.Simulation;

internal static class SharedAllocationChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Shared allocation: " + message); }
    private static void Near(double a, double b, string message) => Check(Math.Abs(a - b) <= 1e-7 * Math.Max(1, Math.Abs(b)), message + " (" + a + " / " + b + ")");
    private static WorldState Fixture(int people = 100, int animals = 20, double q = .5)
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; c.Population = people; c.PlantFood = 0;
        c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true;
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        w.Config.SharedCropHerdAllocationEnabled = true; w.Config.Neolithic.PlannerFulfilment = "Value";
        c.Inventory.HandTools = 100; c.Inventory.HerdingTools = 0; c.Inventory.FarmTools = 0;
        foreach (var t in w.Tiles)
        {
            t.PotentialFood = 90000; t.Food = 9000; t.Freshwater = 100000;
            for (int s = 0; s < 4; s++) { t.Seasons[s].MaxPotentialFood = 100000; t.Seasons[s].MaxFood = 10000; t.Seasons[s].MaxFreshwater = 100000; }
        }
        foreach (int id in CultivationEconomy.Footprint(w, c))
        {
            var t = w.Tiles[id]; t.CultivationCapital = Math.Pow(q / .4, 2); t.CultivationOperator = c.Id; t.CultivationOperatingFraction = 1;
        }
        if (animals > 0) w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, Animals = animals, Species = "goat", DomesticCamp = c.Id, OwnerSociety = c.SocietyId, Capacity = 1 });
        return w;
    }
    private static string Signature(EconomicIntent e) => e.Stamp;
    public static void Run()
    {
        checks = 0;
        // Controlled rate extremes test the ranking, not a proposed calibration.
        SharedAllocation.Choice AtYield(double yield)
        {
            var w = Fixture(100, 40, 0); w.Config.AnimalFoodPerManagedAnimal = yield;
            foreach (var t in w.Tiles) { t.Food = 100; for (int i = 0; i < 4; i++) t.Seasons[i].MaxFood = 100; }
            var c = w.Camps[0]; return new SharedAllocation(w, c, CampProductionDemand.Build(w, c, EconomicIntent.From(c)), .55).Select(0, .1, 100);
        }
        var pastoral = AtYield(10); var crops = AtYield(0);
        Check(pastoral.Target > 40 && pastoral.ExpansionYears == 0, "spare work buys feasible breeding when it beats crops");
        Check(crops.Target < 40 && crops.ExpansionYears > 0, "crops beat herding when animal receipts have no return");
        var committed = Fixture(100, 0, 0);
        foreach (var t in committed.Tiles) { t.Food = 100; for (int i = 0; i < 4; i++) t.Seasons[i].MaxFood = 100; }
        var cc = committed.Camps[0]; var cd = CampProductionDemand.Build(committed, cc, EconomicIntent.From(cc));
        var cp = new SharedAllocation(committed, cc, cd, .55);
        Check(cp.Select(0, .1, 100).ExpansionYears > 0 && cp.Select(0, 100000, 100).ExpansionYears == 0,
            "positive future work can reject expansion despite free current work");
        var satisfied = Fixture(100, 0, 0); var sc = satisfied.Camps[0]; sc.PlantFood = 100000;
        sc.Inventory.Cloth = sc.Inventory.AnimalFabric = sc.Inventory.LightHousingUnits = 10000;
        var sat = new SharedAllocation(satisfied, sc, CampProductionDemand.Build(satisfied, sc, EconomicIntent.From(sc)), .55).Select(0, .1, 100);
        Near(sat.ExpansionYears, 0, "no useful return means no spare field build");
        var reduce = Fixture(100, 0, .6); var rc = reduce.Camps[0];
        rc.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 20 });
        rc.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 10 };
        double required = CultivationEconomy.MaintenanceWorkerRequirement(reduce, rc);
        var labor = CultivationEconomy.PrepareHorizon(reduce, rc, reduce.Days, 20);
        Near(labor.EffectiveMaintenanceFarmers, 10, "explicit build work is not spent on existing upkeep");
        Near(labor.ExpansionFarmers, 10, "reduced operation and expansion coexist in the resolver");
        foreach (int id in labor.Footprint)
            Near(reduce.Tiles[id].CultivationOperatingFraction, 10 / required, "partial staffing commits partial operation");
        var decay = CultivationEconomy.PlanHorizonExpansion(reduce, rc, 20, 0, 0, null);
        for (int i = 0; i < decay.Tiles.Length; i++)
            Near(decay.EndCapital[i], CultivationEconomy.Decay(reduce.Config, decay.StartCapital[i], 10 / required, 20), "reduced upkeep reaches analytical endpoint capital");
        var lowWater = Fixture(100, 40, 0);
        foreach (var t in lowWater.Tiles) { t.Freshwater = 0; for (int i = 0; i < 4; i++) { t.Seasons[i].MaxFreshwater = 0; t.Seasons[i].WaterRecoveryPerDay = 0; } }
        var wc = lowWater.Camps[0]; var wd = CampProductionDemand.Build(lowWater, wc, EconomicIntent.From(wc));
        var waterChoice = new SharedAllocation(lowWater, wc, wd, .55).Select(0, .1, 100);
        Check(waterChoice.Target <= 40, "unavailable water cannot finance future breeding");
        foreach (double inventory in new[] { 0.0, 4000, 16000, 100000 })
        {
            var w = Fixture(100, 0, 0); var c = w.Camps[0];
            c.Inventory.Cloth = c.Inventory.AnimalFabric = c.Inventory.LightHousingUnits = 10000;
            c.PlantFood = inventory;
            var d = CampProductionDemand.Build(w, c, EconomicIntent.From(c)); var ledger = new FoodIncomeLedger(w, c, d);
            var plant = ledger.Receive(2000, 0, 0); var animal = ledger.Receive(0, 0, 2000); var aquatic = ledger.Receive(0, 2000, 0);
            Near(plant.Value, animal.Value, "equivalent generic food uses identical inventory credits");
            Near(plant.Value, aquatic.Value, "aquatic income uses the same ledger");
            Near(plant.Consumption + plant.Reserve + plant.Growth, animal.Consumption + animal.Reserve + animal.Growth, "food buckets agree across sources");
            Check(plant.Consumption + plant.Reserve + plant.Growth <= 2000 + 1e-7, "one receipt is never spent twice");
            Check(plant.Consumption + plant.Reserve + plant.Growth + plant.IncomingPlantInput + plant.IncomingAnimalInput <= 2000 + 1e-7,
                "opening feedstock does not create incoming food or gathering work");
            TurnResolver.FoodBucketTargets(w, c, w.Config.GenerationIntervals, 20, d.Meals, c.Food, out double consumption, out _, out double reserve, out _);
            Near(plant.Consumption, Math.Min(2000, consumption), "protected stock is credited by the authoritative helper only");
            Near(plant.Reserve, Math.Min(Math.Max(0, 2000 - consumption), reserve), "reserve refill follows meals");
        }
        var typed = Fixture(100, 0, 0); var tc = typed.Camps[0]; tc.PlantFood = 0;
        var td = CampProductionDemand.Build(typed, tc, EconomicIntent.From(tc)); var tl = new FoodIncomeLedger(typed, tc, td);
        var pa = tl.Receive(0, 0, 40000); var pp = tl.Receive(40000, 0, 0);
        Check(pa.PlantInput == 0 && pp.AnimalInput == 0, "food cannot change its recipe provenance");
        Check(pa.Consumption + pa.Reserve + pa.Growth + pa.AnimalInput <= 40000 + 1e-7, "typed input and meals share one incoming ledger");

        foreach (double q in new[] { .4, .45, .5, .55, .6 })
        {
            var w = Fixture(10000, 0, q);
            Near(CultivationEconomy.MaintenanceWorkerRequirement(w, w.Camps[0]),
                CultivationEconomy.Footprint(w, w.Camps[0]).Length * 250.0 * .4 / 7 * CultivationEconomy.EffortForCapital(w.Config, Math.Pow(q / .4, 2)), "q-dependent full upkeep remains uncapped");
        }
        var pure = Fixture(); var pc = pure.Camps[0]; string before = SaveCodec.Fingerprint(pure);
        var demand = CampProductionDemand.Build(pure, pc, EconomicIntent.From(pc));
        var portfolio = new SharedAllocation(pure, pc, demand, .55);
        var priced = portfolio.Select(.1, .1, pc.Population); var spare = portfolio.Select(0, .1, pc.Population);
        Check(portfolio.OperationChoices > 1, "maintain and reduce are available in one portfolio");
        Check(spare.Workers <= pc.Population && priced.Workers <= pc.Population, "operation candidates respect the resident budget");
        Check(spare.FutureWork != 0, "free current labour retains future work charges");
        Check(before == SaveCodec.Fingerprint(pure), "all operation and herd forecasts are pure");
        var plan = EconomicPlanner.Plan(pure, pc, EconomicIntent.From(pc));
        Check(plan.Assignments.Sum(a => a.Population) <= pc.Population && plan.Labor.ExpansionFarmers <= plan.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population), "returned workforce and expansion are feasible");
        Check(before == SaveCodec.Fingerprint(pure), "the integrated planner changes no authoritative state");
        pc.Technology = Technology.Cultivation;
        Check(Signature(plan) == Signature(EconomicPlanner.Plan(pure, pc, EconomicIntent.From(pc))), "primary-technology labels do not select a subsistence plan");
        var loaded = SaveCodec.Load(SaveCodec.Save(pure));
        Check(loaded.Config.SharedCropHerdAllocationEnabled && Signature(EconomicPlanner.Plan(loaded, loaded.Camps[0], EconomicIntent.From(loaded.Camps[0]))) == Signature(EconomicPlanner.Plan(pure, pc, EconomicIntent.From(pc))), "enabled save continuation retains the plan");
        using (var input = new MemoryStream(SaveCodec.Save(pure)))
        using (var output = new MemoryStream())
        {
            using (var unzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new BinaryReader(unzip))
            using (var zip = new GZipStream(output, CompressionMode.Compress, true))
            using (var writer = new BinaryWriter(zip))
            {
                writer.Write(reader.ReadString()); int length = reader.ReadInt32();
                using (var xml = XmlDictionaryReader.CreateBinaryReader(reader.ReadBytes(length), XmlDictionaryReaderQuotas.Max))
                using (var metadata = new MemoryStream())
                {
                    var doc = XDocument.Load(xml); doc.Descendants().Where(e => e.Name.LocalName == "SharedCropHerdAllocationEnabled").Remove();
                    using (var binary = XmlDictionaryWriter.CreateBinaryWriter(metadata, null, null, false)) doc.WriteTo(binary);
                    writer.Write((int)metadata.Length); writer.Write(metadata.ToArray());
                }
                unzip.CopyTo(zip);
            }
            Check(!SaveCodec.Load(output.ToArray()).Config.SharedCropHerdAllocationEnabled, "absent legacy field disables the correction");
        }
        foreach (int population in new[] { 100, 1000, 10000 })
        {
            var w = Fixture(population, population / 2); var c = w.Camps[0]; var timer = new Stopwatch();
            var d = CampProductionDemand.Build(w, c, EconomicIntent.From(c)); var p = new SharedAllocation(w, c, d, .55);
            var intent = EconomicPlanner.Plan(w, c, EconomicIntent.From(c)); // warm
            var times = new List<double>(); var allocations = new List<long>();
            for (int repeat = 0; repeat < 5; repeat++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread(); timer.Restart();
                intent = EconomicPlanner.Plan(w, c, EconomicIntent.From(c)); timer.Stop();
                times.Add(timer.Elapsed.TotalMilliseconds); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
            }
            times.Sort(); allocations.Sort();
            Check(p.OperationChoices <= 35 && p.HerdChoices <= p.OperationChoices * 38, "work follows bounded breakpoints at " + population);
            Check(intent.Assignments.Sum(a => a.Population) <= population, "mature staffing stays bounded at " + population);
            Console.WriteLine("SHARED_SCALE residents=" + population + " operations=" + p.OperationChoices + " herdChoices=" + p.HerdChoices + " medianMs=" + times[2].ToString("F2") + " allocatedBytes=" + allocations[2]);
        }
        // One physical prior cohort; liquidation has no future milk or offspring.
        var liquid = Fixture(); liquid.Config.MinimumHerdHerders = 0;
        var ld = CampProductionDemand.Build(liquid, liquid.Camps[0], EconomicIntent.From(liquid.Camps[0]));
        ld.FutureForageAfterHerds = ld.ForageAfterHerds;
        var lh = HerdLabour.Build(liquid, liquid.Camps[0], ld, .55);
        Near(lh.FutureValue[0], 0, "liquidated animals produce no future food"); Near(lh.ResidualValue[0], 0, "liquidated animals have no residual stock");
        Near(lh.FutureAnnualFood[0], 0, "slaughter cannot replace a recurring future food deficit");
        var futureAccounting = Fixture(100, 100, 0);
        futureAccounting.Config.AnnualHumanGrowthRate = .01145;
        futureAccounting.Config.AnimalFoodPerManagedAnimal = 10;
        var fc = futureAccounting.Camps[0];
        var fd = CampProductionDemand.Build(futureAccounting, fc, EconomicIntent.From(fc));
        fd.FutureForageAfterHerds = 2 * fd.Meals; fd.FutureUpkeep = 0;
        var fh = HerdLabour.Build(futureAccounting, fc, fd, .55);
        double r = futureAccounting.Config.Neolithic.DiscountRatePerYear;
        double currentLife = HerdLabour.DiscountedYears(r, 20);
        double futureHarvest = fd.Meals / 20 / (futureAccounting.Config.GatherPerPerson * futureAccounting.Config.DaysPerYear / futureAccounting.Config.BaseDays)
            * HerdLabour.DiscountedYears(r, futureAccounting.Config.Neolithic.PlanningHorizonYears - 20);
        for (int k = 0; k <= fh.Steps; k++)
        {
            double currentSaved = fh.CountAt(k) * currentLife - fh.CurrentWorkerYears[k];
            double savedFuture = (fh.SavedHarvestYears[k] - currentSaved) / Math.Exp(-r * 20);
            Check(savedFuture <= futureHarvest + 1e-7, "future milk cannot release work absent from the human baseline");
        }
        var seed = Fixture(1000, 1000); seed.Config.MinimumHerdHerders = 100;
        var sd = CampProductionDemand.Build(seed, seed.Camps[0], EconomicIntent.From(seed.Camps[0]));
        var sh = HerdLabour.Build(seed, seed.Camps[0], sd, .55);
        Check(sh.Staffed(double.PositiveInfinity) >= 100, "a sparse ladder preserves the seed floor in actual herders");
        var crowded = Fixture(10, 1000); crowded.Config.MinimumHerdHerders = 100;
        var crowdedPlan = EconomicPlanner.Plan(crowded, crowded.Camps[0], EconomicIntent.From(crowded.Camps[0]));
        Check(crowdedPlan.Assignments.Sum(x => x.Population) <= 10, "an infeasible seed floor cannot create residents");
        pc.ManagedBreedingIntensity = 0; pc.HerdTargetSet = true;
        Check(ManagedBreedingPolicy.Recommend(pure, pc, pc.Tile, new PlanningIndex(pure)) > 0, "automatic continuation reconsiders a prior zero breeding cap");
        Replay();
        Console.WriteLine("PASS: " + checks + " shared-allocation assertions");
    }
    private static void Replay()
    {
        var cfg = SimulationConfig.Small(4);
        cfg.SharedCropHerdAllocationEnabled = cfg.HerdLabourEnabled = cfg.QoLMortalityEnabled = cfg.FissionEnabled = true;
        cfg.QoLMortalityElasticity = 1;
        var scalar = new WorldGenerator().Generate(53, cfg); var c = scalar.Camps[0];
        c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true;
        c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic; c.EconomyAutomationTurns = -1;
        scalar.Herds.Add(new Herd { Id = scalar.NextId++, Tile = c.Tile, Species = "goat", Animals = 5, DomesticCamp = c.Id, OwnerSociety = c.SocietyId, Capacity = 1 });
        var parallel = SaveCodec.Load(SaveCodec.Save(scalar)); var workers = new PhaseExecution(4, 3, true, 1);
        int targeted = 0;
        for (int turn = 0; turn < 34; turn++)
        {
            var a = AiPlanning.Decide(scalar); new TurnResolver().Resolve(scalar, a.Orders, a.Traces);
            parallel.Camps.Reverse(); parallel.Herds.Reverse(); parallel.Hunters.Reverse();
            var b = AiPlanning.Decide(parallel, workers); new TurnResolver(workers).Resolve(parallel, b.Orders, b.Traces);
            Check(SaveCodec.Fingerprint(scalar) == SaveCodec.Fingerprint(parallel), "shared scalar/reversed-worker replay turn " + turn);
            targeted += scalar.Camps.Count(x => x.HerdTargetSet);
            foreach (var camp in scalar.Camps)
                Check(camp.Workforce.Assignments.Sum(x => x.Population) <= camp.Population, "rolling plan never overallocates residents");
            if (turn == 15) parallel = SaveCodec.Load(SaveCodec.Save(parallel));
        }
        Check(targeted > 0, "replay exercises shared herd targets");
    }
}
