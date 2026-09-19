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
        public string Key, Glyph, Tint = "#D4D6BF", Background = "#273722", SpritePath, SpriteTint;
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
        public int ContourEdgeMask = 0x3f;
        public int ShoreEdgeMask;
        public readonly List<VisibleOccupant> Entities = new List<VisibleOccupant>();
    }
    public interface ITileRenderer { void Draw(TileRenderContext context); }
    public sealed class LayeredTileRenderer : ITileRenderer
    {
        private readonly TilesetDefinition tileset;
        private readonly Texture2D hex;
        private readonly GUIStyle glyph;
        private static Texture2D lineBrush;
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static readonly Vector2[] Corners = { new Vector2(.5f, 0), new Vector2(1, .25f), new Vector2(1, .75f), new Vector2(.5f, 1), new Vector2(0, .75f), new Vector2(0, .25f) };
        public LayeredTileRenderer(TilesetDefinition tileset, Texture2D hex, Font font)
        {
            this.tileset = tileset; this.hex = hex;
            if (lineBrush == null)
            {
                lineBrush = new Texture2D(1, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                lineBrush.SetPixels(new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, .25f), Color.white, Color.white, Color.white, Color.white, new Color(1, 1, 1, .25f), new Color(1, 1, 1, 0) });
                lineBrush.Apply(false, true);
            }
            glyph = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            foreach (var definition in tileset.Definitions.Where(d => !string.IsNullOrEmpty(d.SpritePath)))
            {
                var sprite = Resources.Load<Sprite>(definition.SpritePath);
                if (sprite == null) Debug.LogWarning("Tileset " + tileset.Id + " sprite is missing: " + definition.SpritePath + "; using its glyph fallback.");
                sprites[definition.Key] = sprite;
            }
        }
        private static Color ColorOf(string text) => ColorUtility.TryParseHtmlString(text, out var color) ? color : Color.white;
        public static Texture2D CreateHexTexture()
        {
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Antialiased map hex" };
            var pixels = new Color32[256 * 256];
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                float dx = Mathf.Abs((x + .5f) / 128 - 1), dy = Mathf.Abs((y + .5f) / 128 - 1);
                float edge = Mathf.Min(1 - dx, (1 - dy) * 2 - dx);
                pixels[y * 256 + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(edge * 128 + .5f)));
            }
            texture.SetPixels32(pixels); texture.Apply(false, true); return texture;
        }
        public static int ContourCount(int level, float height) => height >= 85 ? level : (int)Math.Ceiling(level / 2.0);
        public static int ContourEdgeMask(int level, IReadOnlyList<int?> neighborLevels)
        {
            if (neighborLevels == null || neighborLevels.Count != 6) throw new ArgumentException("Contour topology requires six clockwise neighbor elevations.", nameof(neighborLevels));
            int mask = 0;
            // Neighbor order is east, southeast, southwest, west, northwest, northeast.
            // Renderer sides begin at northeast, so each direction maps to the following side.
            for (int direction = 0; direction < 6; direction++)
                if (!neighborLevels[direction].HasValue || neighborLevels[direction].Value < level) mask |= 1 << ((direction + 1) % 6);
            return mask;
        }
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
                var target = SpriteBounds(rect, definition.Scale * scale);
                if (target.width < 13 && (key.StartsWith("entity.", StringComparison.Ordinal) || key.StartsWith("species.", StringComparison.Ordinal)))
                {
                    // At this size a detailed illustration becomes an indistinct dot.
                    Badge(rect, definition.Glyph, Mathf.Max(6, Mathf.RoundToInt(Mathf.Min(rect.height, rect.width * 1.4f) * .88f))); return;
                }
                // Coloured art has its own palette; glyph colour must not flatten it.
                var previous = GUI.color; GUI.color = ColorOf(definition.SpriteTint ?? definition.Tint); GUI.DrawTextureWithTexCoords(target, sprite.texture, uv); GUI.color = previous;
            }
            else if (!string.IsNullOrEmpty(definition.Glyph)) GUI.Label(rect, definition.Glyph, glyph);
        }
        public static Rect SpriteBounds(Rect slot, float scale)
        {
            float size = Mathf.Max(0, Mathf.Min(slot.width, slot.height) - 4) * Mathf.Clamp01(scale);
            return new Rect(slot.center.x - size / 2, slot.center.y - size / 2, size, size);
        }
        public bool HasSprite(string key) => sprites.TryGetValue(key, out var sprite) && sprite != null;
        public static string BaseKey(TileObservation tile) => tile == null ? "hidden" : "terrain." +
            (tile.Land && (tile.Relief == Relief.Highland || tile.Relief == Relief.Mountain) ? "highland" : tile.Environment == EnvironmentKind.Ice ? "cold" : tile.Environment.ToString().ToLowerInvariant());
        public static int VegetationCount(TileObservation tile, float height) => !tile.Land || tile.Environment == EnvironmentKind.Ice || height < 42 ? 0 :
            tile.CurrentVegetation == VegetationTier.Medium ? 1 : 3;
        public static string VegetationKey(TileObservation tile) => "vegetation." +
            (tile.CurrentVegetation == VegetationTier.Low ? tile.Environment == EnvironmentKind.Desert ? "shrub" : "grass" :
                tile.Environment == EnvironmentKind.Cold ? "conifer" : tile.Environment == EnvironmentKind.Tropical || tile.Environment == EnvironmentKind.Desert ? "tropical_tree" : "temperate_tree");
        private static readonly Vector2[] VegetationSlots = { new Vector2(.28f, .29f), new Vector2(.72f, .37f), new Vector2(.37f, .65f) };
        private static readonly Vector2[] OccupiedVegetationSlots = { new Vector2(.5f, .16f), new Vector2(.105f, .54f), new Vector2(.895f, .54f) };
        private void VegetationLayer(TileObservation tile, Rect rect, bool occupied)
        {
            int count = VegetationCount(tile, rect.height); string motif = VegetationKey(tile);
            // Low has only ground cover; Medium/High have exactly one/three trees.
            // Deterministic interior slots leave contour/river edges and occupant hit regions intact.
            for (int i = 0; i < count; i++)
            {
                var position = occupied ? OccupiedVegetationSlots[i] : VegetationSlots[i];
                float x = position.x + (occupied ? 0 : ((tile.Id * 17 + i * 7) % 5 - 2) * .012f), y = position.y;
                float size = rect.height * (occupied ? .20f : .34f);
                Symbol(motif, new Rect(rect.x + rect.width * x - size / 2, rect.y + rect.height * y - size / 2, size, size));
            }
        }
        public void Draw(TileRenderContext context)
        {
            var rect = context.Bounds; var tile = context.Tile;
            string biome = BaseKey(tile);
            var ground = tileset.Find(biome);
            string border = context.Selected ? "selected" : context.Inspected ? "friendly" : "border";
            Tint(rect, hex, ColorOf(tileset.Find(border).Tint));
            float rim = context.Selected || context.Inspected ? 1.7f : .65f;
            var interior = new Rect(rect.x + rim, rect.y + rim, rect.width - 2 * rim, rect.height - 2 * rim);
            Tint(interior, hex, ColorOf(ground.Background));
            if (tile == null) return;
            if (HasSprite(biome)) Tint(interior, sprites[biome].texture, tileset.Id == "classic" ? Color.white : new Color(.76f, .81f, .78f));
            // Terrain -> contours -> water -> vegetation -> land use -> settlements -> units -> UI.
            var contours = tileset.Find("elevation.contours"); var contourColor = ColorOf(contours.Tint); contourColor.a = contours.Opacity;
            int count = ContourCount(tile.ElevationLevel, rect.height);
            float step = Mathf.Min(contours.Spacing, rect.width * .30f / Mathf.Max(1, count));
            for (int ring = 0; ring < count; ring++)
            {
                float inset = 3 + ring * step; var inner = new Rect(rect.x + inset, rect.y + inset, rect.width - 2 * inset, rect.height - 2 * inset);
                for (int side = 0; side < 6; side++)
                    if ((context.ContourEdgeMask & 1 << side) != 0) Line(inner.position + Vector2.Scale(Corners[side], inner.size), inner.position + Vector2.Scale(Corners[(side + 1) % 6], inner.size), contours.LineWidth, contourColor);
            }
            if (!HasSprite(biome)) Symbol(biome, new Rect(rect.x + rect.width * .05f, rect.y + rect.height * .6f, rect.width * .22f, rect.height * .25f));
            // Shorelines are derived only from observed neighbours, never guessed through fog.
            for (int side = 0; side < 6; side++) if ((context.ShoreEdgeMask & 1 << side) != 0)
            {
                var shore = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
                var a = shore.position + Vector2.Scale(Corners[side], shore.size); var b = shore.position + Vector2.Scale(Corners[(side + 1) % 6], shore.size);
                Line(a, b, Mathf.Clamp(rect.height * .043f, 1.5f, 6), ColorOf("#DCCC9C"));
                shore = new Rect(rect.x + 6, rect.y + 6, rect.width - 12, rect.height - 12);
                Line(shore.position + Vector2.Scale(Corners[side], shore.size), shore.position + Vector2.Scale(Corners[(side + 1) % 6], shore.size), 1.2f, ColorOf("#9DCCD0"));
            }
            foreach (var river in tile.Rivers)
            {
                Vector2 a = rect.position + Vector2.Scale(Corners[river.Side], rect.size), b = rect.position + Vector2.Scale(Corners[(river.Side + 1) % 6], rect.size);
                float width = Mathf.Clamp(river.Width * 1.25f, 1.5f, 5);
                Line(a, b, width + 2, ColorOf("#C6C58D"));
                Line(a, b, width, ColorOf(tileset.Find("feature.river").Tint));
                if (river.FlowBand >= 2)
                {
                    Vector2 direction = (river.Forward ? b - a : a - b).normalized, middle = (a + b) / 2;
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    Line(middle + direction * 3, middle - direction * 2 + normal * 2, 1, ColorOf(tileset.Find("feature.freshwater").Tint));
                    Line(middle + direction * 3, middle - direction * 2 - normal * 2, 1, ColorOf(tileset.Find("feature.freshwater").Tint));
                }
            }
            VegetationLayer(tile, rect, context.Entities.Count > 0);
            if (rect.height >= 40)
            {
                if (tile.Cultivated)
                {
                    string cultivationKey = tile.Cultivation > 0.60 ? "feature.cultivation_heavy" : "feature.cultivation";
                    float scale = tile.Cultivation <= 0.25 ? 0.7f : 1.0f;
                    Symbol(cultivationKey, new Rect(rect.x + rect.width * .24f, rect.y + rect.height * .5f, rect.width * .5f, rect.height * .38f), scale);
                }
                if (tile.Irrigated)
                {
                    Badge(new Rect(rect.x + rect.width * .14f, rect.y + rect.height * .66f, 16, 16), "≈", 13, "#6FD1E3");
                }
                if (tile.Fishery) Symbol("feature.fishery", new Rect(rect.x + rect.width * .50f, rect.y + rect.height * .08f, rect.width * .4f, rect.height * .4f));
            }
            // A mobile camp is already an occupant; do not paint a second tent beneath it.
            for (int i = 0; i < Math.Min(6, context.Entities.Count); i++)
            {
                var slot = OccupantSlot(rect, i, context.Entities.Count);
                // Negative space belongs to the map. Only the icon silhouette covers it.
                bool countBadge = !string.IsNullOrEmpty(context.Entities[i].Badge) && slot.width >= 38 && slot.height >= 34;
                Symbol(context.Entities[i].Key, countBadge ? new Rect(slot.x, slot.y, slot.width, slot.height - 14) : slot, context.Entities.Count > 1 ? 1.4f : 1.8f);
                if (countBadge) Badge(new Rect(slot.x, slot.yMax - 14, slot.width, 14), context.Entities[i].Badge, 11);
                if (context.SelectedEntity >= 0 && context.Entities[i].SelectableId == context.SelectedEntity) SelectionCorners(slot);
                if (context.Entities[i].Key.StartsWith("entity.rival", StringComparison.Ordinal))
                    Badge(new Rect(slot.xMax - 11, slot.y - 1, 12, 13), "◇", 12);
            }
            // Small resource/water badges sit outside the occupant slots and remain on top.
            if (rect.height >= 45)
                for (int mark = 0; mark <= tile.Food; mark++)
                {
                    var dot = new Rect(rect.center.x - tile.Food * 3 + mark * 6 - 2, rect.y + rect.height * .83f, 4, 4);
                    Tint(dot, Texture2D.whiteTexture, ColorOf("#414532"));
                    Tint(new Rect(dot.x + 1, dot.y + 1, 2, 2), Texture2D.whiteTexture, ColorOf(tileset.Find("resource.food").Tint));
                }
            if (tile.Oasis) Symbol("feature.oasis", new Rect(rect.x + rect.width * .34f, rect.y + rect.height * .04f, rect.width * .34f, rect.height * .25f), 1.5f);
            else if (tile.FreshwaterSource != FreshwaterSource.None && tile.WaterBody != WaterKind.Saltwater) Symbol("feature.freshwater", new Rect(rect.x + rect.width * .38f, rect.y + rect.height * .08f, rect.width * .24f, rect.height * .18f), 1.5f);
            if (tile.ElevationLevel == 10) Badge(new Rect(rect.center.x - 7, rect.y + rect.height * .08f, 14, 15), "×", 16);
            if (context.Entities.Count > 6)
            {
                glyph.fontSize = Mathf.Max(11, Mathf.RoundToInt(rect.height * .13f)); glyph.normal.textColor = ColorOf(tileset.Find("entity.stack").Tint);
                GUI.Label(new Rect(rect.x, rect.y + rect.height * .8f, rect.width, rect.height * .2f), "+" + (context.Entities.Count - 6), glyph);
            }
        }
        private void Badge(Rect rect, string label, int size, string colorHex = null)
        {
            glyph.fontSize = size; glyph.normal.textColor = ColorOf("#29372E");
            GUI.Label(new Rect(rect.x - 1, rect.y, rect.width, rect.height), label, glyph);
            GUI.Label(new Rect(rect.x + 1, rect.y, rect.width, rect.height), label, glyph);
            GUI.Label(new Rect(rect.x, rect.y - 1, rect.width, rect.height), label, glyph);
            GUI.Label(new Rect(rect.x, rect.y + 1, rect.width, rect.height), label, glyph);
            glyph.normal.textColor = ColorOf(colorHex ?? tileset.Find("entity.stack").Tint); GUI.Label(rect, label, glyph);
        }
        private void SelectionCorners(Rect slot)
        {
            float length = Mathf.Min(7, slot.width * .23f), width = tileset.Id == "classic" ? 1.5f : 2.2f;
            foreach (var corner in new[] { new Vector2(slot.x, slot.y), new Vector2(slot.xMax, slot.y), new Vector2(slot.xMax, slot.yMax), new Vector2(slot.x, slot.yMax) })
            {
                var horizontal = corner + new Vector2(corner.x == slot.x ? length : -length, 0);
                var vertical = corner + new Vector2(0, corner.y == slot.y ? length : -length);
                Line(corner, horizontal, width + 2, ColorOf("#3D422E")); Line(corner, vertical, width + 2, ColorOf("#3D422E"));
                Line(corner, horizontal, width, ColorOf(tileset.Find("selected").Tint)); Line(corner, vertical, width, ColorOf(tileset.Find("selected").Tint));
            }
        }
        private static void Line(Vector2 start, Vector2 end, float width, Color color)
        {
            var matrix = GUI.matrix; var previous = GUI.color; GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - (width + 1) / 2, Vector2.Distance(start, end) + .5f, width + 1), lineBrush);
            GUI.matrix = matrix; GUI.color = previous;
        }
    }
}
