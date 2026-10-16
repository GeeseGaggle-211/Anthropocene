using System;
using System.Collections.Generic;
using System.Linq;
#pragma warning disable SYSLIB0050 // OptionalField supports DataContractSerializer legacy saves, not BinaryFormatter.

namespace Anthropocene.Simulation
{
    public enum Terrain { Ocean, Shallows, Lake, Grassland, Woodland, Jungle, Desert, Tundra, Mountain, Ice }
    public enum Domain { Hunting, Gathering, Fishing }
    public enum Technology { None, AnimalHusbandry, Cultivation, Watercraft }
    public enum OrderKind { NoOp, Move, Dispatch, Hunt, Fish, Reintegrate, Economy, FissionPolicy }
    public enum WorldShape { Continents, Archipelagos, Pangaea }
    public enum Relief { Lowland, Hills, Highland, Mountain }
    public enum WaterKind { None, Freshwater, Saltwater }
    public enum FreshwaterSource { None, RainPool, Lake, River, Oasis }
    public enum FissionSiteSelection
    {
        EcologicalSupport,
        AcceptedRenewableFood,
        AcceptedAggregateWelfare
    }

    [Serializable]
    public sealed class SimulationConfig
    {
        [System.Runtime.Serialization.OptionalField] public NeolithicConfig Neolithic = new NeolithicConfig();
        public SimulationConfig()
        {
            double referenceYears = Neolithic.ReferenceTurnYears;
            GrowthReserveYears = GrowthReserveTurns * referenceYears;
            MinimumStorageYears = MinimumStorageGenerations * referenceYears;
            MaximumStorageYears = MaximumStorageGenerations * referenceYears;
        }
        [System.Runtime.Serialization.OnDeserializing]
        private void InitializeNeolithic(System.Runtime.Serialization.StreamingContext context)
        {
            var defaults = new SimulationConfig(); Neolithic = defaults.Neolithic;
            // Preserve established tuning defaults when reading older saves that
            // predate these legacy food-horizon fields. Explicit serialized zero
            // values still overwrite these defaults during deserialization.
            GrowthReserveTurns = defaults.GrowthReserveTurns;
            FarmerProductivity = defaults.FarmerProductivity; ReferenceIntensity = defaults.ReferenceIntensity; CultivationRegenFlux = defaults.CultivationRegenFlux; MaintenanceCoefficient = defaults.MaintenanceCoefficient; GathererBridgeCeiling = defaults.GathererBridgeCeiling; GathererBridgeEfficiency = defaults.GathererBridgeEfficiency;
            CultivationVegetationDisplacementStart = defaults.CultivationVegetationDisplacementStart;
            // Old saves retain their crop/accessibility and whole-footprint harvest rule.
            CropMoistureReference = 0; CropMoistureExponent = 2; CropHarvestingOnly = false;
            // Absent in older saves: their generated land stocks keep their
            // original scale and their pressure threshold remains one.
            MinimumCampSpacing = defaults.MinimumCampSpacing;
            ContactExpiryTurns = defaults.ContactExpiryTurns;
            HinterlandReferencePopulation = defaults.HinterlandReferencePopulation; HinterlandRingsPerTurn = defaults.HinterlandRingsPerTurn; HinterlandMaxRadius = defaults.HinterlandMaxRadius; ExploredMemoryTurns = defaults.ExploredMemoryTurns;
            // Absent in older saves: secondary technologies use the established threshold, trade credits no practice, and the
            // settling cost of a husbandry-primary camp keeps its default.
            SecondaryTechnologyThreshold = defaults.SecondaryTechnologyThreshold; TradePracticePerValue = 0; SettledHerdGrowthPenalty = defaults.SettledHerdGrowthPenalty;
            FissionEnabled = false; FissionGainThreshold = defaults.FissionGainThreshold; FissionGainDiscourage = defaults.FissionGainDiscourage; FissionGainEncourage = defaults.FissionGainEncourage;
            FissionSearchFraction = defaults.FissionSearchFraction; FissionDaughterMargin = defaults.FissionDaughterMargin; FissionHousingRelief = defaults.FissionHousingRelief; FissionDaughterBuilderShare = defaults.FissionDaughterBuilderShare; FissionRadius = defaults.FissionRadius; FissionMinimumDaughter = defaults.FissionMinimumDaughter; FissionDaughterCooldownTurns = defaults.FissionDaughterCooldownTurns; FissionSiteSelection = defaults.FissionSiteSelection;
            ImmigrationEnabled = false; ImmigrationRadius = defaults.ImmigrationRadius; ImmigrationQualityWeight = defaults.ImmigrationQualityWeight; ImmigrationDistanceCost = defaults.ImmigrationDistanceCost;
            ImmigrationAttachmentCost = defaults.ImmigrationAttachmentCost; ImmigrationToSedentaryCost = defaults.ImmigrationToSedentaryCost; ImmigrationToNomadicCost = defaults.ImmigrationToNomadicCost;
            ImmigrationDeadband = defaults.ImmigrationDeadband; ImmigrationMaxFraction = defaults.ImmigrationMaxFraction; ImmigrationMinimumGroup = defaults.ImmigrationMinimumGroup; ImmigrationSourceFloor = defaults.ImmigrationSourceFloor;
            ImmigrationReturnCooldownTurns = defaults.ImmigrationReturnCooldownTurns; ImmigrationReturnPenalty = defaults.ImmigrationReturnPenalty;
            TradeEnabled = false; TradeRange = defaults.TradeRange; TradeRequisitionsPerCamp = defaults.TradeRequisitionsPerCamp; TradeListLength = defaults.TradeListLength; TradeMargin = defaults.TradeMargin; TradeDistanceCostPerHex = defaults.TradeDistanceCostPerHex;
            TradeFoodEnabled = defaults.TradeFoodEnabled; TradeFoodSafeOnly = defaults.TradeFoodSafeOnly; TradeExportLabourShare = defaults.TradeExportLabourShare;
            EconomicAllocation = EconomicAllocationStrategy.Legacy;
            GoodsStorageEnabled = false; GoodsBaseStoragePerResident = defaults.GoodsBaseStoragePerResident; GoodsStorageSpacePerUnit = defaults.GoodsStorageSpacePerUnit;
            // Absent in older saves: retain the exact legacy net-growth model. PhysicalMortalityV1 is selected only by a
            // newly-created configuration or an explicit, recorded migration (Docs/DemographyQoLContract-v1.1.md).
            DemographicModel = DemographicModel.LegacyQoLNetGrowth;
            QoLMortalityEnabled = false; BaseBirthRate = defaults.BaseBirthRate; QoLMortalityElasticity = defaults.QoLMortalityElasticity;
            ExposureMortalityRate = defaults.ExposureMortalityRate; ExposureClothingReduction = defaults.ExposureClothingReduction; ExposureHousingReduction = defaults.ExposureHousingReduction;
            CrowdingMortalityRateAt1000 = defaults.CrowdingMortalityRateAt1000; CrowdingExponent = defaults.CrowdingExponent;
            DiseaseMortalityRateAt1000 = defaults.DiseaseMortalityRateAt1000; DiseaseExponent = defaults.DiseaseExponent;
            DemographicLogBandWidth = defaults.DemographicLogBandWidth; DemographicRootTolerance = defaults.DemographicRootTolerance;
            DemographicServiceTolerance = defaults.DemographicServiceTolerance; DemographicCauseCorrectionTolerance = defaults.DemographicCauseCorrectionTolerance;
            DemographicMaxRootIterations = defaults.DemographicMaxRootIterations; DemographicMaxBands = defaults.DemographicMaxBands; DemographicMaxServiceIterations = defaults.DemographicMaxServiceIterations;
            // Absent in older saves: the herd-labour coupling is off, so a loaded game keeps its unconditional herder reservation and never slaughters (Docs/HerdLabour.md).
            HerdLabourEnabled = false; SharedCropHerdAllocationEnabled = false; MinimumHerdHerders = defaults.MinimumHerdHerders;
            LandBiomassMultiplier = 1;
            LandSafeUseRatio = 1;
            LandFoodRegenerationInitialized = false;
            GrowthReserveYears = double.NaN;
            MinimumStorageYears = double.NaN;
            MaximumStorageYears = double.NaN;
        }
        [System.Runtime.Serialization.OnDeserialized]
        private void MigrateFoodHorizons(System.Runtime.Serialization.StreamingContext context)
            => MigrateMissingPhysicalFoodHorizons();

        public void MigrateMissingPhysicalFoodHorizons()
        {
            // Convert legacy policy multipliers against the fixed Neolithic
            // reference generation, never the loaded world's TurnDays.
            var defaults = new SimulationConfig();
            double referenceYears = Neolithic != null && Neolithic.ReferenceTurnYears > 0
                ? Neolithic.ReferenceTurnYears : defaults.Neolithic.ReferenceTurnYears;
            if (double.IsNaN(GrowthReserveYears)) GrowthReserveYears = GrowthReserveTurns * referenceYears;
            double minimumGenerations = MinimumStorageGenerations > 0
                ? MinimumStorageGenerations : defaults.MinimumStorageGenerations;
            double maximumGenerations = MaximumStorageGenerations >= minimumGenerations
                ? MaximumStorageGenerations : defaults.MaximumStorageGenerations;
            if (double.IsNaN(MinimumStorageYears)) MinimumStorageYears = minimumGenerations * referenceYears;
            if (double.IsNaN(MaximumStorageYears)) MaximumStorageYears = maximumGenerations * referenceYears;
        }
        public int Version = 7;
        public double MajorityEligibilityYears = 200, ForcedTransitionYears = 240;
        public bool OpeningCompatibilityEffects = true;
        public double IceTemperature = -10, ColdTemperature = 3, TropicalTemperature = 21, DesertMoisture = .12, SteppeMoisture = .32;
        public double OasisVegetationMoisture = .45, VegetationWarmthRange = 12, VegetationDryFloor = .05, VegetationMoistureRange = .45;
        public double VegetationMediumPotential = .30, VegetationHighPotential = .60;
        public double MediumVegetationDown = .40, MediumVegetationUp = .55, HighVegetationDown = .65, HighVegetationUp = .75, HighPotentialLowDown = .30, HighPotentialLowUp = .40;
        public int Width = 1024, Height = 512, Players = 32, PlateCount = 18;
        public bool BenchmarkScale;
        public WorldShape Shape = WorldShape.Continents;
        public double BaseDays = 91.25, TurnDays = 7300, MinDays = 1, MaxDays = 36500;
        public double DaysPerYear = 365, PracticeDays = 7300, AnnualHumanGrowthRate = .01145, GrowthSurplusRatio = .25;
        public double HoverDelay = 2, HoverFadeIn = .2, HoverFadeOut = .15, HoverMotionPixels = 5;
        public double[] ElevationQuantiles = { .12, .25, .40, .55, .69, .81, .90, .96, .99 };
        public int HerdSmallCountThreshold = 10, HerdCountRounding = 10, HerdBroadCountSize = 100;
        public int CampMovement = 1, HunterMovement = 2, HerdMovement = 1, WaterMovement = 1;
        public int VisionRadius = 2, GatherRadius = 1, MinimumCamp = 6, StartingPopulation = 24;
        // Minimum hex distance between resolved camp destinations (WP10). Saves that predate it load with this default.
        [System.Runtime.Serialization.OptionalField] public int MinimumCampSpacing = 3;
        // Turns after which an unrefreshed settlement contact is forgotten (WP6). Saves that predate it load with this default.
        [System.Runtime.Serialization.OptionalField] public int ContactExpiryTurns = 5;
        // Hinterland knowledge and decaying memory (Docs/ContactKnowledge.md). A camp marks as explored every tile within R_h at the end of each turn:
        // R_h = VisionRadius, plus HinterlandRingsPerTurn per turn it has stayed on its tile (Camp.StationaryTurns), capped by
        // VisionRadius * sqrt(population / HinterlandReferencePopulation) (area worked proportional to people) and by HinterlandMaxRadius.
        // An explored 8x8 block not refreshed for ExploredMemoryTurns turns is forgotten. Saves that predate these fields load these defaults.
        [System.Runtime.Serialization.OptionalField] public int HinterlandReferencePopulation = 24, HinterlandRingsPerTurn = 1, HinterlandMaxRadius = 6, ExploredMemoryTurns = 5;
        // Fission (WP10a, QoLFissionTradeDesign 3.1-3.6). Saves that predate these fields load with fission disabled so they continue exactly.
        [System.Runtime.Serialization.OptionalField] public bool FissionEnabled = true;
        // Push = crowding + expected gain (WP10c): a camp proposes when the stayers' QoL would rise by at least G*(policy) (relative gain).
        // The three thresholds are the analytic calibration to the design onset populations (Docs/Fission.md); they are not monotone in the
        // policy because the policy also sets the daughter fraction and so the gain a split delivers.
        [System.Runtime.Serialization.OptionalField] public double FissionGainThreshold = .113, FissionGainDiscourage = .080, FissionGainEncourage = .0955, FissionSearchFraction = .8, FissionDaughterMargin = .05, FissionHousingRelief = 0;
        [System.Runtime.Serialization.OptionalField] public int FissionRadius = 4, FissionMinimumDaughter = 12;
        // Additional complete turns after founding during which a physical-demography daughter cannot fission.
        // A value of one means the daughter is first eligible at the close of her second economic/demographic turn.
        [System.Runtime.Serialization.OptionalField] public int FissionDaughterCooldownTurns = 1;
        // Experimental cache-miss site policy. The shipped control keeps the
        // historical ecological ordering; alternatives compare only candidates
        // that pass the unchanged shared relocation evaluator.
        [System.Runtime.Serialization.OptionalField] public FissionSiteSelection FissionSiteSelection = FissionSiteSelection.EcologicalSupport;
        // Share of a daughter's people the estimate counts as Builders for first-turn shelter (Fission.DaughterShelter); 0 credits carried shelter only. Absent in older saves: default.
        [System.Runtime.Serialization.OptionalField] public double FissionDaughterBuilderShare = .05;
        // Immigration between known settlements (WP10b, QoLFissionTradeDesign 4). Saves that predate these fields load with immigration disabled.
        // dU = w_Q (Q_j - Q_i) - distance*d - attachment - lifestyle; a source emigrates to its best known camp within the radius when dU exceeds the deadband.
        // WP10b-2: forward-looking utility and return hysteresis. A flow back to a camp that sent people within the cooldown pays a penalty decaying linearly to zero.
        [System.Runtime.Serialization.OptionalField] public int ImmigrationReturnCooldownTurns = 3;
        [System.Runtime.Serialization.OptionalField] public double ImmigrationReturnPenalty = .10;
        [System.Runtime.Serialization.OptionalField] public bool ImmigrationEnabled = true;
        [System.Runtime.Serialization.OptionalField] public int ImmigrationRadius = 4, ImmigrationMinimumGroup = 4, ImmigrationSourceFloor = 12;
        [System.Runtime.Serialization.OptionalField] public double ImmigrationQualityWeight = 1, ImmigrationDistanceCost = .01, ImmigrationAttachmentCost = .02, ImmigrationToSedentaryCost = .05, ImmigrationToNomadicCost = .15, ImmigrationDeadband = .075, ImmigrationMaxFraction = .10;
        // Trade between known settlements (WP7-WP8, Docs/Trade.md): AI-to-AI barter of Cloth, Animal Fabric, Housing Material and food,
        // agreed in the planning pass and settled from committed stock; with food (WP9) unless TradeFoodEnabled is off. Saves that predate these fields load with trade disabled.
        [System.Runtime.Serialization.OptionalField] public bool TradeEnabled = true;
        [System.Runtime.Serialization.OptionalField] public int TradeRange = 6, TradeRequisitionsPerCamp = 3, TradeListLength = 3;
        [System.Runtime.Serialization.OptionalField] public double TradeMargin = .10, TradeDistanceCostPerHex = .01;
        // Food in the tradeable set (WP9); safe-only is the control that never exports growth food.
        [System.Runtime.Serialization.OptionalField] public bool TradeFoodEnabled = true, TradeFoodSafeOnly = false;
        // Share of a seller's resident turn that may go to goods made to order for export (0 disables making to order).
        // 0 by default (stock-only trade, decided 4 October 2026): made-to-order exports displace the seller's own tool-bound
        // goods without pricing that tool time, and cost farming lineages population (Docs/PlannerTradeStorageDesign.md §3.11).
        [System.Runtime.Serialization.OptionalField] public double TradeExportLabourShare = 0;
        // Pottery storage for finished goods (Docs/PlannerTradeStorageDesign.md §3, phase S): goods share Pots with food,
        // above a base allowance per resident; every counted good takes GoodsStorageSpacePerUnit food units of space and
        // additions beyond the room are blocked. Saves that predate these fields load with storage off.
        [System.Runtime.Serialization.OptionalField] public bool GoodsStorageEnabled = false;
        // Automatic economic allocation strategy (Docs/EconomicControlContract.md §15): "Legacy" (the Value/Uniform planner,
        // EconomicPlanner) or "DemandControl" (service controllers and Elder arbitration, DemandControl.cs). Saves without it
        // load Legacy.
        [System.Runtime.Serialization.OptionalField] public string EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        [System.Runtime.Serialization.OptionalField] public double GoodsBaseStoragePerResident = 2, GoodsStorageSpacePerUnit = 20;
        // Versioned human demography. New worlds use the v1.1 physical model. Saves without this field load the legacy
        // net-growth model in InitializeNeolithic; an explicit migration must record its provenance on WorldState.
        [System.Runtime.Serialization.OptionalField] public DemographicModel DemographicModel = DemographicModel.PhysicalMortalityV1;
        // Legacy QoL-dependent mortality (Docs/QoLMortality.md), retained only for legacy saves and controlled comparisons.
        [System.Runtime.Serialization.OptionalField] public bool QoLMortalityEnabled = false;
        [System.Runtime.Serialization.OptionalField] public double BaseBirthRate = .045, QoLMortalityElasticity = 1;
        // PhysicalMortalityV1 annual continuous hazards. These values are the selected v1.1 coefficient set; composite
        // QoL, expectation and social cohesion are deliberately absent. Clothing and housing reductions are additive.
        [System.Runtime.Serialization.OptionalField] public double ExposureMortalityRate = .03255509888111371;
        [System.Runtime.Serialization.OptionalField] public double ExposureClothingReduction = .45, ExposureHousingReduction = .45;
        [System.Runtime.Serialization.OptionalField] public double CrowdingMortalityRateAt1000 = .03130836758391647, CrowdingExponent = .00001;
        [System.Runtime.Serialization.OptionalField] public double DiseaseMortalityRateAt1000 = .010436122527972156, DiseaseExponent = .000027161883008306556;
        // Deterministic equilibrium-rooted log-population integration and finite-service closure controls.
        [System.Runtime.Serialization.OptionalField] public double DemographicLogBandWidth = .05, DemographicRootTolerance = 1e-12;
        [System.Runtime.Serialization.OptionalField] public double DemographicServiceTolerance = 1e-10, DemographicCauseCorrectionTolerance = .002;
        [System.Runtime.Serialization.OptionalField] public int DemographicMaxRootIterations = 128, DemographicMaxBands = 8192, DemographicMaxServiceIterations = 64;
        // Herd-labour coupling (Docs/HerdLabour.md): the Value planner staffs herders on the labour-price rule instead of reserving them first, and animals above the
        // camp's HerdTarget are slaughtered after breeding (carcass food = SpeciesDefinition.FoodYield per animal). MinimumHerdHerders is the seed herd the planner always keeps managed.
        // Saves that predate these fields load with HerdLabourEnabled = false.
        [System.Runtime.Serialization.OptionalField] public bool HerdLabourEnabled = true;
        // Joint Value-rule food accounting, field operation and livestock investment.
        // Opt-in; absent in legacy saves means the measured corrected control.
        [System.Runtime.Serialization.OptionalField] public bool SharedCropHerdAllocationEnabled = true;
        [System.Runtime.Serialization.OptionalField] public int MinimumHerdHerders = 1;
        public int StartSeparation = 12, StarvationGraceTurns = 2;
        public double StartingFood = 48, StartingReserveGenerations = 1, Consumption = 1, Mortality = .18;
        public double GatherFraction = .22, GatherPerPerson = 1.8, PopulationScale = 24;
        // Uniform half-access calibration keeps each habitat's relative
        // human-gatherable share while leaving all biomass available to grazers.
        public double RichGatherableFraction = .125, GrasslandGatherableFraction = .05, AridGatherableFraction = .025, TundraGatherableFraction = .04;
        public double HuntEfficiency = .22, HuntVariance = .35, HuntBreedingReserveFraction = .2, SustainableHerdGrowthRetention = .25, FishPerPerson = 2.6;
        public int SustainableHuntPopulation = 16;
        public double WanderChance = .05;
        public double ExperienceThreshold = 225, UnproductivePracticeFraction = .1, CarryoverMultiplier = 1.25, CarryoverFloor = 100;
        public double PracticeBonusStart = .25, PracticeBonusFull = .5;
        // Practice points of post-breakthrough (Neolithic) practice that grant a secondary technology. Paleolithic practice counts
        // toward the first breakthrough only (TechnologyDiscovery.AddPractice). Default equals ExperienceThreshold.
        [System.Runtime.Serialization.OptionalField] public double SecondaryTechnologyThreshold = 225;
        // Practice points credited per unit of traded goods value (TechnologyDiscovery.RecordTradePractice). 0 keeps the hook inert until trade exists.
        [System.Runtime.Serialization.OptionalField] public double TradePracticePerValue = 0;
        // Fraction of a husbandry-primary camp's expected herd-growth value forfeited when field expansion would make it a village
        // (measured stationary herds support about 1/1.6 of the animals of mobile herds; see Docs/HusbandrySettling.md).
        [System.Runtime.Serialization.OptionalField] public double SettledHerdGrowthPenalty = .35;
        public double GatheringImpactReduction = .5, FishingImpactReduction = .5, HuntingFoodYieldScale = 2.5, HuntingYieldBonus = .5, HuntingPracticeMultiplier = 3.25, ActiveFishingPracticeMultiplier = 4.25, PassiveAquaticFishingPracticeShare = .5;
        public double ConditionRecoveryPerYear = .003093770185904377, SafeUseRatio = 1, SustainableProductionFraction = .5, LandUseDamagePerYear = .03, AquaticUseDamagePerYear = .03, PressureExponent = 2;
        [System.Runtime.Serialization.OptionalField] public double LandBiomassMultiplier = 63, LandSafeUseRatio = .5;
        [System.Runtime.Serialization.OptionalField] public double CultivationVegetationDisplacementStart = .50;
        [System.Runtime.Serialization.OptionalField] public double CropMoistureReference = .50, CropMoistureExponent = 2;
        [System.Runtime.Serialization.OptionalField] public bool CropHarvestingOnly = true;
        public double LowPotentialRecoveryMultiplier = .5, HighPotentialRecoveryMultiplier = 1.5;
        public double OpeningFoodMultiplier = 1.4, CultivationMoisture = .25, RelocationInertia = .18;
        [System.Runtime.Serialization.OptionalField] public double FarmerProductivity = 1.50, ReferenceIntensity = 0.40, CultivationRegenFlux = 0.65, MaintenanceCoefficient = 0.40;
        [System.Runtime.Serialization.OptionalField] public double GathererBridgeCeiling = 0.25, GathererBridgeEfficiency = 0.50;
        public int InitialDomesticAnimals = 5;
        [System.Runtime.Serialization.OptionalField] public double AnimalsPerHerder = 2, AnimalFoodPerManagedAnimal = 1, AnimalFoodNutritionExponent = 1, AnimalManagementExponent = 1;
        [System.Runtime.Serialization.OptionalField] public bool ManagedHerdDefaultsInitialized = true;
        public double HabitableZonePosition = .5, InnerTemperature = 12, OuterTemperature = -12;
        public double SeaLevel = .45, Uplift = .2, ElevationNoise = .11, Erosion = .12;
        public double SeasonalAmplitude = 13, LatitudeGradient = 48, LapseRate = 28, OceanModeration = .45;
        public double Evaporation = .2, MoistureDecay = .97, OrographicRain = 2.5, Monsoon = .12;
        public double RiverThreshold = 28, WaterRecovery = .35, FoodRegeneration = .4;
        // Older saves have neither field. Their stored seasonal rates remain
        // authoritative, and a generated world from that config uses the
        // legacy shared rate. The flag also permits an explicit zero land rate.
        [System.Runtime.Serialization.OptionalField] public double LandFoodRegeneration = .03;
        [System.Runtime.Serialization.OptionalField] public bool LandFoodRegenerationInitialized = true;
        public int Hotspots = 8, HotspotChainLength = 4;
        public double StartViability = 85; // Percent of starting food need supported by conservative recurring gathering.
        public int GenerationBudgetSeconds = 90, GenerationBudgetMegabytes = 1000;
        public double[] BandThresholds = { .03, .25, .5, .8 };
        public int SupplyRange = 3;
        public double CarryingCapacityPerHunter = 5, HunterCarryGenerations = 2.5, ExpeditionProvisionTurns = 3, CampProvisionReserveTurns = .5;
        [System.Runtime.Serialization.OptionalField] public double MinimumStorageGenerations = 1, MaximumStorageGenerations = 4;
        public double HunterForagePerPerson = 1;
        [System.Runtime.Serialization.OptionalField] public double GrowthReserveTurns = 1.5;
        // Physical food horizons. Optional fields use NaN during load so
        // SaveCodec can distinguish old saves from explicit zero values.
        [System.Runtime.Serialization.OptionalField] public double GrowthReserveYears;
        [System.Runtime.Serialization.OptionalField] public double MinimumStorageYears, MaximumStorageYears;
        public double HillsProductivity = .95, HighlandProductivity = .8, MountainProductivity = .5;
        public int HerdRegionSize = 32, MinimumOpeningHerds = 1;
        public double HerdsPerLandTile = .008, HerdRegionVariation = .35, GrasslandHerdDensityMultiplier = 1.35, SteppeHerdDensityMultiplier = 1.15;
        public bool WildHerdFissionEnabled;
        public double StartFreshwaterMinimum = 6, RainPoolRainThreshold = .035, RainPoolDrainageMinimum = 1;
        public int OasisRegionSize = 32, OasisSpacing = 7, OasisSurfaceWaterSeparation = 3, OasisMinimumRegionArea = 100;
        public double OasisDensity = .003, OasisViabilityThreshold = .18, OasisFreshwater = 70, OasisFood = 38, OasisFoodRegeneration = .3, OasisWaterRecovery = .14, OasisNeighborFalloff = .3;
        public List<SpeciesDefinition> Species = SpeciesDefinition.Defaults();
        public SpeciesDefinition GetSpecies(string id) => Species.FirstOrDefault(s => s.Id == id) ?? throw new InvalidOperationException("Unknown species: " + id);

        public void Validate()
        {
            if (double.IsNaN(CropMoistureReference) || double.IsInfinity(CropMoistureReference) || CropMoistureReference < 0 || CropMoistureReference > 1 ||
                double.IsNaN(CropMoistureExponent) || double.IsInfinity(CropMoistureExponent) || CropMoistureExponent <= 0)
                throw new ArgumentException("Invalid crop moisture response.");
            Neolithic.Validate();
            if (double.IsNaN(LandFoodRegeneration) || double.IsInfinity(LandFoodRegeneration)
                || LandFoodRegeneration < 0 || LandFoodRegeneration >= 1)
                throw new ArgumentException("Invalid land food regeneration fraction.");
            if (new[] { FarmerProductivity, ReferenceIntensity, CultivationRegenFlux, MaintenanceCoefficient, GathererBridgeCeiling, GathererBridgeEfficiency }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0) || ReferenceIntensity <= 0 || ReferenceIntensity > 1 || GathererBridgeCeiling > 1 || GathererBridgeEfficiency > 1) throw new ArgumentException("Invalid cultivation calibration.");
            if (Width < 12 || Height < 8 || Width > 2048 || Height > 1024 || Players < 1 || Players > (BenchmarkScale ? 320 : MaximumPlayersFor(Width, Height)))
                throw new ArgumentException("Unsupported map dimensions or player count.");
            if (PlateCount < 2 || PlateCount > 64 || TurnDays < MinDays || TurnDays > MaxDays || BaseDays <= 0 || StarvationGraceTurns < 0 || MajorityEligibilityYears < 0 || ForcedTransitionYears <= MajorityEligibilityYears)
                throw new ArgumentException("Invalid generation or clock configuration.");
            if (MinimumCampSpacing < 1) throw new ArgumentException("Minimum camp spacing must be at least one hex.");
            if (ContactExpiryTurns < 1) throw new ArgumentException("Contact expiry must be at least one turn.");
            // The hinterland marks at most 3 r^2 + 3 r + 1 tiles per camp per turn; r <= 8 bounds that at 217.
            if (HinterlandReferencePopulation < 1 || HinterlandRingsPerTurn < 0 || HinterlandRingsPerTurn > 8 || HinterlandMaxRadius < VisionRadius || HinterlandMaxRadius > 8 || ExploredMemoryTurns < 1) throw new ArgumentException("Invalid hinterland or explored-memory configuration.");
            if (new[] { SecondaryTechnologyThreshold, TradePracticePerValue, SettledHerdGrowthPenalty }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0) || SecondaryTechnologyThreshold <= 0 || SettledHerdGrowthPenalty > 1)
                throw new ArgumentException("Invalid secondary technology, trade practice or settling configuration.");
            if (new[] { FissionGainThreshold, FissionGainDiscourage, FissionGainEncourage, FissionSearchFraction, FissionDaughterMargin, FissionHousingRelief, FissionDaughterBuilderShare }.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || FissionGainThreshold < 0 || FissionGainThreshold > 5 || FissionGainDiscourage < 0 || FissionGainDiscourage > 5 || FissionGainEncourage < 0 || FissionGainEncourage > 5 || FissionSearchFraction < 0 || FissionSearchFraction > 1 || FissionDaughterMargin < 0 || FissionDaughterMargin > 1 || FissionHousingRelief < 0 || FissionHousingRelief > 1 || FissionDaughterBuilderShare < 0 || FissionDaughterBuilderShare > 1 || FissionRadius < 1 || FissionRadius > 5 || FissionMinimumDaughter < 1 || FissionDaughterCooldownTurns < 0 || FissionDaughterCooldownTurns > 20 || !Enum.IsDefined(typeof(FissionSiteSelection), FissionSiteSelection))
                throw new ArgumentException("Invalid fission configuration.");
            if (new[] { ImmigrationQualityWeight, ImmigrationDistanceCost, ImmigrationAttachmentCost, ImmigrationToSedentaryCost, ImmigrationToNomadicCost, ImmigrationDeadband, ImmigrationMaxFraction }.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || ImmigrationQualityWeight < 0 || ImmigrationQualityWeight > 10 || ImmigrationDistanceCost < 0 || ImmigrationDistanceCost > 5 || ImmigrationAttachmentCost < 0 || ImmigrationAttachmentCost > 5 || ImmigrationToSedentaryCost < 0 || ImmigrationToSedentaryCost > 5 || ImmigrationToNomadicCost < 0 || ImmigrationToNomadicCost > 5 || ImmigrationDeadband <= 0 || ImmigrationDeadband > 5 || ImmigrationMaxFraction < 0 || ImmigrationMaxFraction > .5 || ImmigrationRadius < 1 || ImmigrationRadius > 5 || ImmigrationReturnCooldownTurns < 0 || ImmigrationReturnCooldownTurns > 20 || double.IsNaN(ImmigrationReturnPenalty) || double.IsInfinity(ImmigrationReturnPenalty) || ImmigrationReturnPenalty < 0 || ImmigrationReturnPenalty > 5 || ImmigrationMinimumGroup < 1 || ImmigrationSourceFloor < 1
                || TradeRange < 1 || TradeRange > 20 || TradeRequisitionsPerCamp < 1 || TradeRequisitionsPerCamp > 10 || TradeListLength < 1 || TradeListLength > 10 || double.IsNaN(TradeMargin) || double.IsInfinity(TradeMargin) || TradeMargin < 0 || TradeMargin > 5 || double.IsNaN(TradeDistanceCostPerHex) || double.IsInfinity(TradeDistanceCostPerHex) || TradeDistanceCostPerHex < 0 || TradeDistanceCostPerHex > 1 || double.IsNaN(TradeExportLabourShare) || TradeExportLabourShare < 0 || TradeExportLabourShare > 1
                || double.IsNaN(GoodsBaseStoragePerResident) || double.IsInfinity(GoodsBaseStoragePerResident) || GoodsBaseStoragePerResident < 0
                || double.IsNaN(GoodsStorageSpacePerUnit) || double.IsInfinity(GoodsStorageSpacePerUnit) || GoodsStorageSpacePerUnit <= 0
                || !EconomicAllocationStrategy.IsKnown(EconomicAllocation))
                throw new ArgumentException("Invalid immigration or trade configuration.");
            if (!Enum.IsDefined(typeof(DemographicModel), DemographicModel))
                throw new ArgumentException("Unknown demographic model.");
            if (double.IsNaN(BaseBirthRate) || double.IsInfinity(BaseBirthRate) || double.IsNaN(QoLMortalityElasticity) || double.IsInfinity(QoLMortalityElasticity) || BaseBirthRate <= 0 || BaseBirthRate > .2 || QoLMortalityElasticity <= 0 || QoLMortalityElasticity > 20 || DemographicModel == DemographicModel.LegacyQoLNetGrowth && QoLMortalityEnabled && BaseBirthRate <= AnnualHumanGrowthRate)
                throw new ArgumentException("Invalid QoL mortality configuration: BaseBirthRate must exceed AnnualHumanGrowthRate and the elasticity must be positive.");
            var demographicValues = new[] { ExposureMortalityRate, ExposureClothingReduction, ExposureHousingReduction, CrowdingMortalityRateAt1000, CrowdingExponent, DiseaseMortalityRateAt1000, DiseaseExponent, DemographicLogBandWidth, DemographicRootTolerance, DemographicServiceTolerance, DemographicCauseCorrectionTolerance };
            if (demographicValues.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v <= 0) || ExposureClothingReduction + ExposureHousingReduction > 1 || DemographicModel == DemographicModel.PhysicalMortalityV1 && BaseBirthRate != .045 || DemographicLogBandWidth > .25 || DemographicCauseCorrectionTolerance > .01 || DemographicMaxRootIterations < 16 || DemographicMaxRootIterations > 512 || DemographicMaxBands < 32 || DemographicMaxBands > 100000 || DemographicMaxServiceIterations < 8 || DemographicMaxServiceIterations > 256)
                throw new ArgumentException("Invalid physical demographic calibration or numerical controls.");
            if (MinimumHerdHerders < 0 || MinimumHerdHerders > 1000)
                throw new ArgumentException("Invalid herd-labour configuration: MinimumHerdHerders must be between 0 and 1000.");
            if (CampMovement != 1 || HunterMovement != 2 || HerdMovement != 1 || VisionRadius != 2 || GatherRadius != 1)
                throw new ArgumentException("Paleolithic movement and vision must be 1/2/1 and radius 2/1.");
            if (StartingPopulation <= MinimumCamp || MinimumCamp < 1 || Consumption <= 0 || PopulationScale <= 0)
                throw new ArgumentException("Invalid population configuration.");
            if (SupplyRange < 0 || CarryingCapacityPerHunter <= 0 || HunterCarryGenerations <= 0 || StartingReserveGenerations <= 0 || MinimumStorageGenerations <= 0 || MaximumStorageGenerations < MinimumStorageGenerations || HunterForagePerPerson < 0 || HuntBreedingReserveFraction <= 0 || HuntBreedingReserveFraction >= 1 || SustainableHuntPopulation < 1 || SustainableHerdGrowthRetention <= 0 || SustainableHerdGrowthRetention > 1 || UnproductivePracticeFraction < 0 || UnproductivePracticeFraction > 1 || PracticeBonusStart < 0 || PracticeBonusFull <= PracticeBonusStart || PracticeBonusFull > 1 || GatheringImpactReduction < 0 || GatheringImpactReduction > 1 || FishingImpactReduction < 0 || FishingImpactReduction > 1 || HuntingFoodYieldScale <= 0 || HuntingYieldBonus < 0 || HuntingPracticeMultiplier < 1 || ActiveFishingPracticeMultiplier < 1 || PassiveAquaticFishingPracticeShare < 0 || PassiveAquaticFishingPracticeShare > 1 || ConditionRecoveryPerYear < 0 || SafeUseRatio < 0 || LandBiomassMultiplier <= 0 || LandSafeUseRatio < 0 || SustainableProductionFraction <= 0 || SustainableProductionFraction > 1 || LowPotentialRecoveryMultiplier <= 0 || HighPotentialRecoveryMultiplier <= 0 || LandUseDamagePerYear < 0 || AquaticUseDamagePerYear < 0 || PressureExponent < 1 || RichGatherableFraction <= 0 || RichGatherableFraction > 1 || GrasslandGatherableFraction <= 0 || GrasslandGatherableFraction > RichGatherableFraction || AridGatherableFraction <= 0 || AridGatherableFraction > GrasslandGatherableFraction || TundraGatherableFraction <= 0 || TundraGatherableFraction > RichGatherableFraction || AnnualHumanGrowthRate < 0 || GrowthReserveYears < 0 || HerdRegionSize < 4 || MinimumOpeningHerds < 1 || HerdsPerLandTile < 0 || GrasslandHerdDensityMultiplier <= 0 || SteppeHerdDensityMultiplier <= 0 || OasisSpacing < 1 || OasisRegionSize < 4 || DaysPerYear <= 0 || PracticeDays <= 0 || GrowthSurplusRatio <= 0 || HoverDelay < 0 || HoverFadeIn <= 0 || HoverFadeOut <= 0)
                throw new ArgumentException("Invalid logistics, demographic or ecological configuration.");
            if (new[] { GrowthReserveYears, MinimumStorageYears, MaximumStorageYears }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0) || MaximumStorageYears < MinimumStorageYears)
                throw new ArgumentException("Invalid physical food reserve or storage horizons.");
            if (new[] { BaseDays, TurnDays, DaysPerYear, MajorityEligibilityYears, ForcedTransitionYears, ConditionRecoveryPerYear, SafeUseRatio, LandBiomassMultiplier, LandSafeUseRatio, SustainableProductionFraction, LandUseDamagePerYear, AquaticUseDamagePerYear, PressureExponent, CultivationVegetationDisplacementStart, LowPotentialRecoveryMultiplier, HighPotentialRecoveryMultiplier, IceTemperature, ColdTemperature, TropicalTemperature, DesertMoisture, SteppeMoisture, OasisVegetationMoisture, VegetationWarmthRange, VegetationDryFloor, VegetationMoistureRange, VegetationMediumPotential, VegetationHighPotential, MediumVegetationDown, MediumVegetationUp, HighVegetationDown, HighVegetationUp, HighPotentialLowDown, HighPotentialLowUp, FoodRegeneration, WaterRecovery, OasisFoodRegeneration, OasisWaterRecovery }.Any(x => double.IsNaN(x) || double.IsInfinity(x)) || CultivationVegetationDisplacementStart < 0 || CultivationVegetationDisplacementStart >= 1 ||
                IceTemperature >= ColdTemperature || ColdTemperature >= TropicalTemperature || DesertMoisture < 0 || DesertMoisture >= SteppeMoisture || SteppeMoisture > 1 || VegetationWarmthRange <= 0 || VegetationMoistureRange <= 0 || VegetationMediumPotential < 0 || VegetationMediumPotential >= VegetationHighPotential || VegetationHighPotential > 1 || MediumVegetationDown >= MediumVegetationUp || HighPotentialLowDown >= HighPotentialLowUp || HighPotentialLowUp >= HighVegetationDown || HighVegetationDown >= HighVegetationUp || HighVegetationUp > 1)
                throw new ArgumentException("Invalid environmental rates, climate thresholds or vegetation hysteresis.");
            if (MediumVegetationDown < 0 || MediumVegetationUp > 1 || HighPotentialLowDown < 0 || OasisVegetationMoisture < 0 || OasisVegetationMoisture > 1 || VegetationDryFloor < 0 || VegetationDryFloor >= 1 || new[] { FoodRegeneration, WaterRecovery, OasisFoodRegeneration, OasisWaterRecovery }.Any(f => f < 0 || f >= 1))
                throw new ArgumentException("Environmental fractions and hysteresis must lie within their physical ranges.");
            if (ElevationQuantiles == null || ElevationQuantiles.Length != 9 || ElevationQuantiles[0] <= 0 || ElevationQuantiles[8] >= 1 || !ElevationQuantiles.SequenceEqual(ElevationQuantiles.OrderBy(x => x).Distinct())) throw new ArgumentException("Elevation requires nine increasing quantile boundaries.");
            if (HerdSmallCountThreshold < 1 || HerdCountRounding < 1 || HerdBroadCountSize < HerdSmallCountThreshold) throw new ArgumentException("Invalid observable herd count thresholds.");
            if (new[] { AnimalsPerHerder, AnimalFoodPerManagedAnimal, AnimalFoodNutritionExponent, AnimalManagementExponent }.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || AnimalsPerHerder <= 0 || AnimalFoodPerManagedAnimal < 0 || AnimalFoodNutritionExponent <= 0 || AnimalManagementExponent <= 0 || Species == null || Species.Count == 0 || Species.Select(s => s.Id).Distinct().Count() != Species.Count || Species.Any(s => string.IsNullOrWhiteSpace(s.Id) || s.FoodConsumption < 0 || s.FreshwaterConsumption <= 0 || s.GrowthRate < 0 || s.FoodYield <= 0 || s.MaximumHerdSize < s.MaximumAnimals))
                throw new ArgumentException("Species definitions must have unique stable IDs and valid ecological rates.");
        }
        public static int MaximumPlayersFor(int width, int height)
        {
            long tiles = (long)width * height;
            return tiles <= 48L * 24 ? 8 : tiles <= 96L * 48 ? 16 : 80;
        }
        public double GenerationIntervals => TurnDays / BaseDays;
        // Daily food intake is the reference interval's consumption spread
        // evenly across its configured physical duration.
        public double DailyConsumption => Consumption / BaseDays;
        public double StartingReserveFood => StartingPopulation * Consumption * GenerationIntervals * StartingReserveGenerations;
        public static SimulationConfig Small(int players = 4) => new SimulationConfig { Width = 96, Height = 48, Players = players, StartSeparation = 9 };
    }

    [Serializable]
    public struct SeasonData
    {
        public float Temperature, Rain, Snow, WindX, Runoff;
        // Regional excludes local water subsidies; Moisture is the soil water used by productivity.
        public float RegionalMoisture, Moisture, AccessibleFreshwater;
        public float MaxFood, MaxPotentialFood, MaxFreshwater, MaxSaltwater, FoodRecoveryPerDay, BiomassRecoveryPerDay, WaterRecoveryPerDay, Habitat;
    }
    [Serializable]
    public sealed class Tile
    {
        public int Id, Plate, Downstream = -1;
        public EnvironmentKind Environment;
        public VegetationTier NaturalVegetationPotential, CurrentVegetation;
        public double LastEcologyUpdateDays;
        // Legacy view for old callers/assets. Environment + vegetation are authoritative.
        public Terrain Terrain
        {
            get => Environment == EnvironmentKind.Ocean ? Terrain.Ocean : Environment == EnvironmentKind.Shallows ? Terrain.Shallows : Environment == EnvironmentKind.Lake ? Terrain.Lake :
                Environment == EnvironmentKind.Desert ? Terrain.Desert : Environment == EnvironmentKind.Cold ? Terrain.Tundra : Environment == EnvironmentKind.Ice ? Terrain.Ice :
                Environment == EnvironmentKind.Tropical && CurrentVegetation == VegetationTier.High ? Terrain.Jungle :
                (Environment == EnvironmentKind.Temperate || Environment == EnvironmentKind.Tropical) && CurrentVegetation >= VegetationTier.Medium ? Terrain.Woodland : Terrain.Grassland;
            set
            {
                Environment = value == Terrain.Ocean ? EnvironmentKind.Ocean : value == Terrain.Shallows ? EnvironmentKind.Shallows : value == Terrain.Lake ? EnvironmentKind.Lake :
                    value == Terrain.Desert ? EnvironmentKind.Desert : value == Terrain.Tundra ? EnvironmentKind.Cold : value == Terrain.Ice ? EnvironmentKind.Ice :
                    value == Terrain.Woodland ? EnvironmentKind.Temperate : value == Terrain.Jungle ? EnvironmentKind.Tropical : EnvironmentKind.Grassland;
                if (value == Terrain.Woodland || value == Terrain.Jungle) NaturalVegetationPotential = CurrentVegetation = value == Terrain.Jungle ? VegetationTier.High : VegetationTier.Medium;
            }
        }
        public Relief Relief;
        public int ElevationLevel;
        public int RidgeMask;
        public WaterKind WaterBody;
        public FreshwaterSource FreshwaterSource;
        public bool Continental, Cultivated, Fishery, Oasis, Irrigated;
        public float Elevation, Volcanism, Flow, DepressionDepth, OasisInfluence, Cultivation;
        public double CultivationCapital;
        public int CultivationOperator = -1;
        [NonSerialized] public double CultivationOperatingFraction;
        public double Food, PotentialFood, Freshwater, Saltwater;
        public double LandCondition = 1, AquaticCondition = 1;
        public int[] RiverEdges;
        public bool River => RiverEdges != null && RiverEdges.Any(e => e >= 0);
        public double DrinkableWater => WaterBody != WaterKind.Saltwater && FreshwaterSource != FreshwaterSource.None ? Freshwater : 0;
        public SeasonData[] Seasons = new SeasonData[4];
        public bool Aquatic => Terrain == Terrain.Shallows || Terrain == Terrain.Lake;
        public bool Land => Terrain != Terrain.Ocean && !Aquatic;
        public bool TraversableLand => Land && ElevationLevel < 10;
    }
    [Serializable]
    public sealed class CampInventory
    {
        public double HandTools;
        public double Tools;
        [System.Runtime.Serialization.OptionalField] public double CopperTools;
        public double Pots;
        public double Cloth;
        public double FarmTools;
        public double ImprovedCeramics;
        // HousingUnits is retained only for old-save migration. Fixed housing is
        // authoritative in WorldState.FixedHousing; portable shelter and pastoral
        // goods remain camp-carried inventory.
        public double HousingUnits;
        [System.Runtime.Serialization.OptionalField] public double AnimalFabric, LightHousingUnits, HerdingTools;
        // Portable Housing Material (Wood/Clay made into a stored intermediate); absent in older saves means zero.
        [System.Runtime.Serialization.OptionalField] public double HousingMaterial;
        public double Houses;
        public double Stone;
        public double Clay;
        public double Wood;
        internal CampInventory Copy() => (CampInventory)MemberwiseClone();
    }
    [Serializable]
    public sealed class CraftingPriorities
    {
        [System.Runtime.Serialization.OnDeserializing]
        private void InitializeNewRecipeDefaults(System.Runtime.Serialization.StreamingContext context)
        {
            var defaults = new CraftingPriorities();
            Tools = defaults.Tools; AnimalFabric = defaults.AnimalFabric; LightHousing = defaults.LightHousing; HerdingTools = defaults.HerdingTools;
        }
        public double HandTools = 0.15;
        public double Pots = 0.10;
        public double Cloth = 0.45;
        public double FarmTools = 0.10;
        public double Housing = 0.20;
        [System.Runtime.Serialization.OptionalField] public double Tools = .15;
        [System.Runtime.Serialization.OptionalField] public double AnimalFabric = .45, LightHousing = .20, HerdingTools = .10;
    }
    [Serializable]
    public sealed class PopulationAssignment
    {
        public string Role;
        public int Population;
    }
    [Serializable]
    public sealed partial class PopulationLedger
    {
        public List<PopulationAssignment> Assignments = new List<PopulationAssignment>();
        public int Assigned => Assignments.Where(a => a != null && a.Population > 0).Sum(a => a.Population);
        public int Gatherers(int population) => Math.Max(0, population - Assigned);
        public void Normalize(int population)
        {
            Assignments = Assignments.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Role) && a.Population > 0)
                .GroupBy(a => a.Role, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new PopulationAssignment { Role = g.Key, Population = g.Sum(a => a.Population) }).ToList();
            int excess = Math.Max(0, Assigned - population);
            for (int i = Assignments.Count - 1; i >= 0 && excess > 0; i--)
            {
                int removed = Math.Min(excess, Assignments[i].Population); Assignments[i].Population -= removed; excess -= removed;
                if (Assignments[i].Population == 0) Assignments.RemoveAt(i);
            }
        }
    }
    [Serializable]
    public sealed class Camp
    {
        [System.Runtime.Serialization.OnDeserializing]
        private void InitializeEconomicDefaults(System.Runtime.Serialization.StreamingContext context)
        {
            // Deserializers do not run field initializers. Values present in the save,
            // including legitimate zeros, overwrite these absent-field defaults.
            CultivationFocusTile = -1; StonePriority = ClayPriority = WoodPriority = 1;
            QoL = FoodNeedsMetRatio = WaterNeedsMetRatio = ClothPerCapita = HousingPerCapita = 1;
            DemographicGrowthMultiplier = 1;
        }
        public int Id, Player, SocietyId, Tile, Population, DeficitTurns;
        public double PlantFood, AnimalFood, AquaticFood;
        public double Food
        {
            get => PlantFood + AnimalFood + AquaticFood;
            set
            {
                double total = PlantFood + AnimalFood + AquaticFood;
                if (total > 1e-9)
                {
                    double ratio = value / total;
                    PlantFood *= ratio; AnimalFood *= ratio; AquaticFood *= ratio;
                }
                else
                {
                    PlantFood = value; AnimalFood = 0; AquaticFood = 0;
                }
            }
        }
        public double DeficitSeverity, FoodDeficit, LastConsumed, DemographicGrowthMultiplier = 1;
        [System.Runtime.Serialization.OptionalField] public CampInventory Inventory = new CampInventory();
        [System.Runtime.Serialization.OptionalField] public CraftingPriorities CraftingPriorities = new CraftingPriorities();
        [System.Runtime.Serialization.OptionalField] public int CultivationFocusTile = -1;
        [System.Runtime.Serialization.OptionalField] public double QoL = 1.0, FoodNeedsMetRatio = 1.0, ClothPerCapita = 1.0, HousingPerCapita = 1.0;
        [System.Runtime.Serialization.OptionalField] public double StationaryYears, RudimentaryExpectation, WaterNeedsMetRatio = 1;
        [System.Runtime.Serialization.OptionalField] public double FabricConsumed, HousingDemand, HousingReplacementDemand, HousingGrowthDemand;
        // Turn-local provenance of FabricConsumed. Physical demography may reconcile the finite textile envelope after
        // needs resolve; these values are never authoritative save state.
        [NonSerialized] internal double ResolvedClothConsumed, ResolvedAnimalFabricConsumed;
        // Turn-local continuous cohort mass used only when a connected expedition reintegrates after the demographic
        // solve. It keeps hunger-history merging independent of which integer entity received a rounded birth.
        [NonSerialized] internal double DemographicDeficitMergeWeight;
        [System.Runtime.Serialization.OptionalField] public double StonePriority = 1, ClayPriority = 1, WoodPriority = 1;
        [System.Runtime.Serialization.OptionalField] public double PotsTarget, FarmToolsTarget;
        // 0 = manual, -1 = continuous, positive = remaining automated resolutions.
        [System.Runtime.Serialization.OptionalField] public int EconomyAutomationTurns;
        [System.Runtime.Serialization.OptionalField] public EconomicLaborPlan EconomicLabor;
        [System.Runtime.Serialization.OptionalField] public double ManagedBreedingIntensity = .55;
        [System.Runtime.Serialization.OptionalField] public bool ManagedBreedingPolicyInitialized = true;
        // Chosen herd size (Docs/HerdLabour.md): after breeding, managed animals above HerdTarget are slaughtered. Absent (HerdTargetSet false) means no slaughter.
        [System.Runtime.Serialization.OptionalField] public bool HerdTargetSet;
        [System.Runtime.Serialization.OptionalField] public int HerdTarget;
        [System.Runtime.Serialization.OptionalField] public bool IsVillage;
        [System.Runtime.Serialization.OptionalField] public int VillageTurns;
        // Fission (WP10a). Absent policy is Neutral (the enum's zero); an absent destination is none.
        // Existing camps and older saves have eligibility turn zero. Physical daughters receive an absolute boundary at founding.
        [System.Runtime.Serialization.OptionalField] public FissionPolicy FissionPolicy;
        [System.Runtime.Serialization.OptionalField] public FissionTarget FissionDestination;
        [System.Runtime.Serialization.OptionalField] public int FissionEligibleTurn;
        // Immigration hysteresis (WP10b-2): sparse, pruned at commit; null when empty and absent in older saves.
        [System.Runtime.Serialization.OptionalField] public List<MigrationMemory> RecentMigration;
        [NonSerialized] private Society people = new Society();
        public Society People => people ?? (people = new Society());
        internal void Bind(Society owner) { people = owner; SocietyId = owner.Id; Player = owner.Player; }
        // A planning view sharing every field but a private inventory copy (trade
        // deltas, Trade.ApplyToPlanningInventory). Read-only like the camp itself.
        internal Camp WithPrivateInventory() { var view = (Camp)MemberwiseClone(); view.Inventory = Inventory.Copy(); view.TradeExports = (double[])TradeExports?.Clone(); view.TradeImports = (double[])TradeImports?.Clone(); view.TradeOwed = null; view.GoodsStorage = null; return view; }
        // Docs/Trade.md: the made-to-order part of goods this camp has agreed to deliver this turn after its
        // production, by CraftGood (the stock part has already left its stock, into escrow). Set on planning views
        // and, for the duration of production only, on the resolving camp. Demand adds it; never saved.
        [NonSerialized] internal double[] TradeExports;
        internal double TradeExport(CraftGood good) => TradeExports == null ? 0 : TradeExports[(int)good];
        // While the camp produces: the part of TradeExports not yet made. Made exports are held back from
        // inputs and tool coverage (TradeExport - TradeOutstanding).
        [NonSerialized] internal double[] TradeOwed;
        internal double TradeOutstanding(CraftGood good) => TradeOwed == null ? TradeExport(good) : TradeOwed[(int)good];
        internal double TradeHeldForExport(CraftGood good) => Math.Max(0, TradeExport(good) - TradeOutstanding(good));
        // Goods this camp will receive after production and already counts as stock for its needs: never a recipe input this turn.
        [NonSerialized] internal double[] TradeImports;
        // While the turn resolves with goods storage on: this camp's storage terms (Docs/PlannerTradeStorageDesign.md §3), set
        // before the opening trade settlement and cleared at end-of-turn storage. Transient, never saved.
        [NonSerialized] internal GoodsStorage GoodsStorage;
        internal double TradeImport(CraftGood good) => TradeImports == null ? 0 : TradeImports[(int)good];
        // Compatibility accessors for one-camp callers; society owns and serializes the data.
        public double[] Experience { get => People.Experience; set => People.Experience = value; }
        public List<SpeciesPractice> SpeciesExperience { get => People.SpeciesExperience; set => People.SpeciesExperience = value; }
        // In PhysicalMortalityV1, GrowthRemainder and DeathRemainder are respectively the fractional gross-birth and
        // gross-death ledgers. Their difference is the fractional resident endpoint. Legacy saves retain their old use.
        public double GrowthRemainder, DeathRemainder, StepFoodIncome;
        [System.Runtime.Serialization.OptionalField] public double FoodDeficitYears;
        // Last physically reconciled demographic rates, persisted so a save/replay continuation and the owner-only
        // observation expose the same measured outcome without recomputing hidden state.
        [System.Runtime.Serialization.OptionalField] public double EffectiveFertilityRate, EffectiveMortalityRate;
        [System.Runtime.Serialization.OptionalField] public double ExposureMortalityRate, CrowdingMortalityRate, DiseaseMortalityRate, StarvationMortalityRate;
        public Technology Technology { get => People.Technology; set => People.Technology = value; }
        public int TransitionTurn { get => People.TransitionTurn; set => People.TransitionTurn = value; }
        public int SecondaryDomain { get => People.SecondaryDomain; set => People.SecondaryDomain = value; }
        public int Moves, StationaryTurns;
        public bool ForcedTransition { get => People.ForcedTransition; set => People.ForcedTransition = value; }
        public bool[] Technologies { get => People.Technologies; set => People.Technologies = value; }
        public Mandate Mandate { get => People.Mandate; set => People.Mandate = value; }
        [System.Runtime.Serialization.OptionalField] public PopulationLedger Workforce = new PopulationLedger();
        public int GathererPopulation => (Workforce ?? (Workforce = new PopulationLedger())).Gatherers(Population);
    }
    [Serializable]
    public sealed class Hunter
    {
        public int Id, Player, SocietyId, SourceCamp, Tile, Population;
        public double CarriedFood, LastConsumed, FoodDeficit, DeficitSeverity, DeathRemainder;
        [System.Runtime.Serialization.OptionalField] public double GrowthRemainder, FoodDeficitYears;
        [NonSerialized] internal double DemographicDeficitMergeWeight;
        public int DeficitTurns;
    }
    [Serializable]
    public sealed class Herd
    {
        public int Id, Tile, Animals, Capacity = 40, DomesticCamp = -1, OwnerSociety = -1;
        public bool OpeningCompatibility;
        public string Species = "antelope";
        public double GrowthRemainder, UnmetFood, UnmetFreshwater;
        [System.Runtime.Serialization.OptionalField] public int DeficitTurns;
        [System.Runtime.Serialization.OptionalField] public double DeficitSeverity, DeathRemainder, LastNutrition = 1, LastFoodNutrition = 1, LastWaterNutrition = 1, ManagementFulfillment = 1, AnimalFoodPotential, AnimalFoodProduced;
        [System.Runtime.Serialization.OptionalField] public int StartingAnimals, SurvivingAnimals, NewbornAnimals, StarvationDeaths;
        [System.Runtime.Serialization.OptionalField] public bool NutritionInitialized = true;
    }
    [Serializable]
    public sealed class ManagedHerdReport
    {
        public int Herd, Camp, StartingAnimals, SurvivingAnimals, NewbornAnimals, StarvationDeaths;
        public string Species;
        public double FoodNutrition, WaterNutrition, Nutrition, ManagementFulfillment, PotentialFood, AcceptedFood;
        // Controlled slaughter (Docs/HerdLabour.md): animals removed after breeding to reach the camp's HerdTarget and the carcass food credited to its Animal Food store.
        [System.Runtime.Serialization.OptionalField] public int Slaughtered;
        [System.Runtime.Serialization.OptionalField] public double SlaughterFood;
        [System.Runtime.Serialization.OptionalField] public double CoveredHerderYears, HerdingToolsConsumed;
    }
    [Serializable]
    public sealed class Mandate
    {
        public double Risk = .5, Mobility = .5, Exploration = .5, Reserve = .5, Hunting = 1, Gathering = 1, Fishing = 1, Persistence = .1;
    }
    [Serializable]
    public sealed class Order
    {
        [System.Runtime.Serialization.OptionalField] public EconomicIntent Economy;
        // Interturn AI proposal only. It is not an action payload or save field;
        // the prepared turn turns this request into one validated economic
        // intent after movement conflicts have been resolved.
        [NonSerialized] internal bool RequestAutomaticEconomy;
        public OrderKind Kind;
        public int Entity, Target = -1, Destination = -1, Population;
        public Order() { }
        public Order(OrderKind kind, int entity, int target = -1, int destination = -1, int population = 0)
        { Kind = kind; Entity = entity; Target = target; Destination = destination; Population = population; }
        public Order Copy() => new Order(Kind, Entity, Target, Destination, Population)
            { Economy = Economy?.Copy(), RequestAutomaticEconomy = RequestAutomaticEconomy };
        public override string ToString() => Kind + ":" + Entity + ":" + Target + ":" + Destination + ":" + Population + (Economy == null ? "" : ":" + Economy.Stamp);
    }
    [Serializable]
    public sealed class Transfer
    {
        public string Kind;
        public int Tile, Entity;
        public double Food, PotentialFood, Freshwater;
    }
    [Serializable]
    public sealed class TileChange
    {
        public int Tile;
        public double BeforeFood, BeforePotentialFood, BeforeFreshwater, BeforeLandCondition, BeforeAquaticCondition;
        public double AfterFood, AfterPotentialFood, AfterFreshwater, AfterLandCondition, AfterAquaticCondition;
    }
    [Serializable]
    public sealed class TurnRecord
    {
        public int Turn, Seed, Deaths, Growth;
        public double StartDays, Duration;
        public List<string> Events = new List<string>();
        public List<string> Orders = new List<string>();
        public List<Transfer> Transfers = new List<Transfer>();
        [System.Runtime.Serialization.OptionalField] public List<ProductionReceipt> Production = new List<ProductionReceipt>();
        [System.Runtime.Serialization.OptionalField] public List<MaterialReceipt> MaterialTransfers = new List<MaterialReceipt>();
        [NonSerialized] public List<PressureEntry> Pressure = new List<PressureEntry>();
        [NonSerialized] public List<EcologyBalance> EcologyBalances = new List<EcologyBalance>();
        public List<TileChange> Changes = new List<TileChange>();
        public List<DecisionTrace> Decisions = new List<DecisionTrace>();
        public List<LogisticsRecord> Logistics = new List<LogisticsRecord>();
        public List<PopulationChange> PopulationChanges = new List<PopulationChange>();
        public List<DemographicReport> Demographics = new List<DemographicReport>();
        // Canonical (parent camp ID order) record of the settlements founded by fission this turn. Null in saves that predate it.
        [System.Runtime.Serialization.OptionalField] public List<FissionReport> Fissions = new List<FissionReport>();
        // Canonical (destination camp, then source camp ID order) record of the immigration flows committed this turn (WP10b). Null in saves that predate it.
        [System.Runtime.Serialization.OptionalField] public List<MigrationReport> Migrations = new List<MigrationReport>();
        // Canonical (agreement order) record of the trades settled this turn and the handshake counts (WP7). Null in saves that predate it;
        // TradeActivity stays null in turns without trade.
        [System.Runtime.Serialization.OptionalField] public List<TradeRecord> Trades = new List<TradeRecord>();
        [System.Runtime.Serialization.OptionalField] public TradeSummary TradeActivity;
        [System.Runtime.Serialization.OptionalField] public List<ManagedHerdReport> ManagedHerds = new List<ManagedHerdReport>();
        // Diagnostic only: the authorized reward is unchanged. These records separate local welfare from lineage-wide
        // population-weighted welfare and make migration/fission transfer effects explicit.
        [System.Runtime.Serialization.OptionalField] public List<SettlementWelfareReport> SettlementWelfare = new List<SettlementWelfareReport>();
        [System.Runtime.Serialization.OptionalField] public List<LineageWelfareReport> LineageWelfare = new List<LineageWelfareReport>();
        public void Log(string message) => Events.Add(message);
    }
    [Serializable]
    public sealed class FixedHousingStock
    {
        public int CampId, Tile;
        public double Units, LastUpdatedDays;
    }
    [Serializable]
    public sealed class DemographicMigrationRecord
    {
        public int Turn;
        public double Days;
        public DemographicModel From, To;
        public string Reason, SourceCommit;
        public int Camps, Hunters, Population;
    }
    [Serializable]
    public sealed class WorldState
    {
        public const int GeneratorVersion = 14;
        public int Version = 7, Seed, Turn = 1, NextId = 1;
        // Live player-slot count (WP10a): fission creates AI slots mid-game. 0 means not recorded (older saves and hand-built worlds);
        // LivePlayers then falls back to Config.Players and the highest camp player.
        [System.Runtime.Serialization.OptionalField] public int PlayerCount;
        public double Days;
        public SimulationConfig Config;
        public Tile[] Tiles;
        public List<Camp> Camps = new List<Camp>();
        public List<Society> Societies = new List<Society>();
        public List<Improvement> Improvements = new List<Improvement>();
        public List<Hunter> Hunters = new List<Hunter>();
        public List<Herd> Herds = new List<Herd>();
        public List<RiverEdge> Rivers = new List<RiverEdge>();
        public List<TurnRecord> History = new List<TurnRecord>();
        public GenerationReport Generation;
        // Null means legacy/disabled resource layer, never a surveyed barren world.
        [System.Runtime.Serialization.OptionalField] public WorldResourceState Resources;
        [System.Runtime.Serialization.OptionalField] public List<MaterialSourceState> MaterialSources = new List<MaterialSourceState>();
        [NonSerialized] internal MaterialAccessIndex MaterialIndex;
        [NonSerialized] internal FixedHousingIndex HousingIndex;
        [NonSerialized] internal TurnDiagnostics Diagnostics;
        [System.Runtime.Serialization.OptionalField] public List<MaterialKnowledge> MaterialKnowledge = new List<MaterialKnowledge>();
        [System.Runtime.Serialization.OptionalField] public List<FixedHousingStock> FixedHousing = new List<FixedHousingStock>();
        // Explicit provenance only. Loading an old save never appends this list or changes Config.DemographicModel.
        [System.Runtime.Serialization.OptionalField] public List<DemographicMigrationRecord> DemographicMigrations = new List<DemographicMigrationRecord>();
        [System.Runtime.Serialization.OnDeserializing]
        private void MarkLegacyFixedHousing(System.Runtime.Serialization.StreamingContext context) { FixedHousing = null; }
        public int LivePlayers
        {
            get { int live = Math.Max(PlayerCount, Config.Players); foreach (var c in Camps) live = Math.Max(live, c.Player + 1); return live; }
        }
        // Player IDs are never reused. The caller is the canonical fission commit.
        internal int AllocatePlayer() { int id = LivePlayers; PlayerCount = id + 1; return id; }
        public int Season => (int)(Days / Config.BaseDays) % 4;
        // HexMap is immutable; rebuild only if the configured dimensions change.
        [NonSerialized] private HexMap map;
        public HexMap Map
        {
            get
            {
                var current = map;
                if (current == null || current.Width != Config.Width || current.Height != Config.Height)
                    map = current = new HexMap(Config.Width, Config.Height);
                return current;
            }
        }
        public SeasonData Ecology(int tile) => Tiles[tile].Seasons[Season];
        // The opening ends when every founding Society has made its breakthrough. A fission daughter (ParentSociety > 0) inherits its
        // parent's progression but never delays this: fission shrinks neither the rule nor the clock, it only adds settlements.
        public bool Finished => Camps.All(c => c.People.ParentSociety > 0 || c.People.Progression.Era != Era.Paleolithic || LinkedPopulation(c) == 0);
        public int LinkedPopulation(Camp camp) => camp.Population + Hunters.Where(h => h.SourceCamp == camp.Id).Sum(h => h.Population);
        public bool CanEnter(int tile, bool hunter, Camp camp = null) => tile >= 0 && tile < Tiles.Length &&
            Tiles[tile].ElevationLevel < 10 && (Tiles[tile].Land || (hunter && camp != null && camp.Technologies[(int)Technology.Watercraft] && Tiles[tile].Terrain == Terrain.Shallows));
    }
    public static class Numbers
    {
        public static double Clamp(double n, double low, double high) => Math.Max(low, Math.Min(high, n));
        public static double Fraction(double fraction, double time) => Math.Abs(time - 1) < 1e-10 ? Clamp(fraction, 0, 1) : 1 - Math.Pow(1 - Clamp(fraction, 0, 1), time);
        // Stateless named streams: new random work cannot perturb other systems or entities.
        public static uint Hash(int seed, int stream, int entity, int turn = 0)
        {
            unchecked
            {
                uint x = (uint)seed ^ (uint)stream * 0x9E3779B9u ^ (uint)entity * 0x85EBCA6Bu ^ (uint)turn * 0xC2B2AE35u;
                x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; return x ^ (x >> 16);
            }
        }
        public static double Random(int seed, int stream, int entity, int turn = 0) => Hash(seed, stream, entity, turn) / 4294967296.0;
    }
}
