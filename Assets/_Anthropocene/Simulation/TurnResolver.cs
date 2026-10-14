using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class Demand
    {
        public int Tile, Entity;
        public string Kind;
        public bool Manufacturing;
        public double Food, PotentialFood, Freshwater;
        public double ImpactMultiplier = 1;
    }
    public interface IAllocationPolicy { List<Transfer> Allocate(IEnumerable<Demand> demands, Tile[] tiles); }
    public sealed class ProportionalAllocation : IAllocationPolicy
    {
        public List<Transfer> Allocate(IEnumerable<Demand> demands, Tile[] tiles)
        {
            var result = new List<Transfer>();
            foreach (var group in demands.GroupBy(d => d.Tile).OrderBy(g => g.Key))
            {
                var sorted = group.OrderBy(d => d.Entity).ThenBy(d => d.Kind, StringComparer.Ordinal).ToArray();
                double food = sorted.Sum(d => d.Food), potential = sorted.Sum(d => d.PotentialFood), water = sorted.Sum(d => d.Freshwater);
                double supplyFood = Math.Min(tiles[group.Key].Food, food);
                // Human-accessible food is part of total biomass. Reserve the amount actually gathered before animals draw on the wider pool.
                double supplyPotential = Math.Min(Math.Max(0, tiles[group.Key].PotentialFood - supplyFood), potential);
                double supplyWater = Math.Min(tiles[group.Key].DrinkableWater, water);
                double leftFood = supplyFood, leftPotential = supplyPotential, leftWater = supplyWater;
                int lastFood = Array.FindLastIndex(sorted, d => d.Food > 0), lastPotential = Array.FindLastIndex(sorted, d => d.PotentialFood > 0), lastWater = Array.FindLastIndex(sorted, d => d.Freshwater > 0);
                for (int i = 0; i < sorted.Length; i++)
                {
                    var d = sorted[i];
                    double f = food <= 0 ? 0 : Math.Min(leftFood, supplyFood * d.Food / food);
                    double p = potential <= 0 ? 0 : Math.Min(leftPotential, supplyPotential * d.PotentialFood / potential);
                    double w = water <= 0 ? 0 : Math.Min(leftWater, supplyWater * d.Freshwater / water);
                    // Stable last claimant absorbs only floating-point rounding residue.
                    if (i == lastFood) f = leftFood; if (i == lastPotential) p = leftPotential; if (i == lastWater) w = leftWater;
                    leftFood -= f; leftPotential -= p; leftWater -= w;
                    result.Add(new Transfer { Tile = d.Tile, Entity = d.Entity, Kind = d.Kind, Food = f, PotentialFood = p, Freshwater = w });
                }
            }
            return result;
        }
    }
    public sealed partial class TurnResolver
    {
        // This resolution's trade settlement (Docs/Trade.md): opening legs settled, after-production legs pending.
        private TradeSettlement trade;
        private IReadOnlyDictionary<int, double> resolvedFisheryOperations =
            new Dictionary<int, double>();
        private IReadOnlyDictionary<int, ElderCompiledDecision>
            activeElderDecisions =
                new Dictionary<int, ElderCompiledDecision>();
        private readonly PhaseExecution execution;
        public ResolutionMetrics LastMetrics { get; private set; } = new ResolutionMetrics();
        public TurnResolver(PhaseExecution execution = null) { this.execution = execution ?? new PhaseExecution(); }


        public string Validate(WorldState w, int player, Order order, HashSet<int> visible = null) => OrderRules.Validate(new AgentObservationBuilder().Build(w, player), order);

        public TurnRecord Resolve(WorldState w, IReadOnlyDictionary<int, List<Order>> submitted, IEnumerable<DecisionTrace> traces = null)
            => Resolve(w, TurnPlanner.Prepare(w, submitted, traces, execution));

        public TurnRecord Resolve(WorldState w, PreparedTurn prepared)
        {
            if (prepared == null || !ReferenceEquals(w, prepared.World) || w.Turn != prepared.Turn || Math.Abs(w.Days - prepared.StartDays) > 1e-8 || Math.Abs(w.Config.TurnDays - prepared.Horizon.Days) > 1e-8)
                throw new InvalidOperationException("Prepared turn does not match the current world boundary.");
            using var profile = PhaseProfile.Scope("Resolve");
            var resolutionTimer = System.Diagnostics.Stopwatch.StartNew(); LastMetrics = new ResolutionMetrics(); trade = null;
            resolvedFisheryOperations = new Dictionary<int, double>();
            activeElderDecisions = prepared.ElderDecisions;
            Ownership.Bind(w);
            if (w.Camps.All(c => w.LinkedPopulation(c) <= 0)) throw new InvalidOperationException("All tribes have perished.");
            w.Config.Validate();
            var planning = new PlanningIndex(w);
            int peopleBefore = w.Camps.Sum(w.LinkedPopulation);
            var horizon = prepared.Horizon;
            var record = new TurnRecord { Turn = w.Turn, Seed = w.Seed, StartDays = horizon.StartDays, Duration = horizon.Days };
            double time = horizon.ReferenceIntervals;
            record.Decisions.AddRange(prepared.Traces);
            // Groups formed during this resolution cannot act until the next planning phase.
            var establishedHunters = new HashSet<int>(w.Hunters.Where(h => h.Population > 0).Select(h => h.Id));
            var locked = prepared.Orders;
            record.Orders.AddRange(prepared.OrderReceipts);
            foreach (string rejection in prepared.Rejections) record.Log(rejection);
            // Herd intentions are computed before dispatch or movement reveals new hunter positions.
            // Fission policy is a committed camp setting; it applies before this turn's Close-phase fission pass.
            foreach (var order in locked.Where(o => o.Kind == OrderKind.FissionPolicy).OrderBy(o => o.Entity))
            {
                var camp = w.Camps.First(c => c.Id == order.Entity);
                camp.FissionPolicy = (FissionPolicy)order.Target;
            }
            foreach (var order in locked.Where(o => o.Kind == OrderKind.Economy).OrderBy(o => o.Entity))
            {
                var camp = w.Camps.First(c => c.Id == order.Entity);
                EconomicIntents.Apply(camp, order.Economy);
                if (camp.EconomyAutomationTurns > 0) camp.EconomyAutomationTurns--;
            }
            // Goods storage (Docs/PlannerTradeStorageDesign.md §3): each camp's storage terms for this turn, from its prepared
            // needs, before any settlement or production adds goods. Cleared by the end-of-turn storage step.
            if (GoodsStorage.Enabled(w.Config))
            {
                var storageRecipes = NeolithicProduction.Recipes(w.Config);
                foreach (var camp in w.Camps.OrderBy(c => c.Id))
                {
                    prepared.Needs.TryGetValue(camp.Id, out var campNeeds);
                    double equipmentOutputs = 0, housingAssembly = 0;
                    if (campNeeds?.Production != null)
                        GoodsStorage.PlannedUse(storageRecipes, g => campNeeds.Production.TryGetValue(g, out double q) ? q : 0, out equipmentOutputs, out housingAssembly);
                    camp.GoodsStorage = GoodsStorage.Begin(w, camp, horizon.Years, campNeeds?.Residents ?? camp.Population, equipmentOutputs, housingAssembly);
                }
            }
            // Confirmed trades settle from committed stock before dispatch, provisions, production and needs (WP7):
            // every later step sees the post-trade stock the planner and prepared needs saw.
            var tradeTimer = System.Diagnostics.Stopwatch.StartNew(); using (PhaseProfile.Scope("Trade.Settle")) trade = Trade.Settle(w, prepared.Trade, record); LastMetrics.TradeSettleMilliseconds = tradeTimer.Elapsed.TotalMilliseconds;
            LastMetrics.TradePlanMilliseconds = prepared.TradeMilliseconds;
            if (prepared.Trade != null)
            {
                var activity = prepared.Trade.Summary;
                LastMetrics.TradeEligible = activity.Eligible; LastMetrics.TradeRequisitions = activity.Requisitions;
                LastMetrics.TradeHonoured = activity.Honoured; LastMetrics.TradeCountered = activity.Countered; LastMetrics.TradeIgnored = activity.Ignored;
            }
            var phaseTimer = System.Diagnostics.Stopwatch.StartNew();
            var herdInputs = w.Herds.OrderBy(h => h.Id).ToArray();
            int[] destinations; using (PhaseProfile.Scope("HerdIntents")) destinations = execution.Map(herdInputs, h => HerdDestination(w, h, planning));
            var herdMoves = herdInputs.Select((h, i) => new { h.Id, Destination = destinations[i] }).ToDictionary(x => x.Id, x => x.Destination);
            LastMetrics.HerdIntentMilliseconds = phaseTimer.Elapsed.TotalMilliseconds;
            foreach (var order in locked.Where(o => o.Kind == OrderKind.Dispatch).OrderBy(o => o.Entity))
            {
                var camp = w.Camps.First(c => c.Id == order.Entity); camp.Population -= order.Population;
                var hunter = new Hunter { Id = w.NextId++, SourceCamp = camp.Id, SocietyId = camp.SocietyId, Player = camp.Player, Population = order.Population, Tile = camp.Tile };
                w.Hunters.Add(hunter); record.Log("Tribe " + camp.Player + " dispatched " + hunter.Population + " hunters [" + hunter.Id + "].");
            }
            using (PhaseProfile.Scope("Movement"))
            {
                Supply.ProvisionDepartures(w, locked, record, prepared.Placement);
                Move(w, locked, herdMoves, prepared.Placement, prepared.AbandoningVillages,
                    planning, record);
                Supply.DepositReturns(w, record);
            }
            var touched = new Dictionary<int, TileChange>();
            Action<int> touch = id => { if (!touched.ContainsKey(id)) touched[id] = new TileChange { Tile = id, BeforeFood = w.Tiles[id].Food, BeforePotentialFood = w.Tiles[id].PotentialFood, BeforeFreshwater = w.Tiles[id].Freshwater, BeforeLandCondition = w.Tiles[id].LandCondition, BeforeAquaticCondition = w.Tiles[id].AquaticCondition }; };
            using (PhaseProfile.Scope("Economy")) ResolveEcology(w, locked, establishedHunters, record, touch,
                prepared.Needs, prepared.AbandoningVillages, prepared.FishingPlans);
            Trade.Finish(w, trade, record); LastMetrics.TradesSettled = record.Trades.Count; trade = null;
            foreach (var order in locked.Where(o => o.Kind == OrderKind.Reintegrate).OrderBy(o => o.Entity))
            {
                var hunter = w.Hunters.FirstOrDefault(h => h.Id == order.Entity); if (hunter == null) continue;
                var camp = w.Camps.First(c => c.Id == hunter.SourceCamp);
                if (hunter.Tile != camp.Tile) { record.Log("Reintegration deferred: camp moved away from " + hunter.Id + "."); continue; }
                camp.Population += hunter.Population; w.Hunters.Remove(hunter); record.Log("Hunters " + hunter.Id + " rejoined their camp.");
            }
            w.Hunters.RemoveAll(h => h.Population <= 0);
            Ownership.Bind(w);
            // Local herd contact is an end-boundary knowledge receipt. It is
            // separate from hunting performance and can unlock Husbandry at the
            // same Close boundary as trade-derived knowledge.
            TechnologyDiscovery.RecordHusbandryProximity(w);
            foreach (var camp in w.Camps.OrderBy(c => c.Id)) BreakthroughBound(w, camp, record);
            using (PhaseProfile.Scope("Technology")) TechnologyDiscovery.Resolve(w, record);
            // Reconcile compiled Elder targets against the authoritative
            // post-economy state. Do this before immigration/fission so those
            // social processes remain observed outcomes, not action repairs.
            foreach (var decision in prepared.ElderDecisions
                .OrderBy(pair => pair.Key).Select(pair => pair.Value))
            {
                ElderDecisionRealization.Reconcile(w, decision);
                ElderInstitutionMemorySystem.RecordDecision(w, decision,
                    horizon.StartDays);
            }
            activeElderDecisions =
                new Dictionary<int, ElderCompiledDecision>();
            foreach (var camp in w.Camps)
                if (camp.EconomicLabor != null)
                {
                    camp.EconomicLabor.PolicyAuthority = false;
                    camp.EconomicLabor.PostGrowthHerdRetentionSet = false;
                    camp.EconomicLabor.PostGrowthHerdRetention = 0;
                }
            // Close phase, after demography and progression: freeze the post-demography snapshot, then fission (Docs/Fission.md).
            // Immigration between known settlements precedes fission (QoLFissionTradeDesign 4, 9); its sources and arrivals sit out fission this turn.
            HashSet<int> mobile; using (PhaseProfile.Scope("Immigration")) mobile = Immigration.Resolve(w, record, execution, LastMetrics);
            using (PhaseProfile.Scope("Fission")) Fission.Resolve(w, record, execution, LastMetrics, mobile);
            foreach (var change in touched.Values.OrderBy(t => t.Tile))
            { change.AfterFood = w.Tiles[change.Tile].Food; change.AfterPotentialFood = w.Tiles[change.Tile].PotentialFood; change.AfterFreshwater = w.Tiles[change.Tile].Freshwater; change.AfterLandCondition = w.Tiles[change.Tile].LandCondition; change.AfterAquaticCondition = w.Tiles[change.Tile].AquaticCondition; record.Changes.Add(change); }
            // Untouched tiles follow the recorded configuration/clock regeneration rule; replay does not need world-sized snapshots each turn.
            using (PhaseProfile.Scope("Invariants")) Invariants(w, peopleBefore + record.Growth - record.Deaths, touched.Keys.Select(id => w.Tiles[id]));
            // Per-claim pressure and numerical balances are transient phase diagnostics.
            // Persisted transfer totals, condition changes and demographics remain intact.
            if (w.History.Count > 0) { w.History[w.History.Count - 1].Pressure?.Clear(); w.History[w.History.Count - 1].EcologyBalances?.Clear(); }
            // Transfers have no seasonal timestamp. Collapse repeated receipts
            // for the same claimant/tile/kind before persisting the turn; the
            // realized totals and every authoritative stock were already paid.
            record.Transfers = record.Transfers.GroupBy(t => new { t.Tile, t.Entity, t.Kind })
                .OrderBy(g => g.Key.Tile).ThenBy(g => g.Key.Entity).ThenBy(g => g.Key.Kind, StringComparer.Ordinal)
                .Select(g => new Transfer { Tile = g.Key.Tile, Entity = g.Key.Entity, Kind = g.Key.Kind,
                    Food = g.Sum(t => t.Food), PotentialFood = g.Sum(t => t.PotentialFood), Freshwater = g.Sum(t => t.Freshwater) }).ToList();
            // Remembered contact knowledge: end-of-turn, after movement and placement, from the post-turn vision.
            phaseTimer.Restart(); using (PhaseProfile.Scope("ContactKnowledge"))
            {
                ContactKnowledge.EndTurn(w, execution);
                UpdateFishingGroundKnowledge(w, prepared.FishingPlans, record, resolvedFisheryOperations);
            }
            LastMetrics.KnowledgeMilliseconds = phaseTimer.Elapsed.TotalMilliseconds;
            w.History.Add(record);
            AutonomousElderHistory.CloseBoundary(w, prepared.AutonomousDecisions, record);
            foreach (var camp in w.Camps.Where(camp =>
                camp.ElderMemory != null).OrderBy(camp => camp.Id))
                ElderInstitutionMemorySystem.ObserveBoundary(w, camp, record);
            w.Turn++;
            var diagnostics = w.Diagnostics;
            if (diagnostics != null && diagnostics.Turn == prepared.Turn)
            {
                LastMetrics.PlanCalls = diagnostics.PlanCalls;
                LastMetrics.ForecastCalls = diagnostics.ForecastCalls;
                LastMetrics.LocalSourceQueries = diagnostics.LocalSourceQueries;
                LastMetrics.SourceLookups = diagnostics.SourceLookups;
                LastMetrics.DepositEdges = diagnostics.DepositEdges;
                LastMetrics.BodyScans = diagnostics.BodyScans;
                LastMetrics.FoodForecastQueries = diagnostics.FoodForecastQueries;
                LastMetrics.FoodInvestmentComparisons = diagnostics.FoodInvestmentComparisons;
                LastMetrics.SharedAllocations = diagnostics.SharedAllocations;
                LastMetrics.SharedOperationChoices = diagnostics.SharedOperationChoices;
                LastMetrics.SharedHerdChoices = diagnostics.SharedHerdChoices;
                LastMetrics.ExpansionLadderBuilds = diagnostics.ExpansionLadderBuilds;
                LastMetrics.HerdLadderBuilds = diagnostics.HerdLadderBuilds;
                LastMetrics.PastureScans = diagnostics.PastureScans;
                LastMetrics.PlannerTileProjections = diagnostics.PlannerTileProjections;
                LastMetrics.PlannerEvaluations = diagnostics.PlannerEvaluations;
                LastMetrics.SharedSelections = diagnostics.SharedSelections;
                LastMetrics.KernelCacheHits = diagnostics.KernelCacheHits;
                LastMetrics.SharedAllocationMilliseconds = diagnostics.SharedAllocationTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                LastMetrics.PlanMilliseconds = diagnostics.PlanTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
            LastMetrics.TotalMilliseconds = resolutionTimer.Elapsed.TotalMilliseconds; return record;
        }

        public static int HerdDestination(WorldState w, Herd herd, PlanningIndex index = null)
        {
            if (herd.DomesticCamp >= 0) return herd.Tile;
            var map = w.Map; index = index ?? new PlanningIndex(w); var hunters = index.VisibleHunters(map.Radius(herd.Tile, 3)).ToArray();
            bool threat = hunters.Any(h => map.Distance(herd.Tile, h.Tile) <= 1);
            var species = w.Config.GetSpecies(herd.Species);
            double foodDemand = herd.Animals * species.FoodConsumption * Math.Min(w.Config.TurnDays, w.Config.BaseDays) / w.Config.BaseDays;
            double waterDemand = herd.Animals * species.FreshwaterConsumption * Math.Min(w.Config.TurnDays, w.Config.BaseDays) / w.Config.BaseDays;
            bool thirsty = FreshwaterAccess.Available(w, herd.Tile, index) < waterDemand || herd.UnmetFreshwater > waterDemand * .3;
            bool hungry = index.Ecology(herd.Tile).PotentialFood < foodDemand || herd.UnmetFood > foodDemand * .3;
            var candidates = map.Radius(herd.Tile, 1).Where(n => w.Tiles[n].TraversableLand).ToArray();
            if (candidates.Length == 0) return herd.Tile;
            if (threat)
                return candidates.OrderByDescending(n => hunters.Min(h => map.Distance(n, h.Tile)))
                    .ThenByDescending(n => Knowledge.Band(FreshwaterAccess.Available(w, n, index), 70, w.Config))
                    .ThenByDescending(n => Knowledge.Band(index.Ecology(n).PotentialFood, Math.Max(1, w.Ecology(n).MaxPotentialFood), w.Config))
                    .ThenBy(n => n).First();
            if (!thirsty && !hungry && Numbers.Random(w.Seed, 71, herd.Id, w.Turn) >= w.Config.WanderChance) return herd.Tile;
            return candidates.OrderByDescending(n => (thirsty ? FreshwaterAccess.Available(w, n, index) / Math.Max(1, waterDemand) : hungry ? index.Ecology(n).PotentialFood / Math.Max(1, foodDemand) : Numbers.Random(w.Seed, 72, herd.Id ^ n, w.Turn)) + species.Suitability(w.Tiles[n], index.Ecology(n).CurrentVegetation,
                    EcologyMath.CultivationVegetationRetention(w.Config, CultivationEconomy.Intensity(w.Config, index.Ecology(n).CultivationCapital))) * species.MovementHabitatWeight)
                .ThenBy(n => n == herd.Tile ? 0 : 1).ThenBy(n => n).First();
        }

        // Fishing snapshots are owner memory, not a map-wide public cache.
        // Only end-boundary visible grounds and grounds actually worked this
        // turn may refresh them; unseen live stocks never enter policy memory.
        internal static void UpdateFishingGroundKnowledge(WorldState world,
            IReadOnlyDictionary<int, FishingPlan> fishingPlans, TurnRecord record,
            IReadOnlyDictionary<int, double> resolvedOperations = null)
        {
            if (!world.Config.NeolithicFishingEnabled) return;
            fishingPlans ??= new Dictionary<int, FishingPlan>();
            var visibleByPlayer = new Dictionary<int, HashSet<int>>();
            var observedBySociety = new Dictionary<int, HashSet<int>>();
            var plansByCamp = fishingPlans;
            var operationByTile = new Dictionary<int, double>();
            if (resolvedOperations != null)
                foreach (var pair in resolvedOperations.OrderBy(pair => pair.Key))
                    operationByTile[pair.Key] = Numbers.Clamp(pair.Value, 0, 1);
            foreach (var plan in plansByCamp.Values.OrderBy(p => p.CampId))
                foreach (var ground in plan.Grounds.OrderBy(g => g.TileId))
                    if (!operationByTile.ContainsKey(ground.TileId) &&
                        FisherySiteAt(world, ground.TileId)?.OperatorCampId == plan.CampId)
                        operationByTile[ground.TileId] = Numbers.Clamp(ground.OperationFraction, 0, 1);

            var accessCache = new FishingAccessCache(world);
            foreach (var camp in world.Camps.OrderBy(c => c.Id))
            {
                if (!FishingEconomy.FeatureEnabled(world, camp)) continue;
                if (!observedBySociety.TryGetValue(camp.SocietyId, out var observed))
                    observedBySociety[camp.SocietyId] = observed = new HashSet<int>();

                if (camp.Population > 0)
                {
                    if (!visibleByPlayer.TryGetValue(camp.Player, out var visible))
                        visibleByPlayer[camp.Player] = visible = accessCache.Visible(camp.Player);
                    // Closing knowledge needs only reachable visible geometry.
                    // Do not rebuild stock, capital, rivalry, yield and ranking
                    // forecasts that were used by the opening planner.
                    var remembered = accessCache.Remembered(camp.People);
                    foreach (var route in new[] { FishingRoute.Shallows, FishingRoute.Lake })
                        foreach (int tile in accessCache.Distances(camp, camp.Tile, route, visible, remembered).Keys)
                            if (visible.Contains(tile)) observed.Add(tile);
                }
                if (plansByCamp.TryGetValue(camp.Id, out var plan) && !plan.Departing)
                    foreach (var ground in plan.Grounds)
                        if (ground.AssignedFisherYears > 1e-12) observed.Add(ground.TileId);
                foreach (var receipt in record.Transfers.Where(t => t.Entity == camp.Id && t.Kind == "FisheryHarvest"))
                    observed.Add(receipt.Tile);
            }

            foreach (var society in world.Societies.OrderBy(s => s.Id))
            {
                if (!observedBySociety.TryGetValue(society.Id, out var observed))
                    observed = new HashSet<int>();
                var snapshots = (society.FishingGroundKnowledge ?? new List<FishingGroundSnapshot>())
                    .ToDictionary(s => s.TileId, s => s.Copy());
                var explored = society.Memory?.Explored;
                if (explored == null)
                {
                    society.FishingGroundKnowledge = new List<FishingGroundSnapshot>();
                    continue;
                }
                foreach (int tileId in observed.OrderBy(id => id))
                {
                    var tile = world.Tiles[tileId];
                    var view = LazyEcology.ProjectEcology(world, tileId, world.Days);
                    var site = FisherySiteAt(world, tileId);
                    double capital = site == null ? 0 : FishingEconomy.ProjectedCapital(world, site, world.Days);
                    double operation = operationByTile.TryGetValue(tileId, out double fraction) ? fraction : 0;
                    var overlay = new AquaticEcologyOverlay(capital, capital, capital, operation);
                    var profile = new double[4];
                    for (int season = 0; season < 4; season++)
                    {
                        var capacity = new EcologyCapacity(world, tile, view, season,
                            aquaticOverlay: overlay);
                        double accessibleRatio = capacity.Biomass > 1e-12
                            ? Numbers.Clamp(capacity.Food / capacity.Biomass, 0, 1) : 0;
                        double safe = EcologyMath.SustainableHarvestRate(capacity.Biomass,
                            capacity.BiomassRate, world.Config.SustainableProductionFraction,
                            capacity.ManagedFlow);
                        profile[season] = Math.Max(0, safe * accessibleRatio);
                    }
                    snapshots[tileId] = new FishingGroundSnapshot
                    {
                        TileId = tileId, AccessibleFood = Math.Max(0, view.Food),
                        Biomass = Math.Max(0, view.PotentialFood),
                        Condition = Numbers.Clamp(view.AquaticCondition, 0, 1),
                        Capital = Math.Max(0, capital),
                        OperatorCampId = site?.OperatorCampId ?? -1,
                        ObservedDays = world.Days, OperationFraction = operation,
                        ProductiveProfile = profile
                    };
                }

                society.FishingGroundKnowledge = snapshots.Values
                    .Where(snapshot => observed.Contains(snapshot.TileId) || explored.Contains(snapshot.TileId))
                    .OrderBy(snapshot => snapshot.TileId).ToList();
            }
        }

        private static void Move(WorldState w, List<Order> orders, Dictionary<int, int> herds, CampPlacement placement,
            IReadOnlyCollection<int> abandoningVillages, PlanningIndex planning, TurnRecord record)
        {
            foreach (int id in placement.Blocked.OrderBy(id => id))
                record.Log("Camp " + id + " movement blocked by another camp.");
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                int target = placement.Destinations[camp.Id];
                // Fixed settlement Housing remains owned at the site where it
                // was built; migrate any legacy carried stock before changing
                // the camp's location.
                NeolithicProduction.MigrateLegacyFixedHousing(w, camp);
                if (target != camp.Tile)
                {
                    foreach (var hunter in planning.CampHunters(camp.Id).Where(h =>
                        h.EconomicTransitionRecall)) EconomicTransition.Cancel(hunter);
                    bool abandoning = abandoningVillages.Contains(camp.Id);
                    if (abandoning)
                    {
                        // Release only this camp's old local fields. Their
                        // physical capital remains on the map and decays
                        // normally; the moving camp cannot operate it now.
                        foreach (int id in w.Map.Radius(camp.Tile, 1))
                            if (w.Tiles[id].CultivationOperator == camp.Id)
                            {
                                w.Tiles[id].CultivationOperator = -1;
                                w.Tiles[id].CultivationOperatingFraction = 0;
                            }
                        camp.IsVillage = false;
                        camp.VillageTurns = 0;
                        record.Log("Village " + camp.Id + " abandoned its fields and became a camp.");
                    }
                    // Fishery capital remains at its physical site, but an
                    // accepted move immediately releases the departing owner.
                    if (w.Config.NeolithicFishingEnabled)
                    {
                        foreach (int tile in camp.LastSelectedFishingGrounds ?? new List<int>())
                        {
                            var site = FisherySiteAt(w, tile);
                            if (site?.OperatorCampId == camp.Id) site.OperatorCampId = -1;
                        }
                        camp.LastSelectedFishingGrounds?.Clear();
                    }
                    camp.Moves++; camp.StationaryTurns = 0;
                    // Settled expectations survive abandonment, so fixed
                    // housing left behind hurts QoL under the same formula.
                    if (!abandoning) camp.StationaryYears = 0;
                    record.Log("Camp " + camp.Id + " moved from " + camp.Tile + " to " + target + ".");
                }
                else camp.StationaryTurns++;
                camp.Tile = target;
            }
            foreach (var hunter in w.Hunters.OrderBy(h => h.Id))
            {
                var order = orders.FirstOrDefault(o => o.Entity == hunter.Id);
                if (order != null && order.Destination >= 0)
                {
                    if (hunter.EconomicTransitionRecall)
                    {
                        planning.Camps.TryGetValue(hunter.SourceCamp, out var source);
                        if (order.Kind == OrderKind.Move)
                            EconomicTransition.Advance(w, hunter, source, order.Destination);
                        else EconomicTransition.Cancel(hunter);
                    }
                    record.Log("Hunters " + hunter.Id + " moved to " + order.Destination + ".");
                    hunter.Tile = order.Destination;
                }
                else if (hunter.EconomicTransitionRecall && order != null &&
                    order.Kind != OrderKind.Reintegrate)
                    EconomicTransition.Cancel(hunter);
            }
            foreach (var herd in w.Herds.OrderBy(h => h.Id))
            {
                int target = herd.DomesticCamp >= 0 ? w.Camps.First(c => c.Id == herd.DomesticCamp).Tile : herds[herd.Id];
                if (herd.Tile != target) record.Log("Herd " + herd.Id + " moved to " + target + "."); herd.Tile = target;
            }
        }
        private static void Hunt(WorldState w, List<Order> orders, HashSet<int> establishedHunters, IReadOnlyDictionary<int, int> startingHerdPopulation, TurnRecord record, double time, double years,
            IReadOnlyDictionary<int, TurnNeeds> needs, List<HusbandryAcquisition.Opportunity> taming)
        {
            double practiceTime = time * w.Config.BaseDays / w.Config.PracticeDays;
            var requests = new List<Tuple<Hunter, Herd, int>>();
            var huntingIndex = new PlanningIndex(w);
            var committedElsewhere = new HashSet<int>(orders.Where(o => o.Kind == OrderKind.Fish || o.Kind == OrderKind.Reintegrate).Select(o => o.Entity));
            foreach (var hunter in w.Hunters.Where(h => establishedHunters.Contains(h.Id) && h.Population > 0 && !committedElsewhere.Contains(h.Id)).OrderBy(h => h.Id))
            {
                var herd = huntingIndex.VisibleHerds(new[] { hunter.Tile }).Where(h => h.DomesticCamp < 0)
                    .OrderByDescending(h => Math.Max(0, h.Animals - Math.Max(w.Config.GetSpecies(h.Species).MinimumAnimals, (int)Math.Ceiling(h.Animals * w.Config.HuntBreedingReserveFraction))))
                    .ThenByDescending(h => h.Animals).ThenBy(h => h.Id).FirstOrDefault();
                if (herd == null) continue;
                var camp = w.Camps.First(c => c.Id == hunter.SourceCamp);
                var practice = camp.SpeciesExperience.FirstOrDefault(p => p.Species == herd.Species);
                if (practice == null) { practice = new SpeciesPractice { Species = herd.Species }; camp.SpeciesExperience.Add(practice); }
                practice.Experience += hunter.Population * practiceTime;
                var hunterEcology = huntingIndex.Ecology(hunter.Tile);
                double coverRetention = EcologyMath.CultivationVegetationRetention(w.Config, CultivationEconomy.Intensity(w.Config, hunterEcology.CultivationCapital));
                var standingCover = Vegetation.RetainedCover(hunterEcology.CurrentVegetation, coverRetention);
                double efficiency = w.Config.HuntEfficiency * (Vegetation.LegacyTerrain(w.Tiles[hunter.Tile].Environment, standingCover) == Terrain.Woodland ? .85 : 1);
                double expectation = hunter.Population * efficiency * time * (1 - w.Config.HuntVariance + Numbers.Random(w.Seed, 80, hunter.Id, w.Turn) * w.Config.HuntVariance * 2);
                int requested = (int)expectation + (Numbers.Random(w.Seed, 81, hunter.Id, w.Turn) < expectation % 1 ? 1 : 0);
                requests.Add(Tuple.Create(hunter, herd, requested));
            }
            foreach (var group in requests.GroupBy(r => r.Item2.Id).OrderBy(g => g.Key))
            {
                var sorted = group.OrderBy(r => r.Item1.Id).ToArray(); var herd = sorted[0].Item2;
                var species = w.Config.GetSpecies(herd.Species);
                int breedingReserve = Math.Min(herd.Animals, Math.Max(species.MinimumAnimals, (int)Math.Ceiling(herd.Animals * w.Config.HuntBreedingReserveFraction)));
                int huntingPopulation = sorted.Sum(r => r.Item1.Population);
                int protectedPopulation = breedingReserve;
                if (huntingPopulation <= w.Config.SustainableHuntPopulation && startingHerdPopulation.TryGetValue(herd.Id, out int startingAnimals))
                {
                    int naturalCeiling = Math.Min(herd.Capacity, herd.Animals);
                    int retainedGrowth = (int)Math.Ceiling(Math.Max(0, naturalCeiling - startingAnimals) * w.Config.SustainableHerdGrowthRetention);
                    protectedPopulation = Math.Max(protectedPopulation, Math.Min(herd.Capacity, startingAnimals + retainedGrowth));
                }
                int total = sorted.Sum(r => r.Item3), available = Math.Min(total, Math.Max(0, herd.Animals - protectedPopulation));
                int[] kills = sorted.Select(r => total == 0 ? 0 : (int)((long)available * r.Item3 / total)).ToArray();
                int remainder = available - kills.Sum();
                foreach (int i in Enumerable.Range(0, sorted.Length).OrderByDescending(i => total == 0 ? 0 : (long)available * sorted[i].Item3 % total).ThenBy(i => sorted[i].Item1.Id))
                    if (remainder > 0 && kills[i] < sorted[i].Item3) { kills[i]++; remainder--; }
                for (int i = 0; i < sorted.Length; i++)
                {
                    var hunter = sorted[i].Item1; var camp = w.Camps.First(c => c.Id == hunter.SourceCamp);
                    double yieldPerAnimal = species.FoodYield * PracticeEffects.HuntingYield(w.Config, camp);
                    double usefulFood = Supply.Connected(w, hunter)
                        ? RemainingFoodTarget(camp, needs[camp.Id], true)
                        : Math.Max(0, Supply.Capacity(w, hunter) - hunter.CarriedFood);
                    int allocatedKills = kills[i];
                    kills[i] = Math.Min(kills[i], (int)Math.Floor((usefulFood + 1e-9) / Math.Max(1e-9, yieldPerAnimal)));
                    double yield = kills[i] * yieldPerAnimal;
                    Supply.Award(w, hunter, yield, record, "Hunting");
                    // Preserve a zero-yield receipt for an actually unsuccessful hunt, but not
                    // when hunters deliberately leave prey because a whole carcass cannot fit.
                    if (kills[i] > 0 || allocatedKills == 0)
                        record.Transfers.Add(new Transfer { Entity = hunter.Id, Tile = hunter.Tile, Kind = "Hunting", Food = yield });
                    if (kills[i] > 0) record.Log("Hunters " + hunter.Id + " killed " + kills[i] + " animals from herd " + herd.Id + ".");
                    else if (allocatedKills > 0) record.Log("Hunters " + hunter.Id + " left the herd unharmed because no whole carcass could be retained.");
                }
                int realizedKills = kills.Sum();
                herd.Animals -= realizedKills;
                if (realizedKills < total) record.Log("Herd " + herd.Id + " retained " + herd.Animals + " breeding animals after the generation's harvest.");
                // Interacting hunters establish a boundary-time taming
                // opportunity. Animals remain wild through this turn's
                // ecological claims and cannot produce managed goods retroactively.
                if (taming != null)
                    foreach (int campId in sorted.Select(x => x.Item1.SourceCamp).Distinct().OrderBy(id => id))
                        taming.Add(new HusbandryAcquisition.Opportunity(campId, herd.Id));
                if (huntingPopulation <= w.Config.SustainableHuntPopulation && herd.Animals > startingHerdPopulation[herd.Id]) record.Log("Limited hunting pressure allowed herd " + herd.Id + " to keep growing.");
            }
            // Managed herds retain legitimate zero state for persistence and later
            // husbandry decisions; only wild carcasses are removed here.
            w.Herds.RemoveAll(h => h.DomesticCamp < 0 && h.Animals <= 0);
        }
        public static void Breakthrough(WorldState w, Camp camp, TurnRecord record)
        { Ownership.Bind(w); BreakthroughBound(w, camp, record); }
        private static void BreakthroughBound(WorldState w, Camp camp, TurnRecord record)
        {
            if (w.LinkedPopulation(camp) <= 0) return;
            var progression = camp.People.Progression;
            if (progression.Era != Era.Paleolithic) return;
            double elapsed = w.Days - progression.EraStartDays;
            int best = Enumerable.Range(0, 3).OrderByDescending(i => camp.Experience[i]).ThenBy(i => i).First();
            double total = camp.Experience.Sum();
            if (camp.Technology == Technology.None)
            {
                bool threshold = camp.Experience[best] >= w.Config.ExperienceThreshold;
                bool majority = (elapsed >= progression.MajorityEligibilityDays && elapsed < progression.ForcedTransitionDays) && camp.Experience[best] > total * .5;
                if (!threshold && !majority && elapsed < progression.ForcedTransitionDays) return;
                bool economyBefore = EconomicTransition.Enabled(w, camp.People);
                camp.ForcedTransition = elapsed >= progression.ForcedTransitionDays && !threshold && !majority;
                camp.Technology = (Technology)(best + 1); camp.TransitionTurn = w.Turn;
                if (camp.ForcedTransition)
                {
                    int second = Enumerable.Range(0, 3).Where(i => i != best).OrderByDescending(i => camp.Experience[i]).ThenBy(i => i).First();
                    camp.SecondaryDomain = second;
                    // Keep accumulated experience intact; carryover is stored as progress separately at transition display/save.
                }
                progression.Era = Era.Neolithic; progression.CompletionDays = w.Days; progression.CompletionTurn = w.Turn; progression.CompletionRule = threshold ? "Mastery" : majority ? "Maturity" : "Forced";
                OpeningTransitions.Apply(w, camp, camp.Technology);
                if (!economyBefore && EconomicTransition.Enabled(w, camp.People))
                    EconomicTransition.Begin(w, camp.People);
                TechnologyDiscovery.QueueEligible(w, camp.People);
                record.Log("Tribe " + camp.Player + " discovered " + camp.Technology + " and entered the Neolithic.");
            }
        }
        public static double NeolithicProgress(WorldState w, Camp camp, Domain domain)
        {
            return TechnologyDiscovery.EffectiveProgress(camp.People, domain, w.Config);
        }
        public static void Invariants(WorldState w, int expectedPeople = -1, IEnumerable<Tile> ecologyScope = null)
        {
            if (w.MaterialSources == null || w.MaterialKnowledge == null || w.MaterialSources.Any(s => s == null || string.IsNullOrWhiteSpace(s.Id) || !MaterialEconomy.Materials.Contains(s.Material) || new[] { s.Initial, s.Remaining, s.LastDays }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0) || s.Remaining > s.Initial + 1e-8 || s.LastDays > w.Days + 1e-8) || w.MaterialSources.Select(s => s.Id).Distinct().Count() != w.MaterialSources.Count) throw new InvalidOperationException("Invalid authoritative material source state.");
            foreach (var c in w.Camps)
            {
                if (c.EconomyAutomationTurns < -1 || c.EconomicLabor != null && c.EconomicLabor.Values().Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0)) throw new InvalidOperationException("Invalid saved economic automation state.");
                if (c.Inventory == null || typeof(CampInventory).GetFields().Where(f => f.FieldType == typeof(double)).Select(f => (double)f.GetValue(c.Inventory)).Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < -1e-8) || new[] { c.PlantFood, c.AnimalFood, c.AquaticFood, c.StationaryYears, c.HousingDemand, c.FabricConsumed }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < -1e-8) || new[] { c.QoL, c.FoodNeedsMetRatio, c.WaterNeedsMetRatio, c.ClothPerCapita, c.HousingPerCapita, c.RudimentaryExpectation }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0 || v > 1 + 1e-8)) throw new InvalidOperationException("Invalid economic inventory or needs state.");
            }
            if (w.Camps.Select(c => c.Tile).Distinct().Count() != w.Camps.Count) throw new InvalidOperationException("Rival camps overlap.");
            var ids = w.Camps.Select(c => c.Id).Concat(w.Hunters.Select(h => h.Id)).Concat(w.Herds.Select(h => h.Id)).ToArray();
            if (ids.Distinct().Count() != ids.Length || ids.Any(i => i >= w.NextId)) throw new InvalidOperationException("Invalid stable IDs.");
            if (expectedPeople >= 0 && w.Camps.Sum(w.LinkedPopulation) != expectedPeople) throw new InvalidOperationException("Unexplained population change.");
            var invalidCamp = w.Camps.FirstOrDefault(c => c.Population < 0 || c.Food < -1e-8 || c.DemographicGrowthMultiplier <= 0 || double.IsNaN(c.DemographicGrowthMultiplier) || c.Workforce == null || c.Workforce.Assignments.Any(a => a == null || string.IsNullOrWhiteSpace(a.Role) || a.Population <= 0) || c.Workforce.Assignments.GroupBy(a => a.Role, StringComparer.Ordinal).Any(g => g.Count() != 1) || c.Workforce.Assigned > c.Population || !w.CanEnter(c.Tile, false));
            if (invalidCamp != null)
                throw new InvalidOperationException("Invalid people state or location: camp=" + invalidCamp.Id +
                    " pop=" + invalidCamp.Population + " assigned=" + (invalidCamp.Workforce?.Assigned ?? -1) +
                    " food=" + invalidCamp.Food.ToString("R") + " tile=" + invalidCamp.Tile +
                    " canEnter=" + w.CanEnter(invalidCamp.Tile, false) + ".");
            var invalidHunter = w.Hunters.FirstOrDefault(h =>
            {
                var source = w.Camps.FirstOrDefault(c => c.Id == h.SourceCamp);
                return h.Population < 0 || source == null || !w.CanEnter(h.Tile, true, source);
            });
            if (invalidHunter != null)
            {
                var source = w.Camps.FirstOrDefault(c => c.Id == invalidHunter.SourceCamp);
                throw new InvalidOperationException("Invalid people state or location: hunter=" + invalidHunter.Id +
                    " pop=" + invalidHunter.Population + " tile=" + invalidHunter.Tile + " source=" + invalidHunter.SourceCamp +
                    " sourceExists=" + (source != null) + " canEnter=" + (source != null && w.CanEnter(invalidHunter.Tile, true, source)) + ".");
            }
            if (w.Hunters.Any(h => h.CarriedFood < -1e-8 || h.CarriedFood > Supply.Capacity(w, h) + 1e-8) || w.Camps.Any(c => c.GrowthRemainder < 0 || c.GrowthRemainder >= 1)) throw new InvalidOperationException("Invalid carried provisions or demographic remainder.");
            if (w.Herds.Any(h => !w.Tiles[h.Tile].TraversableLand || h.Animals < 0)) throw new InvalidOperationException("Invalid herd population or peak/aquatic location.");
            foreach (var tile in ecologyScope ?? w.Tiles)
            {
                if (double.IsNaN(tile.CultivationCapital) || double.IsInfinity(tile.CultivationCapital) || tile.CultivationCapital < 0 || tile.CultivationCapital > 1 / (w.Config.ReferenceIntensity * w.Config.ReferenceIntensity) + 1e-8 || tile.CultivationOperator < -1) throw new InvalidOperationException("Invalid cultivation capital or operator.");
                var aquaticOverlay = LazyEcology.ProjectAquaticOverlay(w, tile.Id, w.Days, 0);
                var projected = LazyEcology.ProjectEcology(w, tile.Id, w.Days);
                var ecology = w.Ecology(tile.Id); double multiplier = OpeningTransitions.FoodMultiplier(w, tile);
                var capacity = new EcologyCapacity(w, tile, projected, w.Season,
                    aquaticOverlay: aquaticOverlay);
                if (tile.WaterBody == WaterKind.Saltwater && (tile.Freshwater > 0 || ecology.MaxFreshwater > 0 || tile.FreshwaterSource != FreshwaterSource.None)) throw new InvalidOperationException("Seawater is not drinkable.");
                if (tile.Saltwater < 0 || tile.Saltwater > ecology.MaxSaltwater + 1e-6 || tile.FreshwaterSource == FreshwaterSource.None && tile.Freshwater > 1e-8) throw new InvalidOperationException("Water stock lacks a valid source or capacity.");
                if (double.IsNaN(projected.LandCondition) || double.IsNaN(projected.AquaticCondition) || projected.LandCondition < -1e-8 || projected.LandCondition > 1 + 1e-8 || projected.AquaticCondition < -1e-8 || projected.AquaticCondition > 1 + 1e-8 || double.IsNaN(projected.Food) || double.IsInfinity(projected.Food) || projected.Food < -1e-8 || double.IsNaN(projected.PotentialFood) || double.IsInfinity(projected.PotentialFood) || projected.PotentialFood + 1e-8 < projected.Food || projected.PotentialFood > ecology.MaxPotentialFood * multiplier + 1e-5 || projected.PotentialFood > capacity.Biomass + 1e-6 || projected.Freshwater < -1e-8 || double.IsNaN(projected.Freshwater) ||
                    projected.Food > capacity.Food + 1e-6 || projected.Freshwater > ecology.MaxFreshwater + 1e-6)
                    throw new InvalidOperationException("Invalid ecology or capacity overflow at tile " + tile.Id +
                        ": food=" + projected.Food.ToString("R") + "/" + capacity.Food.ToString("R") +
                        ", biomass=" + projected.PotentialFood.ToString("R") + "/" + capacity.Biomass.ToString("R") +
                        ", freshwater=" + projected.Freshwater.ToString("R") + "/" + ecology.MaxFreshwater.ToString("R") +
                        ", landCondition=" + projected.LandCondition.ToString("R") +
                        ", aquaticCondition=" + projected.AquaticCondition.ToString("R") +
                        ", overlay=" + aquaticOverlay.HasValue + ".");
            }
        }
    }
}
