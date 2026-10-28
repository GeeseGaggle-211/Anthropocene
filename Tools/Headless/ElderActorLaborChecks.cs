using System;
using System.Linq;
using Anthropocene.Simulation;

internal static class ElderActorLaborChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("ElderActorLabor: " + message);
    }

    public static int Run()
    {
        checks = 0;
        byte[] snapshot = ElderModel9Environment.GenerateOpening(17, 8, out _);
        var world = SaveCodec.Load(snapshot);
        var camp = world.Camps.Where(c => c.Population > 0 &&
                CultivationEconomy.EconomyEnabled(world, c)).OrderBy(c => c.Id).First();
        string before = SaveCodec.Fingerprint(world);
        var catalog = ElderActorLaborCompiler.BuildCatalog(world, camp);
        Check(catalog.Digest ==
                "411f21f8ce2a3c177515321275ff1d264d9d364480a6ffa7b2d75afa490cd6a2",
            "opening-17/8 catalog canonicalization matches the frozen digest vector");
        Check(before == SaveCodec.Fingerprint(world), "catalog construction is world-pure");
        Check(catalog.RegistryVersion == 1 && catalog.RegistryDigest ==
                ElderActorLaborContract.RegistryDigest && catalog.SpecialtyIds.SequenceEqual(
                ElderActorLaborContract.SpecialtyIds) && catalog.ActivityIds.SequenceEqual(
                ElderActorLaborContract.ActivityIds),
            "catalog binds the producer-owned ordered registries and digest");
        Check(catalog.SpecialtyRows.Length == ElderActorLaborContract.SpecialtyRows &&
                catalog.SpecialtyRows.All(row => row.Features.Length ==
                    ElderActorLaborContract.SpecialtyFeatureWidth),
            "specialty tokens have the fixed semantic feature schema");
        Check(catalog.ActivityRows.Length == ElderActorLaborContract.RoleRows *
                ElderActorLaborContract.ActivityRows && catalog.ActivityRows.Select(
                    (row, index) => row.Row == index && row.SpecialtyRow == index /
                    ElderActorLaborContract.ActivityRows && row.ActivityRow == index %
                    ElderActorLaborContract.ActivityRows && row.Features.Length ==
                    ElderActorLaborContract.ActivityFeatureWidth).All(value => value),
            "activity tokens have canonical identities and semantic features");
        Check(catalog.Resources.Length == ElderActorLaborContract.ResourceRows &&
                catalog.Resources.All(row => row.Features.Length ==
                    ElderActorLaborContract.ResourceFeatureWidth),
            "resource catalog has the fixed 20x8 schema");

        ElderDirective Action(int specialty = 0, int activity = 0)
        {
            var specialtyShares = new double[ElderActorLaborContract.SpecialtyRows];
            specialtyShares[specialty] = 1;
            var activityShares = Enumerable.Range(0, ElderActorLaborContract.RoleRows)
                .Select(role =>
                {
                    var row = new double[ElderActorLaborContract.ActivityRows];
                    // Every specialty row has an explicit idle activity.  A masked
                    // specialty must still submit the unique legal idle-only row.
                    row[0] = 1;
                    return row;
                }).ToArray();
            if (specialty > 0)
            {
                int role = specialty - 1;
                activityShares[role][0] = 0; activityShares[role][activity] = 1;
            }
            return new ElderDirective
            {
                SchemaVersion = ElderActorLaborContract.ActionSchemaVersion,
                LaborSpecialtyAllocation = specialtyShares,
                LaborActivityAllocation = activityShares,
                MovementCandidate = ElderModel9Contract.NativeMovement,
                CommitTurns = 1
            };
        }

        var idleAction = Action();
        var idle = ElderActorLaborCompiler.ValidateSyntax(catalog, idleAction);
        Check(idle.Valid && idle.FeasibilityCost == 0,
            "global idle is an explicit syntactically valid allocation");
        var bad = Action(); bad.LaborSpecialtyAllocation[0] = .4;
        var rejected = ElderActorLaborCompiler.ValidateSyntax(catalog, bad);
        Check(!rejected.Valid && rejected.FeasibilityCost >= 1 &&
                rejected.Violations.Any(v => v.Reason == "specialty-sum" &&
                    v.Avoidable == true && v.ReasonCode > 0),
            "a malformed specialty simplex is an avoidable typed rejection");
        var negative = Action(); negative.LaborSpecialtyAllocation[0] = 1 + 1e-12;
        negative.LaborSpecialtyAllocation[1] = -1e-12;
        var negativeRejected = ElderActorLaborCompiler.ValidateSyntax(catalog,
            negative);
        Check(!negativeRejected.Valid && negativeRejected.Violations.Any(v =>
                v.Reason == "specialty-negative" && v.RequestedKnown &&
                ElderRLContract.Finite(v.Requested)),
            "even roundoff-sized negative shares reject before apportionment");

        int maskedSpecialty = Array.FindIndex(catalog.SpecialtyMask, allowed => !allowed);
        if (maskedSpecialty >= 0)
        {
            var unavailable = Action();
            unavailable.LaborSpecialtyAllocation[0] = 0;
            unavailable.LaborSpecialtyAllocation[maskedSpecialty] = 1;
            var invalid = ElderActorLaborCompiler.ValidateSyntax(catalog, unavailable);
            Check(!invalid.Valid && invalid.Violations.Any(v =>
                    v.Reason == "specialty-masked-nonzero"),
                "masked specialty allocation fails closed");
        }

        var observation = new ElderObservationBuilder { ValueOrderedSites = true }
            .Build(world, camp.Id, true, out _);
        var context = new ElderCompilationContext(world, camp, camp, null,
            observation, camp.Tile, EconomicIntent.From(camp));
        var compiler = new NativeElderDirectiveCompiler();
        var maximumCost = Action();
        for (int index = 0; index < maximumCost.LaborSpecialtyAllocation.Length; index++)
            maximumCost.LaborSpecialtyAllocation[index] = -1;
        for (int role = 0; role < maximumCost.LaborActivityAllocation.Length; role++)
            for (int activity = 0; activity < maximumCost.LaborActivityAllocation[role].Length; activity++)
                maximumCost.LaborActivityAllocation[role][activity] = -1;
        maximumCost.RetainedFieldOperation = 0;
        maximumCost.MovementCandidate = 999;
        maximumCost.FissionPolicy = 99;
        var maximumAttempt = ElderActorLaborLedger.ValidateDecision(catalog,
            maximumCost, observation, movementEnabled: true);
        Check(maximumAttempt.Violations.Length == 318 &&
                maximumAttempt.FeasibilityCost ==
                    ElderActorLaborContract.FeasibilityCostMaximum,
            "the advertised per-attempt feasibility-cost maximum is source reachable");
        var compiledIdle = compiler.Compile(context, idleAction);
        Check(compiledIdle.ActorLabor != null && compiledIdle.ActorLabor.Valid &&
                compiledIdle.ActorLabor.AcceptedSpecialtyResidents[0] == camp.Population &&
                compiledIdle.Intent.Labor.ActorActivityAuthority &&
                compiledIdle.Intent.Assignments.Count == 0,
            "global idle owns the full population without becoming native gatherers");
        Check(SaveCodec.Fingerprint(world) == before,
            "validation and compilation do not mutate the world");

        int gathererSpecialty = Array.IndexOf(ElderActorLaborContract.SpecialtyIds,
            WorkforceRoles.Gatherer);
        var gather = Action(gathererSpecialty, 1);
        gather.LaborActivityAllocation[0][1] = .25;
        gather.LaborActivityAllocation[0][0] = .75;
        var compiled = compiler.Compile(context, gather);
        double expectedWork = camp.Population * catalog.TurnYears * .25;
        Check(compiled.ActorLabor.AcceptedSpecialtyResidents.Sum() == camp.Population &&
                Math.Abs(compiled.ActorLabor.PlannedActivityWorkerYears[0][1] -
                    expectedWork) < 1e-8 &&
                Math.Abs(compiled.Intent.Labor.ActorFoodGathererWorkers -
                    camp.Population * .25) < 1e-8,
            "only specialty staffing is integer; activity worker-time stays fractional");
        Check(compiled.Allocation == null,
            "the specialty compiler does not invoke the legacy DemandControl allocator");

        // An action-12 order has no clothing/food split head: under the shipped kernel split its clothing-need
        // feedstock is protected ahead of meals like the native Elder's (no after-meals bang-bang); without the
        // kernel split it keeps the legacy after-meals order.
        foreach (bool kernel in new[] { true, false })
        {
            var splitWorld = SaveCodec.Load(snapshot); splitWorld.Config.ElderKernelSplit = kernel;
            var splitCamp = splitWorld.Camps.First(c => c.Id == camp.Id);
            var splitObservation = new ElderObservationBuilder { ValueOrderedSites = true }.Build(splitWorld, splitCamp.Id, true, out _);
            var splitPrepared = TurnPlanner.PrepareWithElderDirectives(splitWorld, new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Order>>(),
                new System.Collections.Generic.Dictionary<int, ElderDirective> { [splitCamp.Id] = gather },
                new System.Collections.Generic.Dictionary<int, ElderObservation> { [splitCamp.Id] = splitObservation }, compiler);
            Check(splitPrepared.Needs.TryGetValue(splitCamp.Id, out var splitNeeds) && splitNeeds.Order != null &&
                    splitNeeds.Order.ActorActivityAuthority && splitNeeds.ProtectedClothing == kernel,
                "actor-labor clothing feedstock is protected exactly when the kernel split is on (" + kernel + ")");
        }

        // Unassigned actor residents are explicit idle, never the ledger's
        // legacy implicit Gatherers. Fractional Gatherer activity is the only
        // food-gathering capacity exposed to the ecology pass.
        var originalIntent = EconomicIntent.From(camp);
        EconomicIntents.Apply(camp, compiledIdle.Intent);
        var idleLabor = CultivationEconomy.CalculateLabor(world, camp,
            CultivationEconomy.Footprint(world, camp));
        Check(idleLabor.Gatherers == 0 && idleLabor.FoodGatherers == 0 &&
                camp.EconomicLabor.ActorIdleWorkers == camp.Population,
            "global actor idle cannot become implicit food gathering");
        EconomicIntents.Apply(camp, compiled.Intent);
        var gatherLabor = CultivationEconomy.CalculateLabor(world, camp,
            CultivationEconomy.Footprint(world, camp));
        Check(Math.Abs(gatherLabor.FoodGatherers - camp.Population * .25) < 1e-8 &&
                Math.Abs(TurnResolver.PassiveFoodGathererIntervals(camp,
                    null, gatherLabor.Gatherers, catalog.TurnYears) -
                    camp.Population * .25) < 1e-8,
            "food gathering uses only the sampled fractional activity budget");
        EconomicIntents.Apply(camp, originalIntent);

        int farmerSpecialty = Array.IndexOf(
            ElderActorLaborContract.SpecialtyIds, WorkforceRoles.Farmer);
        if (farmerSpecialty >= 0 && catalog.SpecialtyMask[farmerSpecialty])
        {
            var idleFarmerDecision = compiler.Compile(context,
                Action(farmerSpecialty, 0));
            EconomicIntents.Apply(camp, idleFarmerDecision.Intent);
            var idleFarmerLabor = CultivationEconomy.CalculateLabor(world,
                camp, CultivationEconomy.Footprint(world, camp));
            Check(idleFarmerLabor.OperatingFarmers == 0 &&
                    idleFarmerLabor.ExpansionFarmers == 0,
                "idle Farmer staffing performs no hidden field work");
            EconomicIntents.Apply(camp, originalIntent);
        }

        // A material-starved sibling activity may leave capacity unused, but
        // cannot lend its worker-time to another good. The export pass and the
        // domestic pass also consume one shared activity ledger.
        var manufactureWorld = NeolithicChecks.Fixture();
        var manufactureCamp = manufactureWorld.Camps[0];
        foreach (string tech in NeolithicTech.FirstTier)
            manufactureCamp.People.NeolithicTechnologies.Add(tech);
        manufactureCamp.Population = 10;
        manufactureCamp.Workforce.Assignments.Clear();
        manufactureCamp.Workforce.Assignments.Add(new PopulationAssignment
            { Role = WorkforceRoles.Toolmaker, Population = 1 });
        manufactureCamp.Inventory.Stone = 1000;
        manufactureCamp.Inventory.Wood = 0;
        var autonomous = new AutonomousElderAction();
        int toolmaker = Array.IndexOf(WorkforceRoles.All,
            WorkforceRoles.Toolmaker);
        autonomous.Activities[toolmaker][4 + (int)CraftGood.HandTools] = .5;
        autonomous.Activities[toolmaker][4 + (int)CraftGood.Tools] = .5;
        var counts = new int[ElderActorLaborContract.RoleRows];
        counts[toolmaker] = 1;
        var actorOrder = ProductionOrder.Autonomous(autonomous, counts,
            preserveExplicitIdle: true);
        var productionTargets = NeolithicProduction.Recipes(
                manufactureWorld.Config).ToDictionary(recipe => recipe.Good,
                _ => 0d);
        productionTargets[CraftGood.HandTools] = 1000;
        productionTargets[CraftGood.Tools] = 1000;
        var actorNeeds = new TurnNeeds { Camp = manufactureCamp.Id,
            Residents = manufactureCamp.Population, Order = actorOrder,
            Production = productionTargets,
            Exports = new double[Enum.GetValues(typeof(CraftGood)).Length] };
        actorNeeds.Exports[(int)CraftGood.HandTools] = 1000;
        var actorRecord = new TurnRecord();
        NeolithicProduction.ResolveCampNeolithic(manufactureWorld,
            manufactureCamp, 80, 20, actorRecord, 0, actorNeeds,
            resolveNeeds: false);
        double handWork = actorRecord.Production.Where(receipt =>
            receipt.Good == CraftGood.HandTools).Sum(receipt =>
                receipt.WorkerIntervals);
        double toolWork = actorRecord.Production.Where(receipt =>
            receipt.Good == CraftGood.Tools).Sum(receipt =>
                receipt.WorkerIntervals);
        Check(handWork <= 40 + 1e-8 && handWork > 0 && toolWork == 0,
            "material-starved Tools do not lend their activity budget to HandTools");
        Check(actorRecord.Production.Sum(receipt => receipt.WorkerIntervals) <=
                40 + 1e-8,
            "export and domestic manufacture consume the activity budget once");

        var zeroWorld = NeolithicChecks.Fixture();
        var zeroCamp = zeroWorld.Camps[0];
        foreach (string tech in NeolithicTech.FirstTier)
            zeroCamp.People.NeolithicTechnologies.Add(tech);
        zeroCamp.Workforce.Assignments.Clear();
        zeroCamp.Workforce.Assignments.Add(new PopulationAssignment
            { Role = WorkforceRoles.Toolmaker, Population = 1 });
        var zeroAction = new AutonomousElderAction();
        var zeroCounts = new int[ElderActorLaborContract.RoleRows];
        zeroCounts[toolmaker] = 1;
        var zeroOrder = ProductionOrder.Autonomous(zeroAction, zeroCounts,
            preserveExplicitIdle: true);
        var zeroTargets = NeolithicProduction.Recipes(zeroWorld.Config)
            .ToDictionary(recipe => recipe.Good, _ => 0d);
        zeroTargets[CraftGood.HandTools] = 1000;
        var zeroNeeds = new TurnNeeds { Camp = zeroCamp.Id,
            Residents = zeroCamp.Population, Order = zeroOrder,
            Production = zeroTargets,
            Exports = new double[Enum.GetValues(typeof(CraftGood)).Length] };
        zeroNeeds.Exports[(int)CraftGood.HandTools] = 1000;
        var zeroRecord = new TurnRecord();
        NeolithicProduction.ResolveCampNeolithic(zeroWorld, zeroCamp, 80, 20,
            zeroRecord, 0, zeroNeeds, resolveNeeds: false);
        Check(zeroRecord.Production.Count == 0 &&
                zeroRecord.MaterialTransfers.Count == 0,
            "zero actor activity cannot be overridden by export demand or passive raw/craft work");

        var weaveWorld = NeolithicChecks.Fixture();
        var weaveCamp = weaveWorld.Camps[0];
        foreach (string tech in NeolithicTech.FirstTier)
            weaveCamp.People.NeolithicTechnologies.Add(tech);
        weaveCamp.PlantFood = 0;
        weaveCamp.AnimalFood = 0;
        weaveCamp.Workforce.Assignments.Clear();
        weaveCamp.Workforce.Assignments.Add(new PopulationAssignment
            { Role = WorkforceRoles.Weaver, Population = 1 });
        var weaveAction = new AutonomousElderAction();
        int weaver = Array.IndexOf(WorkforceRoles.All, WorkforceRoles.Weaver);
        weaveAction.Activities[weaver][4 + (int)CraftGood.Cloth] = 1;
        var weaveCounts = new int[ElderActorLaborContract.RoleRows];
        weaveCounts[weaver] = 1;
        var weaveOrder = ProductionOrder.Autonomous(weaveAction, weaveCounts,
            preserveExplicitIdle: true);
        var weaveTargets = NeolithicProduction.Recipes(weaveWorld.Config)
            .ToDictionary(recipe => recipe.Good, _ => 0d);
        weaveTargets[CraftGood.Cloth] = 1000;
        var weaveRecord = new TurnRecord();
        NeolithicProduction.ResolveCampNeolithic(weaveWorld, weaveCamp, 80,
            20, weaveRecord, 0, new TurnNeeds { Camp = weaveCamp.Id,
                Residents = weaveCamp.Population, Order = weaveOrder,
                Production = weaveTargets }, resolveNeeds: false);
        Check(weaveCamp.Workforce.Assignments.Single().Role ==
                WorkforceRoles.Weaver && weaveRecord.Production.All(receipt =>
                    receipt.Good != CraftGood.Cloth),
            "material-starved Weaver remains assigned and idle without Gatherer rescue");

        var fishingWorld = FishingEconomyChecks.Fixture();
        var fishingCamp = fishingWorld.Camps.Single();
        var fishingGround = new FishingGroundPlan(fishingCamp.Tile + 1,
            FishingRoute.Shallows, 1, FishingKnowledgeSource.Visible, false,
            0, 1, 100, 1, 0, 100, 1, new[] { .1, .1, .1, .1 }, 10);
        var fishingAccess = new FishingAccessPlan(fishingCamp.Id,
            fishingCamp.Tile, fishingCamp.Population, fishingWorld.Days,
            fishingWorld.Config.DaysPerYear, 1,
            fishingWorld.Config.Neolithic.FishingToolFisherYearsPerUnit,
            fishingWorld.Config.Neolithic.FishingToolHarvestMultiplier,
            new[] { fishingGround }, new[] { fishingGround });
        var harvestOnly = FishingEconomy.PrepareActorPlan(fishingWorld,
            fishingCamp, fishingAccess, 1, 1, 0, 0,
            fishingWorld.Config.DaysPerYear, departing: false);
        var developmentOnly = FishingEconomy.PrepareActorPlan(fishingWorld,
            fishingCamp, fishingAccess, 1, 0, 1, 0,
            fishingWorld.Config.DaysPerYear, departing: false);
        Check(harvestOnly.Grounds.Single().RequestedCatch > 0 &&
                harvestOnly.Grounds.Single().CapitalWorkerYears == 0,
            "harvest-only Fisher work cannot become fishery development");
        Check(developmentOnly.Grounds.Single().RequestedCatch == 0 &&
                developmentOnly.Grounds.Single().CapitalWorkerYears > 0 &&
                developmentOnly.Grounds.Single().EndingCapital > 0,
            "development-only Fisher work cannot become catch");

        var idleHerder = HerdingTooling.PlanActor(world, camp,
            catalog.TurnYears, 0, 1, animalsOverride: 10);
        Check(idleHerder.Herders == 1 &&
                idleHerder.AssignedHerderWorkers == 0 &&
                idleHerder.HerderYears == 0 && idleHerder.Capacity == 0 &&
                idleHerder.ToolsConsumed == 0,
            "idle Herder staffing performs no hidden management work");

        // Prior assignments are not immutable commitments at the WEGO boundary.
        camp.Workforce.Assignments.Clear();
        camp.Workforce.Assignments.Add(new PopulationAssignment
            { Role = WorkforceRoles.Builder, Population = Math.Min(camp.Population, 3) });
        var priorHostile = compiler.Compile(context, gather);
        Check(priorHostile.ActorLabor.AcceptedSpecialtyResidents.Sum() == camp.Population &&
                priorHostile.Intent.Assignments.Sum(item => item.Population) == camp.Population,
            "actor apportionment owns the full population despite prior assignments");

        // End-to-end environment seam: attempt zero is a pure rejection,
        // attempt one is merged with the original nonlabor choice, and the
        // world advances exactly once with successor provenance.
        var environment = new ElderModel9Environment { ActorLaborEnabled = true };
        var opening = environment.Reset(snapshot, movementEnabled: true);
        var openingVillage = opening.Villages.First(v =>
            v.LaborCatalog != null);
        int controlled = openingVillage.Camp;
        ElderDirective CompleteIdle()
        {
            var action = Action();
            action.MovementCandidate = -1;
            action.FissionPolicy = (int)environment.World.Camps.First(c =>
                c.Id == controlled).FissionPolicy;
            action.TradeValue = Enumerable.Repeat(1d,
                ElderTrade.Groups.Length).ToArray();
            action.TradeGate = Enumerable.Repeat(ElderTrade.Make,
                ElderTrade.Groups.Length).ToArray();
            action.TradeExportShare = environment.World.Config.TradeExportLabourShare;
            return action;
        }
        var malformed = CompleteIdle();
        malformed.LaborSpecialtyAllocation[0] = .5;
        string validationBefore = SaveCodec.Fingerprint(environment.World);
        var attempt0 = environment.ValidateLabor(
            new System.Collections.Generic.Dictionary<int, ElderDirective>
                { [controlled] = malformed }, 0,
            new System.Collections.Generic.Dictionary<int, string>
                { [controlled] = openingVillage.LaborCatalog.Digest })[controlled];
        Check(!attempt0.Valid && attempt0.Attempt == 0 &&
                attempt0.RemainingProposals == 1 &&
                validationBefore == SaveCodec.Fingerprint(environment.World),
            "attempt-zero rejection is pure and leaves exactly one proposal");
        int openingTurn = environment.World.Turn;
        var successor = environment.StepLabor(
            new System.Collections.Generic.Dictionary<int, ElderDirective>
                { [controlled] = malformed },
            new System.Collections.Generic.Dictionary<int, ElderDirective>
                { [controlled] = CompleteIdle() },
            new System.Collections.Generic.Dictionary<int, string>
                { [controlled] = openingVillage.LaborCatalog.Digest });
        var successorVillage = successor.Villages.First(v => v.Camp == controlled);
        Check(environment.World.Turn == openingTurn + 1 &&
                successorVillage.LaborPrevious != null &&
                successorVillage.LaborPrevious.InitialRequest.Attempt == 0 &&
                !successorVillage.LaborPrevious.InitialRequest.Valid &&
                successorVillage.LaborPrevious.FinalSampledRequest.Attempt == 1 &&
                successorVillage.LaborPrevious.FinalSampledRequest.Valid &&
                !successorVillage.LaborPrevious.Fallback,
            "one valid retry executes one turn and preserves both attempts");
        Check(successorVillage.LaborPrevious.DecisionCatalog.Digest ==
                openingVillage.LaborCatalog.Digest &&
                successorVillage.LaborCatalog != null &&
                successorVillage.LaborPrevious.RealizedGoodsOutput.All(value =>
                    value.HasValue) &&
                successorVillage.LaborPrevious.ResourceReceiptKnown[1] &&
                successorVillage.LaborPrevious.ResourceReceiptKnown[2] &&
                successorVillage.LaborPrevious.ResourceReceiptKnown[3],
            "successor feedback binds the prior catalog and exact native receipts");
        int farmerRole = Array.IndexOf(WorkforceRoles.All,
            WorkforceRoles.Farmer);
        Check(!successorVillage.LaborPrevious.ActualWorkerYearsKnown[farmerRole][5] &&
                successorVillage.LaborPrevious.ActualSchedulerConsumedWorkerYears[farmerRole][5] == null,
            "unmeasured field scheduler work remains unavailable, not zero");

        var receiptFeedback = ElderActorLaborFeedbackBuilder.Begin(
            openingVillage.LaborCatalog, idle, idle, openingTurn,
            "receipt-fixture", fallback: false);
        var receiptRecord = new TurnRecord();
        receiptRecord.Transfers.Add(new Transfer { Entity = controlled,
            Kind = "Cultivation", Food = 4 });
        receiptRecord.Transfers.Add(new Transfer { Entity = controlled,
            Kind = "Gathering", Food = 5 });
        receiptRecord.Transfers.Add(new Transfer { Entity = controlled,
            Kind = "Aquatic gathering", Food = 6 });
        receiptRecord.Transfers.Add(new Transfer { Entity = controlled,
            Kind = "FisheryHarvest", Food = 7 });
        receiptRecord.Transfers.Add(new Transfer { Entity = 900,
            Kind = "Hunting", Food = 10 });
        receiptRecord.Transfers.Add(new Transfer { Entity = 900,
            Kind = "Fishing", Food = 5 });
        receiptRecord.Transfers.Add(new Transfer { Entity = 900,
            Kind = "Foraging", Food = 4 });
        receiptRecord.Logistics.Add(new LogisticsRecord { Hunter = 900,
            Camp = controlled, HarvestOverflow = 3 });
        receiptRecord.ManagedHerds.Add(new ManagedHerdReport { Camp = controlled,
            AcceptedFood = 8, SlaughterFood = 2 });
        receiptRecord.MaterialTransfers.Add(new MaterialReceipt { Camp = controlled,
            Material = MaterialId.Stone, Amount = 3 });
        receiptRecord.Production.Add(new ProductionReceipt { Camp = controlled,
            Good = CraftGood.Cloth, Output = 11, PlantFood = 100 });
        ElderActorLaborFeedbackBuilder.BindResolved(receiptFeedback,
            receiptRecord, environment.World.Config, campVanished: false);
        Check(receiptFeedback.RealizedResourceReceipts[1] == 3 &&
                receiptFeedback.RealizedResourceReceipts[4] == 13 &&
                receiptFeedback.RealizedResourceReceipts[5] == 17 &&
                receiptFeedback.RealizedResourceReceipts[8] == 11 &&
                receiptFeedback.RealizedResourceReceipts[14] == 13 &&
                receiptFeedback.RealizedResourceReceipts[15] == 18,
            "successor feedback uses owned retained harvest, herd, material, and craft receipts");
        receiptRecord.Transfers.Add(new Transfer { Entity = 901,
            Kind = "Fishing", Food = 1 });
        var unresolvedOwner = ElderActorLaborFeedbackBuilder.Begin(
            openingVillage.LaborCatalog, idle, idle, openingTurn,
            "unresolved-owner-fixture", fallback: false);
        ElderActorLaborFeedbackBuilder.BindResolved(unresolvedOwner,
            receiptRecord, environment.World.Config, campVanished: false);
        Check(!unresolvedOwner.ResourceReceiptKnown[4] &&
                !unresolvedOwner.ResourceReceiptKnown[5] &&
                !unresolvedOwner.ResourceReceiptKnown[14] &&
                !unresolvedOwner.ResourceReceiptKnown[15],
            "unowned hunter transfers make total food receipts unavailable, not partial zero");

        var fallbackEnvironment = new ElderModel9Environment
            { ActorLaborEnabled = true };
        var fallbackOpening = fallbackEnvironment.Reset(snapshot,
            movementEnabled: true);
        var fallbackVillage = fallbackOpening.Villages.First(v =>
            v.LaborCatalog != null);
        controlled = fallbackVillage.Camp;
        var noRetryBad = CompleteIdle();
        noRetryBad.FissionPolicy = (int)fallbackEnvironment.World.Camps
            .First(c => c.Id == controlled).FissionPolicy;
        noRetryBad.TradeExportShare = fallbackEnvironment.World.Config
            .TradeExportLabourShare;
        noRetryBad.LaborSpecialtyAllocation[0] = .5;
        var fallbackStep = fallbackEnvironment.StepLabor(
            new System.Collections.Generic.Dictionary<int, ElderDirective>
                { [controlled] = noRetryBad },
            new System.Collections.Generic.Dictionary<int, ElderDirective>(),
            new System.Collections.Generic.Dictionary<int, string>
                { [controlled] = fallbackVillage.LaborCatalog.Digest });
        var fallbackFeedback = fallbackStep.Villages.First(v =>
            v.Camp == controlled).LaborPrevious;
        Check(fallbackFeedback.Fallback &&
                fallbackFeedback.ExecutionComparatorAttempt == 0 &&
                fallbackFeedback.FinalSampledRequest.Attempt == 0 &&
                fallbackFeedback.FinalSampledRequest.Fallback &&
                fallbackFeedback.InitialRequest.FeasibilityCost > 0,
            "a missing retry executes explicit idle without inventing attempt one");
        bool bypassRejected = false;
        try { fallbackEnvironment.Step(new System.Collections.Generic.Dictionary<int, ElderDirective>()); }
        catch (InvalidOperationException) { bypassRejected = true; }
        Check(bypassRejected,
            "ordinary Step cannot bypass the actor-labor retry protocol");

        return checks;
    }
}
