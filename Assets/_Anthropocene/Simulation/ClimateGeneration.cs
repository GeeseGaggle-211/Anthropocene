using System;
using System.Collections.Generic;

namespace Anthropocene.Simulation
{
    // Original cylindrical-grid implementation. The upwind humidity / ocean recharge /
    // orographic extraction decomposition is informed by Red Blob Games' mapgen4.
    // This is a deterministic climate proxy, not a fluid solver or a weather simulation.
    internal sealed class ClimateStage : IWorldGenerationStage
    {
        public string Name => "Seasonal two-dimensional moisture transport";
        public string Requires => "Elevation, land/ocean";
        public string Produces => "Seasonal temperature, precipitation, snowfall, wind";

        // Atmospheric distance is normalized to the planet, never to gameplay tile count.
        // A 1024-column world must not lose ten times more moisture than a 96-column one.
        private const int TransportStepsPerCircuit = 48;
        private const int SpinupSteps = 240;

        public void Generate(GenerationContext c)
        {
            var cfg = c.Config;
            var tiles = c.World.Tiles;
            c.ClimateSurfaceHeights = AltitudeClimate.Physical(cfg) ? new float[tiles.Length] : null;
            if (c.ClimateSurfaceHeights != null)
                for (int i = 0; i < tiles.Length; i++) c.ClimateSurfaceHeights[i] = tiles[i].Elevation;
            int width = Math.Min(256, cfg.Width), height = Math.Min(128, cfg.Height), count = width * height;
            var elevation = new float[count];
            var ocean = new float[count];
            var samples = new int[count];
            // Area-average each atmospheric cell so small coasts are not aliased away.
            foreach (var tile in tiles)
            {
                int cell = (tile.Id / cfg.Width * height / cfg.Height) * width + tile.Id % cfg.Width * width / cfg.Width;
                elevation[cell] += tile.Elevation;
                if (!tile.Land) ocean[cell]++;
                samples[cell]++;
            }
            for (int i = 0; i < count; i++) { elevation[i] /= samples[i]; ocean[i] /= samples[i]; }
            var maritime = MaritimeInfluence(ocean, width, height);
            var broadNoise = new float[count];
            var windNoise = new float[count];
            for (int row = 0; row < height; row++)
                for (int col = 0; col < width; col++)
                {
                    int i = row * width + col;
                    double x = col / (double)width, y = row / (double)(height - 1);
                    broadNoise[i] = (float)TerrainNoise.Fractal(c.World.Seed, 270, x, y, 3, 3);
                    windNoise[i] = (float)TerrainNoise.Fractal(c.World.Seed, 271, x, y, 5, 3);
                }

            var upstream = new Sample[count];
            var condensation = new float[count];
            var evaporation = new float[count];
            var saturation = new float[count];
            var windX = new float[count];
            var humidity = new float[count];
            var nextHumidity = new float[count];
            var precipitation = new float[count];
            double retained = Math.Pow(Numbers.Clamp(cfg.MoistureDecay, 0, 1), .2);
            double baseline = cfg.InnerTemperature + (cfg.OuterTemperature - cfg.InnerTemperature) * cfg.HabitableZonePosition;

            for (int season = 0; season < 4; season++)
            {
                double phase = Math.Sin(season * Math.PI / 2);
                for (int row = 0; row < height; row++)
                    for (int col = 0; col < width; col++)
                    {
                        int i = row * width + col;
                        double latitude = row / (double)(height - 1) * 2 - 1;
                        double circulationLatitude = latitude + .11 * broadNoise[i] - phase * (.035 + cfg.Monsoon * .25);
                        double absLatitude = Math.Abs(circulationLatitude);
                        double tropics = Bell(circulationLatitude / .20);
                        double subtropics = Bell((absLatitude - .34) / .13);
                        double storms = Bell((absLatitude - .59) / .20);
                        // Smooth trade winds, midlatitude westerlies and polar easterlies.
                        // Meridional flow and seasonal shifts allow neighboring latitudes to exchange moisture.
                        double zonal = -.90 + 1.90 * Bell((absLatitude - .56) / .23);
                        double meridional = -.28 * Math.Sin(3 * Math.PI * circulationLatitude) + .55 * windNoise[i] + phase * cfg.Monsoon;
                        double speed = Math.Sqrt(zonal * zonal + meridional * meridional);
                        if (speed < .35) { zonal += zonal < 0 ? -.35 : .35; speed = Math.Sqrt(zonal * zonal + meridional * meridional); }
                        double dx = zonal / speed * width / TransportStepsPerCircuit;
                        double dy = meridional / speed * (height - 1) * 2 / TransportStepsPerCircuit;
                        windX[i] = (float)(zonal / speed);
                        upstream[i] = new Sample(col - dx, row - dy, width, height);
                        double rise = Math.Max(0, elevation[i] - upstream[i].Read(elevation));
                        double descent = Math.Max(0, upstream[i].Read(elevation) - elevation[i]);
                        double lift = cfg.OrographicRain * rise;
                        double cloudFormation = .014 + .105 * tropics + .078 * storms - .011 * subtropics;
                        double lee = Math.Exp(-descent * Math.Max(0, cfg.OrographicRain) * 3);
                        condensation[i] = (float)Numbers.Clamp((cloudFormation + lift) * lee, .004, .65);
                        double temp = Temperature(cfg, baseline, latitude, broadNoise[i], elevation[i], maritime[i], phase);
                        // Cold seas still evaporate, but the atmospheric water-carrying capacity falls.
                        saturation[i] = (float)Numbers.Clamp((temp + 24) / 48, .18, 1);
                        evaporation[i] = (float)Numbers.Clamp(cfg.Evaporation * ocean[i] * 2.4, 0, .95);
                        humidity[i] = saturation[i] * (.25f + .60f * ocean[i]);
                    }
                // Synchronous reads make longitude origin, tile iteration order and worker scheduling irrelevant.
                // Several circuits remove the initial humidity field without introducing an artificial wet seam.
                for (int step = 0; step < SpinupSteps; step++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        double water = upstream[i].Read(humidity);
                        water += evaporation[i] * Math.Max(0, saturation[i] - water);
                        double rain = water * condensation[i];
                        // Immediate land recycling is a coarse evapotranspiration proxy. It returns only
                        // part of realized precipitation, so it cannot create water in a dry air mass.
                        double export = rain * (.55 + .45 * ocean[i]);
                        nextHumidity[i] = (float)Math.Max(0, (water - export) * retained);
                        if (step == SpinupSteps - 1) precipitation[i] = (float)rain;
                    }
                    var swap = humidity; humidity = nextHumidity; nextHumidity = swap;
                }

                foreach (var tile in tiles)
                {
                    double gx = tile.Id % cfg.Width * width / (double)cfg.Width;
                    double gy = tile.Id / cfg.Width * (height - 1) / (double)(cfg.Height - 1);
                    var sample = new Sample(gx, gy, width, height);
                    double latitude = tile.Id / cfg.Width / (double)(cfg.Height - 1) * 2 - 1;
                    double temp = Temperature(cfg, baseline, latitude, sample.Read(broadNoise), tile.Elevation, tile.Land ? sample.Read(maritime) : 1, phase);
                    double rain = sample.Read(precipitation);
                    tile.Seasons[season] = new SeasonData
                    {
                        Temperature = (float)temp,
                        Rain = (float)(temp > 0 ? rain : 0),
                        Snow = (float)(temp <= 0 ? rain : 0),
                        WindX = sample.Read(windX),
                        Runoff = (float)rain
                    };
                }
            }
        }

        private static double Temperature(SimulationConfig cfg, double baseline, double latitude, double anomaly, double elevation, double maritime, double phase)
        {
            double thermalLatitude = Numbers.Clamp(Math.Abs(latitude + .045 * anomaly), 0, 1);
            double annual = 29 + baseline - cfg.LatitudeGradient * Math.Pow(thermalLatitude, 1.3)
                - AltitudeClimate.Cooling(cfg, elevation) + 4 * anomaly + 2 * thermalLatitude * maritime;
            double seasonal = phase * latitude * cfg.SeasonalAmplitude * (1 - cfg.OceanModeration * maritime);
            return annual + seasonal;
        }

        private static double Bell(double x) => Math.Exp(-x * x);

        private static float[] MaritimeInfluence(float[] ocean, int width, int height)
        {
            var distance = new int[ocean.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < ocean.Length; i++) { distance[i] = -1; if (ocean[i] > .2) { distance[i] = 0; queue.Enqueue(i); } }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), col = i % width, row = i / width;
                Visit(row * width + (col + width - 1) % width);
                Visit(row * width + (col + 1) % width);
                if (row > 0) Visit(i - width);
                if (row + 1 < height) Visit(i + width);
                void Visit(int n) { if (distance[n] >= 0) return; distance[n] = distance[i] + 1; queue.Enqueue(n); }
            }
            var influence = new float[ocean.Length];
            for (int i = 0; i < influence.Length; i++) influence[i] = distance[i] < 0 ? 0 : (float)Math.Exp(-distance[i] / (width * .035));
            return influence;
        }

        private readonly struct Sample
        {
            private readonly int a, b, c, d;
            private readonly float x, y;
            public Sample(double column, double row, int width, int height)
            {
                column -= Math.Floor(column / width) * width;
                row = Numbers.Clamp(row, 0, height - 1);
                int left = (int)Math.Floor(column), right = (left + 1) % width, top = (int)Math.Floor(row), bottom = Math.Min(top + 1, height - 1);
                a = top * width + left; b = top * width + right; c = bottom * width + left; d = bottom * width + right;
                x = (float)(column - left); y = (float)(row - top);
            }
            public float Read(float[] values) => (values[a] * (1 - x) + values[b] * x) * (1 - y) + (values[c] * (1 - x) + values[d] * x) * y;
        }
    }
}
