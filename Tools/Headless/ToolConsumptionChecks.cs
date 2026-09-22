using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Focused output-capacity and replacement regressions for Neolithic Issue #3.
// This is kept separate from the older NeolithicChecks fixture sweep so that
// each locked tool contract has a named, reviewable assertion.
internal static class ToolConsumptionChecks
{
    private static int checks;

    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception("Tool consumption: " + message);
    }

    private static void Near(double actual, double expected, string message, double tolerance = 1e-8)
    {
        Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);
    }

    public static int Run()
    {
        checks = 0;
        ExactOutputCapacity();
        FractionalOutputCapacity();
        SharedBudgetAndHousingGrades();
        BlockedProductionDoesNotWear();
        CopperRemainsUnspecified();
        NewlyMadeToolsAreNotRetroactive();
        ReceiptAndFractionalSaveRoundTrip();
        ReplacementDemandIsUsefulAndSourceBound();
        NoReplacementEventuallyExhaustsCapacity();
        OrganicReplacementLifecycle();
        Console.WriteLine("PASS: " + checks + " tool-consumption assertions");
        return checks;
    }

    private static WorldState Fixture()
    {
        var w = NeolithicChecks.Fixture();
        var c = w.Camps[0];
        c.PlantFood = 20000;
        c.AnimalFood = c.AquaticFood = 0;
        return w;
    }

    private static void AddSources(WorldState w, double stone = 1000, double wood = 1000)
    {
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState
        {
            MaximumQuantity = 100000,
            CatalogHash = ResourceCatalog.Hash,
            ProfileHash = ResourceCatalog.Hash,
            ConfigurationHash = ResourceCatalog.Hash,
            Provinces = Array.Empty<GeologicalProvince>(),
            Province = new ushort[count],
            History = new ushort[count],
            Host = new byte[count],
            Marine = new byte[count],
            Stone = new ushort[count],
            Clay = new ushort[count],
            Wood = new ushort[count],
            WoodRecoveryPerYear = .05
        };
        int tile = w.Camps[0].Tile;
        w.Resources.Stone[tile] = (ushort)Math.Min(ushort.MaxValue, stone / w.Resources.SurfaceUnit);
        w.Resources.Wood[tile] = (ushort)Math.Min(ushort.MaxValue, wood / w.Resources.SurfaceUnit);
    }

    private static CraftRecipe Recipe(WorldState w, CraftGood good)
    {
        return NeolithicProduction.Recipes(w.Config).Single(recipe => recipe.Good == good);
    }

    private static void ExactOutputCapacity()
    {
        var hand = Fixture();
        var hc = hand.Camps[0];
        hc.Inventory.HandTools = 4;
        hc.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        var handRecord = new TurnRecord();
        double equipment = 0;
        double output = NeolithicProduction.Produce(hand, hc, Recipe(hand, CraftGood.Cloth), WorkforceRoles.Weaver, 80, 80, 4, ref equipment, handRecord);
        Near(output, 4, "four useful outputs are allowed by four Hand Tools");
        Near(hc.Inventory.HandTools, 0, "Hand Tools are consumed by realized output");
        Near(handRecord.Production.Single().HandToolsConsumed, 4, "receipt records Hand Tool consumption");
        Near(handRecord.Production.Single().ToolsConsumed, 0, "Hand Tool production does not consume regular Tools");

        var regular = Fixture();
        var rc = regular.Camps[0];
        rc.Inventory.Tools = 1;
        rc.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        var regularRecord = new TurnRecord();
        equipment = 0;
        output = NeolithicProduction.Produce(regular, rc, Recipe(regular, CraftGood.Cloth), WorkforceRoles.Weaver, 80, 80, 4, ref equipment, regularRecord);
        Near(output, 4, "one regular Tool supports four useful outputs");
        Near(rc.Inventory.Tools, 0, "regular Tool is consumed by four realized outputs");
        Near(regularRecord.Production.Single().ToolsConsumed, 1, "regular Tool receipt uses output capacity units");
    }

    private static void FractionalOutputCapacity()
    {
        var hand = Fixture();
        var hc = hand.Camps[0];
        hc.Inventory.HandTools = .25;
        hc.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        var handRecord = new TurnRecord();
        double equipment = 0;
        double output = NeolithicProduction.Produce(hand, hc, Recipe(hand, CraftGood.Cloth), WorkforceRoles.Weaver, 80, 80, 100, ref equipment, handRecord);
        Near(output, .25, "fractional Hand Tool stock permits fractional useful output");
        Near(handRecord.Production.Single().HandToolsConsumed, .25, "fractional Hand Tool consumption is recorded exactly");

        var regular = Fixture();
        var rc = regular.Camps[0];
        rc.Inventory.Tools = .5;
        rc.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        var regularRecord = new TurnRecord();
        equipment = 0;
        output = NeolithicProduction.Produce(regular, rc, Recipe(regular, CraftGood.Cloth), WorkforceRoles.Weaver, 80, 80, 100, ref equipment, regularRecord);
        Near(output, 2, "fractional regular Tool stock permits two output-capacity units");
        Near(regularRecord.Production.Single().ToolsConsumed, .5, "fractional regular Tool consumption is recorded exactly");
    }

    private static void SharedBudgetAndHousingGrades()
    {
        var w = Fixture();
        var c = w.Camps[0];
        c.Inventory.HandTools = 1;
        c.Inventory.Tools = 1;
        c.Inventory.Wood = 100;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        var recipe = Recipe(w, CraftGood.Housing);
        var budget = new ToolCoverageBudget(w, c, 80);
        var record = new TurnRecord();
        double equipment = budget.Remaining;
        NeolithicProduction.Produce(w, c, recipe, WorkforceRoles.Builder, 80, 80, 100, ref equipment, record, budget);
        NeolithicProduction.Produce(w, c, recipe, WorkforceRoles.Craftsperson, 80, 80, 100, ref equipment, record, budget);
        Near(c.Inventory.HousingUnits, 4.458333333333333, "housing grade order shares one output budget across workers");
        Near(c.Inventory.Tools, 0, "regular housing tool capacity is spent once");
        Near(c.Inventory.HandTools, .541666666666667, "slower Hand Tool housing grade receives only remaining capacity");
        Near(record.Production.Sum(receipt => receipt.ToolsConsumed), 1, "housing receipts conserve shared regular Tool consumption");
        Near(record.Production.Sum(receipt => receipt.HandToolsConsumed), .458333333333333, "housing receipts conserve shared Hand Tool consumption");
    }

    private static void BlockedProductionDoesNotWear()
    {
        foreach (string blockedBy in new[] { "useful demand", "labor", "material" })
        {
            var w = Fixture();
            var c = w.Camps[0];
            c.Inventory.HandTools = 2;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
            if (blockedBy == "material") c.PlantFood = 0;
            double before = c.Inventory.HandTools;
            var record = new TurnRecord();
            double equipment = 0;
            double labor = blockedBy == "labor" ? 0 : 80;
            double useful = blockedBy == "useful demand" ? 0 : 100;
            double output = NeolithicProduction.Produce(w, c, Recipe(w, CraftGood.Cloth), WorkforceRoles.Weaver, labor, 80, useful, ref equipment, record);
            Near(output, 0, "blocked " + blockedBy + " production has no output");
            Near(c.Inventory.HandTools, before, "blocked " + blockedBy + " production does not wear tools");
            Check(record.Production.Count == 0, "blocked " + blockedBy + " production has no receipt");
        }
    }

    private static void CopperRemainsUnspecified()
    {
        var w = Fixture();
        var c = w.Camps[0];
        c.Inventory.CopperTools = 3;
        c.Inventory.HandTools = 0;
        c.People.NeolithicTechnologies.Add(NeolithicTech.CopperWorking);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        var record = new TurnRecord();
        double equipment = 0;
        NeolithicProduction.Produce(w, c, Recipe(w, CraftGood.Cloth), WorkforceRoles.Weaver, 80, 80, 1, ref equipment, record);
        Check(record.Production.Single().Output > 0, "existing Copper coverage hook remains usable");
        Near(c.Inventory.CopperTools, 3, "Copper tools retain the existing hook without invented wear");
        Near(record.Production.Single().CopperToolsConsumed, 0, "Copper receipt consumption remains unspecified");
    }

    private static void NewlyMadeToolsAreNotRetroactive()
    {
        var w = Fixture();
        var c = w.Camps[0];
        c.Inventory.Stone = 10;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        var budget = new ToolCoverageBudget(w, c, 80);
        var record = new TurnRecord();
        double equipment = budget.Remaining;
        NeolithicProduction.Produce(w, c, Recipe(w, CraftGood.HandTools), WorkforceRoles.Craftsperson, 80, 80, 1, ref equipment, record, budget);
        Near(record.Production.Single(receipt => receipt.Good == CraftGood.HandTools).Output, 1, "toolmaking itself does not require an earlier tool");
        Near(c.Inventory.HandTools, 1, "finished Hand Tool is added to inventory");
        double cloth = NeolithicProduction.Produce(w, c, Recipe(w, CraftGood.Cloth), WorkforceRoles.Weaver, 80, 80, 1, ref equipment, record, budget);
        Near(cloth, 0, "newly made Hand Tool cannot backdate into already resolved work");
    }

    private static void ReceiptAndFractionalSaveRoundTrip()
    {
        var w = Fixture();
        var c = w.Camps[0];
        c.Inventory.HandTools = .375;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        double beforeFood = c.PlantFood, beforeTools = c.Inventory.HandTools;
        var record = new TurnRecord { Turn = w.Turn, Seed = w.Seed, StartDays = w.Days, Duration = w.Config.TurnDays };
        double equipment = 0;
        NeolithicProduction.Produce(w, c, Recipe(w, CraftGood.Cloth), WorkforceRoles.Weaver, 80, 80, .125, ref equipment, record);
        var receipt = record.Production.Single();
        Near(beforeFood - c.PlantFood, receipt.PlantFood, "PlantFood receipt reconciles inventory debit");
        Near(beforeTools - c.Inventory.HandTools, receipt.HandToolsConsumed, "Hand Tool receipt reconciles inventory debit");
        Near(receipt.Output, receipt.PlantFood, "Cloth output and plant feedstock are conserved");
        w.History.Add(record);
        var loaded = SaveCodec.Load(SaveCodec.Save(w));
        Near(loaded.Camps[0].Inventory.HandTools, c.Inventory.HandTools, "fractional Hand Tool inventory survives save/load");
        var loadedReceipt = loaded.History.Single().Production.Single();
        Near(loadedReceipt.HandToolsConsumed, receipt.HandToolsConsumed, "fractional receipt Hand Tool field survives save/load");
        Near(loadedReceipt.PlantFood, receipt.PlantFood, "receipt material field survives save/load");
    }

    private static WorldState ReplacementFixture(bool sources)
    {
        var w = Fixture();
        var c = w.Camps[0];
        c.Workforce.Assign(WorkforceRoles.Craftsperson, 5, c.Population);
        c.CraftingPriorities.HandTools = 1;
        c.CraftingPriorities.Tools = 0;
        c.CraftingPriorities.Pots = c.CraftingPriorities.FarmTools = c.CraftingPriorities.Housing = 0;
        c.CraftingPriorities.Cloth = 1;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        c.Inventory.HandTools = c.Inventory.Tools = 0;
        if (sources) AddSources(w);
        return w;
    }

    private static void ReplacementDemandIsUsefulAndSourceBound()
    {
        var useful = ReplacementFixture(true);
        var c = useful.Camps[0];
        Check(NeolithicProduction.Demand(useful, c, CraftGood.HandTools, 20) > 0, "useful Cloth demand creates replacement demand");

        var stocked = ReplacementFixture(true);
        stocked.Camps[0].Inventory.Cloth = 10000;
        Near(NeolithicProduction.Demand(stocked, stocked.Camps[0], CraftGood.HandTools, 20), 0, "finished-goods stock removes speculative tool replacement");

        var noSources = NeolithicChecks.Fixture();
        var nc = noSources.Camps[0];
        nc.Workforce.Assign(WorkforceRoles.Craftsperson, 5, nc.Population);
        nc.CraftingPriorities = new CraftingPriorities { HandTools = 1, Pots = 1, Cloth = 0, FarmTools = 0, Housing = 0, Tools = 0 };
        nc.PotsTarget = 10;
        nc.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
        Near(NeolithicProduction.Demand(noSources, nc, CraftGood.HandTools, 20), 0, "infeasible useful demand creates no replacement demand");

        var noLabor = ReplacementFixture(true);
        noLabor.Camps[0].Workforce.Assignments.Clear();
        Near(NeolithicProduction.Demand(noLabor, noLabor.Camps[0], CraftGood.HandTools, 20), 0, "replacement demand is labor bounded");

        var regular = ReplacementFixture(true);
        regular.Camps[0].People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        regular.Camps[0].CraftingPriorities.Tools = 1;
        Check(NeolithicProduction.Demand(regular, regular.Camps[0], CraftGood.Tools, 20) > 0, "Toolmaking and available Wood/Stone create regular Tool replacement demand");
        var regularNoSource = NeolithicChecks.Fixture();
        var rnc = regularNoSource.Camps[0];
        rnc.Workforce.Assign(WorkforceRoles.Craftsperson, 5, rnc.Population);
        rnc.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 1, Cloth = 0, FarmTools = 0, Housing = 0, Tools = 1 };
        rnc.PotsTarget = 10;
        rnc.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
        rnc.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        Near(NeolithicProduction.Demand(regularNoSource, rnc, CraftGood.Tools, 20), 0, "infeasible useful demand creates no regular replacement demand");
    }

    private static void OrganicReplacementLifecycle()
    {
        var w = ReplacementFixture(true);
        var c = w.Camps[0];
        c.CraftingPriorities.HandTools = .25;
        c.CraftingPriorities.Tools = .25;
        c.EconomyAutomationTurns = -1;
        c.Inventory.Cloth = 0;
        var resolver = new TurnResolver();
        bool unlocked = false, regularMadeAfterUnlock = false, clothMadeAfterUnlock = false;
        for (int turn = 0; turn < 64; turn++)
        {
            var record = resolver.Resolve(w, new Dictionary<int, List<Order>>());
            unlocked |= c.People.HasTech(NeolithicTech.Toolmaking);
            if (unlocked)
            {
                regularMadeAfterUnlock |= record.Production.Any(receipt => receipt.Good == CraftGood.Tools && receipt.Output > 1e-8);
                clothMadeAfterUnlock |= record.Production.Any(receipt => receipt.Good == CraftGood.Cloth && receipt.Output > 1e-8);
            }
            if (regularMadeAfterUnlock && clothMadeAfterUnlock) break;
        }
        Check(unlocked, "sourced useful work organically unlocks Toolmaking");
        Check(regularMadeAfterUnlock, "organic lifecycle makes regular Tools after Toolmaking unlock");
        Check(clothMadeAfterUnlock, "useful Cloth resumes after tool replacement becomes available");
        Check(w.MaterialSources.Any(source => source.Remaining > 0), "organic replacement lifecycle retains a live material source");
    }

    private static void NoReplacementEventuallyExhaustsCapacity()
    {
        var w = NeolithicChecks.Fixture();
        var c = w.Camps[0];
        c.Inventory.HandTools = 2;
        c.Inventory.Tools = 0;
        c.EconomyAutomationTurns = -1;
        c.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 0, Cloth = 1, FarmTools = 0, Housing = 0, Tools = 0 };
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        double early = 0, late = 0;
        for (int turn = 0; turn < 8; turn++)
        {
            var record = new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>());
            double output = record.Production.Where(receipt => receipt.Good == CraftGood.Cloth).Sum(receipt => receipt.Output);
            if (turn < 2) early += output; else late += output;
        }
        Check(early > 0, "existing tools support useful work before depletion");
        Near(late, 0, "a settlement without replacement materials eventually loses productive capacity");
        Near(c.Inventory.HandTools + 4 * c.Inventory.Tools, 0, "unreplaced tool capacity is exhausted");
    }
}
