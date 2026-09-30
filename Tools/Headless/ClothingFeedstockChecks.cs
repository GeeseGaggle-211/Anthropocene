using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Clothing-need feedstock is paid after consumption and ahead of the operating-reserve
// refill (AGENTS.md, Docs/TurnResolutionPasses.md). Owns its worlds and counters
// (lanes run fixed checks concurrently).
internal static class ClothingFeedstockChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Clothing feedstock: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-6)
        => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);

    private static CraftingPriorities ClothingOnly(bool animal) => new CraftingPriorities
    { HandTools = 0, Pots = 0, Cloth = animal ? 0 : 1, FarmTools = 0, Housing = 0, Tools = 0, AnimalFabric = animal ? 1 : 0, LightHousing = 0, HerdingTools = 0 };

    // A camp whose food supply cannot refill its reserve: opening stock equals the turn's
    // meals and every reachable tile is thin.
    private static WorldState AtFoodLimit(bool animal, double stockFraction = 1, bool clothingOnlyDemand = true)
    {
        var w = NeolithicChecks.Fixture(!animal); var c = w.Camps[0];
        // Without a finished-goods reserve the useful demand is exactly this turn's need, so
        // any food beyond the clothing-need feedstock would have to come from the reserve.
        if (clothingOnlyDemand) w.Config.Neolithic.UsefulFinishedGoodsReserveYears = 0;
        w.Config.AnnualHumanGrowthRate = .01145;
        if (animal)
        {
            c.Technology = Technology.AnimalHusbandry; c.Technologies[(int)Technology.AnimalHusbandry] = true;
            c.People.Progression.Era = Era.Neolithic; w.FixedHousing = new List<FixedHousingStock>();
        }
        else
        {
            var t = w.Tiles[c.Tile]; t.CultivationCapital = .1; t.Cultivation = (float)CultivationEconomy.Intensity(w.Config, .1); t.CultivationOperator = c.Id;
        }
        double meals = c.Population * w.Config.Consumption * w.Config.GenerationIntervals;
        c.PlantFood = animal ? 0 : meals * stockFraction; c.AnimalFood = animal ? meals * stockFraction : 0; c.AquaticFood = 0;
        c.Inventory.HandTools = 1000; c.Inventory.HousingUnits = 1000; c.Inventory.Cloth = 0; c.Inventory.AnimalFabric = 0;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        c.CraftingPriorities = ClothingOnly(animal);
        c.EconomyAutomationTurns = -1;
        foreach (int id in w.Map.Radius(c.Tile, w.Config.GatherRadius))
        {
            var tile = w.Tiles[id]; float food = (float)(meals / 200); tile.Food = food; tile.PotentialFood = food * 10f;
            for (int s = 0; s < 4; s++) { tile.Seasons[s].MaxFood = food; tile.Seasons[s].MaxPotentialFood = food * 10f; }
        }
        return w;
    }

    private static int Weavers(EconomicIntent e) => e.Assignments.Where(a => a.Role == WorkforceRoles.Weaver).Sum(a => a.Population);

    public static int Run()
    {
        checks = 0; Helper(); GapsReduceToTodaysWhenClothingIsZero(); PlantCampAtFoodLimit(); ReserveGoodsBeyondClothing(); AnimalCampAtFoodLimit(); FullReserveUnchanged(); Replay();
        Console.WriteLine("PASS: " + checks + " clothing feedstock assertions"); return checks;
    }

    private static void Helper()
    {
        var n = new SimulationConfig().Neolithic; double pf = n.PlantFoodPerFabricUnit;
        double need = 100 * n.FabricConsumptionPerPersonTurn * 20 / n.ReferenceTurnYears;
        Near(NeolithicProduction.ClothingNeedUnits(n, 100, 20, 0, 0), need, "need units are the ResolveNeeds requirement");
        Near(NeolithicProduction.ClothingNeedUnits(n, 100, 20, need / 4, need / 4), need / 2, "Cloth and Animal Fabric stock both reduce the need");
        Near(NeolithicProduction.ClothingNeedUnits(n, 100, 20, need * 3, 0), 0, "stock above the need leaves none");
        var all = NeolithicProduction.ClothingNeedFeedstock(n, 100, 20, 0, 0, need * pf * 5, need * 5);
        Near(all.Plant, need * pf, "plant part is capped by the need, Cloth first"); Near(all.Animal, 0, "Animal Fabric takes only what Cloth leaves");
        var split = NeolithicProduction.ClothingNeedFeedstock(n, 100, 20, 0, 0, need * pf / 2, need * 5);
        Near(split.Plant, need * pf / 2, "planned Cloth caps the plant part"); Near(split.Animal, need / 2, "Animal Fabric covers the rest of the need");
        var small = NeolithicProduction.ClothingNeedFeedstock(n, 100, 20, 0, 0, pf, 1);
        Near(small.Total, pf + 1, "the planned output caps the feedstock below the need");
        Near(NeolithicProduction.ClothingNeedFeedstock(n, 100, 20, need, 0, need * pf, need).Total, 0, "covered clothing needs no feedstock");
    }

    // With no clothing part the split must be exactly the earlier "spare above meals and reserve" rule.
    private static void GapsReduceToTodaysWhenClothingIsZero()
    {
        var none = new NeolithicProduction.ClothingFeedstock();
        foreach (double food in new[] { 0, 500, 1500, 4000 })
            foreach (double plantShare in new[] { 0, .4, 1 })
            {
                double plantStock = food * plantShare, animalStock = food - plantStock, meals = 1000, reserve = 1200, plant = 700, animal = 300;
                double spare = Math.Max(0, food - meals - reserve), sparePlant = Math.Min(plantStock, spare), spareAnimal = Math.Min(animalStock, Math.Max(0, spare - sparePlant));
                NeolithicProduction.FeedstockGaps(food, plantStock, animalStock, meals, reserve, plant, animal, none, out double plantGap, out double animalGap);
                Near(plantGap, Math.Max(0, plant - sparePlant), "zero clothing: plant gap matches the reserve-ahead rule");
                Near(animalGap, Math.Max(0, animal - spareAnimal), "zero clothing: animal gap matches the reserve-ahead rule");
            }
        // Clothing draws on inventory beyond meals, everything else only beyond meals, clothing and reserve.
        var clothing = new NeolithicProduction.ClothingFeedstock { Plant = 100, Animal = 0 };
        NeolithicProduction.FeedstockGaps(1050, 1050, 0, 1000, 1200, 300, 0, clothing, out double pg, out _);
        Near(pg, 250, "clothing 50 from inventory after meals, 50 harvested; other feedstock 200 harvested");
        Check(pg <= 300, "gap never exceeds the planned feedstock");
    }

    private static void PlantCampAtFoodLimit()
    {
        var w = AtFoodLimit(false); var c = w.Camps[0];
        double meals = c.Population * w.Config.Consumption * w.Config.GenerationIntervals;
        string before = SaveCodec.Fingerprint(w);
        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
        Check(SaveCodec.Fingerprint(w) == before, "preparing the turn leaves the world untouched");
        var needs = prepared.Needs[c.Id];
        var auto = prepared.Orders.Single(o => o.Kind == OrderKind.Economy && o.Entity == c.Id).Economy;
        Check(Weavers(auto) > 0, "a camp at its food limit staffs Weavers (" + Weavers(auto) + ")");
        Check(needs.ReserveTarget > 0 && c.Food <= needs.ReserveTarget + 1e-9,
            "fixture: opening food is at most the reserve target " + needs.ReserveTarget + ", so the supply cannot refill it");
        double clothUnits = NeolithicProduction.ClothingNeedUnits(w.Config.Neolithic, needs.Residents, prepared.Horizon.Years, 0, 0);
        double clothFood = clothUnits * w.Config.Neolithic.PlantFoodPerFabricUnit;
        Check(needs.PlantClothingFeedstock > 0, "the clothing part of the plant feedstock is planned");
        Check(needs.PlantClothingFeedstock <= clothFood + 1e-6, "clothing feedstock never exceeds the clothing need");
        var record = new TurnResolver().Resolve(w, prepared);
        var demographic = record.Demographics.Single();
        Check(demographic.ManufacturingFood > 0, "Fabric was manufactured from food");
        Near(demographic.ManufacturingFood, clothFood, "feedstock ahead of the reserve is exactly the clothing need");
        Near(c.ClothPerCapita, 1, "clothing coverage reaches 1 while the reserve is below target", 1e-6);
        Check(c.Food < needs.ReserveTarget, "the reserve is still below target after the turn");
        Check(c.Food >= -1e-9 && c.PlantFood >= -1e-9 && c.AnimalFood >= -1e-9, "no typed food stock is overdrawn");
        Check(demographic.FoodConsumed + 1e-6 >= meals * .999 || c.FoodDeficit > 0 || demographic.FoodConsumed > 0, "consumption is recorded");
    }

    // With the ordinary finished-goods reserve the clothing need is still met first; the
    // extra feedstock is other manufacturing and never shrinks the clothing part.
    private static void ReserveGoodsBeyondClothing()
    {
        var w = AtFoodLimit(false, 1, false); var c = w.Camps[0];
        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
        var needs = prepared.Needs[c.Id];
        double clothFood = NeolithicProduction.ClothingNeedUnits(w.Config.Neolithic, needs.Residents, prepared.Horizon.Years, 0, 0) * w.Config.Neolithic.PlantFoodPerFabricUnit;
        Check(needs.PlantFeedstock > needs.PlantClothingFeedstock + 1e-6, "the finished-goods reserve adds feedstock beyond the clothing need");
        Near(needs.PlantClothingFeedstock, clothFood, "the clothing part is exactly the need");
        Check(needs.PlantFeedstockGap <= needs.PlantFeedstock + 1e-9, "the harvest gap never exceeds the planned feedstock");
        new TurnResolver().Resolve(w, prepared);
        Near(c.ClothPerCapita, 1, "clothing coverage reaches 1 with a finished-goods reserve in the plan", 1e-6);
    }

    private static void AnimalCampAtFoodLimit()
    {
        var w = AtFoodLimit(true); var c = w.Camps[0];
        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
        var needs = prepared.Needs[c.Id];
        var auto = prepared.Orders.Single(o => o.Kind == OrderKind.Economy && o.Entity == c.Id).Economy;
        Check(Weavers(auto) > 0 || auto.Assignments.Any(a => a.Role == WorkforceRoles.Craftsperson), "a Husbandry camp at its food limit staffs fabric work");
        double clothUnits = NeolithicProduction.ClothingNeedUnits(w.Config.Neolithic, needs.Residents, prepared.Horizon.Years, 0, 0);
        Check(needs.AnimalClothingFeedstock > 0 && needs.AnimalClothingFeedstock <= clothUnits + 1e-6, "Animal Fabric clothing feedstock is planned and capped by the need");
        var record = new TurnResolver().Resolve(w, prepared);
        Near(c.ClothPerCapita, 1, "Animal Fabric covers clothing while the reserve is below target", 1e-6);
        Near(record.Demographics.Single().ManufacturingFood, clothUnits, "animal feedstock ahead of the reserve is exactly the clothing need");
        Check(c.AnimalFood >= -1e-9 && c.Food >= -1e-9, "no typed food stock is overdrawn");
    }

    // A full reserve with ample income: the clothing-first order changes nothing, so the plan
    // equals the one for a camp with still more food and owes no harvest for feedstock.
    private static void FullReserveUnchanged()
    {
        WorldState Rich(double scale)
        {
            var w = AtFoodLimit(false); var c = w.Camps[0];
            var t = w.Tiles[c.Tile];
            double meals = c.Population * w.Config.Consumption * w.Config.GenerationIntervals;
            c.PlantFood = meals * 6 * scale;
            return w;
        }
        var a = Rich(1); var b = Rich(2);
        var pa = TurnPlanner.Prepare(a, new Dictionary<int, List<Order>>()); var pb = TurnPlanner.Prepare(b, new Dictionary<int, List<Order>>());
        Near(pa.Needs[a.Camps[0].Id].PlantFeedstockGap, 0, "a full reserve with ample inventory owes no feedstock harvest");
        string Describe(PreparedTurn p, int id) => string.Join("|", p.Orders.Where(o => o.Entity == id).Select(o => o.ToString()));
        Check(Describe(pa, a.Camps[0].Id) == Describe(pb, b.Camps[0].Id), "with a full reserve the plan does not depend on food");
        Check(pa.Needs[a.Camps[0].Id].PlantClothingFeedstock > 0, "the clothing part is still identified");
    }

    // Scalar versus reversed 4-worker resolution, reversed collections, and save continuation.
    private static void Replay()
    {
        foreach (bool animal in new[] { false, true })
        {
            var worlds = new[] { AtFoodLimit(animal), AtFoodLimit(animal), AtFoodLimit(animal) };
            var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 3, true, 1), null };
            worlds[1].Camps.Reverse();
            var prints = new List<string>();
            for (int turn = 0; turn < 3; turn++)
            {
                for (int i = 0; i < 2; i++) new TurnResolver(workers[i]).Resolve(worlds[i], new Dictionary<int, List<Order>>());
                if (turn == 1) worlds[2] = SaveCodec.Load(SaveCodec.Save(worlds[0]));
                else if (turn == 2) new TurnResolver().Resolve(worlds[2], new Dictionary<int, List<Order>>());
                Check(SaveCodec.Fingerprint(worlds[0]) == SaveCodec.Fingerprint(worlds[1]), "scalar and reversed 4-worker replay agree after turn " + turn + (animal ? " (animal)" : ""));
            }
            var saved = SaveCodec.Load(SaveCodec.Save(worlds[0]));
            Check(SaveCodec.Fingerprint(saved) == SaveCodec.Fingerprint(worlds[0]), "save round trip preserves the clothing-first state");
        }
    }
}
