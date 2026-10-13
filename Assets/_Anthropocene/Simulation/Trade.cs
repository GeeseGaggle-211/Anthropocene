using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Trade between known settlements (Docs/Trade.md; design QoLFissionTradeDesign 6-7,
    // work order ValueModelImplementationPlan WP7-WP9). Phase 1 is AI-to-AI barter. Every
    // handshake is agreed in the planning pass from the committed boundary; nothing pending
    // is ever saved. Each leg has a stock part (from the giver's opening stock) and, for a
    // good the giver can make, a made-to-order part. Every stock part leaves its giver into
    // escrow at the opening of resolution. An agreement with no made part settles there; one
    // with a made part settles after production and before needs (pay on delivery), and the
    // maker's planner carries that part as an export claim.

    // Identifiers of the tradeable goods. Behaviour lives in the Trade.Registry descriptors, not in this
    // list: adding a good is one descriptor and, if it needs a new one, one value-curve builder. Member
    // names match the CraftGood names used by earlier records.
    // Append only: saved records store the numeric value.
    public enum TradeGood { Cloth, AnimalFabric, HousingMaterial, Food, LightHousing, Pots, HandTools, FarmTools, Stone, Clay, Wood }

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
        // Actual nonlinear local utility of the quantities that physically
        // settled, in the same person-QoL-year units as the opening value
        // curves. These are not planned gain multiplied by a delivery fraction.
        [System.Runtime.Serialization.OptionalField]
        public double RequesterRealizedUtility, ResponderRealizedUtility;
        [System.Runtime.Serialization.OptionalField] public bool WantMadeToOrder, GiveMadeToOrder;
        [System.Runtime.Serialization.OnDeserializing] private void Defaults(System.Runtime.Serialization.StreamingContext context) { GiveDelivered = 1; }
    }

    // Per-turn handshake counts. Requisitions = Honoured + Countered + Ignored.
    [Serializable]
    public sealed class TradeSummary
    {
        public int Eligible, Requisitions, Honoured, Countered, Ignored;
    }

    // A good's marginal local-use value published by the economic planner (its shadow price), in the planner's units
    // (person-QoL-years per unit). It prices the good's whole curve key (Cloth and Animal Fabric share Clothing) and
    // replaces that key's provisional curve in Trade.Value. Like EconomicLaborPlan.LabourPrice it is lagged: the plan
    // that publishes it runs after this turn's handshake, so trade reads it from the committed plan next turn.
    // The curve is bands (Values[i] below Ends[i], zero beyond; nonincreasing) or, when Exponential, V e^{-K x/N}.
    // x is the curve position: held units of the key's goods plus Offset. The camp never gives below Floor.
    [Serializable]
    public sealed class TradeValueSchedule
    {
        public TradeGood Good;
        public double Offset, Floor;
        public double[] Ends, Values;
        public bool Exponential;
        public double V, N, K;
        // The planner's cost of making one more unit to order; unset, trade derives it from the labour price.
        public bool MakeCostSet;
        public double MakeCost;
        public TradeValueSchedule Copy()
        {
            var copy = (TradeValueSchedule)MemberwiseClone();
            copy.Ends = (double[])Ends?.Clone(); copy.Values = (double[])Values?.Clone();
            return copy;
        }
        // Null when valid, otherwise the reason.
        public string Error()
        {
            if (!Enum.IsDefined(typeof(TradeGood), Good)) return "unknown trade good";
            if (!Finite(Offset) || Offset < 0 || !Finite(Floor) || Floor < 0) return "offset and floor must be finite and nonnegative";
            if (MakeCostSet && (!Finite(MakeCost) || MakeCost < 0)) return "make cost must be finite and nonnegative";
            if (Exponential) return Finite(V) && V >= 0 && Finite(N) && N > 0 && Finite(K) && K > 0 ? null : "exponential terms must be finite with N, K > 0";
            if (Ends == null || Values == null || Ends.Length != Values.Length) return "bands need matching ends and values";
            for (int i = 0; i < Ends.Length; i++)
            {
                if (!Finite(Ends[i]) || !Finite(Values[i]) || Values[i] < 0) return "band ends and values must be finite and values nonnegative";
                if (i > 0 && (!(Ends[i] > Ends[i - 1]) || Values[i] > Values[i - 1])) return "band ends must increase and values must not";
            }
            return null;
        }
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        internal static string Error(IReadOnlyList<TradeValueSchedule> schedules)
        {
            if (schedules == null) return null;
            var keys = new HashSet<string>();
            foreach (var s in schedules)
            {
                if (s == null) return "a trade value schedule is missing";
                string error = s.Error(); if (error != null) return error;
                if (!keys.Add(Trade.Descriptor(s.Good).Curve)) return "two trade value schedules price one curve";
            }
            return null;
        }
    }

    // A fulfilment rule that prices goods implements this to publish their schedules (one per curve key at most) with
    // its plan; EconomicPlanner records them on the plan's EconomicLaborPlan while trade is enabled.
    internal interface ITradeValuePublisher
    {
        IReadOnlyList<TradeValueSchedule> TradeValues(WorldState w, Camp c, CampProductionDemand demand, double position);
    }

    internal sealed class TradeAgreement
    {
        public int Requester, Responder, Distance;
        public TradeGood Want, Give;
        public double WantQuantity, GiveQuantity, Ratio, Affinity, RequesterGain, ResponderGain;
        public bool Countered;
        // The stock part of each leg, per part in the giving side's committed proportions, and its made-to-order part
        // (a made good has one part). The quantities are their sums.
        public double[] WantParts, GiveParts;
        public double WantMade, GiveMade;
        public TradeUtilitySnapshot Utility;
        // No made part: both legs come from opening stock and the agreement settles at the opening.
        public bool AllStock => !(WantMade > 1e-12) && !(GiveMade > 1e-12);
    }

    internal sealed class TradeUtilitySnapshot
    {
        private ValueCurve.Segment[] requesterWant, requesterGive,
            responderGive, responderWant;
        private double requesterWantPosition, requesterGivePosition,
            responderGivePosition, responderWantPosition, transport;

        public static TradeUtilitySnapshot Capture(Trade.Valuation requester,
            Trade.Valuation responder, TradeAgreement agreement,
            SimulationConfig config)
        {
            return new TradeUtilitySnapshot
            {
                requesterWant = (ValueCurve.Segment[])requester.W(
                    agreement.Want).Clone(),
                requesterGive = (ValueCurve.Segment[])requester.W(
                    agreement.Give).Clone(),
                responderGive = (ValueCurve.Segment[])responder.W(
                    agreement.Give).Clone(),
                responderWant = (ValueCurve.Segment[])responder.W(
                    agreement.Want).Clone(),
                requesterWantPosition = requester.Position(agreement.Want),
                requesterGivePosition = requester.Position(agreement.Give),
                responderGivePosition = responder.Position(agreement.Give),
                responderWantPosition = responder.Position(agreement.Want),
                transport = Math.Min(1, config.TradeDistanceCostPerHex *
                    agreement.Distance)
            };
        }

        public double Requester(double wantQuantity, double giveQuantity)
            => ValueCurve.Integral(requesterWant, requesterWantPosition,
                    requesterWantPosition + Math.Max(0, wantQuantity)) *
                    (1 - transport) -
                ValueCurve.Integral(requesterGive,
                    requesterGivePosition - Math.Max(0, giveQuantity),
                    requesterGivePosition);

        public double Responder(double wantQuantity, double giveQuantity)
            => ValueCurve.Integral(responderGive, responderGivePosition,
                    responderGivePosition + Math.Max(0, giveQuantity)) *
                    (1 - transport) -
                ValueCurve.Integral(responderWant,
                    responderWantPosition - Math.Max(0, wantQuantity),
                    responderWantPosition);
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
        // The capped curve as ordered segments [Lo, Hi): constant value C, or (Exponential) V e^{-K (x + S)/N}. The shift S
        // keeps a curve moved left by making capacity exact: folding it into V underflows to zero when N is tiny, and
        // 0 * e^{+huge} is NaN; with S the exponent stays nonpositive on the segment.
        internal struct Segment { public double Lo, Hi, C, V, N, K, S; public bool Exponential; }
        internal abstract IEnumerable<Segment> Segments(double cap);
        // The segments for the last cap asked (a camp's curve is read by one thread at a time; caps rarely change).
        private double cachedCap = double.NaN; private Segment[] cached;
        private Segment[] SegmentArray(double cap)
        {
            if (cached == null || !cap.Equals(cachedCap)) { cached = Segments(cap).ToArray(); cachedCap = cap; }
            return cached;
        }
        // The largest x with Marginal(x) >= c (-infinity when the curve never reaches c).
        public abstract double Crossing(double c);
        internal Segment[] Uncapped => SegmentArrayFor(double.PositiveInfinity);
        internal Segment[] SegmentArrayFor(double cap) => SegmentArray(cap);
        internal static double Marginal(Segment[] segments, double x)
        {
            foreach (var g in segments) if (x < g.Hi && x >= g.Lo) return g.Exponential ? g.V * Math.Exp(-g.K * (x + g.S) / g.N) : g.C;
            return 0;
        }
        internal static double Integral(Segment[] segments, double a, double b)
        {
            if (!(b > a)) return 0;
            double sum = 0;
            foreach (var g in segments) sum += Over(g, Math.Max(a, g.Lo), Math.Min(b, g.Hi));
            return sum;
        }
        internal static double Over(Segment g, double lo, double hi)
            => !(hi > lo) ? 0 : g.Exponential ? g.V * g.N / g.K * (Math.Exp(-g.K * (lo + g.S) / g.N) - Math.Exp(-g.K * (hi + g.S) / g.N)) : g.C * (hi - lo);
        public double InverseUp(double a, double target, double cap) => InverseUp(SegmentArray(cap), a, target);
        public double InverseDown(double top, double target, double cap) => InverseDown(SegmentArray(cap), top, target);
        // Smallest b >= a with the integral over [a, b] >= target; +infinity when no b reaches it. Closed form per segment.
        internal static double InverseUp(Segment[] segments, double a, double target)
        {
            if (!(target > 0)) return a;
            double rem = target;
            for (int i = 0; i < segments.Length; i++)
            {
                var g = segments[i];
                if (g.Hi <= a) continue;
                double lo = Math.Max(a, g.Lo), part = Over(g, lo, g.Hi);
                if (part < rem) { rem -= part; continue; }
                if (!g.Exponential) return lo + rem / g.C;
                double e = Math.Exp(-g.K * (lo + g.S) / g.N) - rem * g.K / (g.V * g.N);
                return e > 0 ? Math.Min(g.Hi, -g.N / g.K * Math.Log(e) - g.S) : g.Hi;
            }
            return double.PositiveInfinity;
        }
        // Largest d >= 0 with the integral over [top - d, top] <= target (+infinity when the whole curve below fits).
        internal static double InverseDown(Segment[] segments, double top, double target)
        {
            if (!(target > 0)) return 0;
            double rem = target;
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                var g = segments[i];
                if (g.Lo >= top) continue;
                double hi = Math.Min(top, g.Hi), part = Over(g, g.Lo, hi);
                if (part <= rem) { rem -= part; continue; }
                if (!g.Exponential) return top - (hi - rem / g.C);
                double e = Math.Exp(-g.K * (hi + g.S) / g.N) + rem * g.K / (g.V * g.N);
                return top - Math.Max(g.Lo, -g.N / g.K * Math.Log(e) - g.S);
            }
            return double.PositiveInfinity;
        }
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
        public override double Crossing(double c) => !(V > 0) || !(N > 0) || !(V >= c) ? double.NegativeInfinity : !(c > 0) ? double.PositiveInfinity : N / K * Math.Log(V / c);
        internal override IEnumerable<Segment> Segments(double cap)
        {
            if (!(V > 0) || !(N > 0)) { yield return new Segment { Lo = double.NegativeInfinity, Hi = double.PositiveInfinity, C = 0 }; yield break; }
            double top = Math.Min(V, cap);
            yield return new Segment { Lo = double.NegativeInfinity, Hi = 0, C = top };
            double cross = cap < V ? N / K * Math.Log(V / cap) : 0;
            if (cross > 0) yield return new Segment { Lo = 0, Hi = cross, C = cap };
            yield return new Segment { Lo = cross, Hi = double.PositiveInfinity, V = V, N = N, K = K, Exponential = true };
        }
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
        public override double Crossing(double c)
        {
            if (!(c > 0)) return double.PositiveInfinity;
            double x = double.NegativeInfinity;
            for (int i = 0; i < Ends.Length; i++) if (Values[i] >= c) x = Ends[i];
            return x;
        }
        internal override IEnumerable<Segment> Segments(double cap)
        {
            double lo = double.NegativeInfinity;
            for (int i = 0; i < Ends.Length; i++) { if (Ends[i] > lo) yield return new Segment { Lo = lo, Hi = Ends[i], C = Math.Min(cap, Values[i]) }; lo = Math.Max(lo, Ends[i]); }
            yield return new Segment { Lo = lo, Hi = double.PositiveInfinity, C = 0 };
        }
    }

    // The handshake result for one turn. A pure function of the committed world, so Draft and Prepare
    // compute the same plan. Transient: never saved. Stock deltas (indexed by Trade.PartIndex) are what
    // the planning views add to stock: every stock part given (it leaves into escrow at the opening),
    // what an all-stock agreement delivers at the opening, and imports that serve this turn's needs.
    // The made-to-order part of a leg is an export claim of its maker (by CraftGood).
    internal sealed class TradePlan
    {
        public static readonly TradePlan Empty = new TradePlan();
        public readonly List<TradeAgreement> Agreements = new List<TradeAgreement>();
        public readonly TradeSummary Summary = new TradeSummary();
        public readonly Dictionary<int, double[]> Deltas = new Dictionary<int, double[]>();
        public readonly Dictionary<int, double[]> Exports = new Dictionary<int, double[]>();
        public bool Changes(int camp) => Deltas.ContainsKey(camp) || Exports.ContainsKey(camp) || Imports.ContainsKey(camp);
        public bool AfterProduction => Agreements.Any(a => !a.AllStock);
        // Net change of a good in a camp's planning view (made exports leave after production).
        public double Delta(int camp, TradeGood good)
        {
            double sum = 0; var g = Trade.Descriptor(good);
            if (Deltas.TryGetValue(camp, out var d)) for (int i = 0; i < g.Parts.Length; i++) sum += d[Trade.PartIndex(good, i)];
            if (g.Recipe.HasValue && Exports.TryGetValue(camp, out var e)) sum -= e[(int)g.Recipe.Value];
            return sum;
        }
        // Goods that arrive after production (an agreement with a made part) and still serve the receiver's needs this
        // turn, by CraftGood: they count as stock for its needs but never as this turn's recipe inputs.
        public readonly Dictionary<int, double[]> Imports = new Dictionary<int, double[]>();
        // A receipt of `parts` (stock and made together, in part order). An all-stock agreement delivers at the opening,
        // so the receiver plans with it; after production only a good that serves needs counts this turn (food, materials,
        // tools and Housing Material are stock from the next turn).
        internal void Receive(int camp, TradeGood good, double[] parts, bool allStock)
        {
            var g = Trade.Descriptor(good);
            if (allStock) { Stock(camp, good, parts, 1); return; }
            if (!g.Recipe.HasValue || !g.ImportServesNeeds) return;
            Stock(camp, good, parts, 1);
            if (!Imports.TryGetValue(camp, out var m)) Imports[camp] = m = new double[Enum.GetValues(typeof(CraftGood)).Length];
            m[(int)g.Recipe.Value] += parts.Sum();
        }
        // A delivery: its stock part leaves the giver's view now; its made part is the giver's export claim.
        internal void Send(int camp, TradeGood good, double[] stockParts, double made)
        {
            var g = Trade.Descriptor(good);
            Stock(camp, good, stockParts, -1);
            if (!(made > 0) || !g.Recipe.HasValue) return;
            if (!Exports.TryGetValue(camp, out var e)) Exports[camp] = e = new double[Enum.GetValues(typeof(CraftGood)).Length];
            e[(int)g.Recipe.Value] += made;
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
        // Pay on delivery: every stock part leaves its giver at the opening into escrow. An agreement with a made part
        // releases it after production, both legs at the fraction both sides can deliver; the rest returns to the giver.
        public readonly double[][] WantEscrow, GiveEscrow;
        public TradeSettlement(TradePlan plan)
        {
            Plan = plan; WantScale = Enumerable.Repeat(double.NaN, plan.Agreements.Count).ToArray(); GiveScale = Enumerable.Repeat(double.NaN, plan.Agreements.Count).ToArray();
            WantEscrow = new double[plan.Agreements.Count][]; GiveEscrow = new double[plan.Agreements.Count][];
        }
        // What escrow holds of a stock part (indexed by Trade.PartIndex).
        public double Escrowed(int part)
        {
            double sum = 0;
            for (int k = 0; k < Plan.Agreements.Count; k++)
            {
                var a = Plan.Agreements[k];
                if (WantEscrow[k] != null) for (int j = 0; j < WantEscrow[k].Length; j++) if (Trade.PartIndex(a.Want, j) == part) sum += WantEscrow[k][j];
                if (GiveEscrow[k] != null) for (int j = 0; j < GiveEscrow[k].Length; j++) if (Trade.PartIndex(a.Give, j) == part) sum += GiveEscrow[k][j];
            }
            return sum;
        }
    }

    internal static class Trade
    {
        // Opt-in work counters for profiling the handshake (--trade-profile); transient, never authoritative.
        internal static class Counters
        {
            public static bool Enabled;
            public static long Responds, Pairs, BestQuantities, RatioSearches, Integrals, ValueTicks, RequestTicks, ResponseTicks;
            public static void Reset() { Enabled = true; Responds = Pairs = BestQuantities = RatioSearches = Integrals = ValueTicks = RequestTicks = ResponseTicks = 0; }
            public static void Count(ref long field) { if (Enabled) System.Threading.Interlocked.Increment(ref field); }
            static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            public static string Describe() => $"values {Ms(ValueTicks):F1} ms, requests {Ms(RequestTicks):F1} ms, responses {Ms(ResponseTicks):F1} ms; Respond {Responds}, pairs {Pairs}, best-quantity {BestQuantities}, ratio searches {RatioSearches}, integrals {Integrals}";
        }
        // A stock part of a good: a camp field read and written by trade.
        internal sealed class Part
        {
            public string Name; public Func<Camp, double> Get; public Action<Camp, double> Set; public Domain? Practice;
        }
        // One tradeable good: its parts, the value curve it is priced on (goods sharing a key share one curve
        // and one position), whether the configuration trades it, and, for a good a camp can make, the recipe
        // that makes it to order (a made good has one part). Everything else is traded from stock only.
        internal sealed class GoodDescriptor
        {
            public TradeGood Id; public string Curve; public Part[] Parts; public Func<SimulationConfig, bool> Active;
            public CraftGood? Recipe;
            public bool Makeable => Recipe.HasValue;
            // A good delivered after production (its agreement has a made part) still counts as stock for the receiver's
            // needs this turn, though never as a recipe input (Camp.TradeImports): clothing, tents and Pots. One used only
            // in production or field work (Housing Material, tools) is stock from the next turn.
            public bool ImportServesNeeds;
        }
        // The terms of one curve for one camp: the curve, the position offset beyond the held parts, the
        // floor below which the camp never gives, and whether the camp can price it at all.
        internal struct CurveTerms
        {
            public ValueCurve Curve; public double Offset, Floor; public bool Unpriced;
        }
        private static Part Field(string name, Func<CampInventory, double> get, Action<CampInventory, double> set)
            => new Part { Name = name, Get = c => get(c.Inventory), Set = (c, v) => set(c.Inventory, v) };

        internal static readonly GoodDescriptor[] Registry =
        {
            new GoodDescriptor { Id = TradeGood.Cloth, Curve = "Clothing", Active = _ => true, Recipe = CraftGood.Cloth, ImportServesNeeds = true,
                Parts = new[] { Field("Cloth", i => i.Cloth, (i, v) => i.Cloth = v) } },
            new GoodDescriptor { Id = TradeGood.AnimalFabric, Curve = "Clothing", Active = _ => true, Recipe = CraftGood.AnimalFabric, ImportServesNeeds = true,
                Parts = new[] { Field("AnimalFabric", i => i.AnimalFabric, (i, v) => i.AnimalFabric = v) } },
            new GoodDescriptor { Id = TradeGood.HousingMaterial, Curve = "Housing", Active = _ => true, Recipe = CraftGood.HousingMaterial,
                Parts = new[] { Field("HousingMaterial", i => i.HousingMaterial, (i, v) => i.HousingMaterial = v) } },
            new GoodDescriptor { Id = TradeGood.Food, Curve = "Food", Active = cfg => cfg.TradeFoodEnabled,
                Parts = new[]
                {
                    new Part { Name = "PlantFood", Get = c => c.PlantFood, Set = (c, v) => c.PlantFood = v, Practice = Domain.Gathering },
                    new Part { Name = "AnimalFood", Get = c => c.AnimalFood, Set = (c, v) => c.AnimalFood = v, Practice = Domain.Hunting },
                    new Part { Name = "AquaticFood", Get = c => c.AquaticFood, Set = (c, v) => c.AquaticFood = v, Practice = Domain.Fishing }
                } },
            new GoodDescriptor { Id = TradeGood.LightHousing, Curve = "Tents", Active = _ => true, Recipe = CraftGood.LightHousing, ImportServesNeeds = true,
                Parts = new[] { Field("LightHousing", i => i.LightHousingUnits, (i, v) => i.LightHousingUnits = v) } },
            new GoodDescriptor { Id = TradeGood.Pots, Curve = "Pots", Active = _ => true, Recipe = CraftGood.Pots, ImportServesNeeds = true,
                Parts = new[] { Field("Pots", i => i.Pots, (i, v) => i.Pots = v) } },
            new GoodDescriptor { Id = TradeGood.HandTools, Curve = "HandTools", Active = _ => true, Recipe = CraftGood.HandTools,
                Parts = new[] { Field("HandTools", i => i.HandTools, (i, v) => i.HandTools = v) } },
            new GoodDescriptor { Id = TradeGood.FarmTools, Curve = "FarmTools", Active = _ => true, Recipe = CraftGood.FarmTools,
                Parts = new[] { Field("FarmTools", i => i.FarmTools, (i, v) => i.FarmTools = v) } },
            new GoodDescriptor { Id = TradeGood.Stone, Curve = "Stone", Active = _ => true, Parts = new[] { Field("Stone", i => i.Stone, (i, v) => i.Stone = v) } },
            new GoodDescriptor { Id = TradeGood.Clay, Curve = "Clay", Active = _ => true, Parts = new[] { Field("Clay", i => i.Clay, (i, v) => i.Clay = v) } },
            new GoodDescriptor { Id = TradeGood.Wood, Curve = "Wood", Active = _ => true, Parts = new[] { Field("Wood", i => i.Wood, (i, v) => i.Wood = v) } },
        };
        private static readonly int[] partOffsets = Offsets();
        private static int[] Offsets()
        {
            var o = new int[Registry.Length]; int n = 0;
            for (int i = 0; i < Registry.Length; i++)
            {
                if ((int)Registry[i].Id != i) throw new InvalidOperationException("Trade registry order must match TradeGood.");
                if (Registry[i].Makeable && Registry[i].Parts.Length != 1) throw new InvalidOperationException("A made trade good has one part.");
                o[i] = n; n += Registry[i].Parts.Length;
            }
            return o;
        }
        internal static readonly int PartCount = Registry.Sum(g => g.Parts.Length);
        internal static int PartIndex(TradeGood good, int part) => partOffsets[(int)good] + part;
        internal static GoodDescriptor Descriptor(TradeGood good) => Registry[(int)good];
        internal static double Held(Camp c, TradeGood good) { double s = 0; foreach (var p in Descriptor(good).Parts) s += Math.Max(0, p.Get(c)); return s; }

        // Per-camp inputs shared by the curve builders: last turn's committed food income, the recipe table, and the
        // need-good terms (computed once per camp, on first use).
        internal sealed class CampContext
        {
            public double LastProduction; public CraftRecipe[] Recipes;
            // Last turn's committed Stone, Clay and Wood use and Hand Tools consumed (production receipts): the lagged need for
            // raw materials and Hand Tools.
            public double[] MaterialUse = new double[4];
            private bool needs; private double clothingV, clothingNeed, housingV, shelter;
            public void NeedTerms(WorldState w, Camp c, out double cv, out double cn, out double hv, out double sh)
            {
                if (!needs) { ClaimValuation.NeedGoodTerms(w, c, out clothingV, out clothingNeed, out _, out housingV, out shelter); needs = true; }
                cv = clothingV; cn = clothingNeed; hv = housingV; sh = shelter;
            }
        }
        // Curve builders by key, from the committed camp.
        private static readonly Dictionary<string, Func<WorldState, Camp, CampContext, CurveTerms>> Builders = new Dictionary<string, Func<WorldState, Camp, CampContext, CurveTerms>>
        {
            // The clothing claim's curve; Cloth and Animal Fabric both fill it.
            ["Clothing"] = (w, c, x) =>
            {
                x.NeedTerms(w, c, out double v, out double need, out _, out _);
                return new CurveTerms { Curve = new ExponentialCurve(v, need, w.Config.Neolithic.ValueCurveSteepness), Floor = need };
            },
            // A Housing Material unit completes one fixed Housing unit; worthless to a camp that cannot build.
            ["Housing"] = (w, c, x) =>
            {
                var cfg = w.Config;
                x.NeedTerms(w, c, out _, out _, out double v, out double shelter);
                bool canBuild = CanBuild(c);
                double quantity = canBuild ? Math.Max(0, NeolithicProduction.Demand(w, c, CraftGood.Housing, cfg.TurnDays / cfg.DaysPerYear, c.Population)) : 0;
                return new CurveTerms { Curve = new ExponentialCurve(canBuild ? v : 0, shelter + quantity, cfg.Neolithic.ValueCurveSteepness), Offset = shelter, Floor = shelter + quantity };
            },
            // The planner's food buckets on this turn's envelope P = stock + last turn's production: consumption of
            // every linked consumer (v_food), clothing-need feedstock and operating reserve (v_reserve), feasible
            // growth (v_growth), surplus (0). Protected food (consumption, feedstock, reserve) is never given;
            // under the safe-only policy, growth food is not given either.
            ["Food"] = (w, c, x) =>
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
                    Offset = x.LastProduction, Floor = cfg.TradeFoodSafeOnly ? growthEnd : protectedEnd
                };
            },
            // Phase V (Docs/PlannerTradeStorageDesign.md §2 P1): the planner's own claim terms (ClaimValuation), from the committed
            // camp, so trade and the fulfilment plan price a unit alike. Every non-food curve's floor is its useful need: stock
            // below it is never offered (the Export mark); only stock above it, or spare making capacity, is.
            // Tents: the Light Housing claim, V = people per tent x housing value per person over a tent's life, on all shelter in
            // tent units (fixed Housing counts), up to the shelter the residents need.
            ["Tents"] = (w, c, x) =>
            {
                var cfg = w.Config; var n = cfg.Neolithic; double years = cfg.TurnDays / cfg.DaysPerYear;
                ClaimValuation.CampTerms(w, c, out double e, out double b, out double s);
                double v = n.LightHousingPeoplePerUnit * n.RudimentaryWeightHousing * e * b * s * ClaimValuation.HousingLife(n, CraftGood.LightHousing);
                var shelter = NeolithicProduction.HousingCapacity(w, c, w.Days + years * cfg.DaysPerYear);
                double stock = shelter.Total / n.LightHousingPeoplePerUnit, gap = OwnDemand(w, c, CraftGood.LightHousing);
                double need = gap > 0 ? stock + gap : Math.Min(stock, c.Population / n.LightHousingPeoplePerUnit);
                return new CurveTerms { Curve = new ExponentialCurve(v, need, n.ValueCurveSteepness), Offset = stock - Held(c, TradeGood.LightHousing), Floor = need };
            },
            // Pots: the food-security claim, V = PotValue at the camp's food value, over the reserve's target Pots, positioned at
            // the Pots this turn's decay leaves.
            ["Pots"] = (w, c, x) =>
            {
                var cfg = w.Config; var n = cfg.Neolithic; double years = cfg.TurnDays / cfg.DaysPerYear;
                double need = Math.Max(0, NeolithicProduction.TargetPots(w, c, years));
                double retained = Held(c, TradeGood.Pots) * Math.Pow(1 - n.PotDecayPerReferenceTurn, years / n.ReferenceTurnYears);
                return new CurveTerms { Curve = new ExponentialCurve(ClaimValuation.PotValue(n, ExpansionValuation.FoodValue(cfg, c)), need, n.ValueCurveSteepness),
                    Offset = retained - Held(c, TradeGood.Pots), Floor = need };
            },
            // Hand Tools (no planner claim): their replacement labour at the lagged labour price, up to last turn's use (receipts,
            // so no tool schedule runs here). Farm Tools: the farmer-years one frees at the lagged farm-labour price, as the
            // planner's labour-saving claim, up to this turn's field need.
            ["HandTools"] = (w, c, x) => Priced(c, price => Band(x.MaterialUse[3],
                price * Math.Max(0, ClaimValuation.LabourPerUnit(w, c, x.Recipes.First(r => r.Good == CraftGood.HandTools), x.Recipes, 1)))),
            ["FarmTools"] = (w, c, x) => c.EconomicLabor != null && c.EconomicLabor.FarmLabourPriceSet && c.EconomicLabor.FarmLabourPrice > 0 && CultivationEconomy.Enabled(c)
                ? Band(Math.Max(0, CultivationEconomy.ForecastFarmToolWorkerYears(w, c, w.Config.TurnDays / w.Config.DaysPerYear)) / w.Config.Neolithic.FarmToolFarmerYearsPerUnit,
                    c.EconomicLabor.FarmLabourPrice * ClaimValuation.FarmToolSavedYears(w.Config.Neolithic))
                : new CurveTerms { Unpriced = true, Floor = double.PositiveInfinity },
            // Raw materials: their extraction labour at the lagged labour price, up to last turn's use over the reserve horizon.
            ["Stone"] = (w, c, x) => Material(w, c, x, MaterialId.Stone, 0),
            ["Clay"] = (w, c, x) => Material(w, c, x, MaterialId.Clay, 1),
            ["Wood"] = (w, c, x) => Material(w, c, x, MaterialId.Wood, 2),
        };
        // Every curve key has a provisional builder, so any good can be valued before a planner publishes a schedule for it.
        internal static readonly bool EveryKeyBuilt = Registry.All(g => Builders.ContainsKey(g.Curve)) ? true : throw new InvalidOperationException("Every trade curve key needs a provisional builder.");
        // The planner's published schedules by curve key (none: every key uses its provisional builder).
        internal static Dictionary<string, TradeValueSchedule> Published(Camp c)
        {
            var list = c.EconomicLabor?.TradeValues;
            if (list == null || list.Count == 0) return null;
            var result = new Dictionary<string, TradeValueSchedule>();
            foreach (var s in list) if (s != null && s.Error() == null && !result.ContainsKey(Descriptor(s.Good).Curve)) result[Descriptor(s.Good).Curve] = s;
            return result;
        }
        internal static CurveTerms Terms(TradeValueSchedule s) => new CurveTerms
        {
            Curve = s.Exponential ? (ValueCurve)new ExponentialCurve(s.V, s.N, s.K) : new BandCurve((double[])s.Ends.Clone(), (double[])s.Values.Clone()),
            Offset = s.Offset, Floor = s.Floor
        };
        private static CurveTerms Band(double need, double value)
            => new CurveTerms { Curve = new BandCurve(new[] { Math.Max(0, need) }, new[] { Math.Max(0, value) }), Floor = Math.Max(0, need) };
        // A curve valued at the lagged labour price; unpriced (never traded by this camp) until a plan has recorded one.
        private static CurveTerms Priced(Camp c, Func<double, CurveTerms> build)
            => c.EconomicLabor != null && c.EconomicLabor.LabourPriceSet && c.EconomicLabor.LabourPrice > 0 ? build(c.EconomicLabor.LabourPrice) : new CurveTerms { Unpriced = true, Floor = double.PositiveInfinity };
        private static double OwnDemand(WorldState w, Camp c, CraftGood good)
            => Math.Max(0, NeolithicProduction.Demand(w, c, good, w.Config.TurnDays / w.Config.DaysPerYear, c.Population));
        private static CurveTerms Material(WorldState w, Camp c, CampContext x, MaterialId m, int index) => Priced(c, price =>
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear, perYear = cfg.DaysPerYear / cfg.BaseDays;
            string role = MaterialEconomy.Specialist(m);
            double rate = n.SpecialistMaterialExtractionRate > 0 ? SpecialistProductivity.ExtractionRate(w, c, m, c.People.HasTech(EconomicIntents.RequiredTechnology(role)) ? role : WorkforceRoles.Gatherer) : 0;
            return rate > 0 ? Band(x.MaterialUse[index] * n.MaterialReserveMultiplier(years), price / (rate * perYear)) : new CurveTerms { Unpriced = true, Floor = double.PositiveInfinity };
        });
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
            // Tool outputs left for made-to-order equipment and housing work, shared by every such good (a Hand Tool covers
            // one output, a Regular Tool four), and which goods draw on it.
            public double ToolPool = double.PositiveInfinity;
            public readonly bool[] UsesTools = new bool[Registry.Length];
            // Goods storage (Docs/PlannerTradeStorageDesign.md §3.5): free room in goods units, shared by the stored goods, and
            // which goods it bounds; infinite without storage. A pair is checked on its net change (a swap frees the room of
            // the stored good it gives), so a full store can still trade one stored good for another.
            public double GoodsRoom = double.PositiveInfinity;
            public readonly bool[] Stored = new bool[Registry.Length];
            // Net stored units a camp takes on by receiving `inQ` of one good and giving `outQ` of another.
            internal double NetStored(TradeGood received, double inQ, TradeGood given, double outQ)
                => (Stored[(int)received] ? inQ : 0) - (Stored[(int)given] ? outQ : 0);
            internal bool Fits(TradeGood received, double inQ, TradeGood given, double outQ)
                => double.IsPositiveInfinity(GoodsRoom) || NetStored(received, inQ, given, outQ) <= Math.Max(0, GoodsRoom) + 1e-9;
            public double MakeCapacity(TradeGood g) => UsesTools[(int)g] ? Math.Min(Capacity[(int)g], ToolPool) : Capacity[(int)g];
            public readonly double[][] Shares = new double[Registry.Length][];          // committed part proportions
            private readonly Dictionary<string, (double Position, double Floor)> positions = new Dictionary<string, (double, double)>();
            internal void SetCurve(string key, double position, double floor) => positions[key] = (position, floor);
            public ValueCurve Curve(TradeGood g) => Curves[(int)g];
            public double Position(TradeGood g) => positions[Descriptor(g).Curve].Position;
            public double Floor(TradeGood g) => positions[Descriptor(g).Curve].Floor;
            // What the camp may give of a good: held stock above its curve's floor, plus what it can make to order.
            public double Stock(TradeGood g) => Active[(int)g] ? Math.Max(0, Math.Min(Held[(int)g], Position(g) - Floor(g))) + MakeCapacity(g) : 0;
            public double Marginal(TradeGood g, double x) => Active[(int)g] ? Curve(g).Marginal(x) : 0;
            // Make or buy (Docs/Trade.md): the value of a unit at position x to a camp that can still make `cap` more at cost c.
            // With u its use value and x* = u.Crossing(c), W'(x) = u(x + cap) below x* - cap, c on [x* - cap, x*], u(x) above.
            // Receiving and giving are both valued on W, so a camp with little making capacity prices stock near its use value.
            private readonly ValueCurve.Segment[][] wCache = new ValueCurve.Segment[Registry.Length][];
            private readonly double[] wCap = Enumerable.Repeat(double.NaN, Registry.Length).ToArray();
            internal double Crossing(TradeGood g) { int i = (int)g; return Active[i] && !double.IsPositiveInfinity(Cost[i]) ? Curve(g).Crossing(Cost[i]) : double.NegativeInfinity; }
            internal ValueCurve.Segment[] W(TradeGood g)
            {
                int i = (int)g; double cap = MakeCapacity(g);
                if (wCache[i] != null && cap.Equals(wCap[i])) return wCache[i];
                var u = Curve(g).Uncapped; double c = Cost[i], xs = Crossing(g);
                ValueCurve.Segment[] w;
                if (!(cap > 0) || double.IsPositiveInfinity(c) || double.IsNegativeInfinity(xs)) w = u;
                else
                {
                    var list = new List<ValueCurve.Segment>();
                    foreach (var seg in u)
                    {
                        // Below x* - cap: the curve shifted down by cap (own making saturates), u(x + cap).
                        double lo = seg.Lo - cap, hi = Math.Min(seg.Hi, xs) - cap;
                        if (hi > lo) list.Add(new ValueCurve.Segment { Lo = lo, Hi = hi, C = seg.C, V = seg.V, N = seg.N, K = seg.K, S = seg.S + cap, Exponential = seg.Exponential });
                    }
                    list.Add(new ValueCurve.Segment { Lo = xs - cap, Hi = xs, C = c });
                    foreach (var seg in u)
                        if (seg.Hi > xs) list.Add(new ValueCurve.Segment { Lo = Math.Max(seg.Lo, xs), Hi = seg.Hi, C = seg.C, V = seg.V, N = seg.N, K = seg.K, S = seg.S, Exponential = seg.Exponential });
                    w = list.ToArray();
                }
                wCache[i] = w; wCap[i] = cap;
                return w;
            }
            // The extra making that giving q from the current position induces: own making refills towards x* within cap.
            internal double Induced(TradeGood g, double q)
            {
                double cap = MakeCapacity(g), xs = Crossing(g), pos = Position(g);
                if (!(cap > 0) || double.IsNegativeInfinity(xs) || !(q > 0)) return 0;
                double Fill(double x) => Math.Max(0, Math.Min(cap, xs - x));
                return Math.Max(0, Math.Min(q, Fill(pos - q) - Fill(pos)));
            }
            public double LossMarginal(TradeGood g, double x) => Active[(int)g] ? ValueCurve.Marginal(W(g), x) : 0;
            public double GainMarginal(TradeGood g, double x) => LossMarginal(g, x);
            public double Integrated(TradeGood g, double a, double b) => Active[(int)g] ? Curve(g).Integrated(a, b) : 0;
            public double Gain(TradeGood g, double q) { Counters.Count(ref Counters.Integrals); return Active[(int)g] ? ValueCurve.Integral(W(g), Position(g), Position(g) + q) : 0; }
            public double Loss(TradeGood g, double q) { Counters.Count(ref Counters.Integrals); return Active[(int)g] ? ValueCurve.Integral(W(g), Position(g) - q, Position(g)) : 0; }
            // Bumped by every Apply: a response scored on unchanged positions is still exact.
            public int Version;
            // Returns the part of a gift that is made to order (zero for receipts).
            public double Apply(TradeGood g, double amount)
            {
                int i = (int)g; Version++; double madeOut = 0;
                // Giving draws the induced making on capacity (and the tool pool) and the rest on held stock.
                if (amount < 0)
                {
                    double made = Math.Min(Induced(g, -amount), Capacity[i]);
                    double giveable = Math.Max(0, Math.Min(Held[i], Position(g) - Floor(g)));
                    if (-amount - made > giveable) made = Math.Min(Capacity[i], -amount - giveable);
                    Capacity[i] = Math.Max(0, Capacity[i] - made); if (UsesTools[i]) ToolPool = Math.Max(0, ToolPool - made);
                    Held[i] = Math.Max(0, Held[i] - (-amount - made)); madeOut = made;
                    if (Stored[i]) GoodsRoom += -amount - made;
                }
                else { Held[i] += amount; if (Stored[i]) GoodsRoom -= amount; }
                var key = Descriptor(g).Curve; var p = positions[key]; positions[key] = (p.Position + amount, p.Floor);
                return madeOut;
            }
            internal double[] Parts(TradeGood g, double quantity) => Shares[(int)g].Select(s => s * quantity).ToArray();
        }

        internal static bool Eligible(WorldState w, Camp c)
            => w.Config.TradeEnabled && c.Population > 0 && c.Player != 0
               && CultivationEconomy.EconomyEnabled(w, c) && c.People?.Memory != null;

        // Last turn's committed food production per camp, the lagged income forecast of the food envelope.
        internal static Dictionary<int, double> LastProduction(WorldState w)
        {
            var result = new Dictionary<int, double>();
            var last = w.History.Count > 0 ? w.History[w.History.Count - 1] : null;
            if (last?.Demographics != null) foreach (var d in last.Demographics) result[d.Camp] = Math.Max(0, d.FoodProduced);
            return result;
        }

        // Last turn's committed Stone, Clay and Wood use per camp, from its production receipts.
        internal static Dictionary<int, double[]> LastMaterialUse(WorldState w)
        {
            var result = new Dictionary<int, double[]>();
            var last = w.History.Count > 0 ? w.History[w.History.Count - 1] : null;
            if (last?.Production != null)
                foreach (var p in last.Production)
                {
                    if (!result.TryGetValue(p.Camp, out var use)) result[p.Camp] = use = new double[4];
                    use[0] += Math.Max(0, p.Stone); use[1] += Math.Max(0, p.Clay); use[2] += Math.Max(0, p.Wood); use[3] += Math.Max(0, p.HandToolsConsumed);
                }
            return result;
        }

        // O(goods) per camp: one builder call per curve key, one recipe cost per makeable good.
        internal static Valuation Value(WorldState w, Camp c, double lastProduction = 0, CraftRecipe[] recipes = null, double[] materialUse = null)
        {
            var context = new CampContext { LastProduction = lastProduction, Recipes = recipes ?? NeolithicProduction.Recipes(w.Config), MaterialUse = materialUse == null ? new double[4] : materialUse.Length >= 4 ? materialUse : materialUse.Concat(new double[4 - materialUse.Length]).ToArray() };
            var cfg = w.Config;
            var v = new Valuation { Camp = c.Id, Society = c.SocietyId, Tile = c.Tile };
            var built = new Dictionary<string, CurveTerms>(); var held = new Dictionary<string, double>();
            // A key the planner has priced uses its published schedule; every other key its provisional builder.
            var published = Published(c);
            CurveTerms Build(string key) => published != null && published.TryGetValue(key, out var s) ? Terms(s) : Builders[key](w, c, context);
            foreach (var g in Registry)
            {
                int i = (int)g.Id;
                v.Active[i] = g.Active(cfg);
                v.Held[i] = Held(c, g.Id);
                v.Shares[i] = g.Parts.Select(p => v.Held[i] > 1e-12 ? Math.Max(0, p.Get(c)) / v.Held[i] : 1.0 / g.Parts.Length).ToArray();
                if (!v.Active[i]) continue;
                if (!built.TryGetValue(g.Curve, out var terms)) built[g.Curve] = terms = Build(g.Curve);
                if (terms.Unpriced) { v.Active[i] = false; continue; }
                v.Curves[i] = terms.Curve;
                held[g.Curve] = (held.TryGetValue(g.Curve, out double h) ? h : 0) + v.Held[i];
            }
            foreach (var pair in built) v.SetCurve(pair.Key, pair.Value.Offset + (held.TryGetValue(pair.Key, out double h) ? h : 0), pair.Value.Floor);
            foreach (var g in Registry) if (!v.Active[(int)g.Id] && (!built.TryGetValue(g.Curve, out var t) || t.Unpriced)) v.SetCurve(g.Curve, v.Held[(int)g.Id], double.PositiveInfinity);
            // Food a made recipe may consume: what lies above the protected band (consumption, feedstock, reserve).
            // Goods storage: the free room at the committed boundary (this turn's tool use is not yet known, so tools count as stored).
            if (GoodsStorage.Enabled(cfg))
            {
                v.GoodsRoom = Math.Max(0, GoodsStorage.Begin(w, c, cfg.TurnDays / cfg.DaysPerYear, c.Population, 0, 0).RoomUnits(c));
                foreach (var g in Registry) v.Stored[(int)g.Id] = g.Recipe.HasValue && GoodsStorage.Index(g.Recipe.Value) >= 0;
            }
            // A published food schedule protects the food below its floor.
            var foodTerms = built.TryGetValue("Food", out var food) ? food : Build("Food");
            bool foodPublished = published != null && published.ContainsKey("Food");
            double foodPosition = foodTerms.Offset + Held(c, TradeGood.Food), protectedEnd = foodPublished ? foodTerms.Floor : ((BandCurve)foodTerms.Curve).Ends[1];
            double spareFood = Math.Max(0, Math.Min(Held(c, TradeGood.Food), foodPosition - protectedEnd));
            MakeToOrder(w, c, v, foodTerms.Curve.Marginal(foodPosition), spareFood, context.Recipes, published);
            return v;
        }

        // A good the camp can make: its cost is the labour per unit at the lagged labour price w (the last plan's,
        // EconomicLaborPlan.LabourPrice) plus its food inputs at the camp's marginal food value; its capacity is what
        // TradeExportLabourShare of the residents' turn could make, bounded by the spare food its recipe consumes and,
        // for equipment and housing work, by that share of its tool outputs (one pool for all such goods).
        // A make cost the planner published for the good's curve replaces that derivation. Without a recorded price or a
        // published cost nothing is made to order.
        private static void MakeToOrder(WorldState w, Camp c, Valuation v, double foodValue, double spareFood, CraftRecipe[] recipes, Dictionary<string, TradeValueSchedule> published)
        {
            var cfg = w.Config;
            bool priced = c.EconomicLabor != null && c.EconomicLabor.LabourPriceSet;
            bool anyPublishedCost = published != null && published.Values.Any(s => s.MakeCostSet);
            if (!(cfg.TradeExportLabourShare > 0) || c.EconomicLabor == null || !priced && !anyPublishedCost) return;
            double price = c.EconomicLabor.LabourPrice, years = cfg.TurnDays / cfg.DaysPerYear;
            v.ToolPool = cfg.TradeExportLabourShare * (Math.Max(0, c.Inventory.HandTools) * ToolCoverageBudget.OutputsPerHandTool + Math.Max(0, c.Inventory.Tools) * ToolCoverageBudget.OutputsPerTool);
            double multiplier = NeolithicProduction.HousingMultiplier(w, c, c.Population);
            // Raw materials a recipe needs: stock plus what the camp's known sources hold, as the planner counts them
            // (CampProductionDemand.Accessible). Census C1 turn 53: Pots promised by camps with no Clay to reach.
            Dictionary<MaterialId, double> materials = null;
            double Material(MaterialId m)
            {
                if (materials == null)
                {
                    var n = cfg.Neolithic; var sources = MaterialEconomy.KnownSources(w, c, w.Days);
                    double Priority(MaterialId x) => x == MaterialId.Stone ? c.StonePriority : x == MaterialId.Clay ? c.ClayPriority : c.WoodPriority;
                    materials = MaterialEconomy.Materials.ToDictionary(x => x, x => MaterialEconomy.Inventory(c, x)
                        + (Priority(x) <= 0 || n.SpecialistMaterialExtractionRate <= 0 ? 0 : sources.Where(s => s.Material == x).GroupBy(s => s.Id).Sum(gr => gr.Sum(s => s.Available))));
                }
                return Math.Max(0, materials[m]);
            }
            foreach (var g in Registry)
            {
                int i = (int)g.Id;
                if (!v.Active[i] || !g.Recipe.HasValue) continue;
                var recipe = recipes.FirstOrDefault(r => r.Good == g.Recipe.Value);
                if (!NeolithicProduction.RecipeAvailable(w, c, recipe)) continue;
                if (NeolithicProduction.Priority(c, recipe.Good) <= 0) continue;
                double labour = ClaimValuation.LabourPerUnit(w, c, recipe, recipes, multiplier);
                if (!(labour > 0) || double.IsInfinity(labour)) continue;
                TradeValueSchedule schedule = null;
                bool costPublished = published != null && published.TryGetValue(g.Curve, out schedule) && schedule.MakeCostSet;
                if (!costPublished && !priced) continue;
                v.Cost[i] = costPublished ? schedule.MakeCost : labour * price + (recipe.PlantFood + recipe.AnimalFood) * Math.Max(0, foodValue);
                double capacity = cfg.TradeExportLabourShare * c.Population * years / labour;
                // Food inputs come from spare food only, typed: Animal Fabric needs animal food, Cloth plant food.
                // Spare food is shared across types in the stock's proportions (consumption draws on every type).
                double stockFood = Math.Max(1e-12, c.Food);
                if (recipe.PlantFood > 0) capacity = Math.Min(capacity, spareFood * Math.Max(0, c.PlantFood) / stockFood / recipe.PlantFood);
                if (recipe.AnimalFood > 0) capacity = Math.Min(capacity, spareFood * Math.Max(0, c.AnimalFood) / stockFood / recipe.AnimalFood);
                // Made goods consumed as inputs (Animal Fabric for tents, Housing Material for Housing) come from stock on hand:
                // a camp without the fabric cannot promise the tents (census C1 turn 52: tents owed with no fabric held).
                if (recipe.AnimalFabric > 0) capacity = Math.Min(capacity, Math.Max(0, c.Inventory.AnimalFabric) / recipe.AnimalFabric);
                if (recipe.HousingMaterial > 0) capacity = Math.Min(capacity, Math.Max(0, c.Inventory.HousingMaterial) / recipe.HousingMaterial);
                if (recipe.Stone > 0) capacity = Math.Min(capacity, Material(MaterialId.Stone) / recipe.Stone);
                if (recipe.Clay > 0) capacity = Math.Min(capacity, Material(MaterialId.Clay) / recipe.Clay);
                if (recipe.Wood + recipe.ConstructionMaterial > 0) capacity = Math.Min(capacity, (recipe.ConstructionMaterial > 0 ? Material(MaterialId.Wood) + Material(MaterialId.Clay) : Material(MaterialId.Wood)) / (recipe.Wood + recipe.ConstructionMaterial));
                v.Capacity[i] = Math.Max(0, capacity);
                // Equipment and housing work need tool coverage: their exports share the tool pool.
                v.UsesTools[i] = recipe.Equipment || ToolCoverageBudget.IsHousingWork(recipe.Good);
            }
        }

        // Requester's best 1:1 quantity: marginal want value equals marginal give
        // loss, bounded by what it may give. Bisection over a monotone difference.
        internal static double BestQuantity(Valuation r, TradeGood want, TradeGood give)
        {
            Counters.Count(ref Counters.BestQuantities);
            if (Descriptor(want).Curve == Descriptor(give).Curve) return 0;
            double hi = r.Stock(give);
            if (!(hi > 1e-9)) return 0;
            double Slope(double q) => r.GainMarginal(want, r.Position(want) + q) - r.LossMarginal(give, r.Position(give) - q);
            if (!(Slope(0) > 0)) return 0;
            if (Slope(hi) > 0) return hi;
            double lo = 0;
            for (int i = 0; i < 40; i++) { double mid = (lo + hi) / 2; if (Slope(mid) > 0) lo = mid; else hi = mid; }
            return lo;
        }

        // The list length W: at least TradeListLength and growing with the number of tradeable goods G as ceil(sqrt(G)),
        // so the W x W pairs a responder evaluates grow linearly in G.
        internal static int ListLength(SimulationConfig cfg)
            => Math.Max(cfg.TradeListLength, (int)Math.Ceiling(Math.Sqrt(Registry.Count(g => g.Active(cfg)))));

        // What a camp offers: every good it may give now (stock above its floor or making capacity). Goods only, no
        // quantities: partners request only goods it offers, so a request it could never fill is never sent.
        internal static bool[] Offers(Valuation v) => Registry.Select(g => v.Stock(g.Id) > 1e-9).ToArray();

        // The requester's bundle lists (QoLFissionTradeDesign 6.2-6.3), O(goods log W): the W goods with the most
        // recoverable utility (integrated gain, capped by its own making cost, up to its target coverage) among those
        // the partner offers, and the W giveable goods of lowest marginal loss. It sees what a partner offers, never
        // how much, so the responder chooses quantities. Ties: lower good id.
        internal static (TradeGood[] Wants, TradeGood[] Gives) Lists(Valuation r, int length, bool[] offered = null)
        {
            var wants = new List<(double Score, int Id)>(); var gives = new List<(double Score, int Id)>();
            foreach (var g in Registry)
            {
                if (!r.Active[(int)g.Id]) continue;
                double position = r.Position(g.Id);
                if (offered == null || offered[(int)g.Id])
                {
                    double recoverable = r.Gain(g.Id, Math.Max(0, r.Curve(g.Id).Target - position));
                    if (recoverable > 1e-12) wants.Add((recoverable, (int)g.Id));
                }
                if (r.Stock(g.Id) > 1e-9) gives.Add((r.LossMarginal(g.Id, position - 1e-9), (int)g.Id));
            }
            return (wants.OrderByDescending(x => x.Score).ThenBy(x => x.Id).Take(length).Select(x => (TradeGood)x.Id).ToArray(),
                    gives.OrderBy(x => x.Score).ThenBy(x => x.Id).Take(length).Select(x => (TradeGood)x.Id).ToArray());
        }

        // Requester limit: the most of `give` per unit of `want` it accepts for q, in closed form: the largest
        // payment whose loss stays within its gain after transport and margin, and within what it may give.
        internal static double MaximumRatio(Valuation r, TradeGood want, TradeGood give, double q, double margin, double transport)
        {
            Counters.Count(ref Counters.RatioSearches);
            if (!(q > 0)) return double.NaN;
            double gain = r.Gain(want, q);
            if (!(gain > 0)) return double.NaN;
            double budget = gain * (1 - transport) / (1 + margin), stock = r.Stock(give);
            double paid = r.Active[(int)give] ? Math.Min(stock, ValueCurve.InverseDown(r.W(give), r.Position(give), budget)) : stock;
            return Math.Max(0, paid) / q;
        }

        // Responder floor: the least of the requester's `give` per unit of `want` it accepts for handing over q
        // units, in closed form: the smallest payment whose gain covers its loss after transport and margin.
        // NaN when no ratio up to `cap` works.
        internal static double MinimumRatio(Valuation p, TradeGood want, TradeGood give, double q, double margin, double transport, double cap)
        {
            Counters.Count(ref Counters.RatioSearches);
            if (!(q > 0) || q > p.Stock(want) + 1e-9 || !(cap > 0) || !p.Active[(int)give] || !(transport < 1)) return double.NaN;
            double needed = (1 + margin) * p.Loss(want, q) / (1 - transport);
            double paid = needed > 0 ? ValueCurve.InverseUp(p.W(give), p.Position(give), needed) - p.Position(give) : 0;
            // A zero loss still needs some payment of positive value.
            if (!(needed > 0)) paid = p.Marginal(give, p.Position(give)) > 0 ? cap * 1e-12 * q : double.PositiveInfinity;
            double ratio = paid / q;
            return ratio <= cap * (1 + 1e-12) ? Math.Min(ratio, cap) : double.NaN;
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
        // `flows` holds the (giver, receiver, curve) flows already agreed this turn: a pair never moves one curve both ways.
        private static TradeAgreement Respond(Valuation r, Valuation p, Requisition x, SimulationConfig cfg, HashSet<(int, int, string)> flows)
        {
            Counters.Count(ref Counters.Responds);
            double margin = cfg.TradeMargin * (1 - x.Affinity), transport = Math.Min(1, cfg.TradeDistanceCostPerHex * x.Distance);
            TradeAgreement best = null;
            foreach (var want in x.Wants)
                foreach (var give in x.Gives)
                {
                    if (Descriptor(want).Curve == Descriptor(give).Curve || !(p.Stock(want) > 1e-9)) continue;
                    if (flows.Contains((r.Camp, p.Camp, Descriptor(want).Curve)) || flows.Contains((p.Camp, r.Camp, Descriptor(give).Curve))) continue;
                    Counters.Count(ref Counters.Pairs);
                    double q = Math.Min(BestQuantity(r, want, give), p.Stock(want));
                    for (int attempt = 0; attempt < 4 && q > 1e-6; attempt++, q /= 2)
                    {
                        double limit = MaximumRatio(r, want, give, q, margin, transport);
                        if (double.IsNaN(limit)) continue;
                        double floor = MinimumRatio(p, want, give, q, margin, transport, limit);
                        if (double.IsNaN(floor) || floor > limit + 1e-12) continue;
                        bool countered = floor > 1 + 1e-12;
                        double ratio = countered ? floor : Math.Min(1, limit), paid = ratio * q;
                        // Storage room, on each side's net change in stored goods; a pair that does not fit tries half.
                        if (!r.Fits(want, q, give, paid) || !p.Fits(give, paid, want, q)) continue;
                        double responderGain = p.Gain(give, paid) * (1 - transport) - p.Loss(want, q);
                        if (best == null || responderGain > best.ResponderGain + 1e-12)
                            best = new TradeAgreement { Requester = r.Camp, Responder = p.Camp, Distance = x.Distance, Want = want, Give = give,
                                WantQuantity = q, GiveQuantity = paid, Ratio = ratio, Affinity = x.Affinity, Countered = countered,
                                RequesterGain = r.Gain(want, q) * (1 - transport) - r.Loss(give, paid), ResponderGain = responderGain };
                        break;
                    }
                }
            return best;
        }

        // The bounded same-turn handshake: O(camps (goods log W + contacts)) pure per-camp work, then
        // O(camps K W^2) canonical responses (responder ID, then the responder's own best gain, then requester
        // ID), updating both sides' positions so no camp is promised the same stock or capacity twice.
        internal static TradePlan Plan(WorldState w, PhaseExecution execution = null, ISet<int> excluded = null)
        {
            var cfg = w.Config;
            if (!cfg.TradeEnabled) return TradePlan.Empty;
            using var profile = PhaseProfile.Scope("Trade.Plan");
            var plan = new TradePlan();
            var eligible = w.Camps.Where(c => Eligible(w, c) && (excluded == null || !excluded.Contains(c.Id))).OrderBy(c => c.Id).ToArray();
            PhaseProfile.Count("Trade.Eligible", eligible.Length);
            plan.Summary.Eligible = eligible.Length;
            if (eligible.Length < 2) return plan;
            var workers = execution ?? new PhaseExecution();
            var production = LastProduction(w); var materialUse = LastMaterialUse(w);
            long stage = System.Diagnostics.Stopwatch.GetTimestamp();
            var recipeTable = NeolithicProduction.Recipes(cfg);
            var map = w.Map;
            // Partners: remembered settlements of other Societies, nearest first, at most K. They depend only on the contact
            // memory, each eligible camp's Society and distance, never on a valuation, so they are chosen first and only camps
            // that can take part (a requester with a partner, and its partners) are valued; a camp with no possible partner
            // could never send or receive a requisition.
            var societyOf = eligible.ToDictionary(c => c.Id, c => c.SocietyId);
            var partnersOf = workers.Map(eligible, c => c.People.Memory.Contacts
                .Where(k => !k.Moved && k.Tile >= 0 && societyOf.TryGetValue(k.Camp, out int society) && society != c.SocietyId)
                .Select(k => (k.Camp, Distance: map.Distance(c.Tile, k.Tile)))
                .Where(p => p.Distance <= cfg.TradeRange)
                .OrderBy(p => p.Distance).ThenBy(p => p.Camp).Take(cfg.TradeRequisitionsPerCamp).ToArray());
            var taking = new HashSet<int>();
            for (int i = 0; i < eligible.Length; i++)
                if (partnersOf[i].Length > 0) { taking.Add(eligible[i].Id); foreach (var partner in partnersOf[i]) taking.Add(partner.Camp); }
            var participants = eligible.Where(c => taking.Contains(c.Id)).ToArray();
            var valueScope = PhaseProfile.Scope("Valuation");
            var values = workers.Map(participants, c => Value(w, c, production.TryGetValue(c.Id, out double p) ? p : 0, recipeTable, materialUse.TryGetValue(c.Id, out var u) ? u : null));
            valueScope.Dispose(); PhaseProfile.Count("Trade.Valued", participants.Length);
            if (Counters.Enabled) { Counters.ValueTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stage; stage = System.Diagnostics.Stopwatch.GetTimestamp(); }
            var byCamp = new Dictionary<int, Valuation>(); var offers = new Dictionary<int, bool[]>();
            for (int i = 0; i < participants.Length; i++) { byCamp[participants[i].Id] = values[i]; offers[participants[i].Id] = Offers(values[i]); }
            int length = ListLength(cfg);
            var societies = w.Societies.ToDictionary(s => s.Id);
            var requestScope = PhaseProfile.Scope("Requisitions");
            var requisitions = workers.Map(Enumerable.Range(0, eligible.Length).Where(i => partnersOf[i].Length > 0).ToArray(), i =>
            {
                var c = eligible[i]; var r = byCamp[c.Id]; var list = new List<Requisition>();
                foreach (var p in partnersOf[i])
                {
                    // Wants among what this partner offers; only a request some pair of which this camp itself would accept is sent.
                    var (wants, gives) = Lists(r, length, offers[p.Camp]);
                    if (!wants.Any(a => gives.Any(b => Descriptor(a).Curve != Descriptor(b).Curve && BestQuantity(r, a, b) > 1e-6))) continue;
                    societies.TryGetValue(r.Society, out var mine); societies.TryGetValue(byCamp[p.Camp].Society, out var theirs);
                    list.Add(new Requisition { From = c.Id, To = p.Camp, Distance = p.Distance, Wants = wants, Gives = gives, Affinity = Kinship.Affinity(mine, theirs) });
                }
                return list;
            });
            requestScope.Dispose();
            var all = requisitions.SelectMany(x => x).ToList();
            var responseScope = PhaseProfile.Scope("Responses");
            if (Counters.Enabled) { Counters.RequestTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stage; stage = System.Diagnostics.Stopwatch.GetTimestamp(); }
            plan.Summary.Requisitions = all.Count;
            var flows = new HashSet<(int, int, string)>();
            foreach (var group in all.GroupBy(x => x.To).OrderBy(g => g.Key))
            {
                var p = byCamp[group.Key];
                // Value order: the responder's best gain from each request on its opening positions, then requester ID.
                var ordered = group.Select(x => { var a = Respond(byCamp[x.From], p, x, cfg, flows); return (x, a, RV: byCamp[x.From].Version, PV: p.Version, Score: a?.ResponderGain ?? double.NegativeInfinity); })
                    .OrderByDescending(t => t.Score).ThenBy(t => t.x.From).ToList();
                foreach (var (x, scored, rv, pv, _) in ordered)
                {
                    var r = byCamp[x.From];
                    // Re-evaluated on the positions earlier agreements left, so honours from several partners never
                    // overshoot and no stock is promised twice; a scored answer on unchanged positions is reused.
                    bool unchanged = r.Version == rv && p.Version == pv && (scored == null
                        || !flows.Contains((r.Camp, p.Camp, Descriptor(scored.Want).Curve)) && !flows.Contains((p.Camp, r.Camp, Descriptor(scored.Give).Curve)));
                    var agreement = unchanged ? scored : Respond(r, p, x, cfg, flows);
                    if (agreement == null) { plan.Summary.Ignored++; continue; }
                    agreement.Utility = TradeUtilitySnapshot.Capture(r, p,
                        agreement, cfg);
                    plan.Agreements.Add(agreement);
                    flows.Add((p.Camp, r.Camp, Descriptor(agreement.Want).Curve)); flows.Add((r.Camp, p.Camp, Descriptor(agreement.Give).Curve));
                    // Each leg splits into the giver's stock part (its committed proportions) and its made-to-order part.
                    r.Apply(agreement.Want, agreement.WantQuantity); double giveMade = r.Apply(agreement.Give, -agreement.GiveQuantity);
                    double wantMade = p.Apply(agreement.Want, -agreement.WantQuantity); p.Apply(agreement.Give, agreement.GiveQuantity);
                    agreement.WantMade = wantMade; agreement.GiveMade = giveMade;
                    agreement.WantParts = p.Parts(agreement.Want, Math.Max(0, agreement.WantQuantity - wantMade));
                    agreement.GiveParts = r.Parts(agreement.Give, Math.Max(0, agreement.GiveQuantity - giveMade));
                    plan.Receive(r.Camp, agreement.Want, WithMade(agreement.WantParts, wantMade), agreement.AllStock); plan.Send(p.Camp, agreement.Want, agreement.WantParts, wantMade);
                    plan.Send(r.Camp, agreement.Give, agreement.GiveParts, giveMade); plan.Receive(p.Camp, agreement.Give, WithMade(agreement.GiveParts, giveMade), agreement.AllStock);
                    if (agreement.Countered) plan.Summary.Countered++; else plan.Summary.Honoured++;
                }
            }
            if (Counters.Enabled) Counters.ResponseTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stage;
            responseScope.Dispose();
            if (PhaseProfile.Enabled) PhaseProfile.Count("Trade.Participants", all.Select(x => x.From).Concat(all.Select(x => x.To)).Distinct().Count());
            return plan;
        }

        // A leg's whole delivery by part: its stock parts with the made part added to the (single) part of a made good.
        internal static double[] WithMade(double[] stockParts, double made)
        {
            var parts = (double[])stockParts.Clone();
            if (made > 0) parts[0] += made;
            return parts;
        }

        // A planning view of a camp after its confirmed trades: stock given leaves its stock, receipts that serve this
        // turn join it, and the made-to-order part it owes becomes an export claim its demand adds (TradeExports).
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
            if (plan.Imports.TryGetValue(view.Id, out var imports)) view.TradeImports = (double[])imports.Clone();
        }

        // The fraction of `quantity` of a good the receiver's goods storage still takes (Docs/PlannerTradeStorageDesign.md §3):
        // 1 without storage or for a good it does not count (food, Pots, raw materials).
        private static double RoomFraction(Camp receiver, TradeGood good, double quantity)
        {
            var storage = receiver.GoodsStorage; var d = Descriptor(good);
            if (storage == null || !(quantity > 1e-12) || !d.Recipe.HasValue || GoodsStorage.Index(d.Recipe.Value) < 0) return 1;
            return Math.Max(0, Math.Min(1, storage.Limit(receiver, d.Recipe.Value, quantity) / quantity));
        }
        // The fraction of a leg the giver can deliver from what it holds now.
        private static double Available(Camp from, TradeGood good, double[] amounts)
        {
            var d = Descriptor(good); double scale = 1;
            for (int i = 0; i < amounts.Length; i++) if (amounts[i] > 0) scale = Math.Min(scale, Math.Max(0, d.Parts[i].Get(from)) / amounts[i]);
            return Math.Max(0, Math.Min(1, scale));
        }
        // Takes a stock part from its giver into escrow at the scale it can deliver. Returns the amounts held.
        private static double[] Escrow(Camp from, TradeGood good, double[] amounts, double scale)
        {
            var d = Descriptor(good); var held = new double[amounts.Length];
            for (int i = 0; i < amounts.Length; i++)
            {
                double x = Math.Min(amounts[i] * scale, Math.Max(0, d.Parts[i].Get(from))); if (!(x > 0)) continue;
                d.Parts[i].Set(from, d.Parts[i].Get(from) - x); held[i] = x;
            }
            return held;
        }
        // Releases escrow: `fraction` to the receiver, the rest back to the giver.
        private static void Release(Camp giver, Camp receiver, TradeGood good, double[] held, double fraction)
        {
            if (held == null) return;
            var d = Descriptor(good); fraction = Math.Max(0, Math.Min(1, fraction));
            for (int i = 0; i < held.Length; i++)
            {
                if (!(held[i] > 0)) continue;
                double paid = held[i] * fraction;
                d.Parts[i].Set(receiver, d.Parts[i].Get(receiver) + paid); d.Parts[i].Set(giver, d.Parts[i].Get(giver) + (held[i] - paid));
            }
        }
        private static double Sum(double[] a) { double s = 0; if (a != null) foreach (double x in a) s += x; return s; }
        // After production, the fraction of a leg its giver can deliver: what escrow holds plus, of its made part,
        // what it now holds of the good.
        private static double Deliverable(Camp giver, TradeGood good, double[] held, double stock, double made)
        {
            double total = stock + made;
            if (!(total > 0)) return 1;
            double produced = made > 0 ? Math.Min(made, Math.Max(0, Descriptor(good).Parts[0].Get(giver))) : 0;
            return Math.Max(0, Math.Min(1, (Sum(held) + produced) / total));
        }
        // Delivers `fraction` of a leg: from escrow first (the rest of escrow is refunded), then from the giver's stock.
        private static void Deliver(Camp giver, Camp receiver, TradeGood good, double[] held, double stock, double made, double fraction)
        {
            double amount = fraction * (stock + made), escrowed = Sum(held), fromEscrow = Math.Min(escrowed, amount);
            Release(giver, receiver, good, held, escrowed > 0 ? fromEscrow / escrowed : 0);
            double rest = amount - fromEscrow;
            if (!(rest > 0)) return;
            // Never more than the giver holds, so rounding cannot leave a negative stock.
            var part = Descriptor(good).Parts[0]; double x = Math.Min(rest, Math.Max(0, part.Get(giver)));
            if (x > 0) { part.Set(giver, part.Get(giver) - x); part.Set(receiver, part.Get(receiver) + x); }
        }

        // Settles, in agreement order, the legs of one timing, checking that every part (with what is in escrow)
        // is conserved and none goes negative. At the opening every stock part goes into escrow at the share its giver
        // holds, and an all-stock agreement settles both legs at the smaller share. After production each other agreement
        // settles both legs at the smaller share their givers can deliver (pay on delivery), and the rest is refunded.
        private static void SettleLegs(WorldState w, TradeSettlement s, bool afterProduction)
        {
            var camps = w.Camps.ToDictionary(c => c.Id);
            var parts = Registry.SelectMany(g => g.Parts).ToArray();
            double Total(int i) => w.Camps.Sum(parts[i].Get) + s.Escrowed(i);
            var before = Enumerable.Range(0, parts.Length).Select(Total).ToArray();
            for (int k = 0; k < s.Plan.Agreements.Count; k++)
            {
                var a = s.Plan.Agreements[k];
                if (!camps.TryGetValue(a.Requester, out var r) || !camps.TryGetValue(a.Responder, out var p))
                {
                    // A party that vanished: refund any escrow to the giver still present.
                    if (camps.TryGetValue(a.Responder, out var pr)) Release(pr, pr, a.Want, s.WantEscrow[k], 0);
                    if (camps.TryGetValue(a.Requester, out var rr)) Release(rr, rr, a.Give, s.GiveEscrow[k], 0);
                    s.WantEscrow[k] = s.GiveEscrow[k] = null; s.WantScale[k] = s.GiveScale[k] = 0; continue;
                }
                if (!afterProduction)
                {
                    double fw = Available(p, a.Want, a.WantParts), fg = Available(r, a.Give, a.GiveParts);
                    s.WantEscrow[k] = Escrow(p, a.Want, a.WantParts, fw); s.GiveEscrow[k] = Escrow(r, a.Give, a.GiveParts, fg);
                    if (!a.AllStock) continue;
                    double f = Math.Min(fw, fg);
                    // A receiver short of storage room takes less, and both legs scale with it.
                    f = Math.Min(f * RoomFraction(r, a.Want, f * Sum(a.WantParts)), f * RoomFraction(p, a.Give, f * Sum(a.GiveParts)));
                    Release(p, r, a.Want, s.WantEscrow[k], fw > 0 ? f / fw : 0); Release(r, p, a.Give, s.GiveEscrow[k], fg > 0 ? f / fg : 0);
                    s.WantEscrow[k] = s.GiveEscrow[k] = null; s.WantScale[k] = s.GiveScale[k] = f;
                    continue;
                }
                if (a.AllStock) continue;
                double wantStock = Sum(a.WantParts), giveStock = Sum(a.GiveParts);
                double share = Math.Min(Deliverable(p, a.Want, s.WantEscrow[k], wantStock, a.WantMade), Deliverable(r, a.Give, s.GiveEscrow[k], giveStock, a.GiveMade));
                share = Math.Min(share * RoomFraction(r, a.Want, share * (wantStock + a.WantMade)), share * RoomFraction(p, a.Give, share * (giveStock + a.GiveMade)));
                Deliver(p, r, a.Want, s.WantEscrow[k], wantStock, a.WantMade, share);
                Deliver(r, p, a.Give, s.GiveEscrow[k], giveStock, a.GiveMade, share);
                s.WantEscrow[k] = s.GiveEscrow[k] = null; s.WantScale[k] = s.GiveScale[k] = share;
            }
            var after = Enumerable.Range(0, parts.Length).Select(Total).ToArray();
            for (int i = 0; i < parts.Length; i++)
                if (Math.Abs(after[i] - before[i]) > 1e-6 * Math.Max(1, Math.Abs(before[i])))
                    throw new InvalidOperationException("Trade settlement changed the world total of " + parts[i].Name + ".");
            if (w.Camps.Any(c => parts.Any(p => p.Get(c) < -1e-9)))
                throw new InvalidOperationException("Trade settlement left a negative stock.");
        }

        // Opening settlement (before dispatch, so provisions and the food ledger see the stock the planner saw):
        // stock parts leave their giver into escrow and all-stock agreements settle. Returns the turn's settlement.
        internal static TradeSettlement Settle(WorldState w, TradePlan plan, TurnRecord record)
        {
            if (plan == null || plan.Agreements.Count == 0 && plan.Summary.Eligible == 0) return null;
            record.TradeActivity = plan.Summary;
            var s = new TradeSettlement(plan);
            if (plan.Agreements.Count > 0) SettleLegs(w, s, false);
            return s;
        }

        // After production and before needs: agreements with a made part settle (a maker that made less delivers in
        // proportion, both legs scale together, and the receiver's needs see what arrived); the rest of escrow is refunded.
        internal static void SettleAfterProduction(WorldState w, TradeSettlement s, TurnRecord record = null)
        {
            if (s == null || s.Plan.Agreements.Count == 0) return;
            var camps = w.Camps.ToDictionary(c => c.Id); var food = w.Camps.ToDictionary(c => c.Id, c => c.Food);
            SettleLegs(w, s, true);
            // The food that moved now (payments and refunds) is reported, so each camp's food ledger still balances.
            if (record != null)
                foreach (var d in record.Demographics)
                    if (food.TryGetValue(d.Camp, out double before)) d.TradeFood += camps[d.Camp].Food - before;
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
                double wantQuantity = a.WantQuantity * want;
                double giveQuantity = a.GiveQuantity * give;
                double requesterUtility = a.Utility?.Requester(wantQuantity,
                    giveQuantity) ?? 0;
                double responderUtility = a.Utility?.Responder(wantQuantity,
                    giveQuantity) ?? 0;
                record.Trades.Add(new TradeRecord { Turn = w.Turn, Requester = r.Id, Responder = p.Id, RequesterSociety = r.SocietyId, ResponderSociety = p.SocietyId,
                    Distance = a.Distance, Want = a.Want, Give = a.Give, WantQuantity = wantQuantity, GiveQuantity = giveQuantity,
                    Ratio = a.Ratio, Affinity = a.Affinity, Countered = a.Countered, RequesterGain = a.RequesterGain, ResponderGain = a.ResponderGain, Delivered = want, GiveDelivered = give,
                    WantMadeToOrder = a.WantMade > 1e-12, GiveMadeToOrder = a.GiveMade > 1e-12,
                    RequesterRealizedUtility = requesterUtility,
                    ResponderRealizedUtility = responderUtility });
                ContactKnowledge.RecordTrade(w, r.People, p);
                ContactKnowledge.RecordTrade(w, p.People, r);
                // Trade is bilateral contact, not one-way imitation of a
                // commodity tag. Each receiver can learn opening technologies
                // held by the counterparty, scaled by the realized value of the
                // leg it actually received.
                TechnologyDiscovery.RecordTradeContactPractice(w, r.People, p.People,
                    Math.Max(0, a.RequesterGain * want));
                TechnologyDiscovery.RecordTradeContactPractice(w, p.People, r.People,
                    Math.Max(0, a.ResponderGain * give));
            }
        }
    }
}
