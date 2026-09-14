using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Anthropocene.Simulation
{
    public sealed class HoverTiming
    {
        public int Candidate = -1, Displayed = -1;
        public double Alpha;
        private double since, lastTime, anchorX, anchorY;
        private string context;
        public void Observe(int tile, double x, double y, double now, string viewContext, double delay, double fadeIn, double fadeOut, double motion)
        {
            double dt = Math.Max(0, now - lastTime); lastTime = now;
            bool reset = tile != Candidate || viewContext != context || (x - anchorX) * (x - anchorX) + (y - anchorY) * (y - anchorY) > motion * motion;
            if (reset) { Candidate = tile; since = now; anchorX = x; anchorY = y; context = viewContext; }
            bool ready = Candidate >= 0 && now - since > delay;
            if (ready && (Displayed == Candidate || Alpha == 0)) { Displayed = Candidate; Alpha = Math.Min(1, Alpha + dt / fadeIn); }
            else Alpha = Math.Max(0, Alpha - dt / fadeOut);
            if (Alpha == 0 && !ready) Displayed = -1;
        }
    }
    public sealed class VisibleOccupant
    {
        public readonly int Id, SelectableId, Rank;
        public readonly string Key, Description, Badge;
        public VisibleOccupant(int id, int selectable, int rank, string key, string description, string badge = "") { Id = id; SelectableId = selectable; Rank = rank; Key = key; Description = description; Badge = badge; }
    }
    public static class TileDetails
    {
        public static VisibleOccupant[] Occupants(AgentObservation o, int tile)
        {
            if (!o.Tiles.ContainsKey(tile)) return new VisibleOccupant[0];
            var occupants = new List<VisibleOccupant>();
            foreach (var c in o.Settlements.Where(c => c.Tile == tile)) occupants.Add(new VisibleOccupant(c.Id, c.Id, 0, "entity.camp", "Your camp: " + c.Population + " people"));
            foreach (var c in o.Rivals.Where(c => c.Tile == tile)) occupants.Add(new VisibleOccupant(c.Id, -1, 0, "entity.rivalcamp", "Tribe " + (c.Player + 1) + " camp; population unknown"));
            foreach (var h in o.Hunters.Where(h => h.Tile == tile)) occupants.Add(new VisibleOccupant(h.Id, h.Id, 1, "entity.hunter", "Hunters " + h.Id + ": " + h.Population + " people; " + (h.InSupply ? "supplied by camp" : "beyond supply; provisions " + Knowledge.Bands[h.Provisions])));
            foreach (var h in o.ForeignHunters.Where(h => h.Tile == tile)) occupants.Add(new VisibleOccupant(h.Id, -1, 1, "entity.rivalhunter", "Tribe " + (h.Player + 1) + " hunters; population/provisions unknown"));
            foreach (var h in o.Herds.Where(h => h.Tile == tile))
            {
                string tracking = !h.Domestic && o.LikelyHerdDestinations.TryGetValue(h.Id, out int likely) ? "; trackers expect tile " + likely : "";
                occupants.Add(new VisibleOccupant(h.Id, -1, h.Domestic ? 3 : 2, h.Domestic ? "entity.domestic" : "species." + h.Species, (h.Domestic ? "Domestic " + h.Name.Replace("Wild ", "") : h.Name) + ": " + h.Count.Label + " animals" + (h.Domestic ? h.Owner == o.Player ? "; your livestock" : "; owner tribe " + (h.Owner + 1) : "") + tracking, h.Count.Compact));
            }
            return occupants.OrderBy(p => p.Rank).ThenBy(p => p.Id).ToArray();
        }
        public static string Describe(AgentObservation o, int id, IEnumerable<Order> queued)
        {
            if (!o.Tiles.TryGetValue(id, out var t)) return "";
            var b = new StringBuilder();
            b.AppendLine((t.Land ? "LAND" : "WATER TILE") + " · (" + id % o.Map.Width + ", " + id / o.Map.Width + ") / tile " + id + " · " + t.Environment);
            if (t.Land) b.AppendLine("VEGETATION · " + Vegetation.Label(t.Environment, t.CurrentVegetation, t.River) + " · " + t.CurrentVegetation + " cover / " + t.NaturalVegetationPotential + " natural potential");
            else b.AppendLine("PLANKTON / ALGAE · " + t.CurrentVegetation + " abundance / " + t.NaturalVegetationPotential + " natural potential");
            b.AppendLine("Elevation " + t.ElevationLevel + "/10 · " + ElevationLevelStage.Description(t.ElevationLevel));
            b.AppendLine("FOOD · " + Knowledge.Bands[t.Food] + " stock; maximum " + Knowledge.Bands[t.FoodMaximum]);
            b.AppendLine((t.Land ? "HERD FORAGE" : "AQUATIC BIOMASS") + " · " + Knowledge.Bands[t.PotentialFood] + " biomass; capacity " + Knowledge.Bands[t.PotentialMaximum] + "; about " + t.GatherablePercent + "% human-gatherable");
            b.AppendLine("Renewal " + Knowledge.Bands[t.Productivity] + " per season within the generation; " + (t.Land ? "land" : "aquatic") + " condition " + Knowledge.Bands[t.Condition] + (t.Land ? "; soil moisture " + Knowledge.Bands[t.Moisture] : ""));
            if (o.RecentOwnExtraction.TryGetValue(id, out double extracted)) b.AppendLine("Your recorded extraction last turn: " + extracted.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            b.AppendLine("WATER · " + (t.WaterBody == WaterKind.Saltwater ? "Saltwater, undrinkable; " + (t.Terrain == Terrain.Shallows ? "coastal shallows" : "open ocean") : t.FreshwaterSource == FreshwaterSource.None ? "No accessible freshwater source" : t.FreshwaterSource + " freshwater: " + Knowledge.Bands[t.Freshwater]));
            foreach (var river in t.Rivers) b.AppendLine("River edge " + river.Side + ": flow " + new[] { "", "small", "medium", "large" }[river.FlowBand] + ", width " + river.Width.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            bool gather = o.Map.Distance(o.CampTile, id) <= 1 && t.Terrain != Terrain.Ocean && t.ElevationLevel < 10;
            b.AppendLine("GATHERING · " + (t.Terrain == Terrain.Shallows ? "Passive marine harvest eligible. " : t.Terrain == Terrain.Ocean ? "Open ocean excluded. " : "Land/lake gathering. ") + (gather ? "In your camp's gathering reach." : "Outside current camp gathering reach."));
            b.AppendLine("FEATURES · " + (t.Oasis ? "Oasis spring (finite). " : "") + (t.Cultivated ? "Cultivated. " : "") + (t.Fishery ? "Fishery. " : "") + (!t.Oasis && !t.Cultivated && !t.Fishery ? "Unimproved" : ""));
            var occupants = Occupants(o, id); b.AppendLine("OCCUPANTS · " + occupants.Length);
            foreach (var occupant in occupants) b.AppendLine(occupant.Description);
            foreach (var order in queued.OrderBy(p => p.Entity))
            {
                int origin = order.Entity == o.CampId ? o.CampTile : o.Hunters.FirstOrDefault(h => h.Id == order.Entity)?.Tile ?? -1;
                bool prey = order.Kind == OrderKind.Hunt && o.Herds.Any(h => h.Id == order.Target && h.Tile == id);
                if (origin == id || order.Destination == id || order.Kind == OrderKind.Fish && order.Target == id || prey) b.AppendLine("QUEUED · " + order.Kind + " by group " + order.Entity + (origin == id ? " from here" : " targeting here"));
            }
            return b.ToString().TrimEnd();
        }
    }
}
