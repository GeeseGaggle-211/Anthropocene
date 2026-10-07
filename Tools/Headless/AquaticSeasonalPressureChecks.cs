using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class AquaticSeasonalPressureChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Aquatic seasonal pressure: " + message);
    }
    private static bool Near(double actual, double expected, double tolerance = 1e-8)
        => Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(expected)));

    private static WorldState ColdWater(bool corrected)
    {
        var config = SimulationConfig.Small(1);
        config.CorrectedAquaticSeasonalPressureEnabled = corrected;
        config.AquaticUseDamagePerYear = .03;
        config.SafeUseRatio = 1;
        config.SustainableProductionFraction = .5;
        config.PressureExponent = 2;
        var tile = new Tile
        {
            Id = 0,
            Environment = EnvironmentKind.Shallows,
            AquaticCondition = 1,
            Food = 1.25,
            PotentialFood = 5,
            FreshwaterSource = FreshwaterSource.None
        };
        // These deliberately separated cold-season profiles make the old
        // horizon-wide rate exceed the finite ceiling of the coldest season.
        double[] biomass = { 5, 1000, 200, 100 };
        double[] renewal = { .000001, .01, .004, .002 };
        for (int season = 0; season < 4; season++)
        {
            tile.Seasons[season] = new SeasonData
            {
                MaxPotentialFood = (float)biomass[season],
                MaxFood = (float)(biomass[season] * .25),
                BiomassRecoveryPerDay = (float)renewal[season],
                FoodRecoveryPerDay = (float)renewal[season]
            };
        }
        return new WorldState { Config = config, Tiles = new[] { tile } };
    }

    private static WorldState WarmWater(bool corrected)
    {
        var world = ColdWater(corrected);
        var tile = world.Tiles[0];
        tile.Food = 25;
        tile.PotentialFood = 100;
        for (int season = 0; season < 4; season++)
        {
            tile.Seasons[season] = new SeasonData
            {
                MaxPotentialFood = 100,
                MaxFood = 25,
                BiomassRecoveryPerDay = .001f,
                FoodRecoveryPerDay = .001f
            };
        }
        return world;
    }

    private static TileEcologyResult Integrate(WorldState world, string kind,
        double requested, double days, double impact = 1)
    {
        return EcologicalPressure.Integrate(world, 0,
            new[] { new Demand { Tile = 0, Entity = 7, Kind = kind, Food = requested, ImpactMultiplier = impact } },
            0, days, projectWood: false);
    }

    private static double LegacyDamage(WorldState world, double days, double harvest,
        double impact)
    {
        var tile = world.Tiles[0];
        var opening = new EcologyView(tile);
        var representative = LazyEcology.MeanView(world, tile, opening, days);
        var exposure = new SeasonExposure(0, days, world.Config.BaseDays);
        double realizedRate = harvest / days, weightedDamage = 0;
        for (int season = 0; season < 4; season++)
        {
            double weight = exposure.At(season) / days;
            if (weight <= 0) continue;
            var profile = new EcologyCapacity(world, tile, representative, season);
            double safe = EcologyMath.SustainableHarvestRate(profile.Biomass, profile.BiomassRate,
                world.Config.SustainableProductionFraction, profile.ManagedFlow);
            double finite = profile.Biomass / Math.Min(world.Config.BaseDays, days)
                + profile.Biomass * profile.BiomassRate + profile.ManagedFlow;
            double rate = Math.Min(realizedRate, finite);
            double utilization = rate / Math.Max(1e-12, safe);
            weightedDamage += weight * Math.Pow(Math.Max(0, utilization - world.Config.SafeUseRatio),
                world.Config.PressureExponent);
        }
        return world.Config.AquaticUseDamagePerYear * impact * weightedDamage
            * days / world.Config.DaysPerYear;
    }

    private static double CorrectedDamage(WorldState world, double days, double harvest,
        double impact)
    {
        var tile = world.Tiles[0];
        var opening = new EcologyView(tile);
        var representative = LazyEcology.MeanView(world, tile, opening, days);
        var exposure = new SeasonExposure(0, days, world.Config.BaseDays);
        var ceilings = new double[4];
        var safeRates = new double[4];
        for (int season = 0; season < 4; season++)
        {
            var profile = new EcologyCapacity(world, tile, representative, season);
            safeRates[season] = EcologyMath.SustainableHarvestRate(profile.Biomass,
                profile.BiomassRate, world.Config.SustainableProductionFraction,
                profile.ManagedFlow);
            ceilings[season] = profile.Biomass / Math.Min(world.Config.BaseDays, days)
                + profile.Biomass * profile.BiomassRate + profile.ManagedFlow;
        }

        // For this four-season fixture only the first profile is capped at
        // lambda. The remaining equally exposed profiles share the residual
        // at a common rate, yielding an independent closed-form pin.
        double firstDays = exposure.At(0);
        double firstRate = ceilings[0];
        double lambda = (harvest - firstDays * firstRate) / (days - firstDays);
        if (lambda < 0 || Enumerable.Range(1, 3).Any(i => lambda >= ceilings[i]))
            throw new Exception("Aquatic seasonal pressure fixture no longer has its pinned cap pattern.");
        double weightedDamage = 0;
        for (int season = 0; season < 4; season++)
        {
            double rate = season == 0 ? firstRate : lambda;
            double utilization = rate / Math.Max(1e-12, safeRates[season]);
            weightedDamage += exposure.At(season) / days * Math.Pow(
                Math.Max(0, utilization - world.Config.SafeUseRatio),
                world.Config.PressureExponent);
        }
        return world.Config.AquaticUseDamagePerYear * impact * weightedDamage
            * days / world.Config.DaysPerYear;
    }

    private static void CheckConservation(TileEcologyResult result, string context)
    {
        var balance = result.Balance;
        Check(Near(balance.StartingBiomass + balance.BiomassRecharge,
            balance.EndingBiomass + balance.BiomassHarvest + balance.BiomassOverflow), context + " biomass ledger");
        Check(Near(balance.StartingFood + balance.FoodRecharge,
            balance.EndingFood + balance.FoodHarvest + balance.FoodOverflow), context + " food ledger");
        Check(Near(balance.StartingWater + balance.WaterRecharge,
            balance.EndingWater + balance.WaterHarvest + balance.WaterOverflow), context + " water ledger");
    }

    public static int Run()
    {
        checks = 0;

        double[] days = { 100, 100, 100, 100 };
        double[] ceilings = { .01, .2, .3, .4 };
        double realizedAmount = 40;
        double legacyRate = realizedAmount / days.Sum();
        var allocated = EcologyMath.AllocateCappedSeasonalRates(realizedAmount, days, ceilings);
        Check(legacyRate > ceilings[0] && Near(allocated[0], ceilings[0]),
            "capped bisection removes the old over-allocation to a low-biomass cold profile");
        Check(Near(Enumerable.Range(0, days.Length).Sum(i => days[i] * allocated[i]), realizedAmount),
            "seasonal rates conserve the fixed realized horizon amount");
        Check(allocated.Select((rate, i) => rate <= ceilings[i] + 1e-12).All(x => x),
            "no seasonal allocation exceeds its finite rate ceiling");
        Check(EcologyMath.AllocateCappedSeasonalRates(0, days, ceilings).All(rate => rate == 0),
            "zero harvest allocates zero use to every season");

        bool rejected = false;
        try { EcologyMath.AllocateCappedSeasonalRates(92, days, ceilings); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "an impossible receipt above combined finite ceilings is rejected");

        const double horizon = 365;
        foreach (string kind in new[] { "Fishing", "Aquatic gathering" })
        {
            var legacyWorld = ColdWater(false);
            double openingFood = legacyWorld.Tiles[0].Food;
            double openingBiomass = legacyWorld.Tiles[0].PotentialFood;
            double openingCondition = legacyWorld.Tiles[0].AquaticCondition;
            double openingTimestamp = legacyWorld.Tiles[0].LastEcologyUpdateDays;
            var legacy = Integrate(legacyWorld, kind, 80, horizon);
            double legacyHarvest = legacy.Transfers.Sum(transfer => transfer.Food);
            double legacyDamage = legacy.Pressure.Single().ConditionDamage;
            Check(Near(legacy.Balance.StartingFood, openingFood)
                && Near(legacy.Balance.StartingBiomass, openingBiomass)
                && Near(openingFood, 1.25) && Near(openingBiomass, 5), kind + " pins opening stock");
            Check(Near(legacyHarvest, 80) && Near(legacy.Balance.FoodHarvest, 80), kind + " pins the horizon receipt");
            Check(Math.Abs(legacy.View.Food - 5.0651793767248816e-8) <= 1e-12
                && Math.Abs(legacy.View.PotentialFood - 2.0260717506899526e-7) <= 1e-12,
                kind + " pins legacy endpoint food and biomass");
            Check(Near(legacyDamage, 3603256.1819647388, 1e-10), kind + " pins legacy condition damage");
            Check(legacy.View.Food >= 0 && legacy.View.PotentialFood >= legacy.View.Food
                && legacy.View.PotentialFood <= legacy.Balance.StartingBiomass + legacy.Balance.BiomassRecharge + 1e-8,
                kind + " pins bounded endpoint stocks");
            Check(Near(legacyDamage, LegacyDamage(legacyWorld, horizon, legacyHarvest, 1), 1e-7),
                kind + " gate-off path retains the legacy season-independent rate comparison");
            Check(legacyWorld.Tiles[0].Food == openingFood
                && legacyWorld.Tiles[0].PotentialFood == openingBiomass
                && legacyWorld.Tiles[0].AquaticCondition == openingCondition
                && legacyWorld.Tiles[0].LastEcologyUpdateDays == openingTimestamp,
                kind + " forecast integration writes no intermediate world state");
            CheckConservation(legacy, kind + " legacy");

            var correctedWorld = ColdWater(true);
            double correctedOpeningFood = correctedWorld.Tiles[0].Food;
            double correctedOpeningBiomass = correctedWorld.Tiles[0].PotentialFood;
            double correctedOpeningCondition = correctedWorld.Tiles[0].AquaticCondition;
            double correctedOpeningTimestamp = correctedWorld.Tiles[0].LastEcologyUpdateDays;
            var corrected = Integrate(correctedWorld, kind, 80, horizon);
            double correctedHarvest = corrected.Transfers.Sum(transfer => transfer.Food);
            double correctedDamage = corrected.Pressure.Single().ConditionDamage;
            Check(Math.Abs(corrected.View.Food - 5.065179359790348e-8) <= 1e-12
                && Math.Abs(corrected.View.PotentialFood - 2.026071743916139e-7) <= 1e-12,
                kind + " pins corrected endpoint food and biomass");
            Check(Near(correctedDamage, 3603256.1940115904, 1e-10),
                kind + " pins corrected condition damage");
            Check(Near(correctedHarvest, legacyHarvest)
                && Near(corrected.View.Food, legacy.View.Food)
                && Near(corrected.View.PotentialFood, legacy.View.PotentialFood),
                kind + " correction changes damage accounting only, not receipts or endpoint stock");
            Check(double.IsFinite(correctedDamage) && correctedDamage >= 0
                && Near(correctedDamage, CorrectedDamage(correctedWorld, horizon,
                    correctedHarvest, 1), 1e-10),
                kind + " condition damage matches conserved harvest allocated under the cold profile cap");
            Check(Math.Abs(correctedDamage - legacyDamage) > .001,
                kind + " corrected cold-profile damage differs from the legacy rate copied to every season");
            Check(correctedWorld.Tiles[0].Food == correctedOpeningFood
                && correctedWorld.Tiles[0].PotentialFood == correctedOpeningBiomass
                && correctedWorld.Tiles[0].AquaticCondition == correctedOpeningCondition
                && correctedWorld.Tiles[0].LastEcologyUpdateDays == correctedOpeningTimestamp,
                kind + " corrected forecast integration writes no intermediate world state");
            CheckConservation(corrected, kind + " corrected");

            var doubledImpact = Integrate(ColdWater(true), kind, 80, horizon, 2);
            Check(Near(doubledImpact.Pressure.Single().ConditionDamage, 2 * correctedDamage, 1e-7),
                kind + " condition damage scales monotonically with the impact multiplier");
            var lowerClaim = Integrate(ColdWater(true), kind, 40, horizon);
            Check(correctedDamage >= lowerClaim.Pressure.Single().ConditionDamage,
                kind + " condition damage is monotone in realized harvest");

            var durationScaled = Integrate(ColdWater(true), kind, 160, 2 * horizon);
            Check(Near(durationScaled.Balance.FoodHarvest, 160)
                && Near(durationScaled.Pressure.Single().ConditionDamage, 2 * correctedDamage, 1e-7),
                kind + " scales harvest and seasonal damage with equivalent doubled duration");
        }

        var warmLegacy = Integrate(WarmWater(false), "Aquatic gathering", 40, horizon);
        var warmCorrected = Integrate(WarmWater(true), "Aquatic gathering", 40, horizon);
        Check(Near(warmCorrected.Pressure.Single().ConditionDamage,
                warmLegacy.Pressure.Single().ConditionDamage, 1e-10)
            && Near(warmCorrected.Balance.FoodHarvest, warmLegacy.Balance.FoodHarvest)
            && Near(warmCorrected.View.Food, warmLegacy.View.Food)
            && Near(warmCorrected.View.PotentialFood, warmLegacy.View.PotentialFood),
            "identical warm profiles retain the legacy condition damage and endpoint calibration");

        var empty = Integrate(ColdWater(true), "Aquatic gathering", 0, horizon);
        Check(empty.Pressure.All(entry => entry.ConditionDamage == 0),
            "zero realized harvest produces zero aquatic use damage");

        var zeroSafeWorld = ColdWater(true);
        for (int season = 0; season < 4; season++)
        {
            var profile = zeroSafeWorld.Tiles[0].Seasons[season];
            profile.BiomassRecoveryPerDay = profile.FoodRecoveryPerDay = 0;
            zeroSafeWorld.Tiles[0].Seasons[season] = profile;
        }
        var zeroSafe = Integrate(zeroSafeWorld, "Fishing", 80, horizon);
        Check(double.IsFinite(zeroSafe.Pressure.Single().ConditionDamage),
            "zero safe rate uses the finite epsilon guard without non-finite damage");

        var highSafeWorld = ColdWater(true);
        for (int season = 0; season < 4; season++)
        {
            var profile = highSafeWorld.Tiles[0].Seasons[season];
            profile.BiomassRecoveryPerDay = profile.FoodRecoveryPerDay = .1f;
            highSafeWorld.Tiles[0].Seasons[season] = profile;
        }
        var highSafe = Integrate(highSafeWorld, "Aquatic gathering", 80, horizon);
        Check(double.IsFinite(highSafe.Pressure.Single().ConditionDamage),
            "high safe-rate profiles keep the corrected damage finite");

        Console.WriteLine("PASS: " + checks + " aquatic seasonal pressure assertions");
        return checks;
    }
}
