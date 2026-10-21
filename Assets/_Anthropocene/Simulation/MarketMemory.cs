using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Contract 13, simulator side (Docs/ElderModel9ArbitrageDesign.md): market memory, a demand-scaled resale value and
    // pack-animal carrying. Everything is gated by SimulationConfig.MarketMemoryEnabled (default false). The flag, these
    // levers and every Society's memory are [NonSerialized] and persist only in the ANTHROPOCENE-15 save extension, written
    // only when the flag is on, so a flag-off world's save bytes and fingerprints are byte-identical to the build before.

    // Provisional levers (unmeasured; see the design's "measured before calibration").
    [Serializable]
    public sealed class MarketMemoryConfig
    {
        // Freshness 0.5^(age / HalfLifeTurns); an entry not refreshed for ExpiryTurns is forgotten.
        public double HalfLifeTurns = 2;
        public int ExpiryTurns = 6;
        // Carrying capacity of a mobile (non-village) camp, in goods units: residents x CarryPerPerson + managed animals x
        // CarryPerPackAnimal. Settled villages relay within their storage (unbounded without goods storage).
        public double CarryPerPerson = .5, CarryPerPackAnimal = 2;
        // Knowledge chain (design section 0): craft-track work credited per person-QoL-year of a received made good's realised
        // use value, and the weight of units held beyond own need (carried, not used).
        public double CraftExposurePerValue = 1, CarriedExposureWeight = .25;
        public MarketMemoryConfig Copy() => (MarketMemoryConfig)MemberwiseClone();
        public void Validate()
        {
            if (!(HalfLifeTurns > 0) || double.IsInfinity(HalfLifeTurns) || ExpiryTurns < 1 || ExpiryTurns > 64 ||
                new[] { CarryPerPerson, CarryPerPackAnimal, CraftExposurePerValue, CarriedExposureWeight }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0) ||
                CarriedExposureWeight > 1)
                throw new ArgumentException("Invalid market memory configuration.");
        }
        internal string Describe() => string.Join(";", HalfLifeTurns.ToString("R"), ExpiryTurns, CarryPerPerson.ToString("R"), CarryPerPackAnimal.ToString("R"),
            CraftExposurePerValue.ToString("R"), CarriedExposureWeight.ToString("R"));
    }

    // What one Society saw of one foreign settlement and one trade good, only through its own requests and responses:
    // that the settlement wanted the good (its want list, or a good it accepted), the last agreed terms, and that it
    // offered the good. Never a partner's stock, valuation or other trades. Location comes from the contact snapshot.
    [Serializable]
    public sealed class MarketEntry
    {
        public int Camp;
        public TradeGood Good;
        // Last turn the settlement was seen to want the good (-1 never); the quantity it last took and the terms it paid
        // (Price units of Payment per unit of Good) at TermsTurn (-1 never).
        public int WantTurn = -1, TermsTurn = -1, SupplyTurn = -1;
        public double Quantity, Price;
        public TradeGood Payment;
        public MarketEntry Copy() => (MarketEntry)MemberwiseClone();
        internal int LastTurn => Math.Max(WantTurn, Math.Max(TermsTurn, SupplyTurn));
    }

    [Serializable]
    public sealed class MarketMemory
    {
        // Sorted by (Camp, Good); at most one entry per pair.
        public List<MarketEntry> Entries = new List<MarketEntry>();
        public MarketMemory Clone() => new MarketMemory { Entries = Entries.Select(e => e.Copy()).ToList() };
        private int Index(int camp, TradeGood good)
        {
            int lo = 0, hi = Entries.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1; var e = Entries[mid];
                int c = e.Camp != camp ? e.Camp.CompareTo(camp) : ((int)e.Good).CompareTo((int)good);
                if (c == 0) return mid; if (c < 0) lo = mid + 1; else hi = mid - 1;
            }
            return ~lo;
        }
        internal MarketEntry At(int camp, TradeGood good)
        {
            int i = Index(camp, good);
            if (i >= 0) return Entries[i];
            var e = new MarketEntry { Camp = camp, Good = good };
            Entries.Insert(~i, e);
            return e;
        }
    }

    // Save extension payload (ANTHROPOCENE-15).
    [Serializable]
    public sealed class MarketMemorySave
    {
        public int SchemaVersion = MarketMemorySystem.SchemaVersion;
        public MarketMemoryConfig Config;
        public List<int> Societies = new List<int>();
        public List<MarketMemory> Memories = new List<MarketMemory>();
    }

    // Cost: memory is O(contacts x goods) per Society; an update is O(agreements + requisitions + offers seen) per turn in
    // canonical order; resale is O(keys x entries of the camp's Society) per valued camp, with contact lookups O(log contacts);
    // the end-of-turn prune is O(entries). Nothing scans the map, scales with elapsed years or reads live foreign state.
    public static class MarketMemorySystem
    {
        public const int SchemaVersion = 1;
        public static bool Enabled(SimulationConfig config) => config != null && config.MarketMemoryEnabled;
        internal static MarketMemoryConfig Levers(SimulationConfig config) => config.MarketMemory ??= new MarketMemoryConfig();

        internal static double Freshness(SimulationConfig config, int age) => age < 0 ? 0 : Math.Pow(.5, age / Levers(config).HalfLifeTurns);

        // A requester's view of one partner's offers (Trade.Plan, flag on); transient.
        internal sealed class OfferSighting { public int Observer, Partner; public bool[] Offers; }
        // A request as the responder received it: the requester's ranked want list.
        internal sealed class RequestSighting { public int Requester, Responder; public TradeGood[] Wants; }

        private static Society Remembering(WorldState w, Camp c)
            => c?.People != null && ContactKnowledge.Remembers(c.People) ? c.People : null;
        private static MarketMemory Of(Society s) => s.Market ??= new MarketMemory();

        // Trade.Finish (flag on): what each side saw this turn, in canonical order: offers (requester, then partner order),
        // requests (requisition order), then settled agreements (agreement order), whose terms overwrite.
        internal static void Observe(WorldState w, TradePlan plan, IReadOnlyList<(TradeAgreement Agreement, double Want, double Give)> settled)
        {
            int turn = w.Turn;
            var camps = w.Camps.ToDictionary(c => c.Id);
            foreach (var o in plan.OfferSightings)
            {
                if (!camps.TryGetValue(o.Observer, out var observer) || !camps.TryGetValue(o.Partner, out _)) continue;
                var s = Remembering(w, observer); if (s == null) continue;
                for (int g = 0; g < o.Offers.Length; g++) if (o.Offers[g]) Of(s).At(o.Partner, (TradeGood)g).SupplyTurn = turn;
            }
            foreach (var r in plan.RequestSightings)
            {
                if (!camps.TryGetValue(r.Responder, out var responder) || !camps.TryGetValue(r.Requester, out _)) continue;
                var s = Remembering(w, responder); if (s == null) continue;
                foreach (var g in r.Wants) Of(s).At(r.Requester, g).WantTurn = turn;
            }
            foreach (var (a, want, give) in settled)
            {
                if (!(want > 1e-12) || !(give > 1e-12) || !camps.TryGetValue(a.Requester, out var requester) || !camps.TryGetValue(a.Responder, out var responder)) continue;
                // The responder sold Want to the requester for Give; the requester sold Give to the responder for Want.
                var ps = Remembering(w, responder);
                if (ps != null)
                {
                    var e = Of(ps).At(requester.Id, a.Want); e.WantTurn = e.TermsTurn = turn; e.Quantity = want; e.Price = give / want; e.Payment = a.Give;
                    Of(ps).At(requester.Id, a.Give).SupplyTurn = turn;
                }
                var rs = Remembering(w, requester);
                if (rs != null)
                {
                    var e = Of(rs).At(responder.Id, a.Give); e.WantTurn = e.TermsTurn = turn; e.Quantity = give; e.Price = want / give; e.Payment = a.Want;
                    Of(rs).At(responder.Id, a.Want).SupplyTurn = turn;
                }
            }
        }

        // End of turn, after ContactKnowledge.EndTurn: entries age out, and a settlement no longer known (forgotten) or known
        // to have moved drops its entries. O(entries).
        internal static void EndTurn(WorldState w)
        {
            int turn = w.Turn, expiry = Levers(w.Config).ExpiryTurns;
            foreach (var s in w.Societies.OrderBy(s => s.Id))
            {
                if (s.Market == null) continue;
                s.Market.Entries.RemoveAll(e =>
                {
                    var contact = s.Memory?.Find(e.Camp);
                    return contact == null || contact.Moved || contact.Tile < 0 || turn - e.LastTurn >= expiry;
                });
                if (s.Market.Entries.Count == 0) s.Market = null;
            }
        }

        // A daughter copies its parent's market knowledge at fission, as it copies its contacts.
        internal static void CopyAtFission(WorldState w, Society parent, Society daughter, Camp parentCamp, Camp daughterCamp)
        {
            if (!Enabled(w.Config) || parent.Market == null) return;
            var copy = parent.Market.Clone();
            copy.Entries.RemoveAll(e => e.Camp == parentCamp.Id || e.Camp == daughterCamp.Id);
            daughter.Market = copy.Entries.Count > 0 ? copy : null;
        }

        // Managed animals per camp (pack animals), built once per handshake: O(herds).
        internal static Dictionary<int, int> PackAnimals(WorldState w)
        {
            var result = new Dictionary<int, int>();
            foreach (var h in w.Herds) if (h.DomesticCamp >= 0 && h.Animals > 0) { result.TryGetValue(h.DomesticCamp, out int n); result[h.DomesticCamp] = n + h.Animals; }
            return result;
        }

        // Goods units a camp can hold in transit beyond its own need: unbounded for a settled village, otherwise its people's
        // and pack animals' loads.
        public static double CarryCapacity(WorldState w, Camp c, int packAnimals)
        {
            if (c.IsVillage) return double.PositiveInfinity;
            var m = Levers(w.Config);
            return Math.Max(0, c.Population) * m.CarryPerPerson + Math.Max(0, packAnimals) * m.CarryPerPackAnimal;
        }

        // One curve key's resale terms at a camp (diagnostic view of what ApplyResale used).
        public struct Resale { public double Demand, Price, Quantity, Value, End; public int Buyers; }

        // Design section 3: W(x) = max(use(x), d r) below own target + Q. r is the best net resale price among reachable known
        // buyers (observed terms x the camp's own marginal value of the payment good x (1 - transport) / (1 + margin) x the
        // terms' freshness); d in [0, 1] is recomputed from memory: buyers term (1 - e^{-sum fresh / (1 + known rival
        // suppliers)}) x scale min(1, wanted / carrying capacity) x unserved share (wanted - held beyond need) / wanted x
        // spread (r - own use at the band end) / r. Wanted is the fresh, rival-shared observed quantity; held beyond need is
        // what the camp already carries toward it. Food is not resold (perishable, protected bands).
        internal static void ApplyResale(WorldState w, Camp c, Trade.Valuation v, int packAnimals, Dictionary<TradeGood, Resale> diagnostics = null)
        {
            var cfg = w.Config;
            var memory = c.People?.Market;
            if (memory == null || memory.Entries.Count == 0 || c.People.Memory == null) return;
            var registry = Trade.Registry;
            var baseCurves = (ValueCurve[])v.Curves.Clone();
            double BaseMarginal(TradeGood g) => v.Active[(int)g] && baseCurves[(int)g] != null ? baseCurves[(int)g].Marginal(v.Position(g)) : 0;
            int intent = c.ElderMemory != null && c.ElderMemory.HasIntent && c.ElderMemory.IntentTile >= 0 ? c.ElderMemory.IntentTile : -1;
            var map = w.Map; int range = cfg.TradeRange; int turn = w.Turn;
            // Resale stock already held per key (beyond own target), and the carrying capacity they share.
            var keys = registry.Where(g => g.Id != TradeGood.Food && v.Active[(int)g.Id] && baseCurves[(int)g.Id] != null).Select(g => g.Curve).Distinct().ToArray();
            var heldBeyond = keys.ToDictionary(k => k, k =>
            {
                var g = registry.First(x => x.Curve == k && v.Active[(int)x.Id]).Id;
                return Math.Max(0, v.Position(g) - baseCurves[(int)g].Target);
            });
            double carry = CarryCapacity(w, c, packAnimals), totalHeld = heldBeyond.Values.Sum();
            // Known suppliers by good: (camp, tile) of fresh supply sightings.
            int expiry = Levers(cfg).ExpiryTurns;
            var suppliers = new List<(int Camp, TradeGood Good, int Tile)>();
            foreach (var e in memory.Entries)
            {
                if (e.SupplyTurn < 0 || turn - e.SupplyTurn >= expiry) continue;
                var k = c.People.Memory.Find(e.Camp); if (k == null || k.Moved || k.Tile < 0) continue;
                suppliers.Add((e.Camp, e.Good, k.Tile));
            }
            foreach (var key in keys)
            {
                var goods = registry.Where(g => g.Curve == key && v.Active[(int)g.Id]).Select(g => g.Id).ToArray();
                var probe = goods[0]; var curve = baseCurves[(int)probe];
                // Per buyer (camp): its best net price, freshest want and largest observed quantity over the key's goods.
                var buyers = new SortedDictionary<int, (double Price, double Fresh, double Quantity, int Tile)>();
                foreach (var e in memory.Entries)
                {
                    if (Array.IndexOf(goods, e.Good) < 0 || e.TermsTurn < 0 || e.WantTurn < 0 || !(e.Quantity > 0) || !(e.Price > 0)) continue;
                    if (Trade.Descriptor(e.Payment).Curve == key) continue;
                    var k = c.People.Memory.Find(e.Camp); if (k == null || k.Moved || k.Tile < 0) continue;
                    int distance = map.Distance(c.Tile, k.Tile);
                    if (intent >= 0) distance = Math.Min(distance, map.Distance(intent, k.Tile));
                    if (distance > range) continue;
                    double transport = Math.Min(1, cfg.TradeDistanceCostPerHex * distance);
                    double price = e.Price * BaseMarginal(e.Payment) * (1 - transport) / (1 + cfg.TradeMargin) * Freshness(cfg, turn - e.TermsTurn);
                    double fresh = Freshness(cfg, turn - e.WantTurn);
                    if (!(price > 0) || !(fresh > 0)) continue;
                    buyers.TryGetValue(e.Camp, out var b);
                    buyers[e.Camp] = (Math.Max(b.Price, price), Math.Max(b.Fresh, fresh), Math.Max(b.Quantity, e.Quantity), k.Tile);
                }
                if (buyers.Count == 0) continue;
                double r = 0, strength = 0, wanted = 0;
                foreach (var pair in buyers)
                {
                    int rivals = 0;
                    foreach (var s in suppliers)
                        if (s.Camp != pair.Key && Array.IndexOf(goods, s.Good) >= 0 && map.Distance(s.Tile, pair.Value.Tile) <= range) rivals++;
                    double share = 1.0 / (1 + rivals);
                    r = Math.Max(r, pair.Value.Price);
                    strength += pair.Value.Fresh * share;
                    wanted += pair.Value.Quantity * pair.Value.Fresh * share;
                }
                double target = curve.Target, held = heldBeyond[key];
                double unserved = Math.Max(0, wanted - held);
                double room = Math.Max(0, carry - (totalHeld - held));
                double quantity = Math.Min(unserved, room);
                double end = target + held + quantity;
                // Own use of the last unit the band would hold: the band only binds where own use falls below it.
                double own = curve.Marginal(end);
                double spread = r > 0 ? Math.Max(0, r - own) / r : 0;
                double scale = double.IsPositiveInfinity(carry) ? 1 : carry > 0 ? Math.Min(1, wanted / carry) : 0;
                double open = wanted > 0 ? unserved / wanted : 0;
                double d = (1 - Math.Exp(-strength)) * scale * open * spread;
                double value = d * r;
                diagnostics?.Add(probe, new Resale { Demand = d, Price = r, Quantity = quantity, Value = value, End = end, Buyers = buyers.Count });
                if (!(value > 0) || !(quantity > 1e-9)) continue;
                var resale = new ResaleCurve(curve, value, end);
                foreach (var g in goods) v.Curves[(int)g] = resale;
                foreach (var g in goods) v.OwnTarget[(int)g] = target;
            }
        }

        // A diagnostic, pure read: the resale terms a camp's valuation would use now (flag on), by the first good of each key.
        public static Dictionary<TradeGood, Resale> Inspect(WorldState w, Camp c)
        {
            var result = new Dictionary<TradeGood, Resale>();
            if (!Enabled(w.Config) || !Trade.Eligible(w, c)) return result;
            Trade.Value(w, c, 0, null, null, null, result);
            return result;
        }

        // Knowledge chain (design section 0): a received made good adds work toward the craft track that makes it, in
        // proportion to its realised use value; units held beyond own need count at CarriedExposureWeight. Discovery uses the
        // existing CraftMasteryThreshold rule.
        internal static void Expose(WorldState w, Camp receiver, TradeGood good, double receivedUtility, double useShare, CraftRecipe[] recipes)
        {
            var d = Trade.Descriptor(good);
            if (!d.Recipe.HasValue || !(receivedUtility > 0) || receiver?.People == null) return;
            var recipe = recipes.FirstOrDefault(r => r.Good == d.Recipe.Value);
            if (recipe == null || receiver.People.HasTech(recipe.Track)) return;
            var m = Levers(w.Config);
            double share = Math.Max(0, Math.Min(1, useShare));
            double work = receivedUtility * m.CraftExposurePerValue * (share + m.CarriedExposureWeight * (1 - share));
            if (!(work > 0) || double.IsInfinity(work)) return;
            var society = receiver.People;
            society.CraftExperience.TryGetValue(recipe.Track, out double experience);
            society.CraftExperience[recipe.Track] = experience + work;
            if (experience + work >= w.Config.Neolithic.CraftMasteryThreshold)
            {
                society.NeolithicTechnologies.Add(recipe.Track);
                foreach (string unlocked in WorkforceRoles.All.Where(role => EconomicIntents.RequiredTechnology(role) == recipe.Track))
                    if (!society.UnlockedSpecialists.Contains(unlocked)) society.UnlockedSpecialists.Add(unlocked);
            }
        }

        // Save extension: canonical Society order, empty memories omitted.
        internal static MarketMemorySave Capture(WorldState w)
        {
            var save = new MarketMemorySave { Config = Levers(w.Config).Copy() };
            foreach (var s in w.Societies.OrderBy(s => s.Id))
                if (s.Market != null && s.Market.Entries.Count > 0) { save.Societies.Add(s.Id); save.Memories.Add(s.Market); }
            return save;
        }

        internal static void Restore(WorldState w, MarketMemorySave save)
        {
            if (save == null || save.SchemaVersion != SchemaVersion || save.Config == null || save.Societies == null || save.Memories == null ||
                save.Societies.Count != save.Memories.Count || save.Societies.Distinct().Count() != save.Societies.Count)
                throw new System.IO.InvalidDataException("Invalid market memory extension.");
            save.Config.Validate();
            w.Config.MarketMemoryEnabled = true; w.Config.MarketMemory = save.Config;
            var societies = w.Societies.ToDictionary(s => s.Id);
            for (int i = 0; i < save.Societies.Count; i++)
            {
                if (!societies.TryGetValue(save.Societies[i], out var s) || save.Memories[i]?.Entries == null)
                    throw new System.IO.InvalidDataException("Invalid market memory society.");
                var entries = save.Memories[i].Entries;
                for (int k = 0; k < entries.Count; k++)
                {
                    var e = entries[k];
                    if (e == null || !Enum.IsDefined(typeof(TradeGood), e.Good) || !Enum.IsDefined(typeof(TradeGood), e.Payment) ||
                        double.IsNaN(e.Quantity) || double.IsInfinity(e.Quantity) || e.Quantity < 0 || double.IsNaN(e.Price) || double.IsInfinity(e.Price) || e.Price < 0 ||
                        e.WantTurn > w.Turn || e.TermsTurn > w.Turn || e.SupplyTurn > w.Turn ||
                        k > 0 && (entries[k - 1].Camp > e.Camp || entries[k - 1].Camp == e.Camp && entries[k - 1].Good >= e.Good))
                        throw new System.IO.InvalidDataException("Invalid market memory entry.");
                }
                s.Market = save.Memories[i];
            }
        }
    }

    // The use curve with the demand-scaled resale band: max(use(x), Value) below End, use(x) beyond. Nonincreasing whenever the
    // use curve is, so the handshake's bisections and inverses stay exact.
    internal sealed class ResaleCurve : ValueCurve
    {
        public readonly ValueCurve Base; public readonly double Value, End;
        public ResaleCurve(ValueCurve baseCurve, double value, double end) { Base = baseCurve; Value = Math.Max(0, value); End = end; }
        public override double Marginal(double x) => x < End ? Math.Max(Base.Marginal(x), Value) : Base.Marginal(x);
        public override double Integrated(double a, double b) => Integral(SegmentArrayFor(double.PositiveInfinity), a, b);
        public override double IntegratedCapped(double a, double b, double cap) => Integral(SegmentArrayFor(cap), a, b);
        public override double Target => Value > 0 ? Math.Max(Base.Target, End) : Base.Target;
        public override double Crossing(double c)
        {
            double x = Base.Crossing(c);
            return c > 0 && Value >= c ? Math.Max(x, End) : x;
        }
        internal override IEnumerable<Segment> Segments(double cap)
        {
            double b = Math.Min(Value, cap);
            foreach (var g in Base.Segments(cap))
            {
                double lo = g.Lo, hi = Math.Min(g.Hi, End);
                if (hi > lo)
                {
                    if (!g.Exponential) yield return new Segment { Lo = lo, Hi = hi, C = Math.Max(g.C, b) };
                    else
                    {
                        // The exponential is at least b up to xs, below it the band holds.
                        double xs = !(b > 0) ? double.PositiveInfinity : g.V > 0 ? g.N / g.K * Math.Log(g.V / b) - g.S : double.NegativeInfinity;
                        double split = Math.Max(lo, Math.Min(hi, xs));
                        if (split > lo) yield return new Segment { Lo = lo, Hi = split, V = g.V, N = g.N, K = g.K, S = g.S, Exponential = true };
                        if (hi > split) yield return new Segment { Lo = split, Hi = hi, C = b };
                    }
                }
                double above = Math.Max(g.Lo, End);
                if (g.Hi > above) { var s = g; s.Lo = above; yield return s; }
            }
        }
    }
}
