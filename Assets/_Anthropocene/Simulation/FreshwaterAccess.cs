using System.Linq;

namespace Anthropocene.Simulation
{
    public static class FreshwaterAccess
    {
        // A land animal may drink at an adjacent shore/bank. Allocation still removes water from the actual finite source tile.
        public static int Source(WorldState world, int tile, PlanningIndex index = null) => world.Map.Radius(tile, 1)
            .OrderByDescending(n => (index == null ? LazyEcology.ProjectEcology(world, n, world.Days) : index.Ecology(n)).Freshwater).ThenBy(n => n == tile ? 0 : 1).ThenBy(n => n).First();
        public static double Available(WorldState world, int tile, PlanningIndex index = null)
        { int source = Source(world, tile, index); return (index == null ? LazyEcology.ProjectEcology(world, source, world.Days) : index.Ecology(source)).Freshwater; }
    }
}
