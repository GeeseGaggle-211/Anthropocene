using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class ScenarioChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Playtest scenarios: " + message); }
        var config = new SimulationConfig(); PlaytestScenarios.Configure(config);
        var world = new WorldGenerator().Generate(PlaytestScenarios.Seed, config);
        config.Shape = WorldShape.Pangaea;
        Check(!PlaytestScenarios.Matches(world, 1), "a different world shape cannot masquerade as the fixed preset");
        config.Shape = WorldShape.Continents; config.Players = 79;
        Check(!PlaytestScenarios.Matches(world, 1), "a different tribe-count configuration cannot masquerade as the fixed preset");
        config.Players = 80;
        // Older saves keep their recorded species roster and original steppe marker.
        var currentSpecies = config.Species;
        config.Species = currentSpecies.Where(s => s.Id != "cattle").ToList();
        Check(PlaytestScenarios.Tile(world, 2) == 413877, "legacy species roster retains the prior steppe preset");
        config.Species = currentSpecies;
        string original = SaveCodec.Fingerprint(world);
        var players = world.Societies.ToDictionary(s => s.Id, s => s.Player);
        foreach (int scenario in new[] { 1, 2, 3 })
        {
            Check(PlaytestScenarios.Matches(world, scenario), "preset habitat and normal viability remain valid");
            PlaytestScenarios.Select(world, scenario);
            var own = world.Camps.Single(c => c.Player == 0);
            Check(own.Tile == PlaytestScenarios.Tile(scenario), "selected natural camp becomes human tribe");
            Check(world.Camps.Select(c => c.Player).Distinct().Count() == 80, "ownership remains one-to-one");
            var observed = new AgentObservationBuilder().Build(world, 0);
            Check(observed.CampId == own.Id && observed.Tiles.Count < world.Tiles.Length, "normal observation remains filtered and follows selected camp");
            var saved = SaveCodec.Save(world); var restored = SaveCodec.Load(saved);
            Check(SaveCodec.Fingerprint(restored) == SaveCodec.Fingerprint(world) && restored.Camps.Single(c => c.Player == 0).Tile == own.Tile, "save restores exact selected ownership without regenerating");
            double land = world.Map.Radius(own.Tile, 1).Where(id => world.Tiles[id].Land).Sum(id => PlaytestScenarios.Food(world, world.Tiles[id], -1));
            double water = world.Map.Radius(own.Tile, 1).Where(id => world.Tiles[id].Aquatic).Sum(id => PlaytestScenarios.Food(world, world.Tiles[id], -1));
            var radius = world.Map.Radius(own.Tile, 1).Select(id => world.Tiles[id]).ToArray();
            double minimumWater = Enumerable.Range(0, 4).Min(s => radius.Where(t => t.WaterBody != WaterKind.Saltwater && t.FreshwaterSource != FreshwaterSource.None).Sum(t => t.Seasons[s].MaxFreshwater));
            Console.WriteLine($"PRESET {scenario} {PlaytestScenarios.Names[scenario]} seed={world.Seed} tile={own.Tile} landFood={land:F2} aquaticFood={water:F2} viability={PlaytestScenarios.Viability(world, own.Tile):F2}% freshwaterMinCapacity={minimumWater:F2}");
            Console.WriteLine("PRESET seasonal land=" + string.Join("/", Enumerable.Range(0, 4).Select(s => radius.Where(t => t.Land).Sum(t => PlaytestScenarios.Food(world, t, s)).ToString("F2")))
                + " aquatic=" + string.Join("/", Enumerable.Range(0, 4).Select(s => radius.Where(t => t.Aquatic).Sum(t => PlaytestScenarios.Food(world, t, s)).ToString("F2"))));
        }
        var probe = world.Tiles[PlaytestScenarios.Tile(3)];
        string beforePreview = SaveCodec.Fingerprint(world);
        foreach (var tile in world.Tiles)
        { PlaytestScenarios.Food(world, tile, -1); PlaytestScenarios.Water(world, tile, -1); }
        LazyEcology.ProjectEcology(world, probe.Id, world.Days);
        Check(SaveCodec.Fingerprint(world) == beforePreview, "preview reads do not materialize ecology, bind ownership or change saves");
        var sea = world.Map.Radius(probe.Id, 1).Select(id => world.Tiles[id]).First(t => t.Environment == EnvironmentKind.Shallows);
        Check(PlaytestScenarios.Water(world, sea, -1) == 0 && PlaytestScenarios.Food(world, sea, -1) > 0, "marine food never becomes freshwater on preview");
        Check(Math.Abs(PlaytestScenarios.Food(world, sea, -1) - Enumerable.Range(0, 4).Average(s => PlaytestScenarios.Food(world, sea, s))) < 1e-8, "annual overlay averages the same seasonal profiles");
        foreach (var society in world.Societies) society.Player = players[society.Id];
        Ownership.Bind(world);
        Check(SaveCodec.Fingerprint(world) == original, "selection changes only player labels, not policies, IDs, terrain, reserves or herds");
        world.Turn = 2;
        bool rejected = false; try { PlaytestScenarios.Select(world, 1); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "cannot reassign a running game with preset selection");
        Console.WriteLine("PASS: " + checks + " preset and developer-preview assertions");
    }
}
