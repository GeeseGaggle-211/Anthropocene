using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            var partitions = Enumerable.Range(0, (inputs.Count + PartitionSize - 1) / PartitionSize).ToArray();
            if (ReversePartitions) Array.Reverse(partitions);
            Parallel.ForEach(partitions, new ParallelOptions { MaxDegreeOfParallelism = Workers }, partition =>
            {
                int end = Math.Min(inputs.Count, (partition + 1) * PartitionSize);
                for (int i = partition * PartitionSize; i < end; i++) output[i] = evaluate(inputs[i]);
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
    public static class AiPlanning
    {
        public static PlanningResult Decide(WorldState world, PhaseExecution execution = null, int excludedPlayer = -1)
        {
            var timer = Stopwatch.StartNew(); var result = new PlanningResult();
            var index = new PlanningIndex(world); var camps = world.Camps.Where(c => c.Player != excludedPlayer).GroupBy(c => c.Player).OrderBy(g => g.Key).Select(g => g.OrderBy(c => c.Id).First()).ToArray();
            var decisions = (execution ?? new PhaseExecution()).Map(camps, c => new HeuristicTribePolicy().Decide(new AgentObservationBuilder().Build(world, c.Player, index), c.Mandate));
            for (int i = 0; i < camps.Length; i++) { result.Orders[camps[i].Player] = decisions[i].Orders; result.Traces.Add(decisions[i].Trace); }
            result.Milliseconds = timer.Elapsed.TotalMilliseconds; return result;
        }
    }
    public sealed class ResolutionMetrics
    {
        public double HerdIntentMilliseconds, EcologyMilliseconds, TotalMilliseconds;
        public int ActiveTiles;
    }
}
