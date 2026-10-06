using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Linq;
using Anthropocene.Simulation;

// WP5a: Housing is split into Housing Material (flexible Wood/Clay, Builder) and
// the Housing assembly that consumes it. These checks own their worlds so the
// fixed suite can run them on a parallel lane. The older residual-material
// policy checks live in HousingMaterialChecks.cs and run from NeolithicChecks.
internal static class HousingMaterialRecipeChecks
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Housing Material recipe: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-8) => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);
    private static CraftRecipe Recipe(WorldState w, CraftGood good) => NeolithicProduction.Recipes(w.Config).Single(r => r.Good == good);

    // Runs the construction stage for one role and labour pool: Housing Material
    // first, then the Housing assembly, on one shared tool budget. Returns the
    // worker intervals spent by both steps.
    internal static double ProduceHousing(WorldState w, Camp c, string role, double workerIntervals, double intervals, double usefulDemand,
        ref double equipmentIntervals, TurnRecord record, ToolCoverageBudget coverage = null, HousingMaterialPlan plan = null)
    {
        coverage = coverage ?? new ToolCoverageBudget(w, c, intervals);
        double used = NeolithicProduction.Produce(w, c, Recipe(w, CraftGood.HousingMaterial), role, workerIntervals, intervals,
            Math.Max(0, usefulDemand - Math.Max(0, c.Inventory.HousingMaterial)), ref equipmentIntervals, record, coverage, plan);
        used += NeolithicProduction.Produce(w, c, Recipe(w, CraftGood.Housing), role, workerIntervals - used, intervals,
            usefulDemand, ref equipmentIntervals, record, coverage, plan);
        return used;
    }

    private static void Enable(Camp c)
    {
        c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        c.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 0, Cloth = 0, FarmTools = 0, Housing = 2, Tools = 0 };
        c.Inventory.HandTools = 1000;
    }
    private static double Sum(TurnRecord r, CraftGood good, Func<ProductionReceipt, double> f) => r.Production.Where(p => p.Good == good).Sum(f);

    public static int Run()
    {
        checks = 0;
        RecipeShape(); LabourMaterialAndWearEquivalence(); SameTurnChain(); NoStockBeyondGap(); ForecastAndPlannerSchedule();
        StockCarriedAndConsumed(); InventoryPreservedAcrossMove(); SaveRoundTripAndLegacy(); ObservationExposesOwnStockOnly(); ScalarVersusParallel();
        MaterialAccessOrder(); MixPrefersUnreserved(); PlannerResolverAgreement(); ContendedConservation(); ContendedScalarVersusParallel();
        Console.WriteLine("PASS: " + checks + " Housing Material recipe assertions"); return checks;
    }

    private static void RecipeShape()
    {
        var w = NeolithicChecks.Fixture(); var n = w.Config.Neolithic;
        var material = Recipe(w, CraftGood.HousingMaterial); var housing = Recipe(w, CraftGood.Housing);
        Check((int)CraftGood.HousingMaterial == 9 && (int)CraftGood.FishingTools == Enum.GetValues(typeof(CraftGood)).Length - 1 &&
            (int)CraftGood.HerdingTools == 8 && (int)CraftGood.Housing == 4,
            "Fishing Tools is appended and existing ordinals are unchanged");
        Near(material.Rate, 2 * n.BuilderHousingOutputPerBaseDays, "material step runs at twice today's Housing rate");
        Near(housing.Rate, 2 * n.BuilderHousingOutputPerBaseDays, "assembly step runs at twice today's Housing rate");
        Near(material.ConstructionMaterial, n.HousingMaterialUnitsPerHousing, "material takes over the flexible Wood/Clay input");
        Near(housing.ConstructionMaterial, 0, "assembly takes no Wood/Clay directly"); Near(housing.HousingMaterial, 1, "assembly consumes one Housing Material");
        Check(!material.Equipment && housing.Equipment, "tool wear belongs to the assembly only");
        Check(material.AgricultureOrFishing && housing.AgricultureOrFishing && material.Specialist == WorkforceRoles.Builder && material.Track == NeolithicTech.Construction,
            "Builder Construction is available to Agriculture or feature-enabled Fishing");
        var c = w.Camps[0]; c.CraftingPriorities = new CraftingPriorities { Housing = .37 }; c.Inventory.HousingMaterial = 1.5;
        Near(NeolithicProduction.Priority(c, CraftGood.HousingMaterial), .37, "material shares Housing's priority"); Near(NeolithicProduction.Stock(c, CraftGood.HousingMaterial), 1.5, "stock reads the new inventory");
        Check(NeolithicProduction.ScheduleKey(CraftGood.HousingMaterial) < NeolithicProduction.ScheduleKey(CraftGood.Housing) && NeolithicProduction.ScheduleKey(CraftGood.HousingMaterial) > NeolithicProduction.ScheduleKey(CraftGood.FarmTools), "schedule places material immediately before assembly");
    }

    private static void LabourMaterialAndWearEquivalence()
    {
        foreach (string grade in new[] { "hand", "regular" })
        {
            var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; var n = w.Config.Neolithic; Enable(c);
            c.Inventory.Wood = 200; c.Inventory.Clay = 100;
            if (grade == "regular") { c.Inventory.HandTools = 0; c.Inventory.Tools = 10; }
            double amount = 3, woodClayBefore = c.Inventory.Wood + c.Inventory.Clay, handBefore = c.Inventory.HandTools, toolsBefore = c.Inventory.Tools, equipment = 1000;
            // Reference: today's single Housing step at the original rate on the same tool budget.
            double reference = new ToolCoverageBudget(w, c, 80).Time(CraftGood.Housing, n.BuilderHousingOutputPerBaseDays, amount);
            var record = new TurnRecord();
            double used = ProduceHousing(w, c, WorkforceRoles.Builder, 10000, 80, amount, ref equipment, record);
            double materialTime = Sum(record, CraftGood.HousingMaterial, p => p.WorkerIntervals), assemblyTime = Sum(record, CraftGood.Housing, p => p.WorkerIntervals);
            Near(Sum(record, CraftGood.HousingMaterial, p => p.Output), amount, grade + ": material made for every Housing unit");
            Near(Sum(record, CraftGood.Housing, p => p.Output), amount, grade + ": Housing assembled");
            Near(materialTime + assemblyTime, reference, grade + ": Builder worker-time of both steps equals today's single Housing step");
            Near(materialTime, assemblyTime, grade + ": labour is split equally");
            Near(used, reference, grade + ": produce reports the same total labour");
            Near(woodClayBefore - c.Inventory.Wood - c.Inventory.Clay, amount * n.HousingMaterialUnitsPerHousing, grade + ": Wood plus Clay debit per Housing is unchanged");
            Near(Sum(record, CraftGood.HousingMaterial, p => p.Wood + p.Clay), amount * n.HousingMaterialUnitsPerHousing, grade + ": material receipt carries the debit");
            Near(Sum(record, CraftGood.Housing, p => p.Wood + p.Clay), 0, grade + ": assembly receipt debits no Wood or Clay");
            double wear = (handBefore - c.Inventory.HandTools) + (toolsBefore - c.Inventory.Tools) * 4;
            Near(wear, amount, grade + ": tool wear is one equipment output per Housing");
            Near(Sum(record, CraftGood.HousingMaterial, p => p.HandToolsConsumed + p.ToolsConsumed + p.EquipmentWorkerIntervals), 0, grade + ": material step wears no tools");
            Near(c.Inventory.HousingMaterial, 0, grade + ": all material is consumed by its assembly");
            Near(NeolithicProduction.FixedHousingAt(w, c.Id, c.Tile, w.Days + 80 * w.Config.BaseDays), amount, grade + ": fixed housing is added by the assembly only");
            Near(w.FixedHousing.Sum(s => s.Units), amount, grade + ": exactly one fixed-housing deposit per Housing");
            Near(c.People.CraftExperience[NeolithicTech.Construction], amount / n.BuilderHousingOutputPerBaseDays, grade + ": Construction progress equals today's per Housing");
        }
        // Labour-limited: one Builder cannot spend labour on material its assembly cannot use.
        var lw = NeolithicChecks.Fixture(); var lc = lw.Camps[0]; Enable(lc); lc.Inventory.Wood = 500; lc.Inventory.Clay = 500; double e2 = 1000; var lr = new TurnRecord();
        ProduceHousing(lw, lc, WorkforceRoles.Builder, 80, 80, 100, ref e2, lr);
        Near(Sum(lr, CraftGood.Housing, p => p.Output), 2.5, "one Builder still makes 2.5 Housing per generation");
        Near(Sum(lr, CraftGood.HousingMaterial, p => p.Output), 2.5, "labour-limited material equals what the assembly can use");
        Near(lc.Inventory.HousingMaterial, 0, "no labour is wasted on unusable material");
        Near(Sum(lr, CraftGood.HousingMaterial, p => p.WorkerIntervals) + Sum(lr, CraftGood.Housing, p => p.WorkerIntervals), 80, "the two steps exactly fill the labour pool");
    }

    private static World ChainWorld(int wood = 100, int clay = 200) => new World(wood, clay);
    private sealed class World
    {
        public readonly WorldState W; public readonly Camp C;
        public World(int wood, int clay)
        {
            W = NeolithicChecks.Fixture(); C = W.Camps[0]; Enable(C); C.Population = 1000; C.Inventory.Wood = wood; C.Inventory.Clay = clay;
            C.Workforce.Assign(WorkforceRoles.Builder, 1, C.Population);
        }
    }

    private static void SameTurnChain()
    {
        var x = ChainWorld(); var w = x.W; var c = x.C; var record = new TurnRecord();
        NeolithicProduction.ResolveCampNeolithic(w, c, 80, 20, record);
        int firstMaterial = record.Production.FindIndex(p => p.Good == CraftGood.HousingMaterial), firstHousing = record.Production.FindIndex(p => p.Good == CraftGood.Housing);
        Check(firstMaterial >= 0 && firstHousing > firstMaterial, "material is produced before its assembly in the resolver");
        Near(Sum(record, CraftGood.HousingMaterial, p => p.Output), 2.5, "material made this turn");
        Near(Sum(record, CraftGood.Housing, p => p.Output), 2.5, "assembly consumes material produced in the same turn");
        Near(Sum(record, CraftGood.Housing, p => p.HousingMaterial), 2.5, "assembly receipt records the material it consumed");
        Near(c.Inventory.HousingMaterial, 0, "no material is left after the same-turn assembly");
        Near(Sum(record, CraftGood.HousingMaterial, p => p.Wood), 2.5 * 100 / 300, "material keeps the fixed residual Wood ratio");
        Near(Sum(record, CraftGood.HousingMaterial, p => p.Clay), 2.5 * 200 / 300, "material keeps the fixed residual Clay ratio");
        Near(100 + 200 - c.Inventory.Wood - c.Inventory.Clay, 2.5, "one total material unit per Housing is debited");
        Near(record.Production.Where(p => p.Good == CraftGood.Housing || p.Good == CraftGood.HousingMaterial).Sum(p => p.WorkerIntervals), 80, "the Builder's 80 intervals are the whole labour of both steps");
        // A Builder plus a later generalist Craftsperson: each pool runs material then assembly.
        var y = ChainWorld(); y.C.Workforce.Assign(WorkforceRoles.Craftsperson, 1, y.C.Population); var r2 = new TurnRecord();
        NeolithicProduction.ResolveCampNeolithic(y.W, y.C, 80, 20, r2);
        Near(Sum(r2, CraftGood.Housing, p => p.Output), 2.5 + .625, "Builder and generalist each realize their own material and assembly");
        Near(y.C.Inventory.HousingMaterial, 0, "generalist assembly leaves no material either");
        Check(r2.Production.Where(p => p.Role == WorkforceRoles.Craftsperson).Select(p => p.Good).Distinct().Count() >= 2, "the generalist runs both steps");
    }

    private static void NoStockBeyondGap()
    {
        var x = ChainWorld(500, 500); var w = x.W; var c = x.C; c.Population = 100; c.Workforce.Assign(WorkforceRoles.Builder, 30, c.Population);
        double gap = NeolithicProduction.Demand(w, c, CraftGood.Housing, 20);
        Check(gap > 0, "fixture has a Housing gap");
        Near(NeolithicProduction.Demand(w, c, CraftGood.HousingMaterial, 20), gap, "material demand is the Housing gap with no stock");
        c.Inventory.HousingMaterial = gap / 4;
        Near(NeolithicProduction.Demand(w, c, CraftGood.HousingMaterial, 20), gap * .75, "stock reduces material demand");
        Near(NeolithicProduction.Demand(w, c, CraftGood.Housing, 20), gap, "Housing demand ignores Housing Material stock");
        c.Inventory.HousingMaterial = gap * 2;
        Near(NeolithicProduction.Demand(w, c, CraftGood.HousingMaterial, 20), 0, "stock beyond the gap makes no demand");
        c.Inventory.HousingMaterial = 0;
        var record = new TurnRecord(); NeolithicProduction.ResolveCampNeolithic(w, c, 80, 20, record);
        Near(Sum(record, CraftGood.HousingMaterial, p => p.Output), gap, "abundant labour makes exactly the gap of material");
        Near(Sum(record, CraftGood.Housing, p => p.Output), gap, "and assembles it all");
        Near(c.Inventory.HousingMaterial, 0, "never stockpiled beyond the gap");
        // With stock covering the gap the resolver makes no material at all.
        var full = ChainWorld(500, 500); full.C.Population = 100; full.C.Workforce.Assign(WorkforceRoles.Builder, 30, full.C.Population);
        double fullGap = NeolithicProduction.Demand(full.W, full.C, CraftGood.Housing, 20); full.C.Inventory.HousingMaterial = fullGap + 3; var fr = new TurnRecord();
        NeolithicProduction.ResolveCampNeolithic(full.W, full.C, 80, 20, fr);
        Near(Sum(fr, CraftGood.HousingMaterial, p => p.Output), 0, "no material is produced when stock covers the gap");
        Near(Sum(fr, CraftGood.Housing, p => p.Output), fullGap, "Housing is assembled from stock");
        Near(full.C.Inventory.HousingMaterial, 3, "surplus stock is retained");
    }

    private static void ForecastAndPlannerSchedule()
    {
        var x = ChainWorld(500, 500); var w = x.W; var c = x.C; c.Population = 100; c.Workforce.Assign(WorkforceRoles.Builder, 30, c.Population);
        double gap = NeolithicProduction.Demand(w, c, CraftGood.Housing, 20);
        var recipes = NeolithicProduction.Recipes(w.Config);
        var planned = MaterialEconomy.Forecast(w, c, recipes, 80, 20, 1000, false, -1, false, false);
        Near(planned[CraftGood.HousingMaterial], gap, "forecast plans material for the gap"); Near(planned[CraftGood.Housing], gap, "forecast assembles that material");
        var plan = MaterialEconomy.PlanHousingMaterials(w, c, recipes, planned, 20, w.Days);
        Near(plan.WoodShare + plan.ClayShare, 1, "the mix plan is fixed by the material recipe");
        // One Builder: the forecast is labour-limited and never plans material it cannot assemble.
        var y = ChainWorld(500, 500); var lp = MaterialEconomy.Forecast(y.W, y.C, NeolithicProduction.Recipes(y.W.Config), 80, 20, 1000, false, -1, false, false);
        Near(lp[CraftGood.Housing], 2.5, "forecast keeps today's one-Builder output"); Near(lp[CraftGood.HousingMaterial], 2.5, "forecast material matches the assembly");
        // Forecast with carried stock: assembly can use stock plus new material within the same labour.
        y.C.Inventory.HousingMaterial = 2;
        var lp2 = MaterialEconomy.Forecast(y.W, y.C, NeolithicProduction.Recipes(y.W.Config), 80, 20, 1000, false, -1, false, false);
        Near(lp2[CraftGood.Housing], lp2[CraftGood.HousingMaterial] + 2, "assembly uses stock plus new material");
        Near(lp2[CraftGood.HousingMaterial] / 0.0625 + lp2[CraftGood.Housing] / 0.0625, 80, "both steps share one labour pool", 1e-6);
        // Planner: staffing is unchanged in worker count and requests both steps.
        var pw = NeolithicChecks.Fixture(); var pc = pw.Camps[0]; Enable(pc); pc.Population = 44; pc.Inventory.Wood = 100; pc.Inventory.Clay = 100; pc.PlantFood = 100000; pw.Config.AnnualHumanGrowthRate = 0;
        var intent = EconomicPlanner.Plan(pw, pc, EconomicIntent.From(pc));
        int builders = intent.Assignments.Where(a => a.Role == WorkforceRoles.Builder).Sum(a => a.Population);
        Check(builders >= 1, "planner still staffs the construction stage with Builders");
        EconomicIntents.Apply(pc, intent); var pr = new TurnRecord(); NeolithicProduction.ResolveCampNeolithic(pw, pc, 80, 20, pr);
        Check(Sum(pr, CraftGood.Housing, p => p.Output) > 0 && Sum(pr, CraftGood.HousingMaterial, p => p.Output) + 1e-9 >= Sum(pr, CraftGood.Housing, p => p.Output), "planned staffing realizes both steps in the resolver");
        Near(pc.Inventory.HousingMaterial, Sum(pr, CraftGood.HousingMaterial, p => p.Output) - Sum(pr, CraftGood.Housing, p => p.HousingMaterial), "material balance is conserved");
    }

    private static void StockCarriedAndConsumed()
    {
        var x = ChainWorld(0, 0); var w = x.W; var c = x.C; c.Population = 1000; c.Inventory.HousingMaterial = 8;
        var first = new TurnRecord(); NeolithicProduction.ResolveCampNeolithic(w, c, 80, 20, first);
        Near(Sum(first, CraftGood.Housing, p => p.Output), 5, "one Builder assembles 5 units from stock in a generation (assembly is half the labour)");
        Near(Sum(first, CraftGood.HousingMaterial, p => p.Output), 0, "no Wood or Clay so no new material");
        Near(c.Inventory.HousingMaterial, 3, "unused material is carried, without decay");
        var second = new TurnRecord(); NeolithicProduction.ResolveCampNeolithic(w, c, 80, 20, second);
        Near(Sum(second, CraftGood.Housing, p => p.Output), 3, "carried material is consumed the next turn");
        Near(c.Inventory.HousingMaterial, 0, "and the stock is exhausted");
        // Material carried into a turn with Wood available saves that turn's labour for more assembly.
        var y = ChainWorld(500, 500); y.C.Inventory.HousingMaterial = 2; var yr = new TurnRecord();
        NeolithicProduction.ResolveCampNeolithic(y.W, y.C, 80, 20, yr);
        double made = Sum(yr, CraftGood.HousingMaterial, p => p.Output), built = Sum(yr, CraftGood.Housing, p => p.Output);
        Near(built, made + 2, "assembly consumes carried stock plus this turn's material");
        Check(built > 2.5, "carried material lets the same labour build more Housing");
    }

    private static void InventoryPreservedAcrossMove()
    {
        var w = NeolithicChecks.Fixture(false); var c = w.Camps[0];
        c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true; c.People.Progression.Era = Era.Neolithic; w.FixedHousing = new List<FixedHousingStock>();
        c.Population = 45; c.PlantFood = 100000; c.Inventory.Cloth = 1000; c.Inventory.HousingMaterial = 4.5;
        int origin = c.Tile; int destination = w.Map.Neighbors(origin).Where(id => w.CanEnter(id, false, c)).OrderBy(id => id).First();
        var resolver = new TurnResolver(); var move = new Order(OrderKind.Move, c.Id, destination: destination);
        Check(resolver.Validate(w, 0, move) == null, "the camp can relocate");
        resolver.Resolve(w, new Dictionary<int, List<Order>> { [0] = new List<Order> { move } });
        Check(c.Tile == destination, "the Move relocates the camp");
        Near(c.Inventory.HousingMaterial, 4.5, "Housing Material moves with the camp and does not decay");
    }

    private static void SaveRoundTripAndLegacy()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0]; c.Inventory.HousingMaterial = 3.25; c.Inventory.LightHousingUnits = 1;
        var loaded = SaveCodec.Load(SaveCodec.Save(w));
        Near(loaded.Camps[0].Inventory.HousingMaterial, 3.25, "Housing Material survives a save round trip");
        Check(SaveCodec.Fingerprint(loaded) == SaveCodec.Fingerprint(w), "round trip preserves the world fingerprint");
        c.Inventory.HousingMaterial = 0; var zero = SaveCodec.Load(SaveCodec.Save(w));
        Near(zero.Camps[0].Inventory.HousingMaterial, 0, "zero stock is preserved");
        // A legacy inventory payload has no HousingMaterial element: it loads as zero.
        var serializer = new DataContractSerializer(typeof(CampInventory));
        var inventory = new CampInventory { Cloth = 7, Wood = 9, HousingMaterial = 2.5, LightHousingUnits = 4 };
        XDocument doc;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, inventory); stream.Position = 0; doc = XDocument.Load(stream); }
        var elements = doc.Descendants().Where(e => e.Name.LocalName == "HousingMaterial").ToArray();
        Check(elements.Length == 1, "the new field is a member of the inventory payload");
        foreach (var element in elements) element.Remove();
        CampInventory legacy;
        using (var stream = new MemoryStream()) { doc.Save(stream); stream.Position = 0; legacy = (CampInventory)serializer.ReadObject(stream); }
        Near(legacy.HousingMaterial, 0, "a legacy payload without the field loads as zero");
        Near(legacy.Cloth, 7, "legacy payload keeps other stock"); Near(legacy.Wood, 9, "legacy payload keeps raw stock"); Near(legacy.LightHousingUnits, 4, "legacy payload keeps pastoral stock");
    }

    private static void ObservationExposesOwnStockOnly()
    {
        var w = NeolithicChecks.Fixture(); var own = w.Camps[0]; own.Inventory.HousingMaterial = 2.75;
        var rival = new Camp { Id = w.NextId++, Player = 1, Tile = w.Map.Neighbors(own.Tile).OrderBy(id => id).First(), Population = 12, PlantFood = 12345 }; rival.Inventory.HousingMaterial = 6543.5;
        w.Camps.Add(rival); Ownership.Bind(w);
        string before = SaveCodec.Fingerprint(w);
        var observed = new AgentObservationBuilder().Build(w, own.Player);
        string json = ObservationContract.Serialize(observed, own.People.Mandate);
        var dto = ObservationContract.Deserialize(json);
        Check(AgentObservation.SchemaVersion == 20 && dto.Schema == 20, "observation schema is 20");
        Near(dto.Settlements.Single(s => s.Id == own.Id).HousingMaterial, 2.75, "owned Housing Material stock is exposed");
        Check(dto.Settlements.All(s => s.Id != rival.Id) && !json.Contains("6543.5"), "foreign Housing Material stays private");
        Near(observed.Settlements.Single(s => s.Id == own.Id).HousingMaterial, 2.75, "the in-process observation exposes the same stock");
        Check(SaveCodec.Fingerprint(w) == before, "building the observation leaves state unchanged");
        Check(!ObservationContract.Serialize(observed, own.People.Mandate).Replace("\"Schema\":18", "").Contains("\"Schema\":15"), "no schema 15 remains");
    }

    private static string RunChain(PhaseExecution execution, bool reverse)
    {
        var x = ChainWorld(300, 300); var w = x.W; var c = x.C;
        c.Workforce.Assign(WorkforceRoles.Craftsperson, 1, c.Population); c.Inventory.HousingMaterial = 1.5;
        var second = new Camp { Id = w.NextId++, Player = 1, Tile = 100, Population = 300, PlantFood = 20000 }; second.Technology = Technology.Cultivation; second.Technologies[(int)Technology.Cultivation] = true;
        second.People.Progression.Era = Era.Neolithic; w.Camps.Add(second); Ownership.Bind(w);
        Enable(second); second.Inventory.Wood = 40; second.Inventory.Clay = 10; second.Workforce.Assign(WorkforceRoles.Builder, 1, second.Population);
        if (reverse) w.Camps.Reverse();
        var resolver = new TurnResolver(execution);
        for (int turn = 0; turn < 4; turn++) resolver.Resolve(w, new Dictionary<int, List<Order>>());
        Check(w.History.Sum(h => h.Production.Where(p => p.Good == CraftGood.HousingMaterial).Sum(p => p.Output)) > 0, "the replay actually produces Housing Material");
        return SaveCodec.Fingerprint(w);
    }
    private static void ScalarVersusParallel()
    {
        string scalar = RunChain(new PhaseExecution(), false);
        string parallel = RunChain(new PhaseExecution(4, 1, true, 1), true);
        Check(scalar == parallel, "scalar and reversed 4-worker resolutions have equal fingerprints across four turns");
    }

    // WP5b: Housing Material takes its ordinary place in the material-access order.
    // A camp with a Builder and a Toolmaker, scarce Wood, no Clay and a small Hand
    // Tool stock (so Regular Tools have real demand): the Housing priority decides
    // who gets the Wood, however the roles happen to be scheduled.
    private static World Contended(double housing, double tools, double wood, double clay)
    {
        var x = new World(0, 0); var c = x.C;
        c.Inventory.Wood = wood; c.Inventory.Clay = clay; c.Inventory.Stone = 1000; c.Inventory.HandTools = .5;
        c.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 0, Cloth = 0, FarmTools = 0, Housing = housing, Tools = tools };
        c.Workforce.Assign(WorkforceRoles.Toolmaker, 1, c.Population);
        return x;
    }
    private static TurnRecord Resolve(World x)
    {
        var record = new TurnRecord(); NeolithicProduction.ResolveCampNeolithic(x.W, x.C, 80, 20, record); return record;
    }
    private static void MaterialAccessOrder()
    {
        // Housing 0.20 outranks Tools 0.15: Housing Material takes the scarce Wood first.
        var a = Contended(.20, .15, 1, 0); var ra = Resolve(a);
        double housingA = Sum(ra, CraftGood.Housing, p => p.Output), toolsA = Sum(ra, CraftGood.Tools, p => p.Output);
        Near(Sum(ra, CraftGood.HousingMaterial, p => p.Wood), .5, "Housing outranking Tools takes the Wood its tool-limited output needs");
        Near(housingA, .5, "Housing is built from that Wood (one material unit per Housing)");
        Near(toolsA, 0, "Tools receive no Wood once the higher-priority Housing has it");
        // Swap the priorities: Tools rank first and Housing gets only what they leave.
        var b = Contended(.15, .20, 1, 0); var rb = Resolve(b);
        double housingB = Sum(rb, CraftGood.Housing, p => p.Output), toolsB = Sum(rb, CraftGood.Tools, p => p.Output);
        Check(toolsB > 0 && toolsB > toolsA, "with the priorities swapped Tools take the Wood first");
        Near(Sum(rb, CraftGood.HousingMaterial, p => p.Wood) + Sum(rb, CraftGood.Tools, p => p.Wood), 1, "and Housing Material gets exactly the Wood Tools leave (their tools also raise Housing coverage)");
        Near(Sum(rb, CraftGood.Tools, p => p.Wood), Sum(rb, CraftGood.Tools, p => p.Output), "Tools consume one Wood each");
        // A strictly better rule than 'last': equal priority ties by good, so Housing Material precedes Tools.
        var t = Contended(.15, .15, 1, 0); Check(Sum(Resolve(t), CraftGood.Housing, p => p.Output) > 0, "a priority tie resolves by good and Housing Material precedes Tools");
        // The pure plan agrees with the resolver about who gets the Wood.
        var planned = NeolithicProduction.Recipes(a.W.Config).ToDictionary(r => r.Good, r => 0d); planned[CraftGood.Tools] = 5; planned[CraftGood.HousingMaterial] = 5; planned[CraftGood.Housing] = 5;
        a = Contended(.20, .15, 1, 0); b = Contended(.15, .20, 1, 0);
        var planA = MaterialEconomy.PlanHousingMaterials(a.W, a.C, NeolithicProduction.Recipes(a.W.Config), planned, 20, a.W.Days);
        Near(planA.WoodShare, 1, "the fixed plan is all Wood when Clay is absent");
        Near(planA.LimitDirect(1, 0, Recipe(a.W, CraftGood.Tools), 5), 0, "a lower-priority direct recipe cannot take the Wood Housing Material still claims");
        var planB = MaterialEconomy.PlanHousingMaterials(b.W, b.C, NeolithicProduction.Recipes(b.W.Config), planned, 20, b.W.Days);
        Near(planB.LimitHousing(1, 0, 5, 1, out _, out _), 0, "a higher-priority direct recipe keeps a hard reservation Housing Material cannot take");
    }
    private static void MixPrefersUnreserved()
    {
        // Tools (lower priority) need every unit of Wood, and Clay is plentiful:
        // Housing Material draws Clay and leaves the Wood to Tools.
        var x = Contended(.20, .15, .125, 50); var r = Resolve(x);
        double material = Sum(r, CraftGood.HousingMaterial, p => p.Output);
        Check(material > 0, "Housing Material is made");
        Near(Sum(r, CraftGood.HousingMaterial, p => p.Wood), 0, "Housing Material leaves the Wood a later recipe needs");
        Near(Sum(r, CraftGood.HousingMaterial, p => p.Clay), material, "and draws Clay for the whole unit");
        Near(Sum(r, CraftGood.Tools, p => p.Wood), .125, "the later recipe keeps its Wood", 1e-6);
        // When the unreserved rest is short, it dips into the reserved amount in proportion to what remains.
        var y = Contended(.20, .15, .125, .1); var ry = Resolve(y);
        Near(Sum(ry, CraftGood.HousingMaterial, p => p.Clay), .1, "all unreserved Clay is used first");
        Near(Sum(ry, CraftGood.HousingMaterial, p => p.Wood) + Sum(ry, CraftGood.HousingMaterial, p => p.Clay), Sum(ry, CraftGood.HousingMaterial, p => p.Output), "one material unit per Housing Material");
        Check(Sum(ry, CraftGood.HousingMaterial, p => p.Wood) > 0, "the shortfall dips into the Wood reserved for the lower-priority recipe");
        // Pure helper: unreserved 10 Wood / 10 Clay, 20 Wood reserved by a later recipe, claim 30.
        var planned = NeolithicProduction.Recipes(x.W.Config).ToDictionary(q => q.Good, q => 0d); planned[CraftGood.Tools] = 1000; planned[CraftGood.HousingMaterial] = 30;
        var plan = MaterialEconomy.BuildHousingPlan(NeolithicProduction.Recipes(x.W.Config).OrderBy(q => NeolithicProduction.ScheduleKey(q.Good)).ToArray(), planned, 1000, 10, 30);
        Near(plan.WoodShare + plan.ClayShare, 1, "plan shares sum to one");
    }
    private static void PlannerResolverAgreement()
    {
        // Uncontended single camp: the plan's forecast is exactly what the resolver realizes.
        var x = ChainWorld(500, 500); var w = x.W; var c = x.C;
        var recipes = NeolithicProduction.Recipes(w.Config);
        double equipment = (c.Inventory.HandTools + c.Inventory.Tools) * 80 / w.Config.Neolithic.ToolCoveragePerWorker;
        var forecast = MaterialEconomy.Forecast(w, c, recipes, 80, 20, equipment, false, -1, true, true);
        var record = new TurnRecord(); NeolithicProduction.ResolveCampNeolithic(w, c, 80, 20, record);
        Near(Sum(record, CraftGood.HousingMaterial, p => p.Output), forecast[CraftGood.HousingMaterial], "resolver realizes the forecast Housing Material", 1e-6);
        Near(Sum(record, CraftGood.Housing, p => p.Output), forecast[CraftGood.Housing], "resolver realizes the forecast Housing", 1e-6);
        Check(forecast[CraftGood.Housing] > 0, "the fixture produces Housing");
        // The automated plan sizes staffing for the same schedule and the resolver realizes it.
        var pw = NeolithicChecks.Fixture(); var pc = pw.Camps[0]; Enable(pc); pc.Population = 44; pc.Inventory.Wood = 100; pc.Inventory.Clay = 100; pc.PlantFood = 100000; pw.Config.AnnualHumanGrowthRate = 0;
        var intent = EconomicPlanner.Plan(pw, pc, EconomicIntent.From(pc)); EconomicIntents.Apply(pc, intent);
        double pe = (pc.Inventory.HandTools + pc.Inventory.Tools) * 80 / pw.Config.Neolithic.ToolCoveragePerWorker;
        var pf = MaterialEconomy.Forecast(pw, pc, NeolithicProduction.Recipes(pw.Config), 80, 20, pe, false, -1, true, true);
        var pr = new TurnRecord(); NeolithicProduction.ResolveCampNeolithic(pw, pc, 80, 20, pr);
        Near(Sum(pr, CraftGood.HousingMaterial, p => p.Output), pf[CraftGood.HousingMaterial], "planned staffing realizes the forecast Housing Material", 1e-6);
        Near(Sum(pr, CraftGood.Housing, p => p.Output), pf[CraftGood.Housing], "planned staffing realizes the forecast Housing", 1e-6);
    }
    private static void ContendedConservation()
    {
        foreach (var (h, t, wood, clay) in new[] { (.20, .15, 1, 0d), (.15, .20, 1, 0d), (.20, .15, .125, 50d), (.20, .15, .125, .1), (.20, .15, 500d, 500d) })
        {
            var x = Contended(h, t, wood, clay); var r = Resolve(x); var c = x.C;
            double woodUsed = r.Production.Sum(p => p.Wood), clayUsed = r.Production.Sum(p => p.Clay);
            Near(wood - c.Inventory.Wood, woodUsed, "Wood debit equals the receipts");
            Near(clay - c.Inventory.Clay, clayUsed, "Clay debit equals the receipts");
            Check(c.Inventory.Wood >= -1e-9 && c.Inventory.Clay >= -1e-9 && c.Inventory.HousingMaterial >= -1e-9, "nothing is over-committed");
            Near(Sum(r, CraftGood.HousingMaterial, p => p.Wood + p.Clay), Sum(r, CraftGood.HousingMaterial, p => p.Output), "one Wood or Clay unit per Housing Material");
            Near(Sum(r, CraftGood.HousingMaterial, p => p.Output) - Sum(r, CraftGood.Housing, p => p.HousingMaterial), c.Inventory.HousingMaterial, "Housing Material balance is conserved");
        }
    }
    private static string RunContended(PhaseExecution execution, bool reverse)
    {
        var x = Contended(.20, .15, 6, 2); var w = x.W; var c = x.C;
        var second = new Camp { Id = w.NextId++, Player = 1, Tile = 100, Population = 300, PlantFood = 20000 }; second.Technology = Technology.Cultivation; second.Technologies[(int)Technology.Cultivation] = true;
        second.People.Progression.Era = Era.Neolithic; w.Camps.Add(second); Ownership.Bind(w);
        Enable(second); second.CraftingPriorities.Tools = .15; second.Inventory.Wood = 5; second.Inventory.Clay = 0; second.Inventory.Stone = 500; second.Inventory.HandTools = 1;
        second.Workforce.Assign(WorkforceRoles.Builder, 1, second.Population); second.Workforce.Assign(WorkforceRoles.Toolmaker, 1, second.Population);
        if (reverse) w.Camps.Reverse();
        var resolver = new TurnResolver(execution);
        for (int turn = 0; turn < 4; turn++) resolver.Resolve(w, new Dictionary<int, List<Order>>());
        Check(w.History.Sum(h => h.Production.Where(p => p.Good == CraftGood.HousingMaterial).Sum(p => p.Output)) > 0, "the contended replay produces Housing Material");
        return SaveCodec.Fingerprint(w);
    }
    private static void ContendedScalarVersusParallel()
    {
        Check(RunContended(new PhaseExecution(), false) == RunContended(new PhaseExecution(4, 1, true, 1), true), "contended scalar and reversed 4-worker fingerprints are equal across four turns");
    }
}
