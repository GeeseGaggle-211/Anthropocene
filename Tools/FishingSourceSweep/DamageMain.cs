using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class DamageMain
{
    const int TargetTile = 6605;
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

    static SimulationConfig Config()
    {
        var c = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"), Json);
        c.Width = 128; c.Height = 64; c.Players = 8; c.PlateCount = 12; c.StartSeparation = 8;
        c.OpeningCompatibilityEffects = false; c.FissionEnabled = false; c.ImmigrationEnabled = false; c.TradeEnabled = false;
        return c;
    }

    static string F(double x) => x.ToString("R", CultureInfo.InvariantCulture);

    static void Main()
    {
        Directory.CreateDirectory("SweepResults");
        var ro = JsonSerializer.Deserialize<ResourceGenerationOptions>(
            File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"), Json);
        var w = new WorldGenerator().Generate(1729, Config(), ro);
        var t = w.Tiles[TargetTile];
        if (t.Environment != EnvironmentKind.Lake)
            throw new Exception("Target tile no longer matches the sweep fixture: " + t.Environment);

        using var output = new StreamWriter("SweepResults/damage_diagnostic.csv", false);
        output.WriteLine("case,season,temperature,productivity,weight,biomass,biomass_rate,food_capacity,food_rate,managed_flow,safe_rate,finite_rate,realized_rate,seasonal_realized,utilization,overuse,damage_component,recovery_per_day,damage_per_day,predicted_condition_end,source_condition_end,source_catch,horizon_safe_rate,horizon_safe_amount,requested_catch,impact");

        foreach (var test in new[]
        {
            (Name:"no_fishing", Catch:0.0, Impact:1.0),
            (Name:"first_turn_impact1", Catch:1241.524634, Impact:1.0),
            (Name:"first_turn_impact05", Catch:1241.524634, Impact:0.5),
            (Name:"late_turn_impact1", Catch:684.920691, Impact:1.0),
            (Name:"late_turn_impact05", Catch:684.920691, Impact:0.5)
        })
            Diagnose(w, t, test.Name, test.Catch, test.Impact, output);

        Console.WriteLine("DAMAGE_DIAGNOSTIC_DONE tile=" + TargetTile);
    }

    static void Diagnose(WorldState w, Tile tile, string name, double requestedCatch, double impact, StreamWriter output)
    {
        double start = w.Days, days = w.Config.TurnDays;
        var opening = LazyEcology.ProjectEcology(w, tile.Id, start);
        var representative = LazyEcology.MeanView(w, tile, opening, days);
        var horizon = new HorizonEcologyCapacity(w, tile, representative, start, days);
        var exposure = new SeasonExposure(start, days, w.Config.BaseDays);
        double realizedRate = requestedCatch / days;
        double weighted = 0;
        var rows = new (double temp,double prod,double weight,double biomass,double biomassRate,double food,double foodRate,double managed,double safe,double finite,double seasonal,double util,double over,double component)[4];

        for (int season = 0; season < 4; season++)
        {
            double weight = exposure.At(season) / days;
            var p = new EcologyCapacity(w, tile, representative, season);
            double safe = EcologyMath.SustainableHarvestRate(p.Biomass, p.BiomassRate,
                w.Config.SustainableProductionFraction, p.ManagedFlow);
            double finite = p.Biomass / Math.Min(w.Config.BaseDays, days)
                + p.Biomass * p.BiomassRate + p.ManagedFlow;
            double seasonal = Math.Min(realizedRate, finite);
            double util = seasonal <= 0 ? 0 : seasonal / Math.Max(1e-12, safe);
            double over = Math.Max(0, util - w.Config.SafeUseRatio);
            double component = weight * Math.Pow(over, w.Config.PressureExponent);
            weighted += component;
            rows[season] = (tile.Seasons[season].Temperature, AquaticEcology.Productivity(w, tile, season),
                weight, p.Biomass, p.BiomassRate, p.Food, p.FoodRate, p.ManagedFlow,
                safe, finite, seasonal, util, over, component);
        }

        double recovery = AquaticEcology.ConditionRecoveryPerDay(tile);
        double damage = w.Config.AquaticUseDamagePerYear * impact * weighted / w.Config.DaysPerYear;
        double predicted = EcologyMath.Condition(opening.AquaticCondition, 1, recovery, damage, days);

        var claims = requestedCatch > 0
            ? new[] { new Demand { Tile = tile.Id, Entity = 1, Kind = "Fishing", Food = requestedCatch, ImpactMultiplier = impact } }
            : Array.Empty<Demand>();
        var source = EcologicalPressure.Integrate(w, tile.Id, claims, start, days, projectWood:false);
        double actualCatch = source.Transfers.Sum(x => x.Food);

        for (int season = 0; season < 4; season++)
        {
            var r = rows[season];
            output.WriteLine(string.Join(",", new[]
            {
                name, season.ToString(CultureInfo.InvariantCulture), F(r.temp), F(r.prod), F(r.weight),
                F(r.biomass), F(r.biomassRate), F(r.food), F(r.foodRate), F(r.managed),
                F(r.safe), F(r.finite), F(realizedRate), F(r.seasonal), F(r.util), F(r.over), F(r.component),
                F(recovery), F(damage), F(predicted), F(source.View.AquaticCondition), F(actualCatch),
                F(horizon.SafeRate), F(horizon.SafeRate * days), F(requestedCatch), F(impact)
            }));
        }
        output.WriteLine(string.Join(",", new[]
        {
            name, "-1", "", "", "1", "", "", "", "", "", "", "", F(realizedRate), "", "", "", F(weighted),
            F(recovery), F(damage), F(predicted), F(source.View.AquaticCondition), F(actualCatch),
            F(horizon.SafeRate), F(horizon.SafeRate * days), F(requestedCatch), F(impact)
        }));
    }
}
