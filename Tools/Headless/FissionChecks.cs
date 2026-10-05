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
        var cfg = SimulationConfig.Small(1); cfg.FissionEnabled = true; cfg.FissionHousingRelief = 1; /* mechanism fixtures exercise the full relief; the shipped default is 0 */ cfg.FissionDaughterMargin = 1; cfg.FissionRadius = 3; /* fixtures are written for the distance-3 ring; the shipped default is 4 */ cfg.Width = width; cfg.Height = height; cfg.AnnualHumanGrowthRate = 0; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0;
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
        checks = 0; Config(); Levers(); Crowding(); DaughterHousing(); Calibration(); Ordering(); Opening(); Destination(); Knowledge(); Commit(); Conservation(); Viability(); UnbuiltCrops(); Relative(); Funnel(); Spacing(); Bounded(); WiderRadius(); Policy(); Distress(); Observations(); Persistence(); Replay();
        Console.WriteLine("PASS: " + checks + " fission assertions"); return checks;
    }

    // B1: the daughter's own shelter at the end of her first turn (Fission.DaughterShelter): her proportional share of carried tents
    // and materials, plus what her Builders can assemble from them in one turn. Closed forms at 100 residents, Neutral policy (30 leave).
    private static void DaughterHousing()
    {
        const double f = .3; const int nd = 30;
        WorldState Fixture(double builders, double hand, double material, double wood = 0, double fabric = 0, double tents = 0, bool construction = false, bool husbandryOnly = false)
        {
            var w = World(true, new[] { 8, 8 }); var c = w.Camps[0]; c.ClothPerCapita = 1; c.Population = 100; w.Config.FissionDaughterBuilderShare = builders;
            c.Inventory.HandTools = hand; c.Inventory.HousingMaterial = material; c.Inventory.Wood = wood; c.Inventory.AnimalFabric = fabric; c.Inventory.LightHousingUnits = tents;
            if (construction) c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
            if (husbandryOnly) { c.Technologies[(int)Technology.Cultivation] = false; c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true; }
            return w;
        }
        double Intervals(WorldState w) => w.Config.GenerationIntervals;
        var n0 = new SimulationConfig().Neolithic;
        double unit = n0.BuilderHousingOutputPerBaseDays, ppu = n0.HousingPeoplePerUnit, tent = n0.LightHousingPeoplePerUnit;
        // Portable share: f of the tents, nothing built with no Builders; the parent's fixed Housing never travels.
        var a = Fixture(0, 0, 0, tents: 10); a.FixedHousing.Add(new FixedHousingStock { CampId = a.Camps[0].Id, Tile = a.Camps[0].Tile, Units = 25, LastUpdatedDays = a.Days });
        var sa = Fission.DaughterShelter(a, a.Camps[0], nd);
        Near(sa.Light, 10 * f * tent, "the daughter carries her proportional share of the portable Light Housing"); Near(sa.Fixed, 0, "the daughter carries none of the parent's fixed Housing");
        // Her tents are all she gets with no Builders, and with Builders but no tools or materials.
        var b = Fixture(.1, 0, 0, tents: 10); var sb = Fission.DaughterShelter(b, b.Camps[0], nd);
        Near(sb.Light, 10 * f * tent, "Builders without tools or materials add nothing"); Near(sb.Fixed, 0, "no tools, no fixed Housing");
        var b2 = Fixture(0, 1000, 1000, tents: 10); Near(Fission.DaughterShelter(b2, b2.Camps[0], nd).Total, 10 * f * tent, "materials and tools without Builders add nothing");
        // Labour-limited: generalist Builders (rate x GeneralistTaskEfficiency), step rate 2x, hand-tool multiplier 1, stock covers the material step.
        var l = Fixture(.05, 1000, 1000); double lab = nd * .05 * Intervals(l), step = 2 * unit * n0.GeneralistTaskEfficiency;
        Near(Fission.DaughterShelter(l, l.Camps[0], nd).Fixed, lab * step * ppu, "labour-limited: Builders' output over one turn at the generalist rate", 1e-6);
        // Specialists (Construction) at 2x...: the need caps the build (30 people = 7.5 units).
        var sp = Fixture(.1, 1000, 1000, construction: true);
        Near(Fission.DaughterShelter(sp, sp.Camps[0], nd).Fixed, nd, "a specialist crew shelters the whole daughter when labour and materials suffice", 1e-6);
        var sp1 = Fixture(.05, 1000, 1000, construction: true); double splab = nd * .05 * Intervals(sp1);
        Near(Fission.DaughterShelter(sp1, sp1.Camps[0], nd).Fixed, splab * 2 * unit * ppu, "specialists build at the full Builder rate", 1e-6);
        // Material-limited: carried Housing Material, then carried Wood/Clay at one unit per Housing; labour includes the material step.
        var m1 = Fixture(.1, 1000, 10, construction: true); Near(Fission.DaughterShelter(m1, m1.Camps[0], nd).Fixed, 10 * f * ppu, "material-limited: carried Housing Material only", 1e-6);
        var m2 = Fixture(.1, 1000, 10, wood: 10, construction: true); Near(Fission.DaughterShelter(m2, m2.Camps[0], nd).Fixed, (10 + 10) * f * ppu, "carried Wood adds one Housing per unit", 1e-6);
        var m3 = Fixture(.02, 1000, 0, wood: 1000, construction: true); double mlab = nd * .02 * Intervals(m3);
        Near(Fission.DaughterShelter(m3, m3.Camps[0], nd).Fixed, mlab * unit * ppu, "making the Housing Material first halves the labour-limited output", 1e-6);
        // Tool-limited: Hand Tool outputs bound the assembly.
        var t1 = Fixture(.1, 2, 1000, construction: true); Near(Fission.DaughterShelter(t1, t1.Camps[0], nd).Fixed, 2 * f * ppu, "tool-limited: one Hand Tool output per Housing", 1e-6);
        // Light Housing from carried Animal Fabric needs Husbandry; tents are sewn after fixed Housing and only to the remaining need.
        var h = Fixture(.05, 1000, 0, fabric: 100, husbandryOnly: true); double hl = nd * .05 * Intervals(h);
        Near(Fission.DaughterShelter(h, h.Camps[0], nd).Light, hl * unit * n0.GeneralistTaskEfficiency * tent, "husbandry daughter sews Light Housing at the Builder rate", 1e-6); Near(Fission.DaughterShelter(h, h.Camps[0], nd).Fixed, 0, "no Cultivation, no fixed Housing");
        var h2 = Fixture(.1, 1000, 0, fabric: 2, husbandryOnly: true, construction: true); Near(Fission.DaughterShelter(h2, h2.Camps[0], nd).Light, 2 * f * tent, "Light Housing is bounded by carried Animal Fabric", 1e-9);
        var fishing = Fixture(.1, 1000, 1000, construction: true); var fishingCamp = fishing.Camps[0];
        fishing.Config.NeolithicFishingEnabled = true;
        fishingCamp.Technologies[(int)Technology.Cultivation] = false;
        fishingCamp.Technologies[(int)Technology.Watercraft] = true;
        fishingCamp.Technology = Technology.Watercraft;
        Near(Fission.DaughterShelter(fishing, fishingCamp, nd).Fixed, nd,
            "a Fishing-only daughter can assemble the fixed Housing recipe", 1e-6);
        var h3 = Fixture(.1, 1000, 1000, tents: 100, fabric: 100, construction: true); Near(Fission.DaughterShelter(h3, h3.Camps[0], nd).Total, 100 * f * tent, "a daughter already sheltered by carried tents builds nothing more");
        // The estimate uses it: H and housing coverage of the daughter come from carried plus built shelter.
        var e = Fixture(.1, 1000, 1000, construction: true); var ec = e.Camps[0]; e.FixedHousing.Add(new FixedHousingStock { CampId = ec.Id, Tile = ec.Tile, Units = 25, LastUpdatedDays = e.Days });
        var ee = Fission.EstimateSplit(e, ec, FissionPolicy.Neutral); var bare = Fixture(0, 0, 0); bare.FixedHousing.Add(new FixedHousingStock { CampId = bare.Camps[0].Id, Tile = bare.Camps[0].Tile, Units = 25, LastUpdatedDays = bare.Days });
        var be = Fission.EstimateSplit(bare, bare.Camps[0], FissionPolicy.Neutral);
        double floor = n0.RudimentaryExpectationFloor, moved = floor + (1 - floor) * (e.Config.TurnDays / e.Config.DaysPerYear / n0.RudimentaryExpectationRampYears);
        Near(ee.DaughterQoL, Cohesion(e, 1, 30), "a fully sheltered daughter has coverage 1 and H = 1", 1e-9);
        Near(be.DaughterQoL, (.5 + .5 * (1 - moved)) * Cohesion(bare, 0, 30), "a daughter with no supplies is the unhoused estimate");
        Check(!be.DaughterAcceptable(.05) && ee.DaughterAcceptable(.05) && ee.DaughterQoL > be.DaughterQoL, "the check binds the unsupplied split and passes the well supplied one");
        // In the pass: the same counters as before, fewer blocks for the supplied camp.
        foreach (var w in new[] { e, bare }) { w.Config.FissionDaughterMargin = .05; Explore(w, w.Camps[0]); Ratio(w, w.Camps[0], 1.5); }
        Pass(e, out var pe); Pass(bare, out var pb);
        Check(pe.FissionDaughterChecks == 1 && pe.FissionDaughterBlocked == 0 && pe.FissionCommits == 1 && pb.FissionDaughterChecks == 1 && pb.FissionDaughterBlocked == 1, "a well-supplied camp splits where an unsupplied one is blocked");
        // Pure: the estimate changes no world state.
        var before = ec.Inventory.HandTools + ec.Inventory.HousingMaterial + ec.Population; Fission.DaughterShelter(e, ec, nd);
        Check(before == ec.Inventory.HandTools + ec.Inventory.HousingMaterial + ec.Population, "the shelter estimate is pure");
    }

    private static void Config()
    {
        var c = new SimulationConfig();
        Check(c.FissionEnabled && c.FissionGainThreshold == .113 && c.FissionGainDiscourage == .080 && c.FissionGainEncourage == .0955 && c.FissionSearchFraction == .8 && c.FissionDaughterMargin == .05 && c.FissionHousingRelief == 0 && c.FissionDaughterBuilderShare == .05 && c.FissionRadius == 4 && c.FissionMinimumDaughter == 12, "fission is on by default (plan of record) and the levers match the calibration");
        foreach (Action<SimulationConfig> bad in new Action<SimulationConfig>[] { x => x.FissionGainThreshold = -.1, x => x.FissionGainDiscourage = 6, x => x.FissionGainEncourage = double.NaN, x => x.FissionRadius = 0, x => x.FissionRadius = 6, x => x.FissionMinimumDaughter = 0, x => x.FissionSearchFraction = 1.5, x => x.FissionSearchFraction = -.1, x => x.FissionDaughterMargin = -.01, x => x.FissionDaughterMargin = 2, x => x.FissionHousingRelief = -.1, x => x.FissionHousingRelief = 1.5, x => x.FissionDaughterBuilderShare = -.1, x => x.FissionDaughterBuilderShare = 1.5 })
        {
            var x = new SimulationConfig(); bad(x); bool rejected = false; try { x.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "an invalid fission lever is rejected");
        }
        var serializer = new DataContractSerializer(typeof(SimulationConfig)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { FissionRadius = 4, FissionEnabled = true, FissionDaughterBuilderShare = .2 }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<FissionRadius>4</FissionRadius>") && xml.Contains("<FissionEnabled>true</FissionEnabled>"), "fission levers are serialized");
        string legacy = Regex.Replace(xml, "<Fission[A-Za-z]*>[^<]*</Fission[A-Za-z]*>", "");
        Check(legacy != xml && !legacy.Contains("<Fission"), "legacy fixture omits every fission field");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy)))
        {
            var old = (SimulationConfig)serializer.ReadObject(stream);
            Check(!old.FissionEnabled && old.FissionGainThreshold == .113 && old.FissionGainDiscourage == .080 && old.FissionGainEncourage == .0955 && old.FissionSearchFraction == .8 && old.FissionDaughterMargin == .05 && old.FissionHousingRelief == 0 && old.FissionDaughterBuilderShare == .05 && old.FissionRadius == 4 && old.FissionMinimumDaughter == 12, "older saves load with fission disabled and default levers");
        }
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) { var back = (SimulationConfig)serializer.ReadObject(stream); Check(back.FissionRadius == 4 && back.FissionDaughterBuilderShare == .2, "saved levers round trip"); }
        string json = File.ReadAllText(FindConfig());
        foreach (var pair in new[] { ("FissionEnabled", "true"), ("FissionGainThreshold", "0.113"), ("FissionGainDiscourage", "0.08"), ("FissionGainEncourage", "0.0955"), ("FissionSearchFraction", "0.8"), ("FissionDaughterMargin", "0.05"), ("FissionHousingRelief", "0"), ("FissionDaughterBuilderShare", "0.05"), ("FissionRadius", "4"), ("FissionMinimumDaughter", "12") })
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
    // The camp's housing structure for the cohesion fixtures: none (H = 0), tents that shelter exactly everyone (H = p_tent) or
    // permanent housing that shelters exactly everyone (H = 1). Derived cohesion depends on the structure, never on a technology label.
    private enum Shelter { None, Tents, Fixed }
    private static double ShareOf(WorldState w, Shelter s) => s == Shelter.None ? 0 : s == Shelter.Tents ? w.Config.Neolithic.LightHousingPermanence : 1;
    private static Camp Covered(WorldState w, Camp c, int residents, Shelter shelter)
    {
        var n = w.Config.Neolithic;
        c.Population = residents; c.ClothPerCapita = 1; c.Inventory.LightHousingUnits = 0; w.FixedHousing.RemoveAll(x => x.CampId == c.Id);
        if (shelter == Shelter.Tents) c.Inventory.LightHousingUnits = residents / n.LightHousingPeoplePerUnit;
        if (shelter == Shelter.Fixed) w.FixedHousing.Add(new FixedHousingStock { CampId = c.Id, Tile = c.Tile, Units = residents / n.HousingPeoplePerUnit, LastUpdatedDays = w.Days });
        return c;
    }
    // S at an explicit housing share, written out: exp(-(N / (N_band (1 + k H)))^beta).
    private static double Cohesion(WorldState w, double share, double population) { var n = w.Config.Neolithic; return Math.Exp(-Math.Pow(population / (n.CohesionBandScale * (1 + n.CohesionHousingGain * share)), n.SocialCohesionExponent)); }
    private static double Cohesion(WorldState w, Camp c, double population)
    {
        var n = w.Config.Neolithic; double cap = NeolithicProduction.FixedHousingAt(w, c.Id, c.Tile, w.Days) * n.HousingPeoplePerUnit + n.LightHousingPermanence * c.Inventory.LightHousingUnits * n.LightHousingPeoplePerUnit;
        return Cohesion(w, Math.Min(1, cap / population), population);
    }
    private static void Crowding()
    {
        // Closed form with full goods coverage: G = (F_hat W_hat / F W) S(N - N_d) / S(N) - 1, with S ratio exp((N^2 - (N - N_d)^2) / N_s^2) (beta = 2); the relief factor is 1 for F = W = 1.
        foreach (var shelter in new[] { Shelter.None, Shelter.Tents, Shelter.Fixed })
            foreach (FissionPolicy policy in Enum.GetValues(typeof(FissionPolicy)))
            {
                var w = World(shelter == Shelter.Fixed, new[] { 8, 8 }); var c = Covered(w, w.Camps[0], 300, shelter);
                c.FissionPolicy = policy; c.FoodNeedsMetRatio = .63; c.WaterNeedsMetRatio = .9;
                var nc = w.Config.Neolithic; double ns = nc.CohesionBandScale * (1 + nc.CohesionHousingGain * ShareOf(w, shelter));
                int nd = Kinship.DaughterSize(300, policy), stay = 300 - nd;
                var e = Fission.EstimateSplit(w, c, policy);
                double e0 = nc.RudimentaryExpectationFloor + (1 - nc.RudimentaryExpectationFloor) * Math.Min(1, c.StationaryYears / nc.RudimentaryExpectationRampYears);
                double coverage = .5 + .5 * (1 - e0 * (1 - (shelter == Shelter.None ? 0 : 1)));
                double relief = Math.Min(1, .63 * 300 / stay) * Math.Min(1, .9 * 300 / stay) / (.63 * .9);   // stayers' food and water relief (distress fission)
                Near(e.Gain, relief * Math.Exp((300.0 * 300 - (double)stay * stay) / (ns * ns)) - 1, shelter + " " + policy + " gain is the closed form of the cohesion ratio at the present housing share times the stayers' food and water relief");
                Near(e.QNow, .63 * .9 * coverage * Math.Exp(-Math.Pow(300 / ns, 2)), shelter + " " + policy + " Q_now = F W R S(N)");
                Check(e.Daughter == nd && e.QAfter > e.QNow, "the estimate carries the daughter size and a higher stayers' QoL");
            }
        // F, W and the goods coverage cancel in the ratio while housing is unaffected: a Paleolithic camp with no goods at all has the same gain.
        double bandScale = new SimulationConfig().Neolithic.CohesionBandScale;   // no housing: N_s = N_band
        var bare = World(false, new[] { 8, 8 }); bare.Camps[0].Population = 300; bare.Camps[0].ClothPerCapita = 0; bare.Camps[0].HousingPerCapita = 0; bare.Camps[0].FoodNeedsMetRatio = .4;
        var bareQ = Fission.EstimateSplit(bare, bare.Camps[0], FissionPolicy.Neutral);
        Near(bareQ.Gain, Math.Min(1, .4 * 300 / 210.0) / .4 * Math.Exp((300.0 * 300 - 210.0 * 210) / (bandScale * bandScale)) - 1, "with no goods and partial food the Paleolithic gain is the cohesion gain times the stayers' food relief F_hat / F");
        Near(bareQ.QNow, .4 * (.5 * (1 - .25) + .5 * (1 - .25)) * Math.Exp(-Math.Pow(300 / bandScale, 2)), "Q_now uses the expectation floor for an unresolved camp");
        // Q_now = 0 (no food): the documented fallback is the cohesion gain, so a destitute crowded camp still feels its crowding.
        var zero = World(false, new[] { 8, 8 }); zero.Camps[0].Population = 300; zero.Camps[0].FoodNeedsMetRatio = 0;
        var ze = Fission.EstimateSplit(zero, zero.Camps[0], FissionPolicy.Neutral);
        Check(ze.QNow == 0 && ze.QAfter == 0, "no food gives zero QoL before and after");
        Near(ze.Gain, Math.Exp((300.0 * 300 - 210.0 * 210) / (bandScale * bandScale)) - 1, "the zero-QoL fallback is the cohesion gain S_after / S_now - 1");
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
        var nd5 = new SimulationConfig().Neolithic; double ns5 = nd5.CohesionBandScale * (1 + nd5.CohesionHousingGain * .5);
        Check(fe.Gain > Math.Exp((100.0 * 100 - 70.0 * 70) / (ns5 * ns5)) - 1, "housing relief adds to the cohesion gain at the present housing share");
        // The relief weight interpolates between the cohesion-and-clothing gain (0) and the full relief (1).
        double QAt(double weight) { fixedWorld.Config.FissionHousingRelief = weight; return Fission.EstimateSplit(fixedWorld, fc, FissionPolicy.Neutral).QAfter; }
        double full = QAt(1), none = QAt(0), half = QAt(.5);
        Near(none, Cov(.5) * Cohesion(fixedWorld, .5, 70), "relief weight 0: housing per head and the cohesion housing share stay at their present values, only the headcount changes");
        Near(half, Cov(.5 + .5 * (50.0 / 70 - .5)) * Cohesion(fixedWorld, .5 + .5 * (50.0 / 70 - .5), 70), "relief weight 0.5 counts half the housing gain in coverage and in the cohesion share"); Near(full, fe.QAfter, "relief weight 1 is the specified rule"); QAt(1);
        Near(Fission.EstimateSplit(fixedWorld, fc, FissionPolicy.Neutral).Gain, fe.Gain, "restoring the weight restores the gain");
        QAt(0); Near(Fission.EstimateSplit(fixedWorld, fc, FissionPolicy.Neutral).Gain, Math.Exp((100.0 * 100 - 70.0 * 70) / (ns5 * ns5)) - 1, "with relief weight 0 a housing shortage leaves the gain at the closed-form cohesion gain at constant H"); QAt(1);
        var mixed = World(true, new[] { 8, 8 }); var mc = mixed.Camps[0]; mc.ClothPerCapita = 1; mc.Population = 100; mc.Inventory.LightHousingUnits = 5;
        mixed.FixedHousing.Add(new FixedHousingStock { CampId = mc.Id, Tile = mc.Tile, Units = 5, LastUpdatedDays = mixed.Days });
        var me = Fission.EstimateSplit(mixed, mc, FissionPolicy.Neutral);
        double tp = mixed.Config.Neolithic.LightHousingPeoplePerUnit, tentP = 5 * tp, hMixed = (20 + .21 * tentP) / 100;
        Near(me.QNow, Cov((20 + tentP) / 100) * Cohesion(mixed, hMixed, 100), "mixed housing: 5 houses and 5 tents shelter 20 + 5 t of 100 people, H counts the tents at p_tent");
        Near(me.QAfter, Cov((20 + tentP * .7) / 70) * Cohesion(mixed, (20 + .21 * tentP * .7) / 70, 70), "mixed housing: all fixed plus 70 percent of the portable Light Housing for 70 stayers");
        var portable = World(true, new[] { 8, 8 }); var pc = portable.Camps[0]; pc.ClothPerCapita = 1; pc.Population = 100; pc.Inventory.LightHousingUnits = 10;
        var pe = Fission.EstimateSplit(portable, pc, FissionPolicy.Neutral);
        Near(pe.QAfter / pe.QNow, Cohesion(portable, .21 * 10 * portable.Config.Neolithic.LightHousingPeoplePerUnit / 100, 70) / Cohesion(portable, .21 * 10 * portable.Config.Neolithic.LightHousingPeoplePerUnit / 100, 100), "portable Light Housing splits in proportion and brings no relief: H is unchanged, only the headcount");
        // Daughter check: F = W = 1 at the site, clothing unchanged, housing from its portable share only, expectation after a move.
        var dw = World(true, new[] { 8, 8 }); var dc = dw.Camps[0]; dc.ClothPerCapita = 1; dc.Population = 100; dw.FixedHousing.Add(new FixedHousingStock { CampId = dc.Id, Tile = dc.Tile, Units = 25, LastUpdatedDays = dw.Days });
        var de = Fission.EstimateSplit(dw, dc, FissionPolicy.Neutral);
        double moved = floor + (1 - floor) * (dw.Config.TurnDays / dw.Config.DaysPerYear / dw.Config.Neolithic.RudimentaryExpectationRampYears);
        Near(de.QNow, Cohesion(dw, dc, 100), "a fully housed parent has Q = S(N)");
        Near(de.DaughterQoL, (.5 + .5 * (1 - moved)) * Cohesion(dw, 0, 30), "the daughter has no housing (H = 0, N_s = N_band), a fresh expectation and F = W = 1");
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
        // The WP10a failure: a camp with no goods at default thresholds did not split at any size. With derived cohesion the onset depends on
        // housing structure alone: unhoused camps split above the N_band onset, tents (exactly covering everyone) above the tent onset and permanent
        // housing above the largest. Sizes are fractions of each closed-form onset at the shipped scale. A Paleolithic camp remembers no
        // geography, so it has nowhere it knows to found a daughter and never splits, whatever its size.
        var shippedConfig = new SimulationConfig(); var sn = shippedConfig.Neolithic;
        foreach (var shelter in new[] { Shelter.None, Shelter.Tents, Shelter.Fixed })
        {
            double share = shelter == Shelter.None ? 0 : shelter == Shelter.Tents ? sn.LightHousingPermanence : 1;
            double onset = AnalyticOnset(sn.CohesionBandScale * (1 + sn.CohesionHousingGain * share), FissionPolicy.Neutral, shippedConfig);
            foreach (var (neo, ofOnset, expected) in new[] { (true, .6, false), (true, .8, false), (true, 1.3, true), (true, 2.0, true), (false, .8, false), (false, 1.3, false), (false, 2.0, false) })
            {
                int size = (int)Math.Round(onset * ofOnset);
                var w = World(neo, new[] { 8, 8 }); var c = w.Camps[0]; Explore(w, c); Covered(w, c, size, shelter); c.PlantFood = 500000;
                w.Config.FissionGainThreshold = shippedConfig.FissionGainThreshold; w.Config.FissionGainDiscourage = shippedConfig.FissionGainDiscourage; w.Config.FissionGainEncourage = shippedConfig.FissionGainEncourage;
                Check((Pass(w, out _) != null) == expected, (neo ? "Neolithic " : "Paleolithic ") + shelter + " camp of " + size + " (" + ofOnset + " of the onset " + onset.ToString("F0") + ") at default thresholds " + (expected ? "splits" : "stays whole"));
            }
        }
    }

    // Onset population per regime and policy under full goods coverage, for the given (default: shipped) thresholds.
    private static int Onset(Shelter shelter, FissionPolicy policy, SimulationConfig config = null)
    {
        var w = World(shelter == Shelter.Fixed, new[] { 8, 8 }); var c = w.Camps[0];
        var d = config ?? new SimulationConfig(); w.Config.FissionGainThreshold = d.FissionGainThreshold; w.Config.FissionGainDiscourage = d.FissionGainDiscourage; w.Config.FissionGainEncourage = d.FissionGainEncourage;
        c.FissionPolicy = policy; double gstar = Kinship.GainThreshold(w.Config, policy);
        for (int n = 1; n <= 6000; n++)
        {
            Covered(w, c, n, shelter); int nd = Kinship.DaughterSize(n, policy);
            if (nd < w.Config.FissionMinimumDaughter || nd >= n - nd || n - nd < w.Config.MinimumCamp) continue;
            if (Fission.EstimateSplit(w, c, policy).Gain >= gstar) return n;
        }
        return -1;
    }
    // Closed-form onset under full goods coverage and constant housing share: (N / N_s)^2 (1 - (1 - d)^2) = ln(1 + G*), d the daughter share.
    private static double AnalyticOnset(double scale, FissionPolicy policy, SimulationConfig config)
    {
        double d = Kinship.DaughterPercent(policy) / 100.0;
        return scale * Math.Sqrt(Math.Log(1 + Kinship.GainThreshold(config, policy)) / (1 - (1 - d) * (1 - d)));
    }
    private static void Calibration()
    {
        // Onsets N* follow from the derived scale N_s = N_band (1 + k H) at H = 0 (no housing), p_tent (all tents) and 1 (all permanent housing).
        // The analytic fit to the design targets (QoLFissionTradeDesign 3.5-3.6: Paleolithic 101 (116 / 83), Nomadic 252 (291 / 208), Agricultural
        // 607-999 (770-1105 / 398-887)) is N_band = 220, k = 7.1, p_tent = 0.21; the shipped values may differ after the calibration sweep.
        var shipped = new SimulationConfig(); var n = shipped.Neolithic;
        foreach (var shelter in new[] { Shelter.None, Shelter.Tents, Shelter.Fixed })
            foreach (FissionPolicy policy in Enum.GetValues(typeof(FissionPolicy)))
            {
                double share = shelter == Shelter.None ? 0 : shelter == Shelter.Tents ? n.LightHousingPermanence : 1;
                double expected = AnalyticOnset(n.CohesionBandScale * (1 + n.CohesionHousingGain * share), policy, shipped);
                int onset = Onset(shelter, policy);
                Check(onset > 0 && Math.Abs(onset - expected) <= Math.Max(2, .035 * expected), shelter + " " + policy + " onset " + onset + " matches the closed form " + expected.ToString("F1") + " for the configured scale");
            }
        // The analytic fit itself reproduces the design targets.
        var fit = new SimulationConfig(); fit.Neolithic.CohesionBandScale = 220; fit.Neolithic.CohesionHousingGain = 7.1; fit.Neolithic.LightHousingPermanence = .21;
        var design = new Dictionary<(Shelter, FissionPolicy), int>
        {
            [(Shelter.None, FissionPolicy.Neutral)] = 101, [(Shelter.None, FissionPolicy.Discourage)] = 116, [(Shelter.None, FissionPolicy.Encourage)] = 83,
            [(Shelter.Tents, FissionPolicy.Neutral)] = 252, [(Shelter.Tents, FissionPolicy.Discourage)] = 291, [(Shelter.Tents, FissionPolicy.Encourage)] = 208
        };
        foreach (var pair in design)
        {
            double share = pair.Key.Item1 == Shelter.None ? 0 : .21;
            double onset = AnalyticOnset(220 * (1 + 7.1 * share), pair.Key.Item2, fit);
            Check(Math.Abs(onset - pair.Value) <= Math.Max(2, .03 * pair.Value), pair.Key + " analytic fit onset " + onset.ToString("F1") + " within 3 percent of the design " + pair.Value);
        }
        var bands = new Dictionary<FissionPolicy, (int, int)> { [FissionPolicy.Neutral] = (607, 999), [FissionPolicy.Discourage] = (770, 1105), [FissionPolicy.Encourage] = (398, 887) };
        foreach (var pair in bands)
        {
            double onset = AnalyticOnset(220 * (1 + 7.1), pair.Key, fit);
            Check(onset >= pair.Value.Item1 && onset <= pair.Value.Item2, "fully housed " + pair.Key + " fit onset " + onset.ToString("F1") + " lies inside the design band " + pair.Value);
        }
        foreach (var shelter in new[] { Shelter.None, Shelter.Tents, Shelter.Fixed })
            Check(Onset(shelter, FissionPolicy.Discourage) > Onset(shelter, FissionPolicy.Neutral) && Onset(shelter, FissionPolicy.Neutral) > Onset(shelter, FissionPolicy.Encourage), shelter + ": Discourage delays and Encourage advances the onset");
        Check(Onset(Shelter.None, FissionPolicy.Neutral) < Onset(Shelter.Tents, FissionPolicy.Neutral) && Onset(Shelter.Tents, FissionPolicy.Neutral) < Onset(Shelter.Fixed, FissionPolicy.Neutral), "onset rises with housing share: none < tents < permanent");
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
        ps.Experience[0] = 41; ps.NeolithicExperience[1] = 17; ps.Progression.EraStartDays = 123; ps.Progression.MajorityEligibilityDays = 7777; ps.Progression.ForcedTransitionDays = 8888; ps.ForcedTransition = true; ps.SecondaryDomain = 2; ps.TransitionTurn = 4;
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
        Check(ds.NeolithicExperience.SequenceEqual(ps.NeolithicExperience) && ds.NeolithicExperience[1] == 17 && !ReferenceEquals(ds.NeolithicExperience, ps.NeolithicExperience), "a daughter copies the parent's Neolithic practice (its own array)");
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
        Near(daughter.QoL, report.DaughterQoL, "a fresh daughter starts at the QoL estimate that justified the split, not the unset 1.0"); Check(daughter.QoL < 1, "the founding QoL estimate of the fixture is below one");
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
        // Relative destination test: the poor site is habitable (ratio > 0) so it is cached, but a camp with nothing to carry is better off at home, so the veto blocks it.
        poor.Camps[0].PlantFood = 0; poor.Config.FissionDaughterMargin = .05;
        Check(Pass(poor, out var pm) == null && poor.Camps.Count == 1 && pm.FissionDaughterBlocked == 1 && pm.FissionCommits == 0, "a habitable site that cannot feed the daughter is vetoed when the parent is better off at home");
        var barren = Pressured(); foreach (var t in barren.Tiles) { t.Food = 0; t.PotentialFood = 0; for (int s = 0; s < 4; s++) { barren.Tiles[t.Id].Seasons[s].MaxFood = 0; barren.Tiles[t.Id].Seasons[s].MaxPotentialFood = 0; } }
        Check(Pass(barren, out var bm) == null && barren.Camps[0].FissionDestination == null && barren.Camps.Count == 1 && bm.FissionSearchesNoViable == 1, "a site with no food at all (support ratio 0) is not a destination");
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
        greedy.Config.FissionDaughterMargin = .05; var gr = Pass(greedy, out var gm); Check(gr == null && greedy.Camps.Count == 1 && gm.FissionDaughterBlocked == 1, "a pastoral daughter whose herd share exceeds forage and water is vetoed (carried human food cannot feed the herd)");
    }


    // Relative destination test (Docs/Fission.md): the site must be habitable (support ratio > 0) and the daughter better off there than at home,
    // judged with F_d = min(1, support ratio + carried food person-equivalents). No absolute viability bar.
    private static WorldState Scaled(double scale, double parentFood, double stores)
    {
        var w = World(true, new[] { 8, 8 }); var c = w.Camps[0]; c.Population = 100; c.ClothPerCapita = 1; c.FoodNeedsMetRatio = parentFood; c.PlantFood = stores; c.Inventory.LightHousingUnits = 100 / w.Config.Neolithic.LightHousingPeoplePerUnit;
        foreach (var t in w.Tiles) { t.Food *= scale; t.PotentialFood *= scale; for (int s = 0; s < 4; s++) { t.Seasons[s].MaxFood *= (float)scale; t.Seasons[s].MaxPotentialFood *= (float)scale; } }
        w.Config.FissionDaughterMargin = .05; Explore(w, c); Pressure(w, 0);
        return w;
    }
    private static void Relative()
    {
        const double scale = .370416, even = .246944;  // actual opening wild stock gives the former .66/.72 support ratios, without hypothetical crop renewal
        double bar = new SimulationConfig().StartViability / 100.0;
        // A parent short of food (F = 0.3) leaves for a thin site (ratio 0.66, below the old absolute bar) that the daughter prefers to home.
        var low = Scaled(scale, .3, 0); var lowReport = Pass(low, out var lm);
        Check(lowReport != null && lowReport.Desirability > .55 && lowReport.Desirability < bar && lm.FissionDaughterBlocked == 0 && lm.FissionCommits == 1, "a site with support ratio 0.66 (below the old 0.85 bar) qualifies when the parent is worse off");
        // A site of like richness is rejected when the parent is well off and carries nothing: the daughter would be worse off than at home.
        var well = Scaled(even, 1, 0); var wm = new ResolutionMetrics(); var wellReport = Pass(well, out wm);
        Check(wellReport == null && wm.FissionDaughterChecks == 1 && wm.FissionDaughterBlocked == 1 && well.Camps.Count == 1 && well.Camps[0].FissionDestination != null, "the site is vetoed when the parent is well off (and stays cached)");
        // Carried food covers the shortfall: the parent's stores travel with the daughter in her proportional share.
        var fed = Scaled(even, 1, 12000); var fedReport = Pass(fed, out var fm);
        Check(fedReport != null && fm.FissionDaughterBlocked == 0 && Immigration.CarriedPerPerson(fed.Config, fed.Camps[0]) > 0, "a well-off parent's carried food lets the thin site qualify");
        // The veto estimate is monotone in the site's support ratio and in the carried food, capped at F_d = 1.
        var est = Scaled(even, 1, 0); var parent = est.Camps[0]; int nd = Kinship.DaughterSize(parent.Population, parent.FissionPolicy);
        double q0 = Fission.EstimateSplit(est, parent, nd, 1, 1, 0).DaughterQoL, q5 = Fission.EstimateSplit(est, parent, nd, 1, 1, .5).DaughterQoL, q1 = Fission.EstimateSplit(est, parent, nd, 1, 1, 1).DaughterQoL, q2 = Fission.EstimateSplit(est, parent, nd, 1, 1, 2).DaughterQoL;
        Near(q0, 0, "no food, no QoL"); Near(q5, q1 / 2, "the veto QoL is linear in F_d"); Near(q2, q1, "F_d is capped at one"); Near(Fission.EstimateSplit(est, parent, nd, 1, 1).DaughterQoL, q1, "the default F_d is one, as before");
        // Ratio zero: the destination is not habitable at all (no food in reach) and is not a candidate.
        var barren = Scaled(0, .3, 12000); var bm = new ResolutionMetrics();
        Check(Pass(barren, out bm) == null && bm.FissionSearchesNoViable == 1 && barren.Camps[0].FissionDestination == null, "a site with support ratio 0 is rejected even for a parent in need with food to carry");
        // More destinations qualify than under the absolute bar: count the scored sites that would have failed it.
        Check(lowReport.Desirability < bar && fedReport.Desirability < bar, "the qualifying sites sit below the old absolute viability bar");
    }
    private static void UnbuiltCrops()
    {
        var w = World(true, new[] { 8, 8 }); Explore(w, w.Camps[0]);
        foreach (var t in w.Tiles)
        {
            t.Food = 0;
            for (int s = 0; s < 4; s++) t.Seasons[s].MaxFood = 0;
        }
        Check(Pass(w, out var metrics) == null && metrics.FissionSearchesNoViable == 1,
            "biomass with no current wild food does not become free primitive farmland for a daughter");
    }

    // The funnel adds up: every screened camp ends in exactly one outcome.
    private static void Funnel()
    {
        var worlds = new List<Func<WorldState>>
        {
            () => { var w = World(true, new[] { 8, 8 }); Explore(w, w.Camps[0]); return w; },                                  // commits
            () => { var w = World(true, new[] { 8, 8 }); Explore(w, w.Camps[0]); Ratio(w, w.Camps[0], .9); return w; },        // destination below threshold
            () => Scaled(.004, 1, 0),                                                                                           // daughter veto
            () => Scaled(0, .3, 12000),                                                                                         // no viable site
            () => { var w = World(true, new[] { 8, 8 }); w.Camps[0].People.Memory = new ContactMemory(w.Config.Width); return w; }, // nothing known: no candidates
            () => { var w = World(true, new[] { 8, 8 }, new[] { 14, 8 }); var mid = w.Map.At(11, 8); foreach (var c in w.Camps) { c.People.Memory = new ContactMemory(w.Config.Width); c.People.Memory.Explored.Add(mid); } return w; }, // one dropped
            () => { var w = World(true, new[] { 4, 4 }, new[] { 10, 4 }, new[] { 16, 4 }, new[] { 7, 10 }, new[] { 13, 10 }); foreach (var c in w.Camps) Explore(w, c); return w; },
            () => { var w = World(true, new[] { 8, 8 }); Explore(w, w.Camps[0]); Quiet(w); return w; },                          // below the search screen: not screened
        };
        var total = new ResolutionMetrics(); int index = 0;
        foreach (var make in worlds)
        {
            var w = make(); Pass(w, out var m); index++;
            Check(m.FissionScreened == m.FissionSearchesNoCandidates + m.FissionSearchesNoViable + m.FissionWithDestination, "world " + index + ": screened = empty searches + non-viable searches + camps with a destination");
            Check(m.FissionWithDestination == m.FissionBelowThreshold + m.FissionDaughterBlocked + m.FissionStayersBlocked + m.FissionProposals, "world " + index + ": destinations = below threshold + daughter veto + stayers veto + proposals");
            Check(m.FissionProposals == m.FissionDropped + m.FissionCommits && m.FissionScreened <= m.FissionCampsEvaluated, "world " + index + ": proposals = dropped + committed");
            total.FissionScreened += m.FissionScreened; total.FissionSearchesNoCandidates += m.FissionSearchesNoCandidates; total.FissionSearchesNoViable += m.FissionSearchesNoViable; total.FissionBelowThreshold += m.FissionBelowThreshold; total.FissionDaughterBlocked += m.FissionDaughterBlocked; total.FissionDropped += m.FissionDropped; total.FissionCommits += m.FissionCommits;
        }
        Check(total.FissionSearchesNoCandidates >= 1 && total.FissionSearchesNoViable >= 1 && total.FissionBelowThreshold >= 1 && total.FissionDaughterBlocked >= 1 && total.FissionDropped >= 1 && total.FissionCommits >= 1, "the fixtures exercise every funnel stage");
        // Determinism: scalar and reversed 4-worker passes report the same funnel.
        ResolutionMetrics Run(PhaseExecution x) { var w = World(true, new[] { 4, 4 }, new[] { 10, 4 }, new[] { 16, 4 }, new[] { 7, 10 }, new[] { 13, 10 }); foreach (var c in w.Camps) Explore(w, c); Pass(w, out var m, x); return m; }
        var a = Run(new PhaseExecution()); var b = Run(new PhaseExecution(4, 2, true, 1));
        Check(a.FissionScreened == b.FissionScreened && a.FissionWithDestination == b.FissionWithDestination && a.FissionCommits == b.FissionCommits && a.FissionSearchesNoViable == b.FissionSearchesNoViable, "the funnel is identical across workers");
    }

    // Distress fission (Docs/Fission.md): a camp that cannot feed itself splits for better ground regardless of crowding. Uncrowded fixture:
    // fully housed (fixed Housing for everyone), full clothing, so the stayers' crowding gain is small and F, W carry the signal.
    private static WorldState Famine(int people, double food, double water, double gstar = .113)
    {
        var w = World(true, new[] { 8, 8 }); var c = w.Camps[0]; c.Population = people; c.ClothPerCapita = 1; c.FoodNeedsMetRatio = food; c.WaterNeedsMetRatio = water;
        w.FixedHousing.Add(new FixedHousingStock { CampId = c.Id, Tile = c.Tile, Units = people, LastUpdatedDays = w.Days });
        Explore(w, c); Pressure(w, gstar);
        return w;
    }
    private static void Nomadic(Camp c) { MakeNeolithic(c, Technology.AnimalHusbandry); c.Technologies[(int)Technology.Cultivation] = false; c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true; }
    private static void Distress()
    {
        // The famine fixture: N = 80, housed, F = 0.4, W = 1, a rich destination. The stayers' crowding gain alone is large only because of food relief.
        var w0 = Famine(80, .4, 1); var c0 = w0.Camps[0]; double gstar = w0.Config.FissionGainThreshold;
        var policySize = Fission.EstimateSplit(w0, c0, FissionPolicy.Neutral);
        Check(policySize.Daughter == 24 && Math.Abs(policySize.StayFood - .4 * 80 / 56) < 1e-12, "the stayers' F_hat = F N / N_stay at the policy size");
        var e39 = Fission.EstimateSplit(w0, c0, 39, 1, 1);
        Near(e39.StayFood, .4 * 80 / 41, "the stayers' food relief at the clamped distress size");
        Check(e39.StayFood > c0.FoodNeedsMetRatio * c0.WaterNeedsMetRatio && e39.QAfter > e39.QNow && e39.StayersAcceptable(0), "the stayers are better off (F_hat rises)");
        double fullySupported = Fission.EstimateSplit(w0, c0, (80 - 1) / 2, 1, 1).DaughterGain;
        var r = Pass(w0, out var m);
        Check(r != null && r.Trigger == FissionTrigger.Distress, "a starving uncrowded camp splits with the Distress trigger");
        int largest = (80 - 1) / 2; int nShort = (int)Math.Ceiling(80 * (1 - .4) - 1e-9);
        Check(nShort == 48 && r.Daughter == largest && w0.Camps.Count == 2, "the leaving group is n_short = ceil(N (1 - F W)) = 48, clamped to the largest legal daughter (" + r.Daughter + ")");
        Check(r.DaughterGain >= gstar && r.Gain > 0 && Math.Abs(r.DaughterGain - fullySupported) < 1e-9, "the report records the daughter gain at the (fully supported) destination");
        Check(m.FissionDistressProposals == 1 && m.FissionDistressCommits == 1 && m.FissionCrowdingProposals == 0 && m.FissionCrowdingCommits == 0 && m.FissionDistressDaughters == largest, "metrics count proposals and commits per trigger");
        Check(Daughter(w0, c0).Population == largest && c0.Population == 80 - largest && Daughter(w0, c0).Population + c0.Population == 80, "the people ledger is conserved by the distress split");
        // Size follows the shortfall: the policy size when the shortfall is smaller, n_short in between, the cap beyond it.
        // F = 0.8: the daughter pull is below G* but the stayers' food relief alone passes it, so the split is the policy size under Crowding.
        foreach (var (food, expected, trigger) in new[] { (.8, 24, FissionTrigger.Crowding), (.6, 32, FissionTrigger.Distress), (.5, 39, FissionTrigger.Distress) })
        {
            var w = Famine(80, food, 1); var rr = Pass(w, out _);
            Check(rr != null && rr.Trigger == trigger && rr.Daughter == expected, "F = " + food + " sends " + expected + " people (" + (rr?.Daughter.ToString() ?? "none") + ")");
            var es = Fission.EstimateSplit(w, w.Camps[0], rr.Daughter, 1, 1);
            Check(es.QAfter >= es.QNow, "the stayers are never worse off by construction");
        }
        // Water distress: the same shortfall through W alone.
        var water = Famine(80, 1, .4); var wr = Pass(water, out _);
        Check(wr != null && wr.Trigger == FissionTrigger.Distress && wr.Daughter == largest, "a camp short of water leaves for better ground too");
        // A camp short enough that the policy daughter is below the minimum can still send the people its land cannot carry; one too small cannot.
        var small = Famine(30, .5, 1); var sr = Pass(small, out _);
        Check(Kinship.DaughterSize(30, FissionPolicy.Neutral) < small.Config.FissionMinimumDaughter && sr != null && sr.Daughter == (30 - 1) / 2 && sr.Trigger == FissionTrigger.Distress, "a starving small camp sends the people its land cannot carry although the policy size is below the minimum");
        Check(Pass(Famine(20, .3, 1), out _) == null, "a camp whose largest legal daughter is below the minimum never splits");
        // The same camp fed: no split, and neither gain reaches G*.
        var fed = Famine(80, 1, 1); var fe = Fission.EstimateSplit(fed, fed.Camps[0], FissionPolicy.Neutral);
        Check(Pass(fed, out var fm) == null && fe.Gain < gstar && fe.DaughterGain < gstar && fm.FissionSearches == 0 && fed.Camps.Count == 1, "with F = W = 1 the uncrowded camp does not split (gain " + fe.Gain.ToString("F3") + ", daughter " + fe.DaughterGain.ToString("F3") + " < G*)");
        // The expectation reset alone does not trigger: a parent with a high expectation and no goods would see a fresh start (post-move
        // expectation) as a large daughter gain, but the trigger compares at the parent's own expectation on both sides.
        WorldState Reset(double threshold)
        {
            var w = Famine(80, .99, 1, threshold); var c = w.Camps[0]; c.ClothPerCapita = 0; c.RudimentaryExpectation = .6; w.FixedHousing.Clear(); return w;
        }
        var probe = Reset(.113); var pe = Fission.EstimateSplit(probe, probe.Camps[0], Kinship.DaughterSize(80, FissionPolicy.Neutral), 1, 1);
        Check(pe.DaughterQoL > pe.DaughterExpected * 1.2 && pe.DaughterQoL / pe.QNow - 1 > 2 * Math.Max(pe.Gain, pe.DaughterGain), "the post-move expectation reset would, if counted, be a large gain (" + (pe.DaughterQoL / pe.QNow - 1).ToString("F2") + " vs " + pe.DaughterGain.ToString("F2") + ")");
        Near(pe.DaughterGain, pe.DaughterExpected / pe.QNow - 1, "the trigger gain compares the parent's expectation on both sides");
        var above = Reset(Math.Max(pe.Gain, pe.DaughterGain) * 1.01); Check(Pass(above, out _) == null && above.Camps.Count == 1, "the reset alone does not found a settlement");
        var below = Reset(Math.Max(pe.Gain, pe.DaughterGain) * .99); Check(Pass(below, out _) != null, "the same camp splits once its threshold is below the like-for-like gain");
        // No viable destination within reach: a waterless landscape, then a landscape too poor for the leaving group.
        var dry = Famine(80, .4, 1); foreach (var t in dry.Tiles) for (int s = 0; s < 4; s++) dry.Tiles[t.Id].Seasons[s].MaxFreshwater = 0;
        Check(Pass(dry, out var dm) == null && dry.Camps[0].FissionDestination == null && dry.Camps.Count == 1 && dm.FissionCandidatesScored > 0 && dm.FissionDistressProposals == 0, "famine without a viable destination does not split");
        var unknown = Famine(80, .4, 1); unknown.Societies[0].Memory = null; unknown.Camps[0].People.Progression.Era = Era.Paleolithic; unknown.Camps[0].Technology = Technology.None;
        Check(Fission.RegimeOf(unknown.Camps[0]) == FissionRegime.Paleolithic && Pass(unknown, out _) == null, "a camp that knows no reachable site does not split");
        // A viable but only just supported destination: F_d < 1 lowers the daughter gain. With G* just below the optimistic gain the screen
        // passes and the destination is found, but the actual gain falls short; the control with rich ground splits.
        WorldState Ground(double k, double threshold)
        {
            var w = Famine(80, .4, 1, threshold); Nomadic(w.Camps[0]);
            foreach (var t in w.Tiles) { t.Food = 1000 * k; t.PotentialFood = 8000 * k; for (int s = 0; s < 4; s++) { w.Tiles[t.Id].Seasons[s].MaxFood = (float)(1000 * k); w.Tiles[t.Id].Seasons[s].MaxPotentialFood = (float)(8000 * k); } }
            return w;
        }
        double kk = 1; FissionReport probeReport = null;
        for (double k = .02; k <= 1.0001; k += .005) { var gw = Ground(k, 0); var pr = Pass(gw, out _); if (pr != null && pr.Desirability >= .86 && pr.Desirability <= .95) { kk = k; probeReport = pr; break; } }
        Check(probeReport != null, "a landscape with a viable but thinly supported destination exists");
        var poorWorld = Ground(kk, 0); var pc = poorWorld.Camps[0]; Check(Fission.RegimeOf(pc) == FissionRegime.Nomadic, "the thin-ground fixture is Nomadic");
        double g1 = Fission.EstimateSplit(poorWorld, pc, largest, 1, 1).DaughterGain, crowdGain = Fission.EstimateSplit(poorWorld, pc, FissionPolicy.Neutral).Gain;
        double between = g1 - .02 * (1 + g1); Check(between > crowdGain && between > 0, "the fixture separates the stayers' gain from the optimistic daughter gain");
        var thin = Ground(kk, between); var tr = Pass(thin, out var tm);
        Check(tr == null && thin.Camps[0].FissionDestination != null && tm.FissionSearches == 1 && thin.Camps.Count == 1, "a destination with poor support (F_d < 1) is found but does not trigger distress");
        var rich = Ground(1, between); var rr2 = Pass(rich, out _); Check(rr2 != null && rr2.Trigger == FissionTrigger.Distress, "with fully supported ground the same threshold splits");
        // Crowding for fed camps is unchanged and labelled Crowding; with G* = 0 a fed camp splits with no distress counts.
        var crowded = World(true, new[] { 8, 8 }); Explore(crowded, crowded.Camps[0]); var cr = Pass(crowded, out var cm);
        Check(cr != null && cr.Trigger == FissionTrigger.Crowding && cm.FissionCrowdingCommits == 1 && cm.FissionDistressCommits == 0 && cm.FissionDistressDaughters == 0 && cr.Daughter == Kinship.DaughterSize(100, FissionPolicy.Neutral), "a fed camp over its crowding threshold splits at the policy size with the Crowding trigger");
        // A starving crowded camp whose stayers' crowding gain passes at the policy size but whose distress pull fails still splits as Crowding.
        var mixed = Ground(kk, 0); Nomadic(mixed.Camps[0]); var mp = mixed.Camps[0]; double stay0 = Fission.EstimateSplit(mixed, mp, FissionPolicy.Neutral).Gain; Pressure(mixed, stay0 * .9 + 0);
        var mr = Pass(mixed, out var mm);
        Check(mr != null && (mr.Trigger == FissionTrigger.Distress ? mm.FissionDistressCommits == 1 : mm.FissionCrowdingCommits == 1), "every commit is counted under exactly one trigger");
        // Determinism, conservation and persistence: starving camps over several turns under the real resolver, scalar vs reversed 4-worker.
        var hungry = new[] { Hungry(), Hungry() }; hungry[1].Camps.Reverse(); hungry[1].Societies.Reverse();
        var lanes = new[] { new PhaseExecution(), new PhaseExecution(4, 2, true, 1) }; int distressed = 0, people = hungry[0].Camps.Sum(c => c.Population);
        for (int t = 0; t < 5; t++)
        {
            for (int i = 0; i < 2; i++) Advance(hungry[i], new TurnResolver(lanes[i]));
            Check(Key(hungry[0]) == Key(hungry[1]), "scalar and reversed 4-worker distress replay agree after turn " + t);
            string Events(WorldState x) => string.Join(";", x.History.Last().Fissions.Select(f => f.ParentCamp + ">" + f.DaughterCamp + ":" + f.Trigger + ":" + f.Daughter));
            Check(Events(hungry[0]) == Events(hungry[1]), "trigger and size sequences agree after turn " + t);
            distressed += hungry[0].History.Last().Fissions.Count(f => f.Trigger == FissionTrigger.Distress);
            TurnResolver.Invariants(hungry[0]);
        }
        Check(distressed >= 1, "the starving world exercises distress fission under the resolver (" + distressed + ")");
        var saved = SaveCodec.Load(SaveCodec.Save(hungry[0]));
        Check(Key(saved) == Key(hungry[0]) && saved.History.SelectMany(h => h.Fissions).Count(f => f.Trigger == FissionTrigger.Distress) == distressed && saved.History.SelectMany(h => h.Fissions).Where(f => f.Trigger == FissionTrigger.Distress).All(f => f.DaughterGain > 0), "the trigger and daughter gain round trip in the saved history");
        // Older saves carry neither field: they read as Crowding with zero daughter gain.
        var serializer = new DataContractSerializer(typeof(FissionReport)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new FissionReport { Trigger = FissionTrigger.Distress, DaughterGain = 1.5, Gain = .5 }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        string old = Regex.Replace(xml, "<(Trigger|DaughterGain)>[^<]*</\\1>", "");
        Check(xml.Contains("<Trigger>") && old != xml && !old.Contains("Trigger"), "the report serializes the new fields and the legacy fixture omits them");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(old))) { var f = (FissionReport)serializer.ReadObject(stream); Check(f.Trigger == FissionTrigger.Crowding && f.DaughterGain == 0 && f.Gain == .5, "an older report loads as Crowding with no daughter gain"); }
        Check(people > 0, "fixture sanity");
    }
    // Starving camps under the real resolver: little stored food, no growth, G* at its default so only the distress pull and crowding relief act.
    private static WorldState Hungry()
    {
        var w = Busy(); Pressure(w, .113);
        foreach (var t in w.Tiles) { t.Food = 60; t.PotentialFood = 600; for (int s = 0; s < 4; s++) { w.Tiles[t.Id].Seasons[s].MaxFood = 60; w.Tiles[t.Id].Seasons[s].MaxPotentialFood = 600; } }
        foreach (var c in w.Camps) { c.PlantFood = 300; c.Population = 120; }
        return w;
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

    // B2: with spacing 3 a radius-3 search sees only the distance-3 ring; radius 4 and 5 reach sites beyond it, nearer still wins ties,
    // and the lazily cached search stays bounded by the disc.
    private static void WiderRadius()
    {
        WorldState Lean(int radius)
        {
            var w = World(true, new[] { 12, 8 }); var c = w.Camps[0]; Explore(w, c, 7); w.Config.FissionRadius = radius;
            // Every site on the distance-3 ring has no food (support ratio 0, so not a destination at all); the sites at distance 4 and 5 stay rich.
            foreach (int n in w.Map.Radius(c.Tile, 4).Where(n => w.Map.Distance(c.Tile, n) <= 4))
            {
                var t = w.Tiles[n]; t.Food = 0; t.PotentialFood = 0; for (int s = 0; s < 4; s++) { t.Seasons[s].MaxFood = 0; t.Seasons[s].MaxPotentialFood = 0; }
            }
            return w;
        }
        var r3 = Lean(3); var none = Pass(r3, out var m3);
        Check(none == null && m3.FissionCandidatesScored == 18, "radius 3 sees only the starved distance-3 ring (" + m3.FissionCandidatesScored + " candidates) and finds nothing");
        foreach (int radius in new[] { 4, 5 })
        {
            var w = Lean(radius); var parent = w.Camps[0]; var report = Pass(w, out var m);
            Check(report != null && w.Map.Distance(parent.Tile, report.Tile) > 3, "radius " + radius + " finds a site beyond the distance-3 ring");
            int disc = w.Map.Radius(parent.Tile, radius).Count(n => w.Map.Distance(parent.Tile, n) >= 3);
            Check(m.FissionSearches == 1 && m.FissionCandidatesScored <= disc && m.FissionCandidatesScored > 18 && m.FissionTileProjections <= 7 * m.FissionCandidatesScored, "radius " + radius + " scores at most the bounded disc outside the spacing (" + m.FissionCandidatesScored + " of " + disc + ")");
        }
        // Nearer still wins ties: in a uniform rich world radius 5 picks the distance-3 ring, as radius 3 does.
        var u3 = World(true, new[] { 12, 8 }); Explore(u3, u3.Camps[0], 7); var near3 = Pass(u3, out _);
        var u5 = World(true, new[] { 12, 8 }); Explore(u5, u5.Camps[0], 7); u5.Config.FissionRadius = 5; var near5 = Pass(u5, out _);
        Check(near3 != null && near5 != null && near3.Tile == near5.Tile && u5.Map.Distance(u5.Camps[0].Tile, near5.Tile) == 3, "radius 5 keeps the nearest tied site, so daughters stay close when sites exist");
        // Deterministic: the same fixture twice gives the same destination.
        var d1 = Lean(5); var d2 = Lean(5); Check(Pass(d1, out _).Tile == Pass(d2, out _).Tile, "the wider search is deterministic");
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
        Check(dto.Schema == 19 && dto.ParentSociety == parent.SocietyId && dto.KinAffinity == 1 && dto.KinRelations.Length == 2 && dto.Settlements.Single().FissionPolicy == FissionPolicy.Neutral && dto.Settlements.Single().FissionDestination == -1, "the serialized contract carries lineage, kin and the settlement's fission state");
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
