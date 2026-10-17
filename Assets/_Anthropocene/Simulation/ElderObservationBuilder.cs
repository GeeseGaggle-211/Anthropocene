using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    internal sealed class ElderObservationEnvelope
    {
        internal ElderObservation Observation;
        internal ElderObservationMetadata Metadata;
    }

    // Builds only from the actor's own state and AgentObservation, the existing
    // permission-filtered game-knowledge boundary. Raw identities are retained
    // in the sidecar solely to compile movement and diagnose replay.
    public sealed class ElderObservationBuilder
    {
        public int HistoryRecordsInspected { get; private set; }
        public bool ValueOrderedSites;
        public ElderObservation Build(WorldState world, int controlledCampId,
            bool movementEnabled, ElderCompiledDecision previous = null)
            => BuildEnvelope(world, controlledCampId, movementEnabled, previous)
                .Observation;

        public ElderObservation Build(WorldState world, int controlledCampId,
            bool movementEnabled, out ElderObservationMetadata metadata,
            ElderCompiledDecision previous = null)
        {
            var envelope = BuildEnvelope(world, controlledCampId,
                movementEnabled, previous);
            metadata = envelope.Metadata;
            return envelope.Observation;
        }

        internal ElderObservationEnvelope BuildEnvelope(WorldState world,
            int controlledCampId, bool movementEnabled,
            ElderCompiledDecision previous = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var camp = world.Camps.FirstOrDefault(item => item.Id == controlledCampId)
                ?? throw new ArgumentException("Controlled camp is not present.",
                    nameof(controlledCampId));
            var index = new PlanningIndex(world);
            var game = new AgentObservationBuilder().Build(world, camp.Player,
                index, false, true);
            var own = game.Settlements.FirstOrDefault(item => item.Id == camp.Id)
                ?? throw new InvalidOperationException("Controlled camp is absent from its own observation.");
            double years = world.Config.TurnDays / world.Config.DaysPerYear;
            HistoryRecordsInspected = world.History.Count > 0 ? 1 : 0;
            var recent = world.History.LastOrDefault();
            var memory = camp.ElderMemory;
            if (memory == null)
                memory = ElderInstitutionMemorySystem.TransientView(world,
                    camp, recent);
            var result = new ElderObservation();
            var metadata = new ElderObservationMetadata
            {
                ControlledCampId = camp.Id,
                ControlledPlayer = camp.Player
            };

            BuildCore(world, camp, own, years, recent, memory, result.Core);
            BuildSites(world, camp, game, movementEnabled, result, metadata);
            BuildFishing(world, camp, game, years, result, metadata);
            BuildFields(world, camp, game, years, result.Fields);
            BuildHerd(world, camp, game, years, recent, result);
            BuildCapabilities(world, camp, game, result);
            BuildCommodities(world, camp, years, recent, result);
            BuildContacts(world, camp, game, recent, result);
            BuildFission(camp, recent, result.Fission);
            BuildPrevious(previous, memory, result.Previous);
            BuildMemory(world, memory, result.Memory);
            BuildActionMask(world, camp, result.Mask);
            return new ElderObservationEnvelope
            {
                Observation = result,
                Metadata = metadata
            };
        }

        private static void BuildCore(WorldState world, Camp camp,
            SettlementObservation own, double years, TurnRecord recent,
            ElderInstitutionMemory memory, ElderCoreObservation core)
        {
            core.Population = Math.Max(0, camp.Population);
            core.ConnectedConsumers = Math.Max(0, own.LinkedPopulation);
            core.HasPreviousInterval = recent != null && recent.Duration > 0;
            core.PreviousIntervalYears = core.HasPreviousInterval
                ? Math.Max(0, recent.Duration) /
                    Math.Max(1e-12, world.Config.DaysPerYear) : 0;
            core.NextIntervalYears = Math.Max(0, years);
            core.HasDeficitAge = memory.HasDeficitStart;
            core.DeficitAgeYears = memory.HasDeficitStart ? Math.Max(0,
                world.Days - memory.DeficitStartDays) /
                Math.Max(1e-12, world.Config.DaysPerYear) : 0;
            core.HasMovementAge = memory.HasLastMovement;
            core.MovementAgeYears = memory.HasLastMovement ? Math.Max(0,
                world.Days - memory.LastMovementDays) /
                Math.Max(1e-12, world.Config.DaysPerYear) : 0;
            core.HasFissionAge = memory.HasLastFission;
            core.FissionAgeYears = memory.HasLastFission ? Math.Max(0,
                world.Days - memory.LastFissionDays) /
                Math.Max(1e-12, world.Config.DaysPerYear) : 0;
            core.QoL = ElderRLContract.Unit(camp.QoL);
            core.FoodNeedsMet = ElderRLContract.Unit(camp.FoodNeedsMetRatio);
            core.WaterNeedsMet = ElderRLContract.Unit(camp.WaterNeedsMetRatio);
            core.ClothingCoverage = ElderRLContract.Unit(camp.ClothPerCapita);
            core.HousingCoverage = ElderRLContract.Unit(camp.HousingPerCapita);
            double annualNeed = Math.Max(1e-12,
                Math.Max(1, camp.Population) * world.Config.Consumption *
                world.Config.DaysPerYear / world.Config.BaseDays);
            core.FoodReserveYears = Math.Max(0, camp.Food) / annualNeed;
            core.IsVillage = camp.IsVillage;
            core.RealizedMigrationPerCapitaYear = memory.CurrentSignals[
                (int)ElderMemorySignal.RealizedMigration];
            core.RealizedTradeUtilityPerCapitaYear = memory.CurrentSignals[
                (int)ElderMemorySignal.RealizedTradeUtility];
        }

        private void BuildSites(WorldState world, Camp camp,
            AgentObservation game, bool movementEnabled, ElderObservation result,
            ElderObservationMetadata metadata)
        {
            var agricultural = game.AgriculturalSites.ToDictionary(site =>
                site.Tile);
            // Contract 7 ranks candidates by observable physical value (current first, then human food after herd use)
            // so the bounded candidate list holds the sites a policy would plausibly choose; v2 keeps tile order.
            var all = (ValueOrderedSites
                ? game.SiteCoverage.OrderBy(site => site.Tile == camp.Tile ? 0 : 1)
                    .ThenByDescending(site => site.HumanFoodAfterHerd).ThenByDescending(site => site.HumanFood).ThenBy(site => site.Tile)
                : game.SiteCoverage.OrderBy(site => site.Tile == camp.Tile ? 0 : 1).ThenBy(site => site.Tile)).ToArray();
            var selected = all.Take(ElderRLContract.MaxLocalSites).ToArray();
            result.Sites = selected.Select((site, candidate) =>
            {
                agricultural.TryGetValue(site.Tile, out var fields);
                return new ElderSiteObservation
                {
                    Candidate = candidate,
                    Current = site.Tile == camp.Tile,
                    Known = true,
                    HumanFood = site.HumanFood,
                    HumanFoodAfterHerd = site.HumanFoodAfterHerd,
                    HerdZeroDisplacement = site.HerdForage,
                    HerdAbsolute = site.HerdAbsolute,
                    HerdWater = site.HerdWater,
                    FixedShelter = site.FixedShelter,
                    RecurringLabor = site.RecurringLabor,
                    AbsoluteRecurringLabor = site.AbsoluteRecurringLabor,
                    RetainedFieldService = fields?.ManagedRecoveryCoverage ?? 0,
                    LostRebuildBurden = fields?.LostRebuildBurden ?? 0
                };
            }).ToArray();
            metadata.SiteTiles = selected.Select(site => site.Tile).ToArray();
            result.Presence.Sites.Present = selected.Length;
            result.Presence.Sites.Omitted = Math.Max(0, all.Length - selected.Length);

            var legal = new List<int> { -1 };
            if (movementEnabled)
                for (int candidate = 0; candidate < selected.Length; candidate++)
                {
                    int tile = selected[candidate].Tile;
                    if (tile == camp.Tile) continue;
                    var move = new Order(OrderKind.Move, camp.Id,
                        destination: tile);
                    if (OrderRules.Validate(game, move) == null) legal.Add(candidate);
                }
            result.Mask.Movement = movementEnabled && legal.Count > 1;
            result.Mask.LegalMovementCandidates = legal.ToArray();
        }

        private static void BuildFishing(WorldState world, Camp camp,
            AgentObservation game, double years, ElderObservation result,
            ElderObservationMetadata metadata)
        {
            var all = game.FishingGrounds
                .OrderByDescending(ground => ground.Selected)
                .ThenByDescending(ground => ground.Productivity)
                .ThenBy(ground => ground.Tile).ToArray();
            var selected = all.Take(ElderRLContract.MaxFishingGrounds).ToArray();
            result.Fishing = selected.Select((ground, candidate) =>
            {
                double q = BandRepresentative(ground.CapitalIntensity,
                    world.Config);
                double capital = Math.Pow(q /
                    world.Config.ReferenceIntensity, 2);
                if (!ground.SitePresent)
                    q = capital = 0;
                var site = ground.OwnOperator
                    ? world.FisherySites?.FirstOrDefault(item =>
                        item.TileId == ground.Tile &&
                        item.OperatorCampId == camp.Id) : null;
                if (site != null)
                {
                    capital = FishingEconomy.ProjectedCapital(world, site,
                        world.Days);
                    q = FishingEconomy.Intensity(world.Config, capital);
                }
                var ownSnapshot = ground.OwnOperator
                    ? camp.People.FishingGroundKnowledge?.FirstOrDefault(
                        snapshot => snapshot.TileId == ground.Tile) : null;
                double operation = ownSnapshot != null
                    ? ElderRLContract.Unit(ownSnapshot.OperationFraction)
                    : ground.HasOperation
                        ? BandRepresentative(ground.Operation, world.Config) : 0;
                bool maintenancePresent = ground.OwnOperator && site != null;
                double maintenance = maintenancePresent
                    ? world.Config.Neolithic.MaintenanceWorkersPerTileE(
                            world.Config.MaintenanceCoefficient) *
                        FishingEconomy.EffortForCapital(world, camp, capital) *
                        years : 0;
                return new ElderFishingObservation
                {
                    Candidate = candidate,
                    Distance = world.Map.Distance(camp.Tile, ground.Tile),
                    Incumbent = ground.Selected,
                    Known = ground.Visible || ground.Visited,
                    YieldPerFisherYear = ground.Productivity,
                    FoodPotential = ground.AccessibleFood,
                    Biomass = ground.Biomass,
                    Condition = ground.Condition,
                    CurrentCapital = capital,
                    CurrentIntensity = q,
                    OperationFraction = operation,
                    MaintenanceWorkerYears = maintenance,
                    MaintenancePresent = maintenancePresent
                };
            }).ToArray();
            metadata.FishingTiles = selected.Select(ground => ground.Tile).ToArray();
            result.Presence.Fishing.Present = selected.Length;
            result.Presence.Fishing.Omitted = Math.Max(0, all.Length - selected.Length);
        }

        private static void BuildFields(WorldState world, Camp camp,
            AgentObservation game, double years, ElderFieldObservation fields)
        {
            var owned = world.Map.Radius(camp.Tile, 1)
                .Where(tile => game.Tiles.ContainsKey(tile) &&
                    world.Tiles[tile].CultivationOperator == camp.Id &&
                    world.Tiles[tile].TraversableLand)
                .OrderBy(tile => tile).ToArray();
            fields.RetainedTiles = owned.Count(tile =>
                LazyEcology.ProjectEcology(world, tile, world.Days)
                    .CultivationCapital > 1e-9);
            var intensities = owned.Select(tile =>
                    LazyEcology.ProjectEcology(world, tile, world.Days)
                        .CultivationCapital)
                .Where(capital => capital > 1e-9)
                .Select(capital => CultivationEconomy.Intensity(world.Config,
                    capital)).ToArray();
            // Deterministic arithmetic mean over current retained, positive-E
            // fields. Empty footprints report zero.
            fields.CurrentIntensity = intensities.Length == 0 ? 0 :
                intensities.Average();
            double maintenance = CultivationEconomy.MaintenanceFarmersForPlanning(
                world, camp, years);
            int farmers = camp.Workforce.Assignments.Where(assignment =>
                assignment.Role == WorkforceRoles.Farmer).Sum(assignment =>
                    assignment.Population);
            fields.MaintenanceWorkerYears = maintenance * years;
            fields.CurrentOperation = maintenance > 1e-9
                ? Numbers.Clamp(farmers / maintenance, 0, 1) : 0;
            var current = game.AgriculturalSites.FirstOrDefault(site =>
                site.Tile == camp.Tile);
            if (current != null)
            {
                fields.CurrentFoodSupport = current.ManagedRecoveryCoverage;
                fields.FutureFoodSupport = current.ManagedRecoveryCoverage;
                fields.RebuildBurden = current.LostRebuildBurden;
                fields.RetainedCapitalService = current.ManagedRecoveryCoverage;
            }
        }

        private static void BuildHerd(WorldState world, Camp camp,
            AgentObservation game, double years, TurnRecord recent,
            ElderObservation result)
        {
            var all = game.Herds.Where(herd => herd.OwnedDomestic &&
                    herd.DomesticCamp == camp.Id)
                .OrderByDescending(herd => herd.Count.Representative)
                .ThenByDescending(herd => herd.Nutrition)
                .ThenByDescending(herd => herd.ManagementFulfillment)
                .ThenBy(herd => world.Config.GetSpecies(herd.Species)
                    .FoodConsumption)
                .ThenBy(herd => world.Config.GetSpecies(herd.Species)
                    .FreshwaterConsumption)
                .ThenBy(herd => herd.StartingAnimals > 0
                    ? herd.NewbornAnimals / (double)herd.StartingAnimals : 0)
                .ThenBy(herd => herd.StartingAnimals > 0
                    ? herd.StarvationDeaths /
                        (double)herd.StartingAnimals : 0)
                // The final tie-break is invisible only among rows with the
                // same encoded physiology/count, so entity identity cannot
                // become a policy feature.
                .ThenBy(herd => herd.Id).ToArray();
            var selected = all.Take(ElderRLContract.MaxHerdGroups).ToArray();
            result.HerdGroups = selected.Select(herd =>
            {
                var species = world.Config.GetSpecies(herd.Species);
                int starting = Math.Max(0, herd.StartingAnimals);
                return new ElderHerdGroupObservation
                {
                    Animals = Math.Max(0, herd.Count.Representative),
                    Nutrition = ElderRLContract.Unit(herd.Nutrition),
                    ManagementFulfilment = ElderRLContract.Unit(
                        herd.ManagementFulfillment),
                    FoodPerAnimal = species.FoodConsumption,
                    WaterPerAnimal = species.FreshwaterConsumption,
                    AnimalFoodPerAnimal = world.Config.AnimalFoodPerManagedAnimal,
                    BirthRate = starting > 0 ? herd.NewbornAnimals /
                        (double)starting : 0,
                    MortalityRate = starting > 0 ? herd.StarvationDeaths /
                        (double)starting : 0
                };
            }).ToArray();
            result.Presence.Herds.Present = selected.Length;
            result.Presence.Herds.Omitted = Math.Max(0, all.Length - selected.Length);

            var herd = result.Herd;
            herd.Groups = all.Length;
            herd.Animals = all.Sum(group => Math.Max(0,
                group.Count.Representative));
            double animals = Math.Max(1, herd.Animals);
            herd.Nutrition = all.Length == 0 ? 1 : all.Sum(group =>
                group.Nutrition * Math.Max(0, group.Count.Representative)) / animals;
            herd.ManagementFulfilment = all.Length == 0 ? 1 : all.Sum(group =>
                group.ManagementFulfillment * Math.Max(0,
                    group.Count.Representative)) / animals;
            var current = game.SiteCoverage.FirstOrDefault(site =>
                site.Tile == camp.Tile);
            if (current != null)
            {
                herd.ZeroDisplacementCapacity = current.HerdForage * herd.Animals;
                herd.AbsoluteCapacity = current.HerdAbsolute * herd.Animals;
                herd.WaterCapacity = current.HerdWater * herd.Animals;
            }
            int herders = camp.Workforce.Assignments.Where(assignment =>
                assignment.Role == WorkforceRoles.Herder).Sum(assignment =>
                    assignment.Population);
            herd.CurrentManagementWorkerYears = herders * years;
            herd.CurrentAnimalFood = all.Sum(group => group.AcceptedFood);
            herd.HumanFoodDisplaced = Math.Max(0, all.Sum(group =>
                group.PotentialFood - group.AcceptedFood));
            int startingAnimals = all.Sum(group => Math.Max(0,
                group.StartingAnimals));
            if (startingAnimals > 0)
            {
                herd.BirthTrend = all.Sum(group => group.NewbornAnimals) /
                    (double)startingAnimals;
                herd.MortalityTrend = all.Sum(group => group.StarvationDeaths) /
                    (double)startingAnimals;
                herd.SlaughterTrend = (recent?.ManagedHerds ??
                    new List<ManagedHerdReport>()).Where(report =>
                        report.Camp == camp.Id).Sum(report => report.Slaughtered) /
                    (double)startingAnimals;
            }
        }

        private static void BuildCapabilities(WorldState world, Camp camp,
            AgentObservation game, ElderObservation result)
        {
            var statuses = game.TechnologyStatuses.ToDictionary(status =>
                status.Domain);
            double Progress(Domain domain) => statuses.TryGetValue(domain,
                out var status) && status.Threshold > 0
                    ? Numbers.Clamp(status.Progress / status.Threshold, 0, 1) : 0;
            result.Capabilities = new[]
            {
                new ElderCapabilityObservation { Key = "field-retention",
                    Available = CultivationEconomy.Enabled(camp),
                    Experience = camp.Experience[(int)Domain.Gathering],
                    Progress = Progress(Domain.Gathering) },
                new ElderCapabilityObservation { Key = "herd-management",
                    Available = CultivationEconomy.HusbandryEnabled(camp),
                    Experience = camp.Experience[(int)Domain.Hunting],
                    Progress = Progress(Domain.Hunting) },
                new ElderCapabilityObservation { Key = "resident-fishing",
                    Available = NeolithicCapabilities.ResidentFishingEnabled(
                        world, camp),
                    Experience = camp.Experience[(int)Domain.Fishing],
                    Progress = Progress(Domain.Fishing) },
                new ElderCapabilityObservation { Key = "fixed-construction",
                    Available = camp.People.HasTech(NeolithicTech.Construction) },
                new ElderCapabilityObservation { Key = "local-trade",
                    Available = world.Config.TradeEnabled }
            };
        }

        private static void BuildCommodities(WorldState world, Camp camp,
            double years, TurnRecord recent, ElderObservation result)
        {
            var recipes = NeolithicProduction.Recipes(world.Config)
                .ToDictionary(recipe => recipe.Good);
            var all = ElderObservationSchema.CommodityGoods.Select(good =>
                {
                    recipes.TryGetValue(good, out var recipe);
                    Trade.GoodDescriptor traded = Trade.Registry.FirstOrDefault(
                        item => item.Recipe == good);
                    double demand = NeolithicProduction.Demand(world, camp,
                        good, years);
                    double imports = 0, exports = 0;
                    foreach (var trade in recent?.Trades ?? new List<TradeRecord>())
                    {
                        bool want = traded != null && trade.Want == traded.Id;
                        bool give = traded != null && trade.Give == traded.Id;
                        if (!want && !give) continue;
                        if (trade.Requester == camp.Id)
                        {
                            if (want) imports += trade.WantQuantity;
                            if (give) exports += trade.GiveQuantity;
                        }
                        else if (trade.Responder == camp.Id)
                        {
                            if (give) imports += trade.GiveQuantity;
                            if (want) exports += trade.WantQuantity;
                        }
                    }
                    return new ElderCommodityObservation
                    {
                        Key = good.ToString(),
                        Stock = Math.Max(0, NeolithicProduction.Stock(camp, good)),
                        LocalNeed = Math.Max(0, demand),
                        ProtectedFloor = Math.Max(0, demand),
                        ProductionCapacity = recipe == null ? 0 :
                            Math.Max(0, recipe.Rate * years),
                        RecentImport = imports,
                        RecentExport = exports,
                        CanProduce = recipe != null &&
                            NeolithicProduction.RecipeAvailable(world, camp, recipe),
                        CanTrade = world.Config.TradeEnabled
                    };
                }).ToArray();
            result.Commodities = all.Take(ElderRLContract.MaxCommoditySlots)
                .ToArray();
            result.Presence.Commodities.Present = result.Commodities.Length;
            result.Presence.Commodities.Omitted = Math.Max(0,
                all.Length - result.Commodities.Length);
        }

        private static void BuildContacts(WorldState world, Camp camp,
            AgentObservation game, TurnRecord recent,
            ElderObservation result)
        {
            var visibleTiles = new HashSet<int>(game.Rivals.Select(rival =>
                rival.Tile));
            var all = game.KnownSettlements.Where(contact => !contact.Moved &&
                    contact.Tile >= 0)
                .OrderBy(contact => world.Map.Distance(camp.Tile, contact.Tile))
                .ThenByDescending(contact => ElderRLContract.Finite(
                    contact.ObservedDays) ? contact.ObservedDays :
                    Math.Min(world.Days, Math.Max(0, contact.Turn) *
                        world.Config.TurnDays)).ToArray();
            var selected = all.Take(ElderRLContract.MaxKnownContacts).ToArray();
            result.Contacts = selected.Select(contact =>
            {
                var relation = game.KinRelations.FirstOrDefault(item =>
                    item.Player == contact.Owner);
                double migration = (recent?.Migrations ??
                    new List<MigrationReport>()).Where(report =>
                        report.SourceCamp == camp.Id &&
                            report.DestinationCamp == contact.Camp ||
                        report.DestinationCamp == camp.Id &&
                            report.SourceCamp == contact.Camp).Sum(report =>
                            report.DestinationCamp == camp.Id ? report.Migrants :
                            -report.Migrants);
                var trades = (recent?.Trades ??
                    new List<TradeRecord>()).Where(record =>
                        record.Requester == camp.Id &&
                            record.Responder == contact.Camp ||
                        record.Responder == camp.Id &&
                            record.Requester == contact.Camp).ToArray();
                double tradeFlow = trades.Sum(record =>
                    record.WantQuantity + record.GiveQuantity);
                double tradeUtility = trades.Sum(record =>
                    record.Requester == camp.Id
                        ? record.RequesterRealizedUtility
                        : record.ResponderRealizedUtility);
                double intervalYears = Math.Max(0, recent?.Duration ?? 0) /
                    Math.Max(1e-12, world.Config.DaysPerYear);
                int priorPopulation = recent?.Demographics?.FirstOrDefault(
                    report => report.Camp == camp.Id)?.StartingPopulation ??
                    Math.Max(1, camp.Population);
                double scale = Math.Max(1e-12,
                    Math.Max(1, priorPopulation) * intervalYears);
                double observedDays = ElderRLContract.Finite(
                    contact.ObservedDays) ? contact.ObservedDays :
                    Math.Min(world.Days, Math.Max(0, contact.Turn) *
                        world.Config.TurnDays);
                return new ElderContactObservation
                {
                    Distance = world.Map.Distance(camp.Tile, contact.Tile),
                    AgeYears = Math.Max(0, world.Days - observedDays) /
                        Math.Max(1e-12, world.Config.DaysPerYear),
                    Affinity = relation?.Affinity ?? 0,
                    RecentTradeUtilityPerCapitaYear = intervalYears > 0
                        ? tradeUtility / scale : 0,
                    RecentTradeFlowPerCapitaYear = intervalYears > 0
                        ? tradeFlow / scale : 0,
                    RecentMigrationPerCapitaYear = intervalYears > 0
                        ? migration / scale : 0,
                    Kin = relation != null,
                    CurrentlyVisible = visibleTiles.Contains(contact.Tile)
                };
            }).ToArray();
            result.Presence.Contacts.Present = selected.Length;
            result.Presence.Contacts.Omitted = Math.Max(0,
                all.Length - selected.Length);
        }

        private static void BuildFission(Camp camp, TurnRecord recentRecord,
            ElderFissionObservation fission)
        {
            fission.CandidatePresent = camp.FissionDestination != null;
            var recent = (recentRecord?.Fissions ??
                new List<FissionReport>()).LastOrDefault(report =>
                    report.ParentCamp == camp.Id);
            if (recent == null) return;
            fission.RecentSplit = true;
            fission.RecentDaughterPopulation = recent.Daughter;
            fission.ExpectedStayerGain = recent.Gain;
            fission.ExpectedDaughterQoL = recent.DaughterQoL;
            fission.ExitOpportunityPressure = Numbers.Clamp(
                recent.ExitOpportunityPressure, 0, 1);
            fission.Pressure = recent.Push;
        }

        private static void BuildActionMask(WorldState world, Camp camp,
            ElderActionMask mask)
        {
            bool cultivation = CultivationEconomy.Enabled(camp);
            bool husbandry = CultivationEconomy.HusbandryEnabled(camp);
            bool retainedField = cultivation &&
                CultivationEconomy.Footprint(world, camp).Any(tile =>
                    world.Tiles[tile].CultivationOperator == camp.Id &&
                    LazyEcology.ProjectEcology(world, tile, world.Days)
                        .CultivationCapital > 1e-9);
            bool retainedHerd = husbandry && world.Herds.Any(herd =>
                herd.DomesticCamp == camp.Id && herd.Animals > 0);

            mask.FieldRetention = retainedField;
            mask.HerdRetention = retainedHerd;
            mask.FieldDevelopment = cultivation;
            mask.HerdDevelopment = retainedHerd;
        }

        private static void BuildPrevious(ElderCompiledDecision decision,
            ElderInstitutionMemory memory,
            ElderPreviousDecisionObservation previous)
        {
            ElderDirective requested = decision?.Requested ??
                memory.LastRequested;
            ElderDirective accepted = decision?.Accepted ??
                memory.LastAccepted;
            ElderDirective realized = decision?.Realized ??
                memory.LastRealized;
            if (requested == null || accepted == null ||
                !(decision != null || memory.HasLastDecision)) return;
            previous.Present = true;
            previous.RequestedFieldRetention =
                requested.RetainedFieldOperation;
            previous.AcceptedFieldRetention =
                accepted.RetainedFieldOperation;
            previous.RequestedHerdRetention =
                requested.RetainedHerdFraction;
            previous.AcceptedHerdRetention =
                accepted.RetainedHerdFraction;
            previous.RequestedDevelopmentBudget =
                requested.DevelopmentBudget;
            previous.AcceptedDevelopmentBudget =
                accepted.DevelopmentBudget;
            previous.RequestedFieldShare =
                requested.FieldDevelopmentShare;
            previous.AcceptedFieldShare =
                accepted.FieldDevelopmentShare;
            previous.MovementAccepted = accepted.MovementCandidate ==
                requested.MovementCandidate;
            previous.InterventionCount = decision?.Interventions?.Length ??
                memory.LastInterventionCount;
            previous.ElapsedYears = memory.LastDecisionElapsedYears;
            if (realized == null) return;
            previous.RealizedFieldOperation =
                realized.RetainedFieldOperation;
            previous.RealizedHerdRetention =
                realized.RetainedHerdFraction;
            previous.RealizedDevelopmentBudget =
                realized.DevelopmentBudget;
            previous.RealizedFieldShare =
                realized.FieldDevelopmentShare;
        }

        private static void BuildMemory(WorldState world,
            ElderInstitutionMemory memory, ElderMemoryObservation observation)
        {
            observation.Present = memory?.Initialized == true;
            if (!observation.Present) return;
            observation.Traces = ElderObservationSchema.MemoryRows.Select(
                (key, signal) => new ElderMemoryTraceObservation
                {
                    Key = key,
                    Short = memory.Mean((ElderMemorySignal)signal, 0),
                    Medium = memory.Mean((ElderMemorySignal)signal, 1),
                    Long = memory.Mean((ElderMemorySignal)signal, 2)
                }).ToArray();
            observation.Maturity = Enumerable.Range(0, 3).Select(horizon =>
                memory.Maturity(horizon, world.Days,
                    world.Config.DaysPerYear)).ToArray();
        }

        private static double BandRepresentative(int band,
            SimulationConfig config)
        {
            var thresholds = config.BandThresholds ?? Array.Empty<double>();
            int bounded = Math.Max(0, Math.Min(4, band));
            if (thresholds.Length == 0) return bounded / 4.0;
            double low = bounded == 0 ? 0 :
                thresholds[Math.Min(thresholds.Length - 1, bounded - 1)];
            double high = bounded < thresholds.Length
                ? thresholds[bounded] : 1;
            return Numbers.Clamp(.5 * (low + high), 0, 1);
        }
    }
}
