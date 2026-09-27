using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class Issue27EcologyChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Issue 27: " + message);
    }
    private static bool Near(double a, double b, double tolerance = 1e-8)
        => Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    private static WorldState Witness()
    {
        var config = SimulationConfig.Small(1);
        var tile = new Tile
        {
            Id = 0, Terrain = Terrain.Grassland, Environment = EnvironmentKind.Grassland,
            NaturalVegetationPotential = VegetationTier.Medium,
            CurrentVegetation = VegetationTier.Medium,
            LandCondition = .75, Food = 5, PotentialFood = 5
        };
        for (int season = 0; season < 4; season++)
            tile.Seasons[season] = new SeasonData
            {
                MaxFood = season == 1 ? 10 : 100,
                MaxPotentialFood = 200,
                FoodRecoveryPerDay = .01f,
                BiomassRecoveryPerDay = .01f
            };
        return new WorldState { Config = config, Tiles = new[] { tile } };
    }

    private static WorldState Case(double q, double condition, double operation,
        bool seasonal, double fill, float rate, double start)
    {
        var world = Witness();
        var tile = world.Tiles[0];
        tile.LandCondition = condition;
        tile.CultivationCapital = Math.Pow(q / world.Config.ReferenceIntensity, 2);
        tile.Cultivation = (float)q;
        tile.CultivationOperatingFraction = operation;
        tile.LastEcologyUpdateDays = start;
        tile.FreshwaterSource = FreshwaterSource.River;
        for (int season = 0; season < 4; season++)
        {
            var profile = tile.Seasons[season];
            profile.MaxFood = seasonal && season == 1 ? 10 : 100;
            profile.MaxPotentialFood = seasonal && season == 1 ? 40 : 200;
            profile.MaxFreshwater = seasonal && season == 1 ? 0 : 80;
            profile.FoodRecoveryPerDay = rate;
            profile.BiomassRecoveryPerDay = rate;
            profile.WaterRecoveryPerDay = rate;
            tile.Seasons[season] = profile;
        }
        var initial = new EcologyCapacity(world, tile, new EcologyView(tile),
            (int)Math.Floor(start / world.Config.BaseDays) % 4);
        tile.PotentialFood = initial.Biomass * fill;
        tile.Food = Math.Min(tile.PotentialFood, initial.Food * fill);
        tile.Freshwater = initial.Water * fill;
        world.Days = start;
        return world;
    }

    private static void Matrix()
    {
        foreach (double q in new[] { 0.0, .25, .6, 1.0 })
        foreach (double condition in new[] { 1.0, .75, .5 })
        foreach (double operation in new[] { 0.0, .5, 1.0 })
        foreach (bool seasonal in new[] { false, true })
        foreach (double fill in new[] { 0.0, .5, 1.0 })
        foreach (float rate in new[] { 0f, .01f })
        foreach (double start in new[] { 0.0, 2.5 * 91.25 })
        {
            var world = Case(q, condition, operation, seasonal, fill, rate, start);
            var tile = world.Tiles[0];
            double horizon = 20 * 365;
            string before = SaveCodec.Fingerprint(world);
            var active = EcologicalPressure.Integrate(world, 0, Array.Empty<Demand>(), start, horizon);
            var passive = LazyEcology.ProjectEcology(world, 0, start + horizon);
            Check(before == SaveCodec.Fingerprint(world), "queries are pure");
            Check(Near(active.View.Food, passive.Food) && Near(active.View.PotentialFood, passive.PotentialFood)
                && Near(active.View.Freshwater, passive.Freshwater)
                && Near(active.View.CultivationCapital, passive.CultivationCapital)
                && active.View.CurrentVegetation == passive.CurrentVegetation,
                "active/passive endpoint q=" + q + " condition=" + condition + " operation=" + operation
                + " seasonal=" + seasonal + " fill=" + fill + " rate=" + rate + " start=" + start
                + " active=" + active.View.Food + "/" + active.View.PotentialFood + "/" + active.View.Freshwater
                + " passive=" + passive.Food + "/" + passive.PotentialFood + "/" + passive.Freshwater);
            var endpoint = new EcologyCapacity(world, tile, passive,
                (int)Math.Floor(passive.Days / world.Config.BaseDays) % 4);
            Check(passive.Food >= 0 && passive.Food <= passive.PotentialFood + 1e-7
                && passive.PotentialFood <= endpoint.Biomass + 1e-7
                && passive.Freshwater >= 0 && passive.Freshwater <= endpoint.Water + 1e-7
                && passive.LandCondition == condition, "bounded, frozen-condition endpoint");
            var balance = active.Balance;
            Check(Near(balance.StartingFood + balance.FoodRecharge,
                    balance.EndingFood + balance.FoodHarvest + balance.FoodOverflow)
                && Near(balance.StartingBiomass + balance.BiomassRecharge,
                    balance.EndingBiomass + balance.BiomassHarvest + balance.BiomassOverflow)
                && Near(balance.StartingWater + balance.WaterRecharge,
                    balance.EndingWater + balance.WaterHarvest + balance.WaterOverflow),
                "zero-claim stocks conserve");
            var materialized = LazyEcology.MaterializeEcology(world, 0, start + horizon);
            Check(Near(materialized.Food, passive.Food)
                && Near(materialized.PotentialFood, passive.PotentialFood)
                && Near(materialized.Freshwater, passive.Freshwater)
                && Near(materialized.CultivationCapital, passive.CultivationCapital)
                && tile.LandCondition == condition, "materialize commits the projection");
        }
    }

    private static void FullCultivationWithNonfoodStock()
    {
        foreach (double start in new[] { 0.0, 2.5 * 91.25 })
        foreach (double duration in new[] { 0.0, 20 * 365.0 })
        {
            var world = Case(1, .75, 1, false, .5, 0, start);
            world.Config.CultivationRegenFlux = 0;
            var tile = world.Tiles[0];
            tile.PotentialFood = 10;
            tile.Food = 5;
            double openingFood = tile.Food, openingBiomass = tile.PotentialFood;
            string before = SaveCodec.Fingerprint(world);
            var active = EcologicalPressure.Integrate(world, 0, Array.Empty<Demand>(), start, duration);
            var passive = LazyEcology.ProjectEcology(world, 0, start + duration);
            Check(before == SaveCodec.Fingerprint(world), "full-field queries remain pure");
            Check(Near(active.Balance.StartingFood, openingFood)
                && Near(active.View.Food, openingFood)
                && Near(active.View.Food, passive.Food) && Near(active.View.PotentialFood, passive.PotentialFood),
                "full cultivation does not create accessible Food before claims or recovery"
                + " start=" + start + " duration=" + duration
                + " opening=" + openingFood + "/" + openingBiomass
                + " active=" + active.View.Food + "/" + active.View.PotentialFood
                + " passive=" + passive.Food + "/" + passive.PotentialFood);
            Check(Near(active.Balance.StartingFood + active.Balance.FoodRecharge,
                    active.Balance.EndingFood + active.Balance.FoodHarvest + active.Balance.FoodOverflow)
                && Near(active.Balance.StartingBiomass + active.Balance.BiomassRecharge,
                    active.Balance.EndingBiomass + active.Balance.BiomassHarvest + active.Balance.BiomassOverflow),
                "full-field zero-claim stocks conserve");
            if (duration > 0)
            {
                var claimed = EcologicalPressure.Integrate(world, 0,
                    new[] { new Demand { Tile = 0, Entity = 1, Kind = "Gathering", Food = 10 } },
                    start, duration);
                Check(Near(claimed.Transfers.Single().Food, openingFood)
                    && Near(claimed.Balance.StartingFood, openingFood),
                    "full-field receipt cannot spend inaccessible biomass");
            }
            var materialized = LazyEcology.MaterializeEcology(world, 0, start + duration);
            Check(Near(materialized.Food, openingFood)
                && Near(materialized.Food, passive.Food)
                && Near(materialized.PotentialFood, passive.PotentialFood)
                && materialized.PotentialFood <= openingBiomass,
                "full-field materialization commits the pure endpoint");
        }

        var saved = NeolithicChecks.Fixture();
        int tileId = saved.Camps[0].Tile;
        var field = saved.Tiles[tileId];
        field.CultivationCapital = Math.Pow(1 / saved.Config.ReferenceIntensity, 2);
        field.Cultivation = 1;
        field.CultivationOperatingFraction = 1;
        field.Food = 5;
        field.PotentialFood = 10;
        var loaded = SaveCodec.Load(SaveCodec.Save(saved));
        Check(Near(loaded.Tiles[tileId].Food, 5) && Near(loaded.Tiles[tileId].PotentialFood, 10),
            "save load retains finite Food below full-field biomass");
        var loadedActive = EcologicalPressure.Integrate(loaded, tileId,
            Array.Empty<Demand>(), loaded.Days, 20 * 365.0);
        var loadedPassive = LazyEcology.ProjectEcology(loaded, tileId, loaded.Days + 20 * 365.0);
        Check(Near(loadedActive.Balance.StartingFood, 5)
            && Near(loadedActive.View.Food, loadedPassive.Food)
            && Near(loadedActive.View.PotentialFood, loadedPassive.PotentialFood),
            "save-loaded full-field recovery shares the passive endpoint");
    }

    private static void TouchAndSave()
    {
        var dormant = Case(0, .75, 0, true, .5, .01f, 0);
        var touched = Case(0, .75, 0, true, .5, .01f, 0);
        LazyEcology.MaterializeEcology(touched, 0, 365);
        dormant.Days = touched.Days = 365;
        Check(Near(LazyEcology.ProjectEcology(dormant, 0, 365).Food, touched.Tiles[0].Food),
            "dormant and touched tiles have the same physical state at the touch boundary");
        var latent = LazyEcology.ProjectEcology(dormant, 0, 7300);
        var previouslyTouched = LazyEcology.ProjectEcology(touched, 0, 7300);
        Check(Near(latent.Food, previouslyTouched.Food)
            && Near(latent.PotentialFood, previouslyTouched.PotentialFood)
            && Near(latent.Freshwater, previouslyTouched.Freshwater),
            "touch history does not select a different recovery law");

        var saved = NeolithicChecks.Fixture();
        int tileId = saved.Camps[0].Tile;
        var tile = saved.Tiles[tileId];
        tile.LandCondition = .75;
        tile.CultivationCapital = Math.Pow(.6 / saved.Config.ReferenceIntensity, 2);
        tile.Cultivation = .6f;
        tile.Food = 10; tile.PotentialFood = 100; tile.Freshwater = 20;
        string original = SaveCodec.Fingerprint(saved);
        var loaded = SaveCodec.Load(SaveCodec.Save(saved));
        Check(SaveCodec.Fingerprint(loaded) == original, "save roundtrip preserves latent ecology");
        var direct = LazyEcology.ProjectEcology(saved, tileId, 7300);
        var restored = LazyEcology.ProjectEcology(loaded, tileId, 7300);
        Check(Near(direct.Food, restored.Food)
            && Near(direct.PotentialFood, restored.PotentialFood)
            && Near(direct.Freshwater, restored.Freshwater)
            && Near(direct.CultivationCapital, restored.CultivationCapital),
            "saved dormant tile continues with identical recovery");
        LazyEcology.ProjectEcology(saved, tileId, 365);
        LazyEcology.ProjectEcology(saved, tileId, 7300);
        Check(SaveCodec.Fingerprint(saved) == original, "observation cannot change later receipts");
        var directClaim = EcologicalPressure.Integrate(saved, tileId,
            new[] { new Demand { Tile = tileId, Entity = saved.Camps[0].Id, Kind = "Gathering", Food = 5 } },
            7300, 365);
        var restoredClaim = EcologicalPressure.Integrate(loaded, tileId,
            new[] { new Demand { Tile = tileId, Entity = loaded.Camps[0].Id, Kind = "Gathering", Food = 5 } },
            7300, 365);
        Check(Near(directClaim.Transfers.Single().Food, restoredClaim.Transfers.Single().Food),
            "save continuation and repeated observation leave the next receipt unchanged");
    }

    private static void WoodContinuation()
    {
        var world = NeolithicChecks.Fixture();
        int tileId = world.Camps[0].Tile, count = world.Tiles.Length;
        var tile = world.Tiles[tileId];
        tile.LandCondition = .75;
        tile.NaturalVegetationPotential = tile.CurrentVegetation = VegetationTier.Medium;
        tile.CultivationCapital = Math.Pow(.6 / world.Config.ReferenceIntensity, 2);
        tile.Cultivation = .6f;
        world.Resources = new WorldResourceState
        {
            MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash,
            ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash,
            Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count],
            History = new ushort[count], Host = new byte[count], Marine = new byte[count],
            Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count],
            WoodRecoveryPerYear = .05
        };
        world.Resources.Wood[tileId] = 100;
        var state = new MaterialSourceState
        {
            Id = "surface:" + tileId + ":" + (int)MaterialId.Wood,
            Material = MaterialId.Wood, Initial = 250, Remaining = 10, LastDays = 0
        };
        world.MaterialSources.Add(state);
        string before = SaveCodec.Fingerprint(world);
        var wood = MaterialEconomy.ProjectWoodSource(world, tileId);
        var pure = LazyEcology.ProjectEcology(world, tileId, 7300, true, wood.Recover);
        var active = EcologicalPressure.Integrate(world, tileId, Array.Empty<Demand>(), 0, 7300);
        Check(before == SaveCodec.Fingerprint(world) && state.Remaining == 10,
            "Wood companion and active result remain pure");
        Check(Near(pure.Food, active.View.Food)
            && Near(wood.Remaining, active.WoodRemaining),
            "Wood continuation uses the same land endpoints");
        LazyEcology.MaterializeEcology(world, tileId, 7300);
        Check(Near(state.Remaining, wood.Remaining) && Near(tile.Food, active.View.Food),
            "materialization commits the matched Wood and food projection");
    }

    private static void WorkAndPartition()
    {
        foreach (double q in new[] { 0.0, .6 })
        foreach (double start in new[] { 0.0, 2.5 * 91.25 })
        {
            int referenceCount = -1;
            foreach (int years in new[] { 20, 200, 2000 })
            {
                var world = Case(q, .75, .5, true, .5, .01f, start);
                var view = new EcologyView(world.Tiles[0]);
                int evaluations = 0;
                LazyEcology.RecoverLandAfterHarvest(world, world.Tiles[0], ref view,
                    start, years * 365.0, null, null, null, true, () => evaluations++);
                if (referenceCount < 0) referenceCount = evaluations;
                Check(evaluations == referenceCount && evaluations == (q == 0 ? 4 : 5),
                    "profile evaluations are bounded independently of years");
                Console.WriteLine("ISSUE27_WORK q=" + q + " start=" + start.ToString("R")
                    + " years=" + years + " profiles=" + evaluations);
            }
        }
        foreach (double q in new[] { 0.0, .6 })
        {
            var wholeWorld = Case(q, .75, 0, true, .5, .01f, 0);
            var annualWorld = Case(q, .75, 0, true, .5, .01f, 0);
            var seasonalWorld = Case(q, .75, 0, true, .5, .01f, 0);
            var whole = LazyEcology.ProjectEcology(wholeWorld, 0, 7300);
            for (int year = 1; year <= 20; year++)
                LazyEcology.MaterializeEcology(annualWorld, 0, year * 365.0);
            for (int season = 1; season <= 80; season++)
                LazyEcology.MaterializeEcology(seasonalWorld, 0, season * 91.25);
            double annualError = Math.Abs(whole.Food - annualWorld.Tiles[0].Food);
            double seasonalError = Math.Abs(whole.Food - seasonalWorld.Tiles[0].Food);
            Console.WriteLine("ISSUE27_PARTITION q=" + q + " whole=" + whole.Food.ToString("R")
                + " annual=" + annualWorld.Tiles[0].Food.ToString("R")
                + " seasonal=" + seasonalWorld.Tiles[0].Food.ToString("R")
                + " annualAbsError=" + annualError.ToString("R")
                + " seasonalAbsError=" + seasonalError.ToString("R"));
            Check(double.IsFinite(annualError) && double.IsFinite(seasonalError),
                "partition comparisons remain finite");
            if (q == 0)
                Check(Near(whole.Food, annualWorld.Tiles[0].Food)
                    && Near(whole.Food, seasonalWorld.Tiles[0].Food),
                    "fixed-forcing seasonal reference composes exactly");
        }
    }

    public static int Run()
    {
        checks = 0;
        var world = Witness();
        double horizon = 20 * 365;
        string before = SaveCodec.Fingerprint(world);
        var active = EcologicalPressure.Integrate(world, 0, Array.Empty<Demand>(), 0, horizon).View;
        var passive = LazyEcology.ProjectEcology(world, 0, horizon);
        if (before != SaveCodec.Fingerprint(world))
            throw new Exception("Issue 27: active integration or pure projection mutated stored state.");
        var materialized = LazyEcology.MaterializeEcology(world, 0, horizon);
        Console.WriteLine("ISSUE27_WITNESS activeFood=" + active.Food.ToString("R")
            + " passiveFood=" + passive.Food.ToString("R")
            + " materializedFood=" + materialized.Food.ToString("R"));
        Check(Near(active.Food, passive.Food), "zero-claim active and passive land recovery agree");
        Check(Near(passive.Food, materialized.Food) && Near(world.Tiles[0].Food, passive.Food)
            && before != SaveCodec.Fingerprint(world), "materialization commits the pure projection");
        Matrix();
        FullCultivationWithNonfoodStock();
        TouchAndSave();
        WoodContinuation();
        WorkAndPartition();
        Console.WriteLine("PASS: " + checks + " issue #27 ecology assertions");
        return checks;
    }
}
