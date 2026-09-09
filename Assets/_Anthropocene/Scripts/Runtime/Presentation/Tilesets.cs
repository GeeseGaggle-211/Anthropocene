using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    [Serializable]
    public sealed class VisualDefinition
    {
        public string Key, Glyph, Tint = "#D4D6BF", Background = "#273722", SpritePath;
        public float Scale = .8f;
        public float Opacity = .38f, LineWidth = 1, Spacing = 2.8f;
    }
    [Serializable]
    public sealed class TilesetDefinition
    {
        public int Version = 1;
        public string Id, Name;
        public VisualDefinition[] Definitions;
        public VisualDefinition Find(string key) => Definitions.FirstOrDefault(d => d.Key == key) ?? Definitions.First(d => d.Key == "fallback");
    }
    public static class TilesetCatalog
    {
        public static TilesetDefinition[] Load()
        {
            var definitions = Resources.LoadAll<TextAsset>("Tilesets").Select(asset => JsonUtility.FromJson<TilesetDefinition>(asset.text)).OrderBy(t => t.Id, StringComparer.Ordinal).ToArray();
            if (definitions.Length == 0 || definitions.Select(t => t.Id).Distinct().Count() != definitions.Length || definitions.Any(t => string.IsNullOrWhiteSpace(t.Id) || t.Version != 1 || t.Definitions == null || !t.Definitions.Any(d => d.Key == "fallback") || t.Definitions.Select(d => d.Key).Distinct().Count() != t.Definitions.Length))
                throw new InvalidOperationException("Tilesets require a unique ID, semantic keys and a fallback visual.");
            return definitions;
        }
    }
    public sealed class TileRenderContext
    {
        public Rect Bounds;
        public TileObservation Tile;
        public bool Inspected, Selected;
        public int SelectedEntity = -1;
        public readonly List<VisibleOccupant> Entities = new List<VisibleOccupant>();
    }
    public interface ITileRenderer { void Draw(TileRenderContext context); }
    public sealed class LayeredTileRenderer : ITileRenderer
    {
        private readonly TilesetDefinition tileset;
        private readonly Texture2D hex;
        private readonly GUIStyle glyph;
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static readonly Vector2[] Corners = { new Vector2(.5f, 0), new Vector2(1, .25f), new Vector2(1, .75f), new Vector2(.5f, 1), new Vector2(0, .75f), new Vector2(0, .25f) };
        public LayeredTileRenderer(TilesetDefinition tileset, Texture2D hex, Font font)
        {
            this.tileset = tileset; this.hex = hex;
            glyph = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            foreach (var definition in tileset.Definitions.Where(d => !string.IsNullOrEmpty(d.SpritePath)))
            {
                var sprite = Resources.Load<Sprite>(definition.SpritePath);
                if (sprite == null) Debug.LogWarning("Tileset " + tileset.Id + " sprite is missing: " + definition.SpritePath + "; using its glyph fallback.");
                sprites[definition.Key] = sprite;
            }
        }
        private static Color ColorOf(string text) => ColorUtility.TryParseHtmlString(text, out var color) ? color : Color.white;
        public static int ContourCount(int level, float height) => height >= 85 ? level : (int)Math.Ceiling(level / 2.0);
        public static Rect OccupantSlot(Rect rect, int index, int count)
        {
            if (count == 1) return new Rect(rect.x + rect.width * .22f, rect.y + rect.height * .24f, rect.width * .56f, rect.height * .5f);
            int columns = count <= 4 ? 2 : 3, rows = (Math.Min(count, 6) + columns - 1) / columns;
            float width = rect.width * .78f / columns, height = rect.height * .54f / rows;
            return new Rect(rect.x + rect.width * .11f + index % columns * width + 1, rect.y + rect.height * .24f + index / columns * height + 1, width - 2, height - 2);
        }
        private static void Tint(Rect rect, Texture texture, Color color)
        { var previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, texture); GUI.color = previous; }
        private void Symbol(string key, Rect rect, float scale = 1)
        {
            var definition = tileset.Find(key);
            glyph.fontSize = Mathf.RoundToInt(rect.height * definition.Scale * .58f * scale); glyph.normal.textColor = ColorOf(definition.Tint);
            if (sprites.TryGetValue(definition.Key, out var sprite) && sprite != null)
            {
                var uv = sprite.textureRect; uv = new Rect(uv.x / sprite.texture.width, uv.y / sprite.texture.height, uv.width / sprite.texture.width, uv.height / sprite.texture.height);
                float size = rect.height * definition.Scale * scale;
                var target = new Rect(rect.center.x - size / 2, rect.center.y - size / 2, size, size);
                var previous = GUI.color; GUI.color = ColorOf(definition.Tint); GUI.DrawTextureWithTexCoords(target, sprite.texture, uv); GUI.color = previous;
            }
            else if (!string.IsNullOrEmpty(definition.Glyph)) GUI.Label(rect, definition.Glyph, glyph);
        }
        public void Draw(TileRenderContext context)
        {
            var rect = context.Bounds; var tile = context.Tile;
            string biome = tile == null ? "hidden" : "biome." + tile.Terrain.ToString().ToLowerInvariant();
            var ground = tileset.Find(biome);
            string border = context.Selected ? "selected" : context.Inspected ? "friendly" : "border";
            Tint(rect, hex, ColorOf(tileset.Find(border).Tint));
            Tint(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width - 3, rect.height - 3), hex, ColorOf(ground.Background));
            if (tile == null) return;
            // Biome, relief, water features, resources and entities are independent semantic layers.
            var ridge = tileset.Find("elevation.ridge"); var ridgeColor = ColorOf(ridge.Tint); ridgeColor.a = ridge.Opacity;
            Vector2[] ridgeEnds = { new Vector2(1, .5f), new Vector2(.75f, .875f), new Vector2(.25f, .875f), new Vector2(0, .5f), new Vector2(.25f, .125f), new Vector2(.75f, .125f) };
            for (int direction = 0; direction < 6; direction++) if ((tile.RidgeMask & 1 << direction) != 0) Line(rect.center, rect.position + Vector2.Scale(ridgeEnds[direction], rect.size), ridge.LineWidth, ridgeColor);
            var contours = tileset.Find("elevation.contours"); var contourColor = ColorOf(contours.Tint); contourColor.a = contours.Opacity;
            int count = ContourCount(tile.ElevationLevel, rect.height);
            float step = Mathf.Min(contours.Spacing, rect.width * .30f / Mathf.Max(1, count));
            for (int ring = 0; ring < count; ring++)
            {
                float inset = 3 + ring * step; var inner = new Rect(rect.x + inset, rect.y + inset, rect.width - 2 * inset, rect.height - 2 * inset);
                for (int side = 0; side < 6; side++) Line(inner.position + Vector2.Scale(Corners[side], inner.size), inner.position + Vector2.Scale(Corners[(side + 1) % 6], inner.size), contours.LineWidth, contourColor);
            }
            Symbol(biome, context.Entities.Count == 0 ? rect : new Rect(rect.x + rect.width * .05f, rect.y + rect.height * .6f, rect.width * .22f, rect.height * .25f));
            if (tile.ElevationLevel == 10) Symbol("elevation.10", rect);
            if (tile.Oasis) Symbol("feature.oasis", new Rect(rect.x, rect.y + rect.height * .15f, rect.width * .5f, rect.height * .5f));
            else if (tile.FreshwaterSource != FreshwaterSource.None && tile.WaterBody != WaterKind.Saltwater) Symbol("feature.freshwater", new Rect(rect.x + 2, rect.y + rect.height * .13f, rect.width * .36f, rect.height * .36f));
            if (tile.Cultivated) Symbol("feature.cultivation", rect);
            if (tile.Fishery) Symbol("feature.fishery", rect);
            foreach (var river in tile.Rivers)
            {
                Vector2 a = rect.position + Vector2.Scale(Corners[river.Side], rect.size), b = rect.position + Vector2.Scale(Corners[(river.Side + 1) % 6], rect.size);
                Line(a, b, Mathf.Clamp(river.Width * 1.25f, 1.5f, 5), ColorOf(tileset.Find("feature.river").Tint));
                if (river.FlowBand >= 2)
                {
                    Vector2 direction = (river.Forward ? b - a : a - b).normalized, middle = (a + b) / 2;
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    Line(middle + direction * 3, middle - direction * 2 + normal * 2, 1, ColorOf(tileset.Find("feature.freshwater").Tint));
                    Line(middle + direction * 3, middle - direction * 2 - normal * 2, 1, ColorOf(tileset.Find("feature.freshwater").Tint));
                }
            }
            if (rect.height >= 45)
                for (int mark = 0; mark <= tile.Food; mark++) Tint(new Rect(rect.center.x - tile.Food * 2.5f + mark * 5 - 1.5f, rect.y + rect.height * .78f, 3, 3), Texture2D.whiteTexture, ColorOf(tileset.Find("resource.food").Tint));
            for (int i = 0; i < Math.Min(6, context.Entities.Count); i++)
            {
                var slot = OccupantSlot(rect, i, context.Entities.Count);
                // Opaque backplates preserve individual glyphs over dense elevation contours.
                if (context.Entities[i].SelectableId == context.SelectedEntity)
                {
                    Color selected = ColorOf(tileset.Find("selected").Tint);
                    Tint(new Rect(slot.x - 2, slot.y - 2, slot.width + 4, slot.height + 4), Texture2D.whiteTexture, selected);
                }
                Tint(new Rect(slot.x + 1, slot.y + 1, slot.width - 2, slot.height - 2), Texture2D.whiteTexture, ColorOf(ground.Background));
                bool countBadge = !string.IsNullOrEmpty(context.Entities[i].Badge) && slot.width >= 38 && slot.height >= 34;
                Symbol(context.Entities[i].Key, countBadge ? new Rect(slot.x, slot.y, slot.width, slot.height - 14) : slot, context.Entities.Count > 1 ? 1.4f : 1.8f);
                if (countBadge) { glyph.fontSize = 11; glyph.normal.textColor = ColorOf(tileset.Find("entity.stack").Tint); GUI.Label(new Rect(slot.x, slot.yMax - 14, slot.width, 14), context.Entities[i].Badge, glyph); }
            }
            if (context.Entities.Count > 6)
            {
                glyph.fontSize = Mathf.Max(11, Mathf.RoundToInt(rect.height * .13f)); glyph.normal.textColor = ColorOf(tileset.Find("entity.stack").Tint);
                GUI.Label(new Rect(rect.x, rect.y + rect.height * .8f, rect.width, rect.height * .2f), "+" + (context.Entities.Count - 6), glyph);
            }
        }
        private static void Line(Vector2 start, Vector2 end, float width, Color color)
        {
            var matrix = GUI.matrix; var previous = GUI.color; GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width / 2, Vector2.Distance(start, end) + .5f, width), Texture2D.whiteTexture);
            GUI.matrix = matrix; GUI.color = previous;
        }
    }
}
