using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class NeolithicConfig
    {
        public double ReferencePopulation = 250, ReferenceFootprintTiles = 7, ReferenceTurnYears = 20, IntensificationTurns = 10;
        public double AllocationStepE = .01, PlanningHorizonYears = 100, DecayHalfLifeYears = 80;
        // Integrated d_conversion per realized fraction; no separate farm biome or material cost.
        public double ClearingConversionPressure = 1, TimberConversionPressure = 1;
        public double OpenBuild = 1, WoodlandBuild = 1.3, ForestBuild = 1.5, OpenMaintenance = 1, WoodlandMaintenance = 1.1, ForestMaintenance = 1.2;
        public double GeneralistTaskEfficiency = .25, SpecialistMaterialExtractionRate = 1, MaterialReserveHorizonTurns = 1;
        public double HandToolsOutput = 1, PotsOutput = 1, WeaverFabricOutputPerBaseDays = 1, FarmToolsOutput = 1, ToolsOutput = 1;
        public double BuilderHousingOutputPerBaseDays = .03125, HandToolsHousingMultiplier = 1, ImprovedToolsHousingMultiplier = 1.5, CopperToolsHousingMultiplier = 2;
        public double PlantFoodPerFabricUnit = 1, HousingMaterialUnitsPerHousing = 1;
        public double StonePerHandTool = 1, ClayPerPot = 1, WoodPerFarmTool = 1, StonePerTool = 1, WoodPerTool = 1;
        public double ToolCoveragePerWorker = 1, CraftMasteryThreshold = 400, UsefulFinishedGoodsReserveTurns = 1;
        public double FabricConsumptionPerPersonTurn = 1, HousingPeoplePerUnit = 4, HousingDecayPerTurn = .10;
        [System.Runtime.Serialization.OptionalField] public double PotStorageFoodPerUnit = 80, PotDecayPerReferenceTurn = .10;
        public double RudimentaryWeightClothing = .5, RudimentaryWeightHousing = .5, RudimentaryExpectationFloor = .25, RudimentaryExpectationRampYears = 100;
        public double BuildWorkerYearsPerTileE => ReferencePopulation * ReferenceTurnYears * IntensificationTurns / ReferenceFootprintTiles;
        public double MaintenanceWorkersPerTileE(double coefficient) => ReferencePopulation * coefficient / ReferenceFootprintTiles;
        [System.Runtime.Serialization.OnDeserializing]
        private void InitializePotStorageDefaults(System.Runtime.Serialization.StreamingContext context)
        {
            PotStorageFoodPerUnit = 80;
            PotDecayPerReferenceTurn = .10;
        }
        public void Validate()
        {
            if (new[] { OpenBuild, WoodlandBuild, ForestBuild, WeaverFabricOutputPerBaseDays, BuilderHousingOutputPerBaseDays, GeneralistTaskEfficiency }.Any(v => v <= 0)) throw new ArgumentException("Neolithic construction and allocation rates must be positive.");
            var values = GetType().GetFields().Where(f => f.FieldType == typeof(double)).Select(f => (double)f.GetValue(this));
            if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0) || ReferencePopulation <= 0 || ReferenceFootprintTiles != 7 || ReferenceTurnYears <= 0 || IntensificationTurns <= 0 || AllocationStepE <= 0 || DecayHalfLifeYears <= 0 || HousingPeoplePerUnit <= 0 || HousingDecayPerTurn >= 1 || PotStorageFoodPerUnit <= 0 || PotDecayPerReferenceTurn >= 1 || ToolCoveragePerWorker <= 0 || RudimentaryExpectationRampYears <= 0 || RudimentaryExpectationFloor > 1 || Math.Abs(RudimentaryWeightClothing + RudimentaryWeightHousing - 1) > 1e-10 || GeneralistTaskEfficiency > 1 || CraftMasteryThreshold <= 0)
                throw new ArgumentException("Invalid Neolithic calibration.");
        }
    }
}
