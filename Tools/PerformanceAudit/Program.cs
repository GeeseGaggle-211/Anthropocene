using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
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
    private static void Warmup()
    {
        var world = new WorldGenerator().Generate(1, new SimulationConfig { Width = 96, Height = 48, Players = 4 });
        var execution = new PhaseExecution(4, 64);
        var resolver = new TurnResolver(execution);
        for (int i = 0; i < 8 && !world.Finished; i++)
        {
            var ai = AiPlanning.Decide(world, execution);
            resolver.Resolve(world, ai.Orders, ai.Traces);
        }
    }
    private static void Main(string[] args)
    {
#if MAIN
        if (Option(args, "--warmup", 0) != 0) Warmup();
        int players = Option(args, "--players", 80), turns = Option(args, "--turns", 12), seed = Option(args, "--seed", 1729);
        string output = Option(args, "--output", ""), shape = Option(args, "--shape", "Continents");
        var config = new SimulationConfig
        {
            Width = players == 320 ? 2048 : players == 80 ? 1024 : 96,
            Height = players == 320 ? 1024 : players == 80 ? 512 : 48,
            Players = players, BenchmarkScale = players == 320, Shape = (WorldShape)Enum.Parse(typeof(WorldShape), shape, true)
        };
        var clock = Stopwatch.StartNew();
        var world = new WorldGenerator().Generate(seed, config);
        double generationMs = clock.Elapsed.TotalMilliseconds;
        var execution = new PhaseExecution(4, 64);
        var resolver = new TurnResolver(execution);
        var samples = new List<object>();
        for (int i = 0; i < turns && !world.Finished; i++)
        {
            var priorResidents = world.Camps.ToDictionary(c => c.Id, c => c.Population);
            clock.Restart(); var ai = AiPlanning.Decide(world, execution); double aiMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart(); var record = resolver.Resolve(world, ai.Orders, ai.Traces); double resolveMs = clock.Elapsed.TotalMilliseconds;
            double foodYield = record.Transfers.Where(t => t.Kind != "Herd").Sum(t => t.Food);
            int population = world.Camps.Sum(world.LinkedPopulation);
            double priorResidentPopulation = priorResidents.Values.Sum(v => (double)v);
            var qolField = typeof(Camp).GetField("QoL");
            double weightedQol = priorResidentPopulation <= 0 || qolField == null ? 0 :
                world.Camps.Sum(c => Convert.ToDouble(qolField.GetValue(c)) * priorResidents[c.Id]) / priorResidentPopulation;
            var row = new { turn = world.Turn - 1, aiMs, resolveMs, totalMs = aiMs + resolveMs,
                population, weightedQol, foodYield, activeTiles = resolver.LastMetrics.ActiveTiles, ecologyMs = resolver.LastMetrics.EcologyMilliseconds };
            samples.Add(row);
            Console.WriteLine("AUDIT turn=" + row.turn + " population=" + population + " food=" + foodYield.ToString("F3") + " aiMs=" + aiMs.ToString("F1") + " resolveMs=" + resolveMs.ToString("F1"));
        }
        var result = new { seed, players, shape, turns = samples.Count, generationMs, samples };
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
#else
        if (Option(args, "--warmup", 0) != 0) Warmup();
        int players = Option(args, "--players", 80), turns = Option(args, "--turns", 12), seed = Option(args, "--seed", 1729);
        string output = Option(args, "--output", "");
        string shape = Option(args, "--shape", "Continents");
        bool details = Option(args, "--details", 0) != 0;
        // The main-branch build predates this switch. Reflection keeps one
        // harness source compatible with both simulation assemblies.
        var detailedCounterSwitch = typeof(ResolutionMetrics).GetField("CaptureDetailedCounters");
        if (detailedCounterSwitch != null) detailedCounterSwitch.SetValue(null, Option(args, "--metrics", 0) != 0);
        var config = new SimulationConfig
        {
            Width = players == 320 ? 2048 : players == 80 ? 1024 : 96,
            Height = players == 320 ? 1024 : players == 80 ? 512 : 48,
            Players = players, BenchmarkScale = players == 320, Shape = (WorldShape)Enum.Parse(typeof(WorldShape), shape, true)
        };
        var clock = Stopwatch.StartNew();
        var world = new WorldGenerator().Generate(seed, config);
        double generationMs = clock.Elapsed.TotalMilliseconds;
        var execution = new PhaseExecution(4, 64);
        var resolver = new TurnResolver(execution);
        var samples = new List<object>();
        for (int i = 0; i < turns && !world.Finished; i++)
        {
            var priorResidents = world.Camps.ToDictionary(c => c.Id, c => c.Population);
            long allocatedBefore = GC.GetTotalAllocatedBytes(false);
            clock.Restart(); var ai = AiPlanning.Decide(world, execution); double aiMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart(); var prepared = TurnPlanner.Prepare(world, ai.Orders, ai.Traces); double turnPrepareMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart(); var record = resolver.Resolve(world, prepared); double turnExecuteMs = clock.Elapsed.TotalMilliseconds;
            double resolveMs = turnPrepareMs + turnExecuteMs;
            long allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocatedBefore;
            double residentPopulation = world.Camps.Sum(c => (double)c.Population);
            double endingPopulationWeightedQol = residentPopulation <= 0 ? 0 : world.Camps.Sum(c => c.QoL * c.Population) / residentPopulation;
            double priorResidentPopulation = priorResidents.Values.Sum(v => (double)v);
            double weightedQol = priorResidentPopulation <= 0 ? 0 : world.Camps.Sum(c => c.QoL * priorResidents[c.Id]) / priorResidentPopulation;
            double foodYield = record.Transfers.Where(t => t.Kind != "Herd").Sum(t => t.Food);
            int population = world.Camps.Sum(world.LinkedPopulation);
            double Metric(string name) => Convert.ToDouble(resolver.LastMetrics.GetType().GetField(name)?.GetValue(resolver.LastMetrics) ?? 0);
            var camps = details ? world.Camps.OrderBy(c => c.Id).Select(c => new
            {
                c.Id, c.Player, c.Population, linked = world.LinkedPopulation(c), c.QoL,
                c.FoodNeedsMetRatio, c.WaterNeedsMetRatio, c.ClothPerCapita, c.HousingPerCapita,
                c.Food, c.PlantFood, c.AnimalFood, c.FabricConsumed, c.HousingDemand,
                c.GrowthRemainder, c.LastConsumed, c.FoodDeficit, c.DeficitTurns,
                c.Inventory.Cloth, c.Inventory.AnimalFabric, c.Inventory.Wood, c.Inventory.Clay,
                c.Inventory.HandTools, c.Inventory.Pots,
                c.Inventory.Stone, c.Inventory.FarmTools,
                assignments = c.Workforce.Assignments.OrderBy(a => a.Role).Select(a => new { a.Role, a.Population }).ToArray(),
                equipmentTarget = c.EconomicLabor?.EquipmentTarget ?? 0,
                production = record.Production.Where(p => p.Camp == c.Id).GroupBy(p => p.Good).ToDictionary(g => g.Key.ToString(), g => g.Sum(p => p.Output)),
                foodYield = record.Transfers.Where(t => t.Entity == c.Id && t.Kind != "Herd").Sum(t => t.Food)
            }).ToArray() : null;
            var row = new { turn = world.Turn - 1, aiMs, resolveMs, turnPrepareMs, turnExecuteMs, totalMs = aiMs + resolveMs,
                population, weightedQol, endingPopulationWeightedQol, foodYield, allocatedBytes, agriculturalCamps = world.Camps.Count(CultivationEconomy.Enabled),
                activeTiles = resolver.LastMetrics.ActiveTiles, ecologyMs = resolver.LastMetrics.EcologyMilliseconds,
                tileIntegrations = Metric("TileIntegrations"), seasonalTiles = Metric("SeasonalTiles"), fullTurnTiles = Metric("FullTurnTiles"),
                setupMs = Metric("SetupEconomyMilliseconds"), productionMs = Metric("ProductionMilliseconds"),
                fullTurnIntegrationMs = Metric("FullTurnIntegrationMilliseconds"), seasonalPreparationMs = Metric("SeasonalPreparationMilliseconds"),
                seasonalIntegrationMs = Metric("SeasonalIntegrationMilliseconds"), seasonalCommitMs = Metric("SeasonalCommitMilliseconds"),
                seasonalExpansionMs = Metric("SeasonalExpansionMilliseconds"),
                planCalls = Metric("PlanCalls"), forecastCalls = Metric("ForecastCalls"), sourceQueries = Metric("LocalSourceQueries"),
                sourceLookups = Metric("SourceLookups"), depositEdges = Metric("DepositEdges"), bodyScans = Metric("BodyScans"),
                claimsAllocated = Metric("ClaimsAllocated"), productionReceipts = Metric("ProductionReceipts"),
                campBoundaries = Metric("SeasonalCampBoundaries"), operatingChanges = Metric("OperatingRegimeChanges"),
                fieldSteps = Metric("FieldWorkSteps"), fieldSwitches = Metric("FieldChoiceSwitches"),
                targetCompletions = Metric("TargetCompletions"), toolExhaustions = Metric("ToolExhaustions"),
                stockDepletions = Metric("StockDepletions"),
                orders = details ? record.Orders.ToArray() : null, camps };
            samples.Add(row);
            Console.WriteLine("AUDIT turn=" + row.turn + " population=" + population + " qol=" + weightedQol.ToString("F6") + " food=" + foodYield.ToString("F3") + " aiMs=" + aiMs.ToString("F1") + " resolveMs=" + resolveMs.ToString("F1"));
        }
        var result = new { seed, players, shape, turns = samples.Count, generationMs, samples };
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
#endif
    }
}
