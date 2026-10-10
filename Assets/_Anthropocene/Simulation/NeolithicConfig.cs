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
        public double GeneralistTaskEfficiency = .25, SpecialistMaterialExtractionRate = 1;
        // True specialists gain a bounded local-concentration advantage only on
        // their own recipe/task. Generalists retain the fixed efficiency above.
        // M(N) = 1 + Bmax (1 - exp(-max(0, N - N0) / Nscale)).
        [System.Runtime.Serialization.OptionalField] public double SpecialistScaleMaxBonus = 0, SpecialistScaleOnsetPopulation = 100, SpecialistScalePopulation = 400;
        // Reserve horizons are physical time. The 20-year default preserves the
        // Neolithic reference turn while remaining stable when turn duration changes.
        [System.Runtime.Serialization.OptionalField] public double MaterialReserveHorizonYears = 20;
        [System.Runtime.Serialization.OptionalField] public double MaterialReserveHorizonTurns = 1;
        [NonSerialized] private bool legacyMaterialReserveTurns;
        [System.Runtime.Serialization.OptionalField] public double PrimitiveAgricultureSupportedIntensity = .40, StoneFarmToolsSupportedIntensity = .45;
        // One preserves the historical quadratic worker cost. Values above one
        // multiply marginal build and maintenance effort per additional .1 q.
        [System.Runtime.Serialization.OptionalField] public double CultivationMarginalEffortMultiplierPerTenth = 16;
        // Experimental labor-cost junctions. These do not change physical
        // capital E or q(E); they only select where nonlinear marginal build
        // and maintenance effort begins. The shipped .40 values preserve the
        // historical curve exactly. Field and fishery knees are independent so
        // counterfactual efficiency changes need not invent a technology tree.
        [System.Runtime.Serialization.OptionalField] public double CultivationMarginalEffortKneeQ = .40;
        [System.Runtime.Serialization.OptionalField] public double FishingMarginalEffortKneeQ = .40;
        // Fishing progression is calibrated independently of agricultural
        // tool tiers. Defaults preserve the primitive frontier until measured.
        [System.Runtime.Serialization.OptionalField] public double PrimitiveFishingSupportedIntensity = .40,
            SailingFishingSupportedIntensity = .40,
            CopperFishingSupportedIntensity = .40;
        // Explicit experiment gates. Production play resolves q_k from each
        // settlement's legal capabilities unless the corresponding gate is on.
        // Keeping the values separate from the gates lets controlled sweeps
        // force the shipped .40 regime as well as counterfactual .50/.80 regimes.
        [System.Runtime.Serialization.OptionalField] public bool ExperimentalCultivationEffortKneeOverride;
        [System.Runtime.Serialization.OptionalField] public bool ExperimentalFishingEffortKneeOverride;
        [System.Runtime.Serialization.OptionalField] public double PloughSupportedIntensity = .52, CopperFarmToolsSupportedIntensity = .62, IronFarmToolsSupportedIntensity = .78, PoweredFarmToolsSupportedIntensity = 1.0;
        [System.Runtime.Serialization.OptionalField] public double FarmToolFarmerYearsPerUnit = 20;
        [System.Runtime.Serialization.OptionalField] public double StoneHerdingToolAnimalsPerHerder = 2.69;
        [System.Runtime.Serialization.OptionalField] public double EarlyAdvancedHerdingAnimalsPerHerder = 3.87, CopperHerdingAnimalsPerHerder = 6.02, IronHerdingAnimalsPerHerder = 10.71, PoweredHerdingAnimalsPerHerder = 20.0;
        [System.Runtime.Serialization.OptionalField] public double HerdingToolCoverageYearsPerUnit = 20;
        // Fishing Tools cover Fisher service-years and increase only resident
        // Fishing harvest work. They do not alter aquatic biology or crafting speed.
        [System.Runtime.Serialization.OptionalField] public double FishingToolFisherYearsPerUnit = 20;
        [System.Runtime.Serialization.OptionalField] public double FishingToolHarvestMultiplier = 1.5;
        // Social cohesion S(N) = exp(-(N / N_s)^beta) with N_s = N_band (1 + k H), derived from housing (Docs/SocialCohesion.md):
        // H = min(1, (fixed capacity + p_tent * portable capacity) / N). Saves written before the derived rule carry the retired
        // CohesionScalePaleolithic/Nomadic/Agricultural fields, which are ignored; they load the new defaults.
        [System.Runtime.Serialization.OptionalField] public double SocialCohesionExponent = 2;
        [System.Runtime.Serialization.OptionalField] public double CohesionBandScale = 220, CohesionHousingGain = 7.1, LightHousingPermanence = .21, LightHousingPeoplePerUnit = 3;
        // Local value rule (ValueModelDesign.md). PlannerFulfilment selects the planner's fulfilment rule by name:
        // "Value" (default; shares from a labour price w, value-ordered production) or "Uniform" (every claim
        // shares the path position). A string keeps sweeps and saved configs readable; saves written before
        // the field existed load as "Uniform" so they continue exactly (see the OnDeserializing hook).
        // lambda = ValueCurveSteepness (> 0, dimensionless), r = DiscountRatePerYear (>= 0, per year).
        [System.Runtime.Serialization.OptionalField] public string PlannerFulfilment = "Value";
        [System.Runtime.Serialization.OptionalField] public double ValueCurveSteepness = 3, DiscountRatePerYear = .01;
        // Expected remaining life of a person in years (> 0); the value of a birth is the QoL-years it adds, discounted at r
        // (L_person = 1 / (1 / PersonLifeYears + r)). Used only by the Value rule's expansion valuation (WP4b).
        [System.Runtime.Serialization.OptionalField] public double PersonLifeYears = 30;
        // Risk placeholder rho: the share of food-security value credited to food that
        // only protects against shocks (operating-reserve refill; Pots in WP4a), since
        // the model does not yet produce disasters. Value rule only.
        [System.Runtime.Serialization.OptionalField] public double FoodRiskWeight = .1;
        public double HandToolsOutput = 1, PotsOutput = 1, WeaverFabricOutputPerBaseDays = 1, FarmToolsOutput = 1, ToolsOutput = 1;
        public double BuilderHousingOutputPerBaseDays = .03125, HandToolsHousingMultiplier = 1, ImprovedToolsHousingMultiplier = 1.5, CopperToolsHousingMultiplier = 2;
        [System.Runtime.Serialization.OptionalField] public double LightHousingBuilderOutputPerBaseDays = .03125;
        public double PlantFoodPerFabricUnit = 1, HousingMaterialUnitsPerHousing = 1;
        [System.Runtime.Serialization.OptionalField] public double AnimalFabricPerLightHousing = 1;
        public double StonePerHandTool = 1, ClayPerPot = 1, WoodPerFarmTool = 1, StonePerTool = 1, WoodPerTool = 1;
        public double ToolCoveragePerWorker = 1, CraftMasteryThreshold = 400;
        [System.Runtime.Serialization.OptionalField] public double UsefulFinishedGoodsReserveYears = 20;
        [System.Runtime.Serialization.OptionalField] public double UsefulFinishedGoodsReserveTurns = 1;
        public double FabricConsumptionPerPersonTurn = 1, HousingPeoplePerUnit = 4, HousingDecayPerTurn = .10;
        [System.Runtime.Serialization.OptionalField] public double LightHousingDecayPerReferenceTurn = .10;
        [System.Runtime.Serialization.OptionalField] public double PotStorageFoodPerUnit = 80, PotDecayPerReferenceTurn = .10;
        public double RudimentaryWeightClothing = .5, RudimentaryWeightHousing = .5, RudimentaryExpectationFloor = .25, RudimentaryExpectationRampYears = 100;
        public double BuildWorkerYearsPerTileE => ReferencePopulation * ReferenceTurnYears * IntensificationTurns / ReferenceFootprintTiles;
        public double MaintenanceWorkersPerTileE(double coefficient) => ReferencePopulation * coefficient / ReferenceFootprintTiles;
        [System.Runtime.Serialization.OnDeserializing]
        private void InitializePotStorageDefaults(System.Runtime.Serialization.StreamingContext context)
        {
            // OptionalField members are initialized here so older ANTHROPOCENE-11
            // saves can be distinguished from saves that carry the new fields.
            MaterialReserveHorizonYears = double.NaN;
            UsefulFinishedGoodsReserveYears = double.NaN;
            MaterialReserveHorizonTurns = double.NaN;
            UsefulFinishedGoodsReserveTurns = double.NaN;
            PotStorageFoodPerUnit = 80;
            PotDecayPerReferenceTurn = .10;
            // Missing portable-housing fields mean the historical shared
            // Housing values. The deserialized shared values are copied in the
            // OnDeserialized migration after present fields overwrite these NaNs.
            LightHousingBuilderOutputPerBaseDays = double.NaN;
            AnimalFabricPerLightHousing = double.NaN;
            LightHousingDecayPerReferenceTurn = double.NaN;
            // Older saves predate specialist scaling and continue at the exact
            // fixed specialist rate. New worlds use the field initializers.
            SpecialistScaleMaxBonus = 0;
            SpecialistScaleOnsetPopulation = 100;
            SpecialistScalePopulation = 400;
            PrimitiveAgricultureSupportedIntensity = .40;
            CultivationMarginalEffortMultiplierPerTenth = 1;
            CultivationMarginalEffortKneeQ = .40;
            FishingMarginalEffortKneeQ = .40;
            PrimitiveFishingSupportedIntensity = .40;
            SailingFishingSupportedIntensity = .40;
            CopperFishingSupportedIntensity = .40;
            ExperimentalCultivationEffortKneeOverride = false;
            ExperimentalFishingEffortKneeOverride = false;
            StoneFarmToolsSupportedIntensity = .45;
            PloughSupportedIntensity = .52;
            CopperFarmToolsSupportedIntensity = .62;
            IronFarmToolsSupportedIntensity = .78;
            PoweredFarmToolsSupportedIntensity = 1.0;
            FarmToolFarmerYearsPerUnit = 20;
            StoneHerdingToolAnimalsPerHerder = 2.69;
            EarlyAdvancedHerdingAnimalsPerHerder = 3.87; CopperHerdingAnimalsPerHerder = 6.02; IronHerdingAnimalsPerHerder = 10.71; PoweredHerdingAnimalsPerHerder = 20.0;
            HerdingToolCoverageYearsPerUnit = 20;
            FishingToolFisherYearsPerUnit = 20; FishingToolHarvestMultiplier = 1.5;
            SocialCohesionExponent = 2;
            CohesionBandScale = 220; CohesionHousingGain = 7.1; LightHousingPermanence = .21; LightHousingPeoplePerUnit = 3;
            // Saves written before the value rule carry no field and keep the Uniform planner, so they
            // continue exactly; new worlds and code defaults use Value.
            PlannerFulfilment = "Uniform"; ValueCurveSteepness = 3; DiscountRatePerYear = .01; PersonLifeYears = 30; FoodRiskWeight = .1;
        }
        [System.Runtime.Serialization.OnDeserialized]
        private void MigratePhysicalReserveHorizons(System.Runtime.Serialization.StreamingContext context)
        {
            bool missingMaterialYears = double.IsNaN(MaterialReserveHorizonYears);
            if (missingMaterialYears)
            {
                legacyMaterialReserveTurns = !double.IsNaN(MaterialReserveHorizonTurns);
                MaterialReserveHorizonYears = double.IsNaN(MaterialReserveHorizonTurns) ? 20 : MaterialReserveHorizonTurns * ReferenceTurnYears;
            }
            if (double.IsNaN(UsefulFinishedGoodsReserveYears))
                UsefulFinishedGoodsReserveYears = double.IsNaN(UsefulFinishedGoodsReserveTurns) ? 20 : UsefulFinishedGoodsReserveTurns * ReferenceTurnYears;
            if (double.IsNaN(MaterialReserveHorizonTurns)) MaterialReserveHorizonTurns = MaterialReserveHorizonYears / ReferenceTurnYears;
            if (double.IsNaN(UsefulFinishedGoodsReserveTurns)) UsefulFinishedGoodsReserveTurns = UsefulFinishedGoodsReserveYears / ReferenceTurnYears;
            if (double.IsNaN(LightHousingBuilderOutputPerBaseDays)) LightHousingBuilderOutputPerBaseDays = BuilderHousingOutputPerBaseDays;
            if (double.IsNaN(AnimalFabricPerLightHousing)) AnimalFabricPerLightHousing = 1;
            if (double.IsNaN(LightHousingDecayPerReferenceTurn)) LightHousingDecayPerReferenceTurn = HousingDecayPerTurn;
        }
        public double MaterialReserveDurationYears(double turnYears)
            => legacyMaterialReserveTurns ? MaterialReserveHorizonTurns * Math.Max(0, turnYears) : MaterialReserveHorizonYears;
        public void MigrateLegacyMaterialReserveDuration(double turnYears)
        {
            if (!legacyMaterialReserveTurns) return;
            MaterialReserveHorizonYears = MaterialReserveHorizonTurns * Math.Max(0, turnYears);
            legacyMaterialReserveTurns = false;
        }
        public double MaterialReserveMultiplier(double turnYears)
            => turnYears <= 0 ? 1 : 1 + MaterialReserveDurationYears(turnYears) / turnYears;
        public double UsefulFinishedGoodsReserveReferenceTurns
            => UsefulFinishedGoodsReserveYears / ReferenceTurnYears;
        public bool ValueFulfilmentSelected => PlannerFulfilment == "Value";
        public void Validate()
        {
            if (PlannerFulfilment != "Uniform" && PlannerFulfilment != "Value") throw new ArgumentException("Invalid planner fulfilment rule.");
            if (!(ValueCurveSteepness > 0)) throw new ArgumentException("Invalid value curve steepness.");
            if (!(PersonLifeYears > 0) || double.IsInfinity(PersonLifeYears)) throw new ArgumentException("Invalid person life years.");
            if (!(FoodRiskWeight >= 0) || double.IsInfinity(FoodRiskWeight)) throw new ArgumentException("Invalid food risk weight.");
            if (CultivationMarginalEffortMultiplierPerTenth < 1 || CultivationMarginalEffortMultiplierPerTenth > 32)
                throw new ArgumentException("Invalid cultivation effort multiplier.");
            if (!(CultivationMarginalEffortKneeQ > 0) ||
                CultivationMarginalEffortKneeQ > 1 ||
                !(FishingMarginalEffortKneeQ > 0) ||
                FishingMarginalEffortKneeQ > 1)
                throw new ArgumentException("Invalid capital-effort knee.");
            if (!(PrimitiveFishingSupportedIntensity > 0) ||
                SailingFishingSupportedIntensity <
                    PrimitiveFishingSupportedIntensity ||
                CopperFishingSupportedIntensity <
                    SailingFishingSupportedIntensity ||
                CopperFishingSupportedIntensity > 1)
                throw new ArgumentException("Invalid fishing technology frontier.");
            if (new[] { OpenBuild, WoodlandBuild, ForestBuild, WeaverFabricOutputPerBaseDays, BuilderHousingOutputPerBaseDays, LightHousingBuilderOutputPerBaseDays, AnimalFabricPerLightHousing, GeneralistTaskEfficiency, SpecialistScalePopulation, FishingToolFisherYearsPerUnit, FishingToolHarvestMultiplier }.Any(v => v <= 0)) throw new ArgumentException("Neolithic construction and allocation rates must be positive.");
            var values = GetType().GetFields().Where(f => f.FieldType == typeof(double)).Select(f => (double)f.GetValue(this));
            if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0) || ReferencePopulation <= 0 || ReferenceFootprintTiles != 7 || ReferenceTurnYears <= 0 || IntensificationTurns <= 0 || AllocationStepE <= 0 || DecayHalfLifeYears <= 0 || HousingPeoplePerUnit <= 0 || LightHousingPeoplePerUnit <= 0 || CohesionBandScale <= 0 || SocialCohesionExponent <= 0 || LightHousingPermanence > 1 || HousingDecayPerTurn >= 1 || LightHousingDecayPerReferenceTurn >= 1 || PotStorageFoodPerUnit <= 0 || PotDecayPerReferenceTurn >= 1 || ToolCoveragePerWorker <= 0 || FarmToolFarmerYearsPerUnit <= 0 || StoneHerdingToolAnimalsPerHerder < 2 || EarlyAdvancedHerdingAnimalsPerHerder < StoneHerdingToolAnimalsPerHerder || CopperHerdingAnimalsPerHerder < EarlyAdvancedHerdingAnimalsPerHerder || IronHerdingAnimalsPerHerder < CopperHerdingAnimalsPerHerder || PoweredHerdingAnimalsPerHerder < IronHerdingAnimalsPerHerder || HerdingToolCoverageYearsPerUnit <= 0 || PrimitiveAgricultureSupportedIntensity <= 0 || StoneFarmToolsSupportedIntensity < PrimitiveAgricultureSupportedIntensity || PloughSupportedIntensity < StoneFarmToolsSupportedIntensity || CopperFarmToolsSupportedIntensity < PloughSupportedIntensity || IronFarmToolsSupportedIntensity < CopperFarmToolsSupportedIntensity || PoweredFarmToolsSupportedIntensity < IronFarmToolsSupportedIntensity || PoweredFarmToolsSupportedIntensity > 1 || RudimentaryExpectationRampYears <= 0 || RudimentaryExpectationFloor > 1 || Math.Abs(RudimentaryWeightClothing + RudimentaryWeightHousing - 1) > 1e-10 || GeneralistTaskEfficiency > 1 || SpecialistScaleMaxBonus > 1 || CraftMasteryThreshold <= 0 || CohesionHousingGain < 0)
                throw new ArgumentException("Invalid Neolithic calibration.");
        }
    }
}
