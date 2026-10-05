using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class DamageMain
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    static readonly int[] Seeds = { 0, 1, 2, 3, 4, 41, 83, 1729, 2718, 9001 };
    static readonly double[] Loads = { .25, .50, 1.00 };
    const double Epsilon = 1e-12;

    sealed class Result
    {
        public int Seed, Tile;
        public EnvironmentKind Environment;
        public double Load, Request, Catch, HorizonSafe, Condition, MinTemp, MaxTemp, MinProductivity, MeanProductivity;
        public double MaxSeasonalUtilization, MinSeasonalSafe, MeanSeasonalSafe, RecoveryPerDay, DamagePerDay;
    }

    static SimulationConfig Config()
    {
        var c = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"), Json);
        c.Width = 128; c.Height = 64; c.Players = 8; c.PlateCount = 12; c.StartSeparation = 8;
        c.OpeningCompatibilityEffects = false; c.FissionEnabled = false; c.ImmigrationEnabled = false; c.TradeEnabled = false;
        return c;
    }

    static void Main()
    {
        Directory.CreateDirectory("SweepResults");
        var ro = JsonSerializer.Deserialize<ResourceGenerationOptions>(
            File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"), Json);

        var results = new List<Result>();
        foreach (int seed in Seeds)
        {
            Console.WriteLine("Generating diagnostic world " + seed);
            var w = new WorldGenerator().Generate(seed, Config(), ro);
            foreach (var tile in w.Tiles.Where(t => t.Environment == EnvironmentKind.Lake || t.Environment == EnvironmentKind.Shallows))
            {
                var opening = LazyEcology.ProjectEcology(w, tile.Id, w.Days);
                var representative = LazyEcology.MeanView(w, tile, opening, w.Config.TurnDays);
                var horizon = new HorizonEcologyCapacity(w, tile, representative, w.Days, w.Config.TurnDays);
                double safeAmount = horizon.SafeRate * w.Config.TurnDays;
                if (!(safeAmount > 1e-8)) continue;
                foreach (double load in Loads)
                    results.Add(Evaluate(w, tile, load, safeAmount));
            }
        }

        WriteTiles(results);
        WriteSummary(results);
        WriteTemperatureBins(results);
        Console.WriteLine("GENERAL_DAMAGE_DIAGNOSTIC_DONE rows=" + results.Count);
    }

    static Result Evaluate(WorldState w, Tile tile, double load, double horizonSafe)
    {
        double days = w.Config.TurnDays;
        double request = horizonSafe * load;
        var source = EcologicalPressure.Integrate(w, tile.Id,
            new[] { new Demand { Tile = tile.Id, Entity = 1, Kind = "Fishing", Food = request, ImpactMultiplier = 1 } },
            w.Days, days, projectWood: false);
        double catchAmount = source.Transfers.Sum(x => x.Food);
        double realizedRate = catchAmount / days;

        var opening = LazyEcology.ProjectEcology(w, tile.Id, w.Days);
        var representative = LazyEcology.MeanView(w, tile, opening, days);
        var exposure = new SeasonExposure(w.Days, days, w.Config.BaseDays);
        double maxUtil = 0, minSafe = double.PositiveInfinity, weightedSafe = 0, weightedDamage = 0;
        var productivities = new double[4];

        for (int season = 0; season < 4; season++)
        {
            double weight = exposure.At(season) / days;
            var p = new EcologyCapacity(w, tile, representative, season);
            double safe = EcologyMath.SustainableHarvestRate(p.Biomass, p.BiomassRate,
                w.Config.SustainableProductionFraction, p.ManagedFlow);
            double finite = p.Biomass / Math.Min(w.Config.BaseDays, days)
                + p.Biomass * p.BiomassRate + p.ManagedFlow;
            double seasonal = Math.Min(realizedRate, finite);
            double util = seasonal <= 0 ? 0 : seasonal / Math.Max(Epsilon, safe);
            double over = Math.Max(0, util - w.Config.SafeUseRatio);
            maxUtil = Math.Max(maxUtil, util);
            minSafe = Math.Min(minSafe, safe);
            weightedSafe += weight * safe;
            weightedDamage += weight * Math.Pow(over, w.Config.PressureExponent);
            productivities[season] = AquaticEcology.Productivity(w, tile, season);
        }

        double recovery = AquaticEcology.ConditionRecoveryPerDay(tile);
        double damage = w.Config.AquaticUseDamagePerYear * weightedDamage / w.Config.DaysPerYear;
        return new Result
        {
            Seed = w.Seed, Tile = tile.Id, Environment = tile.Environment, Load = load,
            Request = request, Catch = catchAmount, HorizonSafe = horizonSafe, Condition = source.View.AquaticCondition,
            MinTemp = tile.Seasons.Min(s => (double)s.Temperature), MaxTemp = tile.Seasons.Max(s => (double)s.Temperature),
            MinProductivity = productivities.Min(), MeanProductivity = productivities.Average(),
            MaxSeasonalUtilization = maxUtil, MinSeasonalSafe = minSafe, MeanSeasonalSafe = weightedSafe,
            RecoveryPerDay = recovery, DamagePerDay = damage
        };
    }

    static void WriteTiles(List<Result> rs)
    {
        using var w = new StreamWriter("SweepResults/general_condition_tiles.csv", false);
        w.WriteLine("seed,tile,environment,load,horizon_safe_amount,requested,realized_catch,catch_over_horizon_safe,condition_end,min_temp,max_temp,min_productivity,mean_productivity,max_seasonal_utilization,min_seasonal_safe_rate,mean_seasonal_safe_rate,recovery_per_day,damage_per_day");
        foreach (var r in rs.OrderBy(x => x.Environment).ThenBy(x => x.Seed).ThenBy(x => x.Tile).ThenBy(x => x.Load))
            w.WriteLine(string.Join(",", new[]
            {
                r.Seed.ToString(CultureInfo.InvariantCulture), r.Tile.ToString(CultureInfo.InvariantCulture), r.Environment.ToString(), F(r.Load),
                F(r.HorizonSafe), F(r.Request), F(r.Catch), F(r.Catch / r.HorizonSafe), F(r.Condition), F(r.MinTemp), F(r.MaxTemp),
                F(r.MinProductivity), F(r.MeanProductivity), F(r.MaxSeasonalUtilization), F(r.MinSeasonalSafe),
                F(r.MeanSeasonalSafe), F(r.RecoveryPerDay), F(r.DamagePerDay)
            }));
    }

    static void WriteSummary(List<Result> rs)
    {
        using var w = new StreamWriter("SweepResults/general_condition_summary.csv", false);
        w.WriteLine("environment,load,n,condition_lt_0_5,condition_lt_0_1,condition_lt_0_01,condition_lt_0_001,median_condition,p10_condition,min_condition,median_max_seasonal_utilization,p90_max_seasonal_utilization,max_seasonal_utilization,median_min_temp");
        foreach (var env in new[] { EnvironmentKind.Shallows, EnvironmentKind.Lake })
        foreach (double load in Loads)
        {
            var g = rs.Where(x => x.Environment == env && Math.Abs(x.Load - load) < 1e-12).ToArray();
            if (g.Length == 0) continue;
            w.WriteLine(string.Join(",", new[]
            {
                env.ToString(), F(load), g.Length.ToString(CultureInfo.InvariantCulture),
                Count(g,.5), Count(g,.1), Count(g,.01), Count(g,.001),
                F(Q(g.Select(x=>x.Condition),.5)), F(Q(g.Select(x=>x.Condition),.1)), F(g.Min(x=>x.Condition)),
                F(Q(g.Select(x=>x.MaxSeasonalUtilization),.5)), F(Q(g.Select(x=>x.MaxSeasonalUtilization),.9)),
                F(g.Max(x=>x.MaxSeasonalUtilization)), F(Q(g.Select(x=>x.MinTemp),.5))
            }));
        }
    }

    static void WriteTemperatureBins(List<Result> rs)
    {
        using var w = new StreamWriter("SweepResults/general_condition_temperature_bins.csv", false);
        w.WriteLine("environment,load,min_temp_bin,n,condition_lt_0_1,condition_lt_0_01,median_condition,median_max_utilization");
        foreach (var env in new[] { EnvironmentKind.Shallows, EnvironmentKind.Lake })
        foreach (double load in Loads)
        foreach (var bin in new[] { "<=-5", "-5..0", "0..5", "5..10", ">10" })
        {
            var g = rs.Where(x => x.Environment == env && Math.Abs(x.Load-load)<1e-12 && Bin(x.MinTemp)==bin).ToArray();
            if (g.Length==0) continue;
            w.WriteLine(string.Join(",", new[] { env.ToString(), F(load), bin, g.Length.ToString(CultureInfo.InvariantCulture),
                Count(g,.1), Count(g,.01), F(Q(g.Select(x=>x.Condition),.5)), F(Q(g.Select(x=>x.MaxSeasonalUtilization),.5)) }));
        }
    }

    static string Bin(double t) => t <= -5 ? "<=-5" : t <= 0 ? "-5..0" : t <= 5 ? "0..5" : t <= 10 ? "5..10" : ">10";
    static string Count(Result[] g, double threshold) => g.Count(x => x.Condition < threshold).ToString(CultureInfo.InvariantCulture);
    static double Q(IEnumerable<double> values, double q)
    {
        var a = values.OrderBy(x => x).ToArray(); if (a.Length == 0) return double.NaN;
        double p = q * (a.Length - 1); int lo = (int)Math.Floor(p), hi = (int)Math.Ceiling(p);
        return lo == hi ? a[lo] : a[lo] + (a[hi] - a[lo]) * (p - lo);
    }
    static string F(double x) => x.ToString("R", CultureInfo.InvariantCulture);
}
