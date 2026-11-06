using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public sealed partial class OpeningGame
    {
        private const int ContourChunkSize = 8;
        private readonly MapLayerState mapLayerState = new MapLayerState();
        private KnownTerrainAnalysis knownTerrain;
        private AgentObservation contourObservation;
        private int contourStepUnits = -1;
        private IReadOnlyList<ContourSegment> contourSegments = Array.Empty<ContourSegment>();
        private readonly Dictionary<int, List<int>> contourChunks = new Dictionary<int, List<int>>();
        private readonly HashSet<int> screenContourChunks = new HashSet<int>();
        private readonly HashSet<int> screenContourIndices = new HashSet<int>();
        private readonly List<MapRenderEntry> mapRenderEntries = new List<MapRenderEntry>(256);

        private struct MapRenderEntry
        {
            public int Tile;
            public Vector2 Position;
            public Rect Bounds;
            public bool Visible;
            public TileRenderContext Context;
        }

        private void RefreshTerrainPresentation()
        {
            knownTerrain = observation == null ? null : KnownTerrainAnalysis.Build(observation);
            InvalidateContourCache();
        }

        private void InvalidateContourCache()
        {
            contourObservation = null;
            contourStepUnits = -1;
            contourSegments = Array.Empty<ContourSegment>();
            contourChunks.Clear();
        }

        private void SelectMapLayer(PlayerMapLayer layer)
        {
            mapLayerState.Select(layer);
            status = layer == PlayerMapLayer.Terrain ? "Terrain layer: biome colours and gameplay elevation bands."
                : layer == PlayerMapLayer.Topography ? "Topography layer: continuous elevation contours and hillshade."
                : "Ruggedness layer: local plane residual on a fixed 0–100 scale.";
        }

        private void ToggleContourDensity()
        {
            mapLayerState.ToggleDensity();
            InvalidateContourCache();
            status = "Contour density: " + mapLayerState.Density + ". Zooming out still removes fine detail.";
        }

        private void ToggleRuggednessContours()
        {
            mapLayerState.ToggleRuggednessContours();
            status = "Ruggedness contour context: " + (mapLayerState.RuggednessContours ? "on" : "off") + ".";
        }

        private void EnsureContourCache()
        {
            if (!mapLayerState.ShowsContinuousContours || observation == null) return;
            int step = TopographicContours.StepUnits(mapLayerState.Density, radius);
            if (ReferenceEquals(contourObservation, observation) && contourStepUnits == step) return;
            var heights = new Dictionary<int, float>();
            var land = new HashSet<int>();
            foreach (var tile in observation.Tiles.Values)
                if (tile.Land) { heights[tile.Id] = tile.Elevation; land.Add(tile.Id); }
            contourSegments = TopographicContours.Build(observation.Map, heights, land, mapLayerState.Density, radius);
            contourObservation = observation;
            contourStepUnits = step;
            BuildContourChunks();
        }

        private void BuildContourChunks()
        {
            contourChunks.Clear();
            for (int i = 0; i < contourSegments.Count; i++)
            {
                var segment = contourSegments[i];
                AddContourChunk(segment.Start.TileA, i);
                AddContourChunk(segment.Start.TileB, i);
                AddContourChunk(segment.End.TileA, i);
                AddContourChunk(segment.End.TileB, i);
            }
        }

        private int ContourChunk(int tile)
        {
            int columns = (world.Config.Width + ContourChunkSize - 1) / ContourChunkSize;
            return tile / world.Config.Width / ContourChunkSize * columns + tile % world.Config.Width / ContourChunkSize;
        }

        private void AddContourChunk(int tile, int segment)
        {
            int key = ContourChunk(tile);
            if (!contourChunks.TryGetValue(key, out var entries)) contourChunks[key] = entries = new List<int>();
            if (entries.Count == 0 || entries[entries.Count - 1] != segment) entries.Add(segment);
        }

        private void AddScreenChunkAndNeighbors(int tile)
        {
            int chunkColumns = (world.Config.Width + ContourChunkSize - 1) / ContourChunkSize;
            int chunkRows = (world.Config.Height + ContourChunkSize - 1) / ContourChunkSize;
            int q = tile % world.Config.Width / ContourChunkSize, r = tile / world.Config.Width / ContourChunkSize;
            for (int dr = -1; dr <= 1; dr++)
            {
                int row = r + dr; if (row < 0 || row >= chunkRows) continue;
                for (int dq = -1; dq <= 1; dq++)
                {
                    int column = (q + dq) % chunkColumns; if (column < 0) column += chunkColumns;
                    screenContourChunks.Add(row * chunkColumns + column);
                }
            }
        }

        private Vector2 ContourPosition(ContourPoint point, Rect area)
        {
            Vector2 a = Position(point.TileA, area);
            if (point.TileA == point.TileB) return a;
            Vector2 b = Position(point.TileB, area);
            float circumference = world.Config.Width * radius * 1.73205f;
            while (b.x - a.x > circumference / 2) b.x -= circumference;
            while (b.x - a.x < -circumference / 2) b.x += circumference;
            return Vector2.LerpUnclamped(a, b, point.T);
        }

        private void DrawVisibleContours(Rect area)
        {
            if (!mapLayerState.ShowsContinuousContours) return;
            EnsureContourCache();
            screenContourChunks.Clear();
            foreach (var entry in mapRenderEntries) AddScreenChunkAndNeighbors(entry.Tile);
            screenContourIndices.Clear();
            foreach (int chunk in screenContourChunks)
                if (contourChunks.TryGetValue(chunk, out var indices))
                    foreach (int index in indices) screenContourIndices.Add(index);
            // Minor first; major contours remain visually dominant at intersections.
            for (int pass = 0; pass < 2; pass++)
                foreach (int index in screenContourIndices)
                {
                    var segment = contourSegments[index];
                    if (segment.Major != (pass == 1)) continue;
                    Vector2 start = ContourPosition(segment.Start, area), end = ContourPosition(segment.End, area);
                    var bounds = new Rect(Mathf.Min(start.x, end.x), Mathf.Min(start.y, end.y), Mathf.Abs(start.x - end.x), Mathf.Abs(start.y - end.y));
                    if (bounds.xMax < -radius || bounds.xMin > area.width + radius || bounds.yMax < -radius || bounds.yMin > area.height + radius) continue;
                    TopographicOverlay.DrawContour(segment, start, end, radius, mapLayerState.Layer);
                }
        }

        private void DrawMapLayerControls(Rect area)
        {
            // Allow the longest label, "3 Ruggedness", to fit with the shared button padding.
            float width = Mathf.Min(390, area.width - 24), x = area.width - width - 12;
            Fill(new Rect(x - 6, 8, width + 6, mapLayerState.Layer == PlayerMapLayer.Terrain ? 38 : 74), new Color(.035f, .044f, .043f, .94f));
            float third = width / 3;
            if (Button(new Rect(x, 11, third - 4, 30), "1 Terrain", mapLayerState.Layer == PlayerMapLayer.Terrain)) SelectMapLayer(PlayerMapLayer.Terrain);
            if (Button(new Rect(x + third, 11, third - 4, 30), "2 Topography", mapLayerState.Layer == PlayerMapLayer.Topography)) SelectMapLayer(PlayerMapLayer.Topography);
            if (Button(new Rect(x + third * 2, 11, third - 4, 30), "3 Ruggedness", mapLayerState.Layer == PlayerMapLayer.Ruggedness)) SelectMapLayer(PlayerMapLayer.Ruggedness);
            if (mapLayerState.Layer != PlayerMapLayer.Terrain)
            {
                if (Button(new Rect(x, 45, width * .52f, 28), "G Density: " + mapLayerState.Density)) ToggleContourDensity();
                if (mapLayerState.Layer == PlayerMapLayer.Ruggedness && Button(new Rect(x + width * .54f, 45, width * .46f - 4, 28), "K Contours: " + (mapLayerState.RuggednessContours ? "On" : "Off"))) ToggleRuggednessContours();
            }

            if (mapLayerState.Layer == PlayerMapLayer.Topography)
            {
                var panel = new Rect(12, area.height - 48, Mathf.Min(360, area.width - 24), 36);
                Fill(panel, new Color(.035f, .044f, .043f, .90f));
                LayeredTileRenderer.DrawLine(new Vector2(panel.x + 12, panel.y + 13), new Vector2(panel.x + 74, panel.y + 13), TopographicOverlay.ContourWidth(false, radius), TopographicOverlay.ContourColor(false, PlayerMapLayer.Topography));
                LayeredTileRenderer.DrawLine(new Vector2(panel.x + 136, panel.y + 13), new Vector2(panel.x + 198, panel.y + 13), TopographicOverlay.ContourWidth(true, radius), TopographicOverlay.ContourColor(true, PlayerMapLayer.Topography));
                GUI.Label(new Rect(panel.x + 79, panel.y + 5, 55, 22), "minor", small);
                GUI.Label(new Rect(panel.x + 203, panel.y + 5, 150, 22), "major · shaded slope", small);
            }
            else if (mapLayerState.Layer == PlayerMapLayer.Ruggedness)
            {
                var panel = new Rect(12, area.height - 58, Mathf.Min(430, area.width - 24), 46);
                Fill(panel, new Color(.035f, .044f, .043f, .92f));
                float barX = panel.x + 12, barY = panel.y + 9, barWidth = panel.width - 24;
                for (int i = 0; i < 20; i++) Fill(new Rect(barX + barWidth * i / 20, barY, barWidth / 20 + 1, 10), TopographicOverlay.RuggednessColor(i * 100f / 19));
                GUI.Label(new Rect(barX, barY + 13, barWidth, 20), "0 Smooth                         50 Broken                         100 Rugged", small);
            }
        }
    }
}
