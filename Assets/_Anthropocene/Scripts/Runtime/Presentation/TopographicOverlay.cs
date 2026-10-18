using System;
using System.Collections.Generic;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public enum PlayerMapLayer { Terrain, Topography, Ruggedness }

    public sealed class MapLayerState
    {
        public PlayerMapLayer Layer { get; private set; } = PlayerMapLayer.Terrain;
        public ContourDensity Density { get; private set; } = ContourDensity.Normal;
        public bool RuggednessContours { get; private set; } = true;
        public bool ShowsContinuousContours => Layer == PlayerMapLayer.Topography || Layer == PlayerMapLayer.Ruggedness && RuggednessContours;

        public void Select(PlayerMapLayer layer) => Layer = layer;
        public void ToggleDensity() => Density = Density == ContourDensity.Normal ? ContourDensity.Fine : ContourDensity.Normal;
        public void ToggleRuggednessContours() => RuggednessContours = !RuggednessContours;
    }

    public static class TopographicOverlay
    {
        public static Color RuggednessColor(float score)
        {
            float value = Mathf.Clamp(score, 0, 100);
            Color smooth = ColorOf("#326A73"), rolling = ColorOf("#7F9F68"), broken = ColorOf("#C3A953"), rough = ColorOf("#B56343"), severe = ColorOf("#67364F");
            if (value <= 25) return Color.Lerp(smooth, rolling, value / 25);
            if (value <= 50) return Color.Lerp(rolling, broken, (value - 25) / 25);
            if (value <= 75) return Color.Lerp(broken, rough, (value - 50) / 25);
            return Color.Lerp(rough, severe, (value - 75) / 25);
        }

        // Signed overlay strength: positive lightens, negative darkens. The local plane
        // gradient is measured in normalized elevation per centre-to-centre hex spacing.
        public static float Hillshade(KnownTerrainMetric metric)
        {
            var normal = new Vector3(-metric.GradientX * 9, -metric.GradientY * 9, 1).normalized;
            var light = new Vector3(-.55f, -.72f, .68f).normalized;
            float illumination = Vector3.Dot(normal, light);
            return Mathf.Clamp((illumination - .66f) * .22f, -.11f, .09f);
        }

        public static float ContourWidth(bool major, float radius) => major
            ? Mathf.Clamp(radius * .030f, 1.25f, 2.8f)
            : Mathf.Clamp(radius * .015f, .65f, 1.35f);

        public static Color ContourColor(bool major, PlayerMapLayer layer)
        {
            Color color = layer == PlayerMapLayer.Ruggedness ? ColorOf("#17272A") : ColorOf("#31362F");
            color.a = major ? .82f : .48f;
            return color;
        }

        public static void DrawContours(IReadOnlyList<ContourSegment> segments, Func<ContourPoint, Vector2> position, float radius, PlayerMapLayer layer)
        {
            if (segments == null || position == null) return;
            foreach (var segment in segments)
            {
                Vector2 start = position(segment.Start), end = position(segment.End);
                float width = ContourWidth(segment.Major, radius);
                if (segment.Major) LayeredTileRenderer.DrawLine(start, end, width + 1.4f, new Color(.88f, .86f, .72f, .32f));
                LayeredTileRenderer.DrawLine(start, end, width, ContourColor(segment.Major, layer));
            }
        }

        public static void DrawContour(ContourSegment segment, Vector2 start, Vector2 end, float radius, PlayerMapLayer layer)
        {
            float width = ContourWidth(segment.Major, radius);
            if (segment.Major) LayeredTileRenderer.DrawLine(start, end, width + 1.4f, new Color(.88f, .86f, .72f, .32f));
            LayeredTileRenderer.DrawLine(start, end, width, ContourColor(segment.Major, layer));
        }

        private static Color ColorOf(string html) => ColorUtility.TryParseHtmlString(html, out var color) ? color : Color.white;
    }
}
