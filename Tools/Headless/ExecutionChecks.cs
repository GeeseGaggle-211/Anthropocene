using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class ExecutionChecks
{
    public static void Run()
    {
        var reference = new WorldGenerator().Generate(53, SimulationConfig.Small(4));
        var workers = new[] { new PhaseExecution(2, 7, false, 1), new PhaseExecution(4, 3, true, 1) };
        var worlds = workers.Select(x => SaveCodec.Load(SaveCodec.Save(reference))).ToArray();
        int turns = 0;
        while (!reference.Finished && turns < 12)
        {
            var plan = AiPlanning.Decide(reference); new TurnResolver().Resolve(reference, plan.Orders, plan.Traces);
            for (int i = 0; i < worlds.Length; i++)
            {
                var w = worlds[i]; w.Camps.Reverse(); w.Hunters.Reverse(); w.Herds.Reverse();
                var parallel = AiPlanning.Decide(w, workers[i]);
                if (!plan.Orders.OrderBy(p => p.Key).SelectMany(p => p.Value.Select(o => p.Key + ":" + o)).SequenceEqual(parallel.Orders.OrderBy(p => p.Key).SelectMany(p => p.Value.Select(o => p.Key + ":" + o)))) throw new Exception("Parallel AI differs from reference.");
                new TurnResolver(workers[i]).Resolve(w, parallel.Orders, parallel.Traces);
                if (SaveCodec.Fingerprint(reference) != SaveCodec.Fingerprint(w)) throw new Exception("Worker/partition replay differs on turn " + turns);
            }
            turns++;
        }
        Console.WriteLine("PASS: reference vs 2/4 workers, reversed partitions and entity collections across " + turns + " resolutions");
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
        var samples = new List<object>(); int turns = 0; double totalAi = 0, totalResolve = 0, totalEcology = 0, totalHerds = 0, totalReference = 0;
        while (!world.Finished && turns < 12)
        {
            var plan = AiPlanning.Decide(world, execution);
            var preparationTimer = Stopwatch.StartNew();
            var prepared = TurnPlanner.Prepare(world, plan.Orders, plan.Traces);
            double preparationMs = preparationTimer.Elapsed.TotalMilliseconds;
            resolver.Resolve(world, prepared);
            var metrics = resolver.LastMetrics;
            var referenceOrders = plan.Orders.ToDictionary(p => p.Key, p => p.Value.AsEnumerable().Reverse().ToList());
            oracle.Resolve(replay, TurnPlanner.Prepare(replay, referenceOrders, plan.Traces));
            if (SaveCodec.Fingerprint(world) != SaveCodec.Fingerprint(replay)) throw new Exception("Scale replay diverged at turn " + world.Turn);
            peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
            totalAi += plan.Milliseconds + preparationMs; totalResolve += metrics.TotalMilliseconds; totalEcology += metrics.EcologyMilliseconds; totalHerds += metrics.HerdIntentMilliseconds; totalReference += oracle.LastMetrics.TotalMilliseconds;
            samples.Add(new { turn = world.Turn - 1, aiMs = plan.Milliseconds + preparationMs, prepareMs = preparationMs, resolveMs = metrics.TotalMilliseconds, ecologyMs = metrics.EcologyMilliseconds, setupEconomyMs = metrics.SetupEconomyMilliseconds, managedHerdMs = metrics.ManagedHerdMilliseconds, growthPlanMs = metrics.GrowthPlanMilliseconds, sustainMs = metrics.SustainMilliseconds, productionMs = metrics.ProductionMilliseconds, herdIntentMs = metrics.HerdIntentMilliseconds, activeTiles = metrics.ActiveTiles, tileIntegrations = metrics.TileIntegrations, seasonalTiles = metrics.SeasonalTiles, fullTurnTiles = metrics.FullTurnTiles, referenceResolveMs = oracle.LastMetrics.TotalMilliseconds }); turns++;
            Console.WriteLine("SCALE_PROGRESS tribes=" + players + " turn=" + turns + " aiMs=" + (plan.Milliseconds + preparationMs).ToString("F1") + " resolveMs=" + metrics.TotalMilliseconds.ToString("F1") + " active=" + metrics.ActiveTiles + " integrations=" + metrics.TileIntegrations);
        }
        timer.Restart(); bytes = SaveCodec.Save(world); double finalSaveMs = timer.Elapsed.TotalMilliseconds;
        replay = null; GC.Collect(); timer.Restart(); var restored = SaveCodec.Load(bytes); double finalLoadMs = timer.Elapsed.TotalMilliseconds;
        if (SaveCodec.Fingerprint(world) != SaveCodec.Fingerprint(restored)) throw new Exception("Final scale save/load differs.");
        process.Refresh(); long finalManaged = GC.GetTotalMemory(false); peakManaged = Math.Max(peakManaged, finalManaged);
        var report = new { seed = 1729, shape = "Continents", config.Width, config.Height, players, config.BenchmarkScale, turns, generationMs, totalAiMs = totalAi, totalResolveMs = totalResolve, totalEcologyMs = totalEcology, totalHerdIntentMs = totalHerds, totalReferenceResolveMs = totalReference, initialSaveMs, initialLoadMs, finalSaveMs, finalLoadMs, saveBytes = bytes.Length, peakWorkingSetBytes = process.PeakWorkingSet64, managedAfterGenerationBytes = managedAfterGeneration, peakSampledManagedBytes = peakManaged, managedBytes = finalManaged, camps = world.Camps.Count, hunterGroups = world.Hunters.Count, herds = world.Herds.Count, workers = 4, replay = "identical every turn and after save/load", samples };
        Directory.CreateDirectory("TestResults"); string path = "TestResults/EcologyScale-" + players + ".json";
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("SCALE_COMPLETE tribes=" + players + " turns=" + turns + " generationMs=" + generationMs.ToString("F1") + " totalAiMs=" + totalAi.ToString("F1") + " totalResolveMs=" + totalResolve.ToString("F1") + " peakMB=" + (process.PeakWorkingSet64 / 1048576) + " report=" + path);
    }
}
