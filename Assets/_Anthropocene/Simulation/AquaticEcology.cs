using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Generation-only proxies, not a current/upwelling or sea-ice simulation. The resulting
    // capacities and daily rates are saved; runtime ecology never recomputes this forcing.
    public static class AquaticEcology
    {
        public static double Productivity(WorldState world, Tile tile, int season)
        {
            if (tile.Environment == EnvironmentKind.Ocean) return .005; // Outside opening harvest reach.
            if (!tile.Aquatic) throw new ArgumentException("Aquatic productivity requires a water tile.");
            double meanTemperature = tile.Seasons.Average(s => s.Temperature);
            double temperature = tile.Seasons[season].Temperature;
            double latitude = (tile.Id / world.Config.Width) / (double)(world.Config.Height - 1) * 2 - 1;
            // Same hemispheric season phase as ClimateStage; a coarse light-availability proxy.
            double light = Numbers.Clamp(1 + .75 * latitude * Math.Sin(season * Math.PI / 2), .25, 1.5);
            int columns = (world.Config.Width + 15) / 16;
            int region = (tile.Id / world.Config.Width / 16) * columns + tile.Id % world.Config.Width / 16;
            double variation = .85 + .30 * Numbers.Random(world.Seed, 142, region);
            double fertility, openWater;
            if (tile.Environment == EnvironmentKind.Lake)
            {
                // Freshwater productivity responds modestly to warmth and catchment supply,
                // rather than inheriting the marine cool-water nutrient advantage.
                double catchment = Numbers.Clamp(Math.Log(1 + Math.Max(0, tile.Flow)) / Math.Log(1 + Math.Max(1, world.Config.RiverThreshold) * 10), 0, 1);
                fertility = .65 + .20 * Numbers.Clamp(meanTemperature / 20, 0, 1) + .10 * catchment;
                openWater = Numbers.Clamp((temperature + 5) / 10, .04, 1);
            }
            else
            {
                // Cooler coasts are a proxy for nutrient replenishment/mixing. Warm coasts
                // retain a useful baseline; cold winter air reduces seasonal availability.
                fertility = .70 + .60 * Numbers.Clamp((22 - meanTemperature) / 18, 0, 1);
                openWater = Numbers.Clamp((temperature + 8) / 8, .04, 1);
            }
            return fertility * variation * light * openWater;
        }
    }
}
