using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public static class NeolithicTech
    {
        public const string Toolmaking = "Toolmaking";
        public const string Pottery = "Pottery";
        public const string Weaving = "Weaving";
        public const string Carpentry = "Carpentry";
        public const string Construction = "Construction";
        public const string KilnFiring = "KilnFiring";
        public const string Plough = "Plough";
        public const string Irrigation = "Irrigation";
        public const string Sailing = "Sailing";
        public const string WheelAndAxle = "WheelAndAxle";
        public const string CopperWorking = "CopperWorking";

        public static readonly string[] FirstTier = { Toolmaking, Pottery, Weaving, Carpentry, Construction };
        public static readonly string[] SecondTier = { KilnFiring, Plough, Irrigation, Sailing };
        public static readonly string[] Convergence = { WheelAndAxle, CopperWorking };
    }

    public static class NeolithicProduction
    {
        public static void VegetationFactors(Tile tile, out double vBuild, out double vMaint)
        {
            if (tile.CurrentVegetation == VegetationTier.High) { vBuild = 1.5; vMaint = 1.2; }
            else if (tile.CurrentVegetation == VegetationTier.Medium) { vBuild = 1.3; vMaint = 1.1; }
            else { vBuild = 1.0; vMaint = 1.0; }
        }

        public static double EffectiveTechnologyIntensity(WorldState w, Camp camp)
        {
            double baseQT = w.Config.ReferenceIntensity; // 0.40
            if (camp.People.HasTech(NeolithicTech.Plough) && camp.Inventory.FarmTools > 0)
            {
                int farmers = camp.Workforce?.Assignments?.FirstOrDefault(a => a.Role == WorkforceRoles.Farmer)?.Population ?? 0;
                double toolRatio = Math.Min(1.0, camp.Inventory.FarmTools / Math.Max(1.0, farmers));
                return baseQT + 0.10 * toolRatio; // up to 0.50
            }
            return baseQT;
        }

        public static double FarmerMaintenanceMultiplier(Camp camp)
        {
            if (camp.Inventory.FarmTools > 0)
            {
                int farmers = camp.Workforce?.Assignments?.FirstOrDefault(a => a.Role == WorkforceRoles.Farmer)?.Population ?? 0;
                double toolRatio = Math.Min(1.0, camp.Inventory.FarmTools / Math.Max(1.0, farmers));
                return 1.0 + 0.50 * toolRatio; // up to 1.5x maintenance capacity
            }
            return 1.0;
        }

        public static double StorageCapacityMultiplier(Camp camp)
        {
            if (camp.Population <= 0) return 1.0;
            double potRatio = Math.Min(1.0, camp.Inventory.Pots / Math.Max(1.0, camp.Population));
            return 1.0 + 3.0 * potRatio; // up to 4.0x storage
        }

        public static double DomesticExpectationFactor(Camp camp)
        {
            if (!camp.IsVillage) return 0.0;
            return Math.Min(1.0, camp.VillageTurns / 10.0); // 10% per turn over 10 turns post-village
        }

        public static bool CanIrrigateTile(WorldState w, Camp camp, int tileId, out string reason)
        {
            if (tileId < 0 || tileId >= w.Tiles.Length) { reason = "Invalid tile."; return false; }
            var tile = w.Tiles[tileId];
            if (!tile.TraversableLand) { reason = "Canal must be on land."; return false; }
            if (tile.Irrigated) { reason = "Already irrigated."; return false; }
            if (tile.Cultivation < 0.40f) { reason = $"Cultivation intensity must reach 0.40 (current: {tile.Cultivation:F2})."; return false; }
            int dist = w.Map.Distance(camp.Tile, tileId);
            if (dist > 1) { reason = "Canal must be within village footprint (radius 1)."; return false; }
            bool connectsToWater = tile.River || (tile.RiverEdges != null && tile.RiverEdges.Any(e => e >= 0)) || tile.FreshwaterSource != FreshwaterSource.None;
            if (!connectsToWater)
            {
                foreach (int n in w.Map.Neighbors(tileId))
                {
                    var nt = w.Tiles[n];
                    if (nt.WaterBody == WaterKind.Freshwater || nt.Terrain == Terrain.Lake || nt.River || nt.Irrigated)
                    {
                        connectsToWater = true;
                        break;
                    }
                }
            }
            if (!connectsToWater) { reason = "Canal must connect to a river, lake, or existing canal network."; return false; }
            reason = null;
            return true;
        }

        public static void ResolveAgriculture(WorldState w, Camp camp, double intervals, double years, TurnRecord record)
        {
            if (camp.Population <= 0) return;
            var inv = camp.Inventory;
            var soc = camp.People;
            int pop = camp.Population;

            // Determine farmer workforce
            int farmers = camp.Workforce?.Assignments?.FirstOrDefault(a => a.Role == WorkforceRoles.Farmer)?.Population ?? 0;
            if (farmers == 0 && camp.IsVillage)
            {
                // In villages with no manual assignment, allocate up to 35% of population to farm
                farmers = Math.Max(0, (int)Math.Floor(pop * 0.35));
            }

            int[] footprint = w.Map.Radius(camp.Tile, 1).Where(id => w.Tiles[id].TraversableLand).ToArray();
            if (footprint.Length == 0) return;

            double effectiveQT = EffectiveTechnologyIntensity(w, camp);
            double farmerMaintMultiplier = FarmerMaintenanceMultiplier(camp);

            // 1. Maintenance Burden Calculation
            double totalMaintRequired = 0.0;
            for (int i = 0; i < footprint.Length; i++)
            {
                var t = w.Tiles[footprint[i]];
                if (t.Cultivation > 0)
                {
                    VegetationFactors(t, out _, out double vMaint);
                    double E = Math.Pow(t.Cultivation / effectiveQT, 2.0);
                    double maintLaborShare = (w.Config.MaintenanceCoefficient / farmerMaintMultiplier) * vMaint * E;
                    totalMaintRequired += maintLaborShare * pop / footprint.Length;
                }
            }

            double laborAssignedToMaint = Math.Min(farmers, totalMaintRequired);
            double laborForExpansion = Math.Max(0.0, farmers - laborAssignedToMaint);

            // Proportional decay under maintenance shortfall
            if (totalMaintRequired > 0 && laborAssignedToMaint < totalMaintRequired)
            {
                double deficitRatio = 1.0 - (laborAssignedToMaint / totalMaintRequired);
                for (int i = 0; i < footprint.Length; i++)
                {
                    var t = w.Tiles[footprint[i]];
                    double decay = 0.15 * Math.Pow(t.Cultivation, 2.0) * deficitRatio;
                    t.Cultivation = (float)Math.Max(0.0, t.Cultivation - decay);
                }
            }

            // 2. Dynamic Field Expansion Allocation
            if (laborForExpansion > 0)
            {
                int targetTile = camp.CultivationFocusTile >= 0 && footprint.Contains(camp.CultivationFocusTile)
                    ? camp.CultivationFocusTile
                    : BestExpansionTile(w, footprint, effectiveQT);

                if (targetTile >= 0)
                {
                    var t = w.Tiles[targetTile];
                    VegetationFactors(t, out double vBuild, out _);
                    double deltaE = laborForExpansion / (vBuild * 120.0);
                    double currentE = Math.Pow(t.Cultivation / effectiveQT, 2.0);
                    double newE = currentE + deltaE;
                    t.Cultivation = (float)Math.Min(1.0, Math.Sqrt(newE) * effectiveQT);
                    t.Cultivated = t.Cultivation > 0.05f;
                }
            }

            // 3. Gatherer Bridge (if q < QBridge in Camp)
            int gatherers = camp.GathererPopulation;
            if (!camp.IsVillage && gatherers > 0)
            {
                for (int i = 0; i < footprint.Length; i++)
                {
                    var t = w.Tiles[footprint[i]];
                    if (t.Cultivation < w.Config.GathererBridgeCeiling)
                    {
                        VegetationFactors(t, out double vBuild, out _);
                        double bridgeDelta = (gatherers * w.Config.GathererBridgeEfficiency * 0.0003) / vBuild;
                        t.Cultivation = (float)Math.Min(w.Config.GathererBridgeCeiling, t.Cultivation + bridgeDelta);
                        t.Cultivated = t.Cultivation > 0.05f;
                    }
                }
            }

            // 4. Irrigation Canal Expansion (Builder work)
            if (soc.HasTech(NeolithicTech.Irrigation))
            {
                for (int i = 0; i < footprint.Length; i++)
                {
                    var t = w.Tiles[footprint[i]];
                    if (!t.Irrigated && t.Cultivation >= 0.40f && !t.River)
                    {
                        bool connectsToWater = w.Map.Neighbors(footprint[i]).Any(n => w.Tiles[n].River || w.Tiles[n].Terrain == Terrain.Lake);
                        if (connectsToWater)
                        {
                            t.Irrigated = true;
                            record.Log($"Irrigation canal constructed on Tile {footprint[i]} by settlement {camp.Id}.");
                        }
                    }
                }
            }

            // 5. Harvest Cultivated Food
            double totalAccessibleYield = 0.0;
            for (int i = 0; i < footprint.Length; i++)
            {
                var t = w.Tiles[footprint[i]];
                double baseFood = t.Seasons.Average(s => s.MaxFood);
                double baseBio = t.Seasons.Average(s => s.MaxPotentialFood);
                if (t.Irrigated) { baseFood *= 1.35; baseBio *= 1.35; }

                double f0 = baseFood / Math.Max(1.0, baseBio);
                double fQ = f0 + (1.0 - f0) * Math.Pow(t.Cultivation, 2.0);
                double kFoodQ = baseBio * fQ;
                double rCult = w.Config.CultivationRegenFlux * t.Cultivation * baseBio * 0.35;
                double tileYield = (kFoodQ + rCult) * intervals * 0.76;
                totalAccessibleYield += tileYield;
            }

            double toolBonus = inv.Tools > 0 ? 1.10 : 1.0;
            double gatherPerPerson = w.Config.GatherPerPerson * intervals;
            double farmerHarvestCapacity = farmers * w.Config.FarmerProductivity * gatherPerPerson * toolBonus;

            double harvested = Math.Min(totalAccessibleYield, farmerHarvestCapacity);
            camp.PlantFood += harvested;
            camp.StepFoodIncome += harvested;
            if (harvested > 0)
            {
                record.Transfers.Add(new Transfer { Tile = camp.Tile, Entity = camp.Id, Kind = "Cultivation", Food = harvested });
            }
        }

        private static int BestExpansionTile(WorldState w, int[] footprint, double effectiveQT)
        {
            int bestTile = -1;
            double bestScore = -1;
            for (int i = 0; i < footprint.Length; i++)
            {
                int tid = footprint[i];
                var t = w.Tiles[tid];
                VegetationFactors(t, out double vBuild, out double vMaint);
                double bio = t.Seasons.Average(s => s.MaxPotentialFood);
                double currentE = Math.Pow(t.Cultivation / effectiveQT, 2.0);
                double score = (bio / (vBuild * (1.0 + currentE * vMaint)));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTile = tid;
                }
            }
            return bestTile;
        }

        public static void ResolveCampNeolithic(WorldState w, Camp camp, double intervals, double years, TurnRecord record)
        {
            var inv = camp.Inventory;
            var soc = camp.People;
            int pop = camp.Population;
            if (pop <= 0) return;

            // 1. Village Transition Check (Average q > 0.25 across 1-radius footprint)
            int[] footprint = w.Map.Radius(camp.Tile, 1).Where(id => w.Tiles[id].TraversableLand).ToArray();
            double avgQ = footprint.Length > 0 ? footprint.Average(tid => w.Tiles[tid].Cultivation) : 0.0;
            if (avgQ > 0.25)
            {
                if (!camp.IsVillage)
                {
                    camp.IsVillage = true;
                    camp.VillageTurns = 0;
                    record.Log($"Camp {camp.Id} (Player {camp.Player}) has transitioned into a permanent Village (Avg Cultivation {avgQ:F2}).");
                }
                camp.VillageTurns++;
            }

            // 2. Pots Degradation (10% per turn)
            inv.Pots = Math.Max(0.0, inv.Pots * (1.0 - w.Config.PotDegradationRate));

            // Gatherers harvest raw materials (Stone, Clay, Wood) from the surrounding landscape
            int gatherers = camp.GathererPopulation;
            if (gatherers > 0)
            {
                double rawGathered = gatherers * 2.0; // 2 units of raw materials per gatherer per generation
                double maxStockpile = Math.Max(200.0, pop * 10.0);
                inv.Stone = Math.Min(maxStockpile, inv.Stone + rawGathered);
                inv.Clay = Math.Min(maxStockpile, inv.Clay + rawGathered);
                inv.Wood = Math.Min(maxStockpile, inv.Wood + rawGathered);
            }

            // 3. Crafting Execution (Only in Village with crafters)
            int crafters = camp.Workforce?.Assignments?.FirstOrDefault(a => a.Role == WorkforceRoles.Craftsperson)?.Population ?? 0;
            foreach (var specialistRole in WorkforceRoles.Specialists)
            {
                crafters += camp.Workforce?.Assignments?.FirstOrDefault(a => a.Role == specialistRole)?.Population ?? 0;
            }

            double toolBonus = inv.Tools > 0 ? 1.10 : 1.0;

            if (camp.IsVillage && crafters > 0)
            {
                ExecuteCraftCycle(w, camp, crafters, toolBonus, record);
            }

            // 4. Housing Construction and Upkeep
            double upkeepNeeded = inv.Houses * 1.0;
            if (inv.HousingUnits >= upkeepNeeded)
            {
                inv.HousingUnits -= upkeepNeeded;
            }
            else
            {
                double deficit = upkeepNeeded - inv.HousingUnits;
                inv.HousingUnits = 0.0;
                double decayedHouses = deficit * 0.5; // deterioration from lack of maintenance
                inv.Houses = Math.Max(0.0, inv.Houses - decayedHouses);
            }

            // New Construction: 10 HousingUnits = 1 House (8 capacity)
            if (inv.HousingUnits >= 10.0)
            {
                double newHouses = Math.Floor(inv.HousingUnits / 10.0);
                inv.Houses += newHouses;
                inv.HousingUnits -= newHouses * 10.0;
            }

            // Initial housing baseline for new camps
            if (inv.Houses <= 0 && pop > 0)
            {
                inv.Houses = Math.Ceiling(pop / 8.0);
            }

            // 5. Cloth Consumption
            double expectation = DomesticExpectationFactor(camp);
            double expectedClothDemand = pop * expectation;
            double clothConsumed = Math.Min(inv.Cloth, expectedClothDemand);
            inv.Cloth = Math.Max(0.0, inv.Cloth - clothConsumed); // worn out over 20-year turn

            // 6. Quality of Life (QoL) Calculation
            double foodDemand = pop * w.Config.Consumption * intervals;
            double foodConsumed = Math.Min(camp.PlantFood + camp.AnimalFood + camp.AquaticFood, foodDemand);
            camp.FoodNeedsMetRatio = foodDemand > 0 ? Math.Min(1.0, foodConsumed / foodDemand) : 1.0;

            camp.ClothPerCapita = expectedClothDemand > 0 ? Math.Min(1.0, clothConsumed / expectedClothDemand) : 1.0;
            double housingCapacity = inv.Houses * 8.0;
            double expectedHousingDemand = pop * expectation;
            camp.HousingPerCapita = expectedHousingDemand > 0 ? Math.Min(1.0, housingCapacity / expectedHousingDemand) : 1.0;

            if (!camp.IsVillage)
            {
                // Nomadic / semi-sedentary camps have no domestic cloth/housing requirements
                camp.QoL = camp.FoodNeedsMetRatio;
                camp.FissionWilling = 0.0;
            }
            else
            {
                camp.QoL = camp.FoodNeedsMetRatio * camp.ClothPerCapita * camp.HousingPerCapita;
                camp.FissionWilling = camp.QoL < 1.0 ? pop * (1.0 - camp.QoL) : 0.0;
            }

            // 7. Check Camp Fission Trigger
            if (camp.IsVillage && camp.FissionWilling >= 25.0)
            {
                ExecuteFission(w, camp, record);
            }
        }

        private static void ExecuteCraftCycle(WorldState w, Camp camp, int crafters, double toolBonus, TurnRecord record)
        {
            var inv = camp.Inventory;
            var soc = camp.People;
            var priorities = camp.CraftingPriorities ?? new CraftingPriorities();

            // Total craft points produced by crafters over the turn (20 items/crafter baseline)
            double outputPerWorker = 20.0 * toolBonus;
            double totalCraftPoints = crafters * outputPerWorker;

            double pHandTools = Math.Max(0.0, priorities.HandTools);
            double pPots = Math.Max(0.0, priorities.Pots);
            double pCloth = Math.Max(0.0, priorities.Cloth);
            double pFarmTools = Math.Max(0.0, priorities.FarmTools);
            double pHousing = Math.Max(0.0, priorities.Housing);
            double pSum = pHandTools + pPots + pCloth + pFarmTools + pHousing;
            if (pSum > 1e-6)
            {
                pHandTools /= pSum; pPots /= pSum; pCloth /= pSum; pFarmTools /= pSum; pHousing /= pSum;
            }
            else
            {
                pHandTools = 0.15; pPots = 0.10; pCloth = 0.45; pFarmTools = 0.10; pHousing = 0.20;
            }

            // Recipe Demands
            double demandHandTools = totalCraftPoints * pHandTools;
            double demandPots = totalCraftPoints * pPots;
            double demandCloth = totalCraftPoints * pCloth;
            double demandFarmTools = totalCraftPoints * pFarmTools;
            double demandHousing = totalCraftPoints * pHousing;

            // Requisition Raw Materials: 1.25x buffer target
            // Gatherers collected stone, clay, wood, fiber
            double producedHandTools = Math.Min(demandHandTools, inv.Stone);
            inv.Stone -= producedHandTools;

            double producedPots = Math.Min(demandPots, inv.Clay);
            inv.Clay -= producedPots;

            // Fiber is drawn directly from Plant Food
            double fiberAvailable = Math.Min(camp.PlantFood, demandCloth);
            double producedCloth = fiberAvailable;
            camp.PlantFood -= fiberAvailable;
            if (fiberAvailable > 0)
            {
                var dem = record.Demographics.FirstOrDefault(d => d.Camp == camp.Id);
                if (dem != null) dem.DiscardedInventory += fiberAvailable;
            }

            double producedFarmTools = Math.Min(demandFarmTools, inv.Wood);
            inv.Wood -= producedFarmTools;

            double housingPossible = Math.Min(demandHousing, Math.Min(inv.Clay / 0.5, inv.Wood / 0.5));
            double producedHousingUnits = housingPossible;
            inv.Clay -= producedHousingUnits * 0.5;
            inv.Wood -= producedHousingUnits * 0.5;

            // Hand Tool wear during crafting (1 hand tool consumed per 4 complex items made)
            double complexItems = producedPots + producedCloth + producedFarmTools + producedHousingUnits;
            double toolsConsumed = complexItems * 0.15;
            inv.HandTools = Math.Max(0.0, inv.HandTools + producedHandTools - toolsConsumed);

            // Add produced goods to stockpiles
            inv.Pots += producedPots;
            inv.Cloth += producedCloth;
            inv.FarmTools = Math.Max(0.0, inv.FarmTools * 0.85 + producedFarmTools); // 15% wear
            inv.HousingUnits += producedHousingUnits;

            // Specialist Unlock: Improved Tools when Toolmaker is unlocked
            if (soc.HasTech(NeolithicTech.Toolmaking) && inv.Stone >= 10 && inv.Wood >= 10 && inv.Tools < camp.Population * 0.5)
            {
                double makeTools = Math.Min(20.0, Math.Min(inv.Stone, inv.Wood));
                inv.Stone -= makeTools;
                inv.Wood -= makeTools;
                inv.Tools += makeTools;
            }

            // Award Craft Progress Points
            AwardCraftProgress(w, soc, NeolithicTech.Toolmaking, producedHandTools);
            AwardCraftProgress(w, soc, NeolithicTech.Pottery, producedPots);
            AwardCraftProgress(w, soc, NeolithicTech.Weaving, producedCloth);
            AwardCraftProgress(w, soc, NeolithicTech.Carpentry, producedFarmTools);
            AwardCraftProgress(w, soc, NeolithicTech.Construction, producedHousingUnits);

            // Convergence Technologies Progress
            CheckConvergenceProgress(w, soc, camp, producedHandTools, producedPots, producedCloth, producedFarmTools, producedHousingUnits, record);
        }

        private static void AwardCraftProgress(WorldState w, Society soc, string track, double points)
        {
            if (points <= 0) return;
            if (!soc.CraftExperience.ContainsKey(track)) soc.CraftExperience[track] = 0.0;
            soc.CraftExperience[track] += points;

            if (soc.CraftExperience[track] >= w.Config.CraftBaseThreshold && !soc.HasTech(track))
            {
                soc.NeolithicTechnologies.Add(track);
                string specialist = SpecialistForTrack(track);
                if (!string.IsNullOrEmpty(specialist) && !soc.UnlockedSpecialists.Contains(specialist))
                {
                    soc.UnlockedSpecialists.Add(specialist);
                }
            }
        }

        private static string SpecialistForTrack(string track)
        {
            switch (track)
            {
                case NeolithicTech.Toolmaking: return WorkforceRoles.Toolmaker;
                case NeolithicTech.Pottery: return WorkforceRoles.Potter;
                case NeolithicTech.Weaving: return WorkforceRoles.Weaver;
                case NeolithicTech.Carpentry: return WorkforceRoles.Carpenter;
                case NeolithicTech.Construction: return WorkforceRoles.Builder;
                default: return null;
            }
        }

        private static void CheckConvergenceProgress(WorldState w, Society soc, Camp camp, double tools, double pots, double cloth, double farmTools, double housing, TurnRecord record)
        {
            // 1. Wheel and Axle Convergence (Toolmaker + Carpenter + Builder + Potter)
            // Diversity synergy: +10% bonus for each distinct contributing producer active in turn
            int contributors = 0;
            if (tools > 0) contributors++;
            if (pots > 0) contributors++;
            if (farmTools > 0) contributors++;
            if (housing > 0) contributors++;

            if (contributors >= 2 && !soc.HasTech(NeolithicTech.WheelAndAxle))
            {
                double diversityBonus = 1.0 + (contributors * 0.10);
                double workPoints = (tools + pots + farmTools + housing) * 0.25 * diversityBonus;
                if (!soc.CraftExperience.ContainsKey(NeolithicTech.WheelAndAxle)) soc.CraftExperience[NeolithicTech.WheelAndAxle] = 0.0;
                soc.CraftExperience[NeolithicTech.WheelAndAxle] += workPoints;

                if (soc.CraftExperience[NeolithicTech.WheelAndAxle] >= w.Config.CraftConvergenceThreshold)
                {
                    soc.NeolithicTechnologies.Add(NeolithicTech.WheelAndAxle);
                    record.Log($"Tribe {camp.Player} mastered Wheel and Axle convergence technology!");
                }
            }

            // 2. Copper Working Convergence (Potter + Toolmaker + Builder + Copper Exposure)
            bool copperExposure = false;
            if (w.Resources != null && w.Resources.Bodies != null)
            {
                copperExposure = w.Resources.Bodies.Any(b =>
                    b.Components != null && b.Components.Any(c => c.Material == MaterialId.Copper) &&
                    b.Sites != null && b.Sites.Any(s => w.Map.Distance(camp.Tile, s.Tile) <= 1));
            }

            if (copperExposure && !soc.HasTech(NeolithicTech.CopperWorking))
            {
                double kilnBonus = soc.HasTech(NeolithicTech.KilnFiring) ? 1.50 : 1.0;
                double copperPoints = (pots + tools + housing) * 0.20 * kilnBonus;
                if (!soc.CraftExperience.ContainsKey(NeolithicTech.CopperWorking)) soc.CraftExperience[NeolithicTech.CopperWorking] = 0.0;
                soc.CraftExperience[NeolithicTech.CopperWorking] += copperPoints;

                if (soc.CraftExperience[NeolithicTech.CopperWorking] >= w.Config.CraftConvergenceThreshold)
                {
                    soc.NeolithicTechnologies.Add(NeolithicTech.CopperWorking);
                    record.Log($"Tribe {camp.Player} discovered Copper Working through pyrotechnology and metal exposure!");
                }
            }
        }

        private static void ExecuteFission(WorldState w, Camp parentCamp, TurnRecord record)
        {
            int maxSplinter = Math.Max(0, parentCamp.Population - 25);
            int splinterCount = Math.Min(parentCamp.Population / 2, Math.Min(maxSplinter, (int)Math.Floor(parentCamp.FissionWilling)));
            if (splinterCount < 25 || parentCamp.Population - splinterCount < 25) return;

            // Find nearest viable unowned traversable land tile with freshwater
            int bestTile = -1;
            int bestDist = int.MaxValue;
            foreach (int n in w.Map.Radius(parentCamp.Tile, 5))
            {
                int dist = w.Map.Distance(parentCamp.Tile, n);
                if (dist < 2) continue;

                var t = w.Tiles[n];
                if (!t.TraversableLand || t.DrinkableWater <= 0) continue;
                if (w.Camps.Any(c => c.Tile == n)) continue;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTile = n;
                }
            }

            if (bestTile < 0) return; // No suitable colonization site found

            // Deduct population from parent village
            parentCamp.Population -= splinterCount;
            parentCamp.Workforce?.Normalize(parentCamp.Population);

            // Create splinter camp
            double share = (double)splinterCount / (parentCamp.Population + splinterCount);
            double splinterFood = parentCamp.Food * share;
            parentCamp.Food -= splinterFood;
            if (splinterFood > 0)
            {
                var dem = record.Demographics.FirstOrDefault(d => d.Camp == parentCamp.Id);
                if (dem != null) dem.DiscardedInventory += splinterFood;
            }

            var newCamp = new Camp
            {
                Id = w.NextId++,
                Player = parentCamp.Player,
                SocietyId = parentCamp.SocietyId,
                Tile = bestTile,
                Population = splinterCount,
                IsVillage = false,
                StationaryTurns = 0,
                VillageTurns = 0,
                Food = splinterFood,
                Inventory = new CampInventory
                {
                    HandTools = parentCamp.Inventory.HandTools * share,
                    Pots = Math.Ceiling(parentCamp.Inventory.Pots * share),
                    Cloth = parentCamp.Inventory.Cloth * share,
                    Stone = parentCamp.Inventory.Stone * share,
                    Clay = parentCamp.Inventory.Clay * share,
                    Wood = parentCamp.Inventory.Wood * share,
                    Houses = Math.Ceiling(splinterCount / 8.0)
                }
            };
            newCamp.Bind(parentCamp.People);
            w.Camps.Add(newCamp);

            // Deduct transferred inventory from parent village
            parentCamp.Inventory.HandTools = Math.Max(0.0, parentCamp.Inventory.HandTools - newCamp.Inventory.HandTools);
            parentCamp.Inventory.Pots = Math.Max(0.0, parentCamp.Inventory.Pots - newCamp.Inventory.Pots);
            parentCamp.Inventory.Cloth = Math.Max(0.0, parentCamp.Inventory.Cloth - newCamp.Inventory.Cloth);
            parentCamp.Inventory.Stone = Math.Max(0.0, parentCamp.Inventory.Stone - newCamp.Inventory.Stone);
            parentCamp.Inventory.Clay = Math.Max(0.0, parentCamp.Inventory.Clay - newCamp.Inventory.Clay);
            parentCamp.Inventory.Wood = Math.Max(0.0, parentCamp.Inventory.Wood - newCamp.Inventory.Wood);

            record.Log($"Settlement Fission: {splinterCount} residents split from Village {parentCamp.Id} and founded Camp {newCamp.Id} on Tile {bestTile}.");
        }
    }
}
