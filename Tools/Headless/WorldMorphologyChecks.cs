using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class WorldMorphologyChecks
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("World morphology: " + message); }

    // This is a guard for the visually reviewed Standard/Continents/1729 reference world,
    // not a demand that arbitrary seeds or Pangaea/Archipelagos satisfy Earth-like quotas.
    public static void Run()
    {
        checks = 0;
        ReferenceWorld();
        ControlledHotspots();
        Console.WriteLine("PASS: " + checks + " reference-world morphology assertions");
    }

    private static void ReferenceWorld()
    {
        var config = new SimulationConfig { Width = 1024, Height = 512, Players = 80, Shape = WorldShape.Continents };
        var world = new WorldGenerator().Generate(1729, config);
        var land = world.Tiles.Where(t => t.Land).ToArray();
        var landComponents = Components(world, t => t.Land);
        var lakeComponents = Components(world, t => t.Environment == EnvironmentKind.Lake);
        var filledComponents = Components(world, t => t.Land && t.DepressionDepth > .008);
        double total = world.Tiles.Length;
        double desert = land.Count(t => t.Environment == EnvironmentKind.Desert) / (double)land.Length;
        double forestClimate = land.Count(t => t.Environment == EnvironmentKind.Temperate || t.Environment == EnvironmentKind.Tropical) / (double)land.Length;
        double highVegetation = land.Count(t => t.CurrentVegetation == VegetationTier.High) / (double)land.Length;
        int lakeTiles = lakeComponents.Sum(c => c.Count), largestLake = lakeComponents.Select(c => c.Count).DefaultIfEmpty().Max();
        int largestFill = filledComponents.Select(c => c.Count).DefaultIfEmpty().Max();
        int straight = LongestMarineBoundary(world);
        int volcanicIslands = landComponents.Count(c => c.Count >= 4 && c.Count < total / 100 && c.Any(id => world.Tiles[id].Volcanism > 0));

        Check(land.Length / total > .15 && land.Length / total < .65, "reference world contains substantial land and open ocean");
        Check(landComponents.Count(c => c.Count >= total / 100) >= 2, "Continents reference retains multiple continental landmasses");
        Check(desert < .45, "reference climate cannot regress to a mostly desert land surface");
        Check(forestClimate >= .10, "temperate and tropical climates occupy more than narrow coastal slivers");
        Check(highVegetation >= .10, "regionally supported dense vegetation remains an extensive land habitat");
        Check(land.Count(t => t.Environment == EnvironmentKind.Grassland) > land.Length * .05, "open steppe remains present alongside forest habitats");
        Check(land.Count(t => t.Environment == EnvironmentKind.Cold || t.Environment == EnvironmentKind.Ice) > land.Length * .03, "cool and polar habitats remain represented");
        Check(lakeTiles >= 4 && lakeTiles / total < .02, "ordinary freshwater lakes remain without flooding a large share of the reference world");
        Check(largestLake / total < .002, "a single filled depression cannot become an inland sea");
        Check(largestFill / total < .002, "removing lake labels cannot conceal a vast deeply filled land plateau");
        Check(straight < 32, "coasts cannot regress to long uninterrupted geometric province boundaries");
        Check(volcanicIslands >= 4, "several separate visible islands carry volcanic terrain");
        Check(world.Camps.Count == 80 && world.Camps.Select(c => c.Player).Distinct().Count() == 80, "full production generation places all 80 distinct tribes");
        Check(world.Camps.All(c => PlacementStage.Viability(world, c.Tile) >= config.StartViability), "every placed tribe satisfies four-season renewable-food and freshwater eligibility");
        Check(world.Camps.All(a => world.Camps.All(b => a.Id == b.Id || world.Map.Distance(a.Tile, b.Tile) >= config.StartSeparation)), "morphology improvements preserve minimum start spacing");
        Check(world.Camps.All(c => world.Herds.Any(h => h.DomesticCamp < 0 && world.Map.Reachable(c.Tile, h.Tile, config.VisionRadius, id => world.Tiles[id].TraversableLand))), "all starts retain reachable opening prey");
        Console.WriteLine("MORPHOLOGY seed=1729 Standard/Continents/80 generator=" + WorldState.GeneratorVersion
            + " desertLand=" + (desert * 100).ToString("F2") + "% temperateTropicalLand=" + (forestClimate * 100).ToString("F2")
            + "% highVegetationLand=" + (highVegetation * 100).ToString("F2") + "% lakes=" + lakeTiles
            + " largestLake=" + largestLake + " largestDeepFill=" + largestFill + " straightCoast=" + straight
            + " volcanicIslands=" + volcanicIslands + " viableStarts=" + world.Camps.Count);
    }

    private static WorldState Relief(int hotspots)
    {
        var config = new SimulationConfig { Width = 256, Height = 128, Players = 1, Shape = WorldShape.Continents, Hotspots = hotspots };
        var world = new WorldState { Seed = 1729, Config = config, Tiles = new Tile[config.Width * config.Height], Generation = new GenerationReport() };
        var context = new GenerationContext { World = world, Map = world.Map };
        new GeometryStage().Generate(context); new PlateStage().Generate(context); new ElevationStage().Generate(context);
        return world;
    }

    private static void ControlledHotspots()
    {
        // Hold seed, plates and continent field constant; only enable the volcanic tracks.
        // Elevation-only worlds avoid confusing island emergence with lake classification.
        var quiet = Relief(0); var active = Relief(8);
        var emerged = new HashSet<int>(active.Tiles.Where(t => t.Land && quiet.Tiles[t.Id].WaterBody == WaterKind.Saltwater).Select(t => t.Id));
        var islands = Components(active, t => t.Land).Where(c => c.All(emerged.Contains)).ToArray();
        Check(emerged.Count >= 12, "hotspots produce new land on formerly marine cells");
        Check(islands.Length >= 4 && islands.Select(c => c.Count).Distinct().Count() >= 3, "volcanic tracks create separate islands with varied areas instead of identical repeated beads");
        Check(active.Tiles.All(t => !float.IsNaN(t.Elevation) && !float.IsInfinity(t.Elevation) && t.Elevation >= quiet.Tiles[t.Id].Elevation), "hotspot uplift stays finite and never excavates the baseline terrain");
        Check(emerged.All(id => active.Tiles[id].Volcanism > quiet.Tiles[id].Volcanism), "new volcanic islands have added volcanic activity, not unrelated coastline changes");
        Console.WriteLine("HOTSPOT_CONTROL seed=1729 256x128 tracks=0/8 emergedLand=" + emerged.Count
            + " newIslandAreas=" + string.Join(",", islands.Select(c => c.Count).OrderByDescending(n => n)));
    }

    private static List<List<int>> Components(WorldState world, Func<Tile, bool> include)
    {
        var visited = new bool[world.Tiles.Length]; var result = new List<List<int>>(); var map = world.Map;
        foreach (var tile in world.Tiles)
        {
            if (visited[tile.Id] || !include(tile)) continue;
            var component = new List<int> { tile.Id }; visited[tile.Id] = true;
            for (int i = 0; i < component.Count; i++)
                foreach (int neighbor in map.Neighbors(component[i]))
                    if (!visited[neighbor] && include(world.Tiles[neighbor])) { visited[neighbor] = true; component.Add(neighbor); }
            result.Add(component);
        }
        return result;
    }

    private static int LongestMarineBoundary(WorldState world)
    {
        // Same three-axis definition as the developer atlas. This deliberately limited
        // metric complements visual inspection; it cannot establish natural-looking coasts alone.
        int best = 0; var map = world.Map;
        int Kind(int q, int r) { int id = map.At(q, r); return id < 0 ? 0 : world.Tiles[id].Land ? 1 : world.Tiles[id].WaterBody == WaterKind.Saltwater ? -1 : 0; }
        int Boundary(int a, int b) => a != 0 && b != 0 && a != b ? a : 0;
        for (int row = 0; row < map.Height - 1; row++)
        {
            int run = 0, previous = 0;
            for (int col = 0; col < map.Width * 2; col++)
            { int edge = Boundary(Kind(col, row), Kind(col, row + 1)); run = edge == 0 ? 0 : edge == previous ? run + 1 : 1; previous = edge; best = Math.Max(best, Math.Min(run, map.Width)); }
        }
        for (int col = 0; col < map.Width; col++)
        {
            int run = 0, diagonal = 0, previous = 0, previousDiagonal = 0;
            for (int row = 0; row < map.Height; row++)
            {
                int edge = Boundary(Kind(col, row), Kind(col + 1, row)), diagonalEdge = Boundary(Kind(col - row, row), Kind(col - row + 1, row));
                run = edge == 0 ? 0 : edge == previous ? run + 1 : 1; previous = edge;
                diagonal = diagonalEdge == 0 ? 0 : diagonalEdge == previousDiagonal ? diagonal + 1 : 1; previousDiagonal = diagonalEdge;
                best = Math.Max(best, Math.Max(run, diagonal));
            }
        }
        return best;
    }
}
