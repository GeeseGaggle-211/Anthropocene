using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Anthropocene.Simulation
{
    // A disposable boundary object. It is never saved or exposed to a player policy.
    public sealed class PreparedTurn
    {
        internal readonly WorldState World;
        internal readonly int Turn;
        internal readonly double StartDays;
        internal readonly TurnHorizon Horizon;
        internal readonly List<Order> Orders = new List<Order>();
        internal readonly List<string> OrderReceipts = new List<string>();
        internal readonly List<string> Rejections = new List<string>();
        internal readonly List<DecisionTrace> Traces = new List<DecisionTrace>();
        internal readonly HashSet<int> AutomatedCamps = new HashSet<int>();
        internal readonly HashSet<int> AbandoningVillages = new HashSet<int>();
        internal readonly Dictionary<int, TurnNeeds> Needs = new Dictionary<int, TurnNeeds>();
        internal CampPlacement Placement;

        internal PreparedTurn(WorldState world)
        {
            World = world; Turn = world.Turn; StartDays = world.Days;
            Horizon = TurnHorizon.Next(world);
        }
    }

    public sealed class TurnNeeds
    {
        public int Camp, Residents, LinkedPopulation, FoodConsumers;
        public double OpeningCampFood, OpeningAnimalFood, ConsumptionRequired, ConsumptionTarget, ReserveTarget, ReserveFillTarget, GrowthFoodTarget, FoodTarget;
        public readonly Dictionary<CraftGood, double> Goods = new Dictionary<CraftGood, double>();
        public double PlantFeedstock, PlantFeedstockGap, AnimalFeedstock, AnimalFeedstockGap;
        internal Dictionary<CraftGood, double> Production;
        internal HousingMaterialPlan Housing;
    }

    // A pure WEGO placement pass. Preparation and resolution use exactly the
    // same collision result, so an economic forecast never targets a site that
    // the camp ultimately fails to enter. No world positions are changed here.
    internal sealed class CampPlacement
    {
        public readonly Dictionary<int, int> Destinations = new Dictionary<int, int>();
        public readonly HashSet<int> Blocked = new HashSet<int>();

        public static CampPlacement Resolve(WorldState world, IEnumerable<Order> orders)
        {
            var result = new CampPlacement();
            foreach (var camp in world.Camps.OrderBy(c => c.Id)) result.Destinations[camp.Id] = camp.Tile;
            foreach (var order in orders.Where(o => o.Destination >= 0).OrderBy(o => o.Entity))
                if (result.Destinations.ContainsKey(order.Entity)) result.Destinations[order.Entity] = order.Destination;
            // A camp that already occupies a tile wins, then the lowest ID.
            // Rolling losers back may block another mover, so repeat to a fixed
            // point. Every iteration can only cancel an intended move.
            bool changed;
            do
            {
                changed = false;
                foreach (var group in world.Camps.GroupBy(c => result.Destinations[c.Id]).OrderBy(g => g.Key).Where(g => g.Count() > 1).ToArray())
                {
                    var winner = group.OrderBy(c => c.Tile == group.Key ? 0 : 1).ThenBy(c => c.Id).First();
                    foreach (var loser in group.Where(c => c != winner).OrderBy(c => c.Id))
                        if (result.Destinations[loser.Id] != loser.Tile)
                        {
                            result.Destinations[loser.Id] = loser.Tile;
                            result.Blocked.Add(loser.Id);
                            changed = true;
                        }
                }
            } while (changed);
            return result;
        }
    }

    internal sealed class DraftIntent
    {
        public int Camp, Tile;
        public bool Abandoning;
        public Order Order;
    }

    // Planning done while the human player thinks. Never saved, never part of
    // an observation, and only advisory: Prepare re-validates every key.
    public sealed class PlanningDraft
    {
        public readonly WorldState World;
        public readonly int Turn;
        public readonly double Days;
        public readonly int ExcludedPlayer;
        public readonly PlanningResult Ai;
        internal readonly Dictionary<int, DraftIntent> Intents = new Dictionary<int, DraftIntent>();
        internal PlanningDraft(WorldState world, int excludedPlayer, PlanningResult ai)
        {
            World = world; Turn = world.Turn; Days = world.Days; ExcludedPlayer = excludedPlayer; Ai = ai;
        }
        public bool Matches(WorldState world) => ReferenceEquals(World, world) && Turn == world.Turn && Days == world.Days;
    }

    public static class TurnPlanner
    {
        public static PreparedTurn Prepare(WorldState world, IReadOnlyDictionary<int, List<Order>> submitted,
            IEnumerable<DecisionTrace> traces = null, PhaseExecution execution = null)
            => Prepare(world, submitted, traces, execution, null);

        // A draft is advisory: an intent is reused only when the world stamp and
        // the camp's effective tile and abandoning flag match this preparation.
        // Every other input to the intent is the committed world itself.
        public static PreparedTurn Prepare(WorldState world, IReadOnlyDictionary<int, List<Order>> submitted,
            IEnumerable<DecisionTrace> traces, PhaseExecution execution, PlanningDraft draft)
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
                // Candidate food and field forecasts guide the AI decision,
                // but no order rule reads them. Validation retains the same
                // filtered tiles, entities, economy constraints, and masks.
                var observation = new AgentObservationBuilder().Build(world, camp.Player, index,
                    includeCandidateForecasts: false);
                foreach (var group in orders.Where(o => o != null).GroupBy(o => o.Entity).OrderBy(g => g.Key))
                {
                    if (group.Count() != 1) { prepared.Rejections.Add("Rejected conflicting orders for " + group.Key); continue; }
                    var order = group.First(); string error = OrderRules.Validate(observation, order);
                    prepared.OrderReceipts.Add(camp.Player + ":" + order + ":" + (error ?? "accepted"));
                    if (error != null) prepared.Rejections.Add("Rejected " + order.Entity + ": " + error);
                    else prepared.Orders.Add(order.Copy());
                }
            }
            prepared.Placement = CampPlacement.Resolve(world, prepared.Orders);
            foreach (var camp in world.Camps.Where(c => c.IsVillage))
                if (prepared.Placement.Destinations[camp.Id] != camp.Tile)
                    prepared.AbandoningVillages.Add(camp.Id);
            var aiEconomicRequests = new HashSet<int>(prepared.Orders.Where(o => o.RequestAutomaticEconomy).Select(o => o.Entity));
            var acceptedActions = prepared.Orders.Where(o => o.Kind != OrderKind.Economy).ToDictionary(o => o.Entity);
            var acceptedEconomy = prepared.Orders.Where(o => o.Kind == OrderKind.Economy).ToDictionary(o => o.Entity);
            var automated = world.Camps.Where(c => c.Population > 0 &&
                (c.EconomyAutomationTurns != 0 || aiEconomicRequests.Contains(c.Id)) &&
                CultivationEconomy.EconomyEnabled(c) && !acceptedEconomy.ContainsKey(c.Id))
                .OrderBy(c => c.Id).ToArray();
            // Each forecast owns a private camp preview and reads only the
            // committed world plus prewarmed indexes. Evaluate in parallel,
            // then append accepted intents in stable camp-ID order.
            bool useDraft = draft != null && draft.Matches(world);
            var diagnostics = TurnDiagnostics.For(world);
            var automaticOrders = (execution ?? new PhaseExecution()).Map(automated, camp =>
            {
                int effectiveTile = prepared.Placement.Destinations[camp.Id];
                bool abandoning = prepared.AbandoningVillages.Contains(camp.Id);
                if (useDraft && draft.Intents.TryGetValue(camp.Id, out var cached) &&
                    cached.Tile == effectiveTile && cached.Abandoning == abandoning)
                {
                    Interlocked.Increment(ref diagnostics.DraftIntentsReused);
                    return cached.Order.Copy();
                }
                if (useDraft) Interlocked.Increment(ref diagnostics.DraftIntentsRecomputed);
                return AutomaticIntent(world, camp, effectiveTile, abandoning, index);
            });
            for (int i = 0; i < automated.Length; i++)
            {
                var camp = automated[i]; var order = automaticOrders[i];
                prepared.Orders.Add(order); prepared.AutomatedCamps.Add(camp.Id);
                acceptedEconomy.Add(camp.Id, order);
                prepared.OrderReceipts.Add(camp.Player + ":automation:" + order + ":accepted");
            }
            // Group and index once before bounded per-camp forecasts. The
            // accepted action is the only possible positional order for an
            // existing hunter, and no world position changes during Prepare.
            var huntersByCamp = world.Hunters.GroupBy(h => h.SourceCamp)
                .ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToArray());
            var camps = world.Camps.OrderBy(c => c.Id).ToArray();
            var needsResults = (execution ?? new PhaseExecution()).Map(camps, c =>
                ComputeNeeds(world, c, prepared, acceptedActions, acceptedEconomy, huntersByCamp));
            for (int i = 0; i < camps.Length; i++)
                prepared.Needs[camps[i].Id] = needsResults[i];
            return prepared;
        }

        // Movement and automation occupy different pass slots. An explicit
        // economic order owns the economic slot; an explicit Move, NoOp, or
        // Dispatch does not suppress automation. Shared by Prepare and Draft
        // so both run literally the same code. Pure over the committed world.
        private static Order AutomaticIntent(WorldState world, Camp camp, int effectiveTile, bool abandoning, PlanningIndex index)
        {
            var planningCamp = PlanningCampAt(camp, effectiveTile);
            var settings = EconomicIntent.From(camp);
            if (CultivationEconomy.HusbandryEnabled(camp))
                settings.ManagedBreedingIntensity = ManagedBreedingPolicy.Recommend(world, camp, effectiveTile, index);
            // Relocating a village abandons field work for this turn. The
            // automated intent can use the prior cohort as gatherers;
            // manual assignments remain the player's responsibility.
            var intent = abandoning ? settings.Copy() : EconomicPlanner.Plan(world, planningCamp, settings);
            if (abandoning)
            {
                intent.Assignments.Clear();
                intent.Labor = null;
                intent.Focus = -1;
            }
            var constraints = new EconomicConstraints
            {
                Agriculture = CultivationEconomy.Enabled(camp), Husbandry = CultivationEconomy.HusbandryEnabled(camp),
                Population = camp.Population, Technologies = camp.People.NeolithicTechnologies.ToArray(),
                FocusTiles = CultivationEconomy.Footprint(world, planningCamp).ToArray()
            };
            string error = EconomicIntents.Validate(constraints, intent);
            if (error != null) throw new InvalidOperationException("Invalid automatic economic plan: " + error);
            return new Order(OrderKind.Economy, camp.Id) { Economy = intent };
        }

        // Touch every lazily created world-attached cache on the calling
        // (UI) thread so a worker draft only reads them. See the audit in
        // Docs/ValueModelImplementationPlan.md WP1.
        public static void Prewarm(WorldState world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            TurnDiagnostics.For(world);
            if (world.Resources != null) MaterialAccessIndex.For(world);
            var map = world.Map;
            foreach (var camp in world.Camps) { var people = camp.People; var gatherers = camp.GathererPopulation; }
        }

        // Runs the AI policies (excluding the human player) and the automatic
        // Economy intents that would result if the excluded player issued no
        // orders. Reads only the committed world; nothing is written to it.
        public static PlanningDraft Draft(WorldState world, int excludedPlayer, PhaseExecution execution = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            Prewarm(world);
            var ai = AiPlanning.Decide(world, execution, excludedPlayer);
            var orders = ai.Orders.OrderBy(p => p.Key).SelectMany(p => p.Value).Where(o => o != null).ToArray();
            var placement = CampPlacement.Resolve(world, orders);
            var abandoning = new HashSet<int>();
            foreach (var camp in world.Camps.Where(c => c.IsVillage))
                if (placement.Destinations[camp.Id] != camp.Tile) abandoning.Add(camp.Id);
            var requests = new HashSet<int>(orders.Where(o => o.RequestAutomaticEconomy).Select(o => o.Entity));
            var explicitEconomy = new HashSet<int>(orders.Where(o => o.Kind == OrderKind.Economy).Select(o => o.Entity));
            var automated = world.Camps.Where(c => c.Population > 0 &&
                (c.EconomyAutomationTurns != 0 || requests.Contains(c.Id)) &&
                CultivationEconomy.EconomyEnabled(c) && !explicitEconomy.Contains(c.Id))
                .OrderBy(c => c.Id).ToArray();
            var index = new PlanningIndex(world);
            var results = (execution ?? new PhaseExecution()).Map(automated, camp =>
            {
                int tile = placement.Destinations[camp.Id]; bool abandon = abandoning.Contains(camp.Id);
                return new DraftIntent { Camp = camp.Id, Tile = tile, Abandoning = abandon,
                    Order = AutomaticIntent(world, camp, tile, abandon, index) };
            });
            var draft = new PlanningDraft(world, excludedPlayer, ai);
            foreach (var result in results) draft.Intents[result.Camp] = result;
            return draft;
        }

        private static TurnNeeds ComputeNeeds(WorldState world, Camp camp, PreparedTurn prepared,
            IReadOnlyDictionary<int, Order> acceptedActions, IReadOnlyDictionary<int, Order> acceptedEconomy,
            IReadOnlyDictionary<int, Hunter[]> huntersByCamp)
        {
            var planningCamp = PlanningCampAt(camp, prepared.Placement.Destinations[camp.Id]);
            // Dispatch changes who remains a camp resident, but creates no
            // new person. Goods and QoL follow those staying at the camp;
            // the departing cohort follows the hunter provision rules.
            acceptedActions.TryGetValue(camp.Id, out var campAction);
            int departing = campAction?.Kind == OrderKind.Dispatch ? campAction.Population : 0;
            int residents = Math.Max(0, camp.Population - departing);
            int connectedHunters = 0, linkedPopulation = camp.Population;
            if (huntersByCamp.TryGetValue(camp.Id, out var hunters))
                foreach (var hunter in hunters)
                {
                    linkedPopulation += hunter.Population;
                    acceptedActions.TryGetValue(hunter.Id, out var hunterAction);
                    int destination = hunterAction != null && hunterAction.Destination >= 0 ? hunterAction.Destination : hunter.Tile;
                    if (Supply.InRange(world, planningCamp, destination)) connectedHunters += hunter.Population;
                }
            // A Dispatch moves existing residents into a new, connected group;
            // it does not create extra consumers or reduce their biological need.
            int foodConsumers = residents + departing + connectedHunters;
            double intervals = prepared.Horizon.ReferenceIntervals, years = prepared.Horizon.Years;
            double consumptionRequired = foodConsumers * world.Config.Consumption * intervals;
            TurnResolver.FoodBucketTargets(world, planningCamp, intervals, years, consumptionRequired, camp.Food,
                out double consumptionTarget, out double reserveTarget, out double reserveFillTarget, out double growthFoodTarget);
            var needs = new TurnNeeds
            {
                Camp = camp.Id, Residents = residents, LinkedPopulation = linkedPopulation,
                FoodConsumers = foodConsumers, OpeningCampFood = camp.Food, OpeningAnimalFood = camp.AnimalFood,
                ConsumptionRequired = consumptionRequired,
                ConsumptionTarget = consumptionTarget, ReserveTarget = reserveTarget,
                ReserveFillTarget = reserveFillTarget, GrowthFoodTarget = growthFoodTarget,
                FoodTarget = consumptionTarget + reserveFillTarget + growthFoodTarget
            };
            if (residents > 0 && CultivationEconomy.EconomyEnabled(camp))
            {
                var economy = acceptedEconomy.TryGetValue(camp.Id, out var economyOrder) ? economyOrder.Economy : EconomicIntent.From(camp);
                var snapshot = EconomicPlanner.Preview(planningCamp, economy, residents);
                var recipes = NeolithicProduction.Recipes(world.Config);
                var sources = MaterialEconomy.KnownSources(world, snapshot, world.Days);
                foreach (var recipe in recipes)
                    needs.Goods[recipe.Good] = NeolithicProduction.Priority(snapshot, recipe.Good) > 0
                        ? NeolithicProduction.Demand(world, snapshot, recipe.Good, years, residents, false, false, sources) : 0;
                double equipment = new ToolCoverageBudget(world, snapshot, prepared.Horizon).Remaining;
                needs.Production = MaterialEconomy.Forecast(world, snapshot, recipes, prepared.Horizon, equipment, true, residents, false, false, -1, -1, sources);
                needs.Housing = MaterialEconomy.PlanHousingMaterials(world, snapshot, recipes, needs.Production, years, world.Days, sources);
                needs.PlantFeedstock = needs.Production[CraftGood.Cloth] * world.Config.Neolithic.PlantFoodPerFabricUnit;
                needs.AnimalFeedstock = needs.Production[CraftGood.AnimalFabric];
                // Only inventory above this cohort's consumption and reserve
                // can substitute for a separately harvested craft input.
                double spareFood = Math.Max(0, camp.Food - needs.ConsumptionRequired - needs.ReserveTarget);
                double sparePlant = Math.Min(camp.PlantFood, spareFood);
                double spareAnimal = Math.Min(camp.AnimalFood, Math.Max(0, spareFood - sparePlant));
                needs.PlantFeedstockGap = Math.Max(0, needs.PlantFeedstock - sparePlant);
                needs.AnimalFeedstockGap = Math.Max(0, needs.AnimalFeedstock - spareAnimal);
            }
            return needs;
        }

        private static Camp PlanningCampAt(Camp camp, int tile)
        {
            if (tile == camp.Tile) return camp;
            // Preview makes a private camp object with copied inventory and
            // assignments. Its position may be changed without mutating the
            // authoritative camp or any persisted collection.
            var copy = EconomicPlanner.Preview(camp, EconomicIntent.From(camp), camp.Population);
            copy.Tile = tile;
            copy.Player = camp.Player; copy.SocietyId = camp.SocietyId;
            copy.DeficitTurns = camp.DeficitTurns; copy.DemographicGrowthMultiplier = camp.DemographicGrowthMultiplier;
            copy.GrowthRemainder = camp.GrowthRemainder; copy.Moves = camp.Moves;
            copy.StationaryTurns = camp.StationaryTurns; copy.StationaryYears = camp.StationaryYears;
            copy.IsVillage = camp.IsVillage;
            if (tile != camp.Tile && camp.IsVillage) copy.IsVillage = false;
            return copy;
        }
    }
}
