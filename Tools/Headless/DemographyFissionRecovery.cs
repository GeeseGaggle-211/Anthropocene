using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Controlled post-fission continuation. Each seed is advanced to the first native
// fission Close boundary, serialized once, and replayed with either native recipe
// rates or explicit productivity controls. Social outflow is disabled in the
// isolated branches, so later capital formation is attributable to the real
// economy rather than population transfers. No control changes shipped defaults.
internal static partial class DemographySocialAblation
{
    private sealed class RecoveryVariant
    {
        public string Name;
        public bool FurtherFission;
        public string EconomicAllocation;
        public bool TrackedDualFixedShelterRoute;
        public bool StablePopulationControl;
        public bool ConsumptionFirstDiagnostic;
        public bool ExcludeGrowthFromSurvivalDiagnostic;
        public bool NoFieldDevelopmentDiagnostic;
        public double ClothMultiplier, HousingMultiplier, FabricFoodYieldMultiplier = 1;
    }

    private sealed class RecoveryPair
    {
        public int Parent, Daughter, ParentPopulation, DaughterPopulation;
    }

    private sealed class RecoveryObservation
    {
        public string Variant, Kind;
        public int Seed, Parent, Daughter, Camp, Age, BoundaryPopulation, Population;
        public bool Exists;
        public double Food, Clothing, Housing, QoL;
        public int Births, Deaths;
        public double ExposureDeaths, CrowdingDeaths, DiseaseDeaths, StarvationDeaths, AdditionalDeaths;
        public double FoodProduced, FoodRequired, ClothProduced, HousingProduced, HousingMaterialProduced;
        public double ShelterProducedPeople;
        public double ClothMade, AnimalFabricMade, FixedHousingMade, LightHousingMade;
        public bool HasPreparedCeilings;
        public double PreparedClothCeiling, PreparedShelterCeilingPeople, PreparedHousingMaterialCeiling;
        public double PreparedClothForecast, PreparedShelterForecastPeople, PreparedHousingMaterialForecast;
        public double PreparedClothMade, PreparedAnimalFabricMade, PreparedFixedHousingMade, PreparedLightHousingMade;
        public double PreparedHandToolsCeiling, PreparedToolsCeiling, PreparedHandToolsForecast, PreparedToolsForecast;
        public double HandToolsMade, ToolsMade, OpeningHandTools, OpeningTools, OpeningHerdingTools;
        public double OpeningStone, OpeningClay, OpeningWood;
        public double WeaverIntervalsUsed, BuilderIntervalsUsed, CraftspersonIntervalsUsed, ToolmakerIntervalsUsed;
        public double HousingMaterialIntervalsUsed, FixedHousingIntervalsUsed, LightHousingIntervalsUsed;
        public double StoneExtracted, ClayExtracted, WoodExtracted;
        public double MaterialExtractionWorkerIntervals;
        public double OtherLocalStoneExtracted, OtherLocalClayExtracted, OtherLocalWoodExtracted;
        public double WoodCapacityLostToCultivation;
        public double PlannedAccessibleStone, PlannedAccessibleClay, PlannedAccessibleWood;
        public double PlannedStoneExtraction, PlannedClayExtraction, PlannedWoodExtraction;
        public double EndingAccessibleStone, EndingAccessibleClay, EndingAccessibleWood;
        public double FixedShelter, LightShelter, FabricStock, HousingMaterialStock;
        public int Weavers, Builders, Craftspersons;
        public int PlanningPopulation, SurvivalWorkers, GoodsBudget, PlannerProtectiveWorkers, RepairPasses;
        public int TrimPasses;
        public double FinalCompiledPersons;
        public int OperatingFarmers, FoodGatherers, ExpansionFarmers;
        public int PreparedWeavers, PreparedBuilders, PreparedCraftspersons, PreparedToolmakers;
        public int PreparedStonecutters, PreparedClayMiners, PreparedWoodcutters, PreparedGatherers;
        public double FeedstockGatherers, MaterialGatherers;
        public double ClothingRequested, ClothingGranted, ShelterRequestedPeople, ShelterGrantedPeople;
        public double ClothingNowRequested, ClothingNowGranted, ClothingReserveRequested, ClothingReserveGranted;
        public double ShelterReplacementRequested, ShelterReplacementGranted, ShelterDeficitRequested, ShelterDeficitGranted;
        public bool FoodLaborRationed;
        public string ClothingBinding, ShelterBinding;
        public double FieldExpansionRequested, FieldExpansionGranted, FieldCapital;
        public int FieldExpansionSteps;
        public double FieldExpansionGapPerYear, FieldExpansionGainPerYear, FieldExpansionWorkerYears, FieldExpansionMinimumScore;
        public double HerdGrowthRequested, HerdGrowthGranted;
        public double FarmToolsRequested, FarmToolsGranted, HerdingToolsRequested, HerdingToolsGranted;
        public double StorageRequested, StorageGranted;
        public bool HasPreparedAllocation, HasFoodFlow, HusbandryEnabled;
        public double ExpectedSupply, PlanMeals, PlanReserve;
        public double ExpectedForageAfterHerds, ExpectedManagedAnimalFood, ActualManagedAnimalFood;
        public double ActualManagedAnimalPotential, ManagedHerdFoodRequested, ManagedHerdFoodReceived;
        public double CompetingHerdFoodRequested, CompetingHerdFoodReceived;
        public double ManagedHerdWaterRequested, ManagedHerdWaterReceived;
        public double CompetingHerdWaterRequested, CompetingHerdWaterReceived;
        public double ManagedAnimals, ManagedFoodNutrition, ManagedWaterNutrition, ManagedFulfillment, ManagedCoveredHerderYears;
        public int PreparedHerders, CompetingHerds, VisibleCompetingHerds, DomesticCompetingHerds;
        public double ConsumptionIncomeTarget, ReserveFillTarget, MaximumGrowthFood;
        public int ConsumptionOnlyFoodGatherers, ConsumptionReserveFoodGatherers, ConsumptionGrowthFoodGatherers;
        public double PlantEnvelope, AnimalEnvelope, FoodRoomEnvelope;
        public double PlantEnvelopeClothing, AnimalEnvelopeClothing, FoodRoomEnvelopeClothing;
        public double AllocatedPlantInput, AllocatedAnimalInput, PriorityClothingPlantInput, PriorityClothingAnimalInput;
        public double NeededPlantInput, NeededAnimalInput, NeededClothingPlantInput, NeededClothingAnimalInput;
        public double FlowProjectedFood, FlowNetIncome, FlowPriorityIncome, FlowAvailableAfterReserve;
        public double FlowPlantAvailable, FlowAnimalAvailable;
        public double FlowRequestedPlantInput, FlowRequestedAnimalInput, FlowBudgetedPlantInput, FlowBudgetedAnimalInput;
        public double ActualPlantInput, ActualAnimalInput;
        public double ManufacturingPlantClaim, ManufacturingPlantReceipt;
        public double BiologicalPlantClaim, BiologicalPlantReceipt;
        public double CompetingPlantClaim, CompetingPlantReceipt;
        public int PlantClaimTiles, ContestedPlantClaimTiles;
        public List<ProductionLimitDiagnostic> RelevantProductionLimits;

        public bool Alive => Exists && Population > 0;
        public bool Healthy => Alive && Food >= .8 && Clothing >= .8 && Housing >= .8;
    }

    private sealed class RecoveryRun
    {
        public string Variant;
        public int Seed, FurtherFissions, Migrations, StartPopulation, EndPopulation, Births, Deaths;
    }

    private sealed class PreparedDemandPlan
    {
        public int Population;
        public double OpeningHandTools, OpeningTools, OpeningHerdingTools, OpeningStone, OpeningClay, OpeningWood, OpeningHousingMaterial;
        public DemandController.Allocation Allocation;
        public EconomicIntent Intent;
        public ProductionOrder ProductionOrder;
        public TurnNeeds Needs;
        public CampFoodFlowDiagnostic FoodFlow;
        public List<FoodClaimDiagnostic> FoodClaims;
    }

    private static readonly RecoveryVariant[] RecoveryVariants =
    {
        new RecoveryVariant { Name = "recursive-native", FurtherFission = true, ClothMultiplier = 1, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-native", FurtherFission = false, ClothMultiplier = 1, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-legacy-planner", FurtherFission = false,
            EconomicAllocation = EconomicAllocationStrategy.Legacy, ClothMultiplier = 1, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-fixed-shelter-route", FurtherFission = false,
            TrackedDualFixedShelterRoute = true, ClothMultiplier = 1, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-consumption-first", FurtherFission = false,
            ConsumptionFirstDiagnostic = true, ClothMultiplier = 1, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-no-growth-survival", FurtherFission = false,
            ExcludeGrowthFromSurvivalDiagnostic = true, ClothMultiplier = 1, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-consumption-first-fixed-shelter", FurtherFission = false,
            ConsumptionFirstDiagnostic = true, TrackedDualFixedShelterRoute = true,
            ClothMultiplier = 1, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-no-field-development", FurtherFission = false,
            NoFieldDevelopmentDiagnostic = true, ClothMultiplier = 1, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-consumption-first-fixed-shelter-no-field-development", FurtherFission = false,
            ConsumptionFirstDiagnostic = true, TrackedDualFixedShelterRoute = true,
            NoFieldDevelopmentDiagnostic = true, ClothMultiplier = 1, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-stable-population-fixed-shelter", FurtherFission = false,
            TrackedDualFixedShelterRoute = true, StablePopulationControl = true,
            ClothMultiplier = 1, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-cloth-1.5x", FurtherFission = false, ClothMultiplier = 1.5, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-housing-1.5x", FurtherFission = false, ClothMultiplier = 1, HousingMultiplier = 1.5 },
        new RecoveryVariant { Name = "isolated-both-1.5x", FurtherFission = false, ClothMultiplier = 1.5, HousingMultiplier = 1.5 },
        new RecoveryVariant { Name = "isolated-cloth-2x", FurtherFission = false, ClothMultiplier = 2, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-housing-2x", FurtherFission = false, ClothMultiplier = 1, HousingMultiplier = 2 },
        new RecoveryVariant { Name = "isolated-both-2x", FurtherFission = false, ClothMultiplier = 2, HousingMultiplier = 2 },
        new RecoveryVariant { Name = "isolated-cloth-4x", FurtherFission = false, ClothMultiplier = 4, HousingMultiplier = 1 },
        new RecoveryVariant { Name = "isolated-housing-4x", FurtherFission = false, ClothMultiplier = 1, HousingMultiplier = 4 },
        new RecoveryVariant { Name = "isolated-both-4x", FurtherFission = false, ClothMultiplier = 4, HousingMultiplier = 4 },
        new RecoveryVariant { Name = "isolated-fabric-food-yield-1.33x", FurtherFission = false,
            ClothMultiplier = 1, HousingMultiplier = 1, FabricFoodYieldMultiplier = 4.0 / 3.0 },
        new RecoveryVariant { Name = "isolated-fabric-food-yield-2x", FurtherFission = false,
            ClothMultiplier = 1, HousingMultiplier = 1, FabricFoodYieldMultiplier = 2 },
        new RecoveryVariant { Name = "isolated-fabric-food-yield-4x", FurtherFission = false,
            ClothMultiplier = 1, HousingMultiplier = 1, FabricFoodYieldMultiplier = 4 }
    };

    public static int RunFissionRecovery(int followTurns = 20)
    {
        bool priorCraft = ResolutionMetrics.CaptureCraftWorkDiagnostics;
        bool priorAllocations = ResolutionMetrics.CaptureDemandAllocations;
        bool priorConsumptionFirst = DemandController.ConsumptionFirstDiagnostic;
        bool priorExcludeGrowth = DemandController.ExcludeGrowthFromSurvivalDiagnostic;
        bool priorNoFieldDevelopment = DemandController.NoFieldDevelopmentDiagnostic;
        ResolutionMetrics.CaptureCraftWorkDiagnostics = true;
        ResolutionMetrics.CaptureDemandAllocations = true;
        try { return RunFissionRecoveryCaptured(followTurns); }
        finally
        {
            DemandController.ConsumptionFirstDiagnostic = priorConsumptionFirst;
            DemandController.ExcludeGrowthFromSurvivalDiagnostic = priorExcludeGrowth;
            DemandController.NoFieldDevelopmentDiagnostic = priorNoFieldDevelopment;
            ResolutionMetrics.CaptureCraftWorkDiagnostics = priorCraft;
            ResolutionMetrics.CaptureDemandAllocations = priorAllocations;
        }
    }

    private static int RunFissionRecoveryCaptured(int followTurns)
    {
        if (followTurns < 10 || followTurns > 30)
            throw new ArgumentOutOfRangeException(nameof(followTurns), "The post-fission recovery diagnostic requires 10-30 continuation decisions.");
        assertions = 0;
        var observations = new List<RecoveryObservation>();
        var runs = new List<RecoveryRun>();

        foreach (int seed in new[] { 17, 53, 991 })
        {
            byte[] opening = OpeningBoundary(seed, out _, out string openingFingerprint, out _, out _);
            WorldState founding = SaveCodec.Load(opening);
            Check(SaveCodec.Fingerprint(founding) == openingFingerprint,
                "seed " + seed + " fission-recovery loads the exact social-free opening boundary");
            founding.Config.FissionEnabled = true;
            founding.Config.ImmigrationEnabled = false;
            founding.Config.Validate();

            var resolver = new TurnResolver();
            byte[] postFission = null;
            string postFissionFingerprint = null;
            List<RecoveryPair> pairs = null;
            int foundingTurn = -1;
            for (int attempt = 0; attempt < 30 && postFission == null; attempt++)
            {
                int before = founding.Camps.Sum(founding.LinkedPopulation);
                PlanningResult plan = AiPlanning.Decide(founding);
                TurnRecord record = resolver.Resolve(founding, plan.Orders, plan.Traces);
                int after = founding.Camps.Sum(founding.LinkedPopulation);
                Check(after == before + record.Growth - record.Deaths,
                    "seed " + seed + " native path to first fission conserves population");
                Check(record.Demographics.Sum(d => d.GrossBirths) == record.Growth
                    && record.Demographics.Sum(d => d.GrossDeaths) == record.Deaths,
                    "seed " + seed + " native path to first fission closes gross demographic ledgers");
                Check(record.Migrations.Count == 0,
                    "seed " + seed + " native path to first fission has immigration disabled");
                if (record.Fissions.Count == 0) continue;

                foundingTurn = record.Turn;
                pairs = record.Fissions.OrderBy(f => f.ParentCamp).Select(f => new RecoveryPair
                {
                    Parent = f.ParentCamp,
                    Daughter = f.DaughterCamp,
                    ParentPopulation = founding.Camps.Where(c => c.Id == f.ParentCamp).Select(founding.LinkedPopulation).Single(),
                    DaughterPopulation = founding.Camps.Where(c => c.Id == f.DaughterCamp).Select(founding.LinkedPopulation).Single()
                }).ToList();
                postFission = SaveCodec.Save(founding);
                postFissionFingerprint = SaveCodec.Fingerprint(founding);
            }

            Check(postFission != null && pairs != null && pairs.Count > 0,
                "seed " + seed + " reaches a native fission within thirty decisions");
            Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_BOUNDARY seed=" + seed
                + " turn=" + foundingTurn + " pairs=" + pairs.Count
                + " camps=" + founding.Camps.Count
                + " population=" + founding.Camps.Sum(founding.LinkedPopulation)
                + " fingerprint=" + postFissionFingerprint);

            foreach (RecoveryVariant variant in RecoveryVariants)
            {
                DemandController.ConsumptionFirstDiagnostic = variant.ConsumptionFirstDiagnostic;
                DemandController.ExcludeGrowthFromSurvivalDiagnostic = variant.ExcludeGrowthFromSurvivalDiagnostic;
                DemandController.NoFieldDevelopmentDiagnostic = variant.NoFieldDevelopmentDiagnostic;
                WorldState world = SaveCodec.Load(postFission);
                Check(SaveCodec.Fingerprint(world) == postFissionFingerprint,
                    "seed " + seed + " variant " + variant.Name + " loads the exact post-fission boundary");
                double baseCloth = world.Config.Neolithic.WeaverFabricOutputPerBaseDays;
                double baseHousing = world.Config.Neolithic.BuilderHousingOutputPerBaseDays;
                double baseLightHousing = world.Config.Neolithic.LightHousingBuilderOutputPerBaseDays;
                double basePlantFoodPerFabric = world.Config.Neolithic.PlantFoodPerFabricUnit;
                world.Config.FissionEnabled = variant.FurtherFission;
                world.Config.ImmigrationEnabled = false;
                if (variant.StablePopulationControl)
                {
                    world.Config.DemographicModel = DemographicModel.LegacyQoLNetGrowth;
                    world.Config.QoLMortalityEnabled = false;
                    world.Config.AnnualHumanGrowthRate = 0;
                }
                if (!string.IsNullOrEmpty(variant.EconomicAllocation))
                    world.Config.EconomicAllocation = variant.EconomicAllocation;
                int[] trackedIds = pairs.SelectMany(p => new[] { p.Parent, p.Daughter }).Distinct().ToArray();
                var openingLightHousingPriorities = trackedIds.ToDictionary(id => id,
                    id => world.Camps.Single(c => c.Id == id).CraftingPriorities.LightHousing);
                int[] trackedDualRouteCamps = Array.Empty<int>();
                if (variant.TrackedDualFixedShelterRoute)
                {
                    trackedDualRouteCamps = trackedIds.Where(id =>
                    {
                        Camp tracked = world.Camps.Single(c => c.Id == id);
                        return CultivationEconomy.Enabled(tracked) && CultivationEconomy.HusbandryEnabled(tracked);
                    }).ToArray();
                    foreach (int campId in trackedDualRouteCamps)
                    {
                        Camp tracked = world.Camps.Single(c => c.Id == campId);
                        tracked.CraftingPriorities.LightHousing = 0;
                    }
                }
                world.Config.Neolithic.WeaverFabricOutputPerBaseDays *= variant.ClothMultiplier;
                world.Config.Neolithic.BuilderHousingOutputPerBaseDays *= variant.HousingMultiplier;
                world.Config.Neolithic.LightHousingBuilderOutputPerBaseDays *= variant.HousingMultiplier;
                world.Config.Neolithic.PlantFoodPerFabricUnit /= variant.FabricFoodYieldMultiplier;
                world.Config.Validate();
                Check(Math.Abs(world.Config.Neolithic.WeaverFabricOutputPerBaseDays
                        - baseCloth * variant.ClothMultiplier) <= 1e-12
                    && Math.Abs(world.Config.Neolithic.BuilderHousingOutputPerBaseDays
                        - baseHousing * variant.HousingMultiplier) <= 1e-12
                    && Math.Abs(world.Config.Neolithic.LightHousingBuilderOutputPerBaseDays
                        - baseLightHousing * variant.HousingMultiplier) <= 1e-12
                    && Math.Abs(world.Config.Neolithic.PlantFoodPerFabricUnit
                        - basePlantFoodPerFabric / variant.FabricFoodYieldMultiplier) <= 1e-12,
                    "seed " + seed + " variant " + variant.Name + " applies only its declared productivity controls");
                Check(string.IsNullOrEmpty(variant.EconomicAllocation)
                        || world.Config.EconomicAllocation == variant.EconomicAllocation,
                    "seed " + seed + " variant " + variant.Name + " applies only its declared allocation strategy");
                Check(!variant.TrackedDualFixedShelterRoute
                        || trackedDualRouteCamps.All(id => world.Camps.Single(c => c.Id == id).CraftingPriorities.LightHousing == 0)
                        && trackedIds.Except(trackedDualRouteCamps).All(id => Math.Abs(world.Camps.Single(c => c.Id == id)
                            .CraftingPriorities.LightHousing - openingLightHousingPriorities[id]) <= 1e-12),
                    "seed " + seed + " variant " + variant.Name
                    + " reroutes only tracked dual-capability shelter backlogs");
                if (variant.TrackedDualFixedShelterRoute)
                    Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_ROUTE_SETUP seed=" + seed
                        + " tracked=" + trackedIds.Length + " dual=" + trackedDualRouteCamps.Length);

                var run = new RecoveryRun
                {
                    Variant = variant.Name,
                    Seed = seed,
                    StartPopulation = world.Camps.Sum(world.LinkedPopulation)
                };
                foreach (RecoveryPair pair in pairs)
                {
                    observations.Add(RecoveryAt(world, null, variant.Name, seed, pair, "parent", pair.Parent, 0, pair.ParentPopulation));
                    observations.Add(RecoveryAt(world, null, variant.Name, seed, pair, "daughter", pair.Daughter, 0, pair.DaughterPopulation));
                }

                var continuationResolver = new TurnResolver();
                var expectedStartingPopulation = world.Camps.ToDictionary(c => c.Id, world.LinkedPopulation);
                for (int age = 1; age <= followTurns; age++)
                {
                    int before = world.Camps.Sum(world.LinkedPopulation);
                    PlanningResult plan = AiPlanning.Decide(world);
                    PreparedTurn prepared = TurnPlanner.Prepare(world, plan.Orders, plan.Traces);
                    var recoveryPlans = new Dictionary<int, PreparedDemandPlan>();
                    foreach (int campId in pairs.SelectMany(p => new[] { p.Parent, p.Daughter }).Distinct().OrderBy(id => id))
                    {
                        Camp tracked = world.Camps.FirstOrDefault(c => c.Id == campId);
                        if (tracked == null || tracked.Population <= 0 || !CultivationEconomy.EconomyEnabled(tracked)) continue;
                        prepared.DemandAllocations.TryGetValue(campId, out DemandController.Allocation allocation);
                        prepared.ProductionOrders.TryGetValue(campId, out ProductionOrder productionOrder);
                        prepared.Needs.TryGetValue(campId, out TurnNeeds needs);
                        EconomicIntent intent = prepared.Orders.FirstOrDefault(o => o.Kind == OrderKind.Economy && o.Entity == campId)?.Economy;
                        recoveryPlans[campId] = new PreparedDemandPlan
                        {
                            Population = tracked.Population, Allocation = allocation, Intent = intent,
                            ProductionOrder = productionOrder, Needs = needs,
                            OpeningHandTools = tracked.Inventory.HandTools, OpeningTools = tracked.Inventory.Tools,
                            OpeningHerdingTools = tracked.Inventory.HerdingTools,
                            OpeningStone = tracked.Inventory.Stone, OpeningClay = tracked.Inventory.Clay,
                            OpeningWood = tracked.Inventory.Wood,
                            OpeningHousingMaterial = tracked.Inventory.HousingMaterial
                        };
                    }
                    TurnRecord record = continuationResolver.Resolve(world, prepared);
                    foreach (var tileClaims in (continuationResolver.LastMetrics.FoodClaims
                        ?? new List<FoodClaimDiagnostic>()).GroupBy(c => c.Tile))
                    {
                        int tileId = tileClaims.Key;
                        FoodClaimDiagnostic tile = tileClaims.First();
                        Check(tile.TileReceived <= tile.TileRequested + 1e-7
                            && tileClaims.All(c => c.Received >= -1e-9 && c.Received <= c.Requested + 1e-7),
                            "seed " + seed + " variant " + variant.Name + " age " + age
                            + " conserves every attributed food claim on tile " + tileId);
                        foreach (var claim in tileClaims.GroupBy(c => (c.Entity, c.Kind)))
                        {
                            double persisted = record.Transfers.Where(t => t.Tile == tileId
                                && t.Entity == claim.Key.Entity && t.Kind == claim.Key.Kind).Sum(t => t.Food);
                            Check(Math.Abs(persisted - claim.Sum(c => c.Received))
                                    <= 1e-7 * Math.Max(1, persisted),
                                "seed " + seed + " variant " + variant.Name + " age " + age
                                + " closes attributed claim receipts into the authoritative transfer");
                        }
                    }
                    foreach (var item in recoveryPlans)
                    {
                        if (continuationResolver.LastMetrics.CraftFoodFlow != null
                            && continuationResolver.LastMetrics.CraftFoodFlow.TryGetValue(item.Key, out CampFoodFlowDiagnostic flow))
                            item.Value.FoodFlow = flow;
                        item.Value.FoodClaims = continuationResolver.LastMetrics.FoodClaims?
                            .Where(c => c.Camp == item.Key).ToList();
                    }
                    int after = world.Camps.Sum(world.LinkedPopulation);
                    Check(after == before + record.Growth - record.Deaths,
                        "seed " + seed + " variant " + variant.Name + " age " + age + " conserves population");
                    if (!variant.StablePopulationControl)
                        Check(record.Demographics.Sum(d => d.GrossBirths) == record.Growth
                            && record.Demographics.Sum(d => d.GrossDeaths) == record.Deaths,
                            "seed " + seed + " variant " + variant.Name + " age " + age + " closes gross demographic ledgers");
                    foreach (DemographicReport demographic in record.Demographics)
                    {
                        Check(expectedStartingPopulation.TryGetValue(demographic.Camp, out int expected)
                            && demographic.StartingPopulation == expected,
                            "seed " + seed + " variant " + variant.Name + " age " + age
                            + " starts demography from the preceding Close boundary");
                        if (!variant.StablePopulationControl)
                        {
                            Check(demographic.EndingPopulation == demographic.StartingPopulation
                                + demographic.GrossBirths - demographic.GrossDeaths,
                                "seed " + seed + " variant " + variant.Name + " age " + age
                                + " closes the camp integer demographic ledger before social transfers");
                            double causes = demographic.ExposureDeaths + demographic.CrowdingDeaths
                                + demographic.DiseaseDeaths + demographic.StarvationDeaths + demographic.AdditionalDeaths;
                            Check(Math.Abs(demographic.ContinuousDeaths - causes)
                                    <= 1e-7 * Math.Max(1, demographic.ContinuousDeaths),
                                "seed " + seed + " variant " + variant.Name + " age " + age
                                + " attributes every continuous death cause");
                            Check(Math.Abs(demographic.ConservationError) <= 1e-7,
                                "seed " + seed + " variant " + variant.Name + " age " + age
                                + " conserves continuous demography");
                        }
                    }
                    Check(record.Migrations.Count == 0,
                        "seed " + seed + " variant " + variant.Name + " has immigration disabled");
                    if (!variant.FurtherFission)
                        Check(record.Fissions.Count == 0,
                            "seed " + seed + " variant " + variant.Name + " has post-boundary fission disabled");
                    run.Births += record.Growth;
                    run.Deaths += record.Deaths;
                    run.FurtherFissions += record.Fissions.Count;
                    run.Migrations += record.Migrations.Count;
                    foreach (RecoveryPair pair in pairs)
                    {
                        recoveryPlans.TryGetValue(pair.Parent, out PreparedDemandPlan parentPlan);
                        recoveryPlans.TryGetValue(pair.Daughter, out PreparedDemandPlan daughterPlan);
                        observations.Add(RecoveryAt(world, record, variant.Name, seed, pair, "parent", pair.Parent, age, pair.ParentPopulation, parentPlan));
                        observations.Add(RecoveryAt(world, record, variant.Name, seed, pair, "daughter", pair.Daughter, age, pair.DaughterPopulation, daughterPlan));
                    }
                    expectedStartingPopulation = world.Camps.ToDictionary(c => c.Id, world.LinkedPopulation);
                }
                run.EndPopulation = world.Camps.Sum(world.LinkedPopulation);
                runs.Add(run);
                Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_RUN seed=" + seed
                    + " variant=" + variant.Name
                    + " allocation=" + world.Config.EconomicAllocation
                    + " trackedDualFixedShelterRoute=" + variant.TrackedDualFixedShelterRoute
                    + " trackedDualRouteCamps=" + trackedDualRouteCamps.Length
                    + " stablePopulationControl=" + variant.StablePopulationControl
                    + " consumptionFirstDiagnostic=" + variant.ConsumptionFirstDiagnostic
                    + " excludeGrowthFromSurvivalDiagnostic=" + variant.ExcludeGrowthFromSurvivalDiagnostic
                    + " noFieldDevelopmentDiagnostic=" + variant.NoFieldDevelopmentDiagnostic
                    + " clothMultiplier=" + F(variant.ClothMultiplier)
                    + " housingMultiplier=" + F(variant.HousingMultiplier)
                    + " fabricFoodYieldMultiplier=" + F(variant.FabricFoodYieldMultiplier)
                    + " plantFoodPerFabric=" + F(world.Config.Neolithic.PlantFoodPerFabricUnit)
                    + " population=" + run.StartPopulation + "->" + run.EndPopulation
                    + " births=" + run.Births + " deaths=" + run.Deaths
                    + " furtherFissions=" + run.FurtherFissions + " migrations=" + run.Migrations);
            }
        }

        int[] reportedAges = { 0, 1, 3, 5, 8, 12, 16, followTurns };
        foreach (RecoveryVariant variant in RecoveryVariants)
        foreach (string kind in new[] { "parent", "daughter" })
        foreach (int age in reportedAges.Distinct().Where(a => a <= followTurns).OrderBy(a => a))
        {
            RecoveryObservation[] rows = observations.Where(o => o.Variant == variant.Name
                && o.Kind == kind && o.Age == age).ToArray();
            if (rows.Length > 0) PrintRecovery(variant.Name, kind, age, rows);
        }

        foreach (RecoveryVariant variant in RecoveryVariants)
        foreach (string kind in new[] { "parent", "daughter" })
        {
            PrintRecoveryAssessment(variant.Name, kind, followTurns, observations);
            PrintRecoveryWindow(variant.Name, kind, Math.Max(1, followTurns - 8), followTurns, observations);
        }

        PrintShelterRouteCausality(followTurns, observations);
        PrintFeedstockRootCause(followTurns, observations);
        PrintEconomicRootCause(followTurns, observations);
        PrintRecoveryEffects(followTurns, observations, runs);
        PrintFabricFoodYieldSweep(followTurns, observations);
        Check(runs.Where(r => r.Variant != "recursive-native").All(r => r.FurtherFissions == 0 && r.Migrations == 0),
            "every isolated recovery branch remains free of social transfers");
        Check(runs.Where(r => r.Variant == "recursive-native").Sum(r => r.FurtherFissions) > 0,
            "the recursive native control exercises later fission");
        Console.WriteLine("PASS: " + assertions + " isolated post-fission recovery assertions");
        return assertions;
    }

    private static RecoveryObservation RecoveryAt(WorldState world, TurnRecord record, string variant,
        int seed, RecoveryPair pair, string kind, int campId, int age, int boundaryPopulation,
        PreparedDemandPlan plan = null)
    {
        Camp camp = world.Camps.FirstOrDefault(c => c.Id == campId);
        DemographicReport demographic = record?.Demographics.FirstOrDefault(d => d.Camp == campId);
        bool physicalReport = demographic?.Model == DemographicModel.PhysicalMortalityV1;
        double Produced(CraftGood good) => record?.Production
            .Where(p => p.Camp == campId && p.Good == good).Sum(p => p.Output) ?? 0;
        if (camp == null)
            return new RecoveryObservation
            {
                Variant = variant, Kind = kind, Seed = seed, Parent = pair.Parent, Daughter = pair.Daughter,
                Camp = campId, Age = age, BoundaryPopulation = boundaryPopulation
            };
        NeolithicProduction.ShelterCapacity shelter = NeolithicProduction.HousingCapacity(world, camp, world.Days);
        DemandController.Allocation allocation = plan?.Allocation;
        DemandController.Request[] clothingRequests = allocation?.Requests
            .Where(q => q.Service == "clothing" || q.Service == "clothing reserve").ToArray()
            ?? Array.Empty<DemandController.Request>();
        DemandController.Request[] shelterRequests = allocation?.Requests
            .Where(q => q.Service == "shelter replacement" || q.Service == "shelter deficit").ToArray()
            ?? Array.Empty<DemandController.Request>();
        double ShelterPeople(DemandController.Request q) => q.Good == CraftGood.Housing
            ? world.Config.Neolithic.HousingPeoplePerUnit
            : world.Config.Neolithic.LightHousingPeoplePerUnit;
        string Bindings(DemandController.Request[] requests) => requests.Length == 0 ? "none"
            : string.Join(",", requests.Select(q => string.IsNullOrEmpty(q.Unmet) ? "fully-funded" : q.Unmet)
                .Distinct().OrderBy(s => s, StringComparer.Ordinal));
        double clothMade = Produced(CraftGood.Cloth), animalFabricMade = Produced(CraftGood.AnimalFabric);
        double housingMade = Produced(CraftGood.Housing), lightHousingMade = Produced(CraftGood.LightHousing);
        double Planned(CraftGood good) => plan?.Needs?.Production != null
            && plan.Needs.Production.TryGetValue(good, out double amount) ? amount : 0;
        double Ceiling(CraftGood good) => plan?.ProductionOrder?.HasCeilings == true
            ? Math.Max(0, plan.ProductionOrder.Ceiling(good)) : 0;
        int Assigned(string role) => plan?.Intent?.Assignments?
            .Where(a => a.Role == role).Sum(a => a.Population) ?? 0;
        double RoleIntervals(string role) => record?.Production
            .Where(p => p.Camp == campId && p.Role == role).Sum(p => p.WorkerIntervals) ?? 0;
        double GoodIntervals(CraftGood good) => record?.Production
            .Where(p => p.Camp == campId && p.Good == good).Sum(p => p.WorkerIntervals) ?? 0;
        double PlantInput() => record?.Production.Where(p => p.Camp == campId).Sum(p => p.PlantFood) ?? 0;
        double AnimalInput() => record?.Production.Where(p => p.Camp == campId).Sum(p => p.AnimalFood) ?? 0;
        double Extracted(MaterialId material) => record?.MaterialTransfers
            .Where(t => t.Camp == campId && t.Material == material).Sum(t => t.Amount) ?? 0;
        double OtherLocalExtracted(MaterialId material) => record?.MaterialTransfers
            .Where(t => t.Camp != campId && t.Material == material
                && world.Map.Distance(camp.Tile, t.Tile) <= 1).Sum(t => t.Amount) ?? 0;
        var endingSources = MaterialEconomy.KnownSources(world, camp, world.Days);
        double EndingAccessible(MaterialId material) => endingSources.Where(s => s.Material == material)
            .GroupBy(s => s.Id).Sum(g => g.Sum(s => s.Available));
        DemandController.Request clothingNow = clothingRequests.FirstOrDefault(q => q.Service == "clothing");
        DemandController.Request clothingReserve = clothingRequests.FirstOrDefault(q => q.Service == "clothing reserve");
        DemandController.Request[] shelterReplacement = shelterRequests.Where(q => q.Service == "shelter replacement").ToArray();
        DemandController.Request[] shelterDeficit = shelterRequests.Where(q => q.Service == "shelter deficit").ToArray();
        DemandController.Request fieldExpansion = allocation?.Requests.FirstOrDefault(q => q.Service == "field expansion");
        DemandController.Request herdGrowth = allocation?.Requests.FirstOrDefault(q => q.Service == "herd growth");
        DemandController.Request farmTools = allocation?.Requests.FirstOrDefault(q => q.Service == "farm tools");
        DemandController.Request herdingTools = allocation?.Requests.FirstOrDefault(q => q.Service == "herding tools");
        DemandController.Request storage = allocation?.Requests.FirstOrDefault(q => q.Service == "storage");
        int survivalWorkers = allocation == null ? 0 : allocation.Herders + allocation.Farmers + allocation.FoodGatherers;
        int plannerProtectiveWorkers = allocation == null ? 0 : new[]
        {
            WorkforceRoles.Weaver, WorkforceRoles.Builder, WorkforceRoles.Craftsperson
        }.Sum(role => allocation.Staff.TryGetValue(role, out int workers) ? workers : 0);
        double AllocatedInput(Func<DemandController.Bundle, double> input, bool priorityClothing = false)
            => allocation?.Requests.Where(q => q.Bundle != null && (!priorityClothing || q.Service == "clothing"))
                .Sum(q => input(q.Bundle) * q.Granted) ?? 0;
        CampFoodFlowDiagnostic flow = plan?.FoodFlow;
        List<FoodClaimDiagnostic> foodClaims = plan?.FoodClaims ?? new List<FoodClaimDiagnostic>();
        var claimTiles = foodClaims.Where(c => c.Requested > 1e-12).GroupBy(c => c.Tile).ToArray();
        var managedHerds = record?.ManagedHerds.Where(h => h.Camp == campId).ToArray()
            ?? Array.Empty<ManagedHerdReport>();
        var managedHerdIds = managedHerds.Select(h => h.Herd).ToHashSet();
        var managedHerdPressure = record?.Pressure.Where(p => p.Kind == "Herd"
            && p.Pool == "Biomass" && managedHerdIds.Contains(p.Entity)).ToArray()
            ?? Array.Empty<PressureEntry>();
        var managedHerdTiles = managedHerdPressure.Select(p => p.Tile).ToHashSet();
        var competingHerdPressure = record?.Pressure.Where(p => p.Kind == "Herd"
            && p.Pool == "Biomass" && !managedHerdIds.Contains(p.Entity)
            && managedHerdTiles.Contains(p.Tile)).ToArray() ?? Array.Empty<PressureEntry>();
        var managedHerdWaterPressure = record?.Pressure.Where(p => p.Kind == "Herd"
            && p.Pool == "Freshwater" && managedHerdIds.Contains(p.Entity)).ToArray()
            ?? Array.Empty<PressureEntry>();
        var managedHerdWaterTiles = managedHerdWaterPressure.Select(p => p.Tile).ToHashSet();
        var competingHerdWaterPressure = record?.Pressure.Where(p => p.Kind == "Herd"
            && p.Pool == "Freshwater" && !managedHerdIds.Contains(p.Entity)
            && managedHerdWaterTiles.Contains(p.Tile)).ToArray() ?? Array.Empty<PressureEntry>();
        var competingHerdIds = competingHerdWaterPressure.Select(p => p.Entity).Distinct().ToHashSet();
        var visibleTiles = Knowledge.Visible(world, camp.Player);
        double fieldCapital = CultivationEconomy.Enabled(camp)
            ? CultivationEconomy.Footprint(world, camp).Sum(tile => Math.Max(0, world.Tiles[tile].CultivationCapital)) : 0;
        return new RecoveryObservation
        {
            Variant = variant, Kind = kind, Seed = seed, Parent = pair.Parent, Daughter = pair.Daughter,
            Camp = campId, Age = age, BoundaryPopulation = boundaryPopulation,
            Exists = true, Population = world.LinkedPopulation(camp),
            Food = physicalReport ? demographic.Nutrition : camp.FoodNeedsMetRatio,
            Clothing = physicalReport ? demographic.ClothingCoverage : camp.ClothPerCapita,
            Housing = physicalReport ? demographic.HousingCoverage : camp.HousingPerCapita,
            QoL = camp.QoL,
            Births = demographic?.GrossBirths ?? 0,
            Deaths = demographic?.GrossDeaths ?? 0,
            ExposureDeaths = demographic?.ExposureDeaths ?? 0,
            CrowdingDeaths = demographic?.CrowdingDeaths ?? 0,
            DiseaseDeaths = demographic?.DiseaseDeaths ?? 0,
            StarvationDeaths = demographic?.StarvationDeaths ?? 0,
            AdditionalDeaths = demographic?.AdditionalDeaths ?? 0,
            FoodProduced = demographic?.FoodProduced ?? 0,
            FoodRequired = demographic?.FoodRequired ?? 0,
            ClothProduced = clothMade + animalFabricMade,
            HousingProduced = housingMade + lightHousingMade,
            HousingMaterialProduced = Produced(CraftGood.HousingMaterial),
            ShelterProducedPeople = housingMade * world.Config.Neolithic.HousingPeoplePerUnit
                + lightHousingMade * world.Config.Neolithic.LightHousingPeoplePerUnit,
            ClothMade = clothMade, AnimalFabricMade = animalFabricMade,
            FixedHousingMade = housingMade, LightHousingMade = lightHousingMade,
            HasPreparedCeilings = plan?.ProductionOrder?.HasCeilings == true,
            PreparedClothCeiling = Ceiling(CraftGood.Cloth) + Ceiling(CraftGood.AnimalFabric),
            PreparedShelterCeilingPeople = Ceiling(CraftGood.Housing) * world.Config.Neolithic.HousingPeoplePerUnit
                + Ceiling(CraftGood.LightHousing) * world.Config.Neolithic.LightHousingPeoplePerUnit,
            PreparedHousingMaterialCeiling = Ceiling(CraftGood.HousingMaterial),
            PreparedClothForecast = Planned(CraftGood.Cloth) + Planned(CraftGood.AnimalFabric),
            PreparedShelterForecastPeople = Planned(CraftGood.Housing) * world.Config.Neolithic.HousingPeoplePerUnit
                + Planned(CraftGood.LightHousing) * world.Config.Neolithic.LightHousingPeoplePerUnit,
            PreparedHousingMaterialForecast = Planned(CraftGood.HousingMaterial),
            PreparedClothMade = Planned(CraftGood.Cloth), PreparedAnimalFabricMade = Planned(CraftGood.AnimalFabric),
            PreparedFixedHousingMade = Planned(CraftGood.Housing), PreparedLightHousingMade = Planned(CraftGood.LightHousing),
            PreparedHandToolsCeiling = Ceiling(CraftGood.HandTools), PreparedToolsCeiling = Ceiling(CraftGood.Tools),
            PreparedHandToolsForecast = Planned(CraftGood.HandTools), PreparedToolsForecast = Planned(CraftGood.Tools),
            HandToolsMade = Produced(CraftGood.HandTools), ToolsMade = Produced(CraftGood.Tools),
            OpeningHandTools = plan?.OpeningHandTools ?? 0, OpeningTools = plan?.OpeningTools ?? 0,
            OpeningHerdingTools = plan?.OpeningHerdingTools ?? 0,
            OpeningStone = plan?.OpeningStone ?? 0, OpeningClay = plan?.OpeningClay ?? 0,
            OpeningWood = plan?.OpeningWood ?? 0,
            WeaverIntervalsUsed = RoleIntervals(WorkforceRoles.Weaver),
            BuilderIntervalsUsed = RoleIntervals(WorkforceRoles.Builder),
            CraftspersonIntervalsUsed = RoleIntervals(WorkforceRoles.Craftsperson),
            ToolmakerIntervalsUsed = RoleIntervals(WorkforceRoles.Toolmaker),
            HousingMaterialIntervalsUsed = GoodIntervals(CraftGood.HousingMaterial),
            FixedHousingIntervalsUsed = GoodIntervals(CraftGood.Housing),
            LightHousingIntervalsUsed = GoodIntervals(CraftGood.LightHousing),
            StoneExtracted = Extracted(MaterialId.Stone), ClayExtracted = Extracted(MaterialId.Clay),
            WoodExtracted = Extracted(MaterialId.Wood),
            MaterialExtractionWorkerIntervals = record?.MaterialTransfers.Where(t => t.Camp == campId)
                .Sum(t => t.WorkerIntervals) ?? 0,
            OtherLocalStoneExtracted = OtherLocalExtracted(MaterialId.Stone),
            OtherLocalClayExtracted = OtherLocalExtracted(MaterialId.Clay),
            OtherLocalWoodExtracted = OtherLocalExtracted(MaterialId.Wood),
            WoodCapacityLostToCultivation = record?.Pressure.Where(p => p.Entity == campId)
                .Sum(p => p.WoodCapacityLoss) ?? 0,
            PlannedAccessibleStone = allocation?.AccessibleStone ?? 0,
            PlannedAccessibleClay = allocation?.AccessibleClay ?? 0,
            PlannedAccessibleWood = allocation?.AccessibleWood ?? 0,
            PlannedStoneExtraction = allocation?.PlannedStoneExtraction ?? 0,
            PlannedClayExtraction = allocation?.PlannedClayExtraction ?? 0,
            PlannedWoodExtraction = allocation?.PlannedWoodExtraction ?? 0,
            EndingAccessibleStone = EndingAccessible(MaterialId.Stone),
            EndingAccessibleClay = EndingAccessible(MaterialId.Clay),
            EndingAccessibleWood = EndingAccessible(MaterialId.Wood),
            FixedShelter = shelter.Fixed, LightShelter = shelter.Light,
            FabricStock = camp.Inventory.Cloth + camp.Inventory.AnimalFabric,
            HousingMaterialStock = camp.Inventory.HousingMaterial,
            Weavers = CultivationEconomy.Workers(camp, WorkforceRoles.Weaver),
            Builders = CultivationEconomy.Workers(camp, WorkforceRoles.Builder),
            Craftspersons = CultivationEconomy.Workers(camp, WorkforceRoles.Craftsperson),
            PreparedWeavers = Assigned(WorkforceRoles.Weaver),
            PreparedBuilders = Assigned(WorkforceRoles.Builder),
            PreparedCraftspersons = Assigned(WorkforceRoles.Craftsperson),
            PreparedToolmakers = Assigned(WorkforceRoles.Toolmaker),
            PreparedStonecutters = Assigned(WorkforceRoles.Stonecutter),
            PreparedClayMiners = Assigned(WorkforceRoles.ClayMiner),
            PreparedWoodcutters = Assigned(WorkforceRoles.Woodcutter),
            PreparedGatherers = Assigned(WorkforceRoles.Gatherer),
            PlanningPopulation = plan?.Population ?? 0,
            SurvivalWorkers = survivalWorkers,
            OperatingFarmers = allocation?.Farmers ?? 0, FoodGatherers = allocation?.FoodGatherers ?? 0,
            ExpansionFarmers = allocation != null && allocation.Staff.TryGetValue(WorkforceRoles.Farmer, out int expansionFarmers)
                ? expansionFarmers : 0,
            GoodsBudget = plan == null ? 0 : Math.Max(0, plan.Population - survivalWorkers),
            PlannerProtectiveWorkers = plannerProtectiveWorkers,
            RepairPasses = allocation?.RepairPasses ?? 0,
            TrimPasses = allocation?.TrimPasses ?? 0,
            FinalCompiledPersons = allocation?.FinalCompiledPersons ?? 0,
            FeedstockGatherers = allocation?.FeedstockGatherers ?? 0,
            MaterialGatherers = allocation?.MaterialGatherers ?? 0,
            ClothingRequested = clothingRequests.Sum(q => q.Units),
            ClothingGranted = clothingRequests.Sum(q => q.Granted),
            ShelterRequestedPeople = shelterRequests.Sum(q => q.Units * ShelterPeople(q)),
            ShelterGrantedPeople = shelterRequests.Sum(q => q.Granted * ShelterPeople(q)),
            ClothingNowRequested = clothingNow?.Units ?? 0, ClothingNowGranted = clothingNow?.Granted ?? 0,
            ClothingReserveRequested = clothingReserve?.Units ?? 0, ClothingReserveGranted = clothingReserve?.Granted ?? 0,
            ShelterReplacementRequested = shelterReplacement.Sum(q => q.Units * ShelterPeople(q)),
            ShelterReplacementGranted = shelterReplacement.Sum(q => q.Granted * ShelterPeople(q)),
            ShelterDeficitRequested = shelterDeficit.Sum(q => q.Units * ShelterPeople(q)),
            ShelterDeficitGranted = shelterDeficit.Sum(q => q.Granted * ShelterPeople(q)),
            FoodLaborRationed = !string.IsNullOrEmpty(allocation?.FoodUnmet),
            ClothingBinding = Bindings(clothingRequests), ShelterBinding = Bindings(shelterRequests),
            FieldExpansionRequested = fieldExpansion?.Units ?? 0,
            FieldExpansionGranted = fieldExpansion?.Granted ?? 0,
            FieldExpansionSteps = allocation?.FieldExpansionSteps ?? 0,
            FieldExpansionGapPerYear = allocation?.FieldExpansionGapPerYear ?? 0,
            FieldExpansionGainPerYear = allocation?.FieldExpansionGainPerYear ?? 0,
            FieldExpansionWorkerYears = allocation?.FieldExpansionWorkerYears ?? 0,
            FieldExpansionMinimumScore = allocation == null || double.IsPositiveInfinity(allocation.FieldExpansionMinimumScore)
                ? 0 : allocation.FieldExpansionMinimumScore,
            HerdGrowthRequested = herdGrowth?.Units ?? 0, HerdGrowthGranted = herdGrowth?.Granted ?? 0,
            FarmToolsRequested = farmTools?.Units ?? 0, FarmToolsGranted = farmTools?.Granted ?? 0,
            HerdingToolsRequested = herdingTools?.Units ?? 0, HerdingToolsGranted = herdingTools?.Granted ?? 0,
            StorageRequested = storage?.Units ?? 0, StorageGranted = storage?.Granted ?? 0,
            FieldCapital = fieldCapital,
            HasPreparedAllocation = allocation != null, HasFoodFlow = flow != null,
            HusbandryEnabled = allocation?.HusbandryEnabled ?? false,
            ExpectedSupply = allocation?.ExpectedSupply ?? 0, PlanMeals = allocation?.Meals ?? 0,
            PlanReserve = allocation?.OperatingReserve ?? 0,
            ExpectedForageAfterHerds = allocation?.ExpectedForageAfterHerds ?? 0,
            ExpectedManagedAnimalFood = allocation?.ExpectedManagedAnimalFood ?? 0,
            ActualManagedAnimalFood = managedHerds.Sum(h => h.AcceptedFood),
            ActualManagedAnimalPotential = managedHerds.Sum(h => h.PotentialFood),
            ManagedAnimals = managedHerds.Sum(h => h.StartingAnimals),
            ManagedFoodNutrition = managedHerds.Sum(h => h.StartingAnimals) > 0
                ? managedHerds.Sum(h => h.StartingAnimals * h.FoodNutrition)
                    / managedHerds.Sum(h => h.StartingAnimals) : 1,
            ManagedWaterNutrition = managedHerds.Sum(h => h.StartingAnimals) > 0
                ? managedHerds.Sum(h => h.StartingAnimals * h.WaterNutrition)
                    / managedHerds.Sum(h => h.StartingAnimals) : 1,
            ManagedFulfillment = managedHerds.Sum(h => h.StartingAnimals) > 0
                ? managedHerds.Sum(h => h.StartingAnimals * h.ManagementFulfillment)
                    / managedHerds.Sum(h => h.StartingAnimals) : 1,
            ManagedCoveredHerderYears = managedHerds.Sum(h => h.CoveredHerderYears),
            PreparedHerders = Assigned(WorkforceRoles.Herder),
            ManagedHerdFoodRequested = managedHerdPressure.Sum(p => p.Requested),
            ManagedHerdFoodReceived = managedHerdPressure.Sum(p => p.Realized),
            CompetingHerdFoodRequested = competingHerdPressure.Sum(p => p.Requested),
            CompetingHerdFoodReceived = competingHerdPressure.Sum(p => p.Realized),
            ManagedHerdWaterRequested = managedHerdWaterPressure.Sum(p => p.Requested),
            ManagedHerdWaterReceived = managedHerdWaterPressure.Sum(p => p.Realized),
            CompetingHerdWaterRequested = competingHerdWaterPressure.Sum(p => p.Requested),
            CompetingHerdWaterReceived = competingHerdWaterPressure.Sum(p => p.Realized),
            CompetingHerds = competingHerdIds.Count,
            VisibleCompetingHerds = world.Herds.Count(h => competingHerdIds.Contains(h.Id)
                && visibleTiles.Contains(h.Tile)),
            DomesticCompetingHerds = world.Herds.Count(h => competingHerdIds.Contains(h.Id)
                && h.DomesticCamp >= 0),
            ConsumptionIncomeTarget = allocation?.ConsumptionIncomeTarget ?? 0,
            ReserveFillTarget = allocation?.ReserveFillTarget ?? 0,
            MaximumGrowthFood = allocation?.MaximumGrowthFood ?? 0,
            ConsumptionOnlyFoodGatherers = allocation?.ConsumptionOnlyFoodGatherers ?? 0,
            ConsumptionReserveFoodGatherers = allocation?.ConsumptionReserveFoodGatherers ?? 0,
            ConsumptionGrowthFoodGatherers = allocation?.ConsumptionGrowthFoodGatherers ?? 0,
            PlantEnvelope = allocation?.PlantEnvelope ?? 0, AnimalEnvelope = allocation?.AnimalEnvelope ?? 0,
            FoodRoomEnvelope = allocation?.FoodRoomEnvelope ?? 0,
            PlantEnvelopeClothing = allocation?.PlantEnvelopeClothing ?? 0,
            AnimalEnvelopeClothing = allocation?.AnimalEnvelopeClothing ?? 0,
            FoodRoomEnvelopeClothing = allocation?.FoodRoomEnvelopeClothing ?? 0,
            AllocatedPlantInput = AllocatedInput(b => b.Plant),
            AllocatedAnimalInput = AllocatedInput(b => b.Animal),
            PriorityClothingPlantInput = AllocatedInput(b => b.Plant, true),
            PriorityClothingAnimalInput = AllocatedInput(b => b.Animal, true),
            NeededPlantInput = plan?.Needs?.PlantFeedstock ?? 0,
            NeededAnimalInput = plan?.Needs?.AnimalFeedstock ?? 0,
            NeededClothingPlantInput = plan?.Needs?.PlantClothingFeedstock ?? 0,
            NeededClothingAnimalInput = plan?.Needs?.AnimalClothingFeedstock ?? 0,
            FlowProjectedFood = flow?.ProjectedFood ?? 0, FlowNetIncome = flow?.NetIncome ?? 0,
            FlowPriorityIncome = flow?.PriorityIncome ?? 0,
            FlowAvailableAfterReserve = flow?.AvailableAfterReserve ?? 0,
            FlowPlantAvailable = flow?.PlantFoodAvailable ?? 0,
            FlowAnimalAvailable = flow?.AnimalFoodAvailable ?? 0,
            FlowRequestedPlantInput = flow?.RequestedPlantInput ?? 0,
            FlowRequestedAnimalInput = flow?.RequestedAnimalInput ?? 0,
            FlowBudgetedPlantInput = flow?.BudgetedPlantInput ?? 0,
            FlowBudgetedAnimalInput = flow?.BudgetedAnimalInput ?? 0,
            ActualPlantInput = PlantInput(), ActualAnimalInput = AnimalInput(),
            ManufacturingPlantClaim = foodClaims.Where(c => c.Manufacturing).Sum(c => c.Requested),
            ManufacturingPlantReceipt = foodClaims.Where(c => c.Manufacturing).Sum(c => c.Received),
            BiologicalPlantClaim = foodClaims.Where(c => !c.Manufacturing).Sum(c => c.Requested),
            BiologicalPlantReceipt = foodClaims.Where(c => !c.Manufacturing).Sum(c => c.Received),
            CompetingPlantClaim = claimTiles.Sum(g => g.First().CompetingRequested),
            CompetingPlantReceipt = claimTiles.Sum(g => g.First().CompetingReceived),
            PlantClaimTiles = claimTiles.Length,
            ContestedPlantClaimTiles = claimTiles.Count(g => g.First().CompetingRequested > 1e-12),
            RelevantProductionLimits = record?.ProductionLimits?
                .Where(d => d.Camp == campId && (d.Good == CraftGood.Cloth
                    || d.Good == CraftGood.HandTools || d.Good == CraftGood.Tools)).ToList()
        };
    }

    private static void PrintRecovery(string variant, string kind, int age, RecoveryObservation[] rows)
    {
        double population = rows.Sum(o => Math.Max(0, o.Population));
        double planningPopulation = rows.Sum(o => Math.Max(0, o.PlanningPopulation));
        double boundary = rows.Sum(o => Math.Max(1, o.BoundaryPopulation));
        double foodRequired = rows.Sum(o => o.FoodRequired);
        double shelter = rows.Sum(o => o.FixedShelter + o.LightShelter);
        Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY variant=" + variant
            + " kind=" + kind + " age=" + age + " samples=" + rows.Length
            + " alive=" + rows.Count(o => o.Alive)
            + " healthy=" + rows.Count(o => o.Healthy)
            + " population=" + F(rows.Average(o => (double)o.Population))
            + " populationRatio=" + F(population / Math.Max(1, boundary))
            + (age == 0 ? " inheritedServiceHistoryFCH=" : " demographicFCH=")
                + F(rows.Average(o => o.Food)) + "/" + F(rows.Average(o => o.Clothing)) + "/" + F(rows.Average(o => o.Housing))
            + " QoL=" + F(rows.Average(o => o.QoL))
            + " birthsDeaths=" + rows.Sum(o => o.Births) + "/" + rows.Sum(o => o.Deaths)
            + " causeDeaths=" + F(rows.Sum(o => o.ExposureDeaths)) + "/" + F(rows.Sum(o => o.CrowdingDeaths))
                + "/" + F(rows.Sum(o => o.DiseaseDeaths)) + "/" + F(rows.Sum(o => o.StarvationDeaths))
                + "/" + F(rows.Sum(o => o.AdditionalDeaths))
            + " foodYieldPerNeed=" + F(foodRequired > 0 ? rows.Sum(o => o.FoodProduced) / foodRequired : 0)
            + " protectiveOutputPer1000=" + F(1000 * rows.Sum(o => o.ClothProduced) / Math.Max(1, population))
                + "/" + F(1000 * rows.Sum(o => o.HousingProduced) / Math.Max(1, population))
                + "/" + F(1000 * rows.Sum(o => o.HousingMaterialProduced) / Math.Max(1, population))
            + " shelterPerPerson=" + F(shelter / Math.Max(1, population))
            + " fixedLightShelterPerPerson=" + F(rows.Sum(o => o.FixedShelter) / Math.Max(1, population))
                + "/" + F(rows.Sum(o => o.LightShelter) / Math.Max(1, population))
            + " protectiveStockPerPerson=" + F(rows.Sum(o => o.FabricStock) / Math.Max(1, population))
                + "/" + F(rows.Sum(o => o.HousingMaterialStock) / Math.Max(1, population))
            + " protectiveCapableWorkersPer1000=" + F(1000.0 * rows.Sum(o => o.Weavers) / Math.Max(1, population))
                + "/" + F(1000.0 * rows.Sum(o => o.Builders) / Math.Max(1, population))
                + "/" + F(1000.0 * rows.Sum(o => o.Craftspersons) / Math.Max(1, population))
            + " preparedSurvivalGoodsShares=" + F(rows.Sum(o => o.SurvivalWorkers) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.GoodsBudget) / Math.Max(1, planningPopulation))
            + " preparedProtectiveCapableShare=" + F(rows.Sum(o => o.PlannerProtectiveWorkers) / Math.Max(1, planningPopulation))
            + " preparedRequestGrantPerPerson=" + F(rows.Sum(o => o.ClothingRequested) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.ClothingGranted) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.ShelterRequestedPeople) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.ShelterGrantedPeople) / Math.Max(1, planningPopulation)));
    }

    private static void PrintRecoveryWindow(string variant, string kind, int firstAge, int lastAge,
        List<RecoveryObservation> observations)
    {
        RecoveryObservation[] rows = observations.Where(o => o.Variant == variant && o.Kind == kind
            && o.Age >= firstAge && o.Age <= lastAge).ToArray();
        double planningPopulation = rows.Sum(o => Math.Max(0, o.PlanningPopulation));
        double clothingRequested = rows.Sum(o => o.ClothingRequested);
        double shelterRequested = rows.Sum(o => o.ShelterRequestedPeople);
        double clothingNowRequested = rows.Sum(o => o.ClothingNowRequested);
        double clothingReserveRequested = rows.Sum(o => o.ClothingReserveRequested);
        double shelterReplacementRequested = rows.Sum(o => o.ShelterReplacementRequested);
        double shelterDeficitRequested = rows.Sum(o => o.ShelterDeficitRequested);
        double clothForecast = rows.Sum(o => o.PreparedClothForecast);
        double shelterForecast = rows.Sum(o => o.PreparedShelterForecastPeople);
        double clothOutput = rows.Sum(o => o.ClothProduced);
        double shelterOutput = rows.Sum(o => o.ShelterProducedPeople);
        Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_WINDOW variant=" + variant
            + " kind=" + kind + " ages=" + firstAge + "-" + lastAge
            + " campTurns=" + rows.Length
            + " meanFCH=" + F(rows.Average(o => o.Food)) + "/" + F(rows.Average(o => o.Clothing)) + "/" + F(rows.Average(o => o.Housing))
            + " meanQoL=" + F(rows.Average(o => o.QoL))
            + " outputPerResidentTurn=" + F(rows.Sum(o => o.ClothProduced) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.ShelterProducedPeople) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.HousingMaterialProduced) / Math.Max(1, planningPopulation))
            + " outputMixPerResidentTurn=" + F(rows.Sum(o => o.ClothMade) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.AnimalFabricMade) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.FixedHousingMade) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.LightHousingMade) / Math.Max(1, planningPopulation))
            + " preparedCeilingPerResidentTurn=" + F(rows.Sum(o => o.PreparedClothCeiling) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.PreparedShelterCeilingPeople) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.PreparedHousingMaterialCeiling) / Math.Max(1, planningPopulation))
            + " preparedForecastPerResidentTurn=" + F(clothForecast / Math.Max(1, planningPopulation))
                + "/" + F(shelterForecast / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.PreparedHousingMaterialForecast) / Math.Max(1, planningPopulation))
            + " preparedForecastMixPerResidentTurn=" + F(rows.Sum(o => o.PreparedClothMade) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.PreparedAnimalFabricMade) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.PreparedFixedHousingMade) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.PreparedLightHousingMade) / Math.Max(1, planningPopulation))
            + " outputVsPreparedForecast=" + F(clothForecast > 0 ? clothOutput / clothForecast : 1)
                + "/" + F(shelterForecast > 0 ? shelterOutput / shelterForecast : 1)
                + "/" + F(rows.Sum(o => o.PreparedHousingMaterialForecast) > 0
                    ? rows.Sum(o => o.HousingMaterialProduced) / rows.Sum(o => o.PreparedHousingMaterialForecast) : 1)
            + " outputMixVsPreparedForecast="
                + F(rows.Sum(o => o.PreparedClothMade) > 0 ? rows.Sum(o => o.ClothMade) / rows.Sum(o => o.PreparedClothMade) : 1)
                + "/" + F(rows.Sum(o => o.PreparedAnimalFabricMade) > 0 ? rows.Sum(o => o.AnimalFabricMade) / rows.Sum(o => o.PreparedAnimalFabricMade) : 1)
                + "/" + F(rows.Sum(o => o.PreparedFixedHousingMade) > 0 ? rows.Sum(o => o.FixedHousingMade) / rows.Sum(o => o.PreparedFixedHousingMade) : 1)
                + "/" + F(rows.Sum(o => o.PreparedLightHousingMade) > 0 ? rows.Sum(o => o.LightHousingMade) / rows.Sum(o => o.PreparedLightHousingMade) : 1)
            + " roleIntervalsUsedPerResidentTurn=" + F(rows.Sum(o => o.WeaverIntervalsUsed) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.BuilderIntervalsUsed) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.CraftspersonIntervalsUsed) / Math.Max(1, planningPopulation))
            + " housingIntervalsUsedPerResidentTurn=" + F(rows.Sum(o => o.HousingMaterialIntervalsUsed) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.FixedHousingIntervalsUsed) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.LightHousingIntervalsUsed) / Math.Max(1, planningPopulation))
            + " extractedMaterialPerResidentTurn=" + F(rows.Sum(o => o.StoneExtracted) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.ClayExtracted) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.WoodExtracted) / Math.Max(1, planningPopulation))
            + " preparedRequestPerResidentTurn=" + F(clothingRequested / Math.Max(1, planningPopulation))
                + "/" + F(shelterRequested / Math.Max(1, planningPopulation))
            + " preparedRequestFulfilment=" + F(clothingRequested > 0 ? rows.Sum(o => o.ClothingGranted) / clothingRequested : 1)
                + "/" + F(shelterRequested > 0 ? rows.Sum(o => o.ShelterGrantedPeople) / shelterRequested : 1)
            + " preparedContinuityFulfilment=" + F(clothingNowRequested > 0 ? rows.Sum(o => o.ClothingNowGranted) / clothingNowRequested : 1)
                + "/" + F(shelterReplacementRequested > 0 ? rows.Sum(o => o.ShelterReplacementGranted) / shelterReplacementRequested : 1)
            + " preparedRecoveryFulfilment=" + F(clothingReserveRequested > 0 ? rows.Sum(o => o.ClothingReserveGranted) / clothingReserveRequested : 1)
                + "/" + F(shelterDeficitRequested > 0 ? rows.Sum(o => o.ShelterDeficitGranted) / shelterDeficitRequested : 1)
            + " preparedSurvivalGoodsShares=" + F(rows.Sum(o => o.SurvivalWorkers) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.GoodsBudget) / Math.Max(1, planningPopulation))
            + " preparedProtectiveCapableShare=" + F(rows.Sum(o => o.PlannerProtectiveWorkers) / Math.Max(1, planningPopulation))
            + " preparedProtectiveRoleShares=" + F(rows.Sum(o => o.PreparedWeavers) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.PreparedBuilders) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.PreparedCraftspersons) / Math.Max(1, planningPopulation))
            + " preparedAuxiliaryGathererShares=" + F(rows.Sum(o => o.FeedstockGatherers) / Math.Max(1, planningPopulation))
                + "/" + F(rows.Sum(o => o.MaterialGatherers) / Math.Max(1, planningPopulation))
            + " preparedFoodLaborRationedRate=" + F(rows.Count(o => o.FoodLaborRationed) / (double)Math.Max(1, rows.Length))
            + " preparedRepairRate=" + F(rows.Count(o => o.RepairPasses > 0) / (double)Math.Max(1, rows.Length))
            + " preparedTrimRate=" + F(rows.Count(o => o.TrimPasses > 0) / (double)Math.Max(1, rows.Length))
            + " finalCompiledGoodsShare=" + F(rows.Sum(o => o.FinalCompiledPersons) / Math.Max(1, planningPopulation)));
        foreach (var group in rows.SelectMany(o => new[]
        {
            (Service: "clothing", Binding: o.ClothingBinding),
            (Service: "shelter", Binding: o.ShelterBinding)
        }).GroupBy(x => (x.Service, x.Binding)).OrderBy(g => g.Key.Service, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Binding, StringComparer.Ordinal))
            Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_PREPARED_BINDING variant=" + variant
                + " kind=" + kind + " ages=" + firstAge + "-" + lastAge
                + " service=" + group.Key.Service + " cause=" + group.Key.Binding.Replace(' ', '_')
                + " campTurns=" + group.Count());
    }

    private static void PrintRecoveryAssessment(string variant, string kind, int followTurns,
        List<RecoveryObservation> observations)
    {
        var paths = observations.Where(o => o.Variant == variant && o.Kind == kind)
            .GroupBy(o => (o.Seed, o.Parent, o.Daughter, o.Camp)).ToArray();
        int firstHealthy = 0, sustainedHealthy = 0, alive = 0;
        double firstAge = 0, sustainedAge = 0;
        foreach (var path in paths)
        {
            RecoveryObservation[] ordered = path.OrderBy(o => o.Age).ToArray();
            RecoveryObservation terminal = ordered.FirstOrDefault(o => o.Age == followTurns);
            if (terminal != null && terminal.Alive) alive++;
            RecoveryObservation first = ordered.FirstOrDefault(o => o.Age > 0 && o.Healthy);
            if (first != null) { firstHealthy++; firstAge += first.Age; }
            for (int i = 0; i + 2 < ordered.Length; i++)
            {
                if (ordered[i].Age <= 0 || ordered[i + 1].Age != ordered[i].Age + 1
                    || ordered[i + 2].Age != ordered[i].Age + 2) continue;
                if (!ordered[i].Healthy || !ordered[i + 1].Healthy || !ordered[i + 2].Healthy) continue;
                sustainedHealthy++; sustainedAge += ordered[i].Age; break;
            }
        }
        Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_ASSESSMENT variant=" + variant
            + " kind=" + kind + " paths=" + paths.Length + " terminalAlive=" + alive
            + " everHealthy=" + firstHealthy
            + " meanFirstHealthyAge=" + F(firstHealthy > 0 ? firstAge / firstHealthy : 0)
            + " sustainedHealthy3=" + sustainedHealthy
            + " meanSustainedStartAge=" + F(sustainedHealthy > 0 ? sustainedAge / sustainedHealthy : 0));
    }

    private static void PrintShelterRouteCausality(int followTurns, List<RecoveryObservation> observations)
    {
        int firstAge = Math.Max(1, followTurns - 8);
        RecoveryObservation[] nativeWindow = observations.Where(o => o.Variant == "isolated-native"
            && o.Kind == "daughter" && o.Age >= firstAge && o.Age <= followTurns).ToArray();
        RecoveryObservation[] fixedWindow = observations.Where(o => o.Variant == "isolated-fixed-shelter-route"
            && o.Kind == "daughter" && o.Age >= firstAge && o.Age <= followTurns).ToArray();
        RecoveryObservation[] nativeTerminal = observations.Where(o => o.Variant == "isolated-native"
            && o.Kind == "daughter" && o.Age == followTurns).ToArray();
        RecoveryObservation[] fixedTerminal = observations.Where(o => o.Variant == "isolated-fixed-shelter-route"
            && o.Kind == "daughter" && o.Age == followTurns).ToArray();
        double Ratio(double actual, double forecast) => forecast > 0 ? actual / forecast : 1;
        double nativeLightRealization = Ratio(nativeWindow.Sum(o => o.LightHousingMade), nativeWindow.Sum(o => o.PreparedLightHousingMade));
        double nativeFixedRealization = Ratio(nativeWindow.Sum(o => o.FixedHousingMade), nativeWindow.Sum(o => o.PreparedFixedHousingMade));
        double nativeShelterRealization = Ratio(nativeWindow.Sum(o => o.ShelterProducedPeople), nativeWindow.Sum(o => o.PreparedShelterForecastPeople));
        double fixedShelterRealization = Ratio(fixedWindow.Sum(o => o.ShelterProducedPeople), fixedWindow.Sum(o => o.PreparedShelterForecastPeople));
        double nativeAnimalFabricRealization = Ratio(nativeWindow.Sum(o => o.AnimalFabricMade), nativeWindow.Sum(o => o.PreparedAnimalFabricMade));
        double nativeClothRealization = Ratio(nativeWindow.Sum(o => o.ClothMade), nativeWindow.Sum(o => o.PreparedClothMade));
        Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_ROOT_CAUSE ages=" + firstAge + "-" + followTurns
            + " nativeLightFixedRealization=" + F(nativeLightRealization) + "/" + F(nativeFixedRealization)
            + " nativeAnimalFabricClothRealization=" + F(nativeAnimalFabricRealization) + "/" + F(nativeClothRealization)
            + " shelterRealizationNativeFixedRoute=" + F(nativeShelterRealization) + "/" + F(fixedShelterRealization)
            + " terminalDaughterHousingNativeFixedRoute=" + F(nativeTerminal.Average(o => o.Housing))
                + "/" + F(fixedTerminal.Average(o => o.Housing))
            + " terminalDaughterQoLNativeFixedRoute=" + F(nativeTerminal.Average(o => o.QoL))
                + "/" + F(fixedTerminal.Average(o => o.QoL)));
        // This was a strict causal-improvement control before the production
        // allocator gained its own bounded fallback. Equality is now expected
        // when no tracked camp is dual-capable or the native fallback selects
        // the same funded route; the control must still never make the matched
        // service or welfare outcome worse.
        Check(fixedShelterRealization + 1e-9 >= nativeShelterRealization,
            "the fixed-shelter route control does not reduce prepared-to-realized shelter closure");
        Check(fixedTerminal.Average(o => o.Housing) + 1e-9 >= nativeTerminal.Average(o => o.Housing),
            "the fixed-shelter route control does not reduce terminal daughter housing");
        Check(fixedTerminal.Average(o => o.QoL) + 1e-9 >= nativeTerminal.Average(o => o.QoL),
            "the fixed-shelter route control does not reduce terminal daughter experienced welfare");
    }

    private static void PrintFeedstockRootCause(int followTurns, List<RecoveryObservation> observations)
    {
        int firstAge = Math.Max(1, followTurns - 8);
        RecoveryObservation[] rows = observations.Where(o => o.Variant == "isolated-native"
            && o.Kind == "daughter" && o.Age >= firstAge && o.Age <= followTurns
            && o.HasPreparedAllocation && o.HasFoodFlow).ToArray();
        RecoveryObservation[] animal = rows.Where(o => o.PreparedAnimalFabricMade > 1e-9
            || o.AllocatedAnimalInput > 1e-9).ToArray();
        bool OrdinaryAnimalExceedsNormal(RecoveryObservation o)
            => o.AllocatedAnimalInput > o.AnimalEnvelope + o.PriorityClothingAnimalInput + 1e-8;
        bool OrdinaryFoodExceedsNormal(RecoveryObservation o)
            => o.HusbandryEnabled && o.AllocatedPlantInput + o.AllocatedAnimalInput
                > o.FoodRoomEnvelope + o.PriorityClothingPlantInput + o.PriorityClothingAnimalInput + 1e-8;
        double Sum(Func<RecoveryObservation, double> value) => animal.Sum(value);
        Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_FEEDSTOCK_ROOT ages=" + firstAge + "-" + followTurns
            + " campTurns=" + animal.Length
            + " ordinaryAnimalBeyondReserve=" + animal.Count(OrdinaryAnimalExceedsNormal)
            + " noPriorityAnimalYetBeyondReserve=" + animal.Count(o => o.PriorityClothingAnimalInput <= 1e-9
                && OrdinaryAnimalExceedsNormal(o))
            + " ordinaryFoodBeyondReserve=" + animal.Count(OrdinaryFoodExceedsNormal)
            + " allocatedAnimal=" + F(Sum(o => o.AllocatedAnimalInput))
            + " normalClothingAnimalEnvelope=" + F(Sum(o => o.AnimalEnvelope)) + "/" + F(Sum(o => o.AnimalEnvelopeClothing))
            + " priorityAnimal=" + F(Sum(o => o.PriorityClothingAnimalInput))
            + " growthRequestedBudgetedAnimal=" + F(Sum(o => o.FlowRequestedAnimalInput)) + "/" + F(Sum(o => o.FlowBudgetedAnimalInput))
            + " actualAnimalInput=" + F(Sum(o => o.ActualAnimalInput))
            + " forecastActualAnimalFabric=" + F(Sum(o => o.PreparedAnimalFabricMade)) + "/" + F(Sum(o => o.AnimalFabricMade))
            + " forecastActualLightHousing=" + F(Sum(o => o.PreparedLightHousingMade)) + "/" + F(Sum(o => o.LightHousingMade)));

        foreach (RecoveryObservation o in animal.Where(OrdinaryAnimalExceedsNormal)
            .OrderByDescending(o => o.AllocatedAnimalInput - o.AnimalEnvelope - o.PriorityClothingAnimalInput)
            .ThenBy(o => o.Seed).ThenBy(o => o.Camp).ThenBy(o => o.Age).Take(12))
            Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_FEEDSTOCK_TRACE seed=" + o.Seed
                + " camp=" + o.Camp + " age=" + o.Age
                + " population=" + o.PlanningPopulation
                + " allocationAnimal=" + F(o.AllocatedAnimalInput)
                + " priorityAnimal=" + F(o.PriorityClothingAnimalInput)
                + " normalClothingAnimalEnvelope=" + F(o.AnimalEnvelope) + "/" + F(o.AnimalEnvelopeClothing)
                + " allocationFood=" + F(o.AllocatedPlantInput + o.AllocatedAnimalInput)
                + " priorityFood=" + F(o.PriorityClothingPlantInput + o.PriorityClothingAnimalInput)
                + " normalClothingFoodRoom=" + F(o.FoodRoomEnvelope) + "/" + F(o.FoodRoomEnvelopeClothing)
                + " expectedActualIncome=" + F(o.ExpectedSupply) + "/" + F(o.FlowNetIncome)
                + " mealsReserve=" + F(o.PlanMeals) + "/" + F(o.PlanReserve)
                + " projectedFood=" + F(o.FlowProjectedFood)
                + " availableAfterReserve=" + F(o.FlowAvailableAfterReserve)
                + " allocationGrowthBudgetActualAnimal=" + F(o.AllocatedAnimalInput) + "/"
                    + F(o.FlowBudgetedAnimalInput) + "/" + F(o.ActualAnimalInput)
                + " forecastActualAnimalFabric=" + F(o.PreparedAnimalFabricMade) + "/" + F(o.AnimalFabricMade)
                + " forecastActualLightHousing=" + F(o.PreparedLightHousingMade) + "/" + F(o.LightHousingMade));
        Check(rows.All(o => !OrdinaryAnimalExceedsNormal(o)),
            "ordinary Animal Fabric inputs never borrow the current-clothing reserve exception");
        Check(rows.All(o => !OrdinaryFoodExceedsNormal(o)),
            "ordinary manufacturing food never borrows the current-clothing reserve exception");
        foreach (RecoveryObservation o in rows.Where(o => o.PreparedClothCeiling > o.PreparedClothForecast + 1e-8)
            .OrderByDescending(o => o.PreparedClothCeiling - o.PreparedClothForecast)
            .ThenBy(o => o.Seed).ThenBy(o => o.Camp).ThenBy(o => o.Age).Take(12))
            Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_CLOSURE_TRACE seed=" + o.Seed
                + " camp=" + o.Camp + " age=" + o.Age + " population=" + o.PlanningPopulation
                + " clothingNowReserveRequest=" + F(o.ClothingNowRequested) + "/" + F(o.ClothingReserveRequested)
                + " clothingNowReserveGrant=" + F(o.ClothingNowGranted) + "/" + F(o.ClothingReserveGranted)
                + " ceilingForecastActual=" + F(o.PreparedClothCeiling) + "/" + F(o.PreparedClothForecast) + "/" + F(o.ClothProduced)
                + " preparedRolesWeaverCraftTool=" + o.PreparedWeavers + "/" + o.PreparedCraftspersons + "/" + o.PreparedToolmakers
                + " roleIntervalsWeaverCraftTool=" + F(o.WeaverIntervalsUsed) + "/" + F(o.CraftspersonIntervalsUsed) + "/" + F(o.ToolmakerIntervalsUsed)
                + " toolOpeningHandRegular=" + F(o.OpeningHandTools) + "/" + F(o.OpeningTools)
                + " toolCeilingHandRegular=" + F(o.PreparedHandToolsCeiling) + "/" + F(o.PreparedToolsCeiling)
                + " toolForecastHandRegular=" + F(o.PreparedHandToolsForecast) + "/" + F(o.PreparedToolsForecast)
                + " toolActualHandRegular=" + F(o.HandToolsMade) + "/" + F(o.ToolsMade)
                + " allocatedNeededActualPlant=" + F(o.AllocatedPlantInput) + "/" + F(o.NeededPlantInput) + "/" + F(o.ActualPlantInput)
                + " foodEnvelopeNormalClothing=" + F(o.PlantEnvelope) + "/" + F(o.PlantEnvelopeClothing)
                + " foodFlowAvailableBudgetedPlant=" + F(o.FlowAvailableAfterReserve) + "/" + F(o.FlowBudgetedPlantInput)
                + " binding=" + o.ClothingBinding + " compiled=" + F(o.FinalCompiledPersons));
        foreach (RecoveryObservation o in rows.Where(o => o.PreparedClothForecast - o.ClothProduced
                > 1e-3 * Math.Max(1, o.PreparedClothForecast))
            .OrderByDescending(o => o.PreparedClothForecast - o.ClothProduced)
            .ThenBy(o => o.Seed).ThenBy(o => o.Camp).ThenBy(o => o.Age).Take(12))
        {
            Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_REALIZATION_TRACE seed=" + o.Seed
                + " camp=" + o.Camp + " age=" + o.Age + " population=" + o.PlanningPopulation
                + " ceilingForecastActualCloth=" + F(o.PreparedClothCeiling) + "/" + F(o.PreparedClothForecast) + "/" + F(o.ClothProduced)
                + " neededAvailableBudgetedActualPlant=" + F(o.NeededPlantInput) + "/" + F(o.FlowPlantAvailable)
                    + "/" + F(o.FlowBudgetedPlantInput) + "/" + F(o.ActualPlantInput)
                + " projectedNetPriorityFood=" + F(o.FlowProjectedFood) + "/" + F(o.FlowNetIncome) + "/" + F(o.FlowPriorityIncome)
                + " mealsReserve=" + F(o.PlanMeals) + "/" + F(o.PlanReserve)
                + " expectedSupply=" + F(o.ExpectedSupply)
                + " expectedActualManagedAnimalFood=" + F(o.ExpectedManagedAnimalFood) + "/" + F(o.ActualManagedAnimalFood)
                + " managedPotential=" + F(o.ActualManagedAnimalPotential)
                + " managedAnimalsNutritionFulfillment=" + F(o.ManagedAnimals) + "/"
                    + F(o.ManagedFoodNutrition) + "/" + F(o.ManagedWaterNutrition) + "/" + F(o.ManagedFulfillment)
                + " preparedHerdersOpeningTools=" + o.PreparedHerders + "/" + F(o.OpeningHerdingTools)
                + " coveredHerderYears=" + F(o.ManagedCoveredHerderYears)
                + " herdForageRequestReceipt=" + F(o.ManagedHerdFoodRequested) + "/" + F(o.ManagedHerdFoodReceived)
                + " competingHerdRequestReceipt=" + F(o.CompetingHerdFoodRequested) + "/" + F(o.CompetingHerdFoodReceived)
                + " herdWaterRequestReceipt=" + F(o.ManagedHerdWaterRequested) + "/" + F(o.ManagedHerdWaterReceived)
                + " competingHerdWaterRequestReceipt=" + F(o.CompetingHerdWaterRequested) + "/" + F(o.CompetingHerdWaterReceived)
                + " competingHerdsVisibleDomestic=" + o.CompetingHerds + "/"
                    + o.VisibleCompetingHerds + "/" + o.DomesticCompetingHerds
                + " expectedForageAfterHerds=" + F(o.ExpectedForageAfterHerds)
                + " fieldExpansionRequestGrantWoodLoss=" + F(o.FieldExpansionRequested) + "/"
                    + F(o.FieldExpansionGranted) + "/" + F(o.WoodCapacityLostToCultivation)
                + " accessiblePlannedEndingSCW=" + F(o.PlannedAccessibleStone) + "/"
                    + F(o.PlannedAccessibleClay) + "/" + F(o.PlannedAccessibleWood) + ":"
                    + F(o.EndingAccessibleStone) + "/" + F(o.EndingAccessibleClay) + "/"
                    + F(o.EndingAccessibleWood)
                + " extractedOwnOtherLocalSCW=" + F(o.StoneExtracted) + "/" + F(o.ClayExtracted)
                    + "/" + F(o.WoodExtracted) + ":" + F(o.OtherLocalStoneExtracted) + "/"
                    + F(o.OtherLocalClayExtracted) + "/" + F(o.OtherLocalWoodExtracted)
                + " openingStockSCW=" + F(o.OpeningStone) + "/" + F(o.OpeningClay) + "/" + F(o.OpeningWood)
                + " plannedExtractSCW=" + F(o.PlannedStoneExtraction) + "/"
                    + F(o.PlannedClayExtraction) + "/" + F(o.PlannedWoodExtraction)
                + " preparedExtractorsSCWG=" + o.PreparedStonecutters + "/" + o.PreparedClayMiners
                    + "/" + o.PreparedWoodcutters + "/" + o.PreparedGatherers
                + " auxiliaryFeedstockMaterial=" + F(o.FeedstockGatherers) + "/" + F(o.MaterialGatherers)
                + " materialWorkerIntervals=" + F(o.MaterialExtractionWorkerIntervals)
                + " repairTrim=" + o.RepairPasses + "/" + o.TrimPasses
                + " shelterCeilingForecastActualPeople=" + F(o.PreparedShelterCeilingPeople) + "/"
                    + F(o.PreparedShelterForecastPeople) + "/" + F(o.ShelterProducedPeople)
                + " housingMaterialCeilingForecastActual=" + F(o.PreparedHousingMaterialCeiling) + "/"
                    + F(o.PreparedHousingMaterialForecast) + "/" + F(o.HousingMaterialProduced)
                + " plantEnvelopeNormalClothing=" + F(o.PlantEnvelope) + "/" + F(o.PlantEnvelopeClothing)
                + " manufacturingClaimReceipt=" + F(o.ManufacturingPlantClaim) + "/" + F(o.ManufacturingPlantReceipt)
                + " biologicalClaimReceipt=" + F(o.BiologicalPlantClaim) + "/" + F(o.BiologicalPlantReceipt)
                + " competingClaimReceipt=" + F(o.CompetingPlantClaim) + "/" + F(o.CompetingPlantReceipt)
                + " claimTilesContested=" + o.PlantClaimTiles + "/" + o.ContestedPlantClaimTiles
                + " binding=" + o.ClothingBinding);
            foreach (ProductionLimitDiagnostic d in o.RelevantProductionLimits ?? new List<ProductionLimitDiagnostic>())
                Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_PRODUCTION_LIMIT seed=" + o.Seed
                    + " camp=" + o.Camp + " age=" + o.Age + " good=" + d.Good
                    + " role=" + d.Role.Replace(' ', '_')
                    + " requestedLaborToolMaterialFoodDependencyStorage=" + F(d.Requested) + "/"
                        + F(d.LaborToolLimit) + "/" + F(d.MaterialLimit) + "/" + F(d.FoodLimit)
                        + "/" + F(d.DependencyLimit) + "/" + F(d.StorageLimit)
                    + " openingPlantAnimal=" + F(d.OpeningPlantFood) + "/" + F(d.OpeningAnimalFood)
                    + " openingStoneClayWood=" + F(d.OpeningStone) + "/" + F(d.OpeningClay) + "/" + F(d.OpeningWood)
                    + " storageRoom=" + F(d.StorageRoomUnits));
        }
    }

    private static void PrintEconomicRootCause(int followTurns, List<RecoveryObservation> observations)
    {
        int firstAge = Math.Max(1, followTurns - 8);
        foreach (string variant in new[] { "isolated-native", "isolated-fixed-shelter-route",
            "isolated-stable-population-fixed-shelter" })
        foreach (string kind in new[] { "parent", "daughter" })
        {
            RecoveryObservation[] rows = observations.Where(o => o.Variant == variant && o.Kind == kind
                && o.Age >= firstAge && o.Age <= followTurns && o.HasPreparedAllocation).ToArray();
            double people = Math.Max(1, rows.Sum(o => o.PlanningPopulation));
            double requested = rows.Sum(o => o.FieldExpansionRequested);
            Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_ECONOMY variant=" + variant
                + " kind=" + kind + " ages=" + firstAge + "-" + followTurns
                + " campTurns=" + rows.Length
                + " foodPlanPerResidentTurn=" + F(rows.Sum(o => o.ExpectedSupply) / people)
                    + "/" + F(rows.Sum(o => o.FlowNetIncome) / people)
                    + "/" + F(rows.Sum(o => o.PlanMeals) / people)
                    + "/" + F(rows.Sum(o => o.PlanReserve) / people)
                + " survivalRoleShares=" + F(rows.Sum(o => o.OperatingFarmers) / people)
                    + "/" + F(rows.Sum(o => o.FoodGatherers) / people)
                    + "/" + F(rows.Sum(o => o.ExpansionFarmers) / people)
                + " herdBaseGrowthShare=" + F(rows.Sum(o => o.PlanningPopulation - o.GoodsBudget
                        - o.OperatingFarmers - o.FoodGatherers) / people)
                    + "/" + F(rows.Sum(o => o.HerdGrowthGranted) / people)
                + " continuityGrantPerResidentTurn="
                    + F(rows.Sum(o => o.FarmToolsGranted) / people) + "/"
                    + F(rows.Sum(o => o.HerdingToolsGranted) / people) + "/"
                    + F(rows.Sum(o => o.StorageGranted) / people)
                + " foodBucketPerResidentTurn=" + F(rows.Sum(o => o.ConsumptionIncomeTarget) / people)
                    + "/" + F(rows.Sum(o => o.ReserveFillTarget) / people)
                    + "/" + F(rows.Sum(o => o.MaximumGrowthFood) / people)
                + " foodGathererShareCurrentCurrentReserveCurrentGrowthAll="
                    + F(rows.Sum(o => o.ConsumptionOnlyFoodGatherers) / people)
                    + "/" + F(rows.Sum(o => o.ConsumptionReserveFoodGatherers) / people)
                    + "/" + F(rows.Sum(o => o.ConsumptionGrowthFoodGatherers) / people)
                    + "/" + F(rows.Sum(o => o.FoodGatherers) / people)
                + " fieldExpansionPerResidentTurn=" + F(requested / people)
                    + "/" + F(rows.Sum(o => o.FieldExpansionGranted) / people)
                + " fieldExpansionFulfilment=" + F(requested > 0 ? rows.Sum(o => o.FieldExpansionGranted) / requested : 1)
                + " fieldLadderGapGainPerResidentYear=" + F(rows.Sum(o => o.FieldExpansionGapPerYear) / people)
                    + "/" + F(rows.Sum(o => o.FieldExpansionGainPerYear) / people)
                + " fieldLadderStepsPerCampTurn=" + F(rows.Sum(o => o.FieldExpansionSteps) / (double)Math.Max(1, rows.Length))
                + " fieldLadderMinScore=" + F(rows.Where(o => o.FieldExpansionMinimumScore > 0)
                    .Select(o => o.FieldExpansionMinimumScore).DefaultIfEmpty(0).Min())
                + " meanFieldCapital=" + F(rows.Average(o => o.FieldCapital))
                + " goodsBudgetShare=" + F(rows.Sum(o => o.GoodsBudget) / people));
        }
    }

    private static void PrintRecoveryEffects(int followTurns, List<RecoveryObservation> observations,
        List<RecoveryRun> runs)
    {
        RecoveryObservation[] Native(string kind) => observations.Where(o => o.Variant == "isolated-native"
            && o.Kind == kind && o.Age == followTurns).ToArray();
        foreach (RecoveryVariant variant in RecoveryVariants.Where(v => v.Name.StartsWith("isolated-", StringComparison.Ordinal)
            && v.Name != "isolated-native"))
        foreach (string kind in new[] { "parent", "daughter" })
        {
            RecoveryObservation[] baseline = Native(kind);
            RecoveryObservation[] controlled = observations.Where(o => o.Variant == variant.Name
                && o.Kind == kind && o.Age == followTurns).ToArray();
            Check(baseline.Length == controlled.Length && baseline.Length > 0,
                variant.Name + " has a matched terminal " + kind + " sample");
            Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_EFFECT variant=" + variant.Name
                + " kind=" + kind + " age=" + followTurns
                + " deltaPopulation=" + F(controlled.Average(o => (double)o.Population) - baseline.Average(o => (double)o.Population))
                + " deltaFCH=" + F(controlled.Average(o => o.Food) - baseline.Average(o => o.Food)) + "/"
                    + F(controlled.Average(o => o.Clothing) - baseline.Average(o => o.Clothing)) + "/"
                    + F(controlled.Average(o => o.Housing) - baseline.Average(o => o.Housing))
                + " deltaQoL=" + F(controlled.Average(o => o.QoL) - baseline.Average(o => o.QoL))
                + " deltaShelterPerPerson=" + F(controlled.Sum(o => o.FixedShelter + o.LightShelter)
                    / Math.Max(1, controlled.Sum(o => o.Population))
                    - baseline.Sum(o => o.FixedShelter + o.LightShelter) / Math.Max(1, baseline.Sum(o => o.Population)))
                + " healthy=" + baseline.Count(o => o.Healthy) + "->" + controlled.Count(o => o.Healthy));
        }
        RecoveryRun[] recursive = runs.Where(r => r.Variant == "recursive-native").ToArray();
        RecoveryRun[] isolated = runs.Where(r => r.Variant == "isolated-native").ToArray();
        Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_RECURSION age=" + followTurns
            + " furtherFissions=" + recursive.Sum(r => r.FurtherFissions) + "->" + isolated.Sum(r => r.FurtherFissions)
            + " births=" + recursive.Sum(r => r.Births) + "->" + isolated.Sum(r => r.Births)
            + " deaths=" + recursive.Sum(r => r.Deaths) + "->" + isolated.Sum(r => r.Deaths)
            + " terminalPopulation=" + recursive.Sum(r => r.EndPopulation) + "->" + isolated.Sum(r => r.EndPopulation));
    }

    private static void PrintFabricFoodYieldSweep(int followTurns, List<RecoveryObservation> observations)
    {
        string[] variants = { "isolated-native", "isolated-fabric-food-yield-1.33x",
            "isolated-fabric-food-yield-2x", "isolated-fabric-food-yield-4x" };
        RecoveryObservation[] baseline = observations.Where(o => o.Variant == "isolated-native"
            && o.Age == followTurns).ToArray();
        RecoveryObservation[] baselineTrajectory = observations.Where(o => o.Variant == "isolated-native"
            && o.Age >= 1 && o.Age <= followTurns).ToArray();
        double baselinePopulation = baseline.Sum(o => Math.Max(0, o.Population));
        double baselinePopQoL = baseline.Sum(o => Math.Max(0, o.Population) * o.QoL);
        double baselineTrajectoryPopQoL = baselineTrajectory.Sum(o => Math.Max(0, o.Population) * o.QoL);
        foreach (string variant in variants)
        {
            RecoveryObservation[] terminal = observations.Where(o => o.Variant == variant
                && o.Age == followTurns).ToArray();
            RecoveryObservation[] trajectory = observations.Where(o => o.Variant == variant
                && o.Age >= 1 && o.Age <= followTurns).ToArray();
            RecoveryObservation[] late = observations.Where(o => o.Variant == variant
                && o.Age >= Math.Max(1, followTurns - 8) && o.Age <= followTurns).ToArray();
            Check(terminal.Length == baseline.Length && terminal.Length > 0,
                variant + " has the matched terminal fabric-food sample");
            double population = terminal.Sum(o => Math.Max(0, o.Population));
            double popQoL = terminal.Sum(o => Math.Max(0, o.Population) * o.QoL);
            double weightedQoL = popQoL / Math.Max(1, population);
            double trajectoryPopulation = trajectory.Sum(o => Math.Max(0, o.Population));
            double trajectoryPopQoL = trajectory.Sum(o => Math.Max(0, o.Population) * o.QoL);
            double clothing = terminal.Sum(o => Math.Max(0, o.Population) * o.Clothing)
                / Math.Max(1, population);
            double food = terminal.Sum(o => Math.Max(0, o.Population) * o.Food)
                / Math.Max(1, population);
            double latePeople = Math.Max(1, late.Sum(o => Math.Max(0, o.PlanningPopulation)));
            Console.WriteLine("DEMOGRAPHY_FISSION_RECOVERY_FABRIC_FOOD_SWEEP variant=" + variant
                + " age=" + followTurns + " camps=" + terminal.Length
                + " population=" + F(population)
                + " deltaPopulation=" + F(population - baselinePopulation)
                + " popQoL=" + F(popQoL)
                + " deltaPopQoL=" + F(popQoL - baselinePopQoL)
                + " populationWeightedQoL=" + F(weightedQoL)
                + " trajectoryPopQoL=" + F(trajectoryPopQoL)
                + " deltaTrajectoryPopQoL=" + F(trajectoryPopQoL - baselineTrajectoryPopQoL)
                + " trajectoryPopulationWeightedQoL=" + F(trajectoryPopQoL / Math.Max(1, trajectoryPopulation))
                + " populationWeightedFoodClothing=" + F(food) + "/" + F(clothing)
                + " lateClothOutputPerResidentTurn=" + F(late.Sum(o => o.ClothProduced) / latePeople)
                + " lateCurrentClothingRequestGrantPerResidentTurn="
                    + F(late.Sum(o => o.ClothingNowRequested) / latePeople) + "/"
                    + F(late.Sum(o => o.ClothingNowGranted) / latePeople)
                + " lateCurrentClothingPlantInputPerResidentTurn="
                    + F(late.Sum(o => o.PriorityClothingPlantInput) / latePeople)
                + " lateFieldExpansionRequestGrantPerResidentTurn="
                    + F(late.Sum(o => o.FieldExpansionRequested) / latePeople) + "/"
                    + F(late.Sum(o => o.FieldExpansionGranted) / latePeople)
                + " lateFoodExpansionWorkerShare="
                    + F(late.Sum(o => o.FoodGatherers) / latePeople) + "/"
                    + F(late.Sum(o => o.ExpansionFarmers) / latePeople)
                + " lateMeanFieldCapital=" + F(late.Average(o => o.FieldCapital))
                + " lateWeaverShare=" + F(late.Sum(o => o.PreparedWeavers) / latePeople)
                + " lateManagedAnimalForecastActual=" + F(late.Sum(o => o.ExpectedManagedAnimalFood))
                    + "/" + F(late.Sum(o => o.ActualManagedAnimalFood))
                + " healthy=" + terminal.Count(o => o.Healthy));
        }
    }
}
