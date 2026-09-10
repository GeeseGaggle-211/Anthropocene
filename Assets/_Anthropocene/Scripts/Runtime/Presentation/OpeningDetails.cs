using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    // Planning explanations consume only the same filtered snapshot available to the player.
    public static class PlanningPreview
    {
        public static int Origin(AgentObservation o, int entity) => entity == o.CampId ? o.CampTile : o.Hunters.FirstOrDefault(h => h.Id == entity)?.Tile ?? -1;
        public static int MovementBudget(AgentObservation o, int entity) => entity == o.CampId ? o.CampMovement : o.HunterMovement;
        public static int Destination(AgentObservation o, int entity, IEnumerable<Order> orders) => orders.FirstOrDefault(p => p.Entity == entity && p.Destination >= 0)?.Destination ?? Origin(o, entity);
        public static string Place(AgentObservation o, int tile) => tile < 0 ? "unknown" : "(" + tile % o.Map.Width + ", " + tile / o.Map.Width + ")";
        public static bool Enter(AgentObservation o, int tile, bool hunter) => o.Tiles.TryGetValue(tile, out var t) && t.ElevationLevel < 10 && (t.Land || hunter && o.Watercraft && t.Terrain == Anthropocene.Simulation.Terrain.Shallows);
        public static int[] Path(AgentObservation o, int entity, int destination, int waterCost = 1)
        {
            int start = Origin(o, entity), budget = MovementBudget(o, entity);
            if (start < 0 || !Enter(o, destination, entity != o.CampId)) return Array.Empty<int>();
            var costs = new Dictionary<int, int> { [start] = 0 }; var previous = new Dictionary<int, int>();
            var queue = new Queue<int>(); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int n in o.Map.Neighbors(current).OrderBy(n => n))
                {
                    if (!Enter(o, n, entity != o.CampId)) continue;
                    int cost = costs[current] + (o.Tiles[n].Aquatic ? Math.Max(1, waterCost) : 1);
                    if (cost > budget || costs.TryGetValue(n, out int old) && old <= cost) continue;
                    costs[n] = cost; previous[n] = current; queue.Enqueue(n);
                }
            }
            if (!costs.ContainsKey(destination)) return Array.Empty<int>();
            var path = new List<int> { destination };
            while (path[path.Count - 1] != start) path.Add(previous[path[path.Count - 1]]);
            path.Reverse(); return path.ToArray();
        }
        public static string MovementError(AgentObservation o, int entity, int destination, int waterCost)
        {
            int origin = Origin(o, entity), budget = MovementBudget(o, entity);
            if (origin < 0) return "The selected group is no longer available.";
            if (!o.Tiles.TryGetValue(destination, out var tile)) return "Destination is outside current sight.";
            if (tile.ElevationLevel >= 10) return "Impassable peak: level 10 blocks movement.";
            if (!Enter(o, destination, entity != o.CampId)) return tile.Terrain == Anthropocene.Simulation.Terrain.Lake ? "Lake entry is unavailable; fish from shore." : "Sea entry is unavailable for this unit; fish from shore.";
            int distance = o.Map.Distance(origin, destination);
            if (distance > budget) return "Beyond movement range: " + distance + " steps away, allowance " + budget + ".";
            if (Path(o, entity, destination, waterCost).Length == 0) return "No traversable route within current sight: intervening terrain or shallow-water movement cost blocks it.";
            return null;
        }
        public static string SupplyState(AgentObservation o, int hunter, IEnumerable<Order> orders)
        {
            int camp = Destination(o, o.CampId, orders), target = Destination(o, hunter, orders);
            if (o.Map.Reachable(camp, target, o.SupplyRange, n => Enter(o, n, true))) return "visible supply path";
            // Unknown terrain can complete a route; never infer its contents from authoritative geography.
            if (o.Map.Reachable(camp, target, o.SupplyRange, n => !o.Tiles.ContainsKey(n) || Enter(o, n, true))) return "uncertain supply: possible route crosses unseen terrain";
            return "outside supply range / blocked supply path";
        }
        public static string SupplyText(AgentObservation o, UnitObservation h, IEnumerable<Order> orders)
        {
            string provisions = Knowledge.Bands[h.Provisions];
            string now = h.InSupply ?
                "Supply path now: connected. Shared camp reserves: " + Knowledge.Bands[o.FoodCondition] + ". Carried provisions: " + provisions + ". Connected groups eat from camp; carried food is deposited on return, so depleted carried stock alone does not mean this group is unfed while the path holds." :
                "Supply path now: disconnected. Carried provisions: " + provisions + "; this finite stock and local harvest must feed the group.";
            string planned = SupplyState(o, h.Id, orders);
            string consequence = planned == "visible supply path" ?
                "The group is expected to share camp food, though connection cannot guarantee that the camp has enough." :
                planned.StartsWith("uncertain", StringComparison.Ordinal) ? "The route cannot be confirmed through unseen terrain; take carried food and harvest risk seriously." :
                "The group will rely on finite carried provisions and harvest and can starve.";
            return now + " Planned after queued moves: " + planned + " to camp " + Place(o, Destination(o, o.CampId, orders)) + ". " + consequence + " Assumes queued moves succeed.";
        }
        public static string Describe(AgentObservation o, Order p) => p.Kind +
            (p.Kind == OrderKind.Dispatch ? " " + p.Population + " people; camp " + o.CampPopulation + " -> " + (o.CampPopulation - p.Population) + " before food/growth; spawn at camp on resolution, orders next turn" :
             p.Kind == OrderKind.Hunt ? " herd " + p.Target : p.Kind == OrderKind.Fish ? " shore " + Place(o, p.Target) : "") +
            (p.Destination >= 0 ? " from " + Place(o, Origin(o, p.Entity)) + " -> " + Place(o, p.Destination) : "");
    }
    public static class HoverPanelLayout
    {
        public static Rect Place(Rect tile, Vector2 size, Rect screen)
        {
            float x = tile.xMax + 14, y = tile.center.y - size.y * .35f;
            if (x + size.x > screen.xMax - 10) x = tile.xMin - size.x - 14;
            if (x < screen.xMin + 10) { x = tile.center.x - size.x / 2; y = tile.yMax + 14; if (y + size.y > screen.yMax - 10) y = tile.yMin - size.y - 14; }
            return new Rect(Mathf.Clamp(x, screen.xMin + 10, Mathf.Max(screen.xMin + 10, screen.xMax - size.x - 10)), Mathf.Clamp(y, screen.yMin + 10, Mathf.Max(screen.yMin + 10, screen.yMax - size.y - 10)), size.x, size.y);
        }
    }
    public sealed partial class OpeningGame
    {
        private void HandleEarlyInput(Event e)
        {
            if (Busy || e.type != EventType.KeyDown) return;
            if (reviewOrders && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { CommitTurn(); e.Use(); return; }
            if (e.keyCode != KeyCode.Escape) return;
            string previousMode = mode;
            bool closingHelp = help, closingReview = reviewOrders;
            mode = "Inspect"; help = false; reviewOrders = false;
            status = closingReview ? "Order review closed. Continue planning; queued orders are unchanged." :
                closingHelp ? "Help closed. Queued orders are unchanged." :
                previousMode != "Inspect" ? "Targeting cancelled. Queued orders are unchanged." : "Inspection mode. Queued orders are unchanged.";
            e.Use();
        }
        private void PlanningLabel(ref float y, float width, string value)
        {
            float height = text.CalcHeight(new GUIContent(value), width) + 10;
            GUI.Label(new Rect(14, y, width, height), value, text); y += height;
        }
        private void DrawOrderReview()
        {
            var r = new Rect(Mathf.Max(12, (Screen.width - 720) / 2), 75, Mathf.Min(720, Screen.width - 24), Mathf.Max(160, Screen.height - 100));
            Fill(r, Panel);
            GUI.Label(new Rect(r.x + 16, r.y + 12, r.width - 32, 32), "REVIEW THIS GENERATION", title);
            string summary = "All tribes resolve together. " + (world.Config.TurnDays / world.Config.DaysPerYear).ToString("0.#") + " years will pass. Destinations and food outcomes are intentions, not guarantees.\n\n";
            foreach (int id in new[] { observation.CampId }.Concat(observation.Hunters.Select(h => h.Id)))
            {
                summary += (id == observation.CampId ? "CAMP " : "HUNTERS ") + id + ": " +
                    (planned.TryGetValue(id, out var p) ? Describe(p) : "No order: stay at " + PlanningPreview.Place(observation, PlanningPreview.Origin(observation, id)) + (id == observation.CampId ? "; automatic gathering." : "; no active harvest.")) + "\n";
                var hunter = observation.Hunters.FirstOrDefault(h => h.Id == id);
                if (hunter != null) summary += PlanningPreview.SupplyText(observation, hunter, planned.Values) + "\n";
                summary += "\n";
            }
            float width = r.width - 54, height = text.CalcHeight(new GUIContent(summary), width);
            reviewScroll = GUI.BeginScrollView(new Rect(r.x + 16, r.y + 52, r.width - 32, r.height - 114), reviewScroll, new Rect(0, 0, width, height));
            GUI.Label(new Rect(0, 0, width, height), summary, text); GUI.EndScrollView();
            if (Button(new Rect(r.x + 16, r.yMax - 48, (r.width - 40) / 2, 34), "Back to planning [Escape]")) reviewOrders = false;
            if (Button(new Rect(r.center.x + 4, r.yMax - 48, (r.width - 40) / 2, 34), "COMMIT GENERATION [Enter]", true)) CommitTurn();
        }
        private void DrawPlannedRoutes(Rect area)
        {
            foreach (var order in planned.Values.Where(p => p.Destination >= 0 && p.Entity != selected).OrderBy(p => p.Entity))
                DrawPlannedRoute(area, order, false);
            if (planned.TryGetValue(selected, out var selectedOrder) && selectedOrder.Destination >= 0)
                DrawPlannedRoute(area, selectedOrder, true);
        }
        private void DrawPlannedRoute(Rect area, Order order, bool active)
        {
            int[] path = PlanningPreview.Path(observation, order.Entity, order.Destination, WaterCost);
            for (int i = 0; i < path.Length; i++)
            {
                var point = Position(path[i], area) + new Vector2(0, radius * .87f);
                if (active || i == path.Length - 1)
                {
                    float badgeWidth = active ? 36 : 48;
                    var badge = new Rect(point.x - badgeWidth / 2, point.y - 8, badgeWidth, 16);
                    Fill(badge, Panel);
                    GUI.Label(badge, active ? i == 0 ? "FROM" : i == path.Length - 1 ? "TO" : i.ToString() : "TO " + order.Entity, small);
                }
                if (i == 0) continue;
                var start = Position(path[i - 1], area) + new Vector2(0, radius * .87f);
                if (Vector2.Distance(start, point) > radius * 2) continue; // Cylindrical seam: no screen-spanning line.
                var matrix = GUI.matrix; var before = GUI.color; GUI.color = active ? Gold : new Color(Paper.r, Paper.g, Paper.b, .42f);
                GUIUtility.RotateAroundPivot(Mathf.Atan2(point.y - start.y, point.x - start.x) * Mathf.Rad2Deg, start);
                GUI.DrawTexture(new Rect(start.x + 16, start.y, Mathf.Max(0, Vector2.Distance(start, point) - 32), active ? 3 : 2), Texture2D.whiteTexture);
                GUI.matrix = matrix; GUI.color = before;
            }
        }
        private readonly HoverTiming hoverTiming = new HoverTiming();
        private int hoveredOccupant = -1;
        private Rect hoveredBounds, detailAnchor;
        private float sidebarHeight = 1800;
        private string hoverText = "";
        private GUIStyle hoverStyle;
        private void SelectOccupant(int tile, int hit)
        {
            var occupants = TileDetails.Occupants(observation, tile).Where(p => p.SelectableId >= 0).ToArray();
            if (occupants.Length == 0) return;
            var exact = occupants.FirstOrDefault(p => p.Id == hit);
            selected = exact != null ? exact.SelectableId : occupants[(Array.FindIndex(occupants, p => p.SelectableId == selected) + 1) % occupants.Length].SelectableId;
        }
        private void DrawStack(int tile, ref float y, float width)
        {
            var occupants = TileDetails.Occupants(observation, tile);
            GUI.Label(new Rect(14, y, width, 24), "TILE OCCUPANTS · " + occupants.Length, small); y += 26;
            foreach (var occupant in occupants)
            {
                float height = Mathf.Max(34, text.CalcHeight(new GUIContent(occupant.Description), width - 20) + 12);
                if (occupant.SelectableId >= 0)
                {
                    if (Button(new Rect(14, y, width, height), (selected == occupant.SelectableId ? "> " : "") + occupant.Description)) { selected = occupant.SelectableId; mode = "Inspect"; }
                }
                else GUI.Label(new Rect(14, y, width, height), occupant.Description, text);
                y += height + 4;
            }
        }
        private void DrawDemographics(ref float y, float width)
        {
            var summary = world.History.LastOrDefault()?.Demographics.FirstOrDefault(d => d.Camp == observation.CampId);
            string value = summary == null ? "GENERATION OUTLOOK\nOne turn spans " + (world.Config.TurnDays / world.Config.DaysPerYear).ToString("0.#") + " years. Growth needs recurring food surplus. Adjacent Shallows contribute passive marine food using the same gathering labor as land." :
                "LAST GENERATION · " + summary.Years.ToString("0.#") + " YEARS\n" + summary.Explanation +
                "\nFood produced / received: " + summary.FoodProduced.ToString("F0") + " / " + summary.FoodReceived.ToString("F0") +
                "\nRequired / eaten: " + summary.FoodRequired.ToString("F0") + " / " + summary.FoodConsumed.ToString("F0") +
                "\nLand gathering: " + summary.TerrestrialFood.ToString("F0") + " · Passive marine: " + summary.MarineFood.ToString("F0") +
                "\nLake gathering: " + summary.LakeFood.ToString("F0") + " · Active fishing: " + summary.FishFood.ToString("F0") + " · Hunting: " + summary.HuntFood.ToString("F0") +
                "\nFractional growth: " + summary.FractionalProgress.ToString("F2");
            float height = text.CalcHeight(new GUIContent(value), width) + 12;
            GUI.Label(new Rect(14, y, width, height), value, text); y += height;
        }
        private void DrawHover(Rect mapRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            Vector2 pointer = Event.current.mousePosition;
            int tile = mapRect.Contains(pointer) ? hovered : -1;
            string context = center + ":" + pan.x + ":" + pan.y + ":" + radius + ":" + mode + ":" + world.Turn + ":" + tilesetIndex;
            var config = world.Config;
            hoverTiming.Observe(tile, pointer.x, pointer.y, Time.unscaledTimeAsDouble, context, config.HoverDelay, config.HoverFadeIn, config.HoverFadeOut, config.HoverMotionPixels);
            if (hoverTiming.Displayed < 0 || hoverTiming.Alpha <= 0 || !observation.Tiles.ContainsKey(hoverTiming.Displayed)) return;
            if (hoverTiming.Displayed == tile) { detailAnchor = hoveredBounds; hoverText = TileDetails.Describe(observation, tile, planned.Values); }
            if (hoverStyle == null) hoverStyle = new GUIStyle(text) { fontSize = 15, richText = false, padding = new RectOffset(0, 0, 0, 0) };
            float width = Mathf.Min(480, Screen.width - 24), availableHeight = Screen.height - 34;
            float height = hoverStyle.CalcHeight(new GUIContent(hoverText), width - 28) + 28;
            if (height > availableHeight) { width = Mathf.Min(760, Screen.width - 24); height = hoverStyle.CalcHeight(new GUIContent(hoverText), width - 28) + 28; }
            // Very large stacks use columns; the panel remains passive and never consumes click or scroll events.
            string left = hoverText, right = null;
            if (height > availableHeight)
            {
                var lines = hoverText.Split('\n'); int half = (lines.Length + 1) / 2;
                left = string.Join("\n", lines.Take(half)); right = string.Join("\n", lines.Skip(half));
                height = Mathf.Max(hoverStyle.CalcHeight(new GUIContent(left), width / 2 - 24), hoverStyle.CalcHeight(new GUIContent(right), width / 2 - 24)) + 28;
            }
            var panel = HoverPanelLayout.Place(detailAnchor, new Vector2(width, Mathf.Min(height, availableHeight)), new Rect(0, 0, Screen.width, Screen.height));
            var previous = GUI.color; GUI.color = new Color(1, 1, 1, (float)hoverTiming.Alpha);
            Fill(panel, new Color(.19f, .23f, .22f, .98f * (float)hoverTiming.Alpha));
            GUI.Label(new Rect(panel.x + 14, panel.y + 14, right == null ? panel.width - 28 : panel.width / 2 - 24, panel.height - 28), left, hoverStyle);
            if (right != null) GUI.Label(new Rect(panel.center.x + 10, panel.y + 14, panel.width / 2 - 24, panel.height - 28), right, hoverStyle);
            GUI.color = previous;
        }
    }
}
