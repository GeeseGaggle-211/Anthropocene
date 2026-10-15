using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// DemandControl slice 1 (Docs/EconomicControlContract.md with Addendum A): the strategy selector, purity, legal intents,
// the §9 Housing reference case, saturated budgets, ceilings honoured by forecast and resolver, determinism and saves.
// Owns its worlds and counters (lanes run checks concurrently).
internal static class DemandControlChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("DemandControl: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-9) => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)), message + " (" + a + " vs " + b + ")");

    private static WorldState World(string strategy, params (int x, int y, int population)[] camps)
    {
        var cfg = SimulationConfig.Small(1); cfg.DemographicModel = DemographicModel.LegacyQoLNetGrowth; cfg.EconomicAllocation = strategy; cfg.Width = 24; cfg.Height = 16; cfg.AnnualHumanGrowthRate = 0; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0;
        var w = new WorldState { Seed = 404, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 8000, Food = 1000 };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = t;
        }
        w.Camps.Add(new Camp { Id = w.NextId++, Player = 0, Tile = w.Map.At(20, 13), Population = 40, PlantFood = 6000, AnimalFood = 1000, RudimentaryExpectation = .5 });
        for (int i = 0; i < camps.Length; i++)
        {
            var (x, y, population) = camps[i];
            var c = new Camp { Id = w.NextId++, Player = i + 1, Tile = w.Map.At(x, y), Population = population, PlantFood = 400 * population, AnimalFood = 50 * population, RudimentaryExpectation = .5, StationaryYears = 40, StationaryTurns = 2, IsVillage = true, EconomyAutomationTurns = -1 };
            c.Inventory.HandTools = 2 * population; c.Inventory.Wood = 5 * population; c.Inventory.Stone = 5 * population; c.Inventory.Clay = 5 * population;
            w.Camps.Add(c);
        }
        w.Config.Players = camps.Length + 1; w.PlayerCount = camps.Length + 1; w.Turn = 3; w.Days = 1000;
        Ownership.Bind(w);
        foreach (var c in w.Camps)
        {
            c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        }
        foreach (var t in w.Tiles) t.LastEcologyUpdateDays = w.Days;
        return w;
    }
    private static Camp CampOf(WorldState w, int player) => w.Camps.Single(c => c.Player == player);
    private static double Years(WorldState w) => w.Config.TurnDays / w.Config.DaysPerYear;
    private static EconomicIntent Plan(WorldState w, Camp c, out ProductionOrder order, out DemandController.Allocation allocation)
        => DemandController.Plan(w, c, EconomicIntent.From(c), out order, out allocation);
    private static EconomicConstraints Constraints(WorldState w, Camp c) => new EconomicConstraints
    {
        Agriculture = CultivationEconomy.Enabled(c), Husbandry = CultivationEconomy.HusbandryEnabled(c), Population = c.Population,
        Technologies = c.People.NeolithicTechnologies.ToArray(), FocusTiles = CultivationEconomy.Footprint(w, c).ToArray()
    };

    public static int Run()
    {
        checks = 0; Config(); Purity(); HousingReference(); Saturation(); Turns();
        Console.WriteLine("PASS: " + checks + " demand-control assertions"); return checks;
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
        Check(new SimulationConfig().EconomicAllocation == EconomicAllocationStrategy.DemandControl, "DemandControl is the default strategy (plan of record)");
        var bad = new SimulationConfig { EconomicAllocation = "Optimal" }; bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "an unknown strategy is rejected");
        var serializer = new DataContractSerializer(typeof(SimulationConfig)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { EconomicAllocation = EconomicAllocationStrategy.DemandControl }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<EconomicAllocation>DemandControl</EconomicAllocation>"), "the strategy is serialized");
        string legacy = Regex.Replace(xml, "<EconomicAllocation>[^<]*</EconomicAllocation>", "");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) Check(((SimulationConfig)serializer.ReadObject(stream)).EconomicAllocation == EconomicAllocationStrategy.Legacy, "an older config loads Legacy");
        var m = Regex.Match(File.ReadAllText(FindConfig()), "\"EconomicAllocation\":\\s*\"([A-Za-z]+)\"");
        Check(m.Success && m.Groups[1].Value == "DemandControl", "OpeningConfig.json carries DemandControl");
    }

    // Pure: planning reads the committed camp and writes nothing; the intent validates.
    private static void Purity()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 120), (12, 6, 60)); var c = CampOf(w, 1);
        string before = SaveCodec.Fingerprint(w);
        var intent = Plan(w, c, out var order, out var allocation);
        Check(SaveCodec.Fingerprint(w) == before, "planning changes no committed state");
        Check(EconomicIntents.Validate(Constraints(w, c), intent) == null, "the intent is legal: " + EconomicIntents.Validate(Constraints(w, c), intent));
        Check(intent.Assignments.Sum(a => a.Population) + intent.Labor.MaterialGatherers <= c.Population + 1e-9, "staffing fits the residents");
        Check(order != null && order.HasCeilings && NeolithicProduction.Recipes(w.Config).All(r => order.Ceiling(r.Good) >= 0 && !double.IsInfinity(order.Ceiling(r.Good))), "every good has a finite ceiling");
        Check(allocation.Requests.Any(q => q.Service == "clothing") && allocation.Requests.All(q => q.Granted <= q.Units + 1e-9), "requests are made and never over-granted");
        var again = Plan(w, c, out var order2, out _);
        Check(again.Stamp == intent.Stamp && order2.ToString() == order.ToString(), "planning is deterministic");
        // Legacy dispatch is the Value/Uniform planner exactly.
        var lw = World(EconomicAllocationStrategy.Legacy, (6, 6, 120)); var lc = CampOf(lw, 1);
        var viaStrategy = EconomicAllocationStrategy.Plan(lw, lc, EconomicIntent.From(lc), out var legacyOrder);
        var direct = EconomicPlanner.Plan(lw, lc, EconomicIntent.From(lc), EconomicPlanner.ConfiguredRule(lw.Config), out _, out var directOrder);
        Check(viaStrategy.Stamp == direct.Stamp && (legacyOrder?.ToString() ?? "") == (directOrder?.ToString() ?? "") && !(legacyOrder?.HasCeilings ?? false), "Legacy dispatch equals the Value/Uniform planner and sets no ceilings");
    }

    // v0.1 §9: 480 residents, 120 fixed Housing units, 10% decay per reference turn: 12 units of recurring replacement, about
    // 4.8 Builder-equivalents compiled to 5 Builders at the unscaled specialist/hand-tool baseline, delivered next turn.
    private static void HousingReference()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 480)); var cfg = w.Config; var n = cfg.Neolithic; var c = CampOf(w, 1);
        NeolithicProduction.AddFixedHousing(w, c, 120, w.Days);
        c.Inventory.Cloth = 2000; // clothing met: shelter is the work
        Check(Math.Abs(Years(w) - n.ReferenceTurnYears) < 1e-9 && Math.Abs(n.HousingDecayPerTurn - .10) < 1e-12 && n.HousingPeoplePerUnit == 4, "fixture: the reference turn, decay and housing size");
        var intent = Plan(w, c, out var order, out var allocation);
        var replacement = allocation.Requests.Single(q => q.Service == "shelter replacement");
        Near(replacement.Units, 12, "12 units of replacement are requested", 1e-6);
        Near(replacement.Granted, 12, "with feasible inputs the replacement is granted in full", 1e-6);
        Check(!allocation.Requests.Any(q => q.Service == "shelter deficit" && q.Units > 1e-6), "with a constant cohort there is no deficit beyond replacement");
        Check(allocation.Staff.TryGetValue(WorkforceRoles.Builder, out int builders) && builders == 5, "the replacement compiles to 5 Builders (" + builders + ")");
        Near(order.Ceiling(CraftGood.Housing), 12, "Housing output is capped at the 12-unit target", 1e-6);
        // Delivered: one resolved turn restores the 120 units, and the next request is replacement again.
        var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
        new TurnResolver().Resolve(w, prepared);
        double made = w.History.Last().Production.Where(p => p.Camp == c.Id && p.Good == CraftGood.Housing).Sum(p => p.Output);
        Near(made, 12, "the resolver makes the 12 units", 1e-6);
        Near(NeolithicProduction.FixedHousingAt(w, c, w.Days), 120, "the turn ends with 120 units", 1e-6);
        Plan(w, c, out _, out var next);
        Near(next.Requests.Single(q => q.Service == "shelter replacement").Units, 12, "the next request is replacement again", 1e-6);
    }

    // Saturated budgets: few residents ration labour, record the cause and never over-staff.
    private static void Saturation()
    {
        var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 12)); var c = CampOf(w, 1); c.Inventory.Cloth = 0;
        NeolithicProduction.AddFixedHousing(w, c, 0.1, w.Days);
        var intent = Plan(w, c, out _, out var allocation);
        Check(EconomicIntents.Validate(Constraints(w, c), intent) == null, "a saturated plan is legal");
        // Every resident needed for survival (census C1 turn 51: 64 herders for 64 residents): no zero-person role remains.
        var herdsOnly = World(EconomicAllocationStrategy.DemandControl, (6, 6, 10)); var hc = CampOf(herdsOnly, 1);
        hc.Technologies[(int)Technology.AnimalHusbandry] = true; hc.Inventory.Wood = 0; hc.Inventory.Stone = 0; hc.Inventory.Clay = 0;
        herdsOnly.Herds.Add(new Herd { Id = herdsOnly.NextId++, Tile = hc.Tile, Animals = 400, DomesticCamp = hc.Id });
        var herdIntent = Plan(herdsOnly, hc, out _, out _);
        Check(herdIntent.Assignments.All(a => a.Population > 0) && EconomicIntents.Validate(Constraints(herdsOnly, hc), herdIntent) == null, "a camp whose residents all herd gets no zero-person role");
        Check(intent.Assignments.Sum(a => a.Population) + intent.Labor.MaterialGatherers <= c.Population + 1e-9, "a saturated plan fits the residents");
        Check(allocation.Requests.Where(q => q.Units > q.Granted + 1e-6).All(q => q.Unmet.Length > 0), "every shortfall names its cause");
        // Materials: no Wood or Clay in reach leaves Housing Material unmade, attributed to material shortage.
        var dry = World(EconomicAllocationStrategy.DemandControl, (6, 6, 200)); var dc = CampOf(dry, 1); dc.Inventory.Wood = 0; dc.Inventory.Clay = 0; dc.Inventory.HousingMaterial = 0;
        Plan(dry, dc, out var dryOrder, out var dryAllocation);
        var shelter = dryAllocation.Requests.Where(q => q.Good == CraftGood.Housing && q.Units > 1e-6).ToList();
        Check(shelter.Count > 0 && shelter.All(q => q.Granted < 1e-6 && q.Unmet == "material shortage") && dryOrder.Ceiling(CraftGood.Housing) < 1e-6, "with no construction material in reach no Housing is promised");
        Check(dryAllocation.Requests.Any(q => q.Good != CraftGood.Housing && q.Granted > 1e-6), "unrelated requests still use the remaining capacity");
    }

    // Whole turns: legal plans, ceilings honoured, invariants, replay and save continuation.
    private static void Turns()
    {
        WorldState Fresh() { var w = World(EconomicAllocationStrategy.DemandControl, (6, 6, 150), (10, 6, 80), (6, 10, 40), (14, 10, 300)); w.Turn = 1; w.Days = 0; foreach (var t in w.Tiles) t.LastEcologyUpdateDays = 0; return w; }
        var world = Fresh();
        for (int turn = 0; turn < 4; turn++)
        {
            var prepared = TurnPlanner.Prepare(world, new Dictionary<int, List<Order>>());
            Check(prepared.AutomatedCamps.Count >= 4, "fixture: the villages are automated");
            var ceilings = prepared.Needs.Where(p => p.Value.Order != null).ToDictionary(p => p.Key, p => p.Value.Order);
            Check(ceilings.Count >= 4 && ceilings.Values.All(o => o.HasCeilings), "prepared needs carry the DemandControl ceilings");
            new TurnResolver().Resolve(world, prepared);
            TurnResolver.Invariants(world); checks++;
            foreach (var g in world.History.Last().Production.GroupBy(p => (p.Camp, p.Good)))
                if (ceilings.TryGetValue(g.Key.Camp, out var o)) Check(g.Sum(p => p.Output) <= o.Ceiling(g.Key.Good) + 1e-6, $"camp {g.Key.Camp} makes no more {g.Key.Good} than allocated ({g.Sum(p => p.Output)} > {o.Ceiling(g.Key.Good)})");
        }
        Check(world.Camps.Where(c => c.Player != 0).All(c => c.Population > 0) && world.History.Sum(h => h.Production.Count) > 0, "the villages live and produce");
        var worlds = new[] { Fresh(), Fresh() }; worlds[1].Camps.Reverse(); worlds[1].Societies.Reverse();
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 2, true, 1) };
        for (int t = 0; t < 3; t++)
        {
            for (int i = 0; i < 2; i++) workersResolve(worlds[i], workers[i]);
            Check(SaveCodec.Fingerprint(worlds[0]) == SaveCodec.Fingerprint(worlds[1]), "DemandControl replays identically across workers and order after turn " + t);
        }
        var saved = Fresh(); workersResolve(saved, new PhaseExecution());
        var loaded = SaveCodec.Load(SaveCodec.Save(saved));
        Check(loaded.Config.EconomicAllocation == EconomicAllocationStrategy.DemandControl, "the strategy round trips");
        for (int t = 0; t < 2; t++) { workersResolve(saved, new PhaseExecution()); workersResolve(loaded, new PhaseExecution()); Check(SaveCodec.Fingerprint(saved) == SaveCodec.Fingerprint(loaded), "continued run equals the uninterrupted run after turn " + t); }
    }
    private static void workersResolve(WorldState w, PhaseExecution execution)
        => new TurnResolver(execution).Resolve(w, TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>(), null, execution));
}
