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
        RatioAndFallback(); SourceRatio(); MandatoryReservations(); BlockedDirectDoesNotReserve();
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
}
