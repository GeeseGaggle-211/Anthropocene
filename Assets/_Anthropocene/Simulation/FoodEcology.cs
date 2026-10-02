using System;

namespace Anthropocene.Simulation
{
    public static class FoodEcology
    {
        // Generator 14: one biomass normalization, fitted to generator-13 fertile
        // land food. Accessibility never enters biomass production. At m=63 the
        // base is 53,190.407 food-equivalent biomass units (see calibration notes).
        public const double LandBiomassBase = 844.2921784808092;
        public static double LandProductivity(Tile tile, SeasonData season, SimulationConfig config)
        {
            if (!tile.Land || tile.Environment == EnvironmentKind.Ice) return 0;
            double warmth = Numbers.Clamp((season.Temperature + 8) / 30.0, .015, 1);
            double relief = tile.Relief == Relief.Mountain ? config.MountainProductivity
                : tile.Relief == Relief.Highland ? config.HighlandProductivity
                : tile.Relief == Relief.Hills ? config.HillsProductivity : 1;
            return Numbers.Clamp(season.Moisture, 0, 1) * warmth * relief;
        }

        public static double GatherableFraction(Tile tile, SimulationConfig config) => GatherableFraction(tile, config, tile.CurrentVegetation);
        public static double GatherableFraction(Tile tile, SimulationConfig config, VegetationTier vegetation)
        {
            if (tile.Aquatic || tile.Terrain == Terrain.Ocean) return AquaticEcology.HarvestableShare(tile);
            if (tile.Terrain == Terrain.Ice) return 1;
            double fraction = tile.Environment == EnvironmentKind.Desert ? config.AridGatherableFraction : tile.Environment == EnvironmentKind.Cold ? config.TundraGatherableFraction : config.GrasslandGatherableFraction;
            if ((tile.Environment == EnvironmentKind.Temperate || tile.Environment == EnvironmentKind.Tropical) && vegetation >= VegetationTier.Medium) fraction = config.RichGatherableFraction;
            if (tile.River) fraction = Math.Max(fraction, config.RichGatherableFraction);
            if (tile.OasisInfluence > 0) fraction += (config.RichGatherableFraction - fraction) * tile.OasisInfluence;
            return Numbers.Clamp(fraction, .001, 1);
        }

        public static void ConfigurePotential(Tile tile, SimulationConfig config, bool initializeStock)
        {
            double fraction = GatherableFraction(tile, config);
            for (int season = 0; season < tile.Seasons.Length; season++)
            {
                var ecology = tile.Seasons[season];
                if (tile.Land)
                {
                    ecology.MaxPotentialFood = (float)(LandBiomassBase * config.LandBiomassMultiplier * LandProductivity(tile, ecology, config));
                    ecology.MaxFood = (float)(ecology.MaxPotentialFood * fraction);
                }
                else ecology.MaxPotentialFood = (float)(ecology.MaxFood / fraction);
                ecology.BiomassRecoveryPerDay = ecology.FoodRecoveryPerDay;
                tile.Seasons[season] = ecology;
            }
            tile.Food = Math.Min(Math.Max(0, tile.Food), tile.Seasons[0].MaxFood);
            if (initializeStock) tile.PotentialFood = Math.Max(tile.Food, tile.Seasons[0].MaxPotentialFood * .9);
            else tile.PotentialFood = Math.Max(tile.Food, Math.Min(tile.PotentialFood, tile.Seasons[0].MaxPotentialFood));
        }
    }
}
