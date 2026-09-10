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
        private GUIStyle text, title, glyph, small, button;
        private Font font;
        private TilesetDefinition[] tilesets;
        private ITileRenderer tileRenderer;
        private int tilesetIndex;
        private int selected, hovered = -1, inspected = -1, center, dispatch = 8, sizeIndex, shapeIndex, players = 6;
        private float radius = 56;
        private Vector2 pan, sidebarScroll, logScroll, helpScroll;
        private string seedText = "1729", status = "Choose a world, then begin.", mode = "Inspect";
        private bool menu = true, help, debug;
        private readonly List<string> journal = new List<string>();
        private static readonly Color Ink = new Color(.055f, .066f, .065f), Panel = new Color(.085f, .098f, .093f), Paper = new Color(.83f, .84f, .75f), Gold = new Color(.91f, .69f, .33f);
        private static readonly string[] Sizes = { "Standard  1024 x 512", "Small  96 x 48", "Tiny  48 x 24" };
        private string SavePath => Path.Combine(Application.persistentDataPath, "opening-v3.anthro");
        public WorldState State => world;
        private bool Busy => resolving != null || generating != null || saving != null;
        private int WaterCost => Math.Max(1, world.Config.HunterMovement / Math.Max(1, world.Config.WaterMovement));
        private bool reviewOrders;
        private Vector2 reviewScroll;

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
            else { world = generating.Result; menu = false; planned.Clear(); journal.Clear(); FocusCamp(); Refresh(); status = operation == "Loading the saved opening" ? "Saved opening restored." : "Your people gather automatically. Choose orders, then end the generation."; }
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
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 8, 5, 5), normal = { textColor = Paper }, hover = { textColor = Gold } };
        }
        private void OnGUI()
        {
            HandleEarlyInput(Event.current);
            Styles(); GUI.depth = -20;
            Fill(new Rect(0, 0, Screen.width, Screen.height), Ink);
            if (resolving != null) { GUI.Label(new Rect(40, 80, Screen.width - 80, 100), "Resolving the generation... Seasonal food flows, consumption and demographics are advancing.", title); return; }
            if (menu || world == null || generating != null) { Menu(); return; }
            GUIUtility.keyboardControl = 0;
            float side = Mathf.Min(340, Screen.width * .33f);
            var mapRect = new Rect(0, 64, Screen.width - side, Screen.height - 190);
            GUI.enabled = !Busy && !reviewOrders && !help;
            Header(side); DrawMap(mapRect); Sidebar(new Rect(Screen.width - side, 64, side, Screen.height - 64));
            if (Busy) { GUI.enabled = true; return; }
            Footer(new Rect(0, Screen.height - 126, Screen.width - side, 126));
            HandleInput(mapRect);
            GUI.enabled = true;
            if (reviewOrders) DrawOrderReview(); else if (help) Help(); else DrawHover(mapRect);
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
            if (Button(new Rect(left, top + 169, width, 34), "World size: " + Sizes[sizeIndex])) sizeIndex = (sizeIndex + 1) % Sizes.Length;
            if (Button(new Rect(left, top + 211, width, 34), "World shape: " + ((WorldShape)shapeIndex))) shapeIndex = (shapeIndex + 1) % 3;
            if (Button(new Rect(left, top + 253, width, 34), "Tribes: " + players + "  (you + " + (players - 1) + " rivals)")) players = players >= 8 ? 2 : players + 1;
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
                    bool own = PlanningPreview.Origin(observation, selected) == id;
                    var rect = new Rect(pos.x - radius * .866f, pos.y - radius, radius * 1.732f, radius * 2);
                    var visual = new TileRenderContext { Bounds = rect, Tile = visible ? tile : null, Inspected = inspected == id, Selected = own, SelectedEntity = selected };
                    if (visible) visual.Entities.AddRange(TileDetails.Occupants(observation, id));
                    tileRenderer.Draw(visual);
                    if (!visible) continue;
                    float distance = (mouse - pos).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; hovered = id; hoveredBounds = new Rect(rect.x + area.x, rect.y + area.y, rect.width, rect.height); }
                    for (int slot = 0; slot < Math.Min(6, visual.Entities.Count); slot++) if (LayeredTileRenderer.OccupantSlot(rect, slot, visual.Entities.Count).Contains(mouse)) hoveredOccupant = visual.Entities[slot].Id;
                    if (mode == "Move" && PlanningPreview.Path(observation, selected, id, WaterCost).Length > 0)
                        GUI.Label(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 20), "MOVE", small);
                    var destinations = planned.Values.Where(o => o.Destination == id || o.Target == id && o.Kind == OrderKind.Fish).OrderBy(o => o.Entity).ToArray();
                    if (destinations.Length > 0)
                    {
                        var badge = new Rect(rect.x + 5, rect.yMax - 22, rect.width - 10, 20);
                        Fill(badge, Panel); GUI.Label(badge, "TO " + string.Join(",", destinations.Select(o => o.Entity)), small);
                    }
                }
            DrawPlannedRoutes(area);
            bool selectedRoute = planned.TryGetValue(selected, out var selectedPlan) && selectedPlan.Destination >= 0;
            bool otherRoutes = planned.Values.Any(o => o.Entity != selected && o.Destination >= 0);
            string routeKey = selectedRoute ? "    |    GOLD ROUTE: " + selected + (otherRoutes ? " · DIM ROUTES: OTHER GROUPS" : "") : otherRoutes ? "    |    DIM ROUTES BELONG TO OTHER GROUPS · SELECT ONE TO HIGHLIGHT" : "";
            GUI.Label(new Rect(16, 12, area.width - 32, 25), "CURRENT SIGHT ONLY    |    " + mode.ToUpperInvariant() + routeKey, small);
            var key = new Rect(12, 38, Mathf.Min(665, area.width - 24), 38);
            Fill(key, new Color(Panel.r, Panel.g, Panel.b, .88f));
            GUI.Label(new Rect(key.x + 8, key.y + 3, key.width - 16, key.height - 6), "MAP KEY  . grass · T woodland · & jungle · : desert · waves water · dots food\n@ camp · h hunters · a antelope · g goats · b bison · C camels · H horses", small);
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
            GUI.Label(new Rect(14, y, width, 30), "YOUR PEOPLE", title); y += 36;
            GUI.Label(new Rect(14, y, width, 58), world.LinkedPopulation(camp) + " people  |  " + camp.Population + " at camp\nFood reserves: " + Knowledge.Bands[observation.FoodCondition] + (camp.DeficitTurns > 0 ? "\nHunger: " + camp.DeficitTurns + " recent seasonal deficits" : world.Turn == 1 ? "\nThe first generation awaits" : "\nCamp supplied in the final interval"), text); y += 70;
            if (Button(new Rect(14, y, width, 32), (selected == camp.Id ? "> " : "") + "@  Camp  [movement 1]")) { selected = camp.Id; center = camp.Tile; pan = Vector2.zero; mode = "Inspect"; } y += 38;
            foreach (var h in observation.Hunters)
            {
                if (Button(new Rect(14, y, width, 32), (selected == h.Id ? "> " : "") + "h  Hunters " + h.Id + "  (" + h.Population + ") [move 2]")) { selected = h.Id; center = h.Tile; pan = Vector2.zero; mode = "Inspect"; } y += 38;
            }
            GUI.Label(new Rect(14, y, width, 23), "PLAN ORDERS", small); y += 26;
            PlanningLabel(ref y, width, "Selected: " + (selected == camp.Id ? "Camp " : "Hunters ") + selected + " | " + mode + "\n" + status);
            if (Button(new Rect(14, y, width, 32), "Move [M]  - choose a visible tile")) mode = "Move"; y += 38;
            if (selected == camp.Id)
            {
                int maximumDispatch = Math.Max(0, camp.Population - observation.MinimumCamp);
                if (maximumDispatch > 0) dispatch = Mathf.Clamp(dispatch, 1, maximumDispatch);
                bool controlsEnabled = GUI.enabled;
                GUI.enabled = controlsEnabled && maximumDispatch > 0;
                if (Button(new Rect(14, y, 35, 30), "-")) dispatch = Math.Max(1, dispatch - 1);
                GUI.Label(new Rect(58, y + 4, width - 100, 25), maximumDispatch > 0 ? dispatch + " people to dispatch" : "No population available to dispatch", text);
                if (Button(new Rect(14 + width - 35, y, 35, 30), "+")) dispatch = Math.Min(maximumDispatch, dispatch + 1); y += 36;
                if (Button(new Rect(14, y, width, 32), "Dispatch hunters [D]")) Queue(new Order(OrderKind.Dispatch, camp.Id, population: dispatch)); y += 38;
                GUI.enabled = controlsEnabled;
                PlanningLabel(ref y, width, maximumDispatch > 0 ? "Dispatch preview: camp " + camp.Population + " -> " + (camp.Population - dispatch) + " people before food/growth (minimum " + observation.MinimumCamp + "). New group appears at camp during resolution; it can receive orders next turn. Dispatch replaces a queued camp move." : "Dispatch unavailable: the camp must retain at least " + observation.MinimumCamp + " people.");
            }
            else
            {
                var expedition = observation.Hunters.FirstOrDefault(h => h.Id == selected);
                if (expedition != null)
                {
                    string supply = PlanningPreview.SupplyText(observation, expedition, planned.Values);
                    if (expedition.DeficitTurns > 0) supply += " Hungry for " + expedition.DeficitTurns + " seasonal checks.";
                    PlanningLabel(ref y, width, supply);
                }
                if (Button(new Rect(14, y, width, 32), "Hunt [H]  - choose a visible herd")) mode = "Hunt"; y += 38;
                if (Button(new Rect(14, y, width, 32), "Fish [F]  - choose adjacent water")) mode = "Fish"; y += 38;
                if (Button(new Rect(14, y, width, 32), "Reintegrate at camp [R]")) Queue(new Order(OrderKind.Reintegrate, selected, camp.Id)); y += 38;
            }
            if (Button(new Rect(14, y, width, 30), "Clear selected unit's order")) { planned.Remove(selected); status = "Order cleared."; } y += 38;
            PlanningLabel(ref y, width, planned.TryGetValue(selected, out var order) ? "Queued: " + Describe(order) : selected == camp.Id ? "No order: the camp stays here and gathers automatically." : "No order: this hunter group stays here with no active harvest strategy.");
            GUI.enabled = !Busy && !help && !reviewOrders && !world.Finished && world.LinkedPopulation(camp) > 0;
            if (Button(new Rect(14, y, width, 44), "REVIEW ALL ORDERS  [Enter]", true)) EndTurn(); GUI.enabled = !Busy && !reviewOrders && !help; y += 56;
            GUI.Label(new Rect(14, y, width, 26), "WAYS OF LIVING", small); y += 28;
            foreach (Domain domain in Enum.GetValues(typeof(Domain)))
            {
                double progress = TurnResolver.NeolithicProgress(world, camp, domain) / world.Config.ExperienceThreshold;
                string label = progress < .25 ? "Unpracticed" : progress < .5 ? "Taking root" : progress < .8 ? "Practiced" : "Well established";
                GUI.Label(new Rect(14, y, width, 26), domain + ": " + label, text); y += 27;
            }
            y += 12; DrawDemographics(ref y, width);
            int target = inspected >= 0 ? inspected : hovered;
            if (target >= 0 && observation.Tiles.TryGetValue(target, out var tile))
            {
                GUI.Label(new Rect(14, y, width, 42), tile.Terrain.ToString().ToUpperInvariant() + " / " + tile.Relief + (tile.Oasis ? " / OASIS" : ""), small); y += 44;
                string water = tile.WaterBody == WaterKind.Saltwater ? "Saltwater - undrinkable\nFreshwater: none" : "Freshwater: " + Knowledge.Bands[tile.Freshwater] + " (" + tile.FreshwaterSource + ")";
                string rivers = tile.Rivers.Count == 0 ? "" : "\nRiver boundary: " + (tile.Rivers.Max(r => r.FlowBand) == 1 ? "small channel" : tile.Rivers.Max(r => r.FlowBand) == 2 ? "river" : "wide river");
                GUI.Label(new Rect(14, y, width, 165), "Elevation " + tile.ElevationLevel + "/10: " + ElevationLevelStage.Description(tile.ElevationLevel) + "\nFood: " + Knowledge.Bands[tile.Food] + "\n" + water + "\nRenewal: " + Knowledge.Bands[tile.Productivity] + rivers + "\n" + (tile.Cultivated ? "Cultivated land" : tile.Fishery ? "Coastal fishery" : tile.Oasis ? "Finite oasis spring; depletion matters" : tile.Aquatic ? "Shore fishing available" : "Wild landscape"), text); y += 168;
                DrawStack(target, ref y, width);
                if (debug) GUI.Label(new Rect(14, y, width, 75), "DEVELOPER: tile " + target + "\nFood " + world.Tiles[target].Food.ToString("F2") + " / water " + world.Tiles[target].Freshwater.ToString("F2"), small);
            }
            else GUI.Label(new Rect(14, y, width, 50), "Hover or click a visible tile to inspect its resources and occupants.", text);
            sidebarHeight = Mathf.Max(area.height, y + 100); GUI.EndScrollView(); GUI.EndGroup();
        }
        private void Footer(Rect area)
        {
            Fill(area, new Color(.065f, .078f, .071f));
            GUI.Label(new Rect(area.x + 16, area.y + 8, area.width - 32, 36), status, text);
            logScroll = GUI.BeginScrollView(new Rect(area.x + 12, area.y + 47, area.width - 24, 70), logScroll, new Rect(0, 0, area.width - 44, Mathf.Max(70, journal.Count * 21)));
            for (int i = 0; i < journal.Count; i++) GUI.Label(new Rect(4, i * 21, area.width - 55, 22), journal[i], small);
            GUI.EndScrollView();
        }
        private void HandleInput(Rect mapArea)
        {
            var e = Event.current; if (help || reviewOrders || Busy) return;
            if (e.type == EventType.ScrollWheel && mapArea.Contains(e.mousePosition)) { radius = Mathf.Clamp(radius - e.delta.y * 2, 20, 96); e.Use(); }
            if (e.type == EventType.MouseDrag && e.button == 2 && mapArea.Contains(e.mousePosition)) { pan += e.delta; e.Use(); }
            if (e.type == EventType.MouseDown && mapArea.Contains(e.mousePosition) && hovered >= 0)
            {
                inspected = hovered;
                if (e.button == 1 || mode == "Move") Queue(new Order(OrderKind.Move, selected, destination: hovered));
                else if (mode == "Hunt")
                {
                    var herd = observation.Herds.FirstOrDefault(h => h.Id == hoveredOccupant && !h.Domestic) ?? observation.Herds.FirstOrDefault(h => h.Tile == hovered && !h.Domestic);
                    int destination = planned.TryGetValue(selected, out var prior) && prior.Destination >= 0 ? prior.Destination : hovered;
                    if (herd == null) status = "Choose a currently visible wild herd."; else Queue(new Order(OrderKind.Hunt, selected, herd.Id, destination));
                }
                else if (mode == "Fish")
                {
                    int destination = planned.TryGetValue(selected, out var prior) ? prior.Destination : -1;
                    Queue(new Order(OrderKind.Fish, selected, hovered, destination));
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
                case KeyCode.Return:
                case KeyCode.KeypadEnter: EndTurn(); break;
                case KeyCode.M: mode = "Move"; break;
                case KeyCode.H: mode = "Hunt"; break;
                case KeyCode.F: mode = "Fish"; break;
                case KeyCode.C: FocusCamp(); break;
                case KeyCode.D: if (selected == observation.CampId) Queue(new Order(OrderKind.Dispatch, selected, population: dispatch)); break;
                case KeyCode.R: Queue(new Order(OrderKind.Reintegrate, selected, observation.CampId)); break;
                case KeyCode.Escape: mode = "Inspect"; break;
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
            if (world == null || Busy || reviewOrders || world.Finished) return;
            string error = order.Destination >= 0 ? PlanningPreview.MovementError(observation, order.Entity, order.Destination, WaterCost) : null;
            error = error ?? resolver.Validate(world, 0, order);
            if (error != null) { status = error; return; }
            string previous = planned.TryGetValue(order.Entity, out var old) ? "Replaced " + Describe(old) + " with " : "Queued ";
            planned[order.Entity] = order.Copy(); status = previous + Describe(order) + "."; mode = "Inspect";
        }
        private void EndTurn()
        {
            if (world == null || Busy || world.Finished || observation.LinkedPopulation == 0) return;
            reviewOrders = true; mode = "Inspect";
        }
        private void CommitTurn()
        {
            if (!reviewOrders || world == null || Busy || world.Finished || observation.LinkedPopulation == 0) return;
            reviewOrders = false;
            var orders = new Dictionary<int, List<Order>> { [0] = planned.Values.OrderBy(o => o.Entity).ToList() }; var traces = new List<DecisionTrace>();
            foreach (var camp in world.Camps.Where(c => c.Player != 0).OrderBy(c => c.Player))
            { var decision = policy.Decide(builder.Build(world, camp.Player), camp.Mandate); orders[camp.Player] = decision.Orders; traces.Add(decision.Trace); }
            resolving = Task.Run(() => resolver.Resolve(world, orders, traces));
        }
        private void FinishTurn(TurnRecord record)
        {
            var ownIds = new HashSet<int>(observation.Hunters.Select(h => h.Id)) { observation.CampId }; int turn = record.Turn;
            var oldHunters = observation.Hunters.ToArray();
            foreach (var ownOrder in planned.Values)
            {
                int location = ownOrder.Entity == observation.CampId ? world.Camps.First(c => c.Player == 0).Tile : world.Hunters.FirstOrDefault(h => h.Id == ownOrder.Entity)?.Tile ?? -1;
                if (location >= 0 && ownOrder.Destination >= 0 && location != ownOrder.Destination) journal.Add("Turn " + turn + ": movement for " + ownOrder.Entity + " to " + PlanningPreview.Place(observation, ownOrder.Destination) + " was blocked.");
                if (ownOrder.Kind == OrderKind.Hunt && !record.Transfers.Any(t => t.Entity == ownOrder.Entity && t.Kind == "Hunting" && t.Food > 0)) journal.Add("Turn " + turn + ": the hunt brought no food; the hunters gained practice.");
                if (ownOrder.Kind == OrderKind.Dispatch) journal.Add("Turn " + turn + ": " + ownOrder.Population + " people formed a hunter group.");
            }
            var losses = oldHunters.Where(h => !world.Hunters.Any(current => current.Id == h.Id) && -record.PopulationChanges.Where(c => c.Camp == observation.CampId && c.Entity == h.Id && c.Count < 0).Sum(c => c.Count) >= h.Population).ToArray();
            foreach (var h in losses) journal.Add("Turn " + turn + ": Hunters " + h.Id + " were lost; no members survived the generation.");
            planned.Clear(); Refresh();
            // Never show the authoritative global event log to the player: it contains hidden rival activity.
            var ownTransfers = record.Transfers.Where(t => ownIds.Contains(t.Entity)).ToArray();
            bool gathered = ownTransfers.Any(t => t.Kind.Contains("gathering") || t.Kind == "Gathering");
            bool hunted = ownTransfers.Any(t => t.Kind == "Hunting" && t.Food > 0), fished = ownTransfers.Any(t => t.Kind == "Fishing" && t.Food > 0);
            var player = world.Camps.First(c => c.Player == 0);
            int grown = record.PopulationChanges.Where(c => c.Camp == player.Id && c.Count > 0).Sum(c => c.Count);
            int died = -record.PopulationChanges.Where(c => c.Camp == player.Id && c.Count < 0).Sum(c => c.Count);
            var demography = record.Demographics.First(d => d.Camp == player.Id); journal.Add("Turn " + turn + ": " + demography.Explanation); journal.Add("Food: land " + demography.TerrestrialFood.ToString("F0") + ", passive marine " + demography.MarineFood.ToString("F0") + ", total received " + demography.FoodReceived.ToString("F0") + " / required " + demography.FoodRequired.ToString("F0"));
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
            if (losses.Length > 0) status = "Hunters lost: " + string.Join(", ", losses.Select(h => h.Id)) + ". See the generation report.";
            if (!world.Hunters.Any(h => h.Id == selected)) selected = player.Id;
        }
        private void Refresh() { observation = builder.Build(world, 0); if (!observation.Tiles.ContainsKey(inspected)) inspected = -1; }
        private void FocusCamp() { var camp = world.Camps.First(c => c.Player == 0); center = camp.Tile; selected = camp.Id; pan = Vector2.zero; mode = "Inspect"; }
        private void Save()
        {
            if (Busy) return;
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
            if (Busy) return;
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
                "@ Your camp (gold) / rival camp (red)   h Hunters\na Antelope   g Goats   b Bison   C Camels   H Horses   d Domestic herd\n. Grassland   T Woodland   & Jungle   : Desert\nNested contours: elevation 1-10; X Impassable peak; o Oasis; w Freshwater\nRivers follow tile edges. Sea tiles contain undrinkable saltwater.\n\nDots beneath glyphs show food from Depleted (one) to Abundant (five). Select a tile for food, water and renewal bands.\n\nLeft-click to inspect/select. Right-click or M to plan movement. Select hunters, then H to hunt or F to fish. To intercept a herd, queue a move to its predicted destination first and then choose Hunt and click the herd.\n\nD dispatches hunters; R reintegrates at camp. Enter opens the all-unit review; Enter again commits the generation for all tribes. Food and ecology advance through seasons within each turn. Escape cancels targeting or closes the review without clearing queued orders.\n\nWheel zooms; middle-drag or arrows pan; C centers camp.\nYour camp gathers within one tile. A connected hunter group shares camp food; carried food is deposited on return. Hunters outside a traversable supply path carry and eat their own finite provisions. V switches tilesets. Nested hex outlines count elevation levels 1-10; X marks impassable peaks. Units occupy separate slots; click a slot or use the inspector stack list. Hold still on a visible tile for two seconds for details. Saltwater is undrinkable. Wild herd labels are estimates: fewer than 10, rounded tens (~40), then broad hundreds (100+). Your own livestock counts are exact.";
            float contentHeight = text.CalcHeight(new GUIContent(helpText), width - 62);
            helpScroll = GUI.BeginScrollView(new Rect(r.x + 20, r.y + 62, width - 40, r.height - 120), helpScroll, new Rect(0, 0, width - 62, contentHeight));
            GUI.Label(new Rect(0, 0, width - 62, contentHeight), helpText, text);
            GUI.EndScrollView();
            if (Button(new Rect(r.x + 20, r.yMax - 45, width - 40, 32), "Return to the land")) help = false;
        }
        private string Describe(Order order) => PlanningPreview.Describe(observation, order);
        private static string TechName(Technology tech) => tech == Technology.AnimalHusbandry ? "Animal Husbandry" : tech == Technology.Cultivation ? "Agriculture / Cultivation" : "Watercraft and Fisheries";
        private bool Button(Rect rect, string label, bool accent = false) { var previous = GUI.backgroundColor; GUI.backgroundColor = accent ? new Color(.55f, .49f, .26f) : new Color(.32f, .37f, .32f); bool clicked = GUI.Button(rect, label, button); GUI.backgroundColor = previous; return clicked; }
        private static void Fill(Rect rect, Color color) { var before = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = before; }
        private static void Tint(Rect rect, Texture texture, Color color) { var before = GUI.color; GUI.color = color; GUI.DrawTexture(rect, texture); GUI.color = before; }
    }
}
