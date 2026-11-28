using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anthropocene.Simulation;

// Offline audit only. Counters never enter saves, observations or policy inputs.
// Work follows village-turns and the existing <=7-tile opportunity search, not population or annual substeps.
internal static class CivicAudit
{
    private static readonly JsonSerializerOptions J = new JsonSerializerOptions { IncludeFields = true };
    private const string Profile = "Assets/_Anthropocene/Resources/CivicGovernanceOpeningConfig.json";
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    private static void Write(StreamWriter writer, object row) => writer.WriteLine(JsonSerializer.Serialize(row, J));
    private static long Population(WorldState w) => w.Camps.Sum(c => (long)c.Population) + w.Hunters.Sum(h => (long)h.Population);
    private static string SourceHash() => Hash(Encoding.UTF8.GetBytes(string.Join("\n", Directory.GetFiles("Assets/_Anthropocene/Simulation", "*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(p => p + ":" + Hash(File.ReadAllBytes(p))))));
    private static TurnRecord Step(WorldState w, bool capture, out PreparedTurn prepared)
    {
        ResolutionMetrics.CaptureDemandAllocations = capture;
        var ai = AiPlanning.Decide(w);
        prepared = TurnPlanner.Prepare(w, ai.Orders, ai.Traces);
        return new TurnResolver().Resolve(w, prepared);
    }
    private static object[] Observe(WorldState w, PreparedTurn prepared)
    {
        var rows = new List<object>();
        foreach (var c in w.Camps.Where(c => c.IsVillage && c.Population > 0).OrderBy(c => c.Id))
        {
            prepared.DemandAllocations.TryGetValue(c.Id, out var a);
            bool knowledge = c.People.HasTech(NeolithicTech.Irrigation);
            bool gate = IrrigationEconomy.FertileGate(w, c);
            var diag = new IrrigationEconomy.OpportunityAudit();
            var project = knowledge ? IrrigationEconomy.Opportunity(w, c, audit: diag) : null;
            var request = a?.Requests.FirstOrDefault(r => r.Service == "irrigation development");
            var upkeep = a?.Requests.FirstOrDefault(r => r.Service == "canal maintenance");
            string reason = !knowledge ? "no knowledge" : a == null ? "no automatic allocation" : !(a.IrrigationFoodGapPerYear > 1e-8) ? "no food gap" : !gate ? "fertile gate" : project == null ?
                diag.Footprint == 0 ? "no footprint" : diag.Sources == 0 ? "no source" : diag.Routes == 0 ? "no legal route" : diag.IncompleteRoutes == 0 ? "routes already complete" : diag.DryTargets == 0 ? "no dry target" : diag.UnservedTargets == 0 ? "targets already served" : diag.CultivatedTargets == 0 ? "dry targets uncultivated" : diag.WateredCandidates == 0 ? "no delivered candidate water" : "no positive food gain" :
                request == null ? "opportunity without request" : !(request.Granted > 1e-8) ? "request unfunded" : "funded";
            var fertile = CultivationEconomy.Footprint(w, c).Where(id => (w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == c.Id) && w.Tiles[id].Seasons.Average(s => s.Moisture) >= w.Config.Agriculture.FertileMoisture).ToArray();
            rows.Add(new {
                camp = c.Id, population = c.Population, knowledge, gate, reason,
                foodGap = a?.IrrigationFoodGapPerYear ?? 0,
                fertileTiles = fertile.Length,
                minFertileQ = fertile.Length == 0 ? (double?)null : fertile.Min(id => CultivationEconomy.Intensity(w.Config, LazyEcology.ProjectEcology(w, id, w.Days).CultivationCapital)),
                diag, target = project?.Target, gain = project?.AnnualFoodGain ?? 0,
                remaining = project?.Remaining ?? 0,
                requested = request?.Units ?? 0, initialGrant = request?.GrantedBeforeProduction ?? 0,
                grant = request?.Granted ?? 0, unmet = request?.Unmet ?? "", score = Finite(request?.PriorityScore),
                upkeepRequested = upkeep?.Units ?? 0, upkeepGrant = upkeep?.Granted ?? 0,
                development = a?.Requests.Where(r => r.Class == DemandController.ServiceClass.Development).Select(r => new { r.Service, r.Units, r.Granted, r.Unmet, score = Finite(r.PriorityScore) }).ToArray()
            });
        }
        return rows.ToArray();
    }
    private static double? Finite(double? n) => n.HasValue && double.IsFinite(n.Value) ? n : null;
    private static object Boundary(WorldState w, double start, TurnRecord r)
    {
        var villages = w.Camps.Where(c => c.IsVillage && c.Population > 0).OrderBy(c => c.Id).ToArray();
        var demographics = r?.Demographics?.ToDictionary(d => d.Camp);
        var fieldCounts = w.Tiles.Where(t => t.CultivationOperator >= 0).GroupBy(t => t.CultivationOperator).ToDictionary(g => g.Key, g => g.Count());
        return new { years = (w.Days - start) / w.Config.DaysPerYear, worldPopulation = Population(w), villages = villages.Length,
            largest = villages.Select(c => c.Population).DefaultIfEmpty(0).Max(),
            over5000 = villages.Count(c => c.Population >= 5000),
            canals = w.Canals?.Count ?? 0, irrigatedFields = w.IrrigatedFields?.Count ?? 0,
            camps = villages.Select(c => {
                DemographicReport d = null;
                demographics?.TryGetValue(c.Id, out d);
                return new { id = c.Id, population = c.Population, c.CivicComplexity, c.CivicStratification,
                    fields = fieldCounts.TryGetValue(c.Id, out var count) ? count : 0,
                    irrigated = w.IrrigatedFields?.Count(f => f.Operator == c.Id) ?? 0,
                    reserves = c.PlantFood + c.AnimalFood + c.AquaticFood,
                    farmers = CultivationEconomy.Workers(c, WorkforceRoles.Farmer),
                    civic = CivicGovernance.CivicCoverage(w, c), sanitation = CivicGovernance.SanitationCoverage(w, c),
                    d = d == null ? null : new { d.Years, d.StartingPopulation, d.EndingPopulation, d.GrossBirths, d.GrossDeaths, d.ContinuousBirths, d.PersonYears, d.SplitOff, d.Emigrated, d.Immigrated, d.FoodProduced, d.FoodRequired, d.GrowthFoodAccepted, d.FinalReserves, d.StarvationDeaths, d.CrowdingDeaths, d.DiseaseDeaths }
                };
            }).ToArray() };
    }
    private static object Prebuild(WorldState w, bool includeWateredZeroGain = false)
    {
        int projects = 0; double segments = 0;
        foreach (var c in w.Camps.Where(c => c.IsVillage && c.Population > 0 && c.People.HasTech(NeolithicTech.Irrigation)).OrderBy(c => c.Id))
            for (int i = 0; i < 7; i++)
            {
                var audit = new IrrigationEconomy.OpportunityAudit();
                var p = IrrigationEconomy.Opportunity(w, c, audit: audit);
                if (p == null && includeWateredZeroGain) p = audit.WateredZeroGainProject;
                if (p == null) break;
                var order = new ProductionOrder(Array.Empty<CraftGood>(), (IReadOnlyDictionary<CraftGood, double>)new Dictionary<CraftGood, double>());
                order.Irrigation = new IrrigationOrder { Project = p, Construction = p.Remaining };
                // Explicit diagnostic subsidy: build and first service period are free;
                // subsequent upkeep, labor, water competition and soil clocks stay native.
                IrrigationEconomy.Produce(w, c, CraftGood.CanalConstruction, p.Remaining, order, w.Days);
                segments += p.Remaining; projects++;
            }
        return new { projects, segments };
    }
    public static void Inspect(string[] args)
    {
        foreach (string path in args.Where(a => a.EndsWith(".anthro")))
        {
            var w = SaveCodec.Load(File.ReadAllBytes(path));
            var rows = w.Camps.Where(c => c.IsVillage).OrderByDescending(c => c.Population).Select(c => new {
                c.Id, c.Population, c.CivicComplexity, c.CivicStratification, c.RecentTradeVolume,
                precedents = c.People.CivicPrecedents,
                c.PlantFood, c.AnimalFood, c.AquaticFood, c.Inventory.FarmTools,
                farmland = CultivationEconomy.Footprint(w,c).Select(id => {
                    var t = w.Tiles[id]; var e = LazyEcology.ProjectEcology(w,id,w.Days);
                    return new { id, t.CultivationOperator, q = CultivationEconomy.Intensity(w.Config,e.CultivationCapital),
                        e.Food, e.LandCondition, t.CultivationOperatingFraction, e.IrrigationSoil, e.IrrigationWater,
                        moisture = t.Seasons.Select(s=>s.Moisture).ToArray(),
                        capacities = Enumerable.Range(0,4).Select(s=> { var cap = new EcologyCapacity(w,t,e,s); return new {cap.Food,cap.FoodRate,cap.ManagedFlow}; }).ToArray() };
                }).ToArray()
            }).ToArray();
            File.WriteAllText(path + ".inspection.json", JsonSerializer.Serialize(new { path, w.Days, turnYears = w.Config.TurnDays / w.Config.DaysPerYear, rows }, J));
            Console.WriteLine("Inspected " + path);
        }
    }
    public static void DurationProbe(string[] args)
    {
        foreach (string path in args.Where(a => a.EndsWith(".anthro")))
        {
            var bytes = File.ReadAllBytes(path);
            var initial = SaveCodec.Load(bytes);
            double totalDays = initial.Config.TurnDays;
            int leader = initial.Camps.Where(c => c.IsVillage).OrderByDescending(c => c.Population).First().Id;
            var leaderCamp = initial.Camps.First(c => c.Id == leader);
            var kernel = new List<object>();
            foreach (int divisions in new[] { 1, 2, 4 })
            {
                double harvested = 0, endingFood = 0, openingFood = 0, recharge = 0, overflow = 0;
                foreach (int tile in CultivationEconomy.Footprint(initial, leaderCamp))
                {
                    var view = LazyEcology.ProjectEcology(initial, tile, initial.Days);
                    double initialFood = view.Food, capital = view.CultivationCapital;
                    openingFood += initialFood;
                    for (int i = 0; i < divisions; i++)
                    {
                        // Identical total request and rate, fixed capital and operation;
                        // only harvest/recovery boundaries differ. No replanning or demography.
                        var claim = new Demand { Tile = tile, Entity = leader, Kind = "Cultivation", Food = 4 * initialFood / divisions };
                        var r = EcologicalPressure.Integrate(initial, tile, new[] { claim }, initial.Days + i * totalDays / divisions,
                            totalDays / divisions, capital, capital, 1, projectWood: false, openingEcology: view);
                        harvested += r.Transfers.Sum(t => t.Food);
                        recharge += r.Balance.FoodRecharge; overflow += r.Balance.FoodOverflow;
                        view = r.View;
                    }
                    endingFood += view.Food;
                }
                double residual = openingFood + recharge - harvested - overflow - endingFood;
                if (Math.Abs(residual) > 1e-6 * Math.Max(1, openingFood)) throw new Exception("Fixed-claim food conservation");
                kernel.Add(new { divisions, openingFood, harvested, endingFood, recharge, overflow, residual });
            }
            var output = new List<object>();
            foreach (int divisions in new[] { 1, 2, 4 })
            {
                var w = SaveCodec.Load(bytes);
                w.Config.SmoothCalendar = null;
                w.Config.TurnDays = totalDays / divisions;
                var records = new List<TurnRecord>();
                for (int i = 0; i < divisions; i++) records.Add(Step(w, false, out _));
                TurnResolver.Invariants(w);
                var c = w.Camps.First(c => c.Id == leader);
                var ds = records.SelectMany(r => r.Demographics).Where(d => d.Camp == leader).ToArray();
                output.Add(new { divisions, elapsedYears = (w.Days-initial.Days)/w.Config.DaysPerYear,
                    worldPopulation = Population(w), camp = leader, population = c.Population,
                    foodProduced = ds.Sum(d=>d.FoodProduced), foodRequired = ds.Sum(d=>d.FoodRequired),
                    agriculturalFood = records.SelectMany(r=>r.AgriculturalFood ?? new List<AgriculturalFoodReceipt>()).Where(r=>r.Camp==leader).Sum(r=>r.CropFood+r.WildFood),
                    births = ds.Sum(d=>d.GrossBirths), deaths = ds.Sum(d=>d.GrossDeaths),
                    reserves = c.PlantFood+c.AnimalFood+c.AquaticFood,
                    fieldFood = CultivationEconomy.Footprint(w,c).Sum(id=>LazyEcology.ProjectEcology(w,id,w.Days).Food),
                    meanQ = CultivationEconomy.Footprint(w,c).Average(id=>CultivationEconomy.Intensity(w.Config,LazyEcology.ProjectEcology(w,id,w.Days).CultivationCapital))
                });
            }
            File.WriteAllText(path+".duration-probe.json",JsonSerializer.Serialize(new { checkpointHash=Hash(bytes), leader, totalYears=totalDays/initial.Config.DaysPerYear, fixedClaimKernel=kernel, arms=output },J));
            Console.WriteLine("Duration probe " + path);
        }
    }
    public static void Run(string[] args)
    {
        string Arg(string key, string fallback) => args.FirstOrDefault(a => a.StartsWith(key + "="))?.Substring(key.Length + 1) ?? fallback;
        string output = Arg("--output", "TestResults/CivicAudit/extended");
        string baseline = Arg("--baseline", "TestResults/CivicAudit/baseline");
        double years = double.Parse(Arg("--years", "2000"), System.Globalization.CultureInfo.InvariantCulture);
        if (!double.IsFinite(years) || years < 1000) throw new ArgumentException("Audit horizon must be at least 1000 years for baseline parity.");
        var seeds = Arg("--seeds", "11,23,47,101,1729").Split(',').Select(int.Parse).ToArray();
        var arms = Arg("--arms", "off,phase1,full").Split(',');
        foreach (string arm in arms) if (!new[] { "off", "phase1", "full", "full-gate-off", "full-early-gate-off", "full-prebuilt", "full-physical-prebuilt", "full-fixed-horizon" }.Contains(arm)) throw new ArgumentException("Unknown arm " + arm);
        Directory.CreateDirectory(output);
        string manifest = Path.Combine(output, "manifest.json");
        if (File.Exists(manifest)) throw new IOException("Use a fresh audit output directory; refusing cached results.");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { sourceHead = "319d03b1719ee49bc7573fd119cb174c920dd074", simulationSourceHash = SourceHash(), binarySha256 = Hash(File.ReadAllBytes(typeof(CivicAudit).Assembly.Location)), profileSha256 = Hash(File.ReadAllBytes(Profile)), years, seeds, arms, baseline, runtime = Environment.Version.ToString(), interventionYears = arms.ToDictionary(a => a, a => a == "full-early-gate-off" ? 0 : 1000), createdUtc = DateTime.UtcNow }, J));
        foreach (int seed in seeds)
        {
            var bytes = File.ReadAllBytes(Path.Combine(baseline, $"seed-{seed}-checkpoint.anthro"));
            string checkpointHash = Hash(bytes);
            foreach (string arm in arms)
            {
                string referenceArm = arm.StartsWith("full") ? "full" : arm;
                using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseline, $"seed-{seed}-{referenceArm}.json")));
                if (reference.RootElement.GetProperty("checkpointSha256").GetString() != checkpointHash) throw new Exception("Checkpoint hash mismatch");
                var w = SaveCodec.Load(bytes);
                var profile = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText(Profile), J);
                w.Config.Civic = arm == "off" ? null : profile.Civic;
                if (arm == "phase1") w.Config.Civic.SanitationEnabled = false;
                bool earlyGate = arm == "full-early-gate-off";
                if (earlyGate) w.Config.Agriculture.DevelopmentQ = 0;
                w.Config.Validate();
                double start = w.Days; int turns = 0; bool verified = false, injected = false;
                int peak = 0; double? crossed5000 = null;
                object intervention = earlyGate ? new { DevelopmentQ = 0 } : null;
                var timer = Stopwatch.StartNew();
                using var trajectory = new StreamWriter(Path.Combine(output, $"seed-{seed}-{arm}-trajectory.jsonl"));
                using var audit = new StreamWriter(Path.Combine(output, $"seed-{seed}-{arm}-irrigation.jsonl"));
                Write(trajectory, Boundary(w, start, null));
                while ((w.Days - start) / w.Config.DaysPerYear < years)
                {
                    double elapsed = (w.Days - start) / w.Config.DaysPerYear;
                    if (elapsed >= 1000 && !injected)
                    {
                        if (arm == "full-gate-off") { w.Config.Agriculture.DevelopmentQ = 0; intervention = new { DevelopmentQ = 0 }; }
                        if (arm == "full-prebuilt") intervention = Prebuild(w);
                        if (arm == "full-physical-prebuilt") intervention = Prebuild(w, true);
                        if (arm == "full-fixed-horizon")
                        {
                            // Isolate continued calendar compression, keeping the already scheduled
                            // next duration and every economic/demographic coefficient unchanged.
                            w.Config.SmoothCalendar = null;
                            intervention = new { fixedTurnYears = w.Config.TurnDays / w.Config.DaysPerYear };
                        }
                        injected = true;
                    }
                    bool capture = arm.StartsWith("full");
                    ResolutionMetrics.CaptureDemandAllocations = capture;
                    var ai = AiPlanning.Decide(w);
                    var prepared = TurnPlanner.Prepare(w, ai.Orders, ai.Traces);
                    // The full first trajectory is fingerprint-checked at 1000y against the untouched binary.
                    var rows = capture ? Observe(w, prepared) : Array.Empty<object>();
                    var r = new TurnResolver().Resolve(w, prepared);
                    turns++;
                    elapsed = (w.Days - start) / w.Config.DaysPerYear;
                    Write(trajectory, Boundary(w, start, r));
                    if (capture) Write(audit, new { years = elapsed, rows, receipts = r.Production.Where(p => p.Good == CraftGood.CanalConstruction || p.Good == CraftGood.CanalMaintenance).Select(p => new { p.Camp, good = p.Good.ToString(), p.Output, p.WorkerIntervals }).ToArray(), water = r.IrrigationWater });
                    int largest = w.Camps.Where(c => c.IsVillage).Select(c => c.Population).DefaultIfEmpty(0).Max();
                    peak = Math.Max(peak, largest);
                    if (largest >= 5000 && crossed5000 == null) crossed5000 = elapsed;
                    if (!verified && elapsed >= 1000)
                    {
                        string fingerprint = SaveCodec.Fingerprint(w);
                        if (!earlyGate && reference.RootElement.GetProperty("finalFingerprint").GetString() != fingerprint) throw new Exception($"Baseline parity failed seed {seed} {arm}");
                        File.WriteAllBytes(Path.Combine(output, $"seed-{seed}-{arm}-1000.anthro"), SaveCodec.Save(w));
                        verified = true;
                        Console.WriteLine($"seed={seed} arm={arm} 1000y baseline parity {(earlyGate ? "not applicable: intervention at year 0" : "PASS")} largest={largest} world={Population(w)}");
                    }
                    if (turns % 100 == 0) { trajectory.Flush(); audit.Flush(); Console.WriteLine($"seed={seed} arm={arm} years={elapsed:F1} largest={largest} world={Population(w)} seconds={timer.Elapsed.TotalSeconds:F0}"); }
                }
                TurnResolver.Invariants(w); IrrigationEconomy.Validate(w);
                if (CivicGovernance.Enabled(w.Config)) CivicGovernance.ValidateInvariants(w);
                var finalSave = SaveCodec.Save(w);
                File.WriteAllBytes(Path.Combine(output, $"seed-{seed}-{arm}-final.anthro"), finalSave);
                var replica = SaveCodec.Load(finalSave);
                Step(w, false, out _); Step(replica, false, out _);
                if (SaveCodec.Fingerprint(w) != SaveCodec.Fingerprint(replica)) throw new Exception("Save continuation parity");
                File.WriteAllText(Path.Combine(output, $"seed-{seed}-{arm}-result.json"), JsonSerializer.Serialize(new { seed, arm, checkpointHash, verifiedBaseline1000 = earlyGate ? (bool?)null : verified, interventionYear = earlyGate ? 0 : 1000, saveContinuation = true, years, turns, peak, crossed5000, intervention, finalSaveHash = Hash(finalSave), seconds = timer.Elapsed.TotalSeconds }, J));
                Console.WriteLine($"DONE seed={seed} arm={arm} peak={peak} first5000={crossed5000} seconds={timer.Elapsed.TotalSeconds:F0}");
            }
        }
        ResolutionMetrics.CaptureDemandAllocations = false;
    }
}
