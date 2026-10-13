using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    internal static class AutonomousElderTrade
    {
        internal static TradePlan Plan(WorldState w,
            IReadOnlyDictionary<int, AutonomousElderAction> actions,
            IReadOnlyDictionary<int, AutonomousElderObservation> observations,
            PhaseExecution execution = null)
        {
            var plan = Trade.Plan(w, execution, new HashSet<int>(actions.Keys));
            // Never append to the shared empty singleton.
            if (ReferenceEquals(plan, TradePlan.Empty)) plan = new TradePlan();
            if (!w.Config.TradeEnabled) return plan;
            var camps = w.Camps.ToDictionary(c => c.Id);
            foreach (int id in actions.Keys.OrderBy(x => x))
            {
                var a = actions[id].Quote;
                if (a == null) continue;
                int other = observations[id].CounterpartyIds[a.Counterparty];
                if (other <= id || !actions.TryGetValue(other, out var action)) continue;
                var b = action.Quote;
                if (b == null || observations[other].CounterpartyIds[b.Counterparty] != id || a.Give != b.Want || a.Want != b.Give) continue;
                var r = camps[id]; var p = camps[other];
                if (p.Tile != observations[id].CounterpartyTiles[a.Counterparty] || r.Tile != observations[other].CounterpartyTiles[b.Counterparty]) continue;
                if (r.SocietyId == p.SocietyId || w.Map.Distance(r.Tile, p.Tile) > w.Config.TradeRange) continue;
                // The lower-ID offer's price must meet the other side's minimum.
                if (a.GiveQuantity * b.GiveQuantity < a.WantQuantity * b.WantQuantity) continue;
                double scale = Math.Min(1, Math.Min(b.GiveQuantity / a.WantQuantity, b.WantQuantity / a.GiveQuantity));
                scale = Math.Min(scale, Math.Min(Trade.Held(r, a.Give) / a.GiveQuantity, Trade.Held(p, a.Want) / a.WantQuantity));
                if (!(scale > 1e-12)) continue;
                double[] Parts(Camp c, TradeGood g, double quantity)
                {
                    double held = Trade.Held(c, g);
                    return Trade.Descriptor(g).Parts.Select(part => Math.Max(0, part.Get(c)) / held * quantity).ToArray();
                }
                var agreement = new TradeAgreement { Requester = id, Responder = other,
                    Distance = w.Map.Distance(r.Tile, p.Tile), Give = a.Give, Want = a.Want,
                    GiveQuantity = a.GiveQuantity * scale, WantQuantity = a.WantQuantity * scale,
                    Ratio = a.GiveQuantity / a.WantQuantity };
                agreement.GiveParts = Parts(r, a.Give, agreement.GiveQuantity);
                agreement.WantParts = Parts(p, a.Want, agreement.WantQuantity);
                plan.Agreements.Add(agreement);
                plan.Send(id, a.Give, agreement.GiveParts, 0); plan.Receive(other, a.Give, agreement.GiveParts, true);
                plan.Send(other, a.Want, agreement.WantParts, 0); plan.Receive(id, a.Want, agreement.WantParts, true);
                plan.Summary.Requisitions++; plan.Summary.Honoured++;
            }
            return plan;
        }
    }
}
