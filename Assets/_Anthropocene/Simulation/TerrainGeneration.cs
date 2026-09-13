using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    /// <summary>Seeded continuous fields, periodic in longitude at every octave.</summary>
    public static class TerrainNoise
    {
        public static double Fractal(int seed, int stream, double x, double y, int frequency = 4, int octaves = 5)
        {
            double sum = 0, weight = 1, total = 0;
            for (int o = 0; o < octaves; o++)
            { sum += weight * Value(seed, stream + o, x, y, frequency); total += weight; weight *= .5; frequency *= 2; }
            return sum / total;
        }
        private static double Value(int seed, int stream, double x, double y, int frequency)
        {
            x = (x - Math.Floor(x)) * frequency; y *= frequency * .5;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            double fx = x - ix, fy = y - iy; fx = fx * fx * fx * (fx * (fx * 6 - 15) + 10); fy = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
            double At(int dx, int dy) => Numbers.Random(seed, stream, unchecked(((ix + dx) % frequency) * 73856093 ^ (iy + dy) * 19349663)) * 2 - 1;
            double a = At(0, 0), b = At(1, 0), d = At(0, 1), e = At(1, 1);
            return (a + (b - a) * fx) * (1 - fy) + (d + (e - d) * fx) * fy;
        }
        internal static double LongitudeDelta(double a, double b) { double d = a - b; return d - Math.Floor(d + .5); }
    }

    internal sealed class PlateStage : IWorldGenerationStage
    {
        public string Name => "Warped tectonic provinces"; public string Requires => "Geometry"; public string Produces => "Plates and movement vectors";
        public void Generate(GenerationContext c)
        {
            int count = c.Config.PlateCount, seed = c.World.Seed;
            c.PlateCenters = new int[count]; c.PlateX = new double[count]; c.PlateY = new double[count]; c.Continental = new bool[count];
            c.OtherPlate = new int[c.World.Tiles.Length]; c.BoundaryDistance = new float[c.World.Tiles.Length];
            for (int p = 0; p < count; p++)
            {
                int best = 0; double separation = -1;
                for (int k = 0; k < 12; k++)
                {
                    int candidate = (int)(Numbers.Random(seed, 310, p * 12 + k) * c.World.Tiles.Length);
                    double distance = p == 0 ? 1 : Enumerable.Range(0, p).Min(i => GeographicDistance(c, candidate, c.PlateCenters[i]));
                    if (distance > separation) { best = candidate; separation = distance; }
                }
                c.PlateCenters[p] = best;
                double angle = Numbers.Random(seed, 311, p) * 2 * Math.PI;
                c.PlateX[p] = Math.Cos(angle); c.PlateY[p] = Math.Sin(angle);
            }
            foreach (var t in c.World.Tiles)
            {
                double x = t.Id % c.Config.Width / (double)c.Config.Width, y = t.Id / c.Config.Width / (double)c.Config.Height;
                double wx = x + .045 * TerrainNoise.Fractal(seed, 320, x, y, 4, 3), wy = y + .09 * TerrainNoise.Fractal(seed, 325, x, y, 4, 3);
                double first = double.MaxValue, second = double.MaxValue; int a = 0, b = 1;
                for (int p = 0; p < count; p++)
                {
                    double dx = 2 * TerrainNoise.LongitudeDelta(wx, c.PlateCenters[p] % c.Config.Width / (double)c.Config.Width);
                    double dy = wy - c.PlateCenters[p] / c.Config.Width / (double)c.Config.Height;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < first) { second = first; b = a; first = d; a = p; } else if (d < second) { second = d; b = p; }
                }
                t.Plate = a; c.OtherPlate[t.Id] = b; c.BoundaryDistance[t.Id] = (float)(second - first);
            }
        }
        internal static double GeographicDistance(GenerationContext c, int a, int b)
        {
            double dx = 2 * TerrainNoise.LongitudeDelta(a % c.Config.Width / (double)c.Config.Width, b % c.Config.Width / (double)c.Config.Width);
            double dy = (a / c.Config.Width - b / c.Config.Width) / (double)c.Config.Height;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    internal sealed class ElevationStage : IWorldGenerationStage
    {
        public string Name => "Continental shelves, mountain belts and hotspot tracks";
        public string Requires => "Tectonic provinces"; public string Produces => "Continuous relief, coastlines and volcanic islands";
        private struct Lobe { public double X, Y, RX, RY, Angle; }
        public void Generate(GenerationContext c)
        {
            var w = c.World; var cfg = c.Config; var lobes = new List<Lobe>();
            bool pangaea = cfg.Shape == WorldShape.Pangaea, islands = cfg.Shape == WorldShape.Archipelagos;
            int nuclei = pangaea ? 1 : islands ? 10 : 3;
            double rotation = Numbers.Random(w.Seed, 330, 0);
            for (int i = 0; i < nuclei; i++)
            {
                double x = rotation + i / (double)nuclei + (Numbers.Random(w.Seed, 331, i) - .5) * .18;
                double y = .23 + Numbers.Random(w.Seed, 332, i) * .54;
                double size = pangaea ? .95 + .1 * Numbers.Random(w.Seed, 337, i) : .7 + .65 * Numbers.Random(w.Seed, 337, i);
                double rx = (pangaea ? .14 : islands ? .037 : .074) * size;
                double ry = (pangaea ? .25 : islands ? .083 : .18) * size;
                double axis = Numbers.Random(w.Seed, 333, i) * Math.PI * 2;
                lobes.Add(new Lobe { X = x, Y = y, RX = rx, RY = ry, Angle = axis });
                // Persistent, branching crustal fragments produce long continental axes, arms,
                // peninsulas and gulfs instead of an evenly spaced set of circular islands.
                int branches = islands ? 2 : 3;
                for (int arm = 0; arm < branches; arm++)
                {
                    double px = x, py = y, heading = axis + arm * Math.PI * .8;
                    int steps = islands ? 2 : pangaea ? 3 : 2 + (int)(Numbers.Random(w.Seed, 338, i * 3 + arm) * 3);
                    for (int j = 0; j < steps; j++)
                    {
                        int key = i * 32 + arm * 8 + j;
                        heading += (Numbers.Random(w.Seed, 334, key) - .5) * 1.4;
                        double factor = Math.Pow(.80, j), stride = .55 + .50 * Numbers.Random(w.Seed, 339, key);
                        px += Math.Cos(heading) * rx * factor * stride;
                        py += Math.Sin(heading) * ry * factor * stride;
                        lobes.Add(new Lobe { X = px, Y = py, RX = rx * factor * (.5 + .35 * Numbers.Random(w.Seed, 335, key)),
                            RY = ry * factor * (.5 + .4 * Numbers.Random(w.Seed, 336, key)), Angle = heading });
                    }
                }
            }
            double Crust(double x, double y)
            {
                double wx = x + .09 * TerrainNoise.Fractal(w.Seed, 340, x, y, 4, 4);
                double wy = y + .18 * TerrainNoise.Fractal(w.Seed, 345, x, y, 4, 4);
                double value = -10;
                foreach (var l in lobes)
                {
                    double dx = TerrainNoise.LongitudeDelta(wx, l.X), dy = (wy - l.Y) / 2;
                    double co = Math.Cos(l.Angle), si = Math.Sin(l.Angle);
                    double a = (dx * co - dy * si) / l.RX, b = (dx * si + dy * co) / (l.RY / 2);
                    value = Math.Max(value, 1 - Math.Sqrt(a * a + b * b));
                }
                return value + .52 * TerrainNoise.Fractal(w.Seed, 350, x, y, 10, 5);
            }
            for (int p = 0; p < cfg.PlateCount; p++) c.Continental[p] = Crust(c.PlateCenters[p] % cfg.Width / (double)cfg.Width, c.PlateCenters[p] / cfg.Width / (double)cfg.Height) > 0;
            foreach (var t in w.Tiles)
            {
                double x = t.Id % cfg.Width / (double)cfg.Width, y = t.Id / cfg.Width / (double)cfg.Height;
                double crust = Crust(x, y); t.Continental = crust > -.12;
                double height = cfg.SeaLevel + .16 * crust;
                int a = t.Plate, b = c.OtherPlate[t.Id];
                double dx = 2 * TerrainNoise.LongitudeDelta(c.PlateCenters[b] % cfg.Width / (double)cfg.Width, c.PlateCenters[a] % cfg.Width / (double)cfg.Width);
                double dy = (c.PlateCenters[b] / cfg.Width - c.PlateCenters[a] / cfg.Width) / (double)cfg.Height;
                double norm = Math.Max(.0001, Math.Sqrt(dx * dx + dy * dy));
                double convergence = ((c.PlateX[a] - c.PlateX[b]) * dx + (c.PlateY[a] - c.PlateY[b]) * dy) / norm;
                double belt = Math.Exp(-Math.Pow(c.BoundaryDistance[t.Id] / .045, 2));
                double ridges = 1 - Math.Abs(TerrainNoise.Fractal(w.Seed, 360, x, y, 32, 4));
                if (t.Continental)
                {
                    height += Math.Max(0, convergence) * cfg.Uplift * belt * ridges;
                    height += cfg.ElevationNoise * .5 * TerrainNoise.Fractal(w.Seed, 365, x, y, 16, 5) * Math.Min(1, Math.Max(0, crust) * 4);
                    if (convergence > .3 && !c.Continental[b]) t.Volcanism = (float)(belt * convergence * .12);
                }
                else if (convergence < -.15) height += .045 * belt;
                else if (convergence > .15) height -= .025 * belt;
                t.Elevation = (float)Numbers.Clamp(height, .025, 1.2);
            }
            // Short-range erosion smooths slopes, not province-wide coast boundaries.
            var heights = new float[w.Tiles.Length];
            foreach (var t in w.Tiles) heights[t.Id] = (float)(t.Elevation * (1 - cfg.Erosion) + c.Map.Neighbors(t.Id).Average(n => w.Tiles[n].Elevation) * cfg.Erosion);
            foreach (var t in w.Tiles) t.Elevation = heights[t.Id];
            Hotspots(c);
            foreach (var t in w.Tiles) t.Terrain = t.Elevation < cfg.SeaLevel - .018 ? Terrain.Ocean : t.Elevation < cfg.SeaLevel ? Terrain.Shallows : Terrain.Grassland;
            Geography.Classify(w);
        }
        private static void Hotspots(GenerationContext c)
        {
            var w = c.World; var cfg = c.Config; double scale = Math.Max(.65, cfg.Width / 256.0);
            var origins = new List<int>();
            for (int h = 0; h < cfg.Hotspots; h++)
            {
                int origin = -1;
                for (int trial = 0; trial < 200; trial++)
                {
                    int id = (int)(Numbers.Random(w.Seed, 380, h * 200 + trial) * w.Tiles.Length);
                    int row = id / cfg.Width;
                    if (row < cfg.Height * .12 || row > cfg.Height * .88 || w.Tiles[id].Elevation > cfg.SeaLevel - .07 || origins.Any(o => c.Map.Distance(o, id) < scale * 15)) continue;
                    origin = id; break;
                }
                if (origin < 0) continue; origins.Add(origin); int plate = w.Tiles[origin].Plate;
                for (int age = 0; age < cfg.HotspotChainLength; age++)
                {
                    double bend = .22 * Math.Sin(age * .8 + h);
                    double spacing = 3.7 + 1.8 * Numbers.Random(w.Seed, 381, h * 37 + age);
                    int col = origin % cfg.Width + (int)Math.Round((c.PlateX[plate] + bend) * age * scale * spacing);
                    int row = origin / cfg.Width + (int)Math.Round(c.PlateY[plate] * age * scale * spacing * .75);
                    int center = c.Map.At(col, row); if (center < 0 || w.Tiles[center].Elevation > cfg.SeaLevel - .025) continue;
                    double radius = scale * (.95 + .55 * Numbers.Random(w.Seed, 382, h * 37 + age)) * Math.Exp(-age * .10);
                    radius = Math.Max(.65, radius);
                    double peak = cfg.SeaLevel + (.07 + .07 * Numbers.Random(w.Seed, 383, h * 37 + age)) * Math.Exp(-age * .27);
                    foreach (int id in c.Map.Radius(center, Math.Max(2, (int)Math.Ceiling(radius * 2))))
                    {
                        var t = w.Tiles[id]; double dr = id / cfg.Width - center / cfg.Width;
                        double dq = TerrainNoise.LongitudeDelta(id % cfg.Width / (double)cfg.Width, center % cfg.Width / (double)cfg.Width) * cfg.Width + dr * .5;
                        double d = Math.Sqrt(dq * dq + dr * dr * .75) / radius;
                        double rough = 1 + .20 * TerrainNoise.Fractal(w.Seed, 390, id % cfg.Width / (double)cfg.Width, id / cfg.Width / (double)cfg.Height, 128, 2);
                        double target = cfg.SeaLevel - .10 + (peak - cfg.SeaLevel + .10) * Math.Exp(-d * d * rough);
                        if (target > t.Elevation) { t.Volcanism += (float)((target - t.Elevation) * Math.Exp(-age * .3)); t.Elevation = (float)target; }
                    }
                }
            }
        }
    }
}
