using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

// Normal AI/Elder play, with compact committed-boundary evidence. No population,
// knowledge, resource, staffing or calendar overrides after generation.
internal static class PopulationRuns
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new JsonSerializerOptions { IncludeFields = true };
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static double Quantile(long[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        double index = (sorted.Length - 1) * p;
        int lower = (int)index, upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }
    private static object Stats(IEnumerable<long> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        return new { count = sorted.Length, total = sorted.Sum(), median = Quantile(sorted, .5),
            p75 = Quantile(sorted, .75), max = sorted.Length == 0 ? 0 : sorted[sorted.Length - 1] };
    }
    internal static void Run(string[] args)
    {
        string Arg(string key, string fallback) => args.FirstOrDefault(a => a.StartsWith(key + "="))?.Substring(key.Length + 1) ?? fallback;
        int seed = int.Parse(Arg("--seed", "11"));
        double years = double.Parse(Arg("--years", "1600"), System.Globalization.CultureInfo.InvariantCulture);
        string output = Arg("--output", "TestResults/ConstructionFix80/seed-" + seed);
        Directory.CreateDirectory(output);
        var stopwatch = Stopwatch.StartNew();
        WorldState w;
        if (args.Any(a => a.StartsWith("--opening="))) w = SaveCodec.Load(File.ReadAllBytes(Arg("--opening", "")));
        else
        {
            var config = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/AgriculturalTechnologyOpeningConfig.json"), Json);
            config.Width = 1024; config.Height = 512; config.Players = 80; config.StartSeparation = 12; config.Shape = WorldShape.Continents;
            var resources = JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"), Json);
            w = new WorldGenerator().Generate(seed, config, resources: resources);
        }
        if (w.Seed != seed || w.Config.Players != 80 || w.Config.SmoothCalendar == null)
            throw new InvalidOperationException("Expected the requested 80-tribe smoothed-calendar opening");
        string openingPath = Path.Combine(output, "opening.anthro");
        File.WriteAllBytes(openingPath, SaveCodec.Save(w));
        var founders = w.Societies.Select(s => s.Id).OrderBy(x => x).ToArray();
        if (founders.Length != 80) throw new InvalidOperationException("Expected 80 founding societies");
        var sources = Directory.GetFiles("Assets/_Anthropocene/Simulation", "*.cs").OrderBy(x => x).ToDictionary(x => x, x => Hash(File.ReadAllBytes(x)));
        var manifest = new { seed, requestedYears = years, config = w.Config, founders,
            openingSha256 = Hash(File.ReadAllBytes(openingPath)), calendarSha256 = sources["Assets/_Anthropocene/Simulation/SmoothCalendar.cs"], sources,
            statistic = "Type-7 interpolated quantiles; living societies exclude extinct societies; founding lineages include all 80, with zero for extinct lineages; both include hunters.",
            finalSave = "Not captured by this compact run; the previous long-run metadata-size persistence blocker is tracked separately." };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        double startDays = w.Days;
        var durations = new List<double>(); var seasons = new double[4];
        var shortfalls = new List<object>(); var reductions = new List<object>(); var attainments = new List<object>();
        double totalAuthorized = 0, totalGranted = 0, totalConstruction = 0, totalUpkeep = 0, materialsSpent = 0, builderYears = 0;
        double waterDelivery = 0, maxResidual = 0;
        int constructionJobs = 0, maintenanceJobs = 0, housingCanalOverlap = 0;
        ResolutionMetrics.CaptureDemandAllocations = true;
        using var journal = new StreamWriter(Path.Combine(output, "turns.jsonl"));
        while (w.Days < startDays + years * w.Config.DaysPerYear)
        {
            var timer = Stopwatch.StartNew();
            var ai = AiPlanning.Decide(w);
            var prepared = TurnPlanner.Prepare(w, ai.Orders, ai.Traces);
            var record = new TurnResolver().Resolve(w, prepared);
            var actual = record.Production.GroupBy(p => (p.Camp, p.Good)).ToDictionary(g => g.Key, g => g.Sum(p => p.Output));
            foreach (var pair in prepared.DemandAllocations.OrderBy(p => p.Key))
            {
                foreach (var request in pair.Value.Requests.Where(r => r.Good.HasValue && IrrigationEconomy.InfrastructureGood(r.Good.Value)))
                {
                    double made = actual.TryGetValue((pair.Key, request.Good.Value), out double q) ? q : 0;
                    prepared.Needs[pair.Key].Production.TryGetValue(request.Good.Value, out double forecast);
                    if (request.Good == CraftGood.CanalConstruction)
                    {
                        totalAuthorized += request.GrantedBeforeProduction; totalGranted += request.Granted;
                        if (request.Granted > 1e-8)
                        {
                            constructionJobs++;
                            if (actual.TryGetValue((pair.Key, CraftGood.Housing), out double housing) && housing > 1e-8) housingCanalOverlap++;
                        }
                    }
                    else if (request.Granted > 1e-8) maintenanceJobs++;
                    if (request.GrantedBeforeProduction > request.Granted + 1e-7)
                        reductions.Add(new { turn = w.Turn - 1, camp = pair.Key, good = request.Good.ToString(), initialGrant = request.GrantedBeforeProduction, grant = request.Granted, forecast, actual = made });
                    if (request.Granted > made + 1e-7)
                        shortfalls.Add(new { turn = w.Turn - 1, camp = pair.Key, good = request.Good.ToString(), initialGrant = request.GrantedBeforeProduction, grant = request.Granted, forecast, actual = made });
                }
            }
            totalConstruction += record.Production.Where(p => p.Good == CraftGood.CanalConstruction).Sum(p => p.Output);
            totalUpkeep += record.Production.Where(p => p.Good == CraftGood.CanalMaintenance).Sum(p => p.Output);
            materialsSpent += record.Production.Where(p => IrrigationEconomy.InfrastructureGood(p.Good)).Sum(p => p.HousingMaterial);
            builderYears += record.Production.Where(p => p.Role == WorkforceRoles.Builder).Sum(p => p.WorkerIntervals) * w.Config.BaseDays / w.Config.DaysPerYear;
            foreach (var water in record.IrrigationWater ?? new List<IrrigationWaterReceipt>())
            {
                waterDelivery += water.Delivery;
                maxResidual = Math.Max(maxResidual, Math.Abs(water.Withdrawal - water.Delivery - water.Loss));
            }
            foreach (string message in record.Events.Where(e => e.Contains("attained Plough") || e.Contains("attained Irrigation")))
                attainments.Add(new { turn = w.Turn - 1, days = w.Days, message });
            durations.Add(record.Duration);
            var exposure = new SeasonExposure(record.StartDays, record.Duration, w.Config.BaseDays);
            for (int s = 0; s < 4; s++) seasons[s] += exposure.At(s);
            long population = w.Camps.Sum(c => (long)c.Population) + w.Hunters.Sum(h => (long)h.Population);
            var frame = new { turn = w.Turn - 1, days = w.Days, years = w.Days / w.Config.DaysPerYear,
                durationDays = record.Duration, population, camps = w.Camps.Count, villages = w.Camps.Count(c => c.IsVillage),
                canals = w.Canals?.Count ?? 0, completedCanals = w.Canals?.Count(c => c.Construction >= 1 - 1e-8) ?? 0,
                construction = totalConstruction, upkeep = totalUpkeep, materialsSpent, builderYears, waterDelivery, maxResidual,
                grantReductions = reductions.Count, shortfalls = shortfalls.Count, milliseconds = timer.Elapsed.TotalMilliseconds };
            journal.WriteLine(JsonSerializer.Serialize(frame, Compact)); journal.Flush();
            Console.WriteLine($"seed={seed} turn={w.Turn - 1} year={w.Days / w.Config.DaysPerYear:F3} pop={population} camps={w.Camps.Count} canals={w.Canals?.Count ?? 0} reductions={reductions.Count} shortfalls={shortfalls.Count} ms={timer.Elapsed.TotalMilliseconds:F0}");
        }
        TurnResolver.Invariants(w);
        var populations = new Dictionary<int, long>();
        void Add(int id, int population) => populations[id] = (populations.TryGetValue(id, out long n) ? n : 0) + population;
        foreach (var c in w.Camps) Add(c.SocietyId, c.Population);
        foreach (var h in w.Hunters) Add(h.SocietyId, h.Population);
        var societies = w.Societies.ToDictionary(s => s.Id);
        var rootCache = founders.ToDictionary(id => id, id => id);
        int Root(int id)
        {
            if (rootCache.TryGetValue(id, out int root)) return root;
            return rootCache[id] = Root(societies[id].ParentSociety);
        }
        var lineages = founders.ToDictionary(id => id, _ => 0L);
        foreach (var pair in populations) lineages[Root(pair.Key)] += pair.Value;
        if (populations.Values.Sum() != lineages.Values.Sum()) throw new Exception("Population accounting mismatch");
        var summary = new { seed, turns = durations.Count, startDays, endDays = w.Days, physicalYears = (w.Days - startDays) / w.Config.DaysPerYear,
            calendar = w.Config.SmoothCalendar, durationsDays = durations, seasonalExposure = seasons,
            livingTribes = Stats(populations.Values.Where(p => p > 0)), foundingLineages = Stats(lineages.Values),
            camps = w.Camps.Count, villages = w.Camps.Count(c => c.IsVillage), hunters = w.Hunters.Sum(h => h.Population),
            totalAuthorized, totalGranted, totalConstruction, totalUpkeep, materialsSpent, builderYears, constructionJobs, maintenanceJobs, housingCanalOverlap,
            waterDelivery, maxWaterConservationResidual = maxResidual, shortfalls, reductions, attainments,
            completeCanals = w.Canals?.Count(c => c.Construction >= 1 - 1e-8) ?? 0,
            partialCanals = w.Canals?.Count(c => c.Construction < 1 - 1e-8) ?? 0,
            meanSoilDevelopment = w.IrrigatedFields?.Select(f => f.Soil).DefaultIfEmpty(0).Average() ?? 0,
            livingSocietyPopulations = populations.Where(p => p.Value > 0).OrderBy(p => p.Key).Select(p => new { society = p.Key, founder = Root(p.Key), population = p.Value }),
            foundingLineagePopulations = lineages.OrderBy(p => p.Key).Select(p => new { founder = p.Key, population = p.Value }),
            elapsedSeconds = stopwatch.Elapsed.TotalSeconds, peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, Json));
        Console.WriteLine(JsonSerializer.Serialize(new { seed, living = Stats(populations.Values.Where(p => p > 0)), founding = Stats(lineages.Values) }, Compact));
        ResolutionMetrics.CaptureDemandAllocations = false;
    }
}
