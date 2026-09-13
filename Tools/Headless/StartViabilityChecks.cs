using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class StartViabilityChecks
{
    private const int Center = 8 * 24 + 6;
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Start viability: " + message); }
    private static WorldState Coast(float temperature)
    {
        var cfg = SimulationConfig.Small(1); cfg.Width = 24; cfg.Height = 17;
        var w = new WorldState { Seed = 41, Config = cfg, Tiles = new Tile[cfg.Width * cfg.Height] };
        int shore = Center + 1;
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, Environment = i == Center ? EnvironmentKind.Desert : i == shore ? EnvironmentKind.Shallows : EnvironmentKind.Ocean, WaterBody = i == Center ? WaterKind.None : WaterKind.Saltwater, ElevationLevel = i == Center ? 1 : 0, FreshwaterSource = i == Center ? FreshwaterSource.RainPool : FreshwaterSource.None };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = temperature };
            w.Tiles[i] = t;
        }
        new VegetationResourceStage().Generate(new GenerationContext { World = w, Map = w.Map });
        // Isolate one genuinely generated coastal food source and a renewable drinking pool.
        var bank = w.Tiles[Center]; bank.Food = bank.PotentialFood = 0; bank.Freshwater = 30;
        for (int s = 0; s < 4; s++) { bank.Seasons[s].MaxFood = bank.Seasons[s].MaxPotentialFood = 0; bank.Seasons[s].MaxFreshwater = 30; bank.Seasons[s].WaterRecoveryPerDay = .01f; }
        return w;
    }
    public static void Run()
    {
        checks = 0;
        var cool = Coast(4); var warm = Coast(28); var frozen = Coast(-15);
        double score = PlacementStage.Viability(cool, Center);
        Check(score >= cool.Config.StartViability && score > PlacementStage.Viability(warm, Center) && PlacementStage.Viability(frozen, Center) < cool.Config.StartViability, "new coastal fertility affects viable starts without rewarding extreme-cold water");
        var food = cool.Tiles[Center + 1]; var bank = cool.Tiles[Center];
        food.Food = food.PotentialFood = 1e6;
        Check(PlacementStage.Viability(cool, Center) == score, "an initial stock or large herd-biomass store cannot inflate recurring support");
        for (int s = 0; s < 4; s++) food.Seasons[s].FoodRecoveryPerDay = 0;
        Check(PlacementStage.Viability(cool, Center) == 0, "a food-rich but nonrenewing shore is not viable");
        cool = Coast(4); food = cool.Tiles[Center + 1]; bank = cool.Tiles[Center];
        for (int s = 1; s < 4; s++) food.Seasons[s].FoodRecoveryPerDay = 0;
        Check(PlacementStage.Viability(cool, Center) < cool.Config.StartViability, "first-season abundance alone cannot pass a four-season check");
        food.Seasons[0].MaxFood = 1e8f;
        Check(PlacementStage.Viability(cool, Center) <= 100 * cool.Config.GatherPerPerson / (4 * cool.Config.Consumption) + 1e-6, "one huge bloom is capped by that season's shared labor");
        cool = Coast(4); bank = cool.Tiles[Center];
        bank.Seasons[2].MaxFreshwater = 0;
        Check(PlacementStage.Viability(cool, Center) == 0, "seasonal loss of all freshwater capacity rejects a start");
        cool = Coast(4); bank = cool.Tiles[Center];
        for (int s = 0; s < 4; s++) bank.Seasons[s].WaterRecoveryPerDay = 0;
        Check(PlacementStage.Viability(cool, Center) == 0, "a one-time freshwater stock without renewal rejects a start");
        cool = Coast(4); cool.Tiles[Center].FreshwaterSource = FreshwaterSource.None;
        Check(PlacementStage.Viability(cool, Center) == 0, "productive seawater never satisfies freshwater eligibility");
        cool = Coast(4); food = cool.Tiles[Center + 1]; food.Environment = EnvironmentKind.Ocean;
        Check(PlacementStage.Viability(cool, Center) == 0, "even an artificially rich ocean is outside opening gathering reach");
        food.Environment = EnvironmentKind.Grassland; food.ElevationLevel = 10;
        Check(PlacementStage.Viability(cool, Center) == 0, "impassable peak resources cannot make an adjacent start viable");
        cool = Coast(4); cool.Tiles[Center].ElevationLevel = 10;
        Check(PlacementStage.Viability(cool, Center) == 0, "the starting camp tile itself must be traversable land");
        cool = Coast(4); food = cool.Tiles[Center + 1]; food.Environment = EnvironmentKind.Desert; food.WaterBody = WaterKind.None; food.ElevationLevel = 1;
        Check(Math.Abs(PlacementStage.Viability(cool, Center) - score) < 1e-6, "a productive dry river valley receives credit from its local food profiles, not its Desert label");
        for (int s = 0; s < 4; s++) { food.Seasons[s].MaxFood = 1; food.Seasons[s].MaxPotentialFood = 10000; }
        Check(PlacementStage.Viability(cool, Center) < cool.Config.StartViability, "inaccessible forage biomass cannot stand in for human food");
        cool = Coast(4); food = cool.Tiles[Center + 1];
        foreach (int n in cool.Map.Radius(Center, 1).Where(n => n != Center))
        {
            var t = cool.Tiles[n]; t.Environment = EnvironmentKind.Shallows;
            for (int s = 0; s < 4; s++) { t.Seasons[s].MaxFood = 1000; t.Seasons[s].FoodRecoveryPerDay = .1f; }
        }
        Check(Math.Abs(PlacementStage.Viability(cool, Center) - 100 * cool.Config.GatherPerPerson / cool.Config.Consumption) < 1e-6, "multiple land/coastal sources share one starting workforce ceiling");
        Console.WriteLine("PASS: " + checks + " seasonal start-viability assertions");
        Console.WriteLine("START_SUPPORT matched one-shore starts cool/warm/frozen=" + score.ToString("F1") + "/" + PlacementStage.Viability(warm, Center).ToString("F1") + "/" + PlacementStage.Viability(frozen, Center).ToString("F1") + "% of starting food need; threshold=" + cool.Config.StartViability);
    }
}
