using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Trade between known settlements (Docs/Trade.md; design QoLFissionTradeDesign 6,
    // work order ValueModelImplementationPlan WP7-WP8). Phase 1 is AI-to-AI barter of
    // the T0 goods from committed stock. Every handshake is agreed in the planning
    // pass from the committed boundary and settles at the opening of the economy
    // phase; nothing pending is ever saved.

    // One confirmed and settled exchange, recorded canonically (agreement order).
    [Serializable]
    public sealed class TradeRecord
    {
        public int Turn, Requester, Responder, RequesterSociety, ResponderSociety, Distance;
        // The requester receives Want and gives Give.
        public CraftGood Want, Give;
        public double WantQuantity, GiveQuantity, Ratio, Affinity, RequesterGain, ResponderGain, Delivered;
        public bool Countered;
    }

    // Per-turn handshake counts. Requisitions = Honoured + Countered + Ignored.
    [Serializable]
    public sealed class TradeSummary
    {
        public int Eligible, Requisitions, Honoured, Countered, Ignored;
    }

    internal sealed class TradeAgreement
    {
        public int Requester, Responder, Distance;
        public CraftGood Want, Give;
        public double WantQuantity, GiveQuantity, Ratio, Affinity, RequesterGain, ResponderGain;
        public bool Countered;
    }

    // The handshake result for one turn. A pure function of the committed world,
    // so Draft and Prepare compute the same plan. Transient: never saved.
    internal sealed class TradePlan
    {
        public static readonly TradePlan Empty = new TradePlan();
        public readonly List<TradeAgreement> Agreements = new List<TradeAgreement>();
        public readonly TradeSummary Summary = new TradeSummary();
        // Net inventory change per camp: (Cloth, Housing Material).
        public readonly Dictionary<int, (double Cloth, double HousingMaterial)> Deltas = new Dictionary<int, (double, double)>();
        public bool Changes(int camp) => Deltas.ContainsKey(camp);
        internal void AddDelta(int camp, CraftGood good, double amount)
        {
            Deltas.TryGetValue(camp, out var d);
            if (good == CraftGood.Cloth) d.Cloth += amount; else d.HousingMaterial += amount;
            Deltas[camp] = d;
        }
    }

    internal static class Trade
    {
        // T0: the two QoL inputs traded from stock.
        internal static readonly CraftGood[] Goods = { CraftGood.Cloth, CraftGood.HousingMaterial };

        // The value terms of one camp for the T0 goods, from the committed camp:
        // the same curves ClaimValuation prices (V e^{-k x/N} per unit). Clothing
        // position counts Cloth and Animal Fabric; a Housing Material unit is worth
        // the Housing unit it completes, and nothing to a camp that cannot build.
        internal sealed class Valuation
        {
            public int Camp, Society, Tile;
            public double ClothingValue, ClothingNeed, ClothingPosition, Cloth;
            public double HousingValue, HousingNeed, HousingPosition, HousingMaterial;
            public bool CanBuild;
            public double Steepness;
            public double Position(CraftGood g) => g == CraftGood.Cloth ? ClothingPosition : HousingPosition;
            public double Stock(CraftGood g) => g == CraftGood.Cloth ? Cloth : HousingMaterial;
            private (double v, double n) Curve(CraftGood g) => g == CraftGood.Cloth ? (ClothingValue, ClothingNeed) : CanBuild ? (HousingValue, HousingNeed) : (0, 0);
            // Integrated value of the units between positions a < b.
            public double Integrated(CraftGood g, double a, double b)
            {
                var (v, n) = Curve(g);
                if (!(v > 0) || !(n > 0) || !(b > a)) return 0;
                return v * n / Steepness * (Math.Exp(-Steepness * Math.Max(0, a) / n) - Math.Exp(-Steepness * Math.Max(0, b) / n));
            }
            public double Marginal(CraftGood g, double x)
            {
                var (v, n) = Curve(g);
                return v > 0 && n > 0 ? v * Math.Exp(-Steepness * Math.Max(0, x) / n) : 0;
            }
            public double Gain(CraftGood g, double q) => Integrated(g, Position(g), Position(g) + q);
            public double Loss(CraftGood g, double q) => Integrated(g, Position(g) - q, Position(g));
            public void Apply(CraftGood g, double amount)
            {
                if (g == CraftGood.Cloth) { Cloth += amount; ClothingPosition += amount; }
                else { HousingMaterial += amount; HousingPosition += amount; }
            }
        }

        internal static bool Eligible(WorldState w, Camp c)
            => w.Config.TradeEnabled && c.Population > 0 && c.Player != 0
               && CultivationEconomy.EconomyEnabled(c) && c.People?.Memory != null;

        internal static Valuation Value(WorldState w, Camp c)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear;
            ClaimValuation.NeedGoodTerms(w, c, out double clothingV, out double clothingNeed, out double clothingStock,
                out double housingV, out double housingStock);
            bool canBuild = CultivationEconomy.Enabled(c) && c.People.HasTech(NeolithicTech.Construction);
            double housingQuantity = canBuild ? Math.Max(0, NeolithicProduction.Demand(w, c, CraftGood.Housing, years, c.Population)) : 0;
            return new Valuation
            {
                Camp = c.Id, Society = c.SocietyId, Tile = c.Tile, Steepness = n.ValueCurveSteepness,
                ClothingValue = clothingV, ClothingNeed = clothingNeed, ClothingPosition = clothingStock, Cloth = Math.Max(0, c.Inventory.Cloth),
                HousingValue = housingV, HousingNeed = housingStock + housingQuantity, CanBuild = canBuild,
                HousingMaterial = Math.Max(0, c.Inventory.HousingMaterial), HousingPosition = housingStock + Math.Max(0, c.Inventory.HousingMaterial)
            };
        }

        // Requester's best 1:1 quantity: marginal want value equals marginal give
        // loss, bounded by its own give stock. Bisection over a monotone difference.
        internal static double BestQuantity(Valuation r, CraftGood want, CraftGood give)
        {
            double hi = r.Stock(give);
            if (!(hi > 1e-9)) return 0;
            double Slope(double q) => r.Marginal(want, r.Position(want) + q) - r.Marginal(give, r.Position(give) - q);
            if (!(Slope(0) > 0)) return 0;
            if (Slope(hi) >= 0) return hi;
            double lo = 0;
            for (int i = 0; i < 50; i++) { double mid = (lo + hi) / 2; if (Slope(mid) > 0) lo = mid; else hi = mid; }
            return lo;
        }

        // The largest give-per-want ratio a side accepts for receiving `receiveGood`
        // q units per... expressed generally: side receives `got` of g1 and gives
        // ratio * q of g2. Returns NaN when no positive ratio is acceptable.
        private static bool Acceptable(Valuation side, CraftGood got, double gotQ, CraftGood gave, double gaveQ, double margin, double transport)
            => gaveQ <= side.Stock(gave) + 1e-9 && side.Gain(got, gotQ) * (1 - transport) >= (1 + margin) * side.Loss(gave, gaveQ) && side.Gain(got, gotQ) > 0;

        // Requester limit: the most of `give` per unit of `want` it accepts for q.
        internal static double MaximumRatio(Valuation r, CraftGood want, CraftGood give, double q, double margin, double transport)
        {
            if (!(q > 0) || !Acceptable(r, want, q, give, 0, margin, transport)) return double.NaN;
            double hi = r.Stock(give) / q;
            if (Acceptable(r, want, q, give, hi * q, margin, transport)) return hi;
            double lo = 0;
            for (int i = 0; i < 50; i++) { double mid = (lo + hi) / 2; if (Acceptable(r, want, q, give, mid * q, margin, transport)) lo = mid; else hi = mid; }
            return lo;
        }

        // Responder floor: the least of the requester's `give` per unit of `want`
        // it accepts for handing over q units. NaN when no ratio up to `cap` works.
        internal static double MinimumRatio(Valuation p, CraftGood want, CraftGood give, double q, double margin, double transport, double cap)
        {
            if (!(q > 0) || q > p.Stock(want) + 1e-9 || !(cap > 0)) return double.NaN;
            bool Ok(double ratio) => p.Gain(give, ratio * q) * (1 - transport) >= (1 + margin) * p.Loss(want, q) && p.Gain(give, ratio * q) > 0;
            if (!Ok(cap)) return double.NaN;
            double lo = 0, hi = cap;
            for (int i = 0; i < 50; i++) { double mid = (lo + hi) / 2; if (Ok(mid)) hi = mid; else lo = mid; }
            return hi;
        }

        private sealed class Requisition
        {
            public int From, To, Distance;
            public CraftGood Want, Give;
            public double Quantity, Affinity;
        }

        // The bounded same-turn handshake. Valuations and requisitions are pure per
        // camp; responses run once in canonical order (responder ID, then the
        // responder's own gain, then requester ID and good), updating both sides'
        // positions so no camp is promised the same stock twice.
        internal static TradePlan Plan(WorldState w, PhaseExecution execution = null)
        {
            var cfg = w.Config;
            if (!cfg.TradeEnabled) return TradePlan.Empty;
            var plan = new TradePlan();
            var eligible = w.Camps.Where(c => Eligible(w, c)).OrderBy(c => c.Id).ToArray();
            plan.Summary.Eligible = eligible.Length;
            if (eligible.Length < 2) return plan;
            var values = (execution ?? new PhaseExecution()).Map(eligible, c => Value(w, c));
            var byCamp = new Dictionary<int, Valuation>();
            for (int i = 0; i < eligible.Length; i++) byCamp[eligible[i].Id] = values[i];
            var societies = w.Societies.ToDictionary(s => s.Id);
            var map = w.Map;
            var requisitions = (execution ?? new PhaseExecution()).Map(eligible, c =>
            {
                var r = byCamp[c.Id]; var list = new List<Requisition>();
                // The requester's direction: what it would gain most from at 1:1.
                CraftGood want = CraftGood.Cloth, give = CraftGood.HousingMaterial; double q = 0, best = 0;
                foreach (var (a, b) in new[] { (CraftGood.Cloth, CraftGood.HousingMaterial), (CraftGood.HousingMaterial, CraftGood.Cloth) })
                {
                    double candidate = BestQuantity(r, a, b);
                    double gain = candidate > 0 ? r.Gain(a, candidate) - (1 + cfg.TradeMargin) * r.Loss(b, candidate) : 0;
                    if (gain > best + 1e-12) { best = gain; want = a; give = b; q = candidate; }
                }
                if (!(q > 1e-6)) return list;
                // Partners: remembered settlements of other Societies, nearest first, at most K.
                var partners = c.People.Memory.Contacts
                    .Where(k => !k.Moved && k.Tile >= 0 && byCamp.ContainsKey(k.Camp) && byCamp[k.Camp].Society != r.Society)
                    .Select(k => (k.Camp, Distance: map.Distance(c.Tile, k.Tile)))
                    .Where(p => p.Distance <= cfg.TradeRange)
                    .OrderBy(p => p.Distance).ThenBy(p => p.Camp).Take(cfg.TradeRequisitionsPerCamp);
                foreach (var p in partners)
                {
                    societies.TryGetValue(r.Society, out var mine); societies.TryGetValue(byCamp[p.Camp].Society, out var theirs);
                    list.Add(new Requisition { From = c.Id, To = p.Camp, Distance = p.Distance, Want = want, Give = give, Quantity = q, Affinity = Kinship.Affinity(mine, theirs) });
                }
                return list;
            });
            var all = requisitions.SelectMany(x => x).ToList();
            plan.Summary.Requisitions = all.Count;
            foreach (var group in all.GroupBy(x => x.To).OrderBy(g => g.Key))
            {
                var p = byCamp[group.Key];
                var ordered = group.OrderByDescending(x => p.Gain(x.Give, x.Quantity) - p.Loss(x.Want, x.Quantity))
                    .ThenBy(x => x.From).ThenBy(x => (int)x.Want).ToList();
                foreach (var x in ordered)
                {
                    var r = byCamp[x.From];
                    double margin = cfg.TradeMargin * (1 - x.Affinity), transport = Math.Min(1, cfg.TradeDistanceCostPerHex * x.Distance);
                    // The requester's want is recomputed on the positions this turn's
                    // earlier agreements left, so honours from several partners never
                    // overshoot; the responder can only hand over what it holds.
                    double q = Math.Min(Math.Min(x.Quantity, BestQuantity(r, x.Want, x.Give)), p.Stock(x.Want));
                    bool done = false;
                    for (int attempt = 0; attempt < 4 && q > 1e-6 && !done; attempt++, q /= 2)
                    {
                        double limit = MaximumRatio(r, x.Want, x.Give, q, margin, transport);
                        if (double.IsNaN(limit)) continue;
                        double floor = MinimumRatio(p, x.Want, x.Give, q, margin, transport, limit);
                        if (double.IsNaN(floor) || floor > limit + 1e-12) continue;
                        // Honour at the requested 1:1 when the responder accepts it,
                        // otherwise counter once at its floor (inside the limit: closed).
                        bool countered = floor > 1 + 1e-12;
                        double ratio = countered ? floor : Math.Min(1, limit);
                        double give = ratio * q;
                        var agreement = new TradeAgreement { Requester = r.Camp, Responder = p.Camp, Distance = x.Distance, Want = x.Want, Give = x.Give,
                            WantQuantity = q, GiveQuantity = give, Ratio = ratio, Affinity = x.Affinity, Countered = countered,
                            RequesterGain = r.Gain(x.Want, q) * (1 - transport) - r.Loss(x.Give, give),
                            ResponderGain = p.Gain(x.Give, give) * (1 - transport) - p.Loss(x.Want, q) };
                        plan.Agreements.Add(agreement);
                        r.Apply(x.Want, q); r.Apply(x.Give, -give); p.Apply(x.Want, -q); p.Apply(x.Give, give);
                        plan.AddDelta(r.Camp, x.Want, q); plan.AddDelta(r.Camp, x.Give, -give);
                        plan.AddDelta(p.Camp, x.Want, -q); plan.AddDelta(p.Camp, x.Give, give);
                        if (countered) plan.Summary.Countered++; else plan.Summary.Honoured++;
                        done = true;
                    }
                    if (!done) plan.Summary.Ignored++;
                }
            }
            return plan;
        }

        // A planning view of a camp after its confirmed trades (null delta: the camp).
        internal static void ApplyToPlanningInventory(CampInventory inventory, (double Cloth, double HousingMaterial) delta)
        {
            inventory.Cloth = Math.Max(0, inventory.Cloth + delta.Cloth);
            inventory.HousingMaterial = Math.Max(0, inventory.HousingMaterial + delta.HousingMaterial);
        }

        // Canonical settlement at the opening of the economy phase, from committed
        // stock. A short side (which committed stock cannot normally produce)
        // scales both sides of that agreement in proportion. Conserves each good.
        internal static void Settle(WorldState w, TradePlan plan, TurnRecord record)
        {
            if (plan == null || plan.Agreements.Count == 0 && plan.Summary.Eligible == 0) return;
            record.TradeActivity = plan.Summary;
            if (plan.Agreements.Count == 0) return;
            var camps = w.Camps.ToDictionary(c => c.Id);
            double Total(CraftGood g) => w.Camps.Sum(c => g == CraftGood.Cloth ? c.Inventory.Cloth : c.Inventory.HousingMaterial);
            double clothBefore = Total(CraftGood.Cloth), materialBefore = Total(CraftGood.HousingMaterial);
            double Held(Camp c, CraftGood g) => g == CraftGood.Cloth ? c.Inventory.Cloth : c.Inventory.HousingMaterial;
            void Move(Camp from, Camp to, CraftGood g, double amount)
            {
                if (g == CraftGood.Cloth) { from.Inventory.Cloth -= amount; to.Inventory.Cloth += amount; }
                else { from.Inventory.HousingMaterial -= amount; to.Inventory.HousingMaterial += amount; }
            }
            foreach (var a in plan.Agreements)
            {
                if (!camps.TryGetValue(a.Requester, out var r) || !camps.TryGetValue(a.Responder, out var p)) continue;
                double scale = 1;
                if (a.WantQuantity > 0) scale = Math.Min(scale, Math.Max(0, Held(p, a.Want)) / a.WantQuantity);
                if (a.GiveQuantity > 0) scale = Math.Min(scale, Math.Max(0, Held(r, a.Give)) / a.GiveQuantity);
                scale = Math.Max(0, Math.Min(1, scale));
                Move(p, r, a.Want, a.WantQuantity * scale);
                Move(r, p, a.Give, a.GiveQuantity * scale);
                record.Trades.Add(new TradeRecord { Turn = w.Turn, Requester = r.Id, Responder = p.Id, RequesterSociety = r.SocietyId, ResponderSociety = p.SocietyId,
                    Distance = a.Distance, Want = a.Want, Give = a.Give, WantQuantity = a.WantQuantity * scale, GiveQuantity = a.GiveQuantity * scale,
                    Ratio = a.Ratio, Affinity = a.Affinity, Countered = a.Countered, RequesterGain = a.RequesterGain, ResponderGain = a.ResponderGain, Delivered = scale });
                ContactKnowledge.RecordTrade(w, r.People, p);
                ContactKnowledge.RecordTrade(w, p.People, r);
            }
            double clothAfter = Total(CraftGood.Cloth), materialAfter = Total(CraftGood.HousingMaterial);
            if (Math.Abs(clothAfter - clothBefore) > 1e-6 * Math.Max(1, clothBefore) || Math.Abs(materialAfter - materialBefore) > 1e-6 * Math.Max(1, materialBefore))
                throw new InvalidOperationException("Trade settlement changed the total stock of a traded good.");
            if (w.Camps.Any(c => c.Inventory.Cloth < -1e-9 || c.Inventory.HousingMaterial < -1e-9))
                throw new InvalidOperationException("Trade settlement left a negative stock.");
        }
    }
}
