using System;

namespace Anthropocene.Simulation
{
    public static class FreshwaterAccess
    {
        // A land animal may drink at an adjacent shore/bank. Allocation still removes water from the actual finite source tile.
        // Most projected freshwater wins; ties prefer the tile itself, then the lowest id.
        public static int Source(WorldState world, int tile, PlanningIndex index = null)
        {
            int best = -1; double bestWater = 0;
            foreach (int n in world.Map.Radius(tile, 1))
            {
                double water = Freshwater(world, n, index);
                if (best < 0) { best = n; bestWater = water; continue; }
                int order = water.CompareTo(bestWater);
                if (order == 0) order = (best == tile ? 0 : 1) - (n == tile ? 0 : 1);
                if (order == 0) order = best - n;
                if (order > 0) { best = n; bestWater = water; }
            }
            if (best < 0) throw new InvalidOperationException("Sequence contains no elements");
            return best;
        }
        public static double Available(WorldState world, int tile, PlanningIndex index = null)
            => Freshwater(world, Source(world, tile, index), index);
        private static double Freshwater(WorldState world, int tile, PlanningIndex index)
            => (index == null ? LazyEcology.ProjectEcology(world, tile, world.Days) : index.Ecology(tile)).Freshwater;
    }
}
