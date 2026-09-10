using System;

namespace Anthropocene.Simulation
{
    public static class FoodEcology
    {
        public static double GatherableFraction(Tile tile, SimulationConfig config)
        {
            if (tile.Aquatic || tile.Terrain == Terrain.Ocean || tile.Terrain == Terrain.Ice) return 1;
            double fraction = tile.Terrain == Terrain.Woodland || tile.Terrain == Terrain.Jungle ? config.RichGatherableFraction :
                tile.Terrain == Terrain.Desert ? config.AridGatherableFraction : tile.Terrain == Terrain.Tundra ? config.TundraGatherableFraction : config.GrasslandGatherableFraction;
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
                ecology.MaxPotentialFood = (float)(ecology.MaxFood / fraction);
                ecology.PotentialFoodRate = ecology.FoodRate;
                tile.Seasons[season] = ecology;
            }
            if (initializeStock) tile.PotentialFood = Math.Max(tile.Food, tile.Seasons[0].MaxPotentialFood * .9);
            else tile.PotentialFood = Math.Max(tile.Food, Math.Min(tile.PotentialFood, tile.Seasons[0].MaxPotentialFood));
        }
    }
}
