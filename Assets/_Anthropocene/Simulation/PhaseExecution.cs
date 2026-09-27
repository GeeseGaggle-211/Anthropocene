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
        {
            var timer = Stopwatch.StartNew(); var result = new PlanningResult();
            TurnDiagnostics.For(world);
            if (world.Resources != null) MaterialAccessIndex.For(world);
            var index = new PlanningIndex(world); var camps = world.Camps.Where(c => c.Player != excludedPlayer).GroupBy(c => c.Player).OrderBy(g => g.Key).Select(g => g.OrderBy(c => c.Id).First()).ToArray();
            // AI chooses movement from the filtered view. Economic automation
            // runs once in TurnPlanner after effective camp destinations are
            // fixed, so observation construction never triggers an early plan.
            var decisions = (execution ?? new PhaseExecution()).Map(camps, c => new HeuristicTribePolicy().Decide(new AgentObservationBuilder().Build(world, c.Player, index), c.Mandate));
            for (int i = 0; i < camps.Length; i++) { result.Orders[camps[i].Player] = decisions[i].Orders; result.Traces.Add(decisions[i].Trace); }
            result.Milliseconds = timer.Elapsed.TotalMilliseconds; return result;
        }
    }
    public sealed class ResolutionMetrics
    {
        // Detailed atomic counters are opt-in so profiling does not add
        // contention to the hot source lookup path in ordinary play.
        public static bool CaptureDetailedCounters;
        public double HerdIntentMilliseconds, EcologyMilliseconds, SetupEconomyMilliseconds, ManagedHerdMilliseconds, GrowthPlanMilliseconds, SustainMilliseconds, ProductionMilliseconds, TotalMilliseconds;
        public double FullTurnIntegrationMilliseconds, SeasonalPreparationMilliseconds, SeasonalIntegrationMilliseconds, SeasonalCommitMilliseconds, SeasonalExpansionMilliseconds;
        public double HorizonPreparationMilliseconds, HorizonCommitMilliseconds;
        public int ActiveTiles, TileIntegrations, SeasonalTiles, FullTurnTiles;
        public long PlanCalls, ForecastCalls, LocalSourceQueries, SourceLookups, DepositEdges, BodyScans, ClaimsAllocated, ProductionReceipts;
        // Opt-in activity diagnostics; these never affect authoritative state.
        public long SeasonalCampBoundaries, OperatingRegimeChanges, FieldWorkSteps, FieldChoiceSwitches;
        public long TargetCompletions, ToolExhaustions, StockDepletions;
        public double ManagedRenewalInput, OperatingFractionSum;
        public int OperatingFieldTiles;
    }
}
