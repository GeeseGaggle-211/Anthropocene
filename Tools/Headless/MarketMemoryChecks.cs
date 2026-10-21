using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Anthropocene.Simulation;

// Contract 13 simulator side (Docs/ElderModel9ArbitrageDesign.md): market memory, the demand-scaled resale band, carrying
// and the knowledge-exposure hook, all behind SimulationConfig.MarketMemoryEnabled. Owns its worlds and counters (lanes run
// checks concurrently).
internal static class MarketMemoryChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("MarketMemory: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-9) => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)), message + " (" + a + " vs " + b + ")");

    public static int Run()
    {
        checks = 0;
        Config();
        Curves();
        Chain();
        Decay();
        Exposure();
        Generated();
        Console.WriteLine("PASS: " + checks + " market memory assertions");
        return checks;
    }

    // A uniform fertile strip. Every camp is an AI village of its own Society that remembers every other camp.
    private static WorldState Strip(params (int x, int y, double cloth, double material)[] traders)
        => Strip(false, traders.Select(t => (t.x, t.y, t.cloth, t.material, 0.0, 8000.0)).ToArray());
    private static WorldState Strip(bool food, params (int x, int y, double cloth, double material, double pots, double plantFood)[] traders)
    {
        var cfg = SimulationConfig.Small(1); cfg.TradeEnabled = true; cfg.TradeFoodEnabled = food; cfg.Width = 32; cfg.Height = 16; cfg.AnnualHumanGrowthRate = 0; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0; cfg.ContactExpiryTurns = 50;
        var w = new WorldState { Seed = 303, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 8000, Food = 1000 };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = t;
        }
        var human = new Camp { Id = w.NextId++, Player = 0, Tile = w.Map.At(28, 13), Population = 60, PlantFood = 6000, AnimalFood = 1000, RudimentaryExpectation = .5 };
        w.Camps.Add(human);
        for (int i = 0; i < traders.Length; i++)
        {
            var (x, y, cloth, material, pots, plantFood) = traders[i];
            var c = new Camp { Id = w.NextId++, Player = i + 1, Tile = w.Map.At(x, y), Population = 80, PlantFood = plantFood, AnimalFood = plantFood > 1000 ? 2000 : 0, RudimentaryExpectation = .5, StationaryYears = 40, StationaryTurns = 2, IsVillage = true };
            c.Inventory.Cloth = cloth; c.Inventory.HousingMaterial = material; c.Inventory.Pots = pots;
            w.Camps.Add(c);
        }
        w.Config.Players = traders.Length + 1; w.PlayerCount = traders.Length + 1; w.Turn = 3; w.Days = 1000;
        Ownership.Bind(w);
        foreach (var c in w.Camps)
        {
            c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        }
        foreach (var c in w.Camps) foreach (var o in w.Camps) if (o != c) ContactKnowledge.MemoryOf(w, c.People).Upsert(o.Id, o.Player, o.Tile, w.Turn, ContactSource.Sighting);
        foreach (var t in w.Tiles) t.LastEcologyUpdateDays = w.Days;
        return w;
    }
    // The chain fixture, ten hexes from producer to consumer: producer A (cloth-rich, otherwise sated, so it never requests
    // anything); intermediary B five hexes from each (a little surplus cloth, no Housing Material, spare Pots); consumer C
    // (no cloth, some spare Housing Material it will not sell for Pots). Pots are everyone's cheap surplus.
    private static WorldState Chain3(double consumerCloth)
        => Strip(false, (2, 6, 600, 30, 200, 8000), (7, 6, 0, 0, 200, 8000), (12, 6, consumerCloth, 60, 200, 8000));
    private static Camp Of(WorldState w, int player) => w.Camps.Single(c => c.Player == player);

    // One handshake round without production: plan, the resolver's three settlement steps, the end-of-turn prune.
    private static TurnRecord Round(WorldState w)
    {
        var plan = Trade.Plan(w); var record = new TurnRecord { Turn = w.Turn };
        var s = Trade.Settle(w, plan, record); Trade.SettleAfterProduction(w, s, record); Trade.Finish(w, s, record);
        if (MarketMemorySystem.Enabled(w.Config)) MarketMemorySystem.EndTurn(w);
        w.Turn++; w.Days += w.Config.TurnDays;
        foreach (var c in w.Camps) foreach (var o in w.Camps) if (o != c) ContactKnowledge.MemoryOf(w, c.People).Upsert(o.Id, o.Player, o.Tile, w.Turn, ContactSource.Sighting);
        return record;
    }

    private static void Config()
    {
        var c = new SimulationConfig();
        Check(!c.MarketMemoryEnabled && c.MarketMemory == null, "market memory is off by default");
        string before = SaveCodec.ConfigurationHash(c);
        c.MarketMemory = new MarketMemoryConfig { HalfLifeTurns = 9 };
        Check(SaveCodec.ConfigurationHash(c) == before, "flag off: market levers do not enter the configuration hash");
        c.MarketMemoryEnabled = true;
        string on = SaveCodec.ConfigurationHash(c);
        Check(on != before, "flag on: the configuration hash changes");
        c.MarketMemory.HalfLifeTurns = 3;
        Check(SaveCodec.ConfigurationHash(c) != on, "flag on: the levers are hashed");
        foreach (Action<MarketMemoryConfig> bad in new Action<MarketMemoryConfig>[] { m => m.HalfLifeTurns = 0, m => m.ExpiryTurns = 0, m => m.CarryPerPerson = -1, m => m.CarriedExposureWeight = 2, m => m.CraftExposurePerValue = double.NaN })
        {
            var x = new SimulationConfig { MarketMemoryEnabled = true, MarketMemory = new MarketMemoryConfig() }; bad(x.MarketMemory);
            bool rejected = false; try { x.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "an invalid market lever is rejected");
        }
    }

    // The resale band: max(use, b) below End, use beyond; integrals, inverses and crossings agree with the marginal.
    private static void Curves()
    {
        foreach (ValueCurve baseCurve in new ValueCurve[] { new ExponentialCurve(10, 5, 3), new BandCurve(new[] { 4.0, 9 }, new[] { 6.0, 1 }) })
        {
            var curve = new ResaleCurve(baseCurve, 2, 12);
            // Midpoint rule on pieces split at every kink and jump, so discontinuities fall on piece boundaries.
            double Piece(Func<double, double> f, double a, double b) { int n = 20000; double h = (b - a) / n, sum = 0; for (int i = 0; i < n; i++) sum += f(a + (i + .5) * h); return sum * h; }
            double Numeric(Func<double, double> f, double a, double b)
            {
                var cuts = new[] { a }.Concat(new[] { 0.0, 4, 9, 12 }.Where(x => x > a && x < b)).Concat(new[] { b }).ToArray();
                double sum = 0; for (int i = 0; i + 1 < cuts.Length; i++) sum += Piece(f, cuts[i], cuts[i + 1]); return sum;
            }
            double previous = double.PositiveInfinity;
            for (double x = -2; x < 20; x += .25)
            {
                double m = curve.Marginal(x);
                Check(m <= previous + 1e-12, "the resale curve is nonincreasing at " + x);
                Near(m, x < 12 ? Math.Max(baseCurve.Marginal(x), 2) : baseCurve.Marginal(x), "the marginal is max(use, band) below the end");
                Near(ValueCurve.Marginal(curve.SegmentArrayFor(double.PositiveInfinity), x), m, "the segments carry the marginal at " + x, 1e-9);
                previous = m;
            }
            foreach (var (a, b) in new[] { (0.0, 3.0), (1.0, 11.0), (4.0, 15.0), (11.5, 18.0) })
            {
                Near(curve.Integrated(a, b), Numeric(curve.Marginal, a, b), "integrated resale value over [" + a + ", " + b + "]", 1e-6);
                Near(curve.IntegratedCapped(a, b, 3), Numeric(x => Math.Min(3, curve.Marginal(x)), a, b), "capped integral over [" + a + ", " + b + "]", 1e-6);
                double target = curve.Integrated(a, b);
                Near(curve.Integrated(a, curve.InverseUp(a, target, double.PositiveInfinity)), target, "InverseUp inverts the integral", 1e-7);
                Near(curve.Integrated(b - curve.InverseDown(b, target, double.PositiveInfinity), b), target, "InverseDown inverts the integral", 1e-7);
            }
            Check(curve.Crossing(1.5) >= 12 && curve.Marginal(curve.Crossing(1.5) - 1e-9) >= 1.5, "below the band value the crossing lies at or beyond the band end");
            Near(curve.Crossing(5), baseCurve.Crossing(5), "above the band value the crossing is the use curve's");
            Check(curve.Target == Math.Max(baseCurve.Target, 12), "the target extends to the band end");
            Check(new ResaleCurve(baseCurve, 0, 12).Integrated(1, 7) == baseCurve.Integrated(1, 7) || Math.Abs(new ResaleCurve(baseCurve, 0, 12).Integrated(1, 7) - baseCurve.Integrated(1, 7)) < 1e-9,
                "a zero band is the use curve");
        }
    }

    // A producer A, a village B within reach of both, and a consumer C beyond A's reach. With market memory, B learns C's
    // demand by selling it its surplus cloth, buys cloth from A beyond its own need, and resells it to C. Without it, B
    // never buys beyond its own need. Goods are conserved through every round.
    private static (List<TurnRecord> Records, WorldState World, double ResaleBought, int BandRounds) ChainRun(bool market, double consumerCloth = 0, int rounds = 8)
    {
        var w = Chain3(consumerCloth);
        w.Config.MarketMemoryEnabled = market; if (market) w.Config.MarketMemory = new MarketMemoryConfig();
        var b = Of(w, 2);
        // B holds 25 cloth above its own clothing floor: the surplus it first sells to C.
        b.Inventory.Cloth = Trade.Value(w, b).Floor(TradeGood.Cloth) + 25;
        var records = new List<TurnRecord>(); double resale = 0; int bands = 0;
        double Total(Func<Camp, double> f) => w.Camps.Sum(f);
        for (int t = 0; t < rounds; t++)
        {
            double cloth = Total(c => c.Inventory.Cloth), material = Total(c => c.Inventory.HousingMaterial), food = Total(c => c.Food);
            var before = Trade.Value(w, b);
            double ownTarget = double.IsNaN(before.OwnTarget[(int)TradeGood.Cloth]) ? before.Curve(TradeGood.Cloth).Target : before.OwnTarget[(int)TradeGood.Cloth];
            double position = before.Position(TradeGood.Cloth);
            if (!double.IsNaN(before.OwnTarget[(int)TradeGood.Cloth])) bands++;
            var record = Round(w);
            Near(Total(c => c.Inventory.Cloth), cloth, "cloth is conserved through round " + t + (market ? " (on)" : " (off)"));
            Near(Total(c => c.Inventory.HousingMaterial), material, "housing material is conserved through round " + t);
            Near(Total(c => c.Food), food, "food is conserved through round " + t);
            double bought = record.Trades.Where(x => x.Responder == Of(w, 1).Id && x.Requester == b.Id && x.Want == TradeGood.Cloth).Sum(x => x.WantQuantity)
                + record.Trades.Where(x => x.Requester == Of(w, 1).Id && x.Responder == b.Id && x.Give == TradeGood.Cloth).Sum(x => x.GiveQuantity);
            resale += Math.Max(0, Math.Min(bought, position + bought - ownTarget));
            records.Add(record);
        }
        return (records, w, resale, bands);
    }

    private static void Chain()
    {
        var w0 = Chain3(0);
        int a = Of(w0, 1).Id, b = Of(w0, 2).Id, c = Of(w0, 3).Id, range = w0.Config.TradeRange;
        Check(w0.Map.Distance(Of(w0, 1).Tile, Of(w0, 3).Tile) > range && w0.Map.Distance(Of(w0, 1).Tile, Of(w0, 2).Tile) <= range && w0.Map.Distance(Of(w0, 2).Tile, Of(w0, 3).Tile) <= range,
            "fixture: C is beyond A's trade range, B within reach of both");
        var on = ChainRun(true); var off = ChainRun(false);
        double Flow(List<TurnRecord> records, int from, int to, TradeGood good, int round = -1)
            => records.Where((r, i) => round < 0 || i == round).SelectMany(r => r.Trades).Sum(x =>
                (x.Responder == from && x.Requester == to && x.Want == good ? x.WantQuantity : 0) + (x.Requester == from && x.Responder == to && x.Give == good ? x.GiveQuantity : 0));
        Check(Flow(on.Records, a, c, TradeGood.Cloth) == 0 && Flow(off.Records, a, c, TradeGood.Cloth) == 0, "A never trades with C directly");
        int firstSale = Enumerable.Range(0, on.Records.Count).FirstOrDefault(i => Flow(on.Records, b, c, TradeGood.Cloth, i) > 0, -1);
        Check(firstSale >= 0, "B sells its surplus cloth to C");
        var marketB = on.World.Societies.Single(s => s.Id == Of(on.World, 2).SocietyId).Market;
        Check(marketB != null && marketB.Entries.Any(e => e.Camp == c && e.Good == TradeGood.Cloth && e.TermsTurn >= 0 && e.Quantity > 0 && e.Price > 0),
            "B remembers C's cloth demand and terms");
        int resaleBuy = Enumerable.Range(firstSale + 1, on.Records.Count - firstSale - 1).FirstOrDefault(i => Flow(on.Records, a, b, TradeGood.Cloth, i) > 0, -1);
        Check(resaleBuy > firstSale, "after learning C's demand, B buys cloth from A");
        int resale = Enumerable.Range(resaleBuy, on.Records.Count - resaleBuy).FirstOrDefault(i => Flow(on.Records, b, c, TradeGood.Cloth, i) > 0, -1);
        Check(resale >= resaleBuy, "the cloth bought from A reaches C through B (A -> B -> C)");
        Check(on.BandRounds > 0 && off.BandRounds == 0, "only market memory gives B a resale band");
        Check(on.ResaleBought > 2 * off.ResaleBought + 1e-6, "market memory makes B buy beyond its own need (" + on.ResaleBought.ToString("0.###") + " vs " + off.ResaleBought.ToString("0.###") + " units)");
        Check(Flow(on.Records, b, c, TradeGood.Cloth) > Flow(off.Records, b, c, TradeGood.Cloth) + 1e-6, "more cloth reaches C with market memory than without");
        // Failure case: C is already clothed (no demand), so B never hoards.
        var sated = ChainRun(true, 600);
        Check(sated.BandRounds == 0, "without demand at C, B never values cloth for resale");
        // With no remembered markets the handshake is exactly today's (d = 0 everywhere).
        var fresh = Chain3(0); var twin = Chain3(0);
        twin.Config.MarketMemoryEnabled = true; twin.Config.MarketMemory = new MarketMemoryConfig();
        Check(Canon(Trade.Plan(fresh)) == Canon(Trade.Plan(twin)), "with empty market memory the handshake is exactly the flag-off one");
        Console.WriteLine("  chain fixture: B first sells to C in round " + firstSale + ", buys from A for resale in round " + resaleBuy + ", resells in round " + resale
            + "; resale bought " + on.ResaleBought.ToString("0.##") + "; cloth B->C on/off " + Flow(on.Records, b, c, TradeGood.Cloth).ToString("0.##") + "/" + Flow(off.Records, b, c, TradeGood.Cloth).ToString("0.##"));
        // Information rule: every remembered term is a settled trade this Society took part in.
        foreach (var s in on.World.Societies.Where(s => s.Market != null))
        {
            var mine = on.World.Camps.Where(x => x.SocietyId == s.Id).Select(x => x.Id).ToHashSet();
            foreach (var e in s.Market.Entries)
            {
                Check(!mine.Contains(e.Camp), "a Society remembers only foreign settlements");
                Check(s.Memory.Find(e.Camp) != null, "every market entry is a remembered contact");
                if (e.TermsTurn < 0) continue;
                var record = on.Records.Single(r => r.Turn == e.TermsTurn);
                Check(record.Trades.Any(x => (mine.Contains(x.Requester) && x.Responder == e.Camp && x.Give == e.Good && Math.Abs(x.GiveQuantity - e.Quantity) < 1e-9)
                    || (mine.Contains(x.Responder) && x.Requester == e.Camp && x.Want == e.Good && Math.Abs(x.WantQuantity - e.Quantity) < 1e-9)),
                    "remembered terms are a settled trade the Society took part in");
            }
        }
        // The consumer received made goods: exposure toward Weaving with the flag on, none without.
        double Weaving(WorldState w) => Of(w, 3).People.CraftExperience.TryGetValue(NeolithicTech.Weaving, out double x) ? x : 0;
        Check(Weaving(on.World) > 0 && Weaving(off.World) == 0, "received cloth adds Weaving exposure only with market memory");
    }

    private static string Canon(TradePlan p) => string.Join(";", p.Agreements.Select(a => a.Requester + ">" + a.Responder + ":" + a.Want + "/" + a.Give + ":" + a.WantQuantity.ToString("R") + "/" + a.GiveQuantity.ToString("R")));

    // d falls as markets age and as the camp's own holdings fill the wanted quantity; entries expire and a moved contact
    // drops its entries.
    private static void Decay()
    {
        var run = ChainRun(true, 0, 1);
        var w = run.World; var b = Of(w, 2); int c = Of(w, 3).Id;
        var memory = b.People.Market;
        Check(memory != null && memory.Entries.Any(e => e.Camp == c && e.TermsTurn >= 0), "fixture: B remembers C's terms");
        var cloth = memory.Entries.Single(e => e.Camp == c && e.Good == TradeGood.Cloth);
        int turn = w.Turn, observed = cloth.TermsTurn;
        var demand = new List<double>();
        for (int age = 1; age <= 4; age++)
        {
            w.Turn = observed + age;
            var r = MarketMemorySystem.Inspect(w, b);
            demand.Add(r.TryGetValue(TradeGood.Cloth, out var x) ? x.Demand * x.Price : 0);
        }
        Check(demand[0] > 0 && demand.Zip(demand.Skip(1), (x, y) => y < x).All(z => z), "the resale value falls as the market ages (" + string.Join(", ", demand.Select(x => x.ToString("0.####"))) + ")");
        w.Turn = observed + 1;
        var base0 = MarketMemorySystem.Inspect(w, b)[TradeGood.Cloth];
        double saved = b.Inventory.Cloth;
        b.Inventory.Cloth = saved + .5 * base0.Quantity;
        var filled = MarketMemorySystem.Inspect(w, b)[TradeGood.Cloth];
        Check(filled.Quantity < base0.Quantity && filled.Demand <= base0.Demand + 1e-12, "holdings beyond own need fill the observed want");
        b.Inventory.Cloth = saved + 10 * base0.Quantity + 100;
        Check(!MarketMemorySystem.Inspect(w, b).TryGetValue(TradeGood.Cloth, out var full) || full.Quantity <= 1e-9 || full.Value == 0, "a filled want stops pulling");
        b.Inventory.Cloth = saved;
        // Expiry: no refresh for ExpiryTurns forgets the entry.
        int expiry = w.Config.MarketMemory.ExpiryTurns;
        w.Turn = cloth.LastTurn + expiry; MarketMemorySystem.EndTurn(w);
        Check(b.People.Market == null || !b.People.Market.Entries.Any(e => e.Camp == c && e.Good == TradeGood.Cloth), "an entry not refreshed for ExpiryTurns is forgotten");
        // A contact known to have moved drops its entries.
        var again = ChainRun(true, 0, 1); var bw = again.World; var bb = Of(bw, 2);
        var contact = bb.People.Memory.Find(Of(bw, 3).Id); contact.Moved = true; contact.Tile = -1;
        MarketMemorySystem.EndTurn(bw);
        Check(bb.People.Market == null || !bb.People.Market.Entries.Any(e => e.Camp == Of(bw, 3).Id), "a moved contact drops its market entries");
        w.Turn = turn;
    }

    // Received made goods expose the receiver to the craft that makes them; carried units count at the carried weight;
    // the existing mastery threshold unlocks the technology and its specialist.
    private static void Exposure()
    {
        var w = Strip((2, 6, 0, 0)); w.Config.MarketMemoryEnabled = true; w.Config.MarketMemory = new MarketMemoryConfig();
        var c = Of(w, 1); var recipes = NeolithicProduction.Recipes(w.Config);
        MarketMemorySystem.Expose(w, c, TradeGood.Cloth, 10, 1, recipes);
        Near(c.People.CraftExperience[NeolithicTech.Weaving], 10, "used units credit their full realised value");
        MarketMemorySystem.Expose(w, c, TradeGood.Cloth, 10, 0, recipes);
        Near(c.People.CraftExperience[NeolithicTech.Weaving], 10 + 10 * w.Config.MarketMemory.CarriedExposureWeight, "carried units count less");
        MarketMemorySystem.Expose(w, c, TradeGood.Food, 1000, 1, recipes);
        Check(!c.People.CraftExperience.ContainsKey("Food") && !c.People.HasTech(NeolithicTech.Weaving), "raw and unmade goods expose nothing");
        MarketMemorySystem.Expose(w, c, TradeGood.Cloth, w.Config.Neolithic.CraftMasteryThreshold, 1, recipes);
        Check(c.People.HasTech(NeolithicTech.Weaving) && c.People.UnlockedSpecialists.Contains(WorkforceRoles.Weaver), "exposure reaching the mastery threshold unlocks the craft");
        double held = c.People.CraftExperience[NeolithicTech.Weaving];
        MarketMemorySystem.Expose(w, c, TradeGood.Cloth, 50, 1, recipes);
        Check(c.People.CraftExperience[NeolithicTech.Weaving] == held, "a held technology takes no further exposure");
    }

    private static string Magic(byte[] save)
    {
        using var input = new MemoryStream(save); using var zip = new GZipStream(input, CompressionMode.Decompress); using var reader = new BinaryReader(zip);
        return reader.ReadString();
    }

    private static List<string> Play(WorldState w, int turns, PhaseExecution execution = null)
    {
        var prints = new List<string>(); var resolver = new TurnResolver(execution ?? new PhaseExecution());
        for (int t = 0; t < turns; t++)
        {
            var plan = AiPlanning.Decide(w, execution ?? new PhaseExecution());
            resolver.Resolve(w, plan.Orders, plan.Traces);
            prints.Add(SaveCodec.Fingerprint(w));
        }
        return prints;
    }

    private static WorldState On(byte[] snapshot)
    {
        var w = SaveCodec.Load((byte[])snapshot.Clone()); w.Config.MarketMemoryEnabled = true; w.Config.MarketMemory = new MarketMemoryConfig();
        return w;
    }

    // A generated Small/8 world: flag-off saves are unchanged in format, flag-on play is deterministic across workers and
    // continues exactly through a save, and older saves load with the flag off.
    private static void Generated()
    {
        var snapshot = ElderModel9Environment.GenerateOpening(17, 8, out _);
        var plain = SaveCodec.Load((byte[])snapshot.Clone());
        Check(!plain.Config.MarketMemoryEnabled && plain.Societies.All(s => s.Market == null), "an older save loads with market memory off");
        Check(Magic(SaveCodec.Save(plain)) != "ANTHROPOCENE-15", "a flag-off save keeps its earlier envelope");
        string offPrint = SaveCodec.Fingerprint(plain);
        var flagged = On(snapshot);
        Check(SaveCodec.Fingerprint(flagged) != offPrint && Magic(SaveCodec.Save(flagged)) == "ANTHROPOCENE-15", "flag on: the save carries the market envelope");
        const int turns = 12;
        var scalar = On(snapshot); var scalarPrints = Play(scalar, turns);
        var parallel = On(snapshot); var parallelPrints = Play(parallel, turns, new PhaseExecution(4, 1, true, 1));
        Check(scalarPrints.SequenceEqual(parallelPrints), "flag-on play is identical on scalar and reversed-partition parallel workers");
        int trades = scalar.History.Sum(r => r.Trades.Count), entries = scalar.Societies.Sum(s => s.Market?.Entries.Count ?? 0);
        Check(scalar.History.Any(r => r.Trades.Count > 0), "the seed-17 world trades with market memory on");
        // Save/load continuation at the midpoint.
        var head = On(snapshot); var headPrints = Play(head, turns / 2);
        var bytes = SaveCodec.Save(head); var loaded = SaveCodec.Load(bytes);
        Check(loaded.Config.MarketMemoryEnabled && loaded.Config.MarketMemory.Describe() == head.Config.MarketMemory.Describe(), "the flag and levers survive a save");
        Check(SaveCodec.Fingerprint(loaded) == SaveCodec.Fingerprint(head), "a loaded flag-on world has the saved fingerprint");
        Check(loaded.Societies.Sum(s => s.Market?.Entries.Count ?? 0) == head.Societies.Sum(s => s.Market?.Entries.Count ?? 0), "market memories survive a save");
        var tail = Play(loaded, turns - turns / 2);
        Check(headPrints.Concat(tail).SequenceEqual(scalarPrints), "flag-on play continues exactly through a save");
        // Conservation: every settled handshake conserved its parts (SettleLegs throws otherwise), and each record's legs are paired.
        Check(scalar.History.SelectMany(r => r.Trades).All(t => t.WantQuantity >= 0 && t.GiveQuantity >= 0), "settled legs are nonnegative");
        // Information rule on the generated world: remembered terms are trades the Society took part in.
        foreach (var s in scalar.Societies.Where(s => s.Market != null))
            foreach (var e in s.Market.Entries.Where(e => e.TermsTurn >= 0))
            {
                // A fission daughter inherits its parent's memory, so its lineage's trades count as its own knowledge.
                var lineage = new HashSet<int>();
                for (var x = s; x != null && lineage.Add(x.Id); x = scalar.Societies.FirstOrDefault(y => y.Id == x.ParentSociety)) { }
                var record = scalar.History.Single(r => r.Turn == e.TermsTurn);
                var mine = record.Trades.Where(t => lineage.Contains(t.RequesterSociety) && t.Responder == e.Camp || lineage.Contains(t.ResponderSociety) && t.Requester == e.Camp).ToList();
                Check(mine.Count > 0, "remembered terms on the generated world are the lineage's own trades");
            }
        // Flag off from the same snapshot, through the market build, is byte-identical to a second flag-off run (no hidden state).
        var offA = SaveCodec.Load((byte[])snapshot.Clone()); var offB = SaveCodec.Load((byte[])snapshot.Clone());
        Check(Play(offA, 4).SequenceEqual(Play(offB, 4)) && offA.Societies.All(s => s.Market == null), "flag-off play keeps no market state");
        Console.WriteLine("  generated: " + trades + " trades over " + turns + " flag-on turns, " + entries + " market entries at the end");
    }

    // --market-perf=<seed>,<turns>: matched Small/8 turn timings with the flag off and on (diagnostic, not a check).
    public static void Perf(string spec)
    {
        var parts = spec.Split(','); int seed = int.Parse(parts[0]), turns = int.Parse(parts[1]);
        var snapshot = ElderModel9Environment.GenerateOpening(seed, 8, out int opening);
        foreach (bool market in new[] { false, true, false, true })
        {
            var w = market ? On(snapshot) : SaveCodec.Load((byte[])snapshot.Clone());
            var resolver = new TurnResolver(); var times = new List<double>(); var tradeTimes = new List<double>(); int trades = 0;
            for (int t = 0; t < turns; t++)
            {
                var clock = Stopwatch.StartNew();
                var plan = AiPlanning.Decide(w);
                var prepared = TurnPlanner.Prepare(w, plan.Orders, plan.Traces);
                var tradeClock = Stopwatch.StartNew(); Trade.Plan(w); tradeTimes.Add(tradeClock.Elapsed.TotalMilliseconds);
                resolver.Resolve(w, prepared);
                times.Add(clock.Elapsed.TotalMilliseconds - tradeTimes[tradeTimes.Count - 1]);
                trades += w.History.Last().Trades.Count;
            }
            times.Sort(); tradeTimes.Sort();
            int entries = w.Societies.Sum(s => s.Market?.Entries.Count ?? 0);
            Console.WriteLine("MARKET_PERF seed=" + seed + " market=" + market + " turns=" + turns + " openingTurns=" + opening + " medianTurnMs=" + times[times.Count / 2].ToString("0.0")
                + " meanTurnMs=" + times.Average().ToString("0.0") + " medianTradePlanMs=" + tradeTimes[tradeTimes.Count / 2].ToString("0.00") + " trades=" + trades + " entries=" + entries
                + " camps=" + w.Camps.Count(c => c.Population > 0));
        }
    }
}
