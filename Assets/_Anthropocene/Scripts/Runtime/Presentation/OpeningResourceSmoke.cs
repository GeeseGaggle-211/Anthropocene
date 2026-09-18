#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public sealed partial class OpeningGame
    {
        private IEnumerator ResourceSmoke(string directory)
        {
            Application.runInBackground = true;
            string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
            scenarioIndex = 0; sizeIndex = 0; shapeIndex = (int)WorldShape.Archipelagos; players = 80; seedText = "42";
            previewAfterGeneration = true; NewGame(); while (generating != null) yield return null;
            if (world?.Resources == null || world.Camps.Count != 80) throw new InvalidOperationException("Resource preview did not generate: " + status);
            string before = SaveCodec.Fingerprint(world); int visible = observation.Tiles.Count, frames = 0;
            previewLayer = 5;
            foreach (var material in PreviewMaterials)
                foreach (bool accessible in new[] { false, true })
                {
                    SelectPreviewResource(material, accessible); BuildPreviewTexture();
                    previewTile = Array.IndexOf(previewResourceValues, previewResourceValues.Max());
                    previewZoom = 1; previewPan = Vector2.zero;
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, material + (accessible ? "-accessible" : "-total") + ".png"));
                    frames++; yield return new WaitForSeconds(.15f);
                }
            SelectPreviewResource(MaterialId.Copper, false); BuildPreviewTexture();
            previewTile = Array.IndexOf(previewResourceValues, previewResourceValues.Max()); previewZoom = 12; previewPan = Vector2.zero;
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "copper-close.png")); frames++; yield return new WaitForSeconds(.3f);
            previewResourcePicker = true;
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "selector.png")); frames++; yield return new WaitForSeconds(.3f);
            previewResourcePicker = false;
            if (SaveCodec.Fingerprint(world) != before || observation.Tiles.Count != visible) throw new InvalidOperationException("Resource preview changed world state or fog.");
            previewOpen = false;
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "normal-fog.png")); frames++; yield return new WaitForSeconds(1);
            if (Directory.GetFiles(output, "*.png").Length != frames) throw new InvalidOperationException("Missing resource screenshots; use a visible player window.");
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS Standard / Archipelagos / 42 / 80: " + PreviewMaterials.Length + " materials, total and accessible overlays, close zoom, selector, normal fog; " + frames + " actual player captures. Authoritative fingerprint and " + visible + " observed tiles unchanged. No user save slot used.\n");
            // Leave the opt-in player at the atlas for an interactive control check.
            previewOpen = true; previewZoom = 1; previewPan = Vector2.zero;
        }
    }
}
#endif
