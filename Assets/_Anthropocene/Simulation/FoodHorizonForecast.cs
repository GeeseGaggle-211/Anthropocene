using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Anthropocene.Simulation
{
    // A read-only view of the resolver's one-horizon tile result. Gross source
    // terms describe physical forcing; only Receipts and Closing* are usable.
    public sealed class FoodTileForecast
    {
        public int Tile;
        public bool HasKnownCompetingClaims;
        public double OpeningFood, OpeningBiomass;
        public double HumanFoodReceipts, HerdBiomassReceipts;
        public double GrossNaturalFoodInput, GrossNaturalNonfoodInput, GrossManagedFoodInput;
        public double RetainedFoodRecovery, RetainedBiomassRecovery;
        // This source-minus-net gap includes ordinary relaxation turnover and
        // accessibility reclassification; it is not an exact clipped-flux
        // integral. FoodOverflow is only the resolver's boundary clamp ledger.
        public double GrossFoodInputLessNetRecovery, ClosingFoodCapacity;
        public bool FoodAtCapacity;
        public double FoodOverflow, BiomassOverflow;
        public double ClosingFood, ClosingBiomass;
        // Counts the ordered land-recovery profile callback only; opening
        // projection and coefficient construction are outside this counter.
        public int RecoveryProfileEvaluations;
        public Transfer[] Transfers = Array.Empty<Transfer>();

        // A query without every competing claim is an optimistic supply
        // envelope. Even a complete claim set is conditional on the supplied
        // fixed plan and opening snapshot, not a promise about future orders.
        public bool OptimisticEnvelope => !HasKnownCompetingClaims;
    }

    public sealed class FoodSettlementForecast
    {
        public int Camp, AcceptedTile;
        public bool OptimisticEnvelope;
        // TerrestrialFood includes the plant feedstock allocated first by the
        // resolver claim builder. MealFood removes that input exactly once.
        public double TerrestrialFood, AquaticFood, TerrestrialPlantFeedstock;
        public double MealFood => Math.Max(0,
            TerrestrialFood + AquaticFood - TerrestrialPlantFeedstock);
        public double OpeningLandFood, OpeningLandBiomass;
        public double GrossNaturalLandInput, GrossManagedLandInput;
        public double RetainedLandFoodRecovery, LandFoodOverflow;
        public int TileQueries;
        public FoodTileForecast[] Tiles = Array.Empty<FoodTileForecast>();
    }

    public sealed class FoodInvestmentForecast
    {
        public FoodTileForecast Before, After;
        public double RetainedFoodGain, AnnualizedRetainedFoodGain;
        public double BuildWorkerYears, OperationWorkerYears;
        public int TileQueries = 2;
        public double AnnualizedFoodPerWorkerYear =>
            AnnualizedRetainedFoodGain / Math.Max(1e-12, BuildWorkerYears + OperationWorkerYears);
    }

    public static class FoodHorizonForecast
    {
        private sealed class RecoveryProfileCounter
        {
            public int Count;
            public void Increment() { Count++; }
        }
        // The current policy compares an annualized next-boundary food gain
        // against one build cost plus its existing PlanningHorizonYears operation
        // weight. Those costs are explicit caller inputs, not ecological source
        // terms. This is a single transition; it assumes no later re-harvest.
        public static FoodInvestmentForecast Investment(WorldState snapshot, int tileId,
            double startDays, double durationDays, double beforeCapital,
            double afterMeanCapital, double afterEndCapital, double operatingFraction,
            double buildWorkerYears = 0, double operationWorkerYears = 0,
            EcologyView? openingEcology = null,
            AquaticEcologyOverlay? aquaticOverlay = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (durationDays < 0 || double.IsNaN(durationDays) || double.IsInfinity(durationDays))
                throw new ArgumentOutOfRangeException(nameof(durationDays));
            if (ResolutionMetrics.CaptureDetailedCounters)
                Interlocked.Increment(ref TurnDiagnostics.For(snapshot).FoodInvestmentComparisons);
            var before = Tile(snapshot, tileId, startDays, durationDays,
                null, beforeCapital, beforeCapital, operatingFraction, true,
                openingEcology, aquaticOverlay: aquaticOverlay);
            var after = Tile(snapshot, tileId, startDays, durationDays,
                null, afterMeanCapital, afterEndCapital, operatingFraction, true,
                openingEcology, aquaticOverlay: aquaticOverlay);
            double gain = after.ClosingFood - before.ClosingFood;
            return new FoodInvestmentForecast
            {
                Before = before, After = after,
                RetainedFoodGain = gain,
                AnnualizedRetainedFoodGain = durationDays > 0
                    ? gain * snapshot.Config.DaysPerYear / durationDays : 0,
                BuildWorkerYears = Math.Max(0, buildWorkerYears),
                OperationWorkerYears = Math.Max(0, operationWorkerYears)
            };
        }

        // The only stock transition here is the authoritative tile integrator.
        // Work is O(claims + local tiles * fixed four-profile cost), independent
        // of duration/BaseDays. The caller supplies known claims; this service
        // never discovers rivals or reads remote private camp inventory.
        public static FoodTileForecast Tile(WorldState snapshot, int tileId, double startDays,
            double durationDays, IEnumerable<Demand> claims = null,
            double? averageCultivationCapital = null, double? endingCultivationCapital = null,
            double? operatingFraction = null, bool allCompetitionKnown = false,
            EcologyView? openingEcology = null, bool countRecoveryProfiles = false,
            AquaticEcologyOverlay? aquaticOverlay = null, IrrigationOverlay? irrigationOverlay = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (tileId < 0 || tileId >= snapshot.Tiles.Length) throw new ArgumentOutOfRangeException(nameof(tileId));
            if (double.IsNaN(durationDays) || double.IsInfinity(durationDays) || durationDays < 0)
                throw new ArgumentOutOfRangeException(nameof(durationDays));
            if (openingEcology.HasValue && Math.Abs(openingEcology.Value.Days - startDays) > 1e-7)
                throw new ArgumentException("Opening ecology must be at the forecast boundary.",
                    nameof(openingEcology));
            if (ResolutionMetrics.CaptureDetailedCounters)
                Interlocked.Increment(ref TurnDiagnostics.For(snapshot).FoodForecastQueries);
            var profileCounter = countRecoveryProfiles ? new RecoveryProfileCounter() : null;
            var result = EcologicalPressure.Integrate(snapshot, tileId,
                claims ?? Array.Empty<Demand>(), startDays, durationDays,
                averageCultivationCapital, endingCultivationCapital, operatingFraction,
                projectWood: false,
                profileEvaluated: profileCounter == null ? null : new Action(profileCounter.Increment),
                openingEcology: openingEcology, captureGrossForcing: true,
                aquaticOverlay: aquaticOverlay, irrigationOverlay: irrigationOverlay);
            var forecast = new FoodTileForecast
            {
                Tile = tileId,
                HasKnownCompetingClaims = allCompetitionKnown,
                OpeningFood = result.Balance.StartingFood,
                OpeningBiomass = result.Balance.StartingBiomass,
                HumanFoodReceipts = result.Transfers.Sum(t => t.Food),
                HerdBiomassReceipts = result.Transfers.Sum(t => t.PotentialFood),
                RetainedFoodRecovery = result.Balance.FoodRecharge,
                RetainedBiomassRecovery = result.Balance.BiomassRecharge,
                GrossNaturalFoodInput = result.GrossNaturalFoodInput,
                GrossNaturalNonfoodInput = result.GrossNaturalNonfoodInput,
                GrossManagedFoodInput = result.ManagedRenewalInput,
                FoodOverflow = result.Balance.FoodOverflow,
                BiomassOverflow = result.Balance.BiomassOverflow,
                ClosingFood = result.Balance.EndingFood,
                ClosingBiomass = result.Balance.EndingBiomass,
                RecoveryProfileEvaluations = profileCounter?.Count ?? 0,
                Transfers = result.Transfers.ToArray()
            };
            forecast.GrossFoodInputLessNetRecovery = Math.Max(0,
                forecast.GrossNaturalFoodInput + forecast.GrossManagedFoodInput -
                forecast.RetainedFoodRecovery);
            var closingOverlay = LazyEcology.EndpointAquaticOverlay(aquaticOverlay ??
                LazyEcology.ProjectAquaticOverlay(snapshot, tileId, startDays,
                    durationDays, operatingFraction));
            var closingCapacity = new EcologyCapacity(snapshot, snapshot.Tiles[tileId],
                result.View, (int)Math.Floor(result.View.Days / snapshot.Config.BaseDays) % 4,
                aquaticOverlay: closingOverlay);
            forecast.ClosingFoodCapacity = Math.Min(closingCapacity.Food,
                forecast.ClosingBiomass);
            forecast.FoodAtCapacity = forecast.ClosingFoodCapacity > 0 &&
                forecast.ClosingFood >= forecast.ClosingFoodCapacity -
                    1e-8 * Math.Max(1, forecast.ClosingFoodCapacity);
            return forecast;
        }

        // A fixed-plan local forecast uses the resolver's exact agricultural
        // claim constructor. The accepted destination is explicit: rejected
        // moves pass the original tile, accepted moves pass the destination.
        // knownCompetingClaims must contain only claims the caller may observe.
        public static FoodSettlementForecast Settlement(WorldState snapshot, Camp camp,
            int acceptedTile, double startDays, double durationDays, AgriculturalLabor labor,
            double biologicalFoodTarget, double plantFeedstockTarget,
            HorizonCultivationAllocation cultivation = null,
            IEnumerable<Demand> knownCompetingClaims = null,
            Func<int, bool> tileKnown = null, bool allCompetitionKnown = false)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (camp == null) throw new ArgumentNullException(nameof(camp));
            if (labor == null) throw new ArgumentNullException(nameof(labor));
            if (acceptedTile < 0 || acceptedTile >= snapshot.Tiles.Length)
                throw new ArgumentOutOfRangeException(nameof(acceptedTile));
            if (durationDays < 0 || double.IsNaN(durationDays) || double.IsInfinity(durationDays))
                throw new ArgumentOutOfRangeException(nameof(durationDays));
            if (labor.Footprint != null && labor.Footprint.Any(id =>
                id < 0 || id >= snapshot.Tiles.Length || snapshot.Map.Distance(acceptedTile, id) > 1))
                throw new ArgumentException("Field footprint must belong to the accepted location.", nameof(labor));
            if (!CultivationEconomy.Enabled(camp) &&
                (labor.OperatingFarmers > 0 || labor.ExpansionFarmers > 0 ||
                 (labor.Footprint?.Length ?? 0) > 0))
                throw new ArgumentException("Agricultural labor requires Agriculture.", nameof(labor));
            bool abandoningVillage = camp.IsVillage && acceptedTile != camp.Tile;
            if (abandoningVillage &&
                ((labor.Footprint?.Length ?? 0) > 0 || labor.OperatingFarmers > 0 ||
                 labor.ExpansionFarmers > 0))
                throw new ArgumentException("An accepted village departure has no field work this turn.",
                    nameof(labor));
            var location = acceptedTile == camp.Tile ? camp : new Camp
            {
                Id = camp.Id, Tile = acceptedTile, Player = camp.Player, Population = camp.Population
            };
            if (!ReferenceEquals(location, camp)) location.Bind(camp.People);
            var ownClaims = new List<Demand>();
            var localTiles = new HashSet<int>();
            double Available(int id)
            {
                if (tileKnown != null && !tileKnown(id)) return 0;
                double? mean = cultivation != null && cultivation.TryGetTile(id, out double average, out _)
                    ? average : (double?)null;
                return LazyEcology.AccessibleAvailability(snapshot, id, startDays,
                    durationDays, null, mean);
            }
            if (CultivationEconomy.Enabled(camp) && !abandoningVillage)
            {
                var scratch = new TurnResolver.AgriculturalClaimScratch(snapshot, location,
                    labor.Footprint ?? Array.Empty<int>());
                TurnResolver.AddAgriculturalClaims(snapshot, location, labor,
                    durationDays / snapshot.Config.BaseDays,
                    Math.Max(0, biologicalFoodTarget),
                    Math.Max(0, plantFeedstockTarget), Available, ownClaims, scratch);
                foreach (int id in scratch.Eligible) localTiles.Add(id);
            }
            else
            {
                TurnResolver.AddPassiveCampClaims(snapshot, location, labor.Gatherers,
                    startDays, durationDays, durationDays / snapshot.Config.BaseDays,
                    abandoningVillage, Available, ownClaims);
                double requested = ownClaims.Sum(d => d.Food);
                double scale = Math.Min(1, Math.Max(0, biologicalFoodTarget) /
                    Math.Max(1e-12, requested));
                foreach (var claim in ownClaims) claim.Food *= scale;
                foreach (int id in snapshot.Map.Radius(acceptedTile,
                    snapshot.Config.GatherRadius))
                    if (PassiveGathering.Eligible(snapshot, location, id)) localTiles.Add(id);
            }
            foreach (int id in labor.Footprint ?? Array.Empty<int>()) localTiles.Add(id);
            foreach (int id in cultivation?.Tiles ?? Array.Empty<int>()) localTiles.Add(id);
            // One shared water forecast, using the caller's known claims. Like
            // food competition this remains an optimistic envelope when the
            // caller cannot observe all competing water users.
            IrrigationAllocation irrigation;
            var localWaterSources = IrrigationEconomy.LocalSources(snapshot, localTiles);
            if (localWaterSources.Count > 0)
            {
                var waterClaims = (knownCompetingClaims ?? Array.Empty<Demand>()).ToList();
                bool hasDrinking = waterClaims.Any(d => d.Kind == "Drinking"), hasSanitation = waterClaims.Any(d => d.Kind == "Sanitation"), hasHerds = waterClaims.Any(d => d.Kind == "Herd" && d.Freshwater > 0);
                waterClaims.AddRange(IrrigationEconomy.EssentialForecastClaimsForSources(snapshot, durationDays / snapshot.Config.DaysPerYear, localWaterSources, tileKnown)
                    .Where(d => d.Kind == "Drinking" ? !hasDrinking : d.Kind == "Sanitation" ? !hasSanitation : !hasHerds));
                irrigation = IrrigationEconomy.AllocateLocal(snapshot, waterClaims, startDays, durationDays, localWaterSources, location);
            }
            else irrigation = new IrrigationAllocation();
            var allClaims = ownClaims.Concat(knownCompetingClaims ?? Array.Empty<Demand>())
                .Where(d => localTiles.Contains(d.Tile) &&
                    (tileKnown == null || tileKnown(d.Tile)))
                .GroupBy(d => d.Tile).ToDictionary(g => g.Key,
                    g => g.OrderBy(d => d.Entity).ThenBy(d => d.Kind,
                        StringComparer.Ordinal).ToArray());
            var tileIds = allClaims.Keys.Concat(labor.Footprint ?? Array.Empty<int>())
                .Concat(cultivation?.Tiles ?? Array.Empty<int>())
                .Where(id => tileKnown == null || tileKnown(id))
                .Distinct().OrderBy(id => id).ToArray();
            var forecast = new FoodSettlementForecast
            {
                Camp = camp.Id, AcceptedTile = acceptedTile,
                OptimisticEnvelope = !allCompetitionKnown
            };
            var tiles = new List<FoodTileForecast>(tileIds.Length);
            foreach (int tileId in tileIds)
            {
                double? average = null, end = null;
                if (cultivation != null && cultivation.TryGetTile(tileId, out double a, out double e))
                { average = a; end = e; }
                double? operation = null;
                if (abandoningVillage && snapshot.Tiles[tileId].CultivationOperator == camp.Id &&
                    snapshot.Map.Distance(camp.Tile, tileId) <= 1)
                    operation = 0;
                else if (snapshot.Tiles[tileId].CultivationOperator == camp.Id)
                {
                    if (labor.RequiredMaintenance > 0)
                        operation = Math.Min(1, labor.EffectiveMaintenanceFarmers /
                            Math.Max(1e-12, labor.RequiredMaintenance));
                    else if (labor.EffectiveMaintenanceFarmers > 0) operation = 1;
                }
                var requested = allClaims.TryGetValue(tileId, out Demand[] claims)
                    ? claims : Array.Empty<Demand>();
                var tileForecast = Tile(snapshot, tileId, startDays, durationDays,
                    requested, average, end, operation, allCompetitionKnown,
                    irrigationOverlay: irrigation.Fields.TryGetValue(tileId, out var overlay) ? overlay : (IrrigationOverlay?)null);
                tiles.Add(tileForecast);
                for (int i = 0; i < requested.Length; i++)
                {
                    if (requested[i].Entity != camp.Id || i >= tileForecast.Transfers.Length) continue;
                    double receipt = tileForecast.Transfers[i].Food;
                    if (snapshot.Tiles[tileId].Land)
                    {
                        forecast.TerrestrialFood += receipt;
                        if (requested[i].Manufacturing)
                            forecast.TerrestrialPlantFeedstock += receipt;
                    }
                    else forecast.AquaticFood += receipt;
                }
                if (!snapshot.Tiles[tileId].Land) continue;
                forecast.OpeningLandFood += tileForecast.OpeningFood;
                forecast.OpeningLandBiomass += tileForecast.OpeningBiomass;
                forecast.GrossNaturalLandInput += tileForecast.GrossNaturalFoodInput;
                forecast.GrossManagedLandInput += tileForecast.GrossManagedFoodInput;
                forecast.RetainedLandFoodRecovery += tileForecast.RetainedFoodRecovery;
                forecast.LandFoodOverflow += tileForecast.FoodOverflow;
            }
            forecast.Tiles = tiles.ToArray();
            forecast.TileQueries = tiles.Count;
            return forecast;
        }
    }
}
