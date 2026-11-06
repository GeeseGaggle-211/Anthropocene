using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class SailingChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Sailing: " + message); }
    private static void Water(WorldState w, int id, EnvironmentKind kind)
    {
        var t = w.Tiles[id]; t.Environment = kind;
        t.Terrain = kind == EnvironmentKind.Ocean ? Terrain.Ocean : kind == EnvironmentKind.Lake ? Terrain.Lake : Terrain.Shallows;
        t.ElevationLevel = 0; t.WaterBody = WaterKind.Saltwater;
        t.Food = 1000; t.PotentialFood = 2000; t.Freshwater = 0; t.FreshwaterSource = FreshwaterSource.None;
        for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { MaxFood = 2000, MaxPotentialFood = 2000, BiomassRecoveryPerDay = .02f };
        w.Camps[0].People.Memory.Explored.Add(id, w.Turn);
    }
    private static WorldState Fixture(bool sailing = true)
    {
        var w = FishingEconomyChecks.Fixture(48); var c = w.Camps[0];
        w.Config.NeolithicSailingEnabled = true; w.Config.VisionRadius = 2;
        w.Config.FissionEnabled = false; w.Config.ImmigrationEnabled = false; w.Config.TradeEnabled = false;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        if (sailing) c.People.NeolithicTechnologies.Add(NeolithicTech.Sailing);
        for (int x = 11; x <= 14; x++) Water(w, w.Map.At(x, 8), EnvironmentKind.Shallows);
        Water(w, w.Map.At(15, 8), EnvironmentKind.Ocean);
        Water(w, w.Map.At(16, 8), EnvironmentKind.Ocean);
        Water(w, w.Map.At(15, 9), EnvironmentKind.Ocean);
        Water(w, w.Map.At(16, 9), EnvironmentKind.Shallows); // unreachable across Ocean
        w.Hunters.Add(new Hunter { Id = w.NextId++, Player = c.Player, SocietyId = c.SocietyId, SourceCamp = c.Id, Tile = w.Map.At(14,8), Population = 1 });
        Ownership.Bind(w);
        return w;
    }
    private static FishingAccessPlan Access(WorldState w) => FishingEconomy.BuildAccess(w, w.Camps[0], w.Camps[0].Tile,
        w.Camps[0].Population, w.Days, w.Config.TurnDays);
    private static void Geometry()
    {
        var w = Fixture(false); var c = w.Camps[0];
        var before = Access(w);
        Check(before.Candidates.All(g => g.Distance <= 3 && !g.Offshore), "pre-Sailing range and offshore exclusion");
        c.People.NeolithicTechnologies.Add(NeolithicTech.Sailing);
        var access = Access(w);
        Check(access.Candidates.Any(g => g.TileId == w.Map.At(15, 8) && g.Distance == 5 && g.Offshore), "terminal Ocean at range five");
        Check(!access.Candidates.Any(g => g.TileId == w.Map.At(16, 8) || g.TileId == w.Map.At(16, 9)), "no repeated Ocean hop or navigable shortcut");
        Check(access.Candidates.All(g => Math.Abs(g.DistanceFactor - Math.Pow(.9, g.Distance - 1)) < 1e-12), "Sailing distance retention");
        // Make all natural coastal yields small so offshore selection is exercised.
        foreach (var g in access.Candidates.Where(g => !g.Offshore))
        {
            w.Tiles[g.TileId].Food = w.Tiles[g.TileId].PotentialFood = 0;
            for (int s = 0; s < 4; s++) w.Tiles[g.TileId].Seasons[s] = new SeasonData();
        }
        access = Access(w);
        Check(access.Grounds.Length == 3 && access.Grounds.Count(g => g.Offshore) == 1, "best offshore ground uses one of three slots");
        c.LastSelectedFishingGrounds = access.Candidates.Where(g => g.Offshore).Select(g => g.TileId).Take(1).ToList();
        Check(Access(w).Grounds.Count(g => g.Offshore) == 1, "incumbency preserves the Ocean limit");
        var view = FishingInspection.Build(w, c.Player, c, EconomicIntent.From(c));
        Check(view.Grounds.Where(g => g.Plan.Offshore).All(g => g.Path.Skip(1).Take(g.Path.Length - 2).All(t => w.Tiles[t].Environment == EnvironmentKind.Shallows)), "offshore routes contain only terminal Ocean");
        var plan = FishingEconomy.PreparePlan(w, c, access, 20, 10, w.Config.TurnDays, false);
        Check(plan.Grounds.Any(g => g.Offshore && g.RequestedCatch > 0), "offshore gets real harvest claim");
        Check(plan.Grounds.Where(g => g.Offshore).All(g => g.CapitalWorkerYears == 0 && g.EndingCapital == 0 && g.ManagementIntensity == 0), "offshore harvest builds no capital");
        w.Config.NeolithicSailingEnabled = false;
        Check(Access(w).Candidates.All(g => g.Distance <= 3 && !g.Offshore), "compatibility gate keeps old access even with saved Sailing knowledge");
        w = Fixture();
        Water(w, w.Map.At(11, 8), EnvironmentKind.Lake);
        Check(!Access(w).Candidates.Any(g => g.Offshore), "Lake cannot join Shallows route to Ocean");
    }
    private static void ProgressionAndReplay()
    {
        var w = Fixture(false); var c = w.Camps[0];
        var cloth = NeolithicProduction.Recipes(w.Config).Single(r => r.Good == CraftGood.Cloth);
        TurnRecord Evidence(string role, bool activity, double output) => new TurnRecord {
            Production = new List<ProductionReceipt> { new ProductionReceipt { Camp = c.Id, Good = CraftGood.Cloth, Role = role, Output = output } },
            Transfers = activity ? new List<Transfer> { new Transfer { Entity = c.Id, Tile = w.Map.At(11,8), Kind = "FisheryHarvest", Food = 1 } } : new List<Transfer>() };
        SecondTierTechnology.Resolve(w, Evidence(WorkforceRoles.Weaver, false, 1000));
        SecondTierTechnology.Resolve(w, Evidence(WorkforceRoles.Craftsperson, true, 1000));
        SecondTierTechnology.Resolve(w, Evidence(WorkforceRoles.Weaver, true, 0));
        Check(!c.People.CraftExperience.ContainsKey(NeolithicTech.Sailing), "idle stock, irrelevant role, and absent activity grant nothing");
        SecondTierTechnology.Resolve(w, Evidence(WorkforceRoles.Weaver, true, cloth.Rate));
        Check(c.People.CraftExperience[NeolithicTech.Sailing] == 1 && !c.People.HasTech(NeolithicTech.Sailing), "useful output uses shared craft mastery units");
        var restored = SaveCodec.Load(SaveCodec.Save(w));
        Check(restored.Camps[0].People.CraftExperience[NeolithicTech.Sailing] == 1 && restored.Config.NeolithicSailingEnabled, "partial progress and gate round-trip");
        // Full Prepare/Resolve: real Weaver output plus resident catch crosses the
        // existing threshold at Close, without backdating access to this plan.
        c.People.CraftExperience[NeolithicTech.Sailing] = w.Config.Neolithic.CraftMasteryThreshold - .001;
        c.PlantFood = 100000; c.Inventory.HandTools = 1000; c.Inventory.Wood = c.Inventory.Stone = c.Inventory.Clay = 1000;
        c.Workforce.Assign(WorkforceRoles.Weaver, 3, c.Population); c.Workforce.Assign(WorkforceRoles.Fisher, 12, c.Population);
        c.CraftingPriorities.Cloth = 10;
        var bytes = SaveCodec.Save(w); var replay = SaveCodec.Load(bytes);
        var orders = new Dictionary<int, List<Order>> { [c.Player] = new List<Order>() };
        var prepared = TurnPlanner.Prepare(w, orders);
        Check(prepared.FishingPlans[c.Id].Grounds.All(g => !g.Offshore && g.Distance <= 3), "unlock turn locks old geometry");
        var record = new TurnResolver(new PhaseExecution(1)).Resolve(w, prepared);
        Check(record.Production.Any(p => p.Role == WorkforceRoles.Weaver && p.Output > 0) && record.Transfers.Any(t => t.Kind == "FisheryHarvest" && t.Food > 0), "real useful Weaver and fishing receipts");
        Check(c.People.HasTech(NeolithicTech.Sailing) && Access(w).Candidates.Any(g => g.Offshore), "Close unlock affects next preparation");
        new TurnResolver(new PhaseExecution(4)).Resolve(replay, TurnPlanner.Prepare(replay, orders));
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(replay), "save continuation and scalar/worker replay");
        restored = SaveCodec.Load(SaveCodec.Save(w));
        Check(restored.Camps[0].People.HasTech(NeolithicTech.Sailing), "acquired Sailing persists");
        // Ensure offshore is actually harvested through the shared ecology path.
        foreach (var g in Access(restored).Candidates.Where(g => !g.Offshore))
        {
            restored.Tiles[g.TileId].Food = restored.Tiles[g.TileId].PotentialFood = 0;
            for (int s = 0; s < 4; s++) restored.Tiles[g.TileId].Seasons[s] = new SeasonData();
        }
        restored.Camps[0].PlantFood = restored.Camps[0].AnimalFood = restored.Camps[0].AquaticFood = 0;
        var offshorePrepared = TurnPlanner.Prepare(restored, orders);
        var offshoreRecord = new TurnResolver().Resolve(restored, offshorePrepared);
        Check(offshoreRecord.Transfers.Any(t => t.Kind == "FisheryHarvest" && restored.Tiles[t.Tile].Environment == EnvironmentKind.Ocean && t.Food > 0), "offshore produces actual shared-stock receipts");
        Check(restored.FisherySites.All(s => restored.Tiles[s.TileId].Environment != EnvironmentKind.Ocean), "resolver creates no offshore sites");
        Check(offshoreRecord.Demographics.Single(d => d.Camp == c.Id).FishingVillageMealSupport == 0, "offshore catch supplies no village credit");
        Check(SaveCodec.Fingerprint(restored) == SaveCodec.Fingerprint(SaveCodec.Load(SaveCodec.Save(restored))), "offshore selected grounds and snapshots round-trip");
    }
    public static int Run()
    {
        checks = 0; Geometry(); ProgressionAndReplay();
        Console.WriteLine("PASS: " + checks + " Sailing assertions"); return checks;
    }
}
