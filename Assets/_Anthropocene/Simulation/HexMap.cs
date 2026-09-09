using System;
using System.Collections.Generic;

namespace Anthropocene.Simulation
{
    // Axial coordinates, cylindrical longitude; no pole wrapping. All mechanics share this service.
    public sealed class HexMap
    {
        public readonly int Width, Height;
        private static readonly int[] Dq = { 1, 1, 0, -1, -1, 0 };
        private static readonly int[] Dr = { 0, -1, -1, 0, 1, 1 };
        public HexMap(int width, int height) { Width = width; Height = height; }
        public int At(int q, int r) => r < 0 || r >= Height ? -1 : r * Width + ((q % Width) + Width) % Width;
        public IEnumerable<int> Neighbors(int tile)
        {
            for (int i = 0; i < 6; i++) { int n = At(tile % Width + Dq[i], tile / Width + Dr[i]); if (n >= 0) yield return n; }
        }
        public int Distance(int a, int b)
        {
            int dr = b / Width - a / Width, dq = b % Width - a % Width, best = int.MaxValue;
            for (int k = -1; k <= 1; k++) { int q = dq + k * Width; best = Math.Min(best, (Math.Abs(q) + Math.Abs(dr) + Math.Abs(q + dr)) / 2); }
            return best;
        }
        public IEnumerable<int> Radius(int tile, int radius)
        {
            var found = new HashSet<int>();
            for (int dr = -radius; dr <= radius; dr++)
                for (int dq = Math.Max(-radius, -dr - radius); dq <= Math.Min(radius, -dr + radius); dq++)
                { int n = At(tile % Width + dq, tile / Width + dr); if (n >= 0 && found.Add(n)) yield return n; }
        }
        public bool Reachable(int start, int destination, int budget, Func<int, bool> enter, Func<int, int> cost = null)
        {
            if (destination < 0 || destination >= Width * Height || Distance(start, destination) > budget || !enter(destination)) return false;
            var remaining = new Dictionary<int, int> { [start] = budget };
            var queue = new Queue<int>(); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue(); if (current == destination) return true;
                foreach (int n in Neighbors(current))
                {
                    if (!enter(n)) continue;
                    int left = remaining[current] - (cost == null ? 1 : cost(n));
                    if (left < 0 || (remaining.TryGetValue(n, out int old) && old >= left)) continue;
                    remaining[n] = left; queue.Enqueue(n);
                }
            }
            return false;
        }
    }
}
