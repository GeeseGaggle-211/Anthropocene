using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
    private static string Option(string[] args, string key, string fallback)
    { int at = Array.IndexOf(args, key); return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback; }
    private static int Main(string[] args)
    {
        try
        {
            string output = Path.GetFullPath(Option(args, "--output", "TestResults/WorldGeneration/Atlas"));
            Directory.CreateDirectory(output);
            var shape = Enum.Parse<WorldShape>(Option(args, "--shape", "Continents"), true);
            int players = int.Parse(Option(args, "--players", "80"));
            int width = int.Parse(Option(args, "--width", "1024")), height = int.Parse(Option(args, "--height", "512"));
            bool geographyOnly = args.Contains("--geography-only"), saves = args.Contains("--save");
            var resources = new ResourceGenerationOptions { MaximumQuantity = int.Parse(Option(args, "--resource-maximum", ResourceCatalog.DefaultMaximumQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture))) };
            resources.Validate();
            var seeds = Option(args, "--seeds", "1729").Split(',').Select(int.Parse).ToArray();
            foreach (int seed in seeds)
            {
                Console.WriteLine($"Generating {shape} / {seed} / {width}x{height} / {players} requested camps ({(geographyOnly ? "geography only" : "complete world")}).");
                var config = new SimulationConfig { Width = width, Height = height, Players = players, Shape = shape, StartSeparation = width <= 48 ? 6 : width <= 96 ? 9 : 12 };
                var timer = Stopwatch.StartNew();
                var world = geographyOnly ? GenerateGeography(seed, config, resources) : new WorldGenerator().Generate(seed, config, resources);
                double generationSeconds = timer.Elapsed.TotalSeconds;
                string name = $"{shape.ToString().ToLowerInvariant()}-{seed}";
                string directory = Path.Combine(output, name); Directory.CreateDirectory(directory);
                var metrics = Metrics(world, geographyOnly, generationSeconds);
                WriteJson(Path.Combine(directory, "metrics.json"), metrics);
                WriteJson(Path.Combine(directory, "config.json"), config);
                WriteJson(Path.Combine(directory, "camps.json"), Camps(world));
                foreach (string layer in new[] { "environment", "elevation", "food", "freshwater", "volcanism" })
                    Png.Write(Path.Combine(directory, layer + ".png"), width, height, Image(world, layer));
                var resourceMaps = GeologyAtlas.Export(world, directory);
                bool hasSave = saves && !geographyOnly;
                if (hasSave) { File.WriteAllBytes(Path.Combine(directory, "world.anthro"), SaveCodec.Save(world)); }
                WriteJson(Path.Combine(directory, "manifest.json"), new {
                    FormatVersion = 1, Name = name, Seed = seed, Shape = shape.ToString(),
                    GeneratorVersion = world.Generation.GeneratorVersion, WorldVersion = world.Version, ConfigurationVersion = config.Version,
                    ConfigurationHash = world.Generation.ConfigurationHash,
                    SimulationSourceSha256 = Option(args, "--source-sha256", "unavailable: direct DLL invocation"),
                    SimulationSourceDescription = Option(args, "--source-description", "not supplied"),
                    ExportAssemblySha256 = FileHash(typeof(Program).Assembly.Location),
                    ExportedUtc = DateTimeOffset.UtcNow.ToString("o"), GenerationSeconds = generationSeconds,
                    GeographyOnly = geographyOnly, PlacedCamps = world.Camps.Count,
                    ConfigurationFile = "config.json", ConfigurationFileSha256 = FileHash(Path.Combine(directory, "config.json")),
                    MetricsFile = "metrics.json", CampsFile = "camps.json", GalleryFile = "index.html",
                    WorldSaveFile = hasSave ? "world.anthro" : null,
                    WorldSaveSha256 = hasSave ? FileHash(Path.Combine(directory, "world.anthro")) : null,
                    ResourceGenerationVersion = world.Resources?.GenerationVersion, ResourceSchemaVersion = world.Resources?.Version, ResourceFingerprint = world.Resources == null ? null : ResourceCodec.Fingerprint(world.Resources),
                    ResourceCatalogHash = world.Resources?.CatalogHash, ResourceConfigurationHash = world.Resources?.ConfigurationHash,
                    ResourceReportFile = "geology.json", ResourceGalleryFile = "geology.html",
                    MapFiles = new[] { "environment.png", "elevation.png", "food.png", "freshwater.png", "volcanism.png" }.Concat(resourceMaps).ToArray()
                });
                WritePage(directory, name, world, metrics, saves && !geographyOnly);
                Console.WriteLine($"Exported {name}: {generationSeconds:F2}s generation; {world.Camps.Count} camps. {directory}");
                Console.WriteLine($"  Land {metrics["LandPercentOfWorld"]:F2}%; lakes {metrics["LakePercentOfWorld"]:F3}%; continents {metrics["ContinentCount"]}; islands >=4 tiles {metrics["IslandsAtLeast4Tiles"]}; largest residual filled-land patch {metrics["LargestResidualFilledLandComponentTiles"]} tiles.");
                // Large worlds must be reclaimed before generating the next seed.
                world = null; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            }
            // Repeated seed/shape invocations form one gallery. Keep manifests from earlier runs;
            // only an explicitly regenerated world's own files are replaced.
            var entries = new List<JsonElement>();
            foreach (string child in Directory.EnumerateDirectories(output).OrderBy(x => x, StringComparer.Ordinal))
            {
                string manifestPath = Path.Combine(child, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    entries.Add(document.RootElement.Clone());
                }
                else if (File.Exists(Path.Combine(child, "metrics.json")) && File.Exists(Path.Combine(child, "index.html")))
                {
                    // Older exports remain inspectable, without claiming provenance they lack.
                    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(child, "metrics.json")));
                    var metrics = document.RootElement;
                    entries.Add(JsonSerializer.SerializeToElement(new {
                        Name = Path.GetFileName(child), Seed = metrics.GetProperty("Seed").GetInt32(), Shape = metrics.GetProperty("Shape").GetString(),
                        GeneratorVersion = metrics.GetProperty("GeneratorVersion").GetInt32(),
                        MetricsFile = "metrics.json", ConfigurationFile = "config.json", GalleryFile = "index.html",
                        Provenance = "Legacy export: no manifest or file fingerprint was recorded."
                    }, Json));
                }
            }
            WriteJson(Path.Combine(output, "index.json"), entries);
            string cards = string.Join("", entries.Select(e => {
                string name = e.GetProperty("Name").GetString(), path = Uri.EscapeDataString(name), label = System.Net.WebUtility.HtmlEncode(name);
                return $"<article><h2><a href='{path}/index.html'>{label}</a></h2><a href='{path}/index.html'><img src='{path}/environment.png' alt='Actual generated environment map for {label}'></a></article>";
            }));
            File.WriteAllText(Path.Combine(output, "index.html"), Page("Anthropocene generated worlds", "<h1>Anthropocene generated worlds</h1><p>Actual simulation output. Open a world for topography, environment, renewable food, freshwater, volcanic activity and measured geography.</p>" + cards));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static WorldState GenerateGeography(int seed, SimulationConfig config, ResourceGenerationOptions resources)
    {
        config.Validate();
        var world = new WorldState { Seed = seed, Config = config, Tiles = new Tile[config.Width * config.Height], Generation = new GenerationReport() };
        world.Generation.ConfigurationHash = SaveCodec.ConfigurationHash(config);
        var context = new GenerationContext { World = world, Map = world.Map, ResourceOptions = resources };
        var stages = WorldGenerator.CreateStages(false);
        foreach (var stage in stages)
        {
            var timer = Stopwatch.StartNew(); stage.Generate(context);
            world.Generation.Stages.Add(stage.Name); world.Generation.Milliseconds.Add(timer.Elapsed.TotalMilliseconds);
            Console.WriteLine($"  {stage.Name}: {timer.Elapsed.TotalSeconds:F2}s");
        }
        return world;
    }

    private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    private static string FileHash(string path)
    { using var input = File.OpenRead(path); using var hash = SHA256.Create(); return Convert.ToHexString(hash.ComputeHash(input)); }
    private static object[] Camps(WorldState w) => w.Camps.OrderBy(c => c.Player).Select(c => {
        var tile = w.Tiles[c.Tile]; var near = w.Map.Radius(c.Tile, 1).Select(i => w.Tiles[i]).ToArray();
        return (object)new {
            c.Player, c.Id, c.Tile, Column = c.Tile % w.Config.Width, Row = c.Tile / w.Config.Width, c.Population,
            Environment = tile.Environment.ToString(), Vegetation = tile.CurrentVegetation.ToString(), tile.River,
            MeanTemperature = tile.Seasons.Average(s => s.Temperature), RegionalMoisture = tile.Seasons.Average(s => s.RegionalMoisture),
            ViabilityPercent = PlacementStage.Viability(w, c.Tile),
            MeanRenewableFoodInGatherRadius = near.Sum(t => PlaytestScenarios.Food(w, t, -1)),
            MeanLandRenewableFood = near.Where(t => t.Land).Sum(t => PlaytestScenarios.Food(w, t, -1)),
            MeanAquaticRenewableFood = near.Where(t => t.Aquatic).Sum(t => PlaytestScenarios.Food(w, t, -1)),
            MeanFreshwaterRecharge = near.Sum(t => PlaytestScenarios.Water(w, t, -1)),
            SeasonalFood = Enumerable.Range(0, 4).Select(s => near.Sum(t => PlaytestScenarios.Food(w, t, s))).ToArray(),
            AdjacentShallows = near.Count(t => t.Environment == EnvironmentKind.Shallows),
            NearbyHerdSpecies = w.Herds.Where(h => w.Map.Reachable(c.Tile, h.Tile, 2, n => w.Tiles[n].TraversableLand)).Select(h => h.Species).Distinct().OrderBy(x => x).ToArray()
        };
    }).ToArray();

    private static Dictionary<string, object> Metrics(WorldState w, bool geographyOnly, double seconds)
    {
        var tiles = w.Tiles; int count = tiles.Length, land = tiles.Count(t => t.Land);
        var landComponents = Components(w, t => t.Land);
        var marineComponents = Components(w, t => t.WaterBody == WaterKind.Saltwater);
        var lakeComponents = Components(w, t => t.Environment == EnvironmentKind.Lake);
        var desertComponents = Components(w, t => t.Environment == EnvironmentKind.Desert);
        var filledLandComponents = Components(w, t => t.Land && t.DepressionDepth > .008);
        int continentLimit = Math.Max(20, count / 100);
        double[] startScores = w.Camps.Select(c => PlacementStage.Viability(w, c.Tile)).ToArray();
        return new Dictionary<string, object> {
            ["Seed"] = w.Seed, ["GeneratorVersion"] = w.Generation.GeneratorVersion, ["ConfigurationHash"] = w.Generation.ConfigurationHash,
            ["Width"] = w.Config.Width, ["Height"] = w.Config.Height, ["Shape"] = w.Config.Shape.ToString(), ["GeographyOnly"] = geographyOnly,
            ["GenerationSeconds"] = seconds, ["LandTiles"] = land, ["LandPercentOfWorld"] = 100.0 * land / count,
            ["OceanAndShallowsPercentOfWorld"] = 100.0 * tiles.Count(t => t.WaterBody == WaterKind.Saltwater) / count,
            ["LakePercentOfWorld"] = 100.0 * tiles.Count(t => t.Environment == EnvironmentKind.Lake) / count,
            ["EnvironmentPercentOfLand"] = tiles.Where(t => t.Land).GroupBy(t => t.Environment).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => 100.0 * g.Count() / Math.Max(1, land)),
            ["VegetationPercentOfLand"] = tiles.Where(t => t.Land).GroupBy(t => t.CurrentVegetation).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => 100.0 * g.Count() / Math.Max(1, land)),
            ["LandComponentSizesDescending"] = landComponents, ["MarineComponentSizesDescending"] = marineComponents,
            ["LakeComponentSizesDescending"] = lakeComponents, ["LargestDesertTiles"] = desertComponents.FirstOrDefault(),
            ["ResidualFilledLandDepthThreshold"] = .008,
            ["ResidualFilledLandTiles"] = tiles.Count(t => t.Land && t.DepressionDepth > .008),
            ["ResidualFilledLandPercentOfLand"] = 100.0 * tiles.Count(t => t.Land && t.DepressionDepth > .008) / Math.Max(1, land),
            ["ResidualFilledLandMaximumDepth"] = tiles.Where(t => t.Land).Max(t => t.DepressionDepth),
            ["ResidualFilledLandComponentSizesDescending"] = filledLandComponents,
            ["LargestResidualFilledLandComponentTiles"] = filledLandComponents.FirstOrDefault(),
            ["ContinentMinimumTiles"] = continentLimit, ["ContinentCount"] = landComponents.Count(n => n >= continentLimit),
            ["IslandCount"] = landComponents.Count(n => n < continentLimit), ["IslandsAtLeast4Tiles"] = landComponents.Count(n => n >= 4 && n < continentLimit),
            ["LargestLandComponentPercentOfLand"] = 100.0 * landComponents.FirstOrDefault() / Math.Max(1, land),
            ["LongestAxisAlignedMarineBoundary"] = LongestBoundary(w),
            ["CoastalLandTiles"] = tiles.Count(t => t.Land && w.Map.Neighbors(t.Id).Any(n => tiles[n].WaterBody == WaterKind.Saltwater)),
            ["FreshwaterSourceLandPercent"] = 100.0 * tiles.Count(t => t.Land && t.FreshwaterSource != FreshwaterSource.None) / Math.Max(1, land),
            ["RiverEdgeCount"] = w.Rivers.Count, ["RiverLandTiles"] = tiles.Count(t => t.Land && t.River),
            ["VolcanismPositiveLandTiles"] = tiles.Count(t => t.Land && t.Volcanism > 0),
            ["VolcanismAbovePoint2LandTiles"] = tiles.Count(t => t.Land && t.Volcanism >= .2),
            ["MaximumVolcanism"] = tiles.Max(t => t.Volcanism),
            ["PlacedCamps"] = w.Camps.Count, ["ViablePlacedCamps"] = startScores.Count(s => s >= w.Config.StartViability),
            ["MinimumStartViabilityPercent"] = startScores.Length == 0 ? (double?)null : startScores.Min(),
            ["MeanStartViabilityPercent"] = startScores.Length == 0 ? (double?)null : startScores.Average(),
            ["MeanLandRegionalMoisture"] = tiles.Where(t => t.Land).Average(t => t.Seasons.Average(s => (double)s.RegionalMoisture)),
            ["MeanLandRenewableFoodPerSeason"] = tiles.Where(t => t.Land).Average(t => PlaytestScenarios.Food(w, t, -1)),
            ["Stages"] = w.Generation.Stages, ["StageMilliseconds"] = w.Generation.Milliseconds,
            ["Definitions"] = new {
                Components = "Six-neighbor axial hex connectivity; longitude wraps, latitude does not. Land excludes lakes. Marine includes Ocean and Shallows. Lists contain tile counts, descending.",
                StraightBoundary = "Maximum repeated consecutive land-versus-saltwater edge on the three hex edge directions. Map pole edges excluded; measured in tile edges, not physical distance.",
                ResidualFilledLand = "Final DepressionDepth greater than 0.008 on land only, excluding Lake/sea; connected components use six-neighbor wrapped hex topology. Depth is normalized elevation, not metres. Maximum depth covers all land, including values below the component threshold.",
                Food = "Mean of all four stored seasons: MaxFood * FoodRecoveryPerDay * BaseDays * SustainableProductionFraction, before labor, depletion and competition. Ocean and level-10 peaks excluded.",
                Freshwater = "Mean stored freshwater recharge per base season; nonsaline sourced capacity only. Land percentage means source present, not proof of water sufficiency.",
                Volcanism = "Stored generation Volcanism diagnostic; not a count of distinct hotspots. A volcanic land tile may be on a continent or an island.",
                Projection = "One pixel per tile in native axial q/r coordinates, cylindrical longitude. This is a topology audit image, not an equal-area planetary projection. North at top."
            }
        };
    }

    private static int[] Components(WorldState w, Func<Tile, bool> include)
    {
        var visited = new bool[w.Tiles.Length]; var queue = new Queue<int>(); var sizes = new List<int>(); var map = w.Map;
        foreach (var tile in w.Tiles)
        {
            if (visited[tile.Id] || !include(tile)) continue;
            visited[tile.Id] = true; queue.Enqueue(tile.Id); int size = 0;
            while (queue.Count > 0)
            {
                int id = queue.Dequeue(); size++;
                foreach (int n in map.Neighbors(id)) if (!visited[n] && include(w.Tiles[n])) { visited[n] = true; queue.Enqueue(n); }
            }
            sizes.Add(size);
        }
        sizes.Sort((a, b) => b.CompareTo(a)); return sizes.ToArray();
    }

    private static int LongestBoundary(WorldState w)
    {
        int best = 0; var map = w.Map;
        int Kind(int q, int r) { int id = map.At(q, r); return id < 0 ? 0 : w.Tiles[id].Land ? 1 : w.Tiles[id].WaterBody == WaterKind.Saltwater ? -1 : 0; }
        int Boundary(int a, int b) => a != 0 && b != 0 && a != b ? a : 0;
        // These are literal axis runs; curvature and diagonal coastlines need visual inspection too.
        for (int r = 0; r < map.Height - 1; r++)
        {
            int run = 0, previous = 0;
            for (int q = 0; q < map.Width * 2; q++) { int edge = Boundary(Kind(q, r), Kind(q, r + 1)); run = edge == 0 ? 0 : edge == previous ? run + 1 : 1; previous = edge; best = Math.Max(best, Math.Min(run, map.Width)); }
        }
        for (int q = 0; q < map.Width; q++)
        {
            int run = 0, diagonal = 0, previous = 0, previousDiagonal = 0;
            for (int r = 0; r < map.Height; r++)
            {
                int edge = Boundary(Kind(q, r), Kind(q + 1, r)), diagonalEdge = Boundary(Kind(q - r, r), Kind(q - r + 1, r));
                run = edge == 0 ? 0 : edge == previous ? run + 1 : 1; previous = edge;
                diagonal = diagonalEdge == 0 ? 0 : diagonalEdge == previousDiagonal ? diagonal + 1 : 1; previousDiagonal = diagonalEdge;
                best = Math.Max(best, Math.Max(run, diagonal));
            }
        }
        return best;
    }

    private static byte[] Image(WorldState w, string layer)
    {
        var pixels = new byte[w.Tiles.Length * 3];
        foreach (var tile in w.Tiles)
        {
            var color = Color(w, tile, layer); int index = tile.Id * 3;
            pixels[index] = (byte)color.r; pixels[index + 1] = (byte)color.g; pixels[index + 2] = (byte)color.b;
        }
        return pixels;
    }
    private static (int r, int g, int b) Blend((int r, int g, int b) a, (int r, int g, int b) b, double t)
    { t = Math.Max(0, Math.Min(1, t)); return ((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t)); }
    private static (int r, int g, int b) Color(WorldState w, Tile t, string layer)
    {
        bool marine = t.WaterBody == WaterKind.Saltwater;
        if (layer == "food") return marine && t.Environment == EnvironmentKind.Ocean ? (11, 29, 49) : Blend((40, 36, 45), (246, 216, 89), Math.Log(1 + PlaytestScenarios.Food(w, t, -1)) / Math.Log(101));
        if (layer == "freshwater") return marine ? (13, 29, 48) : Blend((81, 69, 48), (88, 221, 246), Math.Log(1 + PlaytestScenarios.Water(w, t, -1)) / Math.Log(101));
        if (layer == "volcanism") return t.Volcanism > 0 ? Blend((111, 42, 50), (255, 226, 92), t.Volcanism) : t.Land ? (74, 92, 76) : (14, 36, 58);
        if (layer == "elevation")
        {
            if (marine) return Blend((9, 29, 59), (35, 125, 148), t.Elevation / w.Config.SeaLevel);
            if (t.Environment == EnvironmentKind.Lake) return (61, 142, 176);
            double altitude = (t.Elevation - w.Config.SeaLevel) / (1 - w.Config.SeaLevel);
            return altitude < .45 ? Blend((93, 133, 91), (166, 146, 109), altitude / .45) : Blend((166, 146, 109), (243, 241, 224), (altitude - .45) / .55);
        }
        switch (t.Environment)
        {
            case EnvironmentKind.Ocean: return (17, 43, 71);
            case EnvironmentKind.Shallows: return (42, 121, 148);
            case EnvironmentKind.Lake: return (84, 164, 199);
            case EnvironmentKind.Desert: return t.CurrentVegetation == VegetationTier.Medium ? (176, 172, 103) : (209, 181, 119);
            case EnvironmentKind.Grassland: return (155, 166, 98);
            case EnvironmentKind.Temperate: return t.CurrentVegetation == VegetationTier.High ? (56, 112, 72) : (89, 138, 87);
            case EnvironmentKind.Tropical: return (34, 94, 70);
            case EnvironmentKind.Cold: return t.CurrentVegetation == VegetationTier.High ? (90, 127, 118) : (160, 169, 155);
            default: return (228, 236, 228);
        }
    }

    private static void WritePage(string directory, string name, WorldState w, Dictionary<string, object> metrics, bool save)
    {
        string details = $"<h1>{name}</h1><p>Generator {w.Generation.GeneratorVersion}; {w.Config.Width} × {w.Config.Height}; {w.Camps.Count} placed camps. Native axial coordinates. Longitude wraps; the left and right edges join.</p><p><a href='geology.html'>Geological resource maps</a> · <a href='metrics.json'>Measured geography</a> · <a href='config.json'>Exact tuning</a> · <a href='camps.json'>Start food and freshwater</a> · <a href='manifest.json'>Source, configuration and save fingerprints</a>" + (save ? " · <a href='world.anthro'>Playable save copy</a>" : "") + "</p>";
        foreach (var entry in new[] {
            ("environment", "Environment", "Gold: desert. Pale green: grassland. Green: temperate forest. Dark green: tropical forest. Grey-green: cold. White: ice. Light blue: lakes. Turquoise: shallows. Dark blue: ocean."),
            ("elevation", "Topography", "Marine depth in blue; low land in green; uplands brown; highest elevations pale. Lakes use a distinct blue. Continuous height, not movement levels."),
            ("food", "Renewable human food", "Mean four-season profile potential before workers and competition. Dark: zero. Gold: 100 or more food units/base season. Logarithmic colour scale shared by every map."),
            ("freshwater", "Freshwater recharge", "Brown: zero. Bright cyan: 100 or more freshwater units/base season. Dark blue: saline water, excluded. Shared logarithmic scale."),
            ("volcanism", "Volcanic diagnostic", "Muted green/blue: no volcanic diagnostic. Red through yellow: increasing stored Volcanism, 0 to 1; neither volcano icons nor artificial islands are painted onto this image.") })
            details += $"<section><h2>{entry.Item2}</h2><p>{entry.Item3}</p><a href='{entry.Item1}.png'><img src='{entry.Item1}.png' alt='{entry.Item2} for {name}'></a></section>";
        details += "<h2>Measured geography</h2><pre>" + System.Net.WebUtility.HtmlEncode(JsonSerializer.Serialize(metrics, Json)) + "</pre>";
        File.WriteAllText(Path.Combine(directory, "index.html"), Page(name, details));
    }
    private static string Page(string title, string body) => "<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><title>" + title + "</title><style>body{margin:32px auto;padding:0 24px;max-width:1500px;background:#102027;color:#e4e9e1;font:16px/1.55 system-ui,sans-serif}h1,h2{line-height:1.2}a{color:#b7dacc}img{display:block;width:100%;height:auto;image-rendering:pixelated;border:1px solid #45616a}section,article{margin:40px 0}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#182e36;padding:24px;font:13px/1.45 monospace}p{max-width:1050px}</style><body>" + body + "</body></html>";
}

// Minimal RGB PNG writer: no graphics libraries, platform APIs, installed packages or network required.
internal static class Png
{
    public static void Write(string path, int width, int height, byte[] rgb)
    {
        using var stream = File.Create(path);
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; Big(header, 0, (uint)width); Big(header, 4, (uint)height); header[8] = 8; header[9] = 2;
        Chunk(stream, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (int row = 0; row < height; row++) { zip.WriteByte(0); zip.Write(rgb, row * width * 3, width * 3); }
        Chunk(stream, "IDAT", compressed.ToArray()); Chunk(stream, "IEND", Array.Empty<byte>());
    }
    private static void Big(byte[] bytes, int offset, uint value)
    { bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16); bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value; }
    private static void Chunk(Stream output, string type, byte[] bytes)
    {
        var name = Encoding.ASCII.GetBytes(type); var length = new byte[4]; Big(length, 0, (uint)bytes.Length); output.Write(length); output.Write(name); output.Write(bytes);
        uint crc = 0xffffffff;
        foreach (byte item in name.Concat(bytes)) { crc ^= item; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320U : 0); }
        Big(length, 0, crc ^ 0xffffffff); output.Write(length);
    }
}
