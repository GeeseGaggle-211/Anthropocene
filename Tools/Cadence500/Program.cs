using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class Program
{
    private const string Baseline = "07f3501c2d5e047e44d61e85cc0d4613b54572d6";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static readonly PhaseExecution Execution = new PhaseExecution(4, 64);
    private static readonly List<Snapshot> Rows = new List<Snapshot>();
    private static readonly List<object> CampRows = new List<object>();
    private static readonly List<object> Checks = new List<object>();
    private static string Output, Stage = "initialization", Arm = "opening";
    private static int Seed;
    private static double Epoch, MaxBalanceError;
    private static int Rejections;
    private static WorldState Current;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private sealed class Snapshot
    {
        public int Seed, Turn, TotalPopulation, Residents, Hunters, Settlements, Villages;
        public string Arm;
        public double ElapsedYears, IntervalYears, ResidentMedian, ResidentP25, ResidentP75, ResidentMax;
        public int AgricultureFirstSettlements, AgricultureFirstResidents, AgricultureCapableSettlements, AgricultureCapableResidents;
        public double AgricultureMedian, AgricultureP25, AgricultureP75, AgricultureMax, AgricultureQoL;
        public double WorldQoL, FoodCoverage, ClothingCoverage, HousingCoverage, WaterCoverage;
        public double OwnedFootprintMeanQ, CultivationCapital, ActiveFieldMeanQ, ResidentFoodReserves, ReserveYears;
        public int Farmers, Gatherers, Herders, Fishers, ManagedAnimals, SettlementsAt5000, AgricultureAt5000;
        public double FarmTools, Cloth, FixedHousingCapacity;
        public double FoodProduced, TerrestrialFood, CultivationReceipts, FoodConsumed, ManufacturingFood, FoodDiscarded, PersonYears;
        public int Births, Deaths, Fissions, FissionResidents, Migrants;
        public double ExposureDeaths, CrowdingDeaths, DiseaseDeaths, StarvationDeaths;
        public double AssignedFarmerYears, MaintenanceFarmerYears, HarvestEquivalentYears, ExpansionFarmerYears, ToolCoveredFarmerYears;
        public double WallSeconds;
    }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static double Quantile(IEnumerable<double> values, double p)
    {
        var a = values.OrderBy(v => v).ToArray();
        if (a.Length == 0) return 0;
        double position = (a.Length - 1) * p; int i = (int)Math.Floor(position);
        return a[i] + (a[Math.Min(a.Length - 1, i + 1)] - a[i]) * (position - i);
    }
    private static double Weighted(Camp[] camps, Func<Camp, double> metric)
    { double total = camps.Sum(c => (double)c.Population); return total > 0 ? camps.Sum(c => metric(c) * c.Population) / total : 0; }
    private static double Relative(double a, double b) => Math.Abs(a - b) / Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static string Hash(byte[] bytes) { using var h = SHA256.Create(); return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static void Write(string name, object value) => File.WriteAllText(Path.Combine(Output, name), JsonSerializer.Serialize(value, Json));
    private static double Years(WorldState w) => (w.Days - Epoch) / w.Config.DaysPerYear;
    private static double FootprintQ(WorldState w, Camp camp)
    {
        var ids = CultivationEconomy.Footprint(w, camp);
        return ids.Length == 0 ? 0 : ids.Average(id => w.Tiles[id].CultivationOperator == camp.Id
            ? CultivationEconomy.Intensity(w.Config, LazyEcology.ProjectEcology(w, id, w.Days).CultivationCapital) : 0);
    }
    private static void Capture(WorldState w, string arm, TurnRecord record, ResolutionMetrics metrics)
    {
        var camps = w.Camps.Where(c => c.Population > 0).OrderBy(c => c.Id).ToArray();
        var agri = camps.Where(c => c.Technology == Technology.Cultivation).ToArray();
        var capable = camps.Where(CultivationEconomy.Enabled).ToArray();
        var qByCamp = capable.ToDictionary(c => c.Id, c => FootprintQ(w, c));
        var active = w.Tiles.Where(t => t.CultivationOperator >= 0 && t.CultivationCapital > 0).ToArray();
        var demographics = record == null ? Array.Empty<DemographicReport>() : record.Demographics.ToArray();
        var works = metrics?.CraftFieldWork == null ? Array.Empty<CampFieldWorkDiagnostic>() : metrics.CraftFieldWork.Values.ToArray();
        var row = new Snapshot {
            Seed = Seed, Arm = arm, Turn = w.Turn, ElapsedYears = Years(w), IntervalYears = record == null ? 0 : record.Duration / w.Config.DaysPerYear,
            Residents = camps.Sum(c => c.Population), Hunters = w.Hunters.Sum(h => h.Population),
            TotalPopulation = camps.Sum(c => c.Population) + w.Hunters.Sum(h => h.Population), Settlements = camps.Length, Villages = camps.Count(c => c.IsVillage),
            ResidentMedian = Quantile(camps.Select(c => (double)c.Population), .5), ResidentP25 = Quantile(camps.Select(c => (double)c.Population), .25),
            ResidentP75 = Quantile(camps.Select(c => (double)c.Population), .75), ResidentMax = camps.Select(c => (double)c.Population).DefaultIfEmpty(0).Max(),
            AgricultureFirstSettlements = agri.Length, AgricultureFirstResidents = agri.Sum(c => c.Population),
            AgricultureCapableSettlements = capable.Length, AgricultureCapableResidents = capable.Sum(c => c.Population),
            AgricultureMedian = Quantile(agri.Select(c => (double)c.Population), .5), AgricultureP25 = Quantile(agri.Select(c => (double)c.Population), .25),
            AgricultureP75 = Quantile(agri.Select(c => (double)c.Population), .75), AgricultureMax = agri.Select(c => (double)c.Population).DefaultIfEmpty(0).Max(),
            AgricultureQoL = Weighted(agri, c => c.QoL), WorldQoL = Weighted(camps, c => c.QoL), FoodCoverage = Weighted(camps, c => c.FoodNeedsMetRatio),
            ClothingCoverage = Weighted(camps, c => c.ClothPerCapita), HousingCoverage = Weighted(camps, c => c.HousingPerCapita), WaterCoverage = Weighted(camps, c => c.WaterNeedsMetRatio),
            OwnedFootprintMeanQ = qByCamp.Count > 0 ? qByCamp.Values.Average() : 0,
            CultivationCapital = active.Sum(t => LazyEcology.ProjectEcology(w, t.Id, w.Days).CultivationCapital),
            ActiveFieldMeanQ = active.Length > 0 ? active.Average(t => CultivationEconomy.Intensity(w.Config, LazyEcology.ProjectEcology(w, t.Id, w.Days).CultivationCapital)) : 0,
            ResidentFoodReserves = camps.Sum(c => c.Food),
            ReserveYears = camps.Sum(c => c.Food) / Math.Max(1e-12, camps.Sum(c => c.Population) * PhysicalDemography.FoodPerPersonYear(w.Config)),
            Farmers = camps.Sum(c => CultivationEconomy.Workers(c, WorkforceRoles.Farmer)),
            Gatherers = camps.Sum(c => c.GathererPopulation + CultivationEconomy.Workers(c, WorkforceRoles.Gatherer)),
            Herders = camps.Sum(c => CultivationEconomy.Workers(c, WorkforceRoles.Herder)), Fishers = camps.Sum(c => CultivationEconomy.Workers(c, WorkforceRoles.Fisher)),
            ManagedAnimals = w.Herds.Where(h => h.DomesticCamp >= 0).Sum(h => h.Animals),
            SettlementsAt5000 = camps.Count(c => c.Population >= 5000), AgricultureAt5000 = capable.Count(c => c.Population >= 5000),
            FarmTools = camps.Sum(c => c.Inventory.FarmTools), Cloth = camps.Sum(c => c.Inventory.Cloth),
            FixedHousingCapacity = camps.Sum(c => NeolithicProduction.HousingCapacity(w, c, w.Days).Total),
            FoodProduced = demographics.Sum(d => d.FoodProduced), TerrestrialFood = demographics.Sum(d => d.TerrestrialFood),
            CultivationReceipts = record == null ? 0 : record.Transfers.Where(t => t.Kind == "Cultivation").Sum(t => t.Food),
            FoodConsumed = demographics.Sum(d => d.FoodConsumed), ManufacturingFood = demographics.Sum(d => d.ManufacturingFood),
            FoodDiscarded = demographics.Sum(d => d.DiscardedInventory), PersonYears = demographics.Sum(d => d.PersonYears),
            Births = record?.Growth ?? 0, Deaths = record?.Deaths ?? 0, Fissions = record?.Fissions?.Count ?? 0,
            FissionResidents = record?.Fissions?.Sum(f => f.Daughter) ?? 0, Migrants = record?.Migrations?.Sum(m => m.Migrants) ?? 0,
            ExposureDeaths = demographics.Sum(d => d.ExposureDeaths), CrowdingDeaths = demographics.Sum(d => d.CrowdingDeaths),
            DiseaseDeaths = demographics.Sum(d => d.DiseaseDeaths), StarvationDeaths = demographics.Sum(d => d.StarvationDeaths),
            AssignedFarmerYears = works.Sum(x => x.AssignedFarmerWorkerYears), MaintenanceFarmerYears = works.Sum(x => x.MaintenanceWorkerYears),
            HarvestEquivalentYears = works.Sum(x => x.HarvestEquivalentWorkerYears), ExpansionFarmerYears = works.Sum(x => x.ExpansionFarmerWorkerYears),
            ToolCoveredFarmerYears = works.Sum(x => x.FarmToolCoveredWorkerYears), WallSeconds = Clock.Elapsed.TotalSeconds
        };
        Rows.Add(row);
        foreach (var c in camps)
        {
            var d = demographics.FirstOrDefault(x => x.Camp == c.Id);
            CampRows.Add(new { Seed, Arm = arm, ElapsedYears = row.ElapsedYears, c.Id, c.SocietyId, c.People.ParentSociety, c.Tile,
                FirstTechnology = c.Technology.ToString(), AgricultureCapable = CultivationEconomy.Enabled(c), c.Population, c.IsVillage,
                c.QoL, c.FoodNeedsMetRatio, c.ClothPerCapita, c.HousingPerCapita, c.WaterNeedsMetRatio,
                MeanOwnedFootprintQ = qByCamp.TryGetValue(c.Id, out double cq) ? cq : 0,
                c.Food, ReserveYears = c.Food / Math.Max(1e-12, c.Population * PhysicalDemography.FoodPerPersonYear(w.Config)),
                Farmers = CultivationEconomy.Workers(c, WorkforceRoles.Farmer), Gatherers = c.GathererPopulation + CultivationEconomy.Workers(c, WorkforceRoles.Gatherer),
                c.Inventory.FarmTools, c.Inventory.Cloth, c.Inventory.Wood, c.Inventory.Stone, c.Inventory.Clay,
                FoodProduced = d?.FoodProduced ?? 0, FoodConsumed = d?.FoodConsumed ?? 0, TradeFood = d?.TradeFood ?? 0,
                Nutrition = d?.Nutrition ?? 0, c.EffectiveFertilityRate, c.EffectiveMortalityRate });
        }
        Console.WriteLine($"SNAP seed={Seed} arm={arm} year={row.ElapsedYears:F0} dt={row.IntervalYears:F0} pop={row.TotalPopulation} camps={row.Settlements} agriMedian={row.AgricultureMedian:F1} agriMax={row.AgricultureMax:F0} q={row.OwnedFootprintMeanQ:F3} food={row.FoodCoverage:F3} qol={row.WorldQoL:F3}");
        Write("trajectory.json", Rows); // Preserve completed evidence if a later native invariant fails.
    }
    private static TurnRecord Step(WorldState w, double years, out ResolutionMetrics metrics, bool measure = true)
    {
        // Select the horizon BEFORE all policy observation, AI planning and Prepare.
        // No PlanningDraft is carried across a boundary. Reference unit constants are untouched.
        w.Config.TurnDays = years * w.Config.DaysPerYear;
        w.Config.Validate();
        var ai = AiPlanning.Decide(w, Execution);
        var prepared = TurnPlanner.Prepare(w, ai.Orders, ai.Traces, Execution);
        if (measure) Rejections += prepared.Rejections.Count;
        var resolver = new TurnResolver(Execution);
        var record = resolver.Resolve(w, prepared); metrics = resolver.LastMetrics;
        Check(Math.Abs(record.Duration - years * w.Config.DaysPerYear) < 1e-8, "Duration not honored");
        if (measure)
        foreach (var b in record.EcologyBalances)
        {
            double error = Math.Max(Relative(b.StartingFood + b.FoodRecharge, b.EndingFood + b.FoodHarvest + b.FoodOverflow),
                Relative(b.StartingBiomass + b.BiomassRecharge, b.EndingBiomass + b.BiomassHarvest + b.BiomassOverflow));
            MaxBalanceError = Math.Max(MaxBalanceError, error);
            Check(error < 1e-7, "Ecological receipt conservation failed");
        }
        return record;
    }
    private static void VerifyTransitionReplay(WorldState w, double nextYears, string label)
    {
        Stage = "transition replay " + label;
        byte[] saved = SaveCodec.Save(w); string fingerprint = SaveCodec.Fingerprint(w);
        var first = SaveCodec.Load(saved); Check(SaveCodec.Fingerprint(first) == fingerprint, "Save/load mismatch at " + label);
        Step(first, nextYears, out _, false); string a = SaveCodec.Fingerprint(first);
        first = null; GC.Collect();
        var second = SaveCodec.Load(saved); Step(second, nextYears, out _, false); string b = SaveCodec.Fingerprint(second);
        Check(a == b, "Transition replay mismatch at " + label);
        Checks.Add(new { test = "save-load and exact next-turn replay", label, nextYears, savedSha256 = Hash(saved), before = fingerprint, after = a, passed = true });
    }
    private static object Totals(string arm)
    {
        var r = Rows.Where(x => x.Arm == "common" || x.Arm == arm).Where(x => x.IntervalYears > 0).ToArray();
        return new { arm, measuredTurns = r.Length, years = r.Sum(x => x.IntervalYears), births = r.Sum(x => x.Births), deaths = r.Sum(x => x.Deaths),
            personYears = r.Sum(x => x.PersonYears), fissions = r.Sum(x => x.Fissions), fissionResidents = r.Sum(x => x.FissionResidents), migrants = r.Sum(x => x.Migrants),
            foodProduced = r.Sum(x => x.FoodProduced), foodConsumed = r.Sum(x => x.FoodConsumed), cultivationReceipts = r.Sum(x => x.CultivationReceipts),
            manufacturingFood = r.Sum(x => x.ManufacturingFood), foodDiscarded = r.Sum(x => x.FoodDiscarded),
            exposureDeaths = r.Sum(x => x.ExposureDeaths), crowdingDeaths = r.Sum(x => x.CrowdingDeaths), diseaseDeaths = r.Sum(x => x.DiseaseDeaths), starvationDeaths = r.Sum(x => x.StarvationDeaths),
            assignedFarmerYears = r.Sum(x => x.AssignedFarmerYears), maintenanceFarmerYears = r.Sum(x => x.MaintenanceFarmerYears),
            harvestEquivalentYears = r.Sum(x => x.HarvestEquivalentYears), expansionFarmerYears = r.Sum(x => x.ExpansionFarmerYears),
            toolCoveredFarmerYears = r.Sum(x => x.ToolCoveredFarmerYears), final = Rows.Last(x => x.Arm == arm) };
    }
    private static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Seed = args.Length > 0 ? int.Parse(args[0]) : 11;
        Output = args.Length > 1 ? args[1] : "TestResults/Cadence500/" + Seed;
        Directory.CreateDirectory(Output);
        try
        {
            var cfg = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"), Json);
            var resources = JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"), Json);
            cfg.Width = 1024; cfg.Height = 512; cfg.Players = 80; cfg.Shape = WorldShape.Continents;
            Check(Math.Abs(cfg.TurnDays / cfg.DaysPerYear - 20) < 1e-10, "Unexpected production opening cadence");
            Write("initial-config.json", cfg);
            ResolutionMetrics.CaptureCraftWorkDiagnostics = true;
            Stage = "generate production world";
            Current = new WorldGenerator().Generate(Seed, cfg, resources);
            Stage = "native opening"; int openingTurns = 0;
            while (!Current.Finished && openingTurns < 20) { Step(Current, 20, out _); openingTurns++; }
            Check(Current.Finished, "Opening did not complete");
            TurnResolver.Invariants(Current);
            Epoch = Current.Days;
            Checks.Add(new { test = "native opening complete", openingTurns, startDays = Epoch, passed = true });
            Arm = "common"; Capture(Current, Arm, null, null);
            string initialFingerprint = SaveCodec.Fingerprint(Current);
            while (Years(Current) < 200 - 1e-8)
            {
                Stage = "common 20-year prefix at year " + Years(Current);
                var r = Step(Current, 20, out var metrics); Capture(Current, Arm, r, metrics);
            }
            Check(Math.Abs(Years(Current) - 200) < 1e-8, "Prefix horizon");
            byte[] fork = SaveCodec.Save(Current); string forkFingerprint = SaveCodec.Fingerprint(Current);
            if (Seed == 11) VerifyTransitionReplay(Current, 15, "year200_20to15");
            foreach (string arm in new[] { "fixed20", "dynamic20_15_10" })
            {
                Arm = arm; Current = SaveCodec.Load(fork);
                Check(SaveCodec.Fingerprint(Current) == forkFingerprint, "Arms do not share identical fork");
                while (Years(Current) < 500 - 1e-8)
                {
                    double elapsed = Years(Current);
                    double dt = arm == "fixed20" ? 20 : elapsed < 350 - 1e-8 ? 15 : 10;
                    if (Seed == 11 && arm == "dynamic20_15_10" && Math.Abs(elapsed - 350) < 1e-8)
                        VerifyTransitionReplay(Current, 10, "year350_15to10");
                    Stage = arm + " at year " + elapsed;
                    var r = Step(Current, dt, out var metrics); Capture(Current, arm, r, metrics);
                }
                Check(Math.Abs(Years(Current) - 500) < 1e-8, "Campaign not exactly 500 years");
                Stage = "whole-world invariants " + arm; TurnResolver.Invariants(Current);
                Checks.Add(new { test = "completed 500 years with whole-world invariants", arm, fingerprint = SaveCodec.Fingerprint(Current), passed = true });
                Write("camps.json", CampRows); Current = null; GC.Collect();
            }
            var results = new { status = "complete", baseline = Baseline, Seed, openingTurns, neolithicStartDays = Epoch,
                initialFingerprint, forkYear = 200, forkFingerprint, forkSaveSha256 = Hash(fork),
                schedule = "0-200 years: 10x20; 200-350: 10x15; 350-500: 15x10. Control: 25x20. Common first 200 years shared exactly.",
                scope = "Unchanged native AI -> Prepare -> Resolve, including physical demography, goods, trade, fission, immigration and endogenous field investment. Shipped source/configuration preserved except map size/player count and boundary-selected TurnDays. No plough bonus, q freeze, biomass change, import subsidy, mortality/cohesion tuning or timer normalization. Fishing gates retain production defaults. No cached Draft is reused. Native per-turn social/movement/contact/random-stream effects remain part of the cadence treatment. Fixed20 and dynamic runs use the same byte-identical year-200 checkpoint. This is not a 5000-resident seeded fixture or RL evaluation.",
                checks = Checks, maxEcologicalBalanceRelativeError = MaxBalanceError, rejectedSubmittedOrders = Rejections,
                sourceHashes = Directory.GetFiles("Assets/_Anthropocene/Simulation", "*.cs").OrderBy(p => p).ToDictionary(p => p, p => Hash(File.ReadAllBytes(p))),
                openingConfigHash = Hash(File.ReadAllBytes("Assets/_Anthropocene/Resources/OpeningConfig.json")),
                resourceConfigHash = Hash(File.ReadAllBytes("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json")),
                totals = new[] { Totals("fixed20"), Totals("dynamic20_15_10") }, wallSeconds = Clock.Elapsed.TotalSeconds };
            Write("summary.json", results); Write("checks.json", Checks); Write("camps.json", CampRows);
            Console.WriteLine("COMPLETE " + JsonSerializer.Serialize(new { Seed, results.totals, results.maxEcologicalBalanceRelativeError, results.wallSeconds }, Json));
        }
        catch (Exception e)
        {
            Write("failure.json", new { status = "failed", Seed, baseline = Baseline, Stage, Arm, elapsedYears = Current == null ? -1 : Years(Current),
                exception = e.ToString(), lastSnapshot = Rows.LastOrDefault(), maxEcologicalBalanceRelativeError = MaxBalanceError,
                rejectedSubmittedOrders = Rejections, wallSeconds = Clock.Elapsed.TotalSeconds });
            Write("trajectory.json", Rows); Write("camps.json", CampRows); Write("checks.json", Checks);
            Console.Error.WriteLine(e); System.Environment.ExitCode = 1;
        }
    }
}
