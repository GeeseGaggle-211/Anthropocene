using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class ElderRLChecks
{
    private static int checks;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        IncludeFields = true
    };

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("ElderRL: " + message);
    }

    private static void Near(double actual, double expected, string message,
        double tolerance = 1e-8)
        => Check(Math.Abs(actual - expected) <= tolerance *
            Math.Max(1, Math.Abs(expected)), message + " (" + actual +
            " vs " + expected + ")");

    public static int Run()
    {
        checks = 0;
        Schema(); TechnologyFrontier(); ObservationBoundary();
        ObservationV2Semantics(); ActionMaskSemantics();
        MemoryAndPhysicalTime(); RewardContract();
        DirectiveCausality(); OptionAParity(); EnvironmentAndReplay();
        Console.WriteLine("PASS: " + checks + " Elder RL assertions");
        return checks;
    }

    private static WorldState Fixture()
    {
        var world = EconomicSettlementChecks.DiagnosticProductiveVillage(24);
        var camp = world.Camps[0];
        world.Config.FissionEnabled = false;
        world.Config.ImmigrationEnabled = false;
        world.Config.TradeEnabled = false;
        world.Config.AnnualHumanGrowthRate = 0;
        return world;
    }

    private static ElderCompiledDecision Compile(WorldState world,
        ElderDirective directive)
    {
        var camp = world.Camps.OrderBy(item => item.Id).First();
        var observation = new ElderObservationBuilder().Build(world, camp.Id,
            true);
        var context = new ElderCompilationContext(world, camp, camp, null,
            observation, camp.Tile, EconomicIntent.From(camp));
        return new NativeElderDirectiveCompiler().Compile(context, directive);
    }

    private static void Schema()
    {
        ElderObservationSchema.Validate(); checks++;
        var encoded = ElderObservationSchema.Encode(new ElderObservation());
        Check(encoded.Values.Length == 512 && encoded.Present.Length == 512,
            "the checked-in tensor registry is exactly 512 slots");
        Check(ElderObservationSchema.Registry.All(item =>
                !string.IsNullOrWhiteSpace(item.Source) &&
                !string.IsNullOrWhiteSpace(item.Units) &&
                !string.IsNullOrWhiteSpace(item.MissingValue) &&
                !string.IsNullOrWhiteSpace(item.Permission)),
            "every registry block declares source, units, missing semantics and permission");
        Check(typeof(ElderCoreObservation).GetField("Camp") == null &&
            typeof(ElderSiteObservation).GetField("Tile") == null &&
            typeof(ElderContactObservation).GetField("Society") == null,
            "raw camp, tile and society IDs stay out of policy-visible records");
        var observation = new ElderObservation();
        Check(!observation.Mask.AllowsMovement(0) &&
            observation.Mask.AllowsMovement(-1),
            "the default categorical mask permits only stay");

        observation.Mask.Movement = true;
        observation.Mask.LegalMovementCandidates = new[] { -1, 2, 5 };
        observation.Commodities = new[]
        {
            new ElderCommodityObservation
            {
                Key = ElderObservationSchema.CommodityRows[0],
                CanProduce = true, CanTrade = false
            },
            new ElderCommodityObservation
            {
                Key = ElderObservationSchema.CommodityRows[1],
                CanProduce = false, CanTrade = true
            }
        };
        encoded = ElderObservationSchema.Encode(observation);
        Check(encoded.Values[482] == 1 && encoded.Values[483] == 0 &&
            encoded.Values[485] == 1 && encoded.Values[488] == 1 &&
            encoded.Values[484] == 0 && encoded.Values[489] == 0,
            "the tensor carries the exact stay/site movement mask, not only its count");
        Check(encoded.Values[318] == 1 && encoded.Values[319] == 0 &&
            encoded.Values[326] == 0 && encoded.Values[327] == 1,
            "commodity rows expose exact production and trade capabilities");
        Check(ElderRLContract.ObservationSchemaVersion == 2 &&
            ElderRLContract.EnvironmentSchemaVersion == 2 &&
            ElderRLContract.ActionSchemaVersion == 1,
            "v2 observation/environment and v1 action versions are explicit");
        bool rejectedV1 = false;
        var stale = new ElderObservation { SchemaVersion = 1 };
        try { ElderObservationSchema.Encode(stale); }
        catch (ArgumentException) { rejectedV1 = true; }
        Check(rejectedV1,
            "the v2 encoder rejects a schema-v1 tensor instead of reinterpreting it");
        Check(ElderObservationSchema.CommodityRows.SequenceEqual(new[]
            {
                "HandTools", "Pots", "Cloth", "FarmTools", "Housing",
                "Tools", "AnimalFabric", "LightHousing", "HerdingTools",
                "HousingMaterial", "FishingTools"
            }),
            "commodity rows use a checked-in semantic key list, not enum order");
        Check(ElderObservationSchema.MemoryRows.Length ==
                ElderInstitutionMemory.SignalCount &&
            ElderObservationSchema.Registry.Any(item => item.Offset == 84 &&
                item.Width == 3 && item.Rows == 11),
            "the reserved core range carries the compact 11x3 trace bank");
    }

    private static void TechnologyFrontier()
    {
        var world = Fixture();
        var first = world.Camps[0];
        first.People.NeolithicTechnologies.RemoveAll(tech =>
            tech == NeolithicTech.Plough ||
            tech == NeolithicTech.Sailing ||
            tech == NeolithicTech.CopperWorking);
        world.Config.Neolithic.CultivationMarginalEffortKneeQ = .4;
        world.Config.Neolithic.FishingMarginalEffortKneeQ = .4;
        world.Config.Neolithic.ExperimentalCultivationEffortKneeOverride = false;
        world.Config.Neolithic.ExperimentalFishingEffortKneeOverride = false;

        var second = new Camp
        {
            Id = world.NextId++, Player = 1,
            Tile = world.Map.Neighbors(first.Tile).First(), Population = 80,
            PlantFood = 1000, WaterNeedsMetRatio = 1,
            FoodNeedsMetRatio = 1, QoL = 1
        };
        world.Camps.Add(second); Ownership.Bind(world);
        second.Technology = Technology.Cultivation;
        second.Technologies[(int)Technology.Cultivation] = true;
        second.Technologies[(int)Technology.Watercraft] = true;
        first.Technologies[(int)Technology.Watercraft] = true;
        second.People.NeolithicTechnologies.RemoveAll(tech =>
            tech == NeolithicTech.Plough ||
            tech == NeolithicTech.Sailing ||
            tech == NeolithicTech.CopperWorking);

        double firstE = world.Tiles.Where(tile =>
            tile.CultivationOperator == first.Id).Sum(tile =>
                tile.CultivationCapital);
        double fishE = world.FisherySites?.Sum(site => site.Capital) ?? 0;
        Near(SettlementProductionFrontier
                .EffectiveCultivationEffortKnee(world, first), .4,
            "primitive cultivation frontier begins at q=.4");
        Near(SettlementProductionFrontier
                .EffectiveCultivationEffortKnee(world, second), .4,
            "a second settlement independently begins at q=.4");

        first.People.NeolithicTechnologies.Add(NeolithicTech.Plough);
        Near(SettlementProductionFrontier
                .EffectiveCultivationEffortKnee(world, first),
            world.Config.Neolithic.PloughSupportedIntensity,
            "Plough raises only the capable settlement's cultivation frontier");
        Near(SettlementProductionFrontier
                .EffectiveCultivationEffortKnee(world, second), .4,
            "one settlement's capability does not alter another settlement");
        Near(world.Tiles.Where(tile => tile.CultivationOperator == first.Id)
                .Sum(tile => tile.CultivationCapital), firstE,
            "frontier resolution does not mutate stored field E");
        Near(world.FisherySites?.Sum(site => site.Capital) ?? 0, fishE,
            "frontier resolution does not mutate stored fishery E");

        world.Config.Neolithic.SailingFishingSupportedIntensity = .55;
        world.Config.Neolithic.CopperFishingSupportedIntensity = .65;
        first.People.NeolithicTechnologies.Add(NeolithicTech.Sailing);
        Near(SettlementProductionFrontier
                .EffectiveFishingEffortKnee(world, first), .55,
            "Sailing uses the independent fishing calibration seam");
        Near(SettlementProductionFrontier
                .EffectiveFishingEffortKnee(world, second), .4,
            "fishing frontiers remain settlement-local");
        first.People.NeolithicTechnologies.Add(NeolithicTech.CopperWorking);
        Near(SettlementProductionFrontier
                .EffectiveFishingEffortKnee(world, first), .65,
            "Copper fishing efficacy does not borrow agricultural calibration");
        first.People.NeolithicTechnologies.Remove(NeolithicTech.CopperWorking);

        world.Config.Neolithic.CultivationMarginalEffortKneeQ = .8;
        world.Config.Neolithic.FishingMarginalEffortKneeQ = .8;
        Near(SettlementProductionFrontier
                .EffectiveCultivationEffortKnee(world, first),
            world.Config.Neolithic.PloughSupportedIntensity,
            "an ungated experimental value cannot silently become the production frontier");
        Near(SettlementProductionFrontier
                .EffectiveFishingEffortKnee(world, second), .4,
            "ordinary fishing production remains local while its override gate is off");
        world.Config.Neolithic.ExperimentalCultivationEffortKneeOverride = true;
        world.Config.Neolithic.ExperimentalFishingEffortKneeOverride = true;
        Near(SettlementProductionFrontier
                .EffectiveCultivationEffortKnee(world, first), .8,
            "the controlled-sweep cultivation override remains available");
        Near(SettlementProductionFrontier
                .EffectiveCultivationEffortKnee(world, second), .8,
            "the controlled-sweep override can force a matched global regime");
        Near(SettlementProductionFrontier
                .EffectiveFishingEffortKnee(world, second), .8,
            "the fishing override is independently forceable");

        world.Config.Neolithic.ExperimentalCultivationEffortKneeOverride = false;
        world.Config.Neolithic.ExperimentalFishingEffortKneeOverride = false;
        world.Config.Neolithic.CultivationMarginalEffortKneeQ = .4;
        world.Config.Neolithic.FishingMarginalEffortKneeQ = .4;
        foreach (int tile in CultivationEconomy.Footprint(world, first))
        {
            world.Tiles[tile].CultivationOperator = first.Id;
            world.Tiles[tile].CultivationCapital = 4;
        }
        first.Inventory.FarmTools = 0;
        double required = CultivationEconomy.MaintenanceWorkerRequirement(
            world, first);
        var labor = CultivationEconomy.CalculateLabor(world, first,
            CultivationEconomy.Footprint(world, first), null,
            world.Config.TurnDays / world.Config.DaysPerYear);
        Near(labor.RequiredMaintenance, required,
            "resolver labor and planner maintenance use the identical local frontier");

        var transition = Fixture(); var transitioning = transition.Camps[0];
        transitioning.People.NeolithicTechnologies.RemoveAll(tech =>
            tech == NeolithicTech.Plough ||
            tech == NeolithicTech.CopperWorking);
        transition.Config.Neolithic.ExperimentalCultivationEffortKneeOverride =
            false;
        ElderInstitutionMemorySystem.EnsureInitialized(transition,
            transitioning);
        transitioning.People.NeolithicTechnologies.Add(NeolithicTech.Plough);
        double startDays = transition.Days;
        transition.Days += transition.Config.TurnDays;
        ElderInstitutionMemorySystem.ObserveBoundary(transition,
            transitioning, new TurnRecord
            {
                StartDays = startDays,
                Duration = transition.Config.TurnDays
            });
        var sidecar = ElderTrainingCriticSidecarBuilder.Build(transition,
            transitioning);
        Check(sidecar.CultivationRegimeChanged &&
            sidecar.HasCultivationRegimeChange &&
            sidecar.CultivationRegimeChangeDays == transition.Days,
            "the training-only sidecar records a physical-time local regime transition");
    }

    private static void ObservationBoundary()
    {
        var world = Fixture(); var camp = world.Camps[0];
        string before = SaveCodec.Fingerprint(world);
        var builder = new ElderObservationBuilder();
        var first = builder.Build(world, camp.Id, true, out var metadata);
        var again = builder.Build(world, camp.Id, true, out var againMetadata);
        Check(SaveCodec.Fingerprint(world) == before,
            "observation construction is pure");
        Check(JsonSerializer.Serialize(first, Json) ==
                JsonSerializer.Serialize(again, Json) &&
            metadata.SiteTiles.SequenceEqual(againMetadata.SiteTiles),
            "the same authorized state yields the same observation and candidate ordering");
        Check(first.Sites.Length <= ElderRLContract.MaxLocalSites &&
            first.Fishing.Length <= ElderRLContract.MaxFishingGrounds &&
            first.HerdGroups.Length <= ElderRLContract.MaxHerdGroups &&
            first.Contacts.Length <= ElderRLContract.MaxKnownContacts,
            "all set-valued policy blocks obey encoding caps");
        Check(first.Presence.Sites.Present == first.Sites.Length &&
            first.Presence.Herds.Present == first.HerdGroups.Length,
            "presence summaries describe encoded records");
        Check(metadata.ControlledCampId == camp.Id &&
            metadata.SiteTiles.Length == first.Sites.Length,
            "raw compilation identities are present only in metadata");
        var tensor = ElderObservationSchema.Encode(first);
        Check(tensor.Present[64] && tensor.Present[80] &&
            tensor.Values[80] == 1 && tensor.Values[81] == 1 &&
            tensor.Values[82] == 1 && tensor.Values[83] == 1,
            "the PPO tensor carries legal capabilities and every continuous action mask");
        Check(first.Capabilities.Select(item => item.Key).SequenceEqual(
                ElderObservationSchema.CapabilityRows) &&
            first.Commodities.Select(item => item.Key).SequenceEqual(
                ElderObservationSchema.CommodityRows) &&
            !tensor.Present[41],
            "checked-in row keys and missing previous-decision presence are stable");

        var foreign = new Camp
        {
            Id = world.NextId++, Player = 1,
            Tile = world.Map.At(18, 12), Population = 400,
            PlantFood = 123456, AnimalFood = 789
        };
        world.Camps.Add(foreign); Ownership.Bind(world);
        var hiddenA = builder.Build(world, camp.Id, true);
        foreign.Population = 2; foreign.PlantFood = 0;
        foreign.Inventory.Tools = 999999;
        var hiddenB = builder.Build(world, camp.Id, true);
        Check(JsonSerializer.Serialize(hiddenA, Json) ==
                JsonSerializer.Serialize(hiddenB, Json),
            "unseen foreign population, stocks and tools cannot leak into the Elder observation");

        var orderingWorld = Fixture(); var orderingCamp = orderingWorld.Camps[0];
        orderingWorld.Herds.Add(new Herd
        {
            Id = orderingWorld.NextId++, Tile = orderingCamp.Tile,
            DomesticCamp = orderingCamp.Id,
            OwnerSociety = orderingCamp.SocietyId,
            Species = orderingWorld.Herds[0].Species, Animals = 3,
            Capacity = 1000, LastNutrition = .4,
            ManagementFulfillment = .6, StartingAnimals = 4,
            NewbornAnimals = 1, StarvationDeaths = 1
        });
        var orderedA = ElderObservationSchema.Encode(builder.Build(
            orderingWorld, orderingCamp.Id, true));
        var owned = orderingWorld.Herds.Where(herd =>
                herd.DomesticCamp == orderingCamp.Id).ToArray();
        int firstId = owned[0].Id; owned[0].Id = owned[1].Id;
        owned[1].Id = firstId;
        var orderedB = ElderObservationSchema.Encode(builder.Build(
            orderingWorld, orderingCamp.Id, true));
        Check(orderedA.Values.Skip(248).Take(64).SequenceEqual(
                orderedB.Values.Skip(248).Take(64)) &&
            orderedA.Present.Skip(248).Take(64).SequenceEqual(
                orderedB.Present.Skip(248).Take(64)),
            "owned-herd tensor ordering is invariant to raw entity-ID reassignment");
    }

    private static void ObservationV2Semantics()
    {
        var world = Fixture();
        var camp = world.Camps[0];
        var builder = new ElderObservationBuilder();
        camp.People.NeolithicTechnologies.RemoveAll(tech =>
            tech == NeolithicTech.Plough || tech == NeolithicTech.Sailing ||
            tech == NeolithicTech.CopperWorking);
        world.Config.Neolithic.CultivationMarginalEffortKneeQ = .4;
        world.Config.Neolithic.FishingMarginalEffortKneeQ = .4;
        world.Config.Neolithic.ExperimentalCultivationEffortKneeOverride = false;
        world.Config.Neolithic.ExperimentalFishingEffortKneeOverride = false;
        // Keep the observation's maintenance requirement below the resident
        // workforce cap so this test measures the effort curve itself.
        camp.Population = 10000;
        camp.Inventory.FarmTools = 0;

        var retained = CultivationEconomy.Footprint(world, camp)
            .Where(tile => world.Tiles[tile].TraversableLand).ToArray();
        foreach (int tile in retained)
        {
            world.Tiles[tile].CultivationOperator = camp.Id;
            world.Tiles[tile].CultivationCapital = .25;
        }
        var low = builder.Build(world, camp.Id, false);
        foreach (int tile in retained)
            world.Tiles[tile].CultivationCapital = Math.Pow(.45 /
                world.Config.ReferenceIntensity, 2);
        var high = builder.Build(world, camp.Id, false);
        Check(high.Fields.CurrentIntensity > low.Fields.CurrentIntensity + .2,
            "a physical field-capital change appears as current cultivation intensity in the next observation");
        var highTensor = ElderObservationSchema.Encode(high);
        Near(highTensor.Values[23], high.Fields.CurrentIntensity,
            "slot 23 carries deterministic mean retained-field q", 1e-6);
        double storedFieldE = retained.Sum(tile =>
            world.Tiles[tile].CultivationCapital);
        double primitiveMaintenance = high.Fields.MaintenanceWorkerYears;
        camp.People.NeolithicTechnologies.Add(NeolithicTech.Plough);
        var plough = builder.Build(world, camp.Id, false);
        Check(plough.Fields.MaintenanceWorkerYears < primitiveMaintenance,
            "an applicable local frontier improvement lowers observed current field maintenance (" +
                primitiveMaintenance + " -> " +
                plough.Fields.MaintenanceWorkerYears + ", qk=" +
                SettlementProductionFrontier
                    .EffectiveCultivationEffortKnee(world, camp) + ")");
        Near(retained.Sum(tile => world.Tiles[tile].CultivationCapital),
            storedFieldE,
            "observing a frontier change neither grants nor refunds field capital");

        int ground = world.Map.Neighbors(camp.Tile).OrderBy(tile => tile)
            .First();
        world.Tiles[ground].Terrain = Terrain.Shallows;
        world.Config.NeolithicFishingEnabled = true;
        camp.Technologies[(int)Technology.Watercraft] = true;
        camp.LastSelectedFishingGrounds.Clear();
        camp.LastSelectedFishingGrounds.Add(ground);
        world.FisherySites.RemoveAll(site => site.TileId == ground);
        world.FisherySites.Add(new FisherySite
        {
            TileId = ground, OperatorCampId = camp.Id,
            Capital = 1, LastUpdatedDays = world.Days
        });
        camp.People.FishingGroundKnowledge.RemoveAll(snapshot =>
            snapshot.TileId == ground);
        camp.People.FishingGroundKnowledge.Add(new FishingGroundSnapshot
        {
            TileId = ground, OperatorCampId = camp.Id,
            AccessibleFood = 100, Biomass = 200, Condition = .8,
            Capital = 1, ObservedDays = world.Days,
            OperationFraction = .6,
            ProductiveProfile = new[] { 1.0, 1.0, 1.0, 1.0 }
        });
        var fishingLow = builder.Build(world, camp.Id, false,
            out var fishingMetadata);
        int row = Array.IndexOf(fishingMetadata.FishingTiles, ground);
        Check(row >= 0 && fishingLow.Fishing[row].MaintenancePresent &&
            fishingLow.Fishing[row].MaintenanceWorkerYears > 0,
            "current own-fishery maintenance is non-placeholder and present");
        Near(fishingLow.Fishing[row].OperationFraction, .6,
            "the own-fishery row exposes realized retained operation rather than a forecast or coarse band");
        double expectedFishingMaintenance =
            world.Config.Neolithic.MaintenanceWorkersPerTileE(
                world.Config.MaintenanceCoefficient) *
            FishingEconomy.EffortForCapital(world, camp, 1) *
            world.Config.TurnDays / world.Config.DaysPerYear;
        Near(fishingLow.Fishing[row].MaintenanceWorkerYears,
            expectedFishingMaintenance,
            "fishing observation and production use the same local maintenance equation");
        world.FisherySites.Single(site => site.TileId == ground).Capital = 4;
        var fishingHigh = builder.Build(world, camp.Id, false,
            out fishingMetadata);
        row = Array.IndexOf(fishingMetadata.FishingTiles, ground);
        Check(fishingHigh.Fishing[row].MaintenanceWorkerYears >
                fishingLow.Fishing[row].MaintenanceWorkerYears,
            "observed fishing maintenance responds causally to current stored E");
        double storedFishE = world.FisherySites.Single(site =>
            site.TileId == ground).Capital;
        camp.People.NeolithicTechnologies.Add(NeolithicTech.Sailing);
        var sailing = builder.Build(world, camp.Id, false,
            out fishingMetadata);
        row = Array.IndexOf(fishingMetadata.FishingTiles, ground);
        // Placeholder until a production fishing q_k is calibrated: Sailing
        // is a legal capability seam, but its shipped frontier remains .40.
        Near(sailing.Fishing[row].MaintenanceWorkerYears,
            fishingHigh.Fishing[row].MaintenanceWorkerYears,
            "uncalibrated Sailing does not invent a fishing maintenance improvement");
        Near(world.FisherySites.Single(site => site.TileId == ground).Capital,
            storedFishE,
            "an uncalibrated fishing capability change leaves stored physical capital unchanged");

        Check(typeof(ElderFishingObservation).GetField("BuildWorkerYears") ==
                null &&
            typeof(ElderCommodityObservation).GetField("MarginalValue") ==
                null &&
            typeof(ElderCommodityObservation).GetField(
                "ExternalOpportunity") == null,
            "v2 omits predecision build forecasts and planner strategic scores");
        var actorTypes = new[]
        {
            typeof(ElderObservation), typeof(ElderCoreObservation),
            typeof(ElderFieldObservation), typeof(ElderFishingObservation),
            typeof(ElderMemoryObservation)
        };
        Check(actorTypes.SelectMany(type => type.GetFields()).All(field =>
                field.Name.IndexOf("Knee", StringComparison.OrdinalIgnoreCase) < 0 &&
                field.FieldType != typeof(ElderTrainingCriticSidecar)) &&
            ElderObservationSchema.Registry.All(block =>
                block.Key.IndexOf("knee", StringComparison.OrdinalIgnoreCase) < 0),
            "q_k and the training-only critic sidecar are structurally absent from the actor observation");

        var semantic = Fixture(); var semanticCamp = semantic.Camps[0];
        int otherCamp = semantic.NextId++;
        int otherTile = semantic.Map.Neighbors(semanticCamp.Tile)
            .OrderBy(tile => tile).First();
        var society = semantic.Societies.Single(item =>
            item.Id == semanticCamp.SocietyId);
        ContactKnowledge.MemoryOf(semantic, society).Upsert(otherCamp, 99,
            otherTile, semantic.Turn, semantic.Days -
                4 * semantic.Config.DaysPerYear, ContactSource.Trade);
        double duration = 2 * semantic.Config.DaysPerYear;
        var record = new TurnRecord
        {
            Turn = semantic.Turn, StartDays = semantic.Days - duration,
            Duration = duration
        };
        record.Demographics.Add(new DemographicReport
        {
            Camp = semanticCamp.Id,
            StartingPopulation = semanticCamp.Population
        });
        record.Migrations.Add(new MigrationReport
        {
            SourceCamp = semanticCamp.Id, DestinationCamp = otherCamp,
            Migrants = 5
        });
        record.Trades.Add(new TradeRecord
        {
            Requester = semanticCamp.Id, Responder = otherCamp,
            WantQuantity = 3, GiveQuantity = 2,
            RequesterGain = 999, Delivered = .01,
            RequesterRealizedUtility = 7
        });
        record.Fissions.Add(new FissionReport
        {
            ParentCamp = semanticCamp.Id, Daughter = 12,
            Push = .6, DaughterGain = 1e9,
            ExitOpportunityPressure = .35
        });
        semantic.History.Add(record);
        var approved = builder.Build(semantic, semanticCamp.Id, false);
        double scale = Math.Max(1, semanticCamp.Population) * 2.0;
        Near(approved.Core.RealizedMigrationPerCapitaYear, -5 / scale,
            "migration is signed realized flow per population-year");
        Near(approved.Core.RealizedTradeUtilityPerCapitaYear, 7 / scale,
            "trade uses physically realized local utility, not planned gain times delivery");
        Near(approved.Fission.ExitOpportunityPressure, .35,
            "fission exposes bounded realized exit-opportunity pressure rather than DaughterGain sentinel semantics");
        Check(approved.Contacts.Length == 1,
            "a permission-safe remembered contact is encoded");
        Near(approved.Contacts[0].AgeYears, 4,
            "contact age is a physical duration");
        Near(approved.Contacts[0].RecentMigrationPerCapitaYear, -5 / scale,
            "contact migration is signed and normalized by physical time");
        Near(approved.Contacts[0].RecentTradeUtilityPerCapitaYear, 7 / scale,
            "contact trade utility is the actual settled common-unit flow");
    }

    private static void ActionMaskSemantics()
    {
        var fieldWorld = Fixture();
        var fieldCamp = fieldWorld.Camps[0];
        fieldCamp.Technology = Technology.Cultivation;
        fieldCamp.Technologies[(int)Technology.Cultivation] = true;
        fieldCamp.Technologies[(int)Technology.AnimalHusbandry] = false;
        fieldWorld.Herds.RemoveAll(herd => herd.DomesticCamp == fieldCamp.Id);
        var builder = new ElderObservationBuilder();
        var fieldOnly = builder.Build(fieldWorld, fieldCamp.Id, false);
        Check(fieldOnly.Mask.FieldDevelopment &&
            !fieldOnly.Mask.HerdDevelopment &&
            fieldOnly.Mask.AllowsDevelopment() &&
            !fieldOnly.Mask.AllowsDevelopmentSplit(),
            "a cultivation-only settlement exposes field but not herd Development");
        var fieldTensor = ElderObservationSchema.Encode(fieldOnly);
        Check(fieldTensor.Values[82] == 1 && fieldTensor.Values[83] == 0,
            "tensor masks encode primitive field/herd Development availability");
        var fieldDecision = Compile(fieldWorld, new ElderDirective
        {
            RetainedFieldOperation = 1,
            RetainedHerdFraction = 1,
            DevelopmentBudget = .6,
            FieldDevelopmentShare = 0,
            MovementCandidate = -1
        });
        Near(fieldDecision.Accepted.RetainedHerdFraction, 0,
            "unavailable herd retention is canonically zero");
        Near(fieldDecision.Accepted.FieldDevelopmentShare, 1,
            "cultivation-only Development is canonically field-directed");
        Check(fieldDecision.Interventions.Any(item =>
                item.Control == "field-development-share"),
            "native compilation records structural split canonicalization");

        var herdWorld = Fixture();
        var herdCamp = herdWorld.Camps[0];
        herdCamp.Technology = Technology.AnimalHusbandry;
        herdCamp.Technologies[(int)Technology.Cultivation] = false;
        herdCamp.Technologies[(int)Technology.AnimalHusbandry] = true;
        foreach (int tile in CultivationEconomy.Footprint(herdWorld, herdCamp))
        {
            if (herdWorld.Tiles[tile].CultivationOperator == herdCamp.Id)
            {
                herdWorld.Tiles[tile].CultivationCapital = 0;
                herdWorld.Tiles[tile].CultivationOperator = -1;
            }
        }
        var herdOnly = builder.Build(herdWorld, herdCamp.Id, false);
        Check(!herdOnly.Mask.FieldDevelopment &&
            herdOnly.Mask.HerdDevelopment &&
            herdOnly.Mask.AllowsDevelopment() &&
            !herdOnly.Mask.AllowsDevelopmentSplit(),
            "a herd-only settlement exposes herd but not field Development");
        var herdDecision = Compile(herdWorld, new ElderDirective
        {
            RetainedFieldOperation = 1,
            RetainedHerdFraction = 1,
            DevelopmentBudget = .6,
            FieldDevelopmentShare = 1,
            MovementCandidate = -1
        });
        Near(herdDecision.Accepted.RetainedFieldOperation, 0,
            "unavailable field retention is canonically zero");
        Near(herdDecision.Accepted.FieldDevelopmentShare, 0,
            "herd-only Development is canonically herd-directed");

        var neitherWorld = Fixture();
        var neitherCamp = neitherWorld.Camps[0];
        neitherCamp.Technology = Technology.None;
        neitherCamp.Technologies[(int)Technology.Cultivation] = false;
        neitherCamp.Technologies[(int)Technology.AnimalHusbandry] = false;
        neitherWorld.Herds.RemoveAll(herd => herd.DomesticCamp == neitherCamp.Id);
        var neither = builder.Build(neitherWorld, neitherCamp.Id, false);
        Check(!neither.Mask.AllowsDevelopment(),
            "a settlement with neither capability has no Development channel");
        var neitherDecision = Compile(neitherWorld, new ElderDirective
        {
            DevelopmentBudget = .8,
            FieldDevelopmentShare = 1,
            MovementCandidate = -1
        });
        Near(neitherDecision.Accepted.DevelopmentBudget, 0,
            "Development budget is canonically zero when no channel exists");
        Near(neitherDecision.Accepted.FieldDevelopmentShare, .5,
            "inactive Development split uses a deterministic neutral value");
    }

    private static void MemoryAndPhysicalTime()
    {
        double daysPerYear = 360;
        double[] zero = Enumerable.Repeat(0.0,
            ElderInstitutionMemory.SignalCount).ToArray();
        double[] one = Enumerable.Repeat(1.0,
            ElderInstitutionMemory.SignalCount).ToArray();
        var oneStep = new ElderInstitutionMemory();
        oneStep.Observe(0, daysPerYear, zero);
        oneStep.Observe(0, daysPerYear, one);
        oneStep.AdvanceTo(20 * daysPerYear, daysPerYear);
        var monthly = new ElderInstitutionMemory();
        monthly.Observe(0, daysPerYear, zero);
        monthly.Observe(0, daysPerYear, one);
        for (int month = 1; month <= 240; month++)
            monthly.AdvanceTo(month * daysPerYear / 12.0, daysPerYear);
        for (int horizon = 0; horizon < 3; horizon++)
            Near(monthly.Mean(ElderMemorySignal.QoL, horizon),
                oneStep.Mean(ElderMemorySignal.QoL, horizon),
                "one 20-year EMA integration equals 240 monthly integrations",
                2e-12);

        var pulseCoarse = new ElderInstitutionMemory();
        pulseCoarse.Observe(0, daysPerYear, zero);
        pulseCoarse.Observe(0, daysPerYear, one);
        pulseCoarse.Observe(7 * daysPerYear, daysPerYear, zero);
        pulseCoarse.AdvanceTo(20 * daysPerYear, daysPerYear);
        var pulseFine = new ElderInstitutionMemory();
        pulseFine.Observe(0, daysPerYear, zero);
        pulseFine.Observe(0, daysPerYear, one);
        for (int month = 1; month <= 84; month++)
            pulseFine.AdvanceTo(month * daysPerYear / 12.0, daysPerYear);
        pulseFine.Observe(7 * daysPerYear, daysPerYear, zero);
        for (int month = 85; month <= 240; month++)
            pulseFine.AdvanceTo(month * daysPerYear / 12.0, daysPerYear);
        for (int horizon = 0; horizon < 3; horizon++)
            Near(pulseFine.Mean(ElderMemorySignal.QoL, horizon),
                pulseCoarse.Mean(ElderMemorySignal.QoL, horizon),
                "event-separated pulse integration obeys the EMA semigroup",
                2e-12);

        var world = Fixture(); var camp = world.Camps[0];
        var memory = ElderInstitutionMemorySystem.EnsureInitialized(world,
            camp);
        double initialized = memory.InitializedDays;
        world.Days += 10 * world.Config.DaysPerYear;
        memory.AdvanceTo(world.Days, world.Config.DaysPerYear);
        memory.HasLastDecision = true;
        memory.LastRequested = new ElderDirective
        {
            DevelopmentBudget = .7, FieldDevelopmentShare = .2
        };
        memory.LastAccepted = memory.LastRequested.Copy();
        memory.LastRealized = memory.LastRequested.Copy();
        memory.LastDecisionElapsedYears = 10;
        double maturity = memory.Maturity(1, world.Days,
            world.Config.DaysPerYear);
        string memoryFingerprint = SaveCodec.Fingerprint(world);
        var loaded = SaveCodec.Load(SaveCodec.Save(world));
        var loadedMemory = loaded.Camps.Single(item => item.Id == camp.Id)
            .ElderMemory;
        Check(loadedMemory != null && loadedMemory.Initialized &&
            loadedMemory.HasLastDecision,
            "institutional memory and prior-action evidence survive SaveCodec roundtrip");
        Near(loadedMemory.InitializedDays, initialized,
            "the memory physical initialization timestamp survives snapshots");
        Near(loadedMemory.Maturity(1, loaded.Days,
                loaded.Config.DaysPerYear), maturity,
            "trace maturity survives snapshots");
        Near(loadedMemory.Mean(ElderMemorySignal.QoL, 0),
            memory.Mean(ElderMemorySignal.QoL, 0),
            "EMA state survives snapshots");
        Check(SaveCodec.Fingerprint(loaded) == memoryFingerprint,
            "institutional memory survives a fingerprint-identical SaveCodec roundtrip");

        for (int i = 0; i < 2000; i++)
            loaded.History.Insert(0, new TurnRecord());
        var builder = new ElderObservationBuilder();
        builder.Build(loaded, camp.Id, false);
        Check(builder.HistoryRecordsInspected <= 1,
            "observation construction reads at most the latest history record regardless of total history length");
    }

    private static void RewardContract()
    {
        var ten = ElderRewardContract.Physical(100, .8, 10);
        var twenty = ElderRewardContract.Physical(100, .8, 20);
        Near(ten.WelfareRate, 80,
            "physical reward exposes prior-population QoL welfare rate");
        Near(twenty.UndiscountedIntervalWelfare,
            2 * ten.UndiscountedIntervalWelfare,
            "interval welfare scales with physical dt");
        var firstHalf = ElderRewardContract.Physical(100, .8, 10);
        var secondHalf = ElderRewardContract.Physical(100, .8, 10);
        Near(firstHalf.UndiscountedIntervalWelfare +
                secondHalf.UndiscountedIntervalWelfare,
            twenty.UndiscountedIntervalWelfare,
            "splitting a constant 20-year interval into two 10-year intervals preserves undiscounted welfare");
        double objective = twenty.UndiscountedIntervalWelfare;
        twenty.FoodSecurity = 0; twenty.WaterSecurity = 1;
        twenty.CapitalContinuity = .123; twenty.CatastrophicLoss = 1;
        Near(twenty.UndiscountedIntervalWelfare, objective,
            "diagnostic sidecars cannot change the scalar welfare objective");
        Check(typeof(ElderRewardContract).GetMethods().Where(method =>
                method.Name == nameof(ElderRewardContract.Physical))
            .All(method => method.GetParameters().All(parameter =>
                parameter.Name.IndexOf("initial", StringComparison.OrdinalIgnoreCase) < 0)),
            "the reward contract has no episode-initial-population input");
    }

    private static void DirectiveCausality()
    {
        var fieldWorld = Fixture();
        var noField = Compile(fieldWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 1,
            DevelopmentBudget = 0
        });
        var fullField = Compile(fieldWorld, new ElderDirective
        {
            RetainedFieldOperation = 1, RetainedHerdFraction = 1,
            DevelopmentBudget = 0
        });
        Check(fullField.Realized.RetainedFieldOperation >
                noField.Realized.RetainedFieldOperation + 1e-9,
            "field-retention head actuates a real retained-operation choice");

        var herdWorld = Fixture();
        var noHerd = Compile(herdWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 0,
            DevelopmentBudget = 0
        });
        var fullHerd = Compile(herdWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 1,
            DevelopmentBudget = 0
        });
        Check(fullHerd.Realized.RetainedHerdFraction >
                noHerd.Realized.RetainedHerdFraction + 1e-9 &&
            fullHerd.Intent.HerdTarget > noHerd.Intent.HerdTarget,
            "herd-retention head actuates the authoritative herd target");

        var developmentWorld = Fixture();
        var none = Compile(developmentWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 0,
            DevelopmentBudget = 0, FieldDevelopmentShare = 1
        });
        var admitted = Compile(developmentWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 0,
            DevelopmentBudget = 1, FieldDevelopmentShare = 1
        });
        Check(admitted.AcceptedDevelopmentPeople >
                none.AcceptedDevelopmentPeople + 1e-9,
            "development-budget head admits real compiled Development when feasible (" +
                none.AcceptedDevelopmentPeople + " -> " +
                admitted.AcceptedDevelopmentPeople + ", requested=" +
                admitted.RequestedDevelopmentPeople + ")");

        var gatedWorld = EconomicSettlementChecks.DiagnosticMobileMixed(
            cultivation: true, productive: false);
        var gatedCamp = gatedWorld.Camps[0];
        DemandController.Plan(gatedWorld, gatedCamp,
            EconomicIntent.From(gatedCamp), null, out _, out var gatedBaseline);
        var physicalField = Compile(gatedWorld, new ElderDirective
        {
            RetainedFieldOperation = 0,
            RetainedHerdFraction = 1,
            DevelopmentBudget = .5,
            FieldDevelopmentShare = 1
        });
        Check(gatedBaseline.SelectedFieldBudget <= 1e-9 &&
            physicalField.AcceptedFieldDevelopmentPeople > 1e-9,
            "learned field Development selects physical options without inheriting Option A profitability gates");

        var competitionWorld = Fixture();
        competitionWorld.Config.AnnualHumanGrowthRate = .05;
        var competitionCamp = competitionWorld.Camps[0];
        DemandController.Plan(competitionWorld, competitionCamp,
            EconomicIntent.From(competitionCamp), null, out _,
            out var competitionAllocation,
            elderDirective: new ElderDirective
            {
                RetainedFieldOperation = 0,
                RetainedHerdFraction = 1,
                DevelopmentBudget = .5,
                FieldDevelopmentShare = 1
            }, elderInterventions: new List<ElderIntervention>());
        var competitionGrowth = competitionAllocation.Requests
            .FirstOrDefault(request => request.Service ==
                "food growth gathering");
        Check(competitionGrowth != null && competitionGrowth.Units > 1 &&
            competitionGrowth.Granted > 0 &&
            competitionAllocation.SelectedFieldBudget > 1 &&
            Math.Abs(competitionAllocation.FinalFieldBudget -
                competitionAllocation.SelectedFieldBudget) <= 1e-8,
            "a selected learned field prefix is reserved before competing food-for-growth gathering uses residual labor");

        var exactWorld = Fixture();
        // The compiler's precision, not the herd transition limit (which would keep half of the herd), is under test.
        exactWorld.Config.HerdCullLimitPerTurn = 1;
        var exact = Compile(exactWorld, new ElderDirective
        {
            RetainedFieldOperation = .37,
            RetainedHerdFraction = .37,
            DevelopmentBudget = 0
        });
        Check(Math.Abs(exact.Accepted.RetainedFieldOperation - .37) < .08 &&
            Math.Abs(exact.Accepted.RetainedHerdFraction - .37) < .08,
            "retention targets use adjacent physical/integer options instead of the coarse Option A search grid");

        var splitWorld = Fixture();
        var herdSplit = Compile(splitWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 1,
            DevelopmentBudget = 1, FieldDevelopmentShare = 0
        });
        var fieldSplit = Compile(splitWorld, new ElderDirective
        {
            RetainedFieldOperation = 0, RetainedHerdFraction = 1,
            DevelopmentBudget = 1, FieldDevelopmentShare = 1
        });
        Check(fieldSplit.AcceptedFieldDevelopmentPeople >
                herdSplit.AcceptedFieldDevelopmentPeople + 1e-9 &&
            herdSplit.AcceptedHerdDevelopmentPeople >
                fieldSplit.AcceptedHerdDevelopmentPeople + 1e-9,
            "development-split head routes the common budget between physical channels (herd split=" +
                herdSplit.AcceptedFieldDevelopmentPeople + "/" +
                herdSplit.AcceptedHerdDevelopmentPeople + ", field split=" +
                fieldSplit.AcceptedFieldDevelopmentPeople + "/" +
                fieldSplit.AcceptedHerdDevelopmentPeople + ")");

        var invalid = Compile(Fixture(), new ElderDirective
        {
            MovementCandidate = 999,
            RetainedFieldOperation = double.NaN,
            RetainedHerdFraction = 2
        });
        Check(invalid.Accepted.MovementCandidate == -1 &&
            invalid.Interventions.Any(item => item.Kind ==
                ElderInterventionKind.InvalidActionFallback &&
                item.Control == "movement-candidate"),
            "an invalid categorical action explicitly falls back to stay");
        Check(invalid.Interventions.Any(item => item.Control ==
                "retained-field-operation") &&
            invalid.Interventions.Any(item => item.Control ==
                "retained-herd-fraction"),
            "continuous defensive clipping is explicit");
    }

    private static void OptionAParity()
    {
        foreach (var fixture in new[]
        {
            ("A", EconomicSettlementChecks.DiagnosticVillage(0,
                husbandry: false, housing: 15)),
            ("B", EconomicSettlementChecks.DiagnosticVillage(8)),
            ("C", EconomicSettlementChecks.DiagnosticVillage(400)),
            ("D", EconomicSettlementChecks.DiagnosticVillage(4000)),
            ("E", EconomicSettlementChecks.DiagnosticPastoralOpportunity(
                Technology.AnimalHusbandry)),
            ("F", EconomicSettlementChecks.DiagnosticVillage(80,
                primary: Technology.Cultivation)),
            ("G", EconomicSettlementChecks.DiagnosticPastoralOpportunity(
                Technology.Cultivation)),
            ("H", EconomicSettlementChecks.DiagnosticVillage(0,
                husbandry: true, housing: 15)),
            ("I", EconomicSettlementChecks.DiagnosticProductiveVillage(24))
        })
        {
            var world = fixture.Item2;
            var camp = world.Camps[0]; var settings = EconomicIntent.From(camp);
            var baseline = DemandController.Plan(world, camp, settings, null,
                out var production, out var allocation);
            var observation = new ElderObservationBuilder().Build(world,
                camp.Id, false);
            var directive = OptionAElderPolicy.FromCommittedState(world,
                camp.Id).Decide(observation);
            var compiled = new NativeElderDirectiveCompiler().Compile(
                new ElderCompilationContext(world, camp, camp, null,
                    observation, camp.Tile, settings), directive);
            Check(compiled.Intent.Stamp == baseline.Stamp,
                "Option A's strategic directive reproduces its compiled intent on fixture " +
                    fixture.Item1 + " (baseline=" + baseline.Stamp +
                    ", compiled=" + compiled.Intent.Stamp + ")");
            Check((compiled.Production?.ToString() ?? "") ==
                    (production?.ToString() ?? ""),
                "Option A's strategic directive reproduces its production order on fixture " +
                    fixture.Item1);

            var baselineWorld = SaveCodec.Load(SaveCodec.Save(world));
            var elderWorld = SaveCodec.Load(SaveCodec.Save(world));
            var baselineCamp = baselineWorld.Camps[0];
            var elderCamp = elderWorld.Camps[0];
            var baselineOrders = new Dictionary<int, List<Order>>
            {
                [baselineCamp.Player] = new List<Order>
                {
                    new Order(OrderKind.NoOp, baselineCamp.Id)
                }
            };
            var elderOrders = new Dictionary<int, List<Order>>
            {
                [elderCamp.Player] = new List<Order>
                {
                    new Order(OrderKind.NoOp, elderCamp.Id)
                }
            };
            var elderObservation = new ElderObservationBuilder().Build(
                elderWorld, elderCamp.Id, false);
            var elderDirective = OptionAElderPolicy.FromCommittedState(
                elderWorld, elderCamp.Id).Decide(elderObservation);
            var baselinePrepared = TurnPlanner.Prepare(baselineWorld,
                baselineOrders);
            var elderPrepared = TurnPlanner.PrepareWithElderDirectives(
                elderWorld, elderOrders,
                new Dictionary<int, ElderDirective>
                {
                    [elderCamp.Id] = elderDirective
                },
                new Dictionary<int, ElderObservation>
                {
                    [elderCamp.Id] = elderObservation
                }, new NativeElderDirectiveCompiler());
            new TurnResolver().Resolve(baselineWorld, baselinePrepared);
            new TurnResolver().Resolve(elderWorld, elderPrepared);
            Check(SaveCodec.Fingerprint(elderWorld) ==
                    SaveCodec.Fingerprint(baselineWorld),
                "Option A through the Elder seam reproduces the resolved world on fixture " +
                    fixture.Item1);
        }
    }

    private static ElderResetRequest Request(byte[] snapshot,
        int camp, bool movement = false, int limit = 4,
        bool critic = false)
        => new ElderResetRequest
        {
            Snapshot = snapshot,
            ControlledCampId = camp,
            Options = new ElderEnvironmentOptions
            {
                EpisodeDecisionLimit = limit,
                MovementEnabled = movement,
                IncludeTrainingCriticSidecar = critic
            },
            ExpectedSimulatorVersion = NativeElderEnvironment.SimulatorVersion
        };

    private static void EnvironmentAndReplay()
    {
        var source = Fixture(); int campId = source.Camps[0].Id;
        byte[] snapshot = SaveCodec.Save(source);
        var scalar = new NativeElderEnvironment(execution:
            new PhaseExecution());
        var parallel = new NativeElderEnvironment(execution:
            new PhaseExecution(4, 2, true, 1));
        var a0 = scalar.Reset(Request(snapshot, campId, critic: true));
        var b0 = parallel.Reset(Request(snapshot, campId, critic: true));
        Check(a0.InitialStateFingerprint == b0.InitialStateFingerprint &&
            JsonSerializer.Serialize(a0.Observation, Json) ==
                JsonSerializer.Serialize(b0.Observation, Json),
            "scalar and parallel Reset produce the same first observation");
        var actorOnlyEnvironment = new NativeElderEnvironment();
        var actorOnly = actorOnlyEnvironment.Reset(Request(snapshot, campId));
        Check(actorOnly.TrainingCriticSidecar == null &&
            a0.TrainingCriticSidecar != null &&
            a0.TrainingCriticSidecar.SchemaVersion ==
                ElderRLContract.CriticSidecarSchemaVersion &&
            JsonSerializer.Serialize(actorOnly.Observation, Json) ==
                JsonSerializer.Serialize(a0.Observation, Json),
            "requesting privileged critic ground truth cannot alter or enter the actor observation");
        var action = new ElderDirective
        {
            RetainedFieldOperation = .75,
            RetainedHerdFraction = .8,
            DevelopmentBudget = .4,
            FieldDevelopmentShare = .6
        };
        var a1 = scalar.Step(action); var b1 = parallel.Step(action);
        Check(a1.StateFingerprint == b1.StateFingerprint,
            "scalar and reversed parallel Step resolve identically");
        Check(a1.TrainingCriticSidecar != null &&
            a1.TrainingCriticSidecar.CultivationFrontierIdentity != null &&
            a1.TrainingCriticSidecar.FishingFrontierIdentity != null,
            "the optional training-only sidecar reports replayable local frontier ground truth");
        Near(a1.Reward.UndiscountedIntervalWelfare,
            a1.Reward.PriorPopulation * a1.Reward.RealizedQoL *
                a1.Reward.ElapsedYears,
            "scalar reward diagnostics reconcile to realized local welfare flow");
        Near(a1.ScalarReward, a1.Reward.UndiscountedIntervalWelfare,
            "scalar reward is undiscounted prior-cohort interval welfare");
        Check(a1.Observation.Core.HasPreviousInterval,
            "the post-step actor observation marks the previous physical interval present");
        Near(a1.Observation.Core.PreviousIntervalYears, a1.ElapsedYears,
            "the actor's previous interval is the realized physical duration");
        Check(a1.Decision.Intent != null &&
            a1.Decision.Interventions != null &&
            a1.Observation.Previous.Present,
            "Step reports compiled intent, interventions and previous realized outcome");
        Near(a1.Decision.Realized.RetainedFieldOperation,
            a1.Decision.Accepted.RetainedFieldOperation,
            "resolved field operation reconciles to authoritative labor");
        Near(a1.Decision.Realized.DevelopmentBudget,
            a1.Decision.Accepted.DevelopmentBudget,
            "resolved Development budget reconciles to authoritative labor");
        Check(a1.Decision.Interventions.Any(item =>
                item.Control == "retained-field-operation") &&
            a1.Decision.Interventions.Any(item =>
                item.Control == "retained-herd-fraction") &&
            a1.Decision.Interventions.Any(item =>
                item.Control == "development-budget") &&
            a1.Decision.Interventions.Any(item =>
                item.Control == "field-development-share"),
            "every repaired strategic control has an explicit intervention");

        NativeElderEnvironment.VerifyReplay(scalar.ReplayLog,
            new PhaseExecution(4, 2, true, 1));
        checks++;
        var replayStep = scalar.ReplayLog.Steps[0];
        Check(replayStep.Requested != null && replayStep.Accepted != null &&
            replayStep.Realized != null &&
            replayStep.Interventions != null &&
            replayStep.TrainingCriticSidecar != null &&
            replayStep.ElapsedYears == a1.ElapsedYears &&
            replayStep.PostStepFingerprint == a1.StateFingerprint,
            "replay v2 preserves requested, accepted and realized directives, intervention evidence, elapsed years, critic sidecar and post-step fingerprint");
        double recordedAcceptance = scalar.ReplayLog.Steps[0].Accepted
            .DevelopmentBudget;
        scalar.ReplayLog.Steps[0].Accepted.DevelopmentBudget =
            recordedAcceptance == 0 ? .125 : 0;
        bool rejectedTamper = false;
        try
        {
            NativeElderEnvironment.VerifyReplay(scalar.ReplayLog);
        }
        catch (InvalidOperationException error)
        {
            rejectedTamper = error.Message.Contains("accepted action");
        }
        scalar.ReplayLog.Steps[0].Accepted.DevelopmentBudget =
            recordedAcceptance;
        Check(rejectedTamper,
            "replay verification rejects a changed recorded accepted action");
        double recordedElapsed = scalar.ReplayLog.Steps[0].ElapsedYears;
        scalar.ReplayLog.Steps[0].ElapsedYears = recordedElapsed + 1;
        bool rejectedElapsed = false;
        try { NativeElderEnvironment.VerifyReplay(scalar.ReplayLog); }
        catch (InvalidOperationException error)
        {
            rejectedElapsed = error.Message.Contains("elapsed physical interval");
        }
        scalar.ReplayLog.Steps[0].ElapsedYears = recordedElapsed;
        Check(rejectedElapsed,
            "replay verification rejects changed elapsed physical time");
        int recordedEnvironmentSchema = scalar.ReplayLog
            .EnvironmentSchemaVersion;
        scalar.ReplayLog.EnvironmentSchemaVersion = 1;
        bool rejectedReplaySchema = false;
        try { NativeElderEnvironment.VerifyReplay(scalar.ReplayLog); }
        catch (InvalidOperationException error)
        {
            rejectedReplaySchema = error.Message.Contains("schema version");
        }
        scalar.ReplayLog.EnvironmentSchemaVersion = recordedEnvironmentSchema;
        Check(rejectedReplaySchema,
            "replay v1 metadata is rejected instead of being interpreted as v2");
        var continuedSnapshot = scalar.CaptureSnapshot();
        var restored = new NativeElderEnvironment();
        restored.Reset(Request(continuedSnapshot, campId, limit: 3));
        var a2 = scalar.Step(action); var restored2 = restored.Step(action);
        Check(a2.StateFingerprint == restored2.StateFingerprint,
            "save continuation reaches the uninterrupted state fingerprint");

        var movement = new NativeElderEnvironment();
        var moveReset = movement.Reset(Request(snapshot, campId,
            movement: true, limit: 2));
        int candidate = moveReset.Observation.Mask.LegalMovementCandidates
            .FirstOrDefault(value => value >= 0, -1);
        Check(candidate >= 0,
            "movement fixture exposes at least one masked local candidate");
        var moved = movement.Step(new ElderDirective
        {
            RetainedFieldOperation = 0,
            RetainedHerdFraction = 1,
            MovementCandidate = candidate
        });
        int current = Array.FindIndex(moved.Observation.Sites,
            site => site.Current);
        Check(moved.Decision.Accepted.MovementCandidate == candidate &&
            current >= 0 && moved.ObservationMetadata.SiteTiles[current] ==
                moveReset.ObservationMetadata.SiteTiles[candidate],
            "movement head passes through ordinary placement and changes the accepted camp tile");

        var invalid = new NativeElderEnvironment();
        invalid.Reset(Request(snapshot, campId, movement: true, limit: 1));
        var rejected = invalid.Step(new ElderDirective
        {
            MovementCandidate = int.MaxValue,
            RetainedFieldOperation = double.NaN
        });
        Check(rejected.Decision.Accepted.MovementCandidate == -1 &&
            rejected.Decision.Interventions.Any(intervention =>
                intervention.Kind ==
                    ElderInterventionKind.InvalidActionFallback),
            "Step logs categorical rejection instead of clamping to another site");
        NativeElderEnvironment.VerifyReplay(invalid.ReplayLog);
        checks++;

        var badStream = Request(snapshot, campId); badStream.RandomStream = 1;
        bool streamRejected = false;
        try { new NativeElderEnvironment().Reset(badStream); }
        catch (NotSupportedException) { streamRejected = true; }
        Check(streamRejected,
            "Reset rejects an unsupported random stream instead of treating it as an inert label");

        var badOptions = Request(snapshot, campId);
        badOptions.Options.EnvironmentSchemaVersion++;
        bool optionsRejected = false;
        try { new NativeElderEnvironment().Reset(badOptions); }
        catch (InvalidOperationException) { optionsRejected = true; }
        Check(optionsRejected,
            "Reset validates the environment schema carried by its options payload");
    }
}
