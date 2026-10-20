using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Contract 11 Elder trade posture (Docs/ElderModel9TradeDesign.md): per trade group a relative value (scales the
    // village's use-value curve; its making cost is unchanged) and an offer gate, and the village's export labour
    // share. Per-turn directive fields; no saved state. A native posture reproduces the heuristic's trade exactly.
    public sealed class TradePosture
    {
        public double[] Value = Enumerable.Repeat(1.0, ElderTrade.Groups.Length).ToArray();
        public int[] Gate = Enumerable.Repeat(ElderTrade.Make, ElderTrade.Groups.Length).ToArray();
        public double ExportShare;
    }

    // One trade-group row of the Elder observation (contract 11): the village's own native valuation at the committed
    // boundary and last turn's realised trades. Foreign values, stocks and orders never appear.
    [Serializable]
    public sealed class ElderTradeObservation
    {
        public bool Active, HasMakeCost;
        public double Held, Giveable, MakeCapacity, MakeCost, UseValue, Imported, Exported, RealisedUtility;
    }

    public static class ElderTrade
    {
        public const int RowWidth = 10;

        internal static ElderTradeObservation[] Observe(WorldState w, Camp c, TurnRecord recent)
        {
            var rows = Groups.Select(_ => new ElderTradeObservation()).ToArray();
            if (!Trade.Eligible(w, c)) return rows;
            double production = recent?.Demographics?.FirstOrDefault(d => d.Camp == c.Id)?.FoodProduced ?? 0;
            var use = new double[4];
            foreach (var p in recent?.Production ?? new List<ProductionReceipt>())
                if (p.Camp == c.Id) { use[0] += Math.Max(0, p.Stone); use[1] += Math.Max(0, p.Clay); use[2] += Math.Max(0, p.Wood); use[3] += Math.Max(0, p.HandToolsConsumed); }
            var v = Trade.Value(w, c, Math.Max(0, production), null, use);
            foreach (var g in Trade.Registry)
            {
                int i = (int)g.Id, group = GroupOf(g.Curve);
                if (group < 0 || !v.Active[i]) continue;
                var r = rows[group];
                r.Active = true;
                r.Held += v.Held[i];
                double position = v.Position(g.Id);
                r.Giveable += Math.Max(0, Math.Min(v.Held[i], position - v.Floor(g.Id)));
                r.MakeCapacity += v.MakeCapacity(g.Id);
                if (!double.IsPositiveInfinity(v.Cost[i])) { r.MakeCost = r.HasMakeCost ? Math.Min(r.MakeCost, v.Cost[i]) : v.Cost[i]; r.HasMakeCost = true; }
                r.UseValue = Math.Max(r.UseValue, v.Marginal(g.Id, position));
            }
            foreach (var t in recent?.Trades ?? new List<TradeRecord>())
            {
                int want = GroupOf(Trade.Descriptor(t.Want).Curve), give = GroupOf(Trade.Descriptor(t.Give).Curve);
                if (t.Requester == c.Id)
                {
                    if (want >= 0) { rows[want].Imported += t.WantQuantity; rows[want].RealisedUtility += t.RequesterRealizedUtility; }
                    if (give >= 0) rows[give].Exported += t.GiveQuantity;
                }
                else if (t.Responder == c.Id)
                {
                    if (give >= 0) { rows[give].Imported += t.GiveQuantity; rows[give].RealisedUtility += t.ResponderRealizedUtility; }
                    if (want >= 0) rows[want].Exported += t.WantQuantity;
                }
            }
            return rows;
        }

        // A village's realised trade utility over a resolved turn (person-QoL-years; the trade_utility reward component).
        public static double RealisedUtility(TurnRecord record, int camp)
            => (record?.Trades ?? new List<TradeRecord>()).Sum(t => t.Requester == camp ? t.RequesterRealizedUtility : t.Responder == camp ? t.ResponderRealizedUtility : 0);
        public static readonly string[] Groups = { "clothing", "housing", "tents", "pots", "tools", "materials", "food" };
        public const double MinValue = .25, MaxValue = 4;
        public const int Withhold = 0, Stock = 1, Make = 2;

        // The trade group of a value-curve key (Trade.Registry), -1 for none.
        public static int GroupOf(string curve)
        {
            switch (curve)
            {
                case "Clothing": return 0;
                case "Housing": return 1;
                case "Tents": return 2;
                case "Pots": return 3;
                case "HandTools": case "FarmTools": return 4;
                case "Stone": case "Clay": case "Wood": return 5;
                case "Food": return 6;
                default: return -1;
            }
        }

        // A directive's posture, or null when it is native (absent fields, or an action schema before 11).
        public static TradePosture From(ElderDirective d, SimulationConfig cfg)
        {
            if (d == null || d.SchemaVersion < ElderModel9Contract.TradeSchemaVersion ||
                d.TradeValue == null && d.TradeGate == null && !(d.TradeExportShare >= 0)) return null;
            // Clamped exactly as Repair (Prepare reads the submitted directive; the compiler logs the repair).
            bool valid = (d.TradeValue == null || d.TradeValue.Length == Groups.Length) && (d.TradeGate == null || d.TradeGate.Length == Groups.Length);
            double share = d.TradeExportShare >= 0 && !double.IsInfinity(d.TradeExportShare) ? Math.Min(1, d.TradeExportShare) : cfg.TradeExportLabourShare;
            var p = new TradePosture { ExportShare = share };
            if (valid)
                for (int g = 0; g < Groups.Length; g++)
                {
                    if (d.TradeValue != null) p.Value[g] = ElderRLContract.Finite(d.TradeValue[g]) ? Math.Max(MinValue, Math.Min(MaxValue, d.TradeValue[g])) : 1;
                    if (d.TradeGate != null) p.Gate[g] = d.TradeGate[g] < Withhold || d.TradeGate[g] > Make ? Make : d.TradeGate[g];
                }
            return p;
        }

        // Directed villages' postures for Trade.Plan (camps absent from the map trade natively).
        public static Dictionary<int, TradePosture> Postures(IReadOnlyDictionary<int, ElderDirective> directives, SimulationConfig cfg)
        {
            if (directives == null) return null;
            Dictionary<int, TradePosture> result = null;
            foreach (var pair in directives.OrderBy(p => p.Key))
            {
                var posture = From(pair.Value, cfg);
                if (posture == null) continue;
                (result ??= new Dictionary<int, TradePosture>())[pair.Key] = posture;
            }
            return result;
        }

        // Directive repair (ElderDirectiveValidation): values clamped to [MinValue, MaxValue] (non-finite: 1), gates to
        // 0-2 (out of range: Make), the export share to [0, 1] (non-finite: native); wrong-length vectors become native.
        internal static void Repair(ElderDirective d, List<ElderIntervention> interventions)
        {
            if (d.TradeValue != null && d.TradeValue.Length != Groups.Length || d.TradeGate != null && d.TradeGate.Length != Groups.Length)
            {
                interventions.Add(new ElderIntervention { Kind = ElderInterventionKind.SchemaMismatch, Control = "trade-posture",
                    Reason = "trade posture vector has the wrong length; native posture" });
                d.TradeValue = null; d.TradeGate = null;
            }
            if (d.TradeValue != null)
                for (int g = 0; g < Groups.Length; g++)
                {
                    double v = d.TradeValue[g], ok = ElderRLContract.Finite(v) ? Math.Max(MinValue, Math.Min(MaxValue, v)) : 1;
                    if (ok != v) interventions.Add(new ElderIntervention { Kind = ElderInterventionKind.InvalidActionFallback,
                        Control = "trade-value-" + Groups[g], Reason = "relative value outside [0.25, 4]", Requested = v, Accepted = ok });
                    d.TradeValue[g] = ok;
                }
            if (d.TradeGate != null)
                for (int g = 0; g < Groups.Length; g++)
                    if (d.TradeGate[g] < Withhold || d.TradeGate[g] > Make)
                    {
                        interventions.Add(new ElderIntervention { Kind = ElderInterventionKind.InvalidActionFallback,
                            Control = "trade-gate-" + Groups[g], Reason = "offer gate outside 0-2", Requested = d.TradeGate[g], Accepted = Make });
                        d.TradeGate[g] = Make;
                    }
            if (!double.IsNaN(d.TradeExportShare) && d.TradeExportShare >= 0 && d.TradeExportShare > 1)
            {
                interventions.Add(new ElderIntervention { Kind = ElderInterventionKind.InvalidActionFallback, Control = "trade-export-share",
                    Reason = "export share above 1", Requested = d.TradeExportShare, Accepted = 1 });
                d.TradeExportShare = 1;
            }
            if (double.IsNaN(d.TradeExportShare) || double.IsInfinity(d.TradeExportShare)) d.TradeExportShare = -1;
        }
    }
}
