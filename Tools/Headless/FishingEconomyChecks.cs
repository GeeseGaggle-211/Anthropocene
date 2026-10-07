using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Anthropocene.Simulation;

internal static class FishingEconomyChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Fishing economy: " + message);
    }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-8)
    {
        checks++;
        if (double.IsNaN(actual) || Math.Abs(actual - expected) > tolerance * Math.Max(1, Math.Abs(expected)))
            throw new Exception("Fishing economy: " + message + " (actual=" + actual + ", expected=" + expected + ")");
    }

    // Shared by the small preparation and resolver-focused fishing suites.
    internal static WorldState Fixture(int population = 24)
    {
        var config = SimulationConfig.Small(1);
        config.Width = 24; config.Height = 16; config.TurnDays = 90; config.BaseDays = 90;
        config.DaysPerYear = 365; config.NeolithicFishingEnabled = true;
        config.CorrectedAquaticSeasonalPressureEnabled = true;
        config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        config.MinimumCampSpacing = 1;
        config.Neolithic.FishingToolFisherYearsPerUnit = 20;
        config.Neolithic.FishingToolHarvestMultiplier = 1.5;
        var world = new WorldState
        {
            Config = config, Seed = 17, Generation = new GenerationReport(),
            Tiles = new Tile[config.Width * config.Height], NextId = 500
        };
        for (int id = 0; id < world.Tiles.Length; id++)
        {
            var tile = world.Tiles[id] = new Tile
            {
                Id = id, Terrain = Terrain.Grassland, ElevationLevel = 1,
                Food = 80, PotentialFood = 100, Freshwater = 20,
                FreshwaterSource = FreshwaterSource.River,
                NaturalVegetationPotential = VegetationTier.Medium,
                CurrentVegetation = VegetationTier.Medium
            };
            for (int season = 0; season < 4; season++)
                tile.Seasons[season] = new SeasonData
                {
                    MaxFood = 100, MaxPotentialFood = 100, MaxFreshwater = 20,
                    FoodRecoveryPerDay = 0, BiomassRecoveryPerDay = 0,
                    WaterRecoveryPerDay = 0, Moisture = .5f, Habitat = .5f
                };
        }
        world.Societies.Add(new Society
        {
            Id = 100, Player = 0, Memory = new ContactMemory(config.Width),
            Policy = new PolicyState()
        });
        int campTile = world.Map.At(10, 8);
        world.Camps.Add(new Camp { Id = 1, SocietyId = 100, Player = 0, Tile = campTile, Population = population });
        Ownership.Bind(world);
        var camp = world.Camps[0];
        camp.People.Technologies[(int)Technology.Watercraft] = true;
        camp.People.Technology = Technology.Watercraft;
        camp.People.Memory = new ContactMemory(config.Width);
        return world;
    }

    private static Tile SetWater(WorldState world, int id, bool lake = false, double stock = 100, double biomass = 100)
    {
        var tile = world.Tiles[id];
        tile.Terrain = lake ? Terrain.Lake : Terrain.Shallows;
        tile.WaterBody = WaterKind.Saltwater; tile.FreshwaterSource = FreshwaterSource.None;
        tile.Food = stock; tile.PotentialFood = biomass;
        tile.LandCondition = 1; tile.AquaticCondition = 1;
        tile.NaturalVegetationPotential = tile.CurrentVegetation = VegetationTier.High;
        tile.LastEcologyUpdateDays = world.Days;
        for (int season = 0; season < 4; season++)
            tile.Seasons[season] = new SeasonData
            {
                MaxFood = 100, MaxPotentialFood = 100, MaxFreshwater = 0,
                FoodRecoveryPerDay = 0, BiomassRecoveryPerDay = 0,
                WaterRecoveryPerDay = 0, Moisture = .5f, Habitat = .5f
            };
        return tile;
    }

    private static void Remember(WorldState world, params int[] tiles)
    {
        var explored = world.Camps[0].People.Memory.Explored;
        foreach (int tile in tiles) explored.Add(tile, world.Turn);
    }

    private static FishingGroundPlan Ground(int tile, int distance, double foodPotential,
        double yield, double openingCapital = 0)
        => new FishingGroundPlan(tile, FishingRoute.Shallows, distance, FishingKnowledgeSource.Visible,
            false, 0, foodPotential, 100, 1, openingCapital, 100, foodPotential,
            new[] { .1, .1, .1, .1 }, yield);

    private static FishingAccessPlan Access(WorldState world, Camp camp, params FishingGroundPlan[] grounds)
        => new FishingAccessPlan(camp.Id, camp.Tile, camp.Population, world.Days,
            world.Config.DaysPerYear, 1, world.Config.Neolithic.FishingToolFisherYearsPerUnit,
            world.Config.Neolithic.FishingToolHarvestMultiplier, grounds, grounds);

    private static void RoutesAndKnowledge()
    {
        var world = Fixture(population: 0); var camp = world.Camps[0]; var map = world.Map;
        int origin = camp.Tile;
        int shallow1 = map.At(11, 8), shallow2 = map.At(12, 8), shallow3 = map.At(13, 8), shallow4 = map.At(14, 8);
        int lake1 = map.At(9, 8), lake2 = map.At(8, 8);
        foreach (int tile in new[] { shallow1, shallow2, shallow3, shallow4 }) SetWater(world, tile);
        SetWater(world, lake1, lake: true); SetWater(world, lake2, lake: true);
        Remember(world, shallow1, shallow2, shallow3, shallow4, lake1, lake2);
        camp.People.FishingGroundKnowledge.Add(new FishingGroundSnapshot
        {
            TileId = lake2, AccessibleFood = 24, Biomass = 80, Condition = .7,
            Capital = 2, ObservedDays = world.Days, OperationFraction = .5,
            ProductiveProfile = new[] { .1, .2, .3, .4 }
        });

        var access = FishingEconomy.BuildAccess(world, camp, origin, 10, world.Days,
            world.Config.DaysPerYear);
        var shallow = access.Candidates.Single(g => g.TileId == shallow3);
        var snapshot = access.Candidates.Single(g => g.TileId == lake2);
        Check(shallow.Distance == 3 && shallow.Route == FishingRoute.Shallows,
            "Shallows route is bounded and separately typed");
        Near(shallow.DistanceFactor, .75 * .75, "range-three distance retention");
        Check(!access.Candidates.Any(g => g.TileId == shallow4), "range-four ground is excluded");
        Check(snapshot.Distance == 2 && snapshot.Route == FishingRoute.Lake &&
            snapshot.KnowledgeSource == FishingKnowledgeSource.Snapshot,
            "Lake BFS does not cross Shallows and can use an owner snapshot");
        Near(snapshot.OpeningAccessibleFood, 24, "snapshot opening food");
        Near(snapshot.OpeningCapital, 2, "snapshot opening capital");

        var hidden = access.Candidates.Single(g => g.TileId == shallow1);
        Check(hidden.KnowledgeSource == FishingKnowledgeSource.ConservativePrior,
            "remembered unsurveyed water uses the named conservative prior");
        Near(hidden.OpeningAccessibleFood, 0, "unknown prior opening stock");
        Near(hidden.OpeningCapital, 0, "unknown prior capital");
        Near(hidden.FoodPotential, 0, "unknown prior current catch");
        var hiddenPriorAccess = Access(world, camp, hidden);
        Near(hiddenPriorAccess.CatchEnvelope(4), 0, "unknown live hidden stock does not change the prior catch envelope");
        world.Tiles[shallow1].Food = 99; world.Tiles[shallow1].PotentialFood = 99;
        var afterHiddenChange = FishingEconomy.BuildAccess(world, camp, origin, 10, world.Days,
            world.Config.DaysPerYear).Candidates.Single(g => g.TileId == shallow1);
        Near(afterHiddenChange.FoodPotential, 0, "hidden live stock changes cannot alter the conservative prior");

        camp.People.Memory = new ContactMemory(world.Config.Width);
        Check(!FishingEconomy.CanFish(world, camp), "an unexplored legal source is not eligible");
        var noSource = FishingEconomy.BuildAccess(world, camp, origin, 10, world.Days,
            world.Config.DaysPerYear);
        Check(noSource.Grounds.Length == 0, "no legal known source produces an empty access plan");
        world.Config.NeolithicFishingEnabled = false;
        Check(!FishingEconomy.FeatureEnabled(world, camp) && !FishingEconomy.CanFish(world, camp),
            "feature-off keeps resident Fishing disabled");
    }

    private static void SelectionAndCapital()
    {
        var world = Fixture(); var camp = world.Camps[0]; var map = world.Map;
        var four = map.Neighbors(camp.Tile).OrderBy(id => id).Take(4).ToArray();
        foreach (int tile in four) SetWater(world, tile);
        var initial = FishingEconomy.BuildAccess(world, camp, camp.Tile, camp.Population,
            world.Days, world.Config.DaysPerYear);
        Check(initial.Candidates.Length == 4 && initial.Grounds.Length == 3,
            "resident access selects no more than three grounds");
        var raw = initial.Candidates.OrderByDescending(g => g.YieldPerFisherYear).ThenBy(g => g.TileId).ToArray();
        int incumbent = raw[3].TileId;
        camp.LastSelectedFishingGrounds = new List<int> { incumbent };
        var retained = FishingEconomy.BuildAccess(world, camp, camp.Tile, camp.Population,
            world.Days, world.Config.DaysPerYear);
        Check(retained.Grounds.Length == 3 && retained.GroundIds.Contains(incumbent),
            "incumbent within the 2.5 percent cutoff margin is retained");
        Check(!retained.GroundIds.Contains(raw[2].TileId),
            "incumbency replaces only the weakest selected ground, not higher-yield sites");
        // Zero both available stock and potential habitat so a potential
        // recovery envelope cannot leave this incumbent at the cutoff.
        SetWater(world, incumbent, stock: 0, biomass: 0);
        world.Tiles[incumbent].AquaticCondition = 0;
        for (int seasonIndex = 0; seasonIndex < world.Tiles[incumbent].Seasons.Length; seasonIndex++)
        {
            var season = world.Tiles[incumbent].Seasons[seasonIndex];
            season.MaxFood = 0;
            season.MaxPotentialFood = 0;
            season.Habitat = 0;
            world.Tiles[incumbent].Seasons[seasonIndex] = season;
        }
        var switched = FishingEconomy.BuildAccess(world, camp, camp.Tile, camp.Population,
            world.Days, world.Config.DaysPerYear);
        Check(!switched.GroundIds.Contains(incumbent) && switched.GroundIds.Contains(raw[2].TileId),
            "an incumbent outside the cutoff margin yields its slot to the stronger ground"
                + " (incumbent=" + incumbent + ", yield=" + switched.Candidates.Single(g => g.TileId == incumbent).YieldPerFisherYear
                + ", cutoff=" + switched.Grounds.Min(g => g.YieldPerFisherYear) + ")");

        foreach (double capital in new[] { .2, .7, 1, 1.5, 3, 5 })
            Near(NeolithicCapital.CapitalForEffort(world.Config,
                NeolithicCapital.EffortForCapital(world.Config, capital)), capital,
                "shared effort/capital inverse at " + capital, 1e-7);
        double closed = NeolithicCapital.Decay(world.Config, 2, 0, 2);
        Check(closed < 2 && NeolithicCapital.Decay(world.Config, 2, 1, 2) == 2,
            "capital decays without operation and is held under full operation");
        double mean = NeolithicCapital.MeanDecayedCapital(world.Config, 2, 0, 2);
        Check(mean < 2 && mean > closed, "mean decayed capital lies between opening and endpoint");

        var saturated = Ground(four[0], 1, foodPotential: 1, yield: 10);
        var saturatedAccess = Access(world, camp, saturated);
        var staffed = FishingEconomy.PreparePlan(world, camp, saturatedAccess, 2, 0,
            world.Config.DaysPerYear, departing: false);
        var staffedGround = staffed.Grounds.Single();
        Near(staffedGround.RequestedCatch, 1, "catch request is capped at its accessible envelope");
        Near(staffedGround.AssignedFisherYears, 2, "all staffed Fisher-years remain assigned after catch saturation");
        Near(staffedGround.HarvestWorkerYears, 2, "the full Fisher assignment supplies harvest work");
        Near(staffedGround.CapitalWorkerYears, 2, "the full assignment also supplies capital work");
        Near(staffedGround.BuildWorkerYears, 2, "surplus work builds capital after catch saturates");
        Check(staffedGround.EndingCapital > 0, "catch saturation does not discard capital construction");

        var distant = Ground(four[0], 2, foodPotential: 100, yield: 7.5, openingCapital: 2);
        var toolAccess = Access(world, camp, distant);
        double baseCatch = toolAccess.CatchEnvelope(1, 0);
        double tooledCatch = toolAccess.CatchEnvelope(1, 1.0 /
            world.Config.Neolithic.FishingToolFisherYearsPerUnit);
        Near(tooledCatch, baseCatch * world.Config.Neolithic.FishingToolHarvestMultiplier,
            "covered tools multiply only catch");
        var withoutTools = FishingEconomy.PreparePlan(world, camp, toolAccess, 1, 0,
            world.Config.DaysPerYear, departing: false);
        var withTools = FishingEconomy.PreparePlan(world, camp, toolAccess, 1,
            1.0 / world.Config.Neolithic.FishingToolFisherYearsPerUnit,
            world.Config.DaysPerYear, departing: false);
        var noToolGround = withoutTools.Grounds.Single(); var toolGround = withTools.Grounds.Single();
        Near(noToolGround.CapitalWorkerYears, .75, "distance discounts effective capital work");
        Near(toolGround.CapitalWorkerYears, noToolGround.CapitalWorkerYears,
            "Fishing Tools do not change capital work");
        Near(toolGround.ToolCoveredHarvestWorkerYears, 1,
            "one tool service unit covers one nominal Fisher-year");
        Near(toolGround.RequestedCatch, noToolGround.RequestedCatch * 1.5,
            "tool coverage is nominal Fisher service and only changes catch");

        var departed = FishingEconomy.PreparePlan(world, camp, toolAccess, 1, 0,
            world.Config.DaysPerYear, departing: true);
        Check(departed.Departing && !departed.OperatorEligible,
            "departure releases management eligibility");
        Check(departed.Grounds.Single().RequestedCatch > 0 &&
            departed.Grounds.Single().CapitalWorkerYears == 0,
            "a departing camp may retain legal shared-stock catch without acquiring capital");
    }

    private static void VisibleCapitalAndSingleAccessibility()
    {
        var world = Fixture(); var camp = world.Camps.Single();
        int shore = world.Map.Neighbors(camp.Tile).OrderBy(id => id).First();
        var tile = SetWater(world, shore, stock: 25, biomass: 100);
        for (int i = 0; i < tile.Seasons.Length; i++)
        {
            var season = tile.Seasons[i];
            season.MaxFood = 25;
            season.MaxPotentialFood = 100;
            tile.Seasons[i] = season;
        }
        double q = .5;
        double capital = Math.Pow(q / world.Config.ReferenceIntensity, 2);
        world.FisherySites.Add(new FisherySite
        {
            TileId = shore, OperatorCampId = -1, Capital = capital,
            LastUpdatedDays = world.Days
        });

        var access = FishingEconomy.BuildAccess(world, camp, camp.Tile, camp.Population,
            world.Days, world.Config.DaysPerYear);
        var ground = access.Candidates.Single(g => g.TileId == shore);
        Near(ground.OpeningCapital, capital,
            "visible abandoned fishery capital remains known and reusable");
        Near(ground.ProspectiveClosingCapacity, 25 + (100 - 25) * q * q,
            "visible fishery accessibility applies q-squared exactly once");
    }

    private static void AcceptedRelocation()
    {
        var world = Fixture(); var camp = world.Camps[0]; var map = world.Map;
        int destination = map.At(camp.Tile % world.Config.Width + 1, camp.Tile / world.Config.Width);
        int shore = map.Neighbors(destination).Where(id => id != camp.Tile).OrderBy(id => id).First();
        SetWater(world, shore);
        camp.Workforce.Assignments.Add(new PopulationAssignment
        { Role = WorkforceRoles.Fisher, Population = 1 });
        var submitted = new Dictionary<int, List<Order>>
        {
            [camp.Player] = new List<Order> { new Order(OrderKind.Move, camp.Id, destination: destination) }
        };
        var prepared = TurnPlanner.Prepare(world, submitted);
        Check(prepared.Placement.Destinations[camp.Id] == destination,
            "fixture relocation is accepted");
        Check(prepared.FishingPlans.TryGetValue(camp.Id, out var plan) && plan.Departing,
            "PreparedTurn marks any accepted relocation as departing");
        Check(plan.Grounds.Any(g => g.RequestedCatch > 0) && !plan.OperatorEligible,
            "accepted relocation preserves legal catch but cannot maintain or build a site that turn");
    }

    private static void UnrelatedRecipeEligibilityIsLocal()
    {
        var world = Fixture();
        var camp = world.Camps.Single();
        for (int i = 1; i < 1280; i++)
            world.Camps.Add(new Camp
            {
                Id = 1000 + i, SocietyId = 5000 + i, Player = i,
                Tile = (camp.Tile + i) % world.Tiles.Length, Population = 1
            });
        var handTools = NeolithicProduction.Recipes(world.Config)
            .Single(recipe => recipe.Good == CraftGood.HandTools);
        Check(NeolithicProduction.RecipeAvailable(world, camp, handTools),
            "an unrelated recipe remains available in a Fishing-enabled world");

        // This allocation gate catches accidental construction of the global
        // Fishing access indexes from an unrelated recipe eligibility read.
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        bool available = true;
        for (int i = 0; i < 1000; i++)
            available &= NeolithicProduction.RecipeAvailable(world, camp, handTools);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(available && allocated < 1_000_000,
            "unrelated recipe checks do not rebuild all-camp/all-site Fishing indexes");
        Console.WriteLine("FISHING_RECIPE_SCALE camps=1280 calls=1000 elapsedMs=" +
            timer.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
            " allocatedBytes=" + allocated);
    }

    internal static void Run()
    {
        checks = 0;
        RoutesAndKnowledge(); SelectionAndCapital(); VisibleCapitalAndSingleAccessibility(); AcceptedRelocation();
        UnrelatedRecipeEligibilityIsLocal();
        Console.WriteLine("PASS: " + checks + " Fishing economy assertions");
    }
}
