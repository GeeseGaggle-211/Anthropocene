using System.Linq;
using Anthropocene.Presentation;
using Anthropocene.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Anthropocene.Tests.EditMode
{
    public sealed class ResourcePreviewTests
    {
        [Test]
        public void SharedDepositRaster_ConservesMaterialAndUsesSavedExposureAndAccess()
        {
            var world = new WorldState { Tiles = new[] { new Tile(), new Tile() } };
            world.Resources = new WorldResourceState { Stone = new ushort[2], Clay = new ushort[2], Wood = new ushort[2],
                Bodies = new[] { new DepositBody { Quantity = 100000, Components = new[] { new DepositComponent(MaterialId.Copper, 6000) },
                    Sites = new[] { new DepositSite { Tile = 0, Share = 2500, Exposure = 10000 }, new DepositSite { Tile = 1, Share = 7500, Exposure = 2000 } } } } };
            world.Resources.AccessibleParts[(int)MaterialId.Copper] = 500;
            Assert.That(WorldResourcePreview.Values(world, MaterialId.Copper, false), Is.EqualTo(new[] { 15000.0, 45000.0 }));
            Assert.That(WorldResourcePreview.Values(world, MaterialId.Copper, true), Is.EqualTo(new[] { 750.0, 450.0 }));
        }

        [Test]
        public void AllResourceViews_ArePureAndWoodIsQuarterAccessible()
        {
            var world = new WorldGenerator().Generate(42, SimulationConfig.Small(2));
            string before = SaveCodec.Fingerprint(world);
            var observation = new AgentObservationBuilder().Build(world, 0); var visible = observation.Tiles.Keys.OrderBy(x => x).ToArray();
            foreach (var material in ResourceCatalog.Materials.Where(m => !m.Reserved))
            {
                var totals = WorldResourcePreview.Values(world, material.Id, false);
                var accessible = WorldResourcePreview.Values(world, material.Id, true);
                double expected = world.Tiles.Sum(t => world.Resources.SurfaceTotal(t.Id, material.Id)) + world.Resources.Bodies.Sum(b => world.Resources.ComponentQuantity(b, material.Id));
                Assert.That(totals.Sum(), Is.EqualTo(expected).Within(.0001), material.Name);
                Assert.That(accessible.Zip(totals, (a, t) => a <= t && a >= 0).All(x => x), Is.True, material.Name);
                WorldResourcePreview.Describe(world, world.Camps[0].Tile, material.Id);
                if (material.Id == MaterialId.Wood) Assert.That(accessible.Sum(), Is.EqualTo(totals.Sum() * .25).Within(.0001));
            }
            Assert.That(SaveCodec.Fingerprint(world), Is.EqualTo(before));
            Assert.That(new AgentObservationBuilder().Build(world, 0).Tiles.Keys.OrderBy(x => x), Is.EqualTo(visible));
        }

        [Test]
        public void AbsentLegacyLayer_IsExplicitRatherThanReportedAsAnEmptyResourceWorld()
        {
            var world = new WorldState { Tiles = new[] { new Tile() } };
            Assert.That(WorldResourcePreview.Values(world, MaterialId.Copper, false), Is.EqualTo(new[] { 0.0 }));
            Assert.That(WorldResourcePreview.Describe(world, 0, MaterialId.Copper), Does.Contain("no geological resource layer"));
        }

        [Test]
        public void TraceMaterials_AreVisibleAndAmountsAboveLegendMaximumSaturate()
        {
            var terrain = Color.blue;
            Assert.That(WorldResourcePreview.ColorFor(.001, terrain), Is.Not.EqualTo(WorldResourcePreview.ColorFor(0, terrain)));
            Assert.That(WorldResourcePreview.ColorFor(100000, terrain), Is.EqualTo(WorldResourcePreview.ColorFor(200000, terrain)));
        }
    }
}
