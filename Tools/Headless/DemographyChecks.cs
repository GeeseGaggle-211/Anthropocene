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
        var cfg = new SimulationConfig { DemographicModel = DemographicModel.PhysicalMortalityV1, QoLMortalityEnabled = false };
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

        foreach (double start in new[] { 25.0, 1000.0, 100000.0 })
        foreach (double duration in new[] { .25, 20.0, 73.25 })
        {
            double f = start > 1000 ? .91 : .74, c = start < 1000 ? .35 : .88, housing = start * .67;
            var native = PhysicalDemography.AdvanceFixed(cfg, start, duration, f, c, housing, 0, false);
            double reference = Reference(cfg, start, duration, f, c, housing);
            Near(native.FinalPopulation, reference, 8e-4 * Math.Max(1, reference), "Native solver agrees with RK4 reference at N=" + start + ", T=" + duration);
        }

        ResolutionAndReplay();
        DispatchAndReturn();
        IntegerExtinction();
        ExplicitMigration();

        Console.WriteLine("PASS: " + assertions + " physical demography assertions");
        return assertions;
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
        Check(AgentObservation.SchemaVersion == 19 && settlement.EffectiveMortalityRate > 0 && settlement.Nutrition > 0 && settlement.ClothingCoverage > 0, "Schema 19 exposes owner-only physical diagnostic rates and coverage");
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
}
