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
            int atlas = Array.IndexOf(args, "--anthropocene-atlas-output");
            int resources = Array.IndexOf(args, "--anthropocene-resources-output");
            int neolithic = Array.IndexOf(args, "--anthropocene-neolithic-smoke-output");
            int husbandry = Array.IndexOf(args, "--anthropocene-husbandry-smoke-output");
            int longRun = Array.IndexOf(args, "--anthropocene-long-run-output");
            if (longRun >= 0 && longRun + 1 < args.Length)
            {
                int turnsAt = Array.IndexOf(args, "--anthropocene-long-run-turns");
                int turns = turnsAt >= 0 && turnsAt + 1 < args.Length && int.TryParse(args[turnsAt + 1], out int parsed) && parsed > 0 ? parsed : 50;
                // Optional tribe count on the Standard world (one of its menu choices) and screenshot interval in turns.
                int tribesAt = Array.IndexOf(args, "--anthropocene-long-run-tribes");
                if (tribesAt >= 0 && tribesAt + 1 < args.Length && int.TryParse(args[tribesAt + 1], out int tribes) && TribeChoices[0].Contains(tribes)) { sizeIndex = 0; players = tribes; }
                int everyAt = Array.IndexOf(args, "--anthropocene-long-run-screenshot-every");
                int every = everyAt >= 0 && everyAt + 1 < args.Length && int.TryParse(args[everyAt + 1], out int interval) && interval > 0 ? interval : 10;
                yield return LongRun(args[longRun + 1], turns, every); yield break;
            }
            if (neolithic >= 0 && neolithic + 1 < args.Length) { yield return NeolithicSmoke(args[neolithic + 1]); yield break; }
            if (husbandry >= 0 && husbandry + 1 < args.Length) { yield return HusbandrySmoke(args[husbandry + 1]); yield break; }
            if (resources >= 0 && resources + 1 < args.Length) { yield return ResourceSmoke(args[resources + 1]); yield break; }
            if (atlas >= 0 && atlas + 1 < args.Length) { yield return AtlasSmoke(args[atlas + 1]); yield break; }
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
            // This opt-in test selects a repeatable traversable four-tile route from generated
            // geography. A fixed eastward path from generator 8 is not a generation contract.
            var route = (from first in world.Map.Radius(camp.Tile, 2)
                         where world.Map.Distance(camp.Tile, first) == 2 && world.Map.Reachable(camp.Tile, first, 2, id => world.Tiles[id].TraversableLand)
                         from second in world.Map.Radius(first, 2)
                         where world.Map.Distance(camp.Tile, second) == 4 && world.Map.Reachable(first, second, 2, id => world.Tiles[id].TraversableLand)
                         orderby first, second
                         select new { First = first, Second = second }).FirstOrDefault();
            if (route == null) throw new InvalidOperationException("No four-tile supply demonstration route from this generated camp.");
            var move = new Order(OrderKind.Move, hunter.Id, destination: route.First);
            if (resolver.Validate(world, 0, move) != null) throw new InvalidOperationException("The first generated demonstration move is not legal.");
            Queue(move); EndTurn(); while (resolving != null) yield return null;
            move = new Order(OrderKind.Move, hunter.Id, destination: route.Second);
            if (resolver.Validate(world, 0, move) != null) throw new InvalidOperationException("Second generated demonstration move is not legal.");
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
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: Standard / Continents / 1729 / 32 tribes. Dispatch, exact stack selection, both symbol sets, distant layer simplification, generated two-hop supply route " + camp.Tile + " -> " + route.First + " -> " + route.Second + ", and separate save/load round trip. Final turn " + world.Turn + ", population " + world.LinkedPopulation(world.Camps.First(c => c.Player == 0)) + ". Screenshots captured from the Windows player.\n");
            Application.Quit();
        }

        // Opt-in long run of the shipped configuration through the real player's turn loop (NewGame, EndTurn, the worker
        // resolution, Update/FinishTurn), so the post-breakthrough economy (DemandControl, fission, immigration, trade) runs
        // in Unity rather than only in the headless runner. Works under -batchmode -nographics: screenshots are taken only
        // when a window exists. The human camp switches to continuous economic automation once it has an economy, and
        // otherwise holds. Writes turns.csv and result.txt (PASS/FAIL) and exits with code 0 or 1. Never uses the user's save slot.
        private IEnumerator LongRun(string directory, int turns, int screenshotEvery)
        {
            Application.runInBackground = true;
            string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
            bool windowed = !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            void Finish(bool pass, string message)
            {
                File.WriteAllText(Path.Combine(output, "result.txt"), (pass ? "PASS: " : "FAIL: ") + message + "\n");
                Debug.Log("Anthropocene long run " + (pass ? "PASS: " : "FAIL: ") + message);
                Application.Quit(pass ? 0 : 1);
            }
            NewGame(); while (generating != null) yield return null;
            if (world == null) { Finish(false, "world did not generate; status=" + status); yield break; }
            var cfg = world.Config;
            string configuration = $"EconomicAllocation={cfg.EconomicAllocation} Fission={cfg.FissionEnabled} Immigration={cfg.ImmigrationEnabled} Trade={cfg.TradeEnabled} " +
                $"QoLMortality={cfg.QoLMortalityEnabled} HerdLabour={cfg.HerdLabourEnabled} SharedAllocation={cfg.SharedCropHerdAllocationEnabled}";
            var csv = new System.Text.StringBuilder("turn,years,players,settlements,population,villages,neolithicSettlements,fissions,migrations,trades,humanPopulation,humanEra,turnMs\n");
            var timer = new System.Diagnostics.Stopwatch();
            double slowest = 0;
            for (int i = 0; i < turns; i++)
            {
                var human = world.Camps.FirstOrDefault(c => c.Player == 0 && c.Population > 0);
                if (human == null || world.LinkedPopulation(human) <= 0) { Finish(false, $"the human camp died at turn {world.Turn}; the player turn loop cannot continue. {configuration}"); yield break; }
                if (CultivationEconomy.EconomyEnabled(human) && human.EconomyAutomationTurns != -1)
                {
                    var automated = EconomicIntent.From(human); automated.AutomationTurns = -1;
                    Queue(new Order(OrderKind.Economy, human.Id) { Economy = automated });
                }
                int turnBefore = world.Turn;
                timer.Restart(); EndTurn();
                while (resolving != null) yield return null;
                timer.Stop(); slowest = Math.Max(slowest, timer.Elapsed.TotalMilliseconds);
                if (world.Turn != turnBefore + 1) { Finish(false, $"turn {turnBefore} did not resolve; status={status}. {configuration}"); yield break; }
                var record = world.History.Last(); var live = world.Camps.Where(c => c.Population > 0).ToArray();
                human = world.Camps.FirstOrDefault(c => c.Player == 0);
                // Dismiss the breakthrough dialog as its CONTINUE PLAYING button does, leaving the village inspector closed
                // so later captures show the map.
                if (human != null && human.People.Progression.Era != Era.Paleolithic && !transitionDismissed)
                {
                    transitionDismissed = true; selected = human.Id; inspected = human.Tile; mode = "Inspect"; villageInspectorOpen = false;
                }
                csv.Append(world.Turn).Append(',').Append((world.Days / cfg.DaysPerYear).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                   .Append(world.LivePlayers).Append(',').Append(live.Length).Append(',').Append(live.Sum(c => c.Population)).Append(',')
                   .Append(live.Count(c => c.IsVillage)).Append(',').Append(live.Count(c => c.People.Progression.Era != Era.Paleolithic)).Append(',')
                   .Append(record.Fissions?.Count ?? 0).Append(',').Append(record.Migrations?.Count ?? 0).Append(',').Append(record.Trades?.Count ?? 0).Append(',')
                   .Append(human == null ? 0 : world.LinkedPopulation(human)).Append(',').Append(human?.People.Progression.Era.ToString() ?? "-").Append(',')
                   .Append(timer.Elapsed.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                File.WriteAllText(Path.Combine(output, "turns.csv"), csv.ToString());
                if (windowed && world.Turn % screenshotEvery == 0)
                {
                    yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, $"turn-{world.Turn:D3}.png")); yield return new WaitForSeconds(.4f);
                }
            }
            // The continued world must survive the player's own save/load path exactly.
            string before = SaveCodec.Fingerprint(world); var savedWorld = world;
            operation = "Checking the long-run save";
            generating = Task.Run(() => { var bytes = SaveCodec.Save(savedWorld); File.WriteAllBytes(Path.Combine(output, "long-run.anthro"), bytes); return SaveCodec.Load(bytes); });
            while (generating != null) yield return null;
            if (SaveCodec.Fingerprint(world) != before) { Finish(false, "save/load changed the continued world. " + configuration); yield break; }
            var final = world.Camps.Where(c => c.Population > 0).ToArray();
            var history = world.History.Skip(Math.Max(0, world.History.Count - turns)).ToArray();
            Finish(true, $"{cfg.Players} tribes, {turns} turns through the player loop ({(windowed ? "windowed" : "batch mode, no graphics")}); final turn {world.Turn}, {final.Length} settlements, " +
                $"{final.Sum(c => c.Population)} people, {world.LivePlayers} players, {final.Count(c => c.People.Progression.Era != Era.Paleolithic)} settlements past the breakthrough; " +
                $"{history.Sum(r => r.Fissions?.Count ?? 0)} fissions, {history.Sum(r => r.Migrations?.Count ?? 0)} migrations, {history.Sum(r => r.Trades?.Count ?? 0)} trades; " +
                $"slowest turn {slowest:F0} ms; exact save/load of the continued world. {configuration}");
        }

        private IEnumerator NeolithicSmoke(string directory)
        {
            Application.runInBackground = true;
            string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
            NewGame(); while (generating != null) yield return null;
            if (world == null) throw new InvalidOperationException("Neolithic smoke generation failed.");
            // The fixture below edits the committed world; let the adoption-time AI draft finish
            // reading it first, and plan a fresh draft once the fixture is in place.
            while (drafting != null && !drafting.IsCompleted) yield return null;
            // Explicit synthetic capability/inventory fixture on real generated geography.
            // This opt-in route neither grants a technology through player controls nor
            // uses a user's save slot; it tests the production UI after capability unlock.
            var camp = world.Camps.Single(c => c.Player == 0);
            camp.Population = 100; camp.PlantFood = 12000; camp.Technology = Technology.Cultivation;
            camp.Technologies[(int)Technology.Cultivation] = true; camp.People.Progression.Era = Era.Neolithic;
            camp.Inventory.HandTools = 20; camp.Inventory.Cloth = 100;
            NeolithicProduction.AddFixedHousing(world, camp, 25, world.Days);
            world.Config.AnnualHumanGrowthRate = 0; transitionDismissed = true;
            Refresh(); StartDraft(); villageInspectorOpen = true; villageInspectorCampId = camp.Id;
            string before = SaveCodec.Fingerprint(world);
            var intent = EconomicIntent.From(camp); intent.Assignments.Clear();
            intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 20 });
            intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Craftsperson, Population = 10 });
            intent.Priorities.Cloth = .37; Queue(new Order(OrderKind.Economy, camp.Id) { Economy = intent });
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "01-queued-economy.png")); yield return new WaitForSeconds(.4f);
            if (SaveCodec.Fingerprint(world) != before) throw new InvalidOperationException("Queued economic controls changed live state before commit.");
            EndTurn(); while (resolving != null) yield return null;
            double fabricOutput = world.History.Last().Production.Where(p => p.Camp == camp.Id && p.Good == CraftGood.Cloth).Sum(p => p.Output);
            if (camp.Workforce.Assigned != 30 || camp.CraftingPriorities.Cloth != .37 || camp.FabricConsumed <= 0 || Math.Abs(100 + fabricOutput - camp.FabricConsumed - camp.Inventory.Cloth) > 1e-8 || !world.Tiles.Any(t => t.CultivationCapital > 0) || camp.People.HasTech(NeolithicTech.Irrigation)) throw new InvalidOperationException("Neolithic player intent/needs contract failed: " + status);
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "02-resolved-economy.png")); yield return new WaitForSeconds(.4f);
            string saved = SaveCodec.Fingerprint(world); var snapshot = world;
            operation = "Restoring Neolithic validation state";
            generating = Task.Run(() => { var bytes = SaveCodec.Save(snapshot); File.WriteAllBytes(Path.Combine(output, "validation.anthro"), bytes); return SaveCodec.Load(bytes); });
            while (generating != null) yield return null;
            if (SaveCodec.Fingerprint(world) != saved) throw new InvalidOperationException("Neolithic player save/load changed authoritative state.");
            villageInspectorOpen = true; villageInspectorCampId = camp.Id;
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "03-restored-economy.png")); yield return new WaitForSeconds(1);
            camp = world.Camps.Single(c => c.Player == 0);
            var automated = EconomicIntent.From(camp); automated.AutomationTurns = 2;
            Queue(new Order(OrderKind.Economy, camp.Id) { Economy = automated });
            // This accepted Economy order owns the current turn's manual
            // schedule. The saved automation countdown is applied now, and
            // its planner-generated labor plan begins on the next unsupplied turn.
            EndTurn(); while (resolving != null) yield return null;
            if (camp.EconomyAutomationTurns != 1) throw new InvalidOperationException("Explicit Economy order did not set the automation countdown to one; actual=" + camp.EconomyAutomationTurns + ", lastOrders=" + string.Join("|", world.History.Last().Orders) + ", status=" + status);
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "04-automation-active.png")); yield return new WaitForSeconds(.4f);
            saved = SaveCodec.Fingerprint(world); snapshot = world;
            generating = Task.Run(() => SaveCodec.Load(SaveCodec.Save(snapshot))); while (generating != null) yield return null;
            camp = world.Camps.Single(c => c.Player == 0);
            bool automationStateRestored = saved == SaveCodec.Fingerprint(world);
            if (!automationStateRestored || camp.EconomyAutomationTurns != 1) throw new InvalidOperationException("Active player automation did not survive load; fingerprintMatch=" + automationStateRestored + ", countdown=" + camp.EconomyAutomationTurns + ", status=" + status);
            EndTurn(); while (resolving != null) yield return null;
            bool automaticPlanApplied = world.History.Last().Orders.Any(s => s.Contains(":automation:"));
            if (camp.EconomyAutomationTurns != 0 || camp.EconomicLabor == null || !automaticPlanApplied)
                throw new InvalidOperationException("Saved automation did not finish without resubmission; countdown=" + camp.EconomyAutomationTurns + ", laborPlan=" + (camp.EconomicLabor == null ? "null" : "present") + ", automaticOrder=" + automaticPlanApplied + ", lastOrders=" + string.Join("|", world.History.Last().Orders) + ", status=" + status);
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "05-automation-complete.png")); yield return new WaitForSeconds(1);
            if (Directory.GetFiles(output, "*.png").Length != 5) throw new InvalidOperationException("Missing Neolithic player captures; use a visible player window.");
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: synthetic Agriculture fixture on Standard/Continents/1729 generated geography. Economic plan queued without live mutation, normal turn resolution, real E growth, recurring Fabric debit, no Irrigation grant, isolated exact save/load, and a two-turn automation toggle continuing through save/load without resubmission. Five actual Windows player captures. Not a natural-progression balance study or a human usability test.\n");
            Application.Quit();
        }

        private IEnumerator HusbandrySmoke(string directory)
        {
            const double animalFabricStock = 500, herdingToolStock = 500;
            Application.runInBackground = true;
            string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
            NewGame(); while (generating != null) yield return null;
            if (world == null) throw new InvalidOperationException("Husbandry smoke generation failed.");
            // The fixture below edits the committed world; let the adoption-time AI draft finish
            // reading it first, and plan a fresh draft once the fixture is in place.
            while (drafting != null && !drafting.IsCompleted) yield return null;
            var camp = world.Camps.Single(c => c.Player == 0);
            camp.Population = 100; camp.PlantFood = camp.AnimalFood = camp.AquaticFood = 0;
            camp.Technology = Technology.AnimalHusbandry; camp.Technologies[(int)Technology.AnimalHusbandry] = true;
            camp.People.Progression.Era = Era.Neolithic; camp.Workforce = new PopulationLedger();
            camp.Workforce.Assign(WorkforceRoles.Herder, 50, camp.Population);
            world.Herds.RemoveAll(h => h.DomesticCamp == camp.Id);
            world.Herds.Add(new Herd { Id = world.NextId++, Tile = camp.Tile, Species = world.Config.Species.First().Id, Animals = 100, Capacity = 1, DomesticCamp = camp.Id, OwnerSociety = camp.SocietyId });
            camp.Inventory.HousingUnits = 0;
            camp.Inventory.AnimalFabric = animalFabricStock; camp.Inventory.LightHousingUnits = 8; camp.Inventory.HerdingTools = herdingToolStock;
            world.FixedHousing ??= new System.Collections.Generic.List<FixedHousingStock>();
            world.FixedHousing.Add(new FixedHousingStock { CampId = camp.Id, Tile = camp.Tile, Units = 12, LastUpdatedDays = world.Days });
            world.Config.AnnualHumanGrowthRate = 0; transitionDismissed = true; Refresh(); StartDraft(); villageInspectorOpen = true; villageInspectorCampId = camp.Id;
            string before = SaveCodec.Fingerprint(world);
            var intent = EconomicIntent.From(camp); intent.Assignments.Clear(); intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = 50 }); intent.ManagedBreedingIntensity = 0; intent.AutomationTurns = 0;
            intent.Priorities.AnimalFabric = .31; intent.Priorities.LightHousing = .22; intent.Priorities.HerdingTools = .13;
            Queue(new Order(OrderKind.Economy, camp.Id) { Economy = intent });
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "01-queued-husbandry.png")); yield return new WaitForSeconds(.4f);
            if (SaveCodec.Fingerprint(world) != before) throw new InvalidOperationException("Queued Husbandry controls changed live state before commit.");
            EndTurn(); while (resolving != null) yield return null;
            camp = world.Camps.Single(c => c.Player == 0); var owned = observation.Herds.Single(h => h.OwnedDomestic && h.DomesticCamp == camp.Id); var managed = world.History.Last().ManagedHerds.Single(h => h.Herd == owned.Id);
            double herdingToolOutput = world.History.Last().Production.Where(p => p.Camp == camp.Id && p.Good == CraftGood.HerdingTools).Sum(p => p.Output);
            double animalFabricOutput = world.History.Last().Production.Where(p => p.Camp == camp.Id && p.Good == CraftGood.AnimalFabric).Sum(p => p.Output);
            double fixedShelter = NeolithicProduction.FixedHousingAt(world, camp.Id, camp.Tile, world.Days);
            double shelterSupply = NeolithicProduction.HousingSupply(world, camp, world.Days);
            double herdingToolRemainder = herdingToolStock + herdingToolOutput - managed.HerdingToolsConsumed;
            double animalFabricDebited = animalFabricStock + animalFabricOutput - camp.Inventory.AnimalFabric;
            if (camp.Workforce.Assignments.Any(a => a.Role == WorkforceRoles.Farmer) || !camp.Workforce.Assignments.Any(a => a.Role == WorkforceRoles.Herder) || owned.AcceptedFood <= 0 || managed.Nutrition <= 0 || managed.NewbornAnimals != 0 || camp.ManagedBreedingIntensity != 0 || camp.CraftingPriorities.AnimalFabric != .31 || camp.CraftingPriorities.LightHousing != .22 || camp.CraftingPriorities.HerdingTools != .13 || camp.Inventory.HousingUnits != 0 || camp.Inventory.AnimalFabric <= 0 || animalFabricDebited < -1e-8 || camp.Inventory.LightHousingUnits <= 0 || camp.Inventory.HerdingTools <= 0 || Math.Abs(camp.Inventory.HerdingTools - herdingToolRemainder) > 1e-8 || managed.HerdingToolsConsumed <= 0 || fixedShelter <= 0 || Math.Abs(shelterSupply - fixedShelter - camp.Inventory.LightHousingUnits) > 1e-8) throw new InvalidOperationException("Husbandry priorities, typed inventory, recorded herding-tool use, portable/fixed shelter, b=0 control, or owned managed report failed: " + status);
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "02-resolved-husbandry.png")); yield return new WaitForSeconds(.4f);
            string saved = SaveCodec.Fingerprint(world); var snapshot = world; operation = "Restoring Husbandry validation state";
            generating = Task.Run(() => { var bytes = SaveCodec.Save(snapshot); File.WriteAllBytes(Path.Combine(output, "validation.anthro"), bytes); return SaveCodec.Load(bytes); });
            while (generating != null) yield return null;
            var restoredCamp = world.Camps.Single(c => c.Player == 0);
            if (SaveCodec.Fingerprint(world) != saved || restoredCamp.ManagedBreedingIntensity != 0 || restoredCamp.CraftingPriorities.AnimalFabric != .31 || restoredCamp.CraftingPriorities.LightHousing != .22 || restoredCamp.CraftingPriorities.HerdingTools != .13 || restoredCamp.Inventory.HousingUnits != 0 || restoredCamp.Inventory.AnimalFabric <= 0 || restoredCamp.Inventory.LightHousingUnits <= 0 || restoredCamp.Inventory.HerdingTools <= 0 || NeolithicProduction.FixedHousingAt(world, restoredCamp.Id, restoredCamp.Tile, world.Days) <= 0 || Math.Abs(NeolithicProduction.HousingSupply(world, restoredCamp, world.Days) - NeolithicProduction.FixedHousingAt(world, restoredCamp.Id, restoredCamp.Tile, world.Days) - restoredCamp.Inventory.LightHousingUnits) > 1e-8) throw new InvalidOperationException("Husbandry priorities, pastoral inventories or fixed/portable shelter did not survive player save/load.");
            Refresh(); villageInspectorOpen = true; villageInspectorCampId = world.Camps.Single(c => c.Player == 0).Id;
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "03-restored-husbandry.png")); yield return new WaitForSeconds(.4f);
            var automated = EconomicIntent.From(world.Camps.Single(c => c.Player == 0)); automated.ManagedBreedingIntensity = .4; automated.AutomationTurns = 2;
            Queue(new Order(OrderKind.Economy, world.Camps.Single(c => c.Player == 0).Id) { Economy = automated });
            EndTurn(); while (resolving != null) yield return null;
            camp = world.Camps.Single(c => c.Player == 0); if (camp.EconomyAutomationTurns != 1 || camp.ManagedBreedingIntensity != .4 || camp.CraftingPriorities.AnimalFabric != .31 || camp.CraftingPriorities.LightHousing != .22 || camp.CraftingPriorities.HerdingTools != .13 || camp.Inventory.AnimalFabric <= 0 || camp.Inventory.LightHousingUnits <= 0 || camp.Inventory.HerdingTools <= 0 || NeolithicProduction.FixedHousingAt(world, camp.Id, camp.Tile, world.Days) <= 0) throw new InvalidOperationException("Husbandry automation did not retain craft priorities and typed portable/fixed shelter inventory after commit.");
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "04-automation-active.png")); yield return new WaitForSeconds(.4f);
            saved = SaveCodec.Fingerprint(world); snapshot = world;
            generating = Task.Run(() => SaveCodec.Load(SaveCodec.Save(snapshot))); while (generating != null) yield return null;
            if (saved != SaveCodec.Fingerprint(world) || world.Camps.Single(c => c.Player == 0).EconomyAutomationTurns != 1 || world.Camps.Single(c => c.Player == 0).ManagedBreedingIntensity != .4 || world.Camps.Single(c => c.Player == 0).CraftingPriorities.AnimalFabric != .31 || world.Camps.Single(c => c.Player == 0).CraftingPriorities.LightHousing != .22 || world.Camps.Single(c => c.Player == 0).CraftingPriorities.HerdingTools != .13 || NeolithicProduction.FixedHousingAt(world, world.Camps.Single(c => c.Player == 0).Id, world.Camps.Single(c => c.Player == 0).Tile, world.Days) <= 0) throw new InvalidOperationException("Active Husbandry automation or pastoral craft/shelter state did not survive load.");
            EndTurn(); while (resolving != null) yield return null;
            camp = world.Camps.Single(c => c.Player == 0); if (camp.EconomyAutomationTurns != 0 || camp.ManagedBreedingIntensity != .4 || camp.CraftingPriorities.AnimalFabric != .31 || camp.CraftingPriorities.LightHousing != .22 || camp.CraftingPriorities.HerdingTools != .13 || camp.Inventory.AnimalFabric <= 0 || camp.Inventory.LightHousingUnits <= 0 || camp.Inventory.HerdingTools <= 0 || NeolithicProduction.FixedHousingAt(world, camp.Id, camp.Tile, world.Days) <= 0) throw new InvalidOperationException("Husbandry automation did not retain pastoral priorities, inventories and shelter.");
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "05-automation-complete.png")); yield return new WaitForSeconds(1);
            if (Directory.GetFiles(output, "*.png").Length != 5) throw new InvalidOperationException("Missing Husbandry player captures; use a visible player window.");
            File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS: synthetic Husbandry-only fixture on generated geography. The shared Economy panel exposes Animal Fabric, portable Light Housing, Herding Tools and their priorities alongside breeding controls. Animal Fabric inventory showed {animalFabricOutput:F2} production and {animalFabricDebited:F2} debit from a 500-unit seed; the ManagedHerds receipt accounts for {managed.HerdingToolsConsumed:F2} Herding Tools used from a 500-unit seed. Typed inventories and separate current-site fixed / portable shelter survived isolated save/load and two normal automated commits, with priorities retained. The order resolved a live positively nourished herd with zero intentional births and no Agriculture/Farmer assignment. Owned managed-herd diagnostics and five actual Windows player captures. Not a natural-progression balance study or a human usability test.\n");
            Application.Quit();
        }
        private IEnumerator AtlasSmoke(string directory)
        {
            Application.runInBackground = true;
            string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
            scenarioIndex = 0; sizeIndex = 0; shapeIndex = (int)WorldShape.Archipelagos; players = 80; seedText = "42";
            previewAfterGeneration = true; NewGame(); while (generating != null) yield return null;
            if (world == null || world.Config.Shape != WorldShape.Archipelagos || world.Camps.Count != 80) throw new InvalidOperationException("Archipelago preview did not generate: " + status);
            string before = SaveCodec.Fingerprint(world); int visible = observation.Tiles.Count;
            for (int layer = 0; layer < previewLayers.Length; layer++)
            {
                previewLayer = layer; InvalidateWorldPreview();
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "preview-" + layer + ".png")); yield return new WaitForSeconds(.3f);
            }
            if (SaveCodec.Fingerprint(world) != before || observation.Tiles.Count != visible) throw new InvalidOperationException("Archipelago preview mutated ecology or fog.");
            previewOpen = false;
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "opening.png")); yield return new WaitForSeconds(.3f);
            EndTurn(); while (resolving != null) yield return null;
            if (world.Turn != 2) throw new InvalidOperationException("Archipelago opening could not resolve: " + status);
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, "turn-2.png")); yield return new WaitForSeconds(1);
            if (Directory.GetFiles(output, "*.png").Length != previewLayers.Length + 2) throw new InvalidOperationException("Missing archipelago screenshots; use a visible player window.");
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS Standard / Archipelagos / 42 / 80 tribes / generator " + world.Generation.GeneratorVersion + ": " + previewLayers.Length + " preview layers, unchanged authoritative fingerprint and fog (" + visible + " observed tiles), normal play and turn 2 population " + world.LinkedPopulation(world.Camps.First(c => c.Player == 0)) + ". Actual Windows player captures. No user save slot used.\n");
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
                for (int layer = 0; layer < previewLayers.Length; layer++)
                {
                    previewLayer = layer; InvalidateWorldPreview();
                    yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, preset + "-preview-" + layer + ".png")); yield return new WaitForSeconds(.3f);
                }
                previewTile = PlaytestScenarios.Tile(preset); previewZoom = 12; previewPan = Vector2.zero;
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, preset + "-preview-close.png")); yield return new WaitForSeconds(.3f);
                if (SaveCodec.Fingerprint(world) != before || observation.Tiles.Count != visible) throw new InvalidOperationException("Preview mutated ecology or fog.");
                previewOpen = false;
                if (preset == 3)
                {
                    inspected = world.Map.Neighbors(observation.CampTile).First(id => observation.Tiles.TryGetValue(id, out var tile) && tile.Environment == EnvironmentKind.Shallows);
                    string details = TileDetails.Describe(observation, inspected, planned.Values);
                    if (!details.Contains("PLANKTON / ALGAE") || !details.Contains("25% human-gatherable")) throw new InvalidOperationException("Aquatic inspector did not expose the new food-web profile.");
                    sidebarScroll = new Vector2(0, 100000);
                }
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, preset + "-play.png")); yield return new WaitForSeconds(.3f);
                EndTurn(); while (resolving != null) yield return null;
                if (world.Turn != 2) throw new InvalidOperationException("Preset could not resolve a turn: " + status);
                report.AppendLine("PASS " + PlaytestScenarios.Names[preset] + ": all preview layers, local zoom, normal fog (" + visible + " observed tiles), unchanged preview fingerprint, turn 2 population " + world.LinkedPopulation(world.Camps.First(c => c.Player == 0)));
            }
            yield return new WaitForSeconds(1);
            if (Directory.GetFiles(output, "*.png").Length != 3 * (previewLayers.Length + 3)) throw new InvalidOperationException("Missing preset screenshots.");
            File.WriteAllText(Path.Combine(output, "result.txt"), report.ToString()); Application.Quit();
        }
    }
}
#endif
