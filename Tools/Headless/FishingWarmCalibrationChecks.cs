using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// Reproduces the locked v0.9 warm-site cohort from source evidence S24.
// The historical population sweep used an experiment adapter; production
// validation owns the identical generated geography, route classification and
// 221 exact-one-ground cases, while live economy outcomes are covered by the
// registered 50-turn tests.
internal static class FishingWarmCalibrationChecks
{
    private static readonly int[] Seeds =
        { 0, 1, 2, 3, 4, 11, 15, 17, 41, 42, 83, 1729, 2718, 9001 };

    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Fishing warm calibration: " + message);
    }

    private static string Resource(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "Assets", "_Anthropocene", "Resources", name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(name);
    }

    private static SimulationConfig Configuration(JsonSerializerOptions json)
    {
        var config = JsonSerializer.Deserialize<SimulationConfig>(
            File.ReadAllText(Resource("OpeningConfig.json")), json);
        config.Width = 128;
        config.Height = 64;
        config.Players = 8;
        config.PlateCount = 12;
        config.StartSeparation = 8;
        config.OpeningCompatibilityEffects = false;
        config.FissionEnabled = false;
        config.ImmigrationEnabled = false;
        config.TradeEnabled = false;
        config.NeolithicFishingEnabled = false;
        config.CorrectedAquaticSeasonalPressureEnabled = false;
        config.Validate();
        return config;
    }

    private static Dictionary<int, int> Access(WorldState world, int origin)
    {
        var result = new Dictionary<int, int>();
        if (!world.Tiles[origin].TraversableLand) return result;
        var queue = new Queue<int>();
        foreach (int neighbor in world.Map.Neighbors(origin).OrderBy(id => id))
            if (world.Tiles[neighbor].Environment == EnvironmentKind.Shallows ||
                world.Tiles[neighbor].Environment == EnvironmentKind.Lake)
            {
                if (result.ContainsKey(neighbor)) continue;
                result[neighbor] = 1;
                queue.Enqueue(neighbor);
            }
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int distance = result[current];
            if (distance >= FishingEconomy.GroundRange) continue;
            var environment = world.Tiles[current].Environment;
            foreach (int neighbor in world.Map.Neighbors(current).OrderBy(id => id))
                if (!result.ContainsKey(neighbor) && world.Tiles[neighbor].Environment == environment)
                {
                    result[neighbor] = distance + 1;
                    queue.Enqueue(neighbor);
                }
        }
        return result;
    }

    private static bool Warm(Tile tile)
        => tile.Seasons.Min(season => (double)season.Temperature) > 0;

    public static int Run()
    {
        checks = 0;
        Check(Seeds.Length == 14, "the source cohort contains fourteen compact worlds");
        var defaults = new SimulationConfig();
        Check(FishingEconomy.GroundRange == 3 &&
            Math.Abs(FishingEconomy.DistanceRetention - .75) < 1e-12,
            "range three and 0.75 route retention remain locked");
        Check(Math.Abs(defaults.FishPerPerson - 2.6) < 1e-12 &&
            Math.Abs(defaults.ReferenceIntensity - .4) < 1e-12 &&
            Math.Abs(defaults.MaintenanceCoefficient - .4) < 1e-12 &&
            Math.Abs(defaults.CultivationRegenFlux - .65) < 1e-12 &&
            Math.Abs(defaults.Neolithic.DecayHalfLifeYears - 80) < 1e-12 &&
            Math.Abs(defaults.Neolithic.CultivationMarginalEffortMultiplierPerTenth - 16) < 1e-12,
            "the v0.9 physical calibration constants remain unchanged");

        var json = new JsonSerializerOptions { IncludeFields = true };
        var resources = JsonSerializer.Deserialize<ResourceGenerationOptions>(
            File.ReadAllText(Resource("ResourceGenerationConfig.json")), json);
        var counts = new Dictionary<(string Count, string Water), int>();
        int total = 0, maxCandidates = 0;
        long allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        foreach (int seed in Seeds)
        {
            var world = new WorldGenerator().Generate(seed, Configuration(json), resources);
            foreach (var land in world.Tiles.Where(tile => tile.TraversableLand).OrderBy(tile => tile.Id))
            {
                var access = Access(world, land.Id);
                maxCandidates = Math.Max(maxCandidates, access.Count);
                Check(access.Count <= 36,
                    "bounded range-three access visits at most the local hex area");
                if (access.Count == 0 || access.Keys.Any(id => !Warm(world.Tiles[id]))) continue;
                string water = access.Keys.All(id => world.Tiles[id].Environment == EnvironmentKind.Shallows)
                    ? "Shallows"
                    : access.Keys.All(id => world.Tiles[id].Environment == EnvironmentKind.Lake)
                        ? "Lake" : "Mixed";
                string count = access.Count == 1 ? "exact1" : access.Count == 2 ? "exact2" : "3plus";
                var key = (count, water);
                counts[key] = counts.TryGetValue(key, out int prior) ? prior + 1 : 1;
                total++;
            }
        }
        timer.Stop();

        var expected = new Dictionary<(string Count, string Water), int>
        {
            [("3plus", "Shallows")] = 5481,
            [("3plus", "Lake")] = 184,
            [("3plus", "Mixed")] = 5,
            [("exact2", "Shallows")] = 5,
            [("exact2", "Lake")] = 100,
            [("exact1", "Shallows")] = 31,
            [("exact1", "Lake")] = 190
        };
        Check(total == 5996, "the warm source cohort contains 5,996 eligible sites");
        Check(counts.Count == expected.Count && expected.All(pair =>
                counts.TryGetValue(pair.Key, out int actual) && actual == pair.Value),
            "the complete warm site-class distribution matches source evidence S24");
        Check(counts[("exact1", "Shallows")] + counts[("exact1", "Lake")] == 221,
            "all 221 exact-one-ground calibration cases are reproduced");

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedAtStart;
        Console.WriteLine("FISHING_WARM_CALIBRATION worlds=14 sites=" + total +
            " exactOne=221 maxCandidates=" + maxCandidates +
            " elapsedMs=" + timer.Elapsed.TotalMilliseconds.ToString("F1") +
            " allocatedBytes=" + allocated);
        Console.WriteLine("PASS: " + checks + " Fishing warm calibration assertions");
        return checks;
    }
}
