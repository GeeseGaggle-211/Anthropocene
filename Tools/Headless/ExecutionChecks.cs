using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class ExecutionChecks
{
    private static long MetricLong(ResolutionMetrics metrics, string name)
    {
        var field = typeof(ResolutionMetrics).GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return field == null ? 0 : Convert.ToInt64(field.GetValue(metrics));
    }
    private static double Percentile(IReadOnlyList<double> source, double fraction)
    {
        if (source.Count == 0) return 0;
        var sorted = source.OrderBy(value => value).ToArray();
        return sorted[(int)Math.Round((sorted.Length - 1) * fraction)];
    }
    private static string FirstDifference(object left, object right, string path = "world")
    {
        if (ReferenceEquals(left, right)) return null;
        if (left == null || right == null) return path + ": " + (left ?? "null") + " != " + (right ?? "null");
        var type = left.GetType();
        if (type != right.GetType()) return path + ": " + type.Name + " != " + right.GetType().Name;
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            return left.Equals(right) ? null : path + ": " + left + " != " + right;
        if (left is IEnumerable leftItems && right is IEnumerable rightItems)
        {
            var a = leftItems.GetEnumerator(); var b = rightItems.GetEnumerator(); int index = 0;
            while (true)
            {
                bool hasA = a.MoveNext(), hasB = b.MoveNext();
                if (hasA != hasB) return path + ": collection lengths differ at " + index;
                if (!hasA) return null;
                string difference = FirstDifference(a.Current, b.Current, path + "[" + index + "]");
                if (difference != null) return difference;
                index++;
            }
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            if (type == typeof(WorldState) && field.Name == nameof(WorldState.Generation)) continue;
            string difference = FirstDifference(field.GetValue(left), field.GetValue(right), path + "." + field.Name);
            if (difference != null) return difference;
        }
        return null;
    }
    public static void Run()
    {
        var reference = new WorldGenerator().Generate(53, SimulationConfig.Small(4));
        var workers = new[] { new PhaseExecution(2, 7, false, 1), new PhaseExecution(4, 3, true, 1) };
        var worlds = workers.Select(x => SaveCodec.Load(SaveCodec.Save(reference))).ToArray();
        int turns = 0, postOpeningTurns = 0, agriculturalTurns = 0;
        bool matureSaveRestored = false;
        // Continue into the economy whose seasonal scratch state is being reused.
        // Offset the continuation clock to exercise partial first/last seasons.
        while ((!reference.Finished && turns < 12) || postOpeningTurns < 25)
        {
            if (turns >= 12 && !reference.Finished) throw new Exception("Execution replay opening did not finish within 12 turns.");
            if (reference.Finished)
            {
                if (postOpeningTurns == 0)
                {
                    double offset = .37 * reference.Config.BaseDays;
                    reference.Days += offset;
                    foreach (var world in worlds) world.Days += offset;
                }
                postOpeningTurns++;
            }
            if (reference.Camps.Any(CultivationEconomy.Enabled)) agriculturalTurns++;
            var plan = AiPlanning.Decide(reference); new TurnResolver().Resolve(reference, plan.Orders, plan.Traces);
            for (int i = 0; i < worlds.Length; i++)
            {
                var w = worlds[i]; w.Camps.Reverse(); w.Hunters.Reverse(); w.Herds.Reverse();
                var parallel = AiPlanning.Decide(w, workers[i]);
                if (!plan.Orders.OrderBy(p => p.Key).SelectMany(p => p.Value.Select(o => p.Key + ":" + o)).SequenceEqual(parallel.Orders.OrderBy(p => p.Key).SelectMany(p => p.Value.Select(o => p.Key + ":" + o)))) throw new Exception("Parallel AI differs from reference.");
                new TurnResolver(workers[i]).Resolve(w, parallel.Orders, parallel.Traces);
                if (SaveCodec.Fingerprint(reference) != SaveCodec.Fingerprint(w))
                {
                    var rc = reference.Camps.OrderBy(c => c.Id).First(); var wc = w.Camps.OrderBy(c => c.Id).First();
                    string difference = FirstDifference(reference.Camps.OrderBy(c => c.Id).ToArray(), w.Camps.OrderBy(c => c.Id).ToArray(), "camps")
                        ?? FirstDifference(reference.Hunters.OrderBy(h => h.Id).ToArray(), w.Hunters.OrderBy(h => h.Id).ToArray(), "hunters")
                        ?? FirstDifference(reference.Herds.OrderBy(h => h.Id).ToArray(), w.Herds.OrderBy(h => h.Id).ToArray(), "herds")
                        ?? FirstDifference(reference.History.ToArray(), w.History.ToArray(), "history")
                        ?? FirstDifference(reference.Societies.OrderBy(s => s.Id).ToArray(), w.Societies.OrderBy(s => s.Id).ToArray(), "societies")
                        ?? "unlocated canonical difference";
                    throw new Exception("Worker/partition replay differs on turn " + turns + ": " + difference
                        + " firstCamp=" + rc.Id + "/" + wc.Id + " food=" + rc.PlantFood + "," + rc.AnimalFood + "," + rc.AquaticFood
                        + "/" + wc.PlantFood + "," + wc.AnimalFood + "," + wc.AquaticFood
                        + " ids=" + string.Join(",", reference.Camps.Select(c => c.Id)) + "/" + string.Join(",", w.Camps.Select(c => c.Id)));
                }
            }
            if (postOpeningTurns == 12 && !matureSaveRestored)
            {
                if (agriculturalTurns == 0 || !reference.Camps.Any(CultivationEconomy.Enabled))
                    throw new Exception("Mature save continuation checkpoint did not reach active agriculture.");
                for (int i = 0; i < worlds.Length; i++)
                {
                    string before = SaveCodec.Fingerprint(worlds[i]);
                    var restored = SaveCodec.Load(SaveCodec.Save(worlds[i]));
                    if (before != SaveCodec.Fingerprint(restored))
                        throw new Exception("Mature agricultural save/load differs for worker partition " + i + " at post-opening turn " + postOpeningTurns);
                    worlds[i] = restored;
                }
                matureSaveRestored = true;
                Console.WriteLine("PASS: mature agricultural save restoration matched fingerprints for 2/4-worker worlds at post-opening turn " + postOpeningTurns);
            }
            turns++;
        }
        if (!matureSaveRestored) throw new Exception("Mature agricultural save/load checkpoint was not reached.");
        if (agriculturalTurns < 25) throw new Exception("Mature execution replay did not exercise 25 agricultural turns.");
        Console.WriteLine("PASS: reference vs 2/4 workers, reversed partitions and entity collections across " + turns + " resolutions, including " + postOpeningTurns + " post-opening turns with partial seasonal boundaries");
    }
    public static void Scale(int players)
    {
        if (players != 80 && players != 320) throw new ArgumentException("Scale audit supports 80 or 320 tribes.");
        var process = Process.GetCurrentProcess(); var timer = Stopwatch.StartNew();
        var config = new SimulationConfig { Width = players == 80 ? 1024 : 2048, Height = players == 80 ? 512 : 1024, Players = players, BenchmarkScale = players == 320 };
        var world = new WorldGenerator().Generate(1729, config); double generationMs = timer.Elapsed.TotalMilliseconds; long managedAfterGeneration = GC.GetTotalMemory(true), peakManaged = managedAfterGeneration;
        timer.Restart(); var bytes = SaveCodec.Save(world); double initialSaveMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart(); var replay = SaveCodec.Load(bytes); double initialLoadMs = timer.Elapsed.TotalMilliseconds;
        var execution = new PhaseExecution(4, 64); var resolver = new TurnResolver(execution); var oracle = new TurnResolver();
        var samples = new List<object>(); var turnMs = new List<double>(); var allocatedBytes = new List<double>();
        int turns = 0; double totalAi = 0, totalResolve = 0, totalEcology = 0, totalHerds = 0, totalReference = 0, totalSocial = 0;
        long totalSocialForecasts = 0, totalSocialBuilds = 0, totalSocialHits = 0, totalSocialCandidates = 0;
        while (!world.Finished && turns < 12)
        {
            long allocatedBefore = GC.GetTotalAllocatedBytes(false);
            var wholeTurn = Stopwatch.StartNew();
            var plan = AiPlanning.Decide(world, execution);
            var preparationTimer = Stopwatch.StartNew();
            var prepared = TurnPlanner.Prepare(world, plan.Orders, plan.Traces, execution);
            double preparationMs = preparationTimer.Elapsed.TotalMilliseconds;
            resolver.Resolve(world, prepared);
            var metrics = resolver.LastMetrics;
            wholeTurn.Stop();
            long allocated = Math.Max(0, GC.GetTotalAllocatedBytes(false) - allocatedBefore);
            double socialMs = metrics.ImmigrationMilliseconds + metrics.FissionMilliseconds;
            long socialForecasts = MetricLong(metrics, "SocialForecasts");
            long socialBuilds = MetricLong(metrics, "SocialForecastCacheBuilds");
            long socialHits = MetricLong(metrics, "SocialForecastCacheHits");
            long socialCandidates = MetricLong(metrics, "SocialCandidatesEvaluated");
            var referenceOrders = plan.Orders.ToDictionary(p => p.Key, p => p.Value.AsEnumerable().Reverse().ToList());
            oracle.Resolve(replay, TurnPlanner.Prepare(replay, referenceOrders, plan.Traces));
            if (SaveCodec.Fingerprint(world) != SaveCodec.Fingerprint(replay)) throw new Exception("Scale replay diverged at turn " + world.Turn);
            peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
            totalAi += plan.Milliseconds + preparationMs; totalResolve += metrics.TotalMilliseconds; totalEcology += metrics.EcologyMilliseconds; totalHerds += metrics.HerdIntentMilliseconds; totalReference += oracle.LastMetrics.TotalMilliseconds;
            totalSocial += socialMs; totalSocialForecasts += socialForecasts; totalSocialBuilds += socialBuilds;
            totalSocialHits += socialHits; totalSocialCandidates += socialCandidates;
            turnMs.Add(wholeTurn.Elapsed.TotalMilliseconds); allocatedBytes.Add(allocated);
            samples.Add(new { turn = world.Turn - 1, turnMs = wholeTurn.Elapsed.TotalMilliseconds, allocatedBytes = allocated,
                aiMs = plan.Milliseconds + preparationMs, prepareMs = preparationMs, resolveMs = metrics.TotalMilliseconds,
                socialMs, socialForecasts, socialBuilds, socialHits, socialCandidates,
                immigrationMs = metrics.ImmigrationMilliseconds, fissionMs = metrics.FissionMilliseconds,
                ecologyMs = metrics.EcologyMilliseconds, setupEconomyMs = metrics.SetupEconomyMilliseconds,
                managedHerdMs = metrics.ManagedHerdMilliseconds, growthPlanMs = metrics.GrowthPlanMilliseconds,
                sustainMs = metrics.SustainMilliseconds, productionMs = metrics.ProductionMilliseconds,
                herdIntentMs = metrics.HerdIntentMilliseconds, activeTiles = metrics.ActiveTiles,
                tileIntegrations = metrics.TileIntegrations, seasonalTiles = metrics.SeasonalTiles,
                fullTurnTiles = metrics.FullTurnTiles, referenceResolveMs = oracle.LastMetrics.TotalMilliseconds }); turns++;
            Console.WriteLine("SCALE_PROGRESS tribes=" + players + " turn=" + turns
                + " turnMs=" + wholeTurn.Elapsed.TotalMilliseconds.ToString("F1")
                + " aiMs=" + (plan.Milliseconds + preparationMs).ToString("F1")
                + " resolveMs=" + metrics.TotalMilliseconds.ToString("F1") + " socialMs=" + socialMs.ToString("F1")
                + " allocatedMiB=" + (allocated / 1048576.0).ToString("F2")
                + " active=" + metrics.ActiveTiles + " integrations=" + metrics.TileIntegrations);
        }
        timer.Restart(); bytes = SaveCodec.Save(world); double finalSaveMs = timer.Elapsed.TotalMilliseconds;
        replay = null; GC.Collect(); timer.Restart(); var restored = SaveCodec.Load(bytes); double finalLoadMs = timer.Elapsed.TotalMilliseconds;
        if (SaveCodec.Fingerprint(world) != SaveCodec.Fingerprint(restored)) throw new Exception("Final scale save/load differs.");
        process.Refresh(); long finalManaged = GC.GetTotalMemory(false); peakManaged = Math.Max(peakManaged, finalManaged);
        var report = new { seed = 1729, shape = "Continents", config.Width, config.Height, players, config.BenchmarkScale, turns,
            turnP50Ms = Percentile(turnMs, .5), turnP95Ms = Percentile(turnMs, .95),
            allocationP50Bytes = Percentile(allocatedBytes, .5), allocationP95Bytes = Percentile(allocatedBytes, .95),
            generationMs, totalAiMs = totalAi, totalResolveMs = totalResolve, totalSocialMs = totalSocial,
            totalSocialForecasts, totalSocialBuilds, totalSocialHits, totalSocialCandidates,
            socialCacheHitRate = totalSocialHits / (double)Math.Max(1, totalSocialBuilds + totalSocialHits),
            totalEcologyMs = totalEcology, totalHerdIntentMs = totalHerds, totalReferenceResolveMs = totalReference,
            initialSaveMs, initialLoadMs, finalSaveMs, finalLoadMs, saveBytes = bytes.Length,
            peakWorkingSetBytes = process.PeakWorkingSet64, managedAfterGenerationBytes = managedAfterGeneration,
            peakSampledManagedBytes = peakManaged, managedBytes = finalManaged, camps = world.Camps.Count,
            hunterGroups = world.Hunters.Count, herds = world.Herds.Count, workers = 4,
            replay = "identical every turn and after save/load", samples };
        Directory.CreateDirectory("TestResults"); string path = "TestResults/EcologyScale-" + players + ".json";
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("SCALE_COMPLETE tribes=" + players + " turns=" + turns
            + " turnP50/P95Ms=" + Percentile(turnMs, .5).ToString("F1") + "/" + Percentile(turnMs, .95).ToString("F1")
            + " allocP50/P95MiB=" + (Percentile(allocatedBytes, .5) / 1048576).ToString("F2") + "/" + (Percentile(allocatedBytes, .95) / 1048576).ToString("F2")
            + " generationMs=" + generationMs.ToString("F1") + " totalAiMs=" + totalAi.ToString("F1")
            + " totalResolveMs=" + totalResolve.ToString("F1") + " totalSocialMs=" + totalSocial.ToString("F1")
            + " socialForecast/build/hit/candidates=" + totalSocialForecasts + "/" + totalSocialBuilds + "/" + totalSocialHits + "/" + totalSocialCandidates
            + " socialCacheHitRate=" + (totalSocialHits / (double)Math.Max(1, totalSocialBuilds + totalSocialHits)).ToString("F3")
            + " peakMB=" + (process.PeakWorkingSet64 / 1048576) + " report=" + path);
    }
}
