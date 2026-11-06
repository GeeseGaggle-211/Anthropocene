using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class RiverTerrainChecks
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception("River terrain: " + message); }

    public static void Run()
    {
        checks = 0;
        Margin();
        Corners();
        foreach (int shift in new[] { 0, 23 }) Valley(shift);
        foreach (WorldShape shape in Enum.GetValues(typeof(WorldShape)))
        {
            var config = SimulationConfig.Small(4); config.Shape = shape;
            var world = new WorldGenerator().Generate(42, config);
            Check(RawDownhill(world), shape + " every drawn river descends actual terrain corners");
            Check(world.Tiles.All(t => t.Downstream < 0 || world.Tiles[t.Downstream].Elevation <= t.Elevation + 1e-6), shape + " tile drainage also remains downhill");
            Check(world.Camps.Count == 4 && world.Camps.All(c => PlacementStage.Viability(world, c.Tile) >= config.StartViability), shape + " all starts viable");
            string fingerprint = SaveCodec.Fingerprint(world);
            var loaded = SaveCodec.Load(SaveCodec.Save(world));
            Check(SaveCodec.Fingerprint(loaded) == fingerprint, shape + " eroded terrain and river surfaces round-trip exactly");
            Check(loaded.Generation.AncientMarginErosion == null && loaded.Generation.RiverTerrainErosion == null
                && loaded.Generation.RiverBankGradeTiles == 0, shape + " generation-only erosion diagnostics are excluded from saves");
            Check(SaveCodec.Fingerprint(new WorldGenerator().Generate(42, config)) == fingerprint, shape + " generation repeats exactly");
        }
        Console.WriteLine("PASS: " + checks + " river terrain and uplift-margin assertions");
    }

    private static bool RawDownhill(WorldState w)
    {
        var g = new HexCorners(w.Config.Width, w.Config.Height);
        return w.Rivers.All(e =>
        {
            double from = g.TilesAt(e.FromVertex).Average(id => (double)w.Tiles[id].Elevation);
            double to = g.TilesAt(e.ToVertex).Average(id => (double)w.Tiles[id].Elevation);
            double midpoint = g.TilesAt(e.FromVertex).Intersect(g.TilesAt(e.ToVertex)).Average(id => (double)w.Tiles[id].Elevation);
            return to <= midpoint + 1e-6 && midpoint <= from + 1e-6
                && Math.Abs(from - e.FromSurface) < 1e-6 && Math.Abs(to - e.ToSurface) < 1e-6;
        });
    }

    private static void Corners()
    {
        var g = new HexCorners(48, 24);
        var expected = Enumerable.Range(0, 48 * 24).SelectMany(id => Enumerable.Range(0, 6).Select(c => (v: g.Corner(id, c), id)))
            .GroupBy(x => x.v).ToDictionary(x => x.Key, x => x.Select(t => t.id).OrderBy(id => id).ToArray());
        Check(expected.All(p => g.TilesAt(p.Key).OrderBy(id => id).SequenceEqual(p.Value)), "corner contributors are exact, including longitude wrap and polar edges");
    }

    private static void Margin()
    {
        var w = new WorldState { Config = new SimulationConfig(), Tiles = new Tile[5], Generation = new GenerationReport() };
        w.Generation.AncientOrogenUplift = new[] { .1f, .1f, .1f, 0f, .1f };
        w.Generation.AncientOrogenStrength = new[] { 1f, .5f, .1f, 0f, .1f };
        w.Generation.AncientOrogenAge = Enumerable.Repeat(400f, 5).ToArray();
        for (int i = 0; i < 5; i++) w.Tiles[i] = new Tile { Id = i, Elevation = .7f, Terrain = i == 4 ? Terrain.Ocean : Terrain.Grassland };
        new AncientMarginErosionStage().Generate(new GenerationContext { World = w });
        Check(w.Tiles[0].Elevation > w.Tiles[1].Elevation && w.Tiles[1].Elevation > w.Tiles[2].Elevation, "equal uplift loses a larger fraction towards outer edges");
        Check(w.Tiles[0].Elevation > .68 && w.Tiles[2].Elevation > .6, "broad core survives and erosion never exceeds inherited uplift");
        Check(w.Tiles[3].Elevation == .7f && w.Tiles[4].Elevation == .7f, "unaffected terrain and sea are unchanged");
        Check(AncientMarginErosionStage.Fraction(.2, 500) > AncientMarginErosionStage.Fraction(.2, 100), "older uplift receives stronger bounded erosion");
        Check(AncientMarginErosionStage.Fraction(.2, 1000000) == AncientMarginErosionStage.Fraction(.2, 400), "geological age saturates without historical timesteps");
        w.Config.DrainageTerrainEnabled = false; var before = w.Tiles.Select(t => t.Elevation).ToArray();
        new AncientMarginErosionStage().Generate(new GenerationContext { World = w });
        Check(before.SequenceEqual(w.Tiles.Select(t => t.Elevation)), "legacy generation bypasses margin erosion");
    }

    private static void Valley(int shift)
    {
        var cfg = new SimulationConfig { Width = 48, Height = 24, Players = 1, RiverThreshold = 5 };
        var w = new WorldState { Seed = 42, Config = cfg, Tiles = new Tile[cfg.Width * cfg.Height], Generation = new GenerationReport() };
        foreach (int id in Enumerable.Range(0, w.Tiles.Length))
        {
            int r = id / cfg.Width, q = (id % cfg.Width + shift) % cfg.Width;
            double ridge = .03 * Math.Abs(q - 24) / 24;
            w.Tiles[id] = new Tile { Id = id, Elevation = r == 0 ? .3f : (float)(.46 + r * .008 + ridge), Terrain = r == 0 ? Terrain.Ocean : Terrain.Grassland };
            for (int s = 0; s < 4; s++) w.Tiles[id].Seasons[s] = new SeasonData { Runoff = .1f, Rain = .1f, Temperature = 20 };
        }
        var c = new GenerationContext { World = w, Map = w.Map };
        new HydrologyStage().Generate(c); new BoundaryRiverStage().Generate(c);
        var before = w.Tiles.Select(t => t.Elevation).ToArray();
        var water = w.Tiles.Where(t => !t.Land).Select(t => (t.Id, t.Elevation)).ToArray();
        var flow = w.Rivers.Select(e => (e.FromVertex, e.ToVertex, e.Flow, e.Mouth)).ToArray();
        new RiverTerrainStage().Generate(c);
        Check(w.Rivers.Count > 0 && w.Tiles.Any(t => t.River && t.Elevation < before[t.Id] - .001), "flow cuts real bank elevations, including wrapped valley " + shift);
        Check(RawDownhill(w), "all boundary rivers descend raw corners " + shift);
        Check(w.Tiles.All(t => t.Downstream < 0 || w.Tiles[t.Downstream].Elevation <= t.Elevation + 1e-6), "incision preserves tile-drainage descent " + shift);
        Check(water.All(t => w.Tiles[t.Id].Elevation == t.Elevation), "water datums remain fixed " + shift);
        Check(flow.SequenceEqual(w.Rivers.Select(e => (e.FromVertex, e.ToVertex, e.Flow, e.Mouth))), "incision conserves flow, connectivity and outlets " + shift);
        Check(w.Tiles.All(t => !t.Land || t.Elevation >= cfg.SeaLevel), "incision preserves terrestrial coast " + shift);
    }
}
