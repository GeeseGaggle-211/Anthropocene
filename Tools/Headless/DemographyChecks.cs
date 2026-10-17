using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class DemographyChecks
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    private static void Near(double actual, double expected, double tolerance, string message)
        => Check(Math.Abs(actual - expected) <= tolerance, message + ": " + actual.ToString("G17") + " != " + expected.ToString("G17"));

    public static int Run()
    {
        assertions = 0;
        // The v1.1 kernel reference: surplus-funded births (v1.2) are verified separately in ClothingProbe/KernelMixedChecks.
        var cfg = new SimulationConfig { DemographicModel = DemographicModel.PhysicalMortalityV1, QoLMortalityEnabled = false, SurplusFundedBirths = false };
        cfg.Validate();

        var anchor = PhysicalDemography.Rates(cfg, 1000, 1, 1, 1000);
        Near(anchor.Birth, .045, 1e-14, "Full nutrition uses the exact v1.1 birth rate");
        Near(anchor.Exposure, cfg.ExposureMortalityRate * .1, 1e-14, "Full clothing and housing leave ten percent exposure");
        Near(anchor.Crowding, cfg.CrowdingMortalityRateAt1000, 1e-14, "Crowding anchor at one thousand");
        Near(anchor.Disease, cfg.DiseaseMortalityRateAt1000, 1e-14, "Disease anchor at one thousand");
        Near(anchor.Net, 0, 2e-14, "One-thousand fully protected equilibrium calibration");
        Near(anchor.Crowding / (anchor.Crowding + anchor.Disease), .75, 1e-13, "One-thousand crowding share");
        var large = PhysicalDemography.Rates(cfg, 100000, 1, 1, 100000);
        Near(large.Crowding / (large.Crowding + large.Disease), .5, 2e-13, "One-hundred-thousand crowding/disease parity");
        var exposed = PhysicalDemography.Rates(cfg, 0, 1, 0, 0);
        Near(exposed.Exposure, cfg.ExposureMortalityRate, 1e-14, "Unprotected exposure anchor");
        Near(PhysicalDemography.Rates(cfg, 1000, .4, 1, 1000).Birth, .018, 1e-14, "Nutrition directly scales fertility");
        Near(PhysicalDemography.Rates(cfg, 24, 1, 0, 0).Net, cfg.AnnualHumanGrowthRate, 2e-14,
            "The uncovered 24-person opening preserves the calibrated legacy growth anchor");

        var equilibrium = PhysicalDemography.AdvanceFixed(cfg, 1000, 20, 1, 1, 1000);
        Near(equilibrium.FinalPopulation, 1000, 1e-8, "Equilibrium remains stationary");
        Check(equilibrium.Births > 800 && equilibrium.Deaths > 800, "Equilibrium reports gross turnover");
        Near(equilibrium.Births, equilibrium.Deaths, 1e-7, "Equilibrium gross births equal deaths");
        Near(equilibrium.ConservationError, 0, 1e-9, "Equilibrium accounting conserves people");
        Check(equilibrium.HasPositiveEquilibrium && Math.Abs(equilibrium.EquilibriumPopulation - 1000) < 1e-7, "Exact equilibrium root is exposed");

        var aboveEquilibrium = PhysicalDemography.AdvanceFixed(cfg, 1200, 20, 1, 1, 1200);
        Check(aboveEquilibrium.FinalPopulation < 1200 && aboveEquilibrium.FinalPopulation > 1000
            && aboveEquilibrium.HasPositiveEquilibrium
            && Math.Abs(aboveEquilibrium.EquilibriumPopulation - 1000) < 1e-7,
            "A viable above-equilibrium settlement has negative instantaneous net growth but declines toward a positive root");

        var zero = PhysicalDemography.AdvanceFixed(cfg, 12, 0, .2, .3, 0);
        Near(zero.FinalPopulation, 12, 0, "Zero-time propagation is identity");
        foreach (double small in new[] { .25, 6.0, 12.0, 24.0 })
        {
            var result = PhysicalDemography.AdvanceFixed(cfg, small, 20, .6, .4, small * .5);
            Check(result.FinalPopulation > 0 && !double.IsNaN(result.FinalPopulation) && !double.IsInfinity(result.FinalPopulation),
                "Small and sub-person continuous populations stay finite and positive at N=" + small);
            Near(result.FinalPopulation - result.InitialPopulation, result.Births - result.Deaths, 1e-9,
                "Small-population gross conservation at N=" + small);
        }
        var microscopic = PhysicalDemography.AdvanceFixed(cfg, 1e-20, 1, 1, 1, 1e-20, 0, false);
        Check(microscopic.FinalPopulation > 0 && microscopic.FinalPopulation < 1e-18,
            "The arbitrary-duration API preserves a representable microscopic population instead of flooring it");
        Near(microscopic.FinalPopulation - microscopic.InitialPopulation,
            microscopic.Births - microscopic.Deaths, 1e-28, "Microscopic gross conservation");

        var unfundedMicroscopic = PhysicalDemography.AdvanceBudget(cfg, 1e-15, 20, 0, 0, 0, 0, false);
        Check(unfundedMicroscopic.Births == 0 && unfundedMicroscopic.Nutrition == 0
            && unfundedMicroscopic.ClothingCoverage == 0 && unfundedMicroscopic.ExposureDeaths > 0,
            "A microscopic zero budget receives no free food, clothing or births and integrates unprotected exposure");
        var unfundedCoupled = PhysicalDemography.AdvanceCoupledBudget(cfg, new[]
        {
            new DemographicServicePoolInput(new[] { new DemographicCohortInput(1e-15, 0) }, 0, 0, 0),
            new DemographicServicePoolInput(new[] { new DemographicCohortInput(1e-15, 1) }, 0, 0, 0)
        }, 20);
        Check(!unfundedCoupled.UsedExactScalarReduction && unfundedCoupled.Total.Births == 0
            && unfundedCoupled.Pools.All(p => p.Nutrition == 0 && p.ClothingCoverage == 0 && p.ExposureDeaths > 0),
            "The heterogeneous microscopic coupled path also receives no free physical service");

        var whole = PhysicalDemography.AdvanceFixed(cfg, 275, 31.75, .83, .62, 190, 4);
        var first = PhysicalDemography.AdvanceFixed(cfg, 275, 7.125, .83, .62, 190, 4);
        var second = PhysicalDemography.AdvanceFixed(cfg, first.FinalPopulation, 24.625, .83, .62, 190, 11.125);
        Near(second.FinalPopulation, whole.FinalPopulation, 2e-7 * Math.Max(1, whole.FinalPopulation), "Arbitrary-duration composition");
        Near(whole.FinalPopulation - whole.InitialPopulation, whole.Births - whole.Deaths, 1e-8, "Fixed-service gross conservation");
        var twenty = PhysicalDemography.AdvanceFixed(cfg, 375, 20, .83, .62, 190, 0, false);
        var tenA = PhysicalDemography.AdvanceFixed(cfg, 375, 10, .83, .62, 190, 0, false);
        var tenB = PhysicalDemography.AdvanceFixed(cfg, tenA.FinalPopulation, 10, .83, .62, 190, 0, false);
        double fivePopulation = 375;
        for (int i = 0; i < 4; i++) fivePopulation = PhysicalDemography.AdvanceFixed(cfg, fivePopulation, 5, .83, .62, 190, 0, false).FinalPopulation;
        Near(tenB.FinalPopulation, twenty.FinalPopulation, 2e-7 * Math.Max(1, twenty.FinalPopulation), "Twenty years equals two ten-year calls");
        Near(fivePopulation, twenty.FinalPopulation, 2e-7 * Math.Max(1, twenty.FinalPopulation), "Twenty years equals four five-year calls");

        double food = PhysicalDemography.FoodPerPersonYear(cfg) * 400 * 20 * .72;
        double textile = PhysicalDemography.ClothingPerPersonYear(cfg) * 400 * 20 * .55;
        var finite = PhysicalDemography.AdvanceBudget(cfg, 400, 20, food, textile, 260);
        Check(finite.Nutrition > 0 && finite.Nutrition < 1 && finite.ClothingCoverage > 0 && finite.ClothingCoverage < 1, "Finite budgets close to partial physical coverage");
        Near(finite.FoodConsumed, PhysicalDemography.FoodPerPersonYear(cfg) * finite.PersonYears * finite.Nutrition, 1e-7, "Food person-year closure");
        Near(finite.ClothingConsumed, PhysicalDemography.ClothingPerPersonYear(cfg) * finite.PersonYears * finite.ClothingCoverage, 1e-7, "Textile person-year closure");
        Near(finite.FinalPopulation - finite.InitialPopulation, finite.Births - finite.Deaths, 1e-8, "Budget-service gross conservation");

        var beforeGrace = PhysicalDemography.AdvanceFixed(cfg, 200, 20, .5, 1, 200, 0);
        var afterGrace = PhysicalDemography.AdvanceFixed(cfg, 200, 20, .5, 1, 200, PhysicalDemography.StarvationGraceYears(cfg));
        Near(beforeGrace.LegacyStarvationDeaths, 0, 1e-12, "No compatibility starvation before forty physical years");
        Check(afterGrace.LegacyStarvationDeaths > 0, "Compatibility starvation begins after forty physical years");
        var reset = PhysicalDemography.AdvanceFixed(cfg, 200, 20, 1, 1, 200, 100);
        Near(reset.LegacyStarvationDeaths, 0, 1e-12, "Full nutrition immediately resets starvation exposure");

        var projectedWorld = PhysicalWorld(.8); var projectedCamp = projectedWorld.Camps.Single();
        projectedCamp.FoodDeficitYears = PhysicalDemography.StarvationGraceYears(projectedWorld.Config);
        var projected = PhysicalDemography.ProjectedSettlementRates(projectedWorld, projectedCamp,
            projectedCamp.Population, .5, 1);
        Check(projected.LegacyStarvation > 0,
            "Settlement mortality projections include the active starvation cause after physical grace");
        var beforeProjectedGrace = PhysicalDemography.ProjectedSettlementRates(projectedWorld, projectedCamp,
            projectedCamp.Population, .5, 1, PhysicalDemography.StarvationGraceYears(projectedWorld.Config) - 1e-6);
        var atProjectedGrace = PhysicalDemography.ProjectedSettlementRates(projectedWorld, projectedCamp,
            projectedCamp.Population, .5, 1, PhysicalDemography.StarvationGraceYears(projectedWorld.Config));
        Check(beforeProjectedGrace.LegacyStarvation == 0 && atProjectedGrace.LegacyStarvation > 0,
            "Projected starvation switches at the explicit physical grace boundary");

        foreach (double start in new[] { 25.0, 1000.0, 100000.0 })
        foreach (double duration in new[] { .25, 20.0, 73.25 })
        {
            double f = start > 1000 ? .91 : .74, c = start < 1000 ? .35 : .88, housing = start * .67;
            var native = PhysicalDemography.AdvanceFixed(cfg, start, duration, f, c, housing, 0, false);
            double reference = Reference(cfg, start, duration, f, c, housing);
            Near(native.FinalPopulation, reference, 8e-4 * Math.Max(1, reference), "Native solver agrees with RK4 reference at N=" + start + ", T=" + duration);
        }
        NumericalReferenceMatrix(cfg);
        FiniteBudgetReferenceMatrix(cfg);
        MicroscopicDeclineAndNumericalLimits(cfg);
        CoupledDensityAndCohortChecks(cfg);
        PlanningSolverAgreement(cfg);

        ResolutionAndReplay();
        IncompleteReserveDoesNotGateBirthService();
        DispatchAndReturn();
        ReconnectionPreservesDeficitHistory();
        ConnectedCohortIntegerAttribution();
        ConnectedSingletonPartitionRounding();
        LegacyDispatchCompatibility();
        DetachedCarryingAfterDeaths();
        IntegerExtinction();
        ExplicitMigration();

        Console.WriteLine("PASS: " + assertions + " physical demography assertions");
        return assertions;
    }

    // The Elder's planning solver (PlanningBudget / SurplusBudget fast) agrees with the authoritative kernel on the
    // quantities the kernel split scores, across fed, food-bound and short budgets, clothing and housing coverage,
    // starvation history and density offsets. Called with explicit arguments, never through the process-wide flag.
    private static void PlanningSolverAgreement(SimulationConfig cfg)
    {
        double years = 20, foodRate = PhysicalDemography.FoodPerPersonYear(cfg), clothRate = PhysicalDemography.ClothingPerPersonYear(cfg);
        double worstPy = 0, worstCoverage = 0;
        foreach (double population in new[] { 12.0, 180, 640, 2400 })
        foreach (double foodShare in new[] { .55, .9, 1.05, 1.4, 3 })
        foreach (double clothShare in new[] { 0, .4, 1.2 })
        foreach (double housingShare in new[] { 0, .8, 1.6 })
        foreach (double deficit in new[] { 0.0, 60 })
        foreach (double offset in new[] { 0.0, 300 })
        {
            double basis = population * years;
            double food = foodShare * foodRate * basis, cloth = clothShare * clothRate * basis, housing = housingShare * population;
            for (int surplus = 0; surplus < 2; surplus++)
            {
                var exact = surplus == 1
                    ? PhysicalDemography.SurplusBudget(cfg, population, years, food, cloth, housing, deficit, true, offset, out double g0)
                    : PhysicalDemography.AdvanceBudget(cfg, population, years, food, cloth, housing, deficit, true, offset);
                var fast = surplus == 1
                    ? PhysicalDemography.SurplusBudget(cfg, population, years, food, cloth, housing, deficit, true, offset, out double g1, true)
                    : PhysicalDemography.PlanningBudget(cfg, population, years, food, cloth, housing, deficit, true, offset);
                string at = " (N=" + population + " food=" + foodShare + " cloth=" + clothShare + " housing=" + housingShare + " deficit=" + deficit + " offset=" + offset + " surplus=" + surplus + ")";
                double py = Math.Abs(fast.PersonYears - exact.PersonYears) / Math.Max(1, exact.PersonYears);
                double coverage = Math.Max(Math.Abs(fast.Nutrition - exact.Nutrition), Math.Max(Math.Abs(fast.ClothingCoverage - exact.ClothingCoverage), Math.Abs(fast.AverageHousingCoverage - exact.AverageHousingCoverage)));
                worstPy = Math.Max(worstPy, py); worstCoverage = Math.Max(worstCoverage, coverage);
                Check(py <= 1e-6, "Planning solver person-years agree" + at + ": " + py.ToString("G3"));
                Check(coverage <= 1e-6, "Planning solver nutrition/clothing/housing agree" + at + ": " + coverage.ToString("G3"));
                Check(Math.Abs(fast.FinalPopulation - exact.FinalPopulation) <= 1e-6 * Math.Max(1, exact.FinalPopulation), "Planning solver final population agrees" + at);
                Check(fast.ServiceIterations <= exact.ServiceIterations, "Planning solver needs no more interval solves" + at);
            }
        }
        Console.WriteLine("PLANNING_SOLVER worstRelativePersonYears=" + worstPy.ToString("G3") + " worstCoverage=" + worstCoverage.ToString("G3"));
    }

    private static WorldState PhysicalWorld(double qol)
    {
        var cfg = SimulationConfig.Small(1);
        cfg.FissionEnabled = false; cfg.ImmigrationEnabled = false; cfg.TradeEnabled = false;
        cfg.EconomicAllocation = EconomicAllocationStrategy.Legacy;
        var world = new WorldGenerator().Generate(739, cfg);
        var camp = world.Camps.Single();
        camp.QoL = qol; camp.PlantFood = 100000; camp.AnimalFood = camp.AquaticFood = 0;
        camp.Inventory.Cloth = 10000; camp.Inventory.AnimalFabric = 0;
        camp.DeficitTurns = 7; // obsolete recovery state must not prohibit gross births
        world.FixedHousing.Add(new FixedHousingStock { CampId = camp.Id, Tile = camp.Tile, Units = 10000, LastUpdatedDays = world.Days });
        NeolithicProduction.FixedHousingChanged(world);
        return world;
    }

    private static void ResolutionAndReplay()
    {
        var low = PhysicalWorld(.05);
        var high = SaveCodec.Load(SaveCodec.Save(low)); high.Camps.Single().QoL = .99;
        var scaled = SaveCodec.Load(SaveCodec.Save(low)); scaled.Camps.Single().DemographicGrowthMultiplier = .1;
        var replay = SaveCodec.Load(SaveCodec.Save(low));
        var a = new TurnResolver().Resolve(low, new Dictionary<int, List<Order>>());
        var b = new TurnResolver().Resolve(high, new Dictionary<int, List<Order>>());
        var multiplier = new TurnResolver().Resolve(scaled, new Dictionary<int, List<Order>>());
        var c = new TurnResolver().Resolve(replay, new Dictionary<int, List<Order>>());
        var da = a.Demographics.Single(); var db = b.Demographics.Single();
        Check(da.Model == DemographicModel.PhysicalMortalityV1 && da.GrossBirths > 0 && da.GrossDeaths > 0, "A physical turn reports gross births and deaths even with obsolete recovery state");
        Check(da.StartingPopulation + da.GrossBirths - da.GrossDeaths == da.EndingPopulation, "Physical turn integer population conservation");
        Check(a.Growth == da.GrossBirths && a.Deaths == da.GrossDeaths, "Turn counters are gross demographic events");
        Near(da.ContinuousDeaths, da.ExposureDeaths + da.CrowdingDeaths + da.DiseaseDeaths + da.StarvationDeaths + da.AdditionalDeaths, 1e-8, "Cause-specific deaths reconcile to gross continuous deaths");
        Near(da.ContinuousBirths - da.ContinuousDeaths, da.EndingPopulation + low.Camps.Single().GrowthRemainder - low.Camps.Single().DeathRemainder - da.StartingPopulation, 1e-7, "Integer endpoint plus saved remainders equals the continuous endpoint");
        Near(da.ContinuousBirths, db.ContinuousBirths, 1e-9, "Composite QoL does not change physical births");
        Near(da.ContinuousDeaths, db.ContinuousDeaths, 1e-9, "Composite QoL does not change physical deaths");
        Near(da.ContinuousBirths, multiplier.Demographics.Single().ContinuousBirths, 1e-9,
            "The legacy demographic growth multiplier cannot alter the physical birth law");
        Check(SaveCodec.Fingerprint(low) == SaveCodec.Fingerprint(replay), "Physical save/replay continuation is deterministic");
        Check(a.SettlementWelfare.Count == 1 && a.LineageWelfare.Count == 1 && !a.SettlementWelfare[0].TransferAffected, "Local and lineage welfare diagnostics are separate and transfer-aware");
        var observed = new AgentObservationBuilder().Build(low, low.Camps.Single().Player);
        var settlement = observed.Settlements.Single();
        Check(AgentObservation.SchemaVersion == 22 && settlement.EffectiveMortalityRate > 0 && settlement.Nutrition > 0 && settlement.ClothingCoverage > 0, "Schema 22 exposes owner-only physical diagnostic rates and coverage");
    }

    private static void IntegerExtinction()
    {
        var world = PhysicalWorld(.8); var camp = world.Camps.Single();
        camp.Population = 1; camp.Workforce.Normalize(1);
        camp.GrowthRemainder = camp.DeathRemainder = 0;
        camp.PlantFood = camp.AnimalFood = camp.AquaticFood = 0;
        camp.Inventory.Cloth = camp.Inventory.AnimalFabric = camp.Inventory.LightHousingUnits = 0;
        camp.FoodDeficitYears = PhysicalDemography.StarvationGraceYears(world.Config);
        camp.DeficitTurns = world.Config.StarvationGraceTurns;
        world.FixedHousing.Clear(); NeolithicProduction.FixedHousingChanged(world);
        foreach (var tile in world.Tiles)
        {
            tile.Food = tile.PotentialFood = 0;
            for (int i = 0; i < tile.Seasons.Length; i++)
            {
                var season = tile.Seasons[i];
                season.MaxFood = season.MaxPotentialFood = 0;
                season.FoodRecoveryPerDay = season.BiomassRecoveryPerDay = 0;
                tile.Seasons[i] = season;
            }
        }
        var record = new TurnResolver().Resolve(world, new Dictionary<int, List<Order>>());
        var report = record.Demographics.Single();
        Check(camp.Population == 0 && report.GrossBirths == 0 && report.GrossDeaths == 1
            && report.StartingPopulation + report.GrossBirths - report.GrossDeaths == report.EndingPopulation,
            "A physically declining singleton reaches a conserved integer extinction instead of an immortal fractional ledger");
        Check(report.IntegerExtinction && report.IntegerExtinctionResidual > 0 && report.IntegerExtinctionResidual < .5
            && report.ExposureDeaths > 0 && report.StarvationDeaths > 0,
            "Integer extinction reports its sub-person residual and the physical causes that drove it");
    }

    private static void ExplicitMigration()
    {
        var world = PhysicalWorld(.8); var camp = world.Camps.Single();
        world.Config.DemographicModel = DemographicModel.LegacyQoLNetGrowth;
        camp.DeficitTurns = 3; camp.FoodDeficitYears = 0;
        const string sourceCommit = "86cba712dfd76673bd9637052543bde85f810e0b";
        var migration = DemographicMigration.ToPhysicalV1(world, "controlled headless compatibility check", sourceCommit);
        Check(world.Config.DemographicModel == DemographicModel.PhysicalMortalityV1 && migration.From == DemographicModel.LegacyQoLNetGrowth && migration.To == DemographicModel.PhysicalMortalityV1, "Legacy migration is explicit and records model provenance");
        Near(camp.FoodDeficitYears, 3 * world.Config.Neolithic.ReferenceTurnYears, 1e-12, "Explicit migration converts deficit turns to physical years");
        var restored = SaveCodec.Load(SaveCodec.Save(world));
        Check(restored.DemographicMigrations.Count == 1 && restored.DemographicMigrations[0].Reason == migration.Reason
            && restored.DemographicMigrations[0].SourceCommit == sourceCommit, "Demographic migration provenance survives save/load");
        bool rejected = false; try { DemographicMigration.ToPhysicalV1(world, "duplicate", sourceCommit); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "A demographic migration cannot silently run twice");
        var invalid = PhysicalWorld(.8); invalid.Config.DemographicModel = DemographicModel.LegacyQoLNetGrowth;
        bool missingReason = false, missingSource = false;
        try { DemographicMigration.ToPhysicalV1(invalid, " ", sourceCommit); } catch (ArgumentException) { missingReason = true; }
        try { DemographicMigration.ToPhysicalV1(invalid, "controlled check", " "); } catch (ArgumentException) { missingSource = true; }
        Check(missingReason && missingSource && invalid.Config.DemographicModel == DemographicModel.LegacyQoLNetGrowth,
            "Explicit migration rejects missing reason/source-commit provenance without mutating the world");
    }

    private static void DispatchAndReturn()
    {
        var dispatched = PhysicalWorld(.8); var control = SaveCodec.Load(SaveCodec.Save(dispatched));
        var camp = dispatched.Camps.Single(); camp.Population = 100; camp.GrowthRemainder = .8; camp.DeathRemainder = .6;
        var controlCamp = control.Camps.Single(); controlCamp.Population = 100; controlCamp.GrowthRemainder = .8; controlCamp.DeathRemainder = .6;
        var resolver = new TurnResolver();
        var dispatchOrders = new Dictionary<int, List<Order>>
        {
            [camp.Player] = new List<Order> { new Order(OrderKind.Dispatch, camp.Id, population: 20) }
        };
        var splitTurn = resolver.Resolve(dispatched, dispatchOrders);
        var controlTurn = resolver.Resolve(control, new Dictionary<int, List<Order>>());
        var splitReport = splitTurn.Demographics.Single(); var controlReport = controlTurn.Demographics.Single();
        Check(dispatched.Hunters.Count == 1 && dispatched.LinkedPopulation(camp) == control.LinkedPopulation(controlCamp),
            "dispatch keeps every person in exactly one physical cohort");
        Near(splitReport.ContinuousBirths, controlReport.ContinuousBirths, 1e-8, "dispatch does not manufacture gross births");
        Near(splitReport.ContinuousDeaths, controlReport.ContinuousDeaths, 1e-8, "dispatch does not evade physical mortality");
        Near(camp.GrowthRemainder + dispatched.Hunters.Sum(h => h.GrowthRemainder), controlCamp.GrowthRemainder, 1e-8,
            "dispatch conserves gross-birth progress");
        Near(camp.DeathRemainder + dispatched.Hunters.Sum(h => h.DeathRemainder), controlCamp.DeathRemainder, 1e-8,
            "dispatch conserves gross-death progress");

        int hunter = dispatched.Hunters.Single().Id;
        var returnOrders = new Dictionary<int, List<Order>>
        {
            [camp.Player] = new List<Order> { new Order(OrderKind.Reintegrate, hunter, camp.Id) }
        };
        var returnTurn = resolver.Resolve(dispatched, returnOrders);
        var secondControl = resolver.Resolve(control, new Dictionary<int, List<Order>>());
        Check(dispatched.Hunters.Count == 0 && camp.Population == controlCamp.Population,
            "reintegrating a physical cohort conserves the integer endpoint");
        Near(returnTurn.Demographics.Single().ContinuousBirths, secondControl.Demographics.Single().ContinuousBirths, 1e-8,
            "reintegrating does not duplicate birth service");
        Near(returnTurn.Demographics.Single().ContinuousDeaths, secondControl.Demographics.Single().ContinuousDeaths, 1e-8,
            "reintegrating does not duplicate mortality");
    }

    private static void IncompleteReserveDoesNotGateBirthService()
    {
        var world = PhysicalWorld(.8); var camp = world.Camps.Single();
        camp.Population = 24; camp.Workforce.Normalize(camp.Population);
        foreach (var tile in world.Tiles)
        {
            tile.Food = tile.PotentialFood = 0;
            for (int season = 0; season < tile.Seasons.Length; season++)
            {
                var profile = tile.Seasons[season];
                profile.MaxFood = profile.MaxPotentialFood = 0;
                profile.FoodRecoveryPerDay = profile.BiomassRecoveryPerDay = 0;
                tile.Seasons[season] = profile;
            }
        }
        double intervals = world.Config.GenerationIntervals;
        double years = world.Config.TurnDays / world.Config.DaysPerYear;
        double currentNeed = camp.Population * world.Config.Consumption * intervals;
        TurnResolver.FoodBucketBasis(world, camp, intervals, years, currentNeed,
            out double reserveTarget, out double maximumGrowthFood);
        double partialGrowthFood = maximumGrowthFood * .5;
        Check(partialGrowthFood > 0 && partialGrowthFood < reserveTarget,
            "Incomplete-reserve fixture has food for newborn person-years but not a full operating reserve");
        camp.PlantFood = currentNeed + partialGrowthFood; camp.AnimalFood = camp.AquaticFood = 0;

        double forecast = TurnResolver.PlanningResidentPopulation(world, camp, 0);
        var record = new TurnResolver().Resolve(world, new Dictionary<int, List<Order>>());
        var report = record.Demographics.Single();
        Check(report.InitialReserves + 1e-8 < currentNeed + reserveTarget,
            "The physical fixture begins below the mandatory legacy reserve threshold");
        Check(report.FoodConsumed > currentNeed + .25 * partialGrowthFood && report.GrossBirths > 0,
            "Physical demography consumes available newborn service and records births before the reserve is full");
        Check(Math.Abs(report.EndingPopulation - forecast) <= 1,
            "Incomplete-reserve physical population forecast matches resolution (forecast="
            + forecast.ToString("G17") + ", actual=" + report.EndingPopulation + ")");
    }

    private static void ReconnectionPreservesDeficitHistory()
    {
        WorldState Returning(double campAge, double hunterAge)
        {
            var world = PhysicalWorld(.8); var camp = world.Camps.Single();
            camp.Population = 100; camp.Workforce.Normalize(camp.Population);
            camp.FoodDeficitYears = campAge; camp.DeficitTurns = campAge > 0 ? 4 : 0;
            var hunter = new Hunter
            {
                Id = world.NextId++, Player = camp.Player, SocietyId = camp.SocietyId,
                SourceCamp = camp.Id, Tile = camp.Tile, Population = 100,
                FoodDeficitYears = hunterAge, DeficitTurns = hunterAge > 0 ? 4 : 0
            };
            world.Hunters.Add(hunter);
            double currentNeed = 200 * world.Config.Consumption * world.Config.GenerationIntervals;
            camp.PlantFood = currentNeed * .5; camp.AnimalFood = camp.AquaticFood = 0;
            foreach (var tile in world.Tiles)
            {
                tile.Food = tile.PotentialFood = 0;
                for (int season = 0; season < tile.Seasons.Length; season++)
                {
                    var profile = tile.Seasons[season];
                    profile.MaxFood = profile.MaxPotentialFood = 0;
                    profile.FoodRecoveryPerDay = profile.BiomassRecoveryPerDay = 0;
                    tile.Seasons[season] = profile;
                }
            }
            return world;
        }

        double grace = PhysicalDemography.StarvationGraceYears(PhysicalWorld(.8).Config);
        var olderHunter = Returning(0, 2 * grace);
        var olderCamp = Returning(2 * grace, 0);
        var fresh = Returning(0, 0);
        TurnRecord ResolveReturn(WorldState world)
        {
            var camp = world.Camps.Single(); var hunter = world.Hunters.Single();
            return new TurnResolver().Resolve(world, new Dictionary<int, List<Order>>
            {
                [camp.Player] = new List<Order> { new Order(OrderKind.Reintegrate, hunter.Id, camp.Id) }
            });
        }
        var hunterReport = ResolveReturn(olderHunter).Demographics.Single();
        var campReport = ResolveReturn(olderCamp).Demographics.Single();
        var freshReport = ResolveReturn(fresh).Demographics.Single();
        Check(hunterReport.Nutrition > 0 && hunterReport.Nutrition < 1,
            "Deficit-history reconnection fixture maintains partial nutrition");
        Check(hunterReport.StarvationDeaths > 0 && campReport.StarvationDeaths > 0 && freshReport.StarvationDeaths == 0,
            "Either side's returning deficit age reaches the physical grace boundary while fresh cohorts remain protected");
        Near(hunterReport.StarvationDeaths, campReport.StarvationDeaths, 1e-8,
            "Weighted reconnect history is symmetric between camp and returning hunter");
        Near(olderHunter.Camps.Single().FoodDeficitYears, olderCamp.Camps.Single().FoodDeficitYears, 1e-12,
            "Reintegration retains the same post-turn deficit age in both directions");
    }

    private static void LegacyDispatchCompatibility()
    {
        var world = PhysicalWorld(.8); world.Config.DemographicModel = DemographicModel.LegacyQoLNetGrowth;
        var camp = world.Camps.Single(); camp.Population = 100; camp.Workforce.Normalize(100);
        camp.GrowthRemainder = .8; camp.DeathRemainder = .6;
        camp.FoodDeficitYears = 17; camp.DeficitSeverity = .4; camp.DeficitTurns = 3;
        var resolver = new TurnResolver();
        resolver.Resolve(world, new Dictionary<int, List<Order>>
        {
            [camp.Player] = new List<Order> { new Order(OrderKind.Dispatch, camp.Id, population: 20) }
        });
        var hunter = world.Hunters.Single();
        Near(hunter.GrowthRemainder, 0, 0, "Legacy dispatch does not move camp growth progress into a hunter");
        Near(hunter.DeathRemainder, 0, 0, "Legacy dispatch does not move the unused physical death ledger");
        Near(hunter.FoodDeficitYears, 0, 0, "Legacy dispatch preserves pre-v1.1 deficit-year initialization");
        Near(hunter.DeficitSeverity, 0, 0, "Legacy dispatch preserves pre-v1.1 deficit-severity initialization");

        var clean = SaveCodec.Load(SaveCodec.Save(world));
        hunter.GrowthRemainder = .91; hunter.DeathRemainder = .73;
        hunter.FoodDeficitYears = 999; hunter.DeficitSeverity = 999;
        int cleanHunter = clean.Hunters.Single().Id;
        var poisonedOrders = new Dictionary<int, List<Order>>
        {
            [camp.Player] = new List<Order> { new Order(OrderKind.Reintegrate, hunter.Id, camp.Id) }
        };
        var cleanOrders = new Dictionary<int, List<Order>>
        {
            [clean.Camps.Single().Player] = new List<Order> { new Order(OrderKind.Reintegrate, cleanHunter, clean.Camps.Single().Id) }
        };
        resolver.Resolve(world, poisonedOrders);
        resolver.Resolve(clean, cleanOrders);
        Check(SaveCodec.Fingerprint(world) == SaveCodec.Fingerprint(clean),
            "Legacy reintegration ignores physical-only hunter ledgers and matches the pre-v1.1 state transition");
        Check(camp.GrowthRemainder >= 0 && camp.GrowthRemainder < 1,
            "Legacy reintegration cannot violate the camp growth-remainder invariant");
    }

    private static void ConnectedCohortIntegerAttribution()
    {
        var world = PhysicalWorld(.8); var camp = world.Camps.Single();
        camp.Population = 100; camp.Workforce.Normalize(camp.Population);
        camp.PlantFood = camp.AnimalFood = camp.AquaticFood = 0;
        camp.Inventory.Cloth = camp.Inventory.AnimalFabric = camp.Inventory.LightHousingUnits = 0;
        camp.FoodDeficitYears = 0; camp.DeficitTurns = 0; camp.DeficitSeverity = 0;
        world.FixedHousing.Clear(); NeolithicProduction.FixedHousingChanged(world);
        double grace = PhysicalDemography.StarvationGraceYears(world.Config);
        var hunter = new Hunter
        {
            Id = world.NextId++, Player = camp.Player, SocietyId = camp.SocietyId,
            SourceCamp = camp.Id, Tile = camp.Tile, Population = 100,
            FoodDeficitYears = grace + 1, DeficitTurns = world.Config.StarvationGraceTurns + 1
        };
        world.Hunters.Add(hunter);
        foreach (var tile in world.Tiles)
        {
            tile.Food = tile.PotentialFood = 0;
            for (int i = 0; i < tile.Seasons.Length; i++)
            {
                var season = tile.Seasons[i];
                season.MaxFood = season.MaxPotentialFood = 0;
                season.FoodRecoveryPerDay = season.BiomassRecoveryPerDay = 0;
                tile.Seasons[i] = season;
            }
        }
        double years = world.Config.TurnDays / world.Config.DaysPerYear;
        CoupledDemographicResult expected = PhysicalDemography.AdvanceCoupledBudget(world.Config,
            new[] { new DemographicServicePoolInput(new[]
            {
                new DemographicCohortInput(100, 0),
                new DemographicCohortInput(100, grace + 1)
            }, 0, 0, 0) }, years);
        var record = new TurnResolver().Resolve(world, new Dictionary<int, List<Order>>());
        double settlementEndpoint = camp.Population + hunter.Population
            + camp.GrowthRemainder - camp.DeathRemainder
            + hunter.GrowthRemainder - hunter.DeathRemainder;
        Near(settlementEndpoint, expected.Total.FinalPopulation, 1e-7,
            "The shared rounding ledger preserves the coupled settlement endpoint");
        Check(hunter.Population < camp.Population && record.Demographics.Single().StarvationDeaths > 0,
            "Completed deaths follow distinct cohort hunger burdens instead of population shares");
        Check(camp.Population + hunter.Population == 200 + record.Growth - record.Deaths,
            "Per-cohort integer reconciliation conserves the connected settlement total");
    }

    private static void ConnectedSingletonPartitionRounding()
    {
        var world = PhysicalWorld(.8); var camp = world.Camps.Single();
        camp.Population = 1; camp.Workforce.Normalize(1);
        camp.PlantFood = camp.AnimalFood = camp.AquaticFood = 0;
        camp.Inventory.Cloth = camp.Inventory.AnimalFabric = camp.Inventory.LightHousingUnits = 0;
        world.FixedHousing.Clear(); NeolithicProduction.FixedHousingChanged(world);
        for (int i = 0; i < 2; i++)
            world.Hunters.Add(new Hunter
            {
                Id = world.NextId++, Player = camp.Player, SocietyId = camp.SocietyId,
                SourceCamp = camp.Id, Tile = camp.Tile, Population = 1
            });
        foreach (var tile in world.Tiles)
        {
            tile.Food = tile.PotentialFood = 0;
            for (int i = 0; i < tile.Seasons.Length; i++)
            {
                var season = tile.Seasons[i];
                season.MaxFood = season.MaxPotentialFood = 0;
                season.FoodRecoveryPerDay = season.BiomassRecoveryPerDay = 0;
                tile.Seasons[i] = season;
            }
        }

        double years = world.Config.TurnDays / world.Config.DaysPerYear;
        CoupledDemographicResult expected = PhysicalDemography.AdvanceCoupledBudget(world.Config,
            new[] { new DemographicServicePoolInput(Enumerable.Range(0, 3)
                .Select(_ => new DemographicCohortInput(1)), 0, 0, 0) }, years);
        TurnRecord record = new TurnResolver().Resolve(world, new Dictionary<int, List<Order>>());
        double effective = camp.Population + camp.GrowthRemainder - camp.DeathRemainder
            + world.Hunters.Sum(h => h.Population + h.GrowthRemainder - h.DeathRemainder);
        Near(effective, expected.Total.FinalPopulation, 1e-7,
            "A three-way singleton partition preserves the shared continuous endpoint without a per-cohort floor");
        Check(record.Deaths > 0 && camp.Population + world.Hunters.Sum(h => h.Population)
            == 3 + record.Growth - record.Deaths,
            "Singleton partition rounding commits the conserved settlement-level gross events");
        Check(camp.GrowthRemainder + world.Hunters.Sum(h => h.GrowthRemainder) >= 0
            && camp.DeathRemainder + world.Hunters.Sum(h => h.DeathRemainder) >= 0,
            "A surviving connected entity carries both shared gross remainders");
        WorldState replay = SaveCodec.Load(SaveCodec.Save(world));
        int before = camp.Population + world.Hunters.Sum(h => h.Population);
        TurnRecord continuation = new TurnResolver().Resolve(world, new Dictionary<int, List<Order>>());
        new TurnResolver().Resolve(replay, new Dictionary<int, List<Order>>());
        Check(SaveCodec.Fingerprint(world) == SaveCodec.Fingerprint(replay),
            "A singleton-ledger owner survives save/load continuation exactly");
        Check(camp.Population + world.Hunters.Sum(h => h.Population)
            == before + continuation.Growth - continuation.Deaths,
            "The next connected singleton turn remains population-conservative");
        var partitionReport = continuation.Demographics.Single();
        Check(!partitionReport.IntegerExtinction && partitionReport.IntegerExtinctionResidual == 0,
            "Sub-half connected cohorts that round away while the shared settlement survives do not report false extinction residuals");

        var linked = world.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(world, h))
            .OrderBy(h => h.Id).ToArray();
        var extinctionCohorts = new[]
        {
            new DemographicCohortInput(Math.Max(0, camp.Population + camp.GrowthRemainder - camp.DeathRemainder),
                camp.FoodDeficitYears)
        }.Concat(linked.Select(h => new DemographicCohortInput(
            Math.Max(0, h.Population + h.GrowthRemainder - h.DeathRemainder), h.FoodDeficitYears))).ToArray();
        double expectedExtinctionResidual = PhysicalDemography.AdvanceCoupledBudget(world.Config,
            new[] { new DemographicServicePoolInput(extinctionCohorts, 0, 0, 0) }, years).Total.FinalPopulation;
        TurnRecord extinction = new TurnResolver().Resolve(world, new Dictionary<int, List<Order>>());
        var extinctionReport = extinction.Demographics.Single();
        Near(extinctionReport.IntegerExtinctionResidual, expectedExtinctionResidual, 1e-7,
            "Shared integer extinction reports the one aggregate continuous endpoint actually discarded");
        Check(extinctionReport.IntegerExtinction && camp.Population + world.Hunters.Sum(h => h.Population) == 0
            && extinction.Events.Count(e => e.Contains("reached integer extinction")) == 1,
            "A connected settlement crossing below half a person records exactly one physical extinction event");
    }

    private static void DetachedCarryingAfterDeaths()
    {
        var world = PhysicalWorld(.8); var camp = world.Camps.Single();
        world.Config.HunterCarryGenerations = 100;
        const int cohortPopulation = 1000;
        camp.Population = cohortPopulation; camp.Workforce.Normalize(camp.Population);
        camp.PlantFood = 1e7; camp.AnimalFood = camp.AquaticFood = 0;
        camp.Inventory.Cloth = camp.Inventory.AnimalFabric = 0;
        world.FixedHousing.Clear(); NeolithicProduction.FixedHousingChanged(world);
        int remoteTile = world.Map.Radius(camp.Tile, 8)
            .First(tile => world.Map.Distance(camp.Tile, tile) > world.Config.SupplyRange + 1 && world.CanEnter(tile, true, camp));
        var hunter = new Hunter
        {
            Id = world.NextId++, Player = camp.Player, SocietyId = camp.SocietyId,
            SourceCamp = camp.Id, Tile = remoteTile, Population = cohortPopulation
        };
        hunter.CarriedFood = Supply.Capacity(world, hunter);
        double openingCapacity = hunter.CarriedFood;
        world.Hunters.Add(hunter);
        foreach (var tile in world.Tiles)
        {
            tile.Food = tile.PotentialFood = 0;
            for (int season = 0; season < tile.Seasons.Length; season++)
            {
                var profile = tile.Seasons[season];
                profile.MaxFood = profile.MaxPotentialFood = 0;
                profile.FoodRecoveryPerDay = profile.BiomassRecoveryPerDay = 0;
                tile.Seasons[season] = profile;
            }
        }

        var record = new TurnResolver().Resolve(world, new Dictionary<int, List<Order>>());
        var report = record.Demographics.Single(); var logistics = record.Logistics.Single(l => l.Hunter == hunter.Id);
        Check(report.CoupledPools == 2 && report.CoupledCohorts == 2 && report.CoupledRateEvaluations > 0,
            "The live detached-expedition path uses one diagnosed joint-density solve with two local service pools");
        Check(hunter.Population < cohortPopulation && hunter.Population > 0,
            "A fully provisioned but exposed/crowded detached cohort can physically decline (population="
            + hunter.Population + ", nutrition=" + report.Nutrition.ToString("G17")
            + ", mortality=" + report.EffectiveMortalityRate.ToString("G17") + ")");
        Check(logistics.InventoryLoss > 0 && report.DiscardedInventory >= logistics.InventoryLoss
            && hunter.CarriedFood <= Supply.Capacity(world, hunter) + 1e-8
            && hunter.CarriedFood < openingCapacity,
            "Post-demography logistics re-caps population-dependent provisions and attributes the overflow");
        double foodBalance = report.InitialReserves + report.FoodReceived + report.TradeFood
            - report.FoodConsumed - report.ManufacturingFood - report.DiscardedInventory - report.FinalReserves;
        Near(foodBalance, 0, 1e-5, "Detached post-death food balance remains conservative");

    }

    private static double Reference(SimulationConfig cfg, double start, double years, double food, double clothing, double housing)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(years / .002));
        double dt = years / steps, n = start;
        double F(double population) => PhysicalDemography.Rates(cfg, Math.Max(0, population), food, clothing, housing, false).Net * Math.Max(0, population);
        for (int i = 0; i < steps; i++)
        {
            double k1 = F(n), k2 = F(n + dt * k1 / 2), k3 = F(n + dt * k2 / 2), k4 = F(n + dt * k3);
            n = Math.Max(0, n + dt * (k1 + 2 * k2 + 2 * k3 + k4) / 6);
        }
        return n;
    }

    private sealed class ReferenceFlow
    {
        public double Final, PersonYears, Births, Exposure, Crowding, Disease, Starvation;
        public double Deaths => Exposure + Crowding + Disease + Starvation;
        public void Add(ReferenceFlow part)
        {
            Final = part.Final; PersonYears += part.PersonYears; Births += part.Births;
            Exposure += part.Exposure; Crowding += part.Crowding; Disease += part.Disease;
            Starvation += part.Starvation;
        }
    }

    // Independent fixed-step RK4 reference in log population. This is deliberately separate from the production
    // affine-band flow and its Gauss quadrature, and integrates the original nonlinear causes without reconciliation.
    private static ReferenceFlow ReferenceFlows(SimulationConfig cfg, double start, double years,
        double food, double clothing, double housing, bool starvation = false)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(years / .001));
        double dt = years / steps, x = Math.Log(start);
        var result = new ReferenceFlow();
        for (int i = 0; i < steps; i++)
        {
            double n1 = Math.Exp(x); var r1 = PhysicalDemography.Rates(cfg, n1, food, clothing, housing, starvation);
            double x2 = x + dt * r1.Net * .5;
            double n2 = Math.Exp(x2); var r2 = PhysicalDemography.Rates(cfg, n2, food, clothing, housing, starvation);
            double x3 = x + dt * r2.Net * .5;
            double n3 = Math.Exp(x3); var r3 = PhysicalDemography.Rates(cfg, n3, food, clothing, housing, starvation);
            double x4 = x + dt * r3.Net;
            double n4 = Math.Exp(x4); var r4 = PhysicalDemography.Rates(cfg, n4, food, clothing, housing, starvation);
            double scale = dt / 6;
            result.PersonYears += scale * (n1 + 2 * n2 + 2 * n3 + n4);
            result.Births += scale * (r1.Birth * n1 + 2 * r2.Birth * n2 + 2 * r3.Birth * n3 + r4.Birth * n4);
            result.Exposure += scale * (r1.Exposure * n1 + 2 * r2.Exposure * n2 + 2 * r3.Exposure * n3 + r4.Exposure * n4);
            result.Crowding += scale * (r1.Crowding * n1 + 2 * r2.Crowding * n2 + 2 * r3.Crowding * n3 + r4.Crowding * n4);
            result.Disease += scale * (r1.Disease * n1 + 2 * r2.Disease * n2 + 2 * r3.Disease * n3 + r4.Disease * n4);
            result.Starvation += scale * (r1.LegacyStarvation * n1 + 2 * r2.LegacyStarvation * n2
                + 2 * r3.LegacyStarvation * n3 + r4.LegacyStarvation * n4);
            x += scale * (r1.Net + 2 * r2.Net + 2 * r3.Net + r4.Net);
        }
        result.Final = Math.Exp(x);
        return result;
    }

    private static double Relative(double actual, double expected)
        => Math.Abs(actual - expected) / Math.Max(1e-300, Math.Abs(expected));

    private static void NumericalReferenceMatrix(SimulationConfig cfg)
    {
        double[] populations = { 6, 12, 24, 80, 250, 600, 1000, 1600, 2500, 10000, 50000, 100000 };
        double[] years = { 1, 5, 20, 50 };
        double[,] services =
        {
            { 1, 1, 1 }, { .95, .5, .5 }, { .75, 0, 0 },
            { .4, 1, 1 }, { 0, 0, 0 }, { 1, .4, .75 }
        };
        int scenarios = 0;
        double maxEndpoint = 0, maxPersonYears = 0, maxBirths = 0, maxDeaths = 0;
        double maxCause = 0, maxConservation = 0, maxCorrection = 0;
        foreach (double start in populations)
        for (int service = 0; service < services.GetLength(0); service++)
        foreach (double duration in years)
        {
            double food = services[service, 0], clothing = services[service, 1];
            double housing = services[service, 2] * start;
            var native = PhysicalDemography.AdvanceFixed(cfg, start, duration, food, clothing, housing, 0, false);
            var reference = ReferenceFlows(cfg, start, duration, food, clothing, housing);
            scenarios++;
            maxEndpoint = Math.Max(maxEndpoint, Relative(native.FinalPopulation, reference.Final));
            maxPersonYears = Math.Max(maxPersonYears, Relative(native.PersonYears, reference.PersonYears));
            maxBirths = Math.Max(maxBirths, reference.Births > 0 ? Relative(native.Births, reference.Births) : Math.Abs(native.Births));
            maxDeaths = Math.Max(maxDeaths, Math.Abs(native.Deaths - reference.Deaths) / Math.Max(1, reference.Deaths));
            double causeScale = Math.Max(1, reference.Deaths);
            maxCause = Math.Max(maxCause, Math.Abs(native.ExposureDeaths - reference.Exposure) / causeScale);
            maxCause = Math.Max(maxCause, Math.Abs(native.CrowdingDeaths - reference.Crowding) / causeScale);
            maxCause = Math.Max(maxCause, Math.Abs(native.DiseaseDeaths - reference.Disease) / causeScale);
            maxConservation = Math.Max(maxConservation, Math.Abs(native.ConservationError) / Math.Max(1, start));
            maxCorrection = Math.Max(maxCorrection, native.CauseCorrection);
            Check(native.FinalPopulation > 0 && double.IsFinite(native.FinalPopulation),
                "The 288-case native reference matrix preserves finite positive population");
        }
        Console.WriteLine("DEMOGRAPHY_NUMERICAL_MATRIX scenarios=" + scenarios
            + " endpointMax=" + maxEndpoint.ToString("G8")
            + " personYearsMax=" + maxPersonYears.ToString("G8")
            + " birthsMax=" + maxBirths.ToString("G8")
            + " deathsScaledMax=" + maxDeaths.ToString("G8")
            + " causeScaledMax=" + maxCause.ToString("G8")
            + " correctionMax=" + maxCorrection.ToString("G8")
            + " conservationScaledMax=" + maxConservation.ToString("G8"));
        Check(scenarios == 288, "The native numerical gate covers the contract's 288 fixed-service scenarios");
        Check(maxEndpoint <= .00032, "Fixed-grid endpoint error stays within the v1.1 0.05-band envelope: " + maxEndpoint.ToString("G17"));
        Check(maxPersonYears <= .00032, "Fixed-grid person-year error stays within the v1.1 0.05-band envelope: " + maxPersonYears.ToString("G17"));
        Check(maxBirths <= .00032, "Gross-birth error stays within the paid-person-year envelope: " + maxBirths.ToString("G17"));
        Check(maxDeaths <= .0005, "Gross-death error remains bounded against the nonlinear reference: " + maxDeaths.ToString("G17"));
        Check(maxCause <= .0005, "Cause-flow error remains bounded against the nonlinear reference: " + maxCause.ToString("G17"));
        Check(maxCorrection <= cfg.DemographicCauseCorrectionTolerance,
            "Every matrix case remains within the declared cause-reconciliation tolerance");
        Check(maxConservation <= 1e-12, "Every matrix case conserves gross population flow");
    }

    private static ReferenceFlow ReferenceFixed(SimulationConfig cfg, double start, double years,
        double food, double clothing, double housing, double deficitYears)
    {
        var total = new ReferenceFlow { Final = start };
        double remaining = years, current = start;
        bool deficient = food < 1 - 1e-12;
        double unexposed = !deficient ? remaining : Math.Min(remaining,
            Math.Max(0, PhysicalDemography.StarvationGraceYears(cfg) - Math.Max(0, deficitYears)));
        if (unexposed > 0)
        {
            var part = ReferenceFlows(cfg, current, unexposed, food, clothing, housing, false);
            total.Add(part); current = part.Final; remaining -= unexposed;
        }
        if (remaining > 1e-14)
            total.Add(ReferenceFlows(cfg, current, remaining, food, clothing, housing, deficient));
        return total;
    }

    private static ReferenceFlow ReferenceBudget(SimulationConfig cfg, double start, double years,
        double foodBudget, double clothingBudget, double housing, double deficitYears)
    {
        double foodRate = PhysicalDemography.FoodPerPersonYear(cfg);
        double clothingRate = PhysicalDemography.ClothingPerPersonYear(cfg);
        double maximum = PhysicalDemography.BirthOnlyPersonYears(start, cfg.BaseBirthRate, years);
        bool fullFood = foodBudget >= foodRate * maximum;
        bool fullClothing = clothingBudget >= clothingRate * maximum;
        ReferenceFlow Evaluate(double personYears)
        {
            double denominator = personYears > 0 ? personYears : double.Epsilon;
            double food = fullFood ? 1 : Numbers.Clamp(foodBudget / (foodRate * denominator), 0, 1);
            double clothing = fullClothing ? 1 : Numbers.Clamp(clothingBudget / (clothingRate * denominator), 0, 1);
            return ReferenceFixed(cfg, start, years, food, clothing, housing, deficitYears);
        }
        if (fullFood && fullClothing) return Evaluate(maximum);
        double low = 0, high = maximum; var result = Evaluate(high);
        for (int iteration = 0; iteration < 64; iteration++)
        {
            double middle = (low + high) * .5; result = Evaluate(middle);
            double residual = result.PersonYears - middle;
            if (Math.Abs(residual) <= 1e-12 * Math.Max(1, middle)) break;
            if (residual > 0) low = middle; else high = middle;
        }
        return result;
    }

    private static void FiniteBudgetReferenceMatrix(SimulationConfig cfg)
    {
        double[] populations = { 6, 12, 24, 80, 250, 600, 1000, 1600, 2500, 10000, 50000, 100000 };
        double[] durations = { 1, 5, 20, 50 };
        double[] foodFractions = { .25, .4, .72, 1, .55, .9 };
        double[] clothingFractions = { 0, .35, .7, 1, .5, .9 };
        double[] housingFractions = { 0, .4, .75, 1, .6, .9 };
        double maxEndpoint = 0, maxPersonYears = 0, maxBirths = 0, maxDeaths = 0, maxCause = 0;
        for (int i = 0; i < populations.Length; i++)
        {
            double start = populations[i], years = durations[i % durations.Length];
            double maximum = PhysicalDemography.BirthOnlyPersonYears(start, cfg.BaseBirthRate, years);
            double foodBudget = foodFractions[i % foodFractions.Length] * PhysicalDemography.FoodPerPersonYear(cfg) * maximum;
            double clothingBudget = clothingFractions[(i * 5 + 1) % clothingFractions.Length]
                * PhysicalDemography.ClothingPerPersonYear(cfg) * maximum;
            double housing = housingFractions[(i * 7 + 2) % housingFractions.Length] * start;
            double deficit = foodFractions[i % foodFractions.Length] < 1 && i % 3 == 0
                ? PhysicalDemography.StarvationGraceYears(cfg) : 0;
            var native = PhysicalDemography.AdvanceBudget(cfg, start, years, foodBudget, clothingBudget, housing, deficit, true);
            var reference = ReferenceBudget(cfg, start, years, foodBudget, clothingBudget, housing, deficit);
            maxEndpoint = Math.Max(maxEndpoint, Relative(native.FinalPopulation, reference.Final));
            maxPersonYears = Math.Max(maxPersonYears, Relative(native.PersonYears, reference.PersonYears));
            maxBirths = Math.Max(maxBirths, reference.Births > 0 ? Relative(native.Births, reference.Births) : Math.Abs(native.Births));
            maxDeaths = Math.Max(maxDeaths, Math.Abs(native.Deaths - reference.Deaths) / Math.Max(1, reference.Deaths));
            double causeScale = Math.Max(1, reference.Deaths);
            maxCause = Math.Max(maxCause, Math.Abs(native.ExposureDeaths - reference.Exposure) / causeScale);
            maxCause = Math.Max(maxCause, Math.Abs(native.CrowdingDeaths - reference.Crowding) / causeScale);
            maxCause = Math.Max(maxCause, Math.Abs(native.DiseaseDeaths - reference.Disease) / causeScale);
            maxCause = Math.Max(maxCause, Math.Abs(native.LegacyStarvationDeaths - reference.Starvation) / causeScale);
            Near(native.FoodConsumed, Math.Min(foodBudget, PhysicalDemography.FoodPerPersonYear(cfg) * native.PersonYears),
                1e-8 * Math.Max(1, foodBudget), "Finite-budget matrix pays exactly its realized food service");
            Near(native.ClothingConsumed, Math.Min(clothingBudget, PhysicalDemography.ClothingPerPersonYear(cfg) * native.PersonYears),
                1e-8 * Math.Max(1, clothingBudget), "Finite-budget matrix pays exactly its realized textile service");
        }
        Console.WriteLine("DEMOGRAPHY_BUDGET_MATRIX scenarios=" + populations.Length
            + " endpointMax=" + maxEndpoint.ToString("G8")
            + " personYearsMax=" + maxPersonYears.ToString("G8")
            + " birthsMax=" + maxBirths.ToString("G8")
            + " deathsScaledMax=" + maxDeaths.ToString("G8")
            + " causeScaledMax=" + maxCause.ToString("G8"));
        Check(maxEndpoint <= .00032 && maxPersonYears <= .00032 && maxBirths <= .00032,
            "Finite food/textile closure remains within the v1.1 endpoint/person-year envelope");
        Check(maxDeaths <= .0005 && maxCause <= .0005,
            "Finite-budget gross and cause flows remain bounded against the nonlinear reference");
    }

    private static void MicroscopicDeclineAndNumericalLimits(SimulationConfig cfg)
    {
        double start = 5e-13, grace = PhysicalDemography.StarvationGraceYears(cfg);
        var whole = PhysicalDemography.AdvanceFixed(cfg, start, 100, 0, 0, 0, grace, true);
        var first = PhysicalDemography.AdvanceFixed(cfg, start, 37.25, 0, 0, 0, grace, true);
        var second = PhysicalDemography.AdvanceFixed(cfg, first.FinalPopulation, 62.75, 0, 0, 0, grace, true);
        Check(whole.FinalPopulation > 0 && whole.FinalPopulation < 1e-13,
            "A physical decline passes through the retired 1e-13 floor without clipping");
        Check(Relative(second.FinalPopulation, whole.FinalPopulation) <= 2e-7,
            "Microscopic decline retains arbitrary-interval composition across the retired floor");
        Near(whole.FinalPopulation - whole.InitialPopulation, whole.Births - whole.Deaths, 1e-25,
            "Microscopic decline retains gross/cause conservation");

        bool underflow = false, overflow = false;
        try { PhysicalDemography.AdvanceFixed(cfg, double.Epsilon, 1, 0, 0, 0, grace, true); }
        catch (DemographicNumericalException e) { underflow = e.Message.Contains("underflow"); }
        try { PhysicalDemography.Rates(cfg, 100000000, 1, 1, 100000000); }
        catch (DemographicNumericalException e) { overflow = e.Message.Contains("exponential domain") || e.Message.Contains("overflow"); }
        Check(underflow, "A trajectory below the smallest representable positive population fails explicitly");
        Check(overflow, "An unrepresentable population hazard fails explicitly without a mortality ceiling");
    }

    private sealed class CoupledReference
    {
        public double[] Final, PersonYears, Births, Exposure, Crowding, Disease, Starvation, HousingPersonYears;
    }

    private static void CoupledDensityAndCohortChecks(SimulationConfig cfg)
    {
        const double duration = 6;
        double grace = PhysicalDemography.StarvationGraceYears(cfg);

        // Exact homogeneous schedules use the selected scalar analytical propagator, even when represented as
        // several dispatch cohorts. There is no tolerance-based shortcut: these fixtures use bit-exact proportions.
        DemographicResult scalar = PhysicalDemography.AdvanceBudget(cfg, 800, 20, 800000, 8000, 800, 0, true);
        foreach (int split in new[] { 1, 2, 4, 8 })
        {
            var homogeneous = Enumerable.Range(0, split).Select(_ => new DemographicServicePoolInput(
                new[] { new DemographicCohortInput(800.0 / split) },
                800000.0 / split, 8000.0 / split, 800.0 / split)).ToArray();
            CoupledDemographicResult value = PhysicalDemography.AdvanceCoupledBudget(cfg, homogeneous, 20);
            Check(value.UsedExactScalarReduction, "Exactly proportional " + split + "-way dispatch uses the scalar v1.1 propagator");
            Near(value.Total.FinalPopulation, scalar.FinalPopulation, 0, "Homogeneous split preserves the exact scalar endpoint");
            Near(value.Total.PersonYears, scalar.PersonYears, 0, "Homogeneous split preserves exact paid person-years");
            Near(value.Total.Births, scalar.Births, 0, "Homogeneous split preserves exact gross births");
            Near(value.Total.ExposureDeaths, scalar.ExposureDeaths, 0, "Homogeneous split preserves exposure deaths");
            Near(value.Total.CrowdingDeaths, scalar.CrowdingDeaths, 0, "Homogeneous split preserves crowding deaths");
            Near(value.Total.DiseaseDeaths, scalar.DiseaseDeaths, 0, "Homogeneous split preserves disease deaths");
            Check(value.Pools.Sum(p => p.RootEvaluations) == scalar.RootEvaluations
                && value.Pools.Sum(p => p.BandsCrossed) == scalar.BandsCrossed
                && value.Pools.Sum(p => p.HazardEvaluations) == scalar.HazardEvaluations
                && value.Pools.Sum(p => p.QuadratureEvaluations) == scalar.QuadratureEvaluations
                && value.Pools.Sum(p => p.ServiceIterations) == scalar.ServiceIterations,
                "Homogeneous dispatch reports shared scalar work exactly once across temporary pools");
            for (int p = 0; p < split; p++)
                Near(value.Pools[p].FinalPopulation, scalar.FinalPopulation / split, 1e-12,
                    "Homogeneous dispatch retains its exact population share");
        }

        var pools = new[]
        {
            new DemographicServicePoolInput(new[]
            {
                new DemographicCohortInput(140, 0),
                new DemographicCohortInput(60, grace + 5)
            }, 0, 0, 150),
            new DemographicServicePoolInput(new[] { new DemographicCohortInput(90, grace - 2.5) }, 0, 0, 30),
            new DemographicServicePoolInput(new[] { new DemographicCohortInput(40, 0) }, 0, 0, 0)
        };
        double[] nutrition = { .7, .45, 1 }, clothing = { .8, .2, 0 };
        CoupledDemographicResult coupled = PhysicalDemography.AdvanceCoupledFixed(cfg, pools, duration, nutrition, clothing);
        CoupledReference reference = CoupledReferenceFixed(cfg, pools, duration, nutrition, clothing, .00005);
        int member = 0;
        double worst = 0;
        for (int p = 0; p < pools.Length; p++)
        for (int c = 0; c < pools[p].Cohorts.Length; c++, member++)
        {
            DemographicResult actual = coupled.Cohorts[p][c];
            worst = Math.Max(worst, Relative(actual.FinalPopulation, reference.Final[member]));
            worst = Math.Max(worst, Relative(actual.PersonYears, reference.PersonYears[member]));
            worst = Math.Max(worst, Relative(actual.Births, reference.Births[member]));
            double deathScale = Math.Max(1, reference.Exposure[member] + reference.Crowding[member]
                + reference.Disease[member] + reference.Starvation[member]);
            worst = Math.Max(worst, Math.Abs(actual.ExposureDeaths - reference.Exposure[member]) / deathScale);
            worst = Math.Max(worst, Math.Abs(actual.CrowdingDeaths - reference.Crowding[member]) / deathScale);
            worst = Math.Max(worst, Math.Abs(actual.DiseaseDeaths - reference.Disease[member]) / deathScale);
            worst = Math.Max(worst, Math.Abs(actual.LegacyStarvationDeaths - reference.Starvation[member]) / deathScale);
            Near(actual.FinalPopulation - actual.InitialPopulation, actual.Births - actual.Deaths,
                2e-8 * Math.Max(1, actual.InitialPopulation), "Every coupled biological cohort reconciles its own gross causes");
        }
        Console.WriteLine("DEMOGRAPHY_COUPLED_REFERENCE cohorts=" + coupled.CohortCount
            + " pools=" + coupled.PoolCount + " worstScaledError=" + worst.ToString("G8")
            + " rateEvaluations=" + coupled.RateEvaluations + " accepted=" + coupled.AcceptedSteps
            + " rejected=" + coupled.RejectedSteps);
        Check(worst <= 2e-7, "Heterogeneous joint density agrees with the independent high-accuracy vector reference: " + worst.ToString("G17"));
        Check(coupled.RateEvaluations > 0 && coupled.AcceptedSteps > 0,
            "Coupled numerical diagnostics expose bounded rate and step work");

        double uncoupledCrowding = 0, uncoupledFinal = 0;
        for (int p = 0; p < pools.Length; p++)
        {
            CoupledDemographicResult isolated = PhysicalDemography.AdvanceCoupledFixed(cfg,
                new[] { pools[p] }, duration, new[] { nutrition[p] }, new[] { clothing[p] });
            uncoupledCrowding += isolated.Total.CrowdingDeaths;
            uncoupledFinal += isolated.Total.FinalPopulation;
        }
        Check(coupled.Total.CrowdingDeaths > uncoupledCrowding && coupled.Total.FinalPopulation < uncoupledFinal,
            "Separating service pools cannot remove their shared source-settlement crowding density");

        const double firstYears = 2;
        CoupledDemographicResult first = PhysicalDemography.AdvanceCoupledFixed(cfg, pools, firstYears, nutrition, clothing);
        var continuationPools = new DemographicServicePoolInput[pools.Length];
        for (int p = 0; p < pools.Length; p++)
        {
            var continuation = new DemographicCohortInput[pools[p].Cohorts.Length];
            for (int c = 0; c < continuation.Length; c++)
            {
                double age = nutrition[p] >= 1 - 1e-12 ? 0 : pools[p].Cohorts[c].DeficitYears + firstYears;
                continuation[c] = new DemographicCohortInput(first.Cohorts[p][c].FinalPopulation, age,
                    pools[p].Cohorts[c].IncludeLegacyStarvation);
            }
            continuationPools[p] = new DemographicServicePoolInput(continuation, 0, 0, pools[p].HousingCapacity);
        }
        CoupledDemographicResult second = PhysicalDemography.AdvanceCoupledFixed(cfg, continuationPools,
            duration - firstYears, nutrition, clothing);
        for (int p = 0; p < pools.Length; p++)
        for (int c = 0; c < pools[p].Cohorts.Length; c++)
        {
            DemographicResult wholeMember = coupled.Cohorts[p][c];
            DemographicResult firstMember = first.Cohorts[p][c], secondMember = second.Cohorts[p][c];
            Near(secondMember.FinalPopulation, wholeMember.FinalPopulation,
                2e-7 * Math.Max(1, wholeMember.FinalPopulation), "Coupled arbitrary intervals compose at cohort endpoints");
            Near(firstMember.PersonYears + secondMember.PersonYears, wholeMember.PersonYears,
                2e-7 * Math.Max(1, wholeMember.PersonYears), "Coupled arbitrary intervals compose person-years");
            Near(firstMember.Births + secondMember.Births, wholeMember.Births,
                2e-7 * Math.Max(1, wholeMember.Births), "Coupled arbitrary intervals compose gross births");
            Near(firstMember.Deaths + secondMember.Deaths, wholeMember.Deaths,
                2e-7 * Math.Max(1, wholeMember.Deaths), "Coupled arbitrary intervals compose gross deaths");
        }

        double foodRate = PhysicalDemography.FoodPerPersonYear(cfg), clothRate = PhysicalDemography.ClothingPerPersonYear(cfg);
        var budgetPools = pools.Select((pool, p) =>
        {
            double maxY = PhysicalDemography.BirthOnlyPersonYears(pool.Cohorts.Sum(c => c.Population), cfg.BaseBirthRate, duration);
            return new DemographicServicePoolInput(pool.Cohorts, foodRate * maxY * (.35 + .2 * p),
                clothRate * maxY * (.2 + .25 * p), pool.HousingCapacity);
        }).ToArray();
        CoupledDemographicResult budget = PhysicalDemography.AdvanceCoupledBudget(cfg, budgetPools, duration);
        Check(!budget.UsedExactScalarReduction && budget.ServiceEvaluations > 1,
            "Heterogeneous finite service uses the bounded joint person-year closure");
        for (int p = 0; p < budgetPools.Length; p++)
        {
            DemographicResult value = budget.Pools[p];
            Near(value.FoodConsumed, Math.Min(budgetPools[p].FoodBudget, foodRate * value.PersonYears),
                2e-8 * Math.Max(1, budgetPools[p].FoodBudget), "Coupled food stock pays exactly its realized local service");
            Near(value.ClothingConsumed, Math.Min(budgetPools[p].ClothingBudget, clothRate * value.PersonYears),
                2e-8 * Math.Max(1, budgetPools[p].ClothingBudget), "Coupled textile stock pays exactly its realized local service");
        }

        var extremePools = new[]
        {
            new DemographicServicePoolInput(new[] { new DemographicCohortInput(60000) }, 0, 0, 60000),
            new DemographicServicePoolInput(new[] { new DemographicCohortInput(40000, grace + 1) }, 0, 0, 20000)
        };
        CoupledDemographicResult extreme = PhysicalDemography.AdvanceCoupledFixed(cfg, extremePools, 20,
            new[] { 1.0, .8 }, new[] { 1.0, .5 });
        Check(extreme.Total.FinalPopulation > 0 && extreme.Total.FinalPopulation < 100000
            && extreme.Cohorts.SelectMany(x => x).All(x => x.FinalPopulation > 0 && double.IsFinite(x.FinalPopulation)),
            "A 100,000-person joint decline remains positive and finite without ghost or deleted cohorts");
        Near(extreme.Total.FinalPopulation - extreme.Total.InitialPopulation,
            extreme.Total.Births - extreme.Total.Deaths, 1e-6,
            "Large joint decline conserves gross cause-specific population flow");
    }

    // Independent small-step RK4 vector reference. It shares only the public physical rate law with production;
    // it does not use the adaptive stepper, Richardson estimate, service closure or cause reconciler.
    private static CoupledReference CoupledReferenceFixed(SimulationConfig cfg,
        IReadOnlyList<DemographicServicePoolInput> pools, double years,
        IReadOnlyList<double> nutrition, IReadOnlyList<double> clothing, double maximumStep)
    {
        const int stride = 8;
        int members = pools.Sum(p => p.Cohorts.Length), steps = Math.Max(1, (int)Math.Ceiling(years / maximumStep));
        double dt = years / steps, grace = PhysicalDemography.StarvationGraceYears(cfg);
        var poolOf = new int[members]; var deficit = new double[members]; var include = new bool[members];
        var state = new double[members * stride];
        int index = 0;
        for (int p = 0; p < pools.Count; p++)
        for (int c = 0; c < pools[p].Cohorts.Length; c++, index++)
        {
            poolOf[index] = p; deficit[index] = pools[p].Cohorts[c].DeficitYears;
            include[index] = pools[p].Cohorts[c].IncludeLegacyStarvation;
            state[index * stride] = Math.Log(pools[p].Cohorts[c].Population);
        }
        var k1 = new double[state.Length]; var k2 = new double[state.Length];
        var k3 = new double[state.Length]; var k4 = new double[state.Length]; var scratch = new double[state.Length];
        void Derivative(double[] source, double time, double[] target)
        {
            Array.Clear(target, 0, target.Length);
            var poolPopulation = new double[pools.Count]; double density = 0;
            for (int i = 0; i < members; i++)
            {
                double n = Math.Exp(source[i * stride]); density += n; poolPopulation[poolOf[i]] += n;
            }
            for (int i = 0; i < members; i++)
            {
                int p = poolOf[i], o = i * stride; double n = Math.Exp(source[o]);
                double h = Math.Min(1, pools[p].HousingCapacity / poolPopulation[p]);
                bool starvation = include[i] && nutrition[p] < 1 - 1e-12 && deficit[i] + time >= grace;
                DemographicRates rates = PhysicalDemography.Rates(cfg, n, nutrition[p], clothing[p], h * n,
                    starvation, Math.Max(0, density - n));
                target[o] = rates.Net; target[o + 1] = n; target[o + 2] = rates.Birth * n;
                target[o + 3] = rates.Exposure * n; target[o + 4] = rates.Crowding * n;
                target[o + 5] = rates.Disease * n; target[o + 6] = rates.LegacyStarvation * n;
                target[o + 7] = h * n;
            }
        }
        double time = 0;
        for (int step = 0; step < steps; step++)
        {
            Derivative(state, time, k1);
            for (int j = 0; j < state.Length; j++) scratch[j] = state[j] + dt * k1[j] * .5;
            Derivative(scratch, time + dt * .5, k2);
            for (int j = 0; j < state.Length; j++) scratch[j] = state[j] + dt * k2[j] * .5;
            Derivative(scratch, time + dt * .5, k3);
            for (int j = 0; j < state.Length; j++) scratch[j] = state[j] + dt * k3[j];
            Derivative(scratch, time + dt, k4);
            for (int j = 0; j < state.Length; j++) state[j] += dt * (k1[j] + 2 * k2[j] + 2 * k3[j] + k4[j]) / 6;
            time += dt;
        }
        var result = new CoupledReference
        {
            Final = new double[members], PersonYears = new double[members], Births = new double[members],
            Exposure = new double[members], Crowding = new double[members], Disease = new double[members],
            Starvation = new double[members], HousingPersonYears = new double[members]
        };
        for (int i = 0; i < members; i++)
        {
            int o = i * stride; result.Final[i] = Math.Exp(state[o]); result.PersonYears[i] = state[o + 1];
            result.Births[i] = state[o + 2]; result.Exposure[i] = state[o + 3]; result.Crowding[i] = state[o + 4];
            result.Disease[i] = state[o + 5]; result.Starvation[i] = state[o + 6]; result.HousingPersonYears[i] = state[o + 7];
        }
        return result;
    }
}
