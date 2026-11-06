using System;
using System.Linq;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public sealed partial class OpeningGame
    {
        private int scenarioIndex, previewLayer, previewSeason = -1, previewTile = -1;
        private bool previewOpen, previewAfterGeneration;
        private Texture2D previewTexture;
        private float previewZoom = 1;
        private Vector2 previewPan, menuScroll, previewScroll;
        private readonly string[] previewLayers = { "Environment", "Food renewal", "Freshwater", "Elevation", "Volcanism", "Resources", "Ancient relief", "Ruggedness", "Topography", "Temperature", "Snowfall" };

        private void Menu()
        {
            float width = Mathf.Min(640, Screen.width - 48), left = (Screen.width - width) / 2;
            float height = Mathf.Min(780, Screen.height - 32), top = (Screen.height - height) / 2;
            Fill(new Rect(left - 12, top, width + 24, height), Panel);
            GUI.BeginGroup(new Rect(left, top, width, height));
            menuScroll = GUI.BeginScrollView(new Rect(0, 0, width, height), menuScroll, new Rect(0, 0, width - 20, 764));
            float w = width - 30;
            GUI.Label(new Rect(8, 12, w, 38), "A N T H R O P O C E N E", title);
            GUI.Label(new Rect(8, 55, w, 42), "The first generations · Choose a landscape to playtest", text);
            GUI.enabled = generating == null && saving == null;
            for (int i = 0; i < 4; i++)
                if (Button(new Rect(8 + (i % 2) * (w / 2), 100 + (i / 2) * 40, w / 2 - 6, 34), (scenarioIndex == i ? "◆ " : "◇ ") + PlaytestScenarios.Names[i], scenarioIndex == i)) scenarioIndex = i;
            GUI.Label(new Rect(8, 184, w, 66), PlaytestScenarios.Descriptions[scenarioIndex], text);
            if (scenarioIndex == 0)
            {
                GUI.Label(new Rect(8, 255, 100, 25), "World seed", text);
                seedText = GUI.TextField(new Rect(118, 252, w - 110, 30), seedText, 12);
                if (Button(new Rect(8, 290, w, 32), "World size: " + Sizes[sizeIndex])) CycleWorldSize();
                if (Button(new Rect(8, 328, w, 32), "World shape: " + ((WorldShape)shapeIndex))) shapeIndex = (shapeIndex + 1) % 3;
                if (Button(new Rect(8, 366, w, 32), "Tribes: " + players + " (including you)")) CycleTribes();
            }
            else GUI.Label(new Rect(8, 260, w, 130), "Standard · Continents · seed " + PlaytestScenarios.Seed + " · 80 tribes\n\nSame natural world for all three starts. Food, prey and reserves use normal rules. Technology is earned through play. These are environmental analogues, not historical reconstructions.", text);
            if (Button(new Rect(8, 410, w, 32), "Tileset: " + tilesets[tilesetIndex].Name)) SetTileset(tilesetIndex + 1);
            if (Button(new Rect(8, 454, w, 40), "BEGIN THIS OPENING", true)) { previewAfterGeneration = false; NewGame(); }
            if (Button(new Rect(8, 502, w, 36), "BUILD WORLD PREVIEW (DEV)")) { previewAfterGeneration = true; NewGame(); }
            if (world != null)
            {
                if (Button(new Rect(8, 548, w / 2 - 6, 36), "Return to current opening")) { previewOpen = false; menu = false; }
                if (Button(new Rect(8 + w / 2, 548, w / 2 - 6, 36), "Preview current world (dev)")) { InvalidateWorldPreview(); previewOpen = true; menu = false; }
            }
            if (System.IO.File.Exists(SavePath) && Button(new Rect(8, 594, w, 32), "Load saved opening")) Load();
            GUI.enabled = true;
            GUI.Label(new Rect(8, 642, w, 85), generating != null ? operation + "..." : status, text);
            GUI.Label(new Rect(8, 728, w, 28), "Preview reveals the world. Playing restores normal fog of war.", small);
            GUI.EndScrollView(); GUI.EndGroup();
        }

        private void InvalidateWorldPreview()
        {
            if (previewTexture != null) Destroy(previewTexture);
            previewTexture = null; previewTile = -1; previewPan = Vector2.zero; previewZoom = 1;
            InvalidateAtlasContours();
            previewResourceValues = null; previewResourcePicker = false;
        }
        private static Color EnvironmentColor(Tile t)
        {
            switch (t.Environment)
            {
                case EnvironmentKind.Ocean: return new Color(.055f, .12f, .20f);
                case EnvironmentKind.Shallows: return new Color(.17f, .48f, .60f);
                case EnvironmentKind.Lake: return new Color(.31f, .68f, .78f);
                case EnvironmentKind.Desert: return t.River || t.Oasis ? new Color(.43f, .55f, .28f) : new Color(.69f, .55f, .31f);
                case EnvironmentKind.Grassland: return new Color(.54f, .61f, .31f);
                case EnvironmentKind.Temperate: return new Color(.22f, .43f, .28f);
                case EnvironmentKind.Tropical: return new Color(.12f, .31f, .20f);
                case EnvironmentKind.Cold: return new Color(.56f, .65f, .62f);
                default: return new Color(.85f, .89f, .87f);
            }
        }
        private void BuildPreviewTexture()
        {
            // Explicit, on-demand dev read of a committed world. No observations or ecology are mutated.
            var colors = new Color32[world.Tiles.Length]; int width = world.Config.Width, height = world.Config.Height;
            double resourceScale = WorldResourcePreview.ScaleMaximum(world.Resources, previewMaterial, previewAccessible);
            if (previewLayer == 5)
            {
                previewResourceValues = WorldResourcePreview.Values(world, previewMaterial, previewAccessible);
                previewResourceMaximum = previewResourceValues.Max();
            }
            float maxUplift = 0f;
            if (previewLayer == 6 && world.Generation?.AncientOrogenUplift != null)
            {
                for (int i = 0; i < world.Generation.AncientOrogenUplift.Length; i++)
                    if (world.Generation.AncientOrogenUplift[i] > maxUplift) maxUplift = world.Generation.AncientOrogenUplift[i];
            }
            if (maxUplift <= 0f) maxUplift = 0.15f;
            foreach (var t in world.Tiles)
            {
                Color color = EnvironmentColor(t);
                if (previewLayer == 1 || previewLayer == 2)
                {
                    double value = previewLayer == 1 ? PlaytestScenarios.Food(world, t, previewSeason) : PlaytestScenarios.Water(world, t, previewSeason);
                    float fraction = Mathf.Clamp01((float)(Math.Log(1 + value) / Math.Log(101)));
                    color = Color.Lerp(new Color(.035f, .06f, .09f), new Color(.99f, .78f, .27f), fraction);
                }
                else if (previewLayer == 3)
                {
                    float altitude = t.Elevation - (float)world.Config.SeaLevel;
                    color = altitude < 0 ? Color.Lerp(new Color(.04f, .10f, .20f), new Color(.15f, .46f, .55f), Mathf.Clamp01(1 + altitude / .3f))
                        : Color.Lerp(new Color(.23f, .40f, .25f), new Color(.94f, .91f, .83f), Mathf.Clamp01(altitude / .38f));
                }
                else if (previewLayer == 4) color = Color.Lerp(color * .45f, new Color(1, .29f, .09f), Mathf.Clamp01(t.Volcanism * 5));
                else if (previewLayer == 5) color = WorldResourcePreview.ColorFor(previewResourceValues[t.Id], color, resourceScale);
                else if (previewLayer == 6)
                {
                    if (t.WaterBody == WaterKind.Saltwater || t.Elevation < world.Config.SeaLevel)
                    {
                        float depth = (float)(world.Config.SeaLevel - t.Elevation);
                        color = Color.Lerp(new Color(.08f, .22f, .34f), new Color(.03f, .07f, .14f), Mathf.Clamp01(depth / .3f));
                    }
                    else
                    {
                        float uplift = world.Generation?.AncientOrogenUplift != null && t.Id < world.Generation.AncientOrogenUplift.Length
                            ? world.Generation.AncientOrogenUplift[t.Id]
                            : 0f;
                        if (uplift > 0.0005f)
                        {
                            float frac = Mathf.Clamp01(uplift / maxUplift);
                            Color low = new Color(.55f, .22f, .08f);
                            Color mid = new Color(.95f, .55f, .12f);
                            Color high = new Color(.98f, .88f, .45f);
                            Color crest = new Color(1.0f, .98f, .95f);
                            color = frac < 0.33f
                                ? Color.Lerp(low, mid, frac / 0.33f)
                                : frac < 0.70f
                                    ? Color.Lerp(mid, high, (frac - 0.33f) / 0.37f)
                                    : Color.Lerp(high, crest, (frac - 0.70f) / 0.30f);
                        }
                        else
                        {
                            color = Color.Lerp(EnvironmentColor(t) * 0.32f, new Color(0.18f, 0.20f, 0.19f), 0.65f);
                        }
                    }
                }
                else if (previewLayer == 7)
                {
                    Color rugged = TopographicOverlay.RuggednessColor(world.TerrainMetrics.At(t.Id).Ruggedness);
                    if (t.Land) color = rugged;
                }
                else if (previewLayer == 8 && t.Land)
                {
                    var metric = world.TerrainMetrics.At(t.Id);
                    float shade = TopographicOverlay.Hillshade(metric.GradientX, metric.GradientY);
                    color = Color.Lerp(color, shade < 0 ? Color.black : Color.white, Mathf.Abs(shade));
                }
                else if (previewLayer == 9)
                {
                    float temperature = previewSeason < 0 ? t.Seasons.Average(s => s.Temperature) : t.Seasons[previewSeason].Temperature;
                    color = temperature <= 0 ? Color.Lerp(new Color(.12f, .25f, .64f), new Color(.88f, .94f, .98f), Mathf.InverseLerp(-30, 0, temperature))
                        : Color.Lerp(new Color(.88f, .94f, .98f), new Color(.78f, .20f, .08f), Mathf.InverseLerp(0, 30, temperature));
                }
                else if (previewLayer == 10 && t.Land)
                {
                    float snow = previewSeason < 0 ? t.Seasons.Sum(s => s.Snow) : t.Seasons[previewSeason].Snow;
                    float precipitation = previewSeason < 0 ? t.Seasons.Sum(s => s.Snow + s.Rain) : t.Seasons[previewSeason].Snow + t.Seasons[previewSeason].Rain;
                    color = Color.Lerp(new Color(.20f, .28f, .24f), Color.white, precipitation > 0 ? snow / precipitation : 0);
                }
                colors[(height - 1 - t.Id / width) * width + t.Id % width] = color;
            }
            previewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            previewTexture.SetPixels32(colors); previewTexture.Apply(false, true);
        }
        private Vector2 PreviewPoint(int tile, Rect map)
            => new Vector2(map.x + (tile % world.Config.Width + .5f) / world.Config.Width * map.width, map.y + (tile / world.Config.Width + .5f) / world.Config.Height * map.height);

        private void DrawWorldPreview()
        {
            // OnGUI routes here only after generation/resolution guards. There are no orders in this view.
            if (previewTexture == null) BuildPreviewTexture();
            float side = Mathf.Min(350, Screen.width * .34f);
            GUI.enabled = !previewResourcePicker;
            GUI.Label(new Rect(20, 12, Screen.width - 230, 36), "WORLD PREVIEW · DEV / ALL TILES REVEALED", title);
            if (Button(new Rect(Screen.width - 200, 12, 180, 34), "PLAY CURRENT START", true)) { previewOpen = false; menu = false; }
            GUI.enabled = !previewResourcePicker;
            float layerWidth = Mathf.Min(185, (Screen.width - 40) / (float)previewLayers.Length);
            for (int i = 0; i < previewLayers.Length; i++)
                if (Button(new Rect(20 + i * layerWidth, 58, layerWidth - 6, 32), previewLayers[i], previewLayer == i)) { previewLayer = i; Destroy(previewTexture); previewTexture = null; }
            if (previewLayer == 7 || previewLayer == 8) DrawAtlasTopographyControls();
            else if (previewLayer == 5) DrawResourceControls();
            else if (previewLayer == 6)
            {
                if (Button(new Rect(20, 100, 110, 30), "Fit world")) { previewZoom = 1; previewPan = Vector2.zero; }
                if (Button(new Rect(138, 100, 150, 30), "Find my camp"))
                {
                    previewTile = world.Camps.First(c => c.Player == 0).Tile; previewZoom = 12; previewPan = Vector2.zero;
                }
                if (Button(new Rect(296, 100, 175, 30), "Find highest orogen") && world.Generation?.AncientOrogenUplift != null)
                {
                    int bestTile = -1; float bestUplift = 0;
                    for (int i = 0; i < world.Generation.AncientOrogenUplift.Length; i++)
                    {
                        if (world.Generation.AncientOrogenUplift[i] > bestUplift)
                        {
                            bestUplift = world.Generation.AncientOrogenUplift[i];
                            bestTile = i;
                        }
                    }
                    if (bestTile >= 0) { previewTile = bestTile; previewZoom = 12; previewPan = Vector2.zero; }
                }
            }
            else
            {
                if (Button(new Rect(20, 100, 215, 30), previewSeason < 0 ? "Season: annual mean" : "Season: " + (previewSeason + 1))) { previewSeason = previewSeason == 3 ? -1 : previewSeason + 1; Destroy(previewTexture); previewTexture = null; }
                float controlsX = 243;
                if (Button(new Rect(controlsX, 100, 110, 30), "Fit world")) { previewZoom = 1; previewPan = Vector2.zero; }
                if (Button(new Rect(controlsX + 118, 100, 150, 30), "Find my camp"))
                {
                    previewTile = world.Camps.First(c => c.Player == 0).Tile; previewZoom = 12; previewPan = Vector2.zero;
                }
            }
            var area = new Rect(16, 146, Screen.width - side - 32, Screen.height - 184);
            Fill(area, Panel); GUI.BeginGroup(area);
            float fit = Mathf.Min(area.width / world.Config.Width, area.height / world.Config.Height);
            var map = new Rect((area.width - world.Config.Width * fit * previewZoom) / 2 + previewPan.x, (area.height - world.Config.Height * fit * previewZoom) / 2 + previewPan.y, world.Config.Width * fit * previewZoom, world.Config.Height * fit * previewZoom);
            // Centering is requested once; subsequent dragging remains free.
            if (previewTile >= 0 && previewZoom == 12 && previewPan == Vector2.zero)
            { previewPan = new Vector2(area.width / 2, area.height / 2) - PreviewPoint(previewTile, map); map.position += previewPan; }
            if (previewTexture != null) GUI.DrawTexture(map, previewTexture);
            if (previewLayer == 8 || previewLayer == 7) DrawAtlasContours(map, area.size);
            foreach (var c in world.Camps)
            {
                var p = PreviewPoint(c.Tile, map); bool own = c.Player == 0;
                Fill(new Rect(p.x - (own ? 5 : 2), p.y - (own ? 5 : 2), own ? 10 : 4, own ? 10 : 4), own ? Gold : Color.white);
            }
            if (world.Seed == PlaytestScenarios.Seed && world.Config.Width == 1024 && world.Config.Height == 512)
                for (int i = 1; i <= 3; i++)
                {
                    if (!world.Camps.Any(c => c.Tile == PlaytestScenarios.Tile(world, i))) continue;
                    var p = PreviewPoint(PlaytestScenarios.Tile(world, i), map);
                    Fill(new Rect(p.x - 8, p.y - 8, 16, 16), Gold);
                    GUI.Label(new Rect(p.x - 8, p.y - 8, 16, 16), i.ToString(), badge);
                }
            var evt = Event.current; Vector2 mouse = evt.mousePosition;
            if (!previewResourcePicker && new Rect(0, 0, area.width, area.height).Contains(mouse))
            {
                if (evt.type == EventType.ScrollWheel)
                {
                    float old = previewZoom; previewZoom = Mathf.Clamp(previewZoom * Mathf.Pow(1.15f, -evt.delta.y), 1, 64);
                    previewPan = (previewPan + new Vector2(area.width / 2, area.height / 2) - mouse) * (previewZoom / old) + mouse - new Vector2(area.width / 2, area.height / 2); evt.Use();
                }
                else if (evt.type == EventType.MouseDrag) { previewPan += evt.delta; evt.Use(); }
                else if (evt.type == EventType.MouseDown && map.Contains(mouse))
                {
                    int q = Mathf.FloorToInt((mouse.x - map.x) / map.width * world.Config.Width), r = Mathf.FloorToInt((mouse.y - map.y) / map.height * world.Config.Height);
                    previewTile = world.Map.At(q, r); evt.Use();
                }
            }
            if (previewTile >= 0) { var p = PreviewPoint(previewTile, map); GUI.Label(new Rect(p.x - 8, p.y - 24, 30, 24), "+", title); }
            GUI.EndGroup();
            double contourInterval = atlasContourStep * TopographicContours.BaseInterval;
            string contourUnit = AltitudeClimate.Physical(world.Config) ? (contourInterval * world.Config.ElevationMetersPerUnit).ToString("0.#") + " m" : contourInterval.ToString("0.####") + " height";
            string contourInfo = previewLayer == 7 || previewLayer == 8 ? " · Contours " + contourUnit + " · Blue: rivers" : "";
            GUI.Label(new Rect(20, Screen.height - 30, area.width, 26), "Wheel: zoom · Drag: pan · Click: inspect · Rectangular axial rows" + contourInfo, small);
            DrawPreviewDetails(new Rect(Screen.width - side, 146, side - 16, Screen.height - 158));
            GUI.enabled = true;
            if (previewResourcePicker) DrawResourcePicker();
        }

        private void DrawPreviewDetails(Rect area)
        {
            if (previewLayer == 5) { DrawResourceDetails(area); return; }
            Fill(area, Panel); GUI.BeginGroup(area);
            previewScroll = GUI.BeginScrollView(new Rect(0, 0, area.width, area.height), previewScroll, new Rect(0, 0, area.width - 20, 1060));
            float w = area.width - 40, y = 12;
            GUI.Label(new Rect(12, y, w, 65), "Seed " + world.Seed + " · " + world.Camps.Count + " tribes · turn " + world.Turn + "\nWhite: tribes · Gold: your camp\n1 river · 2 steppe · 3 cold coast", text); y += 76;
            string legend = previewLayer == 0 ? "Ocean: dark blue\nShallows / lakes: blue\nDesert: ochre · river / oasis: olive\nGrassland: light green\nTemperate / tropical: dark green\nCold: grey green · ice: white"
                : previewLayer == 3 ? "Continuous elevation\nDeep blue: ocean floor\nTeal: continental shelf\nGreen: lowland\nPale: high mountain belts\nThis is physical relief, not the 1–10 movement levels."
                : previewLayer == 4 ? "Orange: volcanic activity\nOlder island chains can survive after activity ends, so they may remain dark here. Moving hotspot paths leave islands and submerged remnants. This is a generation proxy, not live tectonics."
                : previewLayer == 6 ? "Ancient orogen elevation\nWarm gradient: net elevation added by extinct Wilson-cycle mountain belts (sienna -> amber -> gold -> ivory crest).\nMuted land: unaffected continental interior.\nDeep ocean: strictly preserved marine bathymetry (0.0 uplift)."
                : previewLayer == 7 ? "Radius-four ruggedness (61 cells in the interior)\n0 teal: smooth plane · 50 gold: broken ground · 100 plum: strongest local irregularity\nResidual RMS is mapped by 100 R / (R + 0.005). Height and uniform slope are fitted out."
                : previewLayer == 8 ? "Biome colours with continuous elevation contours and hillshade.\n" + (AltitudeClimate.Physical(world.Config)
                    ? "Bold: every " + (.1 * world.Config.ElevationMetersPerUnit).ToString("0") + " m. Fine reaches " + (.0025 * world.Config.ElevationMetersPerUnit).ToString("0") + " m when zoomed in."
                    : "Bold: every 0.100 height. Fine reaches 0.0025 when zoomed in.") + (world.Config.DrainageTerrainEnabled
                    ? "\nBlue: rivers follow carved valleys. Their terrain corners descend downstream; flat reaches can occur."
                    : "\nBlue: actual boundary river network. Legacy routing can use a filled surface above terrain.")
                : previewLayer == 9 ? "Temperature in degrees Celsius.\nBlue: -30 C · white: 0 C · red: +30 C\nChoose annual mean or a seasonal profile.\n" + (AltitudeClimate.Physical(world.Config)
                    ? "Altitude cooling: " + world.Config.LapseRateCPerKm.ToString("0.#") + " C/km above sea level. Gameplay levels remain percentile bands."
                    : "Legacy abstract altitude calibration. Gameplay levels are percentile bands, not metres.")
                : previewLayer == 10 ? "Snowfall fraction of precipitation.\nDark: 0% · white: 100%\nAnnual uses the precipitation-weighted fraction over all four profiles. This shows falling snow, not accumulated snow cover or glaciers."
                : "Dark = 0 · bright gold = 100+\nLog scale: 0 / 1 / 10 / 100\nUnits per base season (" + world.Config.BaseDays.ToString("0.##") + " days).\nProfile renewal potential, before labor, depletion and competition. Ocean food is excluded from accessible food.";
            GUI.Label(new Rect(12, y, w, 150), legend, small); y += 158;
            var own = world.Camps.First(c => c.Player == 0);
            GUI.Label(new Rect(12, y, w, 30), "YOUR CAMP · radius 1", section); y += 30;
            var near = world.Map.Radius(own.Tile, 1).Select(n => world.Tiles[n]).ToArray();
            GUI.Label(new Rect(12, y, w, 105), "Annual mean food potential / season\nLand: " + near.Where(t => t.Land).Sum(t => PlaytestScenarios.Food(world, t, -1)).ToString("0.0") + " · Water: " + near.Where(t => t.Aquatic).Sum(t => PlaytestScenarios.Food(world, t, -1)).ToString("0.0") + "\nStart viability: " + PlaytestScenarios.Viability(world, own.Tile).ToString("0.0") + "% (threshold " + world.Config.StartViability + "%)\nViability includes labor limits and freshwater; it is not a survival forecast.", small); y += 115;
            if (previewTile < 0) previewTile = own.Tile;
            var tile = world.Tiles[previewTile];
            GUI.Label(new Rect(12, y, w, 30), "INSPECTED TILE " + tile.Id, section); y += 32;
            float orogenUplift = world.Generation?.AncientOrogenUplift != null && tile.Id < world.Generation.AncientOrogenUplift.Length ? world.Generation.AncientOrogenUplift[tile.Id] : 0f;
            float orogenAge = world.Generation?.AncientOrogenAge != null && tile.Id < world.Generation.AncientOrogenAge.Length ? world.Generation.AncientOrogenAge[tile.Id] : 0f;
            string orogenInfo = orogenUplift > 0.0005f ? $"\nAncient orogen uplift: +{orogenUplift:0.000} ({orogenAge:0} Ma)" : (previewLayer == 6 ? "\nAncient orogen uplift: None (0.000)" : "");
            var terrainMetric = world.TerrainMetrics.At(tile.Id);
            string details = tile.Environment + " · " + tile.CurrentVegetation + (tile.Land ? " vegetation" : " plankton/algae") + "\nElevation " + tile.ElevationLevel + " · continuous " + tile.Elevation.ToString("0.0000") + " · river " + (tile.River ? "yes" : "no") + "\nRuggedness " + terrainMetric.Ruggedness.ToString("0") + "/100 · fitted slope " + terrainMetric.Slope.ToString("0.000") + " · samples " + terrainMetric.Samples + "\nFreshwater source: " + tile.FreshwaterSource + "\nMean temperature: " + tile.Seasons.Average(s => s.Temperature).ToString("0.0") + " C"
                + "\nSeasonal range: " + tile.Seasons.Min(s => s.Temperature).ToString("0.0") + " to " + tile.Seasons.Max(s => s.Temperature).ToString("0.0") + " C"
                + (AltitudeClimate.Physical(world.Config) ? "\nAltitude: " + AltitudeClimate.Meters(world.Config, tile.Elevation).ToString("N0") + " m relative to sea level" : "\nAltitude: legacy abstract scale")
                + "\nAltitude cooling: " + AltitudeClimate.Cooling(world.Config, tile.Elevation).ToString("0.0") + " C"
                + "\nSnowfall in " + tile.Seasons.Count(s => s.Snow > 0) + "/4 seasonal profiles" + orogenInfo;
            float detailsHeight = text.CalcHeight(new GUIContent(details), w);
            GUI.Label(new Rect(12, y, w, detailsHeight), details, text); y += detailsHeight + 8;
            GUI.Label(new Rect(12, y, w, 22), "Season       Food       Freshwater", section); y += 25;
            for (int s = 0; s < 4; s++) { GUI.Label(new Rect(12, y, w, 25), (s + 1) + "                 " + PlaytestScenarios.Food(world, tile, s).ToString("0.0") + "          " + PlaytestScenarios.Water(world, tile, s).ToString("0.0"), small); y += 27; }
            var current = LazyEcology.ProjectEcology(world, tile.Id, world.Days);
            GUI.Label(new Rect(12, y + 8, w, 95), "Current stock: " + current.Food.ToString("0.0") + " food / " + current.Freshwater.ToString("0.0") + " freshwater\nSeason numbers follow the generator; climate differs by hemisphere. This dev view does not reveal tiles to your tribe.", small);
            GUI.EndScrollView(); GUI.EndGroup();
        }
    }
}
