using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Anthropocene.Simulation
{
    // Execution choices are deliberately outside saved configuration and authoritative history.
    public sealed class PhaseExecution
    {
        public readonly int Workers, PartitionSize, ParallelThreshold;
        public readonly bool ReversePartitions;
        public PhaseExecution(int workers = 1, int partitionSize = 64, bool reversePartitions = false, int parallelThreshold = 64)
        { Workers = Math.Max(1, workers); PartitionSize = Math.Max(1, partitionSize); ReversePartitions = reversePartitions; ParallelThreshold = Math.Max(1, parallelThreshold); }
        public static PhaseExecution Desktop => new PhaseExecution(Math.Min(4, System.Environment.ProcessorCount));
        public TResult[] Map<T, TResult>(IReadOnlyList<T> inputs, Func<T, TResult> evaluate)
        {
            var output = new TResult[inputs.Count];
            if (Workers == 1 || inputs.Count < ParallelThreshold)
            { for (int i = 0; i < inputs.Count; i++) output[i] = evaluate(inputs[i]); return output; }
            int effectiveSize = Math.Min(PartitionSize, Math.Max(1, inputs.Count / (Workers * 2)));
            var partitions = Enumerable.Range(0, (inputs.Count + effectiveSize - 1) / effectiveSize).ToArray();
            if (ReversePartitions) Array.Reverse(partitions);
            Parallel.ForEach(partitions, new ParallelOptions { MaxDegreeOfParallelism = Workers }, partition =>
            {
                int end = Math.Min(inputs.Count, (partition + 1) * effectiveSize);
                for (int i = partition * effectiveSize; i < end; i++) output[i] = evaluate(inputs[i]);
            });
            return output;
        }
    }
    public sealed class PlanningResult
    {
        public Dictionary<int, List<Order>> Orders = new Dictionary<int, List<Order>>();
        public List<DecisionTrace> Traces = new List<DecisionTrace>();
        public double Milliseconds;
    }
    // Transient counters are never an input to simulation, observation, or save data.
    internal sealed class TurnDiagnostics
    {
        public readonly int Turn;
        public long PlanCalls, ForecastCalls, LocalSourceQueries, SourceLookups, DepositEdges, BodyScans;
        public long FoodForecastQueries, FoodInvestmentComparisons;
        // Planner work counts (Docs/SharedAllocationValidation.md, planner cost):
        // transient, opt-in through ResolutionMetrics.CaptureDetailedCounters.
        public long SharedAllocations, SharedOperationChoices, SharedHerdChoices, ExpansionLadderBuilds, HerdLadderBuilds, PastureScans;
        public long PlannerTileProjections, PlannerEvaluations, SharedSelections, KernelCacheHits, SharedAllocationTicks, PlanTicks;
        public long DraftIntentsReused, DraftIntentsRecomputed;
        public TurnDiagnostics(int turn) { Turn = turn; }
        public static TurnDiagnostics For(WorldState world)
        {
            var current = world.Diagnostics;
            if (current != null && current.Turn == world.Turn) return current;
            var created = new TurnDiagnostics(world.Turn);
            Interlocked.Exchange(ref world.Diagnostics, created);
            return created;
        }
    }
    public static class AiPlanning
    {
        public static PlanningResult Decide(WorldState world, PhaseExecution execution = null, int excludedPlayer = -1)
            => DecideCore(world, execution, excludedPlayer, true);

        // Headless-only causal seam for the shared settlement-envelope audit.
        // It can restore the formerly uncapped field-capital term inside the
        // unified movement comparison; production callers use Decide above.
        internal static PlanningResult DecideWithEconomicDiagnostics(
            WorldState world, PhaseExecution execution,
            bool boundFieldCapitalByService, int excludedPlayer = -1)
            => DecideCore(world, execution, excludedPlayer,
                boundFieldCapitalByService);

        private static PlanningResult DecideCore(WorldState world,
            PhaseExecution execution, int excludedPlayer,
            bool boundFieldCapitalByService)
        {
            using var profile = PhaseProfile.Scope("Decide");
            var timer = Stopwatch.StartNew(); var result = new PlanningResult();
            TurnDiagnostics.For(world);
            if (world.Resources != null) MaterialAccessIndex.For(world);
            var index = new PlanningIndex(world); var camps = world.Camps.Where(c => c.Player != excludedPlayer).GroupBy(c => c.Player).OrderBy(g => g.Key).Select(g => g.OrderBy(c => c.Id).First()).ToArray();
            // AI chooses movement from the filtered view. Economic automation
            // runs once in TurnPlanner after effective camp destinations are
            // fixed, so observation construction never triggers an early plan.
            var decisions = (execution ?? new PhaseExecution()).Map(camps,
                c => new HeuristicTribePolicy(boundFieldCapitalByService)
                    .Decide(new AgentObservationBuilder().Build(world,
                        c.Player, index), c.Mandate));
            for (int i = 0; i < camps.Length; i++) { result.Orders[camps[i].Player] = decisions[i].Orders; result.Traces.Add(decisions[i].Trace); }
            result.Milliseconds = timer.Elapsed.TotalMilliseconds; return result;
        }
    }
    public sealed class ResolutionMetrics
    {
        // Detailed atomic counters are opt-in so profiling does not add
        // contention to the hot source lookup path in ordinary play.
        public static bool CaptureDetailedCounters;
        // Issue #33 measurement hook. The resolver fills this only when the
        // validation harness opts in; it is transient and never enters a save,
        // observation, or authoritative turn record.
        public static bool CaptureCraftWorkDiagnostics;
        public Dictionary<int, CampFieldWorkDiagnostic> CraftFieldWork { get; private set; }
        public Dictionary<int, CampFoodFlowDiagnostic> CraftFoodFlow { get; private set; }
        internal CampFieldWorkDiagnostic CraftFieldWorkFor(int camp)
        {
            if (!CaptureCraftWorkDiagnostics) return null;
            if (CraftFieldWork == null) CraftFieldWork = new Dictionary<int, CampFieldWorkDiagnostic>();
            if (!CraftFieldWork.TryGetValue(camp, out var value))
                CraftFieldWork[camp] = value = new CampFieldWorkDiagnostic { Camp = camp };
            return value;
        }
        internal CampFoodFlowDiagnostic CraftFoodFlowFor(int camp)
        {
            if (!CaptureCraftWorkDiagnostics) return null;
            if (CraftFoodFlow == null) CraftFoodFlow = new Dictionary<int, CampFoodFlowDiagnostic>();
            if (!CraftFoodFlow.TryGetValue(camp, out var value))
                CraftFoodFlow[camp] = value = new CampFoodFlowDiagnostic { Camp = camp };
            return value;
        }
        public double HerdIntentMilliseconds, EcologyMilliseconds, SetupEconomyMilliseconds, ManagedHerdMilliseconds, GrowthPlanMilliseconds, SustainMilliseconds, ProductionMilliseconds, KnowledgeMilliseconds, TotalMilliseconds;
        public double FullTurnIntegrationMilliseconds, SeasonalPreparationMilliseconds, SeasonalIntegrationMilliseconds, SeasonalCommitMilliseconds, SeasonalExpansionMilliseconds;
        public double HorizonPreparationMilliseconds, HorizonCommitMilliseconds;
        public int ActiveTiles, TileIntegrations, SeasonalTiles, FullTurnTiles;
        // Fission pass (WP10a). Transient work counts; never saved, observed or fingerprinted.
        public long FissionCampsEvaluated, FissionValidations, FissionSearches, FissionCandidatesScored, FissionTileProjections, FissionCacheInvalidations, FissionProposals, FissionCommits, FissionDropped, FissionDaughterChecks, FissionDaughterBlocked;
        // Distress fission: proposals and commits by trigger, and the people in distress daughters.
        public long FissionCrowdingProposals, FissionDistressProposals, FissionCrowdingCommits, FissionDistressCommits, FissionDistressDaughters;
        // Fission funnel (Docs/Fission.md, Hinterland and relative destinations): every camp past the gain screen ends in exactly one outcome.
        // Screened = SearchesNoCandidates + SearchesNoViable + WithDestination;
        // WithDestination = BelowThreshold + DaughterBlocked + StayersBlocked + Proposals; Proposals = Dropped + Commits.
        public long FissionScreened, FissionSearchesNoCandidates, FissionSearchesNoViable, FissionWithDestination, FissionBelowThreshold, FissionStayersBlocked;
        public double FissionMilliseconds;
        // Immigration pass (WP10b). Transient work counts; never saved, observed or fingerprinted.
        public long ImmigrationCampsEvaluated, ImmigrationCandidatesScored, ImmigrationIntents, ImmigrationAccepted, ImmigrationPartial, ImmigrationRejected, ImmigrationMigrants;
        public double ImmigrationMilliseconds;
        // Trade handshake (WP8, computed in planning) and settlement. Transient; never saved, observed or fingerprinted.
        public long TradeEligible, TradeRequisitions, TradeHonoured, TradeCountered, TradeIgnored, TradesSettled;
        public double TradePlanMilliseconds, TradeSettleMilliseconds;
        public long PlanCalls, ForecastCalls, LocalSourceQueries, SourceLookups, DepositEdges, BodyScans, ClaimsAllocated, ProductionReceipts;
        // Sparse Fishing uses sorted point lookups; this counter verifies that
        // dormant sites are not scanned during ordinary resource resolution.
        public long FisherySiteLookups;
        public long FoodForecastQueries, FoodInvestmentComparisons;
        public long SharedAllocations, SharedOperationChoices, SharedHerdChoices, ExpansionLadderBuilds, HerdLadderBuilds, PastureScans;
        public long PlannerTileProjections, PlannerEvaluations, SharedSelections, KernelCacheHits;
        public double SharedAllocationMilliseconds, PlanMilliseconds;
        // Opt-in activity diagnostics; these never affect authoritative state.
        public long SeasonalCampBoundaries, OperatingRegimeChanges, FieldWorkSteps, FieldChoiceSwitches;
        public long TargetCompletions, ToolExhaustions, StockDepletions;
        public double ManagedRenewalInput, OperatingFractionSum;
        public int OperatingFieldTiles;
    }

    public sealed class CampFieldWorkDiagnostic
    {
        public int Camp;
        public double ResidentWorkerYears, AssignedFarmerWorkerYears;
        public double OperatingFarmerReservationWorkerYears, ExpansionFarmerWorkerYears;
        public double MaintenanceWorkerYears, HarvestEquivalentWorkerYears;
        public double ReconciledFarmerWorkerYears;
        public double ExpansionWorkUsedWorkerYears, FarmToolCoveredWorkerYears;
        // The bridge is derived from realized gathering receipts. It is
        // reported as a side effect and must not be added to labor totals.
        public double GatheringBridgeCreditWorkerYears;
    }

    public sealed class CampFoodFlowDiagnostic
    {
        public int Camp;
        public double ConsumptionTarget, ReserveTarget, ReserveFillTarget, GrowthFoodTarget;
        public double GrowthFoodUsed;
    }
}
