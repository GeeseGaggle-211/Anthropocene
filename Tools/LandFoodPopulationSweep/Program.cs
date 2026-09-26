// Population-pressure audit of the harvest-first land kernel. All interventions
// are fixture setup; every turn uses the production planner and resolver.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static readonly (string Name, int Seed, int Camp, int Tile)[] Sites =
    {
        ("open-fertile", 15, 17, 3819),
        ("medium-woodland", 11, 29, 4530),
        ("dry-marginal", 17, 24, 4373)
    };

    private readonly struct Stocks
    {
        public readonly double G, B, MaxG, MaxB, Q0MaxG;
        public readonly double Condition, Q, MaxQ, E, LargestExcess;
        public readonly int FieldTiles;
        public Stocks(double g, double b, double maxG, double maxB, double q0MaxG,
            double condition, double q, double maxQ, double e, int fieldTiles,
            double largestExcess)
        {
            G = g; B = b; MaxG = maxG; MaxB = maxB; Q0MaxG = q0MaxG;
            Condition = condition; Q = q; MaxQ = maxQ;
            E = e; FieldTiles = fieldTiles; LargestExcess = largestExcess;
        }
    }

    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("LAND_FOOD_SWEEP_FAILURE " + ex); return 1; }
    }

    private static void Run(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string Value(string name, string fallback)
        { int index = Array.IndexOf(args, name); return index < 0 ? fallback : args[index + 1]; }
        string root = Path.GetFullPath(Value("--repo", "."));
        string output = Path.GetFullPath(Value("--out", "TestResults/LandFoodPopulationSweep"));
        int[] populations = Value("--populations", "8,16,24,32,48,64,96,144,192")
            .Split(',').Select(int.Parse).Distinct().OrderBy(x => x).ToArray();
        int controlledTurns = int.Parse(Value("--controlled-turns", "8"));
        int liveTurns = int.Parse(Value("--live-turns", "12"));
        string multiplier = Value("--land-m", "");
        string landAccessScale = Value("--land-access-scale", "");
        string cultivationEffort = Value("--cultivation-effort", "");
        bool noBreakthrough = Value("--no-breakthrough", "0") == "1";
        double regeneration = double.Parse(Value("--land-regeneration", "0.03"));
        double managedRegeneration = double.Parse(Value("--cultivation-regeneration", "0.65"));
        if (populations.Length == 0 || populations.Any(p => p < 1) || controlledTurns < 1 || liveTurns < 1)
            throw new ArgumentException("Invalid population sweep dimensions.");
        Directory.CreateDirectory(output);
        var config = JsonSerializer.Deserialize<SimulationConfig>(
            File.ReadAllText(Path.Combine(root, "Assets/_Anthropocene/Resources/OpeningConfig.json")), Json);
        var resources = JsonSerializer.Deserialize<ResourceGenerationOptions>(
            File.ReadAllText(Path.Combine(root, "Assets/_Anthropocene/Resources/ResourceGenerationConfig.json")), Json);
        config.Width = 128; config.Height = 64; config.Players = 8; config.StartSeparation = 10;
        if (multiplier.Length > 0) config.LandBiomassMultiplier = double.Parse(multiplier);
        if (landAccessScale.Length > 0)
        {
            double scale = double.Parse(landAccessScale);
            config.RichGatherableFraction *= scale;
            config.GrasslandGatherableFraction *= scale;
            config.AridGatherableFraction *= scale;
            config.TundraGatherableFraction *= scale;
        }
        if (cultivationEffort.Length > 0)
            config.Neolithic.CultivationMarginalEffortMultiplierPerTenth = double.Parse(cultivationEffort);
        if (noBreakthrough)
        {
            config.ExperienceThreshold = 1000000000;
            config.MajorityEligibilityYears = 1000000;
            config.ForcedTransitionYears = 1000001;
        }
        config.LandSafeUseRatio = .5;
        config.LandFoodRegeneration = regeneration;
        config.LandFoodRegenerationInitialized = true;
        config.CultivationRegenFlux = managedRegeneration;
        config.LandUseDamagePerYear = .03; config.Validate();
        File.WriteAllText(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(config, Json));
        var worlds = new Dictionary<int, byte[]>();
        foreach (int seed in Sites.Select(s => s.Seed).Distinct().OrderBy(x => x))
        {
            Console.WriteLine("GENERATE seed=" + seed);
            var generationConfig = JsonSerializer.Deserialize<SimulationConfig>(JsonSerializer.Serialize(config, Json), Json);
            var world = new WorldGenerator().Generate(seed, generationConfig, resources);
            worlds.Add(seed, SaveCodec.Save(world));
        }
        using (var footprints = new StreamWriter(Path.Combine(output, "footprints.csv")))
        {
            footprints.WriteLine("site,seed,campTile,footprintTile,environment,vegetation,openingBuildFactor,openingMaintenanceFactor");
            foreach (var site in Sites)
            {
                var world = SaveCodec.Load(worlds[site.Seed]);
                foreach (int id in world.Map.Radius(site.Tile, 1).Where(id => world.Tiles[id].Land).OrderBy(id => id))
                {
                    var tile = world.Tiles[id];
                    CultivationEconomy.Factors(world.Config, tile, out double build, out double maintenance);
                    footprints.WriteLine(string.Join(",", new object[]
                    {
                        site.Name, site.Seed, site.Tile, id, tile.Environment, tile.CurrentVegetation, build, maintenance
                    }.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))));
                }
            }
        }
        using var writer = new StreamWriter(Path.Combine(output, "turns.csv"));
        writer.WriteLine("mode,site,seed,tile,initialPopulation,turn,year,startPopulation,endPopulation,openingG,openingB,openingMaxG,openingMaxB,endingG,endingB,endingMaxG,endingMaxB,foodProduced,foodConsumed,basicFoodNeed,reserveEnd,foodSatisfaction,waterSatisfaction,births,deaths,condition,q,maxQ,largestCapacityExcess,technology,openingQ,openingE,endingE,fieldTiles,openingQ0MaxG,endingQ0MaxG,landHumanHarvest,landBiomassHarvest,landFoodRechargeAndReclassification,landBiomassRecharge,landFoodOverflow,landBiomassOverflow,landManagedRenewalInput,operatingFieldTiles,meanOperatingFraction,openingReserves,reserveGoal,foodReceived,manufacturingFood,discardedFood,growthFoodSpent,assignedFarmerYears,assignedExpansionYears,requestedBridgeYears");
        foreach (var site in Sites)
            foreach (int population in populations)
                foreach (string mode in new[] { "controlled-q0", "live" })
                {
                    Console.WriteLine("RUN " + mode + " " + site.Name + " population=" + population);
                    RunCase(worlds[site.Seed], site, population, mode, mode == "live" ? liveTurns : controlledTurns, writer);
                    writer.Flush();
                }
        using var unharvested = new StreamWriter(Path.Combine(output, "unharvested.csv"));
        unharvested.WriteLine("site,seed,tile,turn,year,openingG,openingB,endingG,endingB,endingMaxG,endingMaxB,condition");
        foreach (var site in Sites) RunUnharvested(worlds[site.Seed], site, controlledTurns, unharvested);
        Console.WriteLine("PASS: all projected G and B are nonnegative, nested and within their per-tile capacities.");
    }

    private static void RunUnharvested(byte[] original, (string Name, int Seed, int Camp, int Tile) site,
        int turns, StreamWriter writer)
    {
        var world = SaveCodec.Load(original);
        var camp = world.Camps.Single(c => c.Id == site.Camp);
        world.Camps = new List<Camp> { camp };
        world.Hunters.Clear(); world.Herds.Clear();
        world.Societies = world.Societies.Where(s => s.Id == camp.SocietyId).ToList();
        camp.Tile = site.Tile;
        Ownership.Bind(world);
        var radius = world.Map.Radius(camp.Tile, 1).Where(id => world.Tiles[id].Land).OrderBy(id => id).ToArray();
        for (int turn = 1; turn <= turns; turn++)
        {
            var opening = Snapshot(world, radius);
            double start = world.Days, duration = world.Config.TurnDays;
            var endpoints = radius.Select(id => (Id: id, Result: EcologicalPressure.Integrate(
                world, id, Array.Empty<Demand>(), start, duration))).ToArray();
            foreach (var endpoint in endpoints) endpoint.Result.View.Commit(world.Tiles[endpoint.Id]);
            world.Days += duration;
            var ending = Snapshot(world, radius);
            writer.WriteLine(string.Join(",", new object[]
            {
                site.Name, site.Seed, site.Tile, turn, world.Days / world.Config.DaysPerYear,
                opening.G, opening.B, ending.G, ending.B, ending.MaxG, ending.MaxB,
                ending.Condition
            }.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))));
        }
    }

    private static void RunCase(byte[] original, (string Name, int Seed, int Camp, int Tile) site,
        int population, string mode, int turns, StreamWriter writer)
    {
        var world = SaveCodec.Load(original);
        var camp = world.Camps.Single(c => c.Id == site.Camp);
        world.Camps = new List<Camp> { camp };
        world.Hunters.Clear();
        world.Societies = world.Societies.Where(s => s.Id == camp.SocietyId).ToList();
        camp.Tile = site.Tile; camp.Moves = 0; camp.StationaryTurns = 0; camp.StationaryYears = 0;
        camp.Population = population; camp.Workforce.Normalize(population);
        camp.PlantFood = population * world.Config.Consumption * world.Config.GenerationIntervals;
        camp.AnimalFood = 0; camp.AquaticFood = 0;
        if (mode == "controlled-q0")
        {
            world.Herds.Clear();
            world.Config.AnnualHumanGrowthRate = 0;
            world.Config.StarvationGraceTurns = turns + 2;
            world.Config.ExperienceThreshold = 1e9;
        }
        Ownership.Bind(world);
        var radius = world.Map.Radius(camp.Tile, 1).Where(id => world.Tiles[id].Land).OrderBy(id => id).ToArray();
        var radiusSet = new HashSet<int>(radius);
        if (radius.Length != 7 || camp.Technology == Technology.Cultivation)
            throw new InvalidOperationException("Pinned site no longer has seven land tiles and a Paleo opening: " + site.Name);
        for (int turn = 1; turn <= turns && camp.Population > 0; turn++)
        {
            int startingPopulation = camp.Population;
            var opening = Snapshot(world, radius);
            double openingReserves = camp.Food;
            double intervals = world.Config.GenerationIntervals;
            double years = world.Config.TurnDays / world.Config.DaysPerYear;
            double reserveGoal = Math.Min(
                NeolithicProduction.DesiredReserveFood(world, startingPopulation, intervals),
                NeolithicProduction.StorageCapacityAfterPotDepreciation(world, camp, intervals, years));
            List<Order> orders;
            if (mode == "live" && camp.Technology == Technology.Cultivation)
            {
                var intent = EconomicIntents.Automatic(world, camp, true);
                intent.AutomationTurns = -1;
                orders = new List<Order> { new Order(OrderKind.Economy, camp.Id) { Economy = intent } };
            }
            else orders = new List<Order> { new Order(OrderKind.NoOp, camp.Id) };
            var resolver = new TurnResolver();
            var record = resolver.Resolve(world,
                new Dictionary<int, List<Order>> { [camp.Player] = orders });
            bool economicOrder = mode == "live" && orders[0].Kind == OrderKind.Economy;
            double assignedFarmerYears = economicOrder
                ? orders[0].Economy.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population) * years : 0;
            double assignedExpansionYears = economicOrder
                ? (orders[0].Economy.Labor?.ExpansionFarmers ?? 0) * years : 0;
            double requestedBridgeYears = economicOrder
                ? record.Transfers.Where(t => t.Kind == "Gathering" && t.Entity == camp.Id && world.Tiles[t.Tile].Land)
                    .Sum(t => t.Food) / world.Config.GatherPerPerson * world.Config.BaseDays / world.Config.DaysPerYear : 0;
            var demographic = record.Demographics.Single(d => d.Camp == camp.Id);
            var ending = Snapshot(world, radius);
            if (mode == "controlled-q0" && (camp.Population != population || camp.Technology == Technology.Cultivation))
                throw new InvalidOperationException("Controlled cohort changed population or reached Cultivation.");
            var landBalances = record.EcologyBalances.Where(b => radiusSet.Contains(b.Tile)).ToArray();
            double humanHarvest = record.Transfers.Where(t => radiusSet.Contains(t.Tile)
                && (t.Kind == "Gathering" || t.Kind == "Cultivation")).Sum(t => t.Food);
            writer.WriteLine(string.Join(",", new object[]
            {
                mode, site.Name, site.Seed, site.Tile, population, turn,
                world.Days / world.Config.DaysPerYear, startingPopulation, camp.Population,
                opening.G, opening.B, opening.MaxG, opening.MaxB,
                ending.G, ending.B, ending.MaxG, ending.MaxB,
                demographic.FoodProduced, demographic.FoodConsumed,
                startingPopulation * world.Config.Consumption * world.Config.GenerationIntervals,
                demographic.FinalReserves, camp.FoodNeedsMetRatio, camp.WaterNeedsMetRatio,
                demographic.Additions, demographic.Deaths, ending.Condition, ending.Q, ending.MaxQ,
                Math.Max(opening.LargestExcess, ending.LargestExcess), camp.Technology,
                opening.Q, opening.E, ending.E, ending.FieldTiles,
                opening.Q0MaxG, ending.Q0MaxG,
                humanHarvest, landBalances.Sum(b => b.BiomassHarvest),
                landBalances.Sum(b => b.FoodRecharge), landBalances.Sum(b => b.BiomassRecharge),
                landBalances.Sum(b => b.FoodOverflow), landBalances.Sum(b => b.BiomassOverflow),
                resolver.LastMetrics.ManagedRenewalInput, resolver.LastMetrics.OperatingFieldTiles,
                resolver.LastMetrics.OperatingFieldTiles == 0 ? 0 :
                    resolver.LastMetrics.OperatingFractionSum / resolver.LastMetrics.OperatingFieldTiles,
                openingReserves, reserveGoal, demographic.FoodReceived,
                demographic.ManufacturingFood, demographic.DiscardedInventory,
                Math.Max(0, demographic.FoodConsumed -
                    startingPopulation * world.Config.Consumption * world.Config.GenerationIntervals),
                assignedFarmerYears, assignedExpansionYears, requestedBridgeYears
            }.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))));
        }
        // Quiet tiles are latent. Project the whole map at the final boundary
        // once per case so capacity checks also cover unmaterialized land.
        Snapshot(world, world.Tiles.Where(t => t.Land).Select(t => t.Id));
    }

    private static Stocks Snapshot(WorldState world, IEnumerable<int> tiles)
    {
        double g = 0, b = 0, maxG = 0, maxB = 0, q0MaxG = 0;
        double condition = 0, q = 0, maxQ = 0, e = 0, excess = 0;
        int count = 0, fieldTiles = 0;
        foreach (int id in tiles)
        {
            var tile = world.Tiles[id];
            var view = LazyEcology.ProjectEcology(world, id, world.Days);
            int season = (int)Math.Floor(view.Days / world.Config.BaseDays) % 4;
            var capacity = new EcologyCapacity(world, tile, view, season);
            double largest = Math.Max(Math.Max(-view.Food, -view.PotentialFood),
                Math.Max(view.Food - view.PotentialFood,
                    Math.Max(view.Food - capacity.Food, view.PotentialFood - capacity.Biomass)));
            double tolerance = 1e-6 * Math.Max(1, capacity.Biomass);
            if (double.IsNaN(largest) || double.IsInfinity(largest) || largest > tolerance)
                throw new InvalidOperationException("Land stock exceeded its capacity at tile " + id +
                    " day " + world.Days + ": G=" + view.Food + " B=" + view.PotentialFood +
                    " maxG=" + capacity.Food + " maxB=" + capacity.Biomass);
            g += view.Food; b += view.PotentialFood;
            maxG += capacity.Food; maxB += capacity.Biomass;
            var noField = view; noField.CultivationCapital = 0;
            q0MaxG += new EcologyCapacity(world, tile, noField, season, 0).Food;
            condition += view.LandCondition;
            double tileQ = CultivationEconomy.Intensity(world.Config, view.CultivationCapital);
            q += tileQ; maxQ = Math.Max(maxQ, tileQ);
            e += view.CultivationCapital;
            if (view.CultivationCapital > 0) fieldTiles++;
            excess = Math.Max(excess, largest);
            count++;
        }
        return new Stocks(g, b, maxG, maxB, q0MaxG,
            count == 0 ? 0 : condition / count, count == 0 ? 0 : q / count, maxQ,
            count == 0 ? 0 : e / count, fieldTiles, excess);
    }
}
