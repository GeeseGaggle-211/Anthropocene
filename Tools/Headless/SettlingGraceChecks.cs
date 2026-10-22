using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

// One-time settling grace (SimulationConfig.SettlingGraceOnce, Docs/SettlingGrace.md) and plant-cloth tents
// (SimulationConfig.PlantTentsEnabled, Docs/PlantTents.md). Fixed suite: owns its worlds and counters.
//  - both rules off retain their saved-layout fingerprint regression and keep
//    the earlier save envelopes;
//  - the grace: a camp in its first settling period keeps the ramp, an established camp that moves keeps E = 1,
//    moves reset StationaryTurns and SiteYears, daughters inherit the settling state, the fixed-shelter tenure test
//    reads site tenure; the same on generated trajectories;
//  - plant tents: route gating, Cloth-only recipe, protected clothing, a full DemandControl turn, daughters,
//    conservation;
//  - Save15 round trip and continuation, old envelopes load with both rules off, OpeningConfig.json consistency.
internal static class SettlingGraceChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("SettlingGrace: " + message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-9) => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)), message + " (" + a + " vs " + b + ")");

    public static int Run()
    {
        checks = 0;
        BaseFingerprints(); Grace(); GeneratedTrajectory(); PlantTents(); TentTurn(); Saves(); Json();
        Console.WriteLine("PASS: " + checks + " settling-grace and plant-tent assertions");
        return checks;
    }

    // The rules-off form of a world, as a save from before Save15 would hold it (checks that build "old save"
    // fixtures use it, so they exercise the genuine earlier envelope).
    internal static WorldState WithoutSave15Rules(WorldState w)
    {
        var copy = SaveCodec.Load(SaveCodec.Save(w));
        copy.Config.SettlingGraceOnce = copy.Config.PlantTentsEnabled = false; copy.Config.Neolithic.ClothPerLightHousing = 0;
        foreach (var c in copy.Camps) { c.SettledExpectationEstablished = false; c.SettlingYears = c.SiteYears = 0; }
        return copy;
    }
    // The same world saved in place with the Save15 rules off (no load round trip, so fixtures that build legacy
    // inventory keep it exactly); the world is restored afterwards.
    internal static byte[] SaveWithoutSave15Rules(WorldState w)
    {
        var cfg = w.Config; bool grace = cfg.SettlingGraceOnce, tents = cfg.PlantTentsEnabled; double cloth = cfg.Neolithic.ClothPerLightHousing;
        try { cfg.SettlingGraceOnce = cfg.PlantTentsEnabled = false; return SaveCodec.Save(w); }
        finally { cfg.SettlingGraceOnce = grace; cfg.PlantTentsEnabled = tents; cfg.Neolithic.ClothPerLightHousing = cloth; }
    }
    private static string Magic(byte[] bytes)
    {
        using var zip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress); using var reader = new BinaryReader(zip); return reader.ReadString();
    }

    private static WorldState Generated(int seed, bool rules, int resolves)
    {
        var cfg = SimulationConfig.Small(6); cfg.SettlingGraceOnce = rules; cfg.PlantTentsEnabled = rules; cfg.Validate();
        var w = new WorldGenerator().Generate(seed, cfg); var resolver = new TurnResolver();
        for (int t = 0; t < resolves; t++) { var plan = AiPlanning.Decide(w); resolver.Resolve(w, plan.Orders, plan.Traces); }
        return w;
    }

    // Golden fingerprints: SimulationConfig.Small(6), WorldGenerator, AiPlanning.Decide + TurnResolver.Resolve for 13
    // turns. These values cover the current save/config layout. Historical flag-off semantic and material-transfer
    // compatibility is checked independently; no clean-build comparator evidence is claimed by this golden.
    private static void BaseFingerprints()
    {
        var golden = new Dictionary<int, string>
        {
            [11] = "388D2A8067F0AA1B263C535A762BF6F84D9268B2A2B488F5382DF907E63A084B",
            [23] = "4777169E4B52AA7C483803CD18EBE7D84E4E252E2C8C865F1B9F36804553EC16"
        };
        foreach (var pair in golden)
        {
            var w = Generated(pair.Key, false, 13);
            Check(w.Turn == 14 && SaveCodec.Fingerprint(w) == pair.Value, "rules off reproduce 557c6f9 byte for byte (seed " + pair.Key + ")");
            var bytes = SaveCodec.Save(w);
            Check(Magic(bytes) != "ANTHROPOCENE-15", "rules off keep the earlier save envelope");
            var loaded = SaveCodec.Load(bytes);
            Check(!loaded.Config.SettlingGraceOnce && !loaded.Config.PlantTentsEnabled && loaded.Config.Neolithic.ClothPerLightHousing == 0,
                "saves without Save15 load with both rules off");
            Check(SaveCodec.Fingerprint(loaded) == pair.Value, "earlier envelope round trip exact");
        }
    }

    private static WorldState Fixture(bool husbandry = false, int population = 100)
    {
        var cfg = SimulationConfig.Small(1); cfg.DemographicModel = DemographicModel.PhysicalMortalityV1; cfg.QoLMortalityEnabled = false;
        cfg.EconomicAllocation = EconomicAllocationStrategy.DemandControl; cfg.Width = 24; cfg.Height = 16; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0;
        var w = new WorldState { Seed = 505, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 8000, Food = 1000 };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 1000, MaxPotentialFood = 8000, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = t;
        }
        var c = new Camp { Id = w.NextId++, Player = 0, Tile = w.Map.At(8, 8), Population = population, PlantFood = 400 * population, AnimalFood = 50 * population, EconomyAutomationTurns = -1 };
        c.Inventory.HandTools = 4 * population; c.Inventory.Wood = 5 * population; c.Inventory.Stone = 5 * population; c.Inventory.Clay = 5 * population;
        w.Camps.Add(c);
        w.Config.Players = 1; w.PlayerCount = 1; w.Turn = 3; w.Days = 1000;
        Ownership.Bind(w);
        c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
        if (husbandry) c.Technologies[(int)Technology.AnimalHusbandry] = true;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Construction); c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        foreach (var t in w.Tiles) t.LastEcologyUpdateDays = w.Days;
        return w;
    }

    private static void Grace()
    {
        var w = Fixture(); var c = w.Camps[0]; var n = w.Config.Neolithic; double years = w.Config.TurnDays / w.Config.DaysPerYear;
        double Ramp(double y) => n.RudimentaryExpectationFloor + (1 - n.RudimentaryExpectationFloor) * Math.Min(1, y / n.RudimentaryExpectationRampYears);
        // First settling period: the ramp is the grace.
        c.StationaryYears = 0; NeolithicProduction.ResolveNeeds(w, c, years);
        Near(c.RudimentaryExpectation, Ramp(years), "a camp in its first settling period gets the expectation grace");
        Check(!c.SettledExpectationEstablished && c.SettlingYears == years && c.SiteYears == years, "settling and site years accrue together before a move");
        // A move during the grace: site tenure and the ramp restart, settling years do not.
        c.StationaryYears = 0; c.SiteYears = 0; NeolithicProduction.ResolveNeeds(w, c, years);
        Near(c.RudimentaryExpectation, Ramp(years), "a move inside the grace period still restarts the ramp");
        Check(c.SettlingYears == 2 * years && !c.SettledExpectationEstablished, "settling years survive the move");
        // The grace ends when the lineage has lived the ramp horizon in the Neolithic, whatever its moves.
        while (!c.SettledExpectationEstablished) { c.StationaryYears = 0; c.SiteYears = 0; NeolithicProduction.ResolveNeeds(w, c, years); }
        Near(c.SettlingYears, n.RudimentaryExpectationRampYears, "the grace lasts exactly RudimentaryExpectationRampYears of settled life");
        Near(c.RudimentaryExpectation, 1, "completing the grace sets E = 1");
        c.StationaryYears = 0; c.SiteYears = 0; NeolithicProduction.ResolveNeeds(w, c, years);
        Near(c.RudimentaryExpectation, 1, "after the grace, a move never lowers the settled expectation");
        Near(NeolithicProduction.SettledExpectation(n, c), 1, "the unresolved formula agrees");
        // Rule off: the flag is never set and every move restarts the ramp, as before.
        var off = Fixture(); off.Config.SettlingGraceOnce = false; var oc = off.Camps[0];
        for (int t = 0; t < 8; t++) { oc.StationaryYears = 0; NeolithicProduction.ResolveNeeds(off, oc, years); }
        Check(!oc.SettledExpectationEstablished && oc.SettlingYears == 0 && oc.SiteYears == 0, "rule off: no grace state accrues");
        Near(oc.RudimentaryExpectation, Ramp(years), "rule off: the historical reset");
        // Site tenure: the fixed-shelter test reads SiteYears under the grace, StationaryYears without it.
        var t1 = Fixture(); var tc = t1.Camps[0]; tc.StationaryYears = 200; tc.SiteYears = 0;
        Check(NeolithicProduction.SiteTenureYears(t1.Config, tc) == 0, "the tenure test reads site tenure, not lifestyle years");
        t1.Config.SettlingGraceOnce = false;
        Check(NeolithicProduction.SiteTenureYears(t1.Config, tc) == 200, "rule off: the historical StationaryYears tenure");
    }

    // Generated worlds with both rules on: the resolver's move, the planning copy and fission follow the contract.
    private static void GeneratedTrajectory()
    {
        var cfg = SimulationConfig.Small(6); cfg.Validate();
        Check(cfg.SettlingGraceOnce && cfg.PlantTentsEnabled && cfg.Neolithic.ClothPerLightHousing == 1.5, "new worlds default both rules on");
        var w = new WorldGenerator().Generate(11, cfg); var resolver = new TurnResolver(); var n = w.Config.Neolithic;
        int moves = 0, establishedMoves = 0, daughters = 0;
        for (int turn = 0; turn < 22; turn++)
        {
            var before = w.Camps.ToDictionary(c => c.Id, c => (c.Tile, c.SettledExpectationEstablished, c.SettlingYears, c.IsVillage));
            var plan = AiPlanning.Decide(w); resolver.Resolve(w, plan.Orders, plan.Traces);
            var record = w.History.Last(); double years = record.Duration / w.Config.DaysPerYear;
            foreach (var c in w.Camps)
            {
                if (!before.TryGetValue(c.Id, out var b)) continue;
                Check(c.SettlingYears + 1e-9 >= b.SettlingYears && (!b.SettledExpectationEstablished || c.SettledExpectationEstablished), "settling progress is monotone");
                if (b.Tile == c.Tile) continue;
                moves++;
                Check(c.StationaryTurns == 0, "a move resets StationaryTurns");
                Check(c.SiteYears <= years + 1e-6, "a move resets site tenure");
                if (b.SettledExpectationEstablished) { establishedMoves++; Near(c.RudimentaryExpectation, 1, "an established camp that moves keeps E = 1"); }
            }
            foreach (var f in record.Fissions ?? new List<FissionReport>())
            {
                var daughter = w.Camps.FirstOrDefault(c => c.Id == f.DaughterCamp);
                if (daughter == null || !before.TryGetValue(f.ParentCamp, out var p)) continue;
                daughters++;
                Check(daughter.SettledExpectationEstablished == (p.SettledExpectationEstablished || daughter.SettlingYears + 1e-9 >= n.RudimentaryExpectationRampYears),
                    "a daughter inherits her parent's settling state");
            }
        }
        Check(moves > 0 && establishedMoves > 0 && daughters > 0, "fixture exercises moves of established camps and fission (" + moves + "/" + establishedMoves + "/" + daughters + ")");
        // The planning copy at a destination keeps the settled state and restarts site tenure.
        var camp = w.Camps.First(c => c.SettledExpectationEstablished && c.SiteYears > 0);
        int destination = w.Map.Neighbors(camp.Tile).First();
        var view = (Camp)typeof(TurnPlanner).GetMethod("PlanningCampAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
            null, new[] { typeof(Camp), typeof(int) }, null).Invoke(null, new object[] { camp, destination });
        Check(view.SettledExpectationEstablished && view.SiteYears == 0 && view.StationaryTurns == 0 && view.SettlingYears == camp.SettlingYears,
            "the planning copy agrees with the resolver's move");
        Near(NeolithicProduction.SettledExpectation(n, view), 1, "the planner values the destination at the settled expectation");
    }

    private static void PlantTents()
    {
        var w = Fixture(); var c = w.Camps[0]; var n = w.Config.Neolithic; double years = w.Config.TurnDays / w.Config.DaysPerYear;
        Check(NeolithicProduction.PlantTentRoute(w, c), "a farming society with Weaving and Construction takes the plant route");
        var tent = NeolithicProduction.Recipes(w, c).Single(r => r.Good == CraftGood.LightHousing);
        Check(tent.Cloth == n.ClothPerLightHousing && tent.AnimalFabric == 0 && !tent.HusbandryOnly && NeolithicProduction.RecipeAvailable(w, c, tent),
            "the plant tent uses Cloth only and is available");
        Check(tent.Rate == NeolithicProduction.Recipes(w.Config).Single(r => r.Good == CraftGood.LightHousing).Rate && tent.Specialist == WorkforceRoles.Builder && tent.Track == NeolithicTech.Construction,
            "same Builder, Construction track and rate as the hide tent");
        Check(n.ClothPerLightHousing * n.PlantFoodPerFabricUnit >= n.AnimalFabricPerLightHousing, "a plant tent costs at least the hide tent's fibre");
        var off = Fixture(); off.Config.PlantTentsEnabled = false;
        Check(!NeolithicProduction.PlantTentRoute(off, off.Camps[0]) && NeolithicProduction.Recipes(off, off.Camps[0]).Single(r => r.Good == CraftGood.LightHousing) is var hide && hide.Cloth == 0 && hide.AnimalFabric == n.AnimalFabricPerLightHousing && hide.HusbandryOnly, "rule off: the configuration's recipes");
        var herd = Fixture(husbandry: true);
        Check(!NeolithicProduction.PlantTentRoute(herd, herd.Camps[0]), "husbandry camps keep the hide route");
        var noWeaving = Fixture(); noWeaving.Camps[0].People.NeolithicTechnologies.Remove(NeolithicTech.Weaving);
        Check(!NeolithicProduction.PlantTentRoute(noWeaving, noWeaving.Camps[0]), "the plant route needs Weaving");

        // Production: Cloth beyond this turn's clothing consumption only (protected clothing first).
        double equipment = 0; c.Workforce.Assignments.Clear(); c.Workforce.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Builder, Population = 10 });
        double reserve = NeolithicProduction.TentClothReserve(w, c, years, c.Population);
        Near(reserve, c.Population * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears, "the clothing consumption is reserved");
        c.Inventory.Cloth = reserve; var record = new TurnRecord();
        NeolithicProduction.Produce(w, c, tent, WorkforceRoles.Builder, 10 * w.Config.GenerationIntervals, w.Config.GenerationIntervals, 20, ref equipment, record);
        Check(c.Inventory.LightHousingUnits == 0 && c.Inventory.Cloth == reserve, "no tent takes the Cloth clothing will use");
        c.Inventory.Cloth = reserve + 30;
        NeolithicProduction.Produce(w, c, tent, WorkforceRoles.Builder, 10 * w.Config.GenerationIntervals, w.Config.GenerationIntervals, 1000, ref equipment, record);
        var receipt = record.Production.Last();
        Check(receipt.Good == CraftGood.LightHousing && receipt.Output > 0, "spare Cloth makes tents");
        Near(c.Inventory.LightHousingUnits, receipt.Output, "tents enter the camp's Light Housing");
        Near(receipt.Cloth, receipt.Output * n.ClothPerLightHousing, "the receipt records the Cloth consumed");
        Near(c.Inventory.Cloth, reserve + 30 - receipt.Cloth, "Cloth is conserved");
        Check(c.Inventory.Cloth + 1e-9 >= reserve, "protected clothing is not starved by tents");

        // Fission: a daughter of an agricultural parent can carry and sew cloth tents.
        var p = Fixture(); var parent = p.Camps[0]; parent.Inventory.Cloth = 400; parent.Inventory.LightHousingUnits = 0;
        parent.Inventory.Wood = parent.Inventory.Clay = parent.Inventory.HousingMaterial = 0; // no fixed-housing materials to carry
        var shelter = Fission.DaughterShelter(p, parent, 40);
        Check(shelter.Light > 0, "a daughter of an agricultural parent sews cloth tents");
        p.Config.PlantTentsEnabled = false;
        Check(Fission.DaughterShelter(p, parent, 40).Light == 0, "rule off: no cloth tents for the daughter");
        p.Config.PlantTentsEnabled = true; parent.Inventory.LightHousingUnits = 10; parent.Inventory.Cloth = 0;
        Near(Fission.DaughterShelter(p, parent, 40).Light, 10 * 40.0 / parent.Population * n.LightHousingPeoplePerUnit, "a daughter carries her share of the cloth tents");
    }

    // A full DemandControl turn for a farming camp that has left its fixed housing: the planner routes the portable
    // shelter deficit to cloth tents and protected clothing is unchanged against the rule-off twin.
    private static void TentTurn()
    {
        WorldState Mobile(bool plantTents)
        {
            var w = Fixture(population: 80); var c = w.Camps[0];
            w.Config.PlantTentsEnabled = plantTents;
            c.Inventory.Cloth = 400; c.Inventory.LightHousingUnits = 4; // carried tents: a portable operating policy
            c.StationaryYears = 200; c.SiteYears = 0; c.StationaryTurns = 0; c.Moves = 1; c.SettledExpectationEstablished = true; c.SettlingYears = 200;
            c.RudimentaryExpectation = 1;
            return w;
        }
        var on = Mobile(true); var off = Mobile(false);
        DemandController.Plan(on, on.Camps[0], EconomicIntent.From(on.Camps[0]), out var order, out var allocation);
        Check(order.Ceiling(CraftGood.LightHousing) > 0, "the portable shelter deficit of a moved farming camp is planned as cloth tents");
        var results = new List<(Camp camp, TurnRecord record)>();
        foreach (var w in new[] { on, off })
        {
            double cloth0 = w.Camps[0].Inventory.Cloth;
            var prepared = TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>());
            new TurnResolver().Resolve(w, prepared);
            TurnResolver.Invariants(w); checks++;
            var rec = w.History.Last(); var c = w.Camps.Single(x => x.Player == 0);
            double woven = rec.Production.Where(r => r.Camp == c.Id && r.Good == CraftGood.Cloth).Sum(r => r.Output);
            double tentCloth = rec.Production.Where(r => r.Camp == c.Id && r.Good == CraftGood.LightHousing).Sum(r => r.Cloth);
            Near(c.Inventory.Cloth, cloth0 + woven - tentCloth - c.ResolvedClothConsumed, "Cloth conservation through production, tents and clothing", 1e-6);
            results.Add((c, rec));
        }
        double tentsOn = results[0].record.Production.Where(r => r.Good == CraftGood.LightHousing).Sum(r => r.Output);
        double tentsOff = results[1].record.Production.Where(r => r.Good == CraftGood.LightHousing).Sum(r => r.Output);
        Check(tentsOn > 0 && tentsOff == 0, "with the rule on the camp sews cloth tents; off it cannot (" + tentsOn + "/" + tentsOff + ")");
        Check(results[0].camp.ClothPerCapita + 1e-9 >= results[1].camp.ClothPerCapita, "protected clothing is not starved (" + results[0].camp.ClothPerCapita + " vs " + results[1].camp.ClothPerCapita + ")");
        Check(results[0].camp.HousingPerCapita > results[1].camp.HousingPerCapita, "cloth tents raise housing coverage after a move");
    }

    private static void Saves()
    {
        var w = Generated(11, true, 10);
        var bytes = SaveCodec.Save(w);
        Check(Magic(bytes) == "ANTHROPOCENE-15", "the rules use the Save15 envelope");
        var loaded = SaveCodec.Load(bytes);
        Check(loaded.Config.SettlingGraceOnce && loaded.Config.PlantTentsEnabled && loaded.Config.Neolithic.ClothPerLightHousing == w.Config.Neolithic.ClothPerLightHousing, "Save15 restores the rules");
        Check(w.Camps.All(c => { var l = loaded.Camps.Single(x => x.Id == c.Id); return l.SettledExpectationEstablished == c.SettledExpectationEstablished && l.SettlingYears == c.SettlingYears && l.SiteYears == c.SiteYears; }),
            "Save15 restores each camp's settling state");
        Check(SaveCodec.Fingerprint(loaded) == SaveCodec.Fingerprint(w), "Save15 round trip exact");
        Check(SaveCodec.ConfigurationHash(w.Config) != SaveCodec.ConfigurationHash(Generated(11, false, 0).Config), "the rules enter the configuration hash");
        var r1 = new TurnResolver(); var r2 = new TurnResolver();
        for (int t = 0; t < 3; t++)
        {
            var p1 = AiPlanning.Decide(w); r1.Resolve(w, p1.Orders, p1.Traces);
            var p2 = AiPlanning.Decide(loaded); r2.Resolve(loaded, p2.Orders, p2.Traces);
            Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "save/load continuation exact after turn " + t);
        }
        // Mixed: one rule alone still uses Save15 and restores which.
        var one = Generated(23, false, 2); one.Config.PlantTentsEnabled = true;
        var oneLoaded = SaveCodec.Load(SaveCodec.Save(one));
        Check(!oneLoaded.Config.SettlingGraceOnce && oneLoaded.Config.PlantTentsEnabled, "Save15 records each rule");
    }

    private static void Json()
    {
        string path = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null && path == null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Assets", "_Anthropocene", "Resources", "OpeningConfig.json");
            if (File.Exists(candidate)) path = candidate;
        }
        Check(path != null, "OpeningConfig.json found");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var rules = document.RootElement.GetProperty("Save15Rules");
        var cfg = new SimulationConfig(); var defaults = new Save15Tuning();
        Check(rules.GetProperty("SettlingGraceOnce").GetBoolean() == cfg.SettlingGraceOnce && defaults.SettlingGraceOnce == cfg.SettlingGraceOnce, "OpeningConfig.json carries the settling-grace default");
        Check(rules.GetProperty("PlantTentsEnabled").GetBoolean() == cfg.PlantTentsEnabled && defaults.PlantTentsEnabled == cfg.PlantTentsEnabled, "OpeningConfig.json carries the plant-tent default");
        Check(rules.GetProperty("ClothPerLightHousing").GetDouble() == cfg.Neolithic.ClothPerLightHousing && defaults.ClothPerLightHousing == cfg.Neolithic.ClothPerLightHousing, "OpeningConfig.json carries ClothPerLightHousing");
    }
}
