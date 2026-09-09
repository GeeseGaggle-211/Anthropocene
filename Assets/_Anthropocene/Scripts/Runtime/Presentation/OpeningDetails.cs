using System;
using System.Linq;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
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
