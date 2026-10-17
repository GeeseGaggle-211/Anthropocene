using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Anthropocene.Simulation;

// Common-source native benchmark that can be dropped unchanged onto the pinned
// demographic commit.  It references the new social counters only by reflection,
// so the same executable measures baseline and candidate whole-turn/social cost.
internal static class SocialScaleBenchmark
{
    private static double P(IReadOnlyList<double> source, double fraction)
    {
        if (source.Count == 0) return 0;
        var sorted = source.OrderBy(value => value).ToArray();
        return sorted[(int)Math.Round((sorted.Length - 1) * fraction)];
    }

    private static long Counter(ResolutionMetrics metrics, string name)
    {
        FieldInfo field = typeof(ResolutionMetrics).GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return field == null ? 0 : Convert.ToInt64(field.GetValue(metrics));
    }

    public static void Run(int players, int postOpeningTurns)
    {
        if (players != 8 && players != 80 && players != 320)
            throw new ArgumentException("Social scale benchmark supports 8, 80 or 320 initial camps.");
        if (postOpeningTurns < 0 || postOpeningTurns > 30)
            throw new ArgumentOutOfRangeException(nameof(postOpeningTurns));
        SimulationConfig config;
        if (players == 8) config = SimulationConfig.Small(players);
        else config = new SimulationConfig
        {
            Width = players == 80 ? 1024 : 2048,
            Height = players == 80 ? 512 : 1024,
            Players = players,
            BenchmarkScale = players == 320
        };
        var generation = Stopwatch.StartNew();
        WorldState world = new WorldGenerator().Generate(1729, config);
        generation.Stop();
        var execution = new PhaseExecution(4, 64);
        var resolver = new TurnResolver(execution);
        var turnTimes = new List<double>(); var allocations = new List<double>(); var socialTimes = new List<double>();
        int resolutions = 0, post = 0; long forecasts = 0, builds = 0, hits = 0, candidates = 0;
        long jointBatches = 0, jointEvaluations = 0, jointEndpoints = 0,
            jointDependencies = 0, jointMatches = 0;
        while ((!world.Finished && resolutions < 12) || post < postOpeningTurns)
        {
            if (world.Finished) post++;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            PlanningResult plan = AiPlanning.Decide(world, execution);
            PreparedTurn prepared = TurnPlanner.Prepare(world, plan.Orders, plan.Traces, execution);
            resolver.Resolve(world, prepared);
            timer.Stop();
            ResolutionMetrics metrics = resolver.LastMetrics;
            long allocated = Math.Max(0, GC.GetTotalAllocatedBytes(true) - before);
            double social = metrics.ImmigrationMilliseconds + metrics.FissionMilliseconds;
            turnTimes.Add(timer.Elapsed.TotalMilliseconds); allocations.Add(allocated); socialTimes.Add(social);
            forecasts += Counter(metrics, "SocialForecasts"); builds += Counter(metrics, "SocialForecastCacheBuilds");
            hits += Counter(metrics, "SocialForecastCacheHits"); candidates += Counter(metrics, "SocialCandidatesEvaluated");
            jointBatches += Counter(metrics, "SocialJointBatches");
            jointEvaluations += Counter(metrics, "SocialJointFlowEvaluations");
            jointEndpoints += Counter(metrics, "SocialJointEndpointBuilds");
            jointDependencies += Counter(metrics, "SocialJointDependencyVisits");
            jointMatches += Counter(metrics, "SocialJointOpportunityMatches");
            resolutions++;
            Console.WriteLine("SOCIAL_SCALE_TURN players=" + players + " postTarget=" + postOpeningTurns
                + " resolution=" + resolutions + " camps=" + world.Camps.Count
                + " population=" + world.Camps.Sum(world.LinkedPopulation)
                + " turnMs=" + timer.Elapsed.TotalMilliseconds.ToString("F3")
                + " socialMs=" + social.ToString("F3")
                + " allocatedMiB=" + (allocated / 1048576.0).ToString("F3")
                + " forecasts=" + Counter(metrics, "SocialForecasts")
                + " builds=" + Counter(metrics, "SocialForecastCacheBuilds")
                + " hits=" + Counter(metrics, "SocialForecastCacheHits")
                + " candidates=" + Counter(metrics, "SocialCandidatesEvaluated")
                + " jointBatch/eval/endpoint/dependency/match="
                    + Counter(metrics, "SocialJointBatches") + "/"
                    + Counter(metrics, "SocialJointFlowEvaluations") + "/"
                    + Counter(metrics, "SocialJointEndpointBuilds") + "/"
                    + Counter(metrics, "SocialJointDependencyVisits") + "/"
                    + Counter(metrics, "SocialJointOpportunityMatches"));
        }
        Console.WriteLine("SOCIAL_SCALE_COMPLETE players=" + players + " postOpeningTurns=" + postOpeningTurns
            + " resolutions=" + resolutions + " terminalCamps=" + world.Camps.Count
            + " terminalPopulation=" + world.Camps.Sum(world.LinkedPopulation)
            + " generationMs=" + generation.Elapsed.TotalMilliseconds.ToString("F3")
            + " turnP50/P95Ms=" + P(turnTimes, .5).ToString("F3") + "/" + P(turnTimes, .95).ToString("F3")
            + " socialP50/P95Ms=" + P(socialTimes, .5).ToString("F3") + "/" + P(socialTimes, .95).ToString("F3")
            + " allocP50/P95MiB=" + (P(allocations, .5) / 1048576).ToString("F3")
                + "/" + (P(allocations, .95) / 1048576).ToString("F3")
            + " socialForecast/build/hit/candidates=" + forecasts + "/" + builds + "/" + hits + "/" + candidates
            + " jointBatch/eval/endpoint/dependency/match=" + jointBatches + "/" + jointEvaluations
                + "/" + jointEndpoints + "/" + jointDependencies + "/" + jointMatches
            + " cacheHitRate=" + (hits / (double)Math.Max(1, builds + hits)).ToString("F3"));
    }
}
