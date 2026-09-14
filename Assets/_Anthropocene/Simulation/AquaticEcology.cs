using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Light/nutrient/upwelling proxies, not a physical current or sea-ice simulation.
    // Generated capacities/rates and aquatic vegetation are saved, never regenerated on load.
    public static class AquaticEcology
    {
        public const double ShallowsHarvestableShare = .5, OceanHarvestableShare = .1;
        public const double ShallowsRenewalFraction = .65, OceanRenewalFraction = .08;
        public const double UpwellingBiomassBonus = 4, UpwellingRenewalBonus = 1;
        public const double ConditionRenewalFraction = .1;

        public static double UpwellingStrength(WorldState world, Tile tile)
        {
            if (tile.Environment != EnvironmentKind.Shallows) return 0;
            // One deep-ocean neighbor supplies half the bonus; two or more supply all of it.
            // HexMap handles the longitude seam. Inland lakes never receive marine nutrients.
            return Math.Min(1, world.Map.Neighbors(tile.Id).Count(id => world.Tiles[id].Environment == EnvironmentKind.Ocean) / 2.0);
        }
        public static double HarvestableShare(Tile tile) => tile.Environment == EnvironmentKind.Ocean ? OceanHarvestableShare : ShallowsHarvestableShare;
        public static VegetationTier PlanktonPotential(Tile tile) => tile.Environment == EnvironmentKind.Shallows ? VegetationTier.High : tile.Environment == EnvironmentKind.Lake ? VegetationTier.Medium : VegetationTier.Low;
        public static double RenewalPerDay(SimulationConfig config, Tile tile, double productivity, double upwelling)
        {
            double fraction = tile.Environment == EnvironmentKind.Ocean ? OceanRenewalFraction : tile.Environment == EnvironmentKind.Shallows ? ShallowsRenewalFraction : config.FoodRegeneration;
            return EcologyMath.DailyRate(fraction, config.BaseDays) * productivity * (1 + UpwellingRenewalBonus * upwelling);
        }
        // Ecosystem condition responds to the stored biological turnover, independently of
        // terrestrial vegetation. The coefficient transfers 10% of biomass renewal to recovery.
        public static double ConditionRecoveryPerDay(Tile tile) => ConditionRenewalFraction * tile.Seasons.Average(s => s.BiomassRecoveryPerDay);

        public static double Productivity(WorldState world, Tile tile, int season)
        {
            if (!tile.Aquatic && tile.Environment != EnvironmentKind.Ocean) throw new ArgumentException("Aquatic productivity requires a water tile.");
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
