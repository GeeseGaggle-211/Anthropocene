using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed partial class TurnResolver
    {
        // A claim set is constructed once from locked allocations. Each tile is
        // evaluated once over the full horizon and all receipts are committed in
        // tile order. The cultivation plan uses private average/end-capital values;
        // no fictive mid-turn field state is written into the world.
        private Dictionary<int, double> CommitHorizonResources(
            WorldState w, List<Demand> demands, TurnRecord record, Action<int> touch,
            Dictionary<int, double> foraged, TurnHorizon horizon,
            Dictionary<int, AgriculturalLabor> agriculturalLabor,
            Dictionary<int, TurnNeeds> needs)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var herds = w.Herds.ToDictionary(h => h.Id);
            var camps = w.Camps.ToDictionary(c => c.Id);
            var hunters = w.Hunters.ToDictionary(h => h.Id);
            var agriculturalCamps = w.Camps.Where(CultivationEconomy.Enabled).OrderBy(c => c.Id).ToArray();
            var farmToolBudgets = agriculturalCamps.ToDictionary(c => c.Id, c => new FarmToolWorkBudget(w, c));
            var scratch = agriculturalCamps.ToDictionary(c => c.Id,
                c => new AgriculturalClaimScratch(w, c, agriculturalLabor[c.Id].Footprint));
            var allocations = new Dictionary<int, HorizonCultivationAllocation>();
            var averageCapital = new Dictionary<int, double>();
            var endingCapital = new Dictionary<int, double>();
            var gatheredFoodForMaterials = new Dictionary<int, double>();
            var terrestrialGathered = new Dictionary<int, double>();
            var transfers = new List<Transfer>();
            var claims = demands.Where(d => d.Kind != "Field operation").ToList();
            var activeTiles = new HashSet<int>(demands.Select(d => d.Tile));

            // Active stocks and field ownership are brought to the start boundary
            // before any read-only tile worker sees them.
            foreach (int tile in activeTiles.OrderBy(id => id))
            {
                LazyEcology.MaterializeEcology(w, tile, horizon.StartDays);
                touch(tile);
            }
            foreach (var camp in agriculturalCamps)
            {
                var campScratch = scratch[camp.Id];
                var budget = farmToolBudgets[camp.Id];
                var labor = CultivationEconomy.PrepareHorizon(w, camp, horizon.StartDays,
                    horizon.Years, budget, campScratch.Footprint);
                agriculturalLabor[camp.Id] = labor;

                // The first bridge estimate is limited by available terrestrial
                // food, assigned gathering capacity and the locked camp target.
                // Actual receipts correct endpoint capital once, after integration.
                double availableLand = campScratch.LandIndices.Sum(i =>
                    LazyEcology.AccessibleAvailability(w, campScratch.Eligible[i],
                        horizon.StartDays, horizon.Days));
                double provisionalFood = Math.Min(availableLand,
                    Math.Min(labor.FoodGatherers * w.Config.GatherPerPerson * horizon.ReferenceIntervals,
                        Math.Max(0, needs[camp.Id].FoodTarget - camp.StepFoodIncome) +
                        needs[camp.Id].PlantFeedstockGap));
                double provisionalBridge = provisionalFood / Math.Max(1e-12, w.Config.GatherPerPerson)
                    * w.Config.BaseDays / w.Config.DaysPerYear;
                var allocation = CultivationEconomy.PlanHorizonExpansion(w, camp, horizon.Years,
                    labor.ExpansionFarmers * horizon.Years, provisionalBridge,
                    budget, campScratch.Footprint);
                allocations.Add(camp.Id, allocation);
                foreach (int tile in allocation.NewFieldTiles())
                    if (w.Tiles[tile].CultivationOperator < 0)
                        w.Tiles[tile].CultivationOperator = camp.Id;
                for (int i = 0; i < allocation.Tiles.Length; i++)
                {
                    int tile = allocation.Tiles[i];
                    if (w.Tiles[tile].CultivationOperator != camp.Id) continue;
                    averageCapital[tile] = allocation.AverageCapital[i];
                    endingCapital[tile] = allocation.EndCapital[i];
                    activeTiles.Add(tile);
                }
            }

            var hunterFoodCamps = new Dictionary<int, int>();
            int FoodCamp(Demand demand)
            {
                if (demand.Kind == "Herd" || demand.Kind == "Foraging") return -1;
                if (demand.Kind != "Fishing") return demand.Entity;
                if (!hunterFoodCamps.TryGetValue(demand.Entity, out int camp))
                    hunterFoodCamps[demand.Entity] = camp = Supply.Connected(w, hunters[demand.Entity])
                        ? hunters[demand.Entity].SourceCamp : -1;
                return camp;
            }
            foreach (var camp in agriculturalCamps)
            {
                var campScratch = scratch[camp.Id];
                campScratch.Clear();
                double biological = Math.Max(0, needs[camp.Id].FoodTarget - camp.StepFoodIncome);
                double feedstock = needs[camp.Id].PlantFeedstockGap;
                double Available(int tile) => LazyEcology.AccessibleAvailability(w, tile,
                    horizon.StartDays, horizon.Days, null,
                    averageCapital.TryGetValue(tile, out double capital) ? capital : (double?)null);
                AddAgriculturalClaims(w, camp, agriculturalLabor[camp.Id],
                    horizon.ReferenceIntervals, biological, feedstock, Available, claims, campScratch);
            }
            // Group once after every camp has proposed its fixed claims. Scanning
            // the growing global claim list for each camp becomes quadratic as
            // settlement counts rise, even though food budgets are camp-local.
            var claimsByCamp = claims.GroupBy(FoodCamp).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var camp in agriculturalCamps)
            {
                if (!claimsByCamp.TryGetValue(camp.Id, out var owned)) continue;
                double biological = Math.Max(0, needs[camp.Id].FoodTarget - camp.StepFoodIncome);
                double feedstock = needs[camp.Id].PlantFeedstockGap;
                double biologicalRequested = owned.Where(d => !d.Manufacturing).Sum(d => d.Food);
                double feedstockRequested = owned.Where(d => d.Manufacturing).Sum(d => d.Food);
                double biologicalScale = Math.Min(1, biological / Math.Max(1e-12, biologicalRequested));
                double feedstockScale = Math.Min(1, feedstock / Math.Max(1e-12, feedstockRequested));
                foreach (var claim in owned)
                    claim.Food *= claim.Manufacturing ? feedstockScale : biologicalScale;
            }

            // Empty field tiles still need one ecology transition for their
            // capital/condition/wood endpoint even without a food claim.
            var byTile = claims.GroupBy(d => d.Tile).ToDictionary(g => g.Key,
                g => g.OrderBy(d => d.Entity).ThenBy(d => d.Kind, StringComparer.Ordinal).ToArray());
            var groups = activeTiles.OrderBy(id => id)
                .Select(id => new { Tile = id, Claims = byTile.TryGetValue(id, out var requested)
                    ? requested : Array.Empty<Demand>() })
                .ToArray();
            LastMetrics.ActiveTiles = groups.Length;
            LastMetrics.FullTurnTiles = groups.Length;
            LastMetrics.SeasonalTiles = 0;
            LastMetrics.TileIntegrations = groups.Length;
            LastMetrics.ClaimsAllocated = claims.Count;
            LastMetrics.HorizonPreparationMilliseconds = timer.Elapsed.TotalMilliseconds;
            var integrationTimer = System.Diagnostics.Stopwatch.StartNew();
            var results = execution.Map(groups, group => EcologicalPressure.Integrate(w, group.Tile,
                group.Claims, horizon.StartDays, horizon.Days,
                averageCapital.TryGetValue(group.Tile, out double average) ? average : (double?)null,
                endingCapital.TryGetValue(group.Tile, out double end) ? end : (double?)null));
            LastMetrics.FullTurnIntegrationMilliseconds = integrationTimer.Elapsed.TotalMilliseconds;
            var commitTimer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < groups.Length; i++)
            {
                int tile = groups[i].Tile;
                var result = results[i];
                result.View.Commit(w.Tiles[tile]);
                if (result.HasWoodSource)
                    MaterialEconomy.CommitWoodProjection(w, tile, result.WoodRemaining, result.WoodDays);
                record.Pressure.AddRange(result.Pressure);
                record.EcologyBalances.Add(result.Balance);
                foreach (var transfer in result.Transfers)
                {
                    transfers.Add(transfer);
                    if (transfer.Kind == "Gathering" &&
                        w.Tiles[tile].Land && camps.ContainsKey(transfer.Entity))
                        terrestrialGathered[transfer.Entity] =
                            (terrestrialGathered.TryGetValue(transfer.Entity, out double prior) ? prior : 0)
                            + transfer.Food;
                }
            }

            foreach (var camp in agriculturalCamps)
            {
                double gathered = terrestrialGathered.TryGetValue(camp.Id, out double food) ? food : 0;
                double bridge = gathered / Math.Max(1e-12, w.Config.GatherPerPerson)
                    * w.Config.BaseDays / w.Config.DaysPerYear;
                var corrected = CultivationEconomy.ReconcileHorizonBridge(w, camp,
                    allocations[camp.Id], bridge, farmToolBudgets[camp.Id]);
                CultivationEconomy.CommitHorizonExpansion(w, camp, corrected, record,
                    farmToolBudgets[camp.Id], allocations[camp.Id]);
            }
            LastMetrics.HorizonCommitMilliseconds = commitTimer.Elapsed.TotalMilliseconds;
            foreach (var transfer in transfers)
            {
                if (transfer.Kind == "Herd")
                {
                    var herd = herds[transfer.Entity];
                    herd.UnmetFood = Math.Max(0, herd.UnmetFood - transfer.PotentialFood);
                    herd.UnmetFreshwater = Math.Max(0, herd.UnmetFreshwater - transfer.Freshwater);
                }
                else if (transfer.Kind == "Foraging")
                {
                    foraged[transfer.Entity] =
                        (foraged.TryGetValue(transfer.Entity, out double prior) ? prior : 0) + transfer.Food;
                }
                else
                {
                    var camp = transfer.Kind == "Fishing" ? camps[hunters[transfer.Entity].SourceCamp]
                        : camps[transfer.Entity];
                    if (transfer.Kind == "Gathering" || transfer.Kind == "Marine gathering" ||
                        transfer.Kind == "Aquatic gathering")
                        gatheredFoodForMaterials[camp.Id] =
                            (gatheredFoodForMaterials.TryGetValue(camp.Id, out double prior) ? prior : 0)
                            + transfer.Food;
                    if (transfer.Kind == "Fishing")
                        Supply.Award(w, hunters[transfer.Entity], transfer.Food, record, "Fishing");
                    else
                    {
                        if (transfer.Kind == "Hunting") camp.AnimalFood += transfer.Food;
                        else if (w.Tiles[transfer.Tile].Aquatic ||
                            w.Tiles[transfer.Tile].WaterBody == WaterKind.Saltwater)
                            camp.AquaticFood += transfer.Food;
                        else camp.PlantFood += transfer.Food;
                        camp.StepFoodIncome += transfer.Food;
                    }
                }
                record.Transfers.Add(transfer);
            }
            LastMetrics.EcologyMilliseconds = timer.Elapsed.TotalMilliseconds;
            return gatheredFoodForMaterials;
        }
    }
}
