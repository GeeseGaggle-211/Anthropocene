using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// Developer audit only. These quantities do not enter observations or change placement.
internal static class Program
{
    private static readonly MaterialId[] Materials = { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood,
        MaterialId.Copper, MaterialId.Tin, MaterialId.Gold, MaterialId.Lead, MaterialId.Zinc,
        MaterialId.Silver, MaterialId.Salt, MaterialId.Limestone, MaterialId.Silica, MaterialId.Gypsum };
    private static readonly double[] Thresholds = { 0, 1, 10, 100 };
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static string Option(string[] args, string key, string fallback)
    {
        int at = Array.IndexOf(args, key);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
    }

    static int Main(string[] args)
    {
        try
        {
            string output = Path.GetFullPath(Option(args, "--output", "TestResults/TribeResourceAccess"));
            Directory.CreateDirectory(output);
            var options = JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"), Json);
            options.MaximumQuantity = int.Parse(Option(args, "--maximum", options.MaximumQuantity.ToString()));
            options.Validate();
            if (!options.Enabled) throw new ArgumentException("The audit requires resource generation enabled.");
            var report = new AuditReport { SourceCommit = Option(args, "--source-commit", "unrecorded"), ResourceOptions = options };
            foreach (string item in Option(args, "--worlds", "Continents:11,Continents:42,Continents:1729,Pangaea:42,Archipelagos:42").Split(','))
            {
                string[] parts = item.Split(':');
                var shape = Enum.Parse<WorldShape>(parts[0]);
                int seed = int.Parse(parts[1]);
                var timer = Stopwatch.StartNew();
                var config = new SimulationConfig { Width = 1024, Height = 512, Players = 80, Shape = shape, StartSeparation = 12 };
                var world = new WorldGenerator().Generate(seed, config, options);
                if (world.Camps.Count != 80) throw new Exception("Expected 80 spawned camps.");
                var measured = Measure(world);
                measured.ElapsedSeconds = timer.Elapsed.TotalSeconds;
                report.Worlds.Add(measured);
                File.WriteAllText(Path.Combine(output, measured.Key + ".json"), JsonSerializer.Serialize(measured, Json));
                Console.WriteLine($"PASS {measured.Key}: 80 tribes, {Materials.Length} materials, {timer.Elapsed.TotalSeconds:F1}s; state unchanged.");
                world = null;
                GC.Collect();
            }
            File.WriteAllText(Path.Combine(output, "data.json"), JsonSerializer.Serialize(report, Json));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static WorldReport Measure(WorldState world)
    {
        string before = SaveCodec.Fingerprint(world);
        int size = world.Tiles.Length;
        var result = new WorldReport {
            Key = world.Config.Shape + "-" + world.Seed, Shape = world.Config.Shape.ToString(), Seed = world.Seed,
            Width = world.Config.Width, Height = world.Config.Height, MaximumQuantity = world.Resources.MaximumQuantity,
            ResourceFingerprint = ResourceCodec.Fingerprint(world.Resources), WorldFingerprint = before,
            ConfigurationHash = world.Generation.ConfigurationHash, ResourceConfigurationHash = world.Resources.ConfigurationHash,
            LandTiles = world.Tiles.Count(t => t.Land), TraversableTiles = world.Tiles.Count(t => t.TraversableLand)
        };
        var graph = new int[size * 6];
        Array.Fill(graph, -1);
        for (int i = 0; i < size; i++)
        {
            int direction = 0;
            foreach (int neighbor in world.Map.Neighbors(i)) graph[i * 6 + direction++] = neighbor;
        }
        bool[] walkable = world.Tiles.Select(t => t.TraversableLand).ToArray();
        var components = Components(graph, walkable, out var componentSizes);
        var catchments = new Dictionary<int, List<(int tile, int distance)>>();
        foreach (var camp in world.Camps.OrderBy(c => c.Player))
        {
            result.Camps.Add(new CampReport { Player = camp.Player, Id = camp.Id, Tile = camp.Tile,
                Environment = world.Tiles[camp.Tile].Environment.ToString(), Component = components[camp.Tile],
                ComponentTiles = componentSizes[components[camp.Tile]] });
            catchments[camp.Tile] = Catchment(camp.Tile, graph, walkable, 10);
        }
        // Quarter-resolution land mask for a lightweight spatial audit view; true camp coordinates stay exact.
        for (int y = 0; y < world.Config.Height; y += 4)
        {
            int run = -1;
            for (int x = 0; x <= world.Config.Width; x += 4)
            {
                bool land = x < world.Config.Width && world.Tiles[y * world.Config.Width + x].Land;
                if (land && run < 0) run = x / 4;
                if (!land && run >= 0) { result.LandRuns.Add(new[] { y / 4, run, x / 4 - run }); run = -1; }
            }
        }
        foreach (MaterialId material in Materials)
        {
            var r = world.Resources;
            var total = new double[size]; var accessible = new double[size];
            var scattered = new double[size]; var rich = new double[size];
            for (int i = 0; i < size; i++)
            {
                total[i] = r.SurfaceTotal(i, material);
                accessible[i] = rich[i] = r.SurfaceAccessible(i, material);
            }
            foreach (var body in r.Bodies)
            {
                double component = r.ComponentQuantity(body, material);
                if (component <= 0) continue;
                foreach (var site in body.Sites)
                {
                    double potential = r.AccessibleAt(body, site, material);
                    total[site.Tile] += component * site.Share / 10000;
                    accessible[site.Tile] += potential;
                    if (body.Concentration == MineralConcentration.Scattered) scattered[site.Tile] += potential;
                    else rich[site.Tile] += potential;
                }
            }
            double expected = world.Tiles.Sum(t => r.SurfaceTotal(t.Id, material)) + r.Bodies.Sum(b => r.ComponentQuantity(b, material));
            if (Math.Abs(expected - total.Sum()) > Math.Max(.01, expected * 1e-10)) throw new Exception("Shared-body accounting failed.");
            var geometric = Nearest(graph, walkable, accessible, 0, false);
            var deposited = Nearest(graph, walkable, rich, 0, true);
            var nearest = Thresholds.Select(threshold => Nearest(graph, walkable, accessible, threshold, true)).ToArray();
            foreach (var camp in result.Camps)
            {
                var row = new ResourceRow { Material = material.ToString(), DiscoveryTechnology = r.DiscoveryTechnologies[(int)material],
                    BaseAccessFraction = r.AccessibleFraction(material), GeometricDistance = Distance(geometric.distance[camp.Tile]),
                    RichDistance = Distance(deposited.distance[camp.Tile]), RichTile = Distance(deposited.owner[camp.Tile]), AtCamp = accessible[camp.Tile] };
                foreach (var (tile, distance) in catchments[camp.Tile])
                {
                    if (distance <= 1) row.Within1 += accessible[tile];
                    if (distance <= 2) { row.Within2 += accessible[tile]; row.ScatteredWithin2 += scattered[tile]; }
                    if (distance <= 5) row.Within5 += accessible[tile];
                    row.Within10 += accessible[tile];
                }
                for (int j = 0; j < nearest.Length; j++)
                {
                    int tile = nearest[j].owner[camp.Tile];
                    row.Nearest.Add(new NearestResult { MinimumAccessible = Thresholds[j], LandDistance = Distance(nearest[j].distance[camp.Tile]),
                        Tile = Distance(tile), Total = tile < 0 ? 0 : total[tile], Accessible = tile < 0 ? 0 : accessible[tile], ScatteredAccessible = tile < 0 ? 0 : scattered[tile] });
                }
                camp.Resources.Add(row);
            }
        }
        if (SaveCodec.Fingerprint(world) != before) throw new Exception("Read-only audit changed authoritative state.");
        return result;
    }

    private static int? Distance(int value) => value < 0 ? (int?)null : value;

    private static (int[] distance, int[] owner) Nearest(int[] graph, bool[] walkable, double[] values, double threshold, bool landOnly)
    {
        var distance = new int[walkable.Length]; var owner = new int[walkable.Length]; var queue = new int[walkable.Length];
        Array.Fill(distance, -1); Array.Fill(owner, -1);
        int head = 0, tail = 0;
        for (int tile = 0; tile < values.Length; tile++)
            if (walkable[tile] && values[tile] > 0 && values[tile] >= threshold)
            { distance[tile] = 0; owner[tile] = tile; queue[tail++] = tile; }
        while (head < tail)
        {
            int current = queue[head++];
            for (int direction = 0; direction < 6; direction++)
            {
                int next = graph[current * 6 + direction];
                if (next < 0 || distance[next] >= 0 || landOnly && !walkable[next]) continue;
                distance[next] = distance[current] + 1; owner[next] = owner[current]; queue[tail++] = next;
            }
        }
        return (distance, owner);
    }

    private static int[] Components(int[] graph, bool[] walkable, out List<int> sizes)
    {
        var component = new int[walkable.Length]; Array.Fill(component, -1);
        var queue = new int[walkable.Length]; sizes = new List<int>();
        for (int tile = 0; tile < walkable.Length; tile++)
        {
            if (!walkable[tile] || component[tile] >= 0) continue;
            int head = 0, tail = 1, id = sizes.Count; queue[0] = tile; component[tile] = id;
            while (head < tail)
            {
                int current = queue[head++];
                for (int d = 0; d < 6; d++)
                {
                    int next = graph[current * 6 + d];
                    if (next < 0 || !walkable[next] || component[next] >= 0) continue;
                    component[next] = id; queue[tail++] = next;
                }
            }
            sizes.Add(tail);
        }
        return component;
    }

    private static List<(int tile, int distance)> Catchment(int origin, int[] graph, bool[] walkable, int radius)
    {
        var result = new List<(int tile, int distance)> { (origin, 0) }; var visited = new HashSet<int> { origin };
        for (int i = 0; i < result.Count; i++)
        {
            var current = result[i];
            if (current.distance == radius) continue;
            for (int d = 0; d < 6; d++)
            {
                int next = graph[current.tile * 6 + d];
                if (next >= 0 && walkable[next] && visited.Add(next)) result.Add((next, current.distance + 1));
            }
        }
        return result;
    }
}

internal sealed class AuditReport
{
    public int Schema = 1;
    public string SourceCommit;
    public ResourceGenerationOptions ResourceOptions;
    public string Scope = "Provisional early-material basket; catalog has no official Neolithic era tags.";
    public string Distances = "Hex steps; nearest sources and land paths exclude water, lakes and level-10 peaks. Geometric distance ignores intermediate barriers. Null means unreachable, not zero.";
    public string Quantities = "Latent accessible potential after discovery; shared component/site/exposure/access fractions applied. Radius stock can overlap between tribes and is not exclusive ownership or delivery capacity. Thresholds are diagnostics, not recipe/viability requirements.";
    public List<WorldReport> Worlds = new List<WorldReport>();
}
internal sealed class WorldReport
{
    public string Key, Shape, ResourceFingerprint, WorldFingerprint, ConfigurationHash, ResourceConfigurationHash;
    public int Seed, Width, Height, MaximumQuantity, LandTiles, TraversableTiles;
    public double ElapsedSeconds;
    public List<int[]> LandRuns = new List<int[]>();
    public List<CampReport> Camps = new List<CampReport>();
}
internal sealed class CampReport
{
    public int Player, Id, Tile, Component, ComponentTiles;
    public string Environment;
    public List<ResourceRow> Resources = new List<ResourceRow>();
}
internal sealed class ResourceRow
{
    public string Material, DiscoveryTechnology;
    public int? GeometricDistance, RichDistance, RichTile;
    public double BaseAccessFraction, AtCamp, Within1, Within2, Within5, Within10, ScatteredWithin2;
    public List<NearestResult> Nearest = new List<NearestResult>();
}
internal sealed class NearestResult
{
    public double MinimumAccessible, Total, Accessible, ScatteredAccessible;
    public int? LandDistance, Tile;
}
