using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Anthropocene.Simulation
{
    /// <summary>
    /// Immutable, presentation-independent measurements of the continuous elevation field.
    /// Elevation is in the generator's normalized height units. Gradients are normalized
    /// height per one centre-to-centre hex spacing. Ruggedness is the radius-four local-plane
    /// residual RMS mapped to 0..100 by 100 R / (R + 0.005).
    /// </summary>
    public readonly struct TerrainMetric
    {
        public readonly float Elevation, GradientX, GradientY, Ruggedness;
        public readonly int Samples;
        public float Slope => (float)Math.Sqrt(GradientX * GradientX + GradientY * GradientY);

        internal TerrainMetric(float elevation, float gradientX, float gradientY, float ruggedness, int samples)
        {
            Elevation = elevation;
            GradientX = gradientX;
            GradientY = gradientY;
            Ruggedness = ruggedness;
            Samples = samples;
        }
    }

    /// <summary>
    /// A reusable derived-geography cache. It is not authoritative state and is deliberately
    /// absent from saves and fingerprints; loading reconstructs it deterministically from the
    /// already-saved continuous elevations. World elevation is immutable after generation.
    /// </summary>
    public sealed class TerrainMetrics
    {
        public const int NeighborhoodRadius = 4;
        public const double RuggednessNormalization = 0.005;
        public const int InteriorNeighborhoodCells = 61;

        private readonly float[] gradientX, gradientY, ruggedness;
        private readonly byte[] samples;
        private readonly Tile[] tiles;

        public int Count => tiles.Length;
        public double BuildMilliseconds { get; }
        public long ApproximateBytes => 13L * Count;

        private TerrainMetrics(Tile[] tiles, float[] gradientX, float[] gradientY, float[] ruggedness, byte[] samples, double buildMilliseconds)
        {
            this.tiles = tiles;
            this.gradientX = gradientX;
            this.gradientY = gradientY;
            this.ruggedness = ruggedness;
            this.samples = samples;
            BuildMilliseconds = buildMilliseconds;
        }

        public TerrainMetric At(int tile)
        {
            if (tile < 0 || tile >= Count) throw new ArgumentOutOfRangeException(nameof(tile));
            return new TerrainMetric(tiles[tile].Elevation, gradientX[tile], gradientY[tile], ruggedness[tile], samples[tile]);
        }

        public float HeightAt(int tile) => At(tile).Elevation;

        public float RelativeHeight(int fromTile, int toTile)
        {
            if (fromTile < 0 || fromTile >= Count) throw new ArgumentOutOfRangeException(nameof(fromTile));
            if (toTile < 0 || toTile >= Count) throw new ArgumentOutOfRangeException(nameof(toTile));
            return tiles[toTile].Elevation - tiles[fromTile].Elevation;
        }

        public static TerrainMetrics Build(WorldState world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.Tiles == null || world.Tiles.Length != world.Map.Width * world.Map.Height) throw new ArgumentException("Terrain metrics require a complete tile field.", nameof(world));

            var timer = Stopwatch.StartNew();
            int count = world.Tiles.Length;
            var gx = new float[count];
            var gy = new float[count];
            var scores = new float[count];
            var sampleCounts = new byte[count];
            int[] narrowIds = world.Map.Width < 2 * NeighborhoodRadius + 1 ? new int[InteriorNeighborhoodCells] : null;

            for (int tile = 0; tile < count; tile++)
            {
                LocalPlane plane = TerrainPlane.FitWorld(world.Map, world.Tiles, tile, narrowIds);
                gx[tile] = (float)plane.B;
                gy[tile] = (float)plane.C;
                scores[tile] = Score(plane.ResidualRms);
                sampleCounts[tile] = (byte)plane.Count;
            }
            timer.Stop();
            return new TerrainMetrics(world.Tiles, gx, gy, scores, sampleCounts, timer.Elapsed.TotalMilliseconds);
        }

        public static float Score(double residualRms)
        {
            if (double.IsNaN(residualRms) || double.IsInfinity(residualRms) || residualRms < 0) throw new ArgumentOutOfRangeException(nameof(residualRms));
            return residualRms <= 0 ? 0 : (float)(100 * residualRms / (residualRms + RuggednessNormalization));
        }

        public static int NeighborhoodCellCount(HexMap map, int tile)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (tile < 0 || tile >= map.Width * map.Height) throw new ArgumentOutOfRangeException(nameof(tile));
            int[] narrowIds = map.Width < 2 * NeighborhoodRadius + 1 ? new int[InteriorNeighborhoodCells] : null;
            return TerrainPlane.Count(map, tile, NeighborhoodRadius, narrowIds);
        }
    }

    /// <summary>
    /// The fog-safe terrain measurement used by player presentation. Only elevations already
    /// present in the filtered observation participate. Partial scores are labelled as such and
    /// cannot change when an inaccessible height changes.
    /// </summary>
    public readonly struct KnownTerrainMetric
    {
        public readonly float Elevation, GradientX, GradientY, Ruggedness;
        public readonly int Samples, NeighborhoodSamples;
        public readonly bool Complete;
        public float Slope => (float)Math.Sqrt(GradientX * GradientX + GradientY * GradientY);

        internal KnownTerrainMetric(float elevation, LocalPlane plane, int neighborhoodSamples)
        {
            Elevation = elevation;
            GradientX = (float)plane.B;
            GradientY = (float)plane.C;
            Ruggedness = TerrainMetrics.Score(plane.ResidualRms);
            Samples = plane.Count;
            NeighborhoodSamples = neighborhoodSamples;
            Complete = plane.Count == neighborhoodSamples;
        }
    }

    public sealed class KnownTerrainAnalysis
    {
        private readonly IReadOnlyDictionary<int, KnownTerrainMetric> metrics;
        public int Count => metrics.Count;
        public double BuildMilliseconds { get; }

        private KnownTerrainAnalysis(IReadOnlyDictionary<int, KnownTerrainMetric> metrics, double buildMilliseconds)
        {
            this.metrics = metrics;
            BuildMilliseconds = buildMilliseconds;
        }

        public bool TryGet(int tile, out KnownTerrainMetric metric) => metrics.TryGetValue(tile, out metric);

        public KnownTerrainMetric At(int tile)
        {
            if (!metrics.TryGetValue(tile, out var metric)) throw new ArgumentOutOfRangeException(nameof(tile));
            return metric;
        }

        public static KnownTerrainAnalysis Build(AgentObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            var known = new Dictionary<int, float>(observation.Tiles.Count + observation.RememberedTiles.Count);
            foreach (var pair in observation.RememberedTiles) known[pair.Key] = pair.Value.Elevation;
            foreach (var pair in observation.Tiles) known[pair.Key] = pair.Value.Elevation;
            return Build(observation.Map, known, observation.Tiles.Keys);
        }

        public static KnownTerrainAnalysis Build(HexMap map, IReadOnlyDictionary<int, float> knownHeights, IEnumerable<int> targetTiles)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (knownHeights == null) throw new ArgumentNullException(nameof(knownHeights));
            if (targetTiles == null) throw new ArgumentNullException(nameof(targetTiles));
            var timer = Stopwatch.StartNew();
            var result = new Dictionary<int, KnownTerrainMetric>();
            int[] narrowIds = map.Width < 2 * TerrainMetrics.NeighborhoodRadius + 1 ? new int[TerrainMetrics.InteriorNeighborhoodCells] : null;
            foreach (int tile in targetTiles)
            {
                if (!knownHeights.TryGetValue(tile, out float elevation)) continue;
                LocalPlane plane = TerrainPlane.FitKnown(map, knownHeights, tile, narrowIds);
                int completeCount = TerrainMetrics.NeighborhoodCellCount(map, tile);
                result[tile] = new KnownTerrainMetric(elevation, plane, completeCount);
            }
            timer.Stop();
            return new KnownTerrainAnalysis(result, timer.Elapsed.TotalMilliseconds);
        }
    }

    internal readonly struct LocalPlane
    {
        public readonly int Count;
        public readonly double A, B, C, ResidualRms;
        public LocalPlane(int count, double a, double b, double c, double residualRms)
        { Count = count; A = a; B = b; C = c; ResidualRms = residualRms; }
    }

    internal static class TerrainPlane
    {
        // Pointy-top axial centres, scaled so every neighbouring centre is one unit apart.
        internal static void Centre(int dq, int dr, out double x, out double y)
        {
            x = dq + dr * 0.5;
            y = dr * 0.86602540378443864676;
        }

        private static bool Duplicate(int[] narrowIds, int used, int id)
        {
            if (narrowIds == null) return false;
            for (int i = 0; i < used; i++) if (narrowIds[i] == id) return true;
            return false;
        }

        internal static int Count(HexMap map, int tile, int radius, int[] narrowIds)
        {
            int q0 = tile % map.Width, r0 = tile / map.Width, used = 0, count = 0;
            for (int dr = -radius; dr <= radius; dr++)
            {
                int minimum = Math.Max(-radius, -dr - radius);
                int maximum = Math.Min(radius, -dr + radius);
                for (int dq = minimum; dq <= maximum; dq++)
                {
                    int id = map.At(q0 + dq, r0 + dr);
                    if (id < 0) continue;
                    if (Duplicate(narrowIds, used, id)) continue;
                    if (narrowIds != null) narrowIds[used++] = id;
                    count++;
                }
            }
            return count;
        }

        internal static LocalPlane FitWorld(HexMap map, Tile[] tiles, int tile, int[] narrowIds)
        {
            var fit = new PlaneAccumulator();
            int radius = TerrainMetrics.NeighborhoodRadius;
            int q0 = tile % map.Width, r0 = tile / map.Width, used = 0;
            for (int dr = -radius; dr <= radius; dr++)
            {
                int minimum = Math.Max(-radius, -dr - radius);
                int maximum = Math.Min(radius, -dr + radius);
                for (int dq = minimum; dq <= maximum; dq++)
                {
                    int id = map.At(q0 + dq, r0 + dr);
                    if (id < 0 || Duplicate(narrowIds, used, id)) continue;
                    if (narrowIds != null) narrowIds[used++] = id;
                    Centre(dq, dr, out double x, out double y);
                    fit.Add(x, y, tiles[id].Elevation);
                }
            }
            return fit.Finish();
        }

        internal static LocalPlane FitKnown(HexMap map, IReadOnlyDictionary<int, float> known, int tile, int[] narrowIds)
        {
            var fit = new PlaneAccumulator();
            int radius = TerrainMetrics.NeighborhoodRadius;
            int q0 = tile % map.Width, r0 = tile / map.Width, used = 0;
            for (int dr = -radius; dr <= radius; dr++)
            {
                int minimum = Math.Max(-radius, -dr - radius);
                int maximum = Math.Min(radius, -dr + radius);
                for (int dq = minimum; dq <= maximum; dq++)
                {
                    int id = map.At(q0 + dq, r0 + dr);
                    if (id < 0 || Duplicate(narrowIds, used, id)) continue;
                    if (narrowIds != null) narrowIds[used++] = id;
                    if (!known.TryGetValue(id, out float height)) continue;
                    Centre(dq, dr, out double x, out double y);
                    fit.Add(x, y, height);
                }
            }
            return fit.Finish();
        }

        private struct PlaneAccumulator
        {
            private int count;
            private double meanX, meanY, meanH;
            private double xx, xy, yy, xh, yh, hh;

            // Online centred covariance keeps the result stable under a uniform height offset.
            public void Add(double x, double y, double height)
            {
                count++;
                double dx = x - meanX, dy = y - meanY, dh = height - meanH;
                meanX += dx / count;
                meanY += dy / count;
                meanH += dh / count;
                xx += dx * (x - meanX);
                xy += dx * (y - meanY);
                yy += dy * (y - meanY);
                xh += dx * (height - meanH);
                yh += dy * (height - meanH);
                hh += dh * (height - meanH);
            }

            public LocalPlane Finish()
            {
                if (count == 0) return new LocalPlane(0, 0, 0, 0, 0);
                double b = 0, c = 0;
                double determinant = xx * yy - xy * xy;
                double scale = Math.Max(1, Math.Max(xx * yy, xy * xy));
                if (determinant > 1e-14 * scale)
                {
                    b = (xh * yy - yh * xy) / determinant;
                    c = (yh * xx - xh * xy) / determinant;
                }
                else if (xx >= yy && xx > 1e-14) b = xh / xx;
                else if (yy > 1e-14) c = yh / yy;

                double residual = Math.Max(0, hh - b * xh - c * yh);
                double rms = Math.Sqrt(residual / count);
                double a = meanH - b * meanX - c * meanY;
                return new LocalPlane(count, a, b, c, rms);
            }
        }
    }
}
