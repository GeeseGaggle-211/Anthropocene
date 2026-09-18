using System.Linq;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public sealed partial class OpeningGame
    {
        private MaterialId previewMaterial = MaterialId.Stone;
        private bool previewAccessible, previewResourcePicker;
        private double[] previewResourceValues;
        private double previewResourceMaximum;
        private Vector2 resourcePickerScroll;
        private int resourceDescriptionTile = -1;
        private MaterialId resourceDescriptionMaterial;
        private WorldState resourceDescriptionWorld;
        private string resourceDescription;
        private static readonly MaterialId[] PreviewMaterials = ResourceCatalog.Materials.Where(m => !m.Reserved).Select(m => m.Id).ToArray();

        private void SelectPreviewResource(MaterialId material, bool accessible)
        {
            previewMaterial = material; previewAccessible = accessible; previewResourcePicker = false;
            if (previewTexture != null) Destroy(previewTexture);
            previewTexture = null; previewResourceValues = null; previewScroll = Vector2.zero;
        }

        private void DrawResourceControls()
        {
            if (Button(new Rect(20, 100, 230, 30), WorldResourcePreview.Name(previewMaterial) + "  ▾")) previewResourcePicker = true;
            if (Button(new Rect(258, 100, 190, 30), previewAccessible ? "Human accessible" : "Total quantity")) SelectPreviewResource(previewMaterial, !previewAccessible);
            if (Button(new Rect(732, 100, 155, 30), "Find largest stock") && previewResourceValues != null)
            {
                double maximum = previewResourceMaximum;
                if (maximum > 0) { previewTile = System.Array.IndexOf(previewResourceValues, maximum); previewZoom = 12; previewPan = Vector2.zero; }
            }
        }

        private void DrawResourcePicker()
        {
            var box = new Rect(20, 136, Mathf.Min(620, Screen.width - 40), Mathf.Min(490, Screen.height - 170));
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape || evt.type == EventType.MouseDown && !box.Contains(evt.mousePosition))
            { previewResourcePicker = false; evt.Use(); return; }
            Fill(box, new Color(.10f, .14f, .17f)); GUI.BeginGroup(box);
            GUI.Label(new Rect(12, 8, box.width - 95, 28), "Choose resource · all technology eras (dev)", section);
            if (Button(new Rect(box.width - 80, 5, 72, 28), "Close")) previewResourcePicker = false;
            float cell = (box.width - 32) / 3;
            resourcePickerScroll = GUI.BeginScrollView(new Rect(8, 42, box.width - 16, box.height - 50), resourcePickerScroll,
                new Rect(0, 0, box.width - 34, ((PreviewMaterials.Length + 2) / 3) * 32));
            for (int i = 0; i < PreviewMaterials.Length; i++)
                if (Button(new Rect(i % 3 * cell, i / 3 * 32, cell - 6, 28), WorldResourcePreview.Name(PreviewMaterials[i]), previewMaterial == PreviewMaterials[i]))
                    SelectPreviewResource(PreviewMaterials[i], previewAccessible);
            GUI.EndScrollView(); GUI.EndGroup();
        }

        private void DrawResourceDetails(Rect area)
        {
            Fill(area, Panel); float width = area.width - 40;
            if (previewTile < 0) previewTile = world.Camps.First(c => c.Player == 0).Tile;
            if (resourceDescriptionWorld != world || resourceDescriptionTile != previewTile || resourceDescriptionMaterial != previewMaterial)
            {
                resourceDescriptionWorld = world; resourceDescriptionTile = previewTile; resourceDescriptionMaterial = previewMaterial;
                resourceDescription = WorldResourcePreview.Describe(world, previewTile, previewMaterial);
            }
            string heading = "Seed " + world.Seed + " · " + WorldResourcePreview.Name(previewMaterial) + "\n" + (previewAccessible ? "Human accessible units / tile" : "Total units / tile");
            string legend = "Dim terrain: absent · teal: traces\nGold: 100,000+ units · logarithmic scale\nShared deposit maximum: " + (world.Resources?.MaximumQuantity ?? 100000).ToString("N0") + " units\nWhite: tribes · gold square: your camp\n\nDeveloper view shows all resources. Player discovery requires the relevant technology; this view does not change knowledge.";
            if (world.Resources != null && previewResourceValues != null && previewResourceMaximum == 0) legend += "\n\nNo " + WorldResourcePreview.Name(previewMaterial) + " in this world.";
            float legendHeight = small.CalcHeight(new GUIContent(legend), width), detailHeight = text.CalcHeight(new GUIContent(resourceDescription), width);
            GUI.BeginGroup(area);
            previewScroll = GUI.BeginScrollView(new Rect(0, 0, area.width, area.height), previewScroll, new Rect(0, 0, area.width - 20, 95 + legendHeight + detailHeight));
            GUI.Label(new Rect(12, 12, width, 48), heading, section);
            GUI.Label(new Rect(12, 70, width, legendHeight), legend, small);
            GUI.Label(new Rect(12, 85 + legendHeight, width, detailHeight), resourceDescription, text);
            GUI.EndScrollView(); GUI.EndGroup();
        }
    }
}
