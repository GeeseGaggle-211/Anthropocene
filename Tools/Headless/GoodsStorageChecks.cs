using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// Pottery storage for finished goods (phase S, Docs/PlannerTradeStorageDesign.md §3): the capacity rule and reserve guard,
// goods in use, blocking (never destroying) additions in production, forecast and trade settlement, the trade value cut-off,
// determinism and persistence. Owns its worlds and counters (lanes run checks concurrently).
internal static class GoodsStorageChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("GoodsStorage: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-9) => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)), message + " (" + a + " vs " + b + ")");

    // A uniform fertile fixture: camp 0 (the human) far away, AI villages 1.. of distinct Societies that know each other.
    private static WorldState World(bool storage, params (int x, int y, double cloth, double material)[] camps)
    {
        var cfg = SimulationConfig.Small(1); cfg.GoodsStorageEnabled = storage; cfg.TradeFoodEnabled = false; cfg.Width = 24; cfg.Height = 16; cfg.AnnualHumanGrowthRate = 0; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0; cfg.ContactExpiryTurns = 50;
        var w = new WorldState { Seed = 303, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 8000, Food = 1000 };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = t;
        }
        w.Camps.Add(new Camp { Id = w.NextId++, Player = 0, Tile = w.Map.At(20, 13), Population = 60, PlantFood = 6000, AnimalFood = 1000, RudimentaryExpectation = .5 });
        for (int i = 0; i < camps.Length; i++)
        {
            var (x, y, cloth, material) = camps[i];
            var c = new Camp { Id = w.NextId++, Player = i + 1, Tile = w.Map.At(x, y), Population = 80, PlantFood = 8000, AnimalFood = 2000, RudimentaryExpectation = .5, StationaryYears = 40, StationaryTurns = 2, IsVillage = true };
            c.Inventory.Cloth = cloth; c.Inventory.HousingMaterial = material;
            w.Camps.Add(c);
        }
        w.Config.Players = camps.Length + 1; w.PlayerCount = camps.Length + 1; w.Turn = 3; w.Days = 1000;
        Ownership.Bind(w);
        foreach (var c in w.Camps)
        {
            c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        }
        foreach (var c in w.Camps) foreach (var o in w.Camps) if (o != c) ContactKnowledge.MemoryOf(w, c.People).Upsert(o.Id, o.Player, o.Tile, w.Turn, ContactSource.Sighting);
        foreach (var t in w.Tiles) t.LastEcologyUpdateDays = w.Days;
        return w;
    }
    private static Camp CampOf(WorldState w, int player) => w.Camps.Single(c => c.Player == player);
    private static double Years(WorldState w) => w.Config.TurnDays / w.Config.DaysPerYear;
    private static double Natural(WorldState w, Camp c) => NeolithicProduction.NaturalStorageCapacity(w, c, 0);
    private static double Reserve(WorldState w, Camp c) => NeolithicProduction.DesiredReserveFood(w, NeolithicProduction.ReservePopulation(w, c));
    private static double Counted(Camp c) => GoodsStorage.StockOf(c).Sum();
    private static void Advance(WorldState w, TurnResolver r) => r.Resolve(w, new Dictionary<int, List<Order>>());

    public static int Run()
    {
        checks = 0; Config(); Capacity(); InUse(); Limit(); Production(); Forecast(); Trade(); Turns();
        Console.WriteLine("PASS: " + checks + " goods-storage assertions"); return checks;
    }

    private static string FindConfig()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string path = Path.Combine(d.FullName, "Assets", "_Anthropocene", "Resources", "OpeningConfig.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("OpeningConfig.json");
    }

    private static void Config()
    {
        var c = new SimulationConfig();
        Check(!c.GoodsStorageEnabled && c.GoodsBaseStoragePerResident == 2 && c.GoodsStorageSpacePerUnit == 20, "storage is off by default with the specified levers");
        foreach (Action<SimulationConfig> bad in new Action<SimulationConfig>[] { x => x.GoodsBaseStoragePerResident = -1, x => x.GoodsBaseStoragePerResident = double.NaN, x => x.GoodsStorageSpacePerUnit = 0, x => x.GoodsStorageSpacePerUnit = double.PositiveInfinity })
        {
            var x = new SimulationConfig(); bad(x); bool rejected = false; try { x.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "an invalid storage lever is rejected");
        }
        var serializer = new DataContractSerializer(typeof(SimulationConfig)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { GoodsStorageEnabled = true, GoodsBaseStoragePerResident = 3 }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<GoodsStorageEnabled>true</GoodsStorageEnabled>") && xml.Contains("<GoodsBaseStoragePerResident>3</GoodsBaseStoragePerResident>"), "storage levers are serialized");
        string legacy = Regex.Replace(xml, "<GoodsStorage(Enabled|SpacePerUnit)>[^<]*</GoodsStorage\\1>|<GoodsBaseStoragePerResident>[^<]*</GoodsBaseStoragePerResident>", "");
        Check(legacy != xml && !legacy.Contains("GoodsStorage") && !legacy.Contains("GoodsBaseStorage"), "legacy fixture omits every storage field");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy)))
        {
            var old = (SimulationConfig)serializer.ReadObject(stream);
            Check(!old.GoodsStorageEnabled && old.GoodsBaseStoragePerResident == 2 && old.GoodsStorageSpacePerUnit == 20, "an older config loads with storage off and the default levers");
        }
        string json = File.ReadAllText(FindConfig());
        foreach (var pair in new[] { ("GoodsStorageEnabled", "false"), ("GoodsBaseStoragePerResident", "2"), ("GoodsStorageSpacePerUnit", "20") })
        {
            var m = Regex.Match(json, "\"" + pair.Item1 + "\":\\s*([0-9.a-z]+)");
            Check(m.Success && m.Groups[1].Value == pair.Item2, "OpeningConfig.json carries " + pair.Item1);
        }
    }

    // Capacity: s x N base plus the Pots the food does not hold back, the food holding back max(food, reserve) above natural storage.
    private static void Capacity()
    {
        var w = World(true, (6, 6, 0, 0)); var cfg = w.Config; var c = CampOf(w, 1); double s = cfg.GoodsStorageSpacePerUnit, potFood = cfg.Neolithic.PotStorageFoodPerUnit;
        double nat = Natural(w, c), reserve = Reserve(w, c), baseSpace = s * cfg.GoodsBaseStoragePerResident * c.Population;
        Check(reserve > nat, "fixture: the reserve needs pot space above natural storage");
        var st = GoodsStorage.Begin(w, c, Years(w), c.Population, 0, 0);
        c.Inventory.Pots = 0; Near(st.Capacity(c), baseSpace, "without Pots goods have only their base allowance");
        // Pots just covering the reserve: goods gain nothing (reserve guard) while the food is below the reserve.
        c.PlantFood = nat / 2; c.AnimalFood = 0; c.Inventory.Pots = (reserve - nat) / potFood;
        Near(st.Capacity(c), baseSpace, "pots needed by the food reserve are not goods space");
        c.Inventory.Pots = (reserve - nat + 1000) / potFood;
        Near(st.Capacity(c), baseSpace + 1000, "pots beyond the reserve's share are goods space");
        // Food above the reserve holds back more pot space.
        c.PlantFood = reserve + 600; Near(st.Capacity(c), baseSpace + 400, "food above the reserve competes for the pots");
        c.PlantFood = reserve + 5000; Near(st.Capacity(c), baseSpace, "pots full of food leave goods only their base");
        // Food's capacity keeps the reserve's share whatever goods occupy.
        double pots = (reserve - nat + 1000) / potFood, p = pots * potFood;
        Near(st.FoodCapacity(pots, baseSpace), nat + p, "goods within their base take no food space");
        Near(st.FoodCapacity(pots, baseSpace + 300), nat + p - 300, "goods in pots take food space");
        Near(st.FoodCapacity(pots, baseSpace + 5000), nat + (reserve - nat), "never below the reserve's share");
        Near(st.FoodCapacity(pots, 0), NeolithicProduction.StorageCapacity(w, Clone(c, pots), 0), "with no goods in pots food capacity is today's");
    }
    private static Camp Clone(Camp c, double pots) { var v = c.WithPrivateInventory(); v.Inventory.Pots = pots; return v; }

    // Goods in use take no space: clothing up to this turn's consumption, Housing Material up to Housing assembled, tents up to
    // the unsheltered, Hand/Regular Tools up to this turn's equipment outputs; end of turn drops the consumed goods' allowances.
    private static void InUse()
    {
        var w = World(true, (6, 6, 0, 0)); var n = w.Config.Neolithic; var c = CampOf(w, 1); double s = w.Config.GoodsStorageSpacePerUnit, years = Years(w);
        var st = GoodsStorage.Begin(w, c, years, c.Population, 10, 5);
        double clothing = c.Population * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears, tents = c.Population / n.LightHousingPeoplePerUnit;
        double[] Stock(Action<double[]> set) { var x = new double[GoodsStorage.Count]; set(x); return x; }
        Near(st.Occupied(Stock(x => { x[GoodsStorage.Cloth] = clothing * .6; x[GoodsStorage.AnimalFabric] = clothing * .4; })), 0, "clothing for this turn's consumption is in use");
        Near(st.Occupied(Stock(x => { x[GoodsStorage.Cloth] = clothing + 2; })), 2 * s, "clothing beyond consumption is stored");
        Near(st.Occupied(Stock(x => { x[GoodsStorage.HousingMaterial] = 5 * n.HousingMaterialUnitsPerHousing + 1; })), s, "Housing Material beyond the assembly is stored");
        Near(st.Occupied(Stock(x => { x[GoodsStorage.LightHousing] = tents + 3; })), 3 * s, "tents beyond the unsheltered are stored");
        Near(st.Occupied(Stock(x => { x[GoodsStorage.HandTools] = 6; x[GoodsStorage.Tools] = 2; })), s, "tools cover this turn's outputs (6 Hand Tools, then 1 Tool of 4 outputs) and the rest is stored");
        Near(st.Occupied(Stock(x => { x[GoodsStorage.Ceramics] = 2; })), 2 * s, "uncovered goods are stored");
        var end = GoodsStorage.Begin(w, c, years, c.Population, 10, 5, true);
        Near(end.Occupied(Stock(x => { x[GoodsStorage.Cloth] = 3; x[GoodsStorage.HandTools] = 1; })), 4 * s, "at the end of the turn consumed goods have no allowance");
        Check(GoodsStorage.Index(CraftGood.Pots) < 0 && GoodsStorage.Index(CraftGood.Housing) < 0 && GoodsStorage.Index(CraftGood.Cloth) >= 0, "Pots and fixed Housing are not stored goods");
    }

    // Limit: an addition is cut to what fits; over capacity nothing raises occupancy; uncounted outputs are never cut.
    private static void Limit()
    {
        var w = World(true, (6, 6, 0, 0)); var c = CampOf(w, 1); double s = w.Config.GoodsStorageSpacePerUnit;
        var st = GoodsStorage.Begin(w, c, Years(w), 0, 0, 0);
        var stock = new double[GoodsStorage.Count]; stock[GoodsStorage.Cloth] = 4;
        double capacity = 10 * s;
        Near(st.Limit(stock, capacity, CraftGood.Cloth, 100), 6, "cloth is cut to the six units of room", 1e-9);
        Near(st.Limit(stock, capacity, CraftGood.Cloth, 3), 3, "an addition within the room is kept whole");
        Near(st.Limit(stock, capacity, CraftGood.Pots, 100), 100, "Pots are not stored goods");
        var over = new double[GoodsStorage.Count]; over[GoodsStorage.Cloth] = 10; over[GoodsStorage.HousingMaterial] = 3; over[GoodsStorage.AnimalFabric] = 2;
        Near(st.Limit(over, capacity, CraftGood.Cloth, 5), 0, "over capacity nothing more is stored");
        Near(st.Limit(over, capacity, CraftGood.Housing, 3, 0, 1), 3, "Housing that consumes stored material is never cut");
        Near(st.Limit(over, capacity, CraftGood.LightHousing, 2, 1), 2, "a tent made from a stored fabric unit does not raise occupancy");
        var stockBefore = (double[])stock.Clone(); st.Limit(stock, capacity, CraftGood.Cloth, 100);
        Check(stock.SequenceEqual(stockBefore), "Limit is pure");
        GoodsStorage.Add(stock, CraftGood.LightHousing, 2, 1); Check(stock[GoodsStorage.LightHousing] == 2 && stock[GoodsStorage.AnimalFabric] == 0, "Add records outputs and consumes inputs, never below zero");
    }

    // The resolver's Produce: a weaving step is cut to the room; inputs are only what was made.
    private static void Production()
    {
        var w = World(true, (6, 6, 0, 0)); var cfg = w.Config; var n = cfg.Neolithic; var c = CampOf(w, 1); double s = cfg.GoodsStorageSpacePerUnit, years = Years(w), intervals = cfg.GenerationIntervals;
        c.Inventory.Tools = 200; c.Inventory.Pots = 0;
        var st = GoodsStorage.Begin(w, c, years, c.Population, 1e6, 0); c.GoodsStorage = st;
        double clothing = c.Population * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears;
        c.Inventory.Cloth = clothing + st.Capacity(c) / s - 3; // three units of room
        Near(st.RoomUnits(c), 3, "fixture: three units of room");
        var recipe = NeolithicProduction.Recipes(cfg).First(r => r.Good == CraftGood.Cloth);
        double cloth0 = c.Inventory.Cloth, plant0 = c.PlantFood, equipment = 0; var record = new TurnRecord { Turn = w.Turn };
        NeolithicProduction.Produce(w, c, recipe, WorkforceRoles.Craftsperson, 1000 * intervals, intervals, 100, ref equipment, record, new ToolCoverageBudget(w, c, intervals));
        double made = c.Inventory.Cloth - cloth0;
        Near(made, 3, "weaving stops at the room", 1e-6);
        Near(plant0 - c.PlantFood, made * n.PlantFoodPerFabricUnit, "only the cloth made consumes plant food", 1e-6);
        Near(record.Production.Single().Output, made, "the receipt records what was stored", 1e-9);
        c.Inventory.Cloth += 2; double over = c.Inventory.Cloth;
        NeolithicProduction.Produce(w, c, recipe, WorkforceRoles.Craftsperson, 1000 * intervals, intervals, 100, ref equipment, record, new ToolCoverageBudget(w, c, intervals));
        Check(c.Inventory.Cloth == over, "over capacity: kept, never destroyed, nothing added");
        // Storage off: the same step makes the useful demand.
        var off = World(false, (6, 6, 0, 0)); var oc = CampOf(off, 1); oc.Inventory.Tools = 200; oc.Inventory.Cloth = c.Inventory.Cloth; double off0 = oc.Inventory.Cloth, e2 = 0;
        NeolithicProduction.Produce(off, oc, recipe, WorkforceRoles.Craftsperson, 1000 * intervals, intervals, 100, ref e2, new TurnRecord { Turn = off.Turn }, new ToolCoverageBudget(off, oc, intervals));
        Check(oc.Inventory.Cloth - off0 > 10 && oc.GoodsStorage == null, "without storage production is not cut");
    }

    // The forecast applies the same rule to its virtual stock.
    private static void Forecast()
    {
        Dictionary<CraftGood, double> Planned(bool storage, double baseUnits)
        {
            var w = World(storage, (6, 6, 0, 0)); w.Config.GoodsBaseStoragePerResident = baseUnits; var c = CampOf(w, 1);
            c.Inventory.Tools = 200; c.Inventory.Pots = 0; c.Workforce.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Craftsperson, Population = 20 } };
            return MaterialEconomy.Forecast(w, c, NeolithicProduction.Recipes(w.Config), w.Config.GenerationIntervals, Years(w), new ToolCoverageBudget(w, c, w.Config.GenerationIntervals).Remaining, true, -1, false, false);
        }
        var free = Planned(false, 2); var roomy = Planned(true, 1000); var tight = Planned(true, 0);
        Check(free[CraftGood.Cloth] > 1, "fixture: the forecast plans cloth without storage (" + free[CraftGood.Cloth] + ")");
        Near(roomy[CraftGood.Cloth], free[CraftGood.Cloth], "ample room changes nothing", 1e-9);
        var w0 = World(true, (6, 6, 0, 0)); var n = w0.Config.Neolithic; var c0 = CampOf(w0, 1);
        double clothing = c0.Population * n.FabricConsumptionPerPersonTurn * Years(w0) / n.ReferenceTurnYears;
        Check(tight[CraftGood.Cloth] <= clothing + 1e-6 && tight[CraftGood.Cloth] < free[CraftGood.Cloth], "with no room the forecast plans only cloth in use this turn (" + tight[CraftGood.Cloth] + " <= " + clothing + ")");
    }

    // Trade settlement: a receiver short of room takes less, both legs scaling (the value cut-off at the room is phase V's,
    // Docs/PlannerTradeStorageDesign.md §3.5: a swap frees the giver's room, which a per-good cut-off cannot see).
    private static void Trade()
    {
        var w = World(true, (6, 6, 400, 0), (8, 6, 0, 60)); w.Config.TradeEnabled = true;
        var materialCamp = CampOf(w, 2);
        // Settlement: shrink the material camp's room to two units beyond clothing in use; cloth delivered is cut to fit.
        var plan = Anthropocene.Simulation.Trade.Plan(w);
        var toMaterial = plan.Agreements.Where(a => a.Requester == materialCamp.Id && a.Want == TradeGood.Cloth || a.Responder == materialCamp.Id && a.Give == TradeGood.Cloth).ToArray();
        Check(toMaterial.Length > 0 && plan.Agreements.All(a => a.AllStock), "fixture: the material camp receives cloth from stock");
        foreach (var c in w.Camps) c.GoodsStorage = GoodsStorage.Begin(w, c, Years(w), c.Population, 0, 0);
        double free = materialCamp.GoodsStorage.RoomUnits(materialCamp);
        materialCamp.Inventory.Cloth += free - 2; double clothBefore = materialCamp.Inventory.Cloth;
        double cloth0 = w.Camps.Sum(c => c.Inventory.Cloth), material0 = w.Camps.Sum(c => c.Inventory.HousingMaterial);
        var record = new TurnRecord { Turn = w.Turn };
        var settled = Anthropocene.Simulation.Trade.Settle(w, plan, record); Anthropocene.Simulation.Trade.SettleAfterProduction(w, settled); Anthropocene.Simulation.Trade.Finish(w, settled, record);
        double received = materialCamp.Inventory.Cloth - clothBefore;
        Check(received > 0 && received <= 2 + 1e-6, "the receiver takes only the two units that fit (" + received + ")");
        Near(w.Camps.Sum(c => c.Inventory.Cloth), cloth0, "settlement conserves cloth"); Near(w.Camps.Sum(c => c.Inventory.HousingMaterial), material0, "settlement conserves material");
        Check(record.Trades.All(t => Math.Abs(t.Delivered - t.GiveDelivered) < 1e-9) && record.Trades.Any(t => t.Delivered < 1 - 1e-9), "a room-short agreement settles both legs at one smaller fraction");
        Check(w.Camps.All(c => GoodsStorage.StockOf(c).All(x => x >= 0)), "no stock goes negative");
    }

    private static WorldState Ring(bool storage, double baseUnits)
    {
        var w = World(storage, (6, 6, 300, 0), (8, 6, 0, 50), (7, 8, 150, 20), (5, 8, 20, 30), (16, 10, 200, 0));
        w.Config.GoodsBaseStoragePerResident = baseUnits; w.Config.TradeEnabled = true;
        foreach (var c in w.Camps) { c.Inventory.Tools = 60; c.EconomyAutomationTurns = -1; }
        w.Turn = 1; w.Days = 0; foreach (var t in w.Tiles) t.LastEcologyUpdateDays = 0;
        return w;
    }
    // Whole turns: inert when off, binding when tight, transient terms cleared, replay and save continuation exact.
    private static void Turns()
    {
        // Off: the levers do nothing.
        var a = Ring(false, 2); var b = Ring(false, .01);
        for (int t = 0; t < 2; t++) { Advance(a, new TurnResolver()); Advance(b, new TurnResolver()); }
        Check(a.Camps.Select(c => (c.Population, c.Food, Counted(c))).SequenceEqual(b.Camps.Select(c => (c.Population, c.Food, Counted(c)))), "with storage off the levers change nothing");
        // On and tight: fewer goods than off, and the transient terms never outlive the turn.
        var tight = Ring(true, .05); var loose = Ring(false, .05);
        for (int t = 0; t < 2; t++)
        {
            Advance(tight, new TurnResolver()); Advance(loose, new TurnResolver());
            Check(tight.Camps.All(c => c.GoodsStorage == null), "storage terms are cleared at the end of the turn " + t);
            TurnResolver.Invariants(tight); checks++;
        }
        // Fewer stored goods are made (holdings can still be higher: blocked work also leaves its tools unspent).
        double Made(WorldState x) => x.History.SelectMany(h => h.Production).Where(p => GoodsStorage.Index(p.Good) >= 0).Sum(p => p.Output);
        Check(Made(tight) < Made(loose) - 1e-6, "tight storage makes fewer stored goods (" + Made(tight) + " vs " + Made(loose) + ")");
        // Determinism: scalar against reversed 4-worker partitions; save continuation.
        var worlds = new[] { Ring(true, .3), Ring(true, .3) }; worlds[1].Camps.Reverse(); worlds[1].Societies.Reverse();
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 2, true, 1) };
        for (int t = 0; t < 3; t++)
        {
            for (int i = 0; i < 2; i++) Advance(worlds[i], new TurnResolver(workers[i]));
            Check(SaveCodec.Fingerprint(worlds[0]) == SaveCodec.Fingerprint(worlds[1]), "storage replays identically across workers and order after turn " + t);
        }
        var saved = Ring(true, .3); Advance(saved, new TurnResolver());
        var loaded = SaveCodec.Load(SaveCodec.Save(saved));
        Check(loaded.Config.GoodsStorageEnabled && loaded.Config.GoodsBaseStoragePerResident == .3 && SaveCodec.Fingerprint(loaded) == SaveCodec.Fingerprint(saved), "the storage switch and levers round trip");
        for (int t = 0; t < 2; t++) { Advance(saved, new TurnResolver()); Advance(loaded, new TurnResolver()); Check(SaveCodec.Fingerprint(saved) == SaveCodec.Fingerprint(loaded), "continued run equals the uninterrupted run after turn " + t); }
    }
}
