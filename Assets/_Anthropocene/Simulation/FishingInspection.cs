using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Read-only owner view. Routes and forecasts reuse the knowledge-safe Prepare
    // path; realized amounts come only from this camp's committed receipts.
    // Cache at the UI boundary, never save this derived view.
    public sealed class FishingInspection
    {
        public sealed class Ground
        {
            public FishingGroundPlan Plan;
            public int[] Path;
            public double ObservedDays, LastCatch;
            public double? ObservedOperation;
            public bool PreviouslySelected;
        }
        public Ground[] Grounds;
        public int Fishers, Gatherers, Hunters;
        // Gatherers is the shared pool; MaterialGatherers of it are reserved for raw extraction.
        public double MaterialGatherers, FoodGatherers;
        public double PrimitiveCatch, EquippedCatch, ExpectedToolUse, ToolDemand;
        public double LastResidentCatch, LastFishFood, LastTerrestrialFood, LastMaturity, LastMealSupport;
        public bool HasLastReport;
        public string[] MaterialWarnings;

        public static FishingInspection Build(WorldState world, int player, Camp camp, EconomicIntent intent)
        {
            if (camp == null || camp.Player != player) throw new ArgumentException("Fishing inspection requires an owned camp.");
            var cache = new FishingAccessCache(world);
            var access = FishingEconomy.BuildAccess(world, camp, camp.Tile, camp.Population,
                world.Days, world.Config.TurnDays, cache);
            int fishers = intent.Assignments.Where(a => a.Role == WorkforceRoles.Fisher).Sum(a => a.Population);
            var plan = FishingEconomy.PreparePlan(world, camp, access, fishers, camp.Inventory.FishingTools,
                world.Config.TurnDays, false);
            var last = world.History.LastOrDefault();
            var report = last?.Demographics?.FirstOrDefault(d => d.Camp == camp.Id);
            var receipts = last?.Transfers?.Where(t => t.Entity == camp.Id && t.Kind == "FisheryHarvest").ToArray()
                ?? Array.Empty<Transfer>();
            var snapshots = (camp.People.FishingGroundKnowledge ?? new List<FishingGroundSnapshot>()).ToDictionary(s => s.TileId);
            var selected = new HashSet<int>(camp.LastSelectedFishingGrounds ?? new List<int>());
            var sources = MaterialEconomy.KnownSources(world, camp, world.Days);
            var warnings = new List<string>();
            foreach (var material in new[] { MaterialId.Wood, MaterialId.Stone, MaterialId.Clay })
                if (MaterialEconomy.Inventory(camp, material) <= 0 && !sources.Any(s => s.Material == material && s.Available > 0))
                    warnings.Add("No stocked or known accessible " + material + ".");
            if (fishers > 0 && !intent.Assignments.Any(a => (a.Role == WorkforceRoles.Craftsperson || a.Role == WorkforceRoles.Carpenter) && a.Population > 0))
                warnings.Add("No tool-producing craft labor assigned; Fisher work cannot manufacture replacement Fishing Tools.");
            return new FishingInspection
            {
                Fishers = fishers, Gatherers = camp.Population - intent.Assignments.Sum(a => a.Population),
                MaterialGatherers = Math.Min(Math.Max(0, camp.Population - intent.Assignments.Sum(a => a.Population)), Math.Max(0, intent.Labor?.MaterialGatherers ?? 0)),
                FoodGatherers = Math.Max(0, camp.Population - intent.Assignments.Sum(a => a.Population) - Math.Max(0, intent.Labor?.MaterialGatherers ?? 0)),
                Hunters = world.Hunters.Where(h => h.SourceCamp == camp.Id).Sum(h => h.Population),
                PrimitiveCatch = access.CatchEnvelope(fishers), EquippedCatch = access.CatchEnvelope(fishers, camp.Inventory.FishingTools),
                ExpectedToolUse = plan.Grounds.Sum(g => g.ToolCoveredHarvestWorkerYears) / world.Config.Neolithic.FishingToolFisherYearsPerUnit,
                ToolDemand = Math.Max(0, fishers * access.HorizonYears / world.Config.Neolithic.FishingToolFisherYearsPerUnit -
                    Math.Max(0, camp.Inventory.FishingTools - plan.Grounds.Sum(g => g.ToolCoveredHarvestWorkerYears) / world.Config.Neolithic.FishingToolFisherYearsPerUnit)),
                LastResidentCatch = receipts.Sum(t => t.Food), LastFishFood = report?.FishFood ?? 0,
                LastTerrestrialFood = report?.TerrestrialFood ?? 0, LastMaturity = report?.FishingVillageMaturity ?? 0,
                LastMealSupport = report?.FishingVillageMealSupport ?? 0, HasLastReport = report != null,
                MaterialWarnings = warnings.ToArray(),
                Grounds = access.Grounds.Select(g => new Ground
                {
                    Plan = plan.Grounds.FirstOrDefault(p => p.TileId == g.TileId) ?? g,
                    Path = cache.Path(camp, camp.Tile, g), PreviouslySelected = selected.Contains(g.TileId),
                    ObservedDays = g.KnowledgeSource == FishingKnowledgeSource.Visible ? world.Days :
                        snapshots.TryGetValue(g.TileId, out var snapshot) ? snapshot.ObservedDays : -1,
                    ObservedOperation = snapshots.TryGetValue(g.TileId, out var operationSnapshot) ? operationSnapshot.OperationFraction : (double?)null,
                    LastCatch = receipts.Where(t => t.Tile == g.TileId).Sum(t => t.Food)
                }).ToArray()
            };
        }
    }
}
