using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

// M5 matched three-arm civic governance sweep (Docs/CivicGovernance.md §5 Choice #11.E).
// Each seed plays the normal AI opening and an organic period with Civic == null, then receives Plough and
// Irrigation knowledge (the PR #49 ~1,000-resident regime) and is checkpointed. Every arm resumes the identical
// checkpoint; only SimulationConfig.Civic differs:
//   off    Civic == null (PR #49 behaviour)
//   phase1 Civic enabled, SanitationEnabled = false (crowding relief only)
//   full   Civic enabled, SanitationEnabled = true  (crowding + sanitation)
// No population, staffing, knowledge or civic overrides are applied after the checkpoint.
internal static class Program
{
    private static readonly JsonSerializerOptions J = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new JsonSerializerOptions { IncludeFields = true };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static readonly string[] AllArms = { "off", "phase1", "full" };
    // Diagnostic counterfactual (not organic): Tier 3A is granted the moment a society holds Tier 2.
    private const string AccountingArm = "full-3a";
    private const string Profile = "Assets/_Anthropocene/Resources/CivicGovernanceOpeningConfig.json";

    private static TurnRecord Step(WorldState w)
    {
        var ai = AiPlanning.Decide(w);
        return new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, ai.Orders, ai.Traces));
    }

    private static double Quantile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        double index = (sorted.Length - 1) * p;
        int lo = (int)index, hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (index - lo);
    }

    private static CivicGovernanceSettings ArmSettings(string arm)
    {
        if (arm == "off") return null;
        var profile = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText(Profile), J);
        var s = profile.Civic ?? throw new InvalidOperationException("Civic profile missing");
        s.Enabled = true;
        s.SanitationEnabled = arm == "full" || arm == AccountingArm;
        return s;
    }

    private static object Snapshot(WorldState w, double startDays, TurnRecord last = null)
    {
        var villages = w.Camps.Where(c => c.IsVillage && c.Population > 0).ToList();
        var pops = villages.Select(c => (double)c.Population).OrderBy(x => x).ToArray();
        bool civic = CivicGovernance.Enabled(w.Config);
        int Tier(string p) => villages.Count(c => c.People != null && c.People.HasCivicPrecedent(p));
        var egal = villages.Where(c => c.CivicComplexity > 0.05 && c.CivicStratification < -0.25).ToList();
        var strat = villages.Where(c => c.CivicComplexity > 0.05 && c.CivicStratification > 0.25).ToList();
        var mid = villages.Where(c => c.CivicComplexity > 0.05 && Math.Abs(c.CivicStratification) <= 0.25).ToList();
        return new
        {
            years = (w.Days - startDays) / w.Config.DaysPerYear,
            population = w.Camps.Sum(c => (long)c.Population) + w.Hunters.Sum(h => (long)h.Population),
            camps = w.Camps.Count,
            villages = villages.Count,
            villageMedian = Quantile(pops, 0.5),
            villageP90 = Quantile(pops, 0.9),
            villageMax = pops.Length == 0 ? 0 : pops[pops.Length - 1],
            over1000 = pops.Count(p => p > 1000),
            over2000 = pops.Count(p => p > 2000),
            over3500 = pops.Count(p => p > 3500),
            over5000 = pops.Count(p => p > 5000),
            tier1 = civic ? Tier(CivicPrecedents.CeremonialIntegration) : 0,
            tier2 = civic ? Tier(CivicPrecedents.CivicOrganization) : 0,
            tier3A = civic ? Tier(CivicPrecedents.TokensAndAccounting) : 0,
            tier3B = civic ? Tier(CivicPrecedents.PlannedQuartersAndDrainage) : 0,
            meanComplexity = villages.Count == 0 ? 0 : villages.Average(c => c.CivicComplexity),
            meanStratification = villages.Count == 0 ? 0 : villages.Average(c => c.CivicStratification),
            egalitarian = new { count = egal.Count, maxPop = egal.Select(c => c.Population).DefaultIfEmpty(0).Max(), meanPop = egal.Select(c => (double)c.Population).DefaultIfEmpty(0).Average() },
            middle = new { count = mid.Count, maxPop = mid.Select(c => c.Population).DefaultIfEmpty(0).Max(), meanPop = mid.Select(c => (double)c.Population).DefaultIfEmpty(0).Average() },
            stratified = new { count = strat.Count, maxPop = strat.Select(c => c.Population).DefaultIfEmpty(0).Max(), meanPop = strat.Select(c => (double)c.Population).DefaultIfEmpty(0).Average() },
            tier3ADrivers = civic ? Tier3ADrivers(w, villages) : null,
            stewards = w.Camps.Sum(c => c.Workforce != null ? CultivationEconomy.Workers(c, WorkforceRoles.Steward) : 0),
            civicCapital = w.Camps.Sum(c => c.CivicCapital),
            sanitationCapital = w.Camps.Sum(c => c.SanitationCapital),
            meanCivicCoverage = villages.Count == 0 || !civic ? 0 : villages.Average(c => CivicGovernance.CivicCoverage(w, c)),
            meanSanitationCoverage = villages.Count == 0 || !civic ? 0 : villages.Average(c => CivicGovernance.SanitationCoverage(w, c)),
            largest = villages.OrderByDescending(c => c.Population).Take(5).Select(c => new
            {
                c.Id, c.Population, C = Math.Round(c.CivicComplexity, 3), P = Math.Round(c.CivicStratification, 3),
                Mc = Math.Round(PhysicalDemography.CrowdingMitigation(w, c), 3), Md = Math.Round(PhysicalDemography.DiseaseMitigation(w, c), 3),
                cov = civic ? Math.Round(CivicGovernance.CivicCoverage(w, c), 3) : 0,
                sanit = civic ? Math.Round(CivicGovernance.SanitationCoverage(w, c), 3) : 0,
                water = Math.Round(c.SanitationWaterRatio, 3),
                fields = w.Tiles.Count(t => t.CultivationOperator == c.Id),
                irrigatedFields = w.IrrigatedFields?.Count(f => f.Operator == c.Id) ?? 0,
                farmers = c.Workforce != null ? CultivationEconomy.Workers(c, WorkforceRoles.Farmer) : 0,
                demography = Demography(last, c.Id)
            }).ToArray()
        };
    }

    private static object Demography(TurnRecord r, int camp)
    {
        var d = r?.Demographics?.FirstOrDefault(x => x.Camp == camp);
        if (d == null) return null;
        double py = Math.Max(1e-9, d.PersonYears);
        return new { d.Years, d.StartingPopulation, d.EndingPopulation, d.GrossBirths, d.GrossDeaths, d.SplitOff, d.Emigrated, d.Immigrated,
            birthRate = Math.Round(d.ContinuousBirths / py, 5), exposure = Math.Round(d.ExposureDeaths / py, 5), crowding = Math.Round(d.CrowdingDeaths / py, 5),
            disease = Math.Round(d.DiseaseDeaths / py, 5), starvation = Math.Round(d.StarvationDeaths / py, 5),
            nutrition = Math.Round(d.Nutrition, 3), clothing = Math.Round(d.ClothingCoverage, 3), housing = Math.Round(d.HousingCoverage, 3),
            foodProduced = Math.Round(d.FoodProduced), foodRequired = Math.Round(d.FoodRequired), growthFood = Math.Round(d.GrowthFoodAccepted), reserves = Math.Round(d.FinalReserves) };
    }

    private static object Tier3ADrivers(WorldState w, List<Camp> villages)
    {
        var t2 = villages.Where(c => c.People != null && c.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization)
            && !c.People.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting)).ToList();
        if (t2.Count == 0) return new { tier2Without3A = 0 };
        double perYear = PhysicalDemography.FoodPerPersonYear(w.Config);
        double Store(Camp c) => Math.Min(1, (Math.Max(0, c.PlantFood) + Math.Max(0, c.AnimalFood) + Math.Max(0, c.AquaticFood))
            / Math.Max(1e-9, Math.Max(1, c.Population) * perYear * 20) * Math.Min(1, c.Population / 1200.0));
        double Trade(Camp c) => c.RecentTradeVolume / (c.RecentTradeVolume + 0.15 * Math.Max(100, c.Population));
        double Exp(Camp c) => c.People.CivicExperience.TryGetValue(CivicPrecedents.TokensAndAccounting, out double x) ? x : 0;
        return new { tier2Without3A = t2.Count, meanUStore = t2.Average(Store), meanUTrade = t2.Average(Trade),
            meanExperience = t2.Average(Exp), maxExperience = t2.Max(Exp), threshold = w.Config.Civic.Tier3APracticeThreshold };
    }

    private static byte[] Checkpoint(int seed, double organicYears, string output, int width, int height, int players)
    {
        string path = Path.Combine(output, $"seed-{seed}-checkpoint.anthro");
        if (File.Exists(path)) { Console.WriteLine($"seed={seed} reusing checkpoint {path}"); return File.ReadAllBytes(path); }
        var config = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText(Profile), J);
        config.Civic = null; // organic history is the PR #49 baseline in every arm
        config.Width = width; config.Height = height; config.Players = players; config.StartSeparation = 9;
        var world = new WorldGenerator().Generate(seed, config);
        var timer = Stopwatch.StartNew();
        for (int i = 0; !world.Finished && i < 20; i++) Step(world);
        if (!world.Finished || !world.Config.SmoothCalendar.Started) throw new Exception("Opening not completed");
        double start = world.Days;
        while (world.Days < start + organicYears * world.Config.DaysPerYear) Step(world);
        foreach (var c in world.Camps)
            foreach (string tech in new[] { NeolithicTech.Plough, NeolithicTech.Irrigation })
                if (c.People != null && !c.People.HasTech(tech)) c.People.NeolithicTechnologies.Add(tech);
        var bytes = SaveCodec.Save(world);
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"seed={seed} checkpoint days={world.Days:F0} pop={world.Camps.Sum(c => c.Population)} villages={world.Camps.Count(c => c.IsVillage)} seconds={timer.Elapsed.TotalSeconds:F1}");
        return bytes;
    }

    private static void Main(string[] args)
    {
        if (args.Contains("--civic-playthrough")) { CivicPlaythrough.Run(args); return; }
        if (args.Contains("--annual-food-experiment")) { AnnualFoodExperiment.Run(args); return; }
        if (args.Contains("--audit-inspect")) { CivicAudit.Inspect(args); return; }
        if (args.Contains("--audit-duration-probe")) { CivicAudit.DurationProbe(args); return; }
        if (args.Contains("--audit")) { CivicAudit.Run(args); return; }
        string Arg(string name, string fallback) => args.FirstOrDefault(a => a.StartsWith(name + "="))?.Substring(name.Length + 1) ?? fallback;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string output = Arg("--output", "TestResults/CivicGovernanceSweep");
        Directory.CreateDirectory(output);
        var seeds = Arg("--seeds", "11,23,47,101,1729").Split(',').Select(int.Parse).ToArray();
        var arms = Arg("--arms", string.Join(",", AllArms)).Split(',');
        double organicYears = double.Parse(Arg("--organic-years", "600"), inv);
        double years = double.Parse(Arg("--years", "600"), inv);
        double every = double.Parse(Arg("--snapshot-years", "100"), inv);
        int width = int.Parse(Arg("--width", "96")), height = int.Parse(Arg("--height", "48")), players = int.Parse(Arg("--players", "4"));
        bool checkpointOnly = args.Contains("--checkpoint-only");

        foreach (int seed in seeds)
        {
            var checkpoint = Checkpoint(seed, organicYears, output, width, height, players);
            if (checkpointOnly) continue;
            foreach (string arm in arms)
            {
                string resultPath = Path.Combine(output, $"seed-{seed}-{arm}.json");
                if (File.Exists(resultPath)) { Console.WriteLine($"seed={seed} arm={arm} exists, skipping"); continue; }
                var w = SaveCodec.Load(checkpoint);
                w.Config.Civic = ArmSettings(arm);
                w.Config.Validate();
                double start = w.Days, nextSnap = start;
                var snapshots = new List<object>(); TurnRecord lastRecord = null;
                var timer = Stopwatch.StartNew();
                double civicBuilderYears = 0, sanitBuilderYears = 0, sanitWater = 0, turnMs = 0, maxTurnMs = 0;
                int turns = 0, peakVillage = 0, peakEgal = 0, peakStrat = 0;
                long peakPopulation = 0;
                double yearsPerInterval = w.Config.BaseDays / w.Config.DaysPerYear;
                while (w.Days < start + years * w.Config.DaysPerYear)
                {
                    if (w.Days >= nextSnap - 1e-6) { snapshots.Add(Snapshot(w, start, lastRecord)); nextSnap += every * w.Config.DaysPerYear; }
                    var t = Stopwatch.StartNew();
                    var r = Step(w); lastRecord = r;
                    if (arm == AccountingArm)
                        foreach (var soc in w.Societies)
                            if (soc.HasCivicPrecedent(CivicPrecedents.CivicOrganization) && !soc.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting))
                                soc.CivicPrecedents.Add(CivicPrecedents.TokensAndAccounting);
                    double ms = t.Elapsed.TotalMilliseconds; turnMs += ms; maxTurnMs = Math.Max(maxTurnMs, ms); turns++;
                    civicBuilderYears += r.Production.Where(p => p.Good == CraftGood.CivicConstruction || p.Good == CraftGood.CivicMaintenance).Sum(p => p.WorkerIntervals) * yearsPerInterval;
                    sanitBuilderYears += r.Production.Where(p => p.Good == CraftGood.SanitationConstruction || p.Good == CraftGood.SanitationMaintenance).Sum(p => p.WorkerIntervals) * yearsPerInterval;
                    sanitWater += r.SanitationWater?.Sum(x => x.Withdrawal) ?? 0;
                    foreach (var c in w.Camps.Where(c => c.IsVillage))
                    {
                        peakVillage = Math.Max(peakVillage, c.Population);
                        if (c.CivicComplexity > 0.05 && c.CivicStratification < -0.25) peakEgal = Math.Max(peakEgal, c.Population);
                        if (c.CivicComplexity > 0.05 && c.CivicStratification > 0.25) peakStrat = Math.Max(peakStrat, c.Population);
                    }
                    peakPopulation = Math.Max(peakPopulation, w.Camps.Sum(c => (long)c.Population) + w.Hunters.Sum(h => (long)h.Population));
                }
                snapshots.Add(Snapshot(w, start, lastRecord));
                TurnResolver.Invariants(w);
                if (CivicGovernance.Enabled(w.Config)) CivicGovernance.ValidateInvariants(w);
                var save = SaveCodec.Save(w);
                var a = SaveCodec.Load(save); var b = SaveCodec.Load(save);
                Step(a); Step(b);
                if (SaveCodec.Fingerprint(a) != SaveCodec.Fingerprint(b)) throw new Exception("Deterministic continuation");
                var result = new
                {
                    seed, arm, organicYears, years = (w.Days - start) / w.Config.DaysPerYear, turns,
                    checkpointSha256 = Hash(checkpoint), peakVillage, peakEgalitarianVillage = peakEgal, peakStratifiedVillage = peakStrat, peakPopulation,
                    civicBuilderYears, sanitationBuilderYears = sanitBuilderYears, sanitationWater = sanitWater,
                    meanTurnMs = turns == 0 ? 0 : turnMs / turns, maxTurnMs, seconds = timer.Elapsed.TotalSeconds,
                    finalFingerprint = SaveCodec.Fingerprint(w), snapshots
                };
                File.WriteAllText(resultPath, JsonSerializer.Serialize(result, J));
                var last = snapshots[snapshots.Count - 1];
                Console.WriteLine($"seed={seed} arm={arm} turns={turns} peakVillage={peakVillage} egal={peakEgal} strat={peakStrat} final={JsonSerializer.Serialize(last, Compact).Substring(0, 160)} s={timer.Elapsed.TotalSeconds:F0}");
            }
        }
    }
}
