using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;
using NUnit.Framework;
using Anthropocene.Presentation;
using UnityEngine;

namespace Anthropocene.Tests.EditMode
{
    public sealed class SimulationTests
    {
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
