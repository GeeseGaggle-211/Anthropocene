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
            world.Camps.Sum(c => c.QoL * priorResidents[c.Id]) / priorResidentPopulation;
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
        int husbandryMoves = details ? husbandry.Sum(c => c.Moves - priorMoves[c.Id]) : 0;
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
                c.Id, c.Player, c.Population, c.IsVillage, priorResidents = priorResidents[c.Id],
                startingSite = priorSites[c.Id], endingSite = c.Tile,
                moved = c.Moves != priorMoves[c.Id],
                openingLocalStocks = priorLocalStocks[c.Id],
                endingLocalStocks = LocalStocks(world, c.Tile),
                linked = world.LinkedPopulation(c), technology = c.Technology.ToString(), c.QoL,
                c.FoodNeedsMetRatio, c.WaterNeedsMetRatio, c.ClothPerCapita, c.HousingPerCapita,
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
            " population=" + population + " food=" + foodYield.ToString("F3") + " ai=" + aiMs.ToString("F1") +
            " prepare=" + prepareMs.ToString("F1") + " execute=" + executeMs.ToString("F1"));
    }
}
