using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // One committed flow of people between two settlements, recorded canonically (destination camp, then source camp).
    [Serializable]
    public sealed class MigrationReport
    {
        public int Turn, SourceCamp, DestinationCamp, SourceSociety, DestinationSociety, SourceBefore, DestinationBefore, Intended, Migrants, Distance;
        public bool Kin;
        public double SourceQoL, DestinationQoL, Utility, PostInflowQoL, Plant, Animal, Aquatic;
        // WP10b-2: the return penalty inside Utility and the person-equivalents of food need the migrants carried (absent in older saves).
        [System.Runtime.Serialization.OptionalField] public double Penalty, Carried;
        // Legacy worlds retain ForwardUtility(QoL); Physical-v1 records the shared
        // forecast utility. These annual rates reveal destinations whose welfare is
        // higher while survival risk is worse without making net growth a veto.
        [System.Runtime.Serialization.OptionalField] public double SourceMortalityBefore, DestinationMortalityBefore, SourceMortalityAfter, DestinationMortalityAfter, MortalityGap;
        [System.Runtime.Serialization.OptionalField] public double SourceNetRateAfter, DestinationNetRateAfter;
        [System.Runtime.Serialization.OptionalField] public bool DestinationNetNonnegative;
        [System.Runtime.Serialization.OptionalField] public bool HigherQoLWorseMortality;
        // Unified social-relocation v0.2 diagnostics.  These are forecasts at the
        // decision boundary, not replacements for the next turn's experienced QoL
        // or authoritative demographic report.
        [System.Runtime.Serialization.OptionalField] public double MoverUtilityChange, SourceUtilityChange, IncumbentUtilityChange, AggregateWelfareChange;
        [System.Runtime.Serialization.OptionalField] public double ForecastSourceQoL, ForecastDestinationQoL;
        [System.Runtime.Serialization.OptionalField] public double ForecastSourceNoMoveQoL, ForecastSourceNoMoveMortalityRisk;
        [System.Runtime.Serialization.OptionalField] public double ForecastSourceNutrition, ForecastDestinationNutrition;
        [System.Runtime.Serialization.OptionalField] public double ForecastSourceClothing, ForecastDestinationClothing;
        [System.Runtime.Serialization.OptionalField] public double ForecastSourceHousing, ForecastDestinationHousing;
        [System.Runtime.Serialization.OptionalField] public double ForecastSourceMortalityRisk, ForecastDestinationMortalityRisk;
        [System.Runtime.Serialization.OptionalField] public double ForecastDestinationStability;
        [System.Runtime.Serialization.OptionalField] public double ForecastSourceRenewableFoodRatio, ForecastDestinationRenewableFoodRatio;
        [System.Runtime.Serialization.OptionalField] public double ForecastDestinationPlantFoodProduction, ForecastDestinationAnimalFoodProduction, ForecastDestinationAquaticFoodProduction;
        [System.Runtime.Serialization.OptionalField] public double ForecastSourceExposureRate, ForecastSourceCrowdingRate, ForecastSourceDiseaseRate, ForecastSourceStarvationRate;
        [System.Runtime.Serialization.OptionalField] public double ForecastDestinationExposureRate, ForecastDestinationCrowdingRate, ForecastDestinationDiseaseRate, ForecastDestinationStarvationRate;
        [System.Runtime.Serialization.OptionalField] public int ForecastConfidence, MatchedOpportunities;
    }

    // One remembered flow at a camp (hysteresis): the other camp, the turn and the direction (+1 this camp sent people, -1 it received them).
    [Serializable]
    public sealed class MigrationMemory
    {
        public int Other, Turn, Direction;
    }

    // Immigration between known settlements (QoLFissionTradeDesign 4 and 9). Runs once per turn in the Close phase after
    // demography and before fission. It is a resolver population process like births, not an AI decision.
    //
    // Authoritative state: Camp.Population, the typed food stores and the workforce ledger (people and portable food move
    // between camps), TurnRecord.Migrations, DemographicReport.Emigrated/Immigrated and the remembered contacts.
    // Temporal abstraction: one evaluation per turn boundary from the frozen post-demography snapshot. Physical-v1 worlds use the
    // shared SocialRelocationEvaluator and its cached sustainable-service envelopes. Candidate arrivals are reconciled in canonical
    // order against prior accepted inflows, but every transfer still commits at Close and first affects the real economy/demography
    // on the following turn. Legacy-demography saves retain ForwardUtility and QoLModel for save-stable behavior.
    // Cost: O(N) cached physical-capacity terms plus O(N * r_imm^2 * bounded group sizes) known-contact scans. Reconciliation indexes
    // active flows by endpoint and invalidates only incident dependencies; it never globally rescans flows after a local rejection and
    // never runs the economy, ecology or demographic integrator.
    //
    // Rejected architectures (design 9): all-pairs comparison, rerouting of rejected or excess migrants to second-best targets,
    // stable matching, flows, and recomputing QoL after each transfer.
    public static class Immigration
    {
        // Diagnostic only: when non-null, Resolve records one row per source camp (Tools/Headless/ClothingProbe.cs).
        // Never read by the simulation; null in ordinary play.
        internal sealed class Diag
        {
            public string Stage = "";
            public int Candidates, Evaluations, MoverRejected, AggregateRejected, SourceRejected, IncumbentRejected, MaxGroup, IntentSize, Accepted;
        }
        internal static Dictionary<int, Diag> Diagnostic;
        // Lifestyle barrier: nomadic camp -> village costs ImmigrationToSedentaryCost, village -> nomadic camp ImmigrationToNomadicCost.
        public static double LifestyleCost(SimulationConfig config, Camp source, Camp destination)
            => !source.IsVillage && destination.IsVillage ? config.ImmigrationToSedentaryCost : source.IsVillage && !destination.IsVillage ? config.ImmigrationToNomadicCost : 0;

        // dU = w_Q (Q_j - Q_i) - K_distance * d - K_attachment - K_lifestyle (the opportunity term is zero).
        public static double Utility(SimulationConfig config, double qualitySource, double qualityDestination, int distance, Camp source, Camp destination)
            => config.ImmigrationQualityWeight * (qualityDestination - qualitySource) - config.ImmigrationDistanceCost * distance - config.ImmigrationAttachmentCost - LifestyleCost(config, source, destination);

        // Forward-looking utility of moving m people from source to destination (WP10b-2). The source is evaluated at N_i - m and carries
        // no food term; the destination at N_j + inflow + m with `carried` person-equivalents of food need arriving with all of the
        // inflow so far (earlier sources reconciled at the same destination included).
        public static double ForwardUtility(SimulationConfig config, Fission.QoLModel sourceModel, Fission.QoLModel destinationModel, int m, int inflow, double carried, int distance, Camp source, Camp destination, double penalty)
            => Utility(config, sourceModel.ForwardAt(sourceModel.Residents - m), destinationModel.ForwardAt(destinationModel.Residents + inflow + m, carried), distance, source, destination) - penalty;

        // Person-equivalents of the turn's food need that one migrant brings: the source's per-head stored food over the per-head requirement.
        public static double CarriedPerPerson(SimulationConfig config, Camp source)
        {
            double need = config.Consumption * config.TurnDays / config.BaseDays;
            return source.Population > 0 && need > 0 ? Math.Max(0, source.PlantFood + source.AnimalFood + source.AquaticFood) / source.Population / need : 0;
        }

        // The largest group a source may send this turn: the maximum fraction of residents, keeping the source floor.
        public static int MaxGroup(SimulationConfig config, int residents)
            => Math.Min((int)Math.Floor(residents * config.ImmigrationMaxFraction + 1e-9), residents - config.ImmigrationSourceFloor);

        // Return penalty: a flow from `source` to `destination` within ImmigrationReturnCooldownTurns of the destination having sent people
        // to the source pays ImmigrationReturnPenalty * (cooldown - age + 1) / cooldown for age = turns since that flow (1..cooldown), else 0.
        public static double ReturnPenalty(WorldState w, Camp source, Camp destination)
        {
            int cooldown = w.Config.ImmigrationReturnCooldownTurns; var memory = source.RecentMigration;
            if (cooldown <= 0 || memory == null || w.Config.ImmigrationReturnPenalty <= 0) return 0;
            double best = 0;
            foreach (var m in memory)
            {
                if (m.Other != destination.Id || m.Direction >= 0) continue;
                int age = Math.Max(1, w.Turn - m.Turn);
                if (age <= cooldown) best = Math.Max(best, w.Config.ImmigrationReturnPenalty * (cooldown - age + 1) / cooldown);
            }
            return best;
        }

        // M = floor(N * maxFraction * clamp((dU - theta) / theta, 0, 1)), kept above the source floor; zero below the minimum group.
        public static int Size(SimulationConfig config, int residents, double utility)
        {
            double theta = config.ImmigrationDeadband;
            if (!(utility > theta)) return 0;
            int size = (int)Math.Floor(residents * config.ImmigrationMaxFraction * Numbers.Clamp((utility - theta) / theta, 0, 1) + 1e-9);   // epsilon: exact ramp points must not lose a person to rounding
            size = Math.Min(size, residents - config.ImmigrationSourceFloor);
            return size >= config.ImmigrationMinimumGroup ? size : 0;
        }

        public sealed class Intent
        {
            public Camp Source, Destination;
            public int Residents, DestinationResidents, Distance, Size; public double QualitySource, QualityDestination, Utility, Penalty, CarriedPerPerson;
            public double Plant, Animal, Aquatic;   // frozen source stores: migrants carry their per-capita share of these
            public double GrowthProgress, DeathProgress, FoodDeficitYears, DeficitSeverity;
            public double SourceMortalityBefore, DestinationMortalityBefore, SourceMortalityAfter, DestinationMortalityAfter;
            public double SourceNetRateAfter, DestinationNetRateAfter;
            public int Accepted; public double PostInflowQuality;
            public WorkforceTransfer Workforce;
            public SocialRelocationEvaluation Decision;
            public EmploymentOpportunityMatch Opportunity;
        }

        private sealed class Evaluation
        {
            public Camp Camp; public Intent Intent; public int Candidates;
            public int SocialEvaluations, MoverRejected, AggregateRejected, SourceRejected, IncumbentRejected;
        }

        private sealed class SourceProjection
        {
            public WorkforceTransfer Workforce;
            public SocialServiceForecast Forecast;
        }

        private sealed class Context
        {
            public readonly WorldState World;
            public readonly Dictionary<int, List<Camp>> CampsByTile = new Dictionary<int, List<Camp>>();
            public readonly Dictionary<int, double> Quality = new Dictionary<int, double>();
            public readonly Dictionary<int, Fission.QoLModel> Models = new Dictionary<int, Fission.QoLModel>();
            public readonly SocialRelocationForecastCache Forecasts;
            public readonly IEmploymentOpportunityProvider Opportunities;
            private readonly Dictionary<long, SourceProjection> sourceProjections = new Dictionary<long, SourceProjection>();
            private readonly Lazy<PlanningIndex> index;
            public PlanningIndex Index => index.Value;
            public Context(WorldState w, IEmploymentOpportunityProvider opportunities, SocialRelocationForecastCache forecasts)
            {
                World = w; Opportunities = opportunities ?? NeutralEmploymentOpportunityProvider.Instance;
                Forecasts = forecasts ?? (PhysicalDemography.Enabled(w.Config)
                    ? new SocialRelocationForecastCache(w, w.Camps) : null);
                index = new Lazy<PlanningIndex>(() => new PlanningIndex(w), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
            }
            public SocialServiceForecast Baseline(Camp camp) => Forecasts.Baseline(camp);
            public SourceProjection Source(Camp source, int movers)
            {
                long key = ((long)source.Id << 32) ^ (uint)movers;
                lock (sourceProjections)
                {
                    if (sourceProjections.TryGetValue(key, out var cached)) return cached;
                    int residents = source.Population;
                    var transfer = WorkforceTransfer.Split(source.Workforce, residents, movers);
                    double fraction = residents > 0 ? movers / (double)residents : 0;
                    var state = SocialRelocationForecastCache.Existing(source, residents - movers,
                        source.PlantFood * (1 - fraction), source.AnimalFood * (1 - fraction),
                        source.AquaticFood * (1 - fraction), transfer.Stayers,
                        densityPopulationOffset: Forecasts.DensityOffset(source));
                    return sourceProjections[key] = new SourceProjection
                    {
                        Workforce = transfer,
                        Forecast = Forecasts.Forecast(state)
                    };
                }
            }
        }

        // Deterministic bounded shortlist required by the generalized evaluator.
        // The existing physical minimum, source floor and configured cap remain
        // authoritative.  `preferred` is the legacy ramp's target and is included
        // exactly so the neutral Neolithic path retains its calibrated group scale.
        public static int[] CandidateSizes(SimulationConfig config, int residents, int preferred = 0)
        {
            int max = MaxGroup(config, residents), minimum = config.ImmigrationMinimumGroup;
            if (max < minimum) return Array.Empty<int>();
            var sizes = new HashSet<int> { minimum, max };
            foreach (double fraction in new[] { .025, .05, .075, .10 })
            {
                int value = (int)Math.Floor(residents * fraction + 1e-9);
                value = Math.Max(minimum, Math.Min(max, value)); sizes.Add(value);
            }
            if (preferred >= minimum) sizes.Add(Math.Min(max, preferred));
            return sizes.OrderBy(n => n).ToArray();
        }

        private static double PrivateCost(SimulationConfig config, int distance, Camp source, Camp destination, double penalty)
            => config.ImmigrationDistanceCost * distance + config.ImmigrationAttachmentCost
                + LifestyleCost(config, source, destination) + penalty;

        private static SocialRelocationEvaluation Decide(Context context, Camp source, Camp destination, int movers,
            int pendingPopulation, double pendingPlant, double pendingAnimal, double pendingAquatic,
            PopulationLedger pendingWorkforce, double pendingDeficitExposure,
            IReadOnlyDictionary<string, int> pendingReservations,
            EmploymentOpportunityMatch pendingOpportunity, out WorkforceTransfer transfer)
        {
            var cfg = context.World.Config; int residents = source.Population;
            var sourceProjection = context.Source(source, movers);
            transfer = sourceProjection.Workforce;
            double fraction = residents > 0 ? movers / (double)residents : 0;
            var sourceAfter = sourceProjection.Forecast;

            int destinationBeforePopulation = destination.Population + pendingPopulation;
            var destinationWorkers = WorkforceTransfer.Copy(destination.Workforce);
            if (pendingWorkforce != null)
                destinationWorkers = WorkforceTransfer.Merge(destinationWorkers, pendingWorkforce,
                    destinationBeforePopulation);
            double destinationBeforeDeficit = destinationBeforePopulation > 0
                ? (destination.FoodDeficitYears * destination.Population + pendingDeficitExposure)
                    / destinationBeforePopulation : 0;
            SocialServiceForecast destinationBefore;
            if (pendingPopulation == 0 && (pendingOpportunity == null || pendingOpportunity.Matched == 0))
                destinationBefore = context.Baseline(destination);
            else
            {
                var beforeState = SocialRelocationForecastCache.Existing(destination, destinationBeforePopulation,
                    destination.PlantFood + pendingPlant, destination.AnimalFood + pendingAnimal,
                    destination.AquaticFood + pendingAquatic, destinationWorkers, null, destinationBeforeDeficit,
                    context.Forecasts.DensityOffset(destination));
                destinationBefore = context.Forecasts.Forecast(beforeState, pendingOpportunity);
            }

            int destinationPopulation = destinationBeforePopulation + movers;
            var destinationAfterWorkers = WorkforceTransfer.Merge(destinationWorkers, transfer.Movers, destinationPopulation);
            double deficit = destinationPopulation > 0
                ? (destination.FoodDeficitYears * destination.Population + pendingDeficitExposure
                    + source.FoodDeficitYears * movers) / destinationPopulation : 0;
            var destinationState = SocialRelocationForecastCache.Existing(destination, destinationPopulation,
                destination.PlantFood + pendingPlant + source.PlantFood * fraction,
                destination.AnimalFood + pendingAnimal + source.AnimalFood * fraction,
                destination.AquaticFood + pendingAquatic + source.AquaticFood * fraction,
                destinationAfterWorkers, null, deficit, context.Forecasts.DensityOffset(destination));
            var request = new EmploymentOpportunityRequest(source.Id, destination.Id, movers,
                SocialRelocationDestination.ExistingSettlement, transfer.MoverComposition, pendingReservations);
            var opportunity = EmploymentOpportunityMatcher.Match(context.Opportunities, request);
            var destinationAfter = context.Forecasts.Forecast(destinationState,
                EmploymentOpportunityMatch.Combine(pendingOpportunity, opportunity));
            var policy = new SocialRelocationPolicy(cfg.ImmigrationDeadband, 0, cfg.FissionDaughterMargin,
                cfg.FissionDaughterMargin, 1, 0, PrivateCost(cfg, context.World.Map.Distance(source.Tile, destination.Tile),
                    source, destination, ReturnPenalty(context.World, source, destination)), 0, cfg.ImmigrationQualityWeight);
            return SocialRelocationEvaluator.Evaluate(SocialRelocationDestination.ExistingSettlement,
                context.Baseline(source), sourceAfter, destinationBefore, destinationAfter,
                policy, opportunity);
        }

        // One endpoint in the exact simultaneous Close state. Incoming cohorts
        // are aggregated once; an X0 forecast subtracts only the candidate cohort
        // in O(bounded workforce roles). A source's own frozen residents can emit
        // at most one flow, while arrivals never re-emigrate in the same pass.
        private sealed class JointCamp
        {
            public readonly Camp Camp;
            public Intent Outgoing;
            public readonly List<Intent> Incoming = new List<Intent>();
            public int IncomingPopulation;
            public double IncomingPlant, IncomingAnimal, IncomingAquatic, IncomingDeficitExposure;
            public PopulationLedger IncomingWorkforce = new PopulationLedger();
            public EmploymentOpportunityMatch IncomingOpportunity = EmploymentOpportunityMatch.Zero;
            public SocialServiceForecast FinalForecast;
            public readonly Dictionary<Intent, SocialServiceForecast> MarginalForecasts
                = new Dictionary<Intent, SocialServiceForecast>();

            public JointCamp(Camp camp) { Camp = camp; }

            public void RefreshAggregate(IReadOnlyDictionary<Intent, EmploymentOpportunityMatch> matches)
            {
                IncomingPopulation = 0; IncomingPlant = IncomingAnimal = IncomingAquatic = 0;
                IncomingDeficitExposure = 0; IncomingOpportunity = EmploymentOpportunityMatch.Zero;
                var roles = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var intent in Incoming)
                {
                    double fraction = intent.Accepted / (double)intent.Residents;
                    IncomingPopulation += intent.Accepted;
                    IncomingPlant += intent.Plant * fraction;
                    IncomingAnimal += intent.Animal * fraction;
                    IncomingAquatic += intent.Aquatic * fraction;
                    IncomingDeficitExposure += intent.Source.FoodDeficitYears * intent.Accepted;
                    foreach (var assignment in intent.Workforce.Movers.Assignments)
                        roles[assignment.Role] = (roles.TryGetValue(assignment.Role, out int prior) ? prior : 0)
                            + assignment.Population;
                    if (matches.TryGetValue(intent, out var match))
                        IncomingOpportunity = EmploymentOpportunityMatch.Combine(IncomingOpportunity, match);
                }
                IncomingWorkforce = new PopulationLedger
                {
                    Assignments = roles.OrderBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => new PopulationAssignment { Role = p.Key, Population = p.Value }).ToList()
                };
                Invalidate();
            }

            public void Invalidate()
            {
                FinalForecast = null; MarginalForecasts.Clear();
            }
        }

        // Sparse dependency index for final joint-state revalidation. Removing a
        // flow touches exactly its two endpoint buckets. Only flows incident to
        // those buckets are then dirty; unrelated components retain their cached
        // decisions and forecasts.
        private sealed class JointIndex
        {
            private readonly Context context;
            private readonly ResolutionMetrics metrics;
            private readonly Dictionary<int, JointCamp> camps = new Dictionary<int, JointCamp>();
            private readonly Dictionary<Intent, EmploymentOpportunityMatch> matches
                = new Dictionary<Intent, EmploymentOpportunityMatch>();
            public readonly HashSet<Intent> Active;

            public JointIndex(Context context, IReadOnlyList<Intent> active, ResolutionMetrics metrics)
            {
                this.context = context; this.metrics = metrics;
                Active = new HashSet<Intent>(active);
                JointCamp At(Camp camp)
                {
                    if (!camps.TryGetValue(camp.Id, out var value))
                        camps[camp.Id] = value = new JointCamp(camp);
                    return value;
                }
                foreach (var intent in active)
                {
                    var source = At(intent.Source);
                    if (source.Outgoing != null)
                        throw new InvalidOperationException("A camp cannot emit two simultaneous immigration flows.");
                    source.Outgoing = intent;
                    At(intent.Destination).Incoming.Add(intent);
                }
                foreach (var bucket in camps.Values)
                    bucket.Incoming.Sort((a, b) => a.Source.Id.CompareTo(b.Source.Id));
                foreach (var destination in camps.Values.Where(c => c.Incoming.Count > 0)
                    .OrderBy(c => c.Camp.Id))
                    Rematch(destination);
                foreach (var bucket in camps.Values.Where(c => c.Incoming.Count == 0))
                    bucket.RefreshAggregate(matches);
            }

            private static PopulationLedger Without(PopulationLedger total, PopulationLedger removed)
            {
                var counts = total.Assignments.ToDictionary(a => a.Role, a => a.Population,
                    StringComparer.Ordinal);
                foreach (var assignment in removed.Assignments)
                {
                    if (!counts.TryGetValue(assignment.Role, out int prior)) continue;
                    int remaining = prior - assignment.Population;
                    if (remaining > 0) counts[assignment.Role] = remaining; else counts.Remove(assignment.Role);
                }
                return new PopulationLedger
                {
                    Assignments = counts.OrderBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => new PopulationAssignment { Role = p.Key, Population = p.Value }).ToList()
                };
            }

            private void Rematch(JointCamp destination)
            {
                var pending = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var intent in destination.Incoming)
                {
                    var request = new EmploymentOpportunityRequest(intent.Source.Id, intent.Destination.Id,
                        intent.Accepted, SocialRelocationDestination.ExistingSettlement,
                        intent.Workforce.MoverComposition, pending);
                    var match = EmploymentOpportunityMatcher.Match(context.Opportunities, request);
                    matches[intent] = match; metrics.SocialJointOpportunityMatches++;
                    foreach (var pair in match.Reservations)
                        pending[pair.Key] = (pending.TryGetValue(pair.Key, out int prior) ? prior : 0) + pair.Value;
                }
                destination.RefreshAggregate(matches);
            }

            public EmploymentOpportunityMatch Match(Intent intent)
                => matches.TryGetValue(intent, out var match) ? match : EmploymentOpportunityMatch.Zero;

            public SocialServiceForecast Forecast(Camp camp, Intent excluded)
            {
                var bucket = camps[camp.Id];
                if (excluded == null && bucket.FinalForecast != null) return bucket.FinalForecast;
                if (excluded != null && bucket.MarginalForecasts.TryGetValue(excluded, out var cached))
                    return cached;

                var outgoing = ReferenceEquals(bucket.Outgoing, excluded) ? null : bucket.Outgoing;
                int population = camp.Population;
                double plant = camp.PlantFood, animal = camp.AnimalFood, aquatic = camp.AquaticFood;
                PopulationLedger workforce = WorkforceTransfer.Copy(camp.Workforce);
                if (outgoing != null)
                {
                    double fraction = outgoing.Accepted / (double)outgoing.Residents;
                    population -= outgoing.Accepted;
                    plant -= outgoing.Plant * fraction; animal -= outgoing.Animal * fraction;
                    aquatic -= outgoing.Aquatic * fraction;
                    workforce = outgoing.Workforce.Stayers;
                }
                double deficitExposure = camp.FoodDeficitYears * population;

                // Forecast is only requested for the candidate's own source and
                // destination. Distinct endpoints make this an O(1) membership
                // test; the active index guarantees that the destination bucket
                // contains the candidate.
                bool excludesIncoming = excluded != null && excluded.Destination.Id == camp.Id;
                int incomingPopulation = bucket.IncomingPopulation;
                double incomingPlant = bucket.IncomingPlant, incomingAnimal = bucket.IncomingAnimal;
                double incomingAquatic = bucket.IncomingAquatic;
                double incomingDeficit = bucket.IncomingDeficitExposure;
                var incomingWorkforce = bucket.IncomingWorkforce;
                var opportunity = bucket.IncomingOpportunity;
                if (excludesIncoming)
                {
                    double fraction = excluded.Accepted / (double)excluded.Residents;
                    incomingPopulation -= excluded.Accepted;
                    incomingPlant -= excluded.Plant * fraction;
                    incomingAnimal -= excluded.Animal * fraction;
                    incomingAquatic -= excluded.Aquatic * fraction;
                    incomingDeficit -= excluded.Source.FoodDeficitYears * excluded.Accepted;
                    incomingWorkforce = Without(incomingWorkforce, excluded.Workforce.Movers);
                    opportunity = EmploymentOpportunityMatch.Subtract(opportunity, Match(excluded));
                }
                population += incomingPopulation;
                plant += incomingPlant; animal += incomingAnimal; aquatic += incomingAquatic;
                if (incomingPopulation > 0)
                    workforce = WorkforceTransfer.Merge(workforce, incomingWorkforce, population);
                deficitExposure += incomingDeficit;
                double deficit = population > 0 ? deficitExposure / population : 0;
                var state = SocialRelocationForecastCache.Existing(camp, population, plant, animal, aquatic,
                    workforce, null, deficit, context.Forecasts.DensityOffset(camp));
                var forecast = context.Forecasts.Forecast(state, opportunity);
                metrics.SocialJointEndpointBuilds++;
                if (excluded == null) bucket.FinalForecast = forecast;
                else bucket.MarginalForecasts[excluded] = forecast;
                return forecast;
            }

            public SocialRelocationEvaluation Decide(Intent intent)
            {
                var sourceNoMove = Forecast(intent.Source, intent);
                var sourceAfter = Forecast(intent.Source, null);
                var destinationNoMove = Forecast(intent.Destination, intent);
                var destinationAfter = Forecast(intent.Destination, null);
                var cfg = context.World.Config;
                var policy = new SocialRelocationPolicy(cfg.ImmigrationDeadband, 0,
                    cfg.FissionDaughterMargin, cfg.FissionDaughterMargin, 1, 0,
                    PrivateCost(cfg, intent.Distance, intent.Source, intent.Destination, intent.Penalty),
                    0, cfg.ImmigrationQualityWeight);
                return SocialRelocationEvaluator.Evaluate(SocialRelocationDestination.ExistingSettlement,
                    sourceNoMove, sourceAfter, destinationNoMove, destinationAfter, policy, Match(intent));
            }

            public Intent[] Remove(IReadOnlyList<Intent> removed)
            {
                var affected = new HashSet<int>(); var rematch = new HashSet<int>();
                foreach (var intent in removed)
                {
                    if (!Active.Remove(intent)) continue;
                    var source = camps[intent.Source.Id]; var destination = camps[intent.Destination.Id];
                    if (ReferenceEquals(source.Outgoing, intent)) source.Outgoing = null;
                    destination.Incoming.Remove(intent); matches.Remove(intent);
                    affected.Add(source.Camp.Id); affected.Add(destination.Camp.Id);
                    rematch.Add(destination.Camp.Id);
                }
                foreach (int id in rematch.OrderBy(i => i)) Rematch(camps[id]);
                foreach (int id in affected)
                    if (!rematch.Contains(id)) camps[id].Invalidate();

                var dirty = new HashSet<Intent>();
                foreach (int id in affected.OrderBy(i => i))
                {
                    var bucket = camps[id];
                    if (bucket.Outgoing != null && Active.Contains(bucket.Outgoing))
                    { metrics.SocialJointDependencyVisits++; dirty.Add(bucket.Outgoing); }
                    foreach (var intent in bucket.Incoming)
                    { metrics.SocialJointDependencyVisits++; dirty.Add(intent); }
                }
                return dirty.OrderBy(i => i.Destination.Id).ThenBy(i => i.Source.Id).ToArray();
            }
        }

        private static void ApplyPhysicalDecision(Intent intent, SocialRelocationEvaluation decision)
        {
            intent.Decision = decision; intent.Opportunity = decision.Opportunity;
            intent.PostInflowQuality = decision.DestinationAfter.QoL;
            intent.Utility = decision.MoverUtilityChange;
            intent.SourceMortalityBefore = decision.SourceNoMove.MortalityRate;
            intent.DestinationMortalityBefore = decision.DestinationNoMove.MortalityRate;
            intent.SourceMortalityAfter = decision.SourceAfter.MortalityRate;
            intent.DestinationMortalityAfter = decision.DestinationAfter.MortalityRate;
            intent.SourceNetRateAfter = decision.SourceAfter.BirthRate - decision.SourceAfter.MortalityRate;
            intent.DestinationNetRateAfter = decision.DestinationAfter.BirthRate - decision.DestinationAfter.MortalityRate;
        }

        // Initial destination-ordered reconciliation is followed by a marginal
        // check against the state that would actually be committed simultaneously.
        // Failures in a batch are removed together. Only flows incident to their
        // endpoints are rescored; unaffected components retain exact cached
        // decisions. Each batch removes at least one flow, so this terminates
        // deterministically without an economy/ODE replay or global retry scan.
        private static void RevalidateJointPhysical(Context context, IReadOnlyList<Intent> intents,
            ResolutionMetrics metrics)
        {
            var active = intents.Where(i => i.Accepted > 0)
                .OrderBy(i => i.Destination.Id).ThenBy(i => i.Source.Id).ToArray();
            if (active.Length == 0) return;
            var index = new JointIndex(context, active, metrics);
            var decisions = new Dictionary<Intent, SocialRelocationEvaluation>();
            var dirty = active;
            while (dirty.Length > 0)
            {
                metrics.SocialJointBatches++;
                var failed = new List<Intent>();
                foreach (var intent in dirty)
                {
                    if (!index.Active.Contains(intent)) continue;
                    var decision = index.Decide(intent);
                    decisions[intent] = decision; metrics.SocialCandidatesEvaluated++;
                    metrics.SocialJointFlowEvaluations++;
                    if (!decision.MoverPass) metrics.SocialMoverRejected++;
                    if (!decision.AggregatePass) metrics.SocialAggregateRejected++;
                    if (!decision.SourcePass) metrics.SocialSourceRejected++;
                    if (!decision.IncumbentPass) metrics.SocialIncumbentRejected++;
                    if (!decision.Accepted) failed.Add(intent);
                }
                if (failed.Count == 0) break;
                foreach (var intent in failed)
                {
                    intent.Accepted = 0; intent.Decision = decisions[intent];
                    intent.Opportunity = EmploymentOpportunityMatch.Zero;
                }
                dirty = index.Remove(failed);
            }
            foreach (var intent in active.Where(index.Active.Contains))
            {
                ApplyPhysicalDecision(intent, decisions[intent]);
                intent.Opportunity = index.Match(intent);
            }
        }

        // Returns the camps that took part (sources with an intent and destinations that received arrivals), which sit out fission this
        // turn; null when immigration is disabled.
        public static HashSet<int> Resolve(WorldState w, TurnRecord record, PhaseExecution execution, ResolutionMetrics metrics,
            IEmploymentOpportunityProvider opportunities = null)
            => Resolve(w, record, execution, metrics, opportunities, null);

        internal static HashSet<int> Resolve(WorldState w, TurnRecord record, PhaseExecution execution, ResolutionMetrics metrics,
            IEmploymentOpportunityProvider opportunities, SocialRelocationForecastCache forecasts)
        {
            if (!w.Config.ImmigrationEnabled) return null;
            var timer = System.Diagnostics.Stopwatch.StartNew(); var cfg = w.Config;
            execution = execution ?? new PhaseExecution();
            // Frozen post-demography snapshot: nothing below the parallel maps writes to the world.
            var camps = w.Camps.Where(c => c.Population > 0).OrderBy(c => c.Id).ToArray();
            var context = new Context(w, opportunities, forecasts);
            foreach (var c in camps) { if (!context.CampsByTile.TryGetValue(c.Tile, out var list)) context.CampsByTile[c.Tile] = list = new List<Camp>(); list.Add(c); }
            var modelsByCamp = execution.Map(camps, c => Fission.QoLModelOf(w, c));
            for (int i = 0; i < camps.Length; i++) { context.Models[camps[i].Id] = modelsByCamp[i]; context.Quality[camps[i].Id] = modelsByCamp[i].At(camps[i].Population); }
            bool legacyDecision = !PhysicalDemography.Enabled(cfg);
            // A Physical-v1 daughter can receive people immediately, but cannot
            // initiate an outflow before the same saved maturity boundary that
            // governs recursive fission. Existing camps and legacy saves carry
            // zero (and the exact legacy decision path intentionally bypasses
            // the gate), so this adds no independent threshold or save field.
            var sources = legacyDecision ? camps : camps.Where(c => w.Turn >= c.FissionEligibleTurn).ToArray();
            if (!legacyDecision)
                metrics.ImmigrationOutflowCooldownBlocked += camps.LongCount(c => w.Turn < c.FissionEligibleTurn);
            var evaluations = execution.Map(sources, c => legacyDecision ? EvaluateLegacy(context, c) : Evaluate(context, c));
            var intents = new List<Intent>();
            if (Diagnostic != null)
                foreach (var c in camps.Except(sources)) Diagnostic[c.Id] = new Diag { Stage = "cooldown" };
            foreach (var e in evaluations)
            {
                if (Diagnostic != null)
                    Diagnostic[e.Camp.Id] = new Diag
                    {
                        Stage = e.Camp.Population < cfg.ImmigrationSourceFloor + cfg.ImmigrationMinimumGroup ? "below-source-floor"
                            : e.Candidates == 0 ? "no-known-destination" : e.Intent == null ? "no-accepted-destination" : "intent",
                        Candidates = e.Candidates, Evaluations = e.SocialEvaluations, MoverRejected = e.MoverRejected,
                        AggregateRejected = e.AggregateRejected, SourceRejected = e.SourceRejected, IncumbentRejected = e.IncumbentRejected,
                        MaxGroup = MaxGroup(cfg, e.Camp.Population), IntentSize = e.Intent?.Size ?? 0
                    };
                if (e.Camp.Population >= cfg.ImmigrationSourceFloor + cfg.ImmigrationMinimumGroup) metrics.ImmigrationCampsEvaluated++;
                metrics.ImmigrationCandidatesScored += e.Candidates;
                metrics.SocialCandidatesEvaluated += e.SocialEvaluations;
                metrics.SocialMoverRejected += e.MoverRejected; metrics.SocialAggregateRejected += e.AggregateRejected;
                metrics.SocialSourceRejected += e.SourceRejected; metrics.SocialIncumbentRejected += e.IncumbentRejected;
                if (e.Intent != null) { intents.Add(e.Intent); metrics.ImmigrationIntents++; }
            }
            var taken = new HashSet<int>(intents.Select(i => i.Source.Id));
            // Canonical reconcile: destination camp ID, then source camp ID.
            // Accepted earlier inflows are included in the receiver counterfactual,
            // including their real food and workforce, and every bounded size is
            // rescored.  This is not a demographic/economic update: all movements
            // still commit simultaneously at Close and affect production next turn.
            var inflow = new Dictionary<int, int>();
            var plantIn = new Dictionary<int, double>(); var animalIn = new Dictionary<int, double>();
            var aquaticIn = new Dictionary<int, double>(); var workforceIn = new Dictionary<int, PopulationLedger>();
            var legacyCarriedIn = new Dictionary<int, double>();
            var deficitExposureIn = new Dictionary<int, double>();
            var opportunityReservations = new Dictionary<int, Dictionary<string, int>>();
            var opportunityIn = new Dictionary<int, EmploymentOpportunityMatch>();
            var ordered = intents.OrderBy(i => i.Destination.Id).ThenBy(i => i.Source.Id).ToArray();
            foreach (var intent in ordered)
            {
                var destination = intent.Destination;
                inflow.TryGetValue(destination.Id, out int already);
                plantIn.TryGetValue(destination.Id, out double pendingPlant);
                animalIn.TryGetValue(destination.Id, out double pendingAnimal);
                aquaticIn.TryGetValue(destination.Id, out double pendingAquatic);
                workforceIn.TryGetValue(destination.Id, out var pendingWorkers);
                deficitExposureIn.TryGetValue(destination.Id, out double deficitExposureAlready);
                if (!opportunityReservations.TryGetValue(destination.Id, out var reservations))
                    opportunityReservations[destination.Id] = reservations = new Dictionary<string, int>(StringComparer.Ordinal);
                opportunityIn.TryGetValue(destination.Id, out var pendingOpportunity);
                int acceptedSize = 0; SocialRelocationEvaluation acceptedDecision = null; WorkforceTransfer acceptedTransfer = null;
                if (legacyDecision)
                {
                    legacyCarriedIn.TryGetValue(destination.Id, out double carriedAlready);
                    var destinationModel = context.Models[destination.Id]; var sourceModel = context.Models[intent.Source.Id];
                    bool Accept(int m) => ForwardUtility(cfg, sourceModel, destinationModel, m, already,
                        carriedAlready + m * intent.CarriedPerPerson, intent.Distance, intent.Source,
                        destination, intent.Penalty) > cfg.ImmigrationDeadband;
                    int lo = 0, hi = intent.Size;
                    while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (Accept(mid)) lo = mid; else hi = mid - 1; }
                    acceptedSize = lo;
                    if (lo > 0)
                    {
                        intent.Utility = ForwardUtility(cfg, sourceModel, destinationModel, lo, already,
                            carriedAlready + lo * intent.CarriedPerPerson, intent.Distance, intent.Source,
                            destination, intent.Penalty);
                        intent.PostInflowQuality = destinationModel.ForwardAt(destination.Population + already + lo,
                            carriedAlready + lo * intent.CarriedPerPerson);
                        legacyCarriedIn[destination.Id] = carriedAlready + lo * intent.CarriedPerPerson;
                    }
                }
                else
                    foreach (int size in CandidateSizes(cfg, intent.Residents, intent.Size).Where(n => n <= intent.Size).OrderByDescending(n => n))
                    {
                        var decision = Decide(context, intent.Source, destination, size, already, pendingPlant,
                            pendingAnimal, pendingAquatic, pendingWorkers, deficitExposureAlready, reservations,
                            pendingOpportunity, out var transfer);
                        metrics.SocialCandidatesEvaluated++;
                        if (!decision.MoverPass) metrics.SocialMoverRejected++;
                        if (!decision.AggregatePass) metrics.SocialAggregateRejected++;
                        if (!decision.SourcePass) metrics.SocialSourceRejected++;
                        if (!decision.IncumbentPass) metrics.SocialIncumbentRejected++;
                        if (!decision.Accepted) continue;
                        acceptedSize = size; acceptedDecision = decision; acceptedTransfer = transfer; break;
                    }
                intent.Accepted = acceptedSize; intent.Decision = acceptedDecision; intent.Workforce = acceptedTransfer;
                if (!legacyDecision && acceptedDecision != null)
                    ApplyPhysicalDecision(intent, acceptedDecision);
                if (acceptedSize > 0)
                {
                    double fraction = acceptedSize / (double)intent.Residents;
                    inflow[destination.Id] = already + acceptedSize;
                    if (!legacyDecision)
                    {
                        plantIn[destination.Id] = pendingPlant + intent.Plant * fraction;
                        animalIn[destination.Id] = pendingAnimal + intent.Animal * fraction;
                        aquaticIn[destination.Id] = pendingAquatic + intent.Aquatic * fraction;
                        workforceIn[destination.Id] = WorkforceTransfer.Merge(pendingWorkers,
                            acceptedTransfer.Movers, already + acceptedSize);
                        deficitExposureIn[destination.Id] = deficitExposureAlready
                            + intent.Source.FoodDeficitYears * acceptedSize;
                        foreach (var pair in acceptedDecision.Opportunity.Reservations)
                            reservations[pair.Key] = (reservations.TryGetValue(pair.Key, out int prior) ? prior : 0) + pair.Value;
                        opportunityIn[destination.Id] = EmploymentOpportunityMatch.Combine(pendingOpportunity,
                            acceptedDecision.Opportunity);
                    }
                }
            }
            if (!legacyDecision) RevalidateJointPhysical(context, ordered, metrics);
            foreach (var intent in ordered)
            {
                if (Diagnostic != null && Diagnostic.TryGetValue(intent.Source.Id, out var diag))
                { diag.Accepted = intent.Accepted; diag.Stage = intent.Accepted > 0 ? "moved" : "rejected-at-reconcile"; }
                if (intent.Accepted == 0) metrics.ImmigrationRejected++;
                else
                {
                    metrics.ImmigrationAccepted++;
                    if (intent.Accepted < intent.Size) metrics.ImmigrationPartial++;
                    if (!legacyDecision)
                    {
                        if (intent.DestinationNetRateAfter < 0)
                            metrics.ImmigrationPhysicalDestinationNegativeNet++;
                        metrics.SocialMatchedOpportunities += intent.Opportunity?.Matched ?? 0;
                    }
                }
            }
            var participants = new HashSet<int>(taken);
            var accepted = ordered.Where(i => i.Accepted > 0).ToArray();
            Prune(w);
            if (accepted.Length > 0)
            {
                var demographics = new Dictionary<int, DemographicReport>();
                foreach (var d in record.Demographics) demographics[d.Camp] = d;
                // Commit simultaneously: every departure from the frozen residents and stores first, then every arrival.
                foreach (var intent in accepted.OrderBy(i => i.Source.Id))
                {
                    var source = intent.Source; int m = intent.Accepted; double f = m / (double)intent.Residents;
                    if (legacyDecision) source.Workforce.ApplyCasualties(intent.Residents, m);
                    else source.Workforce = intent.Workforce.Stayers;
                    source.Population -= m;
                    source.PlantFood -= intent.Plant * f; source.AnimalFood -= intent.Animal * f; source.AquaticFood -= intent.Aquatic * f;
                    if (PhysicalDemography.Enabled(w.Config))
                    {
                        intent.GrowthProgress = source.GrowthRemainder * f; intent.DeathProgress = source.DeathRemainder * f;
                        source.GrowthRemainder -= intent.GrowthProgress; source.DeathRemainder -= intent.DeathProgress;
                        intent.FoodDeficitYears = source.FoodDeficitYears; intent.DeficitSeverity = source.DeficitSeverity;
                    }
                    if (demographics.TryGetValue(source.Id, out var report)) report.Emigrated += m;
                }
                foreach (var intent in accepted)
                {
                    var source = intent.Source; var destination = intent.Destination; int m = intent.Accepted; double f = m / (double)intent.Residents;
                    int destinationBefore = destination.Population;
                    if (PhysicalDemography.Enabled(w.Config) && destinationBefore + m > 0)
                    {
                        destination.FoodDeficitYears = (destination.FoodDeficitYears * destinationBefore + intent.FoodDeficitYears * m) / (destinationBefore + m);
                        destination.DeficitSeverity = (destination.DeficitSeverity * destinationBefore + intent.DeficitSeverity * m) / (destinationBefore + m);
                    }
                    destination.Population += m;
                    if (!legacyDecision)
                        destination.Workforce = WorkforceTransfer.Merge(destination.Workforce,
                            intent.Workforce.Movers, destination.Population);
                    if (PhysicalDemography.Enabled(w.Config))
                    {
                        destination.GrowthRemainder += intent.GrowthProgress; destination.DeathRemainder += intent.DeathProgress;
                    }
                    destination.PlantFood += intent.Plant * f; destination.AnimalFood += intent.Animal * f; destination.AquaticFood += intent.Aquatic * f;
                    if (demographics.TryGetValue(destination.Id, out var report)) report.Immigrated += m;
                    ContactKnowledge.RecordMigration(w, source.People, destination);
                    ContactKnowledge.RecordMigration(w, destination.People, source);
                    participants.Add(destination.Id); metrics.ImmigrationMigrants += m;
                    var migration = new MigrationReport
                    {
                        Turn = w.Turn, SourceCamp = source.Id, DestinationCamp = destination.Id, SourceSociety = source.SocietyId, DestinationSociety = destination.SocietyId,
                        SourceBefore = intent.Residents, DestinationBefore = intent.DestinationResidents, Intended = intent.Size, Migrants = m, Distance = intent.Distance, Kin = Kinship.Affinity(source.People, destination.People) > 0,
                        SourceQoL = intent.QualitySource, DestinationQoL = intent.QualityDestination, Utility = intent.Utility, PostInflowQoL = intent.PostInflowQuality,
                        Plant = intent.Plant * f, Animal = intent.Animal * f, Aquatic = intent.Aquatic * f, Penalty = intent.Penalty, Carried = m * intent.CarriedPerPerson,
                        SourceMortalityBefore = intent.SourceMortalityBefore, DestinationMortalityBefore = intent.DestinationMortalityBefore,
                        SourceMortalityAfter = intent.SourceMortalityAfter, DestinationMortalityAfter = intent.DestinationMortalityAfter,
                        SourceNetRateAfter = intent.SourceNetRateAfter, DestinationNetRateAfter = intent.DestinationNetRateAfter,
                        DestinationNetNonnegative = legacyDecision || intent.DestinationNetRateAfter >= 0,
                        // Use the mover's stay-versus-move counterfactual on both
                        // axes: destination X1 versus source X0.
                        MortalityGap = intent.DestinationMortalityAfter - intent.SourceMortalityBefore,
                        HigherQoLWorseMortality = intent.PostInflowQuality
                            > (legacyDecision ? intent.QualitySource : intent.Decision.SourceNoMove.QoL)
                            && intent.DestinationMortalityAfter > intent.SourceMortalityBefore + 1e-12
                    };
                    if (intent.Decision != null)
                    {
                        migration.MoverUtilityChange = intent.Decision.MoverUtilityChange;
                        migration.SourceUtilityChange = intent.Decision.SourceUtilityChange;
                        migration.IncumbentUtilityChange = intent.Decision.IncumbentUtilityChange;
                        migration.AggregateWelfareChange = intent.Decision.AggregateWelfareChange;
                        migration.ForecastSourceQoL = intent.Decision.SourceAfter.QoL;
                        migration.ForecastDestinationQoL = intent.Decision.DestinationAfter.QoL;
                        migration.ForecastSourceNoMoveQoL = intent.Decision.SourceNoMove.QoL;
                        migration.ForecastSourceNoMoveMortalityRisk = intent.Decision.SourceNoMove.MortalityRisk;
                        migration.ForecastSourceNutrition = intent.Decision.SourceAfter.Nutrition;
                        migration.ForecastDestinationNutrition = intent.Decision.DestinationAfter.Nutrition;
                        migration.ForecastSourceClothing = intent.Decision.SourceAfter.ClothingCoverage;
                        migration.ForecastDestinationClothing = intent.Decision.DestinationAfter.ClothingCoverage;
                        migration.ForecastSourceHousing = intent.Decision.SourceAfter.HousingCoverage;
                        migration.ForecastDestinationHousing = intent.Decision.DestinationAfter.HousingCoverage;
                        migration.ForecastSourceMortalityRisk = intent.Decision.SourceAfter.MortalityRisk;
                        migration.ForecastDestinationMortalityRisk = intent.Decision.DestinationAfter.MortalityRisk;
                        migration.ForecastDestinationStability = intent.Decision.DestinationAfter.Stability;
                        migration.ForecastSourceRenewableFoodRatio = intent.Decision.SourceAfter.RenewableFoodRatio;
                        migration.ForecastDestinationRenewableFoodRatio = intent.Decision.DestinationAfter.RenewableFoodRatio;
                        migration.ForecastDestinationPlantFoodProduction = intent.Decision.DestinationAfter.PlantFoodProduction;
                        migration.ForecastDestinationAnimalFoodProduction = intent.Decision.DestinationAfter.AnimalFoodProduction;
                        migration.ForecastDestinationAquaticFoodProduction = intent.Decision.DestinationAfter.AquaticFoodProduction;
                        migration.ForecastSourceExposureRate = intent.Decision.SourceAfter.ExposureRate;
                        migration.ForecastSourceCrowdingRate = intent.Decision.SourceAfter.CrowdingRate;
                        migration.ForecastSourceDiseaseRate = intent.Decision.SourceAfter.DiseaseRate;
                        migration.ForecastSourceStarvationRate = intent.Decision.SourceAfter.StarvationRate;
                        migration.ForecastDestinationExposureRate = intent.Decision.DestinationAfter.ExposureRate;
                        migration.ForecastDestinationCrowdingRate = intent.Decision.DestinationAfter.CrowdingRate;
                        migration.ForecastDestinationDiseaseRate = intent.Decision.DestinationAfter.DiseaseRate;
                        migration.ForecastDestinationStarvationRate = intent.Decision.DestinationAfter.StarvationRate;
                        migration.ForecastConfidence = (int)intent.Decision.DestinationAfter.Confidence;
                        migration.MatchedOpportunities = intent.Decision.Opportunity.Matched;
                    }
                    record.Migrations.Add(migration);
                    if (migration.HigherQoLWorseMortality)
                        metrics.ImmigrationHigherQoLWorseMortality++;
                    Remember(w, source, destination.Id, +1); Remember(w, destination, source.Id, -1);
                    record.Log("Camp " + source.Id + " (tribe " + source.Player + ") sent " + m + " of " + intent.Residents + " people to camp " + destination.Id + " (tribe " + destination.Player + ").");
                }
            }
            metrics.ImmigrationMilliseconds = timer.Elapsed.TotalMilliseconds;
            return participants;
        }

        // Hysteresis memory: drop records older than the cooldown from every camp that holds any (a serial O(camps) scan of a sparse field).
        private static void Prune(WorldState w)
        {
            int cooldown = w.Config.ImmigrationReturnCooldownTurns;
            foreach (var camp in w.Camps)
            {
                if (camp.RecentMigration == null) continue;
                camp.RecentMigration.RemoveAll(m => w.Turn - m.Turn > cooldown);
                if (camp.RecentMigration.Count == 0) camp.RecentMigration = null;
            }
        }
        private static void Remember(WorldState w, Camp camp, int other, int direction)
        {
            if (w.Config.ImmigrationReturnCooldownTurns <= 0) return;
            if (camp.RecentMigration == null) camp.RecentMigration = new List<MigrationMemory>();
            camp.RecentMigration.RemoveAll(m => m.Other == other && m.Direction == direction);   // one record per (camp, other, direction): the latest
            camp.RecentMigration.Add(new MigrationMemory { Other = other, Turn = w.Turn, Direction = direction });
        }

        // Save-stable compatibility path for worlds that explicitly retain the
        // pre-v1.1 demographic model.  New PhysicalMortalityV1 worlds use the
        // shared evaluator below; old saves keep their recorded social decision
        // law rather than being silently rebalanced on load.
        private static Evaluation EvaluateLegacy(Context context, Camp camp)
        {
            var w = context.World; var cfg = w.Config; var e = new Evaluation { Camp = camp };
            if (camp.Population < cfg.ImmigrationSourceFloor + cfg.ImmigrationMinimumGroup) return e;
            var visible = context.Index.VisibleCached(camp.Player);
            var memory = ContactKnowledge.Remembers(camp.People) ? camp.People.Memory : null;
            double qi = context.Quality[camp.Id]; Camp best = null; double bestUtility = 0, bestQuality = 0, bestPenalty = 0; int bestDistance = 0;
            int maxGroup = MaxGroup(cfg, camp.Population); if (maxGroup < cfg.ImmigrationMinimumGroup) return e;
            var sourceModel = context.Models[camp.Id]; double perPerson = CarriedPerPerson(cfg, camp);
            foreach (int tile in w.Map.Radius(camp.Tile, cfg.ImmigrationRadius).OrderBy(n => n))
            {
                if (!context.CampsByTile.TryGetValue(tile, out var here)) continue;
                foreach (var c in here)
                {
                    if (c.Id == camp.Id) continue;
                    bool known = c.SocietyId == camp.SocietyId || visible.Contains(tile);
                    if (!known && memory != null) { var contact = memory.Find(c.Id); known = contact != null && !contact.Moved && contact.Tile == tile; }
                    if (!known) continue;
                    e.Candidates++;
                    int distance = w.Map.Distance(camp.Tile, tile); double qj = context.Quality[c.Id]; double penalty = ReturnPenalty(w, camp, c);
                    double u = ForwardUtility(cfg, sourceModel, context.Models[c.Id], maxGroup, 0,
                        maxGroup * perPerson, distance, camp, c, penalty);
                    if (best == null || u > bestUtility || u == bestUtility
                        && (distance < bestDistance || distance == bestDistance && c.Id < best.Id))
                    { best = c; bestUtility = u; bestQuality = qj; bestDistance = distance; bestPenalty = penalty; }
                }
            }
            if (best == null) return e;
            int size = Size(cfg, camp.Population, bestUtility); if (size <= 0) return e;
            double final = ForwardUtility(cfg, sourceModel, context.Models[best.Id], size, 0,
                size * perPerson, bestDistance, camp, best, bestPenalty);
            if (!(final > cfg.ImmigrationDeadband)) return e;
            e.Intent = new Intent
            {
                Source = camp, Destination = best, Residents = camp.Population,
                DestinationResidents = best.Population, Distance = bestDistance, Size = size,
                QualitySource = qi, QualityDestination = bestQuality, Utility = final,
                Penalty = bestPenalty, CarriedPerPerson = perPerson, Plant = camp.PlantFood,
                Animal = camp.AnimalFood, Aquatic = camp.AquaticFood
            };
            return e;
        }

        private static Evaluation Evaluate(Context context, Camp camp)
        {
            var w = context.World; var cfg = w.Config; var e = new Evaluation { Camp = camp };
            // Sources too small to give a minimum group and keep the floor emit nothing.
            if (camp.Population < cfg.ImmigrationSourceFloor + cfg.ImmigrationMinimumGroup) return e;
            var visible = context.Index.VisibleCached(camp.Player);
            var memory = ContactKnowledge.Remembers(camp.People) ? camp.People.Memory : null;
            double qi = context.Quality[camp.Id]; Camp best = null; double bestUtility = double.NegativeInfinity, bestQuality = 0, bestPenalty = 0; int bestDistance = 0, bestSize = 0;
            SocialRelocationEvaluation bestDecision = null;
            WorkforceTransfer bestTransfer = null;
            void Record(SocialRelocationEvaluation decision)
            {
                e.SocialEvaluations++;
                if (!decision.MoverPass) e.MoverRejected++;
                if (!decision.AggregatePass) e.AggregateRejected++;
                if (!decision.SourcePass) e.SourceRejected++;
                if (!decision.IncumbentPass) e.IncumbentRejected++;
            }
            // Every known destination is scored through the shared physical
            // evaluator at a bounded deterministic group-size shortlist.  The
            // legacy ramp target is included exactly; scoring every shortlist
            // member removes the obsolete monotonicity assumption.
            int maxGroup = MaxGroup(cfg, camp.Population); if (maxGroup < cfg.ImmigrationMinimumGroup) return e;
            double perPerson = CarriedPerPerson(cfg, camp);
            foreach (int tile in w.Map.Radius(camp.Tile, cfg.ImmigrationRadius).OrderBy(n => n))
            {
                if (!context.CampsByTile.TryGetValue(tile, out var here)) continue;
                foreach (var c in here)
                {
                    if (c.Id == camp.Id) continue;
                    // Known settlements only: the owner's own camps, camps in current vision, and (after the first breakthrough) remembered contacts
                    // that have not moved or expired. A contact whose camp is no longer at its recorded tile is not found.
                    bool known = c.SocietyId == camp.SocietyId || visible.Contains(tile);
                    if (!known && memory != null) { var contact = memory.Find(c.Id); known = contact != null && !contact.Moved && contact.Tile == tile; }
                    if (!known) continue;
                    e.Candidates++;
                    int distance = w.Map.Distance(camp.Tile, tile); double qj = context.Quality[c.Id]; double penalty = ReturnPenalty(w, camp, c);
                    var maxDecision = Decide(context, camp, c, maxGroup, 0, 0, 0, 0, null, 0, null,
                        null, out var maxTransfer);
                    Record(maxDecision);
                    int preferred = Size(cfg, camp.Population, maxDecision.MoverUtilityChange);
                    SocialRelocationEvaluation chosen = null; WorkforceTransfer chosenTransfer = null; int chosenSize = 0;
                    var candidates = CandidateSizes(cfg, camp.Population, preferred);
                    foreach (int size in candidates)
                    {
                        SocialRelocationEvaluation decision; WorkforceTransfer transfer;
                        if (size == maxGroup) { decision = maxDecision; transfer = maxTransfer; }
                        else { decision = Decide(context, camp, c, size, 0, 0, 0, 0, null, 0, null,
                            null, out transfer); Record(decision); }
                        if (!decision.Accepted) continue;
                        if (chosen == null || preferred > 0 && (Math.Abs(size - preferred) < Math.Abs(chosenSize - preferred)
                                || Math.Abs(size - preferred) == Math.Abs(chosenSize - preferred) && size > chosenSize)
                            || preferred <= 0 && (decision.MoverUtilityChange > chosen.MoverUtilityChange
                                || decision.MoverUtilityChange == chosen.MoverUtilityChange && size > chosenSize))
                        { chosen = decision; chosenTransfer = transfer; chosenSize = size; }
                    }
                    if (chosen == null) continue;
                    double u = chosen.MoverUtilityChange;
                    if (best == null || u > bestUtility || u == bestUtility && (distance < bestDistance || distance == bestDistance && c.Id < best.Id))
                    {
                        best = c; bestUtility = u; bestQuality = qj; bestDistance = distance; bestPenalty = penalty;
                        bestDecision = chosen; bestTransfer = chosenTransfer; bestSize = chosenSize;
                    }
                }
            }
            if (best == null) return e;
            e.Intent = new Intent
            {
                Source = camp, Destination = best, Residents = camp.Population, DestinationResidents = best.Population,
                Distance = bestDistance, Size = bestSize, QualitySource = qi, QualityDestination = bestQuality,
                Utility = bestUtility, Penalty = bestPenalty, CarriedPerPerson = perPerson, Plant = camp.PlantFood,
                Animal = camp.AnimalFood, Aquatic = camp.AquaticFood, Decision = bestDecision, Workforce = bestTransfer
            };
            return e;
        }
    }
}
