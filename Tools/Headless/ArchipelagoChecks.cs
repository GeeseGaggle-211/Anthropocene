using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Anthropocene.Simulation;

internal static class ArchipelagoChecks
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("Archipelagos: " + message); }

    private sealed class Summary
    {
        public int Total, Land, Fragments, SubstantialFragments, Largest, CoastDistance90;
    }

    private static GenerationContext Relief(int seed, WorldShape shape, int width = 1024, int height = 512)
    {
        var config = new SimulationConfig { Width = width, Height = height, Players = 1, Shape = shape, Hotspots = 0 };
        var world = new WorldState { Seed = seed, Config = config, Tiles = new Tile[width * height], Generation = new GenerationReport() };
        var context = new GenerationContext { World = world, Map = world.Map };
        new GeometryStage().Generate(context); new PlateStage().Generate(context); new ElevationStage().Generate(context);
        return context;
    }

    private static Summary Measure(WorldState world)
    {
        var map = world.Map;
        var distances = Enumerable.Repeat(-1, world.Tiles.Length).ToArray();
        var queue = new Queue<int>();
        foreach (var tile in world.Tiles)
            if (tile.WaterBody == WaterKind.Saltwater) { distances[tile.Id] = 0; queue.Enqueue(tile.Id); }
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            foreach (int n in map.Neighbors(id))
                if (distances[n] < 0) { distances[n] = distances[id] + 1; queue.Enqueue(n); }
        }
        var coastDepths = world.Tiles.Where(t => t.Land).Select(t => distances[t.Id]).OrderBy(n => n).ToArray();
        var seen = new bool[world.Tiles.Length]; var sizes = new List<int>();
        foreach (var tile in world.Tiles)
        {
            if (!tile.Land || seen[tile.Id]) continue;
            int size = 0; queue.Enqueue(tile.Id); seen[tile.Id] = true;
            while (queue.Count > 0)
            {
                int id = queue.Dequeue(); size++;
                foreach (int n in map.Neighbors(id))
                    if (!seen[n] && world.Tiles[n].Land) { seen[n] = true; queue.Enqueue(n); }
            }
            sizes.Add(size);
        }
        return new Summary { Total = world.Tiles.Length, Land = coastDepths.Length,
            Fragments = sizes.Count(n => n >= 4), SubstantialFragments = sizes.Count(n => n >= 100), Largest = sizes.DefaultIfEmpty().Max(),
            CoastDistance90 = coastDepths.Length == 0 ? int.MaxValue : coastDepths[Math.Min(coastDepths.Length - 1, (int)(coastDepths.Length * .9))] };
    }

    private static string ElevationHash(WorldState world)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var tile in world.Tiles) writer.Write(tile.Elevation);
        writer.Flush(); using var hash = SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(stream.ToArray()));
    }

    public static void Run()
    {
        checks = 0;
        // These are reference-shape regression guards, not universal geographic quotas.
        // Disable hotspots so volcanic specks cannot disguise large continental blobs.
        foreach (int seed in new[] { 42, 1729 })
        {
            var archipelago = Measure(Relief(seed, WorldShape.Archipelagos).World);
            Check(archipelago.Land > archipelago.Total * .03 && archipelago.Land < archipelago.Total * .30, "reference archipelago has enough land to play and extensive intervening ocean");
            Check(archipelago.Fragments >= 20, "many distinct fragments remain before volcanic tracks are added");
            Check(archipelago.SubstantialFragments >= 12, "fragmentation includes habitable-sized islands rather than only one-tile specks");
            Check(archipelago.Largest < archipelago.Total * .02 && archipelago.Largest < archipelago.Land * .25, "no reference archipelago fragment dominates as a miniature continent");
            Check(archipelago.CoastDistance90 <= 12, "most archipelago land remains close to the sea instead of forming broad interiors");
            Console.WriteLine("ARCHIPELAGO_REFERENCE seed=" + seed + " Standard hotspots=0 land=" + archipelago.Land
                + " fragments4=" + archipelago.Fragments + " fragments100=" + archipelago.SubstantialFragments
                + " largest=" + archipelago.Largest + " inlandDistance90=" + archipelago.CoastDistance90);
        }

        Check(ElevationHash(Relief(42, WorldShape.Continents, 256, 128).World) == "zY3hLHxmo078RDH++SHsZc0SnHsb4Ut8UMYIk17j14o=", "ancient orogen continental redesign updates Continents elevation baseline");
        Check(ElevationHash(Relief(42, WorldShape.Pangaea, 256, 128).World) == "mjffx+EmKKUXthcyLzpGB1SAFTuM2Wwn+PbB+MMkXT0=", "ancient orogen continental redesign updates Pangaea elevation baseline");
        GaussianDistribution();
        HistoricalTracks();
        MovingWindowTracks();
        RasterizedTracks();
        Console.WriteLine("PASS: " + checks + " archipelago and historical-track assertions");
    }

    private static void GaussianDistribution()
    {
        var values = Enumerable.Range(0, 4096).Select(key => TerrainNoise.Gaussian(913, 705, key)).ToArray();
        double mean = values.Average(), deviation = Math.Sqrt(values.Average(x => (x - mean) * (x - mean)));
        Check(Enumerable.Range(0, values.Length).Reverse().All(key => values[key] == TerrainNoise.Gaussian(913, 705, key)), "Gaussian deviations are repeatable and independent of request order");
        Check(values.All(x => !double.IsNaN(x) && !double.IsInfinity(x) && Math.Abs(x) <= 6), "Gaussian deviations are bounded and finite");
        Check(Math.Abs(mean) < .10 && deviation > .75 && deviation < 1.25, "Gaussian deviations retain a centered spread instead of one-sided or uniform offsets");
        Check(values.Count(x => Math.Abs(x) > 2) > values.Length * .01 && values.Count(x => Math.Abs(x) > 2) < values.Length * .10, "Gaussian deviations include occasional wider offsets without frequent extremes");
        Console.WriteLine("GAUSSIAN_REFERENCE samples=" + values.Length + " mean=" + mean.ToString("F3") + " deviation=" + deviation.ToString("F3"));
    }

    private static void HistoricalTracks()
    {
        var active = Relief(42, WorldShape.Archipelagos, 512, 256);
        var original = active.World.Tiles.Select(t => t.Elevation).ToArray();
        active.World.Config.Hotspots = 8; ElevationStage.Hotspots(active, includeExtinct: false);
        var history = Relief(42, WorldShape.Archipelagos, 512, 256);
        history.World.Config.Hotspots = 8; ElevationStage.Hotspots(history, includeExtinct: true);
        var oldFootprint = history.World.Tiles.Where(t => t.Elevation > active.World.Tiles[t.Id].Elevation + 1e-6).ToArray();
        Check(active.World.Tiles.Count(t => t.Elevation > original[t.Id] + 1e-6) >= 12, "active tracks uplift previously unchanged ocean floor");
        Check(oldFootprint.Length >= 12, "including extinct tracks adds geography beyond the active chains");
        Check(history.World.Tiles.All(t => t.Elevation >= active.World.Tiles[t.Id].Elevation), "old tracks preserve the active terrain and add uplift");
        Check(history.World.Tiles.All(t => t.Volcanism == active.World.Tiles[t.Id].Volcanism), "extinct tracks do not acquire an active volcanic signature");
        Check(oldFootprint.Any(t => t.Elevation >= history.Config.SeaLevel && active.World.Tiles[t.Id].Elevation < history.Config.SeaLevel && t.Volcanism == 0), "some older oceanic islands emerge without active volcanism");
        var repeated = Relief(42, WorldShape.Archipelagos, 512, 256);
        repeated.World.Config.Hotspots = 8; ElevationStage.Hotspots(repeated, includeExtinct: true);
        Check(history.World.Tiles.Zip(repeated.World.Tiles, (a, b) => a.Elevation == b.Elevation && a.Volcanism == b.Volcanism).All(equal => equal), "combined active and historical tracks repeat exactly");
        Console.WriteLine("HISTORICAL_TRACK_REFERENCE seed=42 512x256 extraUpliftTiles=" + oldFootprint.Length
            + " extraExtinctLand=" + oldFootprint.Count(t => t.Elevation >= history.Config.SeaLevel && active.World.Tiles[t.Id].Elevation < history.Config.SeaLevel && t.Volcanism == 0));
    }

    private static GenerationContext TrackContext(double dx, double dy, bool seam = false)
    {
        // HotspotTrack reads only the source plate and continuous fields. A synthetic
        // origin isolates trajectory behavior from coast rejection and cone overlap.
        var config = new SimulationConfig { Width = 512, Height = 256, Players = 1, Shape = WorldShape.Archipelagos, Hotspots = 1 };
        int origin = config.Width * (config.Height / 2) + (seam ? config.Width - 4 : config.Width / 2);
        var world = new WorldState { Seed = 1729, Config = config, Tiles = new Tile[config.Width * config.Height] };
        world.Tiles[origin] = new Tile { Id = origin, Plate = 0, Environment = EnvironmentKind.Ocean, Elevation = .2f };
        return new GenerationContext { World = world, Map = world.Map, PlateCenters = new[] { origin }, PlateX = new[] { dx }, PlateY = new[] { dy } };
    }

    private static double Distance(ElevationStage.HotspotSample a, ElevationStage.HotspotSample b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static void MovingWindowTracks()
    {
        const double scale = 2; // 512-column diagnostic context, matching the generator's map scale.
        foreach (var direction in new[] { (Name: "x", X: 1.0, Y: 0.0), (Name: "y", X: 0.0, Y: 1.0), (Name: "diagonal", X: .7071067811865476, Y: .7071067811865476) })
        {
            var context = TrackContext(direction.X, direction.Y);
            var samples = ElevationStage.HotspotTrack(context, context.PlateCenters[0], 7, extinct: false);
            double extent = samples.Max(s => Distance(s, samples[0]));
            var distances = Enumerable.Range(1, samples.Count - 1).Select(i => Distance(samples[i - 1], samples[i])).ToArray();
            double maxTurn = 0;
            for (int i = 2; i < samples.Count; i++)
            {
                double ax = samples[i - 1].X - samples[i - 2].X, ay = samples[i - 1].Y - samples[i - 2].Y;
                double bx = samples[i].X - samples[i - 1].X, by = samples[i].Y - samples[i - 1].Y;
                maxTurn = Math.Max(maxTurn, Math.Abs(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by)));
            }
            int early = samples.Count / 4;
            double alongMotion = (samples[early].X - samples[0].X) * direction.X + (samples[early].Y - samples[0].Y) * direction.Y;
            double chordX = samples.Last().X - samples[0].X, chordY = samples.Last().Y - samples[0].Y;
            double chord = Math.Max(1e-9, Math.Sqrt(chordX * chordX + chordY * chordY));
            double bend = samples.Max(s => Math.Abs(chordX * (s.Y - samples[0].Y) - chordY * (s.X - samples[0].X)) / chord);
            int windows = Enumerable.Range(1, samples.Count - 2).Count(i => samples[i].Uplift > .6 && samples[i].Uplift >= samples[i - 1].Uplift && samples[i].Uplift > samples[i + 1].Uplift);
            Check(samples.All(s => new[] { s.X, s.Y, s.Age, s.Radius, s.Peak, s.Activity, s.Uplift }.All(v => !double.IsNaN(v) && !double.IsInfinity(v)) && s.Radius > 0 && s.Age >= 0 && s.Activity >= 0 && s.Activity <= 1 && s.Uplift >= 0 && s.Uplift <= 1), direction.Name + " trajectory samples remain finite with valid age and uplift");
            Check(samples.Count >= 32 && extent > 15 * scale, direction.Name + " default archipelago trajectory has substantial history and spatial extent");
            Check(distances.All(d => d > .10 * scale && d < 4 * scale) && maxTurn < 1.5, direction.Name + " successive windows drift continuously without teleportation or sharp reversals");
            Check(alongMotion > distances.Take(early).Sum() * .35, direction.Name + " initial drift responds to its specified plate direction");
            Check(bend > scale, direction.Name + " coherent regional drift bends the chain away from a straight chord");
            Check(windows >= 3 && samples.Count(s => s.Uplift < .10) >= 2, direction.Name + " moving uplift windows are separated by intervals of weak activity");
            Console.WriteLine("TRACK_REFERENCE direction=" + direction.Name + " samples=" + samples.Count + " extent=" + extent.ToString("F2")
                + " maxStep=" + distances.Max().ToString("F2") + " maxTurn=" + maxTurn.ToString("F2") + " bend=" + bend.ToString("F2") + " windows=" + windows);
        }

        var reference = TrackContext(1, 0); int source = reference.PlateCenters[0];
        var young = ElevationStage.HotspotTrack(reference, source, 7, extinct: false);
        var repeat = ElevationStage.HotspotTrack(reference, source, 7, extinct: false);
        Check(young.SequenceEqual(repeat), "continuous trajectories and moving windows repeat exactly");
        Check(young.First().Age == 0 && Enumerable.Range(1, young.Count - 1).All(i => young[i].Age > young[i - 1].Age), "age advances monotonically along the trajectory");
        Check(young.Take(young.Count / 4).Any(s => s.Activity > 0) && young.Skip(young.Count / 4).All(s => s.Activity == 0), "active volcanism is confined to the youngest part of a living chain");
        var extinct = ElevationStage.HotspotTrack(reference, source, 7, extinct: true);
        Check(extinct.All(s => s.Activity == 0) && extinct[0].Age > young[0].Age
            && Enumerable.Range(0, young.Count).Where(i => young[i].Uplift > .5).All(i => extinct[i].Peak < young[i].Peak), "extinct history is older and lower as well as inactive");
        Check(extinct.Skip(extinct.Count * 2 / 3).Any(s => s.Uplift > .5 && s.Peak < reference.Config.SeaLevel), "old uplift windows can survive as submerged seamounts");
        reference.Config.HotspotChainLength *= 2;
        var longer = ElevationStage.HotspotTrack(reference, source, 7, extinct: false);
        Check(longer.Count >= young.Count * 1.5 && longer.Take(young.Count).SequenceEqual(young)
            && longer.Max(s => Distance(s, longer[0])) > young.Max(s => Distance(s, young[0])) * 1.2, "a longer history extends the same trajectory without rerolling its earlier windows");
        var seam = TrackContext(1, 0, seam: true);
        var crossing = ElevationStage.HotspotTrack(seam, seam.PlateCenters[0], 7, extinct: false);
        Check(crossing.Any(s => s.X >= seam.Config.Width) && Enumerable.Range(1, crossing.Count - 1).All(i => Distance(crossing[i], crossing[i - 1]) < 4 * scale), "the unwrapped trajectory crosses longitude continuously before rasterization wraps it");
    }

    private static void RasterizedTracks()
    {
        // Seed 4 places its first oceanic origin near the east seam. Standard resolution
        // resolves the kernel taper; one-tile islands at small scale can legitimately have
        // a steep shoulder adjacent to the untouched seabed.
        const float floor = .20f;
        var config = new SimulationConfig { Width = 1024, Height = 512, Players = 1, Shape = WorldShape.Archipelagos, Hotspots = 1 };
        var world = new WorldState { Seed = 4, Config = config, Tiles = Enumerable.Range(0, 1024 * 512)
            .Select(id => new Tile { Id = id, Plate = 0, Environment = EnvironmentKind.Ocean, WaterBody = WaterKind.Saltwater, Elevation = floor }).ToArray() };
        var context = new GenerationContext { World = world, Map = world.Map, PlateX = new[] { 1.0 }, PlateY = new[] { 0.0 } };
        ElevationStage.Hotspots(context, includeExtinct: false);
        var raised = world.Tiles.Where(t => t.Elevation > floor + .00001).ToArray();
        var boundary = raised.Where(t => world.Map.Neighbors(t.Id).Any(id => world.Tiles[id].Elevation <= floor + .00001)).ToArray();
        Check(raised.Length >= 30 && raised.Any(t => t.Elevation < floor + .025), "rasterized uplift fades into the local deep seabed instead of starting at a shared raised shelf");
        Check(boundary.Length > 0 && boundary.All(t => t.Elevation < floor + .08), "kernel boundaries have small tapering relief rather than steep underwater road edges");
        Check(raised.Any(t => t.Id % config.Width == 0) && raised.Any(t => t.Id % config.Width == config.Width - 1), "the actual oceanic chain footprint continues on both sides of the longitude seam");
        Check(world.Tiles.Count(t => t.Elevation == floor) > world.Tiles.Length * .90 && world.Tiles.All(t => !float.IsNaN(t.Elevation) && !float.IsInfinity(t.Elevation) && t.Elevation >= floor), "localized kernels leave the distant ocean unchanged and never lower the seabed");
        Console.WriteLine("TRACK_RASTER_REFERENCE seed=4 1024x512 raised=" + raised.Length + " boundary=" + boundary.Length
            + " maxBoundaryUplift=" + boundary.Select(t => t.Elevation - floor).DefaultIfEmpty().Max().ToString("F4"));
    }
}
