using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// Fission (WP10a): policy levers, cached destination, the Close-phase pass, daughter Societies in new AI player slots,
// lineage, observations, persistence and scalar/parallel replay. Owns its worlds and counters (lanes run checks concurrently).
internal static class FissionChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Fission: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-9) => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)), message + " (" + a + " vs " + b + ")");

    private static readonly FieldInfo[] InventoryFields = typeof(CampInventory).GetFields().Where(f => f.FieldType == typeof(double)).ToArray();

    // A rich uniform 24x16 fixture: every fully food-supported footprint, freshwater everywhere. Camps at (column, row), one player each.
    // `neolithic` camps are cultivators; the others are Paleolithic. Fission pressure is the expected gain against G*: fixtures are pressured by default (G* = 0) and use Quiet/Ratio to shape it.
    private static WorldState World(bool neolithic, params int[][] sites) => World(24, 16, neolithic, sites);
    private static WorldState World(int width, int height, bool neolithic, params int[][] sites)
    {
        var cfg = SimulationConfig.Small(1); cfg.FissionEnabled = true; cfg.FissionHousingRelief = 1; /* mechanism fixtures exercise the full relief; the shipped default is 0 */ cfg.FissionDaughterMargin = 1; cfg.Width = width; cfg.Height = height; cfg.AnnualHumanGrowthRate = 0; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0;
        var w = new WorldState { Seed = 101, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 8000, Food = 1000 };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = t;
        }
        for (int i = 0; i < sites.Length; i++)
            w.Camps.Add(new Camp { Id = w.NextId++, Player = i, Tile = w.Map.At(sites[i][0], sites[i][1]), Population = 100, PlantFood = 12000, QoL = .5 });
        w.Config.Players = sites.Length; w.PlayerCount = sites.Length; w.Turn = 3; w.Days = 1000;
        Ownership.Bind(w);
        if (neolithic) foreach (var c in w.Camps) MakeNeolithic(c, Technology.Cultivation);
        foreach (var t in w.Tiles) t.LastEcologyUpdateDays = w.Days;
        Pressure(w, 0);
        return w;
    }
    // Every policy's expected-gain threshold G*. Zero is always met (a split never lowers the stayers' QoL); a large value never is.
    private static void Pressure(WorldState w, double gstar) { w.Config.FissionGainThreshold = w.Config.FissionGainDiscourage = w.Config.FissionGainEncourage = gstar; }
    private static void Quiet(WorldState w) => Pressure(w, 5);
    // Sets G* so that the camp's expected gain is `multiple` times the threshold (push = min(1, multiple)).
    private static void Ratio(WorldState w, Camp c, double multiple) => Pressure(w, Fission.EstimateSplit(w, c, c.FissionPolicy).Gain / multiple);
    private static void MakeNeolithic(Camp c, Technology technology)
    {
        c.Technology = technology; c.Technologies[(int)technology] = true; c.People.Progression.Era = Era.Neolithic;
    }
    private static void Explore(WorldState w, Camp c, int radius = 4)
    {
        var memory = ContactKnowledge.MemoryOf(w, c.People);
        foreach (int t in w.Map.Radius(c.Tile, radius)) memory.Explored.Add(t);
    }
    private static FissionReport Pass(WorldState w, out ResolutionMetrics metrics, PhaseExecution execution = null)
    {
        var record = new TurnRecord { Turn = w.Turn }; metrics = new ResolutionMetrics();
        foreach (var c in w.Camps) record.Demographics.Add(new DemographicReport { Camp = c.Id, StartingPopulation = c.Population, EndingPopulation = c.Population });
        Fission.Resolve(w, record, execution ?? new PhaseExecution(), metrics); lastRecord = record;
        return record.Fissions.FirstOrDefault();
    }
    [ThreadStatic] private static TurnRecord lastRecord;
    private static Camp Daughter(WorldState w, Camp parent) => w.Camps.Single(c => c.SocietyId == w.Societies.Single(s => s.ParentSociety == parent.SocietyId).Id);

    public static int Run()
    {
        checks = 0; Config(); Levers(); Crowding(); Calibration(); Ordering(); Opening(); Destination(); Knowledge(); Commit(); Conservation(); Viability(); Spacing(); Bounded(); Policy(); Observations(); Persistence(); Replay();
        Console.WriteLine("PASS: " + checks + " fission assertions"); return checks;
    }

    private static void Config()
    {
        var c = new SimulationConfig();
        Check(!c.FissionEnabled && c.FissionGainThreshold == .113 && c.FissionGainDiscourage == .080 && c.FissionGainEncourage == .0955 && c.FissionSearchFraction == .8 && c.FissionDaughterMargin == .05 && c.FissionHousingRelief == 0 && c.FissionRadius == 3 && c.FissionMinimumDaughter == 12, "defaults match the calibration");
        foreach (Action<SimulationConfig> bad in new Action<SimulationConfig>[] { x => x.FissionGainThreshold = -.1, x => x.FissionGainDiscourage = 6, x => x.FissionGainEncourage = double.NaN, x => x.FissionRadius = 0, x => x.FissionRadius = 6, x => x.FissionMinimumDaughter = 0, x => x.FissionSearchFraction = 1.5, x => x.FissionSearchFraction = -.1, x => x.FissionDaughterMargin = -.01, x => x.FissionDaughterMargin = 2, x => x.FissionHousingRelief = -.1, x => x.FissionHousingRelief = 1.5 })
        {
            var x = new SimulationConfig(); bad(x); bool rejected = false; try { x.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "an invalid fission lever is rejected");
        }
        var serializer = new DataContractSerializer(typeof(SimulationConfig)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { FissionRadius = 4, FissionEnabled = true }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<FissionRadius>4</FissionRadius>") && xml.Contains("<FissionEnabled>true</FissionEnabled>"), "fission levers are serialized");
        string legacy = Regex.Replace(xml, "<Fission[A-Za-z]*>[^<]*</Fission[A-Za-z]*>", "");
        Check(legacy != xml && !legacy.Contains("<Fission"), "legacy fixture omits every fission field");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy)))
        {
            var old = (SimulationConfig)serializer.ReadObject(stream);
            Check(!old.FissionEnabled && old.FissionGainThreshold == .113 && old.FissionGainDiscourage == .080 && old.FissionGainEncourage == .0955 && old.FissionSearchFraction == .8 && old.FissionDaughterMargin == .05 && old.FissionHousingRelief == 0 && old.FissionRadius == 3 && old.FissionMinimumDaughter == 12, "older saves load with fission disabled and default levers");
        }
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) Check(((SimulationConfig)serializer.ReadObject(stream)).FissionRadius == 4, "saved levers round trip");
        string json = File.ReadAllText(FindConfig());
        foreach (var pair in new[] { ("FissionEnabled", "false"), ("FissionGainThreshold", "0.113"), ("FissionGainDiscourage", "0.08"), ("FissionGainEncourage", "0.0955"), ("FissionSearchFraction", "0.8"), ("FissionDaughterMargin", "0.05"), ("FissionHousingRelief", "0"), ("FissionRadius", "3"), ("FissionMinimumDaughter", "12") })
        {
            var m = Regex.Match(json, "\"" + pair.Item1 + "\":\\s*([0-9.a-z]+)");
            Check(m.Success && m.Groups[1].Value.TrimEnd('0').TrimEnd('.') == pair.Item2.TrimEnd('0').TrimEnd('.'), "OpeningConfig.json carries " + pair.Item1);
        }
    }
    private static string FindConfig()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        { string path = Path.Combine(dir.FullName, "Assets", "_Anthropocene", "Resources", "OpeningConfig.json"); if (File.Exists(path)) return path; }
        throw new FileNotFoundException("OpeningConfig.json");
    }

    private static void Levers()
    {
        // Exact tables and exact integer daughter sizes.
        var levers = new SimulationConfig();
        Check(Kinship.GainThreshold(levers, FissionPolicy.Neutral) == levers.FissionGainThreshold && Kinship.GainThreshold(levers, FissionPolicy.Discourage) == levers.FissionGainDiscourage && Kinship.GainThreshold(levers, FissionPolicy.Encourage) == levers.FissionGainEncourage, "each policy reads its own gain threshold");
        Check(Kinship.DaughterPercent(FissionPolicy.Discourage) == 15 && Kinship.DaughterPercent(FissionPolicy.Neutral) == 30 && Kinship.DaughterPercent(FissionPolicy.Encourage) == 40, "daughter fraction is 15/30/40 percent");
        Check(Kinship.PolicyAffinity(FissionPolicy.Discourage) == .5 && Kinship.PolicyAffinity(FissionPolicy.Neutral) == .9 && Kinship.PolicyAffinity(FissionPolicy.Encourage) == 1, "daughter affinity is 0.5/0.9/1.0");
        Check((int)default(FissionPolicy) == 0 && default(FissionPolicy) == FissionPolicy.Neutral, "an absent policy is Neutral");
        for (int n = 0; n <= 3000; n++)
            foreach (FissionPolicy p in Enum.GetValues(typeof(FissionPolicy)))
            {
                int d = Kinship.DaughterSize(n, p), pct = Kinship.DaughterPercent(p);
                if (!(d == (int)Math.Floor(n * pct / 100.0 + 1e-9) && d * 100 <= n * pct && (d + 1) * 100 > n * pct && (n == 0 || d < n - d))) throw new Exception("Fission: daughter size is floor(N*f) exactly for N=" + n + " " + p);
            }
        checks++;
        Check(Kinship.DaughterSize(100, FissionPolicy.Discourage) == 15 && Kinship.DaughterSize(100, FissionPolicy.Neutral) == 30 && Kinship.DaughterSize(100, FissionPolicy.Encourage) == 40 && Kinship.DaughterSize(39, FissionPolicy.Neutral) == 11, "daughter size examples");
        // Push is the diagnostic clamp(G / G*, 0, 1); a zero threshold is always met.
        Near(Fission.Push(.05, .10), .5, "half the threshold gives half push"); Near(Fission.Push(.10, .10), 1, "the threshold gives full push"); Near(Fission.Push(.50, .10), 1, "push is clamped at one");
        Near(Fission.Push(-.01, .10), 0, "a negative gain is clamped at zero"); Near(Fission.Push(0, 0), 1, "a zero threshold is always met"); Near(Fission.Push(-1e-9, 0), 0, "a zero threshold is not met by a loss");
        // Affinity derivation: parent-daughter stored, siblings take the lower, everything else zero.
        var parent = new Society { Id = 1 }; var a = new Society { Id = 2, ParentSociety = 1, KinAffinity = .9 }; var b = new Society { Id = 3, ParentSociety = 1, KinAffinity = .5 }; var grand = new Society { Id = 4, ParentSociety = 2, KinAffinity = 1 };
        Near(Kinship.Affinity(a, parent), .9, "daughter to parent"); Near(Kinship.Affinity(parent, a), .9, "parent to daughter is symmetric");
        Near(Kinship.Affinity(a, b), .5, "siblings take the lower affinity"); Near(Kinship.Affinity(b, a), .5, "sibling affinity is symmetric");
        Near(Kinship.Affinity(grand, parent), 0, "a grandparent is not first-degree kin"); Near(Kinship.Affinity(parent, parent), 0, "a Society has no affinity with itself"); Near(Kinship.Affinity(new Society { Id = 9 }, parent), 0, "unrelated Societies have zero affinity");
    }

    // ---- WP10c: crowding + expected gain ------------------------------------------------------------------------------------------
    // A full-coverage fixture: clothing 1 and Light Housing far beyond need, so Q = F * W * S(N) and R = 1 before and after.
    private static Camp Covered(WorldState w, Camp c, int residents)
    {
        c.Population = residents; c.ClothPerCapita = 1; c.Inventory.LightHousingUnits = 1e9; return c;
    }
    private static double Cohesion(WorldState w, Camp c, double population) => Math.Exp(-Math.Pow(population / NeolithicProduction.CohesionScale(w.Config.Neolithic, c), w.Config.Neolithic.SocialCohesionExponent));
    private static void Nomadic(Camp c) { c.Technologies[(int)Technology.Cultivation] = false; c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true; }
    private static void Crowding()
    {
        // Closed form with full goods coverage: G = S(N - N_d) / S(N) - 1 = exp((N^2 - (N - N_d)^2) / N_s^2) - 1 (beta = 2), any F, W.
        foreach (var regime in new[] { FissionRegime.Paleolithic, FissionRegime.Nomadic, FissionRegime.Agricultural })
            foreach (FissionPolicy policy in Enum.GetValues(typeof(FissionPolicy)))
            {
                var w = World(regime != FissionRegime.Paleolithic, new[] { 8, 8 }); var c = Covered(w, w.Camps[0], 300);
                if (regime == FissionRegime.Nomadic) Nomadic(c);
                Check(Fission.RegimeOf(c) == regime, "the fixture is " + regime);
                c.FissionPolicy = policy; c.FoodNeedsMetRatio = .63; c.WaterNeedsMetRatio = .9;
                double ns = regime == FissionRegime.Paleolithic ? 220 : regime == FissionRegime.Nomadic ? 550 : 1780;
                int nd = Kinship.DaughterSize(300, policy), stay = 300 - nd;
                var e = Fission.EstimateSplit(w, c, policy);
                Near(e.Gain, Math.Exp((300.0 * 300 - (double)stay * stay) / (ns * ns)) - 1, regime + " " + policy + " gain is the closed form of the cohesion ratio");
                Near(e.QNow, .63 * .9 * Math.Exp(-Math.Pow(300 / ns, 2)), regime + " " + policy + " Q_now = F W S(N)");
                Check(e.Daughter == nd && e.QAfter > e.QNow, "the estimate carries the daughter size and a higher stayers' QoL");
            }
        // F, W and the goods coverage cancel in the ratio while housing is unaffected: a Paleolithic camp with no goods at all has the same gain.
        var bare = World(false, new[] { 8, 8 }); bare.Camps[0].Population = 300; bare.Camps[0].ClothPerCapita = 0; bare.Camps[0].HousingPerCapita = 0; bare.Camps[0].FoodNeedsMetRatio = .4;
        var bareQ = Fission.EstimateSplit(bare, bare.Camps[0], FissionPolicy.Neutral);
        Near(bareQ.Gain, Math.Exp((300.0 * 300 - 210.0 * 210) / (220 * 220)) - 1, "with no goods and partial food the Paleolithic gain is the same cohesion gain");
        Near(bareQ.QNow, .4 * (.5 * (1 - .25) + .5 * (1 - .25)) * Math.Exp(-Math.Pow(300 / 220.0, 2)), "Q_now uses the expectation floor for an unresolved camp");
        // Q_now = 0 (no food): the documented fallback is the cohesion gain, so a destitute crowded camp still feels its crowding.
        var zero = World(false, new[] { 8, 8 }); zero.Camps[0].Population = 300; zero.Camps[0].FoodNeedsMetRatio = 0;
        var ze = Fission.EstimateSplit(zero, zero.Camps[0], FissionPolicy.Neutral);
        Check(ze.QNow == 0 && ze.QAfter == 0, "no food gives zero QoL before and after");
        Near(ze.Gain, Math.Exp((300.0 * 300 - 210.0 * 210) / (220 * 220)) - 1, "the zero-QoL fallback is the cohesion gain S_after / S_now - 1");
        // Housing relief: the parent keeps all fixed Housing, so per-head housing rises once the daughter leaves; portable Light Housing splits in proportion and gives none.
        var fixedWorld = World(true, new[] { 8, 8 }); var fc = fixedWorld.Camps[0]; fc.ClothPerCapita = 1; fc.Population = 100;
        fixedWorld.FixedHousing.Add(new FixedHousingStock { CampId = fc.Id, Tile = fc.Tile, Units = 12.5, LastUpdatedDays = fixedWorld.Days }); // 50 people
        Near(fixedWorld.Config.Neolithic.HousingPeoplePerUnit, 4, "four people per Housing unit");
        double floor = fixedWorld.Config.Neolithic.RudimentaryExpectationFloor;
        double Cov(double housing) => .5 + .5 * (1 - floor * (1 - housing));
        var fe = Fission.EstimateSplit(fixedWorld, fc, FissionPolicy.Neutral);
        Near(fe.QNow, Cov(.5) * Cohesion(fixedWorld, fc, 100), "fixed Housing: Q_now at 0.5 housing per head");
        Near(fe.QAfter, Cov(50.0 / 70) * Cohesion(fixedWorld, fc, 70), "fixed Housing: the parent keeps all of it, so 50 beds for 70 stayers");
        Near(fe.Gain, fe.QAfter / fe.QNow - 1, "gain is the relative rise of the stayers' QoL");
        Check(fe.Gain > Math.Exp((100.0 * 100 - 70.0 * 70) / (1780.0 * 1780)) - 1, "housing relief adds to the cohesion gain");
        // The relief weight interpolates between the cohesion-and-clothing gain (0) and the full relief (1).
        double QAt(double weight) { fixedWorld.Config.FissionHousingRelief = weight; return Fission.EstimateSplit(fixedWorld, fc, FissionPolicy.Neutral).QAfter; }
        double full = QAt(1), none = QAt(0), half = QAt(.5);
        Near(none, Cov(.5) * Cohesion(fixedWorld, fc, 70), "relief weight 0: housing per head stays at its present value, only cohesion changes");
        Near(half, Cov(.5 + .5 * (50.0 / 70 - .5)) * Cohesion(fixedWorld, fc, 70), "relief weight 0.5 counts half the housing gain"); Near(full, fe.QAfter, "relief weight 1 is the specified rule"); QAt(1);
        Near(Fission.EstimateSplit(fixedWorld, fc, FissionPolicy.Neutral).Gain, fe.Gain, "restoring the weight restores the gain");
        QAt(0); Near(Fission.EstimateSplit(fixedWorld, fc, FissionPolicy.Neutral).Gain, Math.Exp((100.0 * 100 - 70.0 * 70) / (1780.0 * 1780)) - 1, "with relief weight 0 a housing shortage leaves the gain at the closed-form cohesion gain"); QAt(1);
        var mixed = World(true, new[] { 8, 8 }); var mc = mixed.Camps[0]; mc.ClothPerCapita = 1; mc.Population = 100; mc.Inventory.LightHousingUnits = 5;
        mixed.FixedHousing.Add(new FixedHousingStock { CampId = mc.Id, Tile = mc.Tile, Units = 5, LastUpdatedDays = mixed.Days });
        var me = Fission.EstimateSplit(mixed, mc, FissionPolicy.Neutral);
        Near(me.QNow, Cov(.4) * Cohesion(mixed, mc, 100), "mixed housing: 10 units shelter 40 of 100 people");
        Near(me.QAfter, Cov((5 + 5 * .7) * 4 / 70) * Cohesion(mixed, mc, 70), "mixed housing: all fixed plus 70 percent of the portable Light Housing for 70 stayers");
        var portable = World(true, new[] { 8, 8 }); var pc = portable.Camps[0]; pc.ClothPerCapita = 1; pc.Population = 100; pc.Inventory.LightHousingUnits = 10;
        var pe = Fission.EstimateSplit(portable, pc, FissionPolicy.Neutral);
        Near(pe.QAfter / pe.QNow, Cohesion(portable, pc, 70) / Cohesion(portable, pc, 100), "portable Light Housing splits in proportion and brings no relief");
        // Daughter check: F = W = 1 at the site, clothing unchanged, housing from its portable share only, expectation after a move.
        var dw = World(true, new[] { 8, 8 }); var dc = dw.Camps[0]; dc.ClothPerCapita = 1; dc.Population = 100; dw.FixedHousing.Add(new FixedHousingStock { CampId = dc.Id, Tile = dc.Tile, Units = 25, LastUpdatedDays = dw.Days });
        var de = Fission.EstimateSplit(dw, dc, FissionPolicy.Neutral);
        double moved = floor + (1 - floor) * (dw.Config.TurnDays / dw.Config.DaysPerYear / dw.Config.Neolithic.RudimentaryExpectationRampYears);
        Near(de.QNow, Cohesion(dw, dc, 100), "a fully housed parent has Q = S(N)");
        Near(de.DaughterQoL, (.5 + .5 * (1 - moved)) * Cohesion(dw, dc, 30), "the daughter has no fixed Housing, a fresh expectation and F = W = 1");
        Check(!de.DaughterAcceptable(.05) && de.DaughterAcceptable(.25) && de.DaughterAcceptable(de.QNow - de.DaughterQoL + 1e-12) && !de.DaughterAcceptable(de.QNow - de.DaughterQoL - 1e-6), "the margin test is daughter >= Q_now - margin");
        // The test binds in the pass: the tight margin blocks and counts; a wide margin founds.
        Explore(dw, dc); Ratio(dw, dc, 1.5); dw.Config.FissionDaughterMargin = .05;
        Check(Pass(dw, out var dm) == null && dm.FissionDaughterChecks == 1 && dm.FissionDaughterBlocked == 1 && dm.FissionProposals == 0 && dw.Camps.Count == 1, "a daughter who would be much worse off is blocked and the block is counted");
        dw.Config.FissionDaughterMargin = .25; var free = Pass(dw, out var fm);
        Check(free != null && fm.FissionDaughterChecks == 1 && fm.FissionDaughterBlocked == 0, "a wide margin lets the same split through");
        Near(free.DaughterQoL, de.DaughterQoL, "the report records the estimated daughter QoL"); Near(free.Gain, de.Gain, "the report records the expected gain");
        // Policy thresholds and daughter fractions: only the camp's own policy threshold counts.
        foreach (FissionPolicy policy in Enum.GetValues(typeof(FissionPolicy)))
        {
            var w = World(true, new[] { 8, 8 }); var c = w.Camps[0]; Explore(w, c); c.FissionPolicy = policy; c.Population = 200;
            w.Config.FissionGainThreshold = w.Config.FissionGainDiscourage = w.Config.FissionGainEncourage = 5;
            if (policy == FissionPolicy.Discourage) w.Config.FissionGainDiscourage = 0; else if (policy == FissionPolicy.Encourage) w.Config.FissionGainEncourage = 0; else w.Config.FissionGainThreshold = 0;
            var r = Pass(w, out _);
            Check(r != null && r.Policy == policy && r.Daughter == Kinship.DaughterSize(200, policy), policy + " fissions on its own threshold and splits its own fraction");
            var other = World(true, new[] { 8, 8 }); var oc = other.Camps[0]; Explore(other, oc); oc.FissionPolicy = policy; oc.Population = 200;
            other.Config.FissionGainThreshold = other.Config.FissionGainDiscourage = other.Config.FissionGainEncourage = 0;
            if (policy == FissionPolicy.Discourage) other.Config.FissionGainDiscourage = 5; else if (policy == FissionPolicy.Encourage) other.Config.FissionGainEncourage = 5; else other.Config.FissionGainThreshold = 5;
            Check(Pass(other, out _) == null, policy + " does not fission when only the other policies' thresholds are met");
        }
        // Search fraction: the lazy destination search runs from 0.8 G* and a proposal needs the full G*.
        foreach (var (multiple, searches, founds) in new[] { (.79, 0, false), (.81, 1, false), (1.0, 1, true), (1.3, 1, true) })
        {
            var w = World(true, new[] { 8, 8 }); var c = w.Camps[0]; Explore(w, c); Ratio(w, c, multiple);
            var r = Pass(w, out var m);
            Check(m.FissionSearches == searches && (r != null) == founds, "gain " + multiple + " G*: searches " + searches + ", founds " + founds);
        }
        var fraction = World(true, new[] { 8, 8 }); Explore(fraction, fraction.Camps[0]); fraction.Config.FissionSearchFraction = .5; Ratio(fraction, fraction.Camps[0], .6);
        Check(Pass(fraction, out var frm) == null && frm.FissionSearches == 1 && fraction.Camps[0].FissionDestination != null, "a lower search fraction searches earlier");
        // The WP10a failure: a camp with no goods at default thresholds no longer splits at any size.
        foreach (var (neo, size, expected) in new[] { (false, 60, false), (false, 110, false), (true, 143, false), (true, 400, false), (true, 1200, true) })
        {
            var w = World(neo, new[] { 8, 8 }); var c = w.Camps[0]; Explore(w, c); c.Population = size; c.PlantFood = 500000;
            var d = new SimulationConfig(); w.Config.FissionGainThreshold = d.FissionGainThreshold; w.Config.FissionGainDiscourage = d.FissionGainDiscourage; w.Config.FissionGainEncourage = d.FissionGainEncourage;
            Check((Pass(w, out _) != null) == expected, (neo ? "agricultural" : "Paleolithic") + " camp of " + size + " at default thresholds " + (expected ? "splits" : "stays whole"));
        }
    }

    // Onset population per regime and policy under full goods coverage, for the given (default: shipped) thresholds.
    internal static int Onset(FissionRegime regime, FissionPolicy policy, SimulationConfig config = null)
    {
        var w = World(regime != FissionRegime.Paleolithic, new[] { 8, 8 }); var c = w.Camps[0];
        if (regime == FissionRegime.Nomadic) Nomadic(c);
        var d = config ?? new SimulationConfig(); w.Config.FissionGainThreshold = d.FissionGainThreshold; w.Config.FissionGainDiscourage = d.FissionGainDiscourage; w.Config.FissionGainEncourage = d.FissionGainEncourage;
        c.FissionPolicy = policy; double gstar = Kinship.GainThreshold(w.Config, policy);
        for (int n = 1; n <= 6000; n++)
        {
            Covered(w, c, n); int nd = Kinship.DaughterSize(n, policy);
            if (nd < w.Config.FissionMinimumDaughter || nd >= n - nd || n - nd < w.Config.MinimumCamp) continue;
            if (Fission.EstimateSplit(w, c, policy).Gain >= gstar) return n;
        }
        return -1;
    }
    private static void Calibration()
    {
        // Design onsets N* (QoLFissionTradeDesign 3.5-3.6): Paleolithic 101 (116 / 83), Nomadic 252 (291 / 208), Agricultural inside 607-999 (770-1105 / 398-887).
        var design = new Dictionary<(FissionRegime, FissionPolicy), int>
        {
            [(FissionRegime.Paleolithic, FissionPolicy.Neutral)] = 101, [(FissionRegime.Paleolithic, FissionPolicy.Discourage)] = 116, [(FissionRegime.Paleolithic, FissionPolicy.Encourage)] = 83,
            [(FissionRegime.Nomadic, FissionPolicy.Neutral)] = 252, [(FissionRegime.Nomadic, FissionPolicy.Discourage)] = 291, [(FissionRegime.Nomadic, FissionPolicy.Encourage)] = 208
        };
        foreach (var pair in design)
        {
            int onset = Onset(pair.Key.Item1, pair.Key.Item2);
            Check(onset > 0 && Math.Abs(onset - pair.Value) <= Math.Max(2, .03 * pair.Value), pair.Key + " onset " + onset + " within 3 percent of the design " + pair.Value);
        }
        var bands = new Dictionary<FissionPolicy, (int, int)> { [FissionPolicy.Neutral] = (607, 999), [FissionPolicy.Discourage] = (770, 1105), [FissionPolicy.Encourage] = (398, 887) };
        foreach (var pair in bands)
        {
            int onset = Onset(FissionRegime.Agricultural, pair.Key);
            Check(onset >= pair.Value.Item1 && onset <= pair.Value.Item2, "Agricultural " + pair.Key + " onset " + onset + " lies inside the design band " + pair.Value);
        }
        Check(Onset(FissionRegime.Agricultural, FissionPolicy.Discourage) > Onset(FissionRegime.Agricultural, FissionPolicy.Neutral) && Onset(FissionRegime.Agricultural, FissionPolicy.Neutral) > Onset(FissionRegime.Agricultural, FissionPolicy.Encourage), "Discourage delays and Encourage advances the onset");
    }

    private static void Ordering()
    {
        // D saturates at one: among equally viable sites the richer footprint wins over a lower tile ID.
        var w = World(true, new[] { 8, 8 }); var parent = w.Camps[0]; Explore(w, parent);
        var ring = w.Map.Radius(parent.Tile, 3).Where(n => w.Map.Distance(parent.Tile, n) == 3).OrderBy(n => n).ToArray();
        Check(ring.Length == 18, "the candidate ring has 18 tiles");
        int rich = ring.Last();
        foreach (int n in w.Map.Radius(rich, 1)) { var t = w.Tiles[n]; t.Food *= 3; t.PotentialFood *= 3; for (int s = 0; s < 4; s++) { t.Seasons[s].MaxFood *= 3; t.Seasons[s].MaxPotentialFood *= 3; } }
        var report = Pass(w, out var m);
        Check(report != null && m.FissionCandidatesScored == 18 && Math.Abs(report.Desirability - 1) < 1e-12, "every ring site is fully viable (D = 1)");
        Check(report.Tile == rich && report.Tile != ring.First(), "a richer site beats the lower tile ID when both have D = 1");
        // Equal D and equal support: the nearer site wins, then the lower tile ID. A radius-four search in a uniform world.
        var u = World(true, new[] { 12, 10 }); var pc = u.Camps[0]; Explore(u, pc, 6); u.Config.FissionRadius = 4;
        var candidates = u.Map.Radius(pc.Tile, 4).Where(n => u.Map.Distance(pc.Tile, n) >= 3).OrderBy(n => n).ToArray();
        Check(u.Map.Distance(pc.Tile, candidates.First()) == 4, "precondition: the lowest tile ID is at distance four");
        var near = Pass(u, out _);
        Check(near != null && u.Map.Distance(pc.Tile, near.Tile) == 3 && near.Tile == candidates.Where(n => u.Map.Distance(pc.Tile, n) == 3).Min(), "equal D and support: the nearer site wins, then the lowest tile ID");
        // A site below D = 1 loses to a fully viable one (D is the first key): starve the lowest-ID site's footprint of food.
        var v = World(true, new[] { 8, 8 }); var vp = v.Camps[0]; Explore(v, vp);
        int lean = ring.First();
        foreach (int n in v.Map.Radius(lean, 1)) { var t = v.Tiles[n]; t.Food = 20; t.PotentialFood = 20; for (int s = 0; s < 4; s++) { t.Seasons[s].MaxFood = 20; t.Seasons[s].MaxPotentialFood = 20; } }
        var vr = Pass(v, out _);
        Check(vr != null && vr.Tile != lean && Math.Abs(vr.Desirability - 1) < 1e-12, "a site below D = 1 loses to a fully viable site");
    }

    // A daughter inherits its parent's opening status and never delays the end of the opening.
    private static string Describe(object o) => string.Join("|", o.GetType().GetFields().Select(f => f.Name + "=" + (f.GetValue(o) is double d ? d.ToString("R") : f.GetValue(o)?.ToString())));
    private static void Opening()
    {
        // Every field of the Society that the opening logic reads is copied; only identity, the keyed seed, lineage and memory differ.
        var w = World(false, new[] { 8, 8 }); var parent = w.Camps[0]; Explore(w, parent); var ps = parent.People;
        ps.Experience[0] = 41; ps.Progression.EraStartDays = 123; ps.Progression.MajorityEligibilityDays = 7777; ps.Progression.ForcedTransitionDays = 8888; ps.ForcedTransition = true; ps.SecondaryDomain = 2; ps.TransitionTurn = 4;
        ps.TransitionSeeds.Add(new TransitionSeed { Technology = Technology.Cultivation, Settlement = parent.Id, Tile = parent.Tile, Days = 99 });
        int outpost = w.Map.Radius(parent.Tile, 2).Where(n => w.Map.Distance(parent.Tile, n) == 2).OrderBy(n => n).First();
        w.Hunters.Add(new Hunter { Id = w.NextId++, SourceCamp = parent.Id, SocietyId = parent.SocietyId, Player = parent.Player, Tile = outpost, Population = 6, CarriedFood = 10 });
        Check(Pass(w, out _) != null, "a Paleolithic parent fissions (its hunters see a site)");
        var ds = Daughter(w, parent).People; var skipped = new HashSet<string> { "Id", "Player", "PolicySeed", "ParentSociety", "KinAffinity", "Memory" };
        foreach (var f in typeof(Society).GetFields())
        {
            if (skipped.Contains(f.Name)) continue;
            object a = f.GetValue(ps), b = f.GetValue(ds);
            bool same = a is string || a is ValueType ? Equals(a, b) : a is EraProgression || a is Mandate || a is PolicyState ? Describe(a) == Describe(b) : ((System.Collections.IEnumerable)a).Cast<object>().Count() == ((System.Collections.IEnumerable)b).Cast<object>().Count();
            Check(same, "a daughter inherits Society." + f.Name);
        }
        Check(ds.Progression.Era == Era.Paleolithic && ds.Technology == Technology.None && ds.ForcedTransition == ps.ForcedTransition, "a Paleolithic parent's daughter is Paleolithic");
        // Finished: only founding Societies decide it.
        var f1 = World(true, new[] { 8, 8 }, new[] { 18, 8 }); var p1 = f1.Camps[0]; Explore(f1, p1); Check(f1.Finished, "two Neolithic founders have finished the opening");
        Check(Pass(f1, out _) != null, "a Neolithic founder fissions"); var late = Daughter(f1, p1).People; late.Progression.Era = Era.Paleolithic; late.Technology = Technology.None;
        Check(late.ParentSociety > 0 && late.Progression.Era == Era.Paleolithic && f1.Finished, "a Paleolithic daughter of a finished parent does not delay the end of the opening");
        var f2 = World(false, new[] { 8, 8 }); Check(!f2.Finished, "a Paleolithic founder still holds the opening open");
        // The opening ends on the same turn with fission on as off (seed 53 is the four-tribe fixture that moved from turn 9 to 12 under WP10a).
        foreach (var players in new[] { 4, 6 })
            foreach (var mode in new[] { "default", "eager" })
            {
                int off = OpeningTurns(53, players, false, mode, out _), on = OpeningTurns(53, players, true, mode, out int founded);
                // Default thresholds leave the opening untouched. Forced fission every turn (G* = 0) shrinks the founders themselves, which
                // slows their own practice by at most a turn; it is never a daughter that holds the opening open.
                Check(mode == "default" ? on == off : on >= off && on <= off + 1, "fission " + mode + " thresholds, " + players + " tribes: the opening ends on turn " + off + " with fission off and " + on + " on (" + founded + " settlements)");
            }
    }
    private static int OpeningTurns(int seed, int players, bool fission, string mode, out int founded)
    {
        var cfg = SimulationConfig.Small(players); cfg.FissionEnabled = fission;
        if (mode == "eager") cfg.FissionGainThreshold = cfg.FissionGainDiscourage = cfg.FissionGainEncourage = 0;
        var g = new WorldGenerator().Generate(seed, cfg); var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder(); int turns = 0; founded = 0;
        while (!g.Finished && turns < 20)
        {
            var orders = g.Camps.GroupBy(c => c.Player).ToDictionary(x => x.Key, x => policy.Decide(builder.Build(g, x.Key), x.First().Mandate).Orders);
            new TurnResolver().Resolve(g, orders); turns++; founded += g.History.Last().Fissions.Count;
        }
        return turns;
    }

    private static void Destination()
    {
        // A Neolithic parent with explored surroundings finds and caches the best ring tile; nothing else changes without a proposal.
        var w = World(true, new[] { 8, 8 }); var parent = w.Camps[0]; Explore(w, parent);
        Ratio(w, parent, 1.5); // expected gain 1.5 times G*.
        var report = Pass(w, out var m);
        Check(report != null, "a pressured parent with a supported known site founds a daughter");
        Check(m.FissionSearches == 1 && m.FissionCandidatesScored > 0 && m.FissionCampsEvaluated == 1, "one search with scored candidates");
        Check(parent.FissionDestination == null, "the parent's cache is cleared once its destination is founded");
        Check(w.Map.Distance(parent.Tile, report.Tile) == 3, "the destination is at the configured radius and at least the camp spacing away");
        Near(report.Push, 1, "reported push is clamped at one"); Near(report.Desirability, 1, "a fully supported site has D = 1"); Near(report.Score, 1, "score = push * D"); Check(report.Gain > 0 && report.DaughterQoL > 0, "the report carries the expected gain and the daughter estimate");
        // Below the search threshold nothing is searched and nothing is cached.
        var quiet = World(true, new[] { 8, 8 }); Explore(quiet, quiet.Camps[0]); Quiet(quiet);
        Check(Pass(quiet, out var qm) == null && qm.FissionSearches == 0 && qm.FissionCandidatesScored == 0 && quiet.Camps[0].FissionDestination == null, "no push, no search and no cache");
        // A search below the proposal threshold caches a destination without founding (search threshold lowered).
        var cache = World(true, new[] { 8, 8 }); Explore(cache, cache.Camps[0]); Ratio(cache, cache.Camps[0], .9); // gain 0.9 G*: searches (above 0.8 G*) but does not found
        var cm = Pass(cache, out var cachedMetrics);
        var target = cache.Camps[0].FissionDestination;
        Check(cm == null && target != null && target.Regime == (int)FissionRegime.Agricultural && target.Turn == cache.Turn && cachedMetrics.FissionSearches == 1, "a search below the proposal threshold caches the destination with its regime and turn");
        int cachedTile = target.Tile;
        // The cache persists: no rerank while valid, even with a different best tile appearing.
        cachedMetrics = null; cache.Turn++; Pass(cache, out var again);
        Check(cache.Camps[0].FissionDestination == target && again.FissionSearches == 0 && again.FissionValidations == 1 && target.Tile == cachedTile, "a valid cache is revalidated but never searched or reranked");
        // Regime change invalidates it even with no push; it is then searched afresh on the next pressured turn.
        cache.Camps[0].Technologies[(int)Technology.Cultivation] = false; cache.Camps[0].Technology = Technology.AnimalHusbandry; cache.Camps[0].Technologies[(int)Technology.AnimalHusbandry] = true; Quiet(cache);
        Pass(cache, out var regime);
        Check(cache.Camps[0].FissionDestination == null && regime.FissionCacheInvalidations == 1, "a regime change invalidates the cached destination");
        // Occupied or within spacing of another camp invalidates it.
        var block = World(true, new[] { 8, 8 }); Explore(block, block.Camps[0]); Ratio(block, block.Camps[0], .9); Pass(block, out _);
        int site = block.Camps[0].FissionDestination.Tile;
        block.Camps.Add(new Camp { Id = block.NextId++, Player = 5, Tile = block.Map.Neighbors(site).First(n => block.Map.Distance(n, block.Camps[0].Tile) >= 3), Population = 50, PlantFood = 6000 }); block.PlayerCount = 6; Ownership.Bind(block);
        Pass(block, out var blocked);
        var after = block.Camps[0].FissionDestination;
        Check(blocked.FissionCacheInvalidations >= 1 && (after == null || after.Tile != site) && (after == null || block.Camps.All(c => c == block.Camps[0] || block.Map.Distance(c.Tile, after.Tile) >= 3)), "a camp founded within spacing invalidates the cached destination and the re-search respects spacing");
    }

    private static void Knowledge()
    {
        // Paleolithic: current vision only, even if a memory block exists; with a hunter in view of a ring tile it finds one.
        var w = World(false, new[] { 8, 8 }); var parent = w.Camps[0]; Explore(w, parent); 
        Check(!ContactKnowledge.Remembers(parent.People) && Fission.RegimeOf(parent) == FissionRegime.Paleolithic, "a Paleolithic fixture does not remember");
        var none = Pass(w, out var m);
        Check(none == null && parent.FissionDestination == null && m.FissionSearches == 1 && m.FissionCandidatesScored == 0, "explored tiles are ignored and a camp's own vision sees no site at distance three");
        // A hunter two tiles out sees ring tiles: only visible ring tiles are candidates.
        var seen = World(false, new[] { 8, 8 }); var camp = seen.Camps[0]; 
        int outpost = seen.Map.Radius(camp.Tile, 2).Where(n => seen.Map.Distance(n, camp.Tile) == 2).OrderBy(n => n).First();
        seen.Hunters.Add(new Hunter { Id = seen.NextId++, SourceCamp = camp.Id, SocietyId = camp.SocietyId, Player = camp.Player, Tile = outpost, Population = 6, CarriedFood = 10 });
        var visible = new HashSet<int>(seen.Map.Radius(camp.Tile, 2).Concat(seen.Map.Radius(outpost, 2)));
        var found = Pass(seen, out var sm);
        Check(found != null && visible.Contains(found.Tile) && sm.FissionCandidatesScored > 0, "with a hunter's vision the Paleolithic camp founds only at a visible tile");
        Check(sm.FissionCandidatesScored <= seen.Map.Radius(camp.Tile, 3).Count(n => seen.Map.Distance(n, camp.Tile) == 3 && visible.Contains(n)) + 0 + sm.FissionCandidatesScored - sm.FissionCandidatesScored, "candidates come from the visible ring");
        // A Neolithic Society adds explored tiles: the same outpostless camp now founds at an explored, not visible, site.
        var neo = World(true, new[] { 8, 8 }); var ncamp = neo.Camps[0]; 
        var memory = ContactKnowledge.MemoryOf(neo, ncamp.People); int only = neo.Map.Radius(ncamp.Tile, 3).Where(n => neo.Map.Distance(n, ncamp.Tile) == 3).OrderBy(n => n).Last(); memory.Explored.Add(only);
        Check(!neo.Map.Radius(ncamp.Tile, 2).Contains(only), "the explored tile lies outside current vision");
        var nfound = Pass(neo, out var nm);
        Check(nfound != null && nfound.Tile == only && nm.FissionCandidatesScored == 1, "a Neolithic Society founds at an explored tile outside its vision, and only there");
        Check(Daughter(neo, ncamp).Tile == only, "the daughter camp stands on the explored tile");
    }

    private static void Commit()
    {
        var w = World(true, new[] { 8, 8 }); var parent = w.Camps[0]; Explore(w, parent); parent.FissionPolicy = FissionPolicy.Encourage;
        var ps = parent.People; ps.Experience[0] = 10; ps.Experience[1] = 20; ps.Experience[2] = 30; ps.CraftExperience["Toolmaking"] = 123; ps.NeolithicTechnologies.Add("Toolmaking"); ps.UnlockedSpecialists.Add(WorkforceRoles.Toolmaker);
        ps.SpeciesExperience.Add(new SpeciesPractice { Species = "goat", Experience = 77 }); ps.Policy = new PolicyState { HuntingInclination = .61, GatheringInclination = .92, FishingInclination = .44 }; ps.Mandate.Risk = .31; ps.Mandate.Persistence = .22;
        ps.Progression.CompletionDays = 500; ps.Progression.CompletionTurn = 2; ps.Progression.CompletionRule = "Mastery"; ps.TransitionTurn = 2; ps.OpeningTransitionProcessed = true;
        ps.TransitionSeeds.Add(new TransitionSeed { Technology = Technology.Cultivation, Settlement = parent.Id, Tile = parent.Tile, Days = 400 });
        w.MaterialKnowledge.Add(new MaterialKnowledge { Society = ps.Id, Tile = 5, Source = "src-5" }); w.MaterialKnowledge.Add(new MaterialKnowledge { Society = ps.Id, Tile = 9, Source = "src-9" });
        parent.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 50 }); parent.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Toolmaker, Population = 10 });
        parent.CraftingPriorities.Cloth = .33; parent.StonePriority = 2; parent.ManagedBreedingIntensity = .42;
        var before = SaveCodec.Fingerprint(w); int nextBefore = w.NextId, playersBefore = w.LivePlayers;
        var report = Pass(w, out var metrics);
        Check(report != null && metrics.FissionCommits == 1, "the Encouraging parent fissions");
        var daughter = Daughter(w, parent); var ds = daughter.People;
        Check(daughter.Population == 40 && parent.Population == 60 && report.Daughter == 40 && report.ParentBefore == 100, "Encourage splits 100 into 60 and 40");
        Check(ds.Id == nextBefore && daughter.Id == nextBefore + 1 && w.NextId == nextBefore + 2 && ds.Player == playersBefore && w.PlayerCount == playersBefore + 1 && w.LivePlayers == playersBefore + 1, "the daughter takes the next Society, camp and player IDs");
        Check(report.DaughterSociety == ds.Id && report.DaughterCamp == daughter.Id && report.DaughterPlayer == ds.Player && report.ParentSociety == ps.Id && report.Policy == FissionPolicy.Encourage && report.Affinity == 1, "the report names both sides, the policy and the affinity");
        Check(ds.ParentSociety == ps.Id && ds.KinAffinity == 1 && ps.ParentSociety == 0, "lineage is stored on the daughter with the Encourage affinity");
        Check(daughter.FissionPolicy == FissionPolicy.Neutral && daughter.FissionDestination == null && parent.FissionDestination == null && parent.FissionPolicy == FissionPolicy.Encourage, "the daughter is Neutral with no destination; the parent keeps its policy");
        Check(daughter.EconomyAutomationTurns == -1 && daughter.CraftingPriorities.Cloth == .33 && daughter.StonePriority == 2 && Math.Abs(daughter.ManagedBreedingIntensity - .42) < 1e-12 && daughter.CultivationFocusTile == -1 && daughter.CraftingPriorities != parent.CraftingPriorities, "the daughter copies the economic settings under continuous automation");
        // Knowledge and practice equal the parent's at the moment of fission, then independent.
        Check(ds.Experience.SequenceEqual(ps.Experience) && ds.Experience != ps.Experience && ds.Technologies.SequenceEqual(ps.Technologies) && ds.Technology == ps.Technology && ds.TransitionTurn == ps.TransitionTurn && ds.SecondaryDomain == ps.SecondaryDomain && ds.ForcedTransition == ps.ForcedTransition, "domain experience and technologies are copied");
        Check(ds.SpeciesExperience.Count == 1 && ds.SpeciesExperience[0].Species == "goat" && ds.SpeciesExperience[0].Experience == 77 && ds.SpeciesExperience[0] != ps.SpeciesExperience[0], "species practice is copied, not shared");
        Check(ds.CraftExperience.SequenceEqual(ps.CraftExperience) && ds.CraftExperience != ps.CraftExperience && ds.NeolithicTechnologies.SequenceEqual(ps.NeolithicTechnologies) && ds.UnlockedSpecialists.SequenceEqual(ps.UnlockedSpecialists), "craft experience, Neolithic technologies and specialists are copied");
        Check(ds.Policy.HuntingInclination == .61 && ds.Policy.GatheringInclination == .92 && ds.Policy.FishingInclination == .44 && ds.Policy != ps.Policy && ds.Mandate.Risk == .31 && ds.Mandate.Persistence == .22 && ds.Mandate != ps.Mandate, "policy preferences and mandate are copied");
        Check(ds.Progression.Era == Era.Neolithic && ds.Progression.CompletionDays == 500 && ds.Progression.CompletionTurn == 2 && ds.Progression.CompletionRule == "Mastery" && ds.Progression.EraStartDays == ps.Progression.EraStartDays && ds.Progression.ForcedTransitionDays == ps.Progression.ForcedTransitionDays && ds.Progression.MajorityEligibilityDays == ps.Progression.MajorityEligibilityDays && ds.Progression != ps.Progression, "progression and era are copied");
        Check(ds.OpeningTransitionProcessed && ds.TransitionSeeds.Count == 1 && ds.TransitionSeeds[0].Days == 400 && ds.TransitionSeeds[0] != ps.TransitionSeeds[0], "the opening reward is not replayed and seeds are copied");
        Check(ds.PolicySeed == (int)Numbers.Hash(w.Seed, Fission.PolicySeedStream, parent.Id, 3) && ds.PolicySeed != ps.PolicySeed, "the policy seed comes from the keyed stream (parent id, turn)");
        Check(w.MaterialKnowledge.Count(k => k.Society == ds.Id) == 2 && w.MaterialKnowledge.Count(k => k.Society == ps.Id) == 2 && w.MaterialKnowledge.Where(k => k.Society == ds.Id).Select(k => k.Source).OrderBy(x => x).SequenceEqual(new[] { "src-5", "src-9" }), "material knowledge is copied");
        // Kin contacts and copied memory (Neolithic parent with explored tiles).
        var pm = ps.Memory; var dm = ds.Memory;
        Check(dm != null && pm.Contacts.Any(c => c.Camp == daughter.Id && c.Source == ContactSource.Kin && c.Tile == daughter.Tile) && dm.Contacts.Any(c => c.Camp == parent.Id && c.Source == ContactSource.Kin && c.Tile == parent.Tile), "parent and daughter record each other as kin");
        Check(dm.Explored.Chunks.SequenceEqual(pm.Explored.Chunks) && dm.Explored.Words.SequenceEqual(pm.Explored.Words) && dm.Explored != pm.Explored, "explored tiles are copied");
        // Workforce and stores: farmers stay on the parent's fields; specialists and gatherers split proportionally.
        Check(daughter.Workforce.Assignments.All(a => a.Role != WorkforceRoles.Farmer) && parent.Workforce.Assignments.Single(a => a.Role == WorkforceRoles.Farmer).Population == 30 && daughter.Workforce.Assignments.Single(a => a.Role == WorkforceRoles.Toolmaker).Population == 4 && parent.Workforce.Assignments.Single(a => a.Role == WorkforceRoles.Toolmaker).Population == 6, "workforce splits by largest remainder and the parent keeps fields");
        Check(daughter.Workforce.Assigned <= daughter.Population && parent.Workforce.Assigned <= parent.Population && daughter.GathererPopulation == 36, "workforces stay within residents");
        Near(daughter.PlantFood, 12000 * .4, "food moves in the people fraction"); Near(parent.PlantFood, 12000 * .6, "the parent keeps the rest");
        Check(daughter.Moves == 0 && daughter.StationaryTurns == 0 && !daughter.IsVillage && daughter.DeficitTurns == 0, "a fresh settlement has no history");
        Check(record().Events.Any(e => e.Contains("split") && e.Contains("camp " + daughter.Id)) && w.Camps.Select(c => c.Tile).Distinct().Count() == 2, "a journal line is written and camps do not share a tile");
        Check(record().Demographics.Single(d => d.Camp == parent.Id).SplitOff == 40 && record().Demographics.Single(d => d.Camp == parent.Id).Explanation.Contains("40 of them then left"), "the demographic explanation carries the split");
        TurnResolver.Invariants(w, 100);
        Check(SaveCodec.Fingerprint(w) != before, "fission changes authoritative state");
        // Two daughters of one parent take the lower of their affinities.
        var two = World(true, new[] { 8, 8 }); var p2 = two.Camps[0]; Explore(two, p2); p2.FissionPolicy = FissionPolicy.Encourage; Pass(two, out _);
        p2.FissionPolicy = FissionPolicy.Discourage; two.Turn++; p2.Population = 100; Pass(two, out _);
        var daughters = two.Societies.Where(s => s.ParentSociety == p2.SocietyId).OrderBy(s => s.Id).ToArray();
        Check(daughters.Length == 2 && daughters[0].KinAffinity == 1 && daughters[1].KinAffinity == .5 && Kinship.Affinity(daughters[0], daughters[1]) == .5 && Kinship.Affinity(daughters[1], p2.People) == .5 && Kinship.Affinity(daughters[0], p2.People) == 1, "sibling affinity derives as the lower of the two stored affinities");
    }
    private static TurnRecord record() => lastRecord;

    // Every ledger is conserved exactly across a fission with hunters, typed food, inventory, herds and fractional progress.
    private static Dictionary<string, double> Ledgers(WorldState w)
    {
        var r = new Dictionary<string, double>(StringComparer.Ordinal);
        void Add(string key, double value) { r.TryGetValue(key, out double old); r[key] = old + value; }
        foreach (var c in w.Camps)
        {
            Add("people", c.Population); Add("plant", c.PlantFood); Add("animal", c.AnimalFood); Add("aquatic", c.AquaticFood); Add("growth", c.GrowthRemainder); Add("death", c.DeathRemainder);
            foreach (var f in InventoryFields) Add("inv." + f.Name, (double)f.GetValue(c.Inventory));
        }
        foreach (var h in w.Hunters) { Add("people", h.Population); Add("carried", h.CarriedFood); }
        foreach (var h in w.Herds) { Add("animals." + h.Species + "." + (h.DomesticCamp >= 0), h.Animals); Add("herd.growth", h.GrowthRemainder); Add("herd.unmet", h.UnmetFood + h.UnmetFreshwater); Add("herd.starting", h.StartingAnimals); Add("herd.death", h.DeathRemainder); Add("herd.potential", h.AnimalFoodPotential); }
        Add("fixedHousing", w.FixedHousing.Sum(s => s.Units));
        return r;
    }
    private static void Conservation()
    {
        foreach (var policy in (FissionPolicy[])Enum.GetValues(typeof(FissionPolicy)))
        {
            var w = World(true, new[] { 8, 8 }, new[] { 20, 3 }); var parent = w.Camps[0]; Explore(w, parent); parent.FissionPolicy = policy; parent.Population = 137;
            MakeNeolithic(parent, Technology.Cultivation); parent.Technologies[(int)Technology.AnimalHusbandry] = true;
            double seed = 1.37; foreach (var f in InventoryFields) { f.SetValue(parent.Inventory, seed); seed += 3.1; }
            parent.PlantFood = 4321.5; parent.AnimalFood = 210.25; parent.AquaticFood = 99.125; parent.GrowthRemainder = .61; parent.DeathRemainder = .37;
            parent.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 41 }); parent.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = 23 }); parent.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Potter, Population = 7 });
            w.Herds.Add(new Herd { Id = w.NextId++, Tile = parent.Tile, Animals = 53, DomesticCamp = parent.Id, OwnerSociety = parent.SocietyId, Species = "goat", GrowthRemainder = .4, UnmetFood = 3, UnmetFreshwater = 2, StartingAnimals = 50, DeathRemainder = .2, AnimalFoodPotential = 9 });
            w.Herds.Add(new Herd { Id = w.NextId++, Tile = parent.Tile, Animals = 7, DomesticCamp = parent.Id, OwnerSociety = parent.SocietyId, Species = "horse" });
            w.Herds.Add(new Herd { Id = w.NextId++, Tile = w.Camps[1].Tile, Animals = 90, DomesticCamp = w.Camps[1].Id, OwnerSociety = w.Camps[1].SocietyId, Species = "goat" });
            w.Herds.Add(new Herd { Id = w.NextId++, Tile = w.Map.At(1, 1), Animals = 60, Species = "antelope" });
            w.Hunters.Add(new Hunter { Id = w.NextId++, SourceCamp = parent.Id, SocietyId = parent.SocietyId, Player = parent.Player, Tile = parent.Tile, Population = 9, CarriedFood = 12.5 });
            w.FixedHousing.Add(new FixedHousingStock { CampId = parent.Id, Tile = parent.Tile, Units = 17.5, LastUpdatedDays = w.Days });
            var fieldTile = w.Map.Neighbors(parent.Tile).First(); w.Tiles[fieldTile].CultivationOperator = parent.Id; w.Tiles[fieldTile].CultivationCapital = .4;
            var ledgerBefore = Ledgers(w); int animalsBefore = w.Herds.Count; int tile = parent.Tile;
            var report = Pass(w, out _);
            Check(report != null && report.Policy == policy, "the " + policy + " parent fissions");
            var ledgerAfter = Ledgers(w); var daughter = Daughter(w, parent);
            foreach (var key in ledgerBefore.Keys) Near(ledgerAfter[key], ledgerBefore[key], policy + " conserves " + key, 1e-12);
            Check(ledgerAfter.Keys.All(k => ledgerBefore.ContainsKey(k) || Math.Abs(ledgerAfter[k]) < 1e-12), policy + " adds no new ledger");
            int expected = Kinship.DaughterSize(137, policy);
            Check(daughter.Population == expected && parent.Population == 137 - expected && daughter.Population < parent.Population, policy + " daughter is floor(N*f) and smaller than the parent");
            Check(parent.Tile == tile && w.Tiles[fieldTile].CultivationOperator == parent.Id && w.FixedHousing.Single().CampId == parent.Id && w.FixedHousing.Single().Units == 17.5, policy + " parent keeps site, fields and fixed Housing");
            Check(w.Hunters.Single().SourceCamp == parent.Id && w.Hunters.Single().Population == 9, policy + " hunters stay with the parent");
            int goatsMoved = w.Herds.Where(h => h.DomesticCamp == daughter.Id && h.Species == "goat").Sum(h => h.Animals);
            Check(goatsMoved == (int)(53L * expected / 137) && w.Herds.Where(h => h.DomesticCamp == daughter.Id).All(h => h.Tile == daughter.Tile && h.OwnerSociety == daughter.SocietyId && h.Animals > 0) && w.Herds.Count(h => h.DomesticCamp == w.Camps[1].Id) == 1 && w.Herds.Single(h => h.DomesticCamp < 0).Animals == 60, policy + " domestic herds move in whole animals; foreign and wild herds are untouched");
            Check(w.Herds.Count == animalsBefore + (goatsMoved > 0 ? 1 : 0) + (w.Herds.Any(h => h.DomesticCamp == daughter.Id && h.Species == "horse") ? 1 : 0), policy + " new herds have fresh IDs");
            Check(w.Herds.Select(h => h.Id).Concat(w.Camps.Select(c => c.Id)).Concat(w.Hunters.Select(h => h.Id)).Concat(w.Societies.Select(s => s.Id)).Distinct().Count() == w.Herds.Count + w.Camps.Count + w.Hunters.Count + w.Societies.Count && w.Herds.All(h => h.Id < w.NextId), policy + " IDs are unique");
            TurnResolver.Invariants(w, 137 + 9 + 100);
        }
    }

    private static void Viability()
    {
        // No viable site: a waterless footprint everywhere, then starved food, then a disabled switch, then too small a daughter.
        WorldState Pressured() { var w = World(true, new[] { 8, 8 }); Explore(w, w.Camps[0]); return w; }
        var dry = Pressured(); foreach (var t in dry.Tiles) for (int s = 0; s < 4; s++) dry.Tiles[t.Id].Seasons[s].MaxFreshwater = 0;
        var report = Pass(dry, out var dm);
        Check(report == null && dry.Camps[0].FissionDestination == null && dry.Camps.Count == 1 && dm.FissionCandidatesScored > 0 && dm.FissionCommits == 0, "no site with freshwater means no fission and no cache");
        var poor = Pressured(); foreach (var t in poor.Tiles) { t.Food = 30; t.PotentialFood = 30; for (int s = 0; s < 4; s++) { poor.Tiles[t.Id].Seasons[s].MaxFood = 30; poor.Tiles[t.Id].Seasons[s].MaxPotentialFood = 30; } }
        Check(Pass(poor, out _) == null && poor.Camps[0].FissionDestination == null && poor.Camps.Count == 1, "a site that cannot feed the daughter is unviable");
        var off = Pressured(); off.Config.FissionEnabled = false; var om = new ResolutionMetrics();
        Check(Pass(off, out om) == null && om.FissionCampsEvaluated == 0 && off.Camps.Count == 1, "fission disabled evaluates nothing");
        var small = Pressured(); small.Camps[0].Population = 39;
        Check(Pass(small, out var sm) == null && sm.FissionSearches == 0 && small.Camps.Count == 1, "a daughter below the minimum population never forms (and is never searched for)");
        var discourage = Pressured(); discourage.Camps[0].FissionPolicy = FissionPolicy.Discourage; discourage.Camps[0].Population = 79; 
        Check(Pass(discourage, out _) == null && discourage.Camps.Count == 1, "Discourage at 79 residents gives 11, below the minimum daughter");
        discourage.Camps[0].Population = 80; Check(Pass(discourage, out _) != null && Daughter(discourage, discourage.Camps[0]).Population == 12, "Discourage at 80 residents gives exactly the minimum daughter");
        var calm = Pressured(); Quiet(calm); Check(Pass(calm, out var cm) == null && cm.FissionSearches == 0, "a gain far below G* gives no search");
        var threshold = Pressured(); Ratio(threshold, threshold.Camps[0], .9); Check(Pass(threshold, out _) == null && threshold.Camps[0].FissionDestination != null, "the gain must reach G*: 0.9 G* caches a destination but does not found");
        var destitute = Pressured(); destitute.Camps[0].Population = 0; Check(Pass(destitute, out var dem) == null && dem.FissionCampsEvaluated == 0, "an empty camp is not evaluated");
        // A pastoral daughter also needs herd forage and water; a herd too big for the footprint is unviable.
        var herd = Pressured(); MakeNeolithic(herd.Camps[0], Technology.AnimalHusbandry); herd.Camps[0].Technologies[(int)Technology.Cultivation] = false; herd.Camps[0].Technology = Technology.AnimalHusbandry;
        herd.Herds.Add(new Herd { Id = herd.NextId++, Tile = herd.Camps[0].Tile, Animals = 60, DomesticCamp = herd.Camps[0].Id, OwnerSociety = herd.Camps[0].SocietyId, Species = "goat" });
        Check(Fission.RegimeOf(herd.Camps[0]) == FissionRegime.Nomadic && Pass(herd, out var hm) != null && hm.FissionCommits == 1, "a pastoral camp with a supported herd fissions");
        var greedy = Pressured(); MakeNeolithic(greedy.Camps[0], Technology.AnimalHusbandry); greedy.Camps[0].Technologies[(int)Technology.Cultivation] = false; greedy.Camps[0].Technology = Technology.AnimalHusbandry;
        greedy.Herds.Add(new Herd { Id = greedy.NextId++, Tile = greedy.Camps[0].Tile, Animals = 40000, DomesticCamp = greedy.Camps[0].Id, OwnerSociety = greedy.Camps[0].SocietyId, Species = "bison" });
        Check(Pass(greedy, out _) == null && greedy.Camps.Count == 1, "a pastoral daughter whose herd share exceeds forage and water has no viable site");
    }

    private static void Spacing()
    {
        // Every daughter stands at least the spacing from every camp, including another daughter founded the same turn.
        var w = World(true, new[] { 4, 4 }, new[] { 10, 4 }, new[] { 16, 4 }, new[] { 7, 10 }, new[] { 13, 10 });
        foreach (var c in w.Camps) { Explore(w, c); }
        Pass(w, out var m);
        int spacing = w.Config.MinimumCampSpacing;
        var camps = w.Camps.ToArray();
        for (int i = 0; i < camps.Length; i++) for (int j = i + 1; j < camps.Length; j++) Check(w.Map.Distance(camps[i].Tile, camps[j].Tile) >= spacing, "no two camps are within " + spacing + " hexes");
        Check(camps.Length > 5 && m.FissionCommits == camps.Length - 5, "every camp count difference is a committed fission");
        // A conflict is dropped, keeps its cache, and the loser is revalidated next turn.
        var c2 = World(true, new[] { 8, 8 }, new[] { 14, 8 }); foreach (var c in c2.Camps) { Explore(c2, c); }
        // Only the tile between the two parents (distance 3 from each) is explored, and only by them.
        var mid = c2.Map.At(11, 8); Check(c2.Map.Distance(c2.Camps[0].Tile, mid) == 3 && c2.Map.Distance(c2.Camps[1].Tile, mid) == 3, "the middle tile is three hexes from both parents");
        foreach (var c in c2.Camps) { c.People.Memory = new ContactMemory(c2.Config.Width); c.People.Memory.Explored.Add(mid); }
        Pass(c2, out var cm);
        Check(cm.FissionProposals == 2 && cm.FissionCommits == 1 && cm.FissionDropped == 1, "two proposals for one tile commit once and drop once");
        Check(c2.Societies.Single(s => s.ParentSociety > 0).ParentSociety == c2.Camps[0].SocietyId, "the lower camp id wins a conflict");
        var loser = c2.Camps[1]; Check(loser.FissionDestination != null && loser.FissionDestination.Tile == mid && loser.Population == 100, "the dropped proposal keeps its cache and its people");
        c2.Turn++; Pass(c2, out var next);
        Check(loser.FissionDestination == null && next.FissionCacheInvalidations >= 1 && c2.Camps.Count == 3, "the next turn revalidates: the site is now occupied, so the cache is cleared");
    }

    private static void Bounded()
    {
        // Cost follows active state: unpressured camps cost one push test and nothing else.
        var quiet = World(true, new[] { 4, 4 }, new[] { 10, 4 }, new[] { 16, 4 }, new[] { 7, 10 }, new[] { 13, 10 }); foreach (var c in quiet.Camps) { Explore(quiet, c); } Quiet(quiet);
        Pass(quiet, out var q);
        Check(q.FissionCampsEvaluated == 5 && q.FissionSearches == 0 && q.FissionValidations == 0 && q.FissionCandidatesScored == 0 && q.FissionTileProjections == 0 && q.FissionProposals == 0, "five unpressured camps perform no validation, search or ecology projection");
        var busy = World(true, new[] { 4, 4 }, new[] { 10, 4 }, new[] { 16, 4 }, new[] { 7, 10 }, new[] { 13, 10 }); foreach (var c in busy.Camps) { Explore(busy, c); }
        Pass(busy, out var b);
        int ring = 18;
        Check(b.FissionSearches == 5 && b.FissionCandidatesScored <= b.FissionSearches * ring && b.FissionCandidatesScored > 0, "candidates per search are bounded by the ring at the fission radius (" + b.FissionCandidatesScored + ")");
        Check(b.FissionTileProjections <= b.FissionCandidatesScored * 7 && b.FissionTileProjections > 0, "tile projections are bounded by seven per candidate (" + b.FissionTileProjections + ")");
        // A cached destination is validated with one footprint and never searched while it remains valid.
        var cached = World(true, new[] { 8, 8 }); Explore(cached, cached.Camps[0]); Ratio(cached, cached.Camps[0], .9);
        Pass(cached, out var first); cached.Turn++; Pass(cached, out var second);
        Check(first.FissionSearches == 1 && second.FissionSearches == 0 && second.FissionValidations == 1 && second.FissionTileProjections <= 7 && second.FissionCandidatesScored == 0, "a valid cache costs one validation footprint");
        // The fission radius bounds the search: a radius-two search finds nothing because spacing is three.
        var narrow = World(true, new[] { 8, 8 }); Explore(narrow, narrow.Camps[0]); narrow.Config.FissionRadius = 2; 
        Check(Pass(narrow, out var nm) == null && nm.FissionCandidatesScored == 0, "a radius shorter than the camp spacing has no candidate");
        var wide = World(true, new[] { 8, 8 }); Explore(wide, wide.Camps[0], 6); wide.Config.FissionRadius = 5;  Pass(wide, out var wm);
        Check(wm.FissionCandidatesScored > ring && wm.FissionCandidatesScored <= 3 * 5 * 6 - 3 * 2 * 3, "a wider radius scores the larger bounded disc outside the spacing");
    }

    private static void Policy()
    {
        var w = World(true, new[] { 8, 8 }, new[] { 18, 3 }); var own = w.Camps[0]; var o = new AgentObservationBuilder().Build(w, 0);
        Check(o.Settlements.Single().FissionPolicy == FissionPolicy.Neutral && o.Settlements.Single().FissionDestination == -1, "the owner sees Neutral and no destination by default");
        var mask = LegalActions.Build(o).Single(m => m.Entity == own.Id);
        Check(Enum.GetValues(typeof(FissionPolicy)).Cast<FissionPolicy>().All(p => mask.Actions.Any(a => a.Kind == OrderKind.FissionPolicy && a.Target == (int)p)), "all three policies are legal actions for a settlement");
        Check(mask.Allows(new Order(OrderKind.FissionPolicy, own.Id, target: 2)) && !mask.Allows(new Order(OrderKind.FissionPolicy, own.Id, target: 3)), "the mask allows valid policies only");
        Check(OrderRules.Validate(o, new Order(OrderKind.FissionPolicy, own.Id, target: -1)) != null && OrderRules.Validate(o, new Order(OrderKind.FissionPolicy, own.Id, target: 1, destination: 4)) != null && OrderRules.Validate(o, new Order(OrderKind.FissionPolicy, own.Id, target: 1, population: 2)) != null && OrderRules.Validate(o, new Order(OrderKind.FissionPolicy, 9999, target: 1)) != null, "malformed or foreign policy orders are rejected");
        Check(OrderRules.Validate(o, new Order(OrderKind.FissionPolicy, own.Id, target: 1)) == null, "a valid policy order validates");
        w.Hunters.Add(new Hunter { Id = w.NextId++, SourceCamp = own.Id, SocietyId = own.SocietyId, Player = 0, Tile = own.Tile, Population = 6, CarriedFood = 5 }); var oh = new AgentObservationBuilder().Build(w, 0);
        Check(OrderRules.Validate(oh, new Order(OrderKind.FissionPolicy, w.Hunters[0].Id, target: 1)) != null, "a hunter group has no fission policy");
        // Applied at Resolve: one ordinary order plus one policy is accepted; two policies conflict.
        var resolver = new TurnResolver();
        var orders = new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.NoOp, own.Id), new Order(OrderKind.FissionPolicy, own.Id, target: (int)FissionPolicy.Discourage) } };
        var prepared = TurnPlanner.Prepare(w, orders, null, new PhaseExecution());
        Check(resolver.Validate(w, 0, new Order(OrderKind.FissionPolicy, own.Id, target: 1)) == null, "the resolver validates policy orders through the observation contract");
        var record = resolver.Resolve(w, prepared);
        Check(own.FissionPolicy == FissionPolicy.Discourage && record.Orders.Any(x => x.Contains("FissionPolicy")) && !record.Events.Any(e => e.StartsWith("Rejected")), "a policy accompanying an ordinary order is applied at resolution");
        var conflict = new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.FissionPolicy, own.Id, target: 1), new Order(OrderKind.FissionPolicy, own.Id, target: 2) } };
        var prep2 = TurnPlanner.Prepare(w, conflict, null, new PhaseExecution());
        var rec2 = resolver.Resolve(w, prep2);
        Check(own.FissionPolicy == FissionPolicy.Discourage && rec2.Events.Any(e => e.Contains("conflicting orders")), "two policy orders for one camp conflict and change nothing");
        // The heuristic AI keeps Neutral: it never emits a policy order.
        var ai = new HeuristicTribePolicy().Decide(new AgentObservationBuilder().Build(w, 1), w.Camps[1].Mandate);
        Check(ai.Orders.All(x => x.Kind != OrderKind.FissionPolicy) && w.Camps[1].FissionPolicy == FissionPolicy.Neutral, "the heuristic policy never changes a fission policy");
    }

    private static void Observations()
    {
        var w = World(true, new[] { 8, 8 }, new[] { 20, 12 }); var parent = w.Camps[0]; Explore(w, parent); parent.FissionPolicy = FissionPolicy.Neutral;
        parent.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Potter, Population = 5 });
        // A cached destination is visible to its owner only.
        Ratio(w, parent, .9); Pass(w, out _);
        var cached = parent.FissionDestination; Check(cached != null, "a destination is cached for the owner");
        var own = new AgentObservationBuilder().Build(w, 0); var foreign = new AgentObservationBuilder().Build(w, 1);
        Check(own.Settlements.Single().FissionDestination == cached.Tile && foreign.Settlements.Single().FissionDestination == -1 && foreign.Settlements.Single().Id != parent.Id, "a cached destination appears only in its owner's observation");
        // Found two daughters and inspect lineage and kin.
        Pressure(w, 0); w.Config.FissionGainThreshold = 5; /* Neutral daughters stay put */ parent.FissionPolicy = FissionPolicy.Encourage; parent.Population = 100; Pass(w, out _);
        parent.FissionPolicy = FissionPolicy.Discourage; w.Turn++; parent.Population = 100; Pass(w, out _);
        var kids = w.Societies.Where(s => s.ParentSociety == parent.SocietyId).OrderBy(s => s.Id).ToArray(); Check(kids.Length == 2, "two daughters exist");
        var parentView = new AgentObservationBuilder().Build(w, 0);
        Check(parentView.ParentSociety == 0 && parentView.KinAffinity == 0 && parentView.KinRelations.Count == 2 && parentView.KinRelations.All(k => k.Relation == KinRelation.Daughter) && parentView.KinRelations.Select(k => k.Affinity).OrderBy(x => x).SequenceEqual(new[] { .5, 1 }), "the parent sees its daughters with their affinities");
        var kidView = new AgentObservationBuilder().Build(w, kids[0].Player);
        Check(kidView.ParentSociety == parent.SocietyId && kidView.KinAffinity == 1 && kidView.KinRelations.Count == 2 && kidView.KinRelations.Single(k => k.Relation == KinRelation.Parent).Affinity == 1 && kidView.KinRelations.Single(k => k.Relation == KinRelation.Sibling).Society == kids[1].Id && kidView.KinRelations.Single(k => k.Relation == KinRelation.Sibling).Affinity == .5, "a daughter sees its parent and its sibling with the lower affinity");
        var stranger = new AgentObservationBuilder().Build(w, 1);
        Check(stranger.KinRelations.Count == 0 && stranger.ParentSociety == 0, "an unrelated Society sees no kin");
        Check(!stranger.Rivals.Any(r => r.Player == kids[0].Player) || w.Map.Radius(w.Camps.Single(c => c.Player == 1).Tile, 2).Contains(w.Camps.Single(c => c.SocietyId == kids[0].Id).Tile), "foreign daughters appear only through ordinary vision");
        var dto = ObservationContract.Deserialize(ObservationContract.Serialize(kidView, kids[0].Mandate));
        Check(dto.Schema == 18 && dto.ParentSociety == parent.SocietyId && dto.KinAffinity == 1 && dto.KinRelations.Length == 2 && dto.Settlements.Single().FissionPolicy == FissionPolicy.Neutral && dto.Settlements.Single().FissionDestination == -1, "the serialized contract carries lineage, kin and the settlement's fission state");
        var own2 = ObservationContract.Deserialize(ObservationContract.Serialize(new AgentObservationBuilder().Build(w, 0), parent.Mandate));
        Check(own2.Settlements.Single().FissionPolicy == FissionPolicy.Discourage, "the serialized contract carries the owner's policy");
        // Daughters are AI players: they take part in planning, the human stays player 0.
        var plan = AiPlanning.Decide(w, new PhaseExecution(), 0);
        Check(!plan.Orders.ContainsKey(0) && kids.All(k => plan.Orders.ContainsKey(k.Player)) && plan.Orders.ContainsKey(1), "new player slots are planned by the AI and the human is excluded");
        Check(w.LivePlayers == 4 && w.PlayerCount == 4 && w.Camps.Select(c => c.Player).Distinct().Count() == w.Camps.Count, "the live player count follows the new slots");
    }

    private static string Key(WorldState w) => SaveCodec.Fingerprint(w);
    private static void Persistence()
    {
        var w = World(true, new[] { 8, 8 }, new[] { 20, 12 }); var parent = w.Camps[0]; Explore(w, parent); parent.FissionPolicy = FissionPolicy.Encourage;
        Explore(w, w.Camps[1]); w.Config.FissionGainEncourage = 0; w.Config.FissionGainThreshold = Fission.EstimateSplit(w, w.Camps[1], FissionPolicy.Neutral).Gain / .9; // camp 1 (Neutral) caches, camp 0 (Encourage) founds
        Pass(w, out _); w.History.Add(record());
        w.Turn++;
        var loaded = SaveCodec.Load(SaveCodec.Save(w));
        Check(Key(loaded) == Key(w), "the fingerprint survives a round trip with new player slots and lineage");
        Check(loaded.PlayerCount == w.PlayerCount && loaded.PlayerCount == 3 && loaded.LivePlayers == 3, "the live player count round trips");
        var ls = loaded.Societies.Single(s => s.ParentSociety > 0); Check(ls.ParentSociety == parent.SocietyId && ls.KinAffinity == 1 && ls.Player == 2, "lineage round trips");
        Check(loaded.Camps.Single(c => c.Id == parent.Id).FissionPolicy == FissionPolicy.Encourage && loaded.History.Any(h => h.Fissions != null && h.Fissions.Count == 1), "policy and the fission history round trip");
        Check(loaded.Camps.Single(c => c.Id == w.Camps[1].Id).FissionDestination != null && w.Camps[1].FissionDestination != null && loaded.Camps.Single(c => c.Id == w.Camps[1].Id).FissionDestination.Tile == w.Camps[1].FissionDestination.Tile, "a cached destination round trips");
        // Continuation with real resolution, including further fissions.
        var a = Busy(); var resolver = new TurnResolver(); Advance(a, resolver);
        var b = SaveCodec.Load(SaveCodec.Save(a));
        for (int t = 0; t < 4; t++) { Advance(a, resolver); Advance(b, new TurnResolver()); Check(Key(a) == Key(b), "continued run equals the uninterrupted run after turn " + t); }
        Check(a.PlayerCount > 6 && b.PlayerCount == a.PlayerCount, "the continuation founded settlements in new player slots");
        // Older saves: no lineage, policy, destination, player count or config fields.
        var legacy = Busy(); Check(legacy.PlayerCount == 6, "the legacy fixture starts with six player slots");
        var camp = new DataContractSerializer(typeof(Camp)); string xml;
        using (var stream = new MemoryStream()) { camp.WriteObject(stream, legacy.Camps[0]); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<FissionPolicy>"), "a camp serializes its policy");
        string old = Regex.Replace(Regex.Replace(xml, "<FissionPolicy>[^<]*</FissionPolicy>", ""), "<FissionDestination[^>]*(/>|>.*?</FissionDestination>)", "", RegexOptions.Singleline);
        Check(old != xml && !old.Contains("Fission"), "legacy camp fixture omits the fields");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(old))) { var c = (Camp)camp.ReadObject(stream); Check(c.FissionPolicy == FissionPolicy.Neutral && c.FissionDestination == null, "an older camp loads Neutral with no destination"); }
        var society = new DataContractSerializer(typeof(Society));
        using (var stream = new MemoryStream()) { society.WriteObject(stream, ls); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        string oldSociety = Regex.Replace(xml, "<(ParentSociety|KinAffinity)>[^<]*</\\1>", "");
        Check(oldSociety != xml && !oldSociety.Contains("ParentSociety"), "legacy society fixture omits lineage");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(oldSociety))) { var s = (Society)society.ReadObject(stream); Check(s.ParentSociety == 0 && s.KinAffinity == 0, "an older Society has no lineage"); }
        // A whole older world: strip the optional world field and the fission config, reload, and run: the slot count stays fixed.
        var world = Busy(); world.PlayerCount = 0; world.Config.FissionEnabled = false; var legacyLoaded = SaveCodec.Load(SaveCodec.Save(world));
        Check(legacyLoaded.PlayerCount == 0 && legacyLoaded.LivePlayers == 6 && !legacyLoaded.Config.FissionEnabled, "an older world keeps its fixed player count and runs without fission");
        for (int t = 0; t < 3; t++) Advance(legacyLoaded, new TurnResolver());
        Check(legacyLoaded.Camps.Count == 6 && legacyLoaded.LivePlayers == 6 && legacyLoaded.Societies.All(s => s.ParentSociety == 0), "a world without the fission fields never founds a settlement");
    }

    // Six Neolithic camps under constant maximal pressure so fission, dropped conflicts and chains occur over several turns.
    private static WorldState Busy(bool crowded = true)
    {
        var w = crowded ? World(true, new[] { 3, 3 }, new[] { 9, 3 }, new[] { 15, 3 }, new[] { 3, 10 }, new[] { 9, 10 }, new[] { 15, 10 }) : World(48, 32, true, new[] { 6, 6 }, new[] { 30, 6 }, new[] { 18, 22 });
        Pressure(w, 0); w.Config.AnnualHumanGrowthRate = 0; w.Turn = 1; w.Days = 0; foreach (var t in w.Tiles) t.LastEcologyUpdateDays = 0;
        foreach (var c in w.Camps) { Explore(w, c, crowded ? 5 : 9); c.Population = 160; c.PlantFood = 40000; }
        return w;
    }
    private static void Advance(WorldState w, TurnResolver resolver) => resolver.Resolve(w, new Dictionary<int, List<Order>>());

    private static void Replay()
    {
        var worlds = new[] { Busy(), Busy() };
        worlds[1].Camps.Reverse(); worlds[1].Societies.Reverse();
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 2, true, 1) };
        int founded = 0;
        for (int t = 0; t < 6; t++)
        {
            for (int i = 0; i < 2; i++) Advance(worlds[i], new TurnResolver(workers[i]));
            Check(Key(worlds[0]) == Key(worlds[1]), "scalar and reversed 4-worker replay with reordered collections agree after turn " + t);
            Check(worlds[0].History.Last().Fissions.Select(f => f.ParentCamp + ">" + f.DaughterCamp + "@" + f.Tile).SequenceEqual(worlds[1].History.Last().Fissions.Select(f => f.ParentCamp + ">" + f.DaughterCamp + "@" + f.Tile)), "the canonical fission record is identical after turn " + t);
            Check(worlds[0].History.Last().Fissions.Select(f => f.ParentCamp).SequenceEqual(worlds[0].History.Last().Fissions.Select(f => f.ParentCamp).OrderBy(x => x)), "fissions are recorded in parent camp id order");
            founded += worlds[0].History.Last().Fissions.Count;
            TurnResolver.Invariants(worlds[0]);
        }
        Check(founded >= 6 && worlds[0].PlayerCount == 6 + founded && worlds[0].Camps.Count == 6 + founded, "the replay exercises repeated fission (" + founded + " settlements)");
        // No same-turn multi-hop: no daughter founded in a turn is the parent of one founded in that turn.
        var a = Busy(false); var created = new HashSet<int>(a.Camps.Select(c => c.Id));
        for (int t = 0; t < 9; t++)
        {
            Advance(a, new TurnResolver()); var fissions = a.History.Last().Fissions;
            Check(fissions.All(f => created.Contains(f.ParentCamp)), "every parent of turn " + t + " existed before it");
            foreach (var f in fissions) created.Add(f.DaughterCamp);
            Check(fissions.All(f => a.Camps.Single(c => c.Id == f.DaughterCamp).Moves == 0), "a fresh daughter has not acted");
        }
        Check(a.Societies.Any(s => s.ParentSociety > 0 && a.Societies.Any(p => p.Id == s.ParentSociety && p.ParentSociety > 0)), "multi-hop lineage appears across turns");
        // Daughters are smaller than their parents at every fission, and the people ledger is conserved over the run.
        var r = Busy(); int people = r.Camps.Sum(c => c.Population);
        for (int t = 0; t < 5; t++)
        {
            Advance(r, new TurnResolver());
            Check(r.History.Last().Fissions.All(f => f.Daughter < f.ParentBefore - f.Daughter && f.Daughter >= r.Config.FissionMinimumDaughter), "daughters are always smaller than the remaining parent");
            Check(r.Camps.Sum(r.LinkedPopulation) == people, "the people ledger is conserved over turn " + t);
        }
        // Real Paleolithic worlds: a generated opening founds settlements only from vision, stays deterministic and keeps invariants.
        var cfg = SimulationConfig.Small(6); cfg.FissionEnabled = true; cfg.FissionGainThreshold = cfg.FissionGainDiscourage = cfg.FissionGainEncourage = 0;
        var g1 = new WorldGenerator().Generate(3, cfg); var g2 = new WorldGenerator().Generate(3, cfg);
        g2.Camps.Reverse(); g2.Societies.Reverse();
        var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder();
        for (int t = 0; t < 6 && !g1.Finished; t++)
        {
            var orders1 = g1.Camps.GroupBy(c => c.Player).ToDictionary(g => g.Key, g => policy.Decide(builder.Build(g1, g.Key), g.First().Mandate).Orders);
            var orders2 = g2.Camps.GroupBy(c => c.Player).ToDictionary(g => g.Key, g => policy.Decide(builder.Build(g2, g.Key), g.First().Mandate).Orders);
            new TurnResolver(workers[0]).Resolve(g1, orders1); new TurnResolver(workers[1]).Resolve(g2, orders2);
            Check(Key(g1) == Key(g2), "a generated world replays identically across workers and order after turn " + t);
        }
    }
}
