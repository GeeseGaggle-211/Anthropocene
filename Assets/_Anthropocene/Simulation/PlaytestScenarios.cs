using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    /// <summary>Reproducible natural starts. Selection changes human ownership, never the landscape.</summary>
    public static class PlaytestScenarios
    {
        public const int Seed = 1729;
        public static readonly string[] Names = { "Custom world", "Desert river valley", "Steppe hunters", "Cold coastal fishers" };
        public static readonly string[] Descriptions = {
            "Choose your own seed, world size and tribe count.",
            "Nile / Mesopotamia analogue: a warm desert river with riparian food and dry surroundings. Test gathering and cultivation.",
            "Steppe analogue: open grassland, a freshwater river and nearby bison. Test mobile hunters and animal husbandry.",
            "Scandinavian analogue: a cold shore with five adjacent shallows and freshwater on land. Test fishing and seasonal reserves."
        };
        public static int Tile(int scenario) => scenario == 1 ? 217996 : scenario == 2 ? 148177 : scenario == 3 ? 408950 : -1;
        public static void Configure(SimulationConfig config)
        { config.Width = 1024; config.Height = 512; config.Players = 80; config.Shape = WorldShape.Continents; config.StartSeparation = 12; }

        public static bool Matches(WorldState world, int scenario)
        {
            int id = Tile(scenario);
            if (id < 0 || id >= world.Tiles.Length || world.Seed != Seed || world.Config.Width != 1024 || world.Config.Height != 512) return false;
            var tile = world.Tiles[id]; var near = world.Map.Radius(id, 1).Select(n => world.Tiles[n]).ToArray();
            if (!world.Camps.Any(c => c.Tile == id) || PlacementStage.Viability(world, id) < world.Config.StartViability) return false;
            if (scenario == 1) return tile.Environment == EnvironmentKind.Desert && tile.River && tile.CurrentVegetation == VegetationTier.Medium && near.All(t => t.Environment == EnvironmentKind.Desert);
            if (scenario == 2) return tile.Environment == EnvironmentKind.Grassland && near.Count(t => t.Environment == EnvironmentKind.Grassland) >= 5 && near.All(t => t.TraversableLand && t.CurrentVegetation != VegetationTier.High) && world.Herds.Any(h => h.Species == "bison" && world.Map.Reachable(id, h.Tile, 2, n => world.Tiles[n].TraversableLand));
            return tile.Environment == EnvironmentKind.Cold && near.Count(t => t.Environment == EnvironmentKind.Shallows) >= 5 && tile.FreshwaterSource != FreshwaterSource.None;
        }

        public static void Select(WorldState world, int scenario)
        {
            if (scenario < 1 || scenario > 3) throw new ArgumentOutOfRangeException(nameof(scenario));
            if (world.Turn != 1 || world.Days != 0 || world.Hunters.Count != 0 || !Matches(world, scenario))
                throw new InvalidOperationException("This preset no longer matches the generated opening. Check its habitat and viability before updating the preset.");
            var selected = world.Camps.Single(c => c.Tile == Tile(scenario));
            int previous = selected.Player;
            if (previous == 0) return;
            foreach (var society in world.Societies)
                if (society.Player == 0) society.Player = previous;
                else if (society.Player == previous) society.Player = 0;
            Ownership.Bind(world);
        }

        // Profile potential, in food units per base season, before workers, depletion and competition.
        public static double Food(WorldState world, Tile tile, int season)
        {
            if (tile.Environment == EnvironmentKind.Ocean || tile.ElevationLevel >= 10) return 0;
            if (season < 0) { double sum = 0; for (int s = 0; s < 4; s++) sum += Food(world, tile, s); return sum / 4; }
            var p = tile.Seasons[season];
            return p.MaxFood * p.FoodRecoveryPerDay * world.Config.BaseDays * world.Config.SustainableProductionFraction;
        }
        public static double Water(WorldState world, Tile tile, int season)
        {
            if (tile.WaterBody == WaterKind.Saltwater || tile.FreshwaterSource == FreshwaterSource.None || tile.ElevationLevel >= 10) return 0;
            if (season < 0) { double sum = 0; for (int s = 0; s < 4; s++) sum += Water(world, tile, s); return sum / 4; }
            var p = tile.Seasons[season]; return p.MaxFreshwater * p.WaterRecoveryPerDay * world.Config.BaseDays;
        }
        public static double Viability(WorldState world, int tile) => PlacementStage.Viability(world, tile);
    }
}
