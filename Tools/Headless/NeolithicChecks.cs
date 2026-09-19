using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anthropocene.Simulation;

internal static class NeolithicChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Neolithic: " + message); }

    private static WorldState CreateNeolithicWorld()
    {
        var cfg = SimulationConfig.Small(1);
        cfg.Width = 24; cfg.Height = 16;
        var w = new WorldState { Seed = 101, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile
            {
                Id = i,
                ElevationLevel = 1,
                Terrain = Terrain.Grassland,
                Environment = EnvironmentKind.Grassland,
                FreshwaterSource = FreshwaterSource.River,
                Freshwater = 100,
                PotentialFood = 800,
                Food = 80,
                Cultivation = 0f,
                Irrigated = false
            };
            for (int s = 0; s < 4; s++)
            {
                t.Seasons[s] = new SeasonData
                {
                    Temperature = 18,
                    Rain = 20,
                    Moisture = 0.55f,
                    RegionalMoisture = 0.50f,
                    MaxFood = 80,
                    MaxPotentialFood = 800,
                    MaxFreshwater = 100,
                    FoodRecoveryPerDay = (float)EcologyMath.DailyRate(0.35, 91.25),
                    BiomassRecoveryPerDay = (float)EcologyMath.DailyRate(0.35, 91.25),
                    WaterRecoveryPerDay = (float)EcologyMath.DailyRate(0.35, 91.25),
                    Habitat = 0.6f
                };
            }
            w.Tiles[i] = t;
        }

        var camp = new Camp
        {
            Id = w.NextId++,
            Player = 0,
            SocietyId = 0,
            Tile = 8 * 24 + 8,
            Population = 250,
            PlantFood = 100,
            AnimalFood = 50,
            AquaticFood = 50,
            IsVillage = false,
            VillageTurns = 0,
            StationaryTurns = 0,
            Inventory = new CampInventory
            {
                HandTools = 250,
                Pots = 200,
                Cloth = 250,
                Wood = 150,
                Stone = 150,
                Clay = 150,
                Houses = 32
            }
        };
        camp.Workforce.Assign(WorkforceRoles.Farmer, 80, camp.Population);
        camp.Workforce.Assign(WorkforceRoles.Craftsperson, 30, camp.Population);
        w.Camps.Add(camp);
        Ownership.Bind(w);
        return w;
    }

    public static int Run()
    {
        checks = 0;
        TestFoodTriPartition();
        TestCultivationDynamicsAndHarvest();
        TestIrrigationCanals();
        TestCraftLoopToolWearAndStorage();
        TestVillageTransitionAndQoL();
        TestConvergenceTechnologies();
        TestSaveLoadRoundTrip();
        TestNeolithicTurnContinuation();
        Console.WriteLine($"PASS: {checks} Neolithic Part I assertions");
        return checks;
    }

    private static void TestFoodTriPartition()
    {
        var w = CreateNeolithicWorld();
        var camp = w.Camps[0];

        // 1. Food tri-partition property consistency
        Check(Math.Abs(camp.Food - 200.0) < 1e-6, "Camp Food property equals sum of PlantFood, AnimalFood, and AquaticFood");

        // 2. Setting Food scales all categories proportionally
        camp.Food = 300.0;
        Check(Math.Abs(camp.PlantFood - 150.0) < 1e-6, "Proportional plant food scaling on Food set");
        Check(Math.Abs(camp.AnimalFood - 75.0) < 1e-6, "Proportional animal food scaling on Food set");
        Check(Math.Abs(camp.AquaticFood - 75.0) < 1e-6, "Proportional aquatic food scaling on Food set");

        // 3. Awarding specific food categories
        var hunter = new Hunter { Id = w.NextId++, Player = 0, SourceCamp = camp.Id, SocietyId = 0, Tile = camp.Tile, Population = 8 };
        w.Hunters.Add(hunter);
        var record = new TurnRecord();

        Supply.Award(w, hunter, 40.0, record, "Hunting");
        Check(Math.Abs(camp.AnimalFood - 115.0) < 1e-6, "Supply.Award Hunting awards to AnimalFood");

        Supply.Award(w, hunter, 25.0, record, "Fishing");
        Check(Math.Abs(camp.AquaticFood - 100.0) < 1e-6, "Supply.Award Fishing awards to AquaticFood");

        Check(Math.Abs(camp.Food - 365.0) < 1e-6, "Total Food reflects distinct category awards");
    }

    private static void TestCultivationDynamicsAndHarvest()
    {
        var w = CreateNeolithicWorld();
        var camp = w.Camps[0];
        int homeTile = camp.Tile;
        var footprint = w.Map.Radius(homeTile, 1).ToArray();

        // 1. Initial cultivation is 0
        Check(footprint.All(id => w.Tiles[id].Cultivation == 0f), "Initial cultivation is zero");

        // 2. Resolve agriculture with 80 farmers
        var record = new TurnRecord { Duration = w.Config.TurnDays, StartDays = w.Days };
        double intervals = w.Config.TurnDays / w.Config.BaseDays;
        double years = w.Config.TurnDays / w.Config.DaysPerYear;

        NeolithicProduction.ResolveAgriculture(w, camp, intervals, years, record);

        // Best expansion tile should have received cultivation increase
        double maxQ = footprint.Max(id => w.Tiles[id].Cultivation);
        Check(maxQ > 0.05f, "Farmers expanded cultivation on footprint");

        // 3. Maintenance burden and decay test:
        // Set fields high, remove all farmers, verify quadratic decay
        foreach (var id in footprint) w.Tiles[id].Cultivation = 0.50f;
        camp.Workforce.Assignments.Clear(); // 0 farmers

        float beforeDecay = w.Tiles[homeTile].Cultivation;
        NeolithicProduction.ResolveAgriculture(w, camp, intervals, years, record);
        float afterDecay = w.Tiles[homeTile].Cultivation;

        Check(afterDecay < beforeDecay, "Fields without maintenance decay under labor deficit");
        Check(afterDecay > 0.30f, "Decay is bounded and proportional, not catastrophic in a single turn");

        // 4. Farmer productivity and tool bonus
        camp.Inventory.Tools = 100; // has tools
        camp.Workforce.Assign(WorkforceRoles.Farmer, 50, camp.Population);
        double plantBefore = camp.PlantFood;
        NeolithicProduction.ResolveAgriculture(w, camp, intervals, years, record);
        double plantAfter = camp.PlantFood;
        Check(plantAfter > plantBefore, "Agriculture produces harvest added to PlantFood");
        Check(record.Transfers.Any(t => t.Kind == "Cultivation" && t.Food > 0), "Cultivation harvest is recorded in Transfers");
    }

    private static void TestIrrigationCanals()
    {
        var w = CreateNeolithicWorld();
        var camp = w.Camps[0];
        int center = camp.Tile;
        w.Tiles[center].Cultivation = 0.45f;
        int lakeNeighbor = w.Map.Neighbors(center).First();
        w.Tiles[lakeNeighbor].Terrain = Terrain.Lake;

        // Without technology, irrigation cannot occur
        var record = new TurnRecord { Duration = w.Config.TurnDays, StartDays = w.Days };
        double intervals = w.Config.TurnDays / w.Config.BaseDays;
        double years = w.Config.TurnDays / w.Config.DaysPerYear;

        NeolithicProduction.ResolveAgriculture(w, camp, intervals, years, record);
        Check(!w.Tiles[center].Irrigated, "Irrigation requires Irrigation technology");

        // Grant technology and verify canal construction
        camp.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
        NeolithicProduction.ResolveAgriculture(w, camp, intervals, years, record);
        Check(w.Tiles[center].Irrigated, "Canal constructed when technology and water adjacency are satisfied");
    }

    private static void TestCraftLoopToolWearAndStorage()
    {
        var w = CreateNeolithicWorld();
        var camp = w.Camps[0];
        camp.Inventory.Pots = 250;
        camp.Population = 250;

        // 1. Pot storage capacity multiplier
        double mult = NeolithicProduction.StorageCapacityMultiplier(camp);
        Check(Math.Abs(mult - 4.0) < 1e-4, "Full pot ratio (1.0) provides 4.0x storage capacity");

        camp.Inventory.Pots = 125;
        double halfMult = NeolithicProduction.StorageCapacityMultiplier(camp);
        Check(Math.Abs(halfMult - 2.5) < 1e-4, "Half pot ratio (0.5) provides 2.5x storage capacity");

        // 2. Craft cycle and tool degradation (in village)
        camp.IsVillage = true;
        camp.Inventory.HandTools = 200;
        camp.Inventory.FarmTools = 100;
        camp.Inventory.Pots = 100;
        camp.CraftingPriorities.Pots = 1.0;
        camp.CraftingPriorities.HandTools = 1.0;
        camp.CraftingPriorities.FarmTools = 0.0;
        camp.CraftingPriorities.Cloth = 0.0;
        camp.CraftingPriorities.Housing = 0.0;

        var record = new TurnRecord { Duration = w.Config.TurnDays, StartDays = w.Days };
        double intervals = w.Config.TurnDays / w.Config.BaseDays;
        double years = w.Config.TurnDays / w.Config.DaysPerYear;

        NeolithicProduction.ResolveCampNeolithic(w, camp, intervals, years, record);

        // Tool degradation: 15% wear over 20-year turn when not actively producing farm tools
        Check(camp.Inventory.FarmTools < 100, "Farm tools degrade over agricultural turn");
        Check(Math.Abs(camp.Inventory.FarmTools - 85.0) < 1e-4, "Farm tools suffer 15% degradation");
        Check(camp.Inventory.Pots > 0, "Pots maintained through crafting and degradation");
        Check(camp.Inventory.Wood > 150, "Gatherers harvest wood to replenish raw material reserves");
        Check(camp.Inventory.Clay > 0, "Gatherers harvest clay to replenish raw material reserves");
        Check(camp.Inventory.Stone > 0, "Gatherers harvest stone to replenish raw material reserves");
    }

    private static void TestVillageTransitionAndQoL()
    {
        var w = CreateNeolithicWorld();
        var camp = w.Camps[0];
        int[] footprint = w.Map.Radius(camp.Tile, 1).ToArray();

        // 1. Nomadic Camp has no domestic expectation or QoL penalty
        camp.PlantFood = 25000; // ample food so food needs are 100% met
        foreach (var id in footprint) w.Tiles[id].Cultivation = 0.10f;
        var record = new TurnRecord { Duration = w.Config.TurnDays, StartDays = w.Days };
        double intervals = w.Config.TurnDays / w.Config.BaseDays;
        double years = w.Config.TurnDays / w.Config.DaysPerYear;

        NeolithicProduction.ResolveCampNeolithic(w, camp, intervals, years, record);
        Check(!camp.IsVillage, "Camp does not become village when mean q <= 0.25");
        Check(camp.QoL >= 0.99, "Nomadic camp QoL depends only on food, unaffected by cloth/housing");
        Check(camp.FissionWilling == 0, "Nomadic camp has zero fission willingness");

        // 2. Village Transition at mean q > 0.25
        foreach (var id in footprint) w.Tiles[id].Cultivation = 0.35f;
        NeolithicProduction.ResolveCampNeolithic(w, camp, intervals, years, record);
        Check(camp.IsVillage, "Camp transitions to Village when mean q > 0.25");
        Check(camp.VillageTurns == 1, "Village increments stationary village turns");

        // 3. Domestic expectations ramp over 10 turns (10% per turn)
        double expectationTurn1 = NeolithicProduction.DomesticExpectationFactor(camp);
        Check(Math.Abs(expectationTurn1 - 0.10) < 1e-4, "Domestic expectation is 10% on first village turn");

        camp.VillageTurns = 5;
        double expectationTurn5 = NeolithicProduction.DomesticExpectationFactor(camp);
        Check(Math.Abs(expectationTurn5 - 0.50) < 1e-4, "Domestic expectation is 50% on fifth village turn");

        camp.VillageTurns = 10;
        double expectationTurn10 = NeolithicProduction.DomesticExpectationFactor(camp);
        Check(Math.Abs(expectationTurn10 - 1.00) < 1e-4, "Domestic expectation reaches 100% on tenth village turn");

        // 4. Fission execution when QoL falls and FissionWilling >= 25
        camp.Inventory.Cloth = 0;
        camp.Inventory.Houses = 0;
        camp.PlantFood = 0; camp.AnimalFood = 0; camp.AquaticFood = 0; // complete deficit
        int campsBefore = w.Camps.Count;
        int popBefore = camp.Population;

        NeolithicProduction.ResolveCampNeolithic(w, camp, intervals, years, record);
        Check(camp.QoL < 0.50, "Severe deficits lower QoL");
        Check(w.Camps.Count > campsBefore, "Fission creates splinter camp when deficit exceeds 25 people");
        Check(camp.Population < popBefore, "Parent village population reduced by splinter group");
    }

    private static void TestConvergenceTechnologies()
    {
        var w = CreateNeolithicWorld();
        var camp = w.Camps[0];
        camp.IsVillage = true;
        var soc = camp.People;

        // 1. Wheel and Axle convergence through diverse craft production
        camp.Inventory.Stone = 500;
        camp.Inventory.Wood = 500;
        camp.Inventory.Clay = 500;
        camp.CraftingPriorities.HandTools = 1;
        camp.CraftingPriorities.Pots = 1;
        camp.CraftingPriorities.FarmTools = 1;
        camp.CraftingPriorities.Housing = 1;
        camp.Workforce.Assign(WorkforceRoles.Craftsperson, 80, camp.Population);

        var record = new TurnRecord { Duration = w.Config.TurnDays, StartDays = w.Days };
        double intervals = w.Config.TurnDays / w.Config.BaseDays;
        double years = w.Config.TurnDays / w.Config.DaysPerYear;

        NeolithicProduction.ResolveCampNeolithic(w, camp, intervals, years, record);
        Check(soc.CraftExperience.ContainsKey(NeolithicTech.WheelAndAxle) && soc.CraftExperience[NeolithicTech.WheelAndAxle] > 0, "Crafting accumulates Wheel and Axle experience points");

        // 2. Copper Working convergence requires copper deposit exposure
        Check(!soc.CraftExperience.ContainsKey(NeolithicTech.CopperWorking), "Copper Working requires copper exposure");

        // Simulate local copper exposure
        w.Resources = new WorldResourceState
        {
            Bodies = new[]
            {
                new DepositBody
                {
                    Id = 1,
                    Components = new[] { new DepositComponent(MaterialId.Copper, 10000) },
                    Sites = new[] { new DepositSite { Tile = camp.Tile, Share = 10000, Exposure = 10000 } }
                }
            }
        };

        NeolithicProduction.ResolveCampNeolithic(w, camp, intervals, years, record);
        Check(soc.CraftExperience.ContainsKey(NeolithicTech.CopperWorking) && soc.CraftExperience[NeolithicTech.CopperWorking] > 0, "Pyrotechnology plus local copper exposure generates Copper Working experience");
    }

    private static void TestSaveLoadRoundTrip()
    {
        var w = CreateNeolithicWorld();
        var camp = w.Camps[0];
        camp.IsVillage = true;
        camp.VillageTurns = 4;
        camp.Inventory.Pots = 42;
        camp.Inventory.Cloth = 88;
        camp.Inventory.FarmTools = 19;
        camp.Inventory.Houses = 15;
        w.Tiles[camp.Tile].Cultivation = 0.62f;
        w.Tiles[camp.Tile].Irrigated = true;

        byte[] bytes = SaveCodec.Save(w);
        var loaded = SaveCodec.Load(bytes);

        Check(loaded.Tiles[camp.Tile].Cultivation == 0.62f, "Loaded tile preserves exact Cultivation factor");
        Check(loaded.Tiles[camp.Tile].Irrigated, "Loaded tile preserves Irrigated flag");

        var loadedCamp = loaded.Camps[0];
        Check(loadedCamp.IsVillage, "Loaded camp preserves IsVillage status");
        Check(loadedCamp.VillageTurns == 4, "Loaded camp preserves VillageTurns");
        Check(loadedCamp.Inventory.Pots == 42, "Loaded camp preserves Inventory Pots");
        Check(loadedCamp.Inventory.Cloth == 88, "Loaded camp preserves Inventory Cloth");
        Check(loadedCamp.Inventory.FarmTools == 19, "Loaded camp preserves Inventory FarmTools");
        Check(loadedCamp.Inventory.Houses == 15, "Loaded camp preserves Inventory Houses");
    }

    private static void TestNeolithicTurnContinuation()
    {
        var w = CreateNeolithicWorld();
        var camp = w.Camps[0];
        camp.Technology = Technology.Cultivation;
        camp.People.Progression.Era = Era.Neolithic;
        Check(w.Finished, "World evaluates as Finished for Paleolithic opening when all tribes are Neolithic");

        var resolver = new TurnResolver();
        var orders = new Dictionary<int, List<Order>> { [0] = new List<Order>() };

        // Ensure resolving turns past tech discovery proceeds smoothly without throwing
        for (int i = 1; i <= 3; i++)
        {
            int turnBefore = w.Turn;
            var record = resolver.Resolve(w, orders);
            Check(record != null && w.Turn == turnBefore + 1, "TurnResolver successfully resolves continuous turns in Neolithic era");
        }
        Check(w.Turn == 4, "World successfully reached turn 4 in the Neolithic era");
    }
}
