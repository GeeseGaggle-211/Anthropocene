using System;
using System.Linq;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public sealed partial class OpeningGame
    {
        private bool villageInspectorOpen;
        private int villageInspectorCampId = -1;
        private Vector2 villageInspectorScroll;
        private void DrawVillageDetailedInspector()
        {
            var camp = world.Camps.FirstOrDefault(c => c.Id == villageInspectorCampId && c.Player == 0);
            if (camp == null || !CultivationEconomy.EconomyEnabled(camp)) { villageInspectorOpen = false; return; }
            var draft = planned.TryGetValue(camp.Id, out var pending) && pending.Kind == OrderKind.Economy ? pending.Economy.Copy() : EconomicIntent.From(camp);
            string before = draft.Stamp;
            float width = Mathf.Min(1040, Screen.width - 32), height = Mathf.Min(820, Screen.height - 32);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(.02f, .03f, .03f, .88f));
            var panel = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            Fill(panel, new Color(.08f, .095f, .088f, .98f));
            GUI.BeginGroup(panel);
            GUI.Label(new Rect(18, 12, width - 170, 28), "SETTLEMENT ECONOMY · changes apply on turn commit", title);
            if (Button(new Rect(width - 120, 10, 100, 30), "CLOSE")) villageInspectorOpen = false;
            var ownedLivestock = observation.Herds.Where(h => h.OwnedDomestic && h.DomesticCamp == camp.Id).OrderBy(h => h.Id).ToArray();
            float contentHeight = 1012 + (CultivationEconomy.HusbandryEnabled(camp) ? ownedLivestock.Length * 54 : 0);
            villageInspectorScroll = GUI.BeginScrollView(new Rect(12, 52, width - 24, height - 64), villageInspectorScroll, new Rect(0, 0, width - 44, contentHeight));
            float y = 4;
            GUI.Label(new Rect(4, y, width - 60, 76),
                $"Population {camp.Population} · QoL {camp.QoL:P0} · food {camp.FoodNeedsMetRatio:P0} · water {camp.WaterNeedsMetRatio:P0}\n" +
                $"Clothing {camp.ClothPerCapita:P0} · consumed {camp.FabricConsumed:F2} · stock {camp.Inventory.Cloth:F2}\n" +
                $"Housing {camp.HousingPerCapita:P0} · {camp.Inventory.HousingUnits:F2} units · capacity {camp.Inventory.HousingUnits * world.Config.Neolithic.HousingPeoplePerUnit:F1}\n" +
                $"Expectations {camp.RudimentaryExpectation:P0} · stationary {camp.StationaryYears:F1} years · shortfalls are graded; automatic fission is deferred.", text);
            y += 90;
            if (CultivationEconomy.HusbandryEnabled(camp))
            {
                int animals = ownedLivestock.Sum(h => h.Count.Representative);
                int herders = draft.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population);
                double accepted = ownedLivestock.Sum(h => h.AcceptedFood), potential = ownedLivestock.Sum(h => h.PotentialFood);
                GUI.Label(new Rect(4, y, width - 60, 28), $"Last generation: livestock {animals} · AnimalFood {accepted:F1}/{potential:F1} accepted potential · drafted Herders {herders} · baseline capacity {world.Config.AnimalsPerHerder:F0} animals per Herder", small); y += 32;
                foreach (var herd in ownedLivestock)
                {
                    GUI.Label(new Rect(4, y, width - 60, 46), $"{herd.Species} herd {herd.Count.Representative} · food {herd.FoodNutrition:P0} · water {herd.WaterNutrition:P0} · management {herd.ManagementFulfillment:P0} · deficit {herd.DeficitTurns} · output {herd.AcceptedFood:F1}/{herd.PotentialFood:F1} · births {herd.NewbornAnimals} · deaths {herd.StarvationDeaths}", small);
                    y += 54;
                }
                GUI.Label(new Rect(4, y, width - 150, 24), "Breeding intensity: " + draft.ManagedBreedingIntensity.ToString("F2") + " (0 stops intentional reproduction; 1 uses biological rate)", small);
                if (Button(new Rect(width - 130, y, 52, 22), "−")) draft.ManagedBreedingIntensity = Math.Max(0, draft.ManagedBreedingIntensity - .05);
                if (Button(new Rect(width - 72, y, 52, 22), "+")) draft.ManagedBreedingIntensity = Math.Min(1, draft.ManagedBreedingIntensity + .05);
                y += 30;
            }
            float col = (width - 70) / 3, x = 4, wy = y;
            GUI.Label(new Rect(x, wy, col, 24), "WORKFORCE", section); wy += 28;
            foreach (string role in WorkforceRoles.All.Where(r => r != WorkforceRoles.Gatherer))
            {
                int count = draft.Assignments.Where(a => a.Role == role).Sum(a => a.Population);
                int free = camp.Population - draft.Assignments.Sum(a => a.Population);
                string tech = EconomicIntents.RequiredTechnology(role);
                bool available = role == WorkforceRoles.Farmer ? CultivationEconomy.Enabled(camp) : role == WorkforceRoles.Herder ? CultivationEconomy.HusbandryEnabled(camp) : tech == null || camp.People.HasTech(tech);
                GUI.Label(new Rect(x, wy, col - 78, 24), WorkforceRoles.Label(role) + ": " + count + (available ? "" : " (locked)"), small);
                if (Button(new Rect(x + col - 74, wy, 32, 22), "−") && count > 0)
                {
                    var a = draft.Assignments.First(v => v.Role == role); a.Population--; if (a.Population == 0) draft.Assignments.Remove(a);
                    draft.AutomationTurns = 0; draft.Labor = null;
                }
                if (Button(new Rect(x + col - 38, wy, 32, 22), "+") && available && free > 0)
                {
                    var a = draft.Assignments.FirstOrDefault(v => v.Role == role);
                    if (a == null) draft.Assignments.Add(new PopulationAssignment { Role = role, Population = 1 }); else a.Population++;
                    draft.AutomationTurns = 0; draft.Labor = null;
                }
                wy += 28;
            }
            GUI.Label(new Rect(x, wy, col, 36), "Unassigned food/material gatherers: " + (camp.Population - draft.Assignments.Sum(a => a.Population)), small); wy += 40;
            if (Button(new Rect(x, wy, col - 8, 28), "Reset to gatherers")) { draft.Assignments.Clear(); draft.AutomationTurns = 0; draft.Labor = null; } wy += 34;
            if (Button(new Rect(x, wy, col - 8, 28), "Allocate once")) { draft.AutomationTurns = 0; draft = EconomicPlanner.Plan(world, camp, draft); } wy += 34;
            GUI.Label(new Rect(x, wy, col - 8, 42), "Automation: " + (draft.AutomationTurns == 0 ? "Off" : draft.AutomationTurns < 0 ? "Until disabled" : draft.AutomationTurns + " turns remaining"), small); wy += 44;
            if (Button(new Rect(x, wy, 65, 26), "Off")) draft.AutomationTurns = 0;
            if (Button(new Rect(x + 70, wy, col - 78, 26), "Until disabled")) { draft.AutomationTurns = -1; draft = EconomicPlanner.Plan(world, camp, draft); } wy += 32;
            if (Button(new Rect(x, wy, col - 80, 26), "Next " + Math.Max(1, villageAutomationTurns) + " turns")) { draft.AutomationTurns = Math.Max(1, villageAutomationTurns); draft = EconomicPlanner.Plan(world, camp, draft); }
            if (Button(new Rect(x + col - 76, wy, 30, 26), "−")) villageAutomationTurns = Math.Max(1, villageAutomationTurns - 1);
            if (Button(new Rect(x + col - 40, wy, 30, 26), "+")) villageAutomationTurns = Math.Min(999, villageAutomationTurns + 1); wy += 36;
            GUI.Label(new Rect(x, wy, col - 8, 96), "Replans each turn against demand, stocks and unlocked jobs. Manual workforce edits turn automation off.\n" + (draft.Labor == null ? "" : $"Reserved material gathering: {draft.Labor.MaterialGatherers:F1} workers\nExpansion: {draft.Labor.ExpansionFarmers:F1} farmers"), small);
            x += col + 10; float cy = y;
            GUI.Label(new Rect(x, cy, col, 24), "PRODUCTION PRIORITIES", section); cy += 28;
            DrawEconomicValue("Hand tools", ref draft.Priorities.HandTools, x, ref cy, col, .05);
            DrawEconomicValue("Pots", ref draft.Priorities.Pots, x, ref cy, col, .05);
            DrawEconomicValue("Fabric", ref draft.Priorities.Cloth, x, ref cy, col, .05);
            DrawEconomicValue("Farm tools", ref draft.Priorities.FarmTools, x, ref cy, col, .05);
            DrawEconomicValue("Housing", ref draft.Priorities.Housing, x, ref cy, col, .05);
            DrawEconomicValue("Improved tools", ref draft.Priorities.Tools, x, ref cy, col, .05);
            cy += 12;
            double potYears = world.Config.TurnDays / world.Config.DaysPerYear;
            GUI.Label(new Rect(x, cy, col - 8, 22), "Pots target: " + NeolithicProduction.TargetPots(world, camp, potYears).ToString("F2"), small); cy += 28;
            double farmToolForecast = CultivationEconomy.ForecastFarmToolWorkerYears(world, camp, potYears) / world.Config.Neolithic.FarmToolFarmerYearsPerUnit;
            GUI.Label(new Rect(x, cy, col - 8, 22), "Farm tools forecast: " + farmToolForecast.ToString("F2"), small); cy += 28;
            double storageIntervals = world.Config.TurnDays / world.Config.BaseDays;
            double potStorage = camp.Inventory.Pots * world.Config.Neolithic.PotStorageFoodPerUnit;
            double storageCapacity = NeolithicProduction.StorageCapacity(world, camp, storageIntervals);
            GUI.Label(new Rect(x, cy, col - 8, 132), $"Stone {camp.Inventory.Stone:F2}\nClay {camp.Inventory.Clay:F2}\nWood {camp.Inventory.Wood:F2}\nPots {camp.Inventory.Pots:F2} (+{potStorage:F2} storage)\nFarm tools {camp.Inventory.FarmTools:F2}\nStorage capacity {storageCapacity:F2}\nHand tools {camp.Inventory.HandTools:F2}\nImproved tools {camp.Inventory.Tools:F2}", small); cy += 144;
            GUI.Label(new Rect(x, cy, col - 8, 100), $"Crafting consumes tools with useful output. Pots add {world.Config.Neolithic.PotStorageFoodPerUnit:F0} food storage each and retain {100 * (1 - world.Config.Neolithic.PotDecayPerReferenceTurn):F0}% per {world.Config.Neolithic.ReferenceTurnYears:F0}-year reference turn. Stone Farm Tools reduce maintenance and expansion labor for covered field work; they do not multiply harvest or ecology.", small);
            x += col + 10; float fy = y;
            GUI.Label(new Rect(x, fy, col, 24), "FIELDS & SOURCING", section); fy += 28;
            foreach (var t in observation.Tiles.Values.Where(t => t.Cultivable && world.Map.Distance(camp.Tile, t.Id) <= 1).OrderBy(t => t.Id))
            {
                GUI.Label(new Rect(x, fy, col - 90, 22), $"Tile {t.Id}: q {t.Cultivation:F3}", small);
                if (Button(new Rect(x + col - 86, fy, 78, 22), draft.Focus == t.Id ? "Focused" : "Focus")) draft.Focus = draft.Focus == t.Id ? -1 : t.Id;
                fy += 28;
            }
            fy += 12;
            DrawEconomicValue("Stone priority", ref draft.StonePriority, x, ref fy, col, .1);
            DrawEconomicValue("Clay priority", ref draft.ClayPriority, x, ref fy, col, .1);
            DrawEconomicValue("Wood priority", ref draft.WoodPriority, x, ref fy, col, .1);
            var known = observation.Settlements.First(s => s.Id == camp.Id).MaterialSources;
            foreach (var material in MaterialEconomy.Materials)
            {
                GUI.Label(new Rect(x, fy, col - 8, 24), material + ": " + (known.Any(s => s.Material == material && s.Available > 0) ? "known local source" : "no accessible source"), small); fy += 28;
            }
            GUI.Label(new Rect(x, fy + 10, col - 8, 70), "Irrigation construction, Plough effects and advanced technology effects remain deferred.", small);
            GUI.EndScrollView(); GUI.EndGroup();
            if (draft.Stamp != before) Queue(new Order(OrderKind.Economy, camp.Id) { Economy = draft });
        }
        private int villageAutomationTurns = 5;
        private void DrawEconomicValue(string label, ref double value, float x, ref float y, float width, double step)
        {
            GUI.Label(new Rect(x, y, width - 76, 22), label + ": " + value.ToString("F2"), small);
            if (Button(new Rect(x + width - 72, y, 30, 22), "−")) value = Math.Max(0, value - step);
            if (Button(new Rect(x + width - 38, y, 30, 22), "+")) value += step;
            y += 28;
        }
    }
}
