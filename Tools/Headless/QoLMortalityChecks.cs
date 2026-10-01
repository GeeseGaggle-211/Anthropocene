using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// QoL-dependent mortality (Docs/QoLMortality.md): the closed-form growth ceiling, the single maximum-rate helper shared by the resolver,
// the food-bucket targets and the planner, the Paleolithic exemption, the demographic explanation, old saves and replay.
// Owns its worlds and counters (lanes run fixed checks concurrently).
internal static class QoLMortalityChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("QoL mortality: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-12) => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)), message + " (" + a + " vs " + b + ")");

    private static WorldState Fixture(bool agriculture, double qol, bool enabled = true)
    {
        var w = NeolithicChecks.Fixture(agriculture); var c = w.Camps[0];
        w.Config.AnnualHumanGrowthRate = .01145; w.Config.QoLMortalityEnabled = enabled; c.QoL = qol;
        // A rich land so that food never limits growth: the ceiling alone decides the outcome.
        foreach (var t in w.Tiles) { t.Food = 1000; t.PotentialFood = 8000; for (int s = 0; s < 4; s++) { t.Seasons[s].MaxFood = 1000; t.Seasons[s].MaxPotentialFood = 8000; } }
        return w;
    }
    private static double Years(WorldState w) => w.Config.TurnDays / w.Config.DaysPerYear;

    public static int Run()
    {
        checks = 0; ClosedForm(); Hooks(); Applicability(); Sites(); Report(); Resolution(); Config(); Saves(); Replay();
        Console.WriteLine("PASS: " + checks + " QoL mortality assertions"); return checks;
    }

    private static void ClosedForm()
    {
        foreach (double a in new[] { .5, 1, 2, 4 })
            foreach (double b in new[] { .035, .045, .055 })
            {
                var c = new SimulationConfig { BaseBirthRate = b, QoLMortalityElasticity = a }; double g = c.AnnualHumanGrowthRate, d0 = b - g;
                Near(QoLMortality.CeilingRate(c, 1), g, "r(1) is the pre-existing annual growth rate (a=" + a + ", b0=" + b + ")");
                Near(QoLMortality.DeathRate(c, 1), d0, "d(1) = d0 = b0 - g");
                Near(QoLMortality.DeathRate(c, .5), d0 * Math.Pow(.5, -a), "d(Q) = d0 Q^-a");
                double star = Math.Pow(d0 / b, 1 / a); Near(QoLMortality.ZeroGrowthQoL(c), star, "Q* = (d0/b0)^(1/a)");
                Check(star > 0 && star < 1, "Q* lies inside (0, 1)");
                Near(QoLMortality.CeilingRate(c, star), 0, "r = 0 at Q*", 1e-12);
                Check(QoLMortality.CeilingRate(c, star * .99) == 0 && QoLMortality.CeilingRate(c, star * .5) == 0 && QoLMortality.CeilingRate(c, 0) == 0 && QoLMortality.CeilingRate(c, -1) == 0, "r = 0 below Q* and as Q -> 0");
                Check(QoLMortality.CeilingRate(c, star * 1.01) > 0, "r is positive just above Q*");
                double previous = -1;
                for (double q = star; q <= 1.0000001; q += (1 - star) / 40)
                {
                    double r = QoLMortality.CeilingRate(c, q); Check(r >= previous - 1e-15, "r is non-decreasing in Q"); previous = r;
                }
                Check(QoLMortality.CeilingRate(c, .9) < QoLMortality.CeilingRate(c, .95) || QoLMortality.CeilingRate(c, .9) == 0, "r strictly increases in Q where positive");
            }
        // Higher elasticity means a lower Q*: the ceiling falls more gently.
        var lo = new SimulationConfig { QoLMortalityElasticity = .5 }; var hi = new SimulationConfig { QoLMortalityElasticity = 4 };
        Check(QoLMortality.ZeroGrowthQoL(hi) > QoLMortality.ZeroGrowthQoL(lo), "a larger elasticity raises Q* (mortality grows faster as Q falls)");
    }

    private static void Hooks()
    {
        var w = Fixture(true, .8); var c = w.Camps[0];
        Check(QoLMortality.MortalityFactor(w, c) == 1 && QoLMortality.FertilityFactor(w, c) == 1, "both hook factors are 1 now");
        var cfg = w.Config; double g = cfg.AnnualHumanGrowthRate, b = cfg.BaseBirthRate, d0 = b - g;
        Near(QoLMortality.CeilingRate(cfg, .8, 1, .5), Math.Max(0, b - d0 * .5 * Math.Pow(.8, -cfg.QoLMortalityElasticity)), "a mortality factor multiplies d exactly");
        Near(QoLMortality.CeilingRate(cfg, .8, .9, 1), Math.Max(0, .9 * b - d0 * Math.Pow(.8, -cfg.QoLMortalityElasticity)), "a fertility factor multiplies b0 exactly");
        Near(QoLMortality.CeilingRate(cfg, 1, 1, 1), g, "unit factors leave r(1) = g");
        Check(QoLMortality.CeilingRate(cfg, .9, 1, .5) > QoLMortality.CeilingRate(cfg, .9, 1, 1), "better medicine (lower mortality) raises the ceiling");
        Check(QoLMortality.CeilingRate(cfg, .95, .8, 1) < QoLMortality.CeilingRate(cfg, .95, 1, 1), "lower fertility lowers the ceiling");
    }

    private static void Applicability()
    {
        var neo = Fixture(true, .9); var camp = neo.Camps[0]; var cfg = neo.Config;
        double expected = QoLMortality.CeilingRate(cfg, .9);
        Check(expected > 0 && expected < cfg.AnnualHumanGrowthRate, "fixture: QoL .9 lowers the ceiling without stopping growth");
        Near(QoLMortality.AnnualCeiling(neo, camp), expected, "an economy camp uses its committed QoL");
        Near(QoLMortality.MaximumRate(neo, camp), expected * camp.DemographicGrowthMultiplier, "the maximum rate is the ceiling");
        camp.DemographicGrowthMultiplier = .5; Near(QoLMortality.MaximumRate(neo, camp), expected * .5, "DemographicGrowthMultiplier still multiplies r"); camp.DemographicGrowthMultiplier = 1;
        camp.DeficitTurns = 1; Check(QoLMortality.MaximumRate(neo, camp) == 0, "DeficitTurns > 0 still stops growth"); camp.DeficitTurns = 0;
        Near(QoLMortality.Reduction(neo, camp), cfg.AnnualHumanGrowthRate - expected, "the reduction is r(1) - r(Q)");
        // Paleolithic camps: no goods economy, so their resolved QoL (capped near 0.75) is not a wellbeing signal and growth is unchanged.
        var paleo = Fixture(false, .5); var p = paleo.Camps[0];
        Check(!CultivationEconomy.EconomyEnabled(p), "fixture: a Paleolithic camp has no goods economy");
        Near(QoLMortality.MaximumRate(paleo, p), paleo.Config.AnnualHumanGrowthRate, "a Paleolithic camp keeps the unchanged ceiling at any QoL");
        Check(QoLMortality.Reduction(paleo, p) == 0, "and reports no reduction");
        // Husbandry-only camps have the goods economy too.
        var herd = Fixture(false, .9); var h = herd.Camps[0]; h.Technology = Technology.AnimalHusbandry; h.Technologies[(int)Technology.AnimalHusbandry] = true;
        Near(QoLMortality.AnnualCeiling(herd, h), expected, "a Husbandry camp is covered");
        // Disabled: the ceiling is exactly the previous constant at every QoL.
        var off = Fixture(true, .2, false); Near(QoLMortality.MaximumRate(off, off.Camps[0]), off.Config.AnnualHumanGrowthRate, "disabled leaves the previous ceiling");
        Check(QoLMortality.Reduction(off, off.Camps[0]) == 0, "disabled reports no reduction");
        // Starvation of QoL: below Q* growth is zero.
        var low = Fixture(true, QoLMortality.ZeroGrowthQoL(cfg) * .9); Check(QoLMortality.MaximumRate(low, low.Camps[0]) == 0, "growth stops below Q*");
    }

    // Every consumer of the ceiling reads the same helper: bucket targets, the planner's forecast (including a preview copy) and the resolver.
    private static void Sites()
    {
        var w = Fixture(true, .95); var c = w.Camps[0]; double years = Years(w), intervals = w.Config.GenerationIntervals;
        double rate = QoLMortality.MaximumRate(w, c), population = c.Population * w.Config.Consumption * intervals;
        TurnResolver.FoodBucketTargets(w, c, intervals, years, population, c.Food, out _, out _, out _, out double maxGrowthFood);
        Near(maxGrowthFood, TurnResolver.GrowthFood(population, rate, years), "the food-bucket growth target uses the shared maximum rate");
        var off = Fixture(true, .95, false);
        TurnResolver.FoodBucketTargets(off, off.Camps[0], intervals, years, population, off.Camps[0].Food, out _, out _, out _, out double previousGrowthFood);
        Check(maxGrowthFood < previousGrowthFood, "QoL mortality lowers the planned growth food (" + maxGrowthFood + " < " + previousGrowthFood + ")");
        Near(previousGrowthFood, TurnResolver.GrowthFood(population, off.Config.AnnualHumanGrowthRate, years), "disabled keeps the previous bucket");
        var preview = EconomicPlanner.Preview(c, EconomicIntent.From(c), c.Population);
        Near(preview.QoL, c.QoL, "a planner preview camp carries the committed QoL");
        Near(QoLMortality.MaximumRate(w, preview), rate, "a forecast preview camp sees the resolver's ceiling");
        // Forecast equals resolver: with ample food the planner's resident forecast is the resolver's ending population.
        double forecast = TurnResolver.PlanningResidentPopulation(w, c, 1e9);
        var record = new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>()));
        var d = record.Demographics.Single();
        Check(d.Deaths == 0 && Math.Abs(d.EndingPopulation - forecast) <= 1, "the planner forecast (" + forecast + ") equals the resolver outcome (" + d.EndingPopulation + ")");
        Check(rate > 0 && rate < w.Config.AnnualHumanGrowthRate, "fixture: the shared rate is positive and below the previous ceiling");
        double expectedFinal = 100 * Math.Exp(rate * years);
        Check(Math.Abs(d.EndingPopulation - expectedFinal) <= 1.5, "growth over the turn is exp(r T): " + d.EndingPopulation + " vs " + expectedFinal);
    }

    private static void Report()
    {
        var w = Fixture(true, .9); var c = w.Camps[0];
        var record = new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>()));
        var d = record.Demographics.Single();
        Near(d.QoLMortalityReduction, QoLMortality.CeilingRate(w.Config, 1) - QoLMortality.CeilingRate(w.Config, .9), "the report carries r(1) - r(Q)");
        Near(d.QoL, .9, "and the QoL it was computed at");
        Check(d.Explanation.Contains("Quality of life 0.90") && d.Explanation.Contains("lowered the annual growth ceiling"), "the explanation names the QoL reduction");
        var off = Fixture(true, .9, false); var offRecord = new TurnResolver().Resolve(off, TurnPlanner.Prepare(off, new Dictionary<int, List<Order>>())).Demographics.Single();
        Check(offRecord.QoLMortalityReduction == 0 && !offRecord.Explanation.Contains("Quality of life"), "disabled adds no explanation line");
        var paleo = Fixture(false, .5); var pRecord = new TurnResolver().Resolve(paleo, TurnPlanner.Prepare(paleo, new Dictionary<int, List<Order>>())).Demographics.Single();
        Check(pRecord.QoLMortalityReduction == 0 && !pRecord.Explanation.Contains("Quality of life"), "a Paleolithic camp adds no explanation line");
    }

    private static void Resolution()
    {
        // Disabled is identical to the previous behaviour; enabled at Q = 1 is too (r(1) = g).
        var off = Fixture(true, .3, false); var one = Fixture(true, 1, true);
        var ro = new TurnResolver().Resolve(off, TurnPlanner.Prepare(off, new Dictionary<int, List<Order>>())).Demographics.Single();
        var r1 = new TurnResolver().Resolve(one, TurnPlanner.Prepare(one, new Dictionary<int, List<Order>>())).Demographics.Single();
        var growing = Fixture(true, 1, false); var rg = new TurnResolver().Resolve(growing, TurnPlanner.Prepare(growing, new Dictionary<int, List<Order>>())).Demographics.Single();
        Check(ro.Additions == rg.Additions && r1.Additions == rg.Additions && r1.EndingPopulation == rg.EndingPopulation, "disabled and Q = 1 both reproduce the previous growth");
        Check(rg.Additions > 0, "fixture: the previous ceiling grows the camp");
        // Low QoL: fewer additions, nothing recorded as a death, conservation holds.
        var low = Fixture(true, .95); var rl = new TurnResolver().Resolve(low, TurnPlanner.Prepare(low, new Dictionary<int, List<Order>>())).Demographics.Single();
        Check(rl.Additions < rg.Additions && rl.Deaths == 0, "low QoL slows growth without recording deaths (" + rl.Additions + " < " + rg.Additions + ")");
        Check(rl.EndingPopulation == rl.StartingPopulation + rl.Additions - rl.Deaths, "population is conserved in the report");
        var stopped = Fixture(true, QoLMortality.ZeroGrowthQoL(new SimulationConfig()) * .9); var rs = new TurnResolver().Resolve(stopped, TurnPlanner.Prepare(stopped, new Dictionary<int, List<Order>>())).Demographics.Single();
        Check(rs.Additions == 0 && rs.Deaths == 0, "below Q* the camp neither grows nor records deaths");
        // A camp that is starving keeps the starvation rule (deficit => zero rate) independent of QoL.
        var hungry = Fixture(true, 1); hungry.Camps[0].DeficitTurns = 1; Check(QoLMortality.MaximumRate(hungry, hungry.Camps[0]) == 0, "the deficit rule is unchanged");
    }

    private static string FindConfig()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        { string path = Path.Combine(dir.FullName, "Assets", "_Anthropocene", "Resources", "OpeningConfig.json"); if (File.Exists(path)) return path; }
        throw new FileNotFoundException("OpeningConfig.json");
    }

    private static void Config()
    {
        var c = new SimulationConfig();
        Check(!c.QoLMortalityEnabled && c.BaseBirthRate == .045 && c.QoLMortalityElasticity > 0, "defaults: opt-in (off) until the elasticity is chosen, b0 = 0.045");
        Check(c.BaseBirthRate > c.AnnualHumanGrowthRate, "the default birth rate exceeds the growth rate (d0 > 0)");
        foreach (Action<SimulationConfig> bad in new Action<SimulationConfig>[] { x => x.BaseBirthRate = x.AnnualHumanGrowthRate, x => x.BaseBirthRate = .001, x => x.BaseBirthRate = double.NaN, x => x.BaseBirthRate = .5, x => x.QoLMortalityElasticity = 0, x => x.QoLMortalityElasticity = -1, x => x.QoLMortalityElasticity = double.PositiveInfinity })
        {
            var x = new SimulationConfig { QoLMortalityEnabled = true }; bad(x); bool rejected = false; try { x.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "an invalid QoL mortality lever is rejected");
        }
        var legacyBirth = new SimulationConfig { QoLMortalityEnabled = false, BaseBirthRate = .001 }; legacyBirth.Validate(); checks++;
        string json = File.ReadAllText(FindConfig());
        foreach (string key in new[] { "QoLMortalityEnabled", "BaseBirthRate", "QoLMortalityElasticity" })
            Check(Regex.IsMatch(json, "\"" + key + "\":\\s*[0-9.a-z]+"), "OpeningConfig.json carries " + key);
        var m = Regex.Match(json, "\"QoLMortalityElasticity\":\\s*([0-9.]+)"); Check(m.Success && double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) == c.QoLMortalityElasticity, "the serialized elasticity equals the code default");
        m = Regex.Match(json, "\"BaseBirthRate\":\\s*([0-9.]+)"); Check(m.Success && double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) == c.BaseBirthRate, "the serialized birth rate equals the code default");
        Check(Regex.IsMatch(json, "\"QoLMortalityEnabled\":\\s*false"), "the shipped file leaves QoL mortality off (opt-in)");
    }

    private static void Saves()
    {
        var serializer = new DataContractSerializer(typeof(SimulationConfig)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { QoLMortalityEnabled = true, BaseBirthRate = .05, QoLMortalityElasticity = 2.5 }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<QoLMortalityEnabled>true</QoLMortalityEnabled>") && xml.Contains("<BaseBirthRate>0.05</BaseBirthRate>"), "the levers are serialized");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) { var back = (SimulationConfig)serializer.ReadObject(stream); Check(back.QoLMortalityEnabled && back.BaseBirthRate == .05 && back.QoLMortalityElasticity == 2.5, "saved levers round trip"); }
        string legacy = Regex.Replace(xml, "<(QoLMortalityEnabled|BaseBirthRate|QoLMortalityElasticity)>[^<]*</\\1>", "");
        Check(legacy != xml && !legacy.Contains("QoLMortality") && !legacy.Contains("BaseBirthRate"), "legacy fixture omits every QoL mortality field");
        var defaults = new SimulationConfig();
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) { var old = (SimulationConfig)serializer.ReadObject(stream); Check(!old.QoLMortalityEnabled && old.BaseBirthRate == defaults.BaseBirthRate && old.QoLMortalityElasticity == defaults.QoLMortalityElasticity, "an older save loads with QoL mortality off and the default levers"); old.Validate(); checks++; }
        // A whole world round trips and continues identically; a world saved with the lever off keeps it off.
        var a = Busy(true); var b = SaveCodec.Load(SaveCodec.Save(a)); var r = new TurnResolver();
        Check(b.Config.QoLMortalityEnabled && SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(b), "an enabled world round trips");
        for (int t = 0; t < 3; t++) { Advance(a, r); Advance(b, new TurnResolver()); Check(SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(b), "the loaded world continues identically after turn " + t); }
        var offWorld = Busy(false); var offBack = SaveCodec.Load(SaveCodec.Save(offWorld)); Check(!offBack.Config.QoLMortalityEnabled, "a world saved with the lever off stays off");
    }

    private static WorldState Busy(bool enabled)
    {
        var w = Fixture(true, .9, enabled); var c = w.Camps[0]; c.Population = 120; return w;
    }
    private static void Advance(WorldState w, TurnResolver r) { var record = r.Resolve(w, new Dictionary<int, List<Order>>()); w.History.Add(record); }

    // Scalar and parallel resolution agree, and two identical runs are identical (determinism).
    private static void Replay()
    {
        var a = Busy(true); var b = Busy(true); var c = Busy(true);
        var scalar = new TurnResolver(); var parallel = new TurnResolver(new PhaseExecution(4, 3, true, 1));
        for (int t = 0; t < 4; t++)
        {
            Advance(a, scalar); Advance(b, parallel); Advance(c, new TurnResolver());
            Check(SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(b) && SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(c), "scalar, parallel and repeated runs agree after turn " + t);
        }
        var camp = a.Camps[0];
        Check(camp.Population >= 120 && camp.QoL > 0 && camp.QoL <= 1, "the replay camp grew within a valid QoL");
    }
}
