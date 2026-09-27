using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class EcologyChecks
{
    private static int count;
    private static void Check(bool value, string message) { count++; if (!value) throw new Exception("Ecology: " + message); }
    private static bool Near(double a, double b, double tolerance = 1e-7) => Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static WorldState Fixture()
    {
        var w = new WorldState { Config = SimulationConfig.Small(1), Tiles = new Tile[1] };
        var t = new Tile { Id = 0, Terrain = Terrain.Woodland, Food = 60, PotentialFood = 400, Freshwater = 50, FreshwaterSource = FreshwaterSource.River };
        for (int i = 0; i < 4; i++) t.Seasons[i] = new SeasonData { MaxFood = 60 + i * 20, MaxPotentialFood = 600 + i * 200, MaxFreshwater = 70 + i * 10, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .006f };
        w.Tiles[0] = t; return w;
    }
    public static int Run()
    {
        count = 0;
        var weights = LazyEcology.SeasonWeights(0, 7300, 91.25);
        Check(weights.SequenceEqual(new double[] { 20, 20, 20, 20 }) && weights.Sum() == 80 && weights.SequenceEqual(TurnResolver.ReferenceSeasonWeights(0, 7300, 91.25)), "twenty-year exposure remains exactly twenty of each season");
        double k = EcologyMath.DailyRate(.35, 91.25);
        Check(Near(EcologyMath.Stock(0, 100, k, 0, 91.25).Stock, 35), "legacy fractional freshwater rate converts exactly once");
        var water = EcologyMath.Stock(40, 100, .01, 3, 365);
        Check(water.Stock == 0 && water.Unmet > 0 && Near(40 + water.Recharge, water.Realized), "finite water depletes analytically and continues realizing recharge");
        var noRecovery = EcologyMath.Stock(40, 100, 0, 3, 365);
        Check(noRecovery.Realized == 40 && noRecovery.Stock == 0 && noRecovery.Unmet == 1055, "zero recovery never invents food or water");
        foreach (double withdrawal in new[] { .1, 5.0 })
        {
            var longStep = EcologyMath.Stock(60, 100, .01, withdrawal, 7300); double stock = 60, harvest = 0;
            for (int i = 0; i < 20; i++) { var step = EcologyMath.Stock(stock, 100, .01, withdrawal, 365); stock = step.Stock; harvest += step.Realized; }
            Check(Near(longStep.Stock, stock) && Near(longStep.Realized, harvest), "twenty-year stock flow equals twenty annual steps, including depletion");
            longStep = EcologyMath.Stock(60, 100, .01, withdrawal, 365); stock = 60; harvest = 0;
            for (int i = 0; i < 52; i++) { var step = EcologyMath.Stock(stock, 100, .01, withdrawal, 365.0 / 52); stock = step.Stock; harvest += step.Realized; }
            Check(Near(longStep.Stock, stock) && Near(longStep.Realized, harvest), "annual stock flow equals fifty-two equal weekly intervals");
        }
        double condition = .8;
        for (int i = 0; i < 200; i++) condition = EcologyMath.Condition(condition, 1, .003, .001, 1);
        Check(Near(condition, EcologyMath.Condition(.8, 1, .003, .001, 200)), "condition uses a duration-neutral bounded exponential");
        Check(EcologyMath.Condition(1, 1, .003, .2, 200) < .03 && EcologyMath.Condition(.02, 1, .003, 0, 200) > .4, "extreme overshoot can collapse while abandonment permits recovery");
        Check(SeasonalAffine.Identity.Repeat(12, 1000) == 12 && new SeasonalAffine(1, 2, 100).Repeat(12, 1000) == 100, "cycle composition handles A equals one and bounded ceilings");
        var w = Fixture(); var t = w.Tiles[0];
        var fast = LazyEcology.ProjectEcology(w, 0, 36500); var explicitSteps = LazyEcology.ProjectEcology(w, 0, 36500, false);
        Check(Near(fast.Food, explicitSteps.Food) && Near(fast.PotentialFood, explicitSteps.PotentialFood) && Near(fast.Freshwater, explicitSteps.Freshwater), "composed quiet cycles equal ordered seasonal segments");
        var quiet = Fixture(); var quietTile = quiet.Tiles[0];
        quietTile.Food = quietTile.PotentialFood = quietTile.Freshwater = 0;
        var quietCapacity = new EcologyCapacity(quiet, quietTile, new EcologyView(quietTile), 0);
        var quietFirst = LazyEcology.ProjectEcology(quiet, 0, 30.5);
        double expectedQuietFood = EcologyMath.Stock(0, quietCapacity.Food, quietCapacity.FoodRate, 0, 30.5).Stock;
        double expectedQuietNonfood = EcologyMath.Stock(0, quietCapacity.Biomass - quietCapacity.Food,
            quietCapacity.BiomassRate, 0, 30.5).Stock;
        double expectedQuietWater = EcologyMath.Stock(0, quietCapacity.Water,
            quietCapacity.WaterRate, 0, 30.5).Stock;
        Check(Near(quietFirst.Food, expectedQuietFood)
            && Near(quietFirst.PotentialFood, expectedQuietFood + expectedQuietNonfood)
            && Near(quietFirst.Freshwater, expectedQuietWater)
            && quietFirst.Food < quietCapacity.Food
            && quietFirst.PotentialFood < quietCapacity.Biomass
            && quietFirst.Freshwater < quietCapacity.Water
            && quietFirst.LandCondition == quietTile.LandCondition,
            "quiet fractional-year projection follows the recovery equation for G, B and water without a full-stock reset");
        LazyEcology.MaterializeEcology(quiet, 0, 30.5);
        var quietSecond = LazyEcology.ProjectEcology(quiet, 0, 61);
        Check(quietSecond.Food > quietFirst.Food && quietSecond.PotentialFood > quietFirst.PotentialFood
            && quietSecond.Freshwater > quietFirst.Freshwater
            && quietSecond.Food <= quietCapacity.Food + 1e-8
            && quietSecond.PotentialFood <= quietCapacity.Biomass + 1e-8
            && quietSecond.Freshwater <= quietCapacity.Water + 1e-8,
            "consecutive fractional quiet periods recover monotonically within their capacities");
        string before = SaveCodec.Fingerprint(w); var observed = LazyEcology.ProjectEcology(w, 0, 7300); LazyEcology.ProjectEcology(w, 0, 90); LazyEcology.ProjectEcology(w, 0, 36500);
        Check(SaveCodec.Fingerprint(w) == before && t.LastEcologyUpdateDays == 0, "projection order never mutates stocks, vegetation or fingerprints");
        var materialized = LazyEcology.MaterializeEcology(w, 0, 7300);
        Check(Near(observed.Food, materialized.Food) && t.LastEcologyUpdateDays == 7300, "materialization commits exactly the pure projection");
        w = Fixture(); t = w.Tiles[0]; t.LandCondition = .2; t.NaturalVegetationPotential = VegetationTier.High; t.CurrentVegetation = VegetationTier.Low;
        fast = LazyEcology.ProjectEcology(w, 0, 36500);
        for (int i = 1; i <= 100; i++) LazyEcology.MaterializeEcology(w, 0, i * 365);
        Check(Near(fast.LandCondition, .2) && Near(t.LandCondition, .2) && Near(fast.Food, t.Food)
            && fast.CurrentVegetation == t.CurrentVegetation,
            "quiet projection retains stored land condition while food recovery agrees with annual materialization");
        w = Fixture(); w.Config.LandSafeUseRatio = 0;
        var claims = new[] { new Demand { Tile = 0, Entity = 3, Kind = "Gathering", Food = 5000 }, new Demand { Tile = 0, Entity = 2, Kind = "Gathering", Food = 2000 }, new Demand { Tile = 0, Entity = 4, Kind = "Herd", PotentialFood = 20000, Freshwater = 10000 } };
        var whole = EcologicalPressure.Integrate(w, 0, claims, 0, 7300);
        var reversed = EcologicalPressure.Integrate(w, 0, claims.Reverse(), 0, 7300);
        Check(whole.Transfers.Select(x => x.Food).SequenceEqual(reversed.Transfers.Select(x => x.Food)), "claim order cannot change proportional harvest");
        double cumulativeFood = 0, cumulativeHerd = 0;
        for (int i = 0; i < 20; i++)
        {
            var step = EcologicalPressure.Integrate(w, 0, claims.Select(d => new Demand { Tile = 0, Entity = d.Entity, Kind = d.Kind, Food = d.Food / 20, PotentialFood = d.PotentialFood / 20, Freshwater = d.Freshwater / 20 }), i * 365, 365);
            Check(Near(step.Balance.StartingBiomass + step.Balance.BiomassRecharge,
                step.Balance.EndingBiomass + step.Balance.BiomassHarvest + step.Balance.BiomassOverflow),
                "annual calibration step conserves biomass");
            step.View.Commit(w.Tiles[0]); cumulativeFood += step.Transfers.Sum(x => x.Food); cumulativeHerd += step.Transfers.Sum(x => x.PotentialFood);
        }
        Check(whole.Transfers.Sum(x => x.Food) <= claims.Sum(x => x.Food) + 1e-6
            && whole.Transfers.Sum(x => x.PotentialFood) <= claims.Sum(x => x.PotentialFood) + 1e-6
            && cumulativeFood <= claims.Sum(x => x.Food) + 1e-6
            && cumulativeHerd <= claims.Sum(x => x.PotentialFood) + 1e-6
            && whole.View.PotentialFood >= whole.View.Food && w.Tiles[0].PotentialFood >= w.Tiles[0].Food,
            "turn-horizon and annual calibration both respect finite claims and nested pools");
        Console.WriteLine("ECOLOGY_HORIZON_CALIBRATION foodDelta=" + (whole.Transfers.Sum(x => x.Food) - cumulativeFood).ToString("R")
            + " herdDelta=" + (whole.Transfers.Sum(x => x.PotentialFood) - cumulativeHerd).ToString("R")
            + " endingFoodDelta=" + (whole.View.Food - w.Tiles[0].Food).ToString("R")
            + " conditionDelta=" + (whole.View.LandCondition - w.Tiles[0].LandCondition).ToString("R"));
        var b = whole.Balance;
        Check(Near(b.StartingBiomass + b.BiomassRecharge, b.EndingBiomass + b.BiomassHarvest + b.BiomassOverflow) && Near(b.StartingFood + b.FoodRecharge, b.EndingFood + b.FoodHarvest + b.FoodOverflow), "both nested food pools conserve stock plus renewal and account for seasonal overflow");
        Check(Near(b.StartingWater + b.WaterRecharge, b.EndingWater + b.WaterHarvest + b.WaterOverflow), "freshwater ledger conserves finite stock and recharge");
        Check(whole.Pressure.All(p => Near(p.Requested, p.Realized + p.Unmet) && p.ConditionDamage == 0),
            "land gathering, grazing and drinking record unmet demand without condition damage");
        w = Fixture(); w.Tiles[0].Food = w.Tiles[0].PotentialFood = 0;
        for (int i = 0; i < 4; i++) { w.Tiles[0].Seasons[i].FoodRecoveryPerDay = 0; w.Tiles[0].Seasons[i].BiomassRecoveryPerDay = 0; }
        var failed = EcologicalPressure.Integrate(w, 0, claims, 0, 7300);
        Check(failed.View.LandCondition == 1 && failed.Pressure.Where(p => p.Pool != "Freshwater").All(p => p.Realized == 0 && p.ConditionDamage == 0), "unmet extraction cannot damage an exhausted tile");
        var weekly = Fixture(); weekly.Config.LandSafeUseRatio = 1000;
        for (int i = 1; i < 4; i++) weekly.Tiles[0].Seasons[i] = weekly.Tiles[0].Seasons[0];
        var annualClaims = new[] { new Demand { Tile = 0, Entity = 1, Kind = "Gathering", Food = 100 }, new Demand { Tile = 0, Entity = 2, Kind = "Herd", PotentialFood = 2000, Freshwater = 1000 } };
        var annual = EcologicalPressure.Integrate(weekly, 0, annualClaims, 0, 365); double weeklyHarvest = 0;
        for (int i = 0; i < 52; i++)
        {
            var step = EcologicalPressure.Integrate(weekly, 0, annualClaims.Select(d => new Demand { Tile = 0, Entity = d.Entity, Kind = d.Kind, Food = d.Food / 52, PotentialFood = d.PotentialFood / 52, Freshwater = d.Freshwater / 52 }), i * 365.0 / 52, 365.0 / 52);
            step.View.Commit(weekly.Tiles[0]); weeklyHarvest += step.Transfers.Sum(ti => ti.Food + ti.PotentialFood);
        }
        Check(annual.Transfers.Sum(ti => ti.Food + ti.PotentialFood) <= 400 + 1e-8
            && weeklyHarvest >= annual.Transfers.Sum(ti => ti.Food + ti.PotentialFood)
            && Near(annual.View.Freshwater, weekly.Tiles[0].Freshwater),
            "shorter land turns may claim intervening recovery while freshwater keeps its horizon flow");
        var light = Fixture(); var extreme = Fixture();
        double lastOpeningBiomass = 0, lastExtremeHarvest = 0;
        for (int i = 0; i < 15; i++)
        {
            EcologicalPressure.Integrate(light, 0, new[] { new Demand { Tile = 0, Entity = 1, Kind = "Gathering", Food = 100 } }, i * 7300, 7300).View.Commit(light.Tiles[0]);
            var grazed = EcologicalPressure.Integrate(extreme, 0,
                new[] { new Demand { Tile = 0, Entity = 1, Kind = "Herd", PotentialFood = 1000000 } }, i * 7300, 7300);
            lastOpeningBiomass = grazed.Balance.StartingBiomass;
            lastExtremeHarvest = grazed.Transfers.Sum(x => x.PotentialFood);
            grazed.View.Commit(extreme.Tiles[0]);
        }
        Console.WriteLine("LAND_PRESSURE_PROBE light=" + light.Tiles[0].LandCondition.ToString("R") + " extreme=" + extreme.Tiles[0].LandCondition.ToString("R") + " biomass=" + extreme.Tiles[0].PotentialFood.ToString("R"));
        Check(light.Tiles[0].LandCondition == 1 && extreme.Tiles[0].LandCondition == 1
            && lastExtremeHarvest > 0 && lastExtremeHarvest <= lastOpeningBiomass + 1e-8
            && extreme.Tiles[0].PotentialFood >= 0,
            "sustained grazing debits opening biomass but never alters land condition");
        var depleted = Fixture(); depleted.Tiles[0].Food = depleted.Tiles[0].PotentialFood = 0;
        var recovering = LazyEcology.ProjectEcology(depleted, 0, 365);
        var capacity = new EcologyCapacity(depleted, depleted.Tiles[0], recovering, 0);
        Check(recovering.LandCondition == depleted.Tiles[0].LandCondition && recovering.PotentialFood > 0
            && recovering.PotentialFood <= capacity.Biomass + 1e-8,
            "unclaimed depleted ground recovers biomass within capacity without condition recovery");
        var gradual = Fixture(); gradual.Tiles[0].Food = gradual.Tiles[0].PotentialFood = gradual.Tiles[0].Freshwater = 0;
        gradual.Tiles[0].LandCondition = .37;
        var firstRecovery = EcologicalPressure.Integrate(gradual, 0, Array.Empty<Demand>(), 0, 30.5);
        var firstCapacity = new EcologyCapacity(gradual, gradual.Tiles[0], firstRecovery.View, 0);
        Check(firstRecovery.View.LandCondition == .37
            && firstRecovery.View.Food > 0 && firstRecovery.View.Food <= firstRecovery.View.PotentialFood
            && firstRecovery.View.PotentialFood < firstCapacity.Biomass
            && firstRecovery.View.Freshwater > 0 && firstRecovery.View.Freshwater < firstCapacity.Water,
            "partial-year recovery raises finite land food and water without resetting either pool to capacity");
        firstRecovery.View.Commit(gradual.Tiles[0]);
        var secondRecovery = EcologicalPressure.Integrate(gradual, 0, Array.Empty<Demand>(), 30.5, 30.5);
        Check(secondRecovery.View.LandCondition == .37
            && secondRecovery.View.Food > firstRecovery.View.Food
            && secondRecovery.View.PotentialFood > firstRecovery.View.PotentialFood
            && secondRecovery.View.Freshwater > firstRecovery.View.Freshwater,
            "successive unclaimed partial-year turns continue gradual stock recovery at fixed land condition");
        var late = new WorldGenerator().Generate(31, SimulationConfig.Small(1)); late.Days = 100 * late.Config.DaysPerYear;
        string latent = SaveCodec.Fingerprint(late); var saved = SaveCodec.Save(late); var restored = SaveCodec.Load(saved);
        Check(SaveCodec.Fingerprint(late) == latent && SaveCodec.Fingerprint(restored) == latent && restored.Tiles.All(ti => ti.LastEcologyUpdateDays == 0), "save/load preserves dormant timestamps without materializing the world");
        Console.WriteLine("PASS: " + count + " ecology assertions"); return count;
    }
}
