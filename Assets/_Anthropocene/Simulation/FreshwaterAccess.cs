using System.Linq;

namespace Anthropocene.Simulation
{
    public static class FreshwaterAccess
    {
        // A land animal may drink at an adjacent shore/bank. Allocation still removes water from the actual finite source tile.
        public static int Source(WorldState world, int tile) => world.Map.Radius(tile, 1)
            .OrderByDescending(n => world.Tiles[n].DrinkableWater).ThenBy(n => n == tile ? 0 : 1).ThenBy(n => n).First();
        public static double Available(WorldState world, int tile) => world.Tiles[Source(world, tile)].DrinkableWater;
    }
}
