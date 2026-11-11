using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Null in legacy configurations. Numerical values are engineering calibration,
    // not historical measurements. All durations are physical years.
    [Serializable]
    public sealed class AgriculturalTechnologySettings
    {
        public bool PloughEnabled, IrrigationEnabled;
        public double PloughMaintenance = 1.50, PloughHarvest = 1.25;
        public double DevelopmentQ = .30, FertileMoisture = .50, TargetMoisture = .50;
        public double CanalBuilderYears = 5, CanalHousingMaterials = 10;
        public double UpkeepBuilderYears = .25, UpkeepHousingMaterials = .10;
        public double AnnualDrinkingPerPerson = .10;
        public double AnnualApplication = 1000, AnnualThroughput = 5000, Retention = .80;
        public double SoilYears = 500, SoilCapacityGain = .50, SoilReferenceQ = .40;
        public double HydraulicTolerance = .00001;
        public void Validate()
        {
            foreach (var f in GetType().GetFields().Where(f => f.FieldType == typeof(double)))
            {
                double v = (double)f.GetValue(this);
                if (double.IsNaN(v) || double.IsInfinity(v) || v < 0)
                    throw new ArgumentException("Invalid agricultural technology setting: " + f.Name);
            }
            if (PloughMaintenance < 1 || PloughHarvest < 1 || DevelopmentQ > 1 || FertileMoisture <= 0 ||
                FertileMoisture > 1 || TargetMoisture <= 0 || TargetMoisture > 1 ||
                CanalBuilderYears <= 0 || CanalHousingMaterials <= 0 || UpkeepBuilderYears <= 0 ||
                UpkeepHousingMaterials <= 0 || AnnualApplication <= 0 || AnnualThroughput <= 0 ||
                Retention <= 0 || Retention > 1 || SoilYears <= 0 || SoilReferenceQ <= 0)
                throw new ArgumentException("Invalid agricultural technology calibration.");
        }
    }

    [Serializable]
    public sealed class AgriculturalFoodReceipt
    {
        public int Camp, Tile;
        // The same opening capacity composition used by crop-only harvesting.
        // These are attributed shares of one conserved plant-food receipt.
        public double CropFood, WildFood;
    }

    public static class AgriculturalTechnology
    {
        internal static Func<WorldState, Camp, int?> FixedFieldStaffingDiagnostic;
        public static bool Plough(WorldState w, Camp c)
            => w.Config.Agriculture?.PloughEnabled == true && c.People.HasTech(NeolithicTech.Plough);
        public static double MaintenanceEfficiency(WorldState w, Camp c)
            => Plough(w, c) ? w.Config.Agriculture.PloughMaintenance
                : CultivationEconomy.FarmToolFieldWorkEfficiency(w.Config.Neolithic);
        internal static double OperatingFarmers(Camp c)
            => Math.Max(0, CultivationEconomy.Workers(c, WorkforceRoles.Farmer) - Math.Max(0, c.EconomicLabor?.ExpansionFarmers ?? 0));
        internal static double Coverage(WorldState w, Camp c, double years, double farmers)
            => farmers <= 0 || years <= 0 ? 0 : Math.Min(1, Math.Max(0, c.Inventory.FarmTools) *
                w.Config.Neolithic.FarmToolFarmerYearsPerUnit / (farmers * years));

        // Called at the realized useful-output receipt, before its ordinary craft
        // unlock. A newly acquired prerequisite cannot qualify earlier work.
        internal static void RecordCraft(WorldState w, Camp c, CraftRecipe recipe, string role, double work)
        {
            if (w.Config.Agriculture == null || work <= 0 || !CultivationEconomy.Enabled(c) ||
                OperatingFarmers(c) <= 0 || !CultivationEconomy.Footprint(w, c).Any(id =>
                    w.Tiles[id].CultivationOperator == c.Id && w.Tiles[id].CultivationCapital > 0)) return;
            string tech = null;
            if (w.Config.Agriculture.PloughEnabled && role == WorkforceRoles.Carpenter &&
                recipe.Good == CraftGood.FarmTools && c.People.HasTech(NeolithicTech.Carpentry)) tech = NeolithicTech.Plough;
            if (w.Config.Agriculture.IrrigationEnabled && role == WorkforceRoles.Builder &&
                c.People.HasTech(NeolithicTech.Construction) &&
                !c.People.HasTech(NeolithicTech.Irrigation) && IrrigationEconomy.Opportunity(w, c, discoveryOnly: true) != null) tech = NeolithicTech.Irrigation;
            if (tech == null || c.People.HasTech(tech)) return;
            c.People.CraftExperience.TryGetValue(tech, out double prior);
            c.People.CraftExperience[tech] = prior + work;
        }
        internal static void Close(WorldState w, TurnRecord record)
        {
            if (w.Config.Agriculture == null) return;
            foreach (var society in w.Societies.OrderBy(s => s.Id))
                foreach (string tech in new[] { NeolithicTech.Plough, NeolithicTech.Irrigation })
                {
                    bool enabled = tech == NeolithicTech.Plough ? w.Config.Agriculture.PloughEnabled : w.Config.Agriculture.IrrigationEnabled;
                    if (enabled && !society.HasTech(tech) && society.CraftExperience.TryGetValue(tech, out double value) &&
                        value >= w.Config.Neolithic.CraftMasteryThreshold)
                    {
                        society.NeolithicTechnologies.Add(tech);
                        record.Log("Society " + society.Id + " attained " + tech + " through useful agricultural work.");
                    }
                }
        }
    }
}
