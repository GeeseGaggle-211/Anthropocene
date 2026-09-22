using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class HousingMaterialChecks
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Housing materials: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-8) => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);
    private static void AddResources(WorldState w, int wood, int clay, int stone = 0)
    {
        int count = w.Tiles.Length, tile = w.Camps[0].Tile;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05 };
        w.Resources.Wood[tile] = (ushort)wood; w.Resources.Clay[tile] = (ushort)clay; w.Resources.Stone[tile] = (ushort)stone;
        w.Resources.AccessibleParts[(int)MaterialId.Wood] = 10000;
        w.Resources.AccessibleParts[(int)MaterialId.Clay] = 10000;
        w.Resources.AccessibleParts[(int)MaterialId.Stone] = 10000;
    }
    private static CraftRecipe Recipe(WorldState w, CraftGood good) => NeolithicProduction.Recipes(w.Config).Single(r => r.Good == good);
    private static void Enable(Camp c)
    {
        c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        c.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 0, Cloth = 0, FarmTools = 1, Housing = 1, Tools = 1 };
        c.Inventory.HandTools = 1000;
    }
    private static double ProduceHousing(WorldState w, Camp c, double amount, HousingMaterialPlan plan = null)
    {
        double equipment = 1000; var record = new TurnRecord();
        return NeolithicProduction.Produce(w, c, Recipe(w, CraftGood.Housing), WorkforceRoles.Builder, 10000, w.Config.GenerationIntervals, amount, ref equipment, record, new ToolCoverageBudget(w, c, w.Config.GenerationIntervals), plan);
    }
    public static int Run()
    {
        checks = 0;
        RatioAndFallback(); SourceRatio(); MandatoryReservations(); BlockedDirectDoesNotReserve(); ResolverChronology(); ReplacementForecast(); ReplacementPriorityPreservesLaborOrder(); ReplacementHorizonBlocksZeroResidualHousing(); PlannerHonorsZeroResidualHousing();
        Console.WriteLine("PASS: " + checks + " residual Housing material assertions"); return checks;
    }
    private static void RatioAndFallback()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; Enable(c); c.Inventory.Wood = 200; c.Inventory.Clay = 100;
        ProduceHousing(w, c, 6); Near(c.Inventory.Wood, 196, "2:1 Housing debit Wood"); Near(c.Inventory.Clay, 98, "2:1 Housing debit Clay");
        w = NeolithicChecks.Fixture(); c = w.Camps[0]; Enable(c); c.Inventory.Wood = c.Inventory.Clay = 10;
        ProduceHousing(w, c, 6); Near(c.Inventory.Wood, 7, "1:1 Housing debit Wood"); Near(c.Inventory.Clay, 7, "1:1 Housing debit Clay");
        w = NeolithicChecks.Fixture(); c = w.Camps[0]; Enable(c); c.Inventory.Wood = 10;
        ProduceHousing(w, c, 6); Near(c.Inventory.Wood, 4, "Wood-only Housing"); Near(c.Inventory.Clay, 0, "Wood-only leaves Clay absent");
        w = NeolithicChecks.Fixture(); c = w.Camps[0]; Enable(c); c.Inventory.Clay = 10;
        ProduceHousing(w, c, 6); Near(c.Inventory.Wood, 0, "Clay-only leaves Wood absent"); Near(c.Inventory.Clay, 4, "Clay-only Housing");
        w = NeolithicChecks.Fixture(); c = w.Camps[0]; Enable(c); Near(ProduceHousing(w, c, 6), 0, "zero material blocks Housing");
        w = NeolithicChecks.Fixture(); c = w.Camps[0]; Enable(c); c.Inventory.Wood = 1; c.Inventory.Clay = 10;
        ProduceHousing(w, c, 6); Near(c.Inventory.Wood + c.Inventory.Clay, 5, "fallback preserves exactly one total material per Housing");
    }
    private static void SourceRatio()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; Enable(c); c.Inventory.Wood = c.Inventory.Clay = 10; AddResources(w, 19, 9);
        ProduceHousing(w, c, 6);
        Near(c.Inventory.Wood, 6, "reachable Wood source participates in 2:1 ratio");
        Near(c.Inventory.Clay, 8, "reachable Clay source participates in 2:1 ratio");
    }
    private static void MandatoryReservations()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; Enable(c); w.Config.Neolithic.MaterialReserveHorizonYears = 0;
        c.Inventory.Wood = 12; c.Inventory.Clay = 6;
        var planned = NeolithicProduction.Recipes(w.Config).ToDictionary(r => r.Good, r => 0d); planned[CraftGood.FarmTools] = 6; planned[CraftGood.Housing] = 6;
        var plan = MaterialEconomy.PlanHousingMaterials(w, c, NeolithicProduction.Recipes(w.Config), planned, 20, w.Days);
        Near(plan.WoodShare, .5, "direct Wood demand is reserved before Housing ratio"); Near(plan.ClayShare, .5, "direct Wood reservation leaves Clay share");
        // Housing may execute first by priority, but it cannot consume the six
        // Wood units reserved for the later Carpenter receipt.
        ProduceHousing(w, c, 6, plan); Near(c.Inventory.Wood, 9, "Housing leaves direct Wood reservation"); Near(c.Inventory.Clay, 3, "Housing uses residual Clay share");
        double equipment = 1000; var record = new TurnRecord();
        NeolithicProduction.Produce(w, c, Recipe(w, CraftGood.FarmTools), WorkforceRoles.Carpenter, 1000, w.Config.GenerationIntervals, 6, ref equipment, record, new ToolCoverageBudget(w, c, w.Config.GenerationIntervals), plan);
        Near(c.Inventory.Wood, 3, "later direct recipe receives its reserved Wood once");
        Near(c.Inventory.Wood + c.Inventory.Clay + c.Inventory.FarmTools + c.Inventory.HousingUnits, 18, "Housing and Farm Tool receipts conserve all material units");
    }
    private static void BlockedDirectDoesNotReserve()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; Enable(c); w.Config.Neolithic.MaterialReserveHorizonYears = 0;
        c.Inventory.Wood = 6; c.Inventory.Clay = 3;
        var planned = NeolithicProduction.Recipes(w.Config).ToDictionary(r => r.Good, r => 0d); planned[CraftGood.Tools] = 6; planned[CraftGood.Housing] = 6;
        var plan = MaterialEconomy.PlanHousingMaterials(w, c, NeolithicProduction.Recipes(w.Config), planned, 20, w.Days);
        Near(plan.WoodShare, 2d / 3, "Stone-blocked Tools do not reserve Wood"); Near(plan.ClayShare, 1d / 3, "Stone-blocked Tools retain raw Clay ratio");
        ProduceHousing(w, c, 6, plan); Near(c.Inventory.Wood, 2, "blocked direct work cannot phantom-reserve Housing Wood"); Near(c.Inventory.Clay, 1, "blocked direct work cannot phantom-reserve Housing Clay");
    }
    private static void ResolverChronology()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; Enable(c); c.Population = 1000; c.Inventory.Wood = 100; c.Inventory.Clay = 200;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Pottery); c.Workforce.Assign(WorkforceRoles.Potter, 1, c.Population); c.Workforce.Assign(WorkforceRoles.Builder, 1, c.Population);
        c.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 1, Cloth = 0, FarmTools = 0, Housing = 2, Tools = 0 };
        var record = new TurnRecord(); NeolithicProduction.ResolveCampNeolithic(w, c, 80, 20, record);
        var housing = record.Production.Where(p => p.Good == CraftGood.Housing).ToArray();
        Near(record.Production.Where(p => p.Good == CraftGood.Pots).Sum(p => p.Output), 80, "earlier specialist direct work realizes");
        Near(housing.Sum(p => p.Output), 2.5, "Housing labor is unchanged after direct reservation");
        Near(housing.Sum(p => p.Wood), 2.5 * 100 / 220, "resolver keeps fixed residual Wood ratio");
        Near(housing.Sum(p => p.Clay), 2.5 * 120 / 220, "resolver keeps fixed residual Clay ratio");
        var later = NeolithicChecks.Fixture(); var lc = later.Camps[0]; Enable(lc); lc.Population = 11; lc.Inventory.Wood = .1; lc.Inventory.Clay = 5.5;
        lc.Workforce.Assign(WorkforceRoles.Builder, 1, lc.Population); lc.Workforce.Assign(WorkforceRoles.Craftsperson, 1, lc.Population); lc.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 1, Cloth = 0, FarmTools = 0, Housing = 2, Tools = 0 };
        var laterRecord = new TurnRecord(); NeolithicProduction.ResolveCampNeolithic(later, lc, 80, 20, laterRecord);
        Near(laterRecord.Production.Where(p => p.Good == CraftGood.Pots).Sum(p => p.Output), 5.5, "later generalist direct work remains reserved");
        Near(laterRecord.Production.Where(p => p.Good == CraftGood.Housing).Sum(p => p.Output), .1, "earlier Builder cannot spend later generalist Clay");
    }
    private static void ReplacementForecast()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; c.People.NeolithicTechnologies.Add(NeolithicTech.Construction); c.Population = 4; c.Inventory.HandTools = 0; c.Inventory.Stone = 10; c.Inventory.Wood = c.Inventory.Clay = 1;
        c.CraftingPriorities = new CraftingPriorities { HandTools = 1, Pots = 0, Cloth = 0, FarmTools = 1, Housing = 2, Tools = 0 };
        c.Workforce.Assign(WorkforceRoles.Builder, 1, c.Population); c.Workforce.Assign(WorkforceRoles.Craftsperson, 1, c.Population); c.Workforce.Assign(WorkforceRoles.Farmer, 1, c.Population); c.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 0 };
        foreach (int id in CultivationEconomy.Footprint(w, c))
        {
            var tile = w.Tiles[id]; tile.CultivationCapital = .1; tile.Cultivation = (float)CultivationEconomy.Intensity(w.Config, .1); tile.CultivationOperator = c.Id; tile.Cultivated = true;
        }
        Near(NeolithicProduction.Demand(w, c, CraftGood.HandTools, 20), 2, "replacement forecast reserves later Farm Tool Wood before flexible Housing");
    }
    private static void ReplacementPriorityPreservesLaborOrder()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; c.Inventory.Wood = 100; c.Inventory.Clay = 100; c.Inventory.HandTools = 0; c.PlantFood = 100000;
        c.CraftingPriorities = new CraftingPriorities { HandTools = 1, Pots = 0, Cloth = 1, FarmTools = 0, Housing = 2, Tools = 0 };
        c.Workforce.Assign(WorkforceRoles.Craftsperson, 1, c.Population);
        Near(NeolithicProduction.Demand(w, c, CraftGood.HandTools, 20), .625, "material reservation does not reprioritize Craftsperson labor");
    }
    private static void ReplacementHorizonBlocksZeroResidualHousing()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; c.Population = 4; c.Inventory.Wood = 0; c.Inventory.Clay = 1; c.Inventory.Stone = 10; c.Inventory.HandTools = 0; c.Inventory.Pots = 1;
        w.Config.Neolithic.PotDecayPerReferenceTurn = 0;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Construction); c.People.NeolithicTechnologies.Add(NeolithicTech.Pottery); c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        c.CraftingPriorities = new CraftingPriorities { HandTools = 1, Pots = 1, Cloth = 0, FarmTools = 0, Housing = 2, Tools = 1 };
        c.Workforce.Assign(WorkforceRoles.Potter, 1, c.Population); c.Workforce.Assign(WorkforceRoles.Builder, 1, c.Population);
        Near(NeolithicProduction.Demand(w, c, CraftGood.Pots, 20), 1, "zero residual fixture has one useful Pot direct demand");
        Near(NeolithicProduction.Demand(w, c, CraftGood.HandTools, 20), 1, "zero residual Housing share leaves replacement Hand Tool demand");
        Near(NeolithicProduction.Demand(w, c, CraftGood.Tools, 20), 0, "zero residual Housing share does not create phantom Regular Tool demand");
    }
    private static void PlannerHonorsZeroResidualHousing()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; c.Population = 4; c.Inventory.Wood = 0; c.Inventory.Clay = 2; c.Inventory.Pots = 1; c.PlantFood = 100000;
        w.Config.Neolithic.PotDecayPerReferenceTurn = 0;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Construction); c.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
        c.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 1, Cloth = 0, FarmTools = 0, Housing = 2, Tools = 0 };
        var plan = EconomicPlanner.Plan(w, c, EconomicIntent.From(c));
        Near(plan.Assignments.Where(a => a.Role == WorkforceRoles.Builder).Sum(a => a.Population), 0, "planner does not staff Housing with zero residual material");
    }
}
