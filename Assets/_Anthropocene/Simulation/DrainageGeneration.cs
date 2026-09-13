using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    internal sealed class HydrologyStage : IWorldGenerationStage
    {
        public string Name => "Eroded spillways and water-budgeted basins";
        public string Requires => "Continuous relief and precipitation";
        public string Produces => "Acyclic drainage, retained lakes and runoff";

        public void Generate(GenerationContext c)
        {
            var tiles = c.World.Tiles;
            // A routing surface is not automatically a permanent lake surface. Large barriers and
            // water-deficit basins are breached along their spill path before final drainage is assigned.
            for (int pass = 0; pass < 3; pass++)
            {
                var surface = Flood(c, out var order); Accumulate(tiles, order);
                var visited = new bool[tiles.Length]; bool changed = false;
                foreach (var tile in tiles)
                {
                    if (visited[tile.Id] || !tile.Land || surface[tile.Id] - tile.Elevation < .008) continue;
                    var basin = new List<int>(); var queue = new Queue<int>(); queue.Enqueue(tile.Id); visited[tile.Id] = true;
                    int bottom = tile.Id; double inflow = 0, evaporation = 0, depth = 0;
                    while (queue.Count > 0)
                    {
                        int id = queue.Dequeue(); var t = tiles[id]; basin.Add(id);
                        if (t.Elevation < tiles[bottom].Elevation) bottom = id;
                        depth = Math.Max(depth, surface[id] - t.Elevation); inflow = Math.Max(inflow, t.Flow);
                        evaporation += .035 + Math.Max(0, t.Seasons.Average(s => s.Temperature)) * .004;
                        foreach (int n in c.Map.Neighbors(id))
                            if (!visited[n] && tiles[n].Land && surface[n] - tiles[n].Elevation >= .008) { visited[n] = true; queue.Enqueue(n); }
                    }
                    // At world-map scale, large dams erode through spillways over geologic time.
                    // The area threshold scales with map resolution, unlike the old per-tile algorithm.
                    bool retained = basin.Count <= Math.Max(6, tiles.Length * .0007) && depth >= .008 && inflow > evaporation;
                    if (retained || pass == 2) continue;
                    double level = tiles[bottom].Elevation; int next = tiles[bottom].Downstream;
                    while (next >= 0 && tiles[next].Land)
                    {
                        var t = tiles[next]; level = Math.Max(c.Config.SeaLevel + .000001, level - .000001);
                        if (t.Elevation > level) { t.Elevation = (float)level; changed = true; }
                        else level = t.Elevation;
                        next = t.Downstream;
                    }
                }
                if (changed && pass < 2) continue;
                // Only remaining sufficiently deep, supplied basins retain water. Small routing
                // adjustments stay land; their continuous surface still drains without uphill edges.
                Array.Clear(visited, 0, visited.Length);
                foreach (var tile in tiles)
                {
                    if (visited[tile.Id] || !tile.Land || surface[tile.Id] - tile.Elevation < .008) continue;
                    var basin = new List<int>(); var queue = new Queue<int>(); queue.Enqueue(tile.Id); visited[tile.Id] = true;
                    double inflow = 0, evaporation = 0;
                    while (queue.Count > 0)
                    {
                        int id = queue.Dequeue(); var t = tiles[id]; basin.Add(id); inflow = Math.Max(inflow, t.Flow);
                        evaporation += .035 + Math.Max(0, t.Seasons.Average(s => s.Temperature)) * .004;
                        foreach (int n in c.Map.Neighbors(id))
                            if (!visited[n] && tiles[n].Land && surface[n] - tiles[n].Elevation >= .008) { visited[n] = true; queue.Enqueue(n); }
                    }
                    if (basin.Count <= Math.Max(6, tiles.Length * .0007) && inflow > evaporation)
                        foreach (int id in basin) if (surface[id] - tiles[id].Elevation >= .008) tiles[id].Terrain = Terrain.Lake;
                }
                foreach (var t in tiles)
                {
                    t.DepressionDepth = Math.Max(0, surface[t.Id] - t.Elevation);
                    t.Elevation = surface[t.Id];
                }
                break;
            }
            Geography.Classify(c.World);
        }
        private static float[] Flood(GenerationContext c, out List<int> order)
        {
            var tiles = c.World.Tiles; var surface = new float[tiles.Length]; var visited = new bool[tiles.Length];
            var heap = new StableMinHeap(); order = new List<int>(tiles.Length);
            foreach (var t in tiles)
            {
                surface[t.Id] = t.Elevation; t.Downstream = -1;
                if (!t.Land) { visited[t.Id] = true; heap.Add(t.Id, surface[t.Id]); }
            }
            if (heap.Count == 0) throw new InvalidOperationException("No marine drainage outlet.");
            while (heap.Count > 0)
            {
                int id = heap.Pop(); order.Add(id);
                foreach (int n in c.Map.Neighbors(id))
                {
                    if (visited[n]) continue;
                    visited[n] = true; tiles[n].Downstream = id; surface[n] = Math.Max(surface[n], surface[id]); heap.Add(n, surface[n]);
                }
            }
            return surface;
        }
        private static void Accumulate(Tile[] tiles, List<int> order)
        {
            foreach (var t in tiles) t.Flow = t.Seasons.Sum(s => s.Runoff) * 4;
            for (int i = order.Count - 1; i >= 0; i--)
            { var t = tiles[order[i]]; if (t.Downstream >= 0) tiles[t.Downstream].Flow += t.Flow; }
        }
    }
}
