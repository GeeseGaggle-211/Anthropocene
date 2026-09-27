using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Anthropocene.Simulation;

// Deterministic cultivation ecology matrix for issue #25. It exercises the
// ecology authority directly, without planning or Village Elder decisions.
internal static class Issue25EcologyChecks
{
    private const int TileId = 0;
    private const double SweepDurationDays = 30;
    private const double InitialWoodCapacity = 250;
    private static readonly double[] QValues = { 0, .25, .5, .6, .75, .8, 1 };
    private static readonly double[] Conditions = { 1, .75, .5 };
    private static readonly double[] OperatingFractions = { 0, .5, 1 };
    private static readonly double[] HarvestMultipliers = { 0, 1, 1.25 };
    private static int checks;

    private sealed class SweepRow
    {
        public VegetationTier Vegetation;
        public double Q, Condition, OperatingFraction, HarvestMultiplier;
        public double BiomassCapacity, WildFoodEcologicalCapacity, WildAccessibleCapacity, WildAccessibleFlow;
        public double FoodCapacity, ManagedFlow, SustainableHarvestRate, SustainableHarvestFlow;
        public double RequestedHarvest, RealizedHarvest, LandConditionChange;
        public double Retention, EffectiveNaturalVegetationService, WoodCapacity;
        public double EndingFood, EndingPotentialFood, EndingBiomassCapacity;
    }

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception("Issue25 ecology: " + message);
    }

    private static bool Near(double actual, double expected, double tolerance = 1e-8)
        => Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(expected)));

    private static double EcologicalWildFoodCapacity(VegetationTier vegetation)
        => vegetation == VegetationTier.Low ? 60 : vegetation == VegetationTier.Medium ? 120 : 150;

    private static double EcologicalWoodCapacity(VegetationTier vegetation)
        => vegetation == VegetationTier.Low ? 75 : vegetation == VegetationTier.Medium ? 150 : InitialWoodCapacity;

    private static WorldState Fixture(VegetationTier vegetation, double q, double condition, double operatingFraction)
    {
        var config = SimulationConfig.Small(1);
        config.Width = 12;
        config.Height = 8;
        config.Players = 1;
        config.TurnDays = 20 * config.DaysPerYear;
        config.ConditionRecoveryPerYear = 0;
        config.SafeUseRatio = 1;
        config.LandSafeUseRatio = 1;
        config.Validate();

        var world = new WorldState
        {
            Seed = 250025,
            Config = config,
            Generation = new GenerationReport(),
            Tiles = new Tile[config.Width * config.Height]
        };
        for (int id = 0; id < world.Tiles.Length; id++)
        {
            var tile = new Tile
            {
                Id = id,
                Environment = EnvironmentKind.Temperate,
                NaturalVegetationPotential = vegetation,
                CurrentVegetation = vegetation,
                ElevationLevel = 1,
                LandCondition = condition,
                AquaticCondition = 1,
                Freshwater = 40,
                FreshwaterSource = FreshwaterSource.River,
                CultivationCapital = q <= 0 ? 0 : Math.Pow(q / config.ReferenceIntensity, 2),
                Cultivation = (float)q,
                Cultivated = q > 0,
                CultivationOperator = q > 0 ? 1 : -1,
                CultivationOperatingFraction = operatingFraction,
                LastEcologyUpdateDays = 0
            };
            for (int season = 0; season < tile.Seasons.Length; season++)
            {
                tile.Seasons[season] = new SeasonData
                {
                    Temperature = 18,
                    Moisture = .55f,
                    RegionalMoisture = .5f,
                    Habitat = .6f,
                    MaxFood = (float)EcologicalWildFoodCapacity(vegetation),
                    MaxPotentialFood = 600,
                    MaxFreshwater = 50,
                    FoodRecoveryPerDay = .004f,
                    BiomassRecoveryPerDay = .004f,
                    WaterRecoveryPerDay = .002f
                };
            }
            world.Tiles[id] = tile;
        }

        var field = world.Tiles[TileId];
        var initial = new EcologyView(field);
        var capacity = new EcologyCapacity(world, field, initial, 0, operatingFraction);
        field.Food = capacity.Food * .8;
        field.PotentialFood = capacity.Biomass * .8;
        return world;
    }

    private static double DerivedQ(WorldState world)
        => CultivationEconomy.Intensity(world.Config, world.Tiles[TileId].CultivationCapital);

    private static double EcologicalWildCapacity(WorldState world, EcologyView view)
    {
        var ecologicalView = view;
        ecologicalView.CultivationCapital = 0;
        return new EcologyCapacity(world, world.Tiles[TileId], ecologicalView, 0).WildFood;
    }

    private static TileEcologyResult IntegrateHarvest(WorldState world, double durationDays, double harvest)
    {
        var demand = harvest <= 0
            ? Array.Empty<Demand>()
            : new[] { new Demand { Tile = TileId, Entity = 1, Kind = "Herd", PotentialFood = harvest } };
        return EcologicalPressure.Integrate(world, TileId, demand, world.Tiles[TileId].LastEcologyUpdateDays, durationDays);
    }

    private static void Commit(WorldState world, TileEcologyResult result, double worldDays)
    {
        result.View.Commit(world.Tiles[TileId]);
        if (result.HasWoodSource) MaterialEconomy.CommitWoodProjection(world, TileId, result.WoodRemaining, result.WoodDays);
        world.Days = worldDays;
    }

    private static List<SweepRow> Sweep()
    {
        var rows = new List<SweepRow>();
        foreach (VegetationTier tier in new[] { VegetationTier.Low, VegetationTier.Medium, VegetationTier.High })
        foreach (double q in QValues)
        foreach (double condition in Conditions)
        foreach (double operating in OperatingFractions)
        foreach (double multiplier in HarvestMultipliers)
        {
            var world = Fixture(tier, q, condition, operating);
            var tile = world.Tiles[TileId];
            var initialView = new EcologyView(tile);
            double actualQ = DerivedQ(world);
            var ecology = new EcologyCapacity(world, tile, initialView, 0);
            double ecologicalWild = EcologicalWildCapacity(world, initialView);
            double initialSustainableRate = EcologyMath.SustainableHarvestRate(
                ecology.Biomass, ecology.BiomassRate, world.Config.SustainableProductionFraction, ecology.ManagedFlow);
            // The 30-day authority holds q at its horizon-mean decay, so its
            // non-damaging budget is the reduced four-profile safe rate rather
            // than the instantaneous opening-day rate.
            var horizonView = LazyEcology.MeanView(world, tile, initialView, SweepDurationDays);
            var horizonCapacity = new HorizonEcologyCapacity(world, tile, horizonView, 0, SweepDurationDays);
            double sustainableRate = horizonCapacity.SafeRate;
            double managedFlow = EcologyMath.ManagedRegenerationFlow(world.Config, actualQ, ecology.Biomass,
                ecology.BiomassRate, operating);
            double sustainableFlow = sustainableRate * SweepDurationDays;
            double requestedHarvest = multiplier * sustainableFlow;
            double rawWoodCapacity = EcologicalWoodCapacity(tier) * condition;
            double retention = EcologyMath.CultivationVegetationRetention(world.Config, actualQ);
            double woodCapacity = MaterialEconomy.WoodCapacity(EcologicalWoodCapacity(tier), tile, initialView, world.Config);
            var expectedFoodCapacity = EcologyMath.EffectiveWildFoodCapacity(ecologicalWild, ecology.Biomass, actualQ, world.Config);

            var result = IntegrateHarvest(world, SweepDurationDays, requestedHarvest);
            double realizedHarvest = result.Transfers.Sum(transfer => transfer.Food + transfer.PotentialFood);
            double endingCapacity = new EcologyCapacity(world, tile, result.View, 0).Biomass;
            var row = new SweepRow
            {
                Vegetation = tier,
                Q = actualQ,
                Condition = condition,
                OperatingFraction = operating,
                HarvestMultiplier = multiplier,
                BiomassCapacity = ecology.Biomass,
                WildFoodEcologicalCapacity = ecologicalWild,
                WildAccessibleCapacity = ecology.WildFood,
                WildAccessibleFlow = ecology.WildFood * ecology.FoodRate,
                FoodCapacity = ecology.Food,
                ManagedFlow = ecology.ManagedFlow,
                SustainableHarvestRate = sustainableRate,
                SustainableHarvestFlow = sustainableFlow,
                RequestedHarvest = requestedHarvest,
                RealizedHarvest = realizedHarvest,
                LandConditionChange = result.View.LandCondition - initialView.LandCondition,
                Retention = retention,
                EffectiveNaturalVegetationService = ((int)tier + 1) * retention,
                WoodCapacity = woodCapacity,
                EndingFood = result.View.Food,
                EndingPotentialFood = result.View.PotentialFood,
                EndingBiomassCapacity = endingCapacity
            };
            rows.Add(row);

            Check(Near(actualQ, q), $"fixture preserves q={q:R}");
            Check(Near(ecology.WildFood, ecologicalWild * retention), $"wild food retains the same fraction for {tier} q={q:R}");
            Check(Near(ecology.Food, expectedFoodCapacity), $"accessible food uses the locked wild-plus-cultivated composition for {tier} q={q:R}");
            Check(Near(ecology.Biomass, tile.Seasons[0].MaxPotentialFood * condition), $"q does not reduce biomass ceiling for {tier} q={q:R}");
            Check(Near(woodCapacity, EcologyMath.EffectiveWoodCapacity(rawWoodCapacity, actualQ, world.Config)), $"Wood cap follows the shared retention function for {tier} q={q:R}");
            Check(Near(initialSustainableRate, ecology.Biomass * ecology.BiomassRate * world.Config.SustainableProductionFraction + ecology.ManagedFlow), $"managed renewal enters opening safe rate once and outside the natural fraction for {tier} q={q:R}");
            Check(Near(ecology.ManagedFlow, managedFlow), $"managed regeneration uses the shared condition-dependent flow helper for {tier} q={q:R}");
            Check(Near(sustainableFlow, horizonCapacity.SafeRate * SweepDurationDays), $"reduced horizon safe flow is a rate multiplied by duration once for {tier} q={q:R}");
            Check(Near(result.Balance.StartingBiomass + result.Balance.BiomassRecharge,
                result.Balance.EndingBiomass + result.Balance.BiomassHarvest + result.Balance.BiomassOverflow, 1e-7), $"biomass mass balance for {tier} q={q:R}");
            Check(Near(result.Balance.StartingFood + result.Balance.FoodRecharge,
                result.Balance.EndingFood + result.Balance.FoodHarvest + result.Balance.FoodOverflow, 1e-7), $"food mass balance for {tier} q={q:R}");
            Check(result.View.Food <= result.View.PotentialFood + 1e-8 && result.View.PotentialFood <= endingCapacity + 1e-8,
                $"food stocks remain within biomass capacity for {tier} q={q:R}");
            Check(Math.Abs(row.RealizedHarvest - row.RequestedHarvest) <= 1e-7 * Math.Max(1, row.RequestedHarvest),
                $"the representative 30-day fixture realizes the requested {multiplier:R}x flow for {tier} q={q:R}; realized={row.RealizedHarvest:R} requested={row.RequestedHarvest:R}");
            Check(Near(row.LandConditionChange, 0, 1e-12),
                $"all realized land extraction and cultivation leave stored condition unchanged for {tier} q={q:R} operation={operating:R}");
        }
        return rows;
    }

    private static void LockedRetentionValues()
    {
        var config = Fixture(VegetationTier.High, 0, 1, 0).Config;
        var locked = new[] { (0.0, 1.0), (.25, 1.0), (.5, 1.0), (.6, .8), (.75, .5), (.8, .4), (1.0, 0.0) };
        foreach (var pair in locked)
            Check(Near(EcologyMath.CultivationVegetationRetention(config, pair.Item1), pair.Item2, 1e-12), $"locked retention at q={pair.Item1:R}");
    }

    private static void PaleoEquivalenceAndHealthyIntensiveField()
    {
        var paleo = Fixture(VegetationTier.High, 0, 1, 0);
        var paleoTile = paleo.Tiles[TileId];
        var paleoView = new EcologyView(paleoTile);
        var paleoK = new EcologyCapacity(paleo, paleoTile, paleoView, 0);
        double oldWildFood = Math.Min(paleoK.Biomass, paleoTile.Seasons[0].MaxFood *
            FoodEcology.GatherableFraction(paleoTile, paleo.Config, paleoView.CurrentVegetation) /
            FoodEcology.GatherableFraction(paleoTile, paleo.Config, paleoTile.NaturalVegetationPotential));
        Check(Near(paleoK.Biomass, paleoTile.Seasons[0].MaxPotentialFood) && Near(paleoK.WildFood, oldWildFood)
            && Near(paleoK.Food, oldWildFood) && Near(paleoK.ManagedFlow, 0), "q=0 retains the pre-cultivation Paleo ecology capacities and has no managed flow");

        var healthy = Fixture(VegetationTier.High, 1, 1, 1);
        var field = healthy.Tiles[TileId];
        var before = new EcologyView(field);
        var capacity = new EcologyCapacity(healthy, field, before, 0);
        var noHarvest = IntegrateHarvest(healthy, SweepDurationDays, 0);
        Check(Near(capacity.WildFood, 0) && Near(capacity.Food, capacity.Biomass), "q=1 has zero natural food service and full biomass accessibility");
        Check(Near(noHarvest.View.LandCondition, 1) && noHarvest.View.CurrentVegetation == VegetationTier.High, "healthy q=1 field retains healthy LandCondition and raw ecological vegetation state");
        Check(Near(capacity.Biomass, field.Seasons[0].MaxPotentialFood), "healthy q=1 remains at the ecological biomass ceiling");
        Check(capacity.ManagedFlow > 0, "operated q=1 has additive managed regeneration");
        Check(capacity.Food * capacity.FoodRate + capacity.ManagedFlow > paleoK.Food * paleoK.FoodRate,
            "operated intensive cultivation increases useful renewable food flow");
        Check(Near(MaterialEconomy.WoodCapacity(InitialWoodCapacity, field, before, healthy.Config), 0), "q=1 has no standing natural Wood capacity");
        Check(Vegetation.RetainedCover(field.CurrentVegetation, EcologyMath.CultivationVegetationRetention(healthy.Config, 1)) == VegetationTier.Low
            && field.CurrentVegetation == VegetationTier.High,
            "actual-cover presentation can show q=1 as open while raw ecological vegetation remains High");

        var degraded = Fixture(VegetationTier.High, 1, .5, 1);
        var degradedTile = degraded.Tiles[TileId];
        var degradedK = new EcologyCapacity(degraded, degradedTile, new EcologyView(degradedTile), 0);
        Check(degradedK.Biomass < capacity.Biomass && degradedK.ManagedFlow < capacity.ManagedFlow,
            "independent LandCondition loss reduces biomass capacity and managed agricultural productivity");

        var forest = Fixture(VegetationTier.High, 0, 1, 1);
        var cultivatedForest = Fixture(VegetationTier.High, 1, 1, 1);
        CultivationEconomy.Factors(forest.Config, forest.Tiles[TileId], out double forestBuild, out double forestMaintenance);
        CultivationEconomy.Factors(cultivatedForest.Config, cultivatedForest.Tiles[TileId], out double fieldBuild, out double fieldMaintenance);
        Check(Near(forestBuild, forest.Config.Neolithic.ForestBuild) && Near(fieldBuild, forestBuild), "q=1 forest-capable tile keeps raw forest build surcharge");
        Check(Near(forestMaintenance, forest.Config.Neolithic.ForestMaintenance) && Near(fieldMaintenance, forestMaintenance), "q=1 forest-capable tile keeps raw forest maintenance surcharge");
        Check(forestBuild > forest.Config.Neolithic.OpenBuild && forestMaintenance > forest.Config.Neolithic.OpenMaintenance,
            "raw forest clearing and succession pressure remain more laborious than open-land work");
    }

    private static void ExpansionClearingIsCoverOnly()
    {
        var world = NeolithicChecks.Fixture();
        var camp = world.Camps[0];
        world.Config.ConditionRecoveryPerYear = 0;
        camp.CultivationFocusTile = camp.Tile;
        var field = world.Tiles[camp.Tile];
        field.Environment = EnvironmentKind.Temperate;
        field.NaturalVegetationPotential = VegetationTier.High;
        field.CurrentVegetation = VegetationTier.High;
        field.CultivationCapital = Math.Pow(.5 / world.Config.ReferenceIntensity, 2);
        field.Cultivation = .5f;
        field.Cultivated = true;
        field.CultivationOperator = camp.Id;
        var record = new TurnRecord();

        double used = CultivationEconomy.Expand(world, camp, 1000, false, record);
        double resultingQ = CultivationEconomy.Intensity(world.Config, field.CultivationCapital);
        Check(used > 0 && resultingQ > .5, "focused forest field expands beyond the locked displacement threshold");
        Check(Near(field.LandCondition, 1, 1e-12), "ordinary forest clearing leaves biological land condition unchanged");
        Check(field.CurrentVegetation == VegetationTier.High, "ordinary clearing does not overwrite raw ecological vegetation");
        var conversion = record.Pressure.Where(entry => entry.Kind == "Cultivation vegetation displacement").ToArray();
        Check(conversion.Length > 0 && conversion.All(entry => entry.ConditionDamage == 0),
            "cultivation cover conversion is recorded with zero condition damage");
    }

    private static void WoodClampAndAbandonmentRecovery()
    {
        var world = Fixture(VegetationTier.High, 0, 1, 1);
        AddRecoveringWood(world);
        var source = world.MaterialSources.Single();
        source.Remaining = source.Initial;

        world.Tiles[TileId].CultivationCapital = Math.Pow(1 / world.Config.ReferenceIntensity, 2);
        world.Tiles[TileId].Cultivation = 1;
        LazyEcology.MaterializeEcology(world, TileId, 0);
        Check(Near(source.Remaining, 0), "raising q to one immediately clamps Wood stock to the zero effective cap");

        world.Tiles[TileId].CultivationCapital = 0;
        world.Tiles[TileId].Cultivation = 0;
        LazyEcology.MaterializeEcology(world, TileId, 0);
        Check(Near(source.Remaining, 0), "relaxing q at the same timestamp does not create Wood immediately");
        var recovered = LazyEcology.MaterializeEcology(world, TileId, 20 * world.Config.DaysPerYear);
        double relaxedCap = MaterialEconomy.WoodCapacity(source.Initial, world.Tiles[TileId], recovered, world.Config);
        Check(source.Remaining > 0 && source.Remaining < relaxedCap,
            "Wood stock recovers after the cap relaxes through its duration-aware recovery rule");
        Check(source.Remaining <= relaxedCap + 1e-8, "abandoned-field Wood never exceeds the ecological capacity");

        var abandoned = Fixture(VegetationTier.High, 1, 1, 0);
        var abandonedPartitioned = Fixture(VegetationTier.High, 1, 1, 0);
        AddRecoveringWood(abandoned);
        AddRecoveringWood(abandonedPartitioned);
        var abandonedSource = abandoned.MaterialSources.Single();
        var partitionedSource = abandonedPartitioned.MaterialSources.Single();
        abandonedSource.Remaining = partitionedSource.Remaining = 0;
        LazyEcology.MaterializeEcology(abandoned, TileId, 0);
        LazyEcology.MaterializeEcology(abandonedPartitioned, TileId, 0);
        Check(Near(abandonedSource.Remaining, 0) && Near(partitionedSource.Remaining, 0),
            "q=1 abandonment starts with no Wood above its zero capacity");
        var afterAbandonment = LazyEcology.MaterializeEcology(abandoned, TileId, 160 * abandoned.Config.DaysPerYear);
        for (int year = 1; year <= 160; year++)
            LazyEcology.MaterializeEcology(abandonedPartitioned, TileId, year * abandonedPartitioned.Config.DaysPerYear);
        double decayedQ = DerivedQ(abandoned);
        double abandonmentCap = MaterialEconomy.WoodCapacity(abandonedSource.Initial, abandoned.Tiles[TileId], afterAbandonment, abandoned.Config);
        Check(Near(decayedQ, .5, 1e-8) && Near(EcologyMath.CultivationVegetationRetention(abandoned.Config, decayedQ), 1),
            "existing cultivation-capital decay lowers q to about .5 after 160 years and relaxes the natural-cover cap");
        Check(abandonedSource.Remaining > 0 && abandonedSource.Remaining < abandonmentCap,
            "abandonment restores Wood only through duration-aware recovery under the relaxed cap");
        double abandonmentCapitalDelta = Math.Abs(abandoned.Tiles[TileId].CultivationCapital - abandonedPartitioned.Tiles[TileId].CultivationCapital);
        double abandonmentFoodDelta = Math.Abs(abandoned.Tiles[TileId].Food - abandonedPartitioned.Tiles[TileId].Food);
        double abandonmentBiomassDelta = Math.Abs(abandoned.Tiles[TileId].PotentialFood - abandonedPartitioned.Tiles[TileId].PotentialFood);
        double abandonmentWoodDelta = Math.Abs(abandonedSource.Remaining - partitionedSource.Remaining);
        Check(Near(abandoned.Tiles[TileId].CultivationCapital, abandonedPartitioned.Tiles[TileId].CultivationCapital, 1e-10)
            && abandoned.Tiles[TileId].Food >= 0
            && abandonedPartitioned.Tiles[TileId].Food >= 0
            && abandoned.Tiles[TileId].PotentialFood >= abandoned.Tiles[TileId].Food
            && abandonedPartitioned.Tiles[TileId].PotentialFood >= abandonedPartitioned.Tiles[TileId].Food
            && abandonedSource.Remaining >= 0 && partitionedSource.Remaining >= 0
            && abandonedSource.Remaining <= abandonmentCap + 1e-8
            && partitionedSource.Remaining <= MaterialEconomy.WoodCapacity(partitionedSource.Initial,
                abandonedPartitioned.Tiles[TileId], new EcologyView(abandonedPartitioned.Tiles[TileId]),
                abandonedPartitioned.Config) + 1e-8,
            "160-year direct and annual calibration projections keep exact decay and bounded stocks");
        Console.WriteLine("ISSUE25_ABANDONMENT_PARTITION absDelta cultivationCapital=" + Number(abandonmentCapitalDelta)
            + " food=" + Number(abandonmentFoodDelta) + " biomass=" + Number(abandonmentBiomassDelta) + " wood=" + Number(abandonmentWoodDelta)
            + " mode=calibration q=" + Number(decayedQ));
    }

    private static void DurationPartitionAndSaveReplay()
    {
        var whole = Fixture(VegetationTier.High, .75, 1, 1);
        var split = Fixture(VegetationTier.High, .75, 1, 1);
        AddRecoveringWood(whole);
        AddRecoveringWood(split);
        var initial = new EcologyCapacity(whole, whole.Tiles[TileId], new EcologyView(whole.Tiles[TileId]), 0);
        double annualHarvest = EcologyMath.SustainableHarvestRate(initial.Biomass, initial.BiomassRate,
            whole.Config.SustainableProductionFraction, initial.ManagedFlow) * whole.Config.DaysPerYear;
        var one = IntegrateHarvest(whole, 20 * whole.Config.DaysPerYear, 20 * annualHarvest);
        CheckBalance(one, "20-year direct horizon");
        Commit(whole, one, 20 * whole.Config.DaysPerYear);

        for (int year = 0; year < 20; year++)
        {
            var part = IntegrateHarvest(split, split.Config.DaysPerYear, annualHarvest);
            CheckBalance(part, "one-year calibration horizon " + year);
            Commit(split, part, (year + 1) * split.Config.DaysPerYear);
        }
        double foodDelta = Math.Abs(whole.Tiles[TileId].Food - split.Tiles[TileId].Food);
        double biomassDelta = Math.Abs(whole.Tiles[TileId].PotentialFood - split.Tiles[TileId].PotentialFood);
        double conditionDelta = Math.Abs(whole.Tiles[TileId].LandCondition - split.Tiles[TileId].LandCondition);
        double capitalDelta = Math.Abs(whole.Tiles[TileId].CultivationCapital - split.Tiles[TileId].CultivationCapital);
        double woodDelta = Math.Abs(whole.MaterialSources.Single().Remaining - split.MaterialSources.Single().Remaining);
        Check(Near(whole.Tiles[TileId].CultivationCapital, split.Tiles[TileId].CultivationCapital, 1e-10)
            && whole.Tiles[TileId].Food >= 0 && split.Tiles[TileId].Food >= 0
            && whole.Tiles[TileId].PotentialFood >= whole.Tiles[TileId].Food
            && split.Tiles[TileId].PotentialFood >= split.Tiles[TileId].Food
            && whole.Tiles[TileId].LandCondition >= 0 && whole.Tiles[TileId].LandCondition <= 1
            && split.Tiles[TileId].LandCondition >= 0 && split.Tiles[TileId].LandCondition <= 1
            && whole.MaterialSources.Single().Remaining >= 0 && split.MaterialSources.Single().Remaining >= 0,
            "direct and annual calibration horizons preserve capital decay and bounded ecological state");
        Console.WriteLine("ISSUE25_PARTITION absDelta food=" + Number(foodDelta) + " biomass=" + Number(biomassDelta)
            + " condition=" + Number(conditionDelta) + " cultivationCapital=" + Number(capitalDelta) + " wood=" + Number(woodDelta)
            + " mode=calibration");

        var restored = SaveCodec.Load(SaveCodec.Save(whole));
        bool saveFingerprintMatches = SaveCodec.Fingerprint(whole) == SaveCodec.Fingerprint(restored);
        Check(saveFingerprintMatches, "post-sweep ecology state round-trips through save/load");
        // OperatingFraction is a turn-local resolved input; restore the same next-turn
        // forcing on both worlds, as the turn resolver does after loading.
        whole.Tiles[TileId].CultivationOperatingFraction = restored.Tiles[TileId].CultivationOperatingFraction = 1;
        var replayA = IntegrateHarvest(whole, whole.Config.DaysPerYear, annualHarvest);
        var replayB = IntegrateHarvest(restored, restored.Config.DaysPerYear, annualHarvest);
        Commit(whole, replayA, whole.Days + whole.Config.DaysPerYear);
        Commit(restored, replayB, restored.Days + restored.Config.DaysPerYear);
        bool continuationFingerprintMatches = SaveCodec.Fingerprint(whole) == SaveCodec.Fingerprint(restored);
        Check(continuationFingerprintMatches, "ecology continuation after save/load replays deterministically");
        Console.WriteLine("ISSUE25_REPLAY saveFingerprint=" + (saveFingerprintMatches ? "identical" : "different")
            + " continuationFingerprint=" + (continuationFingerprintMatches ? "identical" : "different"));
    }

    private sealed class HumanRun
    {
        public double Requested, Realized, Unmet, SafeFlow, ManagedFlow, Damage;
        public double StartingFood, StartingBiomass, EndingFood, EndingBiomass, EndingCondition;
        public int YearsRealizedAboveSafe;
    }

    // A fertile controlled tile keeps the production rates unchanged while making
    // realized human extraction above the safe rate physically possible at every q.
    private static WorldState AgricultureFixture(double q, double condition, double operation)
    {
        var world = Fixture(VegetationTier.High, q, condition, operation);
        var tile = world.Tiles[TileId];
        for (int i = 0; i < tile.Seasons.Length; i++)
        {
            var season = tile.Seasons[i];
            season.MaxFood = 550;
            tile.Seasons[i] = season;
        }
        var capacity = new EcologyCapacity(world, tile, new EcologyView(tile), 0);
        tile.Food = capacity.Food * .8;
        tile.PotentialFood = capacity.Biomass * .8;
        return world;
    }

    private static void CheckBalance(TileEcologyResult result, string context)
    {
        var b = result.Balance;
        Check(Near(b.StartingBiomass + b.BiomassRecharge,
            b.EndingBiomass + b.BiomassHarvest + b.BiomassOverflow, 1e-7),
            context + " conserves biomass");
        Check(Near(b.StartingFood + b.FoodRecharge,
            b.EndingFood + b.FoodHarvest + b.FoodOverflow, 1e-7),
            context + " conserves human-accessible food");
    }

    private static HumanRun RunHumanYears(WorldState world, int years, double safeMultiplier, string context)
    {
        var tile = world.Tiles[TileId];
        var run = new HumanRun { StartingFood = tile.Food, StartingBiomass = tile.PotentialFood };
        for (int year = 0; year < years; year++)
        {
            int season = (int)Math.Floor(tile.LastEcologyUpdateDays / world.Config.BaseDays) % 4;
            var capacity = new EcologyCapacity(world, tile, new EcologyView(tile), season);
            double renewalRate = EcologyMath.SustainableHarvestRate(capacity.Biomass, capacity.BiomassRate,
                world.Config.SustainableProductionFraction, capacity.ManagedFlow);
            double safeRate = Math.Min(renewalRate, capacity.Biomass
                * world.Config.SustainableProductionFraction / world.Config.DaysPerYear);
            double request = safeMultiplier * safeRate * world.Config.DaysPerYear;
            var claim = new Demand { Tile = TileId, Entity = 1, Kind = "Cultivation", Food = request };
            var result = EcologicalPressure.Integrate(world, TileId, new[] { claim },
                tile.LastEcologyUpdateDays, world.Config.DaysPerYear);
            double realized = result.Transfers.Sum(transfer => transfer.Food);
            double unmet = result.Pressure.Where(p => p.Pool == "Accessible food").Sum(p => p.Unmet);
            CheckBalance(result, context + " year " + year);
            Check(Near(realized + unmet, request, 1e-7), context + " accounts for human request and unmet demand in year " + year);
            Check(Near(renewalRate, capacity.Biomass * capacity.BiomassRate *
                world.Config.SustainableProductionFraction + capacity.ManagedFlow, 1e-10),
                context + " counts managed flow exactly once in year " + year);
            if (realized > safeRate * world.Config.DaysPerYear * (1 + 1e-6))
                run.YearsRealizedAboveSafe++;
            run.Requested += request;
            run.Realized += realized;
            run.Unmet += unmet;
            run.SafeFlow += safeRate * world.Config.DaysPerYear;
            run.ManagedFlow += capacity.ManagedFlow * world.Config.DaysPerYear;
            run.Damage += result.Pressure.Sum(p => p.ConditionDamage);
            Commit(world, result, tile.LastEcologyUpdateDays + world.Config.DaysPerYear);
        }
        run.EndingFood = tile.Food;
        run.EndingBiomass = tile.PotentialFood;
        run.EndingCondition = tile.LandCondition;
        return run;
    }

    private static void LongDurationHumanAgriculture()
    {
        foreach (double q in new[] { .4, .6, .8, 1.0 })
        foreach (double multiplier in new[] { .75, 1.0, 1.25 })
        {
            string context = "500-year human cultivation q=" + Number(q) + " multiplier=" + Number(multiplier);
            var world = AgricultureFixture(q, 1, 1);
            double initialQ = DerivedQ(world);
            var run = RunHumanYears(world, 500, multiplier, context);
            Check(Near(DerivedQ(world), initialQ, 1e-10), context + " holds capital under full operation");
            Check(Near(run.Requested, run.Realized + run.Unmet, 1e-7), context + " closes cumulative human claims");
            Check(run.EndingFood >= -1e-8 && run.EndingBiomass >= run.EndingFood - 1e-8,
                context + " ends with bounded food and biomass stocks");
            Check(Near(run.EndingCondition, 1, 1e-10) && Near(run.Damage, 0, 1e-10),
                context + " keeps stored land condition under every extraction rate");
            if (q == 1)
            {
                var tile = world.Tiles[TileId];
                var capacity = new EcologyCapacity(world, tile, new EcologyView(tile), 0);
                Check(Near(capacity.WildFood, 0),
                    context + " has zero natural food service");
                if (multiplier <= 1)
                    Check(tile.CurrentVegetation == VegetationTier.High,
                        context + " retains healthy raw forest potential");
            }
            Console.WriteLine("ISSUE25_HUMAN q=" + Number(q) + " multiplier=" + Number(multiplier)
                + " years=500 requested=" + Number(run.Requested) + " realized=" + Number(run.Realized)
                + " unmet=" + Number(run.Unmet) + " safeFlow=" + Number(run.SafeFlow)
                + " managedFlow=" + Number(run.ManagedFlow) + " damage=" + Number(run.Damage)
                + " condition=" + Number(run.EndingCondition) + " food=" + Number(run.EndingFood)
                + " biomass=" + Number(run.EndingBiomass) + " aboveSafeYears=" + run.YearsRealizedAboveSafe);
        }
    }

    private static void DamagedLandRecoveryAndPartialOperation()
    {
        foreach (double q in new[] { .6, 1.0 })
        foreach (double condition in new[] { .5, .75 })
        {
            var world = AgricultureFixture(q, condition, 1);
            world.Config.ConditionRecoveryPerYear = SimulationConfig.Small(1).ConditionRecoveryPerYear;
            var tile = world.Tiles[TileId];
            var initial = new EcologyCapacity(world, tile, new EcologyView(tile), 0);
            var pristine = AgricultureFixture(q, 1, 1);
            var pristineCapacity = new EcologyCapacity(pristine, pristine.Tiles[TileId],
                new EcologyView(pristine.Tiles[TileId]), 0);
            string context = "damaged land q=" + Number(q) + " condition=" + Number(condition);
            Check(initial.Biomass < pristineCapacity.Biomass && initial.ManagedFlow < pristineCapacity.ManagedFlow,
                context + " begins with lower biomass and managed production");
            var run = RunHumanYears(world, 500, 1, context);
            var recovered = new EcologyCapacity(world, tile, new EcologyView(tile), 0);
            Console.WriteLine("ISSUE25_RECOVERY q=" + Number(q) + " initialCondition=" + Number(condition)
                + " endingCondition=" + Number(run.EndingCondition) + " initialManaged=" + Number(initial.ManagedFlow)
                + " endingManaged=" + Number(recovered.ManagedFlow) + " damage=" + Number(run.Damage));
            Check(run.Damage <= 1e-10 && Near(run.EndingCondition, condition, 1e-12),
                context + " retains saved condition while finite stocks recover");
            Check(Near(recovered.Biomass, initial.Biomass, 1e-10)
                && Near(recovered.ManagedFlow, initial.ManagedFlow, 1e-10),
                context + " keeps condition-dependent capacity fixed");
        }

        var partial = AgricultureFixture(.8, 1, .5);
        var full = AgricultureFixture(.8, 1, 1);
        var partialTile = partial.Tiles[TileId];
        var partialInitial = new EcologyCapacity(partial, partialTile, new EcologyView(partialTile), 0);
        var fullInitial = new EcologyCapacity(full, full.Tiles[TileId], new EcologyView(full.Tiles[TileId]), 0);
        double partialSafe = EcologyMath.SustainableHarvestRate(partialInitial.Biomass, partialInitial.BiomassRate,
            partial.Config.SustainableProductionFraction, partialInitial.ManagedFlow);
        double fullSafe = EcologyMath.SustainableHarvestRate(fullInitial.Biomass, fullInitial.BiomassRate,
            full.Config.SustainableProductionFraction, fullInitial.ManagedFlow);
        double initialCapital = partialTile.CultivationCapital;
        Check(Near(partialInitial.ManagedFlow, .5 * fullInitial.ManagedFlow, 1e-10)
            && Near(partialSafe, fullSafe - .5 * fullInitial.ManagedFlow, 1e-10),
            "half operation halves managed flow and adds that half exactly once to safe flow");
        var partialRun = RunHumanYears(partial, 500, .75, "500-year half-operation cultivation");
        double expectedCapital = CultivationEconomy.Decay(partial.Config, initialCapital, .5, 500);
        Check(Near(partialTile.CultivationCapital, expectedCapital, 1e-9)
            && partialTile.CultivationCapital < initialCapital,
            "half operation follows existing cultivation-capital maintenance decay");
        Check(Near(partialRun.Damage, 0, 1e-10) && Near(partialRun.EndingCondition, 1, 1e-10),
            "half operation alone does not damage LandCondition");
        Console.WriteLine("ISSUE25_PARTIAL operation=0.5 years=500 initialManaged=" + Number(partialInitial.ManagedFlow)
            + " initialSafeRate=" + Number(partialSafe) + " endingQ=" + Number(DerivedQ(partial))
            + " condition=" + Number(partialRun.EndingCondition) + " damage=" + Number(partialRun.Damage));
    }

    private static void MixedHumanAndHerdClaims()
    {
        foreach (double multiplier in new[] { .9, 1.3 })
        {
            var forward = AgricultureFixture(.8, 1, 1);
            var reverse = AgricultureFixture(.8, 1, 1);
            var tile = forward.Tiles[TileId];
            var capacity = new EcologyCapacity(forward, tile, new EcologyView(tile), 0);
            double safeRate = EcologyMath.SustainableHarvestRate(capacity.Biomass, capacity.BiomassRate,
                forward.Config.SustainableProductionFraction, capacity.ManagedFlow);
            double safeFlow = safeRate * 30;
            var human = new Demand { Tile = TileId, Entity = 2, Kind = "Cultivation", Food = multiplier * safeFlow * .55 };
            var herd = new Demand { Tile = TileId, Entity = 3, Kind = "Herd", PotentialFood = multiplier * safeFlow * .45 };
            var a = EcologicalPressure.Integrate(forward, TileId, new[] { human, herd }, 0, 30);
            var b = EcologicalPressure.Integrate(reverse, TileId, new[] { herd, human }, 0, 30);
            double humanRealized = a.Transfers.Sum(t => t.Food), herdRealized = a.Transfers.Sum(t => t.PotentialFood);
            double total = humanRealized + herdRealized;
            double damage = a.Pressure.Sum(p => p.ConditionDamage);
            CheckBalance(a, "mixed human/herd " + Number(multiplier));
            Check(Near(safeRate, capacity.Biomass * capacity.BiomassRate *
                forward.Config.SustainableProductionFraction + capacity.ManagedFlow),
                "mixed consumers share a single managed-flow allowance");
            Check(humanRealized > 0 && herdRealized > 0 && Near(total, multiplier * safeFlow, 1e-7),
                "mixed consumers realize both claims against shared biomass");
            Check(Near(a.Balance.BiomassHarvest, total, 1e-7),
                "human and herd extraction debit one biomass budget");
            Check(Near(a.View.Food, b.View.Food) && Near(a.View.PotentialFood, b.View.PotentialFood)
                && Near(a.View.LandCondition, b.View.LandCondition)
                && Near(a.Balance.BiomassHarvest, b.Balance.BiomassHarvest)
                && Near(damage, b.Pressure.Sum(p => p.ConditionDamage)),
                "reordered mixed claim submission has the same ecological result");
            Check(Near(damage, 0, 1e-10) && Near(a.View.LandCondition, 1, 1e-10),
                "mixed extraction debits finite stocks without land-condition damage");
            if (multiplier > 1)
                Check(total > safeFlow, "above-safe mixed claims can still consume finite opening stock");
            Console.WriteLine("ISSUE25_MIXED multiplier=" + Number(multiplier) + " safeFlow=" + Number(safeFlow)
                + " human=" + Number(humanRealized) + " herd=" + Number(herdRealized)
                + " damage=" + Number(damage) + " condition=" + Number(a.View.LandCondition));
        }
    }

    private static WorldState LowFlowFixture()
    {
        var world = AgricultureFixture(.8, 1, 1);
        var tile = world.Tiles[TileId];
        for (int i = 0; i < tile.Seasons.Length; i++)
        {
            var season = tile.Seasons[i];
            season.MaxPotentialFood = 6;
            season.MaxFood = 5.5f;
            tile.Seasons[i] = season;
        }
        var initial = new EcologyCapacity(world, tile, new EcologyView(tile), 0);
        tile.Food = initial.Food * .8;
        tile.PotentialFood = initial.Biomass * .8;
        return world;
    }

    private static HumanRun RunLowFlowPartition(double partitionDays)
    {
        var world = LowFlowFixture();
        var tile = world.Tiles[TileId];
        var initial = new EcologyCapacity(world, tile, new EcologyView(tile), 0);
        double safeRate = EcologyMath.SustainableHarvestRate(initial.Biomass, initial.BiomassRate,
            world.Config.SustainableProductionFraction, initial.ManagedFlow);
        double requestRate = safeRate * 1.2;
        double horizon = 20 * world.Config.DaysPerYear;
        var run = new HumanRun { StartingFood = tile.Food, StartingBiomass = tile.PotentialFood };
        while (world.Days < horizon - 1e-8)
        {
            double days = Math.Min(partitionDays, horizon - world.Days);
            double request = requestRate * days;
            var result = EcologicalPressure.Integrate(world, TileId,
                new[] { new Demand { Tile = TileId, Entity = 1, Kind = "Cultivation", Food = request } },
                tile.LastEcologyUpdateDays, days);
            CheckBalance(result, "low-flow partition " + Number(partitionDays));
            run.Requested += request;
            run.Realized += result.Transfers.Sum(t => t.Food);
            run.Damage += result.Pressure.Sum(p => p.ConditionDamage);
            Commit(world, result, world.Days + days);
        }
        run.EndingFood = tile.Food;
        run.EndingBiomass = tile.PotentialFood;
        run.EndingCondition = tile.LandCondition;
        return run;
    }

    private static void DamageRateHorizonCalibration()
    {
        var fixture = LowFlowFixture();
        var capacity = new EcologyCapacity(fixture, fixture.Tiles[TileId],
            new EcologyView(fixture.Tiles[TileId]), 0);
        double safeRate = EcologyMath.SustainableHarvestRate(capacity.Biomass, capacity.BiomassRate,
            fixture.Config.SustainableProductionFraction, capacity.ManagedFlow);
        Check(safeRate * 7 < 1 && safeRate * fixture.Config.BaseDays > 1,
            "low-flow fixture crosses the old one-unit-per-segment floor between weekly and seasonal partitions");
        var full = RunLowFlowPartition(20 * 365);
        foreach (double days in new[] { 365.0, 365.0 / 4, 7.0 })
        {
            var split = RunLowFlowPartition(days);
            Check(Near(split.Requested, full.Requested, 1e-10)
                && split.Realized >= 0 && split.Realized <= split.Requested + 1e-8
                && Near(split.Damage, 0, 1e-12) && Near(split.EndingCondition, 1, 1e-12)
                && split.EndingFood >= 0 && split.EndingBiomass >= split.EndingFood,
                "low-flow calibration remains bounded at " + Number(days) + " days");
            Console.WriteLine("ISSUE25_LOW_FLOW_PARTITION days=" + Number(days)
                + " extractionDelta=" + Number(split.Realized - full.Realized)
                + " damageDelta=" + Number(split.Damage - full.Damage)
                + " conditionDelta=" + Number(split.EndingCondition - full.EndingCondition)
                + " foodDelta=" + Number(split.EndingFood - full.EndingFood)
                + " biomassDelta=" + Number(split.EndingBiomass - full.EndingBiomass));
        }

        var zero = AgricultureFixture(0, 1, 0);
        var zeroTile = zero.Tiles[TileId];
        for (int i = 0; i < zeroTile.Seasons.Length; i++)
        {
            var season = zeroTile.Seasons[i];
            season.FoodRecoveryPerDay = 0;
            season.BiomassRecoveryPerDay = 0;
            zeroTile.Seasons[i] = season;
        }
        var noClaim = EcologicalPressure.Integrate(zero, TileId, Array.Empty<Demand>(), 0, 7);
        Check(Near(noClaim.View.LandCondition, 1), "zero safe flow with zero extraction causes no damage");
        var stockClaim = EcologicalPressure.Integrate(zero, TileId,
            new[] { new Demand { Tile = TileId, Entity = 1, Kind = "Gathering", Food = 10 } }, 0, 7);
        Check(stockClaim.Transfers.Sum(t => t.Food) > 0
            && stockClaim.Transfers.Sum(t => t.Food) <= stockClaim.Balance.StartingFood + 1e-8
            && stockClaim.View.LandCondition == 1 && stockClaim.Pressure.All(p => p.ConditionDamage == 0),
            "zero renewal still allows only opening-stock extraction without condition damage");
    }

    private static void AddRecoveringWood(WorldState world)
    {
        int count = world.Tiles.Length;
        world.Resources = new WorldResourceState
        {
            MaximumQuantity = 100000,
            CatalogHash = ResourceCatalog.Hash,
            ProfileHash = ResourceCatalog.Hash,
            ConfigurationHash = ResourceCatalog.Hash,
            Provinces = Array.Empty<GeologicalProvince>(),
            Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count],
            Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05
        };
        world.MaterialSources.Add(new MaterialSourceState
        {
            Id = "surface:" + TileId + ":" + (int)MaterialId.Wood,
            Material = MaterialId.Wood,
            Initial = InitialWoodCapacity,
            Remaining = 100,
            LastDays = 0
        });
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Csv(SweepRow row) => string.Join(",", new[]
    {
        row.Vegetation.ToString(), Number(row.Q), Number(row.Condition), Number(row.OperatingFraction), Number(row.HarvestMultiplier),
        Number(row.BiomassCapacity), Number(row.WildFoodEcologicalCapacity), Number(row.WildAccessibleCapacity), Number(row.WildAccessibleFlow), Number(row.FoodCapacity),
        Number(row.ManagedFlow), Number(row.SustainableHarvestRate), Number(row.SustainableHarvestFlow), Number(row.RequestedHarvest),
        Number(row.RealizedHarvest), Number(row.LandConditionChange), Number(row.Retention), Number(row.EffectiveNaturalVegetationService),
        Number(row.WoodCapacity), Number(row.EndingFood), Number(row.EndingPotentialFood), Number(row.EndingBiomassCapacity)
    });

    private static string WriteCsv(List<SweepRow> rows, string[] args)
    {
        string explicitPath = args.FirstOrDefault(arg => arg.StartsWith("--sweep-output=", StringComparison.Ordinal));
        string path = explicitPath == null
            ? Path.Combine(AppContext.BaseDirectory, "Issue25EcologySweep.csv")
            : explicitPath.Substring("--sweep-output=".Length);
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var csv = new StringBuilder();
        csv.AppendLine("Vegetation,q,LandCondition,operatingFraction,harvestMultiplier,BiomassCapacity,WildFoodEcologicalCapacity,WildNaturalAccessibleCapacity,WildNaturalFlowPerDay,AccessibleFoodCapacity,ManagedFlowPerDay,SustainableHarvestRatePerDay,SustainableHarvestFlowFor30Days,RequestedHarvest,RealizedHarvest,LandConditionChange,VegetationRetention,EffectiveNaturalVegetationService,WoodCapacity,EndingFood,EndingPotentialFood,EndingBiomassCapacity");
        foreach (var row in rows) csv.AppendLine(Csv(row));
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
        return Path.GetFullPath(path);
    }

    public static int Run(string[] args = null)
    {
        checks = 0;
        args = args ?? Array.Empty<string>();
        LockedRetentionValues();
        var rows = Sweep();
        PaleoEquivalenceAndHealthyIntensiveField();
        ExpansionClearingIsCoverOnly();
        WoodClampAndAbandonmentRecovery();
        DurationPartitionAndSaveReplay();
        DamageRateHorizonCalibration();
        LongDurationHumanAgriculture();
        DamagedLandRecoveryAndPartialOperation();
        MixedHumanAndHerdClaims();
        string csv = WriteCsv(rows, args);
        Console.WriteLine("ISSUE25_SWEEP rows=" + rows.Count + " checks=" + checks + " grid=Low/Medium/High x 7 q x 3 condition x 3 operation x 3 harvest");
        Console.WriteLine("ISSUE25_SWEEP_CSV " + csv);
        Console.WriteLine("PASS: " + checks + " issue #25 ecology assertions");
        return checks;
    }
}
