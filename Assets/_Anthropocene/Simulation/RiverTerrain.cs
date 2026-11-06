using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Generation-only geometric erosion. Age changes a bounded fraction, never a timestep count.
    internal sealed class AncientMarginErosionStage : IWorldGenerationStage
    {
        public string Name => "Ancient uplift margin erosion";
        public string Requires => "Inherited uplift footprint and age";
        public string Produces => "Preferentially eroded outer flanks, preserved broad cores";
        internal static double Fraction(double strength, double age)
        {
            double edge = 1 - Numbers.Clamp(strength, 0, 1);
            return (.08 + .57 * edge * edge) * Numbers.Clamp(age / 400.0, .25, 1);
        }
        public void Generate(GenerationContext c)
        {
            var report = c.World.Generation;
            if (!c.Config.DrainageTerrainEnabled || report?.AncientOrogenUplift == null) return;
            var removed = report.AncientMarginErosion = new float[c.World.Tiles.Length];
            foreach (var t in c.World.Tiles)
            {
                if (!t.Land) continue;
                double uplift = report.AncientOrogenUplift[t.Id];
                double strength = report.AncientOrogenStrength?[t.Id] ?? 0;
                double age = report.AncientOrogenAge?[t.Id] ?? 0;
                double cut = Math.Min(uplift * Fraction(strength, age), Math.Max(0, t.Elevation - c.Config.SeaLevel - .000001));
                float before = t.Elevation;
                t.Elevation = (float)(t.Elevation - cut);
                removed[t.Id] = before - t.Elevation;
            }
        }
    }

    internal sealed class RiverTerrainStage : IWorldGenerationStage
    {
        public string Name => "Flow-following valley incision";
        public string Requires => "Resolved lakes, tile drainage and boundary river flow";
        public string Produces => "Incised final heights with downhill raw river corners and tile drainage";

        // h[from] >= h[to]. Flat constraints can have cycles: heap propagation handles
        // them once in canonical order, without repeated relaxation scans or path walks.
        private sealed class Constraints
        {
            internal readonly int[] Head, ReverseHead;
            internal readonly List<int> From, To, Next, ReverseNext;
            internal Constraints(int count, int capacity)
            {
                Head = new int[count]; ReverseHead = new int[count];
                Array.Fill(Head, -1); Array.Fill(ReverseHead, -1);
                From = new List<int>(capacity); To = new List<int>(capacity);
                Next = new List<int>(capacity); ReverseNext = new List<int>(capacity);
            }
            internal void Add(int from, int to)
            {
                if (from == to) return;
                int i = To.Count; From.Add(from); To.Add(to);
                Next.Add(Head[from]); Head[from] = i;
                ReverseNext.Add(ReverseHead[to]); ReverseHead[to] = i;
            }
        }

        public void Generate(GenerationContext c)
        {
            if (!c.Config.DrainageTerrainEnabled) return;
            var w = c.World; var tiles = w.Tiles; int count = tiles.Length;
            var corners = new HexCorners(c.Config.Width, c.Config.Height);
            var constraints = new Constraints(count, count + w.Rivers.Count * 5);
            foreach (var tile in tiles)
                if (tile.Downstream >= 0) constraints.Add(tile.Id, tile.Downstream);
            foreach (var edge in w.Rivers)
            {
                int[] a = corners.TilesAt(edge.FromVertex).ToArray(), b = corners.TilesAt(edge.ToVertex).ToArray();
                int[] onlyA = a.Except(b).ToArray(), onlyB = b.Except(a).ToArray(), shared = a.Intersect(b).ToArray();
                // Interior corners share two of their three tiles, so the exact mean
                // inequality cancels those banks and compares the two opposite tiles.
                // Polar corners have fewer contributors; subset cases use sufficient
                // pairwise bounds on their common mean rather than phantom polar tiles.
                var sources = onlyA.Length > 0 ? onlyA : a;
                var targets = onlyB.Length > 0 ? onlyB : b;
                foreach (int from in sources) foreach (int to in targets) constraints.Add(from, to);
                // The displayed triangular height field also crosses the shared-bank
                // midpoint. Endpoint descent alone could conceal an intervening hump.
                foreach (int bank in shared)
                {
                    foreach (int from in onlyA) constraints.Add(from, bank);
                    foreach (int to in onlyB) constraints.Add(bank, to);
                }
            }

            var original = tiles.Select(t => t.Elevation).ToArray();
            var floor = new float[count]; var settled = new bool[count]; var heap = new StableMinHeap();
            foreach (var t in tiles)
            {
                floor[t.Id] = t.Land ? (float)(c.Config.SeaLevel + .000001) : t.Elevation;
                if (!t.Land) heap.Add(t.Id, -floor[t.Id]);
            }
            // Retained water levels are fixed. Propagate their lower bounds upstream.
            while (heap.Count > 0)
            {
                int id = heap.Pop(); if (settled[id]) continue; settled[id] = true;
                for (int i = constraints.ReverseHead[id]; i >= 0; i = constraints.ReverseNext[i])
                {
                    int upstream = constraints.From[i];
                    if (floor[upstream] >= floor[id]) continue;
                    floor[upstream] = floor[id]; heap.Add(upstream, -floor[upstream]);
                }
            }
            var depth = new float[count];
            foreach (var edge in w.Rivers)
            {
                // A bounded stream-power proxy in normalized height, not years of erosion.
                double cut = Math.Min(.025, .0015 + .004 * Math.Log(1 + edge.Flow / c.Config.RiverThreshold));
                AddBank(edge.TileA, cut); AddBank(edge.TileB, cut);
            }
            void AddBank(int id, double cut)
            {
                if (id < 0 || !tiles[id].Land) return;
                double strength = w.Generation?.AncientOrogenStrength?[id] ?? 0;
                double uplift = w.Generation?.AncientOrogenUplift?[id] ?? 0;
                double edgeExposure = (1 - strength) * Numbers.Clamp(uplift / .02, 0, 1);
                float localCut = (float)(cut * (1 + edgeExposure));
                depth[id] = Math.Max(depth[id], localCut);
                foreach (int n in c.Map.Neighbors(id))
                    if (tiles[n].Land) depth[n] = Math.Max(depth[n], localCut * .25f);
            }
            Array.Clear(settled, 0, count);
            foreach (var t in tiles)
            {
                // The tile-centre and corner graphs can disagree immediately beside
                // a retained lake. Grade that bank up to its fixed water datum rather
                // than excavating the lake. Report this explicitly, not as erosion.
                float grade = floor[t.Id] - original[t.Id];
                if (grade > .000001f)
                {
                    if (!t.Land) throw new InvalidOperationException("Incompatible fixed water levels at tile " + t.Id);
                    if (w.Generation != null)
                    {
                        w.Generation.RiverBankGradeTiles++;
                        w.Generation.RiverBankGradeMaximum = Math.Max(w.Generation.RiverBankGradeMaximum, grade);
                    }
                }
                t.Elevation = t.Land ? Math.Max(floor[t.Id], original[t.Id] - depth[t.Id]) : original[t.Id];
                heap.Add(t.Id, t.Elevation);
            }
            // Greatest surface no higher than the proposed incision satisfying every
            // downstream constraint. Multi-source minimum propagation is O((N+R) log N).
            while (heap.Count > 0)
            {
                int id = heap.Pop(); if (settled[id]) continue; settled[id] = true;
                for (int i = constraints.Head[id]; i >= 0; i = constraints.Next[i])
                {
                    var downstream = tiles[constraints.To[i]];
                    if (downstream.Elevation <= tiles[id].Elevation) continue;
                    downstream.Elevation = tiles[id].Elevation;
                    heap.Add(downstream.Id, downstream.Elevation);
                }
            }
            var removed = w.Generation != null ? w.Generation.RiverTerrainErosion = new float[count] : null;
            foreach (var t in tiles)
            {
                float cut = original[t.Id] - t.Elevation;
                if (removed != null) removed[t.Id] = Math.Max(0, cut);
                if (t.Land) t.DepressionDepth = Math.Max(0, t.DepressionDepth - Math.Abs(cut));
                if (!t.Land && t.Elevation != original[t.Id]) throw new InvalidOperationException("River erosion changed a fixed water surface.");
            }
            foreach (var edge in w.Rivers)
            {
                edge.FromSurface = (float)corners.TilesAt(edge.FromVertex).Average(id => (double)tiles[id].Elevation);
                edge.ToSurface = (float)corners.TilesAt(edge.ToVertex).Average(id => (double)tiles[id].Elevation);
            }
            BoundaryRiverStage.Validate(w);
        }
    }
}
