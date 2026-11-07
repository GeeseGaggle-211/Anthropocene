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
    private const int Iterations = 40;
    private const int Tail = 8;
    private const int SamplesPerWorldCohort = 16;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    private static readonly List<Row> Rows = new List<Row>();
    private static readonly List<object> WorldReports = new List<object>();
    private static readonly List<object> Profiles = new List<object>();
    private static long Integrations;
    private static double MaxConservationRelativeError;
    private static double MaxInitialStateRelativeDifference;
    private static double MaxTailRelativeRange;

    private sealed class Site
    {
        public int Center;
        public int[] Tiles;
        public double BiomassScore, MeanSuitability, MeanTemperature;
        public int RiverTiles;
    }
    private sealed class Cell
    {
        public double Food, CropFood, HarvestWorkers, MaintenanceWorkers;
        public double EndingBiomass, EndingFood, InitialDifference, TailRange;
    }
    private sealed class Row
    {
        public int Seed, Center, Tiles, RiverTiles, Phase;
        public string Cohort;
        public double Q, RetainFraction, MeanSuitability, MeanTemperature, BiomassScore;
        public double FoodPerTurn, FoodPerYear, FoodOnlyPopulation, CropComponentPopulation;
        public double HarvestWorkers, MaintenanceWorkers, MinimumFarmWorkers, FarmLaborFraction;
        public bool LaborFeasible;
        public double EndingBiomass, EndingFood, InitialDifference, TailRange;
    }
    private static double Quantile(IEnumerable<double> source, double p)
    {
        var a = source.OrderBy(x => x).ToArray();
        if (a.Length == 0) throw new InvalidOperationException("Empty quantile.");
        double x = (a.Length - 1) * p;
        int k = (int)Math.Floor(x);
        return a[k] + (a[Math.Min(k + 1, a.Length - 1)] - a[k]) * (x - k);
    }
    private static object Distribution(IEnumerable<double> source)
    {
        var a = source.ToArray();
        return new { n = a.Length, min = a.Min(), p10 = Quantile(a, .1), p25 = Quantile(a, .25), median = Quantile(a, .5), p75 = Quantile(a, .75), p90 = Quantile(a, .9), max = a.Max() };
    }
    private static Site Describe(WorldState w, int id)
    {
        var ids = w.Map.Radius(id, 1).Where(i => CultivationEconomy.Cultivable(w.Tiles[i])).OrderBy(i => i).ToArray();
        if (ids.Length != 7) return null;
        return new Site {
            Center = id, Tiles = ids,
            BiomassScore = ids.Sum(i => w.Tiles[i].Seasons.Average(s => s.MaxPotentialFood)),
            MeanSuitability = ids.Average(i => w.Tiles[i].Seasons.Average(s => EcologyMath.CropSuitability(w.Config, s.Moisture))),
            MeanTemperature = ids.Average(i => w.Tiles[i].Seasons.Average(s => s.Temperature)),
            RiverTiles = ids.Count(i => w.Tiles[i].River)
        };
    }
    private static Site[] Sample(Site[] sites)
    {
        var sorted = sites.OrderBy(s => s.BiomassScore).ThenBy(s => s.Center).ToArray();
        int count = Math.Min(SamplesPerWorldCohort, sorted.Length);
        return Enumerable.Range(0, count).Select(k => sorted[count == 1 ? 0 : (int)Math.Round((sorted.Length - 1.0) * k / (count - 1))]).ToArray();
    }
    private static double BalanceError(double start, double recharge, double harvest, double overflow, double end)
    {
        return Math.Abs(start + recharge - harvest - overflow - end) / Math.Max(1, Math.Max(start + recharge, harvest + overflow + end));
    }
    private static Cell Iterate(WorldState w, Tile tile, double q, double retain, int phase, bool empty)
    {
        var cfg = w.Config;
        double capital = q == 0 ? 0 : Math.Pow(q / cfg.ReferenceIntensity, 2);
        double duration = cfg.TurnDays, years = duration / cfg.DaysPerYear;
        var v = new EcologyView(tile) { CultivationCapital = capital, LandCondition = 1, Days = phase * cfg.BaseDays };
        if (empty) { v.Food = 0; v.PotentialFood = 0; }
        double foodSum = 0, cropSum = 0, harvestWork = 0, minFood = double.PositiveInfinity, maxFood = 0;
        double maintenance = 0;
        for (int turn = 0; turn < Iterations; turn++)
        {
            int season = (int)Math.Floor(v.Days / cfg.BaseDays) % 4;
            var cap = new EcologyCapacity(w, tile, v, season, 1);
            double cropShare = cap.Food <= 0 ? 0 : Math.Max(0, cap.Food - cap.WildFood) / cap.Food;
            double requested = Math.Max(0, v.Food) * (1 - retain);
            var claim = new Demand { Tile = tile.Id, Entity = 0, Kind = "Cultivation", Food = requested, ImpactMultiplier = 1 };
            var result = EcologicalPressure.Integrate(w, tile.Id, new[] { claim }, v.Days, duration,
                averageCultivationCapital: capital, endingCultivationCapital: capital,
                operatingFraction: 1, projectWood: false, openingEcology: v);
            Integrations++;
            var balance = result.Balance;
            double error = Math.Max(BalanceError(balance.StartingFood, balance.FoodRecharge, balance.FoodHarvest, balance.FoodOverflow, balance.EndingFood),
                BalanceError(balance.StartingBiomass, balance.BiomassRecharge, balance.BiomassHarvest, balance.BiomassOverflow, balance.EndingBiomass));
            MaxConservationRelativeError = Math.Max(MaxConservationRelativeError, error);
            if (error > 1e-8 || result.View.Food < -1e-8 || result.View.PotentialFood + 1e-7 < result.View.Food)
                throw new InvalidOperationException("Native stock accounting failed at tile " + tile.Id + ", error " + error);
            if (Math.Abs(result.View.CultivationCapital - capital) > 1e-8)
                throw new InvalidOperationException("Fixed capital was not preserved.");
            double food = result.Transfers.Sum(t => t.Food);
            if (turn >= Iterations - Tail)
            {
                foodSum += food;
                cropSum += food * cropShare;
                double efficiency = cfg.CropHarvestingOnly ? 1 - cropShare + cropShare / cfg.FarmerProductivity : 1 / cfg.FarmerProductivity;
                harvestWork += food * efficiency / (cfg.GatherPerPerson * duration / cfg.BaseDays);
                minFood = Math.Min(minFood, food); maxFood = Math.Max(maxFood, food);
            }
            v = result.View;
        }
        var originalVegetation = tile.CurrentVegetation;
        tile.CurrentVegetation = v.CurrentVegetation;
        CultivationEconomy.Factors(cfg, tile, out _, out double terrainMaintenance);
        tile.CurrentVegetation = originalVegetation;
        maintenance = cfg.Neolithic.MaintenanceWorkersPerTileE(cfg.MaintenanceCoefficient) * terrainMaintenance * CultivationEconomy.EffortForCapital(cfg, capital);
        return new Cell { Food = foodSum / Tail, CropFood = cropSum / Tail, HarvestWorkers = harvestWork / Tail,
            MaintenanceWorkers = maintenance, EndingBiomass = v.PotentialFood, EndingFood = v.Food,
            TailRange = (maxFood - minFood) / Math.Max(1, maxFood) };
    }
    private static Cell EvaluateCell(WorldState w, int id, double q, double retain, int phase)
    {
        var tile = w.Tiles[id];
        double oldCapital = tile.CultivationCapital, oldOperation = tile.CultivationOperatingFraction;
        int oldOperator = tile.CultivationOperator;
        try
        {
            tile.CultivationCapital = q == 0 ? 0 : Math.Pow(q / w.Config.ReferenceIntensity, 2);
            tile.CultivationOperatingFraction = 1;
            tile.CultivationOperator = 0;
            var low = Iterate(w, tile, q, retain, phase, true);
            var high = Iterate(w, tile, q, retain, phase, false);
            low.InitialDifference = Math.Abs(low.Food - high.Food) / Math.Max(1, Math.Max(low.Food, high.Food));
            low.TailRange = Math.Max(low.TailRange, high.TailRange);
            MaxInitialStateRelativeDifference = Math.Max(MaxInitialStateRelativeDifference, low.InitialDifference);
            MaxTailRelativeRange = Math.Max(MaxTailRelativeRange, low.TailRange);
            if (low.InitialDifference > 1e-6 || low.TailRange > 1e-6)
                throw new InvalidOperationException("No steady yield at tile " + id + ", q " + q + ", gap " + low.InitialDifference + ", tail " + low.TailRange);
            return low;
        }
        finally { tile.CultivationCapital = oldCapital; tile.CultivationOperatingFraction = oldOperation; tile.CultivationOperator = oldOperator; }
    }
    private static void RunWorld(int seed, string directory)
    {
        var cfg = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"), Json);
        var resources = JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"), Json);
        cfg.Width = 1024; cfg.Height = 512; cfg.Players = 80; cfg.Shape = WorldShape.Continents;
        var clock = Stopwatch.StartNew();
        Console.WriteLine("GENERATING seed=" + seed);
        var w = new WorldGenerator().Generate(seed, cfg, resources);
        Console.WriteLine("GENERATED seed=" + seed + " seconds=" + clock.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture));
        var all = w.Tiles.Where(t => t.River && CultivationEconomy.Cultivable(t)).Select(t => Describe(w, t.Id)).Where(s => s != null).ToArray();
        if (all.Length == 0) throw new InvalidOperationException("No seven-tile river footprints for seed " + seed);
        var moist = all.Where(s => s.MeanSuitability >= .8).ToArray();
        double threshold = moist.Length == 0 ? 0 : Quantile(moist.Select(s => s.BiomassScore), .75);
        var fertile = moist.Where(s => s.BiomassScore >= threshold).ToArray();
        var dry = all.Where(s => w.Tiles[s.Center].Environment == EnvironmentKind.Desert && s.MeanTemperature >= 18).ToArray();
        var chosen = new List<(string cohort, Site site)>();
        chosen.AddRange(Sample(all).Select(s => ("all_river", s)));
        chosen.AddRange(Sample(fertile).Select(s => ("fertile_upper_quartile", s)));
        chosen.AddRange(Sample(dry).Select(s => ("warm_desert_river", s)));
        if (seed == 11)
        {
            int id = PlaytestScenarios.Tile(w, 1);
            var preset = Describe(w, id);
            if (preset != null && w.Tiles[id].River) chosen.Add(("preset_desert_river", preset));
        }
        WorldReports.Add(new { seed, world = "1024x512 Continents, 80 founding tribes", riverFootprints = all.Length,
            moistFootprints = moist.Length, fertileFootprints = fertile.Length, warmDesertFootprints = dry.Length,
            fertileBiomassThreshold = threshold, sampledRows = chosen.Count,
            generationSeconds = clock.Elapsed.TotalSeconds,
            cfg.ReferenceIntensity, cfg.MaintenanceCoefficient, cfg.FarmerProductivity, cfg.GatherPerPerson,
            cfg.TurnDays, cfg.BaseDays, cfg.DaysPerYear, cfg.Consumption, cfg.CultivationRegenFlux,
            cfg.LandBiomassMultiplier, cfg.LandFoodRegeneration,
            cfg.Neolithic.PrimitiveAgricultureSupportedIntensity, cfg.Neolithic.CultivationMarginalEffortMultiplierPerTenth });
        var profiles = chosen.SelectMany(c => c.site.Tiles).Distinct().OrderBy(id => id).Select(id => {
            var t = w.Tiles[id];
            return new { id, t.River, t.Environment, t.CurrentVegetation, t.NaturalVegetationPotential,
                t.Elevation, t.LandCondition, t.Food, t.PotentialFood,
                seasons = t.Seasons.Select(s => new { s.Temperature, s.Moisture, s.MaxFood, s.MaxPotentialFood, s.FoodRecoveryPerDay, s.BiomassRecoveryPerDay }).ToArray() };
        }).ToArray();
        File.WriteAllText(Path.Combine(directory, "profiles-" + seed + ".json"), JsonSerializer.Serialize(new { seed, profiles, sites = chosen.Select(c => new { cohort = c.cohort, site = c.site }) }, Json));
        var cache = new Dictionary<string, Cell>();
        var scenarios = new List<(double q, double retain, int phase)>();
        foreach (double q in new[] { 0.0, .25, .35, .4, .45, .5 }) scenarios.Add((q, 0, 0));
        scenarios.Add((.4, .2, 0)); scenarios.Add((.4, .5, 0));
        for (int phase = 1; phase <= 3; phase++) scenarios.Add((.4, 0, phase));
        foreach (var choice in chosen)
        foreach (var scenario in scenarios)
        {
            var cells = new List<Cell>();
            foreach (int id in choice.site.Tiles)
            {
                string key = id + ":" + scenario.q.ToString("R", CultureInfo.InvariantCulture) + ":" + scenario.retain.ToString("R", CultureInfo.InvariantCulture) + ":" + scenario.phase;
                if (!cache.TryGetValue(key, out var cell)) { cell = EvaluateCell(w, id, scenario.q, scenario.retain, scenario.phase); cache.Add(key, cell); }
                cells.Add(cell);
            }
            double food = cells.Sum(c => c.Food);
            double population = food / (cfg.Consumption * cfg.TurnDays / cfg.BaseDays);
            double harvestWorkers = cells.Sum(c => c.HarvestWorkers), maintenanceWorkers = cells.Sum(c => c.MaintenanceWorkers);
            // Native maintenance and harvest are overlapping work, not additive.
            double farmWorkers = Math.Max(harvestWorkers, maintenanceWorkers);
            Rows.Add(new Row { Seed = seed, Cohort = choice.cohort, Center = choice.site.Center,
                Tiles = choice.site.Tiles.Length, RiverTiles = choice.site.RiverTiles, Q = scenario.q, RetainFraction = scenario.retain, Phase = scenario.phase,
                MeanSuitability = choice.site.MeanSuitability, MeanTemperature = choice.site.MeanTemperature, BiomassScore = choice.site.BiomassScore,
                FoodPerTurn = food, FoodPerYear = food / (cfg.TurnDays / cfg.DaysPerYear), FoodOnlyPopulation = population,
                CropComponentPopulation = cells.Sum(c => c.CropFood) / (cfg.Consumption * cfg.TurnDays / cfg.BaseDays),
                HarvestWorkers = harvestWorkers, MaintenanceWorkers = maintenanceWorkers, MinimumFarmWorkers = farmWorkers,
                FarmLaborFraction = population > 0 ? farmWorkers / population : 0, LaborFeasible = farmWorkers <= population + 1e-8,
                EndingBiomass = cells.Sum(c => c.EndingBiomass), EndingFood = cells.Sum(c => c.EndingFood),
                InitialDifference = cells.Max(c => c.InitialDifference), TailRange = cells.Max(c => c.TailRange) });
        }
        Console.WriteLine("DONE seed=" + seed + " sites=" + chosen.Count + " uniqueTileScenarios=" + cache.Count + " elapsedSeconds=" + clock.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture));
    }
    private static string Sha(string path)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    private static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string directory = args.Length > 0 ? args[0] : "TestResults/RiverCapacitySweep";
        Directory.CreateDirectory(directory);
        var clock = Stopwatch.StartNew();
        foreach (int seed in new[] { 11, 23, 47, 101, 1729 }) { RunWorld(seed, directory); GC.Collect(); }
        if (!Rows.Any(r => r.Cohort == "fertile_upper_quartile")) throw new InvalidOperationException("No fertile cohort.");
        File.WriteAllText(Path.Combine(directory, "rows.json"), JsonSerializer.Serialize(Rows, Json));
        var fields = typeof(Row).GetFields();
        using (var writer = new StreamWriter(Path.Combine(directory, "rows.csv")))
        {
            writer.WriteLine(string.Join(",", fields.Select(f => f.Name)));
            foreach (var row in Rows) writer.WriteLine(string.Join(",", fields.Select(f => Convert.ToString(f.GetValue(row), CultureInfo.InvariantCulture))));
        }
        var groups = Rows.GroupBy(r => new { r.Cohort, r.Q, r.RetainFraction, r.Phase }).Select(g => new {
            g.Key, population = Distribution(g.Select(r => r.FoodOnlyPopulation)),
            cropComponentPopulation = Distribution(g.Select(r => r.CropComponentPopulation)),
            farmLaborFraction = Distribution(g.Select(r => r.FarmLaborFraction)),
            maintenanceWorkers = Distribution(g.Select(r => r.MaintenanceWorkers)),
            laborInfeasible = g.Count(r => !r.LaborFeasible)
        }).ToArray();
        var summary = new {
            baseline = Baseline,
            method = "Unchanged native C# EcologicalPressure.Integrate. Seven-tile radius-one footprints. Fixed mature q and full maintenance; no field expansion. Repeated opening-stock harvest followed by native land regeneration. 40 twenty-year horizons from both empty and generated stocked states; last eight horizons form the steady receipt. RetainFraction is the share of opening accessible stock left unharvested. Native maintenance and crop-share harvest labor overlap. No tools are granted, no food outside the footprint, no trade/herds/fishing, no clothing/shelter/material work, no demography or social movement. This is a physical food ceiling, not an endogenous population trajectory.",
            cohortDefinition = "All river: river-bearing center and seven cultivable tiles. Fertile: footprint mean seasonal CropSuitability >= 0.8, then upper quartile of natural mean total biomass within that eligible river cohort in each world. Sixteen deterministic biomass-quantile sample sites per cohort/world, including endpoints; footprints may overlap and statistics are descriptive, not confidence intervals. Warm desert river is a separately disclosed comparison. Phase zero matches the production opening; phases 1-3 test boundary-season sensitivity without changing turn duration.",
            units = "Population = steady food per turn / (Consumption * TurnDays / BaseDays); production defaults are checked in world metadata. CropComponentPopulation uses the source opening-capacity crop fraction; q=0 comparator is separately provided.",
            checks = new { Integrations, MaxConservationRelativeError, MaxInitialStateRelativeDifference, MaxTailRelativeRange, elapsedSeconds = clock.Elapsed.TotalSeconds },
            sourceHashes = Directory.GetFiles("Assets/_Anthropocene/Simulation", "*.cs").OrderBy(p => p).ToDictionary(p => p, p => Sha(p)),
            openingConfigSha256 = Sha("Assets/_Anthropocene/Resources/OpeningConfig.json"),
            resourceConfigSha256 = Sha("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"),
            worlds = WorldReports,
            groups
        };
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summary, Json));
        Console.WriteLine("SUMMARY_BEGIN");
        Console.WriteLine(JsonSerializer.Serialize(new { summary.baseline, summary.checks, summary.worlds,
            central = groups.Where(g => g.Key.Q == .4 && g.Key.Phase == 0),
            fertileSensitivity = groups.Where(g => g.Key.Cohort == "fertile_upper_quartile") }, Json));
        Console.WriteLine("SUMMARY_END");
    }
}
