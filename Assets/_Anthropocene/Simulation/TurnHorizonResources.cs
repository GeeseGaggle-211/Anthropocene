using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed partial class TurnResolver
    {
        private sealed class FisheryResourceState
        {
            public readonly Dictionary<int, AquaticEcologyOverlay> Overlays = new Dictionary<int, AquaticEcologyOverlay>();
            public readonly Dictionary<int, int> Operators = new Dictionary<int, int>();
            public readonly Dictionary<int, FishingGroundPlan> ManagingGrounds = new Dictionary<int, FishingGroundPlan>();
            public long SiteLookups;
        }

        private static FisherySite FisherySiteAt(WorldState world, int tile)
        {
            var sites = world.FisherySites;
            if (sites == null) return null;
            int low = 0, high = sites.Count - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int compare = sites[middle].TileId.CompareTo(tile);
                if (compare == 0) return sites[middle];
                if (compare < 0) low = middle + 1;
                else high = middle - 1;
            }
            return null;
        }

        private FisheryResourceState PrepareFisheryResources(WorldState world,
            TurnHorizon horizon, IReadOnlyDictionary<int, FishingPlan> fishingPlans,
            HashSet<int> activeTiles)
        {
            var state = new FisheryResourceState();
            if (fishingPlans == null) fishingPlans = new Dictionary<int, FishingPlan>();
            if (!world.Config.NeolithicFishingEnabled) return state;
            world.FisherySites ??= new List<FisherySite>();
            var camps = world.Camps.ToDictionary(c => c.Id);
            var candidates = new Dictionary<int, List<(int Camp, FishingGroundPlan Ground)>>();
            // The persisted last-selected sets are the bounded reverse index
            // for operators that may need release. Capture them before current
            // plans replace those sets; dormant sites never require a scan.
            var affectedSites = new SortedSet<int>();
            FisherySite SiteAt(int tile)
            {
                state.SiteLookups++;
                return FisherySiteAt(world, tile);
            }
            foreach (var camp in camps.Values)
                foreach (int tile in camp.LastSelectedFishingGrounds ?? new List<int>())
                    affectedSites.Add(tile);

            // Plans are already destination-aware and immutable. A camp can
            // manage a site only when it did not depart and supplied positive
            // ground work. Losing plans retain their legal harvest request.
            foreach (var plan in fishingPlans.Values.OrderBy(p => p.CampId))
            {
                if (camps.TryGetValue(plan.CampId, out var camp))
                {
                    camp.LastSelectedFishingGrounds = plan.Departing
                        ? new List<int>() : plan.Grounds.Select(g => g.TileId).Distinct().OrderBy(id => id).ToList();
                }
                foreach (var ground in plan.Grounds.OrderBy(g => g.TileId))
                {
                    activeTiles.Add(ground.TileId);
                    affectedSites.Add(ground.TileId);
                    if (plan.Departing || !plan.OperatorEligible ||
                        ground.AssignedFisherYears <= 1e-12 ||
                        ground.KnowledgeSource == FishingKnowledgeSource.ConservativePrior ||
                        !camps.TryGetValue(plan.CampId, out camp) ||
                        camp.Tile != plan.CampTile || camp.Population <= 0) continue;
                    if (!candidates.TryGetValue(ground.TileId, out var list))
                        candidates.Add(ground.TileId, list = new List<(int, FishingGroundPlan)>());
                    list.Add((plan.CampId, ground));
                }
            }

            // Reconcile all prior sites and selected candidates before workers
            // integrate ecology. Keep a reachable, working incumbent; otherwise
            // the lowest eligible camp ID wins the one operator slot.
            foreach (int tile in affectedSites)
            {
                var site = SiteAt(tile);
                int prior = site?.OperatorCampId ?? -1;
                candidates.TryGetValue(tile, out var eligible);
                eligible ??= new List<(int Camp, FishingGroundPlan Ground)>();
                var selected = eligible.FirstOrDefault(candidate => candidate.Camp == prior);
                if (selected.Ground == null && eligible.Count > 0) selected = eligible[0];
                int operatorCamp = selected.Ground == null ? -1 : selected.Camp;
                state.Operators[tile] = operatorCamp;
                if (selected.Ground != null)
                    state.ManagingGrounds[tile] = selected.Ground;
                if (site != null) site.OperatorCampId = operatorCamp;
            }

            // Construct an explicit ecology overlay for every selected ground
            // and any already-active tile with stored fishery capital. Quiet
            // sites remain lazy and cost no integration until their tile is used.
            foreach (int tile in activeTiles.OrderBy(id => id).ToArray())
            {
                if (state.ManagingGrounds.TryGetValue(tile, out var ground))
                {
                    var site = SiteAt(tile);
                    double opening = site == null ? 0
                        : FishingEconomy.ProjectedCapital(world, site, horizon.StartDays);
                    var capital = FishingEconomy.AllocateCapital(world, opening,
                        ground.CapitalWorkerYears, horizon.Years);
                    state.Overlays[tile] = new AquaticEcologyOverlay(capital.OpeningCapital,
                        capital.AverageCapital, capital.EndingCapital, capital.OperationFraction);
                }
                else
                {
                    var overlay = LazyEcology.ProjectAquaticOverlay(world, tile,
                        horizon.StartDays, horizon.Days);
                    if (overlay.HasValue) state.Overlays[tile] = overlay.Value;
                }
            }
            resolvedFisheryOperations = state.ManagingGrounds.Keys.ToDictionary(tile => tile,
                tile => state.Overlays[tile].OperatingFraction);
            LastMetrics.FisherySiteLookups += state.SiteLookups;
            return state;
        }

        private void CommitFisherySite(WorldState world, int tile,
            AquaticEcologyOverlay overlay, int operatorCamp, double endpointDays)
        {
            LastMetrics.FisherySiteLookups++;
            world.FisherySites ??= new List<FisherySite>();
            int low = 0, high = world.FisherySites.Count - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int compare = world.FisherySites[middle].TileId.CompareTo(tile);
                if (compare == 0)
                {
                    var existing = world.FisherySites[middle];
                    existing.Capital = overlay.EndingCapital;
                    existing.OperatorCampId = operatorCamp;
                    existing.LastUpdatedDays = endpointDays;
                    if (existing.Capital <= 1e-12 && operatorCamp < 0)
                        world.FisherySites.RemoveAt(middle);
                    return;
                }
                if (compare < 0) low = middle + 1;
                else high = middle - 1;
            }
            if (overlay.EndingCapital <= 1e-12 && operatorCamp < 0) return;
            world.FisherySites.Insert(low, new FisherySite
            {
                TileId = tile, OperatorCampId = operatorCamp,
                Capital = overlay.EndingCapital, LastUpdatedDays = endpointDays
            });
        }

        // A claim set is constructed once from locked allocations. Each tile is
        // evaluated once over the full horizon and all receipts are committed in
        // tile order. The cultivation plan uses private average/end-capital values;
        // no fictive mid-turn field state is written into the world.
        private Dictionary<int, double> CommitHorizonResources(
            WorldState w, List<Demand> demands, TurnRecord record, Action<int> touch,
            Dictionary<int, double> foraged, TurnHorizon horizon,
            Dictionary<int, AgriculturalLabor> agriculturalLabor,
            Dictionary<int, TurnNeeds> needs, IReadOnlyCollection<int> abandoningVillages,
            IReadOnlyDictionary<int, FishingPlan> fishingPlans)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var herds = w.Herds.ToDictionary(h => h.Id);
            var camps = w.Camps.ToDictionary(c => c.Id);
            var hunters = w.Hunters.ToDictionary(h => h.Id);
            var agriculturalCamps = w.Camps.Where(c => CultivationEconomy.Enabled(c) &&
                !abandoningVillages.Contains(c.Id)).OrderBy(c => c.Id).ToArray();
            var farmToolBudgets = agriculturalCamps.ToDictionary(c => c.Id, c => new FarmToolWorkBudget(w, c));
            var scratch = agriculturalCamps.ToDictionary(c => c.Id,
                c => new AgriculturalClaimScratch(w, c, agriculturalLabor[c.Id].Footprint));
            var allocations = new Dictionary<int, HorizonCultivationAllocation>();
            var averageCapital = new Dictionary<int, double>();
            var endingCapital = new Dictionary<int, double>();
            var gatheredFoodForMaterials = new Dictionary<int, double>();
            var terrestrialGathered = new Dictionary<int, double>();
            var transfers = new List<Transfer>();
            var claims = demands.Where(d => d.Kind != "Field operation").ToList();
            var activeTiles = new HashSet<int>(demands.Select(d => d.Tile));
            bool fishingEnabled = w.Config.NeolithicFishingEnabled;
            var residentFishReservations = fishingEnabled
                ? claims.Where(d => d.Kind == "FisheryHarvest" && d.Food > 0)
                    .GroupBy(d => d.Entity).ToDictionary(g => g.Key,
                        g => g.GroupBy(d => d.Tile).ToDictionary(tileGroup => tileGroup.Key,
                            tileGroup => tileGroup.Sum(d => d.Food)))
                : null;
            var fishery = fishingEnabled
                ? PrepareFisheryResources(w, horizon, fishingPlans, activeTiles)
                : new FisheryResourceState();
            AquaticEcologyOverlay? FisheryOverlay(int tile)
                => fishery.Overlays.TryGetValue(tile, out var overlay) ? overlay : (AquaticEcologyOverlay?)null;

            // Active stocks and field ownership are brought to the start boundary
            // before any read-only tile worker sees them.
            foreach (int tile in activeTiles.OrderBy(id => id))
            {
                LazyEcology.MaterializeEcology(w, tile, horizon.StartDays);
                touch(tile);
            }
            foreach (var camp in agriculturalCamps)
            {
                var campScratch = scratch[camp.Id];
                var budget = farmToolBudgets[camp.Id];
                var labor = CultivationEconomy.PrepareHorizon(w, camp, horizon.StartDays,
                    horizon.Years, budget, campScratch.Footprint);
                agriculturalLabor[camp.Id] = labor;
                CampFieldWorkDiagnostic fieldWork = null;
                if (ResolutionMetrics.CaptureCraftWorkDiagnostics)
                {
                    fieldWork = LastMetrics.CraftFieldWorkFor(camp.Id);
                    fieldWork.ResidentWorkerYears = Math.Max(0, camp.Population) * horizon.Years;
                    fieldWork.AssignedFarmerWorkerYears = CultivationEconomy.Workers(camp, WorkforceRoles.Farmer) * horizon.Years;
                    fieldWork.OperatingFarmerReservationWorkerYears = labor.OperatingFarmers * horizon.Years;
                    fieldWork.ExpansionFarmerWorkerYears = labor.ExpansionFarmers * horizon.Years;
                    fieldWork.MaintenanceWorkerYears = labor.MaintenanceFarmerWorkerYears;
                    fieldWork.FarmToolCoveredWorkerYears = labor.MaintenanceCoveredWorkerYears;
                }

                // The first bridge estimate is limited by available terrestrial
                // food, assigned gathering capacity and the locked camp target.
                // Actual receipts correct endpoint capital once, after integration.
                double availableLand = campScratch.LandIndices.Sum(i =>
                    LazyEcology.AccessibleAvailability(w, campScratch.Eligible[i],
                        horizon.StartDays, horizon.Days));
                double provisionalFood = Math.Min(availableLand,
                    Math.Min(labor.FoodGatherers * w.Config.GatherPerPerson * horizon.ReferenceIntervals,
                        RemainingFoodTarget(camp, needs[camp.Id]) +
                        needs[camp.Id].PlantFeedstockGap));
                double provisionalBridge = provisionalFood / Math.Max(1e-12, w.Config.GatherPerPerson)
                    * w.Config.BaseDays / w.Config.DaysPerYear;
                var allocation = CultivationEconomy.PlanHorizonExpansion(w, camp, horizon.Years,
                    labor.ExpansionFarmers * horizon.Years, provisionalBridge,
                    budget, campScratch.Footprint);
                allocations.Add(camp.Id, allocation);
                foreach (int tile in allocation.NewFieldTiles())
                    if (w.Tiles[tile].CultivationOperator < 0)
                        w.Tiles[tile].CultivationOperator = camp.Id;
                for (int i = 0; i < allocation.Tiles.Length; i++)
                {
                    int tile = allocation.Tiles[i];
                    if (w.Tiles[tile].CultivationOperator != camp.Id) continue;
                    averageCapital[tile] = allocation.AverageCapital[i];
                    endingCapital[tile] = allocation.EndCapital[i];
                    activeTiles.Add(tile);
                }
            }

            var hunterFoodCamps = new Dictionary<int, int>();
            int FoodCamp(Demand demand)
            {
                if (demand.Kind == "Herd" || demand.Kind == "Foraging") return -1;
                if (demand.Kind != "Fishing") return demand.Entity;
                if (!hunterFoodCamps.TryGetValue(demand.Entity, out int camp))
                    hunterFoodCamps[demand.Entity] = camp = Supply.Connected(w, hunters[demand.Entity])
                        ? hunters[demand.Entity].SourceCamp : -1;
                return camp;
            }
            foreach (var camp in agriculturalCamps)
            {
                var campScratch = scratch[camp.Id];
                campScratch.Clear();
                double biological = RemainingFoodTarget(camp, needs[camp.Id]);
                double feedstock = needs[camp.Id].PlantFeedstockGap;
                var reservations = fishingEnabled && residentFishReservations.TryGetValue(camp.Id, out var campFishAllocations)
                    ? campFishAllocations : new Dictionary<int, double>();
                double Available(int tile)
                {
                    double? capital = averageCapital.TryGetValue(tile, out double value)
                        ? value : (double?)null;
                    double availability = fishingEnabled
                        ? LazyEcology.AccessibleAvailability(w, tile, horizon.StartDays,
                            horizon.Days, null, capital, FisheryOverlay(tile))
                        : LazyEcology.AccessibleAvailability(w, tile, horizon.StartDays,
                            horizon.Days, null, capital);
                    return Math.Max(0, availability -
                        (reservations.TryGetValue(tile, out double reserved) ? reserved : 0));
                }
                AddAgriculturalClaims(w, camp, agriculturalLabor[camp.Id],
                    horizon.ReferenceIntervals, biological, feedstock, Available, claims, campScratch);
            }
            // Group once after every camp has proposed its fixed claims. Scanning
            // the growing global claim list for each camp becomes quadratic as
            // settlement counts rise, even though food budgets are camp-local.
            var claimsByCamp = claims.GroupBy(FoodCamp).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var camp in agriculturalCamps)
            {
                if (!claimsByCamp.TryGetValue(camp.Id, out var owned)) continue;
                double biological = RemainingFoodTarget(camp, needs[camp.Id]);
                double feedstock = needs[camp.Id].PlantFeedstockGap;
                double biologicalRequested = owned.Where(d => !d.Manufacturing).Sum(d => d.Food);
                double feedstockRequested = owned.Where(d => d.Manufacturing).Sum(d => d.Food);
                double biologicalScale = Math.Min(1, biological / Math.Max(1e-12, biologicalRequested));
                double feedstockScale = Math.Min(1, feedstock / Math.Max(1e-12, feedstockRequested));
                foreach (var claim in owned)
                    claim.Food *= claim.Manufacturing ? feedstockScale : biologicalScale;
            }

            if (w.Config.Agriculture != null)
            {
                claims.AddRange(IrrigationEconomy.DrinkingClaims(w, horizon.Years));
                foreach (int id in claims.Where(d => d.Kind == "Drinking").Select(d => d.Tile).Distinct()) { activeTiles.Add(id); touch(id); }
            }
            var irrigation = IrrigationEconomy.Allocate(w, claims, horizon.StartDays, horizon.Days);
            claims.AddRange(irrigation.Withdrawals);
            if (IrrigationEconomy.Enabled(w)) record.IrrigationWater = irrigation.Receipts;
            foreach (int id in irrigation.Fields.Keys.Concat(irrigation.Withdrawals.Select(d => d.Tile)).Distinct())
            { activeTiles.Add(id); touch(id); }
            IrrigationOverlay? IrrigationOverlayAt(int id)
                => irrigation.Fields.TryGetValue(id, out var overlay) ? overlay : (IrrigationOverlay?)null;

            // Empty field tiles still need one ecology transition for their
            // capital/condition/wood endpoint even without a food claim.
            var byTile = claims.GroupBy(d => d.Tile).ToDictionary(g => g.Key,
                g => g.OrderBy(d => d.Entity).ThenBy(d => d.Kind, StringComparer.Ordinal).ToArray());
            var groups = activeTiles.OrderBy(id => id)
                .Select(id => new { Tile = id, Claims = byTile.TryGetValue(id, out var requested)
                    ? requested : Array.Empty<Demand>() })
                .ToArray();
            LastMetrics.ActiveTiles = groups.Length;
            LastMetrics.FullTurnTiles = groups.Length;
            LastMetrics.SeasonalTiles = 0;
            LastMetrics.TileIntegrations = groups.Length;
            LastMetrics.ClaimsAllocated = claims.Count;
            LastMetrics.HorizonPreparationMilliseconds = timer.Elapsed.TotalMilliseconds;
            var cropShares = new Dictionary<int, double>();
            if (w.Config.Agriculture != null)
            {
                record.AgriculturalFood = new List<AgriculturalFoodReceipt>();
                foreach (var group in groups)
                {
                    var tile = w.Tiles[group.Tile];
                    if (!tile.Land) continue;
                    var opening = LazyEcology.ProjectEcology(w, tile.Id, horizon.StartDays);
                    var cap = new EcologyCapacity(w, tile, opening, (int)Math.Floor(horizon.StartDays / w.Config.BaseDays) % 4);
                    cropShares[tile.Id] = cap.Food > 0 ? Numbers.Clamp((cap.Food - cap.WildFood) / cap.Food, 0, 1) : 0;
                }
            }
            var integrationTimer = System.Diagnostics.Stopwatch.StartNew();
            var results = fishingEnabled
                ? execution.Map(groups, group => EcologicalPressure.Integrate(w, group.Tile,
                    group.Claims, horizon.StartDays, horizon.Days,
                    averageCapital.TryGetValue(group.Tile, out double average) ? average : (double?)null,
                    endingCapital.TryGetValue(group.Tile, out double end) ? end : (double?)null,
                    aquaticOverlay: FisheryOverlay(group.Tile), irrigationOverlay: IrrigationOverlayAt(group.Tile)))
                : execution.Map(groups, group => EcologicalPressure.Integrate(w, group.Tile,
                    group.Claims, horizon.StartDays, horizon.Days,
                    averageCapital.TryGetValue(group.Tile, out double average) ? average : (double?)null,
                    endingCapital.TryGetValue(group.Tile, out double end) ? end : (double?)null,
                    irrigationOverlay: IrrigationOverlayAt(group.Tile)));
            IrrigationEconomy.Commit(w, irrigation, horizon.StartDays + horizon.Days, horizon.Years);
            LastMetrics.FullTurnIntegrationMilliseconds = integrationTimer.Elapsed.TotalMilliseconds;
            var commitTimer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < groups.Length; i++)
            {
                int tile = groups[i].Tile;
                var result = results[i];
                if (ResolutionMetrics.CaptureCraftWorkDiagnostics)
                {
                    // groups[i].Claims is already in the canonical Entity/Kind
                    // order consumed by Integrate. LINQ's stable ordering also
                    // retains manufacturing-before-biological order for equal
                    // keys, so the transient receipt can be paired exactly with
                    // the claim whose Manufacturing tag is otherwise discarded.
                    var tileClaims = groups[i].Claims;
                    int paired = Math.Min(tileClaims.Length, result.Transfers.Count);
                    double tileRequested = tileClaims.Sum(d => d.Food);
                    double tileReceived = result.Transfers.Sum(t => t.Food);
                    var requestedByCamp = tileClaims.GroupBy(FoodCamp)
                        .ToDictionary(g => g.Key, g => g.Sum(d => d.Food));
                    var receivedByCamp = new Dictionary<int, double>();
                    for (int j = 0; j < paired; j++)
                    {
                        int owner = FoodCamp(tileClaims[j]);
                        receivedByCamp[owner] = (receivedByCamp.TryGetValue(owner, out double prior) ? prior : 0)
                            + result.Transfers[j].Food;
                    }
                    for (int j = 0; j < paired; j++)
                    {
                        var claim = tileClaims[j];
                        var receipt = result.Transfers[j];
                        int owner = FoodCamp(claim);
                        LastMetrics.CaptureFoodClaim(new FoodClaimDiagnostic
                        {
                            Tile = tile, Camp = owner, Entity = claim.Entity,
                            Kind = claim.Kind, Manufacturing = claim.Manufacturing,
                            Requested = claim.Food, Received = receipt.Food,
                            TileRequested = tileRequested, TileReceived = tileReceived,
                            CompetingRequested = Math.Max(0, tileRequested
                                - (requestedByCamp.TryGetValue(owner, out double ownRequest) ? ownRequest : 0)),
                            CompetingReceived = Math.Max(0, tileReceived
                                - (receivedByCamp.TryGetValue(owner, out double ownReceipt) ? ownReceipt : 0))
                        });
                    }
                }
                LastMetrics.ManagedRenewalInput += result.ManagedRenewalInput;
                if (result.OperatingFraction > 0 && w.Tiles[tile].CultivationCapital > 0)
                {
                    LastMetrics.OperatingFractionSum += result.OperatingFraction;
                    LastMetrics.OperatingFieldTiles++;
                }
                result.View.Commit(w.Tiles[tile]);
                if (fishery.Overlays.TryGetValue(tile, out var fisheryOverlay))
                    CommitFisherySite(w, tile, fisheryOverlay,
                        fishery.Operators.TryGetValue(tile, out int operatorCamp) ? operatorCamp : -1,
                        horizon.StartDays + horizon.Days);
                if (result.HasWoodSource)
                    MaterialEconomy.CommitWoodProjection(w, tile, result.WoodRemaining, result.WoodDays);
                record.Pressure.AddRange(result.Pressure);
                record.EcologyBalances.Add(result.Balance);
                foreach (var transfer in result.Transfers)
                {
                    transfers.Add(transfer);
                    if (record.AgriculturalFood != null && transfer.Food > 0 &&
                        (transfer.Kind == "Cultivation" || transfer.Kind == "Gathering") && cropShares.TryGetValue(tile, out double cropShare))
                        record.AgriculturalFood.Add(new AgriculturalFoodReceipt { Camp = transfer.Entity, Tile = tile,
                            CropFood = transfer.Food * cropShare, WildFood = transfer.Food * (1 - cropShare) });
                    if (transfer.Kind == "Gathering" &&
                        w.Tiles[tile].Land && camps.ContainsKey(transfer.Entity))
                        terrestrialGathered[transfer.Entity] =
                            (terrestrialGathered.TryGetValue(transfer.Entity, out double prior) ? prior : 0)
                            + transfer.Food;
                }
            }

            // Tools present at the opening support this turn's harvest only;
            // newly crafted tools arrive after resolution and cannot be spent
            // retroactively. The prepared coverage is charged once per camp.
            if (fishingEnabled)
                foreach (var plan in (fishingPlans ?? new Dictionary<int, FishingPlan>()).Values.OrderBy(p => p.CampId))
                    if (camps.TryGetValue(plan.CampId, out var fisherCamp))
                    {
                        double coveredWork = plan.Grounds.Sum(g => Math.Max(0, g.ToolCoveredHarvestWorkerYears));
                        double plannedUse = coveredWork / Math.Max(1e-12,
                            w.Config.Neolithic.FishingToolFisherYearsPerUnit);
                        double available = Math.Min(Math.Max(0, fisherCamp.Inventory.FishingTools),
                            Math.Max(0, plan.FishingToolsAtOpen));
                        fisherCamp.Inventory.FishingTools = Math.Max(0,
                            fisherCamp.Inventory.FishingTools - Math.Min(available, plannedUse));
                    }

            foreach (var camp in agriculturalCamps)
            {
                double gathered = terrestrialGathered.TryGetValue(camp.Id, out double food) ? food : 0;
                double bridge = gathered / Math.Max(1e-12, w.Config.GatherPerPerson)
                    * w.Config.BaseDays / w.Config.DaysPerYear;
                var corrected = CultivationEconomy.ReconcileHorizonBridge(w, camp,
                    allocations[camp.Id], bridge, farmToolBudgets[camp.Id]);
                if (ResolutionMetrics.CaptureCraftWorkDiagnostics)
                {
                    var fieldWork = LastMetrics.CraftFieldWorkFor(camp.Id);
                    if (fieldWork != null)
                    {
                        fieldWork.ExpansionWorkUsedWorkerYears = corrected.FarmerWorkerYearsUsed;
                        double cultivatedHarvest = transfers.Where(t => t.Entity == camp.Id && t.Kind == "Cultivation")
                            .Sum(t => t.Food);
                        fieldWork.HarvestEquivalentWorkerYears = cultivatedHarvest /
                            Math.Max(1e-12, agriculturalLabor[camp.Id].HarvestProductivity * w.Config.GatherPerPerson * horizon.ReferenceIntervals)
                            * horizon.Years;
                        // Maintenance and cultivation receipts share the same
                        // operating-farmer reservation in this model. Reconcile
                        // them with max() and then add distinct expansion work.
                        fieldWork.ReconciledFarmerWorkerYears =
                            Math.Max(fieldWork.MaintenanceWorkerYears, fieldWork.HarvestEquivalentWorkerYears)
                            + fieldWork.ExpansionWorkUsedWorkerYears;
                        // This credit is caused by gathered food and is shown
                        // separately from farmer/gatherer work to prevent double
                        // counting the same residents' time.
                        fieldWork.GatheringBridgeCreditWorkerYears = bridge;
                    }
                }
                CultivationEconomy.CommitHorizonExpansion(w, camp, corrected, record,
                    farmToolBudgets[camp.Id], allocations[camp.Id]);
            }
            // Preserve the pre-fishing path exactly when the feature is disabled:
            // only agricultural camps are visited and no fishing indexes are built.
            if (!fishingEnabled)
            {
                foreach (var camp in agriculturalCamps)
                    CultivationEconomy.UpdateVillageAtBoundary(w, camp, record);
            }
            else
            {
                // Index the sparse fishing state once. The former nested passes
                // scanned every managed ground and every transfer for every camp.
                var maturityByCamp = new Dictionary<int, (double Numerator, double Work)>();
                foreach (var pair in fishery.ManagingGrounds.OrderBy(pair => pair.Key))
                {
                    if (!fishery.Operators.TryGetValue(pair.Key, out int owner) || owner < 0) continue;
                    double work = Math.Max(0, pair.Value.AssignedFisherYears);
                    double intensity = fishery.Overlays.TryGetValue(pair.Key, out var overlay)
                        ? FishingEconomy.Intensity(w.Config, overlay.AverageCapital)
                        : pair.Value.ManagementIntensity;
                    maturityByCamp.TryGetValue(owner, out var total);
                    maturityByCamp[owner] = (total.Numerator + work * Numbers.Clamp(intensity, 0, 1),
                        total.Work + work);
                }
                var managedCatchByCamp = new Dictionary<int, double>();
                foreach (var transfer in transfers)
                    if (transfer.Kind == "FisheryHarvest" &&
                        fishery.Operators.TryGetValue(transfer.Tile, out int owner) && owner == transfer.Entity)
                        managedCatchByCamp[owner] = (managedCatchByCamp.TryGetValue(owner, out double prior)
                            ? prior : 0) + transfer.Food;
                var reportByCamp = record.Demographics.ToDictionary(report => report.Camp);
                var villageCampIds = new SortedSet<int>(agriculturalCamps.Select(camp => camp.Id));
                if (fishingPlans != null)
                    foreach (int campId in fishingPlans.Keys) villageCampIds.Add(campId);
                foreach (int campId in villageCampIds)
                {
                    if (!camps.TryGetValue(campId, out var camp)) continue;
                    maturityByCamp.TryGetValue(campId, out var total);
                    double maturity = total.Work > 0
                        ? Numbers.Clamp(total.Numerator / total.Work, 0, 1) : 0;
                    double managedCatch = managedCatchByCamp.TryGetValue(campId, out double caught) ? caught : 0;
                    double consumptionRequired = needs.TryGetValue(campId, out var campNeeds)
                        ? Math.Max(0, campNeeds.ConsumptionRequired) : 0;
                    double mealSupport = consumptionRequired > 0
                        ? Numbers.Clamp(managedCatch / consumptionRequired, 0, 1) : 0;
                    if (reportByCamp.TryGetValue(campId, out var report))
                    {
                        report.FishingVillageMaturity = maturity;
                        report.FishingVillageMealSupport = mealSupport;
                    }
                    CultivationEconomy.UpdateVillageAtBoundary(w, camp, record,
                        maturity, mealSupport, abandoningVillages.Contains(campId));
                }
            }
            LastMetrics.HorizonCommitMilliseconds = commitTimer.Elapsed.TotalMilliseconds;
            foreach (var transfer in transfers)
            {
                if (transfer.Kind == "Drinking" || transfer.Kind == "Irrigation") { /* Water-only receipt; already debited at its source. */ }
                else if (transfer.Kind == "Herd")
                {
                    var herd = herds[transfer.Entity];
                    herd.UnmetFood = Math.Max(0, herd.UnmetFood - transfer.PotentialFood);
                    herd.UnmetFreshwater = Math.Max(0, herd.UnmetFreshwater - transfer.Freshwater);
                }
                else if (transfer.Kind == "Foraging")
                {
                    foraged[transfer.Entity] =
                        (foraged.TryGetValue(transfer.Entity, out double prior) ? prior : 0) + transfer.Food;
                }
                else
                {
                    var camp = transfer.Kind == "Fishing" ? camps[hunters[transfer.Entity].SourceCamp]
                        : camps[transfer.Entity];
                    if (transfer.Kind == "Gathering" || transfer.Kind == "Marine gathering" ||
                        transfer.Kind == "Aquatic gathering")
                        gatheredFoodForMaterials[camp.Id] =
                            (gatheredFoodForMaterials.TryGetValue(camp.Id, out double prior) ? prior : 0)
                            + transfer.Food;
                    if (transfer.Kind == "Fishing")
                        Supply.Award(w, hunters[transfer.Entity], transfer.Food, record, "Fishing");
                    else
                    {
                        if (transfer.Kind == "Hunting") camp.AnimalFood += transfer.Food;
                        else if (w.Tiles[transfer.Tile].Aquatic ||
                            w.Tiles[transfer.Tile].WaterBody == WaterKind.Saltwater)
                            camp.AquaticFood += transfer.Food;
                        else camp.PlantFood += transfer.Food;
                        camp.StepFoodIncome += transfer.Food;
                    }
                }
                record.Transfers.Add(transfer);
            }
            LastMetrics.EcologyMilliseconds = timer.Elapsed.TotalMilliseconds;
            return gatheredFoodForMaterials;
        }
    }
}
