using System;
using System.Collections.Generic;

namespace Anthropocene.Simulation
{
    public enum ContourDensity { Normal, Fine }

    public readonly struct ContourPoint : IEquatable<ContourPoint>
    {
        // A point interpolated between neighbouring tile centres. A == B denotes
        // an exact centre crossing and gives shared triangle vertices one identity.
        public readonly int TileA, TileB;
        public readonly float T;

        internal ContourPoint(int tileA, int tileB, float t)
        { TileA = tileA; TileB = tileB; T = t; }

        public bool Equals(ContourPoint other) => TileA == other.TileA && TileB == other.TileB && T.Equals(other.T);
        public override bool Equals(object obj) => obj is ContourPoint other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((TileA * 397) ^ TileB) * 397 ^ BitConverter.SingleToInt32Bits(T); }
        }
    }

    public readonly struct ContourSegment
    {
        public readonly ContourPoint Start, End;
        public readonly int LevelUnits;
        public double Elevation => LevelUnits * TopographicContours.BaseInterval;
        public bool Major => LevelUnits % TopographicContours.MajorIntervalUnits == 0;

        internal ContourSegment(ContourPoint start, ContourPoint end, int levelUnits)
        { Start = start; End = end; LevelUnits = levelUnits; }
    }

    /// <summary>
    /// Marches the triangular lattice formed by neighbouring hex centres. Crossings are
    /// linearly interpolated on shared centre-to-centre edges, so adjacent triangles share
    /// exact endpoints. Only supplied tiles are read; a filtered player view therefore cannot
    /// disclose a hidden neighbour. Longitude wraps through HexMap.At and latitude stops at
    /// the polar rows.
    /// </summary>
    public static class TopographicContours
    {
        public const double BaseInterval = 0.0125;
        public const int MajorIntervalUnits = 8; // 0.100 normalized elevation.

        public static int StepUnits(ContourDensity density, float hexRadius)
        {
            if (hexRadius < 30) return 8; // Major contours only at the most distant view.
            if (hexRadius < 44) return 4;
            if (density == ContourDensity.Fine && hexRadius >= 52) return 1;
            return 2;
        }

        public static double Interval(ContourDensity density, float hexRadius) => StepUnits(density, hexRadius) * BaseInterval;

        public static IReadOnlyList<ContourSegment> Build(HexMap map, IReadOnlyDictionary<int, float> heights,
            ISet<int> includedTiles, ContourDensity density, float hexRadius)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (heights == null) throw new ArgumentNullException(nameof(heights));
            int step = StepUnits(density, hexRadius);
            var ids = new List<int>(heights.Keys); ids.Sort();
            var triangles = new HashSet<TriangleKey>();
            var unique = new HashSet<SegmentKey>();
            var result = new List<ContourSegment>();
            foreach (int tile in ids)
            {
                int q = tile % map.Width, r = tile / map.Width;
                if (r + 1 >= map.Height || !Included(tile, heights, includedTiles)) continue;
                int east = map.At(q + 1, r);
                int southEast = map.At(q, r + 1);
                int southWest = map.At(q - 1, r + 1);
                AddTriangle(tile, east, southEast, step, heights, includedTiles, triangles, unique, result);
                AddTriangle(tile, southEast, southWest, step, heights, includedTiles, triangles, unique, result);
            }
            return result.AsReadOnly();
        }

        private static bool Included(int tile, IReadOnlyDictionary<int, float> heights, ISet<int> included)
            => tile >= 0 && heights.ContainsKey(tile) && (included == null || included.Contains(tile));

        private static void AddTriangle(int a, int b, int c, int step, IReadOnlyDictionary<int, float> heights,
            ISet<int> included, HashSet<TriangleKey> triangles, HashSet<SegmentKey> unique, List<ContourSegment> result)
        {
            if (a < 0 || b < 0 || c < 0 || a == b || b == c || c == a ||
                !Included(a, heights, included) || !Included(b, heights, included) || !Included(c, heights, included)) return;
            var triangle = new TriangleKey(a, b, c);
            if (!triangles.Add(triangle)) return;
            double ha = heights[a], hb = heights[b], hc = heights[c];
            double minimum = Math.Min(ha, Math.Min(hb, hc)), maximum = Math.Max(ha, Math.Max(hb, hc));
            int first = (int)Math.Floor(minimum / BaseInterval / step) * step;
            int last = (int)Math.Ceiling(maximum / BaseInterval / step) * step;
            for (int units = first; units <= last; units += step)
            {
                double level = units * BaseInterval;
                ContourPoint firstPoint = default, secondPoint = default, thirdPoint = default; int count = 0;
                if (Crosses(ha, hb, level)) { firstPoint = Crossing(a, b, ha, hb, level); count = 1; }
                if (Crosses(hb, hc, level))
                {
                    var point = Crossing(b, c, hb, hc, level);
                    if (count == 0) firstPoint = point; else secondPoint = point;
                    count++;
                }
                if (Crosses(hc, ha, level))
                {
                    var point = Crossing(c, a, hc, ha, level);
                    if (count == 0) firstPoint = point;
                    else if (count == 1) secondPoint = point;
                    else thirdPoint = point;
                    count++;
                }
                if (count == 3 && firstPoint.Equals(secondPoint)) secondPoint = thirdPoint;
                if (count < 2 || firstPoint.Equals(secondPoint)) continue;
                var key = new SegmentKey(firstPoint, secondPoint, units);
                if (unique.Add(key)) result.Add(new ContourSegment(firstPoint, secondPoint, units));
            }
        }

        // Half-open classification resolves exact-level vertices consistently and avoids
        // emitting the same shared edge from both adjacent triangles.
        private static bool Crosses(double a, double b, double level)
            => a < level && b >= level || b < level && a >= level;

        private static ContourPoint Crossing(int a, int b, double ha, double hb, double level)
        {
            // Evaluate in canonical tile order as well as storing in canonical order.
            // The two triangles sharing an edge can traverse it in opposite directions;
            // using one arithmetic order gives their float endpoint exactly one identity.
            if (a > b)
            {
                int tile = a; a = b; b = tile;
                double height = ha; ha = hb; hb = height;
            }
            double denominator = hb - ha;
            double value = Math.Abs(denominator) < 1e-15 ? 0 : (level - ha) / denominator;
            if (value <= 1e-7) return new ContourPoint(a, a, 0);
            if (value >= 1 - 1e-7) return new ContourPoint(b, b, 0);
            return new ContourPoint(a, b, (float)value);
        }

        private readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            private readonly int a, b, c;
            public TriangleKey(int x, int y, int z)
            {
                if (x > y) { int t = x; x = y; y = t; }
                if (y > z) { int t = y; y = z; z = t; }
                if (x > y) { int t = x; x = y; y = t; }
                a = x; b = y; c = z;
            }
            public bool Equals(TriangleKey other) => a == other.a && b == other.b && c == other.c;
            public override bool Equals(object obj) => obj is TriangleKey other && Equals(other);
            public override int GetHashCode() { unchecked { return ((a * 397) ^ b) * 397 ^ c; } }
        }

        private readonly struct SegmentKey : IEquatable<SegmentKey>
        {
            private readonly ContourPoint a, b;
            private readonly int level;
            public SegmentKey(ContourPoint x, ContourPoint y, int level)
            {
                if (Compare(x, y) <= 0) { a = x; b = y; }
                else { a = y; b = x; }
                this.level = level;
            }
            private static int Compare(ContourPoint x, ContourPoint y)
            {
                int c = x.TileA.CompareTo(y.TileA); if (c != 0) return c;
                c = x.TileB.CompareTo(y.TileB); return c != 0 ? c : x.T.CompareTo(y.T);
            }
            public bool Equals(SegmentKey other) => level == other.level && a.Equals(other.a) && b.Equals(other.b);
            public override bool Equals(object obj) => obj is SegmentKey other && Equals(other);
            public override int GetHashCode() { unchecked { return ((a.GetHashCode() * 397) ^ b.GetHashCode()) * 397 ^ level; } }
        }
    }
}
