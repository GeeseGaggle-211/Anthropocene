using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class ElevationLevelStage : IWorldGenerationStage
    {
        public string Name => "Distribution-aware elevation levels and ridge connections";
        public string Requires => "Final continuous elevation and hydrology";
        public string Produces => "Gameplay levels 0–10, independent relief and ridge masks";
        public static string Description(int level) => level == 0 ? "Marine sea level" : level <= 2 ? "Lowland" : level <= 4 ? "Rolling hills" : level <= 6 ? "Ridges and highland" : level <= 9 ? "Mountains" : "Impassable peak";
        public void Generate(GenerationContext context)
        {
            var w = context.World;
            var land = w.Tiles.Where(t => t.Land).Select(t => t.Elevation).OrderBy(h => h).ToArray();
            var boundaries = w.Config.ElevationQuantiles.Select(q => land[Math.Min(land.Length - 1, (int)(q * land.Length))]).ToArray();
            foreach (var tile in w.Tiles)
            {
                if (tile.WaterBody == WaterKind.Saltwater) tile.ElevationLevel = 0;
                else
                {
                    int level = 1; while (level < 10 && tile.Elevation > boundaries[level - 1]) level++;
                    tile.ElevationLevel = tile.Terrain == Terrain.Lake ? Math.Min(9, level) : level;
                }
                int e = tile.ElevationLevel;
                tile.Relief = e <= 2 ? Relief.Lowland : e <= 4 ? Relief.Hills : e <= 6 ? Relief.Highland : Relief.Mountain;
                tile.RidgeMask = 0;
            }
            // Contiguous high ground is connected along neighboring centers. Continuous heights are untouched.
            foreach (var tile in w.Tiles.Where(t => t.Land && t.ElevationLevel >= 5))
                for (int direction = 0; direction < 6; direction++)
                {
                    int n = Neighbor(w, tile.Id, direction);
                    if (n >= 0 && w.Tiles[n].Land && w.Tiles[n].ElevationLevel >= 5 && Math.Abs(tile.ElevationLevel - w.Tiles[n].ElevationLevel) <= 2) tile.RidgeMask |= 1 << direction;
                }
        }
        public static int Neighbor(WorldState w, int tile, int direction)
        {
            int[] dx = { 1, 0, -1, -1, 0, 1 }, dy = { 0, 1, 1, 0, -1, -1 };
            return w.Map.At(tile % w.Config.Width + dx[direction], tile / w.Config.Width + dy[direction]);
        }
        public static void Validate(WorldState w)
        {
            foreach (var t in w.Tiles)
                if (t.ElevationLevel < 0 || t.ElevationLevel > 10 || (t.WaterBody == WaterKind.Saltwater) != (t.ElevationLevel == 0) || t.Terrain == Terrain.Lake && t.ElevationLevel == 10) throw new InvalidOperationException("Invalid gameplay elevation classification.");
        }
    }
}
