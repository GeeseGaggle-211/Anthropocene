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
    // The resolver's three settlement steps without production between them.
    private static void SettleAll(WorldState w, TradePlan plan, TurnRecord record)
    {
        var s = Trade.Settle(w, plan, record); Trade.SettleAfterProduction(w, s); Trade.Finish(w, s, record);
    }
    private static void Advance(WorldState w, TurnResolver r) => r.Resolve(w, new Dictionary<int, List<Order>>());

    public static int Run()
    {
        checks = 0; Config(); Valuation(); Handshake(); Eligibility(); Kin(); Settlement(); Planning(); Replay(); Persistence(); Registry(); Food(); MadeToOrder(); Pots(); Published(); Numerics(); ClaimFirst();
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
        foreach (var pair in new[] { ("TradeEnabled", "false"), ("TradeRange", "6"), ("TradeRequisitionsPerCamp", "3"), ("TradeMargin", "0.1"), ("TradeDistanceCostPerHex", "0.01"), ("TradeFoodEnabled", "true"), ("TradeFoodSafeOnly", "false"), ("TradeExportLabourShare", "0.25"), ("TradeListLength", "3") })
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
        var record = new TurnRecord(); SettleAll(w, prepared.Trade, record);
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
        Near(plan.Deltas.Keys.Union(plan.Exports.Keys).Sum(id => plan.Delta(id, TradeGood.Cloth)), 0, "cloth deltas sum to zero", 1e-9);
        // Without a recorded labour price nothing is made to order: both agreements are all stock and settle at the opening,
        // so the receivers plan with what they receive.
        Check(plan.Agreements.All(a => a.AllStock && a.WantMade == 0 && a.GiveMade == 0) && !plan.AfterProduction && plan.Exports.Count == 0, "without a labour price every agreement is all stock");
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
        double Flow(Camp c, TradeGood g) => plan.Agreements.Sum(a => (a.Requester == c.Id && a.Want == g ? a.WantQuantity : 0) - (a.Responder == c.Id && a.Want == g ? a.WantQuantity : 0) + (a.Responder == c.Id && a.Give == g ? a.GiveQuantity : 0) - (a.Requester == c.Id && a.Give == g ? a.GiveQuantity : 0));
        var expected = w.Camps.ToDictionary(c => c.Id, c => (c.Inventory.Cloth + Flow(c, TradeGood.Cloth), c.Inventory.HousingMaterial + Flow(c, TradeGood.HousingMaterial)));
        SettleAll(w, plan, record);
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
        SettleAll(s, sp, sr);
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
        Check(Trade.PartCount == Trade.Registry.Sum(d => d.Parts.Length) && Trade.PartIndex(TradeGood.Wood, 0) == Trade.PartCount - 1
            && Trade.PartIndex(TradeGood.LightHousing, 0) == Trade.PartIndex(TradeGood.Food, 2) + 1, "parts are indexed contiguously");
        Check(Trade.Registry.All(d => !d.Makeable || d.Parts.Length == 1), "a made good has one part");
        // W grows with the number of goods as ceil(sqrt(G)), never below TradeListLength.
        Check(Trade.ListLength(w0()) == Math.Max(w0().TradeListLength, (int)Math.Ceiling(Math.Sqrt(Trade.Registry.Count(d => d.Active(w0()))))) && Trade.ListLength(w0()) >= 4, "the list length scales with the goods");
        static SimulationConfig w0() { var c = SimulationConfig.Small(1); c.TradeFoodEnabled = true; return c; }
        var w = Complementary(); w.Config.TradeFoodEnabled = true; var v = Trade.Value(w, CampOf(w, 1));
        Check(ReferenceEquals(v.Curve(TradeGood.Cloth), v.Curve(TradeGood.AnimalFabric)) && v.Position(TradeGood.Cloth) == v.Position(TradeGood.AnimalFabric), "Cloth and Animal Fabric share one clothing curve and position");
        Check(Trade.BestQuantity(v, TradeGood.Cloth, TradeGood.AnimalFabric) == 0, "trading along one curve gains nothing");
        // The request lists: wants by recoverable utility up to target coverage, gives by marginal loss, at most W each.
        var (wants, gives) = Trade.Lists(v, w.Config.TradeListLength);
        // As Lists scores it: the make-or-buy gain up to target coverage; an inactive (or unpriced) good has none.
        double Recoverable(TradeGood g) => v.Active[(int)g] ? v.Gain(g, Math.Max(0, v.Curve(g).Target - v.Position(g))) : 0;
        Check(Trade.Registry.Where(d => d.Curve != "Food").All(d => !v.Active[(int)d.Id] || v.Curve(d.Id) != null) && !v.Active[(int)TradeGood.HandTools] && v.Curve(TradeGood.HandTools) == null, "a labour-priced good is inactive (no curve) until a labour price is recorded");
        Check(wants.Length <= w.Config.TradeListLength && gives.Length <= w.Config.TradeListLength, "the lists are bounded by W");
        Check(wants.Zip(wants.Skip(1), (a, b) => Recoverable(a) >= Recoverable(b)).All(x => x) && wants.All(g => Recoverable(g) > 0), "wants are ordered by recoverable utility");
        Check(gives.All(g => v.Stock(g) > 0) && gives.Zip(gives.Skip(1), (a, b) => v.Marginal(a, v.Position(a) - 1e-9) <= v.Marginal(b, v.Position(b) - 1e-9)).All(x => x), "gives hold stock and are ordered by marginal loss");
        var top = Trade.Registry.Select(d => d.Id).Where(g => Recoverable(g) > 1e-12).OrderByDescending(Recoverable).ThenBy(g => (int)g).First();
        Check(wants[0] == top, "the first want is the good with the most recoverable utility");
        var shortList = Trade.Lists(v, 1); Check(shortList.Wants.Length == 1 && shortList.Gives.Length == 1, "W = 1 keeps one want and one give");
        // Offers: the goods a camp may give (stock above its floor or making capacity), goods only. A requester wants only
        // what its partner offers, and every agreement's goods were offered by their givers at the opening.
        var offers = Trade.Offers(v);
        Check(Trade.Registry.All(d => offers[(int)d.Id] == v.Stock(d.Id) > 1e-9) && offers[(int)TradeGood.Cloth] && !offers[(int)TradeGood.HousingMaterial], "a camp offers exactly the goods it may give");
        Check(Trade.Lists(v, 4, new bool[Trade.Registry.Length]).Wants.Length == 0, "a partner that offers nothing is wanted for nothing");
        var only = new bool[Trade.Registry.Length]; only[(int)TradeGood.HousingMaterial] = true;
        Check(Trade.Lists(v, 4, only).Wants.All(g => g == TradeGood.HousingMaterial), "wants are drawn only from the partner's offers");
        foreach (var world in new[] { Complementary(), OrderWorld(fabricOnly: false) })
        {
            var opening = world.Camps.Where(c => Trade.Eligible(world, c)).ToDictionary(c => c.Id, c => Trade.Offers(Trade.Value(world, c)));
            Check(Trade.Plan(world).Agreements.All(a => opening[a.Responder][(int)a.Want] && opening[a.Requester][(int)a.Give]), "every agreed good was offered by its giver");
        }
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
        // The herder has fabric in stock and no labour price, so nothing is made to order: every agreement is all stock and
        // settles at the opening, so both sides plan with what they receive (pay on delivery is checked in MadeToOrder).
        Check(buy.All(a => a.AllStock) && !plan.AfterProduction, "fixture: the food trades are all stock");
        Check(plan.Delta(herder.Id, TradeGood.Food) > 0 && plan.Delta(farmer.Id, TradeGood.Food) < 0 && plan.Delta(farmer.Id, TradeGood.AnimalFabric) > 0 && plan.Delta(herder.Id, TradeGood.AnimalFabric) < 0,
            "an all-stock food trade: both sides plan with what they receive and without what they give");
        // Opening settlement: both legs move at once, through escrow and out again; a short giver scales both legs.
        {
            var ew = FoodWorld(); var ep = Trade.Plan(ew); var er = new TurnRecord(); var ef = CampOf(ew, 1); var eh = CampOf(ew, 2);
            double farmer0 = ef.Food, herder0 = eh.Food, total0 = ew.Camps.Sum(c => c.Food);
            double paid = ep.Agreements.Sum(a => a.Give == TradeGood.Food && a.Requester == ef.Id ? a.GiveQuantity : a.Want == TradeGood.Food && a.Responder == ef.Id ? a.WantQuantity : 0);
            // The herder holds only a third of the fabric it agreed to give. Agreements escrow in order, each at the share
            // its giver still holds, and each settles both legs at the smaller share: all the fabric moves, food in proportion.
            double owedFabric = ep.Agreements.Sum(a => a.Want == TradeGood.AnimalFabric && a.Responder == eh.Id ? a.WantQuantity : a.Give == TradeGood.AnimalFabric && a.Requester == eh.Id ? a.GiveQuantity : 0);
            eh.Inventory.AnimalFabric = owedFabric / 3;
            var st = Trade.Settle(ew, ep, er);
            Near(Enumerable.Range(0, Trade.PartCount).Sum(st.Escrowed), 0, "an all-stock agreement leaves nothing in escrow after the opening");
            Near(ew.Camps.Sum(c => c.Food), total0, "opening settlement conserves food");
            Near(eh.Inventory.AnimalFabric, 0, "the short giver gives all it holds", 1e-6);
            double foodBefore = ew.Camps.Sum(c => c.Food), herderOpen = eh.Food; Trade.SettleAfterProduction(ew, st); Trade.Finish(ew, st, er);
            Near(ew.Camps.Sum(c => c.Food), foodBefore, "an all-stock plan moves nothing after production"); Near(eh.Food, herderOpen, "nothing waits for production");
            double received = er.Trades.Sum(t => t.Give == TradeGood.Food && t.Responder == eh.Id ? t.GiveQuantity : t.Want == TradeGood.Food && t.Requester == eh.Id ? t.WantQuantity : 0);
            double fabricIn = er.Trades.Sum(t => t.Want == TradeGood.AnimalFabric && t.Responder == eh.Id ? t.WantQuantity : t.Give == TradeGood.AnimalFabric && t.Requester == eh.Id ? t.GiveQuantity : 0);
            Near(fabricIn, owedFabric / 3, "the records deliver the third the herder held", 1e-9);
            Near(eh.Food, herder0 + received, "the receiver is paid at the opening what the records say", 1e-6);
            Near(ef.Food, farmer0 - received, "the payer keeps the rest", 1e-6);
            Check(received < paid - 1e-6 && received > 0, "a short giver is paid in part (" + received + " of " + paid + ")");
            Check(er.Trades.All(t => Math.Abs(t.Delivered - t.GiveDelivered) < 1e-9), "each agreement's two legs settle at the same fraction");
        }
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
        SettleAll(copy, rp.Trade.Agreements.Count > 0 ? Trade.Plan(copy) : TradePlan.Empty, record);
        Near(copy.Camps.Sum(c => c.Food), food0, "settlement conserves food"); Near(copy.Camps.Sum(c => c.PlantFood), rw.Camps.Sum(c => c.PlantFood), "settlement conserves plant food");
        Check(record.Trades.Any(t => t.Want == TradeGood.Food || t.Give == TradeGood.Food), "a food trade settles");
        new TurnResolver().Resolve(rw, rp); TurnResolver.Invariants(rw); checks++;
        Check(rw.History.Last().Trades.Any(t => t.Want == TradeGood.Food || t.Give == TradeGood.Food), "a resolved turn records the food trade");
        // Short delivery scales both sides: the seller lost half its food before settlement.
        var sh = FoodWorld(); var shPlan = Trade.Plan(sh); var shFood = shPlan.Agreements.First(a => a.Want == TradeGood.Food || a.Give == TradeGood.Food);
        var seller = sh.Camps.First(c => c.Id == (shFood.Want == TradeGood.Food ? shFood.Responder : shFood.Requester));
        double owed = shPlan.Agreements.Where(a => (a.Want == TradeGood.Food ? a.Responder : a.Give == TradeGood.Food ? a.Requester : -1) == seller.Id).Sum(a => a.Want == TradeGood.Food ? a.WantQuantity : a.GiveQuantity);
        seller.PlantFood = owed / 2; var shRecord = new TurnRecord(); double shTotal = sh.Camps.Sum(c => c.Food);
        SettleAll(sh, shPlan, shRecord);
        Check(shRecord.Trades.Any(t => (t.Want == TradeGood.Food ? t.Delivered : t.GiveDelivered) < 1 - 1e-9) && sh.Camps.All(c => c.PlantFood >= -1e-12), "a short seller delivers in proportion and never goes negative");
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

    // A farming village with food and no clothing beside a herding camp with animal food but no Animal Fabric in
    // stock: the herder can only sell fabric by making it to order. Both carry a lagged labour price, which also prices
    // every other made good, so by default Pots, tents and tools trade to order too (see Pots()). `fabricOnly` gives
    // every craft good but Animal Fabric zero priority, so only fabric can be made to order and only for food.
    private static WorldState OrderWorld(double price = .05, bool fabricOnly = true)
    {
        var w = FoodWorld(); w.Config.Neolithic.PlannerFulfilment = "Value"; w.Config.AnnualHumanGrowthRate = new SimulationConfig().AnnualHumanGrowthRate;
        var herder = CampOf(w, 2); herder.Inventory.AnimalFabric = 0; herder.AnimalFood = 60000; herder.PlantFood = 300;
        if (fabricOnly)
            foreach (var c in w.Camps)
            {
                var p = c.CraftingPriorities;
                p.HandTools = p.Pots = p.Cloth = p.FarmTools = p.Housing = p.Tools = p.LightHousing = p.HerdingTools = 0;
            }
        foreach (var c in w.Camps) { c.EconomicLabor = new EconomicLaborPlan { LabourPriceSet = true, LabourPrice = price }; c.EconomyAutomationTurns = -1; c.Inventory.HandTools = 60; c.Inventory.Tools = 60; }
        // Raw stock for the herder's recipes: the fixture has no geological sources, and make-to-order capacity is bounded by the
        // materials a camp holds or can reach. The farmer holds none, so it buys the Pots it cannot make.
        herder.Inventory.Stone = herder.Inventory.Clay = herder.Inventory.Wood = 500;
        // Spare animal food inside the growth band: enough to make fabric, while more food still funds growth.
        var bands = (BandCurve)Trade.Value(w, herder).Curve(TradeGood.Food);
        herder.PlantFood = 0; herder.AnimalFood = bands.Ends[1] + .3 * (bands.Ends[2] - bands.Ends[1]);
        return w;
    }
    private static void MadeToOrder()
    {
        var w = OrderWorld(); var cfg = w.Config; var herder = CampOf(w, 2); var farmer = CampOf(w, 1);
        double years = cfg.TurnDays / cfg.DaysPerYear;
        var v = Trade.Value(w, herder);
        var recipes = NeolithicProduction.Recipes(cfg); var recipe = recipes.First(r => r.Good == CraftGood.AnimalFabric);
        double labour = ClaimValuation.LabourPerUnit(w, herder, recipe, recipes, NeolithicProduction.HousingMultiplier(w, herder, herder.Population));
        double foodValue = ((BandCurve)v.Curve(TradeGood.Food)).Marginal(v.Position(TradeGood.Food));
        Near(v.Cost[(int)TradeGood.AnimalFabric], labour * .05 + (recipe.PlantFood + recipe.AnimalFood) * foodValue, "the made-to-order cost is labour at the lagged price plus food inputs at the food value");
        var bands = (BandCurve)v.Curve(TradeGood.Food); double spare = Math.Max(0, Math.Min(herder.Food, v.Position(TradeGood.Food) - bands.Ends[1]));
        Near(v.Capacity[(int)TradeGood.AnimalFabric], Math.Min(cfg.TradeExportLabourShare * herder.Population * years / labour, spare * herder.AnimalFood / herder.Food / recipe.AnimalFood), "capacity is the export labour share of the residents' turn, bounded by spare animal food");
        var lean = OrderWorld(); CampOf(lean, 2).AnimalFood = 1000;
        Check(Trade.Value(lean, CampOf(lean, 2)).Capacity[(int)TradeGood.AnimalFabric] == 0, "a herder without spare animal food cannot make fabric to order");
        Check(v.Stock(TradeGood.AnimalFabric) > 0 && v.Held[(int)TradeGood.AnimalFabric] == 0, "a camp with no fabric in stock may still offer to make it");
        Check(double.IsPositiveInfinity(v.Cost[(int)TradeGood.Cloth]) && v.Capacity[(int)TradeGood.Cloth] == 0, "a herding camp without agriculture cannot make Cloth");
        // The loss of giving is the use value, capped by the cost of making another: check against a numeric integral.
        // Make or buy: an independent W'(x) = u(x + cap) below x* - cap, the cost on [x* - cap, x*], u(x) above, with
        // x* where the use value u equals the cost; losses and gains integrate it.
        double WRef(Trade.Valuation val, TradeGood g, double x)
        {
            var u = val.Curve(g); double c = val.Cost[(int)g], cap = val.MakeCapacity(g), xs = u.Crossing(c);
            if (!(cap > 0) || double.IsPositiveInfinity(c) || double.IsNegativeInfinity(xs)) return u.Marginal(x);
            return x > xs ? u.Marginal(x) : x >= xs - cap ? c : u.Marginal(x + cap);
        }
        double NumericLoss(Trade.Valuation val, TradeGood g, double q) { double p0 = val.Position(g), sum = 0; int n = 40000; for (int k = 0; k < n; k++) sum += WRef(val, g, p0 - q + (k + .5) * q / n) * q / n; return sum; }
        double q = 20, pos = v.Position(TradeGood.AnimalFabric), cost = v.Cost[(int)TradeGood.AnimalFabric];
        Near(v.Loss(TradeGood.AnimalFabric, q), NumericLoss(v, TradeGood.AnimalFabric, q), "the make-or-buy loss integral matches a numeric integral", 1e-5);
        for (double x = pos - 3 * v.MakeCapacity(TradeGood.AnimalFabric) - 10; x < pos + 50; x += 7.3)
        {
            double wv = v.LossMarginal(TradeGood.AnimalFabric, x), use = v.Curve(TradeGood.AnimalFabric).Marginal(x);
            Check(wv <= use + 1e-9 && wv >= Math.Min(use, cost) - 1e-9, "make or buy lies between min(use, cost) and use value at x = " + x.ToString("F1"));
            Near(wv, WRef(v, TradeGood.AnimalFabric, x), "W' matches the reference at x = " + x.ToString("F1"), 1e-9);
        }
        double xs0 = v.Curve(TradeGood.AnimalFabric).Crossing(cost);
        Near(v.LossMarginal(TradeGood.AnimalFabric, xs0 - .5 * Math.Min(1, v.MakeCapacity(TradeGood.AnimalFabric))), cost, "within capacity below x* a unit is worth its making cost");
        Check(v.Gain(TradeGood.AnimalFabric, q) <= v.Integrated(TradeGood.AnimalFabric, pos, pos + q) + 1e-9, "receiving is worth at most its use value");
        var fv = Trade.Value(w, farmer); double fq = 30;
        Near(fv.Loss(TradeGood.Cloth, fq), NumericLoss(fv, TradeGood.Cloth, fq), "the make-or-buy integral matches below zero stock too", 1e-5);
        // No recorded labour price or no export labour: nothing is made to order.
        var unpriced = OrderWorld(); foreach (var c in unpriced.Camps) c.EconomicLabor = null;
        Check(Trade.Value(unpriced, CampOf(unpriced, 2)).Stock(TradeGood.AnimalFabric) == 0, "without a lagged labour price nothing is made to order");
        var noShare = OrderWorld(); noShare.Config.TradeExportLabourShare = 0;
        Check(Trade.Value(noShare, CampOf(noShare, 2)).Stock(TradeGood.AnimalFabric) == 0, "a zero export labour share disables making to order");
        // The handshake: the farmer buys fabric the herder will make, paying in food.
        var plan = Trade.Plan(w);
        var order = plan.Agreements.Where(a => a.Want == TradeGood.AnimalFabric && a.Responder == herder.Id || a.Give == TradeGood.AnimalFabric && a.Requester == herder.Id).ToArray();
        Check(order.Length > 0 && order.All(a => (a.Want == TradeGood.AnimalFabric ? a.Give : a.Want) == TradeGood.Food), "the farmer buys made-to-order fabric with food");
        double ordered = order.Sum(a => a.Want == TradeGood.AnimalFabric ? a.WantQuantity : a.GiveQuantity);
        // No pair moves one curve both ways in a turn (a camp may still buy from one neighbour and sell to another).
        var pairFlows = plan.Agreements.SelectMany(g => new[] { (From: g.Responder, To: g.Requester, Curve: Trade.Descriptor(g.Want).Curve), (From: g.Requester, To: g.Responder, Curve: Trade.Descriptor(g.Give).Curve) }).ToArray();
        Check(!pairFlows.Any(f => pairFlows.Any(o => o.From == f.To && o.To == f.From && o.Curve == f.Curve)), "no pair moves one curve both ways");
        Check(plan.Exports.TryGetValue(herder.Id, out var exports) && Math.Abs(exports[(int)CraftGood.AnimalFabric] - ordered) < 1e-9, "the agreed fabric is the herder's export claim");
        Check(!plan.Deltas.TryGetValue(herder.Id, out var hd) || hd[Trade.PartIndex(TradeGood.AnimalFabric, 0)] == 0, "a made-to-order export does not reduce the planning stock");
        Near(plan.Delta(farmer.Id, TradeGood.AnimalFabric), ordered, "the buyer plans with the import as stock");
        // Planning: the export claim raises the herder's demand and prepared production by the export.
        var view = herder.WithPrivateInventory(); Trade.ApplyToPlanningCamp(view, plan);
        Near(NeolithicProduction.Demand(w, view, CraftGood.AnimalFabric, years, herder.Population) - NeolithicProduction.Demand(w, herder, CraftGood.AnimalFabric, years, herder.Population), ordered, "demand nets the export against stock and adds the deficit", 1e-9);
        var off = OrderWorld(); off.Config.TradeEnabled = false;
        var withTrade = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>()); var without = TurnPlanner.Prepare(off, new Dictionary<int, List<Order>>());
        double madeWith = withTrade.Needs[herder.Id].Production?[CraftGood.AnimalFabric] ?? 0, madeWithout = without.Needs[CampOf(off, 2).Id].Production?[CraftGood.AnimalFabric] ?? 0;
        Check(madeWith > madeWithout + .5 * ordered, "the herder's prepared production makes the export (" + madeWith + " vs " + madeWithout + ", ordered " + ordered + ")");
        // Resolution: food at the opening, fabric after production and before needs; the record says which leg was made to order.
        double fabric0 = w.Camps.Sum(c => c.Inventory.AnimalFabric);
        new TurnResolver().Resolve(w, withTrade); TurnResolver.Invariants(w); checks++;
        var records = w.History.Last().Trades.Where(t => t.Want == TradeGood.AnimalFabric || t.Give == TradeGood.AnimalFabric).ToArray();
        Check(records.Length == order.Length && records.All(t => t.Want == TradeGood.AnimalFabric ? t.WantMadeToOrder && !t.GiveMadeToOrder : t.GiveMadeToOrder && !t.WantMadeToOrder), "records mark the made-to-order leg");
        double delivered = records.Sum(t => t.Want == TradeGood.AnimalFabric ? t.WantQuantity : t.GiveQuantity), fraction = records.Min(t => t.Want == TradeGood.AnimalFabric ? t.Delivered : t.GiveDelivered);
        Check(delivered > 0 && fraction > .5, "the herder delivers what it made (" + delivered + " of " + ordered + ", lowest fraction " + fraction + ")");
        Check(w.History.Last().Production.Any(p => p.Camp == herder.Id && p.Good == CraftGood.AnimalFabric && p.Output > 0), "the herder made fabric this turn");
        // The farmer's needs saw the delivered clothing: its clothing coverage beats the no-trade control.
        new TurnResolver().Resolve(off, without);
        Check(farmer.ClothPerCapita > CampOf(off, 1).ClothPerCapita + 1e-9, "the buyer's clothing coverage reflects the delivery (" + farmer.ClothPerCapita + " vs " + CampOf(off, 1).ClothPerCapita + ")");
        // A seller that made less than it agreed delivers in proportion after production; the paid food stays paid.
        // Pay on delivery: at the opening the buyer's food goes into escrow and the seller is not yet paid; after production
        // the seller is paid the share it delivered and the rest of the escrow is refunded.
        var sw = OrderWorld(); var sp = Trade.Plan(sw); var srec = new TurnRecord();
        var seller = CampOf(sw, 2); var buyer = CampOf(sw, 1);
        double seller0 = seller.Food, buyer0 = buyer.Food, food0 = sw.Camps.Sum(c => c.Food);
        double escrowedFood = sp.Agreements.Sum(a => a.Give == TradeGood.Food && a.Requester == buyer.Id ? a.GiveQuantity : a.Want == TradeGood.Food && a.Responder == buyer.Id ? a.WantQuantity : 0);
        Check(sp.AfterProduction && escrowedFood > 0, "fixture: the fabric order is paid in food after production");
        var settled = Trade.Settle(sw, sp, srec);
        Near(buyer.Food, buyer0 - escrowedFood, "the buyer's food leaves into escrow at the opening", 1e-9);
        Near(seller.Food, seller0, "the seller is not paid before delivery");
        Near(sw.Camps.Sum(c => c.Food) + Enumerable.Range(0, Trade.PartCount).Sum(settled.Escrowed), food0, "camps plus escrow conserve food");
        double owed = sp.Exports[seller.Id][(int)CraftGood.AnimalFabric];
        seller.Inventory.AnimalFabric = owed / 4; double total = sw.Camps.Sum(c => c.Inventory.AnimalFabric);
        Trade.SettleAfterProduction(sw, settled); Trade.Finish(sw, settled, srec);
        Near(sw.Camps.Sum(c => c.Inventory.AnimalFabric), total, "short after-production delivery conserves fabric");
        Check(seller.Inventory.AnimalFabric >= -1e-12 && srec.Trades.Where(t => t.Want == TradeGood.AnimalFabric || t.Give == TradeGood.AnimalFabric).All(t => (t.Want == TradeGood.AnimalFabric ? t.Delivered : t.GiveDelivered) <= .25 + 1e-9), "a seller short after production delivers only what it made");
        double paidFood = srec.Trades.Sum(t => t.Give == TradeGood.Food && t.Requester == buyer.Id ? t.GiveQuantity : t.Want == TradeGood.Food && t.Responder == buyer.Id ? t.WantQuantity : 0);
        Near(sw.Camps.Sum(c => c.Food), food0, "pay on delivery conserves food");
        Near(seller.Food, seller0 + paidFood, "the seller is paid what the records say", 1e-6); Near(buyer.Food, buyer0 - paidFood, "the rest of the escrow is refunded to the buyer", 1e-6);
        Check(paidFood > 0 && paidFood < escrowedFood - 1e-6 && srec.Trades.All(t => Math.Abs(t.Delivered - t.GiveDelivered) < 1e-9), "a short delivery is paid in part, both legs at one fraction (" + paidFood + " of " + escrowedFood + ")");
        // The lagged labour price is recorded by the Value planner only while trade is enabled.
        var pw = OrderWorld(fabricOnly: false); foreach (var c in pw.Camps) c.EconomicLabor = null;
        var pt = TurnPlanner.Prepare(pw, new Dictionary<int, List<Order>>());
        var intents = pt.Orders.Where(o => o.Kind == OrderKind.Economy && o.Economy?.Labor != null).ToArray();
        Check(intents.Length > 0 && intents.All(o => o.Economy.Labor.LabourPriceSet && o.Economy.Labor.LabourPrice >= 0), "the Value planner records its labour price while trade is on");
        var po = OrderWorld(fabricOnly: false); po.Config.TradeEnabled = false; foreach (var c in po.Camps) c.EconomicLabor = null;
        Check(TurnPlanner.Prepare(po, new Dictionary<int, List<Order>>()).Orders.Where(o => o.Kind == OrderKind.Economy && o.Economy?.Labor != null).All(o => !o.Economy.Labor.LabourPriceSet), "no labour price is recorded while trade is off");
        // Determinism with made-to-order trade: scalar against reversed 4-worker partitions over three turns.
        var worlds = new[] { OrderWorld(), OrderWorld() }; worlds[1].Camps.Reverse(); worlds[1].Societies.Reverse();
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 2, true, 1) };
        for (int t = 0; t < 3; t++)
        {
            for (int k = 0; k < 2; k++) Advance(worlds[k], new TurnResolver(workers[k]));
            Check(SaveCodec.Fingerprint(worlds[0]) == SaveCodec.Fingerprint(worlds[1]), "made-to-order trade replays identically across workers and order after turn " + t);
        }
        // Older trade records load with both legs fully delivered and nothing made to order.
        var tr = new DataContractSerializer(typeof(TradeRecord)); string xml;
        using (var stream = new MemoryStream()) { tr.WriteObject(stream, new TradeRecord { Requester = 1, GiveDelivered = .5, WantMadeToOrder = true }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        string legacy = Regex.Replace(xml, "<(GiveDelivered|WantMadeToOrder|GiveMadeToOrder)>[^<]*</\\1>", "");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) { var back = (TradeRecord)tr.ReadObject(stream); Check(back.GiveDelivered == 1 && !back.WantMadeToOrder && !back.GiveMadeToOrder && back.Requester == 1, "an older trade record loads with the give leg delivered"); }
    }

    // The provisional Pots value (accepted 4 October 2026 until a planner publishes Pots): with a labour price every made good
    // trades to order, and the herder sells Pots it will make to the farmer. The herder's floor for Pots paid in food, about
    // 90 food a Pot in this fixture, is the accepted value; what the farmer pays with depends on the other goods' curves.
    private static void Pots()
    {
        var w = OrderWorld(fabricOnly: false); var cfg = w.Config; var plan = Trade.Plan(w);
        var farmer = CampOf(w, 1); var herder = CampOf(w, 2);
        Check(plan.Agreements.Any(a => a.Requester == farmer.Id && a.Responder == herder.Id && a.Want == TradeGood.Pots && a.WantMade > 0), "the farmer buys Pots the herder makes to order");
        Check(plan.Agreements.Select(a => a.Want).Concat(plan.Agreements.Select(a => a.Give)).Distinct().Count() >= 2, "with a labour price made goods trade to order");
        var hv = Trade.Value(w, herder); double q = 5, distance = w.Map.Distance(farmer.Tile, herder.Tile);
        Check(hv.Stock(TradeGood.Pots) >= q, "fixture: the herder can make five Pots to order");
        double transport = cfg.TradeDistanceCostPerHex * distance;
        // With spare making capacity a Pot costs its making cost; once that capacity is spent (or from stock with none), a Pot
        // costs its provisional use value. The use value is the accepted ~90 food a Pot (the census-era fixture's price, which
        // followed an earlier agreement that had used up the herder's capacity).
        double made = Trade.MinimumRatio(hv, TradeGood.Pots, TradeGood.Food, q, cfg.TradeMargin, transport, double.PositiveInfinity);
        var full = OrderWorld(fabricOnly: false); var fh = CampOf(full, 2); fh.Inventory.Pots = 50; fh.EconomicLabor = null;
        var fv = Trade.Value(full, fh);
        Check(fv.Capacity[(int)TradeGood.Pots] == 0 && fv.Stock(TradeGood.Pots) >= q, "fixture: a herder with Pots in stock and no making capacity");
        double used = Trade.MinimumRatio(fv, TradeGood.Pots, TradeGood.Food, q, cfg.TradeMargin, transport, double.PositiveInfinity);
        Check(used > 60 && used < 120, "a Pot from stock is priced at its provisional use value, about 90 food (" + used + ")");
        Check(made > 0 && made < used / 5, "a Pot made with spare capacity costs far less, its making cost (" + made + " food)");
        // Make-to-order capacity is bounded by the raw materials the camp holds or can reach: no Clay, no Pots.
        var dry = OrderWorld(fabricOnly: false); CampOf(dry, 2).Inventory.Clay = 0;
        Check(Trade.Value(dry, CampOf(dry, 2)).Capacity[(int)TradeGood.Pots] == 0, "a camp with no Clay to reach cannot make Pots to order");
        CampOf(dry, 2).Inventory.Clay = 12;
        Check(Trade.Value(dry, CampOf(dry, 2)).Capacity[(int)TradeGood.Pots] <= 12 / dry.Config.Neolithic.ClayPerPot + 1e-9, "Pots to order are bounded by the Clay in reach");
    }

    // Claim-first production (Docs/PlannerTradeStorageDesign.md §2 P4): an agreed export takes tool time before the camp's own
    // recipes, in the resolver and the forecast. Census C1 turn 52: Weaver Cloth took all tool time and owed Farm Tools got none.
    private static void ClaimFirst()
    {
        var w = World((6, 6, 0, 0)); var cfg = w.Config; var c = CampOf(w, 1);
        double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals, owed = 10, outputs = 20 * ToolCoverageBudget.OutputsPerTool;
        c.Inventory.HandTools = 0; c.Inventory.Tools = 20; c.Inventory.Wood = 500; c.Inventory.Cloth = 0;
        c.Workforce.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Craftsperson, Population = 40 } };
        var recipes = NeolithicProduction.Recipes(cfg);
        Check(NeolithicProduction.Priority(c, CraftGood.Cloth) > NeolithicProduction.Priority(c, CraftGood.FarmTools), "fixture: Cloth outranks Farm Tools");
        // Resolver: the prepared plan wants far more Cloth than the tool time covers, and owes Farm Tools for export.
        var needs = new TurnNeeds { Camp = c.Id, Residents = c.Population, Exports = new double[Enum.GetValues(typeof(CraftGood)).Length], Production = recipes.ToDictionary(r => r.Good, r => 0d) };
        needs.Exports[(int)CraftGood.FarmTools] = owed; needs.Production[CraftGood.Cloth] = 1000; needs.Production[CraftGood.FarmTools] = owed;
        var record = new TurnRecord { Turn = w.Turn };
        NeolithicProduction.ResolveCampNeolithic(w, c, intervals, years, record, 0, needs, false);
        double Made(CraftGood g) => record.Production.Where(x => x.Camp == c.Id && x.Good == g).Sum(x => x.Output);
        Near(Made(CraftGood.FarmTools), owed, "the owed Farm Tools are made before the camp's own Cloth", 1e-9);
        Near(Made(CraftGood.Cloth) + Made(CraftGood.FarmTools), outputs, "the tool time was binding (Cloth took the rest)", 1e-6);
        Check(c.TradeOwed == null && c.TradeExports == null, "the export claim is transient");
        // Forecast: the same camp's own clothing need would take every tool output; the export is still planned.
        var f = World((6, 6, 0, 0)); var fc = CampOf(f, 1);
        fc.Inventory.HandTools = 0; fc.Inventory.Tools = 20; fc.Inventory.Wood = 500; fc.Inventory.Cloth = 0;
        fc.Workforce.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Craftsperson, Population = 40 } };
        fc.TradeExports = new double[Enum.GetValues(typeof(CraftGood)).Length]; fc.TradeExports[(int)CraftGood.FarmTools] = owed;
        var planned = MaterialEconomy.Forecast(f, fc, recipes, intervals, years, new ToolCoverageBudget(f, fc, intervals).Remaining, true, -1, false, false);
        Check(planned[CraftGood.FarmTools] >= owed - 1e-9, "the forecast plans the owed Farm Tools first (" + planned[CraftGood.FarmTools] + ")");
        // Toolmaking in the first layer adds tool outputs: the equipment work is bounded by start tools plus those made.
        double toolOutputs = outputs + planned[CraftGood.HandTools] * ToolCoverageBudget.OutputsPerHandTool + planned[CraftGood.Tools] * ToolCoverageBudget.OutputsPerTool;
        Near(planned[CraftGood.Cloth] + planned[CraftGood.FarmTools], toolOutputs, "the forecast's Cloth takes the remaining tool time", 1e-6);
        // Make-to-order capacity is bounded by made inputs on hand: no tents promised without Animal Fabric.
        var o = OrderWorld(fabricOnly: false); var herder = CampOf(o, 2);
        Check(herder.Inventory.AnimalFabric == 0 && Trade.Value(o, herder).Capacity[(int)TradeGood.LightHousing] == 0, "a camp without Animal Fabric cannot make tents to order");
        herder.Inventory.AnimalFabric = 7;
        Check(Trade.Value(o, herder).Capacity[(int)TradeGood.LightHousing] <= 7 / o.Config.Neolithic.AnimalFabricPerLightHousing + 1e-9, "tents to order are bounded by the fabric held");
    }

    // Make-or-buy numerics with a vanishing need (census C1 turn 52: a tent maker holding 1.8e-10 tents, so N = 1.8e-10).
    // Shifting the curve left by the making capacity must not underflow V to zero and then integrate 0 * e^{+huge} = NaN:
    // the loss of giving is finite, priced at the making cost, and matches a numeric integral.
    private static void Numerics()
    {
        foreach (double n in new[] { 1.7736e-10, 1e-3, 5 })
        {
            var v = new Trade.Valuation(); int i = (int)TradeGood.LightHousing;
            v.Active[i] = true; v.Curves[i] = new ExponentialCurve(91.1743, n, 3); v.Held[i] = n; v.Cost[i] = .254614; v.Capacity[i] = 436.119;
            v.ToolPool = 52.4757; v.UsesTools[i] = true; v.SetCurve("Tents", n, double.NegativeInfinity);
            double q = 52.4757, loss = v.Loss(TradeGood.LightHousing, q), gain = v.Gain(TradeGood.LightHousing, 10);
            Check(!double.IsNaN(loss) && !double.IsInfinity(loss) && !double.IsNaN(gain) && !double.IsInfinity(gain), "make-or-buy loss and gain are finite with N = " + n + " (" + loss + ", " + gain + ")");
            double numeric = 0; int steps = 200000;
            for (int k = 0; k < steps; k++) numeric += v.LossMarginal(TradeGood.LightHousing, n - q + (k + .5) * q / steps) * q / steps;
            Near(loss, numeric, "the make-or-buy loss matches a numeric integral with N = " + n, 1e-4);
            Check(loss >= .254614 * (q - 52.4757) - 1e-9 && loss <= 91.1743 * q, "the loss lies between the making cost and the use value");
            // The closed-form inverses agree with the integral on the shifted segments.
            double down = ValueCurve.InverseDown(v.W(TradeGood.LightHousing), n, loss / 2);
            Near(v.Loss(TradeGood.LightHousing, down), loss / 2, "InverseDown inverts the shifted loss with N = " + n, 1e-6);
            double up = ValueCurve.InverseUp(v.W(TradeGood.LightHousing), n - q, loss / 2);
            Near(ValueCurve.Integral(v.W(TradeGood.LightHousing), n - q, up), loss / 2, "InverseUp inverts the shifted integral with N = " + n, 1e-6);
        }
    }

    private static TradeValueSchedule Bands(TradeGood g, double value, double end, double offset = 0, double floor = 0)
        => new TradeValueSchedule { Good = g, Ends = new[] { end }, Values = new[] { value }, Offset = offset, Floor = floor };
    // A fulfilment rule that publishes one schedule per call (planner-hook fixture).
    private sealed class PublishingRule : IFulfilmentRule, ITradeValuePublisher
    {
        public int Calls;
        public string Name => "Publishing";
        public void Shares(IReadOnlyList<ProductionClaim> claims, double s, double[] shares) => UniformFulfilment.Instance.Shares(claims, s, shares);
        public IReadOnlyList<TradeValueSchedule> TradeValues(WorldState w, Camp c, CampProductionDemand demand, double position)
        { Calls++; return new[] { Bands(TradeGood.Pots, 1.5, 20 + c.Population) }; }
    }
    // Planner-published values (Docs/Trade.md): every good's curve can come from the planner instead of its provisional
    // builder; nothing published leaves trade unchanged.
    private static void Published()
    {
        string Canon(TradePlan x) => string.Join(";", x.Agreements.Select(a => a.Requester + ">" + a.Responder + ":" + a.Want + ":" + a.Give + ":" + a.WantQuantity.ToString("R") + ":" + a.GiveQuantity.ToString("R")));
        var plain = OrderWorld(fabricOnly: false); var empty = OrderWorld(fabricOnly: false);
        foreach (var c in empty.Camps) if (c.EconomicLabor != null) c.EconomicLabor.TradeValues = new List<TradeValueSchedule>();
        Check(Canon(Trade.Plan(plain)) == Canon(Trade.Plan(empty)) && Trade.Plan(plain).Agreements.Count > 0, "an empty published list leaves the handshake unchanged");
        Check(Trade.EveryKeyBuilt, "every curve key has a provisional builder");
        // Each good in turn: a schedule for its key replaces the provisional curve (shared by the key's goods), prices a
        // good that is otherwise unpriced, and leaves every other key provisional.
        foreach (var d in Trade.Registry)
        {
            var w = Complementary(); w.Config.TradeFoodEnabled = true; var c = CampOf(w, 1);
            double keyHeld = Trade.Registry.Where(x => x.Curve == d.Curve).Sum(x => Trade.Held(c, x.Id)), offset = 3;
            c.EconomicLabor = new EconomicLaborPlan { TradeValues = new List<TradeValueSchedule> { Bands(d.Id, 2.5, keyHeld + offset + 40, offset) } };
            var v = Trade.Value(w, c); double pos = v.Position(d.Id);
            Check(v.Active[(int)d.Id] && v.Curve(d.Id) is BandCurve && Trade.Registry.Where(x => x.Curve == d.Curve).All(x => ReferenceEquals(v.Curve(x.Id), v.Curve(d.Id))), d.Id + " is priced by its published schedule, shared by its curve key");
            Near(pos, keyHeld + offset, d.Id + "'s position is its key's held units plus the published offset");
            Near(v.Marginal(d.Id, pos), 2.5, d.Id + "'s marginal value is the published band"); Check(v.Marginal(d.Id, keyHeld + offset + 41) == 0, d.Id + " is worth nothing beyond the published bands");
            Near(v.Stock(d.Id), Trade.Held(c, d.Id), d.Id + " may give everything above the published floor");
            if (d.Curve != "Clothing") Check(v.Curve(TradeGood.Cloth) is ExponentialCurve, "publishing " + d.Id + " leaves clothing provisional");
            else Check(!v.Active[(int)TradeGood.HandTools], "publishing clothing leaves Hand Tools unpriced");
        }
        // The need-curve form and a floor.
        {
            var w = Complementary(); var c = CampOf(w, 2);
            c.EconomicLabor = new EconomicLaborPlan { TradeValues = new List<TradeValueSchedule> { new TradeValueSchedule { Good = TradeGood.AnimalFabric, Exponential = true, V = 4, N = 30, K = 2 }, Bands(TradeGood.HousingMaterial, 1, 500, 0, 50) } };
            var v = Trade.Value(w, c);
            Near(v.Marginal(TradeGood.Cloth, 10), 4 * Math.Exp(-2 * 10 / 30.0), "a published need curve V e^{-K x/N} prices the clothing key");
            Near(v.Stock(TradeGood.HousingMaterial), c.Inventory.HousingMaterial - 50, "nothing below the published floor is given");
        }
        // A published make cost replaces the derived cost and lets a camp without a labour price make to order.
        {
            var priced = OrderWorld(); var vp = Trade.Value(priced, CampOf(priced, 2)); var curve = (ExponentialCurve)vp.Curve(TradeGood.AnimalFabric);
            int i = (int)TradeGood.AnimalFabric;
            TradeValueSchedule Fabric(bool cost, double makeCost) => new TradeValueSchedule { Good = TradeGood.AnimalFabric, Exponential = true, V = curve.V, N = curve.N, K = curve.K, MakeCostSet = cost, MakeCost = makeCost };
            var unpriced = OrderWorld(); foreach (var c in unpriced.Camps) c.EconomicLabor = new EconomicLaborPlan();
            var herder = CampOf(unpriced, 2);
            herder.EconomicLabor.TradeValues = new List<TradeValueSchedule> { Fabric(false, 0) };
            Check(Trade.Value(unpriced, herder).Capacity[i] == 0 && double.IsPositiveInfinity(Trade.Value(unpriced, herder).Cost[i]), "without a labour price or a published cost nothing is made to order");
            herder.EconomicLabor.TradeValues = new List<TradeValueSchedule> { Fabric(true, vp.Cost[i]) };
            var vu = Trade.Value(unpriced, herder);
            Near(vu.Cost[i], vp.Cost[i], "a published make cost is the made-to-order cost"); Near(vu.Capacity[i], vp.Capacity[i], "the capacity is derived as before");
            Check(Trade.Plan(unpriced).Agreements.Any(a => a.Responder == herder.Id && a.Want == TradeGood.AnimalFabric && a.WantMade > 0), "an unpriced camp sells fabric made to order at its published cost");
            CampOf(priced, 2).EconomicLabor.TradeValues = new List<TradeValueSchedule> { Fabric(true, 2 * vp.Cost[i]) };
            Near(Trade.Value(priced, CampOf(priced, 2)).Cost[i], 2 * vp.Cost[i], "a published make cost overrides the labour-price derivation");
        }
        // Validation: malformed schedules are rejected by intent validation and ignored by trade.
        {
            var bad = new[]
            {
                Bands(TradeGood.Pots, -1, 10), Bands(TradeGood.Pots, double.NaN, 10), Bands(TradeGood.Pots, 1, 10, -1),
                new TradeValueSchedule { Good = TradeGood.Pots, Ends = new[] { 10.0, 5 }, Values = new[] { 2.0, 1 } },
                new TradeValueSchedule { Good = TradeGood.Pots, Ends = new[] { 5.0, 10 }, Values = new[] { 1.0, 2 } },
                new TradeValueSchedule { Good = TradeGood.Pots, Exponential = true, V = 1, N = 0, K = 1 },
                new TradeValueSchedule { Good = (TradeGood)99, Ends = new[] { 1.0 }, Values = new[] { 1.0 } },
                new TradeValueSchedule { Good = TradeGood.Pots, Ends = new[] { 1.0 }, Values = new[] { 1.0 }, MakeCostSet = true, MakeCost = -1 },
            };
            foreach (var s in bad) Check(s.Error() != null, "a malformed schedule is invalid: " + s.Good);
            Check(TradeValueSchedule.Error(new[] { Bands(TradeGood.Cloth, 1, 10), Bands(TradeGood.AnimalFabric, 1, 10) }) != null, "two schedules for one curve key are invalid");
            Check(TradeValueSchedule.Error(new[] { Bands(TradeGood.Cloth, 1, 10), Bands(TradeGood.Pots, 1, 10) }) == null, "schedules for distinct keys are valid");
            var constraints = new EconomicConstraints { Agriculture = true, Population = 80, Technologies = new string[0], FocusTiles = new int[0] };
            var intent = new EconomicIntent { Labor = new EconomicLaborPlan { TradeValues = new List<TradeValueSchedule> { Bands(TradeGood.Pots, 1, 10) } } };
            Check(EconomicIntents.Validate(constraints, intent) == null, "an intent with valid trade values validates");
            intent.Labor.TradeValues.Add(bad[0]);
            Check(EconomicIntents.Validate(constraints, intent)?.Contains("trade values") == true, "an intent with invalid trade values is rejected");
            var w = OrderWorld(fabricOnly: false); var c = CampOf(w, 1);
            c.EconomicLabor.TradeValues = new List<TradeValueSchedule> { Bands(TradeGood.Pots, -1, 10) };
            Check(Canon(Trade.Plan(w)) == Canon(Trade.Plan(OrderWorld(fabricOnly: false))), "trade ignores a malformed schedule and keeps the provisional curve");
        }
        // The copy is deep, and the schedules persist with the plan; an older plan loads with none.
        {
            var plan = new EconomicLaborPlan { LabourPriceSet = true, LabourPrice = .05, TradeValues = new List<TradeValueSchedule> { Bands(TradeGood.Pots, 1, 10) } };
            var copy = plan.Copy(); copy.TradeValues[0].Values[0] = 9; copy.TradeValues.Clear();
            Check(plan.TradeValues.Count == 1 && plan.TradeValues[0].Values[0] == 1, "copying a plan copies its schedules");
            var w = OrderWorld(fabricOnly: false); CampOf(w, 2).EconomicLabor.TradeValues = new List<TradeValueSchedule> { Bands(TradeGood.Pots, .5, 300), new TradeValueSchedule { Good = TradeGood.Cloth, Exponential = true, V = 2, N = 40, K = 2, MakeCostSet = true, MakeCost = .3 } };
            var loaded = SaveCodec.Load(SaveCodec.Save(w)); var back = CampOf(loaded, 2).EconomicLabor.TradeValues;
            Check(back != null && back.Count == 2 && back[0].Good == TradeGood.Pots && back[0].Values[0] == .5 && back[1].Exponential && back[1].MakeCostSet && back[1].MakeCost == .3, "published schedules round trip");
            Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded) && Canon(Trade.Plan(w)) == Canon(Trade.Plan(loaded)), "a loaded world prices and trades identically");
            var serializer = new DataContractSerializer(typeof(EconomicLaborPlan)); string xml;
            using (var stream = new MemoryStream()) { serializer.WriteObject(stream, plan); xml = Encoding.UTF8.GetString(stream.ToArray()); }
            string legacy = Regex.Replace(xml, "<TradeValues>.*?</TradeValues>", "", RegexOptions.Singleline);
            Check(legacy != xml && !legacy.Contains("TradeValues"), "legacy plan fixture omits the schedules");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) { var old = (EconomicLaborPlan)serializer.ReadObject(stream); Check(old.TradeValues == null && old.LabourPriceSet && old.LabourPrice == .05, "an older plan loads with no published values"); }
        }
        // The planner hook: a publishing rule's schedules are recorded on the plan while trade is on, and the next
        // handshake reads them from the committed plan.
        {
            var w = OrderWorld(fabricOnly: false); var c = CampOf(w, 1); var rule = new PublishingRule();
            var intent = EconomicPlanner.Plan(w, c, EconomicIntent.From(c), rule, out _, out _);
            Check(rule.Calls == 1 && intent.Labor?.TradeValues?.Count == 1 && intent.Labor.TradeValues[0].Good == TradeGood.Pots && intent.Labor.TradeValues[0].Ends[0] == 20 + c.Population, "a publishing rule's values are recorded on its plan");
            EconomicIntents.Apply(c, intent);
            var v = Trade.Value(w, c); Near(v.Marginal(TradeGood.Pots, v.Position(TradeGood.Pots)), 1.5, "the next handshake prices Pots from the published plan");
            var off = OrderWorld(fabricOnly: false); off.Config.TradeEnabled = false; var oc = CampOf(off, 1); var offRule = new PublishingRule();
            Check(EconomicPlanner.Plan(off, oc, EconomicIntent.From(oc), offRule, out _, out _).Labor?.TradeValues == null && offRule.Calls == 0, "nothing is published while trade is off");
        }
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

    // Diagnostic (--trade-resolve=<save>,<turns>): advance with AI orders and trade on; on an invariant failure, name the
    // camps whose inventory or needs left the valid range, with their trade records this turn.
    public static void ResolveProbe(string path, int turns)
    {
        var w = SaveCodec.Load(File.ReadAllBytes(path)); w.Config.TradeEnabled = true; Ownership.Bind(w);
        for (int t = 0; t < turns; t++)
        {
            var ai = AiPlanning.Decide(w); var prepared = TurnPlanner.Prepare(w, ai.Orders, ai.Traces);
            var before = w.Camps.ToDictionary(c => c.Id, c => (c.Inventory.Cloth, c.Inventory.AnimalFabric, c.Inventory.HousingMaterial, c.Food, c.ClothPerCapita));
            // Flows of one curve between one pair, by direction: a pair moving a curve both ways is a wash.
            var flows = prepared.Trade.Agreements.SelectMany(g => new[] { (From: g.Responder, To: g.Requester, Curve: Trade.Descriptor(g.Want).Curve), (From: g.Requester, To: g.Responder, Curve: Trade.Descriptor(g.Give).Curve) }).ToArray();
            int wash = flows.Count(f => f.From < f.To && flows.Any(o => o.From == f.To && o.To == f.From && o.Curve == f.Curve));
            // TRADE_SHORT=1: list made-to-order legs delivered short, with the seller's planned and made output.
            if (Environment.GetEnvironmentVariable("TRADE_SHORT") == "1")
            {
                new TurnResolver().Resolve(w, prepared); var rec = w.History.Last();
                var legs = rec.Trades.SelectMany(x => new[] { (M: x.WantMadeToOrder, F: x.Delivered, G: x.Want), (M: x.GiveMadeToOrder, F: x.GiveDelivered, G: x.Give) }).Where(x => x.M).ToArray();
                Console.WriteLine($"made-to-order legs {legs.Length}, short {legs.Count(x => x.F < .999)}, mean delivered {(legs.Length > 0 ? legs.Average(x => x.F) : 1):F3}; by good " + string.Join(", ", legs.GroupBy(x => x.G).Select(gr => gr.Key + " " + gr.Count() + "/" + gr.Average(x => x.F).ToString("F2"))));
                foreach (var g in rec.Trades.SelectMany(x => new[] { (Seller: x.Responder, Good: x.Want, Q: x.WantQuantity, F: x.Delivered, M: x.WantMadeToOrder), (Seller: x.Requester, Good: x.Give, Q: x.GiveQuantity, F: x.GiveDelivered, M: x.GiveMadeToOrder) }).Where(x => x.M).GroupBy(x => (x.Seller, x.Good)).Take(400))
                {
                    double owed = prepared.Needs[g.Key.Seller].Exports?[(int)Trade.Descriptor(g.Key.Good).Recipe.Value] ?? 0, f = g.Min(x => x.F);
                    if (f > .999) continue;
                    var made = rec.Production.Where(p => p.Camp == g.Key.Seller && p.Good == Trade.Descriptor(g.Key.Good).Recipe.Value).Sum(p => p.Output);
                    var planned = prepared.Needs[g.Key.Seller].Production?[Trade.Descriptor(g.Key.Good).Recipe.Value] ?? 0;
                    var camp = w.Camps.First(c => c.Id == g.Key.Seller);
                    if (Environment.GetEnvironmentVariable("TRADE_SELLER") == g.Key.Seller.ToString())
                    {
                        var atStart = SaveCodec.Load(File.ReadAllBytes(path)); Ownership.Bind(atStart); atStart.Config.TradeEnabled = true; var sc = atStart.Camps.First(x => x.Id == g.Key.Seller);
                        var recipe = NeolithicProduction.Recipes(atStart.Config).First(x => x.Good == Trade.Descriptor(g.Key.Good).Recipe.Value);
                        var src = MaterialEconomy.KnownSources(atStart, sc, atStart.Days);
                        var val = Trade.Value(atStart, sc, Trade.LastProduction(atStart).GetValueOrDefault(sc.Id));
                        Console.WriteLine($"seller {sc.Id}: recipe wood {recipe.Wood} clay {recipe.Clay} construction {recipe.ConstructionMaterial} plant {recipe.PlantFood} animal {recipe.AnimalFood} rate {recipe.Rate}; inventory wood {sc.Inventory.Wood:F1} clay {sc.Inventory.Clay:F1}; sources wood {src.Where(x => x.Material == MaterialId.Wood).Sum(x => x.Available):F1} clay {src.Where(x => x.Material == MaterialId.Clay).Sum(x => x.Available):F1}; capacity {val.Capacity[(int)g.Key.Good]:F1} cost {val.Cost[(int)g.Key.Good]:F3} labour {ClaimValuation.LabourPerUnit(atStart, sc, recipe, NeolithicProduction.Recipes(atStart.Config), NeolithicProduction.HousingMultiplier(atStart, sc, sc.Population)):F3} tools {sc.Inventory.HandTools:F1}/{sc.Inventory.Tools:F1}");
                    }
                    Console.WriteLine($"short {g.Key.Good} seller {g.Key.Seller} {camp.Technology} owed {owed:F1} fraction {f:F2} planned {planned:F1} made {made:F1} assign {string.Join(",", camp.Workforce.Assignments.Select(a => a.Role + "=" + a.Population))}");
                }
                return;
            }
            try { new TurnResolver().Resolve(w, prepared); var tr = w.History.Last().Trades; Console.WriteLine("turn " + w.Turn + " ok, trades " + tr.Count + " pair washes " + wash + " | " + string.Join(", ", tr.GroupBy(x => x.Want + "<-" + x.Give).Select(gr => gr.Key + " " + gr.Count()))); }
            catch (InvalidOperationException e)
            {
                Console.WriteLine("turn failed: " + e.Message);
                foreach (var c in w.Camps)
                {
                    var bad = typeof(CampInventory).GetFields().Where(f => f.FieldType == typeof(double)).Select(f => (f.Name, V: (double)f.GetValue(c.Inventory))).Where(x => double.IsNaN(x.V) || double.IsInfinity(x.V) || x.V < -1e-8).Select(x => x.Name + "=" + x.V)
                        .Concat(new[] { ("PlantFood", c.PlantFood), ("AnimalFood", c.AnimalFood), ("AquaticFood", c.AquaticFood), ("HousingDemand", c.HousingDemand), ("FabricConsumed", c.FabricConsumed) }.Where(x => double.IsNaN(x.Item2) || double.IsInfinity(x.Item2) || x.Item2 < -1e-8).Select(x => x.Item1 + "=" + x.Item2))
                        .Concat(new[] { ("QoL", c.QoL), ("Food", c.FoodNeedsMetRatio), ("Water", c.WaterNeedsMetRatio), ("Cloth", c.ClothPerCapita), ("Housing", c.HousingPerCapita), ("E", c.RudimentaryExpectation) }.Where(x => double.IsNaN(x.Item2) || x.Item2 < 0 || x.Item2 > 1 + 1e-8).Select(x => x.Item1 + "=" + x.Item2)).ToArray();
                    if (bad.Length == 0) continue;
                    Console.WriteLine($"camp {c.Id} pop {c.Population} tech {c.Technology}: {string.Join(" ", bad)}; before {before.GetValueOrDefault(c.Id)}; exports {string.Join(",", prepared.Needs.TryGetValue(c.Id, out var nd) && nd.Exports != null ? nd.Exports : new double[0])}");
                    foreach (var a in prepared.Trade.Agreements.Where(a => a.Requester == c.Id || a.Responder == c.Id))
                        Console.WriteLine($"   {a.Requester}<-{a.Responder} want {a.Want} {a.WantQuantity:F2} give {a.Give} {a.GiveQuantity:F2}");
                }
                return;
            }
        }
    }

    // Diagnostic (--trade-profile=<save>): time each handshake stage on a real state, with work counts.
    public static void ProfileProbe(string path)
    {
        var w = SaveCodec.Load(File.ReadAllBytes(path)); w.Config.TradeEnabled = true; Ownership.Bind(w);
        foreach (var workers in new[] { new PhaseExecution(), new PhaseExecution(4, 16, false, 1) })
        {
            double best = double.PositiveInfinity; string detail = "";
            for (int rep = 0; rep < 6; rep++)
            {
                Trade.Counters.Reset();
                var clock = System.Diagnostics.Stopwatch.StartNew(); var plan = Trade.Plan(w, workers); double total = clock.Elapsed.TotalMilliseconds;
                if (rep > 0 && total < best) { best = total; detail = $"camps {w.Camps.Count} eligible {plan.Summary.Eligible} requisitions {plan.Summary.Requisitions} agreements {plan.Agreements.Count} | " + Trade.Counters.Describe(); }
            }
            Console.WriteLine($"workers {workers.Workers}: best {best:F1} ms; {detail}");
        }
    }

    // Diagnostic (--trade-roles=<save>): resolve one turn with trade on and off from the same state and compare clothing,
    // housing, QoL and food by trade role (buyer / seller of goods, non-trader).
    public static void RolesProbe(string path)
    {
        WorldState Run(bool trade)
        {
            var w = SaveCodec.Load(File.ReadAllBytes(path)); w.Config.TradeEnabled = trade; Ownership.Bind(w);
            var ai = AiPlanning.Decide(w); new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, ai.Orders, ai.Traces)); return w;
        }
        var on = Run(true); var off = Run(false);
        var rec = on.History.Last();
        var buyers = new HashSet<int>(); var sellers = new HashSet<int>();
        foreach (var t in rec.Trades)
        {
            if (t.WantMadeToOrder) { buyers.Add(t.Requester); sellers.Add(t.Responder); }
            if (t.GiveMadeToOrder) { buyers.Add(t.Responder); sellers.Add(t.Requester); }
        }
        var offCamps = off.Camps.ToDictionary(c => c.Id);
        foreach (var (name, pick) in new (string, Func<Camp, bool>)[] { ("buyers only", c => buyers.Contains(c.Id) && !sellers.Contains(c.Id)), ("sellers only", c => sellers.Contains(c.Id) && !buyers.Contains(c.Id)), ("both", c => buyers.Contains(c.Id) && sellers.Contains(c.Id)), ("neither", c => !buyers.Contains(c.Id) && !sellers.Contains(c.Id)) })
        {
            var cs = on.Camps.Where(c => offCamps.ContainsKey(c.Id) && pick(c)).ToArray();
            if (cs.Length == 0) continue;
            double D(Func<Camp, double> f) => cs.Average(c => f(c) - f(offCamps[c.Id]));
            Console.WriteLine($"{name}: n={cs.Length} dCloth {D(c => c.ClothPerCapita):+0.000;-0.000} dHousing {D(c => c.HousingPerCapita):+0.000;-0.000} dQoL {D(c => c.QoL):+0.000;-0.000} dFood {D(c => c.Food):+0.0;-0.0} dPop {D(c => c.Population):+0.00;-0.00} cult {cs.Count(c => c.Technology == Technology.Cultivation)}");
        }
    }
}
