using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private static int WorkerCount = 4;
    private static int PartitionSize = 64;
    private static bool ReversePartitions;
    private static bool StationaryHusbandry;
#if !MAIN
    // WP1 exactness: plan AI players in a draft (player 0 excluded) and prepare with it.
    private static bool DraftMode;
#endif
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

    private static void OverrideNeolithic(WorldState world, string field, string value, Func<string, object> parse)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var info = world.Config.Neolithic.GetType().GetField(field)
            ?? throw new ArgumentException("This simulation source has no Neolithic." + field + " to override.");
        info.SetValue(world.Config.Neolithic, parse(value));
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

#if !MAIN
    private sealed class LocalStockSnapshot
    {
        public double Biomass { get; set; }
        public double Gatherable { get; set; }
        public double Freshwater { get; set; }
    }

    private static LocalStockSnapshot LocalStocks(WorldState world, int campTile)
    {
        double biomass = 0, gatherable = 0, freshwater = 0;
        foreach (int id in world.Map.Radius(campTile, 1).OrderBy(id => id))
        {
            var stock = LazyEcology.ProjectEcology(world, id, world.Days);
            if (world.Tiles[id].Land)
            {
                biomass += stock.PotentialFood;
                gatherable += stock.Food;
            }
            freshwater += stock.Freshwater;
        }
        return new LocalStockSnapshot { Biomass = biomass, Gatherable = gatherable, Freshwater = freshwater };
    }
#endif

    private static TurnRecord ResolveTurn(WorldState world, PhaseExecution execution, TurnResolver resolver,
        out double aiMs, out double prepareMs, out double executeMs)
    {
        var clock = Stopwatch.StartNew();
        var ai = AiPlanning.Decide(world, execution);
#if !MAIN
        if (StationaryHusbandry)
        {
            var husbandry = new HashSet<int>(world.Camps.Where(CultivationEconomy.HusbandryEnabled).Select(c => c.Id));
            foreach (var orders in ai.Orders.Values)
                for (int i = 0; i < orders.Count; i++)
                    if (orders[i].Kind == OrderKind.Move && husbandry.Contains(orders[i].Entity))
                    {
                        // Keep any transient AI economy request attached to
                        // the action. This control changes placement alone.
                        var stay = orders[i].Copy();
                        stay.Kind = OrderKind.NoOp;
                        stay.Destination = -1;
                        orders[i] = stay;
                    }
        }
#endif
        aiMs = clock.Elapsed.TotalMilliseconds;

#if MAIN
        clock.Restart();
        var record = resolver.Resolve(world, ai.Orders, ai.Traces);
        prepareMs = 0;
        executeMs = clock.Elapsed.TotalMilliseconds;
        return record;
#else
        PlanningDraft draft = null;
        if (DraftMode)
        {
            // The draft plans every player except 0; player 0's orders and trace come
            // from the full decision, as a human's submitted orders would.
            draft = TurnPlanner.Draft(world, 0, execution);
            var orders = draft.Ai.Orders.ToDictionary(p => p.Key, p => p.Value);
            orders[0] = ai.Orders[0];
            var traces = draft.Ai.Traces.Where(t => t.Player != 0).Concat(ai.Traces.Where(t => t.Player == 0)).OrderBy(t => t.Player).ToList();
            ai = new PlanningResult { Orders = orders, Traces = traces };
        }
        clock.Restart();
        var prepared = TurnPlanner.Prepare(world, ai.Orders, ai.Traces, execution, draft);
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
        var execution = new PhaseExecution(WorkerCount, PartitionSize, ReversePartitions);
        var resolver = new TurnResolver(execution);
        for (int i = 0; i < 8 && !world.Finished; i++)
            ResolveTurn(world, execution, resolver, out _, out _, out _);
    }

    private static void Main(string[] args)
    {
        WorkerCount = Option(args, "--workers", 4);
        PartitionSize = Option(args, "--partition-size", 64);
        ReversePartitions = Option(args, "--reverse-partitions", 0) != 0;
        StationaryHusbandry = Option(args, "--stationary-husbandry", 0) != 0;
#if !MAIN
        DraftMode = Option(args, "--draft", 0) != 0;
        if (DraftMode && StationaryHusbandry) throw new ArgumentException("--draft cannot be combined with --stationary-husbandry.");
#endif
        if (WorkerCount < 1 || PartitionSize < 1)
            throw new ArgumentOutOfRangeException("Worker and partition counts must be positive.");
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
        int fingerprintMode = Option(args, "--fingerprints", 1);
        if (fingerprintMode < 0 || fingerprintMode > 2)
            throw new ArgumentOutOfRangeException("Fingerprint mode must be 0, 1, or 2.");
        bool fingerprintsEnabled = fingerprintMode != 0;
        bool perTurnFingerprints = fingerprintMode == 1;
        bool productionConfig = Option(args, "--production-config", 0) != 0;
        // Fission control (WP10a): 0 disables the pass (same-code baseline), 1 enables it; unset keeps the configured value.
        int fissionSwitch = Option(args, "--fission", -1);
        // Fission housing-relief weight (WP10c): unset keeps the configured value (1); 0 counts cohesion and clothing only.
        string fissionReliefText = Option(args, "--fission-housing-relief", "");
        // Top-level numeric configuration overrides for sensitivity runs: --set=Name=Value[,Name=Value] (for example SecondaryTechnologyThreshold=2000).
        string setText = Option(args, "--set", "");
        // Immigration control (WP10b): 0 disables the pass, 1 enables it; unset keeps the configured value (disabled).
        int immigrationSwitch = Option(args, "--immigration", -1);

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
            var json = new JsonSerializerOptions { IncludeFields = true };
            var config = productionConfig
                ? JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"), json)
                : new SimulationConfig();
            string landM = Option(args, "--land-m", ""), landSafe = Option(args, "--land-safe", ""), landDamage = Option(args, "--land-damage", "");
            string landAccessScale = Option(args, "--land-access-scale", "");
            string cultivationEffort = Option(args, "--cultivation-effort", "");
            string landRegeneration = Option(args, "--land-regeneration", ""), cultivationRegeneration = Option(args, "--cultivation-regeneration", "");
            if (landM.Length > 0) config.LandBiomassMultiplier = double.Parse(landM, CultureInfo.InvariantCulture);
            if (landAccessScale.Length > 0)
            {
                double scale = double.Parse(landAccessScale, CultureInfo.InvariantCulture);
                foreach (string name in new[] { "RichGatherableFraction", "GrasslandGatherableFraction",
                    "AridGatherableFraction", "TundraGatherableFraction" })
                {
                    var fractionField = typeof(SimulationConfig).GetField(name);
                    if (fractionField == null) throw new ArgumentException("This simulation source has no " + name + " override.");
                    fractionField.SetValue(config, (double)fractionField.GetValue(config) * scale);
                }
            }
            if (cultivationEffort.Length > 0)
            {
                // The audit harness also compiles against historical simulation
                // sources, which do not declare this experimental tuning field.
                object neolithic = typeof(SimulationConfig).GetField("Neolithic")?.GetValue(config);
                var effortField = neolithic?.GetType().GetField("CultivationMarginalEffortMultiplierPerTenth");
                if (effortField == null) throw new ArgumentException("This simulation source has no cultivation effort override.");
                effortField.SetValue(neolithic, double.Parse(cultivationEffort, CultureInfo.InvariantCulture));
            }
            if (landSafe.Length > 0) config.LandSafeUseRatio = double.Parse(landSafe, CultureInfo.InvariantCulture);
            if (landDamage.Length > 0) config.LandUseDamagePerYear = double.Parse(landDamage, CultureInfo.InvariantCulture);
            if (landRegeneration.Length > 0)
            {
                config.LandFoodRegeneration = double.Parse(landRegeneration, CultureInfo.InvariantCulture);
                config.LandFoodRegenerationInitialized = true;
            }
            if (cultivationRegeneration.Length > 0)
                config.CultivationRegenFlux = double.Parse(cultivationRegeneration, CultureInfo.InvariantCulture);
            var resources = productionConfig
                ? JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"), json)
                : null;
            config.Width = players == 320 ? 2048 : players == 80 ? 1024 : 96;
            config.Height = players == 320 ? 1024 : players == 80 ? 512 : 48;
            config.Players = players;
            config.BenchmarkScale = players == 320;
            config.Shape = (WorldShape)Enum.Parse(typeof(WorldShape), shapeName, true);
            world = new WorldGenerator().Generate(seed, config, resources);
            generationMs = generationClock.Elapsed.TotalMilliseconds;
            // The planner-rule overrides also apply to freshly generated worlds.
            OverrideNeolithic(world, "PlannerFulfilment", Option(args, "--fulfilment", ""), v => v);
            OverrideNeolithic(world, "ValueCurveSteepness", Option(args, "--value-steepness", ""), v => double.Parse(v, CultureInfo.InvariantCulture));
            OverrideNeolithic(world, "DiscountRatePerYear", Option(args, "--discount-rate", ""), v => double.Parse(v, CultureInfo.InvariantCulture));
            OverrideNeolithic(world, "FoodRiskWeight", Option(args, "--food-risk-weight", ""), v => double.Parse(v, CultureInfo.InvariantCulture));
            OverrideNeolithic(world, "PersonLifeYears", Option(args, "--person-life-years", ""), v => double.Parse(v, CultureInfo.InvariantCulture));
            world.Config.Validate();
        }
        else
        {
            byte[] inputBytes = File.ReadAllBytes(inputSave);
            inputSaveSha256 = HashBytes(inputBytes);
            world = SaveCodec.Load(inputBytes);
            // Planner-rule overrides on a loaded checkpoint (value-rule sweeps). Set by
            // reflection so the harness still builds against older simulation sources.
            OverrideNeolithic(world, "PlannerFulfilment", Option(args, "--fulfilment", ""), v => v);
            OverrideNeolithic(world, "ValueCurveSteepness", Option(args, "--value-steepness", ""), v => double.Parse(v, CultureInfo.InvariantCulture));
            OverrideNeolithic(world, "DiscountRatePerYear", Option(args, "--discount-rate", ""), v => double.Parse(v, CultureInfo.InvariantCulture));
            OverrideNeolithic(world, "FoodRiskWeight", Option(args, "--food-risk-weight", ""), v => double.Parse(v, CultureInfo.InvariantCulture));
            OverrideNeolithic(world, "PersonLifeYears", Option(args, "--person-life-years", ""), v => double.Parse(v, CultureInfo.InvariantCulture));
            world.Config.Validate();
            // Diagnostic priority experiment: set every camp's Housing crafting priority.
            string housingPriority = Option(args, "--housing-priority", "");
            if (!string.IsNullOrWhiteSpace(housingPriority))
                foreach (var camp in world.Camps) camp.CraftingPriorities.Housing = double.Parse(housingPriority, CultureInfo.InvariantCulture);
            generationMs = 0;
            players = world.Config.Players;
            seed = world.Seed;
            shapeName = world.Config.Shape.ToString();
        }

        if (fissionSwitch >= 0)
        {
            var fissionField = world.Config.GetType().GetField("FissionEnabled") ?? throw new ArgumentException("This simulation source has no fission switch.");
            fissionField.SetValue(world.Config, fissionSwitch != 0);
        }
        if (immigrationSwitch >= 0)
        {
            var immigrationField = world.Config.GetType().GetField("ImmigrationEnabled") ?? throw new ArgumentException("This simulation source has no immigration switch.");
            immigrationField.SetValue(world.Config, immigrationSwitch != 0);
        }
        if (!string.IsNullOrWhiteSpace(fissionReliefText))
        {
            var reliefField = world.Config.GetType().GetField("FissionHousingRelief") ?? throw new ArgumentException("This simulation source has no fission housing-relief lever.");
            reliefField.SetValue(world.Config, double.Parse(fissionReliefText, System.Globalization.CultureInfo.InvariantCulture));
        }
        foreach (string assignment in setText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = assignment.Split('=');
            var field = parts.Length == 2 ? world.Config.GetType().GetField(parts[0]) : null;
            object target = world.Config;
#if !MAIN
            // Names that are not top-level configuration fields fall back to the Neolithic calibration (for example CohesionBandScale).
            if (field == null && parts.Length == 2) { field = world.Config.Neolithic.GetType().GetField(parts[0]); target = world.Config.Neolithic; }
#endif
            if (field == null || field.FieldType != typeof(double) && field.FieldType != typeof(int)) throw new ArgumentException("Unsupported --set override: " + assignment);
            double value = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            field.SetValue(target, field.FieldType == typeof(int) ? (object)(int)value : value);
        }
        long managedHeapAfterLoadBytes = GC.GetTotalMemory(false);
        var detailedCounterSwitch = typeof(ResolutionMetrics).GetField("CaptureDetailedCounters");
        if (detailedCounterSwitch != null) detailedCounterSwitch.SetValue(null, metrics);

        var execution = new PhaseExecution(WorkerCount, PartitionSize, ReversePartitions);
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
            RunOneTurn(world, execution, resolver, samples, details, perTurnFingerprints, out string turnFingerprint);
            openingTurnsMeasured++;
            if (world.Finished)
            {
                finishedFingerprint = turnFingerprint ?? (fingerprintsEnabled ? SaveCodec.Fingerprint(world) : null);
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
            RunOneTurn(world, execution, resolver, samples, details, perTurnFingerprints, out _);
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
            reversePartitions = ReversePartitions,
            stationaryHusbandry = StationaryHusbandry,
            warmupRuns = warmup,
            metricsEnabled = metrics,
            detailsEnabled = details,
            productionConfig,
            fissionSwitch,
            immigrationSwitch,
            fissionHousingRelief = string.IsNullOrWhiteSpace(fissionReliefText) ? (double?)null : double.Parse(fissionReliefText, System.Globalization.CultureInfo.InvariantCulture),
            fingerprintsEnabled,
            replayMode = fingerprintMode == 1 ? "exact-per-turn" : fingerprintMode == 2 ? "exact-final" : "outcomes-only",
            exactCheckMode = fingerprintMode == 1 ? "per-turn-fingerprint" : fingerprintMode == 2 ? "final-fingerprint" : "disabled; compare outcomes against replay-check run",
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
#if !MAIN
        var priorSites = details ? world.Camps.ToDictionary(c => c.Id, c => c.Tile) : null;
        var priorMoves = details ? world.Camps.ToDictionary(c => c.Id, c => c.Moves) : null;
        var priorLocalStocks = details ? world.Camps.ToDictionary(c => c.Id, c => LocalStocks(world, c.Tile)) : null;
#endif
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
        int managedAnimals = 0, wildAnimals = 0, managedHerds = 0;
        int husbandryCamps = 0, husbandryResidents = 0, husbandryLinked = 0, husbandryAnimals = 0;
        int husbandryMoves = 0, animalBirths = 0, animalDeaths = 0;
        double herdForageRequested = 0, herdForageRealized = 0, herdWaterRequested = 0, herdWaterRealized = 0;
        int landConditionChangedTiles = 0;
        double maximumLandConditionDelta = 0;
        object camps = null;
        object domesticHerds = null;
#else
        double endingPopulationWeightedQol = residentPopulation <= 0 ? 0 : world.Camps.Sum(c => c.QoL * c.Population) / residentPopulation;
        double priorResidentPopulation = priorResidents.Values.Sum(v => (double)v);
        double priorWeightedQol = priorResidentPopulation <= 0 ? 0 :
            world.Camps.Sum(c => c.QoL * (priorResidents.TryGetValue(c.Id, out int prior) ? prior : 0)) / priorResidentPopulation;
        var domesticAnimalsByCamp = world.Herds.Where(h => h.DomesticCamp >= 0)
            .GroupBy(h => h.DomesticCamp).ToDictionary(g => g.Key, g => g.Sum(h => h.Animals));
        int managedAnimals = domesticAnimalsByCamp.Values.Sum();
        int managedHerds = world.Herds.Count(h => h.DomesticCamp >= 0);
        int wildAnimals = world.Herds.Where(h => h.DomesticCamp < 0).Sum(h => h.Animals);
        var husbandry = world.Camps.Where(CultivationEconomy.HusbandryEnabled).ToArray();
        int husbandryCamps = husbandry.Length;
        int husbandryResidents = husbandry.Sum(c => c.Population);
        int husbandryLinked = husbandry.Sum(world.LinkedPopulation);
        int husbandryAnimals = husbandry.Sum(c => domesticAnimalsByCamp.TryGetValue(c.Id, out int animals) ? animals : 0);
        int husbandryMoves = details ? husbandry.Sum(c => c.Moves - (priorMoves.TryGetValue(c.Id, out int priorMove) ? priorMove : c.Moves)) : 0;
        int animalBirths = record.ManagedHerds.Sum(h => h.NewbornAnimals);
        int animalDeaths = record.ManagedHerds.Sum(h => h.StarvationDeaths);
        var domesticHerdIds = new HashSet<int>(record.ManagedHerds.Select(h => h.Herd));
        double herdForageRequested = record.Pressure.Where(p => domesticHerdIds.Contains(p.Entity) && p.Kind == "Herd" && p.Pool == "Biomass").Sum(p => p.Requested);
        double herdForageRealized = record.Pressure.Where(p => domesticHerdIds.Contains(p.Entity) && p.Kind == "Herd" && p.Pool == "Biomass").Sum(p => p.Realized);
        double herdWaterRequested = record.Pressure.Where(p => domesticHerdIds.Contains(p.Entity) && p.Kind == "Herd" && p.Pool == "Freshwater").Sum(p => p.Requested);
        double herdWaterRealized = record.Pressure.Where(p => domesticHerdIds.Contains(p.Entity) && p.Kind == "Herd" && p.Pool == "Freshwater").Sum(p => p.Realized);
        var landConditionDeltas = record.Changes.Where(change => world.Tiles[change.Tile].Land)
            .Select(change => Math.Abs(change.AfterLandCondition - change.BeforeLandCondition)).ToArray();
        int landConditionChangedTiles = landConditionDeltas.Count(delta => delta > 1e-12);
        double maximumLandConditionDelta = landConditionDeltas.Length == 0 ? 0 : landConditionDeltas.Max();
        var camps = details ? world.Camps.OrderBy(c => c.Id).Select(c =>
        {
            var fieldIntensity = CultivationEconomy.Footprint(world, c)
                .Where(id => world.Tiles[id].Land)
                .Select(id => CultivationEconomy.Intensity(world.Config, world.Tiles[id].CultivationCapital))
                .ToArray();
            return new
            {
                c.Id, c.Player, c.Population, c.IsVillage, priorResidents = priorResidents.TryGetValue(c.Id, out int priorPopulation) ? priorPopulation : 0,
                startingSite = priorSites.TryGetValue(c.Id, out int priorSite) ? priorSite : c.Tile, endingSite = c.Tile,
                moved = priorMoves.TryGetValue(c.Id, out int priorMoveCount) && c.Moves != priorMoveCount,
                openingLocalStocks = priorLocalStocks.TryGetValue(c.Id, out var priorStocks) ? priorStocks : null,
                endingLocalStocks = LocalStocks(world, c.Tile),
                linked = world.LinkedPopulation(c), technology = c.Technology.ToString(), c.QoL,
                holdsCultivation = CultivationEconomy.Enabled(c), holdsHusbandry = CultivationEconomy.HusbandryEnabled(c), holdsWatercraft = c.Technologies[(int)Technology.Watercraft], totalMoves = c.Moves,
                c.FoodNeedsMetRatio, c.WaterNeedsMetRatio, c.ClothPerCapita, c.HousingPerCapita,
                fixedShelterPeople = NeolithicProduction.HousingCapacity(world, c, world.Days).Fixed, tentShelterPeople = NeolithicProduction.HousingCapacity(world, c, world.Days).Light,
                housingShare = NeolithicProduction.HousingShare(world.Config.Neolithic, NeolithicProduction.HousingCapacity(world, c, world.Days), c.Population),
                c.Food, c.PlantFood, c.AnimalFood, c.FabricConsumed, c.HousingDemand,
                c.GrowthRemainder, c.LastConsumed, c.FoodDeficit, c.DeficitTurns,
                c.Inventory.Cloth, c.Inventory.AnimalFabric, c.Inventory.Wood, c.Inventory.Clay,
                c.Inventory.HandTools, c.Inventory.Pots, c.Inventory.Stone, c.Inventory.FarmTools,
                assignments = c.Workforce.Assignments.OrderBy(a => a.Role).Select(a => new { a.Role, a.Population }).ToArray(),
                equipmentTarget = c.EconomicLabor?.EquipmentTarget ?? 0,
                production = record.Production.Where(p => p.Camp == c.Id).GroupBy(p => p.Good).ToDictionary(g => g.Key.ToString(), g => g.Sum(p => p.Output)),
                foodYield = record.Transfers.Where(t => t.Entity == c.Id && t.Kind != "Herd").Sum(t => t.Food),
                managedAnimals = domesticAnimalsByCamp.TryGetValue(c.Id, out int animals) ? animals : 0,
                meanFieldQ = fieldIntensity.Length == 0 ? 0 : fieldIntensity.Average(),
                maxFieldQ = fieldIntensity.Length == 0 ? 0 : fieldIntensity.Max()
            };
        }).ToArray() : null;
        var domesticHerds = details ? world.Herds.Where(h => h.DomesticCamp >= 0)
            .OrderBy(h => h.DomesticCamp).ThenBy(h => h.Id)
            .Select(h => new
            {
                h.Id, h.DomesticCamp, h.Species, h.Animals,
                h.LastFoodNutrition, h.LastWaterNutrition, h.NewbornAnimals,
                h.StarvationDeaths
            }).ToArray() : null;
#endif
        // Preserve schema-1 semantics: weightedQol has always meant the prior cohort.
        double weightedQol = priorWeightedQol;
        double foodYield = record.Transfers.Where(t => t.Kind != "Herd").Sum(t => t.Food);
        int population = world.Camps.Sum(world.LinkedPopulation);
#if MAIN
        int campCount = world.Camps.Count, societyCount = world.Societies.Count, playerCount = world.Camps.Select(c => c.Player).Distinct().Count(), fissionCount = 0;
        object fissionEvents = null; object migrationEvents = null; int migrationCount = 0, migrantCount = 0, kinMigrants = 0;
#else
        // Fission (WP10a): settlement, Society and player-slot counts over time, and this turn's fission events.
        int campCount = world.Camps.Count, societyCount = world.Societies.Count, playerCount = world.LivePlayers;
        var turnFissions = record.Fissions ?? new List<FissionReport>();
        int fissionCount = turnFissions.Count;
        object fissionEvents = turnFissions.Select(f => new { f.ParentCamp, f.DaughterCamp, f.ParentSociety, f.DaughterSociety, f.DaughterPlayer, f.Tile, f.ParentBefore, f.Daughter,
            regime = ((FissionRegime)f.Regime).ToString(), policy = f.Policy.ToString(), f.Push, f.Desirability, f.Score, f.Affinity, f.AnimalsMoved, f.Gain, f.DaughterQoL }).ToArray();
        // Immigration (WP10b): this turn's committed flows, canonical order.
        var turnMigrations = record.Migrations ?? new List<MigrationReport>();
        int migrationCount = turnMigrations.Count, migrantCount = turnMigrations.Sum(m => m.Migrants), kinMigrants = turnMigrations.Where(m => m.Kin).Sum(m => m.Migrants);
        object migrationEvents = turnMigrations.Select(m => new { m.SourceCamp, m.DestinationCamp, m.SourceSociety, m.DestinationSociety, m.SourceBefore, m.DestinationBefore, m.Intended, m.Migrants, m.Distance, m.Kin,
            m.SourceQoL, m.DestinationQoL, m.Utility, m.PostInflowQoL, m.Plant, m.Animal, m.Aquatic }).ToArray();
#endif
        object KnowledgeFootprint(WorldState w)
        {
            var measure = typeof(WorldState).Assembly.GetType("Anthropocene.Simulation.ContactKnowledge")?.GetMethod("Measure");
            if (measure == null) return null;
            var f = measure.Invoke(null, new object[] { w }); var t = f.GetType();
            return new { societies = t.GetField("Societies").GetValue(f), blocks = t.GetField("Blocks").GetValue(f), tiles = t.GetField("Tiles").GetValue(f), contacts = t.GetField("Contacts").GetValue(f), bytes = t.GetField("Bytes").GetValue(f) };
        }
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
            campCount,
            societyCount,
            playerCount,
            fissionCount,
            fissionEvents,
            fissionMs = Metric("FissionMilliseconds"),
            fissionCampsEvaluated = Metric("FissionCampsEvaluated"),
            fissionValidations = Metric("FissionValidations"),
            fissionSearches = Metric("FissionSearches"),
            fissionCandidates = Metric("FissionCandidatesScored"),
            fissionTileProjections = Metric("FissionTileProjections"),
            fissionInvalidations = Metric("FissionCacheInvalidations"),
            fissionProposals = Metric("FissionProposals"),
            fissionDropped = Metric("FissionDropped"),
            fissionDaughterChecks = Metric("FissionDaughterChecks"),
            fissionDaughterBlocked = Metric("FissionDaughterBlocked"),
            migrationCount,
            migrantCount,
            kinMigrants,
            migrationEvents,
            immigrationMs = Metric("ImmigrationMilliseconds"),
            immigrationCampsEvaluated = Metric("ImmigrationCampsEvaluated"),
            immigrationCandidates = Metric("ImmigrationCandidatesScored"),
            immigrationIntents = Metric("ImmigrationIntents"),
            immigrationAccepted = Metric("ImmigrationAccepted"),
            immigrationPartial = Metric("ImmigrationPartial"),
            immigrationRejected = Metric("ImmigrationRejected"),
            managedAnimals,
            wildAnimals,
            managedHerds,
            husbandryCamps,
            husbandryResidents,
            husbandryLinked,
            husbandryAnimals,
            husbandryMoves,
            animalBirths,
            animalDeaths,
            humanBirths = record.Growth,
            humanDeaths = record.Deaths,
            herdForageRequested,
            herdForageRealized,
            herdWaterRequested,
            herdWaterRealized,
            landConditionChangedTiles,
            maximumLandConditionDelta,
            priorWeightedQol,
            weightedQol,
            endingPopulationWeightedQol,
            foodYield,
            foodByKind = details ? record.Transfers.GroupBy(t => t.Kind)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Food)) : null,
            fingerprint,
            activeTiles = resolver.LastMetrics.ActiveTiles,
            ecologyMs = resolver.LastMetrics.EcologyMilliseconds,
            // Contact knowledge (WP6): update cost and remembered footprint. Reflection keeps the harness buildable against older source trees.
            knowledgeMs = Metric("KnowledgeMilliseconds"),
            knowledge = KnowledgeFootprint(world),
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
            foodForecastQueries = Metric("FoodForecastQueries"),
            foodInvestmentComparisons = Metric("FoodInvestmentComparisons"),
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
            camps,
            domesticHerds
        };
        samples.Add(row);
        Console.WriteLine("AUDIT turn=" + (world.Turn - 1) + " phase=" + row.phase + " preAg=" + preAgriculturalCamps +
            " camps=" + campCount + " societies=" + societyCount + " players=" + playerCount + " fissions=" + fissionCount + " population=" + population + " food=" + foodYield.ToString("F3") + " ai=" + aiMs.ToString("F1") +
            " prepare=" + prepareMs.ToString("F1") + " execute=" + executeMs.ToString("F1"));
    }
}
