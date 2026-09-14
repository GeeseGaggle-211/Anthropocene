using System.Linq;
using Anthropocene.Presentation;
using Anthropocene.Simulation;
using NUnit.Framework;
using UnityEngine;
using Terrain = Anthropocene.Simulation.Terrain;

namespace Anthropocene.Tests.EditMode
{
    public sealed class LayeredMapTests
    {
        [Test]
        public void AquaticPlankton_HasWaterLabelsAndNeverDrawsLandTrees()
        {
            var world = new WorldGenerator().Generate(83, SimulationConfig.Small(2));
            int id = world.Map.Neighbors(world.Camps[0].Tile).First();
            var tile = new Tile { Id = id, Environment = EnvironmentKind.Shallows, WaterBody = WaterKind.Saltwater, ElevationLevel = 0, NaturalVegetationPotential = VegetationTier.High, CurrentVegetation = VegetationTier.High, Food = 90, PotentialFood = 180 };
            for (int s = 0; s < 4; s++) tile.Seasons[s] = new SeasonData { MaxFood = 100, MaxPotentialFood = 200, MaxSaltwater = 100, FoodRecoveryPerDay = .01f, BiomassRecoveryPerDay = .01f };
            world.Tiles[id] = tile;
            var observation = new AgentObservationBuilder().Build(world, world.Camps[0].Player);
            foreach (float zoom in new[] { 35f, 80f, 160f }) Assert.That(LayeredTileRenderer.VegetationCount(observation.Tiles[id], zoom), Is.Zero);
            string details = TileDetails.Describe(observation, id, new Order[0]);
            Assert.That(details, Does.Contain("PLANKTON / ALGAE · High"));
            Assert.That(details, Does.Contain("AQUATIC BIOMASS"));
            Assert.That(details, Does.Contain("50% human-gatherable"));
            Assert.That(details, Does.Not.Contain("HERD FORAGE"));
            Assert.That(details, Does.Not.Contain("soil moisture"));
        }
        [Test]
        public void DesertRiver_UsesDesertGroundOneTreeAndRiparianLabel()
        {
            var cfg = new SimulationConfig();
            var tile = new Tile { Environment = EnvironmentKind.Desert, NaturalVegetationPotential = VegetationTier.Medium, CurrentVegetation = VegetationTier.Medium, ElevationLevel = 1, FreshwaterSource = FreshwaterSource.River, RiverEdges = new[] { 0, -1, -1, -1, -1, -1 } };
            var river = new RiverEdge { Id = 0, Flow = 280, Width = 3, TileA = 0, SideA = 0 };
            var observed = new TileObservation(tile, tile.Seasons[0], cfg, new[] { river });
            Assert.That(LayeredTileRenderer.BaseKey(observed), Is.EqualTo("terrain.desert"));
            Assert.That(LayeredTileRenderer.VegetationKey(observed), Is.EqualTo("vegetation.tropical_tree"), "The existing palm silhouette represents dry river vegetation too.");
            Assert.That(LayeredTileRenderer.VegetationCount(observed, 80), Is.EqualTo(1));
            Assert.That(observed.Rivers.Count, Is.EqualTo(1));
            Assert.That(Vegetation.Label(observed.Environment, observed.CurrentVegetation, observed.River), Is.EqualTo("Riparian vegetation"));
            foreach (var set in TilesetCatalog.Load())
                Assert.That(set.Find(LayeredTileRenderer.BaseKey(observed)).Key, Is.EqualTo("terrain.desert"));
        }
        [Test]
        public void StackedSprites_RemainInsideTheirExactHitSlotsAtEveryZoom()
        {
            foreach (float height in new[] { 30f, 80f, 115f })
                for (int count = 1; count <= 6; count++)
                    for (int i = 0; i < count; i++)
                    {
                        var slot = LayeredTileRenderer.OccupantSlot(new Rect(0, 0, height * .86f, height), i, count);
                        var art = LayeredTileRenderer.SpriteBounds(slot, 1.8f);
                        Assert.That(art.xMin, Is.GreaterThanOrEqualTo(slot.xMin));
                        Assert.That(art.yMin, Is.GreaterThanOrEqualTo(slot.yMin));
                        Assert.That(art.xMax, Is.LessThanOrEqualTo(slot.xMax));
                        Assert.That(art.yMax, Is.LessThanOrEqualTo(slot.yMax));
                        Assert.That(art.width, Is.GreaterThan(0));
                    }
        }
        [Test]
        public void SourceSprites_AreImportedAtThePromisedSizeAndBothTilesetsResolveThem()
        {
            var sprites = Resources.LoadAll<Sprite>("MapArt");
            Assert.That(sprites.Length, Is.EqualTo(29));
            Assert.That(sprites.All(s => s.texture.width == 256 && s.texture.height == 256), Is.True);
            Assert.That(sprites.All(s => s.texture.mipmapCount > 1 && s.texture.filterMode == FilterMode.Trilinear), Is.True, "Zoomed artwork must be filtered instead of dropping thin outline pixels.");
            var texture = new Texture2D(2, 2);
            try
            {
                foreach (var set in TilesetCatalog.Load())
                {
                    var renderer = new LayeredTileRenderer(set, texture, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                    foreach (string key in new[] { "terrain.desert", "terrain.temperate", "terrain.tropical", "terrain.highland", "terrain.lake", "vegetation.conifer", "feature.cultivation", "feature.cultivation_heavy", "feature.freshwater", "feature.oasis", "settlement.camp", "settlement.village", "entity.camp", "entity.hunter", "entity.domestic", "species.camel" }) Assert.That(renderer.HasSprite(key), Is.True, key);
                    Assert.That(renderer.HasSprite("missing.art"), Is.False);
                    Assert.That(set.Find("missing.art").Key, Is.EqualTo("fallback"));
                }
            }
            finally { Object.DestroyImmediate(texture); }
        }
        [Test]
        public void HerdSpeciesHaveDistinctArtAndColouredSpritesKeepTheirPalette()
        {
            foreach (var set in TilesetCatalog.Load())
            {
                var paths = new[] { "antelope", "goat", "bison", "camel", "horse" }.Select(s => set.Find("species." + s).SpritePath).ToArray();
                Assert.That(paths.Distinct().Count(), Is.EqualTo(paths.Length), "Players must be able to distinguish prey species by shape.");
                Assert.That(set.Find("entity.camp").SpriteTint, Is.EqualTo("#FFFFFF"));
                Assert.That(set.Find("vegetation.conifer").SpriteTint, Is.EqualTo("#FFFFFF"));
                Assert.That(set.Find("entity.rivalcamp").SpriteTint, Is.Not.EqualTo(set.Find("entity.camp").SpriteTint));
            }
        }
        [Test]
        public void CoverChangesDensityAndClimateMotifsWhileReliefKeepsItsOwnBase()
        {
            var cfg = new SimulationConfig();
            var t = new Tile { Terrain = Terrain.Woodland, Relief = Relief.Highland, ElevationLevel = 8 };
            TileObservation Observe() => new TileObservation(t, t.Seasons[0], cfg, new RiverEdge[0]);
            var highland = Observe(); string ground = LayeredTileRenderer.BaseKey(highland);
            t.CurrentVegetation = VegetationTier.Low; var sparse = Observe();
            t.CurrentVegetation = VegetationTier.High; var dense = Observe();
            Assert.That(LayeredTileRenderer.BaseKey(sparse), Is.EqualTo(ground));
            Assert.That(LayeredTileRenderer.BaseKey(dense), Is.EqualTo(ground));
            Assert.That(LayeredTileRenderer.VegetationKey(sparse), Is.EqualTo("vegetation.grass"));
            foreach (var environment in new[] { EnvironmentKind.Desert, EnvironmentKind.Grassland, EnvironmentKind.Temperate, EnvironmentKind.Tropical, EnvironmentKind.Cold })
            {
                t.Environment = environment;
                foreach (float height in new[] { 42f, 64f, 80f, 112f })
                {
                    t.CurrentVegetation = VegetationTier.Low;
                    Assert.That(LayeredTileRenderer.VegetationKey(Observe()), Is.EqualTo(environment == EnvironmentKind.Desert ? "vegetation.shrub" : "vegetation.grass"), "Low cover must contain no trees.");
                    foreach (var tier in new[] { VegetationTier.Medium, VegetationTier.High })
                    {
                        t.CurrentVegetation = tier;
                        Assert.That(new[] { "vegetation.temperate_tree", "vegetation.tropical_tree", "vegetation.conifer" }.Contains(LayeredTileRenderer.VegetationKey(Observe())), Is.True);
                        Assert.That(LayeredTileRenderer.VegetationCount(Observe(), height), Is.EqualTo(tier == VegetationTier.Medium ? 1 : 3), "Tree counts must not change between detail zoom levels.");
                    }
                }
            }
            Assert.That(LayeredTileRenderer.VegetationCount(dense, 30), Is.Zero);
            Assert.That(LayeredTileRenderer.ContourCount(8, 30), Is.GreaterThan(0));
            t.Environment = EnvironmentKind.Cold; Assert.That(LayeredTileRenderer.VegetationKey(Observe()), Is.EqualTo("vegetation.conifer"));
            t.Environment = EnvironmentKind.Tropical; Assert.That(LayeredTileRenderer.VegetationKey(Observe()), Is.EqualTo("vegetation.tropical_tree"));
        }
    }
}
