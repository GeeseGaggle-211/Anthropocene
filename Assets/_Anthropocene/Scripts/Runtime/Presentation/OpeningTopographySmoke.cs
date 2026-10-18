#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public sealed partial class OpeningGame
    {
        private sealed class TopographyCaptureSite
        {
            public Camp Camp;
            public int Tile;
            public KnownTerrainMetric Metric;
        }

        private void SelectTopographyCamp(Camp target, int cameraTile = -1)
        {
            int prior = target.Player;
            if (prior != 0)
                foreach (var society in world.Societies)
                    if (society.Player == 0) society.Player = prior;
                    else if (society.Player == prior) society.Player = 0;
            Ownership.Bind(world);
            drafting = null;
            planned.Clear();
            center = cameraTile >= 0 ? cameraTile : target.Tile;
            selected = target.Id;
            inspected = center;
            pan = Vector2.zero;
            mode = "Inspect";
            Refresh();
        }

        private IEnumerator CaptureTopographyFrame(string output, string name, PlayerMapLayer layer, ContourDensity density,
            string sourceSha, StringBuilder metadata)
        {
            SelectMapLayer(layer);
            if (mapLayerState.Density != density) ToggleContourDensity();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            EnsureContourCache();
            timer.Stop();
            string fingerprint = SaveCodec.Fingerprint(world);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png"));
            yield return new WaitForSeconds(.35f);
            if (SaveCodec.Fingerprint(world) != fingerprint) throw new InvalidOperationException("Map-layer capture changed authoritative state.");
            var metric = world.TerrainMetrics.At(center);
            bool hasObserved = knownTerrain != null && knownTerrain.TryGet(center, out var observedMetric);
            metadata.AppendLine(name + ": seed=" + world.Seed + " size=" + world.Config.Width + "x" + world.Config.Height +
                " shape=" + world.Config.Shape + " players=" + world.Config.Players + " cameraTile=" + center +
                " cameraQ=" + center % world.Config.Width + " cameraR=" + center / world.Config.Width +
                " pan=" + pan.x.ToString("0.0", CultureInfo.InvariantCulture) + "," + pan.y.ToString("0.0", CultureInfo.InvariantCulture) +
                " zoomRadius=" + radius.ToString("0.0", CultureInfo.InvariantCulture) + " visibleTiles=" + (observation?.Tiles.Count ?? 0) +
                " tileset=" + tilesets[tilesetIndex].Id +
                " layer=" + layer + " density=" + density + " contourStep=" + TopographicContours.Interval(density, radius).ToString("0.0000", CultureInfo.InvariantCulture) +
                " elevation=" + metric.Elevation.ToString("0.0000", CultureInfo.InvariantCulture) + " elevationLevel=" + world.Tiles[center].ElevationLevel +
                " canonicalRuggedness=" + metric.Ruggedness.ToString("0.0", CultureInfo.InvariantCulture) +
                " displayedRuggedness=" + (hasObserved ? observedMetric.Ruggedness.ToString("0.0", CultureInfo.InvariantCulture) : "unavailable") +
                " displayedSamples=" + (hasObserved ? observedMetric.Samples + "/" + observedMetric.NeighborhoodSamples : "0/0") +
                " activationMs=" + timer.Elapsed.TotalMilliseconds.ToString("0.000", CultureInfo.InvariantCulture) +
                " sourceSha=" + sourceSha);
        }

        private IEnumerator TopographySmoke(string directory, string sourceSha)
        {
            Application.runInBackground = true;
            string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
            var metadata = new StringBuilder();
            scenarioIndex = 1; sizeIndex = 0; shapeIndex = (int)WorldShape.Continents; players = 80;
            NewGame(); while (generating != null) yield return null;
            if (world == null || world.Seed != PlaytestScenarios.Seed || world.Config.Shape != WorldShape.Continents) throw new InvalidOperationException("Topography reference world did not generate: " + status);
            // Update starts the normal advisory AI draft when generation completes. Let that
            // read finish before this capture-only route swaps player labels between camps.
            while (drafting != null && !drafting.IsCompleted) yield return null;
            drafting = null;
            radius = 64;
            var sites = new List<TopographyCaptureSite>();
            var index = new PlanningIndex(world);
            var observationBuilder = new AgentObservationBuilder();
            foreach (var camp in world.Camps.OrderBy(c => c.Id))
            {
                var campObservation = observationBuilder.Build(world, camp.Player, index, false, false);
                var campTerrain = KnownTerrainAnalysis.Build(campObservation);
                foreach (var tile in campObservation.Tiles.Values.Where(t => t.Land).OrderBy(t => t.Id))
                    sites.Add(new TopographyCaptureSite { Camp = camp, Tile = tile.Id, Metric = campTerrain.At(tile.Id) });
            }
            var rolling = sites.Where(site => world.Tiles[site.Tile].ElevationLevel >= 2 && world.Tiles[site.Tile].ElevationLevel <= 4)
                .OrderBy(site => Math.Abs(site.Metric.Ruggedness - 23)).ThenBy(site => site.Tile).ThenBy(site => site.Camp.Id).FirstOrDefault()
                ?? sites.OrderBy(site => Math.Abs(site.Metric.Ruggedness - 23)).ThenBy(site => site.Tile).ThenBy(site => site.Camp.Id).First();
            var broken = sites.OrderByDescending(site => site.Metric.Ruggedness).ThenBy(site => site.Tile).ThenBy(site => site.Camp.Id).First();
            var smoothHigh = sites.Where(site => world.Tiles[site.Tile].ElevationLevel >= 5)
                .OrderBy(site => site.Metric.Ruggedness).ThenByDescending(site => world.Tiles[site.Tile].Elevation).ThenBy(site => site.Tile).ThenBy(site => site.Camp.Id).FirstOrDefault()
                ?? sites.OrderByDescending(site => world.Tiles[site.Tile].Elevation).ThenBy(site => site.Metric.Ruggedness).ThenBy(site => site.Tile).First();

            SelectTopographyCamp(rolling.Camp, rolling.Tile);
            yield return CaptureTopographyFrame(output, "01-rolling-terrain", PlayerMapLayer.Terrain, ContourDensity.Normal, sourceSha, metadata);
            yield return CaptureTopographyFrame(output, "02-rolling-topography-normal", PlayerMapLayer.Topography, ContourDensity.Normal, sourceSha, metadata);
            yield return CaptureTopographyFrame(output, "03-rolling-topography-fine", PlayerMapLayer.Topography, ContourDensity.Fine, sourceSha, metadata);

            SelectTopographyCamp(broken.Camp, broken.Tile);
            yield return CaptureTopographyFrame(output, "04-broken-topography", PlayerMapLayer.Topography, ContourDensity.Normal, sourceSha, metadata);
            yield return CaptureTopographyFrame(output, "05-broken-ruggedness", PlayerMapLayer.Ruggedness, ContourDensity.Normal, sourceSha, metadata);

            SelectTopographyCamp(smoothHigh.Camp, smoothHigh.Tile);
            yield return CaptureTopographyFrame(output, "06-smooth-elevated-topography", PlayerMapLayer.Topography, ContourDensity.Normal, sourceSha, metadata);
            yield return CaptureTopographyFrame(output, "07-smooth-elevated-ruggedness", PlayerMapLayer.Ruggedness, ContourDensity.Normal, sourceSha, metadata);

            Camp river = world.Camps.Where(c => world.Map.Radius(c.Tile, world.Config.VisionRadius).Any(id => world.Tiles[id].River))
                .OrderBy(c => c.Id).FirstOrDefault() ?? rolling.Camp;
            SelectTopographyCamp(river);
            Queue(new Order(OrderKind.Dispatch, river.Id, population: 8));
            EndTurn(); while (resolving != null) yield return null;
            yield return CaptureTopographyFrame(output, "08-gameplay-river-units-fog", PlayerMapLayer.Topography, ContourDensity.Normal, sourceSha, metadata);

            scenarioIndex = 0; sizeIndex = 2; shapeIndex = (int)WorldShape.Pangaea; players = 2; seedText = "29";
            NewGame(); while (generating != null) yield return null;
            if (world == null || world.Seed != 29 || world.Config.Shape != WorldShape.Pangaea) throw new InvalidOperationException("Non-Continents smoke world did not generate: " + status);
            FocusCamp(); Refresh(); radius = 56;
            string smokeFingerprint = SaveCodec.Fingerprint(world);
            yield return CaptureTopographyFrame(output, "09-pangaea-seed29-ruggedness", PlayerMapLayer.Ruggedness, ContourDensity.Fine, sourceSha, metadata);
            if (SaveCodec.Fingerprint(world) != smokeFingerprint) throw new InvalidOperationException("Pangaea smoke view changed authoritative state.");

            metadata.AppendLine("ruggednessPreprocessMs=" + world.TerrainMetrics.BuildMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " cacheBytes=" + world.TerrainMetrics.ApproximateBytes);
            File.WriteAllText(Path.Combine(output, "captures.txt"), metadata.ToString());
            yield return new WaitForSeconds(1);
            if (Directory.GetFiles(output, "*.png").Length != 9) throw new InvalidOperationException("Missing topography screenshots; run this check in a visible Unity player window.");
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: nine Unity-rendered topography/ruggedness captures with recorded seed, shape, camera, zoom, density and source SHA; Standard/Continents/11 plus Pangaea/29; authoritative fingerprints unchanged by view use.\n");
            Application.Quit();
        }
    }
}
#endif
