using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;
using NUnit.Framework;
using Anthropocene.Presentation;
using UnityEngine;
using System.Reflection;
using System.Threading.Tasks;

namespace Anthropocene.Tests.EditMode
{
    public sealed class SimulationTests
    {
        private static WorldState PlanningWorld(int hunterQ = 6, int supply = 3)
        {
            var w = new WorldState { Config = new SimulationConfig { Width = 24, Height = 12, Players = 1, SupplyRange = supply } };
            w.Tiles = Enumerable.Range(0, 288).Select(id => new Tile { Id = id, Terrain = Anthropocene.Simulation.Terrain.Grassland, ElevationLevel = 1 }).ToArray();
            w.Camps.Add(new Camp { Id = 1, Player = 0, Tile = w.Map.At(4, 4), Population = 24 });
            w.Hunters.Add(new Hunter { Id = 2, Player = 0, SourceCamp = 1, Tile = w.Map.At(hunterQ, 4), Population = 8 });
            return w;
        }
        [Test]
        public void PlannedSupply_UsesBothDestinations_AndDoesNotPromiseSurvival()
        {
            var w = PlanningWorld(); var o = new AgentObservationBuilder().Build(w, 0);
            var orders = new List<Order> { new Order(OrderKind.Move, 2, destination: w.Map.At(7, 4)) };
            Assert.That(PlanningPreview.SupplyState(o, 2, orders), Is.EqualTo("visible supply path"));
            orders.Add(new Order(OrderKind.Move, 1, destination: w.Map.At(3, 4)));
            Assert.That(PlanningPreview.SupplyState(o, 2, orders), Does.StartWith("outside supply"));
            orders[1].Destination = w.Map.At(5, 4);
            Assert.That(PlanningPreview.SupplyState(o, 2, orders), Is.EqualTo("visible supply path"));
            string connected = PlanningPreview.SupplyText(o, o.Hunters[0], orders);
            Assert.That(connected, Does.Contain("Supply path now: connected"));
            Assert.That(connected, Does.Contain("Shared camp reserves"));
            Assert.That(connected, Does.Contain("Carried provisions: Depleted"));
            Assert.That(connected, Does.Contain("does not mean this group is unfed"));
            orders[1].Destination = w.Map.At(3, 4);
            Assert.That(PlanningPreview.SupplyText(o, o.Hunters[0], orders), Does.Contain("can starve"));
            Assert.That(w.Camps[0].Tile, Is.EqualTo(w.Map.At(4, 4)));
        }
        [Test]
        public void FogSupply_IsUncertain_AndHiddenGeographyCannotChangePreview()
        {
            var w = PlanningWorld(10, 4); var o = new AgentObservationBuilder().Build(w, 0);
            var orders = new[] { new Order(OrderKind.Move, 1, destination: w.Map.At(5, 4)), new Order(OrderKind.Move, 2, destination: w.Map.At(9, 4)) };
            Assert.That(o.Tiles.ContainsKey(w.Map.At(7, 4)), Is.False);
            string before = PlanningPreview.SupplyState(o, 2, orders);
            Assert.That(before, Does.StartWith("uncertain supply"));
            foreach (var tile in w.Tiles.Where(t => !o.Tiles.ContainsKey(t.Id))) { tile.Terrain = Anthropocene.Simulation.Terrain.Ocean; tile.ElevationLevel = 10; }
            Assert.That(PlanningPreview.SupplyState(new AgentObservationBuilder().Build(w, 0), 2, orders), Is.EqualTo(before));
        }
        [Test]
        public void MovementPreview_MatchesValidation_AndDistinguishesTerrainAndRange()
        {
            var w = PlanningWorld(); int peak = w.Map.At(5, 4), sea = w.Map.At(4, 5);
            w.Tiles[peak].ElevationLevel = 10; w.Tiles[sea].Terrain = Anthropocene.Simulation.Terrain.Shallows;
            var o = new AgentObservationBuilder().Build(w, 0); var resolver = new TurnResolver();
            Assert.That(PlanningPreview.MovementError(o, 1, peak, 2), Does.Contain("peak"));
            Assert.That(PlanningPreview.MovementError(o, 1, sea, 2), Does.Contain("Sea"));
            Assert.That(PlanningPreview.MovementError(o, 1, w.Map.At(6, 4), 2), Does.Contain("range"));
            Assert.That(PlanningPreview.MovementError(o, 1, w.Map.At(6, 4), 2), Does.Contain("allowance 1"));
            foreach (int id in new[] { 1, 2 }) foreach (int tile in o.Tiles.Keys)
                Assert.That(PlanningPreview.Path(o, id, tile, 2).Length > 0, Is.EqualTo(resolver.Validate(w, 0, new Order(OrderKind.Move, id, destination: tile)) == null));
            w.Camps[0].Technologies[(int)Technology.Watercraft] = true;
            o = new AgentObservationBuilder().Build(w, 0);
            foreach (int tile in o.Tiles.Keys)
                Assert.That(PlanningPreview.Path(o, 2, tile, 2).Length > 0, Is.EqualTo(resolver.Validate(w, 0, new Order(OrderKind.Move, 2, destination: tile)) == null));
        }
        [Test]
        public void DispatchAndReplacementDescriptions_ExposeTimingHeadcountAndCoordinates()
        {
            var o = new AgentObservationBuilder().Build(PlanningWorld(), 0);
            string dispatch = PlanningPreview.Describe(o, new Order(OrderKind.Dispatch, 1, population: 8));
            Assert.That(dispatch, Does.Contain("24 -> 16")); Assert.That(dispatch, Does.Contain("orders next turn"));
            string a = PlanningPreview.Describe(o, new Order(OrderKind.Move, 2, destination: o.Map.At(7, 4)));
            string b = PlanningPreview.Describe(o, new Order(OrderKind.Move, 2, destination: o.Map.At(5, 4)));
            Assert.That(a, Does.Contain("(7, 4)")); Assert.That(b, Does.Contain("(5, 4)")); Assert.That(a, Is.Not.EqualTo(b));
        }
        private static FieldInfo GameField(string name) => typeof(OpeningGame).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        private static void GameCall(OpeningGame game, string name) => typeof(OpeningGame).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        [Test]
        public void CommitAndEndTurn_RejectDuplicateCallsWhileWorkerOwnsState()
        {
            var go = new GameObject("planning guard test"); var game = go.AddComponent<OpeningGame>();
            try
            {
                var w = PlanningWorld(); GameField("world").SetValue(game, w); GameField("observation").SetValue(game, new AgentObservationBuilder().Build(w, 0));
                GameCall(game, "EndTurn"); Assert.That(GameField("reviewOrders").GetValue(game), Is.True);
                var task = new TaskCompletionSource<TurnRecord>().Task; GameField("resolving").SetValue(game, task);
                GameCall(game, "CommitTurn"); GameCall(game, "EndTurn");
                Assert.That(GameField("resolving").GetValue(game), Is.SameAs(task)); Assert.That(w.Turn, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void Escape_CancelsTargetingBeforeControls_AndKeepsOrders()
        {
            var go = new GameObject("Escape integration test"); var game = go.AddComponent<OpeningGame>();
            try
            {
                var w = PlanningWorld(); GameField("world").SetValue(game, w); GameField("observation").SetValue(game, new AgentObservationBuilder().Build(w, 0));
                GameField("menu").SetValue(game, false); GameField("selected").SetValue(game, 1);
                var orders = (Dictionary<int, Order>)GameField("planned").GetValue(game); orders[1] = new Order(OrderKind.Move, 1, destination: w.Map.At(3, 4));
                foreach (string mode in new[] { "Move", "Hunt", "Fish" })
                {
                    GameField("mode").SetValue(game, mode);
                    var escape = new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape };
                    typeof(OpeningGame).GetMethod("HandleEarlyInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { escape });
                    Assert.That(GameField("mode").GetValue(game), Is.EqualTo("Inspect")); Assert.That(orders.Count, Is.EqualTo(1));
                    Assert.That(GameField("status").GetValue(game), Does.Contain("cancelled"));
                }
                GameField("reviewOrders").SetValue(game, true);
                typeof(OpeningGame).GetMethod("HandleEarlyInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape } });
                Assert.That(GameField("reviewOrders").GetValue(game), Is.False);
                Assert.That(GameField("status").GetValue(game), Does.Contain("review closed"));
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void ElevationContours_AndStackSlots_PreserveCountsAndHitGeometry()
        {
            var bounds = new Rect(0, 0, 110, 128);
            for (int level = 0; level <= 10; level++)
            {
                Assert.That(LayeredTileRenderer.ContourCount(level, 100), Is.EqualTo(level));
                if (level > 0) Assert.That(LayeredTileRenderer.ContourCount(level, 35), Is.GreaterThanOrEqualTo(LayeredTileRenderer.ContourCount(level - 1, 35)));
            }
            for (int i = 0; i < 6; i++)
                for (int j = i + 1; j < 6; j++) Assert.That(LayeredTileRenderer.OccupantSlot(bounds, i, 7).Overlaps(LayeredTileRenderer.OccupantSlot(bounds, j, 7)), Is.False);
            foreach (var set in TilesetCatalog.Load())
            {
                Assert.That(set.Find("elevation.contours").Opacity, Is.InRange(.1f, .9f));
                Assert.That(set.Find("elevation.10").Key, Is.EqualTo("elevation.10"));
            }
        }
        [Test]
        public void HoverPanel_FlipsAndClampsBesideTheTile()
        {
            var screen = new Rect(0, 0, 1280, 800); var tile = new Rect(1160, 700, 90, 90);
            var panel = HoverPanelLayout.Place(tile, new Vector2(450, 550), screen);
            Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(0)); Assert.That(panel.yMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(panel.xMax, Is.LessThanOrEqualTo(screen.xMax)); Assert.That(panel.yMax, Is.LessThanOrEqualTo(screen.yMax)); Assert.That(panel.Overlaps(tile), Is.False);
        }
        [Test]
        public void Tilesets_AreLoadedBySemanticKeys_AndDoNotMutateSimulation()
        {
            var sets = TilesetCatalog.Load();
            Assert.That(sets.Length, Is.GreaterThanOrEqualTo(2));
            foreach (var tileset in sets)
                foreach (string key in new[] { "biome.desert", "relief.hills", "feature.river", "feature.oasis", "feature.freshwater", "species.camel", "species.horse" })
                    Assert.That(tileset.Find(key).Key, Is.EqualTo(key));
            var world = new WorldGenerator().Generate(8, SimulationConfig.Small(2)); var before = SaveCodec.Fingerprint(world);
            var texture = new Texture2D(2, 2);
            try { foreach (var tileset in sets) { ITileRenderer renderer = new LayeredTileRenderer(tileset, texture, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")); Assert.That(renderer, Is.Not.Null); } }
            finally { Object.DestroyImmediate(texture); }
            Assert.That(SaveCodec.Fingerprint(world), Is.EqualTo(before));
        }
        [Test]
        public void Configuration_ContainsStableSpeciesAndFreshwaterTuning()
        {
            var config = new SimulationConfig(); JsonUtility.FromJsonOverwrite(Resources.Load<TextAsset>("OpeningConfig").text, config); config.Validate();
            Assert.That(config.Version, Is.EqualTo(3)); Assert.That(config.GetSpecies("camel").FreshwaterConsumption, Is.GreaterThan(0));
            Assert.That(config.GetSpecies("horse").FoodYield, Is.GreaterThan(0));
            Assert.That(config.StartFreshwaterMinimum, Is.GreaterThan(0));
        }
        [Test]
        public void Map_UsesCylindricalHexDistance()
        {
            var map = new HexMap(24, 16);
            Assert.That(map.Distance(map.At(0, 5), map.At(23, 5)), Is.EqualTo(1));
            Assert.That(map.Radius(map.At(5, 5), 2).Count(), Is.EqualTo(19));
            Assert.That(map.At(5, -1), Is.EqualTo(-1));
        }
        [TestCase(3)]
        [TestCase(17)]
        [TestCase(51)]
        public void Tribes_PlayOpeningWithLegalOrdersAndDeterministicReplay(int seed)
        {
            var world = new WorldGenerator().Generate(seed, SimulationConfig.Small());
            var copy = SaveCodec.Load(SaveCodec.Save(world));
            var policy = new HeuristicTribePolicy(); var builder = new AgentObservationBuilder(); var resolver = new TurnResolver();
            while (!world.Finished && world.Turn <= 12)
            {
                var orders = new Dictionary<int, List<Order>>();
                foreach (var camp in world.Camps)
                {
                    orders[camp.Player] = policy.Decide(builder.Build(world, camp.Player), camp.Mandate).Orders;
                    foreach (var order in orders[camp.Player]) Assert.That(resolver.Validate(world, camp.Player, order), Is.Null);
                }
                resolver.Resolve(world, orders); copy.Herds.Reverse(); copy.Camps.Reverse(); resolver.Resolve(copy, orders);
                Assert.That(SaveCodec.Fingerprint(copy), Is.EqualTo(SaveCodec.Fingerprint(world)));
            }
            Assert.That(world.Finished, Is.True);
            Assert.That(world.Camps.All(c => c.TransitionTurn <= 12), Is.True);
        }
        [Test]
        public void Observation_DoesNotRevealHiddenChanges()
        {
            var world = new WorldGenerator().Generate(19, SimulationConfig.Small());
            var builder = new AgentObservationBuilder(); var policy = new HeuristicTribePolicy();
            var before = policy.Decide(builder.Build(world, 0), world.Camps[0].Mandate).Orders.Select(o => o.ToString()).ToArray();
            var visible = Knowledge.Visible(world, 0);
            foreach (var tile in world.Tiles.Where(t => !visible.Contains(t.Id))) tile.Food = 12345;
            var after = policy.Decide(builder.Build(world, 0), world.Camps[0].Mandate).Orders.Select(o => o.ToString()).ToArray();
            Assert.That(after, Is.EqualTo(before));
        }
        [Test]
        public void ResourceCompetition_IsIndependentOfRequestOrder()
        {
            var policy = new ProportionalAllocation(); var tiles = new[] { new Tile { Food = 5, Freshwater = 4, FreshwaterSource = FreshwaterSource.RainPool } };
            var requests = new[] { new Demand { Entity = 1, Tile = 0, Kind = "Gathering", Food = 7 }, new Demand { Entity = 2, Tile = 0, Kind = "Herd", Food = 3, Freshwater = 9 } };
            var result = policy.Allocate(requests, tiles); var reverse = policy.Allocate(requests.Reverse(), tiles);
            Assert.That(result.Select(t => t.Food), Is.EqualTo(reverse.Select(t => t.Food)));
            Assert.That(result.Sum(t => t.Food), Is.EqualTo(5).Within(1e-9));
            Assert.That(result.Sum(t => t.Freshwater), Is.EqualTo(4).Within(1e-9));
        }
    }
}
