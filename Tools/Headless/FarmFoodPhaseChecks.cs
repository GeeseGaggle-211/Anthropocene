using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Regression coverage for the two chronology boundaries used by mixed field and
// fabric demand: field work is resolved before growth planning, while Pots decay
// inside the later camp production phase.
public static class FarmFoodPhaseChecks
{
    private static int checks;
    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception("FarmFoodPhase: " + message);
    }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-7)
        => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);

    private static WorldState Fixture()
    {
        var w = NeolithicChecks.Fixture();
        w.Config.TurnDays = 20 * w.Config.DaysPerYear;
        w.Config.AnnualHumanGrowthRate = .01145;
        w.Config.FarmerProductivity = .01f;
        w.Config.Neolithic.UsefulFinishedGoodsReserveTurns = 0;
        var c = w.Camps[0];
        c.PlantFood = 16000;
        c.AnimalFood = c.AquaticFood = 0;
        c.Workforce.Assignments.Clear();
        c.Workforce.Assign(WorkforceRoles.Farmer, 99, c.Population);
        c.Workforce.Assign(WorkforceRoles.Craftsperson, 1, c.Population);
        c.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 0 };
        c.CraftingPriorities = new CraftingPriorities { FarmTools = 2, Cloth = 1, Pots = 0 };
        c.Inventory.FarmTools = 2 * (10 / 1.265625);
        c.Inventory.HandTools = 20;
        c.Inventory.Wood = 1000;
        foreach (int id in CultivationEconomy.Footprint(w, c))
        {
            var tile = w.Tiles[id];
            tile.CultivationCapital = .1;
            tile.Cultivation = (float)CultivationEconomy.Intensity(w.Config, .1);
            tile.Cultivated = true;
            tile.CultivationOperator = c.Id;
        }
        return w;
    }

    private static void FieldAndPotBoundaries()
    {
        var w = Fixture();
        var c = w.Camps[0];
        double years = w.Config.TurnDays / w.Config.DaysPerYear;
        double intervals = w.Config.GenerationIntervals;
        double preField = NeolithicProduction.FabricFeedstockNeed(w, c, intervals, years, c.Population, false, false);
        double oneCycle = 10 / 1.265625;
        c.Inventory.FarmTools -= oneCycle;
        double wrongReforecast = NeolithicProduction.FabricFeedstockNeed(w, c, intervals, years, c.Population, false, false);
        double afterField = NeolithicProduction.FabricFeedstockNeed(w, c, intervals, years, c.Population, true, false);
        Near(preField, 20, "two held Farm Tool cycles leave the full Cloth forecast before field resolution");
        Near(afterField, 20, "one held Farm Tool cycle leaves the full Cloth forecast after field resolution");
        Check(afterField > wrongReforecast + 1, "post-field forecast avoids recharging the already consumed Farm Tool cycle");
        var potWorld = Fixture();
        potWorld.Camps[0].Inventory.Pots = 50;
        double potsBefore = NeolithicProduction.Demand(potWorld, potWorld.Camps[0], CraftGood.Pots, years, potWorld.Camps[0].Population, false, false);
        double potsFieldOnly = NeolithicProduction.Demand(potWorld, potWorld.Camps[0], CraftGood.Pots, years, potWorld.Camps[0].Population, true, false);
        Near(potsFieldOnly, potsBefore, "field resolution does not imply Pot depreciation");
        double potsAfter = NeolithicProduction.Demand(potWorld, potWorld.Camps[0], CraftGood.Pots, years, potWorld.Camps[0].Population, true, true);
        Check(potsAfter <= potsBefore + 1e-8, "post-pot phase uses current Pot stock without double depreciation");
    }

    private static void MixedTurnConservation()
    {
        var w = Fixture();
        var c = w.Camps[0];
        var record = new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>());
        var report = record.Demographics.Single(d => d.Camp == c.Id);
        Check(record.Production.Count(p => p.Good == CraftGood.FarmTools && p.Output > 1e-8) == 0,
            "one remaining field cycle does not manufacture Farm Tools after field resolution");
        Check(record.Production.Any(p => p.Good == CraftGood.Cloth && p.Output > 1e-8),
            "released Craftsperson time produces Cloth");
        Check(report.Additions == 3, "mixed field/fabric phase retains the intended birth outcome");
        Near(report.InitialReserves + report.FoodReceived - report.FoodConsumed - report.ManufacturingFood - report.DiscardedInventory - report.FinalReserves, 0,
            "mixed field/fabric phase conserves food");
        var loaded = SaveCodec.Load(SaveCodec.Save(w));
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "mixed field/fabric phase save/load is exact");
    }

    public static int Run()
    {
        checks = 0;
        FieldAndPotBoundaries();
        MixedTurnConservation();
        Console.WriteLine("PASS: " + checks + " mixed FarmTools/Cloth chronology assertions");
        return checks;
    }
}
