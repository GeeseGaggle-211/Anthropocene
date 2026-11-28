using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

// Read-only census around native headless turns. No civic/irrigation subsidies.
internal static class CivicPlaythrough
{
    private static readonly JsonSerializerOptions J = new JsonSerializerOptions { IncludeFields = true };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static PreparedTurn Prepare(WorldState w)
    { var ai = AiPlanning.Decide(w); return TurnPlanner.Prepare(w, ai.Orders, ai.Traces); }
    private static object Census(WorldState w, double start, HashSet<int> originals, PreparedTurn prepared,
        TurnRecord last, Dictionary<string, double> firstTiers, double civicWork, double sanitationWork)
    {
        var demographics = last?.Demographics.ToDictionary(d => d.Camp);
        var villages = w.Camps.Where(c => c.IsVillage && c.Population > 0).OrderBy(c => c.Id).Select(c => {
            bool known = c.People.HasTech(NeolithicTech.Irrigation);
            var audit = new IrrigationEconomy.OpportunityAudit();
            var project = known ? IrrigationEconomy.Opportunity(w, c, audit: audit) : null;
            bool gate = IrrigationEconomy.FertileGate(w, c);
            var fields = (w.IrrigatedFields ?? new List<IrrigatedField>()).Where(f => f.Operator == c.Id).ToArray();
            bool eligible = known && gate && project != null;
            prepared.DemandAllocations.TryGetValue(c.Id, out var allocation);
            var request = allocation?.Requests.FirstOrDefault(r => r.Service == "irrigation development");
            var drivers = CivicGovernance.EvaluateEnvironment(w, c);
            DemographicReport d = null; demographics?.TryGetValue(c.Id, out d);
            string reason = !known ? "no technology" : project != null ? (!gate ? "cultivation gate" :
                !(allocation?.IrrigationFoodGapPerYear > 1e-8) ? "no food gap" : request == null ? "no request" :
                request.Granted <= 1e-8 ? "unfunded" : "funded") :
                audit.Footprint == 0 ? "no footprint" : audit.Sources == 0 ? "no source" : audit.Routes == 0 ? "no route" :
                audit.IncompleteRoutes == 0 ? "routes complete" : audit.DryTargets == 0 ? "no dry target" :
                audit.UnservedTargets == 0 ? "targets served" : audit.CultivatedTargets == 0 ? "uncultivated target" :
                audit.WateredCandidates == 0 ? "no delivered water" : "no positive food gain";
            return new {
                id = c.Id, society = c.People.Id, original = originals.Contains(c.Id), tile = c.Tile,
                population = c.Population, complexity = c.CivicComplexity, stratification = c.CivicStratification,
                group = c.CivicComplexity <= .05 ? "incipient" : c.CivicStratification < -.25 ? "egalitarian" :
                    c.CivicStratification > .25 ? "stratified" : "mixed",
                focus = c.CivicFocus.ToString(), tiers = CivicPrecedents.All.Where(c.People.HasCivicPrecedent).ToArray(),
                practice = c.People.CivicExperience, civicCapital = c.CivicCapital, sanitationCapital = c.SanitationCapital,
                civicCoverage = CivicGovernance.CivicCoverage(w, c), sanitationCoverage = CivicGovernance.SanitationCoverage(w, c),
                sanitationWater = c.SanitationWaterRatio, stewards = CultivationEconomy.Workers(c, WorkforceRoles.Steward),
                crowdingMitigation = PhysicalDemography.CrowdingMitigation(w, c), diseaseMitigation = PhysicalDemography.DiseaseMitigation(w, c),
                reservesYears = c.Food / Math.Max(1, c.Population * PhysicalDemography.FoodPerPersonYear(w.Config)),
                tradeVolume = c.RecentTradeVolume, drivers,
                irrigation = new { known, gate, hasSource = audit.Sources > 0, hasRoute = audit.Routes > 0,
                    dryUnserved = audit.UnservedTargets > 0, cultivatedCandidate = audit.CultivatedTargets > 0,
                    wateredCandidate = audit.WateredCandidates > 0, positiveProject = project != null,
                    eligibleNew = eligible, neededNew = eligible && allocation?.IrrigationFoodGapPerYear > 1e-8,
                    requested = request != null, funded = request?.Granted > 1e-8,
                    gain = project?.AnnualFoodGain ?? 0, foodGap = allocation?.IrrigationFoodGapPerYear ?? 0,
                    installed = fields.Length, watered = fields.Count(f => f.WaterService > 1e-8 && f.LastUpdatedDays >= w.Days - 1e-7), reason },
                demography = d == null ? null : new { d.PersonYears, d.ContinuousBirths, d.ContinuousDeaths,
                    d.CrowdingDeaths, d.DiseaseDeaths, d.StarvationDeaths, d.Nutrition, d.ClothingCoverage, d.HousingCoverage }
            };
        }).ToArray();
        return new {
            years = (w.Days - start) / w.Config.DaysPerYear, absoluteYears = w.Days / w.Config.DaysPerYear,
            turnYears = w.Config.TurnDays / w.Config.DaysPerYear,
            population = w.Camps.Sum(c => (long)c.Population) + w.Hunters.Sum(h => (long)h.Population),
            camps = w.Camps.Count, firstTiers, civicWork, sanitationWork, villages
        };
    }
    public static void Run(string[] args)
    {
        string Arg(string key, string fallback) => args.FirstOrDefault(a => a.StartsWith(key + "="))?.Substring(key.Length + 1) ?? fallback;
        string output = Arg("--output", "TestResults/CivicPlaythrough");
        var seeds = Arg("--seeds", "11,23,47,101,1729").Split(',').Select(int.Parse).ToArray();
        var arms = Arg("--arms", "legacy,annual").Split(',');
        if (arms.Any(a => a != "legacy" && a != "annual")) throw new ArgumentException("Unknown arm");
        double years = double.Parse(Arg("--years", "2000"), System.Globalization.CultureInfo.InvariantCulture);
        Directory.CreateDirectory(output);
        string manifest = Path.Combine(output, "manifest.json");
        if (File.Exists(manifest)) throw new IOException("Use a fresh output directory");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new {
            seeds, arms, years, createdUtc = DateTime.UtcNow, runtime = Environment.Version.ToString(),
            binarySha256 = Hash(File.ReadAllBytes(typeof(CivicPlaythrough).Assembly.Location)),
            note = "Matched initial 600-year organic checkpoints; inherited Plough/Irrigation grant. Civic full treatment and food mode begin together at elapsed year zero."
        }, J));
        ResolutionMetrics.CaptureDemandAllocations = true;
        foreach (int seed in seeds)
        foreach (string arm in arms)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(Arg("--baseline", "TestResults/CivicAudit/baseline"), $"seed-{seed}-checkpoint.anthro"));
            var w = SaveCodec.Load(bytes);
            var profile = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/CivicGovernanceOpeningConfig.json"), J);
            w.Config.Civic = profile.Civic; w.Config.AnnualLandFoodEnabled = arm == "annual"; w.Config.Validate();
            var originals = w.Camps.Select(c => c.Id).ToHashSet();
            double start = w.Days, next = 0, civic = 0, sanitation = 0;
            int turns = 0; TurnRecord last = null; var first = new Dictionary<string, double>();
            var timer = Stopwatch.StartNew();
            using var stream = new StreamWriter(Path.Combine(output, $"seed-{seed}-{arm}.jsonl"));
            void Observe(PreparedTurn prepared) => stream.WriteLine(JsonSerializer.Serialize(Census(w, start, originals, prepared, last, first, civic, sanitation), J));
            while ((w.Days - start) / w.Config.DaysPerYear < years)
            {
                var prepared = Prepare(w);
                double elapsed = (w.Days - start) / w.Config.DaysPerYear;
                if (elapsed >= next) { Observe(prepared); next += 100; }
                last = new TurnResolver().Resolve(w, prepared); turns++;
                civic += last.Production.Where(p => p.Good == CraftGood.CivicConstruction || p.Good == CraftGood.CivicMaintenance)
                    .Sum(p => p.WorkerIntervals) * w.Config.BaseDays / w.Config.DaysPerYear;
                sanitation += last.Production.Where(p => p.Good == CraftGood.SanitationConstruction || p.Good == CraftGood.SanitationMaintenance)
                    .Sum(p => p.WorkerIntervals) * w.Config.BaseDays / w.Config.DaysPerYear;
                foreach (var tier in CivicPrecedents.All)
                    if (!first.ContainsKey(tier) && w.Camps.Any(c => c.People.HasCivicPrecedent(tier)))
                        first[tier] = (w.Days - start) / w.Config.DaysPerYear;
                if (turns % 100 == 0) Console.WriteLine($"seed={seed} arm={arm} years={(w.Days-start)/365:F0} population={w.Camps.Sum(c => c.Population)}");
            }
            string beforeCensus = SaveCodec.Fingerprint(w);
            Observe(Prepare(w)); stream.Flush();
            if (beforeCensus != SaveCodec.Fingerprint(w)) throw new Exception("Census mutated authoritative state");
            TurnResolver.Invariants(w); IrrigationEconomy.Validate(w);
            if (arm == "legacy" && years == 2000)
            {
                string dir = seed == 11 || seed == 23 || seed == 47 ? "extended-a" : "extended-b";
                var reference = SaveCodec.Load(File.ReadAllBytes($"TestResults/CivicAudit/{dir}/seed-{seed}-full-final.anthro"));
                if (SaveCodec.Fingerprint(reference) != beforeCensus) throw new Exception("Legacy baseline parity failed");
            }
            byte[] final = SaveCodec.Save(w); File.WriteAllBytes(Path.Combine(output, $"seed-{seed}-{arm}-final.anthro"), final);
            var loaded = SaveCodec.Load(final);
            new TurnResolver().Resolve(w, Prepare(w)); new TurnResolver().Resolve(loaded, Prepare(loaded));
            if (SaveCodec.Fingerprint(w) != SaveCodec.Fingerprint(loaded)) throw new Exception("Save continuation mismatch");
            File.WriteAllText(Path.Combine(output, $"seed-{seed}-{arm}-result.json"), JsonSerializer.Serialize(new {
                seed, arm, turns, checkpointSha256 = Hash(bytes), finalSha256 = Hash(final),
                legacyParity = arm == "legacy" && years == 2000, censusPure = true, saveContinuation = true,
                seconds = timer.Elapsed.TotalSeconds, originalCamps = originals.OrderBy(x => x).ToArray()
            }, J));
            Console.WriteLine($"PASS seed={seed} arm={arm} seconds={timer.Elapsed.TotalSeconds:F1}");
        }
        ResolutionMetrics.CaptureDemandAllocations = false;
    }
}
