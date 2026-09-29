// Controlled, source-backed production witnesses for the Issue #33 craft audit.
// These fixtures exercise the production and extraction entry points directly;
// all material inputs are debited from finite local sources and all output/input
// accounting is checked against the production receipts.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

public static class PhysicalChainWitnesses
{
    private const double Epsilon = 1e-8;

    private sealed class CaseState
    {
        public WorldState World;
        public Camp Camp;
        public TurnRecord Record;
        public ToolCoverageBudget Coverage;
        public List<string> SourceIds = new List<string>();
        public List<string> SourceAmounts = new List<string>();
        public List<string> ServiceReceipts = new List<string>();
        public double IntervalBudget, GathererIntervalsUsed;
        public string FoodProvenance;
    }

    public static void Write(string output, SimulationConfig config, ResourceGenerationOptions resources)
    {
        Directory.CreateDirectory(output);
        var source = new WorldGenerator().Generate(91033, Clone(config), resources);
        var baseline = SaveCodec.Save(source);
        var lines = new List<string>
        {
            "good,control,role,source_ids,source_amounts,food_provenance,raw_stock_open,material_receipts,bootstrap_handtools,planned_output,actual_output,worker_intervals,worker_years,stone_used,clay_used,wood_used,plantfood_used,animalfood_used,animalfabric_used,housingmaterial_used,handtools_consumed,tools_consumed,stock_before,stock_after,negative_missing_input,negative_output,replacement_period1,replacement_period2,recurrence_scope,status"
        };

        foreach (var recipe in NeolithicProduction.Recipes(source.Config).OrderBy(r => r.Good))
        {
            foreach (bool mastered in new[] { false, true })
            {
                // A true generalist path is deliberately inapplicable only to
                // the recipe whose production contract is SpecialistOnly. It
                // still runs the real production entry point with finite inputs.
                if (!mastered && recipe.SpecialistOnly)
                {
                    var excluded = PrepareCase(baseline, recipe, false);
                    var excludedBefore = SnapshotInputs(excluded.Camp, recipe);
                    double excludedDemand = NeolithicProduction.Demand(excluded.World, excluded.Camp, recipe.Good,
                        excluded.IntervalBudget * excluded.World.Config.BaseDays / excluded.World.Config.DaysPerYear);
                    int excludedReceipts = excluded.Record.Production.Count;
                    double excludedEquipment = excluded.IntervalBudget;
                    double excludedUsed = NeolithicProduction.Produce(excluded.World, excluded.Camp, recipe,
                        WorkforceRoles.Craftsperson, RoleIntervalCapacity(excluded, WorkforceRoles.Craftsperson),
                        excluded.IntervalBudget, .25, ref excludedEquipment, excluded.Record, excluded.Coverage);
                    bool unchanged = excluded.Record.Production.Count == excludedReceipts && excludedUsed <= Epsilon &&
                        Near(excludedBefore.Stone, excluded.Camp.Inventory.Stone) && Near(excludedBefore.Clay, excluded.Camp.Inventory.Clay) &&
                        Near(excludedBefore.Wood, excluded.Camp.Inventory.Wood) && Near(excludedBefore.HandTools, excluded.Camp.Inventory.HandTools) &&
                        Near(excludedBefore.Tools, excluded.Camp.Inventory.Tools);
                    if (!unchanged) throw new InvalidOperationException("SpecialistOnly generalist negative changed state for " + recipe.Good);
                    lines.Add(Row(recipe.Good, "generalist-before-mastery", "Craftsperson",
                        string.Join("|", excluded.SourceIds), string.Join("|", excluded.SourceAmounts), excluded.FoodProvenance,
                        "0/0/0", MaterialSummary(excluded.Record, excluded.Camp), 0, .25, 0, 0, 0,
                        0, 0, 0, 0, 0, 0, 0, 0, 0, NeolithicProduction.Stock(excluded.Camp, recipe.Good),
                        NeolithicProduction.Stock(excluded.Camp, recipe.Good), "SpecialistOnly mastery", 0,
                        excludedDemand, excludedDemand,
                        "not applicable to this intentionally blocked SpecialistOnly generalist negative; matching mastered row establishes replacement",
                        "PASS: real generalist attempt blocked without mutation"));
                    continue;
                }

                string label = mastered ? "matching-mastered-specialist" : "generalist-before-mastery";
                var state = PrepareCase(baseline, recipe, mastered);
                double target = .25;
                var before = SnapshotInputs(state.Camp, recipe);
                double priorHousing = recipe.Good == CraftGood.Housing
                    ? NeolithicProduction.HousingSupply(state.World, state.Camp, state.World.Days + state.Record.Duration) : 0;
                double priorStock = recipe.Good == CraftGood.Housing ? priorHousing : NeolithicProduction.Stock(state.Camp, recipe.Good);
                double demandA = NeolithicProduction.Demand(state.World, state.Camp, recipe.Good,
                    state.World.Config.TurnDays / state.World.Config.DaysPerYear);
                double demandB = NeolithicProduction.Demand(state.World, state.Camp, recipe.Good,
                    state.World.Config.TurnDays / state.World.Config.DaysPerYear);
                string role = mastered ? recipe.Specialist : WorkforceRoles.Craftsperson;
                int receiptStart = state.Record.Production.Count;
                double workerIntervals = RoleIntervalCapacity(state, role);
                double used = NeolithicProduction.Produce(state.World, state.Camp, recipe, role,
                    workerIntervals, state.IntervalBudget, target, ref state.IntervalBudget,
                    state.Record, state.Coverage);
                var receipt = state.Record.Production.Skip(receiptStart).SingleOrDefault(r => r.Good == recipe.Good);
                double actualOutput = receipt?.Output ?? 0;
                bool conserved = receipt != null && Near(receipt.Output, actualOutput) &&
                    Near(receipt.Stone, before.Stone - state.Camp.Inventory.Stone) &&
                    Near(receipt.Clay, before.Clay - state.Camp.Inventory.Clay) &&
                    Near(receipt.Wood, before.Wood - state.Camp.Inventory.Wood) &&
                    Near(receipt.PlantFood, before.PlantFood - state.Camp.PlantFood) &&
                    Near(receipt.AnimalFood, before.AnimalFood - state.Camp.AnimalFood) &&
                    Near(before.AnimalFabric - receipt.AnimalFabric + (recipe.Good == CraftGood.AnimalFabric ? actualOutput : 0), state.Camp.Inventory.AnimalFabric) &&
                    Near(before.HousingMaterial - receipt.HousingMaterial + (recipe.Good == CraftGood.HousingMaterial ? actualOutput : 0), state.Camp.Inventory.HousingMaterial) &&
                    Near(before.HandTools - receipt.HandToolsConsumed + (recipe.Good == CraftGood.HandTools ? actualOutput : 0), state.Camp.Inventory.HandTools) &&
                    Near(before.Tools - receipt.ToolsConsumed + (recipe.Good == CraftGood.Tools ? actualOutput : 0), state.Camp.Inventory.Tools);
                // The target receipt is the authoritative labor receipt. Check
                // the reported debit against its exact cost, not a reconstructed
                // recipe formula.
                conserved &= Near(used, receipt?.WorkerIntervals ?? 0);
                if (recipe.Good == CraftGood.Housing)
                    conserved &= Near(NeolithicProduction.HousingSupply(state.World, state.Camp,
                        state.World.Days + state.Record.Duration) - priorHousing, actualOutput);
                else
                    conserved &= Near(NeolithicProduction.Stock(state.Camp, recipe.Good) - priorStock, actualOutput);

                bool chainOk = HousingChainReceiptsHold(state, recipe, receipt, used, out string chainNote);
                conserved &= chainOk;
                var negative = PrepareCase(baseline, recipe, mastered);
                double missing = RemoveOneRequiredInput(negative.Camp, recipe);
                string negativeInput = MissingInputName(recipe);
                double negativeEquipment = negative.IntervalBudget;
                double blocked = NeolithicProduction.Produce(negative.World, negative.Camp, recipe, role,
                    negative.IntervalBudget, negative.IntervalBudget, target, ref negativeEquipment,
                    negative.Record, negative.Coverage);

                bool positive = actualOutput > 0;
                bool sourceBacked = SourceReceiptsCover(state, recipe, target, out string rawReceipts) &&
                    (recipe.PlantFood <= 0 || state.FoodProvenance.IndexOf("PlantFood", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        state.FoodProvenance.IndexOf("prior", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    ((recipe.AnimalFood <= 0 && recipe.Good != CraftGood.LightHousing) ||
                        state.FoodProvenance.IndexOf("managed-herd", StringComparison.OrdinalIgnoreCase) >= 0);
                double bootstrapOutput = state.Record.Production.Where(r => r.Camp == state.Camp.Id && r.Good == CraftGood.HandTools).Sum(r => r.Output);
                bool laborBounded = used > 0 && RoleLaborFits(state) &&
                    state.GathererIntervalsUsed <= GathererWorkerCapacity(state) + Epsilon;
                bool needsStable = Near(demandA, demandB);
                bool negativeValid = missing > 0 && blocked <= Epsilon;
                bool pass = positive && sourceBacked && conserved && laborBounded && needsStable && negativeValid;
                string status = pass ? "PASS" : "FAIL: " + string.Join("|", new[]
                {
                    positive ? "" : "no-positive-output", sourceBacked ? "" : "raw-source-shortfall",
                    conserved ? "" : "receipt-conservation", laborBounded ? "" : "labor-budget",
                    needsStable ? "" : "nonrepeatable-demand", negativeValid ? "" : "missing-input-negative"
                }.Where(s => s.Length > 0));

                string initialSourceIds = string.Join("|", state.SourceIds), initialSourceAmounts = string.Join("|", state.SourceAmounts);
                double stockAfterInitialOutput = recipe.Good == CraftGood.Housing
                    ? NeolithicProduction.HousingSupply(state.World, state.Camp, state.World.Days + state.Record.Duration)
                    : NeolithicProduction.Stock(state.Camp, recipe.Good);
                var cycles = RunReplacementCycles(state, recipe, role, target);
                pass &= cycles.First > 0 && cycles.Second > 0;
                status = pass ? "PASS" : "FAIL: " + string.Join("|", new[]
                {
                    positive ? "" : "no-positive-output", sourceBacked ? "" : "raw-source-or-food-provenance",
                    conserved ? "" : "receipt-conservation", laborBounded ? "" : "role-labor-budget",
                    needsStable ? "" : "nonrepeatable-demand", negativeValid ? "" : "missing-input-negative",
                    cycles.First > 0 ? "" : "replacement-period-1", cycles.Second > 0 ? "" : "replacement-period-2"
                }.Where(s => s.Length > 0));
                lines.Add(Row(recipe.Good, label, role,
                    initialSourceIds, initialSourceAmounts, state.FoodProvenance,
                    "0/0/0", rawReceipts, bootstrapOutput, target, actualOutput, used,
                    used * state.World.Config.BaseDays / state.World.Config.DaysPerYear,
                    receipt?.Stone ?? 0, receipt?.Clay ?? 0, receipt?.Wood ?? 0,
                    receipt?.PlantFood ?? 0, receipt?.AnimalFood ?? 0, receipt?.AnimalFabric ?? 0, receipt?.HousingMaterial ?? 0,
                    receipt?.HandToolsConsumed ?? 0, receipt?.ToolsConsumed ?? 0,
                    priorStock, stockAfterInitialOutput,
                    negativeInput, blocked, cycles.First, cycles.Second,
                    cycles.Scope + (chainNote.Length > 0 ? "; " + chainNote : ""),
                    status));
                if (!pass) throw new InvalidOperationException("Physical chain witness failed: " + recipe.Good + " / " + label + " / " + status + " / " + cycles.Scope);
            }
        }
        File.WriteAllLines(Path.Combine(output, "recipe-witnesses.csv"), lines);
    }

    private static CaseState PrepareCase(byte[] baseline, CraftRecipe target, bool mastered)
    {
        var world = SaveCodec.Load(baseline);
        var camp = world.Camps.OrderBy(c => c.Id).First();
        camp.Technology = Technology.Cultivation;
        camp.Technologies[(int)Technology.Cultivation] = true;
        camp.Technologies[(int)Technology.AnimalHusbandry] = true;
        camp.People.Progression.Era = Era.Neolithic;
        // A fixed, disclosed 500-person controlled village makes recurring
        // storage and tool-service needs nonzero while keeping all labor roles
        // explicitly bounded below the declared cohort. No population changes
        // occur during the two continuation periods.
        camp.Population = Math.Max(500, camp.Population);
        camp.Inventory = new CampInventory();
        camp.Workforce ??= new PopulationLedger();
        bool managedFoodFixture = target.HusbandryOnly;
        double disclosedPlantFood = managedFoodFixture
            ? camp.Population * world.Config.Consumption * (world.Config.TurnDays / world.Config.BaseDays)
            : 100000;
        camp.PlantFood = disclosedPlantFood; // one meal horizon for herd fixtures; disclosed 100k stock otherwise
        camp.AnimalFood = camp.AquaticFood = 0;
        if (target.PlantFood > 0 && camp.PlantFood + Epsilon < target.PlantFood * .25)
            throw new InvalidOperationException("Disclosed prior PlantFood stock is insufficient before production for " + target.Good);
        camp.People.CraftExperience.Clear();
        camp.People.NeolithicTechnologies.Clear();
        camp.People.UnlockedSpecialists.Clear();
        if (mastered)
        {
            camp.People.NeolithicTechnologies.Add(target.Track);
            if (!camp.People.UnlockedSpecialists.Contains(target.Specialist)) camp.People.UnlockedSpecialists.Add(target.Specialist);
        }
        Ownership.Bind(world);

        var needed = RawNeeds(world.Config, target, .25);
        // Hand-tool replacement is evidenced through actual useful Pot
        // production. Select and disclose a site that can fund that service
        // from a second finite Clay receipt as well as the target recipe.
        if (target.Good == CraftGood.HandTools || target.Good == CraftGood.Tools)
        {
            var pot = NeolithicProduction.Recipes(world.Config).Single(r => r.Good == CraftGood.Pots);
            needed[MaterialId.Clay] = (needed.TryGetValue(MaterialId.Clay, out double clay) ? clay : 0) + .05 * pot.Clay;
        }
        if (NeedsTools(target))
        {
            double bootstrapStone = NeolithicProduction.Recipes(world.Config).Single(r => r.Good == CraftGood.HandTools).Stone;
            needed[MaterialId.Stone] = (needed.TryGetValue(MaterialId.Stone, out double stone) ? stone : 0) + bootstrapStone;
        }
        camp.Tile = SelectTile(world, camp, needed);
        if (target.Good == CraftGood.FarmTools)
        {
            var field = world.Tiles[camp.Tile];
            field.CultivationCapital = .25; field.CultivationOperator = camp.Id;
            field.Cultivation = (float)CultivationEconomy.Intensity(world.Config, field.CultivationCapital);
            field.Cultivated = true;
        }

        string foodProvenance = managedFoodFixture
            ? "managed-herd provenance; disclosed opening PlantFood=" + disclosedPlantFood.ToString("R", CultureInfo.InvariantCulture) + "; AnimalFood=AquaticFood=0"
            : "disclosed prior PlantFood=100000";
        if (target.HusbandryOnly)
        {
            foodProvenance = ProduceManagedAnimalFood(world, camp);
            if ((target.AnimalFood > 0 || target.Good == CraftGood.LightHousing) && camp.AnimalFood <= Epsilon)
                throw new InvalidOperationException("Controlled managed-herd turn produced no accepted AnimalFood for " + target.Good);
        }
        SetAssignments(camp, target, mastered);
        camp.CraftingPriorities = new CraftingPriorities
        {
            HandTools = 1, Pots = 1, Cloth = 1, FarmTools = 1, Housing = 1,
            Tools = 1, AnimalFabric = 1, LightHousing = 1, HerdingTools = 1
        };
        if (target.Good == CraftGood.HandTools)
        {
            // Avoid the intentional Toolmaking redirection of shared tool
            // demand; this controlled case measures HandTools replacement.
            camp.CraftingPriorities.Tools = 0;
            foodProvenance += "; controlled Tools priority=0 isolates HandTools replacement";
        }

        if (target.Good == CraftGood.FarmTools) foodProvenance += "; controlled opening field capital E=0.25";
        var record = new TurnRecord { Turn = world.Turn, Seed = world.Seed, StartDays = world.Days, Duration = world.Config.TurnDays };
        double referenceIntervals = world.Config.TurnDays / world.Config.BaseDays;
        var coverage = new ToolCoverageBudget(world, camp, referenceIntervals);
        var state = new CaseState { World = world, Camp = camp, Record = record, Coverage = coverage,
            IntervalBudget = referenceIntervals, FoodProvenance = foodProvenance };
        foreach (var material in needed.OrderBy(p => p.Key))
            ExtractExact(state, material.Key, material.Value);

        if (NeedsTools(target))
        {
            var handRecipe = NeolithicProduction.Recipes(world.Config).Single(r => r.Good == CraftGood.HandTools);
            string bootstrapRole = mastered && handRecipe.Specialist == target.Specialist ? target.Specialist : WorkforceRoles.Craftsperson;
            if (bootstrapRole == WorkforceRoles.Craftsperson)
            {
                camp.People.NeolithicTechnologies.Remove(handRecipe.Track);
                camp.People.UnlockedSpecialists.Remove(handRecipe.Specialist);
            }
            else
            {
                camp.People.NeolithicTechnologies.Add(handRecipe.Track);
                if (!camp.People.UnlockedSpecialists.Contains(handRecipe.Specialist)) camp.People.UnlockedSpecialists.Add(handRecipe.Specialist);
            }
            double bootstrapCapacity = RoleIntervalCapacity(state, bootstrapRole);
            double handUsed = NeolithicProduction.Produce(world, camp, handRecipe, bootstrapRole,
                bootstrapCapacity, referenceIntervals, 1, ref state.IntervalBudget, record, coverage);
            if (handUsed <= 0 || record.Production.LastOrDefault()?.Good != CraftGood.HandTools)
                throw new InvalidOperationException("Finite Stone did not bootstrap HandTools for " + target.Good);
            // Restore the requested target's mastery state after a possible
            // bootstrap; the bootstrapped item remains a real production receipt.
            if (mastered)
            {
                camp.People.NeolithicTechnologies.Add(target.Track);
                if (!camp.People.UnlockedSpecialists.Contains(target.Specialist)) camp.People.UnlockedSpecialists.Add(target.Specialist);
            }
            else
            {
                camp.People.NeolithicTechnologies.Remove(target.Track);
                camp.People.UnlockedSpecialists.Remove(target.Specialist);
            }
        }

        if (target.Good == CraftGood.LightHousing)
        {
            var animalFabric = NeolithicProduction.Recipes(world.Config).Single(r => r.Good == CraftGood.AnimalFabric);
            string role = camp.People.HasTech(animalFabric.Track) ? animalFabric.Specialist : WorkforceRoles.Craftsperson;
            double capacity = RoleIntervalCapacity(state, role);
            double intervalRef = state.IntervalBudget;
            double output = NeolithicProduction.Produce(world, camp, animalFabric, role, capacity, intervalRef, .25,
                ref state.IntervalBudget, record, coverage);
            if (output <= 0 || camp.Inventory.AnimalFabric + Epsilon < .25)
                throw new InvalidOperationException("LightHousing witness could not make real AnimalFabric from managed AnimalFood.");
        }

        if (target.Good == CraftGood.Housing)
        {
            // Housing assembly consumes real Housing Material made from the
            // extracted Wood/Clay, mirroring the Light Housing / Animal Fabric chain.
            ProduceHousingMaterial(state, .25);
            if (camp.Inventory.HousingMaterial + Epsilon < .25)
                throw new InvalidOperationException("Housing witness could not make real Housing Material from extracted Wood/Clay.");
        }

        return state;
    }

    // Housing Material has no tool wear of its own, but its witness services the
    // stock by assembling Housing, which needs tools like any assembly.
    private static bool NeedsTools(CraftRecipe recipe) => recipe.Equipment || recipe.Good == CraftGood.HousingMaterial;

    private static double ProduceHousingMaterial(CaseState state, double amount)
    {
        var material = NeolithicProduction.Recipes(state.World.Config).Single(r => r.Good == CraftGood.HousingMaterial);
        string role = state.Camp.People.HasTech(material.Track) ? material.Specialist : WorkforceRoles.Craftsperson;
        double capacity = RoleIntervalCapacity(state, role);
        return NeolithicProduction.Produce(state.World, state.Camp, material, role, capacity, state.IntervalBudget, amount,
            ref state.IntervalBudget, state.Record, state.Coverage);
    }

    // Physical-chain audit of the Housing split. For Housing it checks that the
    // Wood/Clay debit sits on the Housing Material receipt (no tool wear, no
    // equipment time), that the assembly receipt debits exactly the Housing
    // Material it outputs and carries the tool wear, and that the two steps'
    // labour equals the single-step Housing labour per unit (equal rates).
    private static bool HousingChainReceiptsHold(CaseState state, CraftRecipe recipe, ProductionReceipt receipt, double used, out string note)
    {
        note = "";
        if (receipt == null) return true;
        var config = state.World.Config;
        var materialRecipe = NeolithicProduction.Recipes(config).Single(r => r.Good == CraftGood.HousingMaterial);
        var housingRecipe = NeolithicProduction.Recipes(config).Single(r => r.Good == CraftGood.Housing);
        var camp = state.Camp.Id;
        if (recipe.Good == CraftGood.HousingMaterial)
        {
            note = "housing-material step: Wood+Clay=" + (receipt.Wood + receipt.Clay).ToString("R", CultureInfo.InvariantCulture) +
                "; tool-wear=0";
            return Near(receipt.Wood + receipt.Clay, receipt.Output * materialRecipe.ConstructionMaterial) &&
                receipt.HandToolsConsumed <= Epsilon && receipt.ToolsConsumed <= Epsilon && receipt.EquipmentWorkerIntervals <= Epsilon;
        }
        if (recipe.Good != CraftGood.Housing) return true;
        var material = state.Record.Production.FirstOrDefault(r => r.Camp == camp && r.Good == CraftGood.HousingMaterial);
        if (material == null) { note = "missing Housing Material receipt"; return false; }
        double tools = receipt.HandToolsConsumed + receipt.ToolsConsumed;
        bool ok = Near(material.Wood + material.Clay, material.Output * materialRecipe.ConstructionMaterial) &&
            material.HandToolsConsumed <= Epsilon && material.ToolsConsumed <= Epsilon && material.EquipmentWorkerIntervals <= Epsilon &&
            receipt.Wood <= Epsilon && receipt.Clay <= Epsilon && Near(receipt.HousingMaterial, receipt.Output * housingRecipe.HousingMaterial) &&
            receipt.HousingMaterial <= material.Output + Epsilon && tools > Epsilon && receipt.EquipmentWorkerIntervals > Epsilon &&
            Near(material.WorkerIntervals, used);
        note = "housing chain: material-labor=" + material.WorkerIntervals.ToString("R", CultureInfo.InvariantCulture) +
            "; assembly-labor=" + used.ToString("R", CultureInfo.InvariantCulture) +
            "; labor-per-housing-unit=" + ((material.WorkerIntervals + used) / Math.Max(Epsilon, receipt.Output)).ToString("R", CultureInfo.InvariantCulture) +
            "; assembly-tool-wear=" + tools.ToString("R", CultureInfo.InvariantCulture) + "; material-tool-wear=0";
        return ok;
    }

    private static string ProduceManagedAnimalFood(WorldState world, Camp camp)
    {
        double openingPlantFood = camp.PlantFood;
        var before = new CampInventory();
        var original = camp.Inventory;
        before.Stone = original.Stone; before.Clay = original.Clay; before.Wood = original.Wood;
        before.HandTools = original.HandTools; before.Tools = original.Tools; before.CopperTools = original.CopperTools;
        before.Pots = original.Pots; before.Cloth = original.Cloth; before.FarmTools = original.FarmTools;
        before.HousingUnits = original.HousingUnits; before.AnimalFabric = original.AnimalFabric;
        before.LightHousingUnits = original.LightHousingUnits; before.HerdingTools = original.HerdingTools;
        before.HousingMaterial = original.HousingMaterial;
        var species = world.Config.Species.FirstOrDefault(s => s.Domesticable);
        if (species == null) throw new InvalidOperationException("No domesticable species available for AnimalFood provenance witness.");
        world.Herds.Add(new Herd { Id = world.NextId++, Tile = camp.Tile, Species = species.Id, Animals = Math.Min(100, species.MaximumAnimals),
            Capacity = Math.Max(100, species.MaximumHerdSize), DomesticCamp = camp.Id, OwnerSociety = camp.SocietyId });
        var intent = EconomicIntent.From(camp);
        intent.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Herder, Population = 12 } };
        intent.Priorities = new CraftingPriorities();
        intent.AutomationTurns = 0; intent.Labor = null;
        var orders = new Dictionary<int, List<Order>> { [camp.Player] = new List<Order> { new Order(OrderKind.Economy, camp.Id) { Economy = intent } } };
        var execution = new PhaseExecution();
        var prepared = TurnPlanner.Prepare(world, orders, null, execution);
        var record = new TurnResolver(execution).Resolve(world, prepared);
        double accepted = record.ManagedHerds.Where(h => h.Camp == camp.Id).Sum(h => h.AcceptedFood);
        if (camp.Inventory.Stone != before.Stone || camp.Inventory.Clay != before.Clay || camp.Inventory.Wood != before.Wood ||
            camp.Inventory.HandTools != before.HandTools || camp.Inventory.Tools != before.Tools || camp.Inventory.CopperTools != before.CopperTools ||
            camp.Inventory.Pots != before.Pots || camp.Inventory.Cloth != before.Cloth || camp.Inventory.FarmTools != before.FarmTools ||
            camp.Inventory.HousingUnits != before.HousingUnits || camp.Inventory.AnimalFabric != before.AnimalFabric ||
            camp.Inventory.LightHousingUnits != before.LightHousingUnits || camp.Inventory.HerdingTools != before.HerdingTools ||
            camp.Inventory.HousingMaterial != before.HousingMaterial)
            throw new InvalidOperationException("Managed-herd provenance turn altered raw/manufactured inventory before the disclosed zero-stock chain start: " + MaterialSummary(record, camp));
        if (accepted <= Epsilon || camp.AnimalFood <= Epsilon)
        {
            var diag = record.ManagedHerds.LastOrDefault(h => h.Camp == camp.Id);
            throw new InvalidOperationException("Managed-herd resolution did not yield finite accepted AnimalFood; " +
                "potential=" + (diag?.PotentialFood ?? 0).ToString("R", CultureInfo.InvariantCulture) +
                ", nutrition=" + (diag?.Nutrition ?? 0).ToString("R", CultureInfo.InvariantCulture) +
                ", management=" + (diag?.ManagementFulfillment ?? 0).ToString("R", CultureInfo.InvariantCulture) +
                ", assignedHerders=" + intent.Assignments[0].Population + ", tile=" + camp.Tile + ", animals=" + world.Herds.Last().Animals);
        }
        return "TurnResolver managed-herd receipt; accepted=" + accepted.ToString("R", CultureInfo.InvariantCulture) +
            "; opening PlantFood=" + openingPlantFood.ToString("R", CultureInfo.InvariantCulture) +
            "; raw/manufactured opening and closing inventory=0";
    }

    private sealed class ReplacementResult { public double First, Second; public string Scope; }

    private static ReplacementResult RunReplacementCycles(CaseState state, CraftRecipe recipe, string role, double amount)
    {
        var result = new ReplacementResult { Scope = "two fixed-population service/wear periods with actual finite-source extraction and output receipts; no births/deaths" };
        double years = state.World.Config.Neolithic.ReferenceTurnYears;
        // The initial production receipt is committed at the horizon endpoint;
        // move to that boundary before measuring its next service period.
        state.World.Days = Math.Max(state.World.Days, state.Record.StartDays + state.Record.Duration);
        state.World.Turn++;
        for (int period = 1; period <= 2; period++)
        {
            state.Record = new TurnRecord { Turn = state.World.Turn, Seed = state.World.Seed,
                StartDays = state.World.Days, Duration = years * state.World.Config.DaysPerYear };
            state.IntervalBudget = state.Record.Duration / state.World.Config.BaseDays;
            state.GathererIntervalsUsed = 0;
            state.Coverage = new ToolCoverageBudget(state.World, state.Camp, state.IntervalBudget);
            if (!ApplyServiceAndWear(state, recipe, years)) { result.Scope += "; service failed: " + string.Join(";", state.ServiceReceipts); return result; }
            double demand = NeolithicProduction.Demand(state.World, state.Camp, recipe.Good, years,
                state.Camp.Population, false, recipe.Good == CraftGood.Pots);
            double useful = Math.Min(amount, demand);
            if (useful <= Epsilon) { result.Scope += "; no useful demand period=" + period + " demand=" + demand.ToString("R", CultureInfo.InvariantCulture); return result; }

            var materials = RawNeeds(state.World.Config, recipe, useful);
            CraftRecipe handRecipe = null;
            if (NeedsTools(recipe) && state.Camp.Inventory.HandTools + state.Camp.Inventory.Tools <= Epsilon)
            {
                handRecipe = NeolithicProduction.Recipes(state.World.Config).Single(r => r.Good == CraftGood.HandTools);
                materials[MaterialId.Stone] = (materials.TryGetValue(MaterialId.Stone, out double stone) ? stone : 0) + handRecipe.Stone;
            }
            foreach (var material in materials.OrderBy(p => p.Key)) ExtractExact(state, material.Key, material.Value);
            if (handRecipe != null)
            {
                string bootstrapRole = state.Camp.People.HasTech(handRecipe.Track) ? handRecipe.Specialist : WorkforceRoles.Craftsperson;
                double workerCapacity = RoleIntervalCapacity(state, bootstrapRole);
                double made = NeolithicProduction.Produce(state.World, state.Camp, handRecipe, bootstrapRole,
                    workerCapacity, state.IntervalBudget, 1, ref state.IntervalBudget, state.Record, state.Coverage);
                if (made <= Epsilon) return result;
            }
            if (recipe.Good == CraftGood.LightHousing)
            {
                var fabric = NeolithicProduction.Recipes(state.World.Config).Single(r => r.Good == CraftGood.AnimalFabric);
                string fabricRole = state.Camp.People.HasTech(fabric.Track) ? fabric.Specialist : WorkforceRoles.Craftsperson;
                double workerCapacity = RoleIntervalCapacity(state, fabricRole);
                if (NeolithicProduction.Produce(state.World, state.Camp, fabric, fabricRole, workerCapacity,
                    state.IntervalBudget, useful, ref state.IntervalBudget, state.Record, state.Coverage) <= Epsilon)
                    return result;
            }
            if (recipe.Good == CraftGood.Housing && ProduceHousingMaterial(state, useful) <= Epsilon)
                return result;
            double capacity = RoleIntervalCapacity(state, role);
            int start = state.Record.Production.Count;
            double equipmentIntervals = state.IntervalBudget;
            double used = NeolithicProduction.Produce(state.World, state.Camp, recipe, role, capacity,
                state.IntervalBudget, useful, ref equipmentIntervals, state.Record, state.Coverage);
            double produced = state.Record.Production.Skip(start).Where(p => p.Good == recipe.Good).Sum(p => p.Output);
            if (produced <= Epsilon || used <= Epsilon || !RoleLaborFits(state)) { result.Scope += "; production failed period=" + period + " produced=" + produced.ToString("R", CultureInfo.InvariantCulture) + " used=" + used.ToString("R", CultureInfo.InvariantCulture) + " rolefits=" + RoleLaborFits(state); return result; }
            if (period == 1) result.First = produced; else result.Second = produced;
            state.World.Days += years * state.World.Config.DaysPerYear;
            state.World.Turn++;
            state.ServiceReceipts.Add("period" + period + "=" + string.Join("|", state.Record.Production
                .Where(p => p.Camp == state.Camp.Id).Select(p => p.Good + ":" + p.Output.ToString("R", CultureInfo.InvariantCulture))) +
                ";raw=" + MaterialSummary(state.Record, state.Camp));
        }
        result.Scope += "; " + string.Join("; ", state.ServiceReceipts);
        return result;
    }

    private static bool ApplyServiceAndWear(CaseState state, CraftRecipe recipe, double years)
    {
        var w = state.World; var c = state.Camp;
        double beforeStock = NeolithicProduction.Stock(c, recipe.Good);
        double beforeHousing = NeolithicProduction.HousingSupply(w, c, w.Days);
        double serviced = 0;
        if (recipe.Good == CraftGood.Pots) NeolithicProduction.DepreciatePots(w, c, years);
        else if (recipe.Good == CraftGood.Cloth || recipe.Good == CraftGood.AnimalFabric)
            NeolithicProduction.ResolveNeeds(w, c, years, c.Population);
        else if (recipe.Good == CraftGood.Housing || recipe.Good == CraftGood.LightHousing)
            NeolithicProduction.DepreciateHousing(w, c, years, c.Population);
        else if (recipe.Good == CraftGood.HousingMaterial)
        {
            // Housing Material neither wears nor decays; the stock is serviced by
            // assembling Housing from it (a real assembly receipt with tool wear).
            var assembly = NeolithicProduction.Recipes(w.Config).Single(r => r.Good == CraftGood.Housing);
            string builder = c.People.HasTech(assembly.Track) ? assembly.Specialist : WorkforceRoles.Craftsperson;
            int start = state.Record.Production.Count;
            double equipment = state.IntervalBudget;
            double usedLabor = NeolithicProduction.Produce(w, c, assembly, builder, RoleIntervalCapacity(state, builder),
                state.IntervalBudget, Math.Min(.1, c.Inventory.HousingMaterial), ref equipment, state.Record, state.Coverage);
            var made = state.Record.Production.Skip(start).Where(p => p.Good == CraftGood.Housing).ToArray();
            serviced = made.Sum(p => p.HousingMaterial);
            if (serviced <= Epsilon || usedLabor <= Epsilon || made.Sum(p => p.HandToolsConsumed + p.ToolsConsumed) <= Epsilon)
            { state.ServiceReceipts.Add("Housing assembly service missing; consumed=" + serviced.ToString("R", CultureInfo.InvariantCulture)); return false; }
            state.ServiceReceipts.Add("HousingMaterial consumed by assembly=" + serviced.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (recipe.Good == CraftGood.FarmTools)
        {
            SetRole(c, WorkforceRoles.Farmer, Math.Max(4, c.Population / 5));
            var budget = new FarmToolWorkBudget(w, c);
            var labor = CultivationEconomy.PrepareHorizon(w, c, w.Days, years, budget);
            double covered = labor.MaintenanceCoveredWorkerYears;
#if ISSUE33_DIAGNOSTICS
            if (labor.MaintenanceFarmerWorkerYears > Assigned(c, WorkforceRoles.Farmer) * years + Epsilon)
                throw new InvalidOperationException("Farm maintenance service exceeds its assigned farmer-years.");
#endif
            if (covered > Epsilon) serviced = budget.Consume(covered, w.Config.Neolithic.FarmToolFarmerYearsPerUnit);
            if (serviced > Epsilon) state.ServiceReceipts.Add("FarmTools covered-farmer-years=" + serviced.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (recipe.Good == CraftGood.HerdingTools)
        {
            SetRole(c, WorkforceRoles.Herder, Math.Max(4, c.Population / 5));
            var management = HerdingTooling.Plan(w, c, years);
            if (management.HerderYears > Assigned(c, WorkforceRoles.Herder) * years + Epsilon)
                throw new InvalidOperationException("Herding service exceeds its assigned herder-years.");
            serviced = Math.Min(c.Inventory.HerdingTools, management.ToolsConsumed);
            c.Inventory.HerdingTools = Math.Max(0, c.Inventory.HerdingTools - serviced);
            if (serviced > Epsilon) state.ServiceReceipts.Add("HerdingTools consumed=" + serviced.ToString("R", CultureInfo.InvariantCulture) +
                ";herder-years=" + management.HerderYears.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (recipe.Good == CraftGood.HandTools || recipe.Good == CraftGood.Tools)
        {
            var pot = NeolithicProduction.Recipes(w.Config).Single(r => r.Good == CraftGood.Pots);
            double useful = Math.Min(.05, NeolithicProduction.Demand(w, c, CraftGood.Pots, years, c.Population));
            if (useful <= Epsilon) { state.ServiceReceipts.Add("Pots service no useful demand=" + useful.ToString("R", CultureInfo.InvariantCulture)); return false; }
            ExtractExact(state, MaterialId.Clay, useful * pot.Clay);
            int start = state.Record.Production.Count;
            string potter = c.People.HasTech(pot.Track) ? pot.Specialist : WorkforceRoles.Craftsperson;
            double labor = RoleIntervalCapacity(state, potter), equipment = state.IntervalBudget;
            double workerUsed = NeolithicProduction.Produce(w, c, pot, potter, labor, state.IntervalBudget,
                useful, ref equipment, state.Record, state.Coverage);
            var receipts = state.Record.Production.Skip(start).Where(p => p.Good == CraftGood.Pots).ToArray();
            double produced = receipts.Sum(p => p.Output);
            if (receipts.Length == 0 || !Near(receipts.Sum(p => p.WorkerIntervals), workerUsed)) { state.ServiceReceipts.Add("Pots service production missing; worker-used=" + workerUsed.ToString("R", CultureInfo.InvariantCulture)); return false; }
            serviced = receipts.Sum(p => p.HandToolsConsumed + p.ToolsConsumed);
            if (serviced <= Epsilon) { state.ServiceReceipts.Add("Pots produced=" + produced.ToString("R", CultureInfo.InvariantCulture) + " but no tools consumed"); return false; }
            state.ServiceReceipts.Add("Pots service output=" + produced.ToString("R", CultureInfo.InvariantCulture) +
                "; tool-use=" + serviced.ToString("R", CultureInfo.InvariantCulture));
        }
        double afterHousing = NeolithicProduction.HousingSupply(w, c, w.Days + years * w.Config.DaysPerYear);
        double afterStock = NeolithicProduction.Stock(c, recipe.Good);
        return serviced > Epsilon || afterStock < beforeStock - Epsilon || afterHousing < beforeHousing - Epsilon ||
            (recipe.Good == CraftGood.Housing && NeolithicProduction.HousingSupply(w, c, w.Days + years * w.Config.DaysPerYear) < beforeHousing - Epsilon);
    }

    // Raw Stone/Clay/Wood a recipe's chain draws from finite sources. A recipe that
    // consumes Housing Material owns no raw input of its own; its raw need is the
    // Housing Material recipe's, per consumed unit.
    private static Dictionary<MaterialId, double> RawNeeds(SimulationConfig config, CraftRecipe recipe, double output)
    {
        var result = new Dictionary<MaterialId, double>();
        if (recipe.HousingMaterial > 0)
        {
            var material = NeolithicProduction.Recipes(config).Single(r => r.Good == CraftGood.HousingMaterial);
            foreach (var need in RawNeeds(config, material, output * recipe.HousingMaterial))
                result[need.Key] = (result.TryGetValue(need.Key, out double have) ? have : 0) + need.Value;
        }
        Add(MaterialId.Stone, recipe.Stone * output);
        Add(MaterialId.Clay, recipe.Clay * output + recipe.ConstructionMaterial * output);
        // Construction's shared material budget is intentionally sourced from
        // Clay alone so the missing-input negative removes one binding input.
        Add(MaterialId.Wood, recipe.Wood * output);
        return result;
        void Add(MaterialId material, double amount) { if (amount > Epsilon) result[material] = amount; }
    }

    private static int SelectTile(WorldState world, Camp camp, Dictionary<MaterialId, double> needs)
    {
        var candidate = world.Tiles.Where(t => t.TraversableLand).Select(t => t.Id).Select(tile =>
        {
            camp.Tile = tile;
            var sources = MaterialEconomy.KnownSources(world, camp, world.Days);
            double shortfall = needs.Sum(p => Math.Max(0, p.Value - sources.Where(s => s.Material == p.Key).Sum(s => s.Available)));
            double supplied = needs.Sum(p => Math.Min(p.Value, sources.Where(s => s.Material == p.Key).Sum(s => s.Available)));
            // Pot/tool recurrence needs must emerge from the actual local
            // physical-storage profile. Prefer source-backed warm/wet tiles,
            // where natural storage is smaller than the configured reserve.
            double storage = TurnResolver.StorageGenerations(world, tile);
            return new { Tile = tile, Shortfall = shortfall, Supplied = supplied, Storage = storage };
        }).OrderBy(x => x.Shortfall).ThenBy(x => x.Storage).ThenByDescending(x => x.Supplied).ThenBy(x => x.Tile).First();
        if (candidate.Shortfall > Epsilon)
            throw new InvalidOperationException("No camp tile has enough known finite local source stock for controlled recipe inputs.");
        return candidate.Tile;
    }

    private static void ExtractExact(CaseState state, MaterialId material, double amount)
    {
        double remaining = amount;
        foreach (var source in MaterialEconomy.KnownSources(state.World, state.Camp, state.World.Days)
            .Where(s => s.Material == material).OrderBy(s => s.Distance).ThenBy(s => s.Tile).ThenBy(s => s.Id))
        {
            if (remaining <= Epsilon) break;
            double rate = state.World.Config.Neolithic.SpecialistMaterialExtractionRate * state.World.Config.Neolithic.GeneralistTaskEfficiency;
            double workers = Math.Min(GathererWorkerCapacity(state) - state.GathererIntervalsUsed, remaining / Math.Max(Epsilon, rate));
            double beforeStock = MaterialEconomy.Inventory(state.Camp, material);
            int receiptCount = state.Record.MaterialTransfers.Count;
            double workerUsed = MaterialEconomy.Extract(state.World, state.Camp, source, WorkforceRoles.Gatherer,
                workers, remaining, state.World.Days, state.Record);
            if (workerUsed <= Epsilon) continue;
            var receipt = state.Record.MaterialTransfers.Skip(receiptCount).Single(r => r.Material == material && r.Camp == state.Camp.Id);
            double got = receipt.Amount;
            double afterStock = MaterialEconomy.Inventory(state.Camp, material);
            if (!Near(workerUsed, receipt.WorkerIntervals) || !Near(got, afterStock - beforeStock) || !Near(receipt.Amount, receipt.Before - receipt.After))
                throw new InvalidOperationException("Raw material extraction receipt failed conservation for " + material);
            state.SourceIds.Add(receipt.Source);
            state.SourceAmounts.Add(material + ":" + receipt.Amount.ToString("R", CultureInfo.InvariantCulture));
            state.GathererIntervalsUsed += receipt.WorkerIntervals;
            remaining -= got;
        }
        if (remaining > Epsilon)
            throw new InvalidOperationException("Finite local " + material + " source could not provide requested input; shortfall=" + remaining.ToString("R", CultureInfo.InvariantCulture));
    }

    private sealed class InputSnapshot
    {
        public double Stone, Clay, Wood, PlantFood, AnimalFood, AnimalFabric, HousingMaterial, HandTools, Tools;
    }
    private static InputSnapshot SnapshotInputs(Camp camp, CraftRecipe recipe) => new InputSnapshot
    {
        Stone = camp.Inventory.Stone, Clay = camp.Inventory.Clay, Wood = camp.Inventory.Wood,
        PlantFood = camp.PlantFood, AnimalFood = camp.AnimalFood, AnimalFabric = camp.Inventory.AnimalFabric, HousingMaterial = camp.Inventory.HousingMaterial,
        HandTools = camp.Inventory.HandTools, Tools = camp.Inventory.Tools
    };

    private static bool SourceReceiptsCover(CaseState state, CraftRecipe recipe, double target, out string summary)
    {
        var needs = RawNeeds(state.World.Config, recipe, target);
        if (NeedsTools(recipe))
        {
            double bootstrapStone = NeolithicProduction.Recipes(state.World.Config).Single(r => r.Good == CraftGood.HandTools).Stone;
            needs[MaterialId.Stone] = (needs.TryGetValue(MaterialId.Stone, out double stone) ? stone : 0) + bootstrapStone;
        }
        var receipts = state.Record.MaterialTransfers.Where(r => r.Camp == state.Camp.Id).ToArray();
        bool all = needs.All(p => receipts.Where(r => r.Material == p.Key).Sum(r => r.Amount) + Epsilon >= p.Value);
        summary = string.Join("|", receipts.Select(r => r.Material + ":" + r.Amount.ToString("R", CultureInfo.InvariantCulture)));
        return all;
    }

    private static string MaterialSummary(TurnRecord record, Camp camp)
        => string.Join("|", record.MaterialTransfers.Where(r => r.Camp == camp.Id)
            .Select(r => r.Material + ":" + r.Amount.ToString("R", CultureInfo.InvariantCulture)));

    private static void SetAssignments(Camp camp, CraftRecipe recipe, bool mastered)
    {
        camp.Workforce.Assignments.Clear();
        void Add(string role, int count)
        {
            if (count > 0) camp.Workforce.Assignments.Add(new PopulationAssignment { Role = role, Population = count });
        }
        Add(WorkforceRoles.Gatherer, 2);
        Add(WorkforceRoles.Craftsperson, 4);
        Add(recipe.Specialist, mastered ? 2 : 0);
        if (recipe.HusbandryOnly) Add(WorkforceRoles.Herder, 12);
        if (recipe.AgricultureOnly) Add(WorkforceRoles.Farmer, 8);
    }

    private static void SetRole(Camp camp, string role, int count)
    {
        camp.Workforce.Assignments.RemoveAll(a => a.Role == role);
        int currently = camp.Workforce.Assignments.Sum(a => a.Population);
        int bounded = Math.Min(Math.Max(0, count), Math.Max(0, camp.Population - currently));
        if (bounded > 0) camp.Workforce.Assignments.Add(new PopulationAssignment { Role = role, Population = bounded });
    }

    private static double Assigned(Camp camp, string role)
        => camp.Workforce.Assignments.Where(a => a.Role == role).Sum(a => a.Population);

    private static double RoleIntervalCapacity(CaseState state, string role)
    {
        double capacity = Assigned(state.Camp, role) * state.IntervalBudget;
        double used = role == WorkforceRoles.Gatherer ? state.GathererIntervalsUsed :
            state.Record.Production.Where(r => r.Camp == state.Camp.Id && r.Role == role).Sum(r => r.WorkerIntervals);
        return Math.Max(0, capacity - used);
    }

    private static double GathererWorkerCapacity(CaseState state)
        => Assigned(state.Camp, WorkforceRoles.Gatherer) * state.IntervalBudget;

    private static bool RoleLaborFits(CaseState state)
    {
        foreach (var group in state.Record.Production.Where(r => r.Camp == state.Camp.Id).GroupBy(r => r.Role))
            if (group.Sum(r => r.WorkerIntervals) > Assigned(state.Camp, group.Key) * state.IntervalBudget + Epsilon)
                return false;
        return state.Record.MaterialTransfers.Where(r => r.Camp == state.Camp.Id).Sum(r => r.WorkerIntervals)
            <= GathererWorkerCapacity(state) + Epsilon;
    }

    private static double RemoveOneRequiredInput(Camp camp, CraftRecipe recipe)
    {
        if (recipe.Stone > 0) { double n = camp.Inventory.Stone; camp.Inventory.Stone = 0; return n; }
        if (recipe.Clay > 0 || recipe.ConstructionMaterial > 0) { double n = camp.Inventory.Clay; camp.Inventory.Clay = 0; return n; }
        if (recipe.Wood > 0) { double n = camp.Inventory.Wood; camp.Inventory.Wood = 0; return n; }
        if (recipe.PlantFood > 0) { double n = camp.PlantFood; camp.PlantFood = 0; return n; }
        if (recipe.AnimalFood > 0) { double n = camp.AnimalFood; camp.AnimalFood = 0; return n; }
        if (recipe.AnimalFabric > 0) { double n = camp.Inventory.AnimalFabric; camp.Inventory.AnimalFabric = 0; return n; }
        if (recipe.HousingMaterial > 0) { double n = camp.Inventory.HousingMaterial; camp.Inventory.HousingMaterial = 0; return n; }
        if (recipe.Equipment) { double n = camp.Inventory.HandTools; camp.Inventory.HandTools = 0; return n; }
        return 0;
    }

    private static string MissingInputName(CraftRecipe recipe)
    {
        if (recipe.Stone > 0) return "Stone";
        if (recipe.Clay > 0 || recipe.ConstructionMaterial > 0) return "Clay";
        if (recipe.Wood > 0) return "Wood";
        if (recipe.PlantFood > 0) return "PlantFood";
        if (recipe.AnimalFood > 0) return "AnimalFood";
        if (recipe.AnimalFabric > 0) return "AnimalFabric";
        if (recipe.HousingMaterial > 0) return "HousingMaterial";
        return "HandTools";
    }

    private static string Row(params object[] values) => string.Join(",", values.Select(v => "\"" + Convert.ToString(v, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    private static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-7 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static SimulationConfig Clone(SimulationConfig config)
    {
        return JsonSerializer.Deserialize<SimulationConfig>(JsonSerializer.Serialize(config, new JsonSerializerOptions { IncludeFields = true }), new JsonSerializerOptions { IncludeFields = true });
    }
}
