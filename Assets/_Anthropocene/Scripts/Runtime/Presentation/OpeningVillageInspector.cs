using System;
using System.Collections.Generic;
using System.Globalization;
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
            if (camp == null || !CultivationEconomy.EconomyEnabled(world, camp)) { villageInspectorOpen = false; return; }
            var draft = planned.TryGetValue(camp.Id, out var pending) && pending.Kind == OrderKind.Economy ? pending.Economy.Copy() : EconomicIntent.From(camp);
            string before = draft.Stamp;
            bool husbandry = CultivationEconomy.HusbandryEnabled(camp);
            bool fishing = FishingEconomy.FeatureEnabled(world, camp);
            bool civicEnabled = CivicGovernance.Enabled(world.Config);
            bool sanitationEnabled = CivicGovernance.SanitationEnabled(world.Config);
            float width = Mathf.Min(1040, Screen.width - 32), height = Mathf.Min(820, Screen.height - 32);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(.02f, .03f, .03f, .88f));
            var panel = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            Fill(panel, new Color(.08f, .095f, .088f, .98f));
            GUI.BeginGroup(panel);
            GUI.Label(new Rect(18, 12, width - 170, 28), "SETTLEMENT ECONOMY · changes apply on turn commit", title);
            if (Button(new Rect(width - 120, 10, 100, 30), "CLOSE")) villageInspectorOpen = false;
            var ownedLivestock = observation.Herds.Where(h => h.OwnedDomestic && h.DomesticCamp == camp.Id).OrderBy(h => h.Id).ToArray();
            float scrollContentWidth = width - 44;
            float col = (width - 70) / 3;
            double potYears = world.Config.TurnDays / world.Config.DaysPerYear;
            var management = HerdingTooling.Plan(world, camp, potYears, draft.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population));
            string pastoralDescription = $"Animal Fabric {camp.Inventory.AnimalFabric:F2}: 1 Animal Product makes 1 unit before tool use; it shares Cloth's clothing need and Light Housing spends 1 per unit.\n" +
                $"Light Housing {Math.Max(0, camp.Inventory.LightHousingUnits):F2}: portable with the camp; {world.Config.Neolithic.LightHousingPeoplePerUnit:F0} people supported per unit (fixed Housing shelters {world.Config.Neolithic.HousingPeoplePerUnit:F0}). Fixed shelter stays at its original site.\n" +
                $"Herding Tools {camp.Inventory.HerdingTools:F2}: 1 Wood + 1 Stone each; {world.Config.Neolithic.HerdingToolCoverageYearsPerUnit:F0} Herder-years per tool. Plan capacity {management.Capacity:F1} / {management.Animals:F0} animals ({management.Fulfillment:P0}); primitive {world.Config.AnimalsPerHerder:F2}, equipped {world.Config.Neolithic.StoneHerdingToolAnimalsPerHerder:F2} animals per Herder.";
            float pastoralDescriptionHeight = husbandry ? small.CalcHeight(new GUIContent(pastoralDescription), col - 8) + 8 : 0;
            float livestockRows = husbandry ? ownedLivestock.Length * 54 : 0;
            float fishingRows = fishing ? observation.FishingGrounds.Count(g => world.Map.Distance(camp.Tile, g.Tile) <= FishingEconomy.GroundRange + 1) * 24 : 0;
            float civicPanelExtra = civicEnabled ? (sanitationEnabled ? 430f : 380f) : (world.Config.FissionEnabled ? 110f : 0f);
            float contentHeight = Mathf.Max(1370 + livestockRows + fishingRows, 940 + livestockRows + pastoralDescriptionHeight + fishingRows) + (world.Config.Agriculture == null ? 0 : 240) + civicPanelExtra;
            villageInspectorScroll = GUI.BeginScrollView(new Rect(12, 52, width - 24, height - 64), villageInspectorScroll, new Rect(0, 0, scrollContentWidth, contentHeight));
            float y = 4;
            var inventory = camp.Inventory;
            double fixedHousing = NeolithicProduction.FixedHousingAt(world, camp.Id, camp.Tile, world.Days);
            double portableHousing = Math.Max(0, inventory.LightHousingUnits);
            double currentHousing = NeolithicProduction.HousingSupply(world, camp, world.Days);
            double shelterPeople = NeolithicProduction.HousingCapacity(world, camp, world.Days).Total;
            string clothingStock = husbandry
                ? $"Clothing {camp.ClothPerCapita:P0} · consumed {camp.FabricConsumed:F2} · stock {inventory.Cloth + inventory.AnimalFabric:F2} (plant {inventory.Cloth:F2}, animal {inventory.AnimalFabric:F2})"
                : $"Clothing {camp.ClothPerCapita:P0} · consumed {camp.FabricConsumed:F2} · stock {inventory.Cloth:F2}";
            string housingStock = husbandry
                ? $"Shelter {camp.HousingPerCapita:P0} · current site {currentHousing:F2} units / {shelterPeople:F1} capacity (fixed {fixedHousing:F2}, portable {portableHousing:F2})"
                : $"Housing {camp.HousingPerCapita:P0} · current site {currentHousing:F2} units · capacity {shelterPeople:F1}";
            GUI.Label(new Rect(4, y, scrollContentWidth - 16, husbandry ? 102 : 76),
                $"Population {camp.Population} · QoL {camp.QoL:P0} · food {camp.FoodNeedsMetRatio:P0} · water {camp.WaterNeedsMetRatio:P0}\n" +
                clothingStock + "\n" + housingStock + "\n" +
                $"Expectations {camp.RudimentaryExpectation:P0} · stationary {camp.StationaryYears:F1} years · shortfalls are graded; automatic fission is deferred.", text);
            y += husbandry ? 116 : 90;
            if (husbandry)
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
                float contentWidth = scrollContentWidth;
                GUI.Label(new Rect(4, y, contentWidth - 150, 24), "Breeding intensity: " + draft.ManagedBreedingIntensity.ToString("F2") + " (0 stops intentional reproduction; 1 uses biological rate)", small);
                if (Button(new Rect(contentWidth - 130, y, 52, 22), "−")) draft.ManagedBreedingIntensity = Math.Max(0, draft.ManagedBreedingIntensity - .05);
                if (Button(new Rect(contentWidth - 72, y, 52, 22), "+")) draft.ManagedBreedingIntensity = Math.Min(1, draft.ManagedBreedingIntensity + .05);
                y += 30;
            }
            float x = 4, wy = y;
            GUI.Label(new Rect(x, wy, col, 24), "WORKFORCE", section); wy += 28;
            foreach (string role in WorkforceRoles.All.Where(r => r != WorkforceRoles.Gatherer && (civicEnabled || r != WorkforceRoles.Steward)))
            {
                int count = draft.Assignments.Where(a => a.Role == role).Sum(a => a.Population);
                int free = camp.Population - draft.Assignments.Sum(a => a.Population);
                string tech = EconomicIntents.RequiredTechnology(role);
                bool available = role == WorkforceRoles.Farmer ? CultivationEconomy.Enabled(camp)
                    : role == WorkforceRoles.Herder ? husbandry
                    : role == WorkforceRoles.Fisher ? fishing
                    : role == WorkforceRoles.Steward ? CivicGovernance.StewardAvailable(world, camp)
                    : tech == null || camp.People.HasTech(tech);
                GUI.Label(new Rect(x, wy, col - 78, 24), WorkforceRoles.Label(role, camp) + ": " + count + (available ? "" : " (locked)"), small);
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
            if (Button(new Rect(x, wy, col - 8, 28), "Allocate once")) { draft.AutomationTurns = 0; draft = EconomicAllocationStrategy.Plan(world, camp, draft, out _); } wy += 34;
            GUI.Label(new Rect(x, wy, col - 8, 42), "Automation: " + (draft.AutomationTurns == 0 ? "Off" : draft.AutomationTurns < 0 ? "Until disabled" : draft.AutomationTurns + " turns remaining"), small); wy += 44;
            if (Button(new Rect(x, wy, 65, 26), "Off")) draft.AutomationTurns = 0;
            if (Button(new Rect(x + 70, wy, col - 78, 26), "Until disabled")) { draft.AutomationTurns = -1; draft = EconomicAllocationStrategy.Plan(world, camp, draft, out _); } wy += 32;
            if (Button(new Rect(x, wy, col - 80, 26), "Next " + Math.Max(1, villageAutomationTurns) + " turns")) { draft.AutomationTurns = Math.Max(1, villageAutomationTurns); draft = EconomicAllocationStrategy.Plan(world, camp, draft, out _); }
            if (Button(new Rect(x + col - 76, wy, 30, 26), "−")) villageAutomationTurns = Math.Max(1, villageAutomationTurns - 1);
            if (Button(new Rect(x + col - 40, wy, 30, 26), "+")) villageAutomationTurns = Math.Min(999, villageAutomationTurns + 1); wy += 36;
            GUI.Label(new Rect(x, wy, col - 8, 96), "Replans each turn against demand, stocks and unlocked jobs. Manual workforce edits turn automation off.\n" + (draft.Labor == null ? "" : $"Reserved material gathering: {draft.Labor.MaterialGatherers:F1} workers\nExpansion: {draft.Labor.ExpansionFarmers:F1} farmers"), small);
            x += col + 10; float cy = y;
            GUI.Label(new Rect(x, cy, col, 24), "PRODUCTION PRIORITIES", section); cy += 28;
            DrawEconomicValue("Hand tools", ref draft.Priorities.HandTools, x, ref cy, col, .05);
            DrawEconomicValue("Pots", ref draft.Priorities.Pots, x, ref cy, col, .05);
            DrawEconomicValue("Improved tools", ref draft.Priorities.Tools, x, ref cy, col, .05);
            if (CultivationEconomy.Enabled(camp))
            {
                DrawEconomicValue("Plant cloth", ref draft.Priorities.Cloth, x, ref cy, col, .05);
                DrawEconomicValue("Farm tools", ref draft.Priorities.FarmTools, x, ref cy, col, .05);
                DrawEconomicValue("Fixed housing", ref draft.Priorities.Housing, x, ref cy, col, .05);
            }
            if (husbandry)
            {
                DrawEconomicValue("Animal fabric", ref draft.Priorities.AnimalFabric, x, ref cy, col, .05);
                DrawEconomicValue("Light housing", ref draft.Priorities.LightHousing, x, ref cy, col, .05);
                DrawEconomicValue("Herding tools", ref draft.Priorities.HerdingTools, x, ref cy, col, .05);
            }
            if (fishing)
                DrawEconomicValue("Fishing tools", ref draft.Priorities.FishingTools, x, ref cy, col, .05);
            cy += 12;
            GUI.Label(new Rect(x, cy, col - 8, 22), "Pots target (reserve: " + world.Config.GrowthReserveYears.ToString("F0") + " years): " + NeolithicProduction.TargetPots(world, camp, potYears).ToString("F2"), small); cy += 28;
            double farmToolForecast = CultivationEconomy.ForecastFarmToolWorkerYears(world, camp, potYears) / world.Config.Neolithic.FarmToolFarmerYearsPerUnit;
            GUI.Label(new Rect(x, cy, col - 8, 22), "Farm tools forecast: " + farmToolForecast.ToString("F2"), small); cy += 28;
            if (fishing)
            {
                int fishers = draft.Assignments.Where(a => a.Role == WorkforceRoles.Fisher).Sum(a => a.Population);
                double coverage = Math.Min(fishers * potYears,
                    Math.Max(0, camp.Inventory.FishingTools) * world.Config.Neolithic.FishingToolFisherYearsPerUnit);
                GUI.Label(new Rect(x, cy, col - 8, 50), $"Fishing tools {camp.Inventory.FishingTools:F2}: 1 Wood + 1 Stone each; {world.Config.Neolithic.FishingToolFisherYearsPerUnit:F0} harvest-Fisher-years per unit, {world.Config.Neolithic.FishingToolHarvestMultiplier:F2}× covered catch only. Current-turn coverage {coverage:F2} Fisher-years.", small); cy += 56;
            }
            double storageIntervals = world.Config.TurnDays / world.Config.BaseDays;
            double potStorage = camp.Inventory.Pots * world.Config.Neolithic.PotStorageFoodPerUnit;
            double storageCapacity = NeolithicProduction.StorageCapacity(world, camp, storageIntervals);
            GUI.Label(new Rect(x, cy, col - 8, fishing ? 154 : 132), $"Stone {inventory.Stone:F2}\nClay {inventory.Clay:F2}\nWood {inventory.Wood:F2}\nPots {inventory.Pots:F2} (+{potStorage:F2} storage)\nFarm tools {inventory.FarmTools:F2}\n{(fishing ? "Fishing tools " + inventory.FishingTools.ToString("F2") + "\n" : "")}Storage capacity {storageCapacity:F2}\nHand tools {inventory.HandTools:F2}\nImproved tools {inventory.Tools:F2}", small); cy += fishing ? 166 : 144;
            string farmToolNote = AgriculturalTechnology.Plough(world, camp)
                ? $"Plough Farm Tools raise maintenance efficiency to {world.Config.Agriculture.PloughMaintenance:F2}× and multiply covered crop harvesting by {world.Config.Agriculture.PloughHarvest:F2}×."
                : world.Config.Agriculture?.PloughEnabled == true
                    ? "Stone Farm Tools reduce maintenance and expansion labor for covered field work; they do not multiply harvest or ecology until Plough is learned."
                    : "Stone Farm Tools reduce maintenance and expansion labor for covered field work; they do not multiply harvest or ecology.";
            string craftSummary = $"Crafting consumes tools with useful output. Pots add {world.Config.Neolithic.PotStorageFoodPerUnit:F0} food storage each and retain {100 * (1 - world.Config.Neolithic.PotDecayPerReferenceTurn):F0}% per {world.Config.Neolithic.ReferenceTurnYears:F0}-year reference turn. {farmToolNote}";
            float craftSummaryHeight = Math.Max(100f, small.CalcHeight(new GUIContent(craftSummary), col - 8) + 8f);
            GUI.Label(new Rect(x, cy, col - 8, craftSummaryHeight), craftSummary, small);
            cy += craftSummaryHeight + 8f;
            if (husbandry)
            {
                GUI.Label(new Rect(x, cy, col - 8, pastoralDescriptionHeight), pastoralDescription, small);
                cy += pastoralDescriptionHeight;
            }
            x += col + 10; float fy = y;
            GUI.Label(new Rect(x, fy, col, 24), "FIELDS & SOURCING", section); fy += 28;
            foreach (var t in observation.Tiles.Values.Where(t => t.Cultivable && world.Map.Distance(camp.Tile, t.Id) <= 1).OrderBy(t => t.Id))
            {
                GUI.Label(new Rect(x, fy, col - 90, 22), $"Tile {t.Id}: q {t.Cultivation:F3}", small);
                if (Button(new Rect(x + col - 86, fy, 78, 22), draft.Focus == t.Id ? "Focused" : "Focus")) draft.Focus = draft.Focus == t.Id ? -1 : t.Id;
                fy += 28;
            }
            if (fishing)
            {
                var ownSettlement = observation.Settlements.First(s => s.Id == camp.Id);
                GUI.Label(new Rect(x, fy, col - 8, 48), $"Fishing village support: maturity {Knowledge.Bands[Math.Max(0, Math.Min(4, ownSettlement.FishingVillageMaturity))]}, managed meal support {Knowledge.Bands[Math.Max(0, Math.Min(4, ownSettlement.FishingVillageMealSupport))]}. These are committed prior-turn bands.", small); fy += 54;
                GUI.Label(new Rect(x, fy, col - 8, 36), "Known Shallows/Lake grounds (live visible state, owner snapshot, or conservative unsurveyed prior):", small); fy += 40;
                foreach (var ground in observation.FishingGrounds.Where(g => world.Map.Distance(camp.Tile, g.Tile) <= FishingEconomy.GroundRange + 1).OrderBy(g => g.Tile))
                {
                    string source = ground.Visible ? "visible" : ground.Visited ? "visited" : "unsurveyed prior";
                    string selected = ground.Selected ? " · selected" : "";
                    string capital = Knowledge.Bands[Math.Max(0, Math.Min(4, ground.CapitalIntensity))];
                    string operation = ground.HasOperation ? Knowledge.Bands[Math.Max(0, Math.Min(4, ground.Operation))] : "unknown";
                    GUI.Label(new Rect(x, fy, col - 8, 22), $"Tile {ground.Tile} {ground.WaterEnvironment}: {source}{selected} · accessible food {Knowledge.Bands[Math.Max(0, Math.Min(4, ground.AccessibleFood))]} · capital {capital} · operation {operation}", small); fy += 24;
                }
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
            GUI.Label(new Rect(x, fy + 10, col - 8, 280), AgriculturalTechnology.Plough(world, camp) || world.Config.Agriculture != null
                ? IrrigationEconomy.Status(world, camp) + $"\nConstruction progress: {(world.Canals?.Where(s => s.Operator == camp.Id).Sum(s => s.Construction) ?? 0):F2} completed segment equivalents." : "Plough and Irrigation are available in the opt-in agricultural technology profile.", small);
            if (civicEnabled || world.Config.FissionEnabled)
            {
                float gy = Mathf.Max(wy + 104f, Mathf.Max(cy + 8f, fy + 296f)) + 12f;
                float panelHeight = civicEnabled ? (sanitationEnabled ? 370f : 330f) : 82f;
                Fill(new Rect(4, gy, scrollContentWidth - 16, panelHeight), new Color(.11f, .13f, .12f));
                float px = 14f, py = gy + 10f, pw = scrollContentWidth - 36f;
                if (civicEnabled)
                {
                    var inv = CultureInfo.InvariantCulture;
                    GUI.Label(new Rect(px, py, pw, 22), "CIVIC & GOVERNANCE", section); py += 26f;
                    double cMax = CivicGovernance.MaxComplexity(world, camp);
                    double friction = CivicGovernance.AlignmentFriction(world, camp);
                    string focusLabel = CivicGovernance.EffectiveFocusLabel(camp);
                    GUI.Label(new Rect(px, py, pw, 22),
                        "Complexity C " + camp.CivicComplexity.ToString("F2", inv) + " / " + cMax.ToString("F2", inv)
                        + " (env " + camp.LastEnvironmentalComplexity.ToString("F2", inv) + ")  ·  Power P "
                        + camp.CivicStratification.ToString("+0.00;-0.00;0.00", inv)
                        + " (env " + camp.LastEnvironmentalStratification.ToString("+0.00;-0.00;0.00", inv) + ")", small);
                    py += 24f;
                    GUI.Label(new Rect(px, py, pw, 22),
                        "Alignment friction " + friction.ToString("F2", inv) + "×  ·  Effective focus: " + focusLabel
                        + "  ·  Role: " + WorkforceRoles.Label(WorkforceRoles.Steward, camp), small);
                    py += 24f;
                    double civicCov = CivicGovernance.CivicCoverage(world, camp);
                    double reqCap = CivicGovernance.RequiredCivicCapital(world, camp);
                    double reqStewards = CivicGovernance.RequiredStewards(world, camp);
                    int committedStewards = CultivationEconomy.Workers(camp, WorkforceRoles.Steward);
                    int draftedStewards = draft.Assignments.Where(a => a.Role == WorkforceRoles.Steward).Sum(a => a.Population);
                    GUI.Label(new Rect(px, py, pw, 22),
                        "Civic coverage I_civic " + (civicCov * 100).ToString("F0", inv) + "%  ·  Civic capital "
                        + camp.CivicCapital.ToString("F1", inv) + " / " + reqCap.ToString("F1", inv)
                        + "  ·  Stewards " + committedStewards
                        + (draftedStewards != committedStewards ? " (draft " + draftedStewards + ")" : "")
                        + " / " + reqStewards.ToString("F1", inv), small);
                    py += 24f;
                    if (sanitationEnabled)
                    {
                        double sanitCov = CivicGovernance.SanitationCoverage(world, camp);
                        double reqSanit = CivicGovernance.RequiredSanitationCapital(world.Config, camp.Population);
                        GUI.Label(new Rect(px, py, pw, 22),
                            "Sanitation I_sanit " + (sanitCov * 100).ToString("F0", inv) + "%  ·  Capital "
                            + camp.SanitationCapital.ToString("F1", inv) + " / " + reqSanit.ToString("F1", inv)
                            + "  ·  Water W_sanit " + (camp.SanitationWaterRatio * 100).ToString("F0", inv) + "%", small);
                        py += 24f;
                    }
                    var cs = world.Config.Civic;
                    var precedentDefs = new List<(string Id, string Label, double Threshold)>
                    {
                        (CivicPrecedents.CeremonialIntegration, "Ceremonial Integration", cs.Tier1PracticeThreshold),
                        (CivicPrecedents.CivicOrganization, "Civic Organization", cs.Tier2PracticeThreshold),
                        (CivicPrecedents.TokensAndAccounting, "Tokens & Accounting", cs.Tier3APracticeThreshold)
                    };
                    if (sanitationEnabled)
                        precedentDefs.Add((CivicPrecedents.PlannedQuartersAndDrainage, "Planned Quarters & Drainage", cs.Tier3BPracticeThreshold));
                    var unlocked = new List<string>();
                    var progress = new List<string>();
                    foreach (var pDef in precedentDefs)
                    {
                        if (camp.People != null && camp.People.HasCivicPrecedent(pDef.Id))
                            unlocked.Add(pDef.Label);
                        else
                        {
                            double xp = 0;
                            if (camp.People != null && camp.People.CivicExperience != null)
                                camp.People.CivicExperience.TryGetValue(pDef.Id, out xp);
                            progress.Add(pDef.Label + " " + xp.ToString("F1", inv) + "/" + pDef.Threshold.ToString("F0", inv));
                        }
                    }
                    GUI.Label(new Rect(px, py, pw, 22),
                        "Unlocked precedents: " + (unlocked.Count == 0 ? "None" : string.Join(", ", unlocked)), small);
                    py += 24f;
                    string progressLine = "Progress: " + (progress.Count == 0 ? "All precedents unlocked" : string.Join("  ·  ", progress));
                    float progressHeight = Mathf.Max(22f, small.CalcHeight(new GUIContent(progressLine), pw) + 4f);
                    GUI.Label(new Rect(px, py, pw, progressHeight), progressLine, small);
                    py += progressHeight + 8f;
                }
                else
                {
                    GUI.Label(new Rect(px, py, pw, 22), "SETTLEMENT POLICY", section); py += 26f;
                }
                if (civicEnabled || world.Config.FissionEnabled)
                    DrawPolicyToggleRow("Fission policy", camp.Id, OrderKind.FissionPolicy, (int)camp.FissionPolicy,
                        new[] { "Neutral", "Discourage", "Encourage" }, px, ref py, pw);
                if (civicEnabled)
                {
                    DrawPolicyToggleRow("Immigration policy", camp.Id, OrderKind.ImmigrationPolicy, (int)camp.ImmigrationPolicy,
                        new[] { "Neutral", "Open", "Restricted" }, px, ref py, pw);
                    DrawPolicyToggleRow("Civic focus", camp.Id, OrderKind.CivicFocus, (int)camp.CivicFocus,
                        new[] { "Auto", "Communal", "Centralized" }, px, ref py, pw);
                }
            }
            GUI.EndScrollView(); GUI.EndGroup();
            if (draft.Stamp != before) Queue(new Order(OrderKind.Economy, camp.Id) { Economy = draft });
        }
        private void DrawPolicyToggleRow(string label, int campId, OrderKind kind, int committedValue, string[] optionLabels, float x, ref float y, float width)
        {
            var key = new PolicyOrderKey(campId, kind);
            int? queuedValue = plannedPolicies.TryGetValue(key, out var queuedOrder) ? queuedOrder.Target : (int?)null;
            int effectiveValue = queuedValue ?? committedValue;
            float labelWidth = 210f;
            float gap = 6f;
            float btnWidth = Mathf.Max(60f, (width - labelWidth - gap * 2f) / 3f);
            GUI.Label(new Rect(x, y + 3f, labelWidth - 8f, 22f),
                label + (queuedValue.HasValue && queuedValue.Value != committedValue ? " (queued)" : ""), small);
            for (int v = 0; v < 3; v++)
            {
                bool isActive = effectiveValue == v;
                bool isQueued = queuedValue.HasValue && queuedValue.Value == v && v != committedValue;
                bool isCommitted = committedValue == v;
                string btnText = (isQueued ? "◆ " : isCommitted ? "● " : "◇ ") + optionLabels[v] + (isQueued ? "*" : "");
                var btnRect = new Rect(x + labelWidth + v * (btnWidth + gap), y, btnWidth, 26f);
                if (Button(btnRect, btnText, isActive))
                {
                    if (v == committedValue) CancelPolicyOrder(campId, kind, label + " " + optionLabels[v]);
                    else QueuePolicyOrder(new Order(kind, campId, target: v));
                }
            }
            y += 32f;
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
