using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Anthropocene.Simulation;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    private static WorldState Flat(int players = 2)
    {
        var cfg = SimulationConfig.Small(players); cfg.TurnDays = 90; cfg.BaseDays = 90; cfg.PracticeDays = 90; cfg.AnnualHumanGrowthRate = 0; cfg.Width = 24; cfg.Height = 16; cfg.ExperienceThreshold = 100000;
        var w = new WorldState { Config = cfg, Seed = 42, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            w.Tiles[i] = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, Food = 60, Freshwater = 60, FreshwaterSource = FreshwaterSource.RainPool };
            for (int s = 0; s < 4; s++) w.Tiles[i].Seasons[s] = new SeasonData { MaxFood = 80, MaxFreshwater = 80, FoodRate = .3f, FreshwaterRate = .3f, Moisture = .5f, Habitat = .5f };
        }
        for (int p = 0; p < players; p++) w.Camps.Add(new Camp { Id = w.NextId++, Player = p, Tile = 150 + p * 6, Population = 24, Food = 50 });
        return w;
    }
    private static Dictionary<int, List<Order>> Orders(params Order[] orders) => new Dictionary<int, List<Order>> { [0] = orders.ToList() };
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
        w = Flat(); camp = w.Camps[0]; var hunter = new Hunter { Id = w.NextId++, SourceCamp = camp.Id, Player = 0, Tile = camp.Tile, Population = 8 }; camp.Population -= 8; w.Hunters.Add(hunter);
        int water = w.Map.Neighbors(camp.Tile).First(); w.Tiles[water].Terrain = Terrain.Shallows;
        Check(resolver.Validate(w, 0, new Order(OrderKind.Move, camp.Id, destination: water)) != null, "Camp cannot embark");
        Check(resolver.Validate(w, 0, new Order(OrderKind.Move, hunter.Id, destination: water)) != null, "Hunters cannot embark before breakthrough");
        var fishRecord = resolver.Resolve(w, Orders(new Order(OrderKind.Fish, hunter.Id, water)));
        Check(fishRecord.Transfers.Any(t => t.Kind == "Fishing" && t.Food > 0), "Fishing yields food");
        var tiles = new[] { new Tile { Food = 10, Freshwater = 5, FreshwaterSource = FreshwaterSource.RainPool } };
        var demands = new[] { new Demand { Tile = 0, Entity = 2, Food = 15, Freshwater = 8, Kind = "a" }, new Demand { Tile = 0, Entity = 1, Food = 5, Freshwater = 2, Kind = "a" } };
        var allocator = new ProportionalAllocation(); var a = allocator.Allocate(demands, tiles); var b = allocator.Allocate(demands.Reverse(), tiles);
        Check(a.Select(t => t.Food).SequenceEqual(b.Select(t => t.Food)) && Math.Abs(a.Sum(t => t.Food) - 10) < 1e-9, "Order independent allocation");
        w = Flat(); w.Camps[1].Tile = w.Camps[0].Tile + 2;
        int destination = w.Camps[0].Tile + 1;
        resolver.Resolve(w, new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.Move, w.Camps[0].Id, destination: destination) }, [1] = new List<Order> { new Order(OrderKind.Move, w.Camps[1].Id, destination: destination) } });
        Check(w.Camps[0].Tile == destination && w.Camps[1].Tile != destination, "Stable camp collision");
        w = Flat(); camp = w.Camps[0]; camp.Food = 0;
        foreach (var tile in w.Tiles) { tile.Food = 0; for (int s = 0; s < 4; s++) tile.Seasons[s].FoodRate = 0; }
        resolver.Resolve(w, Orders()); resolver.Resolve(w, Orders()); Check(camp.Population == 24, "Starvation grace");
        resolver.Resolve(w, Orders()); Check(camp.Population < 24, "Starvation attrition");
        w = Flat(); camp = w.Camps[0]; camp.Experience = new double[] { 100, 100, 100 }; w.Turn = 12;
        TurnResolver.Breakthrough(w, camp, new TurnRecord());
        Check(camp.Technology == Technology.AnimalHusbandry && camp.ForcedTransition && camp.SecondaryDomain == 1, "Hard cap deterministic tie");
        Check(camp.Experience[1] == 100 && TurnResolver.NeolithicProgress(w, camp, Domain.Gathering) > 100, "Secondary experience preserved and boosted");
        w = Flat(); camp = w.Camps[0]; camp.Experience[1] = 120; w.Turn = 9;
        TurnResolver.Breakthrough(w, camp, new TurnRecord()); Check(camp.Technology == Technology.None, "No premature majority");
        w.Turn = 10; TurnResolver.Breakthrough(w, camp, new TurnRecord()); Check(camp.Technology == Technology.Cultivation, "Turn ten majority");
        w = Flat(); var before = Knowledge.Visible(w, 0); w.Camps[0].Tile += 4; var after = Knowledge.Visible(w, 0);
        Check(before.Except(after).Any() && after.Count == 19, "Present-tense fog");
        var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder();
        w = Flat(); var decision = policy.Decide(builder.Build(w, 0), w.Camps[0].Mandate);
        int hidden = Enumerable.Range(0, w.Tiles.Length).First(n => !Knowledge.Visible(w, 0).Contains(n));
        w.Tiles[hidden].Food = 99999; w.Camps[1].Food = 98765;
        var decision2 = policy.Decide(builder.Build(w, 0), w.Camps[0].Mandate);
        Check(decision.Orders.Select(o => o.ToString()).SequenceEqual(decision2.Orders.Select(o => o.ToString())), "AI hidden-state invariance");
        w = Flat(); var bytes = SaveCodec.Save(w); var loaded = SaveCodec.Load(bytes);
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "Save round trip");
        resolver.Resolve(w, Orders()); resolver.Resolve(loaded, Orders()); Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "Replay from save");
        w = Flat(); w.Config.StartingReserveGenerations = w.Config.HunterCarryGenerations = w.Config.MinimumStorageGenerations = w.Config.MaximumStorageGenerations = w.Config.HunterForagePerPerson = w.Config.HuntBreedingReserveFraction = 0;
        foreach (var species in w.Config.Species) species.MaximumHerdSize = 32;
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = w.Camps[0].Tile + 1, Species = "antelope", Animals = 30, Capacity = 32 });
        loaded = SaveCodec.Load(SaveCodec.Save(w));
        Check(loaded.Config.StartingReserveGenerations == 1 && loaded.Config.HunterCarryGenerations == 1 && loaded.Config.MinimumStorageGenerations == 1 && loaded.Config.MaximumStorageGenerations == 4 && loaded.Config.HunterForagePerPerson == 1 && loaded.Config.HuntBreedingReserveFraction == .2, "Legacy saves gain generational defaults");
        Check(loaded.Config.GetSpecies("antelope").MaximumHerdSize == 150 && loaded.Herds[0].Capacity == 150, "Legacy saves gain species herd thresholds");
        w = Flat(); camp = w.Camps[0];
        hunter = new Hunter { Id = w.NextId++, Tile = camp.Tile, Population = 12, Player = 0, SourceCamp = camp.Id }; w.Hunters.Add(hunter); camp.Population -= 12;
        var prey = new Herd { Id = w.NextId++, Tile = camp.Tile + 1, Animals = 8 }; w.Herds.Add(prey);
        int fleeing = TurnResolver.HerdDestination(w, prey);
        Check(w.Map.Distance(fleeing, hunter.Tile) == w.Map.Radius(prey.Tile, 1).Max(n => w.Map.Distance(n, hunter.Tile)), "Herd maximizes escape distance");
        var huntRecord = resolver.Resolve(w, Orders(new Order(OrderKind.Hunt, hunter.Id, prey.Id, fleeing)));
        Check(huntRecord.Transfers.Any(t => t.Kind == "Hunting" && t.Food > 0), "Predictive interception succeeds");
        Check(huntRecord.Transfers.Where(t => t.Entity == prey.Id && t.Kind == "Herd").Sum(t=>t.Food) > 0 && huntRecord.Transfers.Where(t => t.Entity == prey.Id && t.Kind == "Herd").Sum(t=>t.Freshwater) > 0, "Surviving herd consumes both resources");
        w = Flat(); camp = w.Camps[0]; hunter = new Hunter { Id = w.NextId++, Tile = camp.Tile, Population = 8, Player = 0, SourceCamp = camp.Id }; w.Hunters.Add(hunter); camp.Population -= 8;
        prey = new Herd { Id = w.NextId++, Tile = camp.Tile + 1, Animals = 8 }; w.Herds.Add(prey);
        huntRecord = resolver.Resolve(w, Orders(new Order(OrderKind.Hunt, hunter.Id, prey.Id)));
        Check(prey.Animals >= 8 && camp.Experience[0] > 0 && !huntRecord.Transfers.Any(t => t.Kind == "Hunting"), "No contact means no kill but experience");
        w = Flat(); loaded = SaveCodec.Load(SaveCodec.Save(w)); loaded.Config.TurnDays = 45;
        resolver.Resolve(w, Orders()); resolver.Resolve(loaded, Orders());
        Check(loaded.Camps[0].LastConsumed < w.Camps[0].LastConsumed && loaded.History[0].Transfers.Where(t => t.Kind == "Gathering").Sum(t => t.Food) < w.History[0].Transfers.Where(t => t.Kind == "Gathering").Sum(t => t.Food), "Duration scales consumption and extraction");
        w = Flat(); var inertia = policy.Decide(builder.Build(w, 0), w.Camps[0].Mandate);
        Check(inertia.Orders[0].Kind == OrderKind.NoOp, "Equivalent tiles favor camp continuity");
        foreach (int n in w.Map.Radius(w.Camps[0].Tile, 1)) { w.Tiles[n].Food = 0; for (int s = 0; s < 4; s++) w.Tiles[n].Seasons[s].FoodRate = 0; }
        inertia = policy.Decide(builder.Build(w, 0), w.Camps[0].Mandate);
        Check(inertia.Orders[0].Kind == OrderKind.Move, "Depletion overcomes relocation inertia");
        w = Flat(); w.Camps[0].Experience = new double[] { 2, 7, 1 }; w.Turn = 12;
        TurnResolver.Breakthrough(w, w.Camps[0], new TurnRecord());
        Check(w.Camps[0].ForcedTransition, "Turn twelve grants carryover even if a majority only appears at the cap");
        w = Flat(); foreach (var tile in w.Tiles) tile.Seasons[1].MaxFood = 4;
        resolver.Resolve(w, Orders()); Check(w.Tiles.All(t => t.Food <= 4), "Resources respect the next season's carrying capacity");
        Console.WriteLine("PASS: " + assertions + " focused assertions");
    }
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--write-config"))
            {
                System.IO.File.WriteAllText("Assets/_Anthropocene/Resources/OpeningConfig.json", System.Text.Json.JsonSerializer.Serialize(new SimulationConfig(), new System.Text.Json.JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
                return 0;
            }
            Tests(); Milestone2Checks.Run(); Milestone3Checks.Run(); int seeds = args.Length > 0 ? int.Parse(args[0]) : 20; int survivors = 0, total = 0, starvation = 0, growth = 0, illegal = 0;
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
                    Check(generated.Camps.All(c => generated.Herds.Any(h => generated.Map.Reachable(c.Tile, h.Tile, 2, n => generated.Tiles[n].TraversableLand))), "Every start has a herd reachable around peaks");
                    Check(generated.Camps.Count == 80 && generated.Herds.All(h => generated.Tiles[h.Tile].ElevationLevel < 10) && generated.Camps.All(c => generated.Tiles[c.Tile].ElevationLevel < 10), "Eighty camps spawn without using peaks");
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
                var replay = SaveCodec.Load(SaveCodec.Save(w));
                var resolver = new TurnResolver(); var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder();
                while (!w.Finished && w.Turn <= 12)
                {
                    var orders = new Dictionary<int, List<Order>>(); var traces = new List<DecisionTrace>();
                    foreach (var camp in w.Camps)
                    {
                        var decision = policy.Decide(builder.Build(w, camp.Player), camp.Mandate); orders[camp.Player] = decision.Orders; traces.Add(decision.Trace);
                        foreach (var order in decision.Orders) if (resolver.Validate(w, camp.Player, order) != null) illegal++;
                    }
                    var record = resolver.Resolve(w, orders, traces); starvation += record.Deaths; growth += record.Growth;
                    // Reverse entity storage and order insertion to test collection-order independence.
                    replay.Camps.Reverse(); replay.Hunters.Reverse(); replay.Herds.Reverse();
                    resolver.Resolve(replay, orders.Reverse().ToDictionary(p => p.Key, p => p.Value.AsEnumerable().Reverse().ToList()), traces);
                    Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(replay), "Deterministic replay diverged at seed " + seed + " turn " + (w.Turn - 1));
                }
                foreach (var camp in w.Camps) { total++; if (w.LinkedPopulation(camp) > 0) survivors++; technologies[(int)camp.Technology]++; }
            }
            Console.WriteLine("BENCHMARK seeds=" + seeds + " survivors=" + survivors + "/" + total + " starvationDeaths=" + starvation + " netGrowth=" + growth + " illegalOrders=" + illegal + " techs=" + string.Join(",", technologies) + " elapsed=" + timer.Elapsed);
            Check(illegal == 0, "AI emitted illegal orders");
            if (args.Contains("--standard"))
            {
                timer.Restart(); var standardConfig = new SimulationConfig { Players = 80 }; var world = new WorldGenerator().Generate(1729, standardConfig);
                Console.WriteLine("STANDARD tiles=" + world.Tiles.Length + " tribes=" + world.Camps.Count + " generation=" + timer.Elapsed + " memoryMB=" + GC.GetTotalMemory(false) / 1048576 + " land=" + world.Generation.LandTiles + " continents=" + world.Generation.Continents);
                Check(world.Tiles.Length == 524288, "Standard dimensions");
                Check(world.Camps.Count == 80, "Standard supports eighty separated viable starts");
                timer.Restart(); var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder(); var resolver = new TurnResolver();
                while (!world.Finished && world.Turn <= 12)
                {
                    var orders = world.Camps.ToDictionary(c => c.Player, c => policy.Decide(builder.Build(world, c.Player), c.Mandate).Orders);
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
