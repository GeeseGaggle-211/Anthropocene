using System;
using System.Linq;
using Anthropocene.Simulation;

// Standalone (not in the fixed suite): the kernel-split prototype flags are process-wide, and fixed-suite lanes run
// concurrently. --kernel-mixed-checks sets them for its own scalar fixtures and restores them afterwards.
// Covers the mixed-use extension (Docs/ClothingCoverageAndSurplusBirths.md §8): route choice under infeasible plant
// feedstock and fixed-housing materials, the committed ElderAction and its requests, purity and determinism, and that
// non-mixed camps are unchanged by the mixed flag.
internal static class KernelMixedChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("KernelMixed: " + message); }

    private static WorldState World()
    {
        var cfg = SimulationConfig.Small(1); cfg.DemographicModel = DemographicModel.PhysicalMortalityV1; cfg.QoLMortalityEnabled = false;
        cfg.EconomicAllocation = EconomicAllocationStrategy.DemandControl; cfg.Width = 24; cfg.Height = 16; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0;
        var w = new WorldState { Seed = 404, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 8000, Food = 1000 };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = t;
        }
        var c = new Camp { Id = w.NextId++, Player = 0, Tile = w.Map.At(6, 6), Population = 100, PlantFood = 40000, AnimalFood = 5000, RudimentaryExpectation = .5, StationaryYears = 40, StationaryTurns = 2, IsVillage = true, EconomyAutomationTurns = -1 };
        c.Inventory.HandTools = 200; c.Inventory.Wood = 500; c.Inventory.Stone = 500; c.Inventory.Clay = 500;
        w.Camps.Add(c);
        w.Config.Players = 1; w.PlayerCount = 1; w.Turn = 3; w.Days = 1000;
        Ownership.Bind(w);
        c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
        c.Technologies[(int)Technology.AnimalHusbandry] = true;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Construction); c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        foreach (var t in w.Tiles) t.LastEcologyUpdateDays = w.Days;
        return w;
    }

    private static void StarvePlants(WorldState w, Camp c)
    {
        foreach (var tile in w.Tiles)
        {
            tile.Food = tile.PotentialFood = 0;
            for (int season = 0; season < tile.Seasons.Length; season++) { var p = tile.Seasons[season]; p.MaxFood = p.MaxPotentialFood = 0; tile.Seasons[season] = p; }
        }
        c.PlantFood = 0; c.AquaticFood = 0; c.AnimalFood = 200000;
    }

    private static DemandController.Allocation Plan(WorldState w, Camp c, out ProductionOrder order)
    {
        DemandController.Plan(w, c, EconomicIntent.From(c), out order, out var allocation);
        return allocation;
    }

    public static int Run()
    {
        checks = 0;
        bool kernel = DemandController.KernelSplitDiagnostic, mixed = DemandController.KernelMixedDiagnostic, surplus = PhysicalDemography.SurplusFundedBirthsDiagnostic;
        int objective = DemandController.KernelSplitObjective; double tolerance = DemandController.KernelLexTolerance;
        bool capture = ResolutionMetrics.CaptureDemandAllocations;
        try
        {
            ResolutionMetrics.CaptureDemandAllocations = true;
            DemandController.KernelSplitDiagnostic = true; PhysicalDemography.SurplusFundedBirthsDiagnostic = true;
            DemandController.KernelSplitObjective = 2; DemandController.KernelLexTolerance = .05;

            // 1. No plant feedstock, abundant animal food: Cloth cannot be funded, so a mixed Elder clothes with Animal Fabric.
            {
                var w = World(); var c = w.Camps[0]; StarvePlants(w, c);
                DemandController.KernelMixedDiagnostic = false;
                var single = Plan(w, c, out _);
                Check(single.ElderAction != null && single.ElderAction.ClothingRoute == CraftGood.Cloth, "single-route Elder keeps the Cloth route");
                double singleClothing = single.Requests.Where(q => q.Service == "clothing").Sum(q => q.Granted);
                DemandController.KernelMixedDiagnostic = true;
                string before = SaveCodec.Fingerprint(w);
                var both = Plan(w, c, out var order);
                Check(SaveCodec.Fingerprint(w) == before, "mixed planning is pure");
                var action = both.ElderAction;
                Check(action != null && action.ClothingRoute == CraftGood.AnimalFabric && action.ClothingUnits > 0,
                    "mixed Elder commits Animal Fabric clothing when plant feedstock is absent: " + action?.ClothingRoute + " " + action?.ClothingUnits);
                var protectedClothing = both.Requests.Single(q => q.Service == "clothing");
                Check(protectedClothing.Good == CraftGood.AnimalFabric && protectedClothing.Class == DemandController.ServiceClass.Protected,
                    "the protected clothing request follows the committed route");
                Check(protectedClothing.Granted > 1e-9 && order.Ceiling(CraftGood.AnimalFabric) > 1e-9, "the committed Animal Fabric clothing is funded");
                Check(protectedClothing.Granted > singleClothing + 1e-9, "mixed routing clothes more than the infeasible single route ("
                    + protectedClothing.Granted + " vs " + singleClothing + ")");
                Check(both.KernelCandidates != null && both.KernelCandidates.Any(k => k.ClothingRoute == CraftGood.Cloth) && both.KernelCandidates.Any(k => k.ClothingRoute == CraftGood.AnimalFabric),
                    "the scored pool records both clothing routes for teacher data");
                var again = Plan(w, c, out _).ElderAction;
                Check(again.ClothingRoute == action.ClothingRoute && again.ClothingLevel == action.ClothingLevel && again.ShelterRoute == action.ShelterRoute
                    && again.ShelterLevel == action.ShelterLevel && again.Posture == action.Posture && again.PersonYears == action.PersonYears, "mixed planning is deterministic");
            }

            // 2. No Wood or Clay for fixed Housing, abundant animal food: the shelter split moves to Light Housing.
            {
                var w = World(); var c = w.Camps[0];
                c.Inventory.Wood = c.Inventory.Clay = c.Inventory.HousingMaterial = 0; c.WoodPriority = c.ClayPriority = 0; c.AnimalFood = 200000;
                c.Inventory.Cloth = 10000;
                DemandController.KernelMixedDiagnostic = true;
                var both = Plan(w, c, out var order);
                var action = both.ElderAction;
                Check(action != null && action.ShelterRoute == CraftGood.LightHousing && action.ShelterPeople > 0,
                    "mixed Elder shelters with Light Housing when fixed Housing has no material: " + action?.ShelterRoute + " " + action?.ShelterPeople);
                var shelter = both.Requests.Where(q => q.Service == "shelter protected").ToArray();
                Check(shelter.Length >= 1 && shelter[0].Good == CraftGood.LightHousing && shelter[0].Granted > 1e-9 && order.Ceiling(CraftGood.LightHousing) > 1e-9,
                    "the protected shelter request uses the committed tent route and is funded");
            }

            // 3. A farming-only camp is unchanged by the mixed flag (identical requests and grants).
            {
                var w = World(); var c = w.Camps[0];
                c.Technologies[(int)Technology.AnimalHusbandry] = false;
                DemandController.KernelMixedDiagnostic = false;
                var single = Plan(w, c, out var singleOrder);
                DemandController.KernelMixedDiagnostic = true;
                var both = Plan(w, c, out var bothOrder);
                string Describe(DemandController.Allocation a) => string.Join(";", a.Requests.Select(q => q.Service + ":" + q.Good + ":" + q.Class + ":" + q.Units.ToString("R") + ":" + q.Granted.ToString("R")));
                Check(Describe(single) == Describe(both), "non-mixed camps keep the single-route grid");
                Check(single.ElderAction.ClothingLevel == both.ElderAction.ClothingLevel && single.ElderAction.ShelterLevel == both.ElderAction.ShelterLevel
                    && both.ElderAction.Posture == DemandController.LabourPosture.HerdFirst, "non-mixed action is the single-route action");
                Check(Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().All(g => singleOrder.Ceiling(g) == bothOrder.Ceiling(g)), "non-mixed production ceilings are unchanged");
            }
        }
        finally
        {
            DemandController.KernelSplitDiagnostic = kernel; DemandController.KernelMixedDiagnostic = mixed; PhysicalDemography.SurplusFundedBirthsDiagnostic = surplus;
            DemandController.KernelSplitObjective = objective; DemandController.KernelLexTolerance = tolerance;
            ResolutionMetrics.CaptureDemandAllocations = capture;
        }
        Console.WriteLine("PASS: " + checks + " kernel mixed-use assertions");
        return checks;
    }
}
