using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public static class OrderRules
    {
        // Human controls, policies and action masks all consume this filtered contract.
        public static string Validate(AgentObservation o, Order order)
        {
            if (order == null) return "Missing order.";
            var settlement = o.Settlements.FirstOrDefault(c => c.Id == order.Entity);
            var hunter = o.Hunters.FirstOrDefault(h => h.Id == order.Entity);
            if (settlement == null && hunter == null) return "Entity is not controlled by this tribe.";
            var source = settlement ?? o.Settlements.FirstOrDefault(c => c.Id == hunter.SourceCamp);
            if (source == null || source.LinkedPopulation == 0) return "The tribe has no surviving people.";
            int tile = settlement != null ? settlement.Tile : hunter.Tile;
            if (order.Kind == OrderKind.Economy) return order.Target >= 0 || order.Destination >= 0 || order.Population != 0 ? "Economic plans cannot bundle movement or dispatch." : EconomicIntents.Validate(o, settlement, order.Economy);
            if (order.Economy != null) return "Economic payload requires an economic order.";
            if (order.Kind == OrderKind.FissionPolicy)
                return settlement == null || order.Target < 0 || order.Target > (int)FissionPolicy.Encourage || order.Destination >= 0 || order.Population != 0 ? "A fission policy must name Neutral, Discourage or Encourage for a settlement." : null;
            if (order.Kind == OrderKind.NoOp) return order.Destination >= 0 || order.Target >= 0 || order.Population != 0 ? "Wait cannot include movement or another action." : null;
            if (order.Kind == OrderKind.Dispatch)
                return settlement == null || order.Destination >= 0 || order.Target >= 0 || order.Population <= 0 || order.Population > settlement.Gatherers || settlement.Population - order.Population < o.MinimumCamp ? "Dispatch must use available gatherers and preserve the camp's minimum population." : null;
            if (order.Kind == OrderKind.Reintegrate)
                return hunter == null || order.Destination >= 0 || order.Population != 0 || order.Target != source.Id || hunter.Tile != source.Tile ? "Hunters must be at their source camp to reintegrate." : null;
            if (order.Kind == OrderKind.Move || order.Destination >= 0)
            {
                if (!o.Tiles.ContainsKey(order.Destination)) return "Destination is outside current sight.";
                int budget = hunter == null ? o.CampMovement : o.HunterMovement;
                if (!o.Map.Reachable(tile, order.Destination, budget, n => Enter(o, n, hunter != null), n => hunter != null && o.Tiles[n].Aquatic ? o.WaterMovementCost : 1)) return "Destination is unreachable within this turn's movement allowance.";
                if (settlement != null && CampSpacingBlocked(o, settlement.Id, tile, order.Destination)) return "Destination is closer than the minimum camp spacing to another observed camp.";
            }
            if (order.Kind == OrderKind.Move) return order.Population != 0 || order.Target >= 0 ? "Movement cannot include a separate target or dispatch." : null;
            if (hunter == null) return "This order requires a hunter group.";
            if (order.Population != 0) return "This order cannot change the group population.";
            if (order.Kind == OrderKind.Hunt) return o.Herds.Any(h => h.Id == order.Target && !h.Domestic) ? null : "Hunt target is not a currently visible wild herd.";
            if (order.Kind == OrderKind.Fish)
            {
                int final = order.Destination >= 0 ? order.Destination : tile;
                return !o.Tiles.TryGetValue(order.Target, out var target) || !target.Aquatic || o.Map.Distance(final, order.Target) > 1 ? "Fishing requires an accessible adjacent freshwater or shallow tile." : null;
            }
            return "Unknown order.";
        }
        // A camp may always stay put. Any other destination must keep the minimum spacing from every camp
        // this observation can see (own camps and visible foreign camps); unseen camps are enforced only by resolution.
        public static bool CampSpacingBlocked(AgentObservation o, int camp, int origin, int destination)
        {
            if (destination == origin || o.MinimumCampSpacing <= 1) return false;
            foreach (var other in o.Settlements) if (other.Id != camp && o.Map.Distance(destination, other.Tile) < o.MinimumCampSpacing) return true;
            foreach (var rival in o.Rivals) if (o.Map.Distance(destination, rival.Tile) < o.MinimumCampSpacing) return true;
            return false;
        }
        private static bool Enter(AgentObservation o, int n, bool hunter) => o.Tiles.TryGetValue(n, out var t) && t.ElevationLevel < 10 && (t.Land || hunter && o.Watercraft && t.Terrain == Terrain.Shallows);
    }
    [Serializable]
    public sealed class EntityActionMask
    {
        public int Entity, MinimumDispatch, MaximumDispatch;
        public EconomicConstraints Economy;
        public List<Order> Actions = new List<Order>();
        public bool Allows(Order order) => order != null && order.Entity == Entity && (order.Kind == OrderKind.Economy ? order.Target < 0 && order.Destination < 0 && order.Population == 0 && EconomicIntents.Validate(Economy, order.Economy) == null : order.Economy == null && (order.Kind == OrderKind.Dispatch ? order.Target < 0 && order.Destination < 0 && order.Population >= MinimumDispatch && order.Population <= MaximumDispatch && MinimumDispatch > 0 : Actions.Any(a => a.ToString() == order.ToString())));
    }
    public static class LegalActions
    {
        public static EntityActionMask[] Build(AgentObservation o)
        {
            var result = new List<EntityActionMask>();
            foreach (int entity in o.Settlements.Select(c => c.Id).Concat(o.Hunters.Select(h => h.Id)).OrderBy(n => n))
            {
                var mask = new EntityActionMask { Entity = entity };
                void Add(Order order) { if (OrderRules.Validate(o, order) == null) mask.Actions.Add(order); }
                Add(new Order(OrderKind.NoOp, entity));
                var camp = o.Settlements.FirstOrDefault(c => c.Id == entity); var hunter = o.Hunters.FirstOrDefault(h => h.Id == entity);
                int origin = camp != null ? camp.Tile : hunter.Tile;
                var destinations = o.Map.Radius(origin, camp == null ? o.HunterMovement : o.CampMovement).OrderBy(n => n).ToArray();
                foreach (int n in destinations) Add(new Order(OrderKind.Move, entity, destination: n));
                if (camp != null)
                {
                    if (camp.Agriculture || camp.Husbandry || camp.FishingEnabled) { mask.Economy = EconomicIntents.Constraints(o, camp); Add(new Order(OrderKind.Economy, entity) { Economy = camp.Economy.Copy() }); }
                    foreach (FissionPolicy policy in Enum.GetValues(typeof(FissionPolicy))) Add(new Order(OrderKind.FissionPolicy, entity, target: (int)policy));
                    int max = Math.Min(camp.Gatherers, camp.Population - o.MinimumCamp);
                    if (OrderRules.Validate(o, new Order(OrderKind.Dispatch, entity, population: max)) == null) { mask.MinimumDispatch = 1; mask.MaximumDispatch = max; }
                }
                else
                {
                    Add(new Order(OrderKind.Reintegrate, entity, hunter.SourceCamp));
                    foreach (int destination in new[] { -1 }.Concat(destinations))
                    {
                        foreach (var herd in o.Herds.Where(h => !h.Domestic)) Add(new Order(OrderKind.Hunt, entity, herd.Id, destination));
                        foreach (int fish in o.Map.Radius(destination < 0 ? origin : destination, 1)) Add(new Order(OrderKind.Fish, entity, fish, destination));
                    }
                }
                mask.Actions = mask.Actions.OrderBy(a => a.ToString(), StringComparer.Ordinal).ToList(); result.Add(mask);
            }
            return result.ToArray();
        }
    }
}
