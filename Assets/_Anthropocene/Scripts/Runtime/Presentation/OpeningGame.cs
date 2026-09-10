using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Anthropocene.Simulation;
using UnityEngine;
using Terrain = Anthropocene.Simulation.Terrain;

namespace Anthropocene.Presentation
{
    /// <summary>Glyph tiles and immediate-mode controls observe the same filtered knowledge as AI.</summary>
    public sealed partial class OpeningGame : MonoBehaviour
    {
        private WorldState world;
        private AgentObservation observation;
        private readonly TurnResolver resolver = new TurnResolver();
        private readonly AgentObservationBuilder builder = new AgentObservationBuilder();
        private readonly HeuristicTribePolicy policy = new HeuristicTribePolicy();
        private readonly Dictionary<int, Order> planned = new Dictionary<int, Order>();
        private Task<WorldState> generating;
        private Task saving;
        private Task<TurnRecord> resolving;
        private string operation = "Building the world";
        private Texture2D hex;
        private GUIStyle text, title, glyph, small, button, badge, section, centered;
        private Font font;
        private TilesetDefinition[] tilesets;
        private ITileRenderer tileRenderer;
        private int tilesetIndex;
        private int selected, hovered = -1, inspected = -1, center, dispatch = 8, sizeIndex, shapeIndex, players = 32;
        private float radius = 56;
        private Vector2 pan, sidebarScroll, logScroll, helpScroll;
        private string seedText = "1729", status = "Choose a world, then begin.", mode = "Inspect";
        private bool menu = true, help, debug;
        private readonly List<string> journal = new List<string>();
        private static readonly Color Ink = new Color(.035f, .044f, .043f), Panel = new Color(.075f, .086f, .082f), Paper = new Color(.86f, .86f, .78f), Gold = new Color(.91f, .69f, .33f);
        private static readonly Color MoveColor = new Color(.30f, .78f, .58f), FishColor = new Color(.28f, .70f, .88f), InvalidColor = new Color(.88f, .28f, .24f);
        private static readonly string[] Sizes = { "Standard  1024 x 512", "Small  96 x 48", "Tiny  48 x 24" };
        private static readonly int[][] TribeChoices = { new[] { 8, 16, 24, 32, 48, 64, 80 }, new[] { 2, 4, 6, 8, 12, 16 }, new[] { 2, 4, 6, 8 } };
        private string SavePath => Path.Combine(Application.persistentDataPath, "opening-v5.anthro");
        public WorldState State => world;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            hex = new Texture2D(64, 74, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Generated hex tile" };
            for (int y = 0; y < 74; y++) for (int x = 0; x < 64; x++)
            {
                float dy = Mathf.Abs(y - 36.5f) / 36.5f, dx = Mathf.Abs(x - 31.5f) / 31.5f;
                bool inside = dy <= .5f || dx <= (1 - dy) * 2;
                hex.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
            hex.Apply();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tilesets = TilesetCatalog.Load(); SetTileset(0);
        }
        private void SetTileset(int index) { tilesetIndex = index % tilesets.Length; tileRenderer = new LayeredTileRenderer(tilesets[tilesetIndex], hex, font); }
        private void Update()
        {
            if (saving != null && saving.IsCompleted)
            {
                status = saving.IsFaulted ? "Save failed: " + saving.Exception.GetBaseException().Message : "Saved the committed turn. Queued orders are not saved.";
                saving = null;
            }
            if (resolving != null && resolving.IsCompleted) { if (resolving.IsFaulted) status = "Turn failed: " + resolving.Exception.GetBaseException().Message; else FinishTurn(resolving.Result); resolving = null; }
            if (generating == null || !generating.IsCompleted) return;
            if (generating.IsFaulted) status = "World generation failed: " + generating.Exception.GetBaseException().Message;
            else { world = generating.Result; menu = false; planned.Clear(); journal.Clear(); lastResolutionSummary = ""; FocusCamp(); Refresh(); status = operation == "Loading the saved opening" ? "Saved opening restored." : "Your people gather automatically. Choose orders, then commit the generation."; }
            generating = null;
        }
        private void OnDestroy() { if (hex != null) Destroy(hex); }
        private void Styles()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 15, wordWrap = true, normal = { textColor = Paper } };
            title = new GUIStyle(text) { fontSize = 23, fontStyle = FontStyle.Bold };
            small = new GUIStyle(text) { fontSize = 12 };
            glyph = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter, fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = false };
            badge = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Ink } };
            section = new GUIStyle(small) { fontStyle = FontStyle.Bold };
            centered = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 8, 5, 5), normal = { textColor = Paper }, hover = { textColor = Gold } };
        }
        private void OnGUI()
        {
            Styles(); GUI.depth = -20;
            Fill(new Rect(0, 0, Screen.width, Screen.height), Ink);
            if (resolving != null) { ResolutionScreen(); return; }
            if (menu || world == null || generating != null) { Menu(); return; }
            GUIUtility.keyboardControl = 0;
            float side = Mathf.Min(390, Screen.width * .36f);
            var mapRect = new Rect(0, 64, Screen.width - side, Screen.height - 212);
            GUI.enabled = saving == null;
            Header(side); DrawMap(mapRect); Sidebar(new Rect(Screen.width - side, 64, side, Screen.height - 64));
            Footer(new Rect(0, Screen.height - 148, Screen.width - side, 148));
            HandleInput(mapRect);
            GUI.enabled = true;
            if (help) Help(); else DrawHover(mapRect);
        }
        private void Menu()
        {
            float width = Mathf.Min(580, Screen.width - 40), left = (Screen.width - width) / 2;
            float top = Mathf.Max(20, (Screen.height - 620) / 2);
            Fill(new Rect(left - 20, top - 10, width + 40, 620), Panel);
            GUI.Label(new Rect(left, top + 10, width, 42), "A N T H R O P O C E N E", title);
            GUI.Label(new Rect(left, top + 55, width, 55), "The first generations\nA people without a fixed destiny. A landscape with limits.", text);
            GUI.Label(new Rect(left, top + 125, 100, 25), "World seed", text);
            seedText = GUI.TextField(new Rect(left + 110, top + 123, width - 110, 30), seedText, 12);
            if (Button(new Rect(left, top + 169, width, 34), "World size: " + Sizes[sizeIndex])) CycleWorldSize();
            if (Button(new Rect(left, top + 211, width, 34), "World shape: " + ((WorldShape)shapeIndex))) shapeIndex = (shapeIndex + 1) % 3;
            if (Button(new Rect(left, top + 253, width, 34), "Tribes: " + players + "  (you + " + (players - 1) + " rivals)")) CycleTribes();
            GUI.enabled = generating == null;
            if (Button(new Rect(left, top + 295, width, 34), "Tileset: " + tilesets[tilesetIndex].Name)) SetTileset(tilesetIndex + 1);
            if (Button(new Rect(left, top + 348, width, 42), "BEGIN THE PALEOLITHIC", true)) NewGame();
            if (File.Exists(SavePath) && Button(new Rect(left, top + 400, width, 34), "Load saved opening")) Load();
            if (world != null && Button(new Rect(left, top + 442, width, 34), "Return to current opening")) menu = false;
            GUI.enabled = true;
            GUI.Label(new Rect(left, top + 488, width, 70), generating != null ? operation + "..." : status, text);
            GUI.Label(new Rect(left, top + 568, width, 30), "@ camp   h hunters   C camels   H horses   o oasis", small);
        }
        private void NewGame()
        {
            if (!int.TryParse(seedText, out int seed)) { status = "Enter a whole-number seed."; return; }
            var config = new SimulationConfig();
            var tuning = Resources.Load<TextAsset>("OpeningConfig");
            if (tuning != null) JsonUtility.FromJsonOverwrite(tuning.text, config);
            if (sizeIndex == 1) { config.Width = 96; config.Height = 48; config.StartSeparation = 9; }
            if (sizeIndex == 2) { config.Width = 48; config.Height = 24; config.StartSeparation = 6; }
            config.Players = players; config.Shape = (WorldShape)shapeIndex;
            operation = "Building plates, climate, rivers, ecology and viable starts";
            generating = Task.Run(() => new WorldGenerator().Generate(seed, config));
        }
        private void CycleWorldSize()
        {
            sizeIndex = (sizeIndex + 1) % Sizes.Length;
            int maximum = TribeChoices[sizeIndex][TribeChoices[sizeIndex].Length - 1];
            if (players > maximum) players = maximum;
        }
        private void CycleTribes()
        {
            var choices = TribeChoices[sizeIndex]; int next = Array.FindIndex(choices, value => value > players);
            players = next < 0 ? choices[0] : choices[next];
        }
        private void Header(float side)
        {
            Fill(new Rect(0, 0, Screen.width, 64), Panel);
            GUI.Label(new Rect(18, 12, 245, 35), "ANTHROPOCENE", title);
            var camp = world.Camps.First(c => c.Player == 0);
            string season = (world.Config.TurnDays / world.Config.DaysPerYear).ToString("0.#") + " years per turn | " + (world.Days / world.Config.DaysPerYear).ToString("0.#") + " years elapsed";
            GUI.Label(new Rect(270, 12, Mathf.Max(140, Screen.width - side - 280), 46), "Turn " + Math.Min(world.Turn, 12) + " / 12  |  " + (world.Finished ? "Opening complete" : season) + "\n" + (camp.Technology == Technology.None ? "Paleolithic" : "Neolithic: " + TechName(camp.Technology)), text);
            float x = Screen.width - side + 10;
            if (Button(new Rect(x, 16, 66, 30), "Save")) Save();
            if (Button(new Rect(x + 72, 16, 66, 30), "Load")) Load();
            if (Button(new Rect(x + 144, 16, 66, 30), "Help")) help = !help;
            if (Button(new Rect(x + 216, 16, 92, 30), "Menu")) menu = true;
        }
        private Vector2 Position(int tile, Rect area)
        {
            int dr = tile / world.Config.Width - center / world.Config.Width;
            float dq = tile % world.Config.Width - center % world.Config.Width + dr * .5f;
            while (dq > world.Config.Width / 2f) dq -= world.Config.Width;
            while (dq < -world.Config.Width / 2f) dq += world.Config.Width;
            return new Vector2(area.width / 2 + dq * radius * 1.73205f + pan.x, area.height / 2 + dr * radius * 1.5f + pan.y);
        }
        private void DrawMap(Rect area)
        {
            GUI.BeginGroup(area); hovered = -1; hoveredOccupant = -1;
            Vector2 mouse = Event.current.mousePosition;
            int rowRange = Mathf.CeilToInt(area.height / (radius * 1.5f)) + 3;
            int colRange = Mathf.CeilToInt(area.width / (radius * 1.73205f)) + 3;
            int offsetRow = Mathf.RoundToInt(-pan.y / (radius * 1.5f));
            int offsetCol = Mathf.RoundToInt(-pan.x / (radius * 1.73205f) - offsetRow * .5f);
            float nearest = radius * radius;
            var camp = world.Camps.First(c => c.Player == 0);
            for (int dr = offsetRow - rowRange; dr <= offsetRow + rowRange; dr++)
                for (int dq = offsetCol - colRange; dq <= offsetCol + colRange; dq++)
                {
                    int id = world.Map.At(center % world.Config.Width + dq, center / world.Config.Width + dr); if (id < 0) continue;
                    var pos = Position(id, area); if (pos.x < -radius || pos.x > area.width + radius || pos.y < -radius || pos.y > area.height + radius) continue;
                    bool visible = observation.Tiles.TryGetValue(id, out var tile);
                    var rect = new Rect(pos.x - radius * .866f, pos.y - radius, radius * 1.732f, radius * 2);
                    var visual = new TileRenderContext { Bounds = rect, Tile = visible ? tile : null, Inspected = inspected == id, Selected = SelectedTile == id, SelectedEntity = selected };
                    if (visible)
                    {
                        var neighborLevels = new int?[6];
                        for (int direction = 0; direction < neighborLevels.Length; direction++)
                        {
                            int neighbor = ElevationLevelStage.Neighbor(world, id, direction);
                            if (neighbor >= 0 && observation.Tiles.TryGetValue(neighbor, out var neighborTile)) neighborLevels[direction] = neighborTile.ElevationLevel;
                        }
                        visual.ContourEdgeMask = LayeredTileRenderer.ContourEdgeMask(tile.ElevationLevel, neighborLevels);
                    }
                    if (visible) visual.Entities.AddRange(TileDetails.Occupants(observation, id));
                    tileRenderer.Draw(visual);
                    if (!visible) continue;
                    float distance = (mouse - pos).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; hovered = id; hoveredBounds = new Rect(rect.x + area.x, rect.y + area.y, rect.width, rect.height); }
                    for (int slot = 0; slot < Math.Min(6, visual.Entities.Count); slot++) if (LayeredTileRenderer.OccupantSlot(rect, slot, visual.Entities.Count).Contains(mouse)) hoveredOccupant = visual.Entities[slot].Id;
                    DrawPlanningTile(rect, id);
                }
            DrawPaths(area);
            Fill(new Rect(12, 10, Mathf.Min(area.width - 24, 720), 32), new Color(.035f, .044f, .043f, .92f));
            GUI.Label(new Rect(24, 17, Mathf.Min(area.width - 48, 696), 22), ModeInstruction(), small);
            if (world.Finished || world.LinkedPopulation(camp) <= 0) Transition(new Rect(30, 55, Mathf.Min(550, area.width - 60), 225));
            GUI.EndGroup();
        }
        private void Sidebar(Rect area)
        {
            Fill(area, Panel);
            GUI.BeginGroup(area);
            sidebarScroll = GUI.BeginScrollView(new Rect(0, 0, area.width, area.height), sidebarScroll, new Rect(0, 0, area.width - 22, sidebarHeight));
            float width = area.width - 38, y = 12;
            var camp = world.Camps.First(c => c.Player == 0);
            GUI.Label(new Rect(14, y, width, 24), "YOUR TRIBE", section); y += 26;
            double generationNeed = Math.Max(1, world.LinkedPopulation(camp) * world.Config.Consumption * world.Config.GenerationIntervals);
            double reserveGenerations = camp.Food / generationNeed, storageGenerations = TurnResolver.StorageGenerations(world, camp.Tile);
            string hunger = camp.DeficitTurns > 0 ? "\nHunger warning: " + camp.DeficitTurns + " generation" + (camp.DeficitTurns == 1 ? "" : "s") + " in deficit" : world.Turn == 1 ? "\nOne full starting generation is protected" : "\nNo unresolved generational deficit";
            GUI.Label(new Rect(14, y, width, 78), world.LinkedPopulation(camp) + " people  |  " + camp.Population + " at camp (" + camp.GathererPopulation + " gathering)\nReserve: " + reserveGenerations.ToString("0.0") + " generations (" + Knowledge.Bands[observation.FoodCondition] + ")\nLocal storage ceiling: " + storageGenerations.ToString("0.0") + " generations" + hunger, text); y += 90;

            Fill(new Rect(10, y, width + 8, 62), new Color(.14f, .16f, .14f));
            GUI.Label(new Rect(20, y + 7, width - 12, 24), "SELECTED  ·  " + SelectedName, section);
            string selectedDetail = selected == camp.Id ? camp.Population + " people · movement " + observation.CampMovement + " · gathers automatically" : observation.Hunters.First(h => h.Id == selected).Population + " hunters · move " + observation.HunterMovement + " · auto-hunts on contact · " + (observation.Hunters.First(h => h.Id == selected).InSupply ? "supplied" : "carrying provisions");
            GUI.Label(new Rect(20, y + 32, width - 12, 22), selectedDetail, small); y += 70;

            GUI.Label(new Rect(14, y, width, 22), "UNIT STACK / ROSTER", section); y += 24;
            if (Button(new Rect(14, y, width, 34), (selected == camp.Id ? "◆  " : "◇  ") + "@  Camp  ·  tile " + camp.Tile)) { SelectUnit(camp.Id, camp.Tile); } y += 40;
            foreach (var h in observation.Hunters)
            {
                if (Button(new Rect(14, y, width, 34), (selected == h.Id ? "◆  " : "◇  ") + "h  Hunters " + h.Id + "  ·  " + h.Population + " people  ·  tile " + h.Tile)) { SelectUnit(h.Id, h.Tile); } y += 40;
            }

            y += 6; GUI.Label(new Rect(14, y, width, 23), "COMMANDS  ·  " + mode.ToUpperInvariant(), section); y += 26;
            if (Button(new Rect(14, y, width, 36), mode == "Move" ? "◆ MOVE [M]  ·  choose a green tile" : "◇ MOVE [M]  ·  preview range", mode == "Move")) BeginMode("Move"); y += 42;
            if (selected == camp.Id)
            {
                int maximum = Math.Max(0, Math.Min(camp.GathererPopulation, camp.Population - world.Config.MinimumCamp));
                dispatch = Mathf.Clamp(dispatch, 1, Math.Max(1, maximum));
                bool controls = GUI.enabled; GUI.enabled = controls && maximum > 0;
                if (Button(new Rect(14, y, 38, 32), "−")) dispatch = Math.Max(1, dispatch - 1);
                GUI.Label(new Rect(61, y + 5, width - 108, 25), maximum > 0 ? dispatch + " people" : "Camp minimum reached", centered);
                if (Button(new Rect(14 + width - 38, y, 38, 32), "+")) dispatch = Math.Min(maximum, dispatch + 1); y += 38;
                if (Button(new Rect(14, y, width, 38), "DISPATCH [D]  ·  form a hunter group")) Queue(new Order(OrderKind.Dispatch, camp.Id, population: dispatch));
                GUI.enabled = controls; y += 42;
                GUI.Label(new Rect(14, y, width, 44), maximum > 0 ? "Spawns at the camp after this generation resolves; the new group can receive orders next generation." : "Unavailable: dispatch must leave at least " + world.Config.MinimumCamp + " people at camp.", small); y += 50;
            }
            else
            {
                var expedition = observation.Hunters.FirstOrDefault(h => h.Id == selected);
                if (expedition != null)
                {
                    string supply = expedition.InSupply ? "Supplied by camp (path range " + observation.SupplyRange + ")." : "Beyond supply: provisions " + Knowledge.Bands[expedition.Provisions] + "; local foraging supports immediate survival.";
                    if (expedition.DeficitTurns > 0) supply += " Hungry for " + expedition.DeficitTurns + " generation" + (expedition.DeficitTurns == 1 ? "." : "s.");
                    supply += "\n" + PlannedSupplyWarning(expedition);
                    GUI.Label(new Rect(14, y, width, 72), supply, small); y += 76;
                }
                GUI.Label(new Rect(14, y, width, 38), "AUTO-HUNT  ·  End the generation on a wild herd tile.", small); y += 42;
                if (Button(new Rect(14, y, width, 36), mode == "Fish" ? "◆ FISH [F]  ·  choose blue water" : "◇ FISH [F]  ·  preview targets", mode == "Fish")) BeginMode("Fish"); y += 42;
                var reintegrate = new Order(OrderKind.Reintegrate, selected, camp.Id);
                string reintegrateError = resolver.Validate(world, 0, reintegrate, new HashSet<int>(observation.Tiles.Keys));
                bool controls = GUI.enabled; GUI.enabled = controls && reintegrateError == null;
                if (Button(new Rect(14, y, width, 36), "REINTEGRATE [R]  ·  join source camp")) Queue(reintegrate);
                GUI.enabled = controls; y += 40;
                if (reintegrateError != null) { GUI.Label(new Rect(14, y, width, 34), "Unavailable: " + reintegrateError, small); y += 38; }
            }

            bool hasOrder = planned.TryGetValue(selected, out var selectedOrder), available = GUI.enabled;
            GUI.enabled = available && hasOrder;
            if (Button(new Rect(14, y, width, 32), "CANCEL SELECTED ORDER [Backspace]")) CancelSelectedOrder();
            GUI.enabled = available; y += 38;
            GUI.Label(new Rect(14, y, width, 42), hasOrder ? "Selected queue: " + Describe(selectedOrder) + ". Choosing another command replaces it." : "Selected queue: hold position. Choosing a command adds it.", small); y += 48;

            GUI.Label(new Rect(14, y, width, 22), "ORDER QUEUE", section); y += 24;
            if (planned.Count == 0) { GUI.Label(new Rect(14, y, width, 24), "No explicit orders", small); y += 28; }
            foreach (var order in planned.Values.OrderBy(o => o.Entity))
            {
                GUI.Label(new Rect(14, y, width, 36), "◆ " + UnitName(order.Entity) + "  ·  " + Describe(order), small); y += 38;
            }

            string commit = CommitPreview(); float commitHeight = small.CalcHeight(new GUIContent(commit), width);
            Fill(new Rect(10, y, width + 8, commitHeight + 68), new Color(.13f, .145f, .12f));
            GUI.Label(new Rect(20, y + 8, width - 12, commitHeight), commit, small); y += commitHeight + 14;
            GUI.enabled = saving == null && !world.Finished && world.LinkedPopulation(camp) > 0;
            if (Button(new Rect(14, y, width, 44), "COMMIT ORDERS / END GENERATION  [Enter]", true)) EndTurn(); GUI.enabled = saving == null; y += 58;

            if (!string.IsNullOrEmpty(lastResolutionSummary))
            {
                GUI.Label(new Rect(14, y, width, 22), "LAST RESOLUTION", section); y += 24;
                float resultHeight = text.CalcHeight(new GUIContent(lastResolutionSummary), width);
                GUI.Label(new Rect(14, y, width, resultHeight), lastResolutionSummary, text); y += resultHeight + 12;
            }
            GUI.Label(new Rect(14, y, width, 26), "WAYS OF LIVING", section); y += 28;
            foreach (Domain domain in Enum.GetValues(typeof(Domain)))
            {
                double progress = TurnResolver.NeolithicProgress(world, camp, domain) / world.Config.ExperienceThreshold;
                string label = progress < .25 ? "Unpracticed" : progress < .5 ? "Taking root" : progress < .8 ? "Practiced" : "Well established";
                double ramp = PracticeEffects.Ramp(world.Config, camp, domain);
                string effect = domain == Domain.Hunting ? " · meat +" + Math.Round(100 * world.Config.HuntingYieldBonus * ramp) + "%" : " · impact -" + Math.Round(100 * (domain == Domain.Gathering ? world.Config.GatheringImpactReduction : world.Config.FishingImpactReduction) * ramp) + "%";
                GUI.Label(new Rect(14, y, width, 26), domain + ": " + label + effect, text); y += 27;
            }
            y += 12; DrawDemographics(ref y, width);
            int target = inspected >= 0 ? inspected : hovered;
            if (target >= 0 && observation.Tiles.TryGetValue(target, out var tile))
            {
                GUI.Label(new Rect(14, y, width, 42), tile.Terrain.ToString().ToUpperInvariant() + " / " + tile.Relief + (tile.Oasis ? " / OASIS" : ""), small); y += 44;
                string water = tile.WaterBody == WaterKind.Saltwater ? "Saltwater - undrinkable\nFreshwater: none" : "Freshwater: " + Knowledge.Bands[tile.Freshwater] + " (" + tile.FreshwaterSource + ")";
                string rivers = tile.Rivers.Count == 0 ? "" : "\nRiver boundary: " + (tile.Rivers.Max(r => r.FlowBand) == 1 ? "small channel" : tile.Rivers.Max(r => r.FlowBand) == 2 ? "river" : "wide river");
                GUI.Label(new Rect(14, y, width, 210), "Elevation " + tile.ElevationLevel + "/10: " + ElevationLevelStage.Description(tile.ElevationLevel) + "\nGatherable food: " + Knowledge.Bands[tile.Food] + "\nHerd forage: " + Knowledge.Bands[tile.PotentialFood] + " (" + tile.GatherablePercent + "% human-accessible)\n" + water + "\nRenewal: " + Knowledge.Bands[tile.Productivity] + " · " + (tile.Aquatic || tile.WaterBody == WaterKind.Saltwater ? "aquatic" : "land") + " condition: " + Knowledge.Bands[tile.Condition] + rivers + "\n" + (tile.Cultivated ? "Cultivated land" : tile.Fishery ? "Coastal fishery" : tile.Oasis ? "Finite oasis spring; depletion matters" : tile.Aquatic ? "Shore fishing available" : "Wild landscape"), text); y += 213;
                DrawStack(target, ref y, width);
                if (debug) GUI.Label(new Rect(14, y, width, 75), "DEVELOPER: tile " + target + "\nFood " + world.Tiles[target].Food.ToString("F2") + " / water " + world.Tiles[target].Freshwater.ToString("F2"), small);
            }
            else GUI.Label(new Rect(14, y, width, 50), "Hover or click a visible tile to inspect its resources and occupants.", text);
            sidebarHeight = Mathf.Max(area.height, y + 100); GUI.EndScrollView(); GUI.EndGroup();
        }
        private void Footer(Rect area)
        {
            Fill(area, new Color(.065f, .078f, .071f));
            GUI.Label(new Rect(area.x + 16, area.y + 7, area.width - 32, 32), "SELECTED: " + SelectedName.ToUpperInvariant() + "    |    MODE: " + mode.ToUpperInvariant(), section);
            string feedback = status;
            if (hovered >= 0 && (mode == "Move" || mode == "Fish"))
            {
                if (TryTargetOrder(hovered, out _, out string error)) feedback = "Valid target: click to queue. The highlighted path is the route for this generation.";
                else feedback = "Unavailable here: " + error;
            }
            GUI.Label(new Rect(area.x + 16, area.y + 35, area.width - 32, 37), feedback, text);
            GUI.Label(new Rect(area.x + 16, area.y + 74, area.width - 32, 27), "ON COMMIT  ·  " + CommitPreview(), small);
            logScroll = GUI.BeginScrollView(new Rect(area.x + 12, area.y + 104, area.width - 24, 38), logScroll, new Rect(0, 0, area.width - 44, Mathf.Max(38, journal.Count * 21)));
            for (int i = 0; i < journal.Count; i++) GUI.Label(new Rect(4, i * 21, area.width - 55, 22), journal[i], small);
            GUI.EndScrollView();
        }
        private void HandleInput(Rect mapArea)
        {
            var e = Event.current; if (help || saving != null || generating != null) return;
            if (e.type == EventType.ScrollWheel && mapArea.Contains(e.mousePosition)) { radius = Mathf.Clamp(radius - e.delta.y * 2, 20, 96); e.Use(); }
            if (e.type == EventType.MouseDrag && e.button == 2 && mapArea.Contains(e.mousePosition)) { pan += e.delta; e.Use(); }
            if (e.type == EventType.MouseDown && mapArea.Contains(e.mousePosition) && hovered >= 0)
            {
                inspected = hovered;
                if (e.button == 1) Queue(new Order(OrderKind.Move, selected, destination: hovered));
                else if (mode == "Move" || mode == "Fish")
                {
                    if (TryTargetOrder(hovered, out var candidate, out string error)) Queue(candidate);
                    else status = "Unavailable: " + error;
                }
                else
                {
                    SelectOccupant(hovered, hoveredOccupant);
                }
                e.Use();
            }
            if (e.type != EventType.KeyDown) return;
            switch (e.keyCode)
            {
                case KeyCode.Return: EndTurn(); break;
                case KeyCode.M: BeginMode("Move"); break;
                case KeyCode.H: status = "Hunting is automatic when an established hunter group shares a tile with a wild herd."; break;
                case KeyCode.F: BeginMode("Fish"); break;
                case KeyCode.C: FocusCamp(); break;
                case KeyCode.D: if (selected == observation.CampId) Queue(new Order(OrderKind.Dispatch, selected, population: dispatch)); else status = "Select the camp before dispatching a new group."; break;
                case KeyCode.R: if (SelectedIsHunter) Queue(new Order(OrderKind.Reintegrate, selected, observation.CampId)); else status = "Select a hunter group to reintegrate."; break;
                case KeyCode.Backspace: CancelSelectedOrder(); break;
                case KeyCode.Escape: BeginMode("Inspect"); status = "Inspection mode. No queued orders were changed."; break;
                case KeyCode.F3: debug = !debug; break;
                case KeyCode.V: SetTileset(tilesetIndex + 1); status = "Tileset: " + tilesets[tilesetIndex].Name; break;
                case KeyCode.LeftArrow: pan.x += 40; break;
                case KeyCode.RightArrow: pan.x -= 40; break;
                case KeyCode.UpArrow: pan.y += 40; break;
                case KeyCode.DownArrow: pan.y -= 40; break;
                default: return;
            }
            e.Use();
        }
        private void Queue(Order order)
        {
            if (world.Finished || saving != null) return;
            string error = resolver.Validate(world, 0, order, new HashSet<int>(observation.Tiles.Keys));
            if (error != null) { status = "Unavailable: " + error; return; }
            string previous = planned.TryGetValue(order.Entity, out var prior) ? Describe(prior) : null;
            planned[order.Entity] = order.Copy();
            status = PlanningPath.QueueMessage(previous, Describe(order)) + " It will resolve only when you commit the generation.";
            mode = "Inspect";
        }
        private void EndTurn()
        {
            if (saving != null || world.Finished || world.LinkedPopulation(world.Camps.First(c => c.Player == 0)) == 0) return;
            resolvingSummary = CommitPreview();
            var orders = new Dictionary<int, List<Order>> { [0] = planned.Values.OrderBy(o => o.Entity).ToList() }; var traces = new List<DecisionTrace>();
            foreach (var camp in world.Camps.Where(c => c.Player != 0).OrderBy(c => c.Player))
            { var decision = policy.Decide(builder.Build(world, camp.Player), camp.Mandate); orders[camp.Player] = decision.Orders; traces.Add(decision.Trace); }
            resolving = Task.Run(() => resolver.Resolve(world, orders, traces));
        }
        private void FinishTurn(TurnRecord record)
        {
            var ownIds = new HashSet<int>(observation.Hunters.Select(h => h.Id)) { observation.CampId }; int turn = record.Turn;
            var outcomes = new List<string>();
            foreach (var ownOrder in planned.Values)
            {
                int location = ownOrder.Entity == observation.CampId ? world.Camps.First(c => c.Player == 0).Tile : world.Hunters.FirstOrDefault(h => h.Id == ownOrder.Entity)?.Tile ?? -1;
                bool moved = ownOrder.Destination < 0 || location == ownOrder.Destination;
                if (!moved) { journal.Add("Turn " + turn + ": your planned movement was blocked."); outcomes.Add(UnitName(ownOrder.Entity) + " movement was blocked"); }
                else if (ownOrder.Destination >= 0) outcomes.Add(UnitName(ownOrder.Entity) + " reached tile " + ownOrder.Destination);
                if (ownOrder.Kind == OrderKind.Fish)
                {
                    bool success = record.Transfers.Any(t => t.Entity == ownOrder.Entity && t.Kind == "Fishing" && t.Food > 0);
                    outcomes.Add("Hunters " + ownOrder.Entity + (success ? " caught fish" : " caught no fish"));
                }
                if (ownOrder.Kind == OrderKind.Dispatch) { journal.Add("Turn " + turn + ": " + ownOrder.Population + " people formed a hunter group."); outcomes.Add(ownOrder.Population + " people formed a new hunter group at camp"); }
                if (ownOrder.Kind == OrderKind.Reintegrate) outcomes.Add(world.Hunters.Any(h => h.Id == ownOrder.Entity) ? "Hunters " + ownOrder.Entity + " could not reintegrate" : "Hunters " + ownOrder.Entity + " rejoined the camp");
            }
            planned.Clear(); Refresh();
            // Never show the authoritative global event log to the player: it contains hidden rival activity.
            var ownTransfers = record.Transfers.Where(t => ownIds.Contains(t.Entity)).ToArray();
            foreach (var hunt in ownTransfers.Where(t => t.Kind == "Hunting" && t.Food > 0).GroupBy(t => t.Entity).OrderBy(group => group.Key))
                outcomes.Add("Hunters " + hunt.Key + " automatically hunted " + hunt.Sum(t => t.Food).ToString("F0") + " food");
            bool gathered = ownTransfers.Any(t => t.Kind.Contains("gathering") || t.Kind == "Gathering");
            bool hunted = ownTransfers.Any(t => t.Kind == "Hunting" && t.Food > 0), fished = ownTransfers.Any(t => t.Kind == "Fishing" && t.Food > 0);
            var player = world.Camps.First(c => c.Player == 0);
            int grown = record.PopulationChanges.Where(c => c.Camp == player.Id && c.Count > 0).Sum(c => c.Count);
            int died = -record.PopulationChanges.Where(c => c.Camp == player.Id && c.Count < 0).Sum(c => c.Count);
            var demography = record.Demographics.First(d => d.Camp == player.Id); journal.Add("Turn " + turn + ": " + demography.Explanation); journal.Add("Food: land " + demography.TerrestrialFood.ToString("F0") + ", passive marine " + demography.MarineFood.ToString("F0") + ", total received " + demography.FoodReceived.ToString("F0") + " / required " + demography.FoodRequired.ToString("F0"));
            lastResolutionSummary = "Generation " + turn + ": " + demography.StartingPopulation + " + " + demography.Additions + " additions − " + demography.Deaths + " deaths = " + demography.EndingPopulation + " people. " + (outcomes.Count == 0 ? "No explicit orders; units held position." : string.Join(" · ", outcomes)) + " " + demography.Explanation;
            if (grown > 0) journal.Add("Turn " + turn + ": nutrition and reserves supported net growth of " + grown + " people.");
            if (died > 0) journal.Add("Turn " + turn + ": " + died + " people died from sustained hunger.");
            foreach (var supply in record.Logistics.Where(l => l.Camp == player.Id))
            {
                if (supply.RetainedHarvest > 0) journal.Add("Hunters " + supply.Hunter + " kept their harvest beyond the camp's supply range.");
                if (supply.Overflow > 0) journal.Add("Hunters " + supply.Hunter + " could not carry all their harvest.");
                if (supply.Deposited > 0 && supply.ProvisionsLoaded == 0) journal.Add("Hunters " + supply.Hunter + " delivered provisions to the camp.");
            }
            bool hungry = demography.DeficitSteps > 0;
            journal.Add("Turn " + turn + ": " + (gathered ? "gathered wild food" : "gathering yielded little") + (hunted ? ", successful hunt" : "") + (fished ? ", brought in fish" : "") + ". " + (hungry ? "Some people went hungry." : "Everyone was fed."));
            logScroll.y = 100000;
            status = player.Technology == Technology.None ? "A new generation. Inspect the land and plan your next orders." : "Your people discovered " + TechName(player.Technology) + ".";
            if (!world.Hunters.Any(h => h.Id == selected)) selected = player.Id;
        }
        private void Refresh() { observation = builder.Build(world, 0); if (!observation.Tiles.ContainsKey(inspected)) inspected = -1; }
        private void FocusCamp() { var camp = world.Camps.First(c => c.Player == 0); center = camp.Tile; selected = camp.Id; pan = Vector2.zero; mode = "Inspect"; }
        private void Save()
        {
            if (saving != null) return;
            string directory = Application.persistentDataPath, path = SavePath; var committed = world;
            status = "Saving this turn...";
            // Orders stay disabled while the worker reads this committed state.
            saving = Task.Run(() =>
            {
                Directory.CreateDirectory(directory); string temporary = path + ".tmp";
                File.WriteAllBytes(temporary, SaveCodec.Save(committed));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            });
        }
        private void Load()
        {
            if (saving != null || generating != null) return;
            string path = SavePath; operation = "Loading the saved opening";
            generating = Task.Run(() => SaveCodec.Load(File.ReadAllBytes(path)));
        }
        private void Transition(Rect rect)
        {
            Fill(rect, Panel); var camp = world.Camps.First(c => c.Player == 0);
            GUI.Label(new Rect(rect.x + 18, rect.y + 16, rect.width - 36, 38), camp.Technology == Technology.None ? "The camp has fallen silent" : "A new way of living", title);
            string body = camp.Technology == Technology.None ? "Your people could not sustain themselves. A different route or division of labor may change their fate." : "Through practice, your people discovered " + TechName(camp.Technology) + ". The first breakthrough sets a direction; it does not close the other paths.";
            if (camp.ForcedTransition) body += " Your balanced experience carries forward, with a bonus toward " + (Domain)camp.SecondaryDomain + ".";
            GUI.Label(new Rect(rect.x + 18, rect.y + 62, rect.width - 36, 95), body, text);
            if (Button(new Rect(rect.x + 18, rect.y + 175, rect.width - 36, 32), "Explore another beginning")) menu = true;
        }
        private void Help()
        {
            float width = Mathf.Min(660, Screen.width - 50); var r = new Rect((Screen.width - width) / 2, 80, width, Mathf.Min(520, Screen.height - 110));
            Fill(r, new Color(.12f, .14f, .12f));
            GUI.Label(new Rect(r.x + 20, r.y + 15, width - 40, 38), "READING THE LAND", title);
            string helpText =
                "@ Your camp (gold) / rival camp (red)   h Hunters\na Antelope   g Goats   b Bison   C Camels   H Horses   d Domestic herd\n. Grassland   T Woodland   & Jungle   : Desert\nMerged contours: elevation 1-10; shared edges at equal height disappear; X Impassable peak; o Oasis; w Freshwater\nRivers follow tile edges. Sea tiles contain undrinkable saltwater.\n\nDots beneath glyphs show food from Depleted (one) to Abundant (five). Select a tile for food, water and renewal bands.\n\nClick a unit slot or roster entry to select that exact unit. The selected slot has a gold frame. M or right-click enters movement planning: green tiles are valid this generation, a thick line previews the route, and red means the pointed tile is unavailable. Established hunter groups automatically hunt any wild herd sharing their final tile; fishing targets are blue.\n\nEach unit can queue one order. A new order for that unit replaces its previous order; Backspace or the Cancel button removes it. Gold paths and Q badges are committed previews. Enter resolves every queued order simultaneously. Units without orders hold position; camp gathering is automatic.\n\nD dispatches hunters. A dispatched group appears at camp after resolution and can first receive orders next generation. R reintegrates hunters already at their source camp. Escape leaves targeting without changing queued orders.\n\nWheel zooms; middle-drag or arrows pan; C centers camp.\nHunter tethers preview the planned camp connection: green is supplied, amber crosses unknown terrain, and red will be disconnected. Hunters outside supply forage locally for subsistence before consuming carried provisions. Return to deposit surplus food. V switches tilesets. Hold still on a visible tile for two seconds for details. Saltwater is undrinkable. Wild herd labels are estimates: fewer than 10, rounded tens (~40), then broad hundreds (100+). Your own livestock counts are exact.";
            float contentHeight = text.CalcHeight(new GUIContent(helpText), width - 62);
            helpScroll = GUI.BeginScrollView(new Rect(r.x + 20, r.y + 62, width - 40, r.height - 120), helpScroll, new Rect(0, 0, width - 62, contentHeight));
            GUI.Label(new Rect(0, 0, width - 62, contentHeight), helpText, text);
            GUI.EndScrollView();
            if (Button(new Rect(r.x + 20, r.yMax - 45, width - 40, 32), "Return to the land")) help = false;
        }
        private static string Describe(Order order)
        {
            if (order.Kind == OrderKind.Dispatch) return "Dispatch " + order.Population + " people (spawns after resolution)";
            if (order.Kind == OrderKind.Hunt) return "Hunt herd " + order.Target + (order.Destination >= 0 ? " from tile " + order.Destination : "");
            if (order.Kind == OrderKind.Fish) return "Fish water tile " + order.Target + (order.Destination >= 0 ? " from tile " + order.Destination : " from current tile");
            if (order.Kind == OrderKind.Move) return "Move to tile " + order.Destination;
            if (order.Kind == OrderKind.Reintegrate) return "Reintegrate at source camp";
            return "Hold position";
        }
        private static string TechName(Technology tech) => tech == Technology.AnimalHusbandry ? "Animal Husbandry" : tech == Technology.Cultivation ? "Agriculture / Cultivation" : "Watercraft and Fisheries";
        private bool Button(Rect rect, string label, bool accent = false) { var previous = GUI.backgroundColor; GUI.backgroundColor = accent ? new Color(.55f, .49f, .26f) : new Color(.32f, .37f, .32f); bool clicked = GUI.Button(rect, label, button); GUI.backgroundColor = previous; return clicked; }
        private static void Fill(Rect rect, Color color) { var before = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = before; }
        private static void Tint(Rect rect, Texture texture, Color color) { var before = GUI.color; GUI.color = color; GUI.DrawTexture(rect, texture); GUI.color = before; }
    }
}
