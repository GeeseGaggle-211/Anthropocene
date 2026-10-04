using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Trade between known settlements (Docs/Trade.md; design QoLFissionTradeDesign 6-7,
    // work order ValueModelImplementationPlan WP7-WP9). Phase 1 is AI-to-AI barter. Every
    // handshake is agreed in the planning pass from the committed boundary; nothing pending
    // is ever saved. Stock goods (food) settle at the opening of resolution; goods a seller
    // may make to order settle after production and before needs, and the seller's planner
    // carries them as export claims.

    // Identifiers of the tradeable goods. Behaviour lives in the Trade.Registry descriptors, not in this
    // list: adding a good is one descriptor and, if it needs a new one, one value-curve builder. Member
    // names match the CraftGood names used by earlier records.
    public enum TradeGood { Cloth, AnimalFabric, HousingMaterial, Food }

    // One confirmed and settled exchange, recorded canonically (agreement order).
    [Serializable]
    public sealed class TradeRecord
    {
        public int Turn, Requester, Responder, RequesterSociety, ResponderSociety, Distance;
        // The requester receives Want and gives Give; quantities are those delivered.
        public TradeGood Want, Give;
        public double WantQuantity, GiveQuantity, Ratio, Affinity, RequesterGain, ResponderGain, Delivered;
        public bool Countered;
        // Delivered is the want leg's delivered fraction, GiveDelivered the give leg's (absent in older records: 1).
        // Ordered is the part of each leg the seller agreed to make to order rather than give from stock.
        [System.Runtime.Serialization.OptionalField] public double GiveDelivered = 1;
        [System.Runtime.Serialization.OptionalField] public bool WantMadeToOrder, GiveMadeToOrder;
        [System.Runtime.Serialization.OnDeserializing] private void Defaults(System.Runtime.Serialization.StreamingContext context) { GiveDelivered = 1; }
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
        // The integral of min(Marginal, cap): a camp that can make the good again never loses more than its cost.
        public abstract double IntegratedCapped(double a, double b, double cap);
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
            => !(b > a) || !(V > 0) || !(N > 0) ? 0 : V * N / K * (Math.Exp(-K * Math.Max(0, a) / N) - Math.Exp(-K * Math.Max(0, b) / N)) + V * Math.Max(0, Math.Min(b, 0) - a);
        public override double IntegratedCapped(double a, double b, double cap)
        {
            if (!(b > a)) return 0;
            if (double.IsPositiveInfinity(cap) || !(V > cap)) return Integrated(a, b);
            if (!(cap > 0)) return 0;
            // m(x) >= cap below x* = N/K ln(V/cap).
            double cross = N / K * Math.Log(V / cap);
            return cap * Math.Max(0, Math.Min(b, cross) - a) + Integrated(Math.Max(a, cross), b);
        }
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
        public override double Integrated(double a, double b) => IntegratedCapped(a, b, double.PositiveInfinity);
        public override double IntegratedCapped(double a, double b, double cap)
        {
            if (!(b > a)) return 0;
            double total = 0, lo = double.NegativeInfinity;
            for (int i = 0; i < Ends.Length; i++) { total += Math.Min(cap, Values[i]) * Math.Max(0, Math.Min(b, Ends[i]) - Math.Max(a, lo)); lo = Ends[i]; }
            return total;
        }
        public override double Target => Ends.Length > 0 ? Ends[Ends.Length - 1] : 0;
    }

    // The handshake result for one turn. A pure function of the committed world, so Draft and Prepare
    // compute the same plan. Transient: never saved. Stock deltas (indexed by Trade.PartIndex) are what
    // the planning views add to stock: every import, and exports of goods settled from stock. Exports
    // of goods settled after production are export claims (by CraftGood) instead.
    internal sealed class TradePlan
    {
        public static readonly TradePlan Empty = new TradePlan();
        public readonly List<TradeAgreement> Agreements = new List<TradeAgreement>();
        public readonly TradeSummary Summary = new TradeSummary();
        public readonly Dictionary<int, double[]> Deltas = new Dictionary<int, double[]>();
        public readonly Dictionary<int, double[]> Exports = new Dictionary<int, double[]>();
        public bool Changes(int camp) => Deltas.ContainsKey(camp) || Exports.ContainsKey(camp);
        public bool AfterProduction => Agreements.Any(a => Trade.Descriptor(a.Want).AfterProduction || Trade.Descriptor(a.Give).AfterProduction);
        // Net change of a good at a camp once the turn's trades have settled.
        public double Delta(int camp, TradeGood good)
        {
            double sum = 0; var g = Trade.Descriptor(good);
            if (Deltas.TryGetValue(camp, out var d)) for (int i = 0; i < g.Parts.Length; i++) sum += d[Trade.PartIndex(good, i)];
            if (g.Recipe.HasValue && Exports.TryGetValue(camp, out var e)) sum -= e[(int)g.Recipe.Value];
            return sum;
        }
        internal void Receive(int camp, TradeGood good, double[] parts) => Stock(camp, good, parts, 1);
        internal void Send(int camp, TradeGood good, double[] parts)
        {
            var g = Trade.Descriptor(good);
            if (!g.AfterProduction) { Stock(camp, good, parts, -1); return; }
            if (!Exports.TryGetValue(camp, out var e)) Exports[camp] = e = new double[Enum.GetValues(typeof(CraftGood)).Length];
            e[(int)g.Recipe.Value] += parts.Sum();
        }
        private void Stock(int camp, TradeGood good, double[] parts, double sign)
        {
            if (!Deltas.TryGetValue(camp, out var d)) Deltas[camp] = d = new double[Trade.PartCount];
            for (int i = 0; i < parts.Length; i++) d[Trade.PartIndex(good, i)] += sign * parts[i];
        }
    }

    // The resolver's record of one turn's settlement legs (transient, owned by one Resolve call).
    internal sealed class TradeSettlement
    {
        public readonly TradePlan Plan;
        public readonly double[] WantScale, GiveScale;
        public TradeSettlement(TradePlan plan)
        {
            Plan = plan; WantScale = Enumerable.Repeat(double.NaN, plan.Agreements.Count).ToArray(); GiveScale = Enumerable.Repeat(double.NaN, plan.Agreements.Count).ToArray();
        }
    }

    internal static class Trade
    {
        // A stock part of a good: a camp field read and written by trade.
        internal sealed class Part
        {
            public string Name; public Func<Camp, double> Get; public Action<Camp, double> Set; public Domain? Practice;
        }
        // One tradeable good: its parts, the value curve it is priced on (goods sharing a key share one curve
        // and one position), whether the configuration trades it, and, for a good a camp can make, the recipe
        // that makes it to order. Made goods settle after production; the others from opening stock.
        internal sealed class GoodDescriptor
        {
            public TradeGood Id; public string Curve; public Part[] Parts; public Func<SimulationConfig, bool> Active;
            public CraftGood? Recipe;
            public bool AfterProduction => Recipe.HasValue;
        }
        // The terms of one curve for one camp: the curve, the position offset beyond the held parts, and
        // the floor below which the camp never gives.
        internal struct CurveTerms
        {
            public ValueCurve Curve; public double Offset, Floor;
        }

        internal static readonly GoodDescriptor[] Registry =
        {
            new GoodDescriptor { Id = TradeGood.Cloth, Curve = "Clothing", Active = _ => true, Recipe = CraftGood.Cloth,
                Parts = new[] { new Part { Name = "Cloth", Get = c => c.Inventory.Cloth, Set = (c, v) => c.Inventory.Cloth = v } } },
            new GoodDescriptor { Id = TradeGood.AnimalFabric, Curve = "Clothing", Active = _ => true, Recipe = CraftGood.AnimalFabric,
                Parts = new[] { new Part { Name = "AnimalFabric", Get = c => c.Inventory.AnimalFabric, Set = (c, v) => c.Inventory.AnimalFabric = v } } },
            new GoodDescriptor { Id = TradeGood.HousingMaterial, Curve = "Housing", Active = _ => true, Recipe = CraftGood.HousingMaterial,
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
                bool canBuild = CanBuild(c);
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
        internal static bool CanBuild(Camp c) => CultivationEconomy.Enabled(c) && c.People.HasTech(NeolithicTech.Construction);

        // The value terms of one camp: one curve per key, positions over held parts, giveable stock and, for
        // goods it can make to order, a production cost per unit and a labour-bounded capacity.
        internal sealed class Valuation
        {
            public int Camp, Society, Tile;
            public readonly ValueCurve[] Curves = new ValueCurve[Registry.Length];      // per good (shared instances per key)
            public readonly double[] Held = new double[Registry.Length];
            public readonly bool[] Active = new bool[Registry.Length];
            public readonly double[] Cost = Enumerable.Repeat(double.PositiveInfinity, Registry.Length).ToArray();
            public readonly double[] Capacity = new double[Registry.Length];
            public readonly double[][] Shares = new double[Registry.Length][];          // committed part proportions
            private readonly Dictionary<string, (double Position, double Floor)> positions = new Dictionary<string, (double, double)>();
            internal void SetCurve(string key, double position, double floor) => positions[key] = (position, floor);
            public ValueCurve Curve(TradeGood g) => Curves[(int)g];
            public double Position(TradeGood g) => positions[Descriptor(g).Curve].Position;
            public double Floor(TradeGood g) => positions[Descriptor(g).Curve].Floor;
            // What the camp may give of a good: held stock above its curve's floor, plus what it can make to order.
            public double Stock(TradeGood g) => Active[(int)g] ? Math.Max(0, Math.Min(Held[(int)g], Position(g) - Floor(g))) + Capacity[(int)g] : 0;
            public double Marginal(TradeGood g, double x) => Active[(int)g] ? Curve(g).Marginal(x) : 0;
            // The marginal loss of giving at position x: its use value, but never more than the cost of making another.
            public double LossMarginal(TradeGood g, double x) => Active[(int)g] ? Math.Min(Curve(g).Marginal(x), Cost[(int)g]) : 0;
            public double Integrated(TradeGood g, double a, double b) => Active[(int)g] ? Curve(g).Integrated(a, b) : 0;
            public double Gain(TradeGood g, double q) => Integrated(g, Position(g), Position(g) + q);
            public double Loss(TradeGood g, double q) => Active[(int)g] ? Curve(g).IntegratedCapped(Position(g) - q, Position(g), Cost[(int)g]) : 0;
            public void Apply(TradeGood g, double amount)
            {
                int i = (int)g;
                // Giving beyond held stock draws on the capacity to make to order.
                if (amount < 0 && -amount > Held[i]) { Capacity[i] = Math.Max(0, Capacity[i] - (-amount - Math.Max(0, Held[i]))); Held[i] = 0; }
                else Held[i] += amount;
                var key = Descriptor(g).Curve; var p = positions[key]; positions[key] = (p.Position + amount, p.Floor);
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

        // O(goods) per camp: one builder call per curve key, one recipe cost per makeable good.
        internal static Valuation Value(WorldState w, Camp c, double lastProduction = 0)
        {
            var cfg = w.Config;
            var v = new Valuation { Camp = c.Id, Society = c.SocietyId, Tile = c.Tile };
            var built = new Dictionary<string, CurveTerms>(); var held = new Dictionary<string, double>();
            foreach (var g in Registry)
            {
                int i = (int)g.Id;
                v.Active[i] = g.Active(cfg);
                v.Held[i] = Held(c, g.Id);
                v.Shares[i] = g.Parts.Select(p => v.Held[i] > 1e-12 ? Math.Max(0, p.Get(c)) / v.Held[i] : 1.0 / g.Parts.Length).ToArray();
                if (!v.Active[i]) continue;
                if (!built.TryGetValue(g.Curve, out var terms)) built[g.Curve] = terms = Builders[g.Curve](w, c, lastProduction);
                v.Curves[i] = terms.Curve;
                held[g.Curve] = (held.TryGetValue(g.Curve, out double h) ? h : 0) + v.Held[i];
            }
            foreach (var pair in built) v.SetCurve(pair.Key, pair.Value.Offset + held[pair.Key], pair.Value.Floor);
            foreach (var g in Registry) if (!v.Active[(int)g.Id] && !built.ContainsKey(g.Curve)) v.SetCurve(g.Curve, v.Held[(int)g.Id], double.PositiveInfinity);
            // Food a made recipe may consume: what lies above the protected band (consumption, feedstock, reserve).
            var foodTerms = built.TryGetValue("Food", out var food) ? food : Builders["Food"](w, c, lastProduction);
            double foodPosition = foodTerms.Offset + Held(c, TradeGood.Food), protectedEnd = ((BandCurve)foodTerms.Curve).Ends[1];
            double spareFood = Math.Max(0, Math.Min(Held(c, TradeGood.Food), foodPosition - protectedEnd));
            MakeToOrder(w, c, v, foodTerms.Curve.Marginal(foodPosition), spareFood);
            return v;
        }

        // A good the camp can make: its cost is the labour per unit at the lagged labour price w (the last plan's,
        // EconomicLaborPlan.LabourPrice) plus its food inputs at the camp's marginal food value; its capacity is what
        // TradeExportLabourShare of the residents' turn could make, bounded by the spare food its recipe consumes.
        // Without a recorded price nothing is made to order.
        private static void MakeToOrder(WorldState w, Camp c, Valuation v, double foodValue, double spareFood)
        {
            var cfg = w.Config;
            if (!(cfg.TradeExportLabourShare > 0) || c.EconomicLabor == null || !c.EconomicLabor.LabourPriceSet) return;
            double price = c.EconomicLabor.LabourPrice, years = cfg.TurnDays / cfg.DaysPerYear;
            var recipes = NeolithicProduction.Recipes(cfg); double multiplier = NeolithicProduction.HousingMultiplier(w, c, c.Population);
            foreach (var g in Registry)
            {
                int i = (int)g.Id;
                if (!v.Active[i] || !g.Recipe.HasValue) continue;
                var recipe = recipes.FirstOrDefault(r => r.Good == g.Recipe.Value);
                if (recipe == null || recipe.AgricultureOnly && !CultivationEconomy.Enabled(c) || recipe.HusbandryOnly && !CultivationEconomy.HusbandryEnabled(c)) continue;
                if (NeolithicProduction.Priority(c, recipe.Good) <= 0) continue;
                double labour = ClaimValuation.LabourPerUnit(w, c, recipe, recipes, multiplier);
                if (!(labour > 0) || double.IsInfinity(labour)) continue;
                v.Cost[i] = labour * price + (recipe.PlantFood + recipe.AnimalFood) * Math.Max(0, foodValue);
                double capacity = cfg.TradeExportLabourShare * c.Population * years / labour;
                // Food inputs come from spare food only, typed: Animal Fabric needs animal food, Cloth plant food.
                if (recipe.PlantFood > 0) capacity = Math.Min(capacity, Math.Min(spareFood, Math.Max(0, c.PlantFood)) / recipe.PlantFood);
                if (recipe.AnimalFood > 0) capacity = Math.Min(capacity, Math.Min(spareFood, Math.Max(0, c.AnimalFood)) / recipe.AnimalFood);
                v.Capacity[i] = Math.Max(0, capacity);
            }
        }

        // Requester's best 1:1 quantity: marginal want value equals marginal give
        // loss, bounded by what it may give. Bisection over a monotone difference.
        internal static double BestQuantity(Valuation r, TradeGood want, TradeGood give)
        {
            if (Descriptor(want).Curve == Descriptor(give).Curve) return 0;
            double hi = r.Stock(give);
            if (!(hi > 1e-9)) return 0;
            double Slope(double q) => r.Marginal(want, r.Position(want) + q) - r.LossMarginal(give, r.Position(give) - q);
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
                if (r.Stock(g.Id) > 1e-9) gives.Add((r.LossMarginal(g.Id, position - 1e-9), (int)g.Id));
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
        // O(camps K W^2) canonical responses (responder ID, then the responder's own best gain, then requester
        // ID), updating both sides' positions so no camp is promised the same stock or capacity twice.
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
                    plan.Receive(r.Camp, agreement.Want, agreement.WantParts); plan.Send(p.Camp, agreement.Want, agreement.WantParts);
                    plan.Send(r.Camp, agreement.Give, agreement.GiveParts); plan.Receive(p.Camp, agreement.Give, agreement.GiveParts);
                    if (agreement.Countered) plan.Summary.Countered++; else plan.Summary.Honoured++;
                }
            }
            return plan;
        }

        // A planning view of a camp after its confirmed trades: imports and stock-settled exports change its
        // stock; goods it will deliver after production become export claims its demand nets (TradeExports).
        internal static void ApplyToPlanningCamp(Camp view, TradePlan plan)
        {
            if (plan.Deltas.TryGetValue(view.Id, out var delta))
                foreach (var g in Registry)
                    for (int i = 0; i < g.Parts.Length; i++)
                    {
                        double d = delta[PartIndex(g.Id, i)];
                        if (d != 0) g.Parts[i].Set(view, Math.Max(0, g.Parts[i].Get(view) + d));
                    }
            if (plan.Exports.TryGetValue(view.Id, out var exports)) view.TradeExports = (double[])exports.Clone();
        }

        // The fraction of a leg the giver can deliver from what it holds now.
        private static double Available(Camp from, TradeGood good, double[] amounts)
        {
            var d = Descriptor(good); double scale = 1;
            for (int i = 0; i < amounts.Length; i++) if (amounts[i] > 0) scale = Math.Min(scale, Math.Max(0, d.Parts[i].Get(from)) / amounts[i]);
            return Math.Max(0, Math.Min(1, scale));
        }
        // Moves one leg in the giver's committed proportions at the given scale. Returns the scale.
        private static double Leg(Camp from, Camp to, TradeGood good, double[] amounts, double scale)
        {
            var d = Descriptor(good);
            for (int i = 0; i < amounts.Length; i++)
            {
                double x = amounts[i] * scale; if (x == 0) continue;
                d.Parts[i].Set(from, d.Parts[i].Get(from) - x); d.Parts[i].Set(to, d.Parts[i].Get(to) + x);
            }
            return scale;
        }

        // Settles, in agreement order, the legs of one timing (opening stock or after production), checking that
        // every part is conserved and none goes negative.
        private static void SettleLegs(WorldState w, TradeSettlement s, bool afterProduction)
        {
            var camps = w.Camps.ToDictionary(c => c.Id);
            var parts = Registry.SelectMany(g => g.Parts).ToArray();
            var before = parts.Select(p => w.Camps.Sum(p.Get)).ToArray();
            for (int k = 0; k < s.Plan.Agreements.Count; k++)
            {
                var a = s.Plan.Agreements[k];
                if (!camps.TryGetValue(a.Requester, out var r) || !camps.TryGetValue(a.Responder, out var p)) { s.WantScale[k] = s.GiveScale[k] = 0; continue; }
                bool want = Descriptor(a.Want).AfterProduction == afterProduction, give = Descriptor(a.Give).AfterProduction == afterProduction;
                // Two legs settling together scale together: a short side never receives full payment. A leg
                // settling alone (its partner leg already settled) scales by its own giver's holding.
                double wantScale = want ? Available(p, a.Want, a.WantParts) : 1, giveScale = give ? Available(r, a.Give, a.GiveParts) : 1;
                if (want && give) wantScale = giveScale = Math.Min(wantScale, giveScale);
                if (want) s.WantScale[k] = Leg(p, r, a.Want, a.WantParts, wantScale);
                if (give) s.GiveScale[k] = Leg(r, p, a.Give, a.GiveParts, giveScale);
            }
            var after = parts.Select(p => w.Camps.Sum(p.Get)).ToArray();
            for (int i = 0; i < parts.Length; i++)
                if (Math.Abs(after[i] - before[i]) > 1e-6 * Math.Max(1, Math.Abs(before[i])))
                    throw new InvalidOperationException("Trade settlement changed the world total of " + parts[i].Name + ".");
            if (w.Camps.Any(c => parts.Any(p => p.Get(c) < -1e-9)))
                throw new InvalidOperationException("Trade settlement left a negative stock.");
        }

        // Opening settlement (before dispatch, so provisions, production and needs see the stock the planner saw):
        // the stock legs. Returns the turn's settlement for the after-production step and Finish.
        internal static TradeSettlement Settle(WorldState w, TradePlan plan, TurnRecord record)
        {
            if (plan == null || plan.Agreements.Count == 0 && plan.Summary.Eligible == 0) return null;
            record.TradeActivity = plan.Summary;
            var s = new TradeSettlement(plan);
            if (plan.Agreements.Count > 0) SettleLegs(w, s, false);
            return s;
        }

        // After production and before needs: the legs of goods made to order (or given from stock) that settle then.
        // A seller that made less than it agreed delivers in proportion; the buyer's needs see what arrived.
        internal static void SettleAfterProduction(WorldState w, TradeSettlement s)
        {
            if (s == null || s.Plan.Agreements.Count == 0) return;
            SettleLegs(w, s, true);
        }

        // Records the settled agreements canonically, with contacts and the (inert) practice hook.
        internal static void Finish(WorldState w, TradeSettlement s, TurnRecord record)
        {
            if (s == null) return;
            var camps = w.Camps.ToDictionary(c => c.Id);
            for (int k = 0; k < s.Plan.Agreements.Count; k++)
            {
                var a = s.Plan.Agreements[k];
                if (!camps.TryGetValue(a.Requester, out var r) || !camps.TryGetValue(a.Responder, out var p)) continue;
                double want = double.IsNaN(s.WantScale[k]) ? 0 : s.WantScale[k], give = double.IsNaN(s.GiveScale[k]) ? 0 : s.GiveScale[k];
                record.Trades.Add(new TradeRecord { Turn = w.Turn, Requester = r.Id, Responder = p.Id, RequesterSociety = r.SocietyId, ResponderSociety = p.SocietyId,
                    Distance = a.Distance, Want = a.Want, Give = a.Give, WantQuantity = a.WantQuantity * want, GiveQuantity = a.GiveQuantity * give,
                    Ratio = a.Ratio, Affinity = a.Affinity, Countered = a.Countered, RequesterGain = a.RequesterGain, ResponderGain = a.ResponderGain, Delivered = want, GiveDelivered = give,
                    WantMadeToOrder = Descriptor(a.Want).AfterProduction, GiveMadeToOrder = Descriptor(a.Give).AfterProduction });
                ContactKnowledge.RecordTrade(w, r.People, p);
                ContactKnowledge.RecordTrade(w, p.People, r);
                // A receiver learns the practice that produced what it imports (inert while TradePracticePerValue = 0).
                void Learn(Camp receiver, TradeGood good, double[] amounts, double gain, double scale)
                {
                    var d = Descriptor(good); double total = amounts.Sum();
                    if (!(total > 0) || !(gain > 0)) return;
                    for (int i = 0; i < amounts.Length; i++)
                        if (d.Parts[i].Practice.HasValue) TechnologyDiscovery.RecordTradePractice(w.Config, receiver.People, d.Parts[i].Practice.Value, gain * scale * amounts[i] / total);
                }
                Learn(r, a.Want, a.WantParts, a.RequesterGain, want); Learn(p, a.Give, a.GiveParts, a.ResponderGain, give);
            }
        }
    }
}
