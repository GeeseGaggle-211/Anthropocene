using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// Trade between known settlements (WP7-WP8, Docs/Trade.md): T0 valuation, the bounded same-turn handshake, settlement
// conservation, planning injection, determinism and persistence. Owns its worlds and counters (lanes run checks concurrently).
internal static class TradeChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Trade: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-9) => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)), message + " (" + a + " vs " + b + ")");

    // A uniform fertile 24x16 fixture. Camp 0 (the human) sits far from the traders; camps 1.. are AI villages of distinct
    // Societies that know each other. Every trader can build (Cultivation + Construction) and has no shelter, so a Housing
    // Material unit is worth the Housing unit it completes.
    private static WorldState World(params (int x, int y, double cloth, double material)[] traders)
    {
        var cfg = SimulationConfig.Small(1); cfg.TradeEnabled = true; cfg.TradeFoodEnabled = false; cfg.Width = 24; cfg.Height = 16; cfg.AnnualHumanGrowthRate = 0; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0; cfg.ContactExpiryTurns = 50;
        var w = new WorldState { Seed = 202, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 8000, Food = 1000 };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = t;
        }
        var human = new Camp { Id = w.NextId++, Player = 0, Tile = w.Map.At(20, 13), Population = 60, PlantFood = 6000, AnimalFood = 1000, RudimentaryExpectation = .5 };
        w.Camps.Add(human);
        for (int i = 0; i < traders.Length; i++)
        {
            var (x, y, cloth, material) = traders[i];
            var c = new Camp { Id = w.NextId++, Player = i + 1, Tile = w.Map.At(x, y), Population = 80, PlantFood = 8000, AnimalFood = 2000, RudimentaryExpectation = .5, StationaryYears = 40, StationaryTurns = 2, IsVillage = true };
            c.Inventory.Cloth = cloth; c.Inventory.HousingMaterial = material;
            w.Camps.Add(c);
        }
        w.Config.Players = traders.Length + 1; w.PlayerCount = traders.Length + 1; w.Turn = 3; w.Days = 1000;
        Ownership.Bind(w);
        foreach (var c in w.Camps) { Agricultural(c); c.People.NeolithicTechnologies.Add(NeolithicTech.Construction); }
        foreach (var c in w.Camps) foreach (var o in w.Camps) if (o != c) Remember(w, c, o);
        foreach (var t in w.Tiles) t.LastEcologyUpdateDays = w.Days;
        return w;
    }
    private static double V(Trade.Valuation v, TradeGood g) => ((ExponentialCurve)v.Curve(g)).V;
    private static double N(Trade.Valuation v, TradeGood g) => ((ExponentialCurve)v.Curve(g)).N;
    private static void Agricultural(Camp c) { c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic; }
    private static void Remember(WorldState w, Camp observer, Camp other)
        => ContactKnowledge.MemoryOf(w, observer.People).Upsert(other.Id, other.Player, other.Tile, w.Turn, ContactSource.Sighting);
    private static Camp CampOf(WorldState w, int player) => w.Camps.Single(c => c.Player == player);
    // Cloth-rich camp 1 next to Housing-Material-rich camp 2.
    private static WorldState Complementary() => World((6, 6, 400, 0), (8, 6, 0, 60));
    private static void Advance(WorldState w, TurnResolver r) => r.Resolve(w, new Dictionary<int, List<Order>>());

    public static int Run()
    {
        checks = 0; Config(); Valuation(); Handshake(); Eligibility(); Kin(); Settlement(); Planning(); Replay(); Persistence(); Registry(); Food();
        Console.WriteLine("PASS: " + checks + " trade assertions"); return checks;
    }

    private static string FindConfig()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string path = Path.Combine(d.FullName, "Assets", "_Anthropocene", "Resources", "OpeningConfig.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("OpeningConfig.json");
    }

    private static void Config()
    {
        var c = new SimulationConfig();
        Check(c.TradeFoodEnabled && !c.TradeFoodSafeOnly, "food is in the tradeable set by default with welfare-valued growth food");
        Check(!c.TradeEnabled && c.TradeRange == 6 && c.TradeRequisitionsPerCamp == 3 && c.TradeMargin == .10 && c.TradeDistanceCostPerHex == .01, "trade defaults are off with the specified levers");
        foreach (Action<SimulationConfig> bad in new Action<SimulationConfig>[] { x => x.TradeRange = 0, x => x.TradeRange = 21, x => x.TradeRequisitionsPerCamp = 0, x => x.TradeRequisitionsPerCamp = 11,
            x => x.TradeMargin = -.1, x => x.TradeMargin = double.NaN, x => x.TradeDistanceCostPerHex = -.01, x => x.TradeDistanceCostPerHex = 1.5 })
        {
            var x = new SimulationConfig(); bad(x); bool rejected = false; try { x.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "an invalid trade lever is rejected");
        }
        var serializer = new DataContractSerializer(typeof(SimulationConfig)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { TradeEnabled = true, TradeRange = 4 }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<TradeEnabled>true</TradeEnabled>") && xml.Contains("<TradeRange>4</TradeRange>"), "trade levers are serialized");
        string legacy = Regex.Replace(xml, "<Trade(Enabled|Range|RequisitionsPerCamp|Margin|DistanceCostPerHex|FoodEnabled|FoodSafeOnly)>[^<]*</Trade\\1>", "");
        Check(legacy != xml && !legacy.Contains("<TradeEnabled") && !legacy.Contains("<TradeRange"), "legacy fixture omits every trade field");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy)))
        {
            var old = (SimulationConfig)serializer.ReadObject(stream);
            Check(old.TradeFoodEnabled && !old.TradeFoodSafeOnly, "an older config loads the food levers at their defaults");
            Check(!old.TradeEnabled && old.TradeRange == 6 && old.TradeRequisitionsPerCamp == 3 && old.TradeMargin == .10 && old.TradeDistanceCostPerHex == .01, "an older config loads with trade disabled and the default levers");
            old.Validate(); checks++;
        }
        string json = File.ReadAllText(FindConfig());
        foreach (var pair in new[] { ("TradeEnabled", "false"), ("TradeRange", "6"), ("TradeRequisitionsPerCamp", "3"), ("TradeMargin", "0.1"), ("TradeDistanceCostPerHex", "0.01"), ("TradeFoodEnabled", "true"), ("TradeFoodSafeOnly", "false") })
        {
            var m = Regex.Match(json, "\"" + pair.Item1 + "\":\\s*([0-9.a-z]+)");
            Check(m.Success && m.Groups[1].Value == pair.Item2, "OpeningConfig.json carries " + pair.Item1);
        }
        // Disabled: no plan, no record lines, no activity.
        var w = Complementary(); w.Config.TradeEnabled = false;
        Check(ReferenceEquals(Trade.Plan(w), TradePlan.Empty) && TradePlan.Empty.Agreements.Count == 0, "a disabled handshake returns the empty plan");
        var before = w.Camps.Select(x => (x.Inventory.Cloth, x.Inventory.HousingMaterial)).ToArray();
        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
        Check(prepared.Trade.Agreements.Count == 0, "a disabled turn prepares no trade");
        var record = new TurnRecord(); Trade.Settle(w, prepared.Trade, record);
        Check(record.Trades.Count == 0 && record.TradeActivity == null && w.Camps.Select(x => (x.Inventory.Cloth, x.Inventory.HousingMaterial)).SequenceEqual(before), "a disabled settlement changes nothing and reports nothing");
    }

    // Independent closed form of the integrated value V N / k (e^{-k a/N} - e^{-k b/N}).
    private static double Integral(double v, double n, double k, double a, double b) => v * n / k * (Math.Exp(-k * a / n) - Math.Exp(-k * b / n));
    private static void Valuation()
    {
        var w = Complementary(); var cfg = w.Config; var n = cfg.Neolithic; var c = CampOf(w, 2); var v = Trade.Value(w, c);
        double years = cfg.TurnDays / cfg.DaysPerYear;
        double e = c.RudimentaryExpectation, b = c.FoodNeedsMetRatio * c.WaterNeedsMetRatio, s = NeolithicProduction.SocialCohesion(w, c, c.Population);
        Near(V(v, TradeGood.Cloth), n.RudimentaryWeightClothing * e * b * s * (n.ReferenceTurnYears / n.FabricConsumptionPerPersonTurn), "the clothing curve's first unit is ClaimValuation's");
        Near(N(v, TradeGood.Cloth), c.Population * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears, "the clothing need is this turn's requirement");
        Near(v.Position(TradeGood.Cloth), c.Inventory.Cloth + c.Inventory.AnimalFabric, "the clothing position counts Cloth and Animal Fabric");
        double life = 1 / (-Math.Log(1 - n.HousingDecayPerTurn) / n.ReferenceTurnYears + n.DiscountRatePerYear);
        Near(V(v, TradeGood.HousingMaterial), n.HousingPeoplePerUnit * n.RudimentaryWeightHousing * e * b * s * life, "a Housing Material unit is worth the Housing unit it completes");
        Near(N(v, TradeGood.HousingMaterial), NeolithicProduction.Demand(w, c, CraftGood.Housing, years, c.Population), "with no shelter the housing need is the Housing demand");
        Check(N(v, TradeGood.HousingMaterial) > 0 && V(v, TradeGood.HousingMaterial) > 0, "fixture: the camp needs housing and can build");
        // Integrated value: closed form, additive, monotone with diminishing increments.
        double k = n.ValueCurveSteepness;
        Near(v.Integrated(TradeGood.HousingMaterial, 2, 7), Integral(V(v, TradeGood.HousingMaterial), N(v, TradeGood.HousingMaterial), k, 2, 7), "integrated housing value matches the closed form");
        Near(v.Integrated(TradeGood.Cloth, 0, 30) + v.Integrated(TradeGood.Cloth, 30, 50), v.Integrated(TradeGood.Cloth, 0, 50), "integrated value is additive");
        double last = double.PositiveInfinity, total = 0;
        for (int i = 0; i < 20; i++)
        {
            double step = v.Integrated(TradeGood.HousingMaterial, i, i + 1); total += step;
            Check(step > 0 && step < last, "each further unit adds less (unit " + i + ")"); last = step;
            Check(Math.Abs(total - v.Integrated(TradeGood.HousingMaterial, 0, i + 1)) < 1e-9 * Math.Max(1, total), "the gain is monotone and cumulative");
        }
        Near(v.Gain(TradeGood.HousingMaterial, 10), v.Integrated(TradeGood.HousingMaterial, v.Position(TradeGood.HousingMaterial), v.Position(TradeGood.HousingMaterial) + 10), "gain integrates above the current position");
        Near(v.Loss(TradeGood.HousingMaterial, 10), v.Integrated(TradeGood.HousingMaterial, v.Position(TradeGood.HousingMaterial) - 10, v.Position(TradeGood.HousingMaterial)), "loss integrates below the current position");
        // A camp without Construction (or without agriculture) values Housing Material at nothing.
        c.People.NeolithicTechnologies.Remove(NeolithicTech.Construction);
        var noBuild = Trade.Value(w, c);
        Check(!(V(noBuild, TradeGood.HousingMaterial) > 0) && noBuild.Gain(TradeGood.HousingMaterial, 5) == 0 && noBuild.Marginal(TradeGood.HousingMaterial, 0) == 0, "Housing Material is worthless to a camp that cannot build");
        // The requester's best 1:1 quantity equalizes the two marginal values, within its give stock.
        var rw = Complementary(); var r = Trade.Value(rw, CampOf(rw, 1));
        double q = Trade.BestQuantity(r, TradeGood.HousingMaterial, TradeGood.Cloth);
        Check(q > 0 && q < r.Held[(int)TradeGood.Cloth], "the best quantity is interior");
        Near(r.Marginal(TradeGood.HousingMaterial, r.Position(TradeGood.HousingMaterial) + q), r.Marginal(TradeGood.Cloth, r.Position(TradeGood.Cloth) - q), "at the best quantity the marginal want equals the marginal give", 1e-6);
        Check(Trade.BestQuantity(r, TradeGood.Cloth, TradeGood.HousingMaterial) == 0, "a camp with no Housing Material cannot offer it");
    }

    private static void Handshake()
    {
        var w = Complementary(); var cfg = w.Config; var plan = Trade.Plan(w);
        Check(plan.Summary.Eligible == 2 && plan.Summary.Requisitions == 2 && plan.Agreements.Count == 2 && plan.Summary.Honoured + plan.Summary.Countered + plan.Summary.Ignored == plan.Summary.Requisitions, "complementary camps both trade and the summary balances");
        var cloth = CampOf(w, 1).Id; var material = CampOf(w, 2).Id;
        foreach (var a in plan.Agreements)
        {
            Check(a.RequesterGain > 0 && a.ResponderGain > 0 && a.WantQuantity > 0 && a.GiveQuantity > 0, "every agreement gains both sides");
            Near(a.GiveQuantity, a.Ratio * a.WantQuantity, "the give quantity is the ratio times the want");
            Check(a.Distance == w.Map.Distance(w.Camps.First(c => c.Id == a.Requester).Tile, w.Camps.First(c => c.Id == a.Responder).Tile), "the agreement records the hex distance");
        }
        var honoured = plan.Agreements.Single(a => !a.Countered); var countered = plan.Agreements.Single(a => a.Countered);
        Check(honoured.Requester == material && honoured.Want == TradeGood.Cloth && honoured.Ratio == 1, "the material-rich camp asks for cloth and is honoured at 1:1");
        Check(countered.Requester == cloth && countered.Want == TradeGood.HousingMaterial && countered.Ratio > 1, "the cloth-rich camp's second request is countered above 1:1");
        // Every counter sits at the responder's floor: its gain (after transport) is exactly (1 + m) of its loss.
        {
            var fresh = Complementary(); var p = Trade.Value(fresh, fresh.Camps.First(c => c.Id == countered.Responder)); var r = Trade.Value(fresh, fresh.Camps.First(c => c.Id == countered.Requester));
            // Replay the honoured agreement first (canonical order: responder IDs ascending).
            p.Apply(TradeGood.Cloth, honoured.WantQuantity); p.Apply(TradeGood.HousingMaterial, -honoured.GiveQuantity);
            r.Apply(TradeGood.Cloth, -honoured.WantQuantity); r.Apply(TradeGood.HousingMaterial, honoured.GiveQuantity);
            double transport = cfg.TradeDistanceCostPerHex * countered.Distance;
            Near(p.Gain(TradeGood.Cloth, countered.GiveQuantity) * (1 - transport), (1 + cfg.TradeMargin) * p.Loss(TradeGood.HousingMaterial, countered.WantQuantity), "the counter is the responder's floor", 1e-6);
            Check(r.Gain(TradeGood.HousingMaterial, countered.WantQuantity) * (1 - transport) >= (1 + cfg.TradeMargin) * r.Loss(TradeGood.Cloth, countered.GiveQuantity) - 1e-9, "the requester accepts the counter inside its limit");
        }
        // Deltas conserve each good and sum the agreements.
        Near(plan.Deltas.Keys.Sum(id => plan.Delta(id, TradeGood.Cloth)), 0, "cloth deltas sum to zero", 1e-9);
        Near(plan.Deltas.Keys.Sum(id => plan.Delta(id, TradeGood.HousingMaterial)), 0, "housing material deltas sum to zero", 1e-9);
        Near(plan.Delta(material, TradeGood.Cloth), plan.Agreements.Where(a => a.Requester == material && a.Want == TradeGood.Cloth).Sum(a => a.WantQuantity) + plan.Agreements.Where(a => a.Responder == material && a.Give == TradeGood.Cloth).Sum(a => a.GiveQuantity), "a camp's delta sums its agreements");
        foreach (var c in w.Camps.Where(c => plan.Changes(c.Id)))
        {
            Check(c.Inventory.Cloth + plan.Delta(c.Id, TradeGood.Cloth) >= -1e-9 && c.Inventory.HousingMaterial + plan.Delta(c.Id, TradeGood.HousingMaterial) >= -1e-9, "no camp is promised more stock than it holds");
        }
        // The plan is pure: computing it again, or after a draft, changes nothing.
        string Canon(TradePlan x) => string.Join(";", x.Agreements.Select(a => a.Requester + ">" + a.Responder + ":" + a.Want + ":" + a.WantQuantity.ToString("R") + ":" + a.GiveQuantity.ToString("R")));
        string first = Canon(plan); var stocks = w.Camps.Select(c => (c.Inventory.Cloth, c.Inventory.HousingMaterial)).ToArray();
        Check(Canon(Trade.Plan(w)) == first && w.Camps.Select(c => (c.Inventory.Cloth, c.Inventory.HousingMaterial)).SequenceEqual(stocks), "the handshake is pure");
        // Identical camps have nothing to gain.
        var same = World((6, 6, 40, 10), (8, 6, 40, 10)); var none = Trade.Plan(same);
        Check(none.Summary.Eligible == 2 && none.Agreements.Count == 0 && none.Deltas.Count == 0, "identical camps do not trade");
        // A request the responder cannot meet even at the requester's limit is ignored: here the would-be seller is short of
        // clothing itself, so giving any cloth costs it more than the material the requester can offer is worth.
        var tight = World((6, 6, 0, 2), (8, 6, 30, 0)); var t = Trade.Plan(tight);
        Check(t.Summary.Requisitions >= 1 && t.Summary.Honoured + t.Summary.Countered + t.Summary.Ignored == t.Summary.Requisitions, "the tight fixture balances its summary");
        foreach (var a in t.Agreements) Check(a.RequesterGain > 0 && a.ResponderGain > 0, "a tight agreement still gains both sides");
    }

    private static void Eligibility()
    {
        // The human player's camps never trade until the player panel exists.
        var w = World((6, 6, 400, 0), (8, 6, 0, 60));
        var human = w.Camps.Single(c => c.Player == 0); human.Tile = w.Map.At(10, 6); human.Inventory.Cloth = 400; human.Inventory.HousingMaterial = 60;
        var plan = Trade.Plan(w);
        Check(!Trade.Eligible(w, human) && plan.Agreements.All(a => a.Requester != human.Id && a.Responder != human.Id), "player 0 is never a party");
        // Out of range: a remembered partner beyond TradeRange receives no request.
        var far = World((2, 6, 400, 0), (2 + 9, 6, 0, 60));
        Check(far.Map.Distance(far.Camps[1].Tile, far.Camps[2].Tile) > far.Config.TradeRange && Trade.Plan(far).Summary.Requisitions == 0, "a partner beyond trade range is not asked");
        far.Config.TradeRange = 10; Check(Trade.Plan(far).Agreements.Count > 0, "the same partner within a larger range trades");
        // Unknown: without a remembered contact there is no request.
        var unknown = Complementary();
        foreach (var c in unknown.Camps) c.People.Memory.Contacts.Clear();
        Check(Trade.Plan(unknown).Summary.Requisitions == 0, "an unknown settlement is not asked");
        // A stale (moved) contact is not asked either.
        var moved = Complementary();
        foreach (var c in moved.Camps) foreach (var k in c.People.Memory.Contacts) k.Moved = true;
        Check(Trade.Plan(moved).Summary.Requisitions == 0, "a contact known to have moved is not asked");
        // Same Society: camps of one Society share stock decisions through their Society, not trade.
        var one = Complementary(); var a2 = CampOf(one, 1); var b2 = CampOf(one, 2); b2.Player = a2.Player; Ownership.Bind(one);
        if (b2.SocietyId == a2.SocietyId) Check(Trade.Plan(one).Summary.Requisitions == 0, "camps of one Society do not trade with each other");
        // No goods economy: a Paleolithic camp is not eligible.
        var paleo = Complementary(); var p = CampOf(paleo, 1); p.Technology = Technology.None; p.Technologies[(int)Technology.Cultivation] = false;
        Check(!Trade.Eligible(paleo, p) && Trade.Plan(paleo).Agreements.Count == 0, "a camp without a goods economy does not trade");
        // Without Construction anywhere, Housing Material is worthless, so cloth cannot buy it and nobody trades.
        var plain = Complementary(); foreach (var c in plain.Camps) c.People.NeolithicTechnologies.Remove(NeolithicTech.Construction);
        Check(Trade.Plan(plain).Agreements.Count == 0, "with nobody able to build, Housing Material does not trade");
        // K bounds the requisitions per camp, nearest first with ties by camp ID.
        var many = World((6, 6, 400, 0), (8, 6, 0, 60), (6, 8, 0, 60), (4, 6, 0, 60), (6, 4, 0, 60), (10, 6, 0, 60));
        var mp = Trade.Plan(many); int richId = CampOf(many, 1).Id;
        Check(mp.Summary.Requisitions <= many.Camps.Count(c => c.Player != 0) * many.Config.TradeRequisitionsPerCamp, "no camp sends more than K requisitions");
        many.Config.TradeRequisitionsPerCamp = 1; var one1 = Trade.Plan(many);
        Check(one1.Summary.Requisitions <= many.Camps.Count(c => c.Player != 0) && one1.Agreements.Count(a => a.Requester == richId) <= 1, "K = 1 sends one requisition per camp");
    }

    private static void Kin()
    {
        // Kin margin m (1 - a) only lowers the counter; it never changes who is asked.
        var plain = Complementary(); var kin = Complementary();
        var a = CampOf(kin, 1).People; var b = CampOf(kin, 2).People; b.ParentSociety = a.Id; b.KinAffinity = .9;
        Near(Kinship.Affinity(b, a), .9, "fixture: the daughter has affinity .9 to its parent");
        var p0 = Trade.Plan(plain); var p1 = Trade.Plan(kin);
        Check(p0.Summary.Requisitions == p1.Summary.Requisitions && p0.Agreements.Count == p1.Agreements.Count, "kinship changes no request or partner");
        var c0 = p0.Agreements.Single(x => x.Countered); var c1 = p1.Agreements.First(x => x.Requester == c0.Requester && x.Responder == c0.Responder);
        Check(c1.Affinity == .9 && c0.Affinity == 0, "agreements record the affinity");
        Check(c1.Ratio < c0.Ratio, "a kin responder counters lower (" + c1.Ratio + " < " + c0.Ratio + ")");
    }

    private static void Settlement()
    {
        // Settlement moves exactly the agreed quantities, conserves each good and records canonically.
        var w = Complementary(); var plan = Trade.Plan(w); var record = new TurnRecord { Turn = w.Turn };
        double cloth = w.Camps.Sum(c => c.Inventory.Cloth), material = w.Camps.Sum(c => c.Inventory.HousingMaterial);
        var expected = w.Camps.ToDictionary(c => c.Id, c => (c.Inventory.Cloth + plan.Delta(c.Id, TradeGood.Cloth), c.Inventory.HousingMaterial + plan.Delta(c.Id, TradeGood.HousingMaterial)));
        Trade.Settle(w, plan, record);
        Near(w.Camps.Sum(c => c.Inventory.Cloth), cloth, "settlement conserves cloth"); Near(w.Camps.Sum(c => c.Inventory.HousingMaterial), material, "settlement conserves housing material");
        foreach (var c in w.Camps) { Near(c.Inventory.Cloth, expected[c.Id].Item1, "camp " + c.Id + " receives its planned cloth"); Near(c.Inventory.HousingMaterial, expected[c.Id].Item2, "camp " + c.Id + " receives its planned material"); }
        Check(record.Trades.Count == plan.Agreements.Count && record.Trades.All(t => t.Delivered == 1) && record.TradeActivity == plan.Summary, "every agreement is recorded as fully delivered");
        Check(record.Trades.Select((t, i) => t.Requester == plan.Agreements[i].Requester && t.Responder == plan.Agreements[i].Responder).All(x => x), "records follow agreement order");
        // Each party now remembers the other as a trade contact at its current tile.
        foreach (var t in record.Trades)
        {
            var r = w.Camps.First(c => c.Id == t.Requester); var p = w.Camps.First(c => c.Id == t.Responder);
            Check(r.People.Memory.Find(p.Id)?.Source == ContactSource.Trade && p.People.Memory.Find(r.Id)?.Source == ContactSource.Trade, "both parties record the trade contact");
        }
        // A short side scales both sides of that agreement in proportion.
        var s = Complementary(); var sp = Trade.Plan(s); var first = sp.Agreements[0];
        var responder = s.Camps.First(c => c.Id == first.Responder);
        double held = first.Want == TradeGood.Cloth ? responder.Inventory.Cloth : responder.Inventory.HousingMaterial;
        if (first.Want == TradeGood.Cloth) responder.Inventory.Cloth = first.WantQuantity / 2; else responder.Inventory.HousingMaterial = first.WantQuantity / 2;
        var sr = new TurnRecord(); double sc = s.Camps.Sum(c => c.Inventory.Cloth), sm = s.Camps.Sum(c => c.Inventory.HousingMaterial);
        Trade.Settle(s, sp, sr);
        Near(sr.Trades[0].Delivered, .5, "a half-short responder delivers half"); Near(sr.Trades[0].GiveQuantity, first.GiveQuantity / 2, "the requester pays half");
        Near(s.Camps.Sum(c => c.Inventory.Cloth), sc, "short settlement conserves cloth"); Near(s.Camps.Sum(c => c.Inventory.HousingMaterial), sm, "short settlement conserves material");
        Check(s.Camps.All(c => c.Inventory.Cloth >= -1e-12 && c.Inventory.HousingMaterial >= -1e-12), "no stock goes negative");
        // A whole resolved turn: the trades settle, the turn record carries them and the resolver invariants hold.
        var full = Complementary(); var before = Trade.Plan(full); Advance(full, new TurnResolver());
        var last = full.History.Last();
        Check(last.Trades.Count == before.Agreements.Count && last.TradeActivity != null && last.TradeActivity.Requisitions == before.Summary.Requisitions, "a resolved turn records its trades");
        TurnResolver.Invariants(full); checks++;
    }

    private static void Planning()
    {
        // Prepare carries the handshake, and the planner sees each camp's post-trade stock.
        var w = Complementary(); var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
        string Canon(TradePlan x) => string.Join(";", x.Agreements.Select(a => a.Requester + ">" + a.Responder + ":" + a.Want + ":" + a.WantQuantity.ToString("R") + ":" + a.GiveQuantity.ToString("R")));
        Check(Canon(prepared.Trade) == Canon(Trade.Plan(w)) && prepared.Trade.Agreements.Count > 0, "Prepare carries the committed-boundary handshake");
        var clothCamp = CampOf(w, 1);
        Check(prepared.Needs[clothCamp.Id].Camp == clothCamp.Id, "fixture: needs are prepared for the trader");
        // Draft and Prepare agree: the draft carries the same plan, and a draft-backed Prepare equals a fresh one.
        var a = Complementary(); var b = Complementary();
        var draft = TurnPlanner.Draft(a, 0);
        Check(Canon(draft.Trade) == Canon(Trade.Plan(a)), "the draft carries the same handshake");
        var withDraft = TurnPlanner.Prepare(a, new Dictionary<int, List<Order>>(), null, null, draft);
        var fresh = TurnPlanner.Prepare(b, new Dictionary<int, List<Order>>());
        string Orders(PreparedTurn p) => string.Join("|", p.OrderReceipts);
        Check(Orders(withDraft) == Orders(fresh) && Canon(withDraft.Trade) == Canon(fresh.Trade), "a draft-backed preparation equals a fresh one");
        new TurnResolver().Resolve(a, withDraft); new TurnResolver().Resolve(b, fresh);
        Check(SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(b), "draft-backed and fresh resolutions agree");
        // Planning reads the committed world only: the committed stock is unchanged until the resolver settles.
        var c = Complementary(); var stocks = c.Camps.Select(x => (x.Inventory.Cloth, x.Inventory.HousingMaterial)).ToArray();
        TurnPlanner.Prepare(c, new Dictionary<int, List<Order>>()); TurnPlanner.Draft(c, 0);
        Check(c.Camps.Select(x => (x.Inventory.Cloth, x.Inventory.HousingMaterial)).SequenceEqual(stocks), "planning never writes committed stock");
    }

    private static WorldState Ring()
    {
        var w = World((6, 6, 300, 0), (8, 6, 0, 50), (7, 8, 150, 20), (5, 8, 20, 30), (9, 8, 0, 0), (16, 10, 200, 0), (18, 10, 0, 45));
        w.Turn = 1; w.Days = 0; foreach (var t in w.Tiles) t.LastEcologyUpdateDays = 0;
        return w;
    }
    private static void Replay()
    {
        var worlds = new[] { Ring(), Ring() }; worlds[1].Camps.Reverse(); worlds[1].Societies.Reverse();
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 2, true, 1) }; int trades = 0;
        for (int t = 0; t < 4; t++)
        {
            for (int i = 0; i < 2; i++) Advance(worlds[i], new TurnResolver(workers[i]));
            Check(SaveCodec.Fingerprint(worlds[0]) == SaveCodec.Fingerprint(worlds[1]), "scalar and reversed 4-worker replay agree after turn " + t);
            string Canon(WorldState x) => string.Join(";", x.History.Last().Trades.Select(r => r.Requester + ">" + r.Responder + ":" + r.WantQuantity.ToString("R")));
            Check(Canon(worlds[0]) == Canon(worlds[1]), "the canonical trade record is identical after turn " + t);
            trades += worlds[0].History.Last().Trades.Count;
            TurnResolver.Invariants(worlds[0]);
        }
        Check(trades >= 3, "the replay exercises repeated trade (" + trades + " trades)");
    }

    private static void Persistence()
    {
        var a = Ring(); var resolver = new TurnResolver(); Advance(a, resolver);
        Check(a.History.Last().Trades.Count > 0, "fixture: the saved world has trades in its history");
        var b = SaveCodec.Load(SaveCodec.Save(a));
        Check(SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(b), "the fingerprint survives a round trip with trades");
        var ta = a.History.Last().Trades[0]; var tb = b.History.Last().Trades[0];
        Check(b.Config.TradeEnabled && tb.Requester == ta.Requester && tb.Want == ta.Want && tb.WantQuantity == ta.WantQuantity && tb.Ratio == ta.Ratio && tb.Countered == ta.Countered
            && b.History.Last().TradeActivity.Requisitions == a.History.Last().TradeActivity.Requisitions, "the trade history and switch round trip");
        for (int t = 0; t < 3; t++) { Advance(a, resolver); Advance(b, new TurnResolver()); Check(SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(b), "continued run equals the uninterrupted run after turn " + t); }
        // Nothing pending survives: a loaded world prepares the same handshake from its committed stock.
        string Canon(TradePlan x) => string.Join(";", x.Agreements.Select(g => g.Requester + ">" + g.Responder + ":" + g.WantQuantity.ToString("R")));
        Check(Canon(Trade.Plan(a)) == Canon(Trade.Plan(b)), "the next handshake is recomputed identically after a load");
        // Older turn records have no trade journal and load with an empty list and no activity.
        var tr = new DataContractSerializer(typeof(TurnRecord)); string xml; var record = new TurnRecord { Turn = 4 };
        record.Trades.Add(new TradeRecord { Requester = 1, Responder = 2, WantQuantity = 3 }); record.TradeActivity = new TradeSummary { Requisitions = 1, Honoured = 1 };
        using (var stream = new MemoryStream()) { tr.WriteObject(stream, record); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) { var back = (TurnRecord)tr.ReadObject(stream); Check(back.Trades.Count == 1 && back.Trades[0].WantQuantity == 3 && back.TradeActivity.Honoured == 1, "a turn record with trades round trips"); }
        string legacy = Regex.Replace(Regex.Replace(xml, "<Trades>.*?</Trades>", "", RegexOptions.Singleline), "<TradeActivity>.*?</TradeActivity>", "", RegexOptions.Singleline);
        Check(legacy != xml && !legacy.Contains("<Trades>") && !legacy.Contains("<TradeActivity>"), "legacy turn record fixture omits the journal");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) { var back = (TurnRecord)tr.ReadObject(stream); Check(back.Turn == 4 && back.TradeActivity == null, "an older turn record loads with no trade activity"); }
        // A world saved with trade off loads disabled with empty journals.
        var off = Ring(); off.Config.TradeEnabled = false; Advance(off, new TurnResolver());
        var loaded = SaveCodec.Load(SaveCodec.Save(off));
        Check(!loaded.Config.TradeEnabled && loaded.History.All(h => h.Trades != null && h.Trades.Count == 0 && h.TradeActivity == null), "a world saved with trade off loads disabled with empty journals");
    }

    // The registry is data: one descriptor per TradeGood in order, curves shared by key, direction in O(goods).
    private static void Registry()
    {
        Check(Trade.Registry.Select(d => (int)d.Id).SequenceEqual(Enumerable.Range(0, Enum.GetValues(typeof(TradeGood)).Length)), "every TradeGood has exactly one descriptor, in order");
        Check(Trade.PartCount == Trade.Registry.Sum(d => d.Parts.Length) && Trade.PartIndex(TradeGood.Food, 2) == Trade.PartCount - 1, "parts are indexed contiguously");
        var w = Complementary(); w.Config.TradeFoodEnabled = true; var v = Trade.Value(w, CampOf(w, 1));
        Check(ReferenceEquals(v.Curve(TradeGood.Cloth), v.Curve(TradeGood.AnimalFabric)) && v.Position(TradeGood.Cloth) == v.Position(TradeGood.AnimalFabric), "Cloth and Animal Fabric share one clothing curve and position");
        Check(Trade.BestQuantity(v, TradeGood.Cloth, TradeGood.AnimalFabric) == 0, "trading along one curve gains nothing");
        // The request lists: wants by recoverable utility up to target coverage, gives by marginal loss, at most W each.
        var (wants, gives) = Trade.Lists(v, w.Config.TradeListLength);
        double Recoverable(TradeGood g) => v.Integrated(g, v.Position(g), Math.Max(v.Position(g), v.Curve(g).Target));
        Check(wants.Length <= w.Config.TradeListLength && gives.Length <= w.Config.TradeListLength, "the lists are bounded by W");
        Check(wants.Zip(wants.Skip(1), (a, b) => Recoverable(a) >= Recoverable(b)).All(x => x) && wants.All(g => Recoverable(g) > 0), "wants are ordered by recoverable utility");
        Check(gives.All(g => v.Stock(g) > 0) && gives.Zip(gives.Skip(1), (a, b) => v.Marginal(a, v.Position(a) - 1e-9) <= v.Marginal(b, v.Position(b) - 1e-9)).All(x => x), "gives hold stock and are ordered by marginal loss");
        var top = Trade.Registry.Select(d => d.Id).Where(g => Recoverable(g) > 1e-12).OrderByDescending(Recoverable).ThenBy(g => (int)g).First();
        Check(wants[0] == top, "the first want is the good with the most recoverable utility");
        var shortList = Trade.Lists(v, 1); Check(shortList.Wants.Length == 1 && shortList.Gives.Length == 1, "W = 1 keeps one want and one give");
        // An inactive good is neither wanted nor given.
        w.Config.TradeFoodEnabled = false; var off = Trade.Value(w, CampOf(w, 1));
        Check(off.Stock(TradeGood.Food) == 0 && off.Marginal(TradeGood.Food, 0) == 0 && off.Curve(TradeGood.Food) == null, "food is inert when it is not traded");
    }

    // A farming village with food and no clothing beside a herding camp with Animal Fabric and little food.
    private static WorldState FoodWorld()
    {
        var w = World((6, 6, 0, 0), (8, 6, 0, 0)); w.Config.TradeFoodEnabled = true;
        var farmer = CampOf(w, 1); farmer.PlantFood = 30000; farmer.AnimalFood = 0; farmer.AquaticFood = 0;
        var herder = CampOf(w, 2); herder.Technology = Technology.AnimalHusbandry; herder.Technologies[(int)Technology.Cultivation] = false; herder.Technologies[(int)Technology.AnimalHusbandry] = true;
        herder.IsVillage = false; herder.PlantFood = 100; herder.AnimalFood = 200; herder.AquaticFood = 0; herder.Inventory.AnimalFabric = 400;
        return w;
    }
    private static void Food()
    {
        var w = FoodWorld(); var cfg = w.Config; var farmer = CampOf(w, 1); var herder = CampOf(w, 2);
        double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals;
        // Bands: consumption of every linked consumer, then feedstock and reserve (protected), then growth, then surplus.
        var v = Trade.Value(w, farmer); var bands = (BandCurve)v.Curve(TradeGood.Food);
        double consumption = w.LinkedPopulation(farmer) * cfg.Consumption * intervals;
        TurnResolver.FoodBucketBasis(w, farmer, intervals, years, consumption, out double reserve, out double growth);
        double feed = NeolithicProduction.ClothingNeedUnits(cfg.Neolithic, farmer.Population, years, farmer.Inventory.Cloth, farmer.Inventory.AnimalFabric) * cfg.Neolithic.PlantFoodPerFabricUnit;
        Near(bands.Ends[0], consumption, "the first band is this turn's consumption"); Near(bands.Ends[1], consumption + feed + reserve, "the protected band adds the clothing feedstock and the reserve");
        Near(bands.Ends[2], consumption + feed + reserve + growth, "the growth band is the feasible growth food");
        Near(bands.Values[0], ExpansionValuation.FoodValue(cfg, farmer), "consumption food is valued at v_food");
        Check(bands.Values[0] >= bands.Values[1] && bands.Values[1] >= bands.Values[2] && bands.Marginal(bands.Ends[2] + 1) == 0, "band values decrease and surplus is worth nothing locally");
        Near(v.Stock(TradeGood.Food), Math.Min(farmer.Food, farmer.Food - bands.Ends[1]), "only food above the protected band may be given");
        Near(v.Position(TradeGood.Food), farmer.Food, "without a production history the envelope is the stock");
        // The lagged envelope adds last turn's committed production.
        var withIncome = Trade.Value(w, farmer, 500); Near(withIncome.Position(TradeGood.Food), farmer.Food + 500, "the envelope adds last turn's production");
        // Herder short of food wants it and pays in Animal Fabric; the farmer short of clothing wants fabric.
        var plan = Trade.Plan(w);
        var buy = plan.Agreements.Where(a => a.Want == TradeGood.Food || a.Give == TradeGood.Food).ToArray();
        Check(buy.Length > 0 && buy.All(a => (a.Want == TradeGood.Food ? a.Responder : a.Requester) == farmer.Id), "food flows from the farmer");
        Check(buy.All(a => (a.Want == TradeGood.Food ? a.Give : a.Want) == TradeGood.AnimalFabric), "the herder pays in Animal Fabric");
        double sold = buy.Sum(a => a.Want == TradeGood.Food ? a.WantQuantity : a.GiveQuantity);
        Check(farmer.Food + plan.Delta(farmer.Id, TradeGood.Food) >= bands.Ends[1] - 1e-6, "the protected band is never debited (" + (farmer.Food - sold) + " >= " + bands.Ends[1] + ")");
        Check(plan.Delta(herder.Id, TradeGood.Food) > 0 && plan.Delta(farmer.Id, TradeGood.AnimalFabric) > 0, "the herder imports food and the farmer imports fabric");
        foreach (var a in buy) { var parts = a.Want == TradeGood.Food ? a.WantParts : a.GiveParts; Near(parts[0], parts.Sum(), "food moves in the seller's typed proportions (all plant)"); }
        // Safe-only control: growth food is never exported.
        var safe = FoodWorld(); safe.Config.TradeFoodSafeOnly = true; var sv = Trade.Value(safe, CampOf(safe, 1)); var sp = Trade.Plan(safe);
        double safeSold = sp.Agreements.Sum(a => a.Want == TradeGood.Food && a.Responder == CampOf(safe, 1).Id ? a.WantQuantity : a.Give == TradeGood.Food && a.Requester == CampOf(safe, 1).Id ? a.GiveQuantity : 0);
        Check(CampOf(safe, 1).Food - safeSold >= ((BandCurve)sv.Curve(TradeGood.Food)).Ends[2] - 1e-6 && safeSold <= sold + 1e-9, "safe-only exports nothing below the growth band");
        // A camp at its protected band exports nothing.
        var lean = FoodWorld(); var lf = CampOf(lean, 1); var lv = Trade.Value(lean, lf); lf.PlantFood = ((BandCurve)lv.Curve(TradeGood.Food)).Ends[1];
        Check(Trade.Value(lean, lf).Stock(TradeGood.Food) <= 1e-9 && Trade.Plan(lean).Agreements.All(a => !(a.Want == TradeGood.Food && a.Responder == lf.Id)), "a camp at its protected band sells no food");
        // Planning: imports enter the prepared envelope before demographic planning; nothing unconfirmed does.
        var pw = FoodWorld(); var prepared = TurnPlanner.Prepare(pw, new Dictionary<int, List<Order>>());
        foreach (var c in pw.Camps)
            Near(prepared.Needs[c.Id].OpeningCampFood, c.Food + prepared.Trade.Delta(c.Id, TradeGood.Food), "camp " + c.Id + "'s prepared opening food is its committed food plus confirmed trade");
        // Resolution: settles before dispatch, conserves food and typed food, and the resolver invariants hold.
        var rw = FoodWorld(); double food0 = rw.Camps.Sum(c => c.Food); var rp = TurnPlanner.Prepare(rw, new Dictionary<int, List<Order>>());
        var record = new TurnRecord(); var copy = SaveCodec.Load(SaveCodec.Save(rw));
        Trade.Settle(copy, rp.Trade.Agreements.Count > 0 ? Trade.Plan(copy) : TradePlan.Empty, record);
        Near(copy.Camps.Sum(c => c.Food), food0, "settlement conserves food"); Near(copy.Camps.Sum(c => c.PlantFood), rw.Camps.Sum(c => c.PlantFood), "settlement conserves plant food");
        Check(record.Trades.Any(t => t.Want == TradeGood.Food || t.Give == TradeGood.Food), "a food trade settles");
        new TurnResolver().Resolve(rw, rp); TurnResolver.Invariants(rw); checks++;
        Check(rw.History.Last().Trades.Any(t => t.Want == TradeGood.Food || t.Give == TradeGood.Food), "a resolved turn records the food trade");
        // Short delivery scales both sides: the seller lost half its food before settlement.
        var sh = FoodWorld(); var shPlan = Trade.Plan(sh); var shFood = shPlan.Agreements.First(a => a.Want == TradeGood.Food || a.Give == TradeGood.Food);
        var seller = sh.Camps.First(c => c.Id == (shFood.Want == TradeGood.Food ? shFood.Responder : shFood.Requester));
        double owed = shPlan.Agreements.Where(a => (a.Want == TradeGood.Food ? a.Responder : a.Give == TradeGood.Food ? a.Requester : -1) == seller.Id).Sum(a => a.Want == TradeGood.Food ? a.WantQuantity : a.GiveQuantity);
        seller.PlantFood = owed / 2; var shRecord = new TurnRecord(); double shTotal = sh.Camps.Sum(c => c.Food);
        Trade.Settle(sh, shPlan, shRecord);
        Check(shRecord.Trades.Any(t => t.Delivered < 1 - 1e-9) && sh.Camps.All(c => c.PlantFood >= -1e-12), "a short seller delivers in proportion and never goes negative");
        Near(sh.Camps.Sum(c => c.Food), shTotal, "short delivery conserves food");
        // Determinism with food: scalar against reversed 4-worker partitions, and draft against fresh preparation.
        var worlds = new[] { FoodWorld(), FoodWorld() }; worlds[1].Camps.Reverse(); worlds[1].Societies.Reverse();
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 2, true, 1) };
        for (int t = 0; t < 3; t++)
        {
            for (int i = 0; i < 2; i++) Advance(worlds[i], new TurnResolver(workers[i]));
            Check(SaveCodec.Fingerprint(worlds[0]) == SaveCodec.Fingerprint(worlds[1]), "food trade replays identically across workers and order after turn " + t);
        }
        var da = FoodWorld(); var db = FoodWorld(); var draft = TurnPlanner.Draft(da, 0);
        new TurnResolver().Resolve(da, TurnPlanner.Prepare(da, new Dictionary<int, List<Order>>(), null, null, draft)); Advance(db, new TurnResolver());
        Check(SaveCodec.Fingerprint(da) == SaveCodec.Fingerprint(db), "a draft-backed food-trading turn equals a fresh one");
        // Food off: no food moves.
        var nf = FoodWorld(); nf.Config.TradeFoodEnabled = false;
        Check(Trade.Plan(nf).Agreements.All(a => a.Want != TradeGood.Food && a.Give != TradeGood.Food), "with food off no food trades");
    }

    // Diagnostic (--trade-probe=<save>): trade on, then each eligible camp's direction, giveable stock and partner reach.
    public static void Probe(string path)
    {
        var w = SaveCodec.Load(File.ReadAllBytes(path)); w.Config.TradeEnabled = true; Ownership.Bind(w);
        var eligible = w.Camps.Where(c => Trade.Eligible(w, c)).OrderBy(c => c.Id).ToArray(); var ids = new HashSet<int>(eligible.Select(c => c.Id));
        var production = Trade.LastProduction(w);
        Console.WriteLine("camps " + w.Camps.Count + " eligible " + eligible.Length + " turn " + w.Turn);
        int wanting = 0, withContacts = 0, inRange = 0;
        foreach (var c in eligible)
        {
            var v = Trade.Value(w, c, production.TryGetValue(c.Id, out double p) ? p : 0); var (wantList, giveList) = Trade.Lists(v, w.Config.TradeListLength); double q = wantList.Length * giveList.Length;
            var foreign = c.People.Memory.Contacts.Where(k => !k.Moved && ids.Contains(k.Camp) && w.Camps.First(x => x.Id == k.Camp).SocietyId != c.SocietyId).ToArray();
            int near = foreign.Count(k => w.Map.Distance(c.Tile, k.Tile) <= w.Config.TradeRange);
            if (q > 0) wanting++; if (foreign.Length > 0) withContacts++; if (near > 0) inRange++;
            string goods = string.Join(" ", Trade.Registry.Select(d => d.Id).Select(g => g + "=" + v.Held[(int)g].ToString("F1") + "/" + v.Stock(g).ToString("F1") + "@" + v.Marginal(g, v.Position(g)).ToString("G3")));
            Console.WriteLine($"{c.Id} pop={c.Population} tech={c.Technology} {goods} wants={string.Join(",", wantList)} gives={string.Join(",", giveList)} foreign={foreign.Length} inRange={near}");
        }
        Console.WriteLine($"wanting {wanting} withForeignContacts {withContacts} inRange {inRange}");
        var plan = Trade.Plan(w); Console.WriteLine($"requisitions {plan.Summary.Requisitions} honoured {plan.Summary.Honoured} countered {plan.Summary.Countered} ignored {plan.Summary.Ignored}");
        foreach (var g in plan.Agreements.GroupBy(a => a.Want + "<-" + a.Give)) Console.WriteLine($"  {g.Key}: {g.Count()} agreements, want {g.Sum(a => a.WantQuantity):F1}, give {g.Sum(a => a.GiveQuantity):F1}");
    }
}
