using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// WP3b: the Value rule's per-camp production order and where the schedules use it.
// Owns its worlds and counters (fixed-suite lanes run concurrently).
internal static class ProductionOrderChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Production order: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-9)
        => Check(Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Abs(expected)), message + " actual=" + actual + " expected=" + expected);
    private static bool Info => Environment.GetEnvironmentVariable("WP3B_INFO") == "1";

    private static CraftRecipe Recipe(WorldState w, CraftGood good) => NeolithicProduction.Recipes(w.Config).Single(r => r.Good == good);
    private static double Sum(TurnRecord r, CraftGood good, Func<ProductionReceipt, double> f) => r.Production.Where(p => p.Good == good).Sum(f);

    // Single agricultural camp with every first-tier craft, scarce Hand Tools and a
    // Weaver plus a Builder, so tool coverage is the contested input.
    private static (WorldState W, Camp C) Fixture(bool value, double handTools, double cloth, double lightHousing)
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0];
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        c.Inventory.HandTools = handTools; c.Inventory.Cloth = cloth; c.Inventory.LightHousingUnits = lightHousing;
        c.Inventory.Wood = 500; c.Inventory.Clay = 500; c.Inventory.Stone = 500;
        c.Workforce.Assign(WorkforceRoles.Weaver, 5, c.Population); c.Workforce.Assign(WorkforceRoles.Builder, 5, c.Population);
        w.Config.Neolithic.PlannerFulfilment = value ? "Value" : "Uniform"; // explicit either way: the Uniform arms test the legacy no-order path, independent of the shipped default
        return (w, c);
    }

    // What TurnPlanner.ComputeNeeds builds for an automated camp, for a camp with the
    // given (possibly null) production order, then the resolver's production for it.
    private static TurnNeeds Needs(WorldState w, Camp c, ProductionOrder order)
    {
        var recipes = NeolithicProduction.Recipes(w.Config);
        double equipment = new ToolCoverageBudget(w, c, 80).Remaining;
        var needs = new TurnNeeds { Camp = c.Id, Residents = c.Population, Order = order };
        needs.Production = MaterialEconomy.Forecast(w, c, recipes, 80, 20, equipment, true, c.Population, false, false, -1, -1, null, order);
        needs.Housing = MaterialEconomy.PlanHousingMaterials(w, c, recipes, needs.Production, 20, w.Days, null, order);
        return needs;
    }
    private static TurnRecord Resolve(WorldState w, Camp c, ProductionOrder order)
    {
        var needs = Needs(w, c, order); var record = new TurnRecord();
        NeolithicProduction.ResolveCampNeolithic(w, c, 80, 20, record, 0, needs);
        return record;
    }
    private static ProductionOrder OrderOf(WorldState w, Camp c) => CampProductionDemand.Build(w, c, EconomicIntent.From(c)).Order;

    public static void Run()
    {
        checks = 0;
        SyntheticOrder(); StepSequences(); UniformHasNoOrder(); FixtureKeys(); CrossRoleCoverage(); ToolsBeforeHousingMaterial(); ForecastMatchesResolver(); NotInSavesOrFingerprints(); DraftAndDeterminism();
        Console.WriteLine("PASS: " + checks + " production order assertions");
    }

    // Pure construction: formulas, precedence rules, unpriced tail.
    private static void SyntheticOrder()
    {
        var cfg = new SimulationConfig(); var n = cfg.Neolithic; n.PlannerFulfilment = "Value";
        var recipes = NeolithicProduction.Recipes(cfg);
        ProductionClaim Claim(CraftGood good, double v, double stock, double need, double quantity, bool valued = true)
            => new ProductionClaim { Good = good, Quantity = quantity, Stock = stock, Need = need, LabourPerUnit = 1, FirstUnitValue = valued ? v : 0, Valued = valued };
        var claims = new List<ProductionClaim> {
            Claim(CraftGood.Cloth, 10, 0, 100, 100), Claim(CraftGood.AnimalFabric, 10, 0, 100, 100),
            Claim(CraftGood.LightHousing, 100, 0, 10, 10), Claim(CraftGood.Housing, 50, 50, 100, 50),
            Claim(CraftGood.Pots, 0, 0, 1, 5, false), Claim(CraftGood.FarmTools, 0, 0, 1, 5, false), Claim(CraftGood.HerdingTools, 0, 0, 1, 5, false) };
        double Priority(CraftGood g) => g == CraftGood.FarmTools ? .9 : .5;
        var order = ProductionOrder.Build(cfg, claims, recipes, Priority);
        Check(order.Goods.Length == recipes.Length && order.Goods.Distinct().Count() == recipes.Length, "every recipe appears exactly once");
        double lambda = n.ValueCurveSteepness, disc = Math.Exp(-n.DiscountRatePerYear * cfg.TurnDays / cfg.DaysPerYear);
        Near(order.Key(CraftGood.Cloth), 10, "Cloth key at empty coverage is V");
        Near(order.Key(CraftGood.Housing), 50 * Math.Exp(-lambda * .5), "Housing key is V exp(-lambda c0), c0 = Stock / Need");
        Near(order.Key(CraftGood.HousingMaterial), order.Key(CraftGood.Housing), "Housing Material takes the Housing key");
        Near(order.Key(CraftGood.HandTools), 1 * 100 * disc, "Hand Tools: one output per tool times the best equipment need key, discounted one turn");
        Near(order.Key(CraftGood.Tools), 4 * 100 * disc, "Regular Tools: four outputs per tool");
        Check(double.IsNaN(order.Key(CraftGood.Pots)) && double.IsNaN(order.Key(CraftGood.FarmTools)) && double.IsNaN(order.Key(CraftGood.HerdingTools)), "Pots and Farm/Herding Tools carry no value key");
        Check(order.Goods[0] == CraftGood.Tools, "Regular Tools (4 x 0.82 x best key) lead");
        Check(order.Outranks(CraftGood.AnimalFabric, CraftGood.LightHousing) && order.Key(CraftGood.AnimalFabric) < order.Key(CraftGood.LightHousing), "Animal Fabric is moved ahead of the Light Housing that consumes it despite a lower key");
        Check(order.Rank(CraftGood.HousingMaterial) + 1 == order.Rank(CraftGood.Housing), "Housing Material immediately precedes its assembly");
        Check(order.Outranks(CraftGood.AnimalFabric, CraftGood.Cloth), "the precedence move also puts Animal Fabric ahead of the equal-key Cloth");
        int lastPriced = new[] { CraftGood.Tools, CraftGood.HandTools, CraftGood.Cloth, CraftGood.AnimalFabric, CraftGood.LightHousing, CraftGood.Housing, CraftGood.HousingMaterial }.Max(order.Rank);
        Check(new[] { CraftGood.Pots, CraftGood.FarmTools, CraftGood.HerdingTools }.All(g => order.Rank(g) > lastPriced), "unpriced goods follow every priced good and coverage producer");
        Check(order.Goods.Skip(lastPriced + 1).SequenceEqual(new[] { CraftGood.FarmTools, CraftGood.Pots, CraftGood.HerdingTools }), "unpriced goods keep crafting-priority order, ties by schedule key");
        // Equal keys tie by schedule key.
        var tie = ProductionOrder.Build(cfg, new List<ProductionClaim> { Claim(CraftGood.Cloth, 7, 0, 10, 10), Claim(CraftGood.LightHousing, 7, 0, 10, 10) }, recipes, Priority);
        Check(tie.Outranks(CraftGood.Cloth, CraftGood.LightHousing), "equal keys tie by ScheduleKey");
        // No priced equipment good: tools carry no derived value and the order is priority order.
        var none = ProductionOrder.Build(cfg, new List<ProductionClaim> { Claim(CraftGood.Pots, 0, 0, 1, 5, false) }, recipes, Priority);
        Check(double.IsNaN(none.Key(CraftGood.Tools)) && none.Goods.SequenceEqual(ProductionOrder.Sort(recipes, Priority, null).Select(r => r.Good)), "with nothing priced the order is the crafting-priority order");
        // Sorting subsets follows the same ranks; the null order is exactly today's sort.
        var subset = ProductionOrder.Sort(recipes.Where(r => r.Good == CraftGood.Cloth || r.Good == CraftGood.Tools || r.Good == CraftGood.Pots), Priority, order);
        Check(subset.Select(r => r.Good).SequenceEqual(new[] { CraftGood.Tools, CraftGood.Cloth, CraftGood.Pots }), "a subset sorts by rank");
        Check(ProductionOrder.Sort(recipes, Priority, null).Select(r => r.Good).SequenceEqual(recipes.OrderByDescending(r => Priority(r.Good)).ThenBy(r => NeolithicProduction.ScheduleKey(r.Good)).Select(r => r.Good)), "the null order is exactly the crafting-priority sort");
    }

    private static void StepSequences()
    {
        var cfg = new SimulationConfig(); var recipes = NeolithicProduction.Recipes(cfg);
        var sorted = ProductionOrder.Sort(recipes, g => NeolithicProduction.Priority(new Camp { CraftingPriorities = new CraftingPriorities() }, g), null);
        var uniform = ProductionOrder.Steps(sorted, false).ToArray();
        var nested = new List<(string, CraftRecipe)>();
        foreach (string role in WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson })) foreach (var r in sorted) nested.Add((role, r));
        Check(uniform.Length == nested.Count && uniform.Zip(nested, (a, b) => a.Role == b.Item1 && ReferenceEquals(a.Recipe, b.Item2)).All(x => x), "without a Value order the steps are today's roles-in-turn sequence");
        var value = ProductionOrder.Steps(sorted, true).ToArray();
        int housing = Array.FindIndex(value, s => s.Recipe.Good == CraftGood.Housing);
        Check(value[housing].Role == WorkforceRoles.Builder && value[housing + 1].Role == WorkforceRoles.Craftsperson && value[housing + 1].Recipe.Good == CraftGood.Housing, "under the Value order each recipe visits its specialist role and then the Craftsperson");
        Check(value.All(s => s.Role == WorkforceRoles.Craftsperson || s.Role == s.Recipe.Specialist), "no step pairs a recipe with a foreign specialist");
    }

    private static void UniformHasNoOrder()
    {
        var (w, c) = Fixture(false, 6, 190, 0);
        Check(CampProductionDemand.Build(w, c, EconomicIntent.From(c)).Order == null, "Uniform demand builds no order");
        EconomicPlanner.Plan(w, c, EconomicIntent.From(c), EconomicPlanner.ConfiguredRule(w.Config), out _, out var order);
        Check(order == null, "Uniform planning returns no order");
        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.NoOp, c.Id) { RequestAutomaticEconomy = true } } });
        Check(prepared.AutomatedCamps.Contains(c.Id) && prepared.ProductionOrders.Count == 0 && prepared.Needs[c.Id].Order == null, "an automated Uniform camp carries no order in PreparedTurn");
    }

    private static void FixtureKeys()
    {
        var (w, c) = Fixture(true, 6, 190, 0);
        var demand = CampProductionDemand.Build(w, c, EconomicIntent.From(c)); var order = demand.Order;
        Check(order != null, "Value demand builds an order");
        var cloth = demand.Claims.Single(x => x.Good == CraftGood.Cloth); var housing = demand.Claims.Single(x => x.Good == CraftGood.Housing);
        double lambda = w.Config.Neolithic.ValueCurveSteepness;
        Near(order.Key(CraftGood.Cloth), cloth.FirstUnitValue * Math.Exp(-lambda * cloth.Stock / cloth.Need), "Cloth key uses the claim's V, Stock and Need");
        Near(order.Key(CraftGood.Housing), housing.FirstUnitValue * Math.Exp(-lambda * housing.Stock / housing.Need), "Housing key uses the claim's V, Stock and Need");
        Check(order.Outranks(CraftGood.Housing, CraftGood.Cloth), "well-clothed camp without housing keys Housing above Cloth (" + order + ")");
        if (Info) Console.WriteLine("WP3B_INFO order " + order + " keys cloth=" + order.Key(CraftGood.Cloth) + " housing=" + order.Key(CraftGood.Housing) + " tools=" + order.Key(CraftGood.Tools));
    }

    // Scarce shared tool coverage goes to the more valuable recipe, not the earlier role.
    private static void CrossRoleCoverage()
    {
        // Low housing coverage, high clothing coverage: Housing is worth more per unit.
        var (w, c) = Fixture(true, 6, 190, 0); var order = OrderOf(w, c);
        var value = Resolve(w, c, order);
        double housingV = Sum(value, CraftGood.Housing, p => p.Output), clothV = Sum(value, CraftGood.Cloth, p => p.Output);
        Check(housingV > 0 && clothV <= 1e-9, "Value order: Housing takes the scarce coverage before Cloth (housing " + housingV + ", cloth " + clothV + ")");
        Near(Sum(value, CraftGood.Housing, p => p.HandToolsConsumed), 6, "all six Hand Tools of coverage go to Housing");
        var (uw, uc) = Fixture(true, 6, 190, 0);
        var uniform = Resolve(uw, uc, null);
        Check(Sum(uniform, CraftGood.Cloth, p => p.Output) > 0 && Sum(uniform, CraftGood.Housing, p => p.Output) <= 1e-9, "without the order the Weaver's Cloth takes the coverage first (role order)");
        // Low clothing coverage, high housing coverage: Cloth goes first.
        var (bw, bc) = Fixture(true, 6, 0, 26); var borderOrder = OrderOf(bw, bc);
        Check(borderOrder.Outranks(CraftGood.Cloth, CraftGood.Housing), "poorly clothed, well-housed camp keys Cloth above Housing (" + borderOrder + ")");
        var flipped = Resolve(bw, bc, borderOrder);
        Check(Sum(flipped, CraftGood.Cloth, p => p.Output) > 0 && Sum(flipped, CraftGood.Housing, p => p.Output) <= 1e-9, "Value order: Cloth takes the coverage when clothing is the scarcer need");
        // The Craftsperson stays in the sequence: with no Builder the generalist assembles Housing first.
        var (gw, gc) = Fixture(true, 6, 190, 0); gc.Workforce.Assign(WorkforceRoles.Builder, 0, gc.Population); gc.Workforce.Assign(WorkforceRoles.Craftsperson, 5, gc.Population);
        var generalist = Resolve(gw, gc, OrderOf(gw, gc));
        Check(Sum(generalist, CraftGood.Housing, p => p.Output) > 0 && Sum(generalist, CraftGood.Cloth, p => p.Output) <= 1e-9, "a Craftsperson honours the same value order across roles");
        // Coverage is conserved: total Hand Tools consumed never exceed stock.
        Check(value.Production.Sum(p => p.HandToolsConsumed) <= 6 + 1e-9, "coverage spent never exceeds the Hand Tools held");
        if (Info) Console.WriteLine("WP3B_INFO cross-role housing=" + housingV + " cloth=" + clothV + " uniform cloth=" + Sum(uniform, CraftGood.Cloth, p => p.Output));
    }

    // Coverage producers rank above Housing Material for Wood, so the tools that make
    // next turn's coverage are not starved by a higher crafting priority for shelter.
    private static void ToolsBeforeHousingMaterial()
    {
        WorldState World(out Camp c)
        {
            var w = NeolithicChecks.Fixture(); c = w.Camps[0];
            foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
            c.Population = 100; c.Inventory.Wood = 1; c.Inventory.Clay = 0; c.Inventory.Stone = 1000; c.Inventory.HandTools = .5;
            c.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 0, Cloth = 0, FarmTools = 0, Housing = .20, Tools = .15 };
            c.Workforce.Assign(WorkforceRoles.Toolmaker, 1, c.Population); c.Workforce.Assign(WorkforceRoles.Builder, 1, c.Population);
            w.Config.Neolithic.PlannerFulfilment = "Value";
            return w;
        }
        var w1 = World(out var c1); var order = OrderOf(w1, c1);
        Check(order.Outranks(CraftGood.Tools, CraftGood.HousingMaterial) && order.Outranks(CraftGood.Tools, CraftGood.Housing), "Regular Tools outrank Housing Material through their derived value although Housing has the higher priority");
        var value = Resolve(w1, c1, order);
        var w2 = World(out var c2); var priority = Resolve(w2, c2, null);
        double toolsValue = Sum(value, CraftGood.Tools, p => p.Output), toolsPriority = Sum(priority, CraftGood.Tools, p => p.Output);
        Check(toolsPriority <= 1e-9 && toolsValue > 0, "Tools are made under the Value order and starved by Housing Material's priority claim without it (" + toolsValue + " vs " + toolsPriority + ")");
        if (Info) Console.WriteLine("WP3B_INFO tools value=" + toolsValue + " priority=" + toolsPriority + " HM wood value=" + Sum(value, CraftGood.HousingMaterial, p => p.Wood) + " priority=" + Sum(priority, CraftGood.HousingMaterial, p => p.Wood) + " housing value=" + Sum(value, CraftGood.Housing, p => p.Output) + " priority=" + Sum(priority, CraftGood.Housing, p => p.Output) + " toolsWood=" + Sum(value, CraftGood.Tools, p => p.Wood));
        Near(Sum(value, CraftGood.HousingMaterial, p => p.Wood) + Sum(value, CraftGood.Tools, p => p.Wood), 1, "Housing Material gets exactly the Wood the tools leave");
        Check(Sum(value, CraftGood.Housing, p => p.Output) > Sum(priority, CraftGood.Housing, p => p.Output) + 1e-9, "the tools' extra coverage lets more Housing be built from the same Wood");
        Near(w1.Camps[0].Inventory.Wood + value.Production.Sum(p => p.Wood), 1, "Wood is conserved under the Value order");
        // The planner's own forecast honours the order: Tools see Wood ahead of Housing.
        var recipes = NeolithicProduction.Recipes(w1.Config);
        var w3 = World(out var c3); var forecastOrder = OrderOf(w3, c3);
        var forecast = MaterialEconomy.Forecast(w3, c3, recipes, 80, 20, new ToolCoverageBudget(w3, c3, 80).Remaining, true, c3.Population, false, false, -1, -1, null, forecastOrder);
        var plan = MaterialEconomy.PlanHousingMaterials(w3, c3, recipes, forecast, 20, w3.Days, null, forecastOrder);
        Check(plan.LimitDirect(1, 0, Recipe(w3, CraftGood.Tools), 5) > 0, "the fixed plan leaves Tools their Wood when they outrank Housing Material");
    }

    // Uncontended single camp: the planner's forecast is what the resolver realizes.
    private static void ForecastMatchesResolver()
    {
        var (w, c) = Fixture(true, 0, 0, 0);
        c.Population = 44; c.PlantFood = 100000; c.Inventory.HandTools = 30; c.Inventory.Cloth = 0; c.Workforce.Assignments.Clear();
        w.Config.AnnualHumanGrowthRate = 0;
        var intent = EconomicPlanner.Plan(w, c, EconomicIntent.From(c), EconomicPlanner.ConfiguredRule(w.Config), out _, out var order);
        Check(order != null, "the Value plan returns its order");
        EconomicIntents.Apply(c, intent);
        double equipment = new ToolCoverageBudget(w, c, 80).Remaining;
        var recipes = NeolithicProduction.Recipes(w.Config);
        var forecast = MaterialEconomy.Forecast(w, c, recipes, 80, 20, equipment, false, -1, true, true, -1, -1, null, order);
        var needs = new TurnNeeds { Camp = c.Id, Residents = c.Population, Order = order, Production = forecast };
        needs.Housing = MaterialEconomy.PlanHousingMaterials(w, c, recipes, forecast, 20, w.Days, null, order);
        var record = new TurnRecord(); NeolithicProduction.ResolveCampNeolithic(w, c, 80, 20, record, 0, needs);
        int compared = 0;
        foreach (var good in new[] { CraftGood.HandTools, CraftGood.Tools, CraftGood.Cloth, CraftGood.Housing, CraftGood.HousingMaterial })
        {
            Near(Sum(record, good, p => p.Output), forecast[good], "resolver realizes the forecast " + good, 1e-6);
            if (forecast[good] > 0) compared++;
        }
        Check(compared >= 2, "the fixture produced " + compared + " goods to compare: " + string.Join(",", forecast.Select(p => p.Key + "=" + p.Value.ToString("F2"))) + " staffing " + string.Join(",", intent.Assignments.Select(a => a.Role + a.Population)) + " order " + order);
        Check(Sum(record, CraftGood.Housing, p => p.Output) > 0 || Sum(record, CraftGood.Cloth, p => p.Output) > 0, "the planned staffing produces goods (" + string.Join(",", intent.Assignments.Select(a => a.Role + a.Population)) + ")");
    }

    private static void NotInSavesOrFingerprints()
    {
        var (w, c) = Fixture(true, 6, 190, 0);
        string before = SaveCodec.Fingerprint(w); byte[] bytes = SaveCodec.Save(w);
        var order = OrderOf(w, c); EconomicPlanner.Plan(w, c, EconomicIntent.From(c), EconomicPlanner.ConfiguredRule(w.Config), out _, out var planned);
        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.NoOp, c.Id) { RequestAutomaticEconomy = true } } });
        Check(prepared.ProductionOrders.TryGetValue(c.Id, out var carried) && carried.ToString() == order.ToString() && prepared.Needs[c.Id].Order == carried, "PreparedTurn carries the order per automated camp and mirrors it in the camp's needs");
        Check(planned.ToString() == order.ToString(), "the planner's order equals the demand pass order");
        Check(before == SaveCodec.Fingerprint(w) && bytes.SequenceEqual(SaveCodec.Save(w)), "computing and preparing an order leaves the fingerprint and saved bytes unchanged");
        // The order is a pure function of the committed boundary.
        Check(OrderOf(w, c).ToString() == order.ToString(), "the order is deterministic");
        // A manual camp (no automation request) gets none.
        var manual = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>> { [0] = new List<Order>() });
        Check(manual.ProductionOrders.Count == 0, "manual camps carry no order");
    }

    private static string Sig(PreparedTurn p)
        => string.Join(";", p.ProductionOrders.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value + ":" + string.Join(",", x.Value.Goods.Select(g => x.Value.Key(g).ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));

    // Draft reuse stays exact and the order is independent of workers and entity order.
    private static void DraftAndDeterminism()
    {
        var reference = new WorldGenerator().Generate(53, SimulationConfig.Small(4));
        reference.Config.Neolithic.PlannerFulfilment = "Value";
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 3, true, 1) };
        var worlds = workers.Select(x => SaveCodec.Load(SaveCodec.Save(reference))).ToArray();
        Check(worlds.All(x => x.Config.Neolithic.ValueFulfilmentSelected), "replay worlds carry the Value config");
        int turns = 0, post = 0, orders = 0; long reused = 0;
        while ((!reference.Finished && turns < 12) || post < 8)
        {
            if (turns >= 12 && !reference.Finished) throw new Exception("Production order replay opening did not finish within 12 turns.");
            if (reference.Finished) post++;
            var plan = AiPlanning.Decide(reference);
            var refPrepared = TurnPlanner.Prepare(reference, plan.Orders.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList()), plan.Traces);
            string refSig = Sig(refPrepared); orders += refPrepared.ProductionOrders.Count;
            new TurnResolver().Resolve(reference, refPrepared);
            for (int i = 0; i < worlds.Length; i++)
            {
                var w = worlds[i]; w.Camps.Reverse(); w.Hunters.Reverse(); w.Herds.Reverse();
                byte[] before = SaveCodec.Save(w);
                var draft = TurnPlanner.Draft(w, 0, workers[i]);
                Check(before.SequenceEqual(SaveCodec.Save(w)), "the Value draft does not change the world at turn " + turns);
                var full = AiPlanning.Decide(w, workers[i]);
                var merged = draft.Ai.Orders.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList());
                merged[0] = full.Orders[0].Select(o => o.Copy()).ToList();
                var traces = draft.Ai.Traces.Where(t => t.Player != 0).Concat(full.Traces.Where(t => t.Player == 0)).OrderBy(t => t.Player).ToList();
                var plain = TurnPlanner.Prepare(w, merged.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList()), traces, workers[i]);
                var prepared = TurnPlanner.Prepare(w, merged, traces, workers[i], draft);
                reused += TurnDiagnostics.For(w).DraftIntentsReused;
                Check(Sig(plain) == Sig(prepared), "a reused draft carries the same order as a fresh plan at turn " + turns + " world " + i);
                Check(Sig(prepared) == refSig, "the order is independent of workers, partitions and entity order at turn " + turns + " world " + i);
                new TurnResolver(workers[i]).Resolve(w, prepared);
                Check(SaveCodec.Fingerprint(reference) == SaveCodec.Fingerprint(w), "Value draft replay matches the scalar reference at turn " + turns + " world " + i);
            }
            turns++;
        }
        Check(orders > 0, "the replay exercised production orders (" + orders + ")");
        Check(reused > 0, "the replay reused draft intents (" + reused + ")");
        if (Info) Console.WriteLine("WP3B_INFO replay turns=" + turns + " orders=" + orders + " reused=" + reused);
    }
}
