using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private const int WorkerCount = 4;
    private const int PartitionSize = 64;
    private const int ReportSchema = 2;

    private static int Option(string[] args, string key, int fallback)
    {
        var value = args.FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.Ordinal));
        return value == null ? fallback : int.Parse(value.Substring(key.Length + 1));
    }

    private static string Option(string[] args, string key, string fallback)
    {
        var value = args.FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.Ordinal));
        return value == null ? fallback : value.Substring(key.Length + 1);
    }

    private static string HashBytes(byte[] bytes)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }

    private static string WriteBytes(string path, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllBytes(fullPath, bytes);
        return HashBytes(bytes);
    }

    private static int CountAgriculturalCamps(WorldState world)
    {
#if MAIN
        return 0;
#else
        return world.Camps.Count(CultivationEconomy.Enabled);
#endif
    }

    private static TurnRecord ResolveTurn(WorldState world, PhaseExecution execution, TurnResolver resolver,
        out double aiMs, out double prepareMs, out double executeMs)
    {
        var clock = Stopwatch.StartNew();
        var ai = AiPlanning.Decide(world, execution);
        aiMs = clock.Elapsed.TotalMilliseconds;

#if MAIN
        clock.Restart();
        var record = resolver.Resolve(world, ai.Orders, ai.Traces);
        prepareMs = 0;
        executeMs = clock.Elapsed.TotalMilliseconds;
        return record;
#else
        clock.Restart();
        var prepared = TurnPlanner.Prepare(world, ai.Orders, ai.Traces, execution);
        prepareMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        var record = resolver.Resolve(world, prepared);
        executeMs = clock.Elapsed.TotalMilliseconds;
        return record;
#endif
    }

    private static void Warmup()
    {
        var world = new WorldGenerator().Generate(1, new SimulationConfig { Width = 96, Height = 48, Players = 4 });
        var execution = new PhaseExecution(WorkerCount, PartitionSize);
        var resolver = new TurnResolver(execution);
        for (int i = 0; i < 8 && !world.Finished; i++)
            ResolveTurn(world, execution, resolver, out _, out _, out _);
    }

    private static void Main(string[] args)
    {
        int warmup = Option(args, "--warmup", 0);
        int players = Option(args, "--players", 80);
        int openingTurnLimit = Option(args, "--turns", 12);
        int postOpeningTurns = Option(args, "--post-opening-turns", 0);
        int seed = Option(args, "--seed", 1729);
        string output = Option(args, "--output", "");
        string inputSave = Option(args, "--load", "");
        string saveFinished = Option(args, "--save-finished", "");
        string shapeName = Option(args, "--shape", "Continents");
        string revision = Option(args, "--revision", "unspecified");
        bool details = Option(args, "--details", 0) != 0;
        bool metrics = Option(args, "--metrics", 0) != 0;
        bool fingerprintsEnabled = Option(args, "--fingerprints", 1) != 0;

        if (openingTurnLimit < 0 || postOpeningTurns < 0 || warmup < 0)
            throw new ArgumentOutOfRangeException("Turn counts and warmup must be non-negative.");
        for (int i = 0; i < warmup; i++) Warmup();

        var generationClock = Stopwatch.StartNew();
        WorldState world;
        double generationMs;
        string inputSaveSha256 = null;
        string finishedSaveSha256 = null;
        if (string.IsNullOrWhiteSpace(inputSave))
        {
            var config = new SimulationConfig
            {
                Width = players == 320 ? 2048 : players == 80 ? 1024 : 96,
                Height = players == 320 ? 1024 : players == 80 ? 512 : 48,
                Players = players,
                BenchmarkScale = players == 320,
                Shape = (WorldShape)Enum.Parse(typeof(WorldShape), shapeName, true)
            };
            world = new WorldGenerator().Generate(seed, config);
            generationMs = generationClock.Elapsed.TotalMilliseconds;
        }
        else
        {
            byte[] inputBytes = File.ReadAllBytes(inputSave);
            inputSaveSha256 = HashBytes(inputBytes);
            world = SaveCodec.Load(inputBytes);
            generationMs = 0;
            players = world.Config.Players;
            seed = world.Seed;
            shapeName = world.Config.Shape.ToString();
        }

        long managedHeapAfterLoadBytes = GC.GetTotalMemory(false);
        var detailedCounterSwitch = typeof(ResolutionMetrics).GetField("CaptureDetailedCounters");
        if (detailedCounterSwitch != null) detailedCounterSwitch.SetValue(null, metrics);

        var execution = new PhaseExecution(WorkerCount, PartitionSize);
        var resolver = new TurnResolver(execution);
        var samples = new List<object>();
        string initialFingerprint = fingerprintsEnabled ? SaveCodec.Fingerprint(world) : null;
        string finishedFingerprint = world.Finished ? initialFingerprint : null;
        int openingTurnsMeasured = 0;

        if (world.Finished && !string.IsNullOrWhiteSpace(saveFinished))
            finishedSaveSha256 = WriteBytes(saveFinished, SaveCodec.Save(world));

        // --turns caps opening resolution. If the save is already at the opening
        // boundary, it contributes zero opening turns and the requested fixed
        // post-opening horizon still runs.
        for (int i = 0; i < openingTurnLimit && !world.Finished; i++)
        {
            RunOneTurn(world, execution, resolver, samples, details, fingerprintsEnabled, out string turnFingerprint);
            openingTurnsMeasured++;
            if (world.Finished)
            {
                finishedFingerprint = turnFingerprint;
                if (!string.IsNullOrWhiteSpace(saveFinished))
                    finishedSaveSha256 = WriteBytes(saveFinished, SaveCodec.Save(world));
            }
        }

        if (!string.IsNullOrWhiteSpace(saveFinished) && !world.Finished)
            throw new InvalidOperationException("The opening did not complete within --turns; no finished-state save was written.");

        if (postOpeningTurns > 0 && !world.Finished)
            throw new InvalidOperationException("The opening did not complete within --turns; refusing an incomplete post-opening workload.");

        int postOpeningTurnsMeasured = 0;
        for (int i = 0; i < postOpeningTurns; i++)
        {
            if (!world.Finished && i == 0)
                throw new InvalidOperationException("Post-opening turns require a completed opening checkpoint.");
            RunOneTurn(world, execution, resolver, samples, details, fingerprintsEnabled, out _);
            postOpeningTurnsMeasured++;
        }

        string configHash = SaveCodec.ConfigurationHash(world.Config);
        var result = new
        {
            schemaVersion = ReportSchema,
            revision,
            createdUtc = DateTime.UtcNow.ToString("O"),
            runtime = RuntimeInformation.FrameworkDescription,
            runtimeVersion = Environment.Version.ToString(),
            operatingSystem = RuntimeInformation.OSDescription,
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            seed,
            players,
            shape = shapeName,
            configHash,
            worldWidth = world.Config.Width,
            worldHeight = world.Config.Height,
            turnDays = world.Config.TurnDays,
            workerCount = WorkerCount,
            partitionSize = PartitionSize,
            warmupRuns = warmup,
            metricsEnabled = metrics,
            detailsEnabled = details,
            fingerprintsEnabled,
            replayMode = fingerprintsEnabled ? "exact" : "outcomes-only",
            exactCheckMode = fingerprintsEnabled ? "per-turn-fingerprint" : "disabled; compare outcomes against replay-check run",
            loadedFromSave = !string.IsNullOrWhiteSpace(inputSave),
            inputSave = string.IsNullOrWhiteSpace(inputSave) ? null : Path.GetFullPath(inputSave),
            inputSaveSha256,
            finishedSave = string.IsNullOrWhiteSpace(saveFinished) ? null : Path.GetFullPath(saveFinished),
            finishedSaveSha256,
            initialFingerprint,
            finishedFingerprint,
            openingTurnLimit,
            openingTurnsMeasured,
            postOpeningTurnsRequested = postOpeningTurns,
            postOpeningTurnsMeasured,
            finalFingerprint = fingerprintsEnabled ? SaveCodec.Fingerprint(world) : null,
            generationMs,
            managedHeapAfterLoadBytes,
            managedHeapAfterRunBytes = GC.GetTotalMemory(false),
            samples
        };

        if (!string.IsNullOrWhiteSpace(output))
        {
            string fullPath = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }

        Console.WriteLine("AUDIT revision=" + revision + " players=" + players + " seed=" + seed +
            " openingTurns=" + openingTurnsMeasured + " postOpeningTurns=" + postOpeningTurnsMeasured +
            " metrics=" + metrics + " output=" + output);
    }

    private static void RunOneTurn(WorldState world, PhaseExecution execution, TurnResolver resolver,
        List<object> samples, bool details, bool captureFingerprint, out string fingerprint)
    {
        bool postOpening = world.Finished;
        int preAgriculturalCamps = CountAgriculturalCamps(world);
        var priorResidents = world.Camps.ToDictionary(c => c.Id, c => c.Population);
        int gc0Before = GC.CollectionCount(0), gc1Before = GC.CollectionCount(1), gc2Before = GC.CollectionCount(2);
        long allocatedBefore = GC.GetTotalAllocatedBytes(false);
        var record = ResolveTurn(world, execution, resolver, out double aiMs, out double prepareMs, out double executeMs);
        long allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocatedBefore;
        long managedHeapBytes = GC.GetTotalMemory(false);
        double totalMs = aiMs + prepareMs + executeMs;
        int gc0Collections = GC.CollectionCount(0) - gc0Before;
        int gc1Collections = GC.CollectionCount(1) - gc1Before;
        int gc2Collections = GC.CollectionCount(2) - gc2Before;

        // Serialization, derived totals, counters, and diagnostics remain outside
        // all timed phases. Fingerprints are authoritative save-state checks.
        fingerprint = captureFingerprint ? SaveCodec.Fingerprint(world) : null;
        double residentPopulation = world.Camps.Sum(c => (double)c.Population);
#if MAIN
        double priorWeightedQol = 0;
        double endingPopulationWeightedQol = 0;
        object camps = null;
#else
        double endingPopulationWeightedQol = residentPopulation <= 0 ? 0 : world.Camps.Sum(c => c.QoL * c.Population) / residentPopulation;
        double priorResidentPopulation = priorResidents.Values.Sum(v => (double)v);
        double priorWeightedQol = priorResidentPopulation <= 0 ? 0 :
            world.Camps.Sum(c => c.QoL * priorResidents[c.Id]) / priorResidentPopulation;
        var camps = details ? world.Camps.OrderBy(c => c.Id).Select(c => new
        {
            c.Id, c.Player, c.Population, linked = world.LinkedPopulation(c), c.QoL,
            c.FoodNeedsMetRatio, c.WaterNeedsMetRatio, c.ClothPerCapita, c.HousingPerCapita,
            c.Food, c.PlantFood, c.AnimalFood, c.FabricConsumed, c.HousingDemand,
            c.GrowthRemainder, c.LastConsumed, c.FoodDeficit, c.DeficitTurns,
            c.Inventory.Cloth, c.Inventory.AnimalFabric, c.Inventory.Wood, c.Inventory.Clay,
            c.Inventory.HandTools, c.Inventory.Pots, c.Inventory.Stone, c.Inventory.FarmTools,
            assignments = c.Workforce.Assignments.OrderBy(a => a.Role).Select(a => new { a.Role, a.Population }).ToArray(),
            equipmentTarget = c.EconomicLabor?.EquipmentTarget ?? 0,
            production = record.Production.Where(p => p.Camp == c.Id).GroupBy(p => p.Good).ToDictionary(g => g.Key.ToString(), g => g.Sum(p => p.Output)),
            foodYield = record.Transfers.Where(t => t.Entity == c.Id && t.Kind != "Herd").Sum(t => t.Food)
        }).ToArray() : null;
#endif
        // Preserve schema-1 semantics: weightedQol has always meant the prior cohort.
        double weightedQol = priorWeightedQol;
        double foodYield = record.Transfers.Where(t => t.Kind != "Herd").Sum(t => t.Food);
        int population = world.Camps.Sum(world.LinkedPopulation);
        double Metric(string name) => Convert.ToDouble(resolver.LastMetrics.GetType().GetField(name)?.GetValue(resolver.LastMetrics) ?? 0);

        var row = new
        {
            turn = world.Turn - 1,
            phase = postOpening ? "post-opening" : "opening",
            postOpening,
            openingCompleteAfterTurn = world.Finished,
            aiMs,
            prepareMs,
            executeMs,
            turnPrepareMs = prepareMs,
            turnExecuteMs = executeMs,
            resolveMs = prepareMs + executeMs,
            totalMs,
            allocatedBytes,
            managedHeapBytes,
            gcGen0Collections = gc0Collections,
            gcGen1Collections = gc1Collections,
            gcGen2Collections = gc2Collections,
            preAgriculturalCamps,
            agriculturalCampsAfterTurn = CountAgriculturalCamps(world),
            population,
            priorWeightedQol,
            weightedQol,
            endingPopulationWeightedQol,
            foodYield,
            fingerprint,
            activeTiles = resolver.LastMetrics.ActiveTiles,
            ecologyMs = resolver.LastMetrics.EcologyMilliseconds,
            tileIntegrations = Metric("TileIntegrations"),
            seasonalTiles = Metric("SeasonalTiles"),
            fullTurnTiles = Metric("FullTurnTiles"),
            setupMs = Metric("SetupEconomyMilliseconds"),
            productionMs = Metric("ProductionMilliseconds"),
            fullTurnIntegrationMs = Metric("FullTurnIntegrationMilliseconds"),
            horizonPreparationMs = Metric("HorizonPreparationMilliseconds"),
            horizonCommitMs = Metric("HorizonCommitMilliseconds"),
            seasonalPreparationMs = Metric("SeasonalPreparationMilliseconds"),
            seasonalIntegrationMs = Metric("SeasonalIntegrationMilliseconds"),
            seasonalCommitMs = Metric("SeasonalCommitMilliseconds"),
            seasonalExpansionMs = Metric("SeasonalExpansionMilliseconds"),
            planCalls = Metric("PlanCalls"),
            forecastCalls = Metric("ForecastCalls"),
            sourceQueries = Metric("LocalSourceQueries"),
            sourceLookups = Metric("SourceLookups"),
            depositEdges = Metric("DepositEdges"),
            bodyScans = Metric("BodyScans"),
            claimsAllocated = Metric("ClaimsAllocated"),
            productionReceipts = Metric("ProductionReceipts"),
            campBoundaries = Metric("SeasonalCampBoundaries"),
            operatingChanges = Metric("OperatingRegimeChanges"),
            fieldSteps = Metric("FieldWorkSteps"),
            fieldSwitches = Metric("FieldChoiceSwitches"),
            targetCompletions = Metric("TargetCompletions"),
            toolExhaustions = Metric("ToolExhaustions"),
            stockDepletions = Metric("StockDepletions"),
            orders = details ? record.Orders.ToArray() : null,
            camps
        };
        samples.Add(row);
        Console.WriteLine("AUDIT turn=" + (world.Turn - 1) + " phase=" + row.phase + " preAg=" + preAgriculturalCamps +
            " population=" + population + " food=" + foodYield.ToString("F3") + " ai=" + aiMs.ToString("F1") +
            " prepare=" + prepareMs.ToString("F1") + " execute=" + executeMs.ToString("F1"));
    }
}
