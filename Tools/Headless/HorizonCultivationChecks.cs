using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class HorizonCultivationChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Horizon cultivation: " + message);
    }

    private static void Near(double actual, double expected, string message, double tolerance = 1e-8)
        => Check(Math.Abs(actual - expected) <= tolerance,
            message + " actual=" + actual + " expected=" + expected);

    private static WorldState Fixture(double startingCapital = 0, double farmTools = 0)
    {
        var world = NeolithicChecks.Fixture();
        var camp = world.Camps[0];
        camp.CultivationFocusTile = camp.Tile;
        camp.Workforce.Assignments.Clear();
        camp.Workforce.Assign(WorkforceRoles.Farmer, 10, camp.Population);
        camp.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 8 };
        camp.Inventory.FarmTools = farmTools;
        if (startingCapital > 0)
        {
            var tile = world.Tiles[camp.Tile];
            tile.CultivationCapital = startingCapital;
            tile.Cultivation = (float)CultivationEconomy.Intensity(world.Config, startingCapital);
            tile.Cultivated = true; tile.CultivationOperator = camp.Id;
        }
        return world;
    }

    private static void PurePlanAndLocalBounds()
    {
        var world = Fixture(0, 2);
        var camp = world.Camps[0];
        var budget = new FarmToolWorkBudget(world, camp);
        var labor = CultivationEconomy.PrepareHorizon(world, camp, 0, 20, budget);
        string before = SaveCodec.Fingerprint(world);
        var plan = CultivationEconomy.PlanHorizonExpansion(world, camp, 20,
            labor.ExpansionFarmers * 20, 20, budget, labor.Footprint);
        Check(SaveCodec.Fingerprint(world) == before, "planning leaves authoritative world unchanged");
        Check(plan.Tiles.Length > 0 && plan.Tiles.Length <= 7, "at most the radius-one field footprint is planned");
        Check(plan.Tiles.SequenceEqual(plan.Tiles.OrderBy(id => id)), "field result slots use tile order");
        Check(plan.Tiles.All(id => world.Tiles[id].TraversableLand && world.Map.Distance(camp.Tile, id) <= 1),
            "no field escapes the radius-one land footprint");
        Check(plan.NewFieldTiles().All(id => plan.Tiles.Contains(id)), "new-field reservations are a subset of the plan");
        double ceiling = 1 / (world.Config.ReferenceIntensity * world.Config.ReferenceIntensity);
        for (int i = 0; i < plan.Tiles.Length; i++)
        {
            Check(plan.StartCapital[i] >= 0 && plan.AverageCapital[i] >= 0 &&
                plan.EndCapital[i] >= 0 && plan.EndCapital[i] <= ceiling + 1e-8,
                "planned field capital stays physical");
            Check(plan.TryGetTile(plan.Tiles[i], out double mean, out double end) &&
                Math.Abs(mean - plan.AverageCapital[i]) < 1e-10 &&
                Math.Abs(end - plan.EndCapital[i]) < 1e-10, "tile lookup matches indexed plan");
        }
        Check(plan.BridgeWorkerYearsUsed <= 20 + 1e-8 &&
            plan.FarmerWorkerYearsUsed <= labor.ExpansionFarmers * 20 + 1e-8,
            "physical work never exceeds the turn-level labor budget");
    }

    private static void SameWorkIsIndependentOfSeasonCount()
    {
        var shortWorld = Fixture();
        var longWorld = Fixture();
        var shortCamp = shortWorld.Camps[0]; var longCamp = longWorld.Camps[0];
        var shortBudget = new FarmToolWorkBudget(shortWorld, shortCamp);
        var longBudget = new FarmToolWorkBudget(longWorld, longCamp);
        CultivationEconomy.PrepareHorizon(shortWorld, shortCamp, 0, 1, shortBudget);
        CultivationEconomy.PrepareHorizon(longWorld, longCamp, 0, 20, longBudget);
        // Eight worker-years fit the one-year workforce as well as the longer
        // turn; the same physical effort should not depend on season count.
        var shortPlan = CultivationEconomy.PlanHorizonExpansion(shortWorld, shortCamp, 1, 8, 0, shortBudget);
        var longPlan = CultivationEconomy.PlanHorizonExpansion(longWorld, longCamp, 20, 8, 0, longBudget);
        Check(shortPlan.Tiles.SequenceEqual(longPlan.Tiles), "same fields are considered at one and twenty years");
        for (int i = 0; i < shortPlan.Tiles.Length; i++)
            Near(shortPlan.EndCapital[i], longPlan.EndCapital[i],
                "equal worker-years build equal new fields regardless of seasonal count", 1e-8);
    }

    private static void FarmToolCoverageAndWorkConservation()
    {
        double[] startingTools = { 0, 2, 8 };
        double[] builtCapital = new double[startingTools.Length];
        for (int scenario = 0; scenario < startingTools.Length; scenario++)
        {
            var world = Fixture(0, startingTools[scenario]);
            var camp = world.Camps[0];
            var budget = new FarmToolWorkBudget(world, camp);
            var labor = CultivationEconomy.PrepareHorizon(world, camp, 0, 20, budget);
            double requested = labor.ExpansionFarmers * 20;
            var plan = CultivationEconomy.PlanHorizonExpansion(world, camp, 20,
                requested, 0, budget, labor.Footprint);
            double covered = Math.Min(requested,
                startingTools[scenario] * world.Config.Neolithic.FarmToolFarmerYearsPerUnit);
            Near(plan.CoveredFarmerWorkerYears, covered,
                "zero, partial, and full starting Farm Tool stocks cover only available farmer-years");
            Near(plan.FarmerWorkerYearsUsed, requested,
                "all requested farmer-years reach initially empty fields");
            Near(camp.Inventory.FarmTools, startingTools[scenario],
                "the field plan does not debit Farm Tools before endpoint commit");
            double effectiveWork = plan.FarmerWorkerYearsUsed +
                (CultivationEconomy.FarmToolFieldWorkEfficiency(world.Config.Neolithic) - 1) * covered;
            builtCapital[scenario] = plan.EndCapital.Sum();
            Near(builtCapital[scenario] * world.Config.Neolithic.BuildWorkerYearsPerTileE,
                effectiveWork, "field investment conserves uncovered and covered work", 1e-7);

            CultivationEconomy.CommitHorizonExpansion(world, camp, plan, new TurnRecord(), budget);
            double spent = covered / world.Config.Neolithic.FarmToolFarmerYearsPerUnit;
            Near(startingTools[scenario] - camp.Inventory.FarmTools, spent,
                "endpoint tool debit equals realized covered farmer-years");
            Check(camp.Inventory.FarmTools >= -1e-10 && budget.RemainingTools >= -1e-10,
                "field service cannot make held Farm Tools negative");
            Near(plan.Tiles.Sum(id => world.Tiles[id].CultivationCapital), builtCapital[scenario],
                "committed field capital equals the planned endpoint", 1e-7);
        }
        Check(builtCapital[0] < builtCapital[1] && builtCapital[1] < builtCapital[2],
            "partial Farm Tool service produces capital between zero and full coverage");
    }

    private static void ZeroDurationDoesNoFieldWork()
    {
        var world = Fixture(.1, 1);
        var camp = world.Camps[0];
        double before = world.Tiles[camp.Tile].CultivationCapital;
        var budget = new FarmToolWorkBudget(world, camp);
        var labor = CultivationEconomy.PrepareHorizon(world, camp, 0, 0, budget);
        var plan = CultivationEconomy.PlanHorizonExpansion(world, camp, 0, 100, 100, budget, labor.Footprint);
        Near(plan.FarmerWorkerYearsUsed, 0, "zero-duration horizon cannot spend farmer work");
        Near(plan.BridgeWorkerYearsUsed, 0, "zero-duration horizon cannot spend bridge work");
        Near(plan.EndCapital[Array.IndexOf(plan.Tiles, camp.Tile)], before,
            "zero-duration horizon does not change capital");
        Near(budget.ReservedMaintenanceWorkerYears, 0, "zero-duration maintenance does not use farm tools");
    }

    private static void BridgeReconciliationAndOneToolDebit()
    {
        var world = Fixture(.1, 2);
        var camp = world.Camps[0];
        var budget = new FarmToolWorkBudget(world, camp);
        var labor = CultivationEconomy.PrepareHorizon(world, camp, 0, 20, budget);
        Near(camp.Inventory.FarmTools, 2, "preparation only reserves farm-tool service");
        var estimated = CultivationEconomy.PlanHorizonExpansion(world, camp, 20,
            labor.ExpansionFarmers * 20, 80, budget, labor.Footprint);
        var realized = CultivationEconomy.ReconcileHorizonBridge(world, camp, estimated, 0, budget);
        Check(realized.BridgeWorkerYearsUsed == 0, "zero realized gathering removes bridge investment");
        Check(realized.NewFieldTiles().All(id => estimated.NewFieldTiles().Contains(id)),
            "reconciliation cannot reserve a new competing field");
        Near(camp.Inventory.FarmTools, 2, "reconciliation remains pure");
        var record = new TurnRecord();
        foreach (int id in estimated.Tiles)
        {
            estimated.TryGetTile(id, out double average, out double endpoint);
            var integrated = EcologicalPressure.Integrate(world, id, Array.Empty<Demand>(), 0,
                20 * world.Config.DaysPerYear, average, endpoint, world.Tiles[id].CultivationOperatingFraction);
            integrated.View.Commit(world.Tiles[id]);
            record.EcologyBalances.Add(integrated.Balance);
        }
        CultivationEconomy.CommitHorizonExpansion(world, camp, realized, record, budget, estimated);
        Near(2 - camp.Inventory.FarmTools,
            (budget.ReservedMaintenanceWorkerYears + realized.CoveredFarmerWorkerYears) /
            world.Config.Neolithic.FarmToolFarmerYearsPerUnit, "maintenance and expansion debit tools once", 1e-8);
        Check(world.Tiles.All(t => t.CultivationCapital >= -1e-8 && t.Food >= -1e-8 &&
            t.PotentialFood >= t.Food - 1e-8), "reconciled field remains nonnegative and food is bounded");
        bool rejected = false;
        try { CultivationEconomy.CommitHorizonExpansion(world, camp, realized, record, budget, estimated); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "a second endpoint commit cannot consume farm tools twice");
    }

    public static int Run()
    {
        checks = 0;
        PurePlanAndLocalBounds();
        SameWorkIsIndependentOfSeasonCount();
        FarmToolCoverageAndWorkConservation();
        ZeroDurationDoesNoFieldWork();
        BridgeReconciliationAndOneToolDebit();
        Console.WriteLine("PASS: " + checks + " horizon cultivation assertions");
        return checks;
    }
}
