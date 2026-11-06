using System;
using System.Collections.Generic;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public sealed partial class OpeningGame
    {
        private ContourDensity atlasContourDensity = ContourDensity.Normal;
        private bool atlasRuggednessContours = true;
        private Texture2D atlasContourTexture;
        private Rect atlasContourMap;
        private Vector2 atlasContourSize;
        private int atlasContourStep = -1;
        private bool atlasContourRugged;
        private double atlasContourMilliseconds;

        private void InvalidateAtlasContours()
        {
            if (atlasContourTexture != null) Destroy(atlasContourTexture);
            atlasContourTexture = null;
        }

        private void DrawAtlasTopographyControls()
        {
            if (Button(new Rect(20, 100, 110, 30), "Fit world")) { previewZoom = 1; previewPan = Vector2.zero; }
            if (Button(new Rect(138, 100, 150, 30), "Find my camp"))
            { previewTile = world.Camps.Find(c => c.Player == 0).Tile; previewZoom = 12; previewPan = Vector2.zero; }
            if (Button(new Rect(296, 100, 190, 30), "Density: " + atlasContourDensity))
                atlasContourDensity = atlasContourDensity == ContourDensity.Normal ? ContourDensity.Fine : ContourDensity.Normal;
            if (previewLayer == 7 && Button(new Rect(494, 100, 175, 30), "Contours: " + (atlasRuggednessContours ? "On" : "Off")))
                atlasRuggednessContours = !atlasRuggednessContours;
        }

        // A derived viewport raster avoids thousands of IMGUI calls at world scale.
        // Rebuild only on camera/LOD/layer changes, reading just intersecting tiles plus
        // a one-cell margin. At fit-world this is O(N log N) contour construction;
        // upload/memory are bounded by screen pixels. No player observation is expanded.
        private void DrawAtlasContours(Rect map, Vector2 size)
        {
            float cellPixels = map.width / world.Config.Width;
            int step = TopographicContours.StepUnits(atlasContourDensity, cellPixels);
            bool rugged = previewLayer == 7;
            if (atlasContourTexture == null || map != atlasContourMap || size != atlasContourSize || step != atlasContourStep || rugged != atlasContourRugged)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                InvalidateAtlasContours();
                var heights = new Dictionary<int, float>();
                int q0 = Mathf.Max(0, Mathf.FloorToInt(-map.x / cellPixels) - 1);
                int q1 = Mathf.Min(world.Config.Width - 1, Mathf.CeilToInt((size.x - map.x) / cellPixels) + 1);
                int r0 = Mathf.Max(0, Mathf.FloorToInt(-map.y / cellPixels) - 1);
                int r1 = Mathf.Min(world.Config.Height - 1, Mathf.CeilToInt((size.y - map.y) / cellPixels) + 1);
                for (int r = r0; r <= r1; r++)
                    for (int q = q0; q <= q1; q++)
                    {
                        var tile = world.Tiles[r * world.Config.Width + q];
                        if (tile.Land) heights[tile.Id] = tile.Elevation;
                    }
                var segments = TopographicContours.Build(world.Map, heights, null, atlasContourDensity, cellPixels);
                int width = Mathf.CeilToInt(size.x), height = Mathf.CeilToInt(size.y);
                var pixels = new Color32[width * height];
                var layer = rugged ? PlayerMapLayer.Ruggedness : PlayerMapLayer.Topography;
                for (int pass = 0; pass < 2; pass++)
                    foreach (var segment in segments)
                    {
                        if (segment.Major != (pass == 1)) continue;
                        Vector2 a = AtlasContourPoint(segment.Start, map), b = AtlasContourPoint(segment.End, map);
                        if (b.x - a.x > map.width / 2) b.x -= map.width;
                        if (a.x - b.x > map.width / 2) b.x += map.width;
                        Color color = TopographicOverlay.ContourColor(segment.Major, layer);
                        float lineWidth = TopographicOverlay.ContourWidth(segment.Major, cellPixels);
                        RasterAtlasLine(pixels, width, height, a, b, lineWidth, color);
                        if (a.x < map.x || b.x < map.x)
                            RasterAtlasLine(pixels, width, height, a + Vector2.right * map.width, b + Vector2.right * map.width, lineWidth, color);
                    }
                atlasContourTexture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                atlasContourTexture.SetPixels32(pixels); atlasContourTexture.Apply(false, true);
                atlasContourMap = map; atlasContourSize = size; atlasContourStep = step; atlasContourRugged = rugged;
                atlasContourMilliseconds = timer.Elapsed.TotalMilliseconds;
            }
            GUI.DrawTexture(new Rect(Vector2.zero, size), atlasContourTexture);
        }

        private Vector2 AtlasContourPoint(ContourPoint point, Rect map)
        {
            Vector2 a = PreviewPoint(point.TileA, map), b = PreviewPoint(point.TileB, map);
            if (b.x - a.x > map.width / 2) b.x -= map.width;
            if (a.x - b.x > map.width / 2) b.x += map.width;
            return Vector2.LerpUnclamped(a, b, point.T);
        }

        private static void RasterAtlasLine(Color32[] pixels, int width, int height, Vector2 a, Vector2 b, float lineWidth, Color color)
        {
            float reach = lineWidth / 2 + .5f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - reach));
            int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + reach));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - reach));
            int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + reach));
            Vector2 ab = b - a; float lengthSquared = ab.sqrMagnitude;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(x + .5f, y + .5f);
                    float t = lengthSquared > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared) : 0;
                    float alpha = color.a * Mathf.Clamp01(reach - Vector2.Distance(p, a + t * ab));
                    int index = (height - 1 - y) * width + x;
                    if (alpha <= pixels[index].a / 255f) continue;
                    Color value = color; value.a = alpha; pixels[index] = value;
                }
        }
    }
}
