using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class DrainageGenerationChecks
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("Drainage generation: " + message); }

    // A raised land mass with a single northward outlet isolates basin decisions from
    // continent generation. Explicit rainfall permits the same geometry to be wet or dry.
    private static WorldState Bowl(float runoff, int shift = 0, bool large = false)
    {
        var config = new SimulationConfig { Width = 48, Height = 24, Players = 1 };
        var world = new WorldState { Seed = 9041, Config = config, Tiles = new Tile[48 * 24] };
        for (int row = 0; row < 24; row++)
            for (int col = 0; col < 48; col++)
            {
                int id = row * 48 + col;
                world.Tiles[id] = new Tile { Id = id, Terrain = row == 0 ? Terrain.Ocean : Terrain.Grassland, Elevation = row == 0 ? .30f : .70f + row * .001f };
                for (int s = 0; s < 4; s++) world.Tiles[id].Seasons[s] = new SeasonData { Temperature = 20, Rain = runoff, Runoff = runoff };
            }
        for (int row = 1; row <= 11; row++) world.Tiles[world.Map.At(24 + shift, row)].Elevation = .45f + row * .015f;
        if (large)
        {
            int center = world.Map.At(24 + shift, 14);
            foreach (int id in world.Map.Radius(center, 3)) world.Tiles[id].Elevation = .54f + world.Map.Distance(center, id) * .001f;
        }
        else
        {
            world.Tiles[world.Map.At(24 + shift, 12)].Elevation = .55f;
            world.Tiles[world.Map.At(25 + shift, 12)].Elevation = .56f;
            world.Tiles[world.Map.At(24 + shift, 13)].Elevation = .555f;
        }
        return world;
    }

    private static void Drain(WorldState world) => new HydrologyStage().Generate(new GenerationContext { World = world, Map = world.Map });

    private static bool AllDrainToSea(WorldState world)
    {
        foreach (var tile in world.Tiles)
        {
            var visited = new HashSet<int>();
            int id = tile.Id;
            while (world.Tiles[id].Downstream >= 0)
            {
                if (!visited.Add(id)) return false;
                int next = world.Tiles[id].Downstream;
                if (!world.Map.Neighbors(id).Contains(next) || world.Tiles[next].Elevation > world.Tiles[id].Elevation + 1e-6) return false;
                id = next;
            }
            if (world.Tiles[id].WaterBody != WaterKind.Saltwater) return false;
        }
        return true;
    }

    public static void Run()
    {
        checks = 0;
        var wet = Bowl(.1f); var dry = Bowl(0); var shallow = Bowl(.1f);
        shallow.Tiles[shallow.Map.At(24, 12)].Elevation = .603f;
        shallow.Tiles[shallow.Map.At(25, 12)].Elevation = .605f;
        shallow.Tiles[shallow.Map.At(24, 13)].Elevation = .604f;
        var oldWet = wet.Tiles.Select(t => t.Elevation).ToArray();
        Drain(wet); Drain(dry); Drain(shallow);
        var lakes = wet.Tiles.Where(t => t.Environment == EnvironmentKind.Lake).ToArray();
        Check(lakes.Length == 3 && shallow.Tiles.Count(t => t.Environment == EnvironmentKind.Lake && t.DepressionDepth >= .008 && t.DepressionDepth < .018) == 3, "supplied deep and shallow bowls both retain their three submerged cells");
        Check(lakes.All(t => t.WaterBody == WaterKind.Freshwater && t.FreshwaterSource == FreshwaterSource.Lake), "retained water is freshwater with a lake source");
        Check(lakes.All(t => t.Elevation == lakes[0].Elevation && t.DepressionDepth >= .008), "a connected retained lake has a flat spill surface above its bed");
        Check(wet.Tiles.Where(t => t.WaterBody == WaterKind.Saltwater).All(t => t.FreshwaterSource == FreshwaterSource.None && t.Freshwater == 0), "lake assignment cannot make the ocean drinkable");
        Check(dry.Tiles.All(t => t.Environment != EnvironmentKind.Lake), "identical geometry without runoff does not retain a permanent lake");
        Check(dry.Tiles[dry.Map.At(24, 11)].Elevation < oldWet[dry.Map.At(24, 11)] - .05, "a dry basin cuts its obstructing spillway instead of merely hiding the lake label");
        Check(dry.Tiles[dry.Map.At(24, 12)].Elevation <= oldWet[dry.Map.At(24, 12)] + 1e-5 && dry.Tiles.All(t => !t.Land || t.DepressionDepth < .008), "the drained floor is not replaced by a deep dry fill plateau");
        Check(AllDrainToSea(wet) && AllDrainToSea(dry), "wet and dry drainage is neighbor-connected, acyclic and non-uphill to a marine outlet");
        double supplied = wet.Tiles.Sum(t => t.Seasons.Sum(s => (double)s.Runoff) * 4);
        double discharged = wet.Tiles.Where(t => t.Downstream < 0).Sum(t => (double)t.Flow);
        Check(Math.Abs(supplied - discharged) < supplied * 1e-5, "annual runoff reaches the outlets without duplication or loss, including lake catchments");
        Check(dry.Tiles.All(t => t.Flow == 0), "spillway carving cannot invent runoff");

        var large = Bowl(.1f, large: true); int largeCenter = large.Map.At(24, 14);
        float originalFloor = large.Tiles[largeCenter].Elevation;
        Drain(large);
        Check(large.Tiles.All(t => t.Environment != EnvironmentKind.Lake), "an oversized wet depression is breached instead of becoming a giant lake");
        Check(large.Tiles[largeCenter].Elevation <= originalFloor + 1e-5 && large.Tiles.All(t => !t.Land || t.DepressionDepth < .008), "large-basin repair preserves the low floor instead of flattening the flooded footprint upward");
        Check(AllDrainToSea(large), "large-basin spillway still provides valid downstream drainage");

        // Multiple dams with intervening lower terrain exercise the complete escape path,
        // including the case that reaching a local descent is not reaching the ocean.
        var nested = Bowl(0);
        for (int row = 1; row < 24; row++) nested.Tiles[nested.Map.At(24, row)].Elevation = .46f + row * .002f;
        foreach (int row in new[] { 5, 9, 13, 17 }) nested.Tiles[nested.Map.At(24, row)].Elevation = .65f + row * .001f;
        int nestedFloor = nested.Map.At(24, 21);
        float lowFloor = nested.Tiles[nestedFloor].Elevation;
        Drain(nested);
        Check(new[] { 5, 9, 13, 17 }.All(row => nested.Tiles[nested.Map.At(24, row)].Elevation < .53), "nested barriers separated by descents are all opened to the sea");
        Check(nested.Tiles[nestedFloor].Elevation <= lowFloor + 1e-5 && nested.Tiles.All(t => !t.Land || t.DepressionDepth < .008), "nested spillways do not leave a deep raised floor behind");
        Check(AllDrainToSea(nested), "nested basin repairs preserve acyclic non-uphill drainage");

        var seam = Bowl(.1f, shift: 23); Drain(seam);
        Check(seam.Tiles.Count(t => t.Environment == EnvironmentKind.Lake) == lakes.Length && new[] { seam.Map.At(47, 12), seam.Map.At(0, 12), seam.Map.At(47, 13) }.All(id => seam.Tiles[id].Environment == EnvironmentKind.Lake), "a basin crossing the longitude seam retains the same connected lake");
        Check(new[] { seam.Map.At(47, 12), seam.Map.At(0, 12), seam.Map.At(47, 13) }.All(id => seam.Tiles[id].Elevation == lakes[0].Elevation) && AllDrainToSea(seam), "seam-crossing water remains flat and drains through wrapped neighbors");

        var repeat = Bowl(.1f); Drain(repeat);
        Check(wet.Tiles.Zip(repeat.Tiles, (a, b) => a.Elevation == b.Elevation && a.Downstream == b.Downstream && a.Flow == b.Flow && a.Environment == b.Environment && a.DepressionDepth == b.DepressionDepth).All(equal => equal), "basin generation repeats exactly for identical inputs");
        Check(new[] { -.31, -.000001, 0, .271, .999999, 1.42 }.All(x => Math.Abs(TerrainNoise.Fractal(902, 340, x, .417) - TerrainNoise.Fractal(902, 340, x + 1, .417)) < 1e-12), "multi-octave terrain noise is periodic at positive and negative longitudes");
        Console.WriteLine("PASS: " + checks + " drainage and longitude assertions");
    }
}
