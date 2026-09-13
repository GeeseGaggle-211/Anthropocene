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
        // Bounded normal samples keep rare excursions finite without making every track equally wavy.
        internal static double Gaussian(int seed, int stream, int key)
        {
            double u = Math.Max(1e-12, Numbers.Random(seed, stream, key, 0));
            return Numbers.Clamp(Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * Numbers.Random(seed, stream, key, 1)), -3, 3);
        }
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
        private struct Lobe { public double X, Y, RX, RY, Angle, Cos, Sin; }
        public void Generate(GenerationContext c)
        {
            var w = c.World; var cfg = c.Config; var lobes = new List<Lobe>(); var rifts = new List<Lobe>();
            bool pangaea = cfg.Shape == WorldShape.Pangaea, islands = cfg.Shape == WorldShape.Archipelagos;
            int nuclei = pangaea ? 1 : islands ? 0 : 3;
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
            if (islands) ArchipelagoCrust(c, lobes, rifts);
            for (int i = 0; i < lobes.Count; i++) { var l = lobes[i]; l.Cos = Math.Cos(l.Angle); l.Sin = Math.Sin(l.Angle); lobes[i] = l; }
            for (int i = 0; i < rifts.Count; i++) { var l = rifts[i]; l.Cos = Math.Cos(l.Angle); l.Sin = Math.Sin(l.Angle); rifts[i] = l; }
            double Crust(double x, double y)
            {
                double wx = x + (islands ? .025 : .09) * TerrainNoise.Fractal(w.Seed, 340, x, y, islands ? 8 : 4, 4);
                double wy = y + (islands ? .05 : .18) * TerrainNoise.Fractal(w.Seed, 345, x, y, islands ? 8 : 4, 4);
                double value = -10;
                foreach (var l in lobes)
                {
                    double dx = TerrainNoise.LongitudeDelta(wx, l.X), dy = (wy - l.Y) / 2;
                    double co = l.Cos, si = l.Sin;
                    double a = (dx * co - dy * si) / l.RX, b = (dx * si + dy * co) / (l.RY / 2);
                    value = Math.Max(value, 1 - Math.Sqrt(a * a + b * b));
                }
                if (islands)
                    foreach (var r in rifts)
                    {
                        double dx = TerrainNoise.LongitudeDelta(wx, r.X), dy = (wy - r.Y) / 2;
                        double a = (dx * r.Cos - dy * r.Sin) / r.RX, b = (dx * r.Sin + dy * r.Cos) / (r.RY / 2);
                        double d = a * a + b * b;
                        if (d < 8) value -= 1.65 * Math.Exp(-d * 1.5);
                    }
                return value + (islands ? .60 : .52) * TerrainNoise.Fractal(w.Seed, 350, x, y, islands ? 64 : 10, 5);
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
        private static void ArchipelagoCrust(GenerationContext c, List<Lobe> lobes, List<Lobe> rifts)
        {
            // Many narrow, curved crustal ribbons, broken by transverse rifts. These are
            // a procedural fragmentation model, not plate labels or a simulated geological history.
            int seed = c.World.Seed; var centers = new List<Lobe>();
            for (int chain = 0; chain < 30; chain++)
            {
                double x = 0, y = 0, best = -1;
                for (int trial = 0; trial < 16; trial++)
                {
                    int key = chain * 16 + trial;
                    double px = Numbers.Random(seed, 410, key), py = .12 + .76 * Numbers.Random(seed, 411, key);
                    double separation = centers.Count == 0 ? 1 : centers.Min(l => Math.Pow(TerrainNoise.LongitudeDelta(px, l.X) * 2, 2) + Math.Pow(py - l.Y, 2));
                    if (separation > best) { best = separation; x = px; y = py; }
                }
                centers.Add(new Lobe { X = x, Y = y });
                double heading = Numbers.Random(seed, 412, chain) * Math.PI * 2;
                double curvature = TerrainNoise.Gaussian(seed, 413, chain) * .15;
                double size = .8 + .45 * Numbers.Random(seed, 414, chain);
                int count = 4 + (int)(Numbers.Random(seed, 415, chain) * 4);
                for (int piece = 0; piece < count; piece++)
                {
                    int key = chain * 32 + piece;
                    double major = (.012 + .014 * Numbers.Random(seed, 416, key)) * size;
                    double minor = (.0045 + .0055 * Numbers.Random(seed, 417, key)) * size;
                    lobes.Add(new Lobe { X = x, Y = y, RX = minor, RY = major * 2, Angle = Math.PI / 2 - heading });
                    if (Numbers.Random(seed, 422, key) < .30)
                        lobes.Add(new Lobe { X = x + Math.Cos(heading + 1.1) * minor * .8, Y = y + Math.Sin(heading + 1.1) * minor * 1.6,
                            RX = minor * .7, RY = major * 1.3, Angle = Math.PI / 2 - heading - .8 });
                    double stride = .026 * Math.Exp(.28 * TerrainNoise.Gaussian(seed, 418, key)) * size;
                    if (piece < count - 1 && Numbers.Random(seed, 419, key) < .60)
                        rifts.Add(new Lobe { X = x + Math.Cos(heading) * stride * .5, Y = y + Math.Sin(heading) * stride,
                            RX = .003 + .003 * Numbers.Random(seed, 420, key), RY = minor * 5, Angle = -heading });
                    x += Math.Cos(heading) * stride; y += Math.Sin(heading) * stride * 2;
                    heading += curvature + .15 * TerrainNoise.Gaussian(seed, 421, key);
                    // Bend tracks back toward the inhabited latitude range without a hard coastline cutoff.
                    if (y < .10 || y > .90) heading = Math.Atan2((.5 - y) * 2, Math.Cos(heading));
                }
            }
        }
        internal struct HotspotSample
        {
            public double X, Y, Age, Radius, Peak, Activity, Uplift;
        }
        internal static List<HotspotSample> HotspotTrack(GenerationContext c, int origin, int track, bool extinct)
        {
            var cfg = c.Config; int seed = c.World.Seed, plate = c.World.Tiles[origin].Plate;
            bool archipelago = cfg.Shape == WorldShape.Archipelagos;
            double scale = Math.Max(.65, cfg.Width / 256.0), x = origin % cfg.Width, y = origin / cfg.Width;
            double motion = Math.Atan2(c.PlateY[plate] * .75, c.PlateX[plate]);
            double heading = motion + .25 * TerrainNoise.Gaussian(seed, 430, track);
            double curvature = .018 * TerrainNoise.Gaussian(seed, 431, track), angular = 0, lateral = 0;
            double startAge = extinct ? 2 + 4 * Numbers.Random(seed, 432, track) : 0;
            int episodes = cfg.HotspotChainLength + (extinct ? (int)(Numbers.Random(seed, 433, track) * 4) : 0);
            int steps = episodes * (archipelago ? 10 : 4);
            var result = new List<HotspotSample>(steps);
            double window = 0, width = .7 + .35 * Numbers.Random(seed, 443, track), previousWindow = -20, previousWidth = 1;
            for (int step = 0; step < steps; step++)
            {
                int key = unchecked(track * 65537 + step);
                // A stochastic moving uplift window follows one continuous trajectory.
                // Correlated heading/lateral innovations retain a chain's direction, while
                // its regional flow field bends it smoothly through x, y and diagonals.
                angular = .8 * angular + .035 * TerrainNoise.Gaussian(seed, 434, key);
                double target = motion + .65 * TerrainNoise.Fractal(seed, 442, x / cfg.Width, y / cfg.Height, 8, 3);
                double turn = Math.Atan2(Math.Sin(target - heading), Math.Cos(target - heading));
                heading += .06 * turn + curvature + angular;
                lateral = .8 * lateral + .12 * TerrainNoise.Gaussian(seed, 435, key);
                if (step > 0)
                {
                    double stride = scale * 1.25 * Math.Exp(.14 * TerrainNoise.Gaussian(seed, 436, key));
                    x += Math.Cos(heading) * stride; y += Math.Sin(heading) * stride;
                }
                if (step > window)
                {
                    previousWindow = window; previousWidth = width;
                    window += Math.Max(3, Math.Round(4.7 * Math.Exp(.25 * TerrainNoise.Gaussian(seed, 444, key))));
                    width = .55 + .65 * Numbers.Random(seed, 445, key);
                }
                double pulse = Math.Max(Math.Exp(-.5 * Math.Pow((step - window) / width, 2)), Math.Exp(-.5 * Math.Pow((step - previousWindow) / previousWidth, 2)));
                double elapsed = startAge + step * .18;
                double radius = Math.Max(.65, scale * 1.20 * Math.Exp(.17 * TerrainNoise.Gaussian(seed, 437, key) - elapsed * .035));
                double relief = .12 * Math.Exp(.17 * TerrainNoise.Gaussian(seed, 438, key) - elapsed * .14);
                double peak = cfg.SeaLevel - .10 + (.10 + relief) * pulse - .006 * Math.Max(0, elapsed - 4);
                result.Add(new HotspotSample { X = x - Math.Sin(heading) * lateral * scale, Y = y + Math.Cos(heading) * lateral * scale,
                    Age = elapsed, Radius = radius, Peak = peak, Uplift = pulse, Activity = extinct || step > 2 ? 0 : pulse * Math.Exp(-step * 2) });
            }
            return result;
        }
        internal static void Hotspots(GenerationContext c, bool includeExtinct = true)
        {
            var w = c.World; var cfg = c.Config; double scale = Math.Max(.65, cfg.Width / 256.0);
            var origins = new List<int>();
            // Every moving window stamps against the same initial ocean floor. It can
            // overlap earlier uplift in its own track without rejecting it as new land.
            var oceanFloor = w.Tiles.Select(t => t.Elevation).ToArray();
            var tectonicActivity = w.Tiles.Select(t => t.Volcanism).ToArray(); var hotspotActivity = new float[w.Tiles.Length];
            int historical = includeExtinct ? cfg.Hotspots * (cfg.Shape == WorldShape.Archipelagos ? 5 : 2) : 0;
            for (int h = 0; h < cfg.Hotspots + historical; h++)
            {
                bool extinct = h >= cfg.Hotspots;
                int origin = -1;
                for (int trial = 0; trial < 200; trial++)
                {
                    int id = (int)(Numbers.Random(w.Seed, 380, h * 200 + trial) * w.Tiles.Length);
                    int row = id / cfg.Width;
                    if (row < cfg.Height * .10 || row > cfg.Height * .90 || oceanFloor[id] > cfg.SeaLevel - .07 || origins.Any(o => c.Map.Distance(o, id) < scale * (extinct ? 8 : 15))) continue;
                    origin = id; break;
                }
                if (origin < 0) continue; origins.Add(origin);
                foreach (var sample in HotspotTrack(c, origin, h, extinct))
                {
                    int col = (int)Math.Round(sample.X), row = (int)Math.Round(sample.Y);
                    int center = c.Map.At(col, row); if (center < 0 || oceanFloor[center] > cfg.SeaLevel - .025) continue;
                    double radius = sample.Radius, peak = sample.Peak;
                    foreach (int id in c.Map.Radius(center, Math.Max(2, (int)Math.Ceiling(radius * 2))))
                    {
                        var t = w.Tiles[id]; double dr = id / cfg.Width - center / cfg.Width;
                        double dq = TerrainNoise.LongitudeDelta(id % cfg.Width / (double)cfg.Width, center % cfg.Width / (double)cfg.Width) * cfg.Width + dr * .5;
                        double d = Math.Sqrt(dq * dq + dr * dr * .75) / radius;
                        if (d >= 2) continue;
                        double rough = 1 + .20 * TerrainNoise.Fractal(w.Seed, 390, id % cfg.Width / (double)cfg.Width, id / cfg.Width / (double)cfg.Height, 128, 2);
                        // Fade into the actual seabed, not a shared raised floor that would
                        // connect long tracks with flat, sharp-edged underwater roads.
                        double edge = Numbers.Clamp((d - 1.5) * 2, 0, 1), taper = 1 - edge * edge * (3 - 2 * edge);
                        double target = oceanFloor[id] + Math.Max(0, peak - oceanFloor[id]) * Math.Exp(-d * d * rough) * taper;
                        if (target > t.Elevation) t.Elevation = (float)target;
                        // Activity is a separate maximum envelope. An older overlapping
                        // landform must not suppress a younger active window's signature.
                        hotspotActivity[id] = Math.Max(hotspotActivity[id], (float)((target - oceanFloor[id]) * sample.Activity));
                        t.Volcanism = tectonicActivity[id] + hotspotActivity[id];
                    }
                }
            }
        }
    }
}
