using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;
using UnityEngine;
using Terrain = Anthropocene.Simulation.Terrain;

namespace Anthropocene.Presentation
{
    /// <summary>Small, deterministic path helper used only to explain already-valid player orders.</summary>
    public static class PlanningPath
    {
        public static IReadOnlyList<int> Find(HexMap map, int start, int destination, int budget, Func<int, bool> enter, Func<int, int> cost = null)
        {
            if (map == null || enter == null || start < 0 || destination < 0 || !enter(start) || !enter(destination)) return Array.Empty<int>();
            var remaining = new Dictionary<int, int> { [start] = budget };
            var previous = new Dictionary<int, int>();
            var open = new List<int> { start };
            while (open.Count > 0)
            {
                int current = open.OrderByDescending(n => remaining[n]).ThenBy(n => n).First();
                open.Remove(current);
                if (current == destination)
                {
                    var path = new List<int> { current };
                    while (previous.TryGetValue(current, out int parent)) { path.Add(parent); current = parent; }
                    path.Reverse();
                    return path;
                }
                foreach (int next in map.Neighbors(current).OrderBy(n => n))
                {
                    if (!enter(next)) continue;
                    int left = remaining[current] - (cost == null ? 1 : Math.Max(1, cost(next)));
                    if (left < 0 || remaining.TryGetValue(next, out int old) && old >= left) continue;
                    remaining[next] = left;
                    previous[next] = current;
                    if (!open.Contains(next)) open.Add(next);
                }
            }
            return Array.Empty<int>();
        }

        public static string QueueMessage(string previous, string next)
        {
            return string.IsNullOrEmpty(previous) ? "Queued " + next + "." : "Replaced " + previous + " with " + next + ".";
        }
    }

    public sealed partial class OpeningGame
    {
        private string resolvingSummary = "";
        private string lastResolutionSummary = "";

        private bool SelectedIsHunter => observation != null && observation.Hunters.Any(h => h.Id == selected);
        private int SelectedTile
        {
            get
            {
                if (observation == null) return -1;
                if (selected == observation.CampId) return observation.CampTile;
                var hunter = observation.Hunters.FirstOrDefault(h => h.Id == selected);
                return hunter == null ? -1 : hunter.Tile;
            }
        }
        private int SelectedMovement => SelectedIsHunter ? observation.HunterMovement : observation.CampMovement;
        private string SelectedName => selected == observation.CampId ? "Camp" : "Hunters " + selected;

        private bool CanEnterObserved(int tile)
        {
            if (!observation.Tiles.TryGetValue(tile, out var seen) || seen.ElevationLevel >= 10) return false;
            return seen.Land || SelectedIsHunter && observation.Watercraft && seen.Terrain == Terrain.Shallows;
        }

        private IReadOnlyList<int> PathTo(int destination)
        {
            int waterCost = Math.Max(1, observation.HunterMovement / Math.Max(1, world.Config.WaterMovement));
            return PlanningPath.Find(observation.Map, SelectedTile, destination, SelectedMovement, CanEnterObserved,
                n => SelectedIsHunter && observation.Tiles[n].Aquatic ? waterCost : 1);
        }

        private bool TryTargetOrder(int tile, out Order candidate, out string error)
        {
            candidate = null;
            error = null;
            if (mode == "Move") candidate = new Order(OrderKind.Move, selected, destination: tile);
            else if (mode == "Hunt")
            {
                var herd = observation.Herds.FirstOrDefault(h => tile == hovered && h.Id == hoveredOccupant && !h.Domestic) ?? observation.Herds.FirstOrDefault(h => h.Tile == tile && !h.Domestic);
                if (!SelectedIsHunter) error = "Select a hunter group before choosing a hunt target.";
                else if (herd == null) error = "No visible wild herd is on this tile.";
                else
                {
                    int destination = planned.TryGetValue(selected, out var prior) && prior.Destination >= 0 ? prior.Destination : tile;
                    candidate = new Order(OrderKind.Hunt, selected, herd.Id, destination);
                }
            }
            else if (mode == "Fish")
            {
                if (!SelectedIsHunter) error = "Select a hunter group before choosing fishing water.";
                else
                {
                    int destination = planned.TryGetValue(selected, out var prior) ? prior.Destination : -1;
                    candidate = new Order(OrderKind.Fish, selected, tile, destination);
                }
            }
            if (candidate != null) error = resolver.Validate(world, 0, candidate, new HashSet<int>(observation.Tiles.Keys));
            return candidate != null && error == null;
        }

        private bool IsValidTarget(int tile)
        {
            return TryTargetOrder(tile, out _, out _);
        }

        private void DrawPlanningTile(Rect rect, int tile)
        {
            bool active = mode == "Move" || mode == "Hunt" || mode == "Fish";
            bool valid = active && IsValidTarget(tile);
            bool pointed = hovered == tile;
            bool queued = planned.Values.Any(o => o.Destination == tile || o.Kind == OrderKind.Fish && o.Target == tile || o.Kind == OrderKind.Hunt && observation.Herds.Any(h => h.Id == o.Target && h.Tile == tile));
            if (valid)
            {
                Color color = mode == "Move" ? MoveColor : mode == "Hunt" ? HuntColor : FishColor;
                color.a = pointed ? .30f : .13f;
                Tint(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), hex, color);
            }
            else if (active && pointed)
            {
                var invalid = InvalidColor; invalid.a = .26f;
                Tint(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), hex, invalid);
            }
            if (queued)
            {
                var queuedColor = Gold; queuedColor.a = .22f;
                Tint(new Rect(rect.x + 7, rect.y + 7, rect.width - 14, rect.height - 14), hex, queuedColor);
                GUI.Label(new Rect(rect.x + rect.width - 31, rect.y + 7, 24, 22), "Q", badge);
            }
        }

        private void DrawPaths(Rect area)
        {
            foreach (var order in planned.Values.OrderBy(o => o.Entity))
            {
                int origin = order.Entity == observation.CampId ? observation.CampTile : observation.Hunters.FirstOrDefault(h => h.Id == order.Entity)?.Tile ?? -1;
                if (origin < 0 || order.Destination < 0) continue;
                bool hunter = order.Entity != observation.CampId;
                int budget = hunter ? observation.HunterMovement : observation.CampMovement;
                bool Enter(int n)
                {
                    if (!observation.Tiles.TryGetValue(n, out var seen) || seen.ElevationLevel >= 10) return false;
                    return seen.Land || hunter && observation.Watercraft && seen.Terrain == Terrain.Shallows;
                }
                int waterCost = Math.Max(1, observation.HunterMovement / Math.Max(1, world.Config.WaterMovement));
                var path = PlanningPath.Find(observation.Map, origin, order.Destination, budget, Enter, n => hunter && observation.Tiles[n].Aquatic ? waterCost : 1);
                DrawPath(path, area, Gold, 3);
            }
            if (hovered >= 0 && (mode == "Move" || mode == "Hunt" || mode == "Fish") && TryTargetOrder(hovered, out var preview, out _))
            {
                var path = preview.Destination >= 0 ? PathTo(preview.Destination) : Array.Empty<int>();
                DrawPath(path, area, mode == "Hunt" ? HuntColor : mode == "Fish" ? FishColor : MoveColor, 5);
            }
        }

        private void DrawPath(IReadOnlyList<int> path, Rect area, Color color, float width)
        {
            for (int i = 1; i < path.Count; i++) MapLine(Position(path[i - 1], area), Position(path[i], area), width, color);
            for (int i = 0; i < path.Count; i++)
            {
                var p = Position(path[i], area);
                Tint(new Rect(p.x - 4, p.y - 4, 8, 8), Texture2D.whiteTexture, color);
            }
        }

        private string ModeInstruction()
        {
            if (mode == "Move") return "MOVE MODE  ·  Green tiles are reachable this generation  ·  click to queue  ·  Esc cancels mode";
            if (mode == "Hunt") return "HUNT MODE  ·  Amber tiles contain a valid visible herd  ·  click to queue  ·  Esc cancels mode";
            if (mode == "Fish") return "FISH MODE  ·  Blue water is fishable from the group's final tile  ·  click to queue  ·  Esc cancels mode";
            return "INSPECT MODE  ·  click a unit slot to select  ·  right-click a tile for movement";
        }

        private string CommitPreview()
        {
            if (planned.Count == 0) return "No explicit orders. The camp will gather locally and every group will hold position.";
            string orders = string.Join("  ·  ", planned.Values.OrderBy(o => o.Entity).Select(o => UnitName(o.Entity) + ": " + Describe(o)));
            return planned.Count + " queued: " + orders + " Automatic camp gathering still occurs.";
        }

        private string UnitName(int id) => id == observation.CampId ? "Camp" : "Hunters " + id;

        private void SelectUnit(int id, int tile)
        {
            selected = id;
            center = tile;
            pan = Vector2.zero;
            BeginMode("Inspect");
            status = UnitName(id) + " selected. Choose a command or inspect the visible land.";
        }

        private void BeginMode(string next)
        {
            if ((next == "Hunt" || next == "Fish") && !SelectedIsHunter)
            {
                status = "Select a hunter group to use " + next.ToLowerInvariant() + ".";
                return;
            }
            mode = next;
            if (next == "Inspect") return;
            status = ModeInstruction();
        }

        private void CancelSelectedOrder()
        {
            if (!planned.Remove(selected)) return;
            status = UnitName(selected) + " will now hold position; its queued order was cancelled.";
            mode = "Inspect";
        }

        private void ResolutionScreen()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), Ink);
            float width = Mathf.Min(760, Screen.width - 80), left = (Screen.width - width) / 2;
            GUI.Label(new Rect(left, Screen.height * .24f, width, 48), "RESOLVING THE GENERATION", title);
            GUI.Label(new Rect(left, Screen.height * .24f + 58, width, 70), "All tribes committed simultaneously. Seasonal gathering, hunting, fishing, consumption, ecology and demographics are now advancing.", text);
            Fill(new Rect(left, Screen.height * .24f + 145, width, 2), Gold);
            GUI.Label(new Rect(left, Screen.height * .24f + 170, width, 120), resolvingSummary, text);
            GUI.Label(new Rect(left, Screen.height * .24f + 300, width, 30), "Input is paused until the committed world is ready.", small);
        }

        private static void MapLine(Vector2 start, Vector2 end, float width, Color color)
        {
            var matrix = GUI.matrix; var previous = GUI.color; GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width / 2, Vector2.Distance(start, end) + .5f, width), Texture2D.whiteTexture);
            GUI.matrix = matrix; GUI.color = previous;
        }
    }
}
