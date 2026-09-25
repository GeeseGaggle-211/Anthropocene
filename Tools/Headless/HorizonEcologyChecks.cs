using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class HorizonEcologyChecks
{
    private static int count;
    private static void Check(bool condition, string name)
    {
        count++;
        if (!condition) throw new Exception("Horizon ecology: " + name);
    }
    private static bool Near(double a, double b, double tolerance = 1e-7)
        => Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    private static WorldState Fixture(bool aquatic = false)
    {
        var config = SimulationConfig.Small(1);
        var tile = new Tile
        {
            Id = 0,
            Environment = aquatic ? EnvironmentKind.Shallows : EnvironmentKind.Grassland,
            NaturalVegetationPotential = aquatic ? VegetationTier.High : VegetationTier.Medium,
            CurrentVegetation = aquatic ? VegetationTier.High : VegetationTier.Medium,
            FreshwaterSource = aquatic ? FreshwaterSource.None : FreshwaterSource.River,
            Food = 20, PotentialFood = 100, Freshwater = aquatic ? 0 : 40
        };
        for (int season = 0; season < 4; season++)
            tile.Seasons[season] = new SeasonData
            {
                MaxFood = aquatic ? 100 : 70 + 10 * season,
                MaxPotentialFood = aquatic ? 400 : 350 + 50 * season,
                MaxFreshwater = aquatic ? 0 : 80 + 10 * season,
                FoodRecoveryPerDay = .01f,
                BiomassRecoveryPerDay = .01f,
                WaterRecoveryPerDay = .01f
            };
        return new WorldState { Config = config, Tiles = new[] { tile } };
    }
    private static void Conserves(TileEcologyResult result, string context)
    {
        var b = result.Balance;
        Check(Near(b.StartingBiomass + b.BiomassRecharge,
            b.EndingBiomass + b.BiomassHarvest + b.BiomassOverflow), context + " biomass ledger");
        Check(Near(b.StartingFood + b.FoodRecharge,
            b.EndingFood + b.FoodHarvest + b.FoodOverflow), context + " food ledger");
        Check(Near(b.StartingWater + b.WaterRecharge,
            b.EndingWater + b.WaterHarvest + b.WaterOverflow), context + " water ledger");
        Check(result.View.Food >= 0 && result.View.PotentialFood >= result.View.Food - 1e-8
            && result.View.Freshwater >= 0, context + " bounded endpoint");
        Check(result.Transfers.All(t => t.Food >= 0 && t.PotentialFood >= 0 && t.Freshwater >= 0)
            && result.Pressure.All(p => p.Realized >= 0 && p.Unmet >= -1e-8),
            context + " bounded receipts");
    }

    private static void ContestedFoodDebitsOneTileSequentially()
    {
        void Verify(double firstRequest, double secondRequest, string context)
        {
            var first = new Demand { Tile = 0, Entity = 11, Kind = "Gathering", Food = firstRequest };
            var second = new Demand { Tile = 0, Entity = 22, Kind = "Gathering", Food = secondRequest };
            var forward = EcologicalPressure.Integrate(Fixture(), 0, new[] { first, second }, 0, 365);
            var reversed = EcologicalPressure.Integrate(Fixture(), 0, new[] { second, first }, 0, 365);
            Conserves(forward, context);
            double available = forward.Transfers.Sum(transfer => transfer.Food);
            double expectedFirst = Math.Min(firstRequest, available);
            double expectedSecond = Math.Min(secondRequest, Math.Max(0, available - expectedFirst));
            Check(available <= firstRequest + secondRequest + 1e-8,
                context + " receipts cannot exceed requested food");
            Check(Near(forward.Transfers.Single(transfer => transfer.Entity == first.Entity).Food, expectedFirst)
                && Near(forward.Transfers.Single(transfer => transfer.Entity == second.Entity).Food, expectedSecond),
                context + " first claimant debits finite food before the second");
            Check(forward.Transfers.Select(transfer => transfer.Entity)
                    .SequenceEqual(reversed.Transfers.Select(transfer => transfer.Entity))
                && forward.Transfers.Zip(reversed.Transfers,
                    (a, b) => Near(a.Food, b.Food)).All(equal => equal)
                && Near(forward.View.Food, reversed.View.Food)
                && Near(forward.View.PotentialFood, reversed.View.PotentialFood),
                context + " receipt and endpoint are independent of input enumeration order");
        }

        Verify(10, 10000, "partial first claimant");
        Verify(10000, 10, "exhausting first claimant");
    }

    private static void ContestedBiomassAndWaterDebitOneTileSequentially()
    {
        void Verify(double firstRequest, double secondRequest, string context)
        {
            var first = new Demand { Tile = 0, Entity = 11, Kind = "Herd",
                PotentialFood = firstRequest, Freshwater = firstRequest };
            var second = new Demand { Tile = 0, Entity = 22, Kind = "Herd",
                PotentialFood = secondRequest, Freshwater = secondRequest };
            var forward = EcologicalPressure.Integrate(Fixture(), 0, new[] { first, second }, 0, 365);
            var reversed = EcologicalPressure.Integrate(Fixture(), 0, new[] { second, first }, 0, 365);
            Conserves(forward, context);
            double biomass = forward.Transfers.Sum(transfer => transfer.PotentialFood);
            double water = forward.Transfers.Sum(transfer => transfer.Freshwater);
            var firstReceipt = forward.Transfers.Single(transfer => transfer.Entity == first.Entity);
            var secondReceipt = forward.Transfers.Single(transfer => transfer.Entity == second.Entity);
            Check(Near(firstReceipt.PotentialFood, Math.Min(firstRequest, biomass))
                && Near(secondReceipt.PotentialFood,
                    Math.Min(secondRequest, Math.Max(0, biomass - firstReceipt.PotentialFood))),
                context + " finite biomass is debited before the second claimant");
            Check(Near(firstReceipt.Freshwater, Math.Min(firstRequest, water))
                && Near(secondReceipt.Freshwater,
                    Math.Min(secondRequest, Math.Max(0, water - firstReceipt.Freshwater))),
                context + " finite freshwater is debited before the second claimant");
            Check(forward.Transfers.Select(transfer => transfer.Entity)
                    .SequenceEqual(reversed.Transfers.Select(transfer => transfer.Entity))
                && forward.Transfers.Zip(reversed.Transfers, (a, b) =>
                    Near(a.PotentialFood, b.PotentialFood) && Near(a.Freshwater, b.Freshwater)).All(equal => equal)
                && Near(forward.View.PotentialFood, reversed.View.PotentialFood)
                && Near(forward.View.Freshwater, reversed.View.Freshwater),
                context + " biomass and water receipts are independent of input enumeration order");
        }

        Verify(10, 10000, "partial first herd claimant");
        Verify(10000, 10, "exhausting first herd claimant");
    }

    public static int Run()
    {
        count = 0;
        var quarter = LazyEcology.SeasonWeights(90, 10, 91.25);
        Check(Near(quarter[0] * 91.25, 1.25) && Near(quarter[1] * 91.25, 8.75)
            && Near(quarter[2], 0) && Near(quarter[3], 0), "fractional boundary exposure");
        var twenty = LazyEcology.SeasonWeights(0, 7300, 91.25);
        Check(twenty.SequenceEqual(new[] { 20.0, 20.0, 20.0, 20.0 }),
            "four profiles cover twenty years in constant work");

        foreach (double years in new[] { 0.0, .5 / 365, 1.0, 5.0, 10.0, 20.0 })
        {
            double days = years * 365;
            var world = Fixture();
            var demands = days == 0 ? Array.Empty<Demand>() : new[]
            {
                new Demand { Tile = 0, Entity = 1, Kind = "Gathering", Food = .12 * days },
                new Demand { Tile = 0, Entity = 2, Kind = "Herd", PotentialFood = .2 * days,
                    Freshwater = .06 * days }
            };
            var result = EcologicalPressure.Integrate(world, 0, demands, 0, days);
            Conserves(result, years + " year horizon");
            Check(Near(result.View.Days, days), years + " year endpoint time");
            Check(Near(world.Tiles[0].LastEcologyUpdateDays, 0),
                years + " year integration remains pure until commit");
        }

        var exhausted = Fixture();
        exhausted.Tiles[0].Food = exhausted.Tiles[0].PotentialFood = exhausted.Tiles[0].Freshwater = 0;
        for (int season = 0; season < 4; season++)
        {
            exhausted.Tiles[0].Seasons[season].FoodRecoveryPerDay = 0;
            exhausted.Tiles[0].Seasons[season].BiomassRecoveryPerDay = 0;
            exhausted.Tiles[0].Seasons[season].WaterRecoveryPerDay = 0;
        }
        var dry = EcologicalPressure.Integrate(exhausted, 0, new[]
        {
            new Demand { Tile = 0, Entity = 1, Kind = "Gathering", Food = 1000,
                PotentialFood = 1000, Freshwater = 1000 }
        }, 0, 7300);
        Conserves(dry, "zero-stock horizon");
        Check(dry.Transfers.All(t => Near(t.Food + t.PotentialFood + t.Freshwater, 0)),
            "zero stock and renewal cannot create harvest");

        var fishery = Fixture(true);
        fishery.Tiles[0].AquaticCondition = .25;
        fishery.Tiles[0].CurrentVegetation = VegetationTier.Low;
        fishery.Tiles[0].Food = fishery.Tiles[0].PotentialFood = 0;
        var recovered = LazyEcology.ProjectEcology(fishery, 0, 7300);
        Check(recovered.AquaticCondition > .99 && recovered.CurrentVegetation == VegetationTier.High
            && recovered.Food > 99, "passive stock responds to late-horizon plankton recovery");
        Check(fishery.Tiles[0].Food == 0 && fishery.Tiles[0].AquaticCondition == .25,
            "passive response remains a pure projection");

        ContestedFoodDebitsOneTileSequentially();
        ContestedBiomassAndWaterDebitOneTileSequentially();

        Console.WriteLine("PASS: " + count + " horizon ecology assertions");
        return count;
    }
}
