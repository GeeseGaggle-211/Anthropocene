#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public sealed partial class OpeningGame
    {
        // Opt-in validation of the real player/UI; never touches the user's save slot.
        private IEnumerator Start()
        {
            var args = System.Environment.GetCommandLineArgs(); int option = Array.IndexOf(args, "--anthropocene-smoke-output");
            int presets = Array.IndexOf(args, "--anthropocene-presets-output");
            if (presets >= 0 && presets + 1 < args.Length) { yield return PresetSmoke(args[presets + 1]); yield break; }
            if (option < 0 || option + 1 >= args.Length) yield break;
            Application.runInBackground = true; // This opt-in capture run must survive focus returning to the review window.
            string output = Path.GetFullPath(args[option + 1]); Directory.CreateDirectory(output);
            NewGame(); while (generating != null) yield return null;
            if (world == null) throw new InvalidOperationException("Smoke world did not generate.");
            artReviewMode = 2; yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "00-transparency.png")); yield return new WaitForSeconds(.4f);
            artReviewMode = 1; yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "00-art-classic.png")); yield return new WaitForSeconds(.4f);
            SetTileset(1); yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "00-art-contrast.png")); yield return new WaitForSeconds(.4f);
            SetTileset(0); artReviewMode = 0;
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "01-opening.png")); yield return new WaitForSeconds(.4f);
            var camp = world.Camps.First(c => c.Player == 0); Queue(new Order(OrderKind.Dispatch, camp.Id, population: 8)); EndTurn();
            while (resolving != null) yield return null;
            var hunter = world.Hunters.First(h => h.Player == 0); var slot = TileDetails.Occupants(observation, camp.Tile).Single(p => p.Id == hunter.Id);
            SelectOccupant(camp.Tile, slot.Id);
            if (selected != hunter.Id) throw new InvalidOperationException("Stack selection did not select the exact hunter slot.");
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "02-dispatch-stack.png")); yield return new WaitForSeconds(.4f);
            SetTileset(1); yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "03-high-contrast.png")); yield return new WaitForSeconds(.4f);
            SetTileset(0);
            float normalRadius = radius; radius = 20;
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "03b-distant-view.png")); yield return new WaitForSeconds(.4f);
            radius = normalRadius;
            int east = world.Map.At(hunter.Tile % world.Config.Width + 2, hunter.Tile / world.Config.Width);
            var move = new Order(OrderKind.Move, hunter.Id, destination: east);
            if (resolver.Validate(world, 0, move) != null) throw new InvalidOperationException("The documented eastward demonstration is no longer legal.");
            Queue(move); EndTurn(); while (resolving != null) yield return null;
            east = world.Map.At(hunter.Tile % world.Config.Width + 2, hunter.Tile / world.Config.Width);
            move = new Order(OrderKind.Move, hunter.Id, destination: east);
            if (resolver.Validate(world, 0, move) != null) throw new InvalidOperationException("Second eastward demonstration order is no longer legal.");
            Queue(move);
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "04-supply-warning.png")); yield return new WaitForSeconds(.4f);
            string before = SaveCodec.Fingerprint(world); var savedWorld = world;
            operation = "Loading the validation opening";
            generating = Task.Run(() => { var bytes = SaveCodec.Save(savedWorld); File.WriteAllBytes(Path.Combine(output, "validation.anthro"), bytes); return SaveCodec.Load(bytes); });
            while (generating != null) yield return null;
            if (SaveCodec.Fingerprint(world) != before) throw new InvalidOperationException("Player save/load changed authoritative state.");
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "05-restored.png")); yield return new WaitForSeconds(.6f);
            string[] captures = { "00-transparency", "00-art-classic", "00-art-contrast", "01-opening", "02-dispatch-stack", "03-high-contrast", "03b-distant-view", "04-supply-warning", "05-restored" };
            float captureDeadline = Time.realtimeSinceStartup + 5;
            while (captures.Any(name => !File.Exists(Path.Combine(output, name + ".png"))) && Time.realtimeSinceStartup < captureDeadline) yield return null;
            if (captures.Any(name => !File.Exists(Path.Combine(output, name + ".png")))) throw new InvalidOperationException("Player screenshots are missing. Run this visual check in a visible game window.");
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: Standard / Continents / 1729 / 32 tribes. Dispatch, exact stack selection, both symbol sets, distant layer simplification, two eastward move previews, and separate save/load round trip. Final turn " + world.Turn + ", population " + world.LinkedPopulation(world.Camps.First(c => c.Player == 0)) + ". Screenshots captured from the Windows player.\n");
            Application.Quit();
        }

        private IEnumerator PresetSmoke(string directory)
        {
            Application.runInBackground = true;
            string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
            var report = new System.Text.StringBuilder();
            for (int preset = 1; preset <= 3; preset++)
            {
                scenarioIndex = preset; menu = true;
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, preset + "-menu.png")); yield return new WaitForSeconds(.3f);
                previewAfterGeneration = true; NewGame(); while (generating != null) yield return null;
                if (world == null || world.Camps.Single(c => c.Player == 0).Tile != PlaytestScenarios.Tile(preset)) throw new InvalidOperationException("Preset did not load: " + status);
                string before = SaveCodec.Fingerprint(world); int visible = observation.Tiles.Count;
                for (int layer = 0; layer < 3; layer++)
                {
                    previewLayer = layer; InvalidateWorldPreview();
                    yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, preset + "-preview-" + layer + ".png")); yield return new WaitForSeconds(.3f);
                }
                previewTile = PlaytestScenarios.Tile(preset); previewZoom = 12; previewPan = Vector2.zero;
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, preset + "-preview-close.png")); yield return new WaitForSeconds(.3f);
                if (SaveCodec.Fingerprint(world) != before || observation.Tiles.Count != visible) throw new InvalidOperationException("Preview mutated ecology or fog.");
                previewOpen = false;
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, preset + "-play.png")); yield return new WaitForSeconds(.3f);
                EndTurn(); while (resolving != null) yield return null;
                if (world.Turn != 2) throw new InvalidOperationException("Preset could not resolve a turn: " + status);
                report.AppendLine("PASS " + PlaytestScenarios.Names[preset] + ": all preview layers, local zoom, normal fog (" + visible + " observed tiles), unchanged preview fingerprint, turn 2 population " + world.LinkedPopulation(world.Camps.First(c => c.Player == 0)));
            }
            yield return new WaitForSeconds(1);
            if (Directory.GetFiles(output, "*.png").Length != 18) throw new InvalidOperationException("Missing preset screenshots.");
            File.WriteAllText(Path.Combine(output, "result.txt"), report.ToString()); Application.Quit();
        }
    }
}
#endif
