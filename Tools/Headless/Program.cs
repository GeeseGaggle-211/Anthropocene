using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections;
using System.Linq;
using System.Reflection;
using Anthropocene.Simulation;

internal static class Program
{
    private sealed class HabitatPathStats
    {
        public int Tribes, Survivors, Husbandry;
        public double StartingPopulation, EndingPopulation, HuntFood, GatherFood;
    }
    private static int assertions;
    private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    private static WorldState Flat(int players = 2)
    {
        var cfg = SimulationConfig.Small(players); cfg.TurnDays = 90; cfg.BaseDays = 90; cfg.PracticeDays = 90; cfg.AnnualHumanGrowthRate = 0; cfg.Width = 24; cfg.Height = 16; cfg.ExperienceThreshold = 100000;
        var w = new WorldState { Config = cfg, Seed = 42, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            w.Tiles[i] = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, Food = 60, PotentialFood = 600, Freshwater = 60, FreshwaterSource = FreshwaterSource.RainPool };
            for (int s = 0; s < 4; s++) w.Tiles[i].Seasons[s] = new SeasonData { MaxFood = 80, MaxPotentialFood = 800, MaxFreshwater = 80, FoodRecoveryPerDay = (float)EcologyMath.DailyRate(.3, 91.25), BiomassRecoveryPerDay = (float)EcologyMath.DailyRate(.3, 91.25), WaterRecoveryPerDay = (float)EcologyMath.DailyRate(.3, 91.25), Moisture = .5f, Habitat = .5f };
        }
        for (int p = 0; p < players; p++) w.Camps.Add(new Camp { Id = w.NextId++, Player = p, Tile = 150 + p * 6, Population = 24, Food = 50 });
        Ownership.Bind(w); foreach (var c in w.Camps) c.People.Policy = new PolicyState(); return w;
    }
    private static Dictionary<int, List<Order>> Orders(params Order[] orders) => new Dictionary<int, List<Order>> { [0] = orders.ToList() };
    private static string StartingHabitat(WorldState world, Camp camp)
    {
        var tiles = world.Map.Radius(camp.Tile, 1).Select(id => world.Tiles[id]).Where(tile => tile.Land).ToArray();
        if (tiles.Any(tile => tile.River || tile.Oasis) || tiles.Count(tile => tile.Terrain == Terrain.Woodland || tile.Terrain == Terrain.Jungle) > tiles.Length / 2) return "River/woodland";
        var counts = new[]
        {
            new { Name = "Desert", Count = tiles.Count(tile => tile.Terrain == Terrain.Desert) },
            new { Name = "Grassland", Count = tiles.Count(tile => tile.Terrain == Terrain.Grassland) },
            new { Name = "Cold steppe", Count = tiles.Count(tile => tile.Terrain == Terrain.Tundra) }
        };
        return counts.OrderByDescending(item => item.Count).ThenBy(item => item.Name, StringComparer.Ordinal).First().Name;
    }
    private static int Percentile(IReadOnlyList<int> sorted, double fraction) => sorted.Count == 0 ? 0 : sorted[(int)Math.Round((sorted.Count - 1) * fraction)];
    private static void RunNomadStandardAudit(int games)
    {
        int totalNomads = 0, totalCultivation = 0, totalFishing = 0, totalTribes = 0;
        for (int seed = 1; seed <= games; seed++)
        {
            var config = new SimulationConfig { Players = 32 };
            var world = new WorldGenerator().Generate(seed, config);
            var resolver = new TurnResolver(); var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder();
            int illegal = 0;
            while (!world.Finished && world.Turn <= 1000)
            {
                var orders = new Dictionary<int, List<Order>>();
                var planningIndex = new PlanningIndex(world);
                foreach (var camp in world.Camps)
                {
                    var observed = builder.Build(world, camp.Player, planningIndex); var decision = policy.Decide(observed, camp.Mandate);
                    orders[camp.Player] = decision.Orders;
                    illegal += decision.Orders.Count(order => OrderRules.Validate(observed, order) != null);
                }
                resolver.Resolve(world, orders);
            }
            int nomads = world.Camps.Count(camp => camp.Technology == Technology.AnimalHusbandry);
            int cultivation = world.Camps.Count(camp => camp.Technology == Technology.Cultivation);
            int fishing = world.Camps.Count(camp => camp.Technology == Technology.Watercraft);
            var populations = world.Camps.Select(world.LinkedPopulation).OrderBy(population => population).ToArray();
            int survivors = populations.Count(population => population > 0);
            totalNomads += nomads; totalCultivation += cultivation; totalFishing += fishing; totalTribes += world.Camps.Count;
            Console.WriteLine("NOMAD_STANDARD seed=" + seed + " husbandry=" + nomads + " cultivation=" + cultivation + " fishing=" + fishing + " tribes=" + world.Camps.Count + " survivors=" + survivors + "/" + world.Camps.Count + " medianPopulation=" + Percentile(populations, .5) + " maxPopulation=" + populations.LastOrDefault() + " herds=" + world.Herds.Count(herd => herd.DomesticCamp < 0));
            Check(illegal == 0, "Standard nomad audit emitted illegal AI orders at seed " + seed);


            world = null; GC.Collect();
        }
        Console.WriteLine("NOMAD_STANDARD_TOTAL husbandry=" + totalNomads + " cultivation=" + totalCultivation + " fishing=" + totalFishing + " tribes=" + totalTribes);
        Check(new[] { totalFishing, totalCultivation, totalNomads }.Count(n => n > 0) > 1, "Standard aggregate should show variation; percentages are balance targets");
    }
    private static string FirstDifference(object left, object right, string path = "world")
    {
        if (ReferenceEquals(left, right)) return null;
        if (left == null || right == null) return path + ": " + (left ?? "null") + " != " + (right ?? "null");
        var type = left.GetType();
        if (type != right.GetType()) return path + ": " + type.Name + " != " + right.GetType().Name;
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            return left.Equals(right) ? null : path + ": " + left + " != " + right;
        if (left is IEnumerable leftItems && right is IEnumerable rightItems)
        {
            var a = leftItems.GetEnumerator(); var b = rightItems.GetEnumerator(); int index = 0;
            while (true)
            {
                bool hasA = a.MoveNext(), hasB = b.MoveNext();
                if (hasA != hasB) return path + ": collection lengths differ at " + index;
                if (!hasA) return null;
                string difference = FirstDifference(a.Current, b.Current, path + "[" + index + "]");
                if (difference != null) return difference;
                index++;
            }
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            if (type == typeof(WorldState) && field.Name == nameof(WorldState.Generation)) continue;
            string difference = FirstDifference(field.GetValue(left), field.GetValue(right), path + "." + field.Name);
            if (difference != null) return difference;
        }
        return null;
    }
    private static void Tests()
    {
        var map = new HexMap(24, 16);
        Check(map.Distance(map.At(0, 5), map.At(23, 5)) == 1, "Longitude wrap");
        Check(map.Radius(map.At(5, 5), 1).Count() == 7 && map.Radius(map.At(5, 5), 2).Count() == 19, "Hex radii");
        Check(map.At(1, -1) == -1, "No polar wrapping");
        var w = Flat(); var resolver = new TurnResolver(); var camp = w.Camps[0];
        resolver.Resolve(w, Orders(new Order(OrderKind.Dispatch, camp.Id, population: 8)));
        Check(w.LinkedPopulation(camp) == 24 && w.Hunters.Count == 1, "Dispatch conservation");
        resolver.Resolve(w, Orders(new Order(OrderKind.Reintegrate, w.Hunters[0].Id, camp.Id)));
        Check(w.Hunters.Count == 0 && camp.Population == 24, "Reintegration conservation");
        w = Flat(); camp = w.Camps[0]; var hunter = new Hunter { Id = w.NextId++, SourceCamp = camp.Id, SocietyId = camp.SocietyId, Player = 0, Tile = camp.Tile, Population = 8 }; camp.Population -= 8; w.Hunters.Add(hunter);
        int water = w.Map.Neighbors(camp.Tile).First(); w.Tiles[water].Terrain = Terrain.Shallows;
        Check(resolver.Validate(w, 0, new Order(OrderKind.Move, camp.Id, destination: water)) != null, "Camp cannot embark");
        Check(resolver.Validate(w, 0, new Order(OrderKind.Move, hunter.Id, destination: water)) != null, "Hunters cannot embark before breakthrough");
        var fishRecord = resolver.Resolve(w, Orders(new Order(OrderKind.Fish, hunter.Id, water)));
        Check(fishRecord.Transfers.Any(t => t.Kind == "Fishing" && t.Food > 0), "Fishing yields food");
        var tiles = new[] { new Tile { Food = 10, Freshwater = 5, FreshwaterSource = FreshwaterSource.RainPool } };
        var demands = new[] { new Demand { Tile = 0, Entity = 2, Food = 15, Freshwater = 8, Kind = "a" }, new Demand { Tile = 0, Entity = 1, Food = 5, Freshwater = 2, Kind = "a" } };
        var allocator = new ProportionalAllocation(); var a = allocator.Allocate(demands, tiles); var b = allocator.Allocate(demands.Reverse(), tiles);
        Check(a.Select(t => t.Food).SequenceEqual(b.Select(t => t.Food)) && Math.Abs(a.Sum(t => t.Food) - 10) < 1e-9, "Order independent allocation");
        tiles[0].PotentialFood = 100; demands = new[] { new Demand { Tile = 0, Entity = 1, Food = 10, Kind = "Gathering" }, new Demand { Tile = 0, Entity = 2, PotentialFood = 100, Kind = "Herd" } };
        a = allocator.Allocate(demands, tiles); Check(a.Sum(t => t.Food) == 10 && a.Sum(t => t.PotentialFood) == 90, "Human gathering uses its subset while herds draw on remaining potential biomass");
        w = Flat(); w.Camps[1].Tile = w.Camps[0].Tile + 2; w.Config.MinimumCampSpacing = 1; // isolates the same-tile rule from camp spacing
        int destination = w.Camps[0].Tile + 1;
        resolver.Resolve(w, new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.Move, w.Camps[0].Id, destination: destination) }, [1] = new List<Order> { new Order(OrderKind.Move, w.Camps[1].Id, destination: destination) } });
        Check(w.Camps[0].Tile == destination && w.Camps[1].Tile != destination, "Stable camp collision");
        w = Flat(); camp = w.Camps[0]; camp.Food = 0;
        foreach (var tile in w.Tiles) { tile.Food = 0; for (int s = 0; s < 4; s++) tile.Seasons[s].FoodRecoveryPerDay = 0; }
        resolver.Resolve(w, Orders()); resolver.Resolve(w, Orders()); Check(camp.Population == 24, "Starvation grace");
        resolver.Resolve(w, Orders()); Check(camp.Population < 24, "Starvation attrition");
        w = Flat(); camp = w.Camps[0]; camp.Experience = new double[] { 100, 100, 100 }; w.Turn = 12; w.Days = 240 * w.Config.DaysPerYear;
        TurnResolver.Breakthrough(w, camp, new TurnRecord());
        Check(camp.Technology == Technology.AnimalHusbandry && camp.ForcedTransition && camp.SecondaryDomain == 1, "Hard cap deterministic tie");
        Check(camp.Experience[1] == 100 && TurnResolver.NeolithicProgress(w, camp, Domain.Gathering) > 100, "Secondary experience preserved and boosted");
        w = Flat(); camp = w.Camps[0]; camp.Experience[1] = 120; w.Turn = 9;
        TurnResolver.Breakthrough(w, camp, new TurnRecord()); Check(camp.Technology == Technology.None, "No premature majority");
        w.Turn = 10; w.Days = 200 * w.Config.DaysPerYear; TurnResolver.Breakthrough(w, camp, new TurnRecord()); Check(camp.Technology == Technology.Cultivation, "Turn ten majority");
        w = Flat(); var before = Knowledge.Visible(w, 0); w.Camps[0].Tile += 4; var after = Knowledge.Visible(w, 0);
        Check(before.Except(after).Any() && after.Count == 19, "Present-tense fog");
        var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder();
        w = Flat(3); camp = w.Camps[1]; hunter = new Hunter { Id = w.NextId++, SourceCamp = camp.Id, SocietyId = camp.SocietyId, Player = camp.Player, Tile = camp.Tile, Population = 8 }; camp.Population -= 8; w.Hunters.Add(hunter);
        water = w.Map.Neighbors(camp.Tile).First(); w.Tiles[water].Terrain = Terrain.Shallows; w.Tiles[water].WaterBody = WaterKind.Saltwater; w.Tiles[water].Freshwater = 0; w.Tiles[water].FreshwaterSource = FreshwaterSource.None;
        var fishingDecision = policy.Decide(builder.Build(w, camp.Player), camp.Mandate);
        Check(fishingDecision.Orders.Any(order => order.Entity == hunter.Id && order.Kind == OrderKind.Fish), "Fishing-oriented AI commits an established group to a visible productive fishing tile");
        w = Flat(); var decision = policy.Decide(builder.Build(w, 0), w.Camps[0].Mandate);
        int hidden = Enumerable.Range(0, w.Tiles.Length).First(n => !Knowledge.Visible(w, 0).Contains(n));
        w.Tiles[hidden].Food = 99999; w.Camps[1].Food = 98765;
        var decision2 = policy.Decide(builder.Build(w, 0), w.Camps[0].Mandate);
        Check(decision.Orders.Select(o => o.ToString()).SequenceEqual(decision2.Orders.Select(o => o.ToString())), "AI hidden-state invariance");
        w = Flat(); var bytes = SaveCodec.Save(w); var loaded = SaveCodec.Load(bytes);
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "Save round trip");
        resolver.Resolve(w, Orders()); resolver.Resolve(loaded, Orders()); Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "Replay from save");
        w = Flat(); w.Config.StartingReserveGenerations = w.Config.HunterCarryGenerations = w.Config.MinimumStorageGenerations = w.Config.MaximumStorageGenerations = w.Config.HunterForagePerPerson = w.Config.HuntBreedingReserveFraction = w.Config.SustainableHerdGrowthRetention = 0; w.Config.SustainableHuntPopulation = 0; w.Config.ActiveFishingPracticeMultiplier = w.Config.GrasslandHerdDensityMultiplier = w.Config.SteppeHerdDensityMultiplier = 0;
        foreach (var species in w.Config.Species) species.MaximumHerdSize = 32;
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = w.Camps[0].Tile + 1, Species = "antelope", Animals = 30, Capacity = 32 });
        loaded = SaveCodec.Load(SaveCodec.Save(w));
        Check(loaded.Config.StartingReserveGenerations == 1 && loaded.Config.HunterCarryGenerations == 2.5 && loaded.Config.MinimumStorageGenerations == 1 && loaded.Config.MaximumStorageGenerations == 4 && loaded.Config.HunterForagePerPerson == 1 && loaded.Config.HuntBreedingReserveFraction == .2 && loaded.Config.SustainableHuntPopulation == 16 && loaded.Config.SustainableHerdGrowthRetention == .25 && loaded.Config.ActiveFishingPracticeMultiplier == 4.25 && loaded.Config.GrasslandHerdDensityMultiplier == 1.35 && loaded.Config.SteppeHerdDensityMultiplier == 1.15, "Missing tuning values gain current logistics, practice and herd-density defaults");
        Check(loaded.Config.GetSpecies("antelope").MaximumHerdSize == 150 && loaded.Herds[0].Capacity == 150, "Legacy saves gain species herd thresholds");
        w = Flat(); w.Config.SustainableHuntPopulation = 8; w.Config.HuntingFoodYieldScale = .25; camp = w.Camps[0]; camp.Food = 0;
        hunter = new Hunter { Id = w.NextId++, Tile = camp.Tile, Population = 12, Player = 0, SourceCamp = camp.Id, SocietyId = camp.SocietyId }; w.Hunters.Add(hunter); camp.Population -= 12;
        var prey = new Herd { Id = w.NextId++, Tile = camp.Tile + 1, Animals = 8 }; w.Herds.Add(prey);
        int fleeing = TurnResolver.HerdDestination(w, prey);
        Check(w.Map.Distance(fleeing, hunter.Tile) == w.Map.Radius(prey.Tile, 1).Max(n => w.Map.Distance(n, hunter.Tile)), "Herd maximizes escape distance");
        var huntRecord = resolver.Resolve(w, Orders(new Order(OrderKind.Move, hunter.Id, destination: fleeing)));
        Check(huntRecord.Transfers.Any(t => t.Kind == "Hunting" && t.Food > 0), "Movement into a herd's resolved tile triggers automatic hunting");
        Check(huntRecord.Transfers.Where(t => t.Entity == prey.Id && t.Kind == "Herd").Sum(t=>t.PotentialFood) > 0 && huntRecord.Transfers.Where(t => t.Entity == prey.Id && t.Kind == "Herd").Sum(t=>t.Freshwater) > 0, "Surviving herd consumes potential food and freshwater");
        w = Flat(); camp = w.Camps[0]; hunter = new Hunter { Id = w.NextId++, Tile = camp.Tile, Population = 8, Player = 0, SourceCamp = camp.Id, SocietyId = camp.SocietyId }; w.Hunters.Add(hunter); camp.Population -= 8;
        prey = new Herd { Id = w.NextId++, Tile = camp.Tile + 1, Animals = 8 }; w.Herds.Add(prey);
        huntRecord = resolver.Resolve(w, Orders(new Order(OrderKind.Move, hunter.Id, destination: hunter.Tile + 1)));
        Check(prey.Animals >= 8 && camp.Experience[0] == 0 && !huntRecord.Transfers.Any(t => t.Kind == "Hunting"), "Movement without herd contact causes neither kills nor hunting progress");
        w = Flat(); loaded = SaveCodec.Load(SaveCodec.Save(w)); loaded.Config.TurnDays = 45;
        resolver.Resolve(w, Orders()); resolver.Resolve(loaded, Orders());
        Check(loaded.Camps[0].LastConsumed < w.Camps[0].LastConsumed && loaded.History[0].Transfers.Where(t => t.Kind == "Gathering").Sum(t => t.Food) < w.History[0].Transfers.Where(t => t.Kind == "Gathering").Sum(t => t.Food), "Duration scales consumption and extraction");
        w = Flat(3); var inertia = policy.Decide(builder.Build(w, 2), w.Camps[2].Mandate);
        Check(inertia.Orders[0].Kind == OrderKind.NoOp, "Equivalent tiles favor camp continuity");
        foreach (int n in w.Map.Radius(w.Camps[2].Tile, 1)) { w.Tiles[n].Food = 0; for (int s = 0; s < 4; s++) w.Tiles[n].Seasons[s].FoodRecoveryPerDay = 0; }
        inertia = policy.Decide(builder.Build(w, 2), w.Camps[2].Mandate);
        Check(inertia.Orders[0].Kind == OrderKind.Move, "Depletion overcomes relocation inertia");
        w = Flat(); w.Camps[0].Experience = new double[] { 2, 7, 1 }; w.Turn = 12; w.Days = 240 * w.Config.DaysPerYear;
        TurnResolver.Breakthrough(w, w.Camps[0], new TurnRecord());
        Check(w.Camps[0].ForcedTransition, "Turn twelve grants carryover even if a majority only appears at the cap");
        w = Flat(); foreach (var tile in w.Tiles) tile.Seasons[1].MaxFood = 4;
        resolver.Resolve(w, Orders()); Check(w.Tiles.All(t => LazyEcology.ProjectEcology(w, t.Id, w.Days).Food <= 4), "Projected resources respect the next season's carrying capacity without materializing quiet tiles");
        Console.WriteLine("PASS: " + assertions + " focused assertions");
    }
    private static void Issue31Microbench()
    {
        foreach (int societies in new[] { 80, 320 })
        {
            var world = new WorldState { Config = SimulationConfig.Small(1) };
            for (int i = 0; i < societies; i++)
            {
                var owner = new Society { Id = i + 1, Player = i, Technology = Technology.AnimalHusbandry,
                    Progression = new EraProgression { Era = Era.Neolithic }, Experience = new[] { 225.0, 224.0, 0.0 } };
                owner.Technologies[(int)Technology.AnimalHusbandry] = true;
                world.Societies.Add(owner);
                world.Camps.Add(new Camp { Id = 1000 + i, SocietyId = owner.Id, Player = i, Population = 24 });
            }
            var record = new TurnRecord();
            double checksum = 0;
            void Run(int repetitions)
            {
                for (int iteration = 0; iteration < repetitions; iteration++)
                {
                    TechnologyDiscovery.Resolve(world, record);
                    foreach (var owner in world.Societies)
                        checksum += TechnologyDiscovery.Statuses(world.Config, owner)[(int)Domain.Gathering].Progress;
                }
            }
            Run(100);
            const int measuredRepetitions = 2000;
            var milliseconds = new double[5]; var bytes = new long[5];
            for (int sample = 0; sample < 5; sample++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                Run(measuredRepetitions);
                milliseconds[sample] = timer.Elapsed.TotalMilliseconds / measuredRepetitions;
                bytes[sample] = (GC.GetAllocatedBytesForCurrentThread() - beforeBytes) / measuredRepetitions;
            }
            Array.Sort(milliseconds); Array.Sort(bytes);
            Console.WriteLine("ISSUE31_MICROBENCH societies=" + societies + " close+ownStatuses_ms=" + milliseconds[2].ToString("F6", System.Globalization.CultureInfo.InvariantCulture) +
                " allocatedBytes=" + bytes[2] + " repetitions=" + measuredRepetitions + " samples=5 checksum=" + checksum.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
    private static int Main(string[] args)
    {
        if (args.Contains("--issue31-microbench")) { Issue31Microbench(); return 0; }
        if (args.Contains("--foundation-only")) { Console.WriteLine("PASS: " + FoundationChecks.Run() + " foundation assertions"); return 0; }
        if (args.Contains("--issue25-ecology-only")) { Issue25EcologyChecks.Run(args); return 0; }
        if (args.Contains("--issue27-ecology-only")) { Issue27EcologyChecks.Run(); return 0; }
        if (args.Contains("--issue26-forecast-only")) { Issue26ForecastChecks.Run(); return 0; }
        if (args.Contains("--issue28-food-timebase-only")) { Issue28FoodTimebaseChecks.Run(); return 0; }
        var tradeProbe = args.FirstOrDefault(a => a.StartsWith("--trade-probe=", StringComparison.Ordinal));
        var tradeResolve = args.FirstOrDefault(a => a.StartsWith("--trade-resolve=", StringComparison.Ordinal));
        var tradeProfile = args.FirstOrDefault(a => a.StartsWith("--trade-profile=", StringComparison.Ordinal));
        var tradeRoles = args.FirstOrDefault(a => a.StartsWith("--trade-roles=", StringComparison.Ordinal));
        if (tradeRoles != null) { TradeChecks.RolesProbe(tradeRoles.Substring("--trade-roles=".Length)); return 0; }
        if (tradeProfile != null) { TradeChecks.ProfileProbe(tradeProfile.Substring("--trade-profile=".Length)); return 0; }
        if (tradeResolve != null) { var parts = tradeResolve.Substring("--trade-resolve=".Length).Split(','); TradeChecks.ResolveProbe(parts[0], int.Parse(parts[1])); return 0; }
        if (tradeProbe != null) { TradeChecks.Probe(tradeProbe.Substring("--trade-probe=".Length)); return 0; }
        if (args.Contains("--issue32-capability-only")) { Issue32CapabilityChecks.Run(); return 0; }
        if (args.Contains("--cultivation-cache-only")) { CultivationCacheChecks.Run(); return 0; }
        if (args.Contains("--horizon-only")) { HorizonEcologyChecks.Run(); HorizonCultivationChecks.Run(); HorizonExecutionChecks.Run(); return 0; }
        if (args.Contains("--land-food-only")) { AquaticChecks.Run(); HorizonEcologyChecks.Run(); Issue27EcologyChecks.Run(); return 0; }
        if (args.Contains("--neolithic-only")) { NeolithicChecks.Run(); ToolConsumptionChecks.Run(); FarmToolChecks.Run(); FarmPlannerChecks.Run(); FarmFoodPhaseChecks.Run(); CultivationCacheChecks.Run(); HorizonCultivationChecks.Run(); HorizonEcologyChecks.Run(); HorizonExecutionChecks.Run(); HusbandryChecks.Run(); PastoralCraftChecks.Run(); Issue25EcologyChecks.Run(args); Issue27EcologyChecks.Run(); Issue26ForecastChecks.Run(); Issue28FoodTimebaseChecks.Run(); Issue32CapabilityChecks.Run(); return 0; }
        if (args.Contains("--economy-only")) { Milestone2Checks.Run(); Milestone3Checks.Run(); EcologyChecks.Run(); AgentContractChecks.Run(); ExecutionChecks.Run(); NeolithicChecks.Run(); ToolConsumptionChecks.Run(); FarmToolChecks.Run(); FarmPlannerChecks.Run(); FarmFoodPhaseChecks.Run(); CultivationCacheChecks.Run(); HorizonCultivationChecks.Run(); HorizonEcologyChecks.Run(); HorizonExecutionChecks.Run(); HusbandryChecks.Run(); PastoralCraftChecks.Run(); Issue25EcologyChecks.Run(args); Issue27EcologyChecks.Run(); Issue26ForecastChecks.Run(); Issue28FoodTimebaseChecks.Run(); Issue32CapabilityChecks.Run(); return 0; }
        if (args.Contains("--pastoral-craft-only")) { PastoralCraftChecks.Run(); return 0; }
        if (args.Contains("--husbandry-only")) { HusbandryChecks.Run(); return 0; }
        if (args.Contains("--farm-food-phase")) { FarmFoodPhaseChecks.Run(); return 0; }
        try
        {
            var scaleOnly = args.FirstOrDefault(a => a.StartsWith("--scale-only=", StringComparison.Ordinal));
            if (scaleOnly != null) { ExecutionChecks.Scale(int.Parse(scaleOnly.Substring(13))); return 0; }
            if (args.Contains("--write-config"))
            {
                System.IO.File.WriteAllText("Assets/_Anthropocene/Resources/OpeningConfig.json", System.Text.Json.JsonSerializer.Serialize(new SimulationConfig(), new System.Text.Json.JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
                return 0;
            }
            if (args.Contains("--geology-only")) { GeologyGenerationChecks.Run(); return 0; }
            var jobsArgument = args.FirstOrDefault(a => a.StartsWith("--jobs=", StringComparison.Ordinal));
            int jobs = jobsArgument != null ? int.Parse(jobsArgument.Substring(7)) : ParallelChecks.DefaultJobs;
            var fixedChecks = new[]
            {
                new ParallelChecks.Entry { Name = "Program.Tests", Run = () => Tests() },
                new ParallelChecks.Entry { Name = "Milestone2Checks", Run = () => Milestone2Checks.Run() },
                new ParallelChecks.Entry { Name = "Milestone3Checks", Run = () => Milestone3Checks.Run() },
                new ParallelChecks.Entry { Name = "FoundationChecks", Run = () => Console.WriteLine("PASS: " + FoundationChecks.Run() + " foundation assertions") },
                new ParallelChecks.Entry { Name = "CropAgricultureChecks", Run = () => CropAgricultureChecks.Run() },
                new ParallelChecks.Entry { Name = "ClimateMoistureChecks", Run = () => ClimateMoistureChecks.Run() },
                new ParallelChecks.Entry { Name = "ClimateGenerationChecks", Cost = 1.5, Run = () => ClimateGenerationChecks.Run() },
                new ParallelChecks.Entry { Name = "WorldMorphologyChecks", Cost = 7, Run = () => WorldMorphologyChecks.Run() },
                new ParallelChecks.Entry { Name = "ArchipelagoChecks", Cost = 10, Run = () => ArchipelagoChecks.Run() },
                new ParallelChecks.Entry { Name = "GeologyGenerationChecks", Cost = 7, Run = () => GeologyGenerationChecks.Run() },
                new ParallelChecks.Entry { Name = "AquaticChecks", Run = () => AquaticChecks.Run() },
                new ParallelChecks.Entry { Name = "StartViabilityChecks", Run = () => StartViabilityChecks.Run() },
                new ParallelChecks.Entry { Name = "ScenarioChecks", Cost = 32, Run = () => ScenarioChecks.Run() },
                new ParallelChecks.Entry { Name = "EcologyChecks", Run = () => EcologyChecks.Run() },
                new ParallelChecks.Entry { Name = "AgentContractChecks", Run = () => AgentContractChecks.Run() },
                new ParallelChecks.Entry { Name = "ExecutionChecks", Cost = 5, Run = () => ExecutionChecks.Run() },
                new ParallelChecks.Entry { Name = "PlanningDraftChecks", Cost = 5, Run = () => PlanningDraftChecks.Run() },
                new ParallelChecks.Entry { Name = "CampSpacingChecks", Run = () => CampSpacingChecks.Run() },
                new ParallelChecks.Entry { Name = "ContactKnowledgeChecks", Run = () => ContactKnowledgeChecks.Run() },
                new ParallelChecks.Entry { Name = "FissionChecks", Cost = 3, Run = () => FissionChecks.Run() },
                new ParallelChecks.Entry { Name = "ImmigrationChecks", Cost = 2, Run = () => ImmigrationChecks.Run() },
                new ParallelChecks.Entry { Name = "TradeChecks", Cost = 2, Run = () => TradeChecks.Run() },
                new ParallelChecks.Entry { Name = "GoodsStorageChecks", Cost = 2, Run = () => GoodsStorageChecks.Run() },
                new ParallelChecks.Entry { Name = "DemandControlChecks", Cost = 2, Run = () => DemandControlChecks.Run() },
                new ParallelChecks.Entry { Name = "ClothingFeedstockChecks", Run = () => ClothingFeedstockChecks.Run() },
                new ParallelChecks.Entry { Name = "NeolithicChecks", Run = () => NeolithicChecks.Run() },
                new ParallelChecks.Entry { Name = "ToolConsumptionChecks", Run = () => ToolConsumptionChecks.Run() },
                new ParallelChecks.Entry { Name = "FarmToolChecks", Run = () => FarmToolChecks.Run() },
                new ParallelChecks.Entry { Name = "FarmPlannerChecks", Cost = 1.2, Run = () => FarmPlannerChecks.Run() },
                new ParallelChecks.Entry { Name = "FarmFoodPhaseChecks", Run = () => FarmFoodPhaseChecks.Run() },
                new ParallelChecks.Entry { Name = "CultivationCacheChecks", Run = () => CultivationCacheChecks.Run() },
                new ParallelChecks.Entry { Name = "HorizonCultivationChecks", Run = () => HorizonCultivationChecks.Run() },
                new ParallelChecks.Entry { Name = "HorizonEcologyChecks", Run = () => HorizonEcologyChecks.Run() },
                new ParallelChecks.Entry { Name = "HorizonExecutionChecks", Run = () => HorizonExecutionChecks.Run() },
                new ParallelChecks.Entry { Name = "HusbandryChecks", Run = () => HusbandryChecks.Run() },
                new ParallelChecks.Entry { Name = "PastoralCraftChecks", Run = () => PastoralCraftChecks.Run() },
                new ParallelChecks.Entry { Name = "HousingMaterialRecipeChecks", Run = () => HousingMaterialRecipeChecks.Run() },
                new ParallelChecks.Entry { Name = "Issue25EcologyChecks", Run = () => Issue25EcologyChecks.Run(args) },
                new ParallelChecks.Entry { Name = "Issue27EcologyChecks", Run = () => Issue27EcologyChecks.Run() },
                new ParallelChecks.Entry { Name = "Issue26ForecastChecks", Run = () => Issue26ForecastChecks.Run() },
                new ParallelChecks.Entry { Name = "Issue28FoodTimebaseChecks", Run = () => Issue28FoodTimebaseChecks.Run() },
                new ParallelChecks.Entry { Name = "Issue32CapabilityChecks", Run = () => Issue32CapabilityChecks.Run() },
                new ParallelChecks.Entry { Name = "QoLCohesionChecks", Run = () => QoLCohesionChecks.Run() },
                new ParallelChecks.Entry { Name = "QoLMortalityChecks", Run = () => QoLMortalityChecks.Run() },
                new ParallelChecks.Entry { Name = "DemographicStructureChecks", Run = () => DemographicStructureChecks.Run() },
                new ParallelChecks.Entry { Name = "HerdLabourChecks", Cost = 2, Run = () => HerdLabourChecks.Run() },
                new ParallelChecks.Entry { Name = "SharedAllocationChecks", Cost = 2, Run = () => SharedAllocationChecks.Run() },
                new ParallelChecks.Entry { Name = "ValueFulfilmentChecks", Cost = 4, Run = () => ValueFulfilmentChecks.Run() },
                new ParallelChecks.Entry { Name = "ExpansionClaimChecks", Cost = 2, Run = () => ExpansionClaimChecks.Run() },
                new ParallelChecks.Entry { Name = "ProductionOrderChecks", Cost = 3, Run = () => ProductionOrderChecks.Run() },
                new ParallelChecks.Entry { Name = "WP4aValueChecks", Cost = 3, Run = () => WP4aValueChecks.Run() },
                new ParallelChecks.Entry { Name = "SettlingChecks", Cost = 2, Run = () => SettlingChecks.Run() },
            };
            bool filtered = ParallelChecks.Filter(args, ref fixedChecks);
            ParallelChecks.Run(fixedChecks, jobs, args.Contains("--check-timings"));
            if (filtered) return 0;
            int seeds = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? int.Parse(args[0]) : 20;
            var scaleArgument = args.FirstOrDefault(a => a.StartsWith("--scale=")); if (scaleArgument != null) { ExecutionChecks.Scale(int.Parse(scaleArgument.Substring(8))); return 0; }
            if (args.Contains("--nomad-standard")) { RunNomadStandardAudit(seeds); return 0; }
            int survivors = 0, total = 0, starvation = 0, growth = 0, illegal = 0;
            var habitatPaths = new Dictionary<string, HabitatPathStats>(StringComparer.Ordinal);
            int aridRiverCorridors = 0;
            var endingPopulations = new List<int>(); int growthResponseOpportunities = 0, growthResponses = 0, dispatchResponses = 0, additionalDispatchResponses = 0, gatheringResponses = 0;
            int huntPursuits = 0, successfulHuntPursuits = 0;
            if (args.Contains("--find-ui3"))
            {
                var config = new SimulationConfig { Players = 32 }; var preview = new WorldGenerator().Generate(1729, config); var camp = preview.Camps[0];
                var resolver = new TurnResolver(); var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder();
                var orders = preview.Camps.Where(c => c.Player != 0).ToDictionary(c => c.Player, c => policy.Decide(builder.Build(preview, c.Player), c.Mandate).Orders);
                orders[0] = new List<Order> { new Order(OrderKind.Dispatch, camp.Id, population: 8) }; resolver.Resolve(preview, orders);
                var report = preview.History.Last().Demographics.First(d => d.Camp == camp.Id); var hunter = preview.Hunters.First(h => h.Player == 0);
                Check(report.Additions > 0 && report.Deaths == 0 && hunter.Population == 8, "Standard interactive demonstration grows and preserves the dispatched group");
                Console.WriteLine("DEMO Standard seed=1729 tribes=32 dispatch=8 people=" + report.StartingPopulation + "+" + report.Additions + "-" + report.Deaths + "=" + report.EndingPopulation + " reserve=" + (camp.Food / Math.Max(1, preview.LinkedPopulation(camp) * config.Consumption * config.GenerationIntervals)).ToString("F2") + " storage=" + TurnResolver.StorageGenerations(preview, camp.Tile).ToString("F2")); return 0;
            }
            if (args.Contains("--audit3"))
            {
                for (int seed = 1; seed <= 8; seed++)
                {
                    var generated = new WorldGenerator().Generate(seed, new SimulationConfig { Players = 80 }); var report = generated.Generation;
                    Check(report.ElevationCounts.All(count => count > 0), "Every intended elevation level appears in Standard world " + seed);
                    Check(generated.Camps.All(c => generated.Herds.Count(h => generated.Map.Reachable(c.Tile, h.Tile, 2, n => generated.Tiles[n].TraversableLand)) >= generated.Config.MinimumOpeningHerds), "Every start has the configured minimum reachable herds around peaks");
                    Check(generated.Camps.Count == 80 && generated.Herds.All(h => generated.Tiles[h.Tile].ElevationLevel < 10) && generated.Camps.All(c => generated.Tiles[c.Tile].ElevationLevel < 10), "Eighty camps spawn without using peaks");
                    Check(generated.Camps.All(c => PlacementStage.Viability(generated, c.Tile) >= generated.Config.StartViability), "Every Standard start meets seasonal recurring-food and freshwater eligibility");
                    var copy = SaveCodec.Load(SaveCodec.Save(generated));
                    Check(copy.Tiles.Select(t => t.ElevationLevel).SequenceEqual(generated.Tiles.Select(t => t.ElevationLevel)), "All elevation levels survive Standard save/load");
                    Console.WriteLine("ELEVATION seed=" + seed + " levels=" + string.Join(",", report.ElevationCounts) + " ridgeTiles=" + generated.Tiles.Count(t => t.RidgeMask != 0) + " herds=" + report.WildHerds + " starts=" + report.CampsWithVisibleHerds + " oases=" + report.OasisCount + " riverEdges=" + report.RiverEdges);
                    generated = null; copy = null; GC.Collect();
                }
                return 0;
            }
            if (args.Contains("--find-ui"))
            {
                var found = new HashSet<string>();
                for (int seed = 1; seed <= 300 && found.Count < 3; seed++)
                {
                    var preview = new WorldGenerator().Generate(seed, SimulationConfig.Small(6));
                    var visible = Knowledge.Visible(preview, 0);
                    foreach (var feature in new[] { "oasis", "river", "saltwater" })
                        if (!found.Contains(feature) && visible.Any(n => feature == "oasis" ? preview.Tiles[n].Oasis : feature == "river" ? preview.Tiles[n].River : preview.Tiles[n].WaterBody == WaterKind.Saltwater))
                        { found.Add(feature); Console.WriteLine("UI Small seed=" + seed + " feature=" + feature); }
                }
                return 0;
            }
            if (args.Contains("--audit2"))
            {
                for (int seed = 1; seed <= 8; seed++)
                {
                    var baseline = new WorldGenerator().Generate(seed, new SimulationConfig()); var map = baseline.Map;
                    var camp = baseline.Camps.First(c => c.Player == 0);
                    int seen = baseline.Herds.Count(h => map.Distance(camp.Tile, h.Tile) <= 2);
                    int nearest = baseline.Herds.Count == 0 ? -1 : baseline.Herds.Min(h => map.Distance(camp.Tile, h.Tile));
                    int badCoasts = baseline.Tiles.Count(t => t.Terrain == Terrain.Ocean && map.Neighbors(t.Id).Any(n => baseline.Tiles[n].Land));
                    var report = baseline.Generation;
                    int arid = baseline.Tiles.Count(t => t.Terrain == Terrain.Desert), oasisHabitat = baseline.Tiles.Count(t => t.Terrain == Terrain.Desert && t.OasisInfluence > 0);
                    int aridCamels = baseline.Herds.Count(h => h.Species == "camel" && baseline.Tiles[h.Tile].Terrain == Terrain.Desert);
                    int horses = baseline.Herds.Count(h => h.Species == "horse"), grassHorses = baseline.Herds.Count(h => h.Species == "horse" && baseline.Tiles[h.Tile].Terrain == Terrain.Grassland);
                    Console.WriteLine("MILESTONE2 seed=" + seed + " herds=" + baseline.Herds.Count + " visible=" + seen + " nearest=" + nearest + " suppliedStarts=" + report.CampsWithVisibleHerds + "/" + baseline.Camps.Count + " relief=" + string.Join(",", report.ReliefCounts) + " riverEdges=" + report.RiverEdges + " riverTiles=" + report.Rivers + " oases=" + report.OasisCount + " camels=" + report.CamelHerds + " camelsNearOases=" + report.CamelsNearOases + " freshwaterTiles=" + report.FreshwaterTiles + " saltwaterTiles=" + report.SaltwaterTiles + " coastViolations=" + badCoasts);
                    Console.WriteLine("HABITAT seed=" + seed + " aridTiles=" + arid + " oasisAridTiles=" + oasisHabitat + " aridCamels=" + aridCamels + " horses=" + horses + " grassHorses=" + grassHorses);
                    Check(report.CampsWithVisibleHerds == baseline.Camps.Count, "Every Standard opening has a reachable herd");
                    Check(badCoasts == 0 && report.OasisCount > 0 && report.RiverEdges > 0, "Standard geographic feature and coast invariants");
                    Check(report.CamelsNearOases / (double)Math.Max(1, report.CamelHerds) > oasisHabitat / (double)Math.Max(1, arid), "Camel distribution favors scarce oasis habitat");
                    baseline = null; GC.Collect();
                }
                return 0;
            }
            var technologies = new int[4]; var timer = Stopwatch.StartNew();
            for (int seed = 1; seed <= seeds; seed++)
            {
                var config = SimulationConfig.Small(); var w = new WorldGenerator().Generate(seed, config);
                int startingWildHerds = w.Herds.Count(herd => herd.DomesticCamp < 0);
                var startingHabitats = w.Camps.ToDictionary(camp => camp.Id, camp => StartingHabitat(w, camp));
                var startingPopulations = w.Camps.ToDictionary(camp => camp.Id, camp => (double)w.LinkedPopulation(camp));
                aridRiverCorridors += w.Tiles.Count(tile => tile.River && w.Map.Neighbors(tile.Id).Any(neighbor => w.Tiles[neighbor].Terrain == Terrain.Desert));
                var replay = SaveCodec.Load(SaveCodec.Save(w));
                var resolver = new TurnResolver(); var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder();
                var previousPopulation = new Dictionary<int, int>(); var previousGathering = new Dictionary<int, double>();
                while (!w.Finished && w.Turn <= 1000)
                {
                    var orders = new Dictionary<int, List<Order>>(); var traces = new List<DecisionTrace>();
                    var planningIndex = new PlanningIndex(w);
                    var hunterGroupsBefore = w.Camps.ToDictionary(camp => camp.Player, camp => w.Hunters.Count(h => h.SourceCamp == camp.Id));
                    foreach (var camp in w.Camps)
                    {
                        var observed = builder.Build(w, camp.Player, planningIndex); var decision = policy.Decide(observed, camp.Mandate); orders[camp.Player] = decision.Orders; traces.Add(decision.Trace);
                        foreach (var order in decision.Orders) if (OrderRules.Validate(observed, order) != null) illegal++;
                    }
                    var pursuits = new List<int>();
                    foreach (var hunter in w.Hunters.OrderBy(h => h.Id))
                    {
                        var order = orders[hunter.Player].FirstOrDefault(candidate => candidate.Entity == hunter.Id);
                        int destination = order != null && order.Destination >= 0 ? order.Destination : hunter.Tile;
                        var visible = Knowledge.Visible(w, hunter.Player);
                        if (w.Herds.Any(herd => herd.DomesticCamp < 0 && visible.Contains(herd.Tile) && w.Map.Distance(herd.Tile, destination) <= 1)) pursuits.Add(hunter.Id);
                    }
                    var record = resolver.Resolve(w, orders, traces); starvation += record.Deaths; growth += record.Growth;
                    huntPursuits += pursuits.Count;
                    successfulHuntPursuits += pursuits.Count(id => record.Transfers.Any(transfer => transfer.Entity == id && transfer.Kind == "Hunting" && transfer.Food > 0));
                    foreach (var report in record.Demographics)
                    {
                        var camp = w.Camps.First(c => c.Id == report.Camp);
                        double gathering = report.TerrestrialFood + report.MarineFood + report.LakeFood;
                        if (previousPopulation.TryGetValue(camp.Id, out int priorPopulation) && report.StartingPopulation > priorPopulation)
                        {
                            growthResponseOpportunities++;
                            bool dispatched = orders[camp.Player].Any(order => order.Kind == OrderKind.Dispatch);
                            bool gatheredMore = previousGathering.TryGetValue(camp.Id, out double priorGathering) && gathering > priorGathering + 1e-6;
                            if (dispatched) { dispatchResponses++; if (hunterGroupsBefore[camp.Player] > 0) additionalDispatchResponses++; }
                            if (gatheredMore) gatheringResponses++;
                            if (dispatched || gatheredMore) growthResponses++;
                        }
                        previousPopulation[camp.Id] = report.StartingPopulation; previousGathering[camp.Id] = gathering;
                    }
                    // Reverse entity storage and order insertion to test collection-order independence.
                    replay.Camps.Reverse(); replay.Hunters.Reverse(); replay.Herds.Reverse();
                    resolver.Resolve(replay, orders.Reverse().ToDictionary(p => p.Key, p => p.Value.AsEnumerable().Reverse().ToList()), traces);
                    string fingerprint = SaveCodec.Fingerprint(w), replayFingerprint = SaveCodec.Fingerprint(replay);
                    if (fingerprint != replayFingerprint)
                    {
                        var canonical = new WorldState { Version = w.Version, Seed = w.Seed, Turn = w.Turn, NextId = w.NextId, Days = w.Days, Config = w.Config, Tiles = w.Tiles, Camps = w.Camps.OrderBy(c => c.Id).ToList(), Hunters = w.Hunters.OrderBy(h => h.Id).ToList(), Herds = w.Herds.OrderBy(h => h.Id).ToList(), Rivers = w.Rivers, History = w.History };
                        var canonicalReplay = new WorldState { Version = replay.Version, Seed = replay.Seed, Turn = replay.Turn, NextId = replay.NextId, Days = replay.Days, Config = replay.Config, Tiles = replay.Tiles, Camps = replay.Camps.OrderBy(c => c.Id).ToList(), Hunters = replay.Hunters.OrderBy(h => h.Id).ToList(), Herds = replay.Herds.OrderBy(h => h.Id).ToList(), Rivers = replay.Rivers, History = replay.History };
                        Console.Error.WriteLine("FIRST DIFFERENCE: " + FirstDifference(canonical, canonicalReplay));
                    }
                    Check(fingerprint == replayFingerprint, "Deterministic replay diverged at seed " + seed + " turn " + (w.Turn - 1));
                }
                Check(w.Herds.Count(herd => herd.DomesticCamp < 0) <= startingWildHerds, "Wild herd entities proliferated while fission was disabled at seed " + seed);
                if (args.Contains("--nomad-audit"))
                {
                    if (args.Contains("--nomad-detail"))
                        foreach (var audited in w.Camps.OrderBy(c => c.Player))
                            Console.WriteLine("NOMAD seed=" + seed + " tribe=" + audited.Player + " tech=" + audited.Technology + " turn=" + audited.TransitionTurn + " xp=" + string.Join("/", audited.Experience.Select(x => x.ToString("F1"))) + " mandate=" + audited.Mandate.Hunting.ToString("F2") + "/" + audited.Mandate.Gathering.ToString("F2") + "/" + audited.Mandate.Fishing.ToString("F2") + " population=" + w.LinkedPopulation(audited) + " moves=" + audited.Moves + " hunters=" + w.Hunters.Count(h => h.SourceCamp == audited.Id) + " huntFood=" + w.History.SelectMany(r => r.Demographics).Where(d => d.Camp == audited.Id).Sum(d => d.HuntFood).ToString("F0") + " gatherFood=" + w.History.SelectMany(r => r.Demographics).Where(d => d.Camp == audited.Id).Sum(d => d.TerrestrialFood + d.MarineFood + d.LakeFood).ToString("F0") + " huntByTurn=" + string.Join(",", w.History.Select(r => r.Demographics.First(d => d.Camp == audited.Id).HuntFood.ToString("F0"))));
                    int nomads = w.Camps.Count(c => c.Technology == Technology.AnimalHusbandry);
                    Console.WriteLine("NOMAD_GAME seed=" + seed + " husbandry=" + nomads + "/" + w.Camps.Count);

                }
                foreach (var camp in w.Camps)
                {
                    total++; if (w.LinkedPopulation(camp) > 0) survivors++; technologies[(int)camp.Technology]++;
                    endingPopulations.Add(w.LinkedPopulation(camp));
                    string habitat = startingHabitats[camp.Id];
                    if (!habitatPaths.TryGetValue(habitat, out var path)) habitatPaths[habitat] = path = new HabitatPathStats();
                    path.Tribes++; path.StartingPopulation += startingPopulations[camp.Id]; path.EndingPopulation += w.LinkedPopulation(camp);
                    if (w.LinkedPopulation(camp) > 0) path.Survivors++;
                    if (camp.Technology == Technology.AnimalHusbandry) path.Husbandry++;
                    foreach (var demographic in w.History.SelectMany(record => record.Demographics).Where(report => report.Camp == camp.Id))
                    { path.HuntFood += demographic.HuntFood; path.GatherFood += demographic.TerrestrialFood; }
                }
            }
            Console.WriteLine("BENCHMARK seeds=" + seeds + " survivors=" + survivors + "/" + total + " starvationDeaths=" + starvation + " netGrowth=" + growth + " illegalOrders=" + illegal + " techs=" + string.Join(",", technologies) + " elapsed=" + timer.Elapsed);
            if (args.Contains("--biome-paths"))
            {
                endingPopulations.Sort(); int atLeastOneHundred = endingPopulations.Count(population => population >= 100);
                Console.WriteLine("PALEOLITHIC_POPULATION p25=" + Percentile(endingPopulations, .25) + " median=" + Percentile(endingPopulations, .5) + " p75=" + Percentile(endingPopulations, .75) + " max=" + endingPopulations.LastOrDefault() + " atLeast100=" + atLeastOneHundred + "/" + endingPopulations.Count);
                Console.WriteLine("GROWTH_RESPONSE opportunities=" + growthResponseOpportunities + " satisfied=" + growthResponses + " dispatches=" + dispatchResponses + " additionalDispatches=" + additionalDispatchResponses + " increasedGathering=" + gatheringResponses);
                Console.WriteLine("HUNT_PURSUIT attempts=" + huntPursuits + " successful=" + successfulHuntPursuits + " rate=" + (100.0 * successfulHuntPursuits / Math.Max(1, huntPursuits)).ToString("F1") + "%");
                foreach (var item in habitatPaths.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    var path = item.Value;
                    Console.WriteLine("PALEOLITHIC_PATH habitat=" + item.Key.Replace(' ', '_') + " tribes=" + path.Tribes + " survivors=" + path.Survivors + " meanPopulation=" + (path.EndingPopulation / Math.Max(1, path.Tribes)).ToString("F1") + " growthRatio=" + (path.EndingPopulation / Math.Max(1, path.StartingPopulation)).ToString("F2") + " huntFoodPerTribe=" + (path.HuntFood / Math.Max(1, path.Tribes)).ToString("F1") + " gatheredFoodPerTribe=" + (path.GatherFood / Math.Max(1, path.Tribes)).ToString("F1") + " husbandry=" + path.Husbandry);
                }
                Console.WriteLine("ARID_RIVER_CORRIDORS riverTilesAdjacentToDesert=" + aridRiverCorridors + " acrossSeeds=" + seeds);
                if (seeds >= 30)
                {
                    Check(Percentile(endingPopulations, .5) >= 85 && Percentile(endingPopulations, .75) >= 100 && atLeastOneHundred * 5 >= endingPopulations.Count * 2 && endingPopulations.Last() <= 300, "Typical tribes should approach one hundred people without explosive Paleolithic populations");
                    Check(technologies.Skip(1).Count(n => n > 0) > 1, "The balance sample should contain distinct subsistence outcomes; shares are targets");
                }
                if (growthResponseOpportunities > 0) Check(growthResponses >= growthResponseOpportunities * .75, "Most observed population increases should be answered by more gathering or hunter dispatch");
            }
            Check(illegal == 0, "AI emitted illegal orders");
            if (args.Contains("--standard"))
            {
                timer.Restart(); var standardConfig = new SimulationConfig { Players = 80 }; var world = new WorldGenerator().Generate(1729, standardConfig);
                Console.WriteLine("STANDARD tiles=" + world.Tiles.Length + " tribes=" + world.Camps.Count + " generation=" + timer.Elapsed + " memoryMB=" + GC.GetTotalMemory(false) / 1048576 + " land=" + world.Generation.LandTiles + " continents=" + world.Generation.Continents);
                Check(world.Tiles.Length == 524288, "Standard dimensions");
                Check(world.Camps.Count == 80, "Standard supports eighty separated viable starts");
                timer.Restart(); var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder(); var resolver = new TurnResolver();
                while (!world.Finished && world.Turn <= 1000)
                {
                    var planningIndex = new PlanningIndex(world);
                    var orders = world.Camps.ToDictionary(c => c.Player, c => policy.Decide(builder.Build(world, c.Player, planningIndex), c.Mandate).Orders);
                    resolver.Resolve(world, orders);
                }
                Console.WriteLine("STANDARD opening=" + timer.Elapsed + " memoryMB=" + GC.GetTotalMemory(false) / 1048576 + " turns=" + (world.Turn - 1));
                timer.Restart(); var saved = SaveCodec.Save(world);
                Console.WriteLine("STANDARD save=" + timer.Elapsed + " bytes=" + saved.Length);
                timer.Restart(); var restored = SaveCodec.Load(saved);
                Console.WriteLine("STANDARD load=" + timer.Elapsed);
                Check(restored.Turn == world.Turn && restored.Tiles[123456].Food == world.Tiles[123456].Food, "Standard save preserves tiles and clock");
            }
            if (args.Contains("--geography"))
            {
                foreach (WorldShape shape in Enum.GetValues(typeof(WorldShape)))
                {
                    double land = 0, islands = 0, coast = 0;
                    for (int seed = 1; seed <= 12; seed++)
                    {
                        var config = SimulationConfig.Small(2); config.Shape = shape;
                        var generated = new WorldGenerator().Generate(seed, config);
                        land += generated.Generation.LandTiles / (double)generated.Tiles.Length;
                        islands += generated.Generation.Islands; coast += generated.Generation.CoastFraction;
                        Check(generated.Camps.All(c => PlacementStage.Viability(generated, c.Tile) >= config.StartViability), "Viable start");
                        foreach (var tile in generated.Tiles)
                        {
                            int next = tile.Id, count = 0;
                            while (next >= 0 && count++ <= generated.Tiles.Length) next = generated.Tiles[next].Downstream;
                            Check(count <= generated.Tiles.Length, "No drainage cycles");
                        }
                    }
                    Console.WriteLine("GEOGRAPHY " + shape + " meanLand=" + (land / 12).ToString("F3") + " islands=" + (islands / 12).ToString("F1") + " coastline=" + (coast / 12).ToString("F3"));
                }
                var coldConfig = SimulationConfig.Small(1); coldConfig.HabitableZonePosition = .8;
                var hotConfig = SimulationConfig.Small(1); hotConfig.HabitableZonePosition = .2;
                var cold = new WorldGenerator().Generate(7, coldConfig); var hot = new WorldGenerator().Generate(7, hotConfig);
                Check(hot.Generation.MeanTemperature > cold.Generation.MeanTemperature + 10, "Stellar baseline precedes local climate");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
