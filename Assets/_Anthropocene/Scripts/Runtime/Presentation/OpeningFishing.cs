using System;
using Anthropocene.Simulation;
using UnityEngine;

namespace Anthropocene.Presentation
{
    public sealed partial class OpeningGame
    {
        private float fishingInspectorBottom;
        private FishingInspection fishingInspection;
        private WorldState fishingInspectionWorld, fishingRouteWorld;
        private int fishingInspectionTurn = -1, fishingInspectionCamp = -1, fishingRouteTurn = -1;
        private string fishingInspectionStamp;
        private int[] fishingRoute = Array.Empty<int>();

        private void DrawFishingInspection(Camp camp, EconomicIntent draft, float y, float width)
        {
            if (fishingInspectionWorld != world || fishingInspectionTurn != world.Turn ||
                fishingInspectionCamp != camp.Id || fishingInspectionStamp != draft.Stamp)
            {
                fishingInspection = FishingInspection.Build(world, 0, camp, draft);
                fishingInspectionWorld = world; fishingInspectionTurn = world.Turn;
                fishingInspectionCamp = camp.Id; fishingInspectionStamp = draft.Stamp;
            }
            var view = fishingInspection;
            void Label(string value)
            {
                float height = small.CalcHeight(new GUIContent(value), width - 16) + 8;
                GUI.Label(new Rect(4, y, width - 16, height), value, small); y += height;
            }
            GUI.Label(new Rect(4, y, width - 16, 28), "FISHING · next-turn estimate at this settlement", section); y += 32;
            Label($"Drafted Fishers {view.Fishers} · resident Gatherers {view.Gatherers} ({view.FoodGatherers:F1} food, {view.MaterialGatherers:F1} reserved for material gathering) · detached Hunters {view.Hunters}. Grounds are chosen automatically through the shared Economy plan. A move or competition may change the resolved outcome.");
            Label($"Estimated catch: primitive {view.PrimitiveCatch:F1}, with opening tools {view.EquippedCatch:F1}. Expected tool use {view.ExpectedToolUse:F2}; replacement demand {view.ToolDemand:F2}. Tools made this turn serve later fishing. Fishing food is edible and tradable, never Cloth feedstock.");
            camp.People.CraftExperience.TryGetValue(NeolithicTech.Sailing, out double sailingPractice);
            Label(!world.Config.NeolithicSailingEnabled ? "Sailing progression is disabled in this saved world." : camp.People.HasTech(NeolithicTech.Sailing) ? "Sailing mastered: up to five water steps, 90% distance retention, at most one terminal Ocean ground; offshore grounds never build capital or qualify a village." :
                $"Sailing useful Weaver practice: {sailingPractice:F1} / {world.Config.Neolithic.CraftMasteryThreshold:F0}. Produce needed textiles while actually fishing with Watercraft. Coast ownership or idle stock grants no progress.");
            if (view.ToolDemand > 0) Label($"Fishing Tools still needed: {view.ToolDemand:F2} Wood and Stone units before other recipes compete; reserve material-gathering and crafting labor in Economy. Available Wood {camp.Inventory.Wood:F2}, Stone {camp.Inventory.Stone:F2}.");
            foreach (string warning in view.MaterialWarnings) Label(warning);
            if (view.HasLastReport)
                Label($"Last turn: resident fishing {view.LastResidentCatch:F1}; all fishing receipts {view.LastFishFood:F1}; terrestrial food {view.LastTerrestrialFood:F1}. Village maturity {view.LastMaturity:P0} / 50%, managed meal support {view.LastMealSupport:P0} / 50%. Status: {(camp.IsVillage ? "Village (retained until departure)" : "Camp")}. Capital and fixed housing stay behind when moving.");
            else Label("No committed production or village qualification report yet.");
            if (view.Grounds.Length == 0) Label("No known legal fishing grounds. Settle beside Shallows or a Lake and survey connected water.");
            foreach (var entry in view.Grounds)
            {
                var ground = entry.Plan;
                string knowledge = ground.KnowledgeSource == FishingKnowledgeSource.Visible ? "currently observed" :
                    ground.KnowledgeSource == FishingKnowledgeSource.Snapshot ? $"historical observation, {(world.Days - entry.ObservedDays) / world.Config.DaysPerYear:F1} years old; capital estimated by decay" : "unsurveyed: conservative zero-yield estimate";
                Label($"Ground {ground.TileId} · {knowledge}. Route: {string.Join(" → ", entry.Path)}. Distance {ground.Distance}; retained labor efficacy {ground.DistanceFactor:P1} (penalty {1 - ground.DistanceFactor:P1}).");
                Label(entry.ObservedOperation.HasValue ? $"Last observed management: {entry.ObservedOperation.Value:P0} of maintenance covered; {(entry.ObservedOperation.Value > 0 ? "operated" : "resting / not yet maintained")}. This is historical, not a live reading outside vision." : "No past management observation yet.");
                if (ground.Offshore) Label("Offshore ground: harvest only; no capital, management or village credit.");
                Label($"Known accessible food {ground.OpeningAccessibleFood:F1}; condition {ground.OpeningCondition:P0}; estimated q {FishingEconomy.Intensity(world.Config, ground.OpeningCapital):F3}. Draft: {(ground.AssignedFisherYears > 0 ? "active" : "resting")}, {ground.AssignedFisherYears:F1} Fisher-years; catch estimate {ground.RequestedCatch:F1}. Last own catch here {entry.LastCatch:F1}{(entry.PreviouslySelected ? " (previously selected)" : "")}.");
                if (Button(new Rect(4, y, 240, 28), "Show ground and water route"))
                {
                    fishingRoute = entry.Path; fishingRouteWorld = world; fishingRouteTurn = world.Turn;
                    center = ground.TileId; villageInspectorOpen = false;
                }
                y += 34;
            }
            if (Button(new Rect(4, y, 240, 28), "Clear fishing route")) fishingRoute = Array.Empty<int>();
            fishingInspectorBottom = y + 36;
        }
    }
}
