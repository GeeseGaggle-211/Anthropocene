using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class EconomicAutomationChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Automation: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-7) => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);
    private static WorldState Fixture(bool sources = true)
    {
        var w = NeolithicChecks.Fixture();
        if (sources)
        {
            int count = w.Tiles.Length;
            w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05 };
            int tile = w.Camps[0].Tile; w.Resources.Stone[tile] = 1000; w.Resources.Clay[tile] = 1000; w.Resources.Wood[tile] = 1000;
        }
        return w;
    }
    private static TurnRecord Resolve(WorldState w) => new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>());
    private static void Field(WorldState w, double e = .1)
    {
        var c = w.Camps[0]; var t = w.Tiles[c.Tile]; t.CultivationCapital = e; t.Cultivation = (float)CultivationEconomy.Intensity(w.Config, e); t.CultivationOperator = c.Id;
    }
    private static void OpeningFood(WorldState w, Camp camp, float food)
    {
        // The bucket fixtures need enough opening G because land renewal is
        // available to the next turn, not the turn making these claims.
        foreach (int id in w.Map.Radius(camp.Tile, 1))
        {
            var tile = w.Tiles[id]; tile.Food = food; tile.PotentialFood = food * 10;
            for (int s = 0; s < 4; s++) { tile.Seasons[s].MaxFood = food; tile.Seasons[s].MaxPotentialFood = food * 10; }
        }
    }
    private static void Budget(WorldState w, EconomicIntent e)
    {
        var c = w.Camps[0]; var observation = new AgentObservationBuilder().Build(w, c.Player);
        Check(OrderRules.Validate(observation, new Order(OrderKind.Economy, c.Id) { Economy = e }) == null, "automatic intent is legal");
        Check(e.Assignments.Sum(a => a.Population) <= c.Population, "one resident ledger");
    }
    public static int Run()
    {
        checks = 0; RolesAndRates(); Labor(); Planning(); DemandSnapshot(); Persistence(); PreparedBoundary(); DestinationPreparation(); LocalSourceIndex(); FabricDoesNotFundBirths(); FabricUsesPriorResidents(); StarvationUsesPriorCohort(); PlanningResidentPopulationIgnoresStaleIncome();
        Console.WriteLine("PASS: " + checks + " demand automation assertions"); return checks;
    }
    private static void RolesAndRates()
    {
        Check(EconomicIntents.RequiredTechnology(WorkforceRoles.ClayMiner) == NeolithicTech.Pottery, "Pottery unlocks Clay Miner");
        Check(WorkforceRoles.Label(WorkforceRoles.Stonecutter) == "Stone Quarrier" && WorkforceRoles.Stonecutter == "Stonecutter", "display rename retains saved role identifier");
        foreach (bool specialist in new[] { false, true })
        {
            var w = Fixture(); var c = w.Camps[0]; var r = new TurnRecord();
            string extraction = specialist ? WorkforceRoles.ClayMiner : WorkforceRoles.Gatherer;
            string manufacture = specialist ? WorkforceRoles.Potter : WorkforceRoles.Craftsperson;
            if (specialist) c.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
            var source = MaterialEconomy.KnownSources(w, c, 0).First(s => s.Material == MaterialId.Clay);
            Near(MaterialEconomy.Extract(w, c, source, extraction, 80, 1000, 0, r), 80, "one extractor's actual time");
            double material = c.Inventory.Clay, equipment = 80;
            Near(material, specialist ? 80 : 20, "matching specialist/generalist extraction throughput");
            // Tool wear is output-capacity based; provide one Hand Tool per
            // expected pot so this fixture continues to isolate extraction and
            // manufacturer rates.
            c.Inventory.HandTools = material;
            NeolithicProduction.Produce(w, c, NeolithicProduction.Recipes(w.Config).First(p => p.Good == CraftGood.Pots), manufacture, 80, 80, 1000, ref equipment, r);
            Near(c.Inventory.Pots, material, "one extractor supplies one matching manufacturer's single-input throughput");
            Near(c.Inventory.Clay, 0, "every produced pot pays clay input");
            Check(r.MaterialTransfers.All(t => Math.Abs(t.Before - t.After - t.Amount) < 1e-7), "source receipts reconcile");
        }
        var missing = Fixture(); var mc = missing.Camps[0]; var mr = new TurnRecord();
        var clay = MaterialEconomy.KnownSources(missing, mc, 0).First(s => s.Material == MaterialId.Clay);
        Near(MaterialEconomy.Extract(missing, mc, clay, WorkforceRoles.ClayMiner, 80, 80, 0, mr), 0, "locked extractor cannot operate");
        mc.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
        var stone = MaterialEconomy.KnownSources(missing, mc, 0).First(s => s.Material == MaterialId.Stone);
        Near(MaterialEconomy.Extract(missing, mc, stone, WorkforceRoles.ClayMiner, 80, 80, 0, mr), 0, "Clay Miner cannot quarry Stone");
    }
    private static void Labor()
    {
        var w = Fixture(); var c = w.Camps[0]; Field(w);
        c.Workforce.Assign(WorkforceRoles.Farmer, 60, 100);
        c.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 20, MaterialGatherers = 10 };
        var labor = CultivationEconomy.Prepare(w, c, 0);
        Near(labor.OperatingFarmers, 40, "harvesting farmers are not capped at maintenance minimum");
        Near(labor.ExpansionFarmers, 20, "expansion time separated"); Near(labor.FoodGatherers, 30, "material gathering time protected");
        Near(w.Tiles[c.Tile].CultivationOperatingFraction, 1, "extra harvesters do not multiply ecological operation");
        var record = Resolve(w);
        double gather = record.Transfers.Where(t => t.Kind.EndsWith("gathering") || t.Kind == "Gathering").Sum(t => t.Food) / w.Config.GatherPerPerson;
        double raw = record.MaterialTransfers.Where(t => t.Role == WorkforceRoles.Gatherer).Sum(t => t.WorkerIntervals);
        Check(gather <= 30 * 80 + 1e-6 && gather + raw <= 40 * 80 + 1e-6, "food and materials never duplicate gatherer time");
        Check(record.Transfers.Any(t => t.Kind == "Cultivation" && t.Tile != c.Tile && t.Food > 0), "farm operators harvest shared accessible footprint beyond their field");
        var zero = Fixture(); var zc = zero.Camps[0]; zc.Workforce.Assign(WorkforceRoles.Farmer, 40, 100); zc.EconomicLabor = new EconomicLaborPlan();
        Near(CultivationEconomy.Prepare(zero, zc, 0).OperatingFarmers, 0, "q zero remains expansion-only");
        Near(CultivationEconomy.Prepare(zero, zc, 0).ExpansionFarmers, 0, "explicit zero expansion is respected at q zero");
        zero.Config.MaintenanceCoefficient = 0; Field(zero); Near(CultivationEconomy.Prepare(zero, zc, 0).OperatingFarmers, 40, "zero upkeep does not erase explicit harvest labor");
        var paleo = NeolithicChecks.Fixture(false); var pe = EconomicPlanner.Plan(paleo, paleo.Camps[0], new EconomicIntent { AutomationTurns = -1 });
        Check(pe.Assignments.Count == 0 && pe.AutomationTurns == 0, "no Agriculture no farming automation");
    }
    private static void Planning()
    {
        var w = Fixture(); var c = w.Camps[0]; Field(w);
        c.Inventory.HandTools = 1000;
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        c.PotsTarget = 80; c.FarmToolsTarget = 80;
        string before = SaveCodec.Fingerprint(w);
        var e = EconomicIntents.Automatic(w, c, true); Budget(w, e);
        Check(before == SaveCodec.Fingerprint(w), "planner and observation do not mutate world or stocks");
        Check(e.Assignments.Any(a => a.Role == WorkforceRoles.Weaver) && e.Assignments.Any(a => a.Role == WorkforceRoles.Builder), "unlocked manufacturers adopted for useful needs");
        Check(e.Assignments.Any(a => a.Role == WorkforceRoles.ClayMiner), "pottery demand recruits Clay Miners");
        Check(!e.Assignments.Any(a => a.Role == WorkforceRoles.Craftsperson), "matching specialists replace generalist craft quotas");
        Check(e.Assignments.Any(a => a.Role == WorkforceRoles.Farmer), "established farmers chosen before supplementary gathering");
        c.CultivationFocusTile = 0; c.EconomyAutomationTurns = 1;
        var moved = EconomicIntents.Automatic(w, c, true); Budget(w, moved); Check(moved.Focus == -1, "stale field focus clears after relocation");
        Resolve(w); Check(c.EconomyAutomationTurns == 0 && c.CultivationFocusTile == -1, "invalid old focus cannot crash continuing automation");
        var locked = Fixture(); var lc = locked.Camps[0]; lc.Inventory.HandTools = 100;
        var le = EconomicIntents.Automatic(locked, lc, true); Budget(locked, le);
        Check(le.Assignments.All(a => EconomicIntents.RequiredTechnology(a.Role) == null), "no locked specialists in proposals");
        Check(le.Labor.MaterialGatherers > 0, "generalist extraction is budgeted explicitly");
        var noSource = Fixture(false); var nc = noSource.Camps[0]; nc.Inventory.HandTools = 100; nc.CraftingPriorities.Cloth = 0;
        var ne = EconomicIntents.Automatic(noSource, nc, true); Budget(noSource, ne);
        Check(!ne.Assignments.Any(a => a.Role == WorkforceRoles.Craftsperson || WorkforceRoles.Specialists.Contains(a.Role) || WorkforceRoles.Extractors.Contains(a.Role)), "missing materials do not recruit idle manufacturers or extractors");
        var stocked = Fixture(); var sc = stocked.Camps[0]; sc.Inventory.HandTools = 10000; sc.Inventory.FarmTools = 10000; sc.Inventory.Cloth = 10000; sc.Inventory.HousingUnits = 1000; sc.Inventory.Pots = 1000;
        var se = EconomicIntents.Automatic(stocked, sc, true); Budget(stocked, se);
        Check(!se.Assignments.Any(a => a.Role == WorkforceRoles.Craftsperson || WorkforceRoles.Specialists.Contains(a.Role)), "finished stock bridging removes redundant manufacture");
        sc.AnimalFood = 1000000; Near(TurnResolver.PlanningFoodDemand(stocked, sc), 0, "imported/stored food shares the useful demand budget");
        var scarce = Fixture(); var fc = scarce.Camps[0]; Field(scarce); fc.PlantFood = 0; fc.Inventory.HandTools = 100; fc.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        foreach (var t in scarce.Tiles) { t.Food = 0; for (int season = 0; season < t.Seasons.Length; season++) { t.Seasons[season].MaxFood = 40; t.Seasons[season].FoodRecoveryPerDay = .002f; t.Seasons[season].BiomassRecoveryPerDay = 0; } }
        var shortage = EconomicIntents.Automatic(scarce, fc, true); Budget(scarce, shortage);
        Check(!shortage.Assignments.Any(a => a.Role == WorkforceRoles.Weaver), "food needed for meals cannot also justify idle Weavers");
        foreach (int population in new[] { 1, 2, 7, 24, 250, 1000 })
        { var tiny = Fixture(); tiny.Camps[0].Population = population; Budget(tiny, EconomicIntents.Automatic(tiny, tiny.Camps[0], true)); }
    }
    // The planner's demand pass is pure and yields one claim per plannable good.
    private static void DemandSnapshot()
    {
        var w = Fixture(); var c = w.Camps[0]; Field(w);
        w.Config.Neolithic.PlannerFulfilment = "Uniform"; // pins the Uniform contract: claims carry Value = Priority and the swappable-rule fixtures below assume it (Value is covered by ValueFulfilmentChecks)
        c.Inventory.HandTools = 1000;
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        string before = SaveCodec.Fingerprint(w);
        var d = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(before == SaveCodec.Fingerprint(w), "demand pass does not mutate world or stocks");
        Check(d.Claims.Select(x => x.Good).SequenceEqual(d.Goods.Select(r => r.Good)), "one claim per plannable good in recipe order");
        Check(d.Claims.All(x => x.Quantity == d.Goals[x.Good] && x.Value == x.Priority), "claims carry the demand-pass quantities and default value");
        Check(d.Claims.Single(x => x.Good == CraftGood.FarmTools).Quantity == 0, "Farm Tools are sized from the bounded field workforce, not the demand pass");
        var cloth = d.Claims.Single(x => x.Good == CraftGood.Cloth);
        Check(cloth.Quantity > 0 && cloth.Role == WorkforceRoles.Weaver && cloth.Rate == cloth.Recipe.Rate, "unlocked specialists carry their full recipe rate");
        var again = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(again.Claims.Select(x => (x.Good, x.Quantity, x.Priority, x.Role, x.Rate)).SequenceEqual(d.Claims.Select(x => (x.Good, x.Quantity, x.Priority, x.Role, x.Rate))), "demand pass is deterministic");
        // Fulfilment rules are swappable: the uniform rule gives every claim the
        // path position, and a rule that withholds one claim removes its staff.
        var shares = new double[d.Claims.Count];
        UniformFulfilment.Instance.Shares(d.Claims, .375, shares);
        Check(shares.All(x => x == .375), "uniform rule shares the path position with every claim");
        var uniform = EconomicPlanner.Plan(w, c, EconomicIntent.From(c), UniformFulfilment.Instance);
        var withheld = EconomicPlanner.Plan(w, c, EconomicIntent.From(c), new WithholdRule(CraftGood.Cloth));
        Check(before == SaveCodec.Fingerprint(w), "rule-driven planning does not mutate world or stocks");
        Check(uniform.Assignments.Any(a => a.Role == WorkforceRoles.Weaver), "uniform rule staffs useful Cloth");
        Check(!withheld.Assignments.Any(a => a.Role == WorkforceRoles.Weaver), "a rule that withholds Cloth removes its Weavers");
        Check(withheld.Assignments.Sum(a => a.Population) <= c.Population, "rule-driven plans stay resident-bounded");
    }
    private sealed class WithholdRule : IFulfilmentRule
    {
        private readonly CraftGood withheld;
        public WithholdRule(CraftGood good) { withheld = good; }
        public string Name => "Withhold " + withheld;
        public void Shares(IReadOnlyList<ProductionClaim> claims, double s, double[] shares)
        {
            for (int i = 0; i < claims.Count; i++) shares[i] = claims[i].Good == withheld ? 0 : s;
        }
    }
    private static void Persistence()
    {
        var w = Fixture(); var c = w.Camps[0]; c.EconomyAutomationTurns = 2;
        var first = Resolve(w); Check(c.EconomyAutomationTurns == 1 && first.Orders.Any(s => s.Contains(":automation:")), "countdown runs on empty submission");
        Check(first.Production.Any(p => p.Good == CraftGood.HandTools && p.Output > 0), "zero-tool economy bootstraps through sourced useful production");
        // This fixture isolates the automation countdown. Its old 80-unit
        // land stock cannot feed 100 residents under opening-stock harvest.
        c.PlantFood = 100000;
        var copy = SaveCodec.Load(SaveCodec.Save(w)); Check(SaveCodec.Fingerprint(copy) == SaveCodec.Fingerprint(w), "active automation and labor reservations save exactly");
        Resolve(w); Resolve(copy); Check(c.EconomyAutomationTurns == 0 && SaveCodec.Fingerprint(copy) == SaveCodec.Fingerprint(w), "countdown/save continuation deterministic");
        string assignments = string.Join(",", c.Workforce.Assignments.Select(a => a.Role + a.Population));
        var third = Resolve(w); Check(!third.Orders.Any(s => s.Contains(":automation:")) && string.Join(",", c.Workforce.Assignments.Select(a => a.Role + a.Population)) == assignments, "expired automation retains manual assignments");
        c.EconomyAutomationTurns = -1; Resolve(w); Check(c.EconomyAutomationTurns == -1, "continuous automation does not expire");
        var off = EconomicIntent.From(c); off.AutomationTurns = 0;
        new TurnResolver().Resolve(w, new Dictionary<int, List<Order>> { [c.Player] = new List<Order> { new Order(OrderKind.Economy, c.Id) { Economy = off } } });
        Check(c.EconomyAutomationTurns == 0 && !w.History.Last().Orders.Any(s => s.Contains(":automation:")), "explicit off takes effect before current turn automation");
        var bad = EconomicIntent.From(c); bad.AutomationTurns = -2;
        Check(new TurnResolver().Validate(w, c.Player, new Order(OrderKind.Economy, c.Id) { Economy = bad }) != null, "invalid countdown rejected");
        bad.AutomationTurns = 0; bad.Labor = new EconomicLaborPlan { MaterialGatherers = double.NaN };
        Check(new TurnResolver().Validate(w, c.Player, new Order(OrderKind.Economy, c.Id) { Economy = bad }) != null, "nonfinite labor reservation rejected");
        bad.Labor.MaterialGatherers = c.Population + 1;
        Check(new TurnResolver().Validate(w, c.Player, new Order(OrderKind.Economy, c.Id) { Economy = bad }) != null, "phantom material workforce rejected");
        c.EconomicLabor = new EconomicLaborPlan(); c.EconomyAutomationTurns = 0;
        var zero = SaveCodec.Load(SaveCodec.Save(w)).Camps[0]; Check(zero.EconomicLabor != null && zero.EconomicLabor.Values().All(v => v == 0) && zero.EconomyAutomationTurns == 0, "explicit zero plan and off survive saving");
    }
    private static void PreparedBoundary()
    {
        var w = Fixture(); var c = w.Camps[0]; c.EconomyAutomationTurns = -1;
        string before = SaveCodec.Fingerprint(w);
        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
        Check(SaveCodec.Fingerprint(w) == before, "preparing automation, needs and orders leaves authoritative state untouched");
        var resolver = new TurnResolver(); var record = resolver.Resolve(w, prepared);
        Check(record.Orders.Count(s => s.Contains(":automation:")) == 1, "prepared automatic intent is applied once");
        bool staleRejected = false;
        try { resolver.Resolve(w, prepared); } catch (InvalidOperationException) { staleRejected = true; }
        Check(staleRejected, "a prepared turn cannot be reused after its boundary advances");
        Check(resolver.LastMetrics.PlanCalls >= 1 && resolver.LastMetrics.ForecastCalls >= 1,
            "transient counters include interturn planning and needs forecasts");
        var stocked = Fixture(); var sc = stocked.Camps[0];
        sc.Inventory.HandTools = 1000; sc.Workforce.Assign(WorkforceRoles.Craftsperson, 10, sc.Population);
        sc.Food = sc.Population * stocked.Config.Consumption * stocked.Config.GenerationIntervals;
        sc.PlantFood = sc.Food;
        var locked = TurnPlanner.Prepare(stocked, new Dictionary<int, List<Order>>()).Needs[sc.Id];
        Check(locked.PlantFeedstock > 0, "the prepared craft job requests plant feedstock");
        Near(locked.PlantFeedstockGap, locked.PlantFeedstock,
            "food reserved for the prior cohort cannot substitute for craft feedstock");
    }
    private static void DestinationPreparation()
    {
        var world = Fixture(); var camp = world.Camps[0]; camp.EconomyAutomationTurns = -1;
        int origin = camp.Tile;
        int destination = world.Map.Neighbors(origin).Where(id => world.Tiles[id].TraversableLand).OrderBy(id => id).First();
        int newSource = world.Map.Neighbors(destination).Where(id => world.Map.Distance(origin, id) == 2 && world.Tiles[id].TraversableLand).OrderBy(id => id).First();
        world.Resources.Stone[origin] = 0; world.Resources.Stone[newSource] = 1000;
        var move = new Order(OrderKind.Move, camp.Id, destination: destination);
        var submitted = new Dictionary<int, List<Order>> { [camp.Player] = new List<Order> { move } };
        string before = SaveCodec.Fingerprint(world);
        var prepared = TurnPlanner.Prepare(world, submitted);
        Check(SaveCodec.Fingerprint(world) == before && camp.Tile == origin, "destination preparation is read-only");
        Check(prepared.Placement.Destinations[camp.Id] == destination && prepared.Orders.Any(o => o.Kind == OrderKind.Move && o.Entity == camp.Id),
            "an accepted movement order survives the automatic economic pass");
        var automatic = prepared.Orders.Single(o => o.Kind == OrderKind.Economy && o.Entity == camp.Id);
        Check(prepared.AutomatedCamps.Contains(camp.Id) && prepared.OrderReceipts.Count(s => s.Contains(":automation:")) == 1,
            "a moving camp receives one automatic economic plan");
        var projected = EconomicPlanner.Preview(camp, EconomicIntent.From(camp), camp.Population); projected.Tile = destination;
        Check(MaterialEconomy.KnownSources(world, projected, world.Days).Any(s => s.Tile == newSource && s.Material == MaterialId.Stone),
            "destination planning can see a newly adjacent material source");
        var expected = EconomicPlanner.Plan(world, projected, EconomicIntent.From(camp));
        Check(automatic.Economy.Stamp == expected.Stamp, "the automatic intent is planned for the effective destination");
        var record = new TurnResolver().Resolve(world, prepared);
        Check(camp.Tile == destination && record.Orders.Count(s => s.Contains(":automation:")) == 1,
            "movement and the single prepared intent commit in one turn");

        var rejected = Fixture(); var rejectedCamp = rejected.Camps[0]; rejectedCamp.EconomyAutomationTurns = -1;
        var invalid = EconomicIntent.From(rejectedCamp); invalid.AutomationTurns = -2;
        var rejectedTurn = TurnPlanner.Prepare(rejected, new Dictionary<int, List<Order>>
            { [rejectedCamp.Player] = new List<Order> { new Order(OrderKind.Economy, rejectedCamp.Id) { Economy = invalid } } });
        Check(rejectedTurn.Rejections.Count == 1 && rejectedTurn.Orders.Count(o => o.Kind == OrderKind.Economy && o.Entity == rejectedCamp.Id) == 1 &&
            rejectedTurn.AutomatedCamps.Contains(rejectedCamp.Id),
            "an invalid explicit economic order cannot suppress enabled automation");

        var blocked = Fixture(); var mover = blocked.Camps[0]; mover.EconomyAutomationTurns = -1;
        int occupied = blocked.Map.Neighbors(mover.Tile).Where(id => blocked.Tiles[id].TraversableLand).OrderBy(id => id).First();
        var stationary = new Camp { Id = blocked.NextId++, Player = mover.Player, SocietyId = mover.SocietyId,
            Tile = occupied, Population = 10 }; stationary.Bind(mover.People);
        blocked.Camps.Add(stationary);
        var associatedHunter = new Hunter { Id = blocked.NextId++, Player = mover.Player, SocietyId = mover.SocietyId,
            SourceCamp = mover.Id, Tile = mover.Tile, Population = 8 };
        blocked.Hunters.Add(associatedHunter);
        var attempted = new Dictionary<int, List<Order>> { [mover.Player] = new List<Order> { new Order(OrderKind.Move, mover.Id, destination: occupied) } };
        var blockedTurn = TurnPlanner.Prepare(blocked, attempted);
        Check(blockedTurn.Placement.Destinations[mover.Id] == mover.Tile && blockedTurn.Placement.Blocked.Contains(mover.Id),
            "stationary camp wins the pure placement snapshot");
        var blockedIntent = blockedTurn.Orders.Single(o => o.Kind == OrderKind.Economy && o.Entity == mover.Id);
        var originIntent = EconomicPlanner.Plan(blocked, mover, EconomicIntent.From(mover));
        Check(blockedIntent.Economy.Stamp == originIntent.Stamp, "blocked movement prepares the original camp site");
        var logistics = new TurnRecord();
        Supply.ProvisionDepartures(blocked, blockedTurn.Orders, logistics, blockedTurn.Placement);
        Check(associatedHunter.CarriedFood == 0 && logistics.Logistics.All(l => l.ProvisionsLoaded == 0),
            "a blocked camp move does not provision hunters for a departure that never happens");

        var manual = Fixture(); var manualCamp = manual.Camps[0]; manualCamp.EconomyAutomationTurns = 0;
        manualCamp.Workforce.Assign(WorkforceRoles.Gatherer, 10, manualCamp.Population);
        int manualTarget = manual.Map.Neighbors(manualCamp.Tile).Where(id => manual.Tiles[id].TraversableLand).OrderBy(id => id).First();
        var manualPrepared = TurnPlanner.Prepare(manual, new Dictionary<int, List<Order>>
            { [manualCamp.Player] = new List<Order> { new Order(OrderKind.Move, manualCamp.Id, destination: manualTarget) } });
        Check(!manualPrepared.Orders.Any(o => o.Kind == OrderKind.Economy && o.Entity == manualCamp.Id) &&
            manualCamp.Workforce.Assignments.Single().Population == 10, "automation-off movement retains manual staffing");
        var aiWorld = Fixture(); var aiCamp = aiWorld.Camps[0]; aiCamp.EconomyAutomationTurns = 0;
        var aiChoice = AiPlanning.Decide(aiWorld);
        var aiPrepared = TurnPlanner.Prepare(aiWorld, aiChoice.Orders, aiChoice.Traces);
        Check(aiChoice.Orders[aiCamp.Player].Single(o => o.Entity == aiCamp.Id).RequestAutomaticEconomy &&
            aiPrepared.Orders.Count(o => o.Kind == OrderKind.Economy && o.Entity == aiCamp.Id) == 1,
            "AI keeps an interturn economic proposal when its movement action occupies the camp order slot");

        var many = NeolithicChecks.Fixture(false); var first = many.Camps[0];
        first.Technology = Technology.AnimalHusbandry;
        first.Technologies[(int)Technology.AnimalHusbandry] = true;
        var secondTile = many.Map.Neighbors(first.Tile).Where(id => many.Tiles[id].TraversableLand).OrderBy(id => id).First();
        var second = new Camp { Id = many.NextId++, Player = first.Player, SocietyId = first.SocietyId,
            Tile = secondTile, Population = 20, EconomyAutomationTurns = -1 };
        second.Bind(first.People); many.Camps.Add(second);
        many.Herds.Add(new Herd { Id = many.NextId++, Tile = secondTile, DomesticCamp = second.Id,
            OwnerSociety = second.SocietyId, Animals = 20, StartingAnimals = 20,
            SurvivingAnimals = 10, LastNutrition = .6, ManagementFulfillment = 1 });
        var index = new PlanningIndex(many);
        var secondView = new AgentObservation(many, second, index, false);
        double filtered = ManagedBreedingPolicy.Recommend(secondView, secondView.Settlements.Single(s => s.Id == second.Id));
        double local = ManagedBreedingPolicy.Recommend(many, second, secondTile, index);
        Near(local, filtered, "local breeding recommendation matches the filtered view for a second same-player camp");
    }
    private static void LocalSourceIndex()
    {
        var w = Fixture(); var c = w.Camps[0]; int origin = c.Tile;
        int neighbor = w.Map.Neighbors(origin).Where(id => w.Tiles[id].TraversableLand).OrderBy(id => id).First();
        int distant = w.Map.Neighbors(neighbor).Where(id => w.Tiles[id].TraversableLand && w.Map.Distance(origin, id) == 2).OrderBy(id => id).First();
        w.Resources.Stone[origin] = 0; w.Resources.Stone[distant] = 1000;
        Check(!MaterialEconomy.KnownSources(w, c, w.Days).Any(s => s.Material == MaterialId.Stone),
            "a camp cannot scan a source beyond its one-turn radius");
        c.Tile = neighbor;
        var visible = MaterialEconomy.KnownSources(w, c, w.Days).Single(s => s.Material == MaterialId.Stone);
        Check(visible.Tile == distant && visible.Available > 0, "moving rebuilds local adjacency at the new site");
        var second = new Camp { Id = w.NextId++, Player = c.Player, SocietyId = c.SocietyId, Tile = distant, Population = 10 };
        second.Bind(c.People);
        var receipt = new TurnRecord();
        MaterialEconomy.Extract(w, c, visible, WorkforceRoles.Gatherer, 40, 20, w.Days, receipt);
        double used = receipt.MaterialTransfers.Sum(t => t.Amount);
        var remaining = MaterialEconomy.KnownSources(w, second, w.Days).Single(s => s.Material == MaterialId.Stone && s.Tile == distant);
        Near(remaining.Available, visible.Available - used, "shared indexed source reports only the debited remainder");
    }
    private static void FabricDoesNotFundBirths()
    {
        var w = Fixture(); var c = w.Camps[0]; Field(w); w.Config.AnnualHumanGrowthRate = .01145;
        OpeningFood(w, c, 2000);
        c.PlantFood = 8000; c.Inventory.HandTools = 100; c.Inventory.Cloth = 100; c.Inventory.HousingUnits = 100;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        c.Workforce.Assign(WorkforceRoles.Farmer, 40, 100); c.Workforce.Assign(WorkforceRoles.Weaver, 2, 100);
        c.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 2.5, MaterialGatherers = 58 };
        var r = Resolve(w);
        Near(r.Transfers.Where(t => t.Kind == "Cultivation").Sum(t => t.Food), 8100, "controlled harvest funds consumption plus Fabric only", 1e-5);
        Check(r.Growth == 0 && c.Population == 100, "manufacturing feedstock is not reused to fund births");
        Near(r.Demographics.Single().ManufacturingFood, 100, "planned plant input actually consumed");
        Near(c.Food, 8000, "full operating reserve retained after manufacturing", 1e-5);
    }
    private static void FabricUsesPriorResidents()
    {
        WorldState GrowingFixture(bool hunter)
        {
            var w = Fixture(); var c = w.Camps[0]; Field(w); w.Config.MaintenanceCoefficient = 0; w.Config.AnnualHumanGrowthRate = .01145;
            OpeningFood(w, c, 2000);
            c.Population = 10; c.PlantFood = c.AnimalFood = c.AquaticFood = 0; c.Inventory.HandTools = 100; c.Inventory.HousingUnits = 10;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
            c.Workforce.Assign(WorkforceRoles.Farmer, 9, c.Population); c.Workforce.Assign(WorkforceRoles.Weaver, 1, c.Population);
            c.EconomicLabor = new EconomicLaborPlan();
            if (hunter)
            {
                // Give the linked group a stock buffer so this case isolates
                // reproductive population from Clothing's resident population.
                c.AnimalFood = 4000;
                w.Hunters.Add(new Hunter { Id = w.NextId++, Player = c.Player, SourceCamp = c.Id, SocietyId = c.SocietyId, Tile = c.Tile, Population = 4 });
            }
            return w;
        }

        var w = GrowingFixture(false); var c = w.Camps[0]; var record = Resolve(w);
        Check(record.Growth > 0 && c.Population > 10, "zero-reserve fixture grows after needs resolve");
        Near(record.Demographics.Single().ManufacturingFood, 20, "Fabric feedstock belongs to ten prior-turn residents");
        Near(c.FabricConsumed + c.Inventory.Cloth, 20, "prior-cohort Fabric production splits into current use and retained reserve");
        Check(c.PlantFood >= 0 && c.Food >= c.PlantFood, "plant stock remains part of the conserved food reserve after Fabric consumption");

        var withHunter = GrowingFixture(true); var camp = withHunter.Camps[0]; var hunterRecord = Resolve(withHunter);
        Check(hunterRecord.Growth > 0 && camp.Population > 10, "connected-hunter fixture grows at the camp");
        Near(hunterRecord.Demographics.Single().ManufacturingFood, 20, "connected hunters affect biological growth but do not add prior resident Clothing demand");
        Check(withHunter.Hunters.Single().Population == 4, "connected hunters remain residents only for biological demand and birth placement");
    }
    private static void StarvationUsesPriorCohort()
    {
        WorldState Make(double mortality)
        {
            var w = Fixture(); var c = w.Camps[0];
            w.Config.Mortality = mortality;
            c.Population = 100; c.Food = 6000; c.PlantFood = c.AnimalFood = c.AquaticFood = 0;
            c.DeficitTurns = w.Config.StarvationGraceTurns;
            c.Inventory.Cloth = 100; c.Inventory.HousingUnits = 25;
            c.CraftingPriorities = new CraftingPriorities();
            foreach (var tile in w.Tiles)
            {
                tile.Food = tile.PotentialFood = 0;
                for (int i = 0; i < tile.Seasons.Length; i++)
                {
                    var season = tile.Seasons[i];
                    season.MaxFood = season.MaxPotentialFood = 0;
                    season.FoodRecoveryPerDay = season.BiomassRecoveryPerDay = 0;
                    tile.Seasons[i] = season;
                }
            }
            return w;
        }
        var dying = Make(.5); var control = Make(0);
        var result = Resolve(dying); Resolve(control);
        var prior = control.Camps[0]; var survivors = dying.Camps[0];
        Check(result.Deaths > 0 && survivors.Population < prior.Population, "starvation applies to the existing cohort after grace");
        Near(survivors.FoodNeedsMetRatio, prior.FoodNeedsMetRatio, "starvation does not revise prior-cohort food fulfillment");
        Near(survivors.FabricConsumed, prior.FabricConsumed, "deaths cannot reduce same-turn Fabric consumption");
        Near(survivors.HousingPerCapita, prior.HousingPerCapita, "deaths cannot improve same-turn Housing coverage");
        Near(survivors.QoL, prior.QoL, "QoL is resolved before losses are committed");
    }
    private static void PlanningResidentPopulationIgnoresStaleIncome()
    {
        WorldState Make(bool stale, double remainder)
        {
            var w = Fixture(); var c = w.Camps[0];
            w.Config.AnnualHumanGrowthRate = .01145;
            c.PlantFood = 8000; c.GrowthRemainder = remainder; c.Inventory.HandTools = 10;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
            c.Workforce.Assign(WorkforceRoles.Weaver, 10, c.Population);
            c.StepFoodIncome = stale ? 10000 : 0;
            return w;
        }
        foreach (double remainder in new[] { 0d, .37 })
        {
            var clean = Make(false, remainder); var stale = Make(true, remainder);
            double cleanPopulation = TurnResolver.PlanningResidentPopulation(clean, clean.Camps[0], 10000);
            double stalePopulation = TurnResolver.PlanningResidentPopulation(stale, stale.Camps[0], 10000);
            Check(cleanPopulation > clean.Camps[0].Population, "resident planning preserves positive growth with a stock buffer");
            Near(stalePopulation, cleanPopulation, "turn-start resident planning ignores stale prior-turn income");
        }
    }
}
