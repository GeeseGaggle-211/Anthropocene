using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // A disposable boundary object. It is never saved or exposed to a player policy.
    public sealed class PreparedTurn
    {
        internal readonly WorldState World;
        internal readonly int Turn;
        internal readonly double StartDays;
        internal readonly List<Order> Orders = new List<Order>();
        internal readonly List<string> OrderReceipts = new List<string>();
        internal readonly List<string> Rejections = new List<string>();
        internal readonly List<DecisionTrace> Traces = new List<DecisionTrace>();
        internal readonly HashSet<int> AutomatedCamps = new HashSet<int>();
        internal readonly Dictionary<int, TurnNeeds> Needs = new Dictionary<int, TurnNeeds>();

        internal PreparedTurn(WorldState world) { World = world; Turn = world.Turn; StartDays = world.Days; }
    }

    public sealed class TurnNeeds
    {
        public int Camp, Residents, LinkedPopulation;
        public double FoodTarget;
        public readonly Dictionary<CraftGood, double> Goods = new Dictionary<CraftGood, double>();
        public double PlantFeedstock, PlantFeedstockGap, AnimalFeedstock;
        internal Dictionary<CraftGood, double> Production;
        internal HousingMaterialPlan Housing;
    }

    public static class TurnPlanner
    {
        public static PreparedTurn Prepare(WorldState world, IReadOnlyDictionary<int, List<Order>> submitted,
            IEnumerable<DecisionTrace> traces = null, PhaseExecution execution = null)
        {
            if (world == null || submitted == null) throw new ArgumentNullException(world == null ? nameof(world) : nameof(submitted));
            TurnDiagnostics.For(world);
            if (world.Resources != null) MaterialAccessIndex.For(world);
            var prepared = new PreparedTurn(world);
            if (traces != null) prepared.Traces.AddRange(traces.OrderBy(t => t.Player));
            var index = new PlanningIndex(world);
            foreach (var camp in world.Camps.GroupBy(c => c.Player).OrderBy(g => g.Key).Select(g => g.OrderBy(c => c.Id).First()))
            {
                if (!submitted.TryGetValue(camp.Player, out var orders)) continue;
                var observation = new AgentObservationBuilder().Build(world, camp.Player, index);
                foreach (var group in orders.Where(o => o != null).GroupBy(o => o.Entity).OrderBy(g => g.Key))
                {
                    if (group.Count() != 1) { prepared.Rejections.Add("Rejected conflicting orders for " + group.Key); continue; }
                    var order = group.First(); string error = OrderRules.Validate(observation, order);
                    prepared.OrderReceipts.Add(camp.Player + ":" + order + ":" + (error ?? "accepted"));
                    if (error != null) prepared.Rejections.Add("Rejected " + order.Entity + ": " + error);
                    else prepared.Orders.Add(order.Copy());
                }
            }
            foreach (var camp in world.Camps.Where(c => c.Population > 0 && c.EconomyAutomationTurns != 0 && CultivationEconomy.EconomyEnabled(c)).OrderBy(c => c.Id))
            {
                // A submitted camp order owns this turn's choice, even if it was rejected.
                if (submitted.TryGetValue(camp.Player, out var orders) && orders.Any(o => o != null && o.Entity == camp.Id)) continue;
                var intent = EconomicPlanner.Plan(world, camp, EconomicIntent.From(camp));
                var constraints = new EconomicConstraints
                {
                    Agriculture = CultivationEconomy.Enabled(camp), Husbandry = CultivationEconomy.HusbandryEnabled(camp),
                    Population = camp.Population, Technologies = camp.People.NeolithicTechnologies.ToArray(),
                    FocusTiles = CultivationEconomy.Footprint(world, camp).ToArray()
                };
                string error = EconomicIntents.Validate(constraints, intent);
                if (error != null) throw new InvalidOperationException("Invalid automatic economic plan: " + error);
                var order = new Order(OrderKind.Economy, camp.Id) { Economy = intent };
                prepared.Orders.Add(order); prepared.AutomatedCamps.Add(camp.Id);
                prepared.OrderReceipts.Add(camp.Player + ":automation:" + order + ":accepted");
            }
            var camps = world.Camps.OrderBy(c => c.Id).ToArray();
            var needsResults = (execution ?? new PhaseExecution()).Map(camps, c => ComputeNeeds(world, c, prepared));
            for (int i = 0; i < camps.Length; i++)
                prepared.Needs[camps[i].Id] = needsResults[i];
            return prepared;
        }

        private static TurnNeeds ComputeNeeds(WorldState world, Camp camp, PreparedTurn prepared)
        {
            // Dispatch changes who remains a camp resident, but creates no
            // new person. Goods and QoL follow those staying at the camp;
            // the departing cohort follows the hunter provision rules.
            int departing = prepared.Orders.Where(o => o.Kind == OrderKind.Dispatch && o.Entity == camp.Id)
                .Sum(o => o.Population);
            int residents = Math.Max(0, camp.Population - departing);
            var needs = new TurnNeeds
            {
                Camp = camp.Id, Residents = residents, LinkedPopulation = world.LinkedPopulation(camp),
                FoodTarget = TurnResolver.PlanningFoodDemand(world, camp)
            };
            if (residents > 0 && CultivationEconomy.EconomyEnabled(camp))
            {
                var economy = prepared.Orders.FirstOrDefault(o => o.Kind == OrderKind.Economy && o.Entity == camp.Id)?.Economy ?? EconomicIntent.From(camp);
                var snapshot = EconomicPlanner.Preview(camp, economy, residents);
                double intervals = world.Config.GenerationIntervals, years = world.Config.TurnDays / world.Config.DaysPerYear;
                var recipes = NeolithicProduction.Recipes(world.Config);
                var sources = MaterialEconomy.KnownSources(world, snapshot, world.Days);
                foreach (var recipe in recipes)
                    needs.Goods[recipe.Good] = NeolithicProduction.Priority(snapshot, recipe.Good) > 0
                        ? NeolithicProduction.Demand(world, snapshot, recipe.Good, years, residents, false, false, sources) : 0;
                double equipment = new ToolCoverageBudget(world, snapshot, intervals).Remaining;
                needs.Production = MaterialEconomy.Forecast(world, snapshot, recipes, intervals, years, equipment, true, residents, false, false, -1, -1, sources);
                needs.Housing = MaterialEconomy.PlanHousingMaterials(world, snapshot, recipes, needs.Production, years, world.Days, sources);
                needs.PlantFeedstock = needs.Production[CraftGood.Cloth] * world.Config.Neolithic.PlantFoodPerFabricUnit;
                needs.AnimalFeedstock = needs.Production[CraftGood.AnimalFabric];
                // Only inventory above this cohort's consumption and reserve
                // can substitute for a separately harvested craft input.
                int foodConsumers = camp.Population + world.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(world, h)).Sum(h => h.Population);
                double reserve = Math.Min(NeolithicProduction.DesiredReserveFood(world, foodConsumers, intervals),
                    NeolithicProduction.StorageCapacityAfterPotDepreciation(world, snapshot, intervals, years));
                double sparePlant = Math.Min(camp.PlantFood,
                    Math.Max(0, camp.Food - foodConsumers * world.Config.Consumption * intervals - reserve));
                needs.PlantFeedstockGap = Math.Max(0, needs.PlantFeedstock - sparePlant);
            }
            return needs;
        }
    }
}
