using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class AgriculturalLabor
    {
        public double OperatingFarmers, ExpansionFarmers, Gatherers, FoodGatherers, GatheringWorkerIntervals;
        public int[] Footprint = Array.Empty<int>();
    }

    public static class CultivationEconomy
    {
        public static bool Enabled(Camp camp) => camp.Technology == Technology.Cultivation || camp.Technologies[(int)Technology.Cultivation];
        public static double Intensity(SimulationConfig c, double capital) => Math.Min(1, c.ReferenceIntensity * Math.Sqrt(Math.Max(0, capital)));
        public static double Decay(SimulationConfig c, double capital, double operatingFraction, double years) => capital * Math.Exp(-Math.Log(2) / c.Neolithic.DecayHalfLifeYears * (1 - Numbers.Clamp(operatingFraction, 0, 1)) * years);
        public static void Factors(SimulationConfig c, Tile t, out double build, out double maintenance)
        {
            var n = c.Neolithic;
            build = !Vegetation.IsWooded(t) ? n.OpenBuild : t.CurrentVegetation == VegetationTier.High ? n.ForestBuild : n.WoodlandBuild;
            maintenance = !Vegetation.IsWooded(t) ? n.OpenMaintenance : t.CurrentVegetation == VegetationTier.High ? n.ForestMaintenance : n.WoodlandMaintenance;
        }
        public static int Workers(Camp camp, string role) => camp.Workforce.Assignments.Where(a => a.Role == role).Sum(a => a.Population);
        public static int[] Footprint(WorldState w, Camp camp) => w.Map.Radius(camp.Tile, 1).Where(id => w.Tiles[id].TraversableLand && w.Tiles[id].Environment != EnvironmentKind.Ice && w.Tiles[id].Seasons.Any(s => s.MaxPotentialFood > 0)).OrderBy(id => id).ToArray();
        public static AgriculturalLabor Prepare(WorldState w, Camp camp, double start)
        {
            var plan = new AgriculturalLabor { Gatherers = camp.GathererPopulation + Workers(camp, WorkforceRoles.Gatherer) };
            plan.FoodGatherers = Math.Max(0, plan.Gatherers - (camp.EconomicLabor?.MaterialGatherers ?? 0));
            if (!Enabled(camp) || camp.Population <= 0) return plan;
            plan.Footprint = Footprint(w, camp);
            double required = 0;
            foreach (int id in plan.Footprint)
            {
                var t = w.Tiles[id]; LazyEcology.MaterializeEcology(w, id, start);
                if (t.CultivationOperator >= 0 && !w.Camps.Any(c => c.Id == t.CultivationOperator && c.Population > 0 && Enabled(c) && w.Map.Distance(c.Tile, id) <= 1)) t.CultivationOperator = -1;
                if (t.CultivationCapital > 0 && t.CultivationOperator < 0) t.CultivationOperator = camp.Id;
                if (t.CultivationOperator != camp.Id) continue;
                Factors(w.Config, t, out _, out double maint);
                required += w.Config.Neolithic.MaintenanceWorkersPerTileE(w.Config.MaintenanceCoefficient) * maint * t.CultivationCapital;
            }
            double farmers = Workers(camp, WorkforceRoles.Farmer);
            plan.OperatingFarmers = Math.Min(farmers, required);
            plan.ExpansionFarmers = Math.Max(0, farmers - plan.OperatingFarmers);
            bool established = plan.Footprint.Any(id => w.Tiles[id].CultivationOperator == camp.Id && w.Tiles[id].CultivationCapital > 0);
            if (camp.EconomicLabor != null)
            {
                // Maintenance is a minimum, not a ceiling on harvesting employment.
                // Explicit expansion time cannot simultaneously harvest or do upkeep.
                plan.ExpansionFarmers = Math.Min(plan.ExpansionFarmers, camp.EconomicLabor.ExpansionFarmers);
                plan.OperatingFarmers = established ? farmers - plan.ExpansionFarmers : 0;
            }
            foreach (int id in plan.Footprint.Where(id => w.Tiles[id].CultivationOperator == camp.Id))
                w.Tiles[id].CultivationOperatingFraction = required > 0 ? Math.Min(1, plan.OperatingFarmers / required) : plan.OperatingFarmers > 0 ? 1 : 0;
            return plan;
        }
        // Each increment is scored again against the current capital and upkeep costs.
        public static double Expand(WorldState w, Camp camp, double workerYears, bool bridge, TurnRecord record)
        {
            if (!Enabled(camp) || camp.Population <= 0 || workerYears <= 0 || bridge && w.Config.GathererBridgeEfficiency <= 0) return 0;
            var c = w.Config; var n = c.Neolithic;
            double remaining = workerYears * (bridge ? c.GathererBridgeEfficiency : 1);
            double ceiling = Math.Pow((bridge ? c.GathererBridgeCeiling : 1) / c.ReferenceIntensity, 2);
            var candidates = Footprint(w, camp).Where(id => w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == camp.Id).ToArray();
            while (remaining > 1e-10)
            {
                int best = -1; double bestScore = -1, step = 0, cost = 0;
                foreach (int id in candidates)
                {
                    var t = w.Tiles[id]; double e = t.CultivationCapital;
                    if (e >= ceiling - 1e-12) continue;
                    Factors(c, t, out double vb, out double vm);
                    if (bridge || Intensity(c, e) < c.GathererBridgeCeiling) vb = n.OpenBuild;
                    double de = Math.Min(n.AllocationStepE, Math.Min(ceiling - e, remaining / (n.BuildWorkerYearsPerTileE * vb)));
                    if (!bridge && Intensity(c, e) < c.GathererBridgeCeiling) de = Math.Min(de, Math.Pow(c.GathererBridgeCeiling / c.ReferenceIntensity, 2) - e);
                    if (de <= 1e-12) continue;
                    double q = Intensity(c, e), nextQ = Intensity(c, e + de);
                    double gain = 0;
                    var view = new EcologyView(t);
                    for (int season = 0; season < 4; season++)
                    {
                        var k = new EcologyCapacity(w, t, view, season);
                        double f0 = k.Biomass > 0 ? k.WildFood / k.Biomass : 0;
                        gain += (k.Biomass * (1 - f0) * (nextQ * nextQ - q * q) * k.FoodRate + c.CultivationRegenFlux * (nextQ - q) * k.Biomass * k.BiomassRate) * c.DaysPerYear / 4;
                    }
                    double buildCost = de * n.BuildWorkerYearsPerTileE * vb;
                    double score = gain / Math.Max(1e-12, buildCost + n.PlanningHorizonYears * n.MaintenanceWorkersPerTileE(c.MaintenanceCoefficient) * vm * de);
                    bool focused = id == camp.CultivationFocusTile;
                    if (score > 0 && (best < 0 || focused || best != camp.CultivationFocusTile && score > bestScore)) { best = id; bestScore = score; step = de; cost = buildCost; }
                }
                if (best < 0) break;
                var target = w.Tiles[best]; double oldQ = Intensity(c, target.CultivationCapital);
                target.CultivationOperator = camp.Id; target.CultivationCapital += step;
                target.Cultivation = (float)Intensity(c, target.CultivationCapital); target.Cultivated = target.CultivationCapital > 0;
                remaining -= cost;
                if (!bridge && target.Cultivation > c.GathererBridgeCeiling && Vegetation.IsWooded(target))
                    EcologicalPressure.ApplyConversion(w, best, camp.Id, "Cultivation clearing", "Land conversion", step, step, Math.Max(0, Intensity(c, target.CultivationCapital) - Math.Max(c.GathererBridgeCeiling, oldQ)) * n.ClearingConversionPressure, target.LastEcologyUpdateDays, record);
            }
            return (workerYears * (bridge ? c.GathererBridgeEfficiency : 1) - remaining) / (bridge ? c.GathererBridgeEfficiency : 1);
        }
    }
}
