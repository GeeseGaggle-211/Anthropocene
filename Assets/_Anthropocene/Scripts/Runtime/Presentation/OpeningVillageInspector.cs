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
            // Dim background
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.03f, 0.03f, 0.88f));

            var village = world.Camps.FirstOrDefault(c => c.Id == villageInspectorCampId && c.Player == 0);
            if (village == null || !village.IsVillage)
            {
                villageInspectorOpen = false;
                return;
            }

            float width = Mathf.Min(1020, Screen.width - 32);
            float height = Mathf.Min(820, Screen.height - 32);
            float left = (Screen.width - width) / 2;
            float top = (Screen.height - height) / 2;
            var windowRect = new Rect(left, top, width, height);

            Fill(windowRect, new Color(0.08f, 0.095f, 0.088f, 0.98f));

            GUI.BeginGroup(windowRect);

            // 1. Header Bar
            Fill(new Rect(0, 0, width, 54), new Color(0.13f, 0.155f, 0.14f));
            GUI.Label(new Rect(18, 8, width - 180, 26), $"VILLAGE MANAGEMENT  ·  TILE {village.Tile}", title);
            string subHeader = $"Turn {world.Turn}  ·  Permanent Settlement (Age: {village.VillageTurns * 20} years)  ·  Population: {village.Population}";
            GUI.Label(new Rect(18, 32, width - 180, 20), subHeader, small);

            if (Button(new Rect(width - 130, 11, 115, 32), "CLOSE [ESC]", true))
            {
                villageInspectorOpen = false;
            }

            // Scrollable main content
            villageInspectorScroll = GUI.BeginScrollView(
                new Rect(0, 56, width, height - 60),
                villageInspectorScroll,
                new Rect(0, 0, width - 20, 840)
            );

            float y = 10;
            float contentWidth = width - 36;

            // 2. QoL & Fission Dashboard
            DrawVillageDashboard(village, ref y, contentWidth);

            // 3. Three Columns: Workforce (left), Crafting & Demand (center), Cultivation & Irrigation (right)
            float colWidth = (contentWidth - 20) / 3f;
            float col1X = 14;
            float col2X = col1X + colWidth + 10;
            float col3X = col2X + colWidth + 10;

            float yCol1 = y;
            DrawVillageWorkforceColumn(village, col1X, ref yCol1, colWidth);

            float yCol2 = y;
            DrawVillageCraftingColumn(village, col2X, ref yCol2, colWidth);

            float yCol3 = y;
            DrawVillageFootprintColumn(village, col3X, ref yCol3, colWidth);

            GUI.EndScrollView();
            GUI.EndGroup();
        }

        private void DrawVillageDashboard(Camp village, ref float y, float width)
        {
            var inv = village.Inventory;
            float panelHeight = 118;
            Fill(new Rect(14, y, width, panelHeight), new Color(0.11f, 0.135f, 0.125f));

            // Dashboard title & population
            GUI.Label(new Rect(24, y + 8, width - 20, 22), $"QUALITY OF LIFE & STABILITY METER  ·  RESIDENTS: {village.Population}  ({village.GathererPopulation} gatherers, {village.Workforce?.Assigned ?? 0} assigned)", section);

            // Row 1: QoL component meters
            double domesticExpectation = NeolithicProduction.DomesticExpectationFactor(village);
            string qolBreakdown =
                $"Overall QoL: {village.QoL:P1}   |   Food Needs: {village.FoodNeedsMetRatio:P0} (Plant: {village.PlantFood:F0} · Animal: {village.AnimalFood:F0} · Fish: {village.AquaticFood:F0})\n" +
                $"Cloth Satisfaction: {village.ClothPerCapita:P0} (Stock: {inv.Cloth:F0} · Mature Expectation: {village.Population * domesticExpectation:F0})   |   " +
                $"Shelter: {village.HousingPerCapita:P0} (Capacity: {inv.Houses * 8} / {village.Population} people · {inv.Houses} houses)\n" +
                $"Domestic Expectation Ramp: {domesticExpectation:P0} (Turn {Math.Min(10, village.VillageTurns)}/10 to full maturity)";
            GUI.Label(new Rect(24, y + 32, width - 40, 50), qolBreakdown, text);

            // Row 2: Fission status
            if (village.FissionWilling >= 25.0)
            {
                Fill(new Rect(20, y + 86, width - 32, 24), new Color(0.45f, 0.14f, 0.12f, 0.9f));
                GUI.Label(new Rect(26, y + 90, width - 44, 20), $"⚠ DANGER: FISSION IMMINENT! {village.FissionWilling:F0} unfulfilled residents will splinter off into a new settlement on turn commit!", badge);
            }
            else
            {
                Fill(new Rect(20, y + 86, width - 32, 24), new Color(0.13f, 0.20f, 0.15f, 0.9f));
                GUI.Label(new Rect(26, y + 90, width - 44, 20), $"✔ STABLE: Fission risk {village.FissionWilling:F1} / 25.0 threshold. Domestic needs are satisfactorily met.", small);
            }

            y += panelHeight + 12;
        }

        private void DrawVillageWorkforceColumn(Camp village, float x, ref float y, float width)
        {
            float panelHeight = 560;
            Fill(new Rect(x, y, width, panelHeight), new Color(0.10f, 0.12f, 0.11f));
            float innerY = y + 10;
            float innerW = width - 20;
            float leftX = x + 10;

            GUI.Label(new Rect(leftX, innerY, innerW, 22), "WORKFORCE ROLES", section); innerY += 24;
            GUI.Label(new Rect(leftX, innerY, innerW, 36), $"Unassigned Gatherers: {village.GathererPopulation}\n(Gather wild food, freshwater, and craft supplies)", small); innerY += 40;

            // Farmers row
            int farmers = village.Workforce?.Assignments?.FirstOrDefault(a => a.Role == WorkforceRoles.Farmer)?.Population ?? 0;
            GUI.Label(new Rect(leftX, innerY, innerW, 20), $"Farmers: {farmers} workers", text); innerY += 22;
            GUI.Label(new Rect(leftX, innerY, innerW, 18), "Cultivates fields & maintains footprint", small); innerY += 20;

            if (Button(new Rect(leftX, innerY, 34, 26), "−5")) village.Workforce.Release(WorkforceRoles.Farmer, Math.Min(5, farmers), village.Population);
            if (Button(new Rect(leftX + 38, innerY, 30, 26), "−")) village.Workforce.Release(WorkforceRoles.Farmer, Math.Min(1, farmers), village.Population);
            if (Button(new Rect(leftX + 72, innerY, 30, 26), "+")) village.Workforce.Assign(WorkforceRoles.Farmer, Math.Min(1, village.GathererPopulation), village.Population);
            if (Button(new Rect(leftX + 106, innerY, 34, 26), "+5")) village.Workforce.Assign(WorkforceRoles.Farmer, Math.Min(5, village.GathererPopulation), village.Population);
            innerY += 32;

            // General Crafters row
            int crafters = village.Workforce?.Assignments?.FirstOrDefault(a => a.Role == WorkforceRoles.Craftsperson)?.Population ?? 0;
            GUI.Label(new Rect(leftX, innerY, innerW, 20), $"General Crafters: {crafters} workers", text); innerY += 22;
            GUI.Label(new Rect(leftX, innerY, innerW, 18), "Produces tools, pots, cloth, and housing", small); innerY += 20;

            if (Button(new Rect(leftX, innerY, 34, 26), "−5")) village.Workforce.Release(WorkforceRoles.Craftsperson, Math.Min(5, crafters), village.Population);
            if (Button(new Rect(leftX + 38, innerY, 30, 26), "−")) village.Workforce.Release(WorkforceRoles.Craftsperson, Math.Min(1, crafters), village.Population);
            if (Button(new Rect(leftX + 72, innerY, 30, 26), "+")) village.Workforce.Assign(WorkforceRoles.Craftsperson, Math.Min(1, village.GathererPopulation), village.Population);
            if (Button(new Rect(leftX + 106, innerY, 34, 26), "+5")) village.Workforce.Assign(WorkforceRoles.Craftsperson, Math.Min(5, village.GathererPopulation), village.Population);
            innerY += 34;

            // Specialists header & rows
            GUI.Label(new Rect(leftX, innerY, innerW, 20), "SPECIALIST GUILDS (100% eff)", section); innerY += 22;
            var soc = village.People;

            foreach (var role in WorkforceRoles.Specialists)
            {
                bool unlocked = soc.UnlockedSpecialists != null && soc.UnlockedSpecialists.Contains(role);
                int count = village.Workforce?.Assignments?.FirstOrDefault(a => a.Role == role)?.Population ?? 0;

                if (unlocked)
                {
                    GUI.Label(new Rect(leftX, innerY, innerW - 74, 20), $"{role}: {count}", text);
                    if (Button(new Rect(leftX + innerW - 70, innerY, 32, 22), "−")) village.Workforce.Release(role, Math.Min(1, count), village.Population);
                    if (Button(new Rect(leftX + innerW - 34, innerY, 32, 22), "+")) village.Workforce.Assign(role, Math.Min(1, village.GathererPopulation), village.Population);
                    innerY += 24;
                }
                else
                {
                    double exp = soc.CraftExperience.TryGetValue(role, out double v) ? v : 0.0;
                    GUI.Label(new Rect(leftX, innerY, innerW, 20), $"{role}: Locked ({exp:F0}/5000 exp)", small);
                    innerY += 22;
                }
            }

            innerY += 10;
            GUI.Label(new Rect(leftX, innerY, innerW, 20), "WORKFORCE PRESETS", section); innerY += 22;

            if (Button(new Rect(leftX, innerY, innerW / 2 - 4, 26), "Balanced"))
            {
                village.Workforce.Assignments.Clear();
                int farmTarget = (int)(village.Population * 0.40);
                int craftTarget = (int)(village.Population * 0.25);
                village.Workforce.Assign(WorkforceRoles.Farmer, farmTarget, village.Population);
                village.Workforce.Assign(WorkforceRoles.Craftsperson, craftTarget, village.Population);
            }
            if (Button(new Rect(leftX + innerW / 2 + 4, innerY, innerW / 2 - 4, 26), "Farm Heavy"))
            {
                village.Workforce.Assignments.Clear();
                int farmTarget = (int)(village.Population * 0.60);
                int craftTarget = (int)(village.Population * 0.15);
                village.Workforce.Assign(WorkforceRoles.Farmer, farmTarget, village.Population);
                village.Workforce.Assign(WorkforceRoles.Craftsperson, craftTarget, village.Population);
            }
            innerY += 30;

            if (Button(new Rect(leftX, innerY, innerW / 2 - 4, 26), "Craft Heavy"))
            {
                village.Workforce.Assignments.Clear();
                int farmTarget = (int)(village.Population * 0.25);
                int craftTarget = (int)(village.Population * 0.45);
                village.Workforce.Assign(WorkforceRoles.Farmer, farmTarget, village.Population);
                village.Workforce.Assign(WorkforceRoles.Craftsperson, craftTarget, village.Population);
            }
            if (Button(new Rect(leftX + innerW / 2 + 4, innerY, innerW / 2 - 4, 26), "Reset All"))
            {
                village.Workforce.Assignments.Clear();
            }

            y += panelHeight;
        }

        private void DrawVillageCraftingColumn(Camp village, float x, ref float y, float width)
        {
            float panelHeight = 560;
            Fill(new Rect(x, y, width, panelHeight), new Color(0.10f, 0.12f, 0.11f));
            float innerY = y + 10;
            float innerW = width - 20;
            float leftX = x + 10;

            GUI.Label(new Rect(leftX, innerY, innerW, 22), "CRAFT DEMAND & STORES", section); innerY += 24;

            var inv = village.Inventory;
            var pri = village.CraftingPriorities ?? (village.CraftingPriorities = new CraftingPriorities());

            // Stores summary
            string stockSummary =
                $"Hand Tools: {inv.HandTools:F0} · Pots: {inv.Pots:F0} · Cloth: {inv.Cloth:F0}\n" +
                $"Farm Tools: {inv.FarmTools:F0} · Houses: {inv.Houses:F0} (Units: {inv.HousingUnits:F0})\n" +
                $"Raw: Stone {inv.Stone:F0} · Clay {inv.Clay:F0} · Wood {inv.Wood:F0}";
            GUI.Label(new Rect(leftX, innerY, innerW, 44), stockSummary, small); innerY += 48;

            GUI.Label(new Rect(leftX, innerY, innerW, 20), "PRODUCTION PRIORITY SLIDERS", section); innerY += 22;

            double sum = pri.HandTools + pri.Pots + pri.Cloth + pri.FarmTools + pri.Housing;
            if (sum < 1e-6) sum = 1.0;

            DrawPriorityRow("Hand Tools", ref pri.HandTools, sum, leftX, ref innerY, innerW);
            DrawPriorityRow("Storage Pots", ref pri.Pots, sum, leftX, ref innerY, innerW);
            DrawPriorityRow("Woven Cloth", ref pri.Cloth, sum, leftX, ref innerY, innerW);
            DrawPriorityRow("Farm Tools", ref pri.FarmTools, sum, leftX, ref innerY, innerW);
            DrawPriorityRow("Housing Units", ref pri.Housing, sum, leftX, ref innerY, innerW);

            innerY += 10;
            GUI.Label(new Rect(leftX, innerY, innerW, 20), "DEMAND PRESETS", section); innerY += 22;

            if (Button(new Rect(leftX, innerY, innerW, 26), "Balanced Demand (15/10/45/10/20)"))
            {
                pri.HandTools = 0.15; pri.Pots = 0.10; pri.Cloth = 0.45; pri.FarmTools = 0.10; pri.Housing = 0.20;
            }
            innerY += 30;

            if (Button(new Rect(leftX, innerY, innerW, 26), "Housing Push (10/5/25/10/50)"))
            {
                pri.HandTools = 0.10; pri.Pots = 0.05; pri.Cloth = 0.25; pri.FarmTools = 0.10; pri.Housing = 0.50;
            }
            innerY += 30;

            if (Button(new Rect(leftX, innerY, innerW, 26), "Cloth Focus (10/5/70/5/10)"))
            {
                pri.HandTools = 0.10; pri.Pots = 0.05; pri.Cloth = 0.70; pri.FarmTools = 0.05; pri.Housing = 0.10;
            }
            innerY += 30;

            double potMult = NeolithicProduction.StorageCapacityMultiplier(village);
            double farmMult = NeolithicProduction.FarmerMaintenanceMultiplier(village);
            string bonuses = $"Active Bonuses:\n• Tool bonus: {(inv.Tools > 0 ? "+10% speed" : "None")}\n• Storage pot buffer: {potMult:F1}x capacity\n• Farm tool maintenance: {farmMult:F1}x capacity";
            GUI.Label(new Rect(leftX, innerY, innerW, 58), bonuses, small);

            y += panelHeight;
        }

        private void DrawPriorityRow(string label, ref double value, double sum, float x, ref float y, float width)
        {
            double share = value / sum;
            GUI.Label(new Rect(x, y, width - 84, 20), $"{label}: {share:P0}", text);

            if (Button(new Rect(x + width - 80, y, 36, 20), "−"))
            {
                value = Math.Max(0.0, value - 0.05);
            }
            if (Button(new Rect(x + width - 40, y, 36, 20), "+"))
            {
                value = Math.Min(1.0, value + 0.05);
            }
            y += 24;
        }

        private void DrawVillageFootprintColumn(Camp village, float x, ref float y, float width)
        {
            float panelHeight = 560;
            Fill(new Rect(x, y, width, panelHeight), new Color(0.10f, 0.12f, 0.11f));
            float innerY = y + 10;
            float innerW = width - 20;
            float leftX = x + 10;

            int[] footprint = world.Map.Radius(village.Tile, 1).Where(id => world.Tiles[id].TraversableLand).ToArray();
            double avgQ = footprint.Length > 0 ? footprint.Average(tid => world.Tiles[tid].Cultivation) : 0.0;

            GUI.Label(new Rect(leftX, innerY, innerW, 22), "CULTIVATION FOOTPRINT", section); innerY += 24;
            GUI.Label(new Rect(leftX, innerY, innerW, 20), $"Avg Footprint Intensity: q = {avgQ:F2}", text); innerY += 22;
            GUI.Label(new Rect(leftX, innerY, innerW, 30), "Radius 1 tiles around village. Select focus tile or dig irrigation canals.", small); innerY += 34;

            for (int i = 0; i < footprint.Length; i++)
            {
                int tid = footprint[i];
                var t = world.Tiles[tid];
                bool isCenter = tid == village.Tile;
                bool isFocus = village.CultivationFocusTile == tid;

                string centerTag = isCenter ? " [Center]" : "";
                string irrigTag = t.Irrigated ? " [≈ Irrigated]" : "";
                string tileTitle = $"Tile {tid}{centerTag}: q={t.Cultivation:F2}{irrigTag}";
                GUI.Label(new Rect(leftX, innerY, innerW - 130, 20), tileTitle, small);

                // Focus toggle button
                if (Button(new Rect(leftX + innerW - 128, innerY, 60, 20), isFocus ? "★ Focus" : "Focus", isFocus))
                {
                    village.CultivationFocusTile = isFocus ? -1 : tid;
                }

                // Dig canal button
                bool canIrrigate = NeolithicProduction.CanIrrigateTile(world, village, tid, out _);
                if (!t.Irrigated)
                {
                    bool prevEnabled = GUI.enabled;
                    GUI.enabled = canIrrigate;
                    if (Button(new Rect(leftX + innerW - 64, innerY, 60, 20), "Canal"))
                    {
                        t.Irrigated = true;
                        if (!village.People.HasTech(NeolithicTech.Irrigation))
                        {
                            village.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
                        }
                    }
                    GUI.enabled = prevEnabled;
                }
                else
                {
                    GUI.Label(new Rect(leftX + innerW - 64, innerY, 60, 20), "Active", centered);
                }

                innerY += 24;
            }

            innerY += 10;
            GUI.Label(new Rect(leftX, innerY, innerW, 20), "CONVERGENCE TECHNOLOGIES", section); innerY += 22;

            var soc = village.People;
            double wheelExp = soc.CraftExperience.TryGetValue(NeolithicTech.WheelAndAxle, out double wExp) ? wExp : 0.0;
            string wheelStatus = soc.HasTech(NeolithicTech.WheelAndAxle) ? "Mastered" : $"{wheelExp:F0}/10000";
            GUI.Label(new Rect(leftX, innerY, innerW, 20), $"Wheel and Axle: {wheelStatus}", text); innerY += 20;

            double copperExp = soc.CraftExperience.TryGetValue(NeolithicTech.CopperWorking, out double cExp) ? cExp : 0.0;
            string copperStatus = soc.HasTech(NeolithicTech.CopperWorking) ? "Mastered" : $"{copperExp:F0}/10000";
            GUI.Label(new Rect(leftX, innerY, innerW, 20), $"Copper Working: {copperStatus}", text); innerY += 20;

            GUI.Label(new Rect(leftX, innerY, innerW, 20), $"Plough: {(soc.HasTech(NeolithicTech.Plough) ? "Mastered (q_T up to 0.50)" : "Unresearched")}", small); innerY += 18;
            GUI.Label(new Rect(leftX, innerY, innerW, 20), $"Kiln Firing: {(soc.HasTech(NeolithicTech.KilnFiring) ? "Mastered (+50% copper bonus)" : "Unresearched")}", small); innerY += 18;

            y += panelHeight;
        }
    }
}
