using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Anthropocene.Simulation;

internal static class DamageDiagnostic
{
    const int TargetTile = 6605;
    const double FirstTurnCatch = 1241.524634;
    const double LateTurnCatch = 684.920691;
    const double Epsilon = 1e-12;

    public static void RunIfTargetWorld(WorldState w)
    {
        if (w == null || w.Seed != 1729 || TargetTile < 0 || TargetTile >= w.Tiles.Length) return;
        Directory.CreateDirectory("SweepResults");
        string path = Path.Combine("SweepResults", "damage_diagnostic.csv");
        using var output = new StreamWriter(path, false);
        output.WriteLine("case,season,weight,temperature,productivity_proxy,biomass,biomass_rate,managed_flow,safe_rate,finite_rate,realized_rate,seasonal_realized,utilization,overuse,damage_component,recovery_per_day,damage_per_day,condition_end,catch,impact");

        foreach (var tuple in new[]
        {
            (Name: "first_turn_impact1", Catch: FirstTurnCatch, Impact: 1.0),
            (Name: "first_turn_impact05", Catch: FirstTurnCatch, Impact: 0.5),
            (Name: "late_turn_impact1", Catch: LateTurnCatch, Impact: 1.0),
            (Name: "late_turn_impact05", Catch: LateTurnCatch, Impact: 0.5),
            (Name: "no_fishing", Catch: 0.0, Impact: 1.0)
        })
            Diagnose(w, output, tuple.Name, tuple.Catch, tuple.Impact);
    }

    static void Diagnose(WorldState w, StreamWriter output, string name, double catchAmount, double impact)
    {
        var tile = w.Tiles[TargetTile];
        double start = w.Days, days = w.Config.TurnDays;
        var opening = LazyEcology.ProjectEcology(w, TargetTile, start);
        var representative = LazyEcology.MeanView(w, tile, opening, days);
        var exposure = new SeasonExposure(start, days, w.Config.BaseDays);
        double realizedRate = catchAmount / days, weighted = 0;
        var rows = new (double weight,double temperature,double biomass,double rate,double managed,double safe,double finite,double seasonal,double utilization,double overuse,double component)[4];

        for (int season = 0; season < 4; season++)
        {
            double weight = exposure.At(season) / days;
            var profile = new EcologyCapacity(w, tile, representative, season);
            double safe = EcologyMath.SustainableHarvestRate(profile.Biomass, profile.BiomassRate,
                w.Config.SustainableProductionFraction, profile.ManagedFlow);
            double finite = profile.Biomass / Math.Min(w.Config.BaseDays, days)
                + profile.Biomass * profile.BiomassRate + profile.ManagedFlow;
            double seasonal = Math.Min(realizedRate, finite);
            double utilization = seasonal <= 0 ? 0 : seasonal / Math.Max(Epsilon, safe);
            double overuse = Math.Max(0, utilization - w.Config.SafeUseRatio);
            double component = weight * Math.Pow(overuse, w.Config.PressureExponent);
            weighted += component;
            rows[season] = (weight, tile.Seasons[season].Temperature, profile.Biomass,
                profile.BiomassRate, profile.ManagedFlow, safe, finite, seasonal, utilization, overuse, component);
        }

        double damagePerDay = w.Config.AquaticUseDamagePerYear * impact * weighted / w.Config.DaysPerYear;
        double recovery = AquaticEcology.ConditionRecoveryPerDay(tile);
        var v = opening;
        LazyEcology.RecoverCondition(w, tile, ref v, days, damagePerDay);

        for (int season = 0; season < 4; season++)
        {
            var r = rows[season];
            double productivity = AquaticEcology.Productivity(w, tile, season);
            Write(output, name, season, r.weight, r.temperature, productivity, r.biomass, r.rate, r.managed,
                r.safe, r.finite, realizedRate, r.seasonal, r.utilization, r.overuse, r.component,
                recovery, damagePerDay, v.AquaticCondition, catchAmount, impact);
        }
        Write(output, name, -1, 1, tile.Seasons.Average(s => s.Temperature), 0, 0, 0, 0, 0, 0,
            realizedRate, catchAmount / days, 0, 0, weighted, recovery, damagePerDay,
            v.AquaticCondition, catchAmount, impact);
    }

    static void Write(StreamWriter output, params object[] cells)
    {
        string Cell(object o)
        {
            var s = o is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : o?.ToString() ?? "";
            return """ + s.Replace(""", """") + """;
        }
        output.WriteLine(string.Join(",", cells.Select(Cell)));
    }
}
