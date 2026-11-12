using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using Anthropocene.Simulation;

// Read-only capture of real committed headless turns. No gameplay rule overrides.
internal static class Gameplay
{
    private static readonly JsonSerializerOptions J = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static object Camps(WorldState w) => w.Camps.OrderBy(c => c.Id).Select(c => new {
        id = c.Id, player = c.Player, tile = c.Tile, population = c.Population, village = c.IsVillage,
        food = c.Food, tools = c.Inventory.FarmTools, materials = c.Inventory.HousingMaterial,
        plough = AgriculturalTechnology.Plough(w, c), irrigation = c.People.HasTech(NeolithicTech.Irrigation),
        farmers = AgriculturalTechnology.OperatingFarmers(c), builders = CultivationEconomy.Workers(c, WorkforceRoles.Builder),
        status = IrrigationEconomy.Status(w, c), fertileGate = IrrigationEconomy.Enabled(w) && IrrigationEconomy.FertileGate(w, c),
        fertile = CultivationEconomy.Footprint(w, c).Where(id => w.Tiles[id].Seasons.Average(s => s.Moisture) >= (w.Config.Agriculture?.FertileMoisture ?? .5) &&
            (w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == c.Id)).Select(id => new {
                tile = id, q = CultivationEconomy.Intensity(w.Config, LazyEcology.ProjectEcology(w, id, w.Days).CultivationCapital) }).ToArray()
    }).ToArray();
    private static object Snapshot(WorldState w, TurnRecord r, object decisions, double milliseconds = 0)
    {
        return new {
            turn = w.Turn - 1, days = w.Days, year = w.Days / w.Config.DaysPerYear,
            duration = r?.Duration / w.Config.DaysPerYear ?? 0, camps = Camps(w),
            milliseconds, births = r?.Growth ?? 0, deaths = r?.Deaths ?? 0,
            calendarYear = w.Config.SmoothCalendar?.Started == true ? w.Config.SmoothCalendar.NeolithicStartYear + (w.Days - w.Config.SmoothCalendar.EpochDays) / w.Config.DaysPerYear : (double?)null,
            freshwater = r?.Transfers.Sum(t => t.Freshwater) ?? 0,
            population = w.Camps.Sum(c => c.Population) + w.Hunters.Sum(h => h.Population),
            food = w.Camps.Sum(c => c.Food), tools = w.Camps.Sum(c => c.Inventory.FarmTools),
            materials = w.Camps.Sum(c => c.Inventory.HousingMaterial), decisions,
            fields = w.Tiles.Where(t => t.CultivationCapital > 0 && t.CultivationOperator >= 0).Select(t => new {
                tile = t.Id, owner = t.CultivationOperator, q = CultivationEconomy.Intensity(w.Config, LazyEcology.ProjectEcology(w, t.Id, w.Days).CultivationCapital)
            }).ToArray(),
            canals = (w.Canals ?? new List<CanalSegment>()).Select(s => new { id = s.Id, owner = s.Operator, source = s.Source,
                from = s.From, tile = s.Tile, progress = s.Construction, funded = s.FundedYears, activeDays = s.ActiveDays }).ToArray(),
            irrigationFields = (w.IrrigatedFields ?? new List<IrrigatedField>()).Select(f => new {
                tile = f.Tile, owner = f.Operator, soil = f.Soil, water = f.WaterService, area = f.Area }).ToArray(),
            events = r?.Events.ToArray() ?? Array.Empty<string>(),
            irrigationWater = r?.IrrigationWater?.ToArray() ?? Array.Empty<IrrigationWaterReceipt>(),
            production = r?.Production.Select(p => new { camp = p.Camp, good = p.Good.ToString(), output = p.Output,
                materials = p.HousingMaterial, workerYears = p.WorkerIntervals * w.Config.BaseDays / w.Config.DaysPerYear,
                toolsConsumed = p.HandToolsConsumed + p.ToolsConsumed }).ToArray(),
            foodReceipts = r?.AgriculturalFood?.ToArray() ?? Array.Empty<AgriculturalFoodReceipt>()
        };
    }
    internal static void Run(string[] args)
    {
        string Arg(string name, string fallback) => args.FirstOrDefault(a => a.StartsWith(name + "="))?.Substring(name.Length + 1) ?? fallback;
        int seed = int.Parse(Arg("--seed", "23")); bool natural = args.Contains("--natural");
        bool standard = args.Contains("--standard");
        double years = double.Parse(Arg("--years", natural ? "1400" : "600"), System.Globalization.CultureInfo.InvariantCulture);
        string output = Arg("--output", "TestResults/AgriculturalGameplay"); Directory.CreateDirectory(output);
        WorldState original;
        if (args.Any(a => a.StartsWith("--opening="))) original = SaveCodec.Load(File.ReadAllBytes(Arg("--opening", "")));
        else if (natural)
        {
            var config = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/AgriculturalTechnologyOpeningConfig.json"), J);
            if (standard) { config.Width = 1024; config.Height = 512; config.Players = 80; config.StartSeparation = 12; config.Shape = WorldShape.Continents; }
            else { config.Width = 96; config.Height = 48; config.Players = 4; config.StartSeparation = 9; }
            var resources = standard ? JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"), J) : null;
            original = new WorldGenerator().Generate(seed, config, resources: resources);
        }
        else original = SaveCodec.Load(File.ReadAllBytes($"TestResults/AgriculturalTechnology/seed-{seed}.anthro"));
        var checkpoint = SaveCodec.Save(original);
        File.WriteAllBytes(Path.Combine(output, "opening.anthro"), checkpoint);
        string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var sources = Directory.GetFiles("Assets/_Anthropocene/Simulation", "*.cs").OrderBy(x => x).ToDictionary(x => x, x => Hash(File.ReadAllBytes(x)));
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new { seed, standard, years, config = original.Config,
            openingSha256 = Hash(checkpoint), calendarSha256 = Hash(File.ReadAllBytes("Assets/_Anthropocene/Simulation/SmoothCalendar.cs")), sources }, J));
        var runs = new List<object>();
        foreach (string treatment in natural ? new[] { "natural" } : new[] { "combined-soil", "control" })
        {
            var w = SaveCodec.Load(checkpoint);
            if (treatment == "control") { w.Config.Agriculture.PloughEnabled = false; w.Config.Agriculture.IrrigationEnabled = false; }
            var frames = new List<object> { Snapshot(w, null, Array.Empty<object>()) };
            using var journal = new StreamWriter(Path.Combine(output, treatment + "-frames.jsonl"));
            var compact = new JsonSerializerOptions { IncludeFields = true };
            journal.WriteLine(JsonSerializer.Serialize(frames[0], compact)); journal.Flush();
            double start = w.Days; int frame = 0;
            ResolutionMetrics.CaptureDemandAllocations = true;
            var durations = new List<double>(); var seasons = new double[4];
            while (w.Days < start + years * w.Config.DaysPerYear)
            {
                var timer = Stopwatch.StartNew();
                var ai = AiPlanning.Decide(w);
                var prepared = TurnPlanner.Prepare(w, ai.Orders, ai.Traces);
                var decisions = prepared.DemandAllocations.OrderBy(p => p.Key).Select(p => new {
                    camp = p.Key, developmentFoodGap = p.Value.DevelopmentFoodGap,
                    requests = p.Value.Requests.Where(r => r.Good == CraftGood.CanalConstruction || r.Good == CraftGood.CanalMaintenance)
                        .Select(r => new { service = r.Service, wanted = r.Units, granted = r.Granted, unmet = r.Unmet }).ToArray()
                }).ToArray();
                var record = new TurnResolver().Resolve(w, prepared);
                timer.Stop(); durations.Add(record.Duration);
                var exposure = new SeasonExposure(record.StartDays, record.Duration, w.Config.BaseDays);
                for (int s = 0; s < 4; s++) seasons[s] += exposure.At(s);
                var snapshot = Snapshot(w, record, decisions, timer.Elapsed.TotalMilliseconds);
                frames.Add(snapshot); frame++;
                journal.WriteLine(JsonSerializer.Serialize(snapshot, compact)); journal.Flush();
                double made = record.Production.Where(p => p.Good == CraftGood.CanalConstruction).Sum(p => p.Output);
                double water = record.IrrigationWater?.Sum(r => r.Delivery) ?? 0;
                Console.WriteLine($"{treatment,-14} turn={w.Turn - 1,3} year={w.Days / w.Config.DaysPerYear,8:F2} dt={record.Duration / w.Config.DaysPerYear,5:F2} population={w.Camps.Sum(c => c.Population),6} canals={w.Canals?.Count ?? 0,2} built={made,6:F3} water={water,8:F1} ms={timer.Elapsed.TotalMilliseconds:F0}");
                foreach (string e in record.Events.Where(e => e.Contains("attained Plough") || e.Contains("attained Irrigation"))) Console.WriteLine("  " + e);
            }
            var final = SaveCodec.Save(w);
            File.WriteAllBytes(Path.Combine(output, treatment + "-final.anthro"), final);
            string fingerprint = SaveCodec.Fingerprint(w);
            string validationFailure = null; bool saveLoadPassed = false, continuationVerified = false;
            try
            {
                var reloaded = SaveCodec.Load(final);
                if (fingerprint != SaveCodec.Fingerprint(reloaded)) throw new InvalidDataException("Gameplay save/load mismatch");
                saveLoadPassed = true;
                if (args.Contains("--verify-continuation"))
                {
                    foreach (var state in new[] { w, reloaded })
                    { var ai = AiPlanning.Decide(state); new TurnResolver().Resolve(state, TurnPlanner.Prepare(state, ai.Orders, ai.Traces)); }
                    if (SaveCodec.Fingerprint(w) != SaveCodec.Fingerprint(reloaded)) throw new InvalidDataException("Gameplay continuation mismatch");
                    continuationVerified = true;
                    Console.WriteLine("PASS: exact save/load next-turn continuation");
                }
            }
            catch (InvalidDataException error) { validationFailure = error.Message; Console.WriteLine("VALIDATION FAILURE: " + error.Message); }
            File.WriteAllText(Path.Combine(output, treatment + "-timing.json"), JsonSerializer.Serialize(new { durations, seasonExposure = seasons,
                calendar = w.Config.SmoothCalendar, finalSaveSha256 = Hash(final), finalFingerprint = fingerprint,
                saveLoadPassed, continuationVerified, validationFailure }, J));
            runs.Add(new { name = treatment, frames, finalFingerprint = fingerprint });
        }
        ResolutionMetrics.CaptureDemandAllocations = false;
        var replay = new { seed, natural, years, startDays = original.Days, width = original.Config.Width, height = original.Config.Height,
            scope = natural ? "Fresh generated world; normal AI and Elder; organic discovery; no knowledge grants." :
                "Matched mature saves from the approved sweep; technologies were granted identically at its checkpoint. No construction commands or resource grants. Control disables both effects.",
            geography = original.Tiles.Select(t => new { id = t.Id, terrain = t.Terrain.ToString(), elevation = t.Elevation,
                river = t.River, source = t.FreshwaterSource.ToString(), moisture = t.Seasons.Average(s => s.Moisture) }).ToArray(), runs };
        File.WriteAllText(Path.Combine(output, "replay.json"), JsonSerializer.Serialize(replay, J));
        Console.WriteLine("Saved headless gameplay replay: " + Path.Combine(output, "replay.json"));
    }
}
