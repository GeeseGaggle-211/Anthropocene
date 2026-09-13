using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class RiverEdge
    {
        public int Id, FromVertex, ToVertex, TileA = -1, TileB = -1, SideA = -1, SideB = -1;
        public float Flow, Width, FromSurface, ToSurface;
        public bool Mouth;
    }
    // Exact integer corner lattice for pointy-top axial hexes. Longitude wraps in corner space too.
    public sealed class HexCorners
    {
        private readonly int width, height;
        private static readonly int[] X = { 0, 1, 1, 0, -1, -1 }, Y = { -2, -1, 1, 2, 1, -1 };
        public int Capacity => (3 * height + 2) * 2 * width;
        public HexCorners(int width, int height) { this.width = width; this.height = height; }
        public int At(int x, int y) => y < -2 || y > 3 * height - 1 ? -1 : (y + 2) * 2 * width + ((x % (2 * width)) + 2 * width) % (2 * width);
        public int Corner(int tile, int corner) => At(2 * (tile % width) + tile / width + X[corner], 3 * (tile / width) + Y[corner]);
        public int Column(int vertex) => vertex % (2 * width);
        public int Row(int vertex) => vertex / (2 * width) - 2;
        public IEnumerable<int> Neighbors(int vertex)
        {
            int x = Column(vertex), y = Row(vertex), direction = ((y % 3) + 3) % 3 == 1 ? 1 : -1;
            int a = At(x - 1, y + direction), b = At(x + 1, y + direction), c = At(x, y - 2 * direction);
            if (a >= 0) yield return a; if (b >= 0) yield return b; if (c >= 0) yield return c;
        }
        public static ulong EdgeKey(int a, int b) => ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
        // Adjacent corner IDs make ulong's default (high XOR low) hash collapse into a few
        // buckets. Mix all bits before lookup; wet worlds contain far more river edges.
        internal static readonly IEqualityComparer<ulong> EdgeComparer = new MixedEdgeComparer();
        private sealed class MixedEdgeComparer : IEqualityComparer<ulong>
        {
            public bool Equals(ulong a, ulong b) => a == b;
            public int GetHashCode(ulong value)
            {
                unchecked { value ^= value >> 30; value *= 0xbf58476d1ce4e5b9UL; value ^= value >> 27; value *= 0x94d049bb133111ebUL; value ^= value >> 31; return (int)value; }
            }
        }
    }
    internal sealed class StableMinHeap
    {
        private readonly List<KeyValuePair<int, float>> items = new List<KeyValuePair<int, float>>();
        public int Count => items.Count;
        private static bool Less(KeyValuePair<int, float> a, KeyValuePair<int, float> b) => a.Value < b.Value || a.Value == b.Value && a.Key < b.Key;
        public void Add(int id, float priority)
        {
            var item = new KeyValuePair<int, float>(id, priority); items.Add(item); int i = items.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (!Less(item, items[p])) break; items[i] = items[p]; i = p; } items[i] = item;
        }
        public int Pop()
        {
            int result = items[0].Key; var last = items[items.Count - 1]; items.RemoveAt(items.Count - 1); if (items.Count == 0) return result;
            int i = 0;
            while (i * 2 + 1 < items.Count)
            { int child = i * 2 + 1; if (child + 1 < items.Count && Less(items[child + 1], items[child])) child++; if (!Less(items[child], last)) break; items[i] = items[child]; i = child; }
            items[i] = last; return result;
        }
    }
    public static class Geography
    {
        public static void Classify(WorldState w)
        {
            var map = w.Map;
            // Only sea becomes coast. Lakes remain freshwater and are never folded into marine geometry.
            foreach (var tile in w.Tiles)
                if (tile.Terrain == Terrain.Ocean && map.Neighbors(tile.Id).Any(n => w.Tiles[n].Land)) tile.Terrain = Terrain.Shallows;
            foreach (var tile in w.Tiles)
            {
                tile.WaterBody = tile.Terrain == Terrain.Ocean || tile.Terrain == Terrain.Shallows ? WaterKind.Saltwater : tile.Terrain == Terrain.Lake ? WaterKind.Freshwater : WaterKind.None;
                if (tile.WaterBody == WaterKind.Saltwater) { tile.Freshwater = 0; tile.FreshwaterSource = FreshwaterSource.None; }
                else if (tile.Terrain == Terrain.Lake) tile.FreshwaterSource = FreshwaterSource.Lake;
            }
        }
        public static void Validate(WorldState w)
        {
            foreach (var tile in w.Tiles)
            {
                if (tile.Terrain == Terrain.Ocean && w.Map.Neighbors(tile.Id).Any(n => w.Tiles[n].Land)) throw new InvalidOperationException("Deep sea touches land at " + tile.Id);
                if (tile.WaterBody == WaterKind.Saltwater && (tile.Freshwater != 0 || tile.FreshwaterSource != FreshwaterSource.None || tile.Seasons.Any(s => s.MaxFreshwater != 0))) throw new InvalidOperationException("Saltwater cannot supply freshwater.");
                if (tile.Terrain == Terrain.Lake && tile.WaterBody != WaterKind.Freshwater) throw new InvalidOperationException("Lake lost its freshwater classification.");
            }
        }
    }
    internal sealed class BoundaryRiverStage : IWorldGenerationStage
    {
        public string Name => "Dual-graph boundary rivers";
        public string Requires => "Continuous elevation, runoff, resolved lakes and coast";
        public string Produces => "Connected directed river edges on shared hex boundaries";
        public void Generate(GenerationContext context)
        {
            var w = context.World; Geography.Classify(w);
            var geometry = new HexCorners(w.Config.Width, w.Config.Height); int size = geometry.Capacity;
            var contributors = new byte[size]; var surface = new float[size]; var flow = new float[size]; var outlet = new bool[size];
            foreach (var tile in w.Tiles)
            {
                float runoff = tile.Seasons.Sum(s => s.Runoff) * (4f / 6f);
                for (int c = 0; c < 6; c++)
                {
                    int vertex = geometry.Corner(tile.Id, c); contributors[vertex]++; surface[vertex] += tile.Elevation; flow[vertex] += runoff;
                    if (!tile.Land) outlet[vertex] = true;
                }
            }
            var parent = new int[size]; Array.Fill(parent, -1); var visited = new bool[size]; var order = new List<int>(); var heap = new StableMinHeap();
            for (int v = 0; v < size; v++)
            {
                if (contributors[v] == 0) continue; surface[v] /= contributors[v];
                if (outlet[v]) { visited[v] = true; heap.Add(v, surface[v]); }
            }
            while (heap.Count > 0)
            {
                int v = heap.Pop(); order.Add(v);
                foreach (int n in geometry.Neighbors(v))
                {
                    if (contributors[n] == 0 || visited[n]) continue;
                    visited[n] = true; parent[n] = v; surface[n] = Math.Max(surface[n], surface[v]); heap.Add(n, surface[n]);
                }
            }
            for (int i = order.Count - 1; i >= 0; i--) { int v = order[i]; if (parent[v] >= 0) flow[parent[v]] += flow[v]; }
            w.Rivers.Clear();
            for (int v = 0; v < size; v++)
                if (parent[v] >= 0 && flow[v] >= w.Config.RiverThreshold)
                    w.Rivers.Add(new RiverEdge { Id = w.Rivers.Count, FromVertex = v, ToVertex = parent[v], Flow = flow[v], Width = (float)Numbers.Clamp(Math.Sqrt(flow[v] / w.Config.RiverThreshold), 1, 5), FromSurface = surface[v], ToSurface = surface[parent[v]], Mouth = outlet[parent[v]] });
            var edges = w.Rivers.ToDictionary(e => HexCorners.EdgeKey(e.FromVertex, e.ToVertex), HexCorners.EdgeComparer);
            foreach (var tile in w.Tiles)
                for (int side = 0; side < 6; side++)
                {
                    ulong key = HexCorners.EdgeKey(geometry.Corner(tile.Id, side), geometry.Corner(tile.Id, (side + 1) % 6));
                    if (!edges.TryGetValue(key, out var edge)) continue;
                    if (tile.RiverEdges == null) tile.RiverEdges = Enumerable.Repeat(-1, 6).ToArray();
                    tile.RiverEdges[side] = edge.Id;
                    if (edge.TileA < 0) { edge.TileA = tile.Id; edge.SideA = side; } else { edge.TileB = tile.Id; edge.SideB = side; }
                }
            Validate(w);
        }
        public static void Validate(WorldState w)
        {
            var geometry = new HexCorners(w.Config.Width, w.Config.Height);
            var outgoing = w.Rivers.ToDictionary(e => e.FromVertex);
            var seenEdges = new HashSet<ulong>(HexCorners.EdgeComparer);
            foreach (var edge in w.Rivers)
            {
                if (float.IsNaN(edge.Flow) || float.IsInfinity(edge.Flow) || edge.Flow <= 0 || float.IsNaN(edge.Width) || float.IsInfinity(edge.Width) || edge.Width <= 0 || edge.ToSurface > edge.FromSurface + 1e-6 || !geometry.Neighbors(edge.FromVertex).Contains(edge.ToVertex)) throw new InvalidOperationException("Invalid river direction or boundary geometry.");
                if (!seenEdges.Add(HexCorners.EdgeKey(edge.FromVertex, edge.ToVertex))) throw new InvalidOperationException("Duplicate shared river edge.");
                if (edge.TileA < 0 || w.Tiles[edge.TileA].RiverEdges[edge.SideA] != edge.Id || edge.TileB >= 0 && w.Tiles[edge.TileB].RiverEdges[edge.SideB] != edge.Id) throw new InvalidOperationException("Mismatched shared river edge.");
                ulong key = HexCorners.EdgeKey(edge.FromVertex, edge.ToVertex);
                if (HexCorners.EdgeKey(geometry.Corner(edge.TileA, edge.SideA), geometry.Corner(edge.TileA, (edge.SideA + 1) % 6)) != key || edge.TileB >= 0 && HexCorners.EdgeKey(geometry.Corner(edge.TileB, edge.SideB), geometry.Corner(edge.TileB, (edge.SideB + 1) % 6)) != key) throw new InvalidOperationException("River bank does not match its dual-graph edge.");
                if (!edge.Mouth && (!outgoing.TryGetValue(edge.ToVertex, out var next) || next.Flow + 1e-5 < edge.Flow)) throw new InvalidOperationException("Unintended dangling river or lost flow.");
            }
            var completed = new HashSet<int>();
            foreach (var edge in w.Rivers)
            {
                var active = new HashSet<int>(); var next = edge;
                while (next != null && !completed.Contains(next.FromVertex))
                {
                    if (!active.Add(next.FromVertex)) throw new InvalidOperationException("River cycle.");
                    next = outgoing.TryGetValue(next.ToVertex, out var downstream) ? downstream : null;
                }
                completed.UnionWith(active);
            }
        }
    }
}
