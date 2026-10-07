using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class PreparedResidentCohortChecks
{
    private static int checks;
    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception("Prepared resident cohort: " + message);
    }
    private static WorldState Fixture()
    {
        var w = NeolithicChecks.Fixture();
        w.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        w.Config.NeolithicFishingEnabled = true;
        w.Config.CorrectedAquaticSeasonalPressureEnabled = true;
        w.Config.HusbandryProximityPracticePerYear = 0;
        w.Config.HusbandryTamingEnabled = false;
        var c = w.Camps.Single();
        c.Population = 100; c.EconomyAutomationTurns = -1;
        c.PlantFood = c.AnimalFood = c.AquaticFood = 0;
        c.Technologies[(int)Technology.Watercraft] = true;
        c.Workforce.Assignments.Clear();
        foreach (int tile in w.Map.Radius(c.Tile, 1))
        {
            w.Tiles[tile].CultivationCapital = 1;
            w.Tiles[tile].Cultivation = .4f;
            w.Tiles[tile].CultivationOperator = c.Id;
            w.Tiles[tile].CultivationOperatingFraction = 1;
        }
        return w;
    }
    private static Dictionary<int, List<Order>> Orders(WorldState w)
    {
        var c = w.Camps.Single();
        return new Dictionary<int, List<Order>> { [c.Player] = new List<Order>
        { new Order(OrderKind.Dispatch, c.Id, population: 94) } };
    }
    private static void CheckPrepared(WorldState w, PreparedTurn p)
    {
        var c = w.Camps.Single();
        Check(p.Rejections.Count == 0, "dispatch and automation remain legal together");
        var e = p.Orders.Single(o => o.Kind == OrderKind.Economy).Economy;
        Console.WriteLine("COHORT prepared residents=" + p.Needs[c.Id].Residents +
            " consumers=" + p.Needs[c.Id].FoodConsumers +
            " assigned=" + e.Assignments.Sum(a => a.Population));
        Check(e.Assignments.Sum(a => a.Population) <= 6,
            "automatic assignments must fit the post-dispatch resident cohort");
        Check(p.Needs[c.Id].Residents == 6 && p.Needs[c.Id].FoodConsumers == 100,
            "dispatch removes local workers, not biological consumers");
        Check(Math.Abs(p.Needs[c.Id].ConsumptionRequired -
            100 * w.Config.Consumption * w.Config.GenerationIntervals) < 1e-8,
            "prepared consumption includes the connected departing cohort");
        Check(p.FishingPlans[c.Id].Residents == 6,
            "resident Fishing preparation uses the same reduced workforce");
    }
    public static int Run()
    {
        checks = 0;
        var fresh = Fixture(); var cached = SaveCodec.Load(SaveCodec.Save(fresh));
        string before = SaveCodec.Fingerprint(fresh);
        var fp = TurnPlanner.Prepare(fresh, Orders(fresh));
        CheckPrepared(fresh, fp);
        Check(SaveCodec.Fingerprint(fresh) == before, "preparation does not mutate population or workforce");
        var draft = TurnPlanner.Draft(cached, cached.Camps.Single().Player);
        var cp = TurnPlanner.Prepare(cached, Orders(cached), null, null, draft);
        CheckPrepared(cached, cp);
        Check(fp.Orders.Single(o => o.Kind == OrderKind.Economy).Economy.Stamp ==
            cp.Orders.Single(o => o.Kind == OrderKind.Economy).Economy.Stamp,
            "a cached no-dispatch plan is recomputed when dispatch changes the resident cohort");
        new TurnResolver().Resolve(fresh, fp); new TurnResolver().Resolve(cached, cp);
        Check(fresh.Camps.Single().Workforce.Assigned <= fresh.Camps.Single().Population,
            "resolved staffing stays feasible without a late worker-count repair");
        Check(SaveCodec.Fingerprint(fresh) == SaveCodec.Fingerprint(cached),
            "cached and fresh dispatch resolutions match");
        Console.WriteLine("PASS: " + checks + " prepared resident cohort assertions");
        return checks;
    }
}
