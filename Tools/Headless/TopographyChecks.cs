using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Anthropocene.Simulation;

internal static class TopographyChecks
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("Topography: " + message); }

    public static void Run()
    {
        checks = 0;
        PlaneMathematics();
        WrappingAndPoles();
        ContourTopology();
        KnowledgeBoundary();
        PersistenceAndAuthority();
        NonContinentsSmoke();
        Console.WriteLine("PASS: " + checks + " topography/ruggedness assertions");
    }

    private static WorldState Surface(int width, int height, Func<int, int, double> elevation)
    {
        var config = new SimulationConfig { Width = width, Height = height, Players = 2, StartSeparation = 6 };
        var world = new WorldState { Config = config, Seed = 9001, Generation = new GenerationReport(), Tiles = new Tile[width * height] };
        for (int r = 0; r < height; r++)
            for (int q = 0; q < width; q++)
            {
                int id = world.Map.At(q, r);
                world.Tiles[id] = new Tile { Id = id, Environment = EnvironmentKind.Grassland, WaterBody = WaterKind.None,
                    Elevation = (float)elevation(q, r), ElevationLevel = 3, Relief = Relief.Hills };
            }
        return world;
    }

    private static void PlaneMathematics()
    {
        const int width = 32, height = 18;
        double Height(int q, int r)
        {
            TerrainPlane.Centre(q, r, out double x, out double y);
            return .42 + .012 * x - .009 * y;
        }
        var plane = Surface(width, height, Height);
        var shifted = Surface(width, height, (q, r) => Height(q, r) + .37);
        int centre = plane.Map.At(16, 9);
        var planeMetrics = TerrainMetrics.Build(plane);
        var a = planeMetrics.At(centre);
        var b = TerrainMetrics.Build(shifted).At(centre);
        Check(a.Ruggedness < .001f, "a uniform incline has approximately zero ruggedness");
        Check(Math.Abs(a.GradientX - .012) < 2e-6 && Math.Abs(a.GradientY + .009) < 2e-6, "hex-centre plane fit recovers the incline");
        Check(Math.Abs(a.Ruggedness - b.Ruggedness) < .0002f, "adding a uniform elevation offset does not change ruggedness");
        int east = plane.Map.At(17, 9);
        Check(Math.Abs(planeMetrics.RelativeHeight(centre, east) - (plane.Tiles[east].Elevation - plane.Tiles[centre].Elevation)) < 1e-8,
            "the read-only terrain interface keeps relative height distinct from fitted slope and ruggedness");
        foreach (var tile in plane.Tiles) tile.ElevationLevel = (tile.Id * 7) % 10 + 1;
        var requantized = TerrainMetrics.Build(plane).At(centre);
        Check(requantized.Ruggedness.Equals(a.Ruggedness) && requantized.GradientX.Equals(a.GradientX) && requantized.GradientY.Equals(a.GradientY),
            "ruggedness and slope are independent of global gameplay elevation quantiles");

        var flat = Surface(width, height, (_, __) => .63);
        Check(TerrainMetrics.Build(flat).At(centre).Ruggedness < 1e-5, "a flat plane scores zero");
        var broken = Surface(width, height, (q, r) => Height(q, r) + (((q * 7 + r * 11) & 1) == 0 ? .018 : -.018));
        Check(TerrainMetrics.Build(broken).At(centre).Ruggedness > 50, "local undulations increase ruggedness independently of mean height and slope");
        Check(TerrainMetrics.Score(.005) == 50, "the provisional 0.005 normalization maps an equal residual to 50");
    }

    private static void WrappingAndPoles()
    {
        const int width = 24, height = 14, shift = 7;
        double Periodic(int q, int r) => .5 + .025 * Math.Cos(2 * Math.PI * q / width) + .012 * Math.Sin(4 * Math.PI * q / width + r * .37);
        var original = Surface(width, height, Periodic);
        var rotated = Surface(width, height, (q, r) => Periodic((q - shift + width) % width, r));
        var first = TerrainMetrics.Build(original);
        var second = TerrainMetrics.Build(rotated);
        for (int r = 0; r < height; r += 3)
            for (int q = 0; q < width; q += 5)
            {
                var left = first.At(original.Map.At(q, r));
                var right = second.At(rotated.Map.At((q + shift) % width, r));
                Check(Math.Abs(left.Ruggedness - right.Ruggedness) < 1e-5 && Math.Abs(left.Slope - right.Slope) < 1e-6, "longitude rotation preserves wrapped terrain metrics");
            }
        Check(first.At(original.Map.At(0, 7)).Samples == TerrainMetrics.InteriorNeighborhoodCells, "a seam-centred interior neighborhood has 61 unique cells");
        Check(first.At(original.Map.At(8, 0)).Samples == 35 && first.At(original.Map.At(8, height - 1)).Samples == 35, "polar rows use finite partial neighborhoods");
        Check(!float.IsNaN(first.At(original.Map.At(8, 0)).Ruggedness) && !float.IsInfinity(first.At(original.Map.At(8, 0)).Ruggedness), "partial polar neighborhoods produce valid scores");
    }

    private static void ContourTopology()
    {
        var map = new HexMap(12, 8);
        var heights = new Dictionary<int, float>();
        var land = new HashSet<int>();
        for (int r = 0; r < map.Height; r++)
            for (int q = 0; q < map.Width; q++)
            {
                int id = map.At(q, r);
                heights[id] = (float)(.403 + .0171 * (q + r * .5) + .0093 * r);
                land.Add(id);
            }
        var normal = TopographicContours.Build(map, heights, land, ContourDensity.Normal, 60);
        var fine = TopographicContours.Build(map, heights, land, ContourDensity.Fine, 60);
        var distant = TopographicContours.Build(map, heights, land, ContourDensity.Fine, 20);
        Check(normal.Count > 0 && fine.Count > normal.Count && distant.Count < normal.Count, "Normal, Fine and distant zoom select nested contour detail");
        Check(Math.Abs(TopographicContours.Interval(ContourDensity.Normal, 60) - .025) < 1e-12
            && Math.Abs(TopographicContours.Interval(ContourDensity.Fine, 60) - .0125) < 1e-12
            && Math.Abs(TopographicContours.Interval(ContourDensity.Fine, 20) - .1) < 1e-12,
            "finer atlas lattice preserves the established player contour intervals");
        var shallow = heights.ToDictionary(pair => pair.Key, pair => .501f + .0003f * Math.Abs(pair.Key % map.Width - 5));
        var shallowPlayer = TopographicContours.Build(map, shallow, land, ContourDensity.Fine, 60);
        var shallowAtlas = TopographicContours.BuildAtStep(map, shallow, land, 1);
        Check(shallowPlayer.Count == 0 && shallowAtlas.Count > 0, "atlas fine contours expose shallow relief between player contour levels");
        Check(shallowAtlas.All(s => Math.Abs(s.Elevation - .5025) < 1e-10), "shallow valley contours retain absolute height calibration");

        string Key(ContourSegment segment) => segment.LevelUnits + ":" + Point(segment.Start) + ":" + Point(segment.End);
        string Point(ContourPoint point) => point.TileA + "," + point.TileB + "," + BitConverter.SingleToInt32Bits(point.T);
        var fineKeys = new HashSet<string>(fine.Select(Key));
        Check(normal.All(segment => fineKeys.Contains(Key(segment))), "Fine density retains every Normal contour at identical placement");
        var normalKeys = new HashSet<string>(normal.Select(Key));
        Check(distant.All(segment => normalKeys.Contains(Key(segment))), "zoom simplification removes detail without moving retained contours");

        float HeightAt(ContourPoint point) => point.TileA == point.TileB ? heights[point.TileA]
            : heights[point.TileA] + (heights[point.TileB] - heights[point.TileA]) * point.T;
        foreach (var segment in fine)
        {
            Check(Math.Abs(HeightAt(segment.Start) - segment.Elevation) < 2e-6 && Math.Abs(HeightAt(segment.End) - segment.Elevation) < 2e-6,
                "contour crossings interpolate the actual neighbouring centre heights");
            Check((segment.Start.TileA == segment.Start.TileB || map.Distance(segment.Start.TileA, segment.Start.TileB) == 1) &&
                  (segment.End.TileA == segment.End.TileB || map.Distance(segment.End.TileA, segment.End.TileB) == 1),
                "every crossing lies on a real wrapped neighbour edge");
        }
        var endpointUse = new Dictionary<ContourPoint, int>();
        foreach (var segment in fine)
            foreach (var point in new[] { segment.Start, segment.End }) endpointUse[point] = endpointUse.TryGetValue(point, out int count) ? count + 1 : 1;
        Check(endpointUse.Values.Any(count => count == 2), "adjacent triangles share exact endpoints for continuous linework");
        Check(fine.Select(Key).Distinct().Count() == fine.Count, "triangulation emits no duplicate contour segments");

        var seamHeights = new Dictionary<int, float>();
        foreach (int id in land) seamHeights[id] = id % map.Width == map.Width - 1 ? .45f : .55f;
        var seam = TopographicContours.Build(map, seamHeights, land, ContourDensity.Normal, 60);
        Check(seam.Any(segment => new[] { segment.Start, segment.End }.Any(point => point.TileA != point.TileB &&
            Math.Abs(point.TileA % map.Width - point.TileB % map.Width) == map.Width - 1)), "contours cross the cylindrical longitude seam through one shared edge");
    }

    private static void KnowledgeBoundary()
    {
        var world = Surface(24, 14, (q, r) => .45 + .004 * q + .003 * r + (((q + r) & 1) == 0 ? .01 : -.01));
        int centre = world.Map.At(12, 7);
        var known = world.Map.Radius(centre, 2).ToDictionary(id => id, id => world.Tiles[id].Elevation);
        var visible = new HashSet<int>(known.Keys);
        var before = KnownTerrainAnalysis.Build(world.Map, known, visible);
        var contoursBefore = TopographicContours.Build(world.Map, known, visible, ContourDensity.Fine, 60);
        int hidden = Enumerable.Range(0, world.Tiles.Length).First(id => !visible.Contains(id));
        world.Tiles[hidden].Elevation += .5f;
        var knownAfter = visible.ToDictionary(id => id, id => world.Tiles[id].Elevation);
        var after = KnownTerrainAnalysis.Build(world.Map, knownAfter, visible);
        var contoursAfter = TopographicContours.Build(world.Map, knownAfter, visible, ContourDensity.Fine, 60);
        foreach (int id in visible)
            Check(Math.Abs(before.At(id).Ruggedness - after.At(id).Ruggedness) < 1e-7 && Math.Abs(before.At(id).Slope - after.At(id).Slope) < 1e-7,
                "hidden elevation cannot change an observed terrain metric");
        Check(contoursBefore.Count == contoursAfter.Count && contoursBefore.Zip(contoursAfter, (a, b) => a.LevelUnits == b.LevelUnits && a.Start.Equals(b.Start) && a.End.Equals(b.End)).All(equal => equal),
            "hidden elevation cannot change visible contour geometry");
        Check(before.At(centre).Samples == 19 && !before.At(centre).Complete, "a radius-two view exposes a labelled partial score rather than reading the hidden radius-four ring");
        Check(before.At(centre).NeighborhoodSamples == TerrainMetrics.InteriorNeighborhoodCells, "partial metrics report their complete local sample count");
        Check(contoursBefore.All(segment => visible.Contains(segment.Start.TileA) && visible.Contains(segment.Start.TileB) && visible.Contains(segment.End.TileA) && visible.Contains(segment.End.TileB)),
            "visible contours reference no inaccessible tile centre");
    }

    private static void PersistenceAndAuthority()
    {
        var config = new SimulationConfig { Width = 48, Height = 24, Players = 2, StartSeparation = 6 };
        var world = new WorldGenerator().Generate(731, config);
        string fingerprint = SaveCodec.Fingerprint(world);
        byte[] before = SaveCodec.Save(world);
        var metrics = world.TerrainMetrics;
        Check(ReferenceEquals(metrics, world.TerrainMetrics), "whole-world ruggedness is cached once");
        Check(metrics.Count == world.Tiles.Length && metrics.ApproximateBytes == 13L * world.Tiles.Length, "cache memory is bounded to three floats and one byte per tile");
        Check(SaveCodec.Fingerprint(world) == fingerprint, "building terrain metrics does not change the authoritative fingerprint");
        byte[] after = SaveCodec.Save(world);
        Check(before.SequenceEqual(after), "derived terrain is absent from the save schema and byte stream");
        var loaded = SaveCodec.Load(before);
        Check(SaveCodec.Fingerprint(loaded) == fingerprint, "existing save/load remains authoritative");
        foreach (int id in new[] { 0, world.Map.At(17, 12), world.Tiles.Length - 1 })
        {
            var left = metrics.At(id); var right = loaded.TerrainMetrics.At(id);
            Check(left.Ruggedness.Equals(right.Ruggedness) && left.GradientX.Equals(right.GradientX) && left.GradientY.Equals(right.GradientY), "loaded elevation reconstructs identical derived metrics");
        }

        var control = SaveCodec.Load(before);
        _ = loaded.TerrainMetrics;
        var orders = control.Camps.Select(c => c.Player).Distinct().ToDictionary(player => player, _ => new List<Order>());
        new TurnResolver().Resolve(control, orders);
        new TurnResolver().Resolve(loaded, orders);
        Check(SaveCodec.Fingerprint(control) == SaveCodec.Fingerprint(loaded), "terrain caches do not change turn results");
    }

    private static void NonContinentsSmoke()
    {
        var config = new SimulationConfig { Width = 48, Height = 24, Players = 2, StartSeparation = 6, Shape = WorldShape.Pangaea };
        var world = new WorldGenerator().Generate(29, config);
        var repeat = new WorldGenerator().Generate(29, new SimulationConfig { Width = 48, Height = 24, Players = 2, StartSeparation = 6, Shape = WorldShape.Pangaea });
        string before = SaveCodec.Fingerprint(world);
        var values = world.TerrainMetrics;
        var repeatValues = repeat.TerrainMetrics;
        Check(Enumerable.Range(0, values.Count).All(id => !float.IsNaN(values.At(id).Ruggedness) && values.At(id).Ruggedness >= 0 && values.At(id).Ruggedness <= 100), "a second seed and non-Continents shape produce finite 0..100 scores");
        Check(Enumerable.Range(0, values.Count).All(id => values.At(id).Ruggedness.Equals(repeatValues.At(id).Ruggedness) && values.At(id).GradientX.Equals(repeatValues.At(id).GradientX) && values.At(id).GradientY.Equals(repeatValues.At(id).GradientY)),
            "identical seed and configuration reproduce every terrain metric exactly");
        Check(SaveCodec.Fingerprint(world) == before, "non-Continents terrain analysis is presentation-only");
    }

    public static void Benchmark()
    {
        var config = new SimulationConfig();
        PlaytestScenarios.Configure(config);
        var generation = Stopwatch.StartNew();
        var world = new WorldGenerator().Generate(PlaytestScenarios.Seed, config);
        generation.Stop();
        string before = SaveCodec.Fingerprint(world);
        var saveBaselineTimer = Stopwatch.StartNew();
        byte[] saveBaseline = SaveCodec.Save(world);
        saveBaselineTimer.Stop();
        long managedBefore = GC.GetTotalMemory(true);
        var activation = Stopwatch.StartNew();
        var metrics = world.TerrainMetrics;
        activation.Stop();
        long managedAfter = GC.GetTotalMemory(true);
        var observation = new AgentObservationBuilder().Build(world, 0);
        var knowledgeTimer = Stopwatch.StartNew();
        var knowledge = KnownTerrainAnalysis.Build(observation);
        knowledgeTimer.Stop();
        var heights = observation.Tiles.Values.Where(t => t.Land).ToDictionary(t => t.Id, t => t.Elevation);
        var land = new HashSet<int>(heights.Keys);
        var normalTimer = Stopwatch.StartNew();
        var normal = TopographicContours.Build(world.Map, heights, land, ContourDensity.Normal, 56);
        normalTimer.Stop();
        var fineTimer = Stopwatch.StartNew();
        var fine = TopographicContours.Build(world.Map, heights, land, ContourDensity.Fine, 56);
        fineTimer.Stop();
        var saveCachedTimer = Stopwatch.StartNew();
        byte[] saveCached = SaveCodec.Save(world);
        saveCachedTimer.Stop();
        var loadTimer = Stopwatch.StartNew();
        var loaded = SaveCodec.Load(saveBaseline);
        loadTimer.Stop();
        var loadDeriveTimer = Stopwatch.StartNew();
        _ = loaded.TerrainMetrics;
        loadDeriveTimer.Stop();
        Console.WriteLine("TOPOGRAPHY_BENCHMARK seed=" + world.Seed + " shape=" + world.Config.Shape + " size=" + world.Config.Width + "x" + world.Config.Height +
            " generationMs=" + generation.Elapsed.TotalMilliseconds.ToString("F1") + " ruggednessMs=" + metrics.BuildMilliseconds.ToString("F1") +
            " activationMs=" + activation.Elapsed.TotalMilliseconds.ToString("F1") + " cacheBytes=" + metrics.ApproximateBytes +
            " measuredManagedDelta=" + (managedAfter - managedBefore) + " visibleTiles=" + observation.Tiles.Count +
            " knowledgeMs=" + knowledgeTimer.Elapsed.TotalMilliseconds.ToString("F3") + " knowledgeTiles=" + knowledge.Count +
            " normalSegments=" + normal.Count + " normalMs=" + normalTimer.Elapsed.TotalMilliseconds.ToString("F3") +
            " fineSegments=" + fine.Count + " fineMs=" + fineTimer.Elapsed.TotalMilliseconds.ToString("F3") +
            " saveBytes=" + saveBaseline.Length + " saveBaselineMs=" + saveBaselineTimer.Elapsed.TotalMilliseconds.ToString("F1") +
            " saveCachedMs=" + saveCachedTimer.Elapsed.TotalMilliseconds.ToString("F1") + " saveBytesIdentical=" + saveBaseline.SequenceEqual(saveCached) +
            " loadMs=" + loadTimer.Elapsed.TotalMilliseconds.ToString("F1") + " loadDeriveMs=" + loadDeriveTimer.Elapsed.TotalMilliseconds.ToString("F1") +
            " fingerprintUnchanged=" + (before == SaveCodec.Fingerprint(world)));
    }
}
