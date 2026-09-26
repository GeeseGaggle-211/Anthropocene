using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Differential oracle for the per-call Expand score cache. ExpandReference is
// the pre-cache marginal allocator copied here so the checks compare complete
// outcomes, rather than assertions derived from the optimized implementation.
internal static class CultivationCacheChecks
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("CultivationCache: " + message);
    }

    private static WorldState Fixture(string scenario)
    {
        var w = NeolithicChecks.Fixture();
        var camp = w.Camps[0];
        camp.PlantFood = 1_000_000;
        camp.CultivationFocusTile = scenario == "focus" || scenario == "displacement" ? camp.Tile : -1;
        w.Config.GathererBridgeEfficiency = .5;
        w.Config.GathererBridgeCeiling = .25;
        if (scenario == "custom-step") w.Config.Neolithic.AllocationStepE = .023;
        if (scenario == "displacement")
        {
            w.Config.Neolithic.ReferencePopulation = 10;
            w.Config.Neolithic.ReferenceTurnYears = 1;
            w.Config.Neolithic.IntensificationTurns = 1;
        }
        var footprint = CultivationEconomy.Footprint(w, camp);
        double bridgeCapital = Math.Pow(w.Config.GathererBridgeCeiling / w.Config.ReferenceIntensity, 2);
        for (int i = 0; i < footprint.Length; i++)
        {
            var tile = w.Tiles[footprint[i]];
            tile.Environment = i % 4 == 0 ? EnvironmentKind.Temperate : i % 4 == 1 ? EnvironmentKind.Cold :
                i % 4 == 2 ? EnvironmentKind.Tropical : EnvironmentKind.Grassland;
            tile.NaturalVegetationPotential = i % 3 == 0 ? VegetationTier.High : VegetationTier.Medium;
            tile.CurrentVegetation = i % 3 == 0 ? VegetationTier.High : i % 3 == 1 ? VegetationTier.Medium : VegetationTier.Low;
            tile.LandCondition = .55 + i * .055;
            tile.Food = 40 + i * 2;
            tile.PotentialFood = 600 + i * 31;
            for (int season = 0; season < 4; season++)
            {
                tile.Seasons[season].MaxFood = 45 + i * 2 + season * 3;
                tile.Seasons[season].MaxPotentialFood = 620 + i * 31 + season * 27;
                tile.Seasons[season].FoodRecoveryPerDay = .0025f + season * .0002f;
                tile.Seasons[season].BiomassRecoveryPerDay = .0035f + i * .0001f;
            }
            double capital = scenario == "bridge" && i == 1 ? bridgeCapital - 1e-5 :
                scenario == "ceiling" && i == 2 ? 6.245 :
                scenario == "uniform" ? 0 : (i % 4) * .075;
            tile.CultivationCapital = capital;
            tile.Cultivation = (float)CultivationEconomy.Intensity(w.Config, capital);
            tile.Cultivated = capital > 0;
            tile.CultivationOperator = capital > 0 ? camp.Id : -1;
            if (scenario == "focus" && tile.Id == camp.Tile)
            {
                tile.CultivationCapital = .32;
                tile.Cultivation = (float)CultivationEconomy.Intensity(w.Config, tile.CultivationCapital);
                tile.Cultivated = true;
                tile.CultivationOperator = camp.Id;
            }
        }
        if (scenario == "woods")
        {
            w.Tiles[footprint[0]].Environment = EnvironmentKind.Temperate;
            w.Tiles[footprint[0]].CurrentVegetation = VegetationTier.High;
            w.Tiles[footprint[1]].Environment = EnvironmentKind.Tropical;
            w.Tiles[footprint[1]].CurrentVegetation = VegetationTier.Medium;
        }
        return w;
    }

    private static double BuildMultiplier(SimulationConfig c, Tile t, VegetationTier vegetation)
    {
        var n = c.Neolithic;
        bool wooded = t.Land && vegetation >= VegetationTier.Medium &&
            (t.Environment == EnvironmentKind.Temperate || t.Environment == EnvironmentKind.Tropical || t.Environment == EnvironmentKind.Cold);
        return !wooded ? n.OpenBuild : vegetation == VegetationTier.High ? n.ForestBuild : n.WoodlandBuild;
    }

    private static double MaintenanceMultiplier(SimulationConfig c, Tile t, VegetationTier vegetation)
    {
        var n = c.Neolithic;
        bool wooded = t.Land && vegetation >= VegetationTier.Medium &&
            (t.Environment == EnvironmentKind.Temperate || t.Environment == EnvironmentKind.Tropical || t.Environment == EnvironmentKind.Cold);
        return !wooded ? n.OpenMaintenance : vegetation == VegetationTier.High ? n.ForestMaintenance : n.WoodlandMaintenance;
    }

    private static double ExpandReference(WorldState w, Camp camp, double workerYears, bool bridge, TurnRecord record,
        FarmToolWorkBudget toolBudget, Action<int> selectedTile)
    {
        if (!CultivationEconomy.Enabled(camp) || camp.Population <= 0 || workerYears <= 0 || bridge && w.Config.GathererBridgeEfficiency <= 0) return 0;
        var c = w.Config; var n = c.Neolithic;
        double coveredWorkerYears = !bridge && toolBudget != null ? Math.Min(workerYears, toolBudget.RemainingTools * n.FarmToolFarmerYearsPerUnit) : 0;
        double efficiency = !bridge ? CultivationEconomy.FarmToolFieldWorkEfficiency(n) : 1;
        double remaining = bridge ? workerYears * c.GathererBridgeEfficiency : workerYears - coveredWorkerYears + coveredWorkerYears * efficiency;
        double ceiling = bridge ? Math.Min(CultivationEconomy.InvestmentCapitalCeiling(c),
            Math.Pow(c.GathererBridgeCeiling / c.ReferenceIntensity, 2)) : CultivationEconomy.InvestmentCapitalCeiling(c);
        var candidates = CultivationEconomy.Footprint(w, camp).Where(id => w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == camp.Id).ToArray();
        while (remaining > 1e-10)
        {
            int best = -1; double bestScore = -1, step = 0, cost = 0;
            foreach (int id in candidates)
            {
                var t = w.Tiles[id];
                var ecology = LazyEcology.ProjectEcology(w, id, Math.Max(w.Days, t.LastEcologyUpdateDays));
                double e = ecology.CultivationCapital;
                if (e >= ceiling - 1e-12) continue;
                double vb = BuildMultiplier(c, t, ecology.CurrentVegetation);
                double vm = MaintenanceMultiplier(c, t, ecology.CurrentVegetation);
                if (bridge || CultivationEconomy.Intensity(c, e) < c.GathererBridgeCeiling) vb = n.OpenBuild;
                double de = Math.Min(n.AllocationStepE, Math.Min(ceiling - e, remaining / (n.BuildWorkerYearsPerTileE * vb)));
                if (!bridge && CultivationEconomy.Intensity(c, e) < c.GathererBridgeCeiling) de = Math.Min(de, Math.Pow(c.GathererBridgeCeiling / c.ReferenceIntensity, 2) - e);
                if (de <= 1e-12) continue;
                double gain = 0;
                var beforeView = ecology;
                var afterView = ecology;
                afterView.CultivationCapital = e + de;
                for (int season = 0; season < 4; season++)
                {
                    var beforeCapacity = new EcologyCapacity(w, t, beforeView, season, 1);
                    var afterCapacity = new EcologyCapacity(w, t, afterView, season, 1);
                    double beforeFlow = Math.Min(beforeCapacity.Food * beforeCapacity.FoodRate + beforeCapacity.ManagedFlow,
                        EcologyMath.SustainableHarvestRate(beforeCapacity.Biomass, beforeCapacity.BiomassRate, c.SustainableProductionFraction, beforeCapacity.ManagedFlow) * c.LandSafeUseRatio);
                    double afterFlow = Math.Min(afterCapacity.Food * afterCapacity.FoodRate + afterCapacity.ManagedFlow,
                        EcologyMath.SustainableHarvestRate(afterCapacity.Biomass, afterCapacity.BiomassRate, c.SustainableProductionFraction, afterCapacity.ManagedFlow) * c.LandSafeUseRatio);
                    gain += (afterFlow - beforeFlow) * c.DaysPerYear / 4;
                }
                double buildCost = de * n.BuildWorkerYearsPerTileE * vb;
                double score = gain / Math.Max(1e-12, buildCost + n.PlanningHorizonYears * n.MaintenanceWorkersPerTileE(c.MaintenanceCoefficient) * vm * de);
                bool focused = id == camp.CultivationFocusTile;
                if (score > 0 && (best < 0 || focused || best != camp.CultivationFocusTile && score > bestScore)) { best = id; bestScore = score; step = de; cost = buildCost; }
            }
            if (best < 0) break;
            selectedTile?.Invoke(best);
            var target = w.Tiles[best];
            var beforeEcology = LazyEcology.MaterializeEcology(w, best, Math.Max(w.Days, target.LastEcologyUpdateDays));
            double oldQ = CultivationEconomy.Intensity(c, beforeEcology.CultivationCapital);
            double oldRetention = EcologyMath.CultivationVegetationRetention(c, oldQ);
            target.CultivationOperator = camp.Id; target.CultivationCapital = beforeEcology.CultivationCapital + step;
            double newQ = CultivationEconomy.Intensity(c, target.CultivationCapital);
            target.Cultivation = (float)newQ; target.Cultivated = target.CultivationCapital > 0;
            double newRetention = EcologyMath.CultivationVegetationRetention(c, newQ);
            if (newRetention < oldRetention)
            {
                double woodCapacityLoss = MaterialEconomy.ClampWoodSource(w, best, beforeEcology, new EcologyView(target));
                double displacedRetention = oldRetention - newRetention;
                record.Pressure.Add(new PressureEntry
                {
                    Tile = best, Entity = camp.Id, Kind = "Cultivation vegetation displacement", Pool = "Vegetation cover",
                    Requested = displacedRetention, Realized = displacedRetention, WoodCapacityLoss = woodCapacityLoss, ConditionDamage = 0
                });
            }
            remaining -= cost;
        }
        double effectiveUsed = (bridge ? workerYears * c.GathererBridgeEfficiency : workerYears - coveredWorkerYears + coveredWorkerYears * efficiency) - remaining;
        if (!bridge && toolBudget != null && effectiveUsed > 0)
        {
            double coveredUsed = Math.Min(coveredWorkerYears, effectiveUsed / efficiency);
            toolBudget.Consume(coveredUsed, n.FarmToolFarmerYearsPerUnit);
            double uncoveredUsed = Math.Max(0, effectiveUsed - coveredUsed * efficiency);
            return coveredUsed + uncoveredUsed;
        }
        return effectiveUsed / (bridge ? c.GathererBridgeEfficiency : 1);
    }

    private static void ConfigureScenario(WorldState w, string scenario)
    {
        if (scenario == "uniform")
        {
            foreach (int id in CultivationEconomy.Footprint(w, w.Camps[0]))
            {
                var tile = w.Tiles[id];
                tile.Environment = EnvironmentKind.Grassland;
                tile.NaturalVegetationPotential = VegetationTier.Low;
                tile.CurrentVegetation = VegetationTier.Low;
                tile.LandCondition = 1;
                tile.CultivationCapital = 0;
                tile.CultivationOperator = -1;
                for (int season = 0; season < 4; season++)
                {
                    tile.Seasons[season].MaxFood = 80;
                    tile.Seasons[season].MaxPotentialFood = 800;
                    tile.Seasons[season].FoodRecoveryPerDay = .004f;
                    tile.Seasons[season].BiomassRecoveryPerDay = .004f;
                }
            }
        }
    }

    private static void RunCase(string scenario, double workerYears, bool bridge, double toolStock, bool callback = false, bool callbackMutatesNeighbor = false)
    {
        var expectedWorld = Fixture(scenario); ConfigureScenario(expectedWorld, scenario);
        var actualWorld = Fixture(scenario); ConfigureScenario(actualWorld, scenario);
        var expectedCamp = expectedWorld.Camps[0]; var actualCamp = actualWorld.Camps[0];
        expectedCamp.Inventory.FarmTools = actualCamp.Inventory.FarmTools = toolStock;
        var expectedBudget = bridge ? null : new FarmToolWorkBudget(expectedWorld, expectedCamp);
        var actualBudget = bridge ? null : new FarmToolWorkBudget(actualWorld, actualCamp);
        var expectedRecord = new TurnRecord(); var actualRecord = new TurnRecord();
        var expectedSelected = new List<int>(); var actualSelected = new List<int>();
        var footprint = CultivationEconomy.Footprint(actualWorld, actualCamp);
        int mutationTile = footprint.Last();
        Action<int> expectedCallback = callback ? id =>
        {
            expectedSelected.Add(id);
            if (callbackMutatesNeighbor && id != mutationTile) expectedWorld.Tiles[mutationTile].LandCondition -= .001;
        } : null;
        Action<int> actualCallback = callback ? id =>
        {
            actualSelected.Add(id);
            if (callbackMutatesNeighbor && id != mutationTile) actualWorld.Tiles[mutationTile].LandCondition -= .001;
        } : null;
        double expectedUsed = ExpandReference(expectedWorld, expectedCamp, workerYears, bridge, expectedRecord, expectedBudget, expectedCallback);
        double actualUsed = CultivationEconomy.Expand(actualWorld, actualCamp, workerYears, bridge, actualRecord, actualBudget, actualCallback, footprint);

        if (scenario == "uniform" && workerYears >= 100_000)
        {
            double maxQ = footprint.Max(id => CultivationEconomy.Intensity(actualWorld.Config, actualWorld.Tiles[id].CultivationCapital));
            Check(maxQ <= actualWorld.Config.Neolithic.PrimitiveAgricultureSupportedIntensity + 1e-10,
                "primitive fields must not expand beyond supported q");
            Check(maxQ >= actualWorld.Config.Neolithic.PrimitiveAgricultureSupportedIntensity - 1e-10,
                "ample work must reach supported primitive q");
        }

        Check(expectedUsed == actualUsed, scenario + " realized worker-years");
        Check(expectedSelected.SequenceEqual(actualSelected), scenario + " selected tile order");
        Check(expectedRecord.Pressure.Count == actualRecord.Pressure.Count, scenario + " pressure count");
        for (int i = 0; i < expectedRecord.Pressure.Count; i++)
        {
            var a = expectedRecord.Pressure[i]; var b = actualRecord.Pressure[i];
            Check(a.Tile == b.Tile && a.Entity == b.Entity && a.Kind == b.Kind && a.Pool == b.Pool &&
                a.Requested == b.Requested && a.Realized == b.Realized && a.WoodCapacityLoss == b.WoodCapacityLoss && a.ConditionDamage == b.ConditionDamage,
                scenario + " pressure receipt " + i);
        }
        Check(expectedBudget == null || expectedBudget.RemainingTools == actualBudget.RemainingTools && expectedBudget.CoveredWorkerYears == actualBudget.CoveredWorkerYears,
            scenario + " Farm Tool budget");
        Check(expectedCamp.Inventory.FarmTools == actualCamp.Inventory.FarmTools, scenario + " Farm Tool inventory");
        for (int i = 0; i < expectedWorld.Tiles.Length; i++)
        {
            var a = expectedWorld.Tiles[i]; var b = actualWorld.Tiles[i];
            Check(a.CultivationCapital == b.CultivationCapital && a.CultivationOperator == b.CultivationOperator &&
                a.Cultivation == b.Cultivation && a.Cultivated == b.Cultivated && a.Food == b.Food &&
                a.PotentialFood == b.PotentialFood && a.LandCondition == b.LandCondition && a.LastEcologyUpdateDays == b.LastEcologyUpdateDays,
                scenario + " final tile state " + i);
        }
    }

    public static void Run()
    {
        checks = 0;
        RunCase("partial-budget", 4.321, false, 0);
        RunCase("custom-step", 420.5, false, 0);
        RunCase("uniform", 520.75, false, 0);
        RunCase("uniform", 100_000, false, 0);
        RunCase("focus", 438.4, false, 0);
        RunCase("displacement", 40, false, 0);
        RunCase("bridge", 444.25, true, 0);
        RunCase("ceiling", 18.7, false, 0);
        RunCase("woods", 461.125, false, 0);
        RunCase("tools", 427.6, false, .63);
        RunCase("tools-clipped", .000001, false, .00000002);
        RunCase("callback", 24.5, false, .17, true);
        RunCase("callback-mutates-neighbor", 24.5, false, .17, true, true);
        Console.WriteLine("PASS: " + checks + " cultivation score-cache differential assertions");
    }
}
