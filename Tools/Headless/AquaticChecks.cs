using System;
using System.IO;
using System.Linq;
using Anthropocene.Simulation;

internal static class AquaticChecks
{
    private static int checks;
    private const int Center = 8 * 24 + 6;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Aquatic ecology: " + message); }
    private static WorldState Profile(float temperature, EnvironmentKind environment = EnvironmentKind.Shallows, float moisture = 0, int seed = 41, float flow = 0)
    {
        var cfg = SimulationConfig.Small(1); cfg.Width = 24; cfg.Height = 17;
        var w = new WorldState { Seed = seed, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            bool lake = environment == EnvironmentKind.Lake;
            var t = new Tile { Id = i, Environment = environment, ElevationLevel = lake ? 1 : 0, WaterBody = lake ? WaterKind.Freshwater : WaterKind.Saltwater, FreshwaterSource = lake ? FreshwaterSource.Lake : FreshwaterSource.None, Flow = flow };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = temperature, Moisture = moisture, RegionalMoisture = moisture, Rain = moisture, Snow = moisture };
            w.Tiles[i] = t;
        }
        new VegetationResourceStage().Generate(new GenerationContext { World = w, Map = w.Map });
        return w;
    }
    public static void Run()
    {
        checks = 0;
        var cool = Profile(4); var warm = Profile(28); var frozen = Profile(-15);
        var c = cool.Tiles[Center].Seasons[0]; var h = warm.Tiles[Center].Seasons[0]; var f = frozen.Tiles[Center].Seasons[0];
        Check(c.MaxFood > 1.7 * h.MaxFood && c.FoodRecoveryPerDay > 1.7 * h.FoodRecoveryPerDay, "cool unfrozen coast has greater capacity and renewal than equally situated warm coast");
        Check(h.MaxFood > 50 && h.FoodRecoveryPerDay > .002, "warm coastal waters retain useful food and renewal");
        Check(f.MaxFood < .1 * c.MaxFood && f.FoodRecoveryPerDay < .1 * c.FoodRecoveryPerDay, "extreme cold limits both food availability and renewal");
        var wet = Profile(4, moisture: 1);
        Check(cool.Tiles.SelectMany(t => t.Seasons).Select(s => (s.MaxFood, s.FoodRecoveryPerDay)).SequenceEqual(wet.Tiles.SelectMany(t => t.Seasons).Select(s => (s.MaxFood, s.FoodRecoveryPerDay))), "marine food ignores soil moisture, rainfall and snow inputs");
        Check(cool.Tiles.All(t => t.DrinkableWater == 0 && t.Seasons.All(s => s.MaxFreshwater == 0 && s.MaxSaltwater == 100)), "richer marine food does not create freshwater");
        var ocean = Profile(4, EnvironmentKind.Ocean).Tiles[Center];
        Check(ocean.Seasons[0].MaxFood < 1 && !ocean.Aquatic, "open ocean retains its negligible inaccessible opening-food profile");
        var north = cool.Tiles[0]; var south = cool.Tiles[16 * 24];
        Check(north.Seasons[3].MaxFood > 4 * north.Seasons[1].MaxFood && south.Seasons[1].MaxFood > 4 * south.Seasons[3].MaxFood, "polar light seasons reverse across hemispheres independently of temperature");
        Check(cool.Tiles[Center].Seasons.All(s => s.MaxFood == c.MaxFood), "equatorial light proxy has no artificial polar seasonality");
        var lakeCool = Profile(5, EnvironmentKind.Lake).Tiles[Center]; var lakeWarm = Profile(25, EnvironmentKind.Lake).Tiles[Center]; var lakeFrozen = Profile(-10, EnvironmentKind.Lake).Tiles[Center];
        Check(lakeWarm.Seasons[0].MaxFood > lakeCool.Seasons[0].MaxFood && lakeFrozen.Seasons[0].MaxFood < .1 * lakeCool.Seasons[0].MaxFood, "lakes use modest warmth support and their own freeze response, not the marine cold bonus");
        Check(lakeCool.DrinkableWater > 0 && lakeCool.Seasons.All(s => s.MaxFreshwater == 120 && s.MaxSaltwater == 0), "lake fisheries preserve finite freshwater and water identity");
        Check(Profile(5, EnvironmentKind.Lake, flow: 280).Tiles[Center].Seasons[0].MaxFood > lakeCool.Seasons[0].MaxFood, "lake catchment supply can increase productivity");
        Check(Profile(5, EnvironmentKind.Lake, moisture: 1).Tiles[Center].Seasons[0].FoodRecoveryPerDay == lakeCool.Seasons[0].FoodRecoveryPerDay, "lake renewal also excludes soil moisture");
        var twin = Profile(4); var varied = Profile(4, seed: 42);
        Check(twin.Tiles[Center].Seasons[0].MaxFood == c.MaxFood && varied.Tiles[Center].Seasons[0].MaxFood != c.MaxFood, "regional fertility variation is seeded and reproducible");
        Check(cool.Tiles.All(t => t.Seasons.All(s => s.MaxFood >= 0 && s.MaxPotentialFood == s.MaxFood && s.FoodRecoveryPerDay == s.BiomassRecoveryPerDay && !float.IsNaN(s.FoodRecoveryPerDay))), "aquatic food/biomass shares and daily rates remain finite and consistent");
        double coolHarvest = EcologyMath.Stock(c.MaxFood, c.MaxFood, c.FoodRecoveryPerDay, 10, 7300).Realized;
        double warmHarvest = EcologyMath.Stock(h.MaxFood, h.MaxFood, h.FoodRecoveryPerDay, 10, 7300).Realized;
        Check(coolHarvest > 2 * warmHarvest, "cool-water benefit persists under sustained harvest, beyond the initial stock");
        var longProjection = LazyEcology.ProjectEcology(cool, 0, 7300); var ordered = LazyEcology.ProjectEcology(cool, 0, 7300, false);
        Check(Math.Abs(longProjection.Food - ordered.Food) < 1e-6 && Math.Abs(longProjection.PotentialFood - ordered.PotentialFood) < 1e-6, "seasonal fishery profiles preserve exact lazy cycle composition");
        double untouched = cool.Tiles[0].Food; var materialized = LazyEcology.MaterializeEcology(cool, 0, 7300);
        Check(materialized.Food == longProjection.Food && cool.Tiles[0].LastEcologyUpdateDays == 7300 && untouched >= 0, "materialization agrees with projection and commits the latent timestamp");

        var generated = new WorldGenerator().Generate(83, SimulationConfig.Small(2));
        var copy = SaveCodec.Load(SaveCodec.Save(generated));
        Check(generated.Generation.GeneratorVersion == 8 && SaveCodec.Fingerprint(copy) == SaveCodec.Fingerprint(generated), "new generated fishery profiles round-trip without regeneration");
        // Same binary format as the previous release; preserve deliberately distinctive legacy forcing.
        generated.Generation.GeneratorVersion = 7; var legacyTile = generated.Tiles.First(t => t.Aquatic);
        for (int s = 0; s < 4; s++) { legacyTile.Seasons[s].MaxFood = legacyTile.Seasons[s].MaxPotentialFood = 77; legacyTile.Seasons[s].FoodRecoveryPerDay = legacyTile.Seasons[s].BiomassRecoveryPerDay = .000123f; }
        legacyTile.Food = legacyTile.PotentialFood = 1;
        copy = SaveCodec.Load(SaveCodec.Save(generated));
        Check(copy.Generation.GeneratorVersion == 7 && SaveCodec.Fingerprint(copy) == SaveCodec.Fingerprint(generated) && copy.Tiles[legacyTile.Id].Seasons[0].FoodRecoveryPerDay == .000123f, "generator-7 saves keep their exact stored fishery profiles and metadata");
        generated.Generation.GeneratorVersion = 9; bool rejected = false;
        try { SaveCodec.Load(SaveCodec.Save(generated)); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "future generator versions remain explicitly unsupported");
        Console.WriteLine("PASS: " + checks + " aquatic productivity assertions");
        Console.WriteLine("FISHERY matched equatorial tiles: cool(4C)/warm(28C)/frozen(-15C) foodCapacity=" + c.MaxFood.ToString("F2") + "/" + h.MaxFood.ToString("F2") + "/" + f.MaxFood.ToString("F2") + " dailyRenewal=" + c.FoodRecoveryPerDay.ToString("F6") + "/" + h.FoodRecoveryPerDay.ToString("F6") + "/" + f.FoodRecoveryPerDay.ToString("F6") + " sustainedHarvestRatio=" + (coolHarvest / warmHarvest).ToString("F2"));
    }
}
