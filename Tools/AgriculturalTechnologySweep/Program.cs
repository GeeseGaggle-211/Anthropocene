using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private static readonly JsonSerializerOptions J = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static ResolutionMetrics lastMetrics;
    private static TurnRecord Step(WorldState w)
    {
        var ai = AiPlanning.Decide(w); var resolver = new TurnResolver();
        var record = resolver.Resolve(w, TurnPlanner.Prepare(w, ai.Orders, ai.Traces));
        lastMetrics = resolver.LastMetrics; return record;
    }
    private static readonly string[] Treatments = { "none", "maintenance", "harvest", "combined", "water", "soil", "combined-water", "combined-soil", "low-plough", "high-plough", "half-cost", "double-cost", "moisture-one" };
    private static void Treatment(WorldState w, string name)
    {
        var n = w.Config.Agriculture;
        n.PloughEnabled = name != "none" && name != "water" && name != "soil";
        n.IrrigationEnabled = name == "water" || name == "soil" || name.Contains("-") && name != "low-plough" && name != "high-plough";
        if (name == "maintenance") n.PloughHarvest = 1;
        if (name == "harvest") n.PloughMaintenance = CultivationEconomy.FarmToolFieldWorkEfficiency(w.Config.Neolithic);
        if (name.EndsWith("water")) n.SoilCapacityGain = 0;
        if (name == "low-plough") { n.PloughMaintenance = 1.4; n.PloughHarvest = 1.15; }
        if (name == "high-plough") { n.PloughMaintenance = 1.6; n.PloughHarvest = 1.35; }
        double cost = name == "half-cost" ? .5 : name == "double-cost" ? 2 : 1;
        n.CanalBuilderYears *= cost; n.CanalHousingMaterials *= cost; n.UpkeepBuilderYears *= cost; n.UpkeepHousingMaterials *= cost;
        if (name == "moisture-one") n.TargetMoisture = 1;
        w.Config.Validate();
    }
    private static void LaborAudit()
    {
        const string output = "TestResults/AgriculturalTechnology";
        var expected = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(output, "results.json")));
        var audit = new List<object>();
        ResolutionMetrics.CaptureCraftWorkDiagnostics = true;
        foreach (var row in expected)
        {
            int seed = row.GetProperty("seed").GetInt32(); string treatment = row.GetProperty("treatment").GetString();
            bool fixedStaff = row.GetProperty("fixedFieldStaffing").GetBoolean();
            var w = SaveCodec.Load(File.ReadAllBytes(Path.Combine(output, $"seed-{seed}.anthro")));
            Treatment(w, treatment);
            var staff = w.Camps.ToDictionary(c => c.Id, c => (int)AgriculturalTechnology.OperatingFarmers(c));
            AgriculturalTechnology.FixedFieldStaffingDiagnostic = fixedStaff ? (state, camp) => staff.TryGetValue(camp.Id, out int n) ? n : (int?)null : null;
            double maintenance = 0, harvest = 0, reconciled = 0, extraction = 0, gathering = 0, manufacturing = 0, expansion = 0;
            while (w.Days < row.GetProperty("endDays").GetDouble() - 1e-8)
            {
                var record = Step(w);
                foreach (var work in lastMetrics.CraftFieldWork?.Values ?? Enumerable.Empty<CampFieldWorkDiagnostic>())
                {
                    maintenance += work.MaintenanceWorkerYears; harvest += work.HarvestEquivalentWorkerYears;
                    reconciled += work.ReconciledFarmerWorkerYears; expansion += work.ExpansionWorkUsedWorkerYears;
                }
                extraction += record.MaterialTransfers.Sum(r => r.WorkerIntervals) * w.Config.BaseDays / w.Config.DaysPerYear;
                gathering += record.Transfers.Where(t => t.Kind == "Gathering").Sum(t => t.Food) / w.Config.GatherPerPerson * w.Config.BaseDays / w.Config.DaysPerYear;
                manufacturing += record.Production.Sum(p => p.WorkerIntervals) * w.Config.BaseDays / w.Config.DaysPerYear;
            }
            if (SaveCodec.Fingerprint(w) != row.GetProperty("finalFingerprint").GetString()) throw new Exception("Labor diagnostics changed authoritative replay");
            audit.Add(new { seed, treatment, fixedFieldStaffing = fixedStaff, maintenanceWorkerYears = maintenance,
                harvestEquivalentWorkerYears = harvest, expansionWorkerYears = expansion, reconciledFarmerWorkerYears = reconciled,
                extractionWorkerYears = extraction, gatheringEquivalentWorkerYears = gathering, manufacturingWorkerYears = manufacturing,
                measuredAgricultureAndCraftWorkerYears = reconciled + extraction + gathering + manufacturing });
            File.WriteAllText(Path.Combine(output, "labor.json"), JsonSerializer.Serialize(audit, J));
            Console.WriteLine($"labor seed={seed} treatment={treatment} fixed={fixedStaff} exact-replay=pass");
        }
        AgriculturalTechnology.FixedFieldStaffingDiagnostic = null; ResolutionMetrics.CaptureCraftWorkDiagnostics = false;
    }
    private static void Benchmark()
    {
        var reports = new List<object>();
        foreach (int count in new[] { 1, 16, 80, 320 })
        {
            var cfg = SimulationConfig.Small(1); cfg.Width = 64; cfg.Height = 8 + 4 * ((count + 7) / 8);
            cfg.Agriculture = new AgriculturalTechnologySettings { IrrigationEnabled = true };
            var w = new WorldState { Config = cfg, Tiles = new Tile[cfg.Width * cfg.Height], Canals = new List<CanalSegment>(), IrrigatedFields = new List<IrrigatedField>() };
            for (int i = 0; i < w.Tiles.Length; i++)
            {
                var tile = w.Tiles[i] = new Tile { Id = i, Terrain = Terrain.Grassland, ElevationLevel = 1, Elevation = .1f,
                    Food = 80, PotentialFood = 800, Freshwater = 10000, FreshwaterSource = FreshwaterSource.RainPool };
                for (int season = 0; season < 4; season++) tile.Seasons[season] = new SeasonData { Temperature = 18, Moisture = .1f,
                    MaxFood = 80, MaxPotentialFood = 800, MaxFreshwater = 10000, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .1f };
            }
            for (int n = 0; n < count; n++)
            {
                int target = (4 + n / 8 * 4) * cfg.Width + 4 + n % 8 * 6, source = target - 1;
                var camp = new Camp { Id = w.NextId++, Tile = target, Player = n, Population = 100, IsVillage = true };
                w.Camps.Add(camp); var tile = w.Tiles[target]; tile.CultivationOperator = camp.Id; tile.CultivationCapital = 1; tile.CultivationOperatingFraction = 1;
                w.Tiles[source].FreshwaterSource = FreshwaterSource.River; w.Tiles[source].Elevation = .2f;
                var segment = new CanalSegment { Id = w.NextId++, Operator = camp.Id, Source = source, From = source, Tile = target,
                    Construction = 1, FundedYears = 1000, Throughput = cfg.Agriculture.AnnualThroughput };
                w.Canals.Add(segment); w.IrrigatedFields.Add(new IrrigatedField { Tile = target, Operator = camp.Id, Endpoint = segment.Id });
            }
            foreach (int population in new[] { 100, 1000000 }) foreach (double years in new[] { 1.0 / 365, 20, 600 })
            {
                foreach (var c in w.Camps) c.Population = population;
                void Run()
                {
                    var allocation = IrrigationEconomy.Allocate(w, Array.Empty<Demand>(), 0, years * 365);
                    foreach (var group in allocation.Withdrawals.GroupBy(d => d.Tile))
                    {
                        var receipt = EcologicalPressure.Integrate(w, group.Key, group, 0, years * 365);
                        if (Math.Abs(receipt.Transfers.Sum(t => t.Freshwater) - group.Sum(t => t.Freshwater)) > 1e-5) throw new Exception("Benchmark source conservation");
                    }
                    foreach (var field in allocation.Fields) EcologicalPressure.Integrate(w, field.Key, Array.Empty<Demand>(), 0, years * 365, irrigationOverlay: field.Value);
                }
                Run(); long before = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
                for (int repeat = 0; repeat < 20; repeat++) Run();
                reports.Add(new { networks = count, populationPerSettlement = population, years, milliseconds = clock.Elapsed.TotalMilliseconds / 20,
                    allocatedBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 20 });
            }
        }
        Directory.CreateDirectory("TestResults/AgriculturalTechnology");
        File.WriteAllText("TestResults/AgriculturalTechnology/benchmark.json", JsonSerializer.Serialize(reports, J));
        Console.WriteLine(JsonSerializer.Serialize(reports, J));
    }
    private static void Main(string[] args)
    {
        if (args.Contains("--population-run")) { PopulationRuns.Run(args); return; }
        if (args.Contains("--gameplay")) { Gameplay.Run(args); return; }
        if (args.Contains("--grant-audit")) { GrantAudit.Run(); return; }
        if (args.Contains("--grant-audit-fixture")) { GrantAudit.Fixture(); return; }
        if (args.Contains("--labor-audit")) { LaborAudit(); return; }
        if (args.Contains("--benchmark")) { Benchmark(); return; }
        string Arg(string name, string fallback) => args.FirstOrDefault(a => a.StartsWith(name + "="))?.Substring(name.Length + 1) ?? fallback;
        string output = Arg("--output", "TestResults/AgriculturalTechnology"); Directory.CreateDirectory(output);
        var seeds = Arg("--seeds", "11,23,47,101,1729").Split(',').Select(int.Parse).ToArray();
        var treatments = Arg("--treatments", string.Join(",", Treatments)).Split(',');
        double years = double.Parse(Arg("--years", "600"), System.Globalization.CultureInfo.InvariantCulture);
        var results = new List<object>(); var checkpoints = new List<object>();
        foreach (int seed in seeds)
        {
            AgriculturalTechnology.FixedFieldStaffingDiagnostic = null;
            var config = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/AgriculturalTechnologyOpeningConfig.json"), J);
            config.Width = 96; config.Height = 48; config.Players = 4; config.StartSeparation = 9;
            var world = new WorldGenerator().Generate(seed, config);
            var attainment = new Dictionary<string, double>();
            void Observe()
            {
                foreach (var c in world.Camps)
                    foreach (string tech in new[] { NeolithicTech.Plough, NeolithicTech.Irrigation })
                        if (c.People.HasTech(tech) && !attainment.ContainsKey(c.Id + ":" + tech)) attainment[c.Id + ":" + tech] = world.Days;
            }
            for (int i = 0; !world.Finished && i < 20; i++) { Step(world); Observe(); }
            if (!world.Finished || !world.Config.SmoothCalendar.Started) throw new Exception("Opening not completed");
            // Organic cohort: attainable dates are measured before diagnostic knowledge grants.
            double organicStart = world.Days;
            while (world.Days < organicStart + 600 * config.DaysPerYear) { Step(world); Observe(); }
            // Mature matched checkpoint; all treatments receive identical knowledge,
            // inventories, ecology and endogenous demographic state.
            foreach (var c in world.Camps)
                foreach (string tech in new[] { NeolithicTech.Plough, NeolithicTech.Irrigation })
                    if (!c.People.HasTech(tech)) c.People.NeolithicTechnologies.Add(tech);
            var checkpoint = SaveCodec.Save(world); string path = $"seed-{seed}.anthro"; File.WriteAllBytes(Path.Combine(output, path), checkpoint);
            checkpoints.Add(new { seed, path, sha256 = Hash(checkpoint), days = world.Days, config = world.Config, organicAttainmentDays = attainment });
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new { seeds, years, treatments,
                calendarSha256 = Hash(File.ReadAllBytes("Assets/_Anthropocene/Simulation/SmoothCalendar.cs")),
                sourceFiles = Directory.GetFiles("Assets/_Anthropocene/Simulation", "*.cs").OrderBy(x => x).ToDictionary(x => x, x => Hash(File.ReadAllBytes(x))), checkpoints }, J));
            foreach (bool fixedStaff in new[] { false, true }) foreach (string treatment in treatments)
            {
                var w = SaveCodec.Load(checkpoint); Treatment(w, treatment);
                var staff = w.Camps.ToDictionary(c => c.Id, c => (int)AgriculturalTechnology.OperatingFarmers(c));
                AgriculturalTechnology.FixedFieldStaffingDiagnostic = fixedStaff ? (state, camp) => staff.TryGetValue(camp.Id, out int n) ? n : (int?)null : null;
                double cropReceipts = 0, wildReceipts = 0, constructionWorkerYears = 0, upkeepWorkerYears = 0;
                double start = w.Days, productionWork = 0, fieldWork = 0, crops = 0, wild = 0, materials = 0, water = 0, loss = 0, farmToolsMade = 0, genericToolsUsed = 0;
                var durations = new List<double>(); var seasons = new double[4]; var timer = Stopwatch.StartNew();
                while (w.Days < start + years * w.Config.DaysPerYear)
                {
                    var r = Step(w); durations.Add(r.Duration);
                    var exposure = new SeasonExposure(r.StartDays, r.Duration, w.Config.BaseDays);
                    for (int s = 0; s < 4; s++) seasons[s] += exposure.At(s);
                    cropReceipts += r.AgriculturalFood?.Sum(x => x.CropFood) ?? 0;
                    wildReceipts += r.AgriculturalFood?.Sum(x => x.WildFood) ?? 0;
                    constructionWorkerYears += r.Production.Where(p => p.Good == CraftGood.CanalConstruction).Sum(p => p.WorkerIntervals) * w.Config.BaseDays / w.Config.DaysPerYear;
                    upkeepWorkerYears += r.Production.Where(p => p.Good == CraftGood.CanalMaintenance).Sum(p => p.WorkerIntervals) * w.Config.BaseDays / w.Config.DaysPerYear;
                    productionWork += r.Production.Sum(p => p.WorkerIntervals) * w.Config.BaseDays / w.Config.DaysPerYear;
                    fieldWork += w.Camps.Sum(c => AgriculturalTechnology.OperatingFarmers(c)) * r.Duration / w.Config.DaysPerYear;
                    materials += r.Production.Where(p => IrrigationEconomy.InfrastructureGood(p.Good)).Sum(p => p.HousingMaterial);
                    farmToolsMade += r.Production.Where(p => p.Good == CraftGood.FarmTools).Sum(p => p.Output);
                    genericToolsUsed += r.Production.Sum(p => p.ToolsConsumed + p.HandToolsConsumed);
                    crops += r.Transfers.Where(t => t.Kind == "Cultivation").Sum(t => t.Food);
                    wild += r.Transfers.Where(t => t.Kind == "Gathering").Sum(t => t.Food);
                    water += r.IrrigationWater?.Sum(x => x.Delivery) ?? 0; loss += r.IrrigationWater?.Sum(x => x.Loss) ?? 0;
                    foreach (var receipt in r.IrrigationWater ?? new List<IrrigationWaterReceipt>())
                        if (Math.Abs(receipt.Withdrawal - receipt.Delivery - receipt.Loss) > 1e-7) throw new Exception("Water ledger");
                }
                TurnResolver.Invariants(w);
                var save = SaveCodec.Save(w); var replay = SaveCodec.Load(save);
                if (SaveCodec.Fingerprint(w) != SaveCodec.Fingerprint(replay)) throw new Exception("Save roundtrip");
                var result = new { seed, treatment, fixedFieldStaffing = fixedStaff, startDays = start, endDays = w.Days, durationDays = durations, seasonalExposureDays = seasons,
                    elapsedYears = (w.Days - start) / w.Config.DaysPerYear, population = w.Camps.Sum(c => c.Population), camps = w.Camps.Count,
                    cropReceipts, wildReceipts, constructionWorkerYears, upkeepWorkerYears, productionWorkerYears = productionWork, operatingFarmerYears = fieldWork, farmerFoodReceipts = crops, gathererFoodReceipts = wild,
                    irrigationHousingMaterials = materials, farmToolsMade, genericToolsUsed, deliveredWater = water, waterLoss = loss,
                    soil = w.IrrigatedFields?.Sum(f => f.Soil * f.Area) ?? 0, canals = w.Canals?.Count ?? 0,
                    foodReserves = w.Camps.Sum(c => c.Food), farmTools = w.Camps.Sum(c => c.Inventory.FarmTools), housingMaterials = w.Camps.Sum(c => c.Inventory.HousingMaterial),
                    seconds = timer.Elapsed.TotalSeconds, coefficients = w.Config.Agriculture, finalFingerprint = SaveCodec.Fingerprint(w) };
                var replay2 = SaveCodec.Load(save); Step(replay); Step(replay2);
                if (SaveCodec.Fingerprint(replay) != SaveCodec.Fingerprint(replay2)) throw new Exception("Deterministic continuation");
                results.Add(result); File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, J));
                Console.WriteLine($"seed={seed} treatment={treatment} fixed={fixedStaff} years={result.elapsedYears:F2} canals={result.canals} water={water:F1} pop={result.population} seconds={result.seconds:F2}");
            }
        }
        AgriculturalTechnology.FixedFieldStaffingDiagnostic = null;
        File.WriteAllText(Path.Combine(output, "scope.json"), JsonSerializer.Serialize(new { seeds, years, treatments,
            calendarSha256 = Hash(File.ReadAllBytes("Assets/_Anthropocene/Simulation/SmoothCalendar.cs")), checkpoints,
            scope = "Native 96x48 four-player worlds; 600-year organic attainment, then matched knowledge-granted treatment checkpoints. Fixed staffing pins operating Farmers of checkpoint camps; other Elder labor remains endogenous. No demographic tuning." }, J));
    }
}
