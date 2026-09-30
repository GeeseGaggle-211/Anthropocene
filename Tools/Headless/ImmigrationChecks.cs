using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// Immigration between known settlements (WP10b): known-settlement filter, utility and size closed forms, reconcile, ordering with
// fission, conservation, determinism and persistence. Owns its worlds and counters (lanes run checks concurrently).
internal static class ImmigrationChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Immigration: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-9) => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)), message + " (" + a + " vs " + b + ")");
    [ThreadStatic] private static TurnRecord lastRecord;
    [ThreadStatic] private static ResolutionMetrics lastMetrics;

    // A rich uniform 24x16 fixture. Camps are Paleolithic (cohesion scale 220) unless made Nomadic or Agricultural; every camp has full
    // clothing and ample Light Housing so that Q = F * W * coverage * S and the closed forms stay readable.
    private static WorldState World(params int[][] sites) => World(24, 16, sites);
    private static WorldState World(int width, int height, params int[][] sites)
    {
        var cfg = SimulationConfig.Small(1); cfg.ImmigrationEnabled = true; cfg.Width = width; cfg.Height = height; cfg.AnnualHumanGrowthRate = 0; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0; cfg.ContactExpiryTurns = 50;
        cfg.FissionHousingRelief = 1; cfg.FissionDaughterMargin = 1;
        var w = new WorldState { Seed = 101, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 8000, Food = 1000 };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = t;
        }
        for (int i = 0; i < sites.Length; i++)
        {
            var c = new Camp { Id = w.NextId++, Player = i, Tile = w.Map.At(sites[i][0], sites[i][1]), Population = 100, PlantFood = 6000, AnimalFood = 3000, AquaticFood = 1500, RudimentaryExpectation = .5 };
            c.Inventory.LightHousingUnits = 1e9; c.Inventory.Cloth = 77; c.Inventory.AnimalFabric = 11; c.Inventory.HerdingTools = 3; c.GrowthRemainder = .4;
            w.Camps.Add(c);
        }
        w.Config.Players = sites.Length; w.PlayerCount = sites.Length; w.Turn = 3; w.Days = 1000;
        Ownership.Bind(w);
        foreach (var t in w.Tiles) t.LastEcologyUpdateDays = w.Days;
        return w;
    }
    // A world whose camps sit at chosen tiles (by exact hex distances) instead of columns and rows.
    private static WorldState WorldAt(int count, Func<WorldState, int, int> place)
    {
        var w = World(Enumerable.Range(0, count).Select(i => new[] { 0, 0 }).ToArray());
        for (int i = 0; i < count; i++) w.Camps[i].Tile = place(w, i);
        return w;
    }
    private static int Ring(WorldState w, int center, int distance, int nth = 0, params int[] avoid)
    {
        var found = w.Map.Radius(center, distance).Where(t => w.Map.Distance(center, t) == distance && !avoid.Contains(t)).OrderBy(t => t).ToArray();
        return found[Math.Min(nth, found.Length - 1)];
    }
    private static void MakeNomadic(Camp c) { c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true; c.People.Progression.Era = Era.Neolithic; }
    private static void MakeAgricultural(Camp c) { c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic; }
    private static double Scale(WorldState w, Camp c) => NeolithicProduction.CohesionScale(w.Config.Neolithic, c);
    // Independent closed form of the committed QoL at a headcount (incoming people bring neither clothing nor housing).
    private static double Quality(WorldState w, Camp c, int people)
    {
        var n = w.Config.Neolithic; double cloth = Math.Min(1, c.ClothPerCapita * c.Population / (double)people), housing = 1; // supply 1e9
        double e = c.RudimentaryExpectation, coverage = n.RudimentaryWeightClothing * (1 - e * (1 - cloth)) + n.RudimentaryWeightHousing * (1 - e * (1 - housing));
        return c.FoodNeedsMetRatio * c.WaterNeedsMetRatio * coverage * Math.Exp(-Math.Pow(people / Scale(w, c), n.SocialCohesionExponent));
    }
    // Sets the destination's food ratio so that Q_j - Q_i equals `gap` at present residents (both camps with 100 residents).
    private static void Gap(WorldState w, Camp source, Camp destination, double gap)
    {
        source.FoodNeedsMetRatio = .5; double target = Quality(w, source, source.Population) + gap; destination.FoodNeedsMetRatio = 1;
        destination.FoodNeedsMetRatio = target / Quality(w, destination, destination.Population);
    }
    private static void Remember(WorldState w, Camp observer, Camp other, bool moved = false, int tile = -1)
    {
        var memory = ContactKnowledge.MemoryOf(w, observer.People); memory.Upsert(other.Id, other.Player, tile < 0 ? other.Tile : tile, w.Turn, ContactSource.Sighting);
        if (moved) { var c = memory.Find(other.Id); c.Moved = true; c.Tile = -1; }
    }
    private static HashSet<int> Pass(WorldState w, PhaseExecution execution = null)
    {
        var record = new TurnRecord { Turn = w.Turn }; var metrics = new ResolutionMetrics();
        foreach (var c in w.Camps) record.Demographics.Add(new DemographicReport { Camp = c.Id, StartingPopulation = c.Population, EndingPopulation = c.Population });
        var result = Immigration.Resolve(w, record, execution ?? new PhaseExecution(), metrics); lastRecord = record; lastMetrics = metrics; return result;
    }
    private static MigrationReport Flow(TurnRecord r, int source) => r.Migrations.SingleOrDefault(m => m.SourceCamp == source);

    public static int Run()
    {
        checks = 0; Config(); Closed(); Known(); Choice(); Deadband(); Reconcile(); Ordering(); MultiHop(); Conservation(); Records(); Replay(); Persistence();
        Console.WriteLine("PASS: " + checks + " immigration assertions"); return checks;
    }

    private static void Config()
    {
        var c = new SimulationConfig();
        Check(!c.ImmigrationEnabled && c.ImmigrationRadius == 4 && c.ImmigrationQualityWeight == 1 && c.ImmigrationDistanceCost == .01 && c.ImmigrationAttachmentCost == .02 && c.ImmigrationToSedentaryCost == .05 && c.ImmigrationToNomadicCost == .15
            && c.ImmigrationDeadband == .075 && c.ImmigrationMaxFraction == .10 && c.ImmigrationMinimumGroup == 4 && c.ImmigrationSourceFloor == 12, "immigration defaults are off with the specified levers");
        foreach (Action<SimulationConfig> bad in new Action<SimulationConfig>[] { x => x.ImmigrationRadius = 0, x => x.ImmigrationRadius = 6, x => x.ImmigrationQualityWeight = -1, x => x.ImmigrationDistanceCost = -.1, x => x.ImmigrationAttachmentCost = double.NaN,
            x => x.ImmigrationToSedentaryCost = -1, x => x.ImmigrationToNomadicCost = 9, x => x.ImmigrationDeadband = 0, x => x.ImmigrationMaxFraction = .6, x => x.ImmigrationMinimumGroup = 0, x => x.ImmigrationSourceFloor = 0 })
        {
            var x = new SimulationConfig(); bad(x); bool rejected = false; try { x.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "an invalid immigration lever is rejected");
        }
        var serializer = new DataContractSerializer(typeof(SimulationConfig)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { ImmigrationRadius = 5, ImmigrationEnabled = true }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<ImmigrationRadius>5</ImmigrationRadius>") && xml.Contains("<ImmigrationEnabled>true</ImmigrationEnabled>"), "immigration levers are serialized");
        string legacy = Regex.Replace(xml, "<Immigration[A-Za-z]*>[^<]*</Immigration[A-Za-z]*>", "");
        Check(legacy != xml && !legacy.Contains("<Immigration"), "legacy fixture omits every immigration field");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy)))
        {
            var old = (SimulationConfig)serializer.ReadObject(stream);
            Check(!old.ImmigrationEnabled && old.ImmigrationRadius == 4 && old.ImmigrationQualityWeight == 1 && old.ImmigrationDistanceCost == .01 && old.ImmigrationAttachmentCost == .02 && old.ImmigrationToSedentaryCost == .05 && old.ImmigrationToNomadicCost == .15
                && old.ImmigrationDeadband == .075 && old.ImmigrationMaxFraction == .10 && old.ImmigrationMinimumGroup == 4 && old.ImmigrationSourceFloor == 12, "an older config loads with immigration disabled and the default levers");
            old.Validate(); checks++;
        }
        string json = File.ReadAllText(FindConfig());
        foreach (var pair in new[] { ("ImmigrationEnabled", "false"), ("ImmigrationRadius", "4"), ("ImmigrationQualityWeight", "1"), ("ImmigrationDistanceCost", "0.01"), ("ImmigrationAttachmentCost", "0.02"), ("ImmigrationToSedentaryCost", "0.05"),
            ("ImmigrationToNomadicCost", "0.15"), ("ImmigrationDeadband", "0.075"), ("ImmigrationMaxFraction", "0.1"), ("ImmigrationMinimumGroup", "4"), ("ImmigrationSourceFloor", "12") })
        {
            var m = Regex.Match(json, "\"" + pair.Item1 + "\":\\s*([0-9.a-z]+)");
            Check(m.Success && m.Groups[1].Value == pair.Item2, "OpeningConfig.json carries " + pair.Item1);
        }
        // Disabled by default: the pass neither runs nor reports, whatever the populations.
        var w = World(new[] { 8, 8 }, new[] { 8, 10 }); w.Config.ImmigrationEnabled = false; Gap(w, w.Camps[0], w.Camps[1], .5);
        Check(Pass(w) == null && lastRecord.Migrations.Count == 0 && lastMetrics.ImmigrationCampsEvaluated == 0 && w.Camps.All(x => x.Population == 100), "a disabled pass changes nothing");
    }
    private static string FindConfig()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        { string path = Path.Combine(dir.FullName, "Assets", "_Anthropocene", "Resources", "OpeningConfig.json"); if (File.Exists(path)) return path; }
        throw new FileNotFoundException("OpeningConfig.json");
    }

    // Utility closed form (each barrier), the size formula and its floors.
    private static void Closed()
    {
        var cfg = new SimulationConfig(); var a = new Camp { Id = 1 }; var b = new Camp { Id = 2 }; var village = new Camp { Id = 3, IsVillage = true }; var village2 = new Camp { Id = 4, IsVillage = true };
        Near(Immigration.Utility(cfg, .5, .8, 2, a, b), .3 - .02 - .02, "a same-lifestyle move pays only distance and attachment");
        Near(Immigration.Utility(cfg, .5, .8, 4, a, b), .3 - .04 - .02, "distance costs 0.01 per hex");
        Near(Immigration.Utility(cfg, .5, .8, 2, a, village), .3 - .02 - .02 - .05, "nomadic to village pays the sedentary barrier");
        Near(Immigration.Utility(cfg, .5, .8, 2, village, a), .3 - .02 - .02 - .15, "village to nomadic pays the larger nomadic barrier");
        Near(Immigration.Utility(cfg, .5, .8, 2, village, village2), .3 - .02 - .02, "village to village has no lifestyle barrier");
        Near(Immigration.Utility(cfg, .8, .5, 2, a, b), -.3 - .04, "a worse destination has negative utility");
        cfg.ImmigrationQualityWeight = 2; Near(Immigration.Utility(cfg, .5, .8, 2, a, b), .6 - .04, "the quality weight scales the gap"); cfg.ImmigrationQualityWeight = 1;
        Check(Immigration.LifestyleCost(cfg, a, village) == cfg.ImmigrationToSedentaryCost && Immigration.LifestyleCost(cfg, village, a) == cfg.ImmigrationToNomadicCost && Immigration.LifestyleCost(cfg, a, b) == 0, "lifestyle barrier table");
        Check(cfg.ImmigrationToNomadicCost > cfg.ImmigrationToSedentaryCost, "leaving a village costs more than settling into one");
        // Size: floor(N * 0.10 * clamp((dU - theta) / theta, 0, 1)), floors and minimum group.
        double theta = cfg.ImmigrationDeadband;
        Check(Immigration.Size(cfg, 100, theta) == 0 && Immigration.Size(cfg, 100, theta - .01) == 0 && Immigration.Size(cfg, 100, -1) == 0, "no size at or below the deadband");
        Check(Immigration.Size(cfg, 100, 1.5 * theta) == 5 && Immigration.Size(cfg, 100, 2 * theta) == 10 && Immigration.Size(cfg, 100, 9) == 10, "size ramps from zero at theta to the maximum fraction at 2 theta");
        Check(Immigration.Size(cfg, 100, theta * (1 + .39)) == 0 && Immigration.Size(cfg, 100, theta * (1 + .4 + 1e-9)) == 4, "below the minimum group of four there is no intent");
        Check(Immigration.Size(cfg, 15, 9) == 0 && Immigration.Size(cfg, 16, 9) == 0 && Immigration.Size(cfg, 40, 9) == 4, "a small source emits nothing without a minimum group");
        cfg.ImmigrationMaxFraction = .5; Check(Immigration.Size(cfg, 20, 9) == 8 && Immigration.Size(cfg, 17, 9) == 5 && Immigration.Size(cfg, 15, 9) == 0 && Immigration.Size(cfg, 100, 9) == 50, "a source always keeps at least the floor of 12 residents"); cfg.ImmigrationMaxFraction = .1;
        for (int n = 0; n <= 400; n++)
            for (int k = 0; k <= 40; k++)
            {
                double u = k * .01; int expected = u > theta ? (int)Math.Floor(n * .1 * Math.Min(1, Math.Max(0, (u - theta) / theta))) : 0;
                expected = Math.Min(expected, n - 12); if (expected < 4) expected = 0;
                if (Immigration.Size(cfg, n, u) != expected) throw new Exception("Immigration: size formula for N=" + n + " dU=" + u);
            }
        checks++;
        // The QoL estimator is the same formula as the fission estimator at present residents, and only dilutes with arrivals.
        var w = World(new[] { 8, 8 }); var c = w.Camps[0]; MakeAgricultural(c); c.ClothPerCapita = .7; c.Inventory.LightHousingUnits = 20; c.FoodNeedsMetRatio = .9; c.WaterNeedsMetRatio = .95; c.RudimentaryExpectation = 0; c.StationaryYears = 40;
        var model = Fission.QoLModelOf(w, c);
        Near(model.At(c.Population), Fission.EstimateSplit(w, c, FissionPolicy.Neutral).QNow, "the estimator at present residents equals the fission estimator's Q_now");
        double last = model.At(c.Population);
        for (int extra = 1; extra <= 200; extra++) { double q = model.At(c.Population + extra); if (!(q <= last + 1e-15)) throw new Exception("Immigration: QoL is not non-increasing in headcount"); last = q; }
        checks++;
        Check(model.At(0) == 0, "no residents has no QoL");
    }

    // Unknown settlements are never targeted; Paleolithic Societies see only what is in current vision.
    private static void Known()
    {
        // Paleolithic: a far better camp at distance 3 is outside the vision radius of 2 and never targeted; at distance 2 it is seen.
        var w = WorldAt(2, (x, i) => i == 0 ? x.Map.At(8, 8) : Ring(x, x.Map.At(8, 8), 3)); Gap(w, w.Camps[0], w.Camps[1], 1.0);
        Check(w.Map.Distance(w.Camps[0].Tile, w.Camps[1].Tile) == 3 && ContactKnowledge.Remembers(w.Camps[0].People) == false, "fixture: Paleolithic source and a camp three hexes away");
        Pass(w); Check(lastRecord.Migrations.Count == 0 && lastMetrics.ImmigrationIntents == 0, "a Paleolithic source never targets a camp outside current vision");
        // The same camp is also unknown when a contact was (wrongly) stored: Paleolithic Societies use vision only.
        Remember(w, w.Camps[0], w.Camps[1]); Pass(w); Check(lastRecord.Migrations.Count == 0, "Paleolithic Societies ignore stored contacts");
        w.Camps[1].Tile = Ring(w, w.Camps[0].Tile, 2); Gap(w, w.Camps[0], w.Camps[1], 1.0); Pass(w);
        var seen = Flow(lastRecord, w.Camps[0].Id);
        Check(seen != null && seen.DestinationCamp == w.Camps[1].Id && seen.Distance == 2, "a Paleolithic source targets a camp in current vision");
        // A visible camp is seen through the source's hunters too: a hunter group extends vision.
        var h = WorldAt(2, (x, i) => i == 0 ? x.Map.At(8, 8) : Ring(x, x.Map.At(8, 8), 4)); Gap(h, h.Camps[0], h.Camps[1], 1.0);
        Pass(h); Check(lastRecord.Migrations.Count == 0, "fixture: a camp four hexes away is unseen without hunters");
        h.Hunters.Add(new Hunter { Id = h.NextId++, SourceCamp = h.Camps[0].Id, SocietyId = h.Camps[0].SocietyId, Player = h.Camps[0].Player, Population = 4, Tile = Ring(h, h.Camps[1].Tile, 1) });
        Pass(h); Check(lastRecord.Migrations.Count == 1, "a camp in the vision of the source's hunters is known");

        // After the first breakthrough: remembered contacts count; moved, unrecorded and mislocated ones do not.
        var k = WorldAt(2, (x, i) => i == 0 ? x.Map.At(8, 8) : Ring(x, x.Map.At(8, 8), 4)); MakeNomadic(k.Camps[0]); MakeNomadic(k.Camps[1]); Gap(k, k.Camps[0], k.Camps[1], 1.0);
        Check(ContactKnowledge.Remembers(k.Camps[0].People), "fixture: the source is past its first breakthrough");
        Pass(k); Check(lastRecord.Migrations.Count == 0 && lastMetrics.ImmigrationCandidatesScored == 0, "an unrecorded camp outside vision is unknown after the breakthrough");
        Remember(k, k.Camps[0], k.Camps[1], moved: true); Pass(k); Check(lastRecord.Migrations.Count == 0, "a contact marked Moved is not a destination");
        Remember(k, k.Camps[0], k.Camps[1], tile: Ring(k, k.Camps[1].Tile, 1)); Pass(k); Check(lastRecord.Migrations.Count == 0, "a contact whose camp is no longer at the recorded tile is not found");
        Remember(k, k.Camps[0], k.Camps[1]); Pass(k);
        Check(lastRecord.Migrations.Count == 1 && lastRecord.Migrations[0].DestinationCamp == k.Camps[1].Id && lastRecord.Migrations[0].Distance == 4, "a remembered contact within the radius is a destination");
        // Radius: the same contact at distance five is out of range (default radius four).
        var far = WorldAt(2, (x, i) => i == 0 ? x.Map.At(8, 8) : Ring(x, x.Map.At(8, 8), 5)); MakeNomadic(far.Camps[0]); MakeNomadic(far.Camps[1]); Gap(far, far.Camps[0], far.Camps[1], 1.0); Remember(far, far.Camps[0], far.Camps[1]);
        Pass(far); Check(lastRecord.Migrations.Count == 0, "a known camp beyond ImmigrationRadius is not a candidate");
        far.Config.ImmigrationRadius = 5; Pass(far); Check(lastRecord.Migrations.Count == 1, "raising the radius admits it");
        // Kin contacts recorded at fission count the same way (contacts are contacts).
        var kin = WorldAt(2, (x, i) => i == 0 ? x.Map.At(8, 8) : Ring(x, x.Map.At(8, 8), 3)); MakeNomadic(kin.Camps[0]); MakeNomadic(kin.Camps[1]); Gap(kin, kin.Camps[0], kin.Camps[1], 1.0);
        ContactKnowledge.MemoryOf(kin, kin.Camps[0].People).Upsert(kin.Camps[1].Id, kin.Camps[1].Player, kin.Camps[1].Tile, kin.Turn, ContactSource.Kin); Pass(kin);
        Check(lastRecord.Migrations.Count == 1, "a kin contact is a known settlement");
        // A source never targets itself; a camp with no other known camp emits nothing.
        var alone = World(new[] { 8, 8 }); Pass(alone); Check(lastRecord.Migrations.Count == 0 && lastMetrics.ImmigrationCandidatesScored == 0, "a lone camp has no candidate (j != i)");
        // Camps of the source's own Society are always known (the owner sees its own settlements).
        var own = WorldAt(2, (x, i) => i == 0 ? x.Map.At(8, 8) : Ring(x, x.Map.At(8, 8), 4)); own.Camps[1].Bind(own.Camps[0].People); Gap(own, own.Camps[0], own.Camps[1], 1.0); Pass(own);
        Check(lastRecord.Migrations.Count == 1, "a camp of the source's own Society is known");
    }

    // Each source emits at most one intent, for its best valid destination.
    private static void Choice()
    {
        var w = WorldAt(4, (x, i) => i == 0 ? x.Map.At(10, 8) : Ring(x, x.Map.At(10, 8), 2, i - 1)); var s = w.Camps[0];
        Gap(w, s, w.Camps[1], .30); Gap(w, s, w.Camps[2], .50); Gap(w, s, w.Camps[3], .40);
        foreach (var c in w.Camps.Skip(1)) c.Population = 100;
        // The other camps also see each other: make them unattractive to each other by equalizing their quality.
        double qi0 = Quality(w, s, 100); Pass(w);
        Check(lastRecord.Migrations.Count(m => m.SourceCamp == s.Id) == 1 && Flow(lastRecord, s.Id).DestinationCamp == w.Camps[2].Id, "a source with three better camps emits one intent, for the best");
        var f = Flow(lastRecord, s.Id);
        double u = Immigration.Utility(w.Config, f.SourceQoL, f.DestinationQoL, f.Distance, s, w.Camps[2]);
        Near(f.Utility, u, "the reported utility is the closed form"); Near(f.SourceQoL, qi0, "Q_i is the committed QoL at present residents");
        Check(f.Intended == Immigration.Size(w.Config, 100, u), "the intended size is the closed-form size");
        // Ties on utility: the nearer camp, then the lower camp id.
        var t = WorldAt(3, (x, i) => i == 0 ? x.Map.At(10, 8) : i == 1 ? Ring(x, x.Map.At(10, 8), 2, 0) : Ring(x, x.Map.At(10, 8), 2, 1));
        t.Camps[0].FoodNeedsMetRatio = .5; foreach (var c in t.Camps.Skip(1)) c.FoodNeedsMetRatio = 1.4;
        Pass(t); Check(Flow(lastRecord, t.Camps[0].Id)?.DestinationCamp == t.Camps[1].Id, "an exact tie goes to the lower camp id");
        var near = WorldAt(3, (x, i) => i == 0 ? x.Map.At(10, 8) : i == 1 ? Ring(x, x.Map.At(10, 8), 2) : Ring(x, x.Map.At(10, 8), 1));
        near.Camps[0].FoodNeedsMetRatio = .5; near.Camps[1].FoodNeedsMetRatio = 1.4;
        // Equal utility despite different distance needs a compensating quality: the nearer camp is cheaper, so give the farther one exactly the extra gap.
        var cfg = near.Config; double q0 = Quality(near, near.Camps[0], 100);
        near.Camps[2].FoodNeedsMetRatio = 1; near.Camps[2].FoodNeedsMetRatio = (q0 + .40) / Quality(near, near.Camps[2], 100);
        near.Camps[1].FoodNeedsMetRatio = 1; near.Camps[1].FoodNeedsMetRatio = (q0 + .40 + cfg.ImmigrationDistanceCost) / Quality(near, near.Camps[1], 100);
        Pass(near); var nf = Flow(lastRecord, near.Camps[0].Id);
        Check(nf != null && Math.Abs(Immigration.Utility(cfg, nf.SourceQoL, Quality(near, near.Camps[1], 100), 2, near.Camps[0], near.Camps[1]) - Immigration.Utility(cfg, nf.SourceQoL, Quality(near, near.Camps[2], 100), 1, near.Camps[0], near.Camps[2])) < 1e-9
            && nf.DestinationCamp == near.Camps[2].Id, "equal utility goes to the nearer camp");
        // A camp that is a better destination than a worse one chooses by utility, not by nearness.
        near.Camps[1].FoodNeedsMetRatio *= 1.3; Pass(near); Check(Flow(lastRecord, near.Camps[0].Id).DestinationCamp == near.Camps[1].Id, "a higher utility beats a nearer camp");
        // Distance and lifestyle barriers change the choice.
        var life = WorldAt(3, (x, i) => i == 0 ? x.Map.At(10, 8) : Ring(x, x.Map.At(10, 8), 2, i - 1)); MakeNomadic(life.Camps[0]); MakeNomadic(life.Camps[1]); MakeNomadic(life.Camps[2]);
        life.Camps[2].IsVillage = true; life.Camps[0].FoodNeedsMetRatio = .3; life.Camps[1].FoodNeedsMetRatio = 1; life.Camps[2].FoodNeedsMetRatio = 1;
        life.Camps[1].FoodNeedsMetRatio = (Quality(life, life.Camps[0], 100) + .35) / Quality(life, life.Camps[1], 100); life.Camps[2].FoodNeedsMetRatio = (Quality(life, life.Camps[0], 100) + .35 + .03) / Quality(life, life.Camps[2], 100);
        foreach (var c in life.Camps) Remember(life, life.Camps[0], c);
        Pass(life); Check(Flow(lastRecord, life.Camps[0].Id)?.DestinationCamp == life.Camps[1].Id, "the sedentary barrier outweighs a small quality edge of a village");
        life.Camps[2].FoodNeedsMetRatio *= 1.2; Pass(life); Check(Flow(lastRecord, life.Camps[0].Id)?.DestinationCamp == life.Camps[2].Id, "a large enough quality edge overcomes the sedentary barrier");
        var leave = WorldAt(2, (x, i) => i == 0 ? x.Map.At(10, 8) : Ring(x, x.Map.At(10, 8), 2)); MakeNomadic(leave.Camps[0]); MakeNomadic(leave.Camps[1]); leave.Camps[0].IsVillage = true; Gap(leave, leave.Camps[0], leave.Camps[1], .22); Remember(leave, leave.Camps[0], leave.Camps[1]);
        Pass(leave); Check(lastRecord.Migrations.Count == 0, "the larger village-to-nomadic barrier blocks a gap that a same-lifestyle move would take");
        leave.Camps[0].IsVillage = false; Pass(leave); Check(lastRecord.Migrations.Count == 1, "the same gap moves a nomadic camp");
    }

    private static void Deadband()
    {
        // With both camps nomadic and two hexes apart, dU = gap - 0.04; the deadband is 0.075, so the gap threshold is 0.115.
        foreach (var (gap, expected) in new[] { (.10, false), (.1149, false), (.1185, true), (.25, true) })
        {
            var w = WorldAt(2, (x, i) => i == 0 ? x.Map.At(10, 8) : Ring(x, x.Map.At(10, 8), 2)); Gap(w, w.Camps[0], w.Camps[1], gap); w.Config.ImmigrationMinimumGroup = 1; w.Config.ImmigrationMaxFraction = .5; Pass(w);
            Check((lastRecord.Migrations.Count == 1) == expected, "a quality gap of " + gap + (expected ? " clears" : " stays inside") + " the deadband");
        }
        var b = WorldAt(2, (x, i) => i == 0 ? x.Map.At(10, 8) : Ring(x, x.Map.At(10, 8), 2)); Gap(b, b.Camps[0], b.Camps[1], .13);   // dU = .09: (dU - theta) / theta = 0.2 -> floor(100 * .1 * .2) = 2 < minimum group
        Pass(b); Check(lastRecord.Migrations.Count == 0 && lastMetrics.ImmigrationIntents == 0, "a group below the minimum of four is not sent");
        Gap(b, b.Camps[0], b.Camps[1], .115 + .075 * .6); Pass(b); Check(Flow(lastRecord, b.Camps[0].Id)?.Migrants == 6, "size follows the ramp (0.6 of the maximum fraction gives six of 100)");
        var small = WorldAt(2, (x, i) => i == 0 ? x.Map.At(10, 8) : Ring(x, x.Map.At(10, 8), 2)); small.Camps[0].Population = 14; small.Camps[1].Population = 14; Gap(small, small.Camps[0], small.Camps[1], 2); Pass(small);
        Check(lastRecord.Migrations.Count == 0, "a source with fewer than floor plus minimum-group residents sends nobody");
        small.Config.ImmigrationMaxFraction = .5; small.Camps[0].Population = 20; small.Camps[1].Population = 20; Gap(small, small.Camps[0], small.Camps[1], 2); Pass(small);
        Check(Flow(lastRecord, small.Camps[0].Id)?.Migrants == 8 && small.Camps[0].Population == 12, "a source never drops below twelve residents");
    }

    // Destination acceptance cap, partial acceptance and no rerouting.
    private static void Reconcile()
    {
        // Three sources share one destination; all three see the same camp and rate it best. A fourth camp C is better than each source but worse than the destination.
        for (double ratio = 1.0; ratio < 12; ratio += .01)
        {
            var w = WorldAt(5, (x, i) =>
            {
                int center = x.Map.At(12, 8);
                return i == 0 ? center : i == 4 ? Ring(x, center, 2, 5) : Ring(x, center, 2, i - 1);
            });
            var dest = w.Camps[0]; var c = w.Camps[4]; var sources = new[] { w.Camps[1], w.Camps[2], w.Camps[3] };
            foreach (var camp in w.Camps) { MakeNomadic(camp); foreach (var o in w.Camps) if (o != camp) Remember(w, camp, o); }
            foreach (var s in sources) { s.FoodNeedsMetRatio = 8; s.Population = 100; }
            dest.FoodNeedsMetRatio = 8 * ratio; dest.Population = 100; c.Population = 100;
            double q = Quality(w, sources[0], 100);
            c.FoodNeedsMetRatio = 1; c.FoodNeedsMetRatio = (q + .20) / Quality(w, c, 100);   // a better home than the sources' own, but less than the destination
            if (Quality(w, dest, 100) - q < .30) continue;
            // Independent expectation by linear scan, in canonical (source id) order.
            var sizes = sources.Select(s => Immigration.Size(w.Config, 100, Immigration.Utility(w.Config, q, Quality(w, dest, 100), w.Map.Distance(s.Tile, dest.Tile), s, dest))).ToArray();
            int inflow = 0; var expected = new List<int>();
            foreach (int size in sizes)
            {
                int m = 0; for (int k = size; k >= 1; k--) if (Quality(w, dest, 100 + inflow + k) >= q + w.Config.ImmigrationDeadband) { m = k; break; }
                expected.Add(m); inflow += m;
            }
            if (!(expected.Any(m => m == 0) && expected.Any(m => m > 0) && expected.Zip(sizes, (m, s) => m < s).Any(x => x))) continue;
            var before = w.Camps.ToDictionary(x => x.Id, x => x.Population);
            Pass(w); var r = lastRecord;
            var actual = sources.Select(s => Flow(r, s.Id)?.Migrants ?? 0).ToArray();
            Check(actual.SequenceEqual(expected), "the destination accepts in source-id order until its post-inflow QoL would fall below Q_i + theta (" + string.Join(",", actual) + " vs " + string.Join(",", expected) + ")");
            Check(dest.Population == before[dest.Id] + actual.Sum() && sources.Zip(actual, (s, m) => s.Population == before[s.Id] - m).All(x => x), "accepted migrants move, the excess stays home");
            int rejected = actual.ToList().FindIndex(m => m == 0); var second = Immigration.Utility(w.Config, q, Quality(w, c, 100), w.Map.Distance(sources[rejected].Tile, c.Tile), sources[rejected], c);
            Check(Immigration.Size(w.Config, 100, second) > 0, "fixture: the rejected source had a valid second-best camp");
            Check(c.Population == before[c.Id] && lastMetrics.ImmigrationPartial >= 1 && lastMetrics.ImmigrationAccepted >= 1 && lastMetrics.ImmigrationMigrants == actual.Sum(), "a rejected or partial source is not rerouted to the second-best camp");
            Check(Flow(r, sources[actual.ToList().FindIndex(m => m == 0)].Id) == null, "a fully rejected source commits no flow");
            Check(r.Migrations.Select(m => m.SourceCamp).SequenceEqual(r.Migrations.Select(m => m.SourceCamp).OrderBy(x => x)), "flows into one destination are recorded in source id order");
            Check(r.Migrations.All(m => m.PostInflowQoL >= m.SourceQoL + w.Config.ImmigrationDeadband - 1e-12), "every committed flow keeps the destination at least theta above the source");
            return;
        }
        throw new Exception("Immigration: no fixture produced a partially accepted source");
    }

    // Immigration runs first; its sources and arrivals emit no fission intent.
    private static void Ordering()
    {
        WorldState Make(bool immigration)
        {
            var w = WorldAt(3, (x, i) => i == 0 ? x.Map.At(6, 4) : i == 1 ? Ring(x, x.Map.At(6, 4), 2) : x.Map.At(18, 12));
            w.Config.ImmigrationEnabled = immigration; w.Config.FissionEnabled = true; w.Config.FissionGainThreshold = w.Config.FissionGainDiscourage = w.Config.FissionGainEncourage = 0; w.Config.FissionMinimumDaughter = 12;
            foreach (var c in w.Camps) { MakeNomadic(c); c.Population = 100; foreach (int t in w.Map.Radius(c.Tile, 5)) ContactKnowledge.MemoryOf(w, c.People).Explored.Add(t); }
            w.Camps[0].Population = 300; w.Camps[1].Population = 60;
            return w;
        }
        var control = Make(false); var cm = new ResolutionMetrics(); var cr = new TurnRecord { Turn = control.Turn };
        foreach (var c in control.Camps) cr.Demographics.Add(new DemographicReport { Camp = c.Id });
        Fission.Resolve(control, cr, new PhaseExecution(), cm);
        Check(cr.Fissions.Count == 3, "control: with immigration off every pressured camp founds a settlement (" + cr.Fissions.Count + ")");
        var w = Make(true); var record = new TurnRecord { Turn = w.Turn }; var metrics = new ResolutionMetrics();
        foreach (var c in w.Camps) record.Demographics.Add(new DemographicReport { Camp = c.Id });
        var taken = Immigration.Resolve(w, record, new PhaseExecution(), metrics);
        Check(record.Migrations.Count == 1 && record.Migrations[0].SourceCamp == w.Camps[0].Id && record.Migrations[0].DestinationCamp == w.Camps[1].Id, "the crowded camp sends people to the smaller neighbour");
        Check(taken.SetEquals(new[] { w.Camps[0].Id, w.Camps[1].Id }), "the source and the destination are the participants");
        Fission.Resolve(w, record, new PhaseExecution(), metrics, taken);
        Check(record.Fissions.Count == 1 && record.Fissions[0].ParentCamp == w.Camps[2].Id, "only the uninvolved camp fissions: the source emits no fission intent and arrivals cannot fission");
        Check(w.Camps.Count == 4, "no settlement was founded by the source or the destination");
        // Through the real resolver: immigration precedes fission in the same Close phase.
        var real = Make(true); new TurnResolver().Resolve(real, new Dictionary<int, List<Order>>());
        var h = real.History.Last();
        Check(h.Migrations.Count >= 1 && h.Fissions.All(f => f.ParentCamp != h.Migrations[0].SourceCamp && f.ParentCamp != h.Migrations[0].DestinationCamp), "the resolver runs immigration before fission and excludes its participants");
        // Fission alone and immigration alone each run without the other.
        var only = Make(true); only.Config.FissionEnabled = false; new TurnResolver().Resolve(only, new Dictionary<int, List<Order>>());
        Check(only.History.Last().Migrations.Count >= 1 && only.History.Last().Fissions.Count == 0, "immigration runs with fission disabled");
    }

    // No same-turn multi-hop: A -> B -> C. B both receives from A and sends to C; A's migrants cannot continue.
    private static void MultiHop()
    {
        var w = WorldAt(3, (x, i) => i == 0 ? x.Map.At(6, 4) : i == 1 ? Ring(x, x.Map.At(6, 4), 2) : Ring(x, Ring(x, x.Map.At(6, 4), 2), 2, 3, x.Map.At(6, 4)));
        w.Camps[0].Population = 400; w.Camps[1].Population = 200; w.Camps[2].Population = 40;
        Check(w.Map.Distance(w.Camps[0].Tile, w.Camps[2].Tile) > 2, "fixture: A does not see C");
        var before = w.Camps.ToDictionary(c => c.Id, c => c.Population);
        Pass(w); var r = lastRecord; var ab = Flow(r, w.Camps[0].Id); var bc = Flow(r, w.Camps[1].Id);
        Check(ab != null && bc != null && ab.DestinationCamp == w.Camps[1].Id && bc.DestinationCamp == w.Camps[2].Id, "A sends to B while B sends to C");
        Check(w.Camps[2].Population == before[w.Camps[2].Id] + bc.Migrants && w.Camps[1].Population == before[w.Camps[1].Id] - bc.Migrants + ab.Migrants, "B's arrivals stay in B; only B's own residents travel on");
        Check(bc.SourceBefore == before[w.Camps[1].Id] && bc.Migrants <= (int)(before[w.Camps[1].Id] * .1), "B's group is sized from its frozen residents, not including A's arrivals");
        Check(r.Migrations.Count == 2 && r.Migrations.All(m => m.SourceCamp != m.DestinationCamp), "each source emits one flow");
        // A second resolution in the same turn state can move people again only by being a fresh turn: the pass itself never iterates.
        Check(lastMetrics.ImmigrationIntents == 2, "two intents, evaluated once");
    }

    private static void Conservation()
    {
        var w = WorldAt(4, (x, i) => i == 0 ? x.Map.At(10, 8) : Ring(x, x.Map.At(10, 8), 2, i - 1)); var s = w.Camps[0];
        foreach (var c in w.Camps) { MakeNomadic(c); c.Workforce.Assign(WorkforceRoles.Herder, 7, c.Population); }
        s.Population = 300; s.Workforce.Assign(WorkforceRoles.Farmer, 40, 300); s.PlantFood = 9000.5; s.AnimalFood = 2345.25; s.AquaticFood = 777.125;
        w.Camps[1].FoodNeedsMetRatio = 1.6; w.Camps[2].FoodNeedsMetRatio = .4; w.Camps[3].FoodNeedsMetRatio = .4;
        var herd = new Herd { Id = w.NextId++, Tile = s.Tile, Animals = 50, Capacity = 100, DomesticCamp = s.Id, OwnerSociety = s.SocietyId, Species = "goat" }; w.Herds.Add(herd);
        double Sum(Func<Camp, double> f) => w.Camps.Sum(f);
        var inv = typeof(CampInventory).GetFields().Where(f => f.FieldType == typeof(double)).ToArray();
        var snapshot = w.Camps.ToDictionary(c => c.Id, c => new { c.Population, c.PlantFood, c.AnimalFood, c.AquaticFood, c.GrowthRemainder, c.DeathRemainder, Inv = inv.Select(f => (double)f.GetValue(c.Inventory)).ToArray(), Roles = c.Workforce.Assignments.ToDictionary(a => a.Role, a => a.Population) });
        double p0 = Sum(c => c.PlantFood), a0 = Sum(c => c.AnimalFood), q0 = Sum(c => c.AquaticFood); int people = w.Camps.Sum(c => c.Population);
        Pass(w); var f0 = Flow(lastRecord, s.Id);
        Check(f0 != null && f0.Migrants > 0, "fixture: a flow happens");
        int m = f0.Migrants; var d = w.Camps.Single(c => c.Id == f0.DestinationCamp);
        var flows = lastRecord.Migrations;
        Check(flows.Count >= 2 && w.Camps.Sum(c => c.Population) == people, "people are conserved exactly over several simultaneous flows");
        foreach (var c in w.Camps)
        {
            int outP = flows.Where(x => x.SourceCamp == c.Id).Sum(x => x.Migrants), inP = flows.Where(x => x.DestinationCamp == c.Id).Sum(x => x.Migrants);
            Check(c.Population == snapshot[c.Id].Population - outP + inP, "camp " + c.Id + " population is before - emigrants + immigrants");
            Near(c.PlantFood, snapshot[c.Id].PlantFood - flows.Where(x => x.SourceCamp == c.Id).Sum(x => x.Plant) + flows.Where(x => x.DestinationCamp == c.Id).Sum(x => x.Plant), "camp plant food is before - carried out + carried in");
            Near(c.AnimalFood, snapshot[c.Id].AnimalFood - flows.Where(x => x.SourceCamp == c.Id).Sum(x => x.Animal) + flows.Where(x => x.DestinationCamp == c.Id).Sum(x => x.Animal), "camp animal food is before - carried out + carried in");
            Near(c.AquaticFood, snapshot[c.Id].AquaticFood - flows.Where(x => x.SourceCamp == c.Id).Sum(x => x.Aquatic) + flows.Where(x => x.DestinationCamp == c.Id).Sum(x => x.Aquatic), "camp aquatic food is before - carried out + carried in");
        }
        Near(Sum(c => c.PlantFood), p0, "plant food is conserved"); Near(Sum(c => c.AnimalFood), a0, "animal food is conserved"); Near(Sum(c => c.AquaticFood), q0, "aquatic food is conserved");
        double f = m / 300.0;
        Near(f0.Plant, 9000.5 * f, "migrants carry their per-capita plant food"); Near(f0.Animal, 2345.25 * f, "migrants carry their per-capita animal food"); Near(f0.Aquatic, 777.125 * f, "migrants carry their per-capita aquatic food");
        Check(w.Camps.All(c => inv.Select(x => (double)x.GetValue(c.Inventory)).SequenceEqual(snapshot[c.Id].Inv)), "no tools, housing, fabric or other goods move");
        Check(herd.Animals == 50 && herd.DomesticCamp == s.Id && w.Herds.Count == 1, "no herds move");
        Check(w.Camps.All(c => c.GrowthRemainder == snapshot[c.Id].GrowthRemainder && c.DeathRemainder == snapshot[c.Id].DeathRemainder), "fractional demographic progress stays with its camp");
        Check(w.Camps.All(c => c.Workforce.Assigned <= c.Population && c.Workforce.Assignments.All(a => a.Population >= 0)), "workforce ledgers stay within residents");
        int rolesBefore = snapshot[s.Id].Roles.Values.Sum();
        Check(s.Workforce.Assigned < rolesBefore && s.Workforce.Assigned >= rolesBefore - m && s.Workforce.Assignments.Single(a => a.Role == WorkforceRoles.Farmer).Population < 40, "emigrants leave the source's roles in proportion");
        Check(w.Camps.Where(c => flows.All(x => x.SourceCamp != c.Id)).All(c => c.Workforce.Assigned == snapshot[c.Id].Roles.Values.Sum()), "arrivals join a pure destination as gatherers, leaving its roles untouched");
        Check(w.Societies.Count == 4 && flows.All(x => x.SourceSociety != x.DestinationSociety), "migrants join the destination's Society; no Society is created");
        var report = lastRecord.Demographics.Single(x => x.Camp == s.Id);
        Check(report.Emigrated == m && report.Explanation.Contains(m + " then moved"), "the demographic explanation carries the departure");
        var dreport = lastRecord.Demographics.Single(x => x.Camp == d.Id); int arrivals = flows.Where(x => x.DestinationCamp == d.Id).Sum(x => x.Migrants);
        Check(dreport.Immigrated == arrivals && dreport.Explanation.Contains(arrivals + " more arrived"), "the demographic explanation carries the arrivals");
    }

    private static void Records()
    {
        var w = WorldAt(3, (x, i) => i == 0 ? x.Map.At(10, 8) : Ring(x, x.Map.At(10, 8), 2, i - 1)); foreach (var c in w.Camps) MakeNomadic(c);
        w.Camps[0].Population = 300; w.Camps[1].FoodNeedsMetRatio = 1.5; w.Camps[2].FoodNeedsMetRatio = 1.5;
        foreach (var c in w.Camps) foreach (var o in w.Camps) if (o != c) Remember(w, c, o);
        // Kin: the destination Society is the source's daughter.
        w.Societies[1].ParentSociety = w.Societies[0].Id; w.Societies[1].KinAffinity = .9;
        Pass(w); var r = lastRecord; var flow = Flow(r, w.Camps[0].Id);
        Check(flow != null && r.Events.Any(e => e.Contains("sent " + flow.Migrants)), "a journal line is written");
        Check(flow.Kin == (flow.DestinationCamp == w.Camps[1].Id) && flow.SourceSociety == w.Camps[0].SocietyId && flow.DestinationSociety == w.Camps.Single(c => c.Id == flow.DestinationCamp).SocietyId && flow.Turn == w.Turn, "the report names the Societies and marks kin flows");
        Check(flow.Plant > 0 && flow.Animal > 0 && flow.Aquatic > 0 && flow.Plant <= 6000 && flow.Migrants > 0, "the carried food is reported per type");
        var sourceMemory = w.Camps[0].People.Memory; var destination = w.Camps.Single(c => c.Id == flow.DestinationCamp);
        Check(sourceMemory.Find(destination.Id).Source == ContactSource.Migration && sourceMemory.Find(destination.Id).Turn == w.Turn, "the source Society records the destination contact as Migration");
        Check(destination.People.Memory.Find(w.Camps[0].Id).Source == ContactSource.Migration && destination.People.Memory.Find(w.Camps[0].Id).Tile == w.Camps[0].Tile, "the destination Society records the source contact as Migration");
        // Paleolithic Societies remember nothing, so no contact is created for them.
        var p = WorldAt(2, (x, i) => i == 0 ? x.Map.At(10, 8) : Ring(x, x.Map.At(10, 8), 2)); Gap(p, p.Camps[0], p.Camps[1], 1); Pass(p);
        Check(lastRecord.Migrations.Count == 1 && p.Camps.All(c => c.People.Memory == null), "Paleolithic Societies record no contact");
        // Serialized round trip of the report.
        var serializer = new DataContractSerializer(typeof(MigrationReport)); using (var stream = new MemoryStream())
        { serializer.WriteObject(stream, flow); stream.Position = 0; var back = (MigrationReport)serializer.ReadObject(stream); Check(back.Migrants == flow.Migrants && back.DestinationCamp == flow.DestinationCamp && back.Kin == flow.Kin, "a migration report round trips"); }
        // Work counts are bounded by the intents, not by the number of camps squared.
        Check(lastMetrics.ImmigrationCampsEvaluated >= 1 && lastMetrics.ImmigrationCandidatesScored >= 1, "work counters are recorded");
    }

    // Eight camps in two clusters with different headcounts, several turns, scalar versus reversed four-worker replay with reordered collections.
    private static WorldState Cluster()
    {
        var w = WorldAt(8, (x, i) => { int center = i < 4 ? x.Map.At(6, 5) : x.Map.At(16, 11); return i % 4 == 0 ? center : Ring(x, center, 2, (i % 4) - 1 + 2 * (i % 4)); });
        for (int i = 0; i < 8; i++) { var c = w.Camps[i]; MakeNomadic(c); c.Population = new[] { 320, 60, 150, 90, 280, 70, 210, 40 }[i]; c.PlantFood = 40000; c.RudimentaryExpectation = 0; foreach (var o in w.Camps) if (o != c) Remember(w, c, o); }
        w.Turn = 1; w.Days = 0; foreach (var t in w.Tiles) t.LastEcologyUpdateDays = 0;
        return w;
    }
    private static void Advance(WorldState w, TurnResolver r) => r.Resolve(w, new Dictionary<int, List<Order>>());
    private static void Replay()
    {
        var worlds = new[] { Cluster(), Cluster() }; worlds[1].Camps.Reverse(); worlds[1].Societies.Reverse();
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 2, true, 1) }; int flows = 0, migrants = 0;
        for (int t = 0; t < 5; t++)
        {
            for (int i = 0; i < 2; i++) Advance(worlds[i], new TurnResolver(workers[i]));
            Check(SaveCodec.Fingerprint(worlds[0]) == SaveCodec.Fingerprint(worlds[1]), "scalar and reversed 4-worker replay with reordered collections agree after turn " + t);
            string Canon(WorldState x) => string.Join(";", x.History.Last().Migrations.Select(m => m.SourceCamp + ">" + m.DestinationCamp + ":" + m.Migrants));
            Check(Canon(worlds[0]) == Canon(worlds[1]), "the canonical migration record is identical after turn " + t);
            var migrations = worlds[0].History.Last().Migrations;
            flows += migrations.Count; migrants += migrations.Sum(m => m.Migrants);
            TurnResolver.Invariants(worlds[0]);
        }
        Check(flows >= 4 && migrants >= 20, "the replay exercises repeated migration (" + flows + " flows, " + migrants + " people)");
        Check(worlds[0].Camps.Sum(worlds[0].LinkedPopulation) == 1220 + worlds[0].History.Sum(h => h.Growth - h.Deaths), "migration only moves people: the ledger changes by births and deaths alone over five turns");
        // Canonical order of one pass: destination id, then source id.
        var o = Cluster(); Pass(o); var ms = lastRecord.Migrations;
        Check(ms.Count >= 2 && ms.Select(m => m.DestinationCamp * 1000 + m.SourceCamp).SequenceEqual(ms.Select(m => m.DestinationCamp * 1000 + m.SourceCamp).OrderBy(x => x)), "flows are recorded by destination then source camp id");
        // Real generated worlds replay identically too, with immigration and fission both enabled.
        var cfg = SimulationConfig.Small(6); cfg.ImmigrationEnabled = true; cfg.FissionEnabled = true; cfg.FissionGainThreshold = cfg.FissionGainDiscourage = cfg.FissionGainEncourage = 0;
        var g1 = new WorldGenerator().Generate(3, cfg); var g2 = new WorldGenerator().Generate(3, cfg); g2.Camps.Reverse(); g2.Societies.Reverse();
        var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder();
        for (int t = 0; t < 5 && !g1.Finished; t++)
        {
            var orders1 = g1.Camps.GroupBy(c => c.Player).ToDictionary(g => g.Key, g => policy.Decide(builder.Build(g1, g.Key), g.First().Mandate).Orders);
            var orders2 = g2.Camps.GroupBy(c => c.Player).ToDictionary(g => g.Key, g => policy.Decide(builder.Build(g2, g.Key), g.First().Mandate).Orders);
            new TurnResolver(workers[0]).Resolve(g1, orders1); new TurnResolver(workers[1]).Resolve(g2, orders2);
            Check(SaveCodec.Fingerprint(g1) == SaveCodec.Fingerprint(g2), "a generated world replays identically across workers and order after turn " + t);
        }
    }

    private static void Persistence()
    {
        var a = Cluster(); var resolver = new TurnResolver(); Advance(a, resolver);
        Check(a.History.Last().Migrations.Count > 0, "fixture: the saved world has a migration in its history");
        var b = SaveCodec.Load(SaveCodec.Save(a));
        Check(SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(b), "the fingerprint survives a round trip with migrations");
        Check(b.Config.ImmigrationEnabled && b.History.Last().Migrations.Count == a.History.Last().Migrations.Count && b.History.Last().Migrations[0].Migrants == a.History.Last().Migrations[0].Migrants, "the migration history and switch round trip");
        Check(b.History.Last().Demographics.Any(d => d.Emigrated > 0) && b.History.Last().Demographics.Any(d => d.Immigrated > 0), "the demographic report lines round trip");
        for (int t = 0; t < 4; t++) { Advance(a, resolver); Advance(b, new TurnResolver()); Check(SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(b), "continued run equals the uninterrupted run after turn " + t); }
        // Older saves: no immigration config, no migrations in the history, no demographic lines.
        var old = Cluster(); old.Config.ImmigrationEnabled = false; Advance(old, new TurnResolver());
        var loaded = SaveCodec.Load(SaveCodec.Save(old));
        Check(!loaded.Config.ImmigrationEnabled && loaded.History.All(h => h.Migrations != null && h.Migrations.Count == 0), "a world saved with immigration off loads disabled with empty migration histories");
        var dr = new DataContractSerializer(typeof(DemographicReport)); string xml;
        using (var stream = new MemoryStream()) { dr.WriteObject(stream, new DemographicReport { Camp = 1, Emigrated = 3, Immigrated = 2, LimitingFactor = "x" }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        string legacy = Regex.Replace(xml, "<(Emigrated|Immigrated)>[^<]*</\\1>", "");
        Check(legacy != xml && !legacy.Contains("migrated"), "legacy demographic fixture omits the fields");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) { var d = (DemographicReport)dr.ReadObject(stream); Check(d.Emigrated == 0 && d.Immigrated == 0 && !d.Explanation.Contains("moved"), "an older demographic report has no moves"); }
        var tr = new DataContractSerializer(typeof(TurnRecord)); var record = new TurnRecord { Turn = 4 }; record.Migrations.Add(new MigrationReport { SourceCamp = 1, DestinationCamp = 2, Migrants = 5 });
        using (var stream = new MemoryStream()) { tr.WriteObject(stream, record); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        legacy = Regex.Replace(xml, "<Migrations.*?</Migrations>", "", RegexOptions.Singleline);
        Check(legacy != xml && !legacy.Contains("Migrations"), "legacy turn record fixture omits the journal");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) { var t = (TurnRecord)tr.ReadObject(stream); Check(t.Turn == 4, "an older turn record loads"); }
        // An older contact source enum value still loads; the new value round trips through the contact memory.
        var mem = new DataContractSerializer(typeof(ContactMemory)); var memory = new ContactMemory(24); memory.Upsert(3, 1, 10, 2, ContactSource.Migration);
        using (var stream = new MemoryStream()) { mem.WriteObject(stream, memory); stream.Position = 0; Check(((ContactMemory)mem.ReadObject(stream)).Find(3).Source == ContactSource.Migration, "a Migration contact round trips"); }
    }
}
