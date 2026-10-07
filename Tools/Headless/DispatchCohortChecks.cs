using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class DispatchCohortChecks
{
    private static int checks;
    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception("Dispatch cohort: " + message);
    }
    private static WorldState Fixture(Technology primary)
    {
        var w = FishingEconomyChecks.Fixture(80);
        w.Config.FissionEnabled = false; w.Config.ImmigrationEnabled = false;
        w.Config.TradeEnabled = false; w.Config.HusbandryTamingEnabled = false;
        w.Config.HusbandryProximityPracticePerYear = 0;
        w.Config.QoLMortalityEnabled = false; w.Config.AnnualHumanGrowthRate = 0;
        var c = w.Camps.Single(); c.Technology = primary;
        c.Technologies[(int)primary] = true;
        c.People.Progression.Era = Era.Neolithic;
        c.EconomyAutomationTurns = -1;
        c.PlantFood = 200000; c.Inventory.HandTools = 1000;
        c.Inventory.Cloth = c.Inventory.LightHousingUnits = 0;
        return w;
    }
    private static Dictionary<int,List<Order>> Dispatch(WorldState w, int count)
    {
        var c=w.Camps.Single();
        return new Dictionary<int,List<Order>> { [c.Player] = new List<Order> {
            new Order(OrderKind.Dispatch,c.Id,population:count) { RequestAutomaticEconomy=true }
        }};
    }
    public static int Run()
    {
        checks=0;
        foreach(var primary in new[]{Technology.Cultivation,Technology.AnimalHusbandry,Technology.Watercraft})
        {
            var w=Fixture(primary); var c=w.Camps.Single();
            string before=SaveCodec.Fingerprint(w);
            var draft=TurnPlanner.Draft(w,0);
            Check(SaveCodec.Fingerprint(w)==before,"draft is pure");
            int departing=c.Population-w.Config.MinimumCamp;
            var prepared=TurnPlanner.Prepare(w,Dispatch(w,departing),null,null,draft);
            var fresh=TurnPlanner.Prepare(w,Dispatch(w,departing));
            Check(prepared.Rejections.Count==0,primary+" dispatch remains legal");
            var intent=prepared.Orders.Single(o=>o.Kind==OrderKind.Economy&&o.Entity==c.Id).Economy;
            var freshIntent=fresh.Orders.Single(o=>o.Kind==OrderKind.Economy&&o.Entity==c.Id).Economy;
            Check(intent.Assignments.Sum(a=>a.Population)<=w.Config.MinimumCamp,
                primary+" automatic work excludes dispatched residents");
            Check(intent.Stamp==freshIntent.Stamp,"draft must not reuse a different resident cohort");
            Check(prepared.Needs[c.Id].Residents==w.Config.MinimumCamp &&
                prepared.Needs[c.Id].FoodConsumers==80,"dispatch changes workers, not total connected food consumers");
            Check(prepared.FishingPlans[c.Id].Residents==w.Config.MinimumCamp,
                "resident fishing plan uses the same reduced cohort");
            Check(SaveCodec.Fingerprint(w)==before && c.PendingDispatchConsumers==0,
                "preparation does not mutate the authoritative population or transient preview on the real camp");

            var view=c.WithPrivateInventory(); view.Population=w.Config.MinimumCamp;
            view.PendingDispatchConsumers=departing;
            var demand=CampProductionDemand.BuildPhysical(w,view,EconomicIntent.From(c),false);
            Check(demand.Consumers==80,"allocator demand retains the dispatched cohort's meals");
            var clone=SaveCodec.Load(SaveCodec.Save(w));
            var parallel=new PhaseExecution(4,1,true);
            var other=TurnPlanner.Prepare(clone,Dispatch(clone,departing),null,parallel);
            new TurnResolver().Resolve(w,prepared);
            new TurnResolver(parallel).Resolve(clone,other);
            Check(w.Camps.Single().Workforce.Assigned<=w.Camps.Single().Population,
                "resolved staffing remains legal without end-of-turn repair");
            Check(w.LinkedPopulation(w.Camps.Single())==80,"dispatch conserves people");
            Check(SaveCodec.Fingerprint(w)==SaveCodec.Fingerprint(clone),
                "scalar/draft and reverse-parallel/save continuation agree");
            TurnResolver.Invariants(w);
        }
        var invalid=Fixture(Technology.Watercraft);
        var rejected=TurnPlanner.Prepare(invalid,Dispatch(invalid,1000));
        Check(rejected.Rejections.Count>0 && rejected.Needs[invalid.Camps.Single().Id].Residents==80,
            "rejected dispatch cannot shrink the economic cohort");
        Console.WriteLine("PASS: "+checks+" dispatch cohort assertions");
        return checks;
    }
}
