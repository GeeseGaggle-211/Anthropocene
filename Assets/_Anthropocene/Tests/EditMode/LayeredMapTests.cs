using System.Collections.Generic;
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
            var tile = new Tile { Id = id, Environment = EnvironmentKind.Shallows, WaterBody = WaterKind.Saltwater, ElevationLevel = 0, NaturalVegetationPotential = VegetationTier.High, CurrentVegetation = VegetationTier.High, Food = 90, PotentialFood = 360 };
            for (int s = 0; s < 4; s++) tile.Seasons[s] = new SeasonData { MaxFood = 100, MaxPotentialFood = 400, MaxSaltwater = 100, FoodRecoveryPerDay = .01f, BiomassRecoveryPerDay = .01f };
            world.Tiles[id] = tile;
            var observation = new AgentObservationBuilder().Build(world, world.Camps[0].Player);
            foreach (float zoom in new[] { 35f, 80f, 160f }) Assert.That(LayeredTileRenderer.VegetationCount(observation.Tiles[id], zoom), Is.Zero);
            string details = TileDetails.Describe(observation, id, new Order[0]);
            Assert.That(details, Does.Contain("PLANKTON / ALGAE · High"));
            Assert.That(details, Does.Contain("AQUATIC BIOMASS"));
            Assert.That(details, Does.Contain("25% human-gatherable"));
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
        public void CoverChangesDensityAndClimateMotifsWhileBiomeOwnsGroundAtEveryRelief()
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
                foreach (var relief in new[] { Relief.Lowland, Relief.Hills, Relief.Highland, Relief.Mountain })
                {
                    t.Relief = relief;
                    Assert.That(LayeredTileRenderer.BaseKey(Observe()), Is.EqualTo("terrain." + environment.ToString().ToLowerInvariant()), "Elevation must not replace biome ground with mountain artwork.");
                }
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

        [Test]
        public void CultivationDisplacementChangesOnlyStandingVegetationService()
        {
            var cfg = new SimulationConfig();
            var tile = new Tile { Id = 7, Terrain = Terrain.Grassland, Environment = EnvironmentKind.Temperate, NaturalVegetationPotential = VegetationTier.High, CurrentVegetation = VegetationTier.High };
            TileObservation ObserveAt(double q)
            {
                var ecology = new EcologyView(tile) { CultivationCapital = System.Math.Pow(q / cfg.ReferenceIntensity, 2) };
                return new TileObservation(tile, tile.Seasons[0], cfg, new RiverEdge[0], ecology);
            }

            var retained = ObserveAt(.5);
            Assert.That(retained.CurrentVegetation, Is.EqualTo(VegetationTier.High), "Cultivation must not overwrite raw ecological vegetation.");
            Assert.That(retained.NaturalVegetationRetention, Is.EqualTo(1).Within(1e-10));
            Assert.That(LayeredTileRenderer.VegetationCount(retained, 80), Is.EqualTo(3), "At q=.5, High vegetation keeps its normal three-tree presentation.");

            var displaced = ObserveAt(1);
            Assert.That(displaced.CurrentVegetation, Is.EqualTo(VegetationTier.High), "Raw ecological vegetation remains available for succession and clearing labor.");
            Assert.That(displaced.NaturalVegetationRetention, Is.EqualTo(0).Within(1e-10));
            Assert.That(displaced.EffectiveNaturalVegetationService, Is.EqualTo(0).Within(1e-10));
            Assert.That(LayeredTileRenderer.VegetationCount(displaced, 80), Is.Zero, "A fully cultivated field has no standing natural vegetation sprites.");
        }

        [Test]
        public void ContinuousContours_AreNestedAcrossDensityAndZoomWithoutHiddenInputs()
        {
            var map = new HexMap(10, 7);
            var heights = new Dictionary<int, float>();
            var included = new HashSet<int>();
            foreach (int id in map.Radius(map.At(5, 3), 2))
            {
                int q = id % map.Width, r = id / map.Width;
                heights[id] = .451f + q * .0143f + r * .0081f;
                included.Add(id);
            }
            var normal = TopographicContours.Build(map, heights, included, ContourDensity.Normal, 60);
            var fine = TopographicContours.Build(map, heights, included, ContourDensity.Fine, 60);
            var distant = TopographicContours.Build(map, heights, included, ContourDensity.Fine, 20);
            string Key(ContourSegment s) => s.LevelUnits + ":" + s.Start.GetHashCode() + ":" + s.End.GetHashCode();
            var fineKeys = new HashSet<string>(fine.Select(Key));
            var normalKeys = new HashSet<string>(normal.Select(Key));
            Assert.That(normal.Count, Is.GreaterThan(0));
            Assert.That(fine.Count, Is.GreaterThan(normal.Count));
            Assert.That(normal.All(s => fineKeys.Contains(Key(s))), Is.True, "Fine adds levels but cannot move Normal contours.");
            Assert.That(distant.All(s => normalKeys.Contains(Key(s))), Is.True, "Distant zoom retains a fixed subset.");
            Assert.That(fine.All(s => included.Contains(s.Start.TileA) && included.Contains(s.Start.TileB) && included.Contains(s.End.TileA) && included.Contains(s.End.TileB)), Is.True, "No contour references unknown geography.");
        }

        [Test]
        public void MapLayerSwitching_IsPresentationOnlyAndRuggednessUsesOneScale()
        {
            var world = new WorldGenerator().Generate(64, SimulationConfig.Small(2));
            string fingerprint = SaveCodec.Fingerprint(world);
            var observation = new AgentObservationBuilder().Build(world, 0);
            var terrain = KnownTerrainAnalysis.Build(observation);
            float score = terrain.At(observation.CampTile).Ruggedness;
            int rivers = observation.Tiles.Values.Sum(t => t.Rivers.Count);
            int occupants = observation.Tiles.Keys.Sum(tile => TileDetails.Occupants(observation, tile).Length);
            var state = new MapLayerState();
            state.Select(PlayerMapLayer.Topography);
            state.ToggleDensity();
            Assert.That(state.Layer, Is.EqualTo(PlayerMapLayer.Topography));
            Assert.That(state.Density, Is.EqualTo(ContourDensity.Fine));
            Assert.That(state.ShowsContinuousContours, Is.True);
            state.Select(PlayerMapLayer.Ruggedness);
            state.ToggleRuggednessContours();
            Assert.That(state.ShowsContinuousContours, Is.False);
            state.Select(PlayerMapLayer.Terrain);
            Assert.That(terrain.At(observation.CampTile).Ruggedness, Is.EqualTo(score));
            Assert.That(observation.Tiles.Values.Sum(t => t.Rivers.Count), Is.EqualTo(rivers));
            Assert.That(observation.Tiles.Keys.Sum(tile => TileDetails.Occupants(observation, tile).Length), Is.EqualTo(occupants));
            Assert.That(SaveCodec.Fingerprint(world), Is.EqualTo(fingerprint));
            Assert.That(TopographicOverlay.RuggednessColor(0), Is.Not.EqualTo(TopographicOverlay.RuggednessColor(50)));
            Assert.That(TopographicOverlay.RuggednessColor(50), Is.Not.EqualTo(TopographicOverlay.RuggednessColor(100)));
            Assert.That(TopographicOverlay.Hillshade(terrain.At(observation.CampTile)), Is.InRange(-.11f, .09f));
        }
    }
}
