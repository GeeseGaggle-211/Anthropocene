using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Anthropocene.Simulation
{
    // Shared physical counterfactual for immigration and fission
    // (Docs/UnifiedSocialRelocationContract-v0.2.md).  Authoritative state stays in
    // Camp, PopulationLedger, inventory, herds and the demographic ledgers; every
    // type in this file is a transient read-only forecast or a pure calculation.
    //
    // Temporal abstraction: one bounded turn-horizon service envelope.  The
    // forecast does not advance the economy, ecology or demographic integrator.
    // It reads one cached local physical-capacity projection per settlement, caps
    // it by the workforce in the counterfactual, runs the bounded material recipe
    // forecast, and analytically integrates constant hazard bands.  Candidate work
    // is O(recipes + workforce roles); local ecology/source discovery is cached per
    // settlement or performed once for the selected daughter site.

    public enum SocialRelocationDestination { ExistingSettlement, NewDaughter }

    [Flags]
    public enum SocialForecastConfidence
    {
        None = 0,
        LocalPhysicalCapacity = 1,
        WorkforceBound = 2,
        FiniteStocks = 4,
        CommittedTrade = 8,
        MaterialInputs = 16,
        NewSiteEstimate = 32,
        WaterObserved = 64
    }

    // Future-only opportunity contract.  Every ceiling is a count of positions.
    // A position is credible only up to the minimum of demand, funding, tools,
    // inputs and workplace capacity, after filled and reserved positions.  Output
    // terms are physical additions per matched worker; preference is not aggregate
    // welfare and has zero weight in current Neolithic gameplay.
    public sealed class EmploymentOpportunityRecord
    {
        public readonly string Role;
        public readonly int Demand, Funding, ToolCapacity, InputCapacity, WorkplaceCapacity, Filled, Reserved;
        public readonly double PreferencePerWorker, FoodPerWorker, ClothingPerWorker, ShelterPeoplePerWorker, VerifiedSurplusPerWorker;
        public EmploymentOpportunityRecord(string role, int demand, int funding, int toolCapacity, int inputCapacity,
            int workplaceCapacity, int filled = 0, int reserved = 0, double preferencePerWorker = 0,
            double foodPerWorker = 0, double clothingPerWorker = 0, double shelterPeoplePerWorker = 0,
            double verifiedSurplusPerWorker = 0)
        {
            Role = role ?? ""; Demand = demand; Funding = funding; ToolCapacity = toolCapacity;
            InputCapacity = inputCapacity; WorkplaceCapacity = workplaceCapacity; Filled = filled; Reserved = reserved;
            PreferencePerWorker = preferencePerWorker; FoodPerWorker = foodPerWorker;
            ClothingPerWorker = clothingPerWorker; ShelterPeoplePerWorker = shelterPeoplePerWorker;
            VerifiedSurplusPerWorker = verifiedSurplusPerWorker;
        }
    }

    public sealed class EmploymentOpportunityRequest
    {
        public readonly int SourceCamp, DestinationCamp, Movers;
        public readonly SocialRelocationDestination DestinationKind;
        public readonly IReadOnlyDictionary<string, int> QualifiedWorkers;
        public readonly IReadOnlyDictionary<string, int> PendingReservations;
        public EmploymentOpportunityRequest(int sourceCamp, int destinationCamp, int movers,
            SocialRelocationDestination destinationKind, IReadOnlyDictionary<string, int> qualifiedWorkers,
            IReadOnlyDictionary<string, int> pendingReservations = null)
        {
            SourceCamp = sourceCamp; DestinationCamp = destinationCamp; Movers = Math.Max(0, movers);
            DestinationKind = destinationKind;
            QualifiedWorkers = qualifiedWorkers ?? new Dictionary<string, int>();
            PendingReservations = pendingReservations ?? new Dictionary<string, int>();
        }
    }

    public interface IEmploymentOpportunityProvider
    {
        IReadOnlyList<EmploymentOpportunityRecord> Opportunities(EmploymentOpportunityRequest request);
    }

    public sealed class NeutralEmploymentOpportunityProvider : IEmploymentOpportunityProvider
    {
        public static readonly NeutralEmploymentOpportunityProvider Instance = new NeutralEmploymentOpportunityProvider();
        private static readonly EmploymentOpportunityRecord[] Empty = Array.Empty<EmploymentOpportunityRecord>();
        private NeutralEmploymentOpportunityProvider() { }
        public IReadOnlyList<EmploymentOpportunityRecord> Opportunities(EmploymentOpportunityRequest request) => Empty;
    }

    public sealed class EmploymentOpportunityMatch
    {
        public readonly int Matched;
        public readonly double Preference, Food, Clothing, ShelterPeople, VerifiedSurplus;
        public readonly IReadOnlyDictionary<string, int> Reservations;
        internal EmploymentOpportunityMatch(int matched, double preference, double food, double clothing,
            double shelterPeople, double verifiedSurplus, IReadOnlyDictionary<string, int> reservations)
        {
            Matched = matched; Preference = preference; Food = food; Clothing = clothing;
            ShelterPeople = shelterPeople; VerifiedSurplus = verifiedSurplus; Reservations = reservations;
        }
        public static readonly EmploymentOpportunityMatch Zero = new EmploymentOpportunityMatch(0, 0, 0, 0, 0, 0,
            new Dictionary<string, int>());

        internal static EmploymentOpportunityMatch Combine(EmploymentOpportunityMatch left,
            EmploymentOpportunityMatch right)
        {
            left = left ?? Zero; right = right ?? Zero;
            if (left.Matched == 0) return right;
            if (right.Matched == 0) return left;
            var reservations = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in left.Reservations.Concat(right.Reservations))
                reservations[pair.Key] = (reservations.TryGetValue(pair.Key, out int prior) ? prior : 0) + pair.Value;
            return new EmploymentOpportunityMatch(left.Matched + right.Matched,
                left.Preference + right.Preference, left.Food + right.Food,
                left.Clothing + right.Clothing, left.ShelterPeople + right.ShelterPeople,
                left.VerifiedSurplus + right.VerifiedSurplus, reservations);
        }

        // Exact inverse of Combine for one constituent match.  Joint migration
        // forecasting aggregates all arrivals at an endpoint once, then removes
        // the candidate's own contribution for its X0 counterfactual.  Match
        // fields are additive here (Preference is already the per-request value
        // emitted by Match), so this remains O(bounded roles) and avoids rescanning
        // every other incoming flow for each candidate.
        internal static EmploymentOpportunityMatch Subtract(EmploymentOpportunityMatch total,
            EmploymentOpportunityMatch part)
        {
            total = total ?? Zero; part = part ?? Zero;
            if (part.Matched == 0) return total;
            int matched = total.Matched - part.Matched;
            if (matched <= 0) return Zero;
            var reservations = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in total.Reservations)
            {
                part.Reservations.TryGetValue(pair.Key, out int removed);
                int remaining = pair.Value - removed;
                if (remaining > 0) reservations[pair.Key] = remaining;
            }
            return new EmploymentOpportunityMatch(matched,
                total.Preference - part.Preference, total.Food - part.Food,
                total.Clothing - part.Clothing, total.ShelterPeople - part.ShelterPeople,
                total.VerifiedSurplus - part.VerifiedSurplus, reservations);
        }
    }

    public static class EmploymentOpportunityMatcher
    {
        private static int Nonnegative(int value) => Math.Max(0, value);
        private static double FiniteNonnegative(double value)
            => double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, value);

        // Pure deterministic matching.  Roles are canonical and a worker can fill
        // at most one position even if a provider emits duplicate records.
        public static EmploymentOpportunityMatch Match(IEmploymentOpportunityProvider provider,
            EmploymentOpportunityRequest request)
        {
            provider = provider ?? NeutralEmploymentOpportunityProvider.Instance;
            var records = provider.Opportunities(request) ?? Array.Empty<EmploymentOpportunityRecord>();
            if (records.Count == 0 || request.Movers <= 0) return EmploymentOpportunityMatch.Zero;
            var qualified = request.QualifiedWorkers.ToDictionary(p => p.Key, p => Nonnegative(p.Value), StringComparer.Ordinal);
            var reservations = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = 0; double preference = 0, food = 0, clothing = 0, shelter = 0, surplus = 0;
            foreach (var record in records.Where(r => r != null).OrderBy(r => r.Role, StringComparer.Ordinal))
            {
                string role = record.Role ?? "";
                qualified.TryGetValue(role, out int workers);
                request.PendingReservations.TryGetValue(role, out int pending);
                int capacity = new[] { record.Demand, record.Funding, record.ToolCapacity, record.InputCapacity, record.WorkplaceCapacity }.Min();
                int available = Math.Max(0, capacity - Nonnegative(record.Filled) - Nonnegative(record.Reserved) - Nonnegative(pending));
                int matched = Math.Min(Math.Min(workers, available), request.Movers - total);
                if (matched <= 0) continue;
                qualified[role] = workers - matched; total += matched;
                reservations[role] = (reservations.TryGetValue(role, out int prior) ? prior : 0) + matched;
                preference += matched * FiniteNonnegative(record.PreferencePerWorker);
                food += matched * FiniteNonnegative(record.FoodPerWorker);
                clothing += matched * FiniteNonnegative(record.ClothingPerWorker);
                shelter += matched * FiniteNonnegative(record.ShelterPeoplePerWorker);
                surplus += matched * FiniteNonnegative(record.VerifiedSurplusPerWorker);
                if (total == request.Movers) break;
            }
            return total == 0 ? EmploymentOpportunityMatch.Zero
                : new EmploymentOpportunityMatch(total, preference / request.Movers, food, clothing, shelter, surplus, reservations);
        }
    }

    // Immutable service envelope.  QoL is the forecast of the existing experienced
    // welfare function; mortality inputs and hazards remain explicit and separate.
    public sealed class SocialServiceForecast
    {
        public readonly int Camp, Population;
        public readonly double HorizonYears, FoodAvailable, FoodNeed, Nutrition;
        public readonly double ClothingAvailable, ClothingNeed, ClothingCoverage;
        public readonly double ShelterCapacity, HousingCoverage, WaterCoverage;
        public readonly double QoL, MortalityRisk, PersonYears, EndPopulation;
        public readonly double BirthRate, MortalityRate, ExposureRate, CrowdingRate, DiseaseRate, StarvationRate;
        public readonly double ExposureDeaths, CrowdingDeaths, DiseaseDeaths, StarvationDeaths;
        public readonly double RenewableFoodCoverage, RenewableClothingCoverage, Stability;
        public readonly SocialForecastConfidence Confidence;
        public SocialServiceForecast(int camp, int population, double horizonYears, double foodAvailable, double foodNeed,
            double nutrition, double clothingAvailable, double clothingNeed, double clothingCoverage,
            double shelterCapacity, double housingCoverage, double waterCoverage, double qoL, double mortalityRisk,
            double personYears, double endPopulation, double birthRate, double mortalityRate, double exposureRate,
            double crowdingRate, double diseaseRate, double starvationRate, double exposureDeaths,
            double crowdingDeaths, double diseaseDeaths, double starvationDeaths, double renewableFoodCoverage,
            double renewableClothingCoverage, double stability, SocialForecastConfidence confidence)
        {
            Camp = camp; Population = population; HorizonYears = horizonYears; FoodAvailable = foodAvailable;
            FoodNeed = foodNeed; Nutrition = nutrition; ClothingAvailable = clothingAvailable; ClothingNeed = clothingNeed;
            ClothingCoverage = clothingCoverage; ShelterCapacity = shelterCapacity; HousingCoverage = housingCoverage;
            WaterCoverage = waterCoverage; QoL = qoL; MortalityRisk = mortalityRisk; PersonYears = personYears;
            EndPopulation = endPopulation; BirthRate = birthRate; MortalityRate = mortalityRate;
            ExposureRate = exposureRate; CrowdingRate = crowdingRate; DiseaseRate = diseaseRate;
            StarvationRate = starvationRate; ExposureDeaths = exposureDeaths; CrowdingDeaths = crowdingDeaths;
            DiseaseDeaths = diseaseDeaths; StarvationDeaths = starvationDeaths;
            RenewableFoodCoverage = renewableFoodCoverage; RenewableClothingCoverage = renewableClothingCoverage;
            Stability = stability; Confidence = confidence;
        }

        public static SocialServiceForecast Empty(int camp, double years)
            => new SocialServiceForecast(camp, 0, years, 0, 0, 1, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, SocialForecastConfidence.None);
    }

    public sealed class SocialRelocationPolicy
    {
        public readonly double MoverThreshold, AggregateThreshold, SourceHarmTolerance, IncumbentHarmTolerance;
        public readonly double WelfareWeight, MortalityWeight, JobWeight, PrivateCost, RealCost;
        public SocialRelocationPolicy(double moverThreshold, double aggregateThreshold, double sourceHarmTolerance,
            double incumbentHarmTolerance, double mortalityWeight = 1, double jobWeight = 0,
            double privateCost = 0, double realCost = 0, double welfareWeight = 1)
        {
            MoverThreshold = moverThreshold; AggregateThreshold = aggregateThreshold;
            SourceHarmTolerance = sourceHarmTolerance; IncumbentHarmTolerance = incumbentHarmTolerance;
            WelfareWeight = welfareWeight; MortalityWeight = mortalityWeight; JobWeight = jobWeight;
            PrivateCost = privateCost; RealCost = realCost;
        }
    }

    public sealed class SocialRelocationEvaluation
    {
        public readonly SocialRelocationDestination DestinationKind;
        public readonly SocialServiceForecast SourceNoMove, SourceAfter, DestinationNoMove, DestinationAfter;
        public readonly double MoverUtilityChange, SourceUtilityChange, IncumbentUtilityChange, AggregateWelfareChange;
        public readonly double MoverUtilityBefore, MoverUtilityAfter;
        public readonly bool MoverPass, AggregatePass, SourcePass, IncumbentPass, Accepted;
        public readonly EmploymentOpportunityMatch Opportunity;
        internal SocialRelocationEvaluation(SocialRelocationDestination destinationKind,
            SocialServiceForecast sourceNoMove, SocialServiceForecast sourceAfter,
            SocialServiceForecast destinationNoMove, SocialServiceForecast destinationAfter,
            double moverUtilityChange, double sourceUtilityChange, double incumbentUtilityChange,
            double aggregateWelfareChange, double moverUtilityBefore, double moverUtilityAfter,
            bool moverPass, bool aggregatePass, bool sourcePass, bool incumbentPass,
            EmploymentOpportunityMatch opportunity)
        {
            DestinationKind = destinationKind; SourceNoMove = sourceNoMove; SourceAfter = sourceAfter;
            DestinationNoMove = destinationNoMove; DestinationAfter = destinationAfter;
            MoverUtilityChange = moverUtilityChange; SourceUtilityChange = sourceUtilityChange;
            IncumbentUtilityChange = incumbentUtilityChange; AggregateWelfareChange = aggregateWelfareChange;
            MoverUtilityBefore = moverUtilityBefore; MoverUtilityAfter = moverUtilityAfter;
            MoverPass = moverPass; AggregatePass = aggregatePass; SourcePass = sourcePass;
            IncumbentPass = incumbentPass; Accepted = moverPass && aggregatePass && sourcePass && incumbentPass;
            Opportunity = opportunity ?? EmploymentOpportunityMatch.Zero;
        }
    }

    public static class SocialRelocationEvaluator
    {
        private static double Utility(SocialServiceForecast forecast, SocialRelocationPolicy policy, double jobPreference)
            => policy.WelfareWeight * forecast.QoL - policy.MortalityWeight * forecast.MortalityRisk
                + policy.JobWeight * jobPreference;

        // Pure shared acceptance rule.  The mover's X0 is the source no-move
        // envelope; source stayers and receiver incumbents compare their own X1
        // and X0 envelopes.  Aggregate welfare contains only forecast experienced
        // QoL, verified surplus and a real cost paid once; job preference never
        // appears in aggregate benefit.
        public static SocialRelocationEvaluation Evaluate(SocialRelocationDestination kind,
            SocialServiceForecast sourceNoMove, SocialServiceForecast sourceAfter,
            SocialServiceForecast destinationNoMove, SocialServiceForecast destinationAfter,
            SocialRelocationPolicy policy, EmploymentOpportunityMatch opportunity = null,
            double sourceForegoneJobPreference = 0)
        {
            opportunity = opportunity ?? EmploymentOpportunityMatch.Zero;
            double before = Utility(sourceNoMove, policy, sourceForegoneJobPreference);
            double after = Utility(destinationAfter, policy, opportunity.Preference) - policy.PrivateCost;
            double mover = after - before;
            double source = Utility(sourceAfter, policy, 0) - Utility(sourceNoMove, policy, 0);
            double incumbent = kind == SocialRelocationDestination.NewDaughter ? 0
                : Utility(destinationAfter, policy, 0) - Utility(destinationNoMove, policy, 0);
            double initial = sourceNoMove.Population + (kind == SocialRelocationDestination.ExistingSettlement ? destinationNoMove.Population : 0);
            double horizon = Math.Max(1e-12, sourceNoMove.HorizonYears);
            double beforeWelfare = sourceNoMove.PersonYears * sourceNoMove.QoL
                + (kind == SocialRelocationDestination.ExistingSettlement ? destinationNoMove.PersonYears * destinationNoMove.QoL : 0);
            double afterWelfare = sourceAfter.PersonYears * sourceAfter.QoL + destinationAfter.PersonYears * destinationAfter.QoL;
            double aggregate = initial > 0
                ? (afterWelfare - beforeWelfare + opportunity.VerifiedSurplus - policy.RealCost) / (horizon * initial)
                : 0;
            const double tolerance = 1e-12;
            bool moverPass = mover + tolerance >= policy.MoverThreshold;
            bool aggregatePass = aggregate + tolerance >= policy.AggregateThreshold;
            bool sourcePass = source + tolerance >= -policy.SourceHarmTolerance;
            bool incumbentPass = kind == SocialRelocationDestination.NewDaughter
                || incumbent + tolerance >= -policy.IncumbentHarmTolerance;
            return new SocialRelocationEvaluation(kind, sourceNoMove, sourceAfter, destinationNoMove, destinationAfter,
                mover, source, incumbent, aggregate, before, after, moverPass, aggregatePass, sourcePass,
                incumbentPass, opportunity);
        }
    }

    public sealed class WorkforceTransfer
    {
        public readonly PopulationLedger Stayers, Movers;
        public readonly IReadOnlyDictionary<string, int> MoverComposition;
        private WorkforceTransfer(PopulationLedger stayers, PopulationLedger movers, IReadOnlyDictionary<string, int> composition)
        { Stayers = stayers; Movers = movers; MoverComposition = composition; }

        public static PopulationLedger Copy(PopulationLedger ledger)
        {
            var copy = new PopulationLedger();
            if (ledger?.Assignments != null)
                copy.Assignments = ledger.Assignments.Where(a => a != null && a.Population > 0)
                    .Select(a => new PopulationAssignment { Role = a.Role, Population = a.Population }).ToList();
            return copy;
        }

        // Same largest-remainder rule previously local to fission and used by
        // ApplyCasualties.  The mover ledger is retained so immigration does not
        // silently destroy assigned workforce composition.
        public static WorkforceTransfer Split(PopulationLedger ledger, int residents, int take,
            bool daughterFarmersBecomeGatherers = false)
        {
            if (residents < 0 || take < 0 || take > residents) throw new ArgumentException("Invalid workforce transfer.");
            var source = Copy(ledger); source.Normalize(residents);
            var roles = source.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).ToArray();
            var counts = new[] { source.Gatherers(residents) }.Concat(roles.Select(a => a.Population)).ToArray();
            long total = counts.Sum(c => (long)c);
            var share = counts.Select(c => total <= 0 ? 0 : (int)(take * (long)c / total)).ToArray();
            int remainder = take - share.Sum();
            foreach (int i in Enumerable.Range(0, counts.Length)
                .OrderByDescending(i => total <= 0 ? 0 : take * (long)counts[i] % total).ThenBy(i => i))
                if (remainder > 0 && share[i] < counts[i]) { share[i]++; remainder--; }
            var movers = new PopulationLedger();
            var composition = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [WorkforceRoles.Gatherer] = share[0]
            };
            for (int i = 0; i < roles.Length; i++)
            {
                int moved = share[i + 1]; roles[i].Population -= moved;
                string role = daughterFarmersBecomeGatherers && roles[i].Role == WorkforceRoles.Farmer
                    ? WorkforceRoles.Gatherer : roles[i].Role;
                if (moved <= 0) continue;
                composition[role] = (composition.TryGetValue(role, out int prior) ? prior : 0) + moved;
                if (role != WorkforceRoles.Gatherer)
                    movers.Assignments.Add(new PopulationAssignment { Role = role, Population = moved });
            }
            source.Normalize(residents - take); movers.Normalize(take);
            return new WorkforceTransfer(source, movers, composition);
        }

        public static PopulationLedger Merge(PopulationLedger residents, PopulationLedger arrivals, int finalPopulation)
        {
            var result = Copy(residents);
            foreach (var assignment in arrivals?.Assignments ?? new List<PopulationAssignment>())
            {
                var existing = result.Assignments.FirstOrDefault(a => a.Role == assignment.Role);
                if (existing == null) result.Assignments.Add(new PopulationAssignment { Role = assignment.Role, Population = assignment.Population });
                else existing.Population += assignment.Population;
            }
            result.Normalize(finalPopulation); return result;
        }
    }

    // Counterfactual camp view plus optional new-site capacity.  Views always own
    // their inventory and workforce; no forecast may mutate the authoritative camp.
    internal sealed class SocialSettlementState
    {
        internal Camp Origin, View;
        internal double DeficitYears, WaterCoverage, DensityPopulationOffset;
        internal double ManagedAnimalFraction = 1;
        internal bool NewSite;
        internal double SiteFoodCapacity;
        internal SocialForecastConfidence Confidence;
    }

    internal sealed class SocialRelocationForecastCache
    {
        private sealed class Capacity
        {
            internal bool Cultivation, Husbandry;
            internal double ForageSupply, TerrestrialSupply, ManagedHumanFoodDisplaced;
            internal double ManagedAnimalFood, HarvestProductivity;
            internal int ReservedHerders;
            internal KnownMaterialSource[] Sources;
            internal CraftRecipe[] Recipes;
        }

        private static readonly FieldInfo[] InventoryFields = typeof(CampInventory).GetFields()
            .Where(f => f.FieldType == typeof(double)).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        private readonly WorldState world;
        private readonly Dictionary<int, Lazy<Capacity>> capacity = new Dictionary<int, Lazy<Capacity>>();
        private readonly Dictionary<int, Lazy<SocialServiceForecast>> baseline = new Dictionary<int, Lazy<SocialServiceForecast>>();
        private readonly Dictionary<(int Parent, int Tile, int Population, long FoodBits), Capacity> newSiteCapacity
            = new Dictionary<(int Parent, int Tile, int Population, long FoodBits), Capacity>();
        private readonly Dictionary<int, int> densityOffsets;
        private long forecasts, hits, builds;
        internal long Forecasts => Interlocked.Read(ref forecasts);
        internal long CacheHits => Interlocked.Read(ref hits);
        internal long Builds => Interlocked.Read(ref builds);

        internal SocialRelocationForecastCache(WorldState world, IEnumerable<Camp> camps)
        {
            this.world = world;
            densityOffsets = world.Hunters.GroupBy(h => h.SourceCamp)
                .ToDictionary(g => g.Key, g => g.Sum(h => Math.Max(0, h.Population)));
            // Capacity and baseline entries are installed on first use.  Most
            // settlements fail the O(1) fission pressure screen and have no
            // immigration candidate, so eagerly allocating two Lazy objects per
            // camp made the close pass scale with every settlement even when no
            // physical counterfactual was requested.
            _ = camps;
        }

        private Capacity Build(Camp camp)
        {
            Interlocked.Increment(ref builds);
            var physical = CampProductionDemand.BuildPhysical(world, camp, EconomicIntent.From(camp), false);
            return new Capacity
            {
                Cultivation = physical.CultivationEnabled, Husbandry = physical.HusbandryEnabled,
                ForageSupply = Math.Max(0, physical.ForageSupply),
                TerrestrialSupply = Math.Max(0, physical.PlantSupply),
                ManagedHumanFoodDisplaced = Math.Max(0, physical.ManagedFood.HumanFoodDisplaced),
                ManagedAnimalFood = Math.Max(0, physical.ManagedFood.AnimalFood),
                HarvestProductivity = Math.Max(0, physical.HarvestProductivity),
                ReservedHerders = Math.Max(0, physical.ReservedHerders), Sources = physical.Sources,
                Recipes = physical.Recipes
            };
        }

        private Capacity ExistingCapacity(Camp camp)
        {
            Lazy<Capacity> value;
            lock (capacity)
            {
                if (!capacity.TryGetValue(camp.Id, out value))
                {
                    Camp captured = camp;
                    capacity[camp.Id] = value = new Lazy<Capacity>(() => Build(captured),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                }
            }
            bool hit = value.IsValueCreated;
            Capacity result = value.Value;
            if (hit) Interlocked.Increment(ref hits);
            return result;
        }

        internal SocialServiceForecast Baseline(Camp camp)
        {
            Lazy<SocialServiceForecast> value;
            lock (baseline)
            {
                if (!baseline.TryGetValue(camp.Id, out value))
                {
                    Camp captured = camp;
                    baseline[camp.Id] = value = new Lazy<SocialServiceForecast>(() =>
                    {
                        var state = Existing(captured, captured.Population, captured.PlantFood, captured.AnimalFood,
                            captured.AquaticFood, captured.Workforce,
                            densityPopulationOffset: DensityOffset(captured));
                        return Forecast(state);
                    }, LazyThreadSafetyMode.ExecutionAndPublication);
                }
            }
            bool hit = value.IsValueCreated;
            SocialServiceForecast result = value.Value;
            if (hit) Interlocked.Increment(ref hits);
            return result;
        }

        internal static CampInventory ProportionalInventory(CampInventory inventory, double fraction)
        {
            var result = new CampInventory(); fraction = Numbers.Clamp(fraction, 0, 1);
            foreach (var field in InventoryFields) field.SetValue(result, Math.Max(0, (double)field.GetValue(inventory)) * fraction);
            return result;
        }

        internal static CampInventory RemainingInventory(CampInventory inventory, double fraction)
        {
            var result = new CampInventory(); fraction = Numbers.Clamp(fraction, 0, 1);
            foreach (var field in InventoryFields) field.SetValue(result, Math.Max(0, (double)field.GetValue(inventory)) * (1 - fraction));
            return result;
        }

        internal double DensityOffset(Camp camp)
            => densityOffsets.TryGetValue(camp.Id, out int population) ? population : 0;

        internal static SocialSettlementState Existing(Camp camp, int population, double plant, double animal,
            double aquatic, PopulationLedger workforce, CampInventory inventory = null, double? deficitYears = null,
            double densityPopulationOffset = 0, double managedAnimalFraction = 1)
        {
            var view = camp.WithPrivateInventory();
            view.Population = Math.Max(0, population); view.PlantFood = Math.Max(0, plant);
            view.AnimalFood = Math.Max(0, animal); view.AquaticFood = Math.Max(0, aquatic);
            view.Workforce = WorkforceTransfer.Copy(workforce);
            if (inventory != null) view.Inventory = inventory.Copy();
            view.Workforce.Normalize(view.Population);
            return new SocialSettlementState
            {
                Origin = camp, View = view, DeficitYears = Math.Max(0, deficitYears ?? camp.FoodDeficitYears),
                WaterCoverage = Numbers.Clamp(camp.WaterNeedsMetRatio, 0, 1),
                DensityPopulationOffset = Math.Max(0, densityPopulationOffset),
                ManagedAnimalFraction = Numbers.Clamp(managedAnimalFraction, 0, 1),
                Confidence = SocialForecastConfidence.LocalPhysicalCapacity | SocialForecastConfidence.WorkforceBound
                    | SocialForecastConfidence.FiniteStocks | SocialForecastConfidence.CommittedTrade
                    | SocialForecastConfidence.MaterialInputs | SocialForecastConfidence.WaterObserved
            };
        }

        internal SocialSettlementState Daughter(Camp parent, int population, int tile, CampInventory inventory,
            PopulationLedger workforce, double plant, double animal, double aquatic,
            double siteFoodCapacity)
        {
            // Negative transient ID guarantees that the daughter cannot borrow the
            // parent's fixed-housing index entry.
            var view = parent.WithPrivateInventory();
            view.Id = -1 - parent.Id; view.Tile = tile; view.Population = Math.Max(0, population);
            view.PlantFood = Math.Max(0, plant); view.AnimalFood = Math.Max(0, animal);
            view.AquaticFood = Math.Max(0, aquatic); view.Inventory = inventory.Copy();
            view.Inventory.HousingUnits = 0; view.Workforce = WorkforceTransfer.Copy(workforce);
            view.Workforce.Normalize(view.Population); view.EconomicLabor = null; view.IsVillage = false;
            return new SocialSettlementState
            {
                Origin = parent, View = view, DeficitYears = Math.Max(0, parent.FoodDeficitYears),
                WaterCoverage = 1, NewSite = true, SiteFoodCapacity = Math.Max(0, siteFoodCapacity),
                Confidence = SocialForecastConfidence.WorkforceBound | SocialForecastConfidence.FiniteStocks
                    | SocialForecastConfidence.MaterialInputs | SocialForecastConfidence.NewSiteEstimate
            };
        }

        private Capacity NewSiteCapacity(SocialSettlementState state)
        {
            var c = state.View;
            var key = (state.Origin.Id, c.Tile, c.Population,
                BitConverter.DoubleToInt64Bits(state.SiteFoodCapacity));
            lock (newSiteCapacity)
            {
                if (newSiteCapacity.TryGetValue(key, out var cached)) { Interlocked.Increment(ref hits); return cached; }
                Interlocked.Increment(ref builds);
                return newSiteCapacity[key] = new Capacity
                {
                    Cultivation = CultivationEconomy.Enabled(c), Husbandry = CultivationEconomy.HusbandryEnabled(c),
                    ForageSupply = state.SiteFoodCapacity,
                    TerrestrialSupply = state.SiteFoodCapacity, ManagedHumanFoodDisplaced = 0,
                    ManagedAnimalFood = 0,
                    HarvestProductivity = 1, ReservedHerders = CultivationEconomy.Workers(c, WorkforceRoles.Herder),
                    Sources = MaterialEconomy.KnownSources(world, c, world.Days), Recipes = NeolithicProduction.Recipes(world.Config)
                };
            }
        }

        private static Camp ReserveOpportunityWorkers(Camp source, EmploymentOpportunityMatch opportunity)
        {
            if (opportunity == null || opportunity.Matched <= 0 || opportunity.Reservations.Count == 0)
                return source;
            var view = source.WithPrivateInventory();
            view.Workforce = WorkforceTransfer.Copy(source.Workforce);
            int reserved = 0;
            foreach (var pair in opportunity.Reservations.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                int remaining = Math.Max(0, pair.Value);
                if (pair.Key == WorkforceRoles.Gatherer)
                {
                    int implicitGatherers = view.Workforce.Gatherers(view.Population);
                    int take = Math.Min(remaining, implicitGatherers);
                    reserved += take; remaining -= take;
                }
                foreach (var assignment in view.Workforce.Assignments
                    .Where(a => a.Role == pair.Key).OrderBy(a => a.Role, StringComparer.Ordinal))
                {
                    int take = Math.Min(remaining, assignment.Population);
                    assignment.Population -= take; remaining -= take; reserved += take;
                    if (remaining == 0) break;
                }
            }
            if (reserved > 0)
                view.Workforce.Assignments.Add(new PopulationAssignment
                    { Role = "__SocialOpportunity", Population = reserved });
            view.Workforce.Normalize(view.Population);
            return view;
        }

        private readonly struct MaterialBudget
        {
            internal readonly double Stone, Clay, Wood;
            internal MaterialBudget(double stone, double clay, double wood)
            { Stone = stone; Clay = clay; Wood = wood; }
        }

        // Pure finite raw-input envelope. A first bounded recipe pass supplies
        // demand only; this method then allocates opening stock, local finite
        // sources, material-specific specialists and the one shared generalist
        // extraction pool. The second recipe pass below is authoritative for the
        // social forecast and debits these budgets in production order.
        private MaterialBudget RealizableMaterials(Camp c, Capacity cap, CraftRecipe[] recipes,
            IReadOnlyDictionary<CraftGood, double> requested, double intervals)
        {
            double Stock(MaterialId material) => Math.Max(0, MaterialEconomy.Inventory(c, material));
            double Source(MaterialId material) => (cap.Sources ?? Array.Empty<KnownMaterialSource>())
                .Where(s => s.Material == material).GroupBy(s => s.Id, StringComparer.Ordinal)
                .Sum(g => g.Sum(s => Math.Max(0, s.Available)));
            double Priority(MaterialId material) => material == MaterialId.Stone ? c.StonePriority
                : material == MaterialId.Clay ? c.ClayPriority : c.WoodPriority;

            var demand = new Dictionary<MaterialId, double>
            {
                [MaterialId.Stone] = 0, [MaterialId.Clay] = 0, [MaterialId.Wood] = 0
            };
            double construction = 0;
            foreach (var recipe in recipes)
            {
                requested.TryGetValue(recipe.Good, out double output); output = Math.Max(0, output);
                demand[MaterialId.Stone] += output * recipe.Stone;
                demand[MaterialId.Clay] += output * recipe.Clay;
                demand[MaterialId.Wood] += output * recipe.Wood;
                construction += output * recipe.ConstructionMaterial;
            }
            double woodPotential = Stock(MaterialId.Wood) + Source(MaterialId.Wood);
            double clayPotential = Stock(MaterialId.Clay) + Source(MaterialId.Clay);
            double constructionPotential = woodPotential + clayPotential;
            double woodShare = constructionPotential > 0 ? woodPotential / constructionPotential : .5;
            demand[MaterialId.Wood] += construction * woodShare;
            demand[MaterialId.Clay] += construction * (1 - woodShare);

            var extracted = new Dictionary<MaterialId, double>();
            var sourceLeft = new Dictionary<MaterialId, double>();
            foreach (var material in MaterialEconomy.Materials)
            {
                double available = Source(material);
                string role = MaterialEconomy.Specialist(material);
                double labor = CultivationEconomy.Workers(c, role) * intervals;
                double rate = SpecialistProductivity.ExtractionRate(world, c, material, role, c.Population);
                double amount = Math.Min(available, Math.Max(0, labor * rate));
                extracted[material] = amount; sourceLeft[material] = available - amount;
            }
            double generalists = Math.Min(c.GathererPopulation,
                Math.Max(0, c.EconomicLabor?.MaterialGatherers ?? 0));
            double generalistLabor = generalists * intervals;
            foreach (var material in MaterialEconomy.Materials
                .OrderByDescending(m => Math.Max(0, demand[m] - Stock(m) - extracted[m]) * Priority(m))
                .ThenBy(m => (int)m))
            {
                if (!(generalistLabor > 0)) break;
                double rate = SpecialistProductivity.ExtractionRate(world, c, material,
                    WorkforceRoles.Gatherer, c.Population);
                if (!(rate > 0)) continue;
                double shortfall = Math.Max(0, demand[material] - Stock(material) - extracted[material]);
                double amount = Math.Min(sourceLeft[material], Math.Min(shortfall, generalistLabor * rate));
                extracted[material] += amount; generalistLabor -= amount / rate;
            }
            return new MaterialBudget(Stock(MaterialId.Stone) + extracted[MaterialId.Stone],
                Stock(MaterialId.Clay) + extracted[MaterialId.Clay],
                Stock(MaterialId.Wood) + extracted[MaterialId.Wood]);
        }

        internal SocialServiceForecast Forecast(SocialSettlementState state,
            EmploymentOpportunityMatch opportunity = null)
        {
            Interlocked.Increment(ref forecasts);
            opportunity = opportunity ?? EmploymentOpportunityMatch.Zero;
            var cfg = world.Config; var n = cfg.Neolithic;
            var c = ReserveOpportunityWorkers(state.View, opportunity);
            double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals;
            if (c.Population <= 0) return SocialServiceForecast.Empty(c.Id, years);
            var cap = state.NewSite ? NewSiteCapacity(state) : ExistingCapacity(state.Origin);

            double plantProduction, animalProduction, aquaticProduction;
            {
                int explicitGatherers = CultivationEconomy.Workers(c, WorkforceRoles.Gatherer);
                double gatherers = c.GathererPopulation + explicitGatherers;
                gatherers = Math.Max(0, gatherers - Math.Max(0, c.EconomicLabor?.MaterialGatherers ?? 0));
                double gatherCapacity = gatherers * cfg.GatherPerPerson * intervals;
                double herdDisplacement = cap.ManagedHumanFoodDisplaced * state.ManagedAnimalFraction;
                double forageAfterHerds = Math.Max(0, cap.ForageSupply - herdDisplacement);
                double terrestrial = Math.Max(0, cap.TerrestrialSupply - herdDisplacement);
                double aquatic = Math.Max(0, forageAfterHerds - terrestrial);
                if (cap.Cultivation)
                {
                    double farmCapacity = CultivationEconomy.Workers(c, WorkforceRoles.Farmer)
                        * cap.HarvestProductivity * cfg.GatherPerPerson * intervals;
                    double farm = Math.Min(terrestrial, farmCapacity);
                    double gatheredLand = Math.Min(Math.Max(0, terrestrial - farm), gatherCapacity);
                    plantProduction = farm + gatheredLand;
                    aquaticProduction = Math.Min(aquatic, Math.Max(0, gatherCapacity - gatheredLand));
                }
                else
                {
                    double access = forageAfterHerds * Math.Min(1,
                        Numbers.Fraction(cfg.GatherFraction, intervals) * gatherers / Math.Max(1e-12, cfg.PopulationScale));
                    double gathered = Math.Min(access, gatherCapacity);
                    double plantShare = forageAfterHerds > 0 ? terrestrial / forageAfterHerds : 1;
                    plantProduction = gathered * Numbers.Clamp(plantShare, 0, 1);
                    aquaticProduction = gathered - plantProduction;
                }
                int herders = CultivationEconomy.Workers(c, WorkforceRoles.Herder);
                double reservedHerders = cap.ReservedHerders * state.ManagedAnimalFraction;
                double herdFulfilment = reservedHerders > 0 ? Math.Min(1, herders / reservedHerders)
                    : cap.Husbandry ? (herders > 0 ? 1 : 0) : 1;
                animalProduction = cap.ManagedAnimalFood * state.ManagedAnimalFraction * herdFulfilment;
            }
            plantProduction = Math.Max(0, plantProduction) + opportunity.Food;
            double production = plantProduction + animalProduction + aquaticProduction;
            double foodAvailable = Math.Max(0, c.Food) + production;
            // A finite stock must cover the people who can exist throughout the
            // horizon, not merely the opening headcount. Birth-only person-years
            // are a bounded upper envelope because every physical death hazard is
            // nonnegative and full-nutrition fertility is the maximum birth rate.
            // This avoids an authoritative per-candidate demographic solve while
            // ensuring the service envelope can never spend unfunded food/textile.
            double fertility = PhysicalDemography.FertilityFactor(world, state.Origin);
            double servicePersonYears = PhysicalDemography.BirthOnlyPersonYears(c.Population,
                cfg.BaseBirthRate * fertility, years);
            double foodNeed = servicePersonYears * PhysicalDemography.FoodPerPersonYear(cfg);
            double nutrition = foodNeed > 0 ? Math.Min(1, foodAvailable / foodNeed) : 1;

            // Production gets only the typed food left after the cohort's physical
            // consumption requirement.  This is deliberately conservative and
            // prevents a cloth forecast from spending the same food as survival.
            double plantPool = Math.Max(0, c.PlantFood) + plantProduction;
            double animalPool = Math.Max(0, c.AnimalFood) + animalProduction;
            double aquaticPool = Math.Max(0, c.AquaticFood) + aquaticProduction;
            double afterMeals = Math.Max(0, plantPool + animalPool + aquaticPool - foodNeed);
            double plantBudget = Math.Min(plantPool, afterMeals);
            double animalBudget = Math.Min(animalPool, Math.Max(0, afterMeals - plantBudget));
            var recipes = cap.Recipes ?? NeolithicProduction.Recipes(cfg);
            var tools = new ToolCoverageBudget(world, c, intervals);
            var requested = MaterialEconomy.Forecast(world, c, recipes, intervals, years, tools.Remaining,
                false, c.Population, false, false, plantBudget, animalBudget, cap.Sources);
            var material = RealizableMaterials(c, cap, recipes, requested, intervals);
            var outputs = MaterialEconomy.Forecast(world, c, recipes, intervals, years, tools.Remaining,
                false, c.Population, false, false, plantBudget, animalBudget, cap.Sources, null,
                material.Stone, material.Clay, material.Wood);
            double Produced(CraftGood good) => outputs.TryGetValue(good, out double amount) ? Math.Max(0, amount) : 0;
            double cloth = Math.Max(0, c.Inventory.Cloth + c.TradeImport(CraftGood.Cloth)
                + Produced(CraftGood.Cloth) - c.TradeExport(CraftGood.Cloth));
            double animalFabric = Math.Max(0, c.Inventory.AnimalFabric + c.TradeImport(CraftGood.AnimalFabric)
                + Produced(CraftGood.AnimalFabric) - c.TradeExport(CraftGood.AnimalFabric)
                - Produced(CraftGood.LightHousing) * n.AnimalFabricPerLightHousing);
            double clothingAvailable = cloth + animalFabric + opportunity.Clothing;
            double clothingNeed = servicePersonYears * PhysicalDemography.ClothingPerPersonYear(cfg);
            double clothingCoverage = clothingNeed > 0 ? Math.Min(1, clothingAvailable / clothingNeed) : 1;

            NeolithicProduction.ShelterCapacity shelter;
            {
                double endDays = world.Days + cfg.TurnDays;
                double fixedPeople = NeolithicProduction.FixedHousingAt(world, c, endDays) * n.HousingPeoplePerUnit
                    + Produced(CraftGood.Housing) * n.HousingPeoplePerUnit;
                double retainedLight = Math.Max(0, c.Inventory.LightHousingUnits)
                    * Math.Pow(1 - n.LightHousingDecayPerReferenceTurn, Math.Max(0, years) / n.ReferenceTurnYears)
                    * n.LightHousingPeoplePerUnit;
                double lightPeople = retainedLight + Produced(CraftGood.LightHousing) * n.LightHousingPeoplePerUnit
                    + opportunity.ShelterPeople;
                shelter = new NeolithicProduction.ShelterCapacity(fixedPeople, lightPeople);
            }
            double housingCoverage = NeolithicProduction.HousingCoverage(shelter, c.Population);
            double housingShare = NeolithicProduction.HousingShare(n, shelter, c.Population);
            double expectation = c.RudimentaryExpectation > 0 ? c.RudimentaryExpectation
                : n.RudimentaryExpectationFloor + (1 - n.RudimentaryExpectationFloor)
                    * Numbers.Clamp(c.StationaryYears / n.RudimentaryExpectationRampYears, 0, 1);
            double protection = n.RudimentaryWeightClothing * (1 - expectation * (1 - clothingCoverage))
                + n.RudimentaryWeightHousing * (1 - expectation * (1 - housingCoverage));
            double qoL = nutrition * state.WaterCoverage * protection
                * NeolithicProduction.SocialCohesion(n, housingShare, c.Population);

            bool deficient = nutrition < 1 - 1e-12;
            double graceRemaining = deficient
                ? Math.Max(0, PhysicalDemography.StarvationGraceYears(cfg) - state.DeficitYears) : years;
            double firstYears = Math.Min(years, graceRemaining);
            var first = PhysicalDemography.Rates(cfg, c.Population, nutrition, clothingCoverage,
                shelter.Total, false, state.DensityPopulationOffset,
                PhysicalDemography.CrowdingMitigation(world, state.Origin),
                PhysicalDemography.DiseaseMitigation(world, state.Origin),
                fertility, PhysicalDemography.AdditionalMortality(world, state.Origin));
            var second = deficient && firstYears < years
                ? PhysicalDemography.Rates(cfg, c.Population, nutrition, clothingCoverage, shelter.Total, true,
                    state.DensityPopulationOffset,
                    PhysicalDemography.CrowdingMitigation(world, state.Origin),
                    PhysicalDemography.DiseaseMitigation(world, state.Origin),
                    fertility, PhysicalDemography.AdditionalMortality(world, state.Origin))
                : first;
            double population = c.Population, personYears = 0, exposureDeaths = 0, crowdingDeaths = 0, diseaseDeaths = 0, starvationDeaths = 0;
            void Segment(double duration, DemographicRates rates)
            {
                if (!(duration > 0) || !(population > 0)) return;
                double net = rates.Net;
                double py = Math.Abs(net) < 1e-12 ? population * duration
                    : population * (Math.Exp(net * duration) - 1) / net;
                py = Math.Max(0, py); personYears += py;
                exposureDeaths += py * rates.Exposure; crowdingDeaths += py * rates.Crowding;
                diseaseDeaths += py * rates.Disease; starvationDeaths += py * rates.LegacyStarvation;
                population *= Math.Exp(net * duration);
            }
            Segment(firstYears, first); Segment(years - firstYears, second);
            double integratedDeathHazard = first.Death * firstYears + second.Death * (years - firstYears);
            double mortalityRisk = 1 - Math.Exp(-Math.Max(0, integratedDeathHazard));
            double averageBirth = years > 0 ? (first.Birth * firstYears + second.Birth * (years - firstYears)) / years : first.Birth;
            double averageExposure = years > 0 ? (first.Exposure * firstYears + second.Exposure * (years - firstYears)) / years : first.Exposure;
            double averageCrowding = years > 0 ? (first.Crowding * firstYears + second.Crowding * (years - firstYears)) / years : first.Crowding;
            double averageDisease = years > 0 ? (first.Disease * firstYears + second.Disease * (years - firstYears)) / years : first.Disease;
            double averageStarvation = years > 0 ? (first.LegacyStarvation * firstYears + second.LegacyStarvation * (years - firstYears)) / years : first.LegacyStarvation;
            double mortality = averageExposure + averageCrowding + averageDisease + averageStarvation
                + (years > 0 ? (first.Additional * firstYears + second.Additional * (years - firstYears)) / years : first.Additional);
            double renewableFood = foodNeed > 0 ? Math.Min(1, production / foodNeed) : 1;
            double renewableClothing = clothingNeed > 0
                ? Math.Min(1, (Produced(CraftGood.Cloth) + Produced(CraftGood.AnimalFabric) + opportunity.Clothing) / clothingNeed) : 1;
            double stability = Math.Min(renewableFood, Math.Min(renewableClothing, housingCoverage));
            return new SocialServiceForecast(c.Id, c.Population, years, foodAvailable, foodNeed, nutrition,
                clothingAvailable, clothingNeed, clothingCoverage, shelter.Total, housingCoverage,
                state.WaterCoverage, Numbers.Clamp(qoL, 0, 1), mortalityRisk, personYears, population,
                averageBirth, mortality, averageExposure, averageCrowding, averageDisease, averageStarvation,
                exposureDeaths, crowdingDeaths, diseaseDeaths, starvationDeaths, renewableFood,
                renewableClothing, stability, state.Confidence);
        }
    }
}
