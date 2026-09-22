using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Focused acceptance checks for the Stone Farm Tool labor contract.  Keep these
// separate from the general Neolithic checks so failures identify a field-work
// invariant rather than a broader economy fixture.
public static class FarmToolChecks
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("FarmTools: " + message);
    }

    private static void Near(double actual, double expected, string message, double tolerance = 1e-8)
        => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);

    private static WorldState FieldFixture(double capital = 0, int farmers = 0, double tools = 0)
    {
        var w = NeolithicChecks.Fixture();
        w.Config.TurnDays = 7300;
        var c = w.Camps[0];
        c.PlantFood = 1_000_000;
        c.AnimalFood = c.AquaticFood = 0;
        c.Workforce.Assignments.Clear();
        if (farmers > 0) c.Workforce.Assign(WorkforceRoles.Farmer, farmers, c.Population);
        c.Inventory.FarmTools = tools;
        c.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 0 };
        var tile = w.Tiles[c.Tile];
        tile.CultivationCapital = capital;
        tile.Cultivation = (float)CultivationEconomy.Intensity(w.Config, capital);
        tile.Cultivated = capital > 0;
        tile.CultivationOperator = capital > 0 ? c.Id : -1;
        return w;
    }

    private static double FieldCapital(WorldState w)
    {
        var c = w.Camps[0];
        return CultivationEconomy.Footprint(w, c).Sum(id => w.Tiles[id].CultivationCapital);
    }

    private static void FixedPhysicalStateAndEcology()
    {
        var plain = FieldFixture(.7, 4, 0);
        var equipped = SaveCodec.Load(SaveCodec.Save(plain));
        var pc = plain.Camps[0];
        var ec = equipped.Camps[0];
        ec.Inventory.FarmTools = 100;
        plain.Tiles[pc.Tile].CultivationOperatingFraction = .6;
        equipped.Tiles[ec.Tile].CultivationOperatingFraction = .6;

        double q = CultivationEconomy.Intensity(plain.Config, plain.Tiles[pc.Tile].CultivationCapital);
        double before = plain.Tiles[pc.Tile].CultivationCapital;
        double managedPlain = new EcologyCapacity(plain, plain.Tiles[pc.Tile], LazyEcology.ProjectEcology(plain, pc.Tile, 0), 0).ManagedFlow;
        double managedEquipped = new EcologyCapacity(equipped, equipped.Tiles[ec.Tile], LazyEcology.ProjectEcology(equipped, ec.Tile, 0), 0).ManagedFlow;
        Check(managedPlain > 0, "fixed-q managed regeneration fixture is non-vacuous");
        Near(CultivationEconomy.FarmToolFieldWorkEfficiency(plain.Config.Neolithic), 1.265625, "Stone efficiency derives from .45/.40");
        Near(CultivationEconomy.Intensity(equipped.Config, equipped.Tiles[ec.Tile].CultivationCapital), q, "acquiring tools does not change physical q");
        Near(equipped.Tiles[ec.Tile].CultivationCapital, before, "acquiring tools does not change persistent E");
        Near(managedEquipped, managedPlain, "tools do not directly change managed regeneration");
        Near(equipped.Config.ReferenceIntensity, plain.Config.ReferenceIntensity, "tools do not mutate the physical intensity calibration");
        Near(equipped.Config.CultivationRegenFlux, plain.Config.CultivationRegenFlux, "tools do not mutate ecology regeneration tuning");
    }

    private static void MaintenanceCoveragePriority()
    {
        var w = FieldFixture(.1, 10, 4);
        var c = w.Camps[0];
        // Ask automation for all available expansion time.  Maintenance must
        // be satisfied first, even when the forecast asks for expansion.
        c.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 10 };
        var t = w.Tiles[c.Tile];
        CultivationEconomy.Factors(w.Config, t, out _, out double maintenanceFactor);
        double required = w.Config.Neolithic.MaintenanceWorkersPerTileE(w.Config.MaintenanceCoefficient) * maintenanceFactor * t.CultivationCapital;
        double efficiency = CultivationEconomy.FarmToolFieldWorkEfficiency(w.Config.Neolithic);
        var budget = new FarmToolWorkBudget(w, c);
        var plan = CultivationEconomy.Prepare(w, c, 0, budget, 20);
        Near(plan.MaintenanceCoveredWorkerYears, (required / efficiency) * 20, "covered field work is allocated to maintenance before expansion", 1e-7);
        Check(plan.MaintenanceEffectiveWorkerYears + 1e-8 >= required, "maintenance receives enough effective worker-time before expansion");
        Check(plan.OperatingFarmers >= required / efficiency - 1e-8, "maintenance farmers remain assigned when expansion is requested");
        Check(plan.OperatingFarmers + plan.ExpansionFarmers <= 10 + 1e-8, "maintenance and expansion do not duplicate farmers");
    }

    private static void RealizedConsumptionAndPartialCoverage()
    {
        const double yearsPerUnit = 20;
        var w = FieldFixture(0, 1, .75);
        var c = w.Camps[0];
        var budget = new FarmToolWorkBudget(w, c);
        double used = CultivationEconomy.Expand(w, c, 10, false, new TurnRecord(), budget);
        Near(used, 10, "realized expansion reports actual worker-years");
        Near(.75 - c.Inventory.FarmTools, .5, "ten covered worker-years consume one-half Farm Tool");
        Near(budget.CoveredWorkerYears, 10, "covered expansion records actual worker-years");

        var partial = FieldFixture(0, 1, .25);
        var pc = partial.Camps[0];
        var partialBudget = new FarmToolWorkBudget(partial, pc);
        CultivationEconomy.Expand(partial, pc, 10, false, new TurnRecord(), partialBudget);
        Near(.25 - pc.Inventory.FarmTools, .25, "partial stock is consumed only for covered work");
        Near(FieldCapital(partial), (5 * 1.265625 + 5) / partial.Config.Neolithic.BuildWorkerYearsPerTileE, "partial covered expansion applies proportional Stone efficiency");

        var idle = FieldFixture(0, 0, 1.25);
        new TurnResolver().Resolve(idle, new Dictionary<int, List<Order>>());
        Near(idle.Camps[0].Inventory.FarmTools, 1.25, "idle Farm Tools do not decay");

        var blocked = FieldFixture(6.25, 1, 2);
        var bc = blocked.Camps[0];
        foreach (int id in CultivationEconomy.Footprint(blocked, bc))
        {
            var tile = blocked.Tiles[id];
            tile.CultivationCapital = 6.25;
            tile.Cultivation = (float)CultivationEconomy.Intensity(blocked.Config, tile.CultivationCapital);
            tile.Cultivated = true;
            tile.CultivationOperator = bc.Id;
        }
        var blockedBudget = new FarmToolWorkBudget(blocked, bc);
        Near(CultivationEconomy.Expand(blocked, bc, 20, false, new TurnRecord(), blockedBudget), 0, "blocked expansion realizes no field work");
        Near(bc.Inventory.FarmTools, 2, "blocked expansion consumes no Farm Tools");
        Near(blockedBudget.CoveredWorkerYears, 0, "blocked expansion has no covered realized work");

        // A turn-local budget captures the opening stock.  Tools made later in
        // the turn must not retroactively cover already-resolved expansion.
        var retro = FieldFixture(0, 1, 0);
        var rc = retro.Camps[0];
        var oldBudget = new FarmToolWorkBudget(retro, rc);
        rc.Inventory.FarmTools = 10;
        var plain = FieldFixture(0, 1, 0);
        double plainUsed = CultivationEconomy.Expand(plain, plain.Camps[0], 20, false, new TurnRecord());
        double retroUsed = CultivationEconomy.Expand(retro, rc, 20, false, new TurnRecord(), oldBudget);
        Near(retroUsed, plainUsed, "new Farm Tools cannot backdate benefit into captured work");
        Near(FieldCapital(retro), FieldCapital(plain), "new Farm Tools do not change already-captured expansion work");
        Near(rc.Inventory.FarmTools, 10, "newly made Farm Tools remain available for future work");

        // The units are explicit in the public budget contract as well.
        var direct = FieldFixture(0, 1, 1);
        var dc = direct.Camps[0];
        var directBudget = new FarmToolWorkBudget(direct, dc);
        Near(directBudget.Consume(20, yearsPerUnit), 20, "one generation consumes one Farm Tool unit of covered work");
        Near(dc.Inventory.FarmTools, 0, "direct realized budget debit is exactly one unit");
    }

    private static void EffectiveExpansionAndTimePartition()
    {
        const double efficiency = 1.265625;
        var plain = FieldFixture(0, 1, 0);
        var equipped = FieldFixture(0, 1, 10);
        double plainUsed = CultivationEconomy.Expand(plain, plain.Camps[0], 20, false, new TurnRecord());
        var equippedBudget = new FarmToolWorkBudget(equipped, equipped.Camps[0]);
        double equippedUsed = CultivationEconomy.Expand(equipped, equipped.Camps[0], 20, false, new TurnRecord(), equippedBudget);
        Near(plainUsed, 20, "uncovered expansion uses the requested worker-time");
        Near(equippedUsed, 20, "covered expansion still reports actual worker-time");
        Near(FieldCapital(equipped) / Math.Max(1e-12, FieldCapital(plain)), efficiency, "fully covered expansion contributes Stone efficiency");

        var whole = FieldFixture(0, 1, .75);
        var split = SaveCodec.Load(SaveCodec.Save(whole));
        var wholeBudget = new FarmToolWorkBudget(whole, whole.Camps[0]);
        var splitBudget = new FarmToolWorkBudget(split, split.Camps[0]);
        CultivationEconomy.Expand(whole, whole.Camps[0], 20, false, new TurnRecord(), wholeBudget);
        CultivationEconomy.Expand(split, split.Camps[0], 10, false, new TurnRecord(), splitBudget);
        CultivationEconomy.Expand(split, split.Camps[0], 10, false, new TurnRecord(), splitBudget);
        Near(whole.Camps[0].Inventory.FarmTools, split.Camps[0].Inventory.FarmTools, "tool consumption is time-partition invariant");
        Near(FieldCapital(whole), FieldCapital(split), "effective expansion work is time-partition invariant", 1e-7);
    }

    private static void FullTurnFixedQHarvestNeutrality()
    {
        var plain = FieldFixture(.1, 10, 0);
        var equipped = FieldFixture(.1, 10, 4);
        plain.Camps[0].PlantFood = equipped.Camps[0].PlantFood = 0;
        plain.Camps[0].EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 0 };
        equipped.Camps[0].EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 0 };
        var plainRecord = new TurnResolver().Resolve(plain, new Dictionary<int, List<Order>>());
        var equippedRecord = new TurnResolver().Resolve(equipped, new Dictionary<int, List<Order>>());
        int tile = plain.Camps[0].Tile;
        double plainHarvest = plainRecord.Transfers.Where(t => t.Entity == plain.Camps[0].Id && t.Kind == "Cultivation").Sum(t => t.Food);
        double equippedHarvest = equippedRecord.Transfers.Where(t => t.Entity == equipped.Camps[0].Id && t.Kind == "Cultivation").Sum(t => t.Food);
        Check(plainHarvest > 0 && equippedHarvest > 0, "fixed-q field produces positive harvest in both controls");
        Near(equippedHarvest, plainHarvest, "Farm Tools do not directly multiply fixed-q farmer harvest", 1e-7);
        Near(equipped.Tiles[tile].Food, plain.Tiles[tile].Food, "Farm Tools do not directly change fixed-q food stock", 1e-7);
        Near(equipped.Tiles[tile].PotentialFood, plain.Tiles[tile].PotentialFood, "Farm Tools do not directly change fixed-q biomass", 1e-7);
        Near(equipped.Tiles[tile].CultivationCapital, plain.Tiles[tile].CultivationCapital, "fixed-q field E remains equal with tools", 1e-7);
        Near(equipped.Config.FarmerProductivity, plain.Config.FarmerProductivity, "Farm Tools do not mutate farmer harvest productivity");
        Check(equipped.Camps[0].Inventory.FarmTools < 4 && equipped.Camps[0].Inventory.FarmTools > 0, "fixed-q tool wear is positive fractional maintenance use only");
        Near(plain.Camps[0].Inventory.FarmTools, 0, "plain fixed-q control has no Farm Tool wear");
    }

    private static void FullTurnConservationDeterminismAndSave()
    {
        var scalar = FieldFixture(.1, 10, 1.75);
        var replay = SaveCodec.Load(SaveCodec.Save(scalar));
        scalar.Camps[0].EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 0 };
        replay.Camps[0].EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 0 };
        var scalarRecord = new TurnResolver().Resolve(scalar, new Dictionary<int, List<Order>>());
        var replayRecord = new TurnResolver(new PhaseExecution(2, 1, true, 1)).Resolve(replay, new Dictionary<int, List<Order>>());
        Check(SaveCodec.Fingerprint(scalar) == SaveCodec.Fingerprint(replay), "scalar and parallel field-tool turns replay identically");
        var demographic = scalarRecord.Demographics.Single();
        Near(demographic.InitialReserves + demographic.FoodReceived - demographic.FoodConsumed - demographic.ManufacturingFood - demographic.DiscardedInventory - demographic.FinalReserves, 0, "full-turn food conservation");
        Check(scalar.Tiles.All(t => t.Food <= t.PotentialFood + 1e-8), "full-turn food remains below biomass");
        Check(scalar.Camps.All(c => c.Inventory.FarmTools >= -1e-8), "full-turn Farm Tool stock remains nonnegative");
        Check(scalar.Camps[0].Inventory.FarmTools > 0 && scalar.Camps[0].Inventory.FarmTools < 1.75, "full-turn save fixture preserves fractional Farm Tool stock");
        var loaded = SaveCodec.Load(SaveCodec.Save(scalar));
        Check(SaveCodec.Fingerprint(scalar) == SaveCodec.Fingerprint(loaded), "fractional Farm Tool inventory saves and reloads exactly");
    }

    public static int Run()
    {
        checks = 0;
        FixedPhysicalStateAndEcology();
        MaintenanceCoveragePriority();
        RealizedConsumptionAndPartialCoverage();
        EffectiveExpansionAndTimePartition();
        FullTurnFixedQHarvestNeutrality();
        FullTurnConservationDeterminismAndSave();
        Console.WriteLine("PASS: " + checks + " Stone Farm Tool contract assertions");
        return checks;
    }
}
