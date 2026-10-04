using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Trade between known settlements (Docs/Trade.md; design QoLFissionTradeDesign 6-7,
    // work order ValueModelImplementationPlan WP7-WP9). Phase 1 is AI-to-AI barter from
    // committed stock. Every handshake is agreed in the planning pass from the committed
    // boundary and settles at the opening of resolution; nothing pending is ever saved.

    // Identifiers of the tradeable goods. Behaviour lives in the Trade.Registry descriptors, not in this
    // list: adding a good is one descriptor and, if it needs a new one, one value-curve builder. Member
    // names match the CraftGood names used by earlier records.
    public enum TradeGood { Cloth, AnimalFabric, HousingMaterial, Food }

    // One confirmed and settled exchange, recorded canonically (agreement order).
    [Serializable]
    public sealed class TradeRecord
    {
        public int Turn, Requester, Responder, RequesterSociety, ResponderSociety, Distance;
        // The requester receives Want and gives Give.
        public TradeGood Want, Give;
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
        public TradeGood Want, Give;
        public double WantQuantity, GiveQuantity, Ratio, Affinity, RequesterGain, ResponderGain;
        public bool Countered;
        // Amount of each stock part the agreement moves, in the giving side's committed proportions.
        public double[] WantParts, GiveParts;
    }

    // Marginal local-use value of one good (person-QoL-years per unit, as the planner's claims). Decreasing in x.
    internal abstract class ValueCurve
    {
        public abstract double Marginal(double x);
        public abstract double Integrated(double a, double b);
        // The coverage the curve's camp wants to reach: recoverable utility is the integral up to it.
        public abstract double Target { get; }
    }

    // The need curve V e^{-k x/N} (QoLFissionTradeDesign 5.1).
    internal sealed class ExponentialCurve : ValueCurve
    {
        public readonly double V, N, K;
        public ExponentialCurve(double v, double n, double k) { V = v; N = n; K = k; }
        public override double Marginal(double x) => V > 0 && N > 0 ? V * Math.Exp(-K * Math.Max(0, x) / N) : 0;
        public override double Integrated(double a, double b)
            => !(b > a) || !(V > 0) || !(N > 0) ? 0 : V * N / K * (Math.Exp(-K * Math.Max(0, a) / N) - Math.Exp(-K * Math.Max(0, b) / N));
        public override double Target => N;
    }

    // Piecewise-constant bands: Values[i] below Ends[i], zero beyond the last end (the food buckets, 7.2).
    internal sealed class BandCurve : ValueCurve
    {
        public readonly double[] Ends, Values;
        public BandCurve(double[] ends, double[] values) { Ends = ends; Values = values; }
        public override double Marginal(double x)
        {
            for (int i = 0; i < Ends.Length; i++) if (x < Ends[i]) return Values[i];
            return 0;
        }
        public override double Integrated(double a, double b)
        {
            if (!(b > a)) return 0;
            double total = 0, lo = double.NegativeInfinity;
            for (int i = 0; i < Ends.Length; i++) { total += Values[i] * Math.Max(0, Math.Min(b, Ends[i]) - Math.Max(a, lo)); lo = Ends[i]; }
            return total;
        }
        public override double Target => Ends.Length > 0 ? Ends[Ends.Length - 1] : 0;
    }

    // The net planned change of one camp's stock parts (indexed by Trade.PartIndex). Transient: never saved.
    internal sealed class TradePlan
    {
        public static readonly TradePlan Empty = new TradePlan();
        public readonly List<TradeAgreement> Agreements = new List<TradeAgreement>();
        public readonly TradeSummary Summary = new TradeSummary();
        public readonly Dictionary<int, double[]> Deltas = new Dictionary<int, double[]>();
        public bool Changes(int camp) => Deltas.ContainsKey(camp);
        public double Delta(int camp, TradeGood good)
        {
            if (!Deltas.TryGetValue(camp, out var d)) return 0;
            var g = Trade.Descriptor(good); double sum = 0;
            for (int i = 0; i < g.Parts.Length; i++) sum += d[Trade.PartIndex(good, i)];
            return sum;
        }
        internal void Add(int camp, TradeGood good, double[] parts, double sign)
        {
            if (!Deltas.TryGetValue(camp, out var d)) Deltas[camp] = d = new double[Trade.PartCount];
            for (int i = 0; i < parts.Length; i++) d[Trade.PartIndex(good, i)] += sign * parts[i];
        }
    }

    internal static class Trade
    {
        // A stock part of a good: a camp field read and written by trade.
        internal sealed class Part
        {
            public string Name; public Func<Camp, double> Get; public Action<Camp, double> Set; public Domain? Practice;
        }
        // One tradeable good: its parts, the value curve it is priced on (goods sharing a key share one
        // curve and one position), and whether the configuration trades it.
        internal sealed class GoodDescriptor
        {
            public TradeGood Id; public string Curve; public Part[] Parts; public Func<SimulationConfig, bool> Active;
        }
        // The terms of one curve for one camp: the curve, the position offset beyond the held parts, and
        // the floor below which the camp never gives.
        internal struct CurveTerms
        {
            public ValueCurve Curve; public double Offset, Floor;
        }

        internal static readonly GoodDescriptor[] Registry =
        {
            new GoodDescriptor { Id = TradeGood.Cloth, Curve = "Clothing", Active = _ => true,
                Parts = new[] { new Part { Name = "Cloth", Get = c => c.Inventory.Cloth, Set = (c, v) => c.Inventory.Cloth = v } } },
            new GoodDescriptor { Id = TradeGood.AnimalFabric, Curve = "Clothing", Active = _ => true,
                Parts = new[] { new Part { Name = "AnimalFabric", Get = c => c.Inventory.AnimalFabric, Set = (c, v) => c.Inventory.AnimalFabric = v } } },
            new GoodDescriptor { Id = TradeGood.HousingMaterial, Curve = "Housing", Active = _ => true,
                Parts = new[] { new Part { Name = "HousingMaterial", Get = c => c.Inventory.HousingMaterial, Set = (c, v) => c.Inventory.HousingMaterial = v } } },
            new GoodDescriptor { Id = TradeGood.Food, Curve = "Food", Active = cfg => cfg.TradeFoodEnabled,
                Parts = new[]
                {
                    new Part { Name = "PlantFood", Get = c => c.PlantFood, Set = (c, v) => c.PlantFood = v, Practice = Domain.Gathering },
                    new Part { Name = "AnimalFood", Get = c => c.AnimalFood, Set = (c, v) => c.AnimalFood = v, Practice = Domain.Hunting },
                    new Part { Name = "AquaticFood", Get = c => c.AquaticFood, Set = (c, v) => c.AquaticFood = v, Practice = Domain.Fishing }
                } },
        };
        private static readonly int[] partOffsets = Offsets();
        private static int[] Offsets()
        {
            var o = new int[Registry.Length]; int n = 0;
            for (int i = 0; i < Registry.Length; i++) { if ((int)Registry[i].Id != i) throw new InvalidOperationException("Trade registry order must match TradeGood."); o[i] = n; n += Registry[i].Parts.Length; }
            return o;
        }
        internal static readonly int PartCount = Registry.Sum(g => g.Parts.Length);
        internal static int PartIndex(TradeGood good, int part) => partOffsets[(int)good] + part;
        internal static GoodDescriptor Descriptor(TradeGood good) => Registry[(int)good];
        internal static double Held(Camp c, TradeGood good) { double s = 0; foreach (var p in Descriptor(good).Parts) s += Math.Max(0, p.Get(c)); return s; }

        // Curve builders by key, from the committed camp. `lastProduction` is last turn's committed food income.
        private static readonly Dictionary<string, Func<WorldState, Camp, double, CurveTerms>> Builders = new Dictionary<string, Func<WorldState, Camp, double, CurveTerms>>
        {
            // The clothing claim's curve; Cloth and Animal Fabric both fill it.
            ["Clothing"] = (w, c, _) =>
            {
                ClaimValuation.NeedGoodTerms(w, c, out double v, out double need, out _, out _, out _);
                return new CurveTerms { Curve = new ExponentialCurve(v, need, w.Config.Neolithic.ValueCurveSteepness), Floor = double.NegativeInfinity };
            },
            // A Housing Material unit completes one fixed Housing unit; worthless to a camp that cannot build.
            ["Housing"] = (w, c, _) =>
            {
                var cfg = w.Config;
                ClaimValuation.NeedGoodTerms(w, c, out _, out _, out _, out double v, out double shelter);
                bool canBuild = CultivationEconomy.Enabled(c) && c.People.HasTech(NeolithicTech.Construction);
                double quantity = canBuild ? Math.Max(0, NeolithicProduction.Demand(w, c, CraftGood.Housing, cfg.TurnDays / cfg.DaysPerYear, c.Population)) : 0;
                return new CurveTerms { Curve = new ExponentialCurve(canBuild ? v : 0, shelter + quantity, cfg.Neolithic.ValueCurveSteepness), Offset = shelter, Floor = double.NegativeInfinity };
            },
            // The planner's food buckets on this turn's envelope P = stock + last turn's production: consumption of
            // every linked consumer (v_food), clothing-need feedstock and operating reserve (v_reserve), feasible
            // growth (v_growth), surplus (0). Protected food (consumption, feedstock, reserve) is never given;
            // under the safe-only policy, growth food is not given either.
            ["Food"] = (w, c, lastProduction) =>
            {
                var cfg = w.Config; var n = cfg.Neolithic;
                double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals;
                double consumption = w.LinkedPopulation(c) * cfg.Consumption * intervals;
                TurnResolver.FoodBucketBasis(w, c, intervals, years, consumption, out double reserveTarget, out double maximumGrowthFood);
                double feedstock = Math.Max(0, NeolithicProduction.ClothingNeedUnits(n, c.Population, years, c.Inventory.Cloth, c.Inventory.AnimalFabric)) * Math.Max(0, n.PlantFoodPerFabricUnit);
                double vFood = ExpansionValuation.FoodValue(cfg, c), vReserve = Math.Min(vFood, n.FoodRiskWeight * vFood);
                double perBirth = ExpansionValuation.FoodPerBirth(cfg.Consumption * intervals, QoLMortality.MaximumRate(w, c), years);
                double vGrowth = perBirth > 0 ? Math.Max(0, ExpansionValuation.MarginalWelfare(w, c)) * ExpansionValuation.PersonLife(n) / perBirth : 0;
                double protectedEnd = consumption + feedstock + reserveTarget, growthEnd = protectedEnd + maximumGrowthFood;
                return new CurveTerms
                {
                    Curve = new BandCurve(new[] { consumption, protectedEnd, growthEnd }, new[] { vFood, vReserve, Math.Min(vReserve, vGrowth) }),
                    Offset = lastProduction, Floor = cfg.TradeFoodSafeOnly ? growthEnd : protectedEnd
                };
            },
        };

        // The value terms of one camp: one curve per key, positions over held parts, giveable stock per good.
        internal sealed class Valuation
        {
            public int Camp, Society, Tile;
            public readonly ValueCurve[] Curves = new ValueCurve[Registry.Length];      // per good (shared instances per key)
            public readonly double[] Held = new double[Registry.Length];
            public readonly bool[] Active = new bool[Registry.Length];
            public readonly double[][] Shares = new double[Registry.Length][];          // committed part proportions
            private readonly Dictionary<string, (double Position, double Floor)> positions = new Dictionary<string, (double, double)>();
            internal void SetCurve(string key, double position, double floor) => positions[key] = (position, floor);
            public ValueCurve Curve(TradeGood g) => Curves[(int)g];
            public double Position(TradeGood g) => positions[Descriptor(g).Curve].Position;
            public double Floor(TradeGood g) => positions[Descriptor(g).Curve].Floor;
            // What the camp may give of a good: held stock, never below its curve's floor.
            public double Stock(TradeGood g) => Active[(int)g] ? Math.Max(0, Math.Min(Held[(int)g], Position(g) - Floor(g))) : 0;
            public double Marginal(TradeGood g, double x) => Active[(int)g] ? Curve(g).Marginal(x) : 0;
            public double Integrated(TradeGood g, double a, double b) => Active[(int)g] ? Curve(g).Integrated(a, b) : 0;
            public double Gain(TradeGood g, double q) => Integrated(g, Position(g), Position(g) + q);
            public double Loss(TradeGood g, double q) => Integrated(g, Position(g) - q, Position(g));
            public void Apply(TradeGood g, double amount)
            {
                Held[(int)g] += amount; var key = Descriptor(g).Curve; var p = positions[key]; positions[key] = (p.Position + amount, p.Floor);
            }
            internal double[] Parts(TradeGood g, double quantity) => Shares[(int)g].Select(s => s * quantity).ToArray();
        }

        internal static bool Eligible(WorldState w, Camp c)
            => w.Config.TradeEnabled && c.Population > 0 && c.Player != 0
               && CultivationEconomy.EconomyEnabled(c) && c.People?.Memory != null;

        // Last turn's committed food production per camp, the lagged income forecast of the food envelope.
        internal static Dictionary<int, double> LastProduction(WorldState w)
        {
            var result = new Dictionary<int, double>();
            var last = w.History.Count > 0 ? w.History[w.History.Count - 1] : null;
            if (last?.Demographics != null) foreach (var d in last.Demographics) result[d.Camp] = Math.Max(0, d.FoodProduced);
            return result;
        }

        // O(goods) per camp: one builder call per curve key.
        internal static Valuation Value(WorldState w, Camp c, double lastProduction = 0)
        {
            var v = new Valuation { Camp = c.Id, Society = c.SocietyId, Tile = c.Tile };
            var built = new Dictionary<string, CurveTerms>(); var held = new Dictionary<string, double>();
            foreach (var g in Registry)
            {
                int i = (int)g.Id;
                v.Active[i] = g.Active(w.Config);
                v.Held[i] = Held(c, g.Id);
                v.Shares[i] = g.Parts.Select(p => v.Held[i] > 1e-12 ? Math.Max(0, p.Get(c)) / v.Held[i] : 1.0 / g.Parts.Length).ToArray();
                if (!v.Active[i]) continue;
                if (!built.TryGetValue(g.Curve, out var terms)) built[g.Curve] = terms = Builders[g.Curve](w, c, lastProduction);
                v.Curves[i] = terms.Curve;
                held[g.Curve] = (held.TryGetValue(g.Curve, out double h) ? h : 0) + v.Held[i];
            }
            foreach (var pair in built) v.SetCurve(pair.Key, pair.Value.Offset + held[pair.Key], pair.Value.Floor);
            foreach (var g in Registry) if (!v.Active[(int)g.Id] && !built.ContainsKey(g.Curve)) v.SetCurve(g.Curve, v.Held[(int)g.Id], double.PositiveInfinity);
            return v;
        }

        // Requester's best 1:1 quantity: marginal want value equals marginal give
        // loss, bounded by what it may give. Bisection over a monotone difference.
        internal static double BestQuantity(Valuation r, TradeGood want, TradeGood give)
        {
            if (Descriptor(want).Curve == Descriptor(give).Curve) return 0;
            double hi = r.Stock(give);
            if (!(hi > 1e-9)) return 0;
            double Slope(double q) => r.Marginal(want, r.Position(want) + q) - r.Marginal(give, r.Position(give) - q);
            if (!(Slope(0) > 0)) return 0;
            if (Slope(hi) > 0) return hi;
            double lo = 0;
            for (int i = 0; i < 50; i++) { double mid = (lo + hi) / 2; if (Slope(mid) > 0) lo = mid; else hi = mid; }
            return lo;
        }

        // The requester's bundle lists (QoLFissionTradeDesign 6.2-6.3), O(goods log W): the W goods with the most
        // recoverable utility (integrated gain up to its target coverage) and the W giveable goods of lowest marginal
        // loss. It cannot see what a partner holds, so the responder chooses from these lists. Ties: lower good id.
        internal static (TradeGood[] Wants, TradeGood[] Gives) Lists(Valuation r, int length)
        {
            var wants = new List<(double Score, int Id)>(); var gives = new List<(double Score, int Id)>();
            foreach (var g in Registry)
            {
                if (!r.Active[(int)g.Id]) continue;
                double position = r.Position(g.Id), recoverable = r.Integrated(g.Id, position, Math.Max(position, r.Curve(g.Id).Target));
                if (recoverable > 1e-12) wants.Add((recoverable, (int)g.Id));
                if (r.Stock(g.Id) > 1e-9) gives.Add((r.Marginal(g.Id, position - 1e-9), (int)g.Id));
            }
            return (wants.OrderByDescending(x => x.Score).ThenBy(x => x.Id).Take(length).Select(x => (TradeGood)x.Id).ToArray(),
                    gives.OrderBy(x => x.Score).ThenBy(x => x.Id).Take(length).Select(x => (TradeGood)x.Id).ToArray());
        }

        private static bool Acceptable(Valuation side, TradeGood got, double gotQ, TradeGood gave, double gaveQ, double margin, double transport)
            => gaveQ <= side.Stock(gave) + 1e-9 && side.Gain(got, gotQ) > 0 && side.Gain(got, gotQ) * (1 - transport) >= (1 + margin) * side.Loss(gave, gaveQ);

        // Requester limit: the most of `give` per unit of `want` it accepts for q.
        internal static double MaximumRatio(Valuation r, TradeGood want, TradeGood give, double q, double margin, double transport)
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
        internal static double MinimumRatio(Valuation p, TradeGood want, TradeGood give, double q, double margin, double transport, double cap)
        {
            if (!(q > 0) || q > p.Stock(want) + 1e-9 || !(cap > 0)) return double.NaN;
            bool Ok(double ratio) => p.Gain(give, ratio * q) > 0 && p.Gain(give, ratio * q) * (1 - transport) >= (1 + margin) * p.Loss(want, q);
            if (!Ok(cap)) return double.NaN;
            double lo = 0, hi = cap;
            for (int i = 0; i < 50; i++) { double mid = (lo + hi) / 2; if (Ok(mid)) hi = mid; else lo = mid; }
            return hi;
        }

        private sealed class Requisition
        {
            public int From, To, Distance;
            public TradeGood[] Wants, Gives;
            public double Affinity;
        }

        // The responder's answer to one request: over the requester's W x W listed pairs (constant work), the
        // agreement that gains it most, or null (ignore). For a pair it gives at most what it may give of the
        // want; the requester's 1:1 best quantity is halved up to three times until both accept. It honours at
        // 1:1 when it accepts that, otherwise counters once at its floor inside the requester's limit (closed).
        private static TradeAgreement Respond(Valuation r, Valuation p, Requisition x, SimulationConfig cfg)
        {
            double margin = cfg.TradeMargin * (1 - x.Affinity), transport = Math.Min(1, cfg.TradeDistanceCostPerHex * x.Distance);
            TradeAgreement best = null;
            foreach (var want in x.Wants)
                foreach (var give in x.Gives)
                {
                    if (Descriptor(want).Curve == Descriptor(give).Curve || !(p.Stock(want) > 1e-9)) continue;
                    double q = Math.Min(BestQuantity(r, want, give), p.Stock(want));
                    for (int attempt = 0; attempt < 4 && q > 1e-6; attempt++, q /= 2)
                    {
                        double limit = MaximumRatio(r, want, give, q, margin, transport);
                        if (double.IsNaN(limit)) continue;
                        double floor = MinimumRatio(p, want, give, q, margin, transport, limit);
                        if (double.IsNaN(floor) || floor > limit + 1e-12) continue;
                        bool countered = floor > 1 + 1e-12;
                        double ratio = countered ? floor : Math.Min(1, limit), paid = ratio * q;
                        double responderGain = p.Gain(give, paid) * (1 - transport) - p.Loss(want, q);
                        if (best == null || responderGain > best.ResponderGain + 1e-12)
                            best = new TradeAgreement { Requester = r.Camp, Responder = p.Camp, Distance = x.Distance, Want = want, Give = give,
                                WantQuantity = q, GiveQuantity = paid, Ratio = ratio, Affinity = x.Affinity, Countered = countered,
                                RequesterGain = r.Gain(want, q) * (1 - transport) - r.Loss(give, paid), ResponderGain = responderGain,
                                WantParts = p.Parts(want, q), GiveParts = r.Parts(give, paid) };
                        break;
                    }
                }
            return best;
        }

        // The bounded same-turn handshake: O(camps (goods log W + contacts)) pure per-camp work, then
        // O(camps K W^2) canonical responses (responder ID, then the responder's own gain, then requester
        // ID and good), updating both sides' positions so no camp is promised the same stock twice.
        internal static TradePlan Plan(WorldState w, PhaseExecution execution = null)
        {
            var cfg = w.Config;
            if (!cfg.TradeEnabled) return TradePlan.Empty;
            var plan = new TradePlan();
            var eligible = w.Camps.Where(c => Eligible(w, c)).OrderBy(c => c.Id).ToArray();
            plan.Summary.Eligible = eligible.Length;
            if (eligible.Length < 2) return plan;
            var workers = execution ?? new PhaseExecution();
            var production = LastProduction(w);
            var values = workers.Map(eligible, c => Value(w, c, production.TryGetValue(c.Id, out double p) ? p : 0));
            var byCamp = new Dictionary<int, Valuation>();
            for (int i = 0; i < eligible.Length; i++) byCamp[eligible[i].Id] = values[i];
            var societies = w.Societies.ToDictionary(s => s.Id);
            var map = w.Map;
            var requisitions = workers.Map(eligible, c =>
            {
                var r = byCamp[c.Id]; var list = new List<Requisition>();
                var (wants, gives) = Lists(r, cfg.TradeListLength);
                // Only a request some pair of which this camp itself would accept is sent.
                if (!wants.Any(a => gives.Any(b => Descriptor(a).Curve != Descriptor(b).Curve && BestQuantity(r, a, b) > 1e-6))) return list;
                // Partners: remembered settlements of other Societies, nearest first, at most K.
                var partners = c.People.Memory.Contacts
                    .Where(k => !k.Moved && k.Tile >= 0 && byCamp.ContainsKey(k.Camp) && byCamp[k.Camp].Society != r.Society)
                    .Select(k => (k.Camp, Distance: map.Distance(c.Tile, k.Tile)))
                    .Where(p => p.Distance <= cfg.TradeRange)
                    .OrderBy(p => p.Distance).ThenBy(p => p.Camp).Take(cfg.TradeRequisitionsPerCamp);
                foreach (var p in partners)
                {
                    societies.TryGetValue(r.Society, out var mine); societies.TryGetValue(byCamp[p.Camp].Society, out var theirs);
                    list.Add(new Requisition { From = c.Id, To = p.Camp, Distance = p.Distance, Wants = wants, Gives = gives, Affinity = Kinship.Affinity(mine, theirs) });
                }
                return list;
            });
            var all = requisitions.SelectMany(x => x).ToList();
            plan.Summary.Requisitions = all.Count;
            foreach (var group in all.GroupBy(x => x.To).OrderBy(g => g.Key))
            {
                var p = byCamp[group.Key];
                // Value order: the responder's best gain from each request on its opening positions, then requester ID.
                var ordered = group.Select(x => (x, Score: Respond(byCamp[x.From], p, x, cfg)?.ResponderGain ?? double.NegativeInfinity))
                    .OrderByDescending(t => t.Score).ThenBy(t => t.x.From).Select(t => t.x).ToList();
                foreach (var x in ordered)
                {
                    var r = byCamp[x.From];
                    // Re-evaluated on the positions earlier agreements left, so honours from several
                    // partners never overshoot and no stock is promised twice.
                    var agreement = Respond(r, p, x, cfg);
                    if (agreement == null) { plan.Summary.Ignored++; continue; }
                    plan.Agreements.Add(agreement);
                    r.Apply(agreement.Want, agreement.WantQuantity); r.Apply(agreement.Give, -agreement.GiveQuantity);
                    p.Apply(agreement.Want, -agreement.WantQuantity); p.Apply(agreement.Give, agreement.GiveQuantity);
                    plan.Add(r.Camp, agreement.Want, agreement.WantParts, 1); plan.Add(p.Camp, agreement.Want, agreement.WantParts, -1);
                    plan.Add(r.Camp, agreement.Give, agreement.GiveParts, -1); plan.Add(p.Camp, agreement.Give, agreement.GiveParts, 1);
                    if (agreement.Countered) plan.Summary.Countered++; else plan.Summary.Honoured++;
                }
            }
            return plan;
        }

        // A planning view of a camp after its confirmed trades.
        internal static void ApplyToPlanningCamp(Camp view, double[] delta)
        {
            foreach (var g in Registry)
                for (int i = 0; i < g.Parts.Length; i++)
                {
                    double d = delta[PartIndex(g.Id, i)];
                    if (d != 0) g.Parts[i].Set(view, Math.Max(0, g.Parts[i].Get(view) + d));
                }
        }

        // Canonical settlement at the opening of resolution (before dispatch, so provisions,
        // production and needs all see the post-trade stock the planner saw), from committed
        // stock. A short side, which committed stock cannot normally produce, scales both
        // sides of that agreement in proportion. Conserves every stock part.
        internal static void Settle(WorldState w, TradePlan plan, TurnRecord record)
        {
            if (plan == null || plan.Agreements.Count == 0 && plan.Summary.Eligible == 0) return;
            record.TradeActivity = plan.Summary;
            if (plan.Agreements.Count == 0) return;
            var camps = w.Camps.ToDictionary(c => c.Id);
            var parts = Registry.SelectMany(g => g.Parts).ToArray();
            var before = parts.Select(p => w.Camps.Sum(p.Get)).ToArray();
            double Available(Camp from, TradeGood good, double[] amounts)
            {
                double s = 1; var d = Descriptor(good);
                for (int i = 0; i < amounts.Length; i++) if (amounts[i] > 0) s = Math.Min(s, Math.Max(0, d.Parts[i].Get(from)) / amounts[i]);
                return s;
            }
            void Move(Camp from, Camp to, TradeGood good, double[] amounts, double scale)
            {
                var d = Descriptor(good);
                for (int i = 0; i < amounts.Length; i++)
                {
                    double x = amounts[i] * scale; if (x == 0) continue;
                    d.Parts[i].Set(from, d.Parts[i].Get(from) - x); d.Parts[i].Set(to, d.Parts[i].Get(to) + x);
                }
            }
            foreach (var a in plan.Agreements)
            {
                if (!camps.TryGetValue(a.Requester, out var r) || !camps.TryGetValue(a.Responder, out var p)) continue;
                double scale = Math.Max(0, Math.Min(Available(p, a.Want, a.WantParts), Available(r, a.Give, a.GiveParts)));
                Move(p, r, a.Want, a.WantParts, scale);
                Move(r, p, a.Give, a.GiveParts, scale);
                record.Trades.Add(new TradeRecord { Turn = w.Turn, Requester = r.Id, Responder = p.Id, RequesterSociety = r.SocietyId, ResponderSociety = p.SocietyId,
                    Distance = a.Distance, Want = a.Want, Give = a.Give, WantQuantity = a.WantQuantity * scale, GiveQuantity = a.GiveQuantity * scale,
                    Ratio = a.Ratio, Affinity = a.Affinity, Countered = a.Countered, RequesterGain = a.RequesterGain, ResponderGain = a.ResponderGain, Delivered = scale });
                ContactKnowledge.RecordTrade(w, r.People, p);
                ContactKnowledge.RecordTrade(w, p.People, r);
                // A receiver learns the practice that produced what it imports (inert while TradePracticePerValue = 0).
                void Learn(Camp receiver, TradeGood good, double[] amounts, double gain)
                {
                    var d = Descriptor(good); double total = amounts.Sum();
                    if (!(total > 0) || !(gain > 0)) return;
                    for (int i = 0; i < amounts.Length; i++)
                        if (d.Parts[i].Practice.HasValue) TechnologyDiscovery.RecordTradePractice(w.Config, receiver.People, d.Parts[i].Practice.Value, gain * scale * amounts[i] / total);
                }
                Learn(r, a.Want, a.WantParts, a.RequesterGain); Learn(p, a.Give, a.GiveParts, a.ResponderGain);
            }
            var after = parts.Select(p => w.Camps.Sum(p.Get)).ToArray();
            for (int i = 0; i < parts.Length; i++)
                if (Math.Abs(after[i] - before[i]) > 1e-6 * Math.Max(1, Math.Abs(before[i])))
                    throw new InvalidOperationException("Trade settlement changed the world total of " + parts[i].Name + ".");
            if (w.Camps.Any(c => parts.Any(p => p.Get(c) < -1e-9)))
                throw new InvalidOperationException("Trade settlement left a negative stock.");
        }
    }
}
