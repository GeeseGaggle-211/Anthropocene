#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public sealed partial class OpeningGame
    {
        // Only the opt-in smoke run uses synthetic fixtures. They never enter world state.
        private int artReviewMode;
        private static TileRenderContext ArtTile(Rect rect, EnvironmentKind environment = EnvironmentKind.Grassland, VegetationTier cover = VegetationTier.Low, int level = 2, params string[] occupants)
        {
            var t = new Tile { Id = 43, Environment = environment, CurrentVegetation = cover, NaturalVegetationPotential = cover, ElevationLevel = level,
                Relief = level >= 8 ? Relief.Highland : Relief.Lowland, Food = 45, PotentialFood = 200 };
            if (environment == EnvironmentKind.Ocean || environment == EnvironmentKind.Shallows) { t.WaterBody = WaterKind.Saltwater; t.ElevationLevel = 0; }
            if (environment == EnvironmentKind.Lake) { t.WaterBody = WaterKind.Freshwater; t.FreshwaterSource = FreshwaterSource.Lake; }
            var context = new TileRenderContext { Bounds = rect, Tile = new TileObservation(t, t.Seasons[0], new SimulationConfig(), new RiverEdge[0]) };
            for (int i = 0; i < occupants.Length; i++) context.Entities.Add(new VisibleOccupant(i, occupants[i].StartsWith("entity.") ? i : -1, i, occupants[i], "Art fixture", occupants[i].StartsWith("species.") ? "~20" : ""));
            return context;
        }
        private void DrawArtReview()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(.91f, .89f, .83f));
            var heading = new GUIStyle(title) { normal = { textColor = new Color(.18f, .23f, .18f) } };
            var caption = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(.22f, .28f, .21f) } };
            GUI.Label(new Rect(30, 18, Screen.width - 60, 35), "ANTHROPOCENE  /  CARTOGRAPHIC ART", heading);
            GUI.Label(new Rect(30, 55, Screen.width - 60, 26), "Unity render · illustrative tiles, separate from the playable world · " + tilesets[tilesetIndex].Name, caption);
            if (artReviewMode == 2)
            {
                var empty = ArtTile(new Rect(100, 120, 152, 176), EnvironmentKind.Desert); empty.Selected = true;
                var occupied = ArtTile(new Rect(400, 120, 152, 176), EnvironmentKind.Desert, VegetationTier.Low, 2, "entity.camp"); occupied.Selected = true; occupied.SelectedEntity = 0;
                // Suppress vegetation in both samples so only occupant composition differs.
                empty.Tile = new TileObservation(new Tile { Environment = EnvironmentKind.Ice, ElevationLevel = 3 }, default, new SimulationConfig(), new RiverEdge[0]);
                occupied.Tile = empty.Tile;
                tileRenderer.Draw(empty); tileRenderer.Draw(occupied);
                GUI.Label(new Rect(90, 310, 170, 25), "Underlying tile", caption); GUI.Label(new Rect(390, 310, 170, 25), "Same tile + selected camp", caption);
                return;
            }
            float cell = (Screen.width - 60) / 9f, row = Mathf.Min(202, (Screen.height - 170) / 3f);
            void Draw(int column, int line, string label, EnvironmentKind env, VegetationTier cover = VegetationTier.Low, int level = 2, float height = 112, params string[] entities)
            {
                float x = 30 + column * cell + cell / 2, y = 110 + line * row;
                var context = ArtTile(new Rect(x - height * .433f, y + (128 - height) / 2, height * .866f, height), env, cover, level, entities);
                if (label == "Shallows / shore") context.ShoreEdgeMask = 0x30;
                if (label == "Selected camp" || label.StartsWith("Stack")) { context.Selected = true; context.SelectedEntity = 0; }
                if (label == "Cultivation" || label == "Fishery" || label == "Oasis")
                {
                    var t = new Tile { Id = 43, Environment = env, CurrentVegetation = cover, ElevationLevel = 2, Food = 45, Cultivated = label == "Cultivation", Fishery = label == "Fishery", Oasis = label == "Oasis", FreshwaterSource = label == "Oasis" ? FreshwaterSource.Oasis : FreshwaterSource.None };
                    context.Tile = new TileObservation(t, default, new SimulationConfig(), new RiverEdge[0]);
                }
                tileRenderer.Draw(context); GUI.Label(new Rect(x - cell / 2, y + 133, cell, 40), label, caption);
            }
            Draw(0,0,"Desert",EnvironmentKind.Desert); Draw(1,0,"Grassland",EnvironmentKind.Grassland);
            Draw(2,0,"Temperate",EnvironmentKind.Temperate,VegetationTier.Medium); Draw(3,0,"Tropical",EnvironmentKind.Tropical,VegetationTier.High);
            Draw(4,0,"Cold",EnvironmentKind.Cold,VegetationTier.Medium); Draw(5,0,"Highland",EnvironmentKind.Grassland,VegetationTier.Low,8);
            Draw(6,0,"Ocean",EnvironmentKind.Ocean); Draw(7,0,"Shallows / shore",EnvironmentKind.Shallows); Draw(8,0,"Freshwater lake",EnvironmentKind.Lake);
            Draw(0,1,"Low cover",EnvironmentKind.Temperate); Draw(1,1,"Medium cover",EnvironmentKind.Temperate,VegetationTier.Medium); Draw(2,1,"High cover",EnvironmentKind.Temperate,VegetationTier.High);
            Draw(3,1,"Selected camp",EnvironmentKind.Grassland,VegetationTier.Low,2,112,"entity.camp");
            Draw(4,1,"Foreign camp ◇",EnvironmentKind.Grassland,VegetationTier.Low,2,112,"entity.rivalcamp");
            Draw(5,1,"Hunters",EnvironmentKind.Grassland,VegetationTier.Low,2,112,"entity.hunter");
            Draw(6,1,"Antelope",EnvironmentKind.Grassland,VegetationTier.Low,2,112,"species.antelope");
            Draw(7,1,"Goat",EnvironmentKind.Grassland,VegetationTier.Low,2,112,"species.goat");
            Draw(8,1,"Bison",EnvironmentKind.Grassland,VegetationTier.Low,2,112,"species.bison");
            Draw(0,2,"Camel",EnvironmentKind.Desert,VegetationTier.Low,2,112,"species.camel");
            Draw(1,2,"Horse",EnvironmentKind.Grassland,VegetationTier.Low,2,112,"species.horse");
            Draw(2,2,"Livestock",EnvironmentKind.Grassland,VegetationTier.Low,2,112,"entity.domestic");
            Draw(3,2,"Cultivation",EnvironmentKind.Grassland); Draw(4,2,"Fishery",EnvironmentKind.Shallows); Draw(5,2,"Oasis",EnvironmentKind.Desert);
            Draw(6,2,"Stack / 2 groups",EnvironmentKind.Temperate,VegetationTier.High,4,112,"entity.camp","entity.hunter");
            Draw(7,2,"Stack / 6 groups",EnvironmentKind.Temperate,VegetationTier.High,4,112,"entity.camp","entity.hunter","species.bison","species.goat","species.horse","entity.domestic");
            Draw(8,2,"Distant zoom",EnvironmentKind.Temperate,VegetationTier.High,4,40,"entity.camp","entity.hunter");
            GUI.Label(new Rect(30,Screen.height-64,Screen.width-60,45), "All normal tiles use the game's 112 px tile height. Transparent icons retain terrain, contours and resource marks.\nVillage and heavy cultivation remain future art hooks.", caption);
        }
    }
}
#endif
