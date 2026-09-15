using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class Milestone2Checks
{
    private static int assertions;
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception("Milestone 2: " + message); }
    private static WorldState Flat()
    {
        var c = SimulationConfig.Small(1); c.TurnDays = 90; c.BaseDays = 90; c.PracticeDays = 90; c.Width = 24; c.Height = 16; c.ExperienceThreshold = 100000; c.AnnualHumanGrowthRate = 0;
        var w = new WorldState { Seed = 11, Config = c, Tiles = new Tile[384], Generation = new GenerationReport() };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var tile = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Food = 60, PotentialFood = 600, Freshwater = 60, Elevation = .55f };
            for (int s = 0; s < 4; s++) tile.Seasons[s] = new SeasonData { MaxFood = 80, MaxPotentialFood = 800, MaxFreshwater = 80, FoodRecoveryPerDay = (float)EcologyMath.DailyRate(.3, 91.25), BiomassRecoveryPerDay = (float)EcologyMath.DailyRate(.3, 91.25), WaterRecoveryPerDay = (float)EcologyMath.DailyRate(.3, 91.25), Moisture = .5f };
            w.Tiles[i] = tile;
        }
        w.Camps.Add(new Camp { Id = w.NextId++, Tile = 150, Player = 0, Population = 16, Food = 100 }); Ownership.Bind(w); return w;
    }
    private static Hunter Hunter(WorldState w, int tile, double food = 0)
    {
        var hunter = new Hunter { Id = w.NextId++, SourceCamp = w.Camps[0].Id, SocietyId = w.Camps[0].SocietyId, Tile = tile, Population = 8, Player = 0, CarriedFood = food }; w.Hunters.Add(hunter); return hunter;
    }
    private static Dictionary<int, List<Order>> Orders(params Order[] orders) => new Dictionary<int, List<Order>> { [0] = orders.ToList() };
    private static void Marine(Tile t)
    {
        t.Terrain = Terrain.Shallows; t.ElevationLevel = 0; t.WaterBody = WaterKind.Saltwater; t.FreshwaterSource = FreshwaterSource.None; t.Freshwater = 0; t.Saltwater = 100;
        for (int s = 0; s < 4; s++) { t.Seasons[s].MaxFreshwater = 0; t.Seasons[s].MaxSaltwater = 100; }
    }
    public static void Run()
    {
        var resolver = new TurnResolver(); var w = Flat(); w.Config.SupplyRange = 2; var camp = w.Camps[0]; var h = Hunter(w, 152);
        Check(Supply.Connected(w, h), "ordinary traversable path supplies hunters");
        for (int r = 0; r < w.Config.Height; r++) Marine(w.Tiles[w.Map.At(7, r)]);
        Check(w.Map.Distance(camp.Tile, h.Tile) == 2 && !Supply.Connected(w, h), "supply cannot cross an impassable sea barrier even inside sight");
        Check(resolver.Validate(w, 0, new Order(OrderKind.Move, h.Id, destination: 153)) == null, "supply does not impose a hard movement leash");
        w = Flat(); w.Config.SupplyRange = 1; h = Hunter(w, 158);
        int fish = h.Tile + 1; Marine(w.Tiles[fish]);
        var control = SaveCodec.Load(SaveCodec.Save(w));
        var fishing = resolver.Resolve(w, Orders(new Order(OrderKind.Fish, h.Id, fish))); resolver.Resolve(control, Orders());
        Check(Math.Abs(w.Camps[0].Food - control.Camps[0].Food) < 1e-8, "distant fish do not teleport into the camp store");
        Check(h.CarriedFood > 0 && h.LastConsumed == 8 && h.FoodDeficit == 0, "disconnected hunters retain harvest and eat their own provisions");
        Check(fishing.Logistics.Any(l => l.Hunter == h.Id && !l.InSupply && l.RetainedHarvest > 0), "history records expedition supply and retained harvest");
        double campFood = w.Camps[0].Food; var record = new TurnRecord(); Supply.Award(w, h, 1000, record);
        Check(h.CarriedFood == Supply.Capacity(w, h) && record.Logistics[0].Overflow > 0 && w.Camps[0].Food == campFood, "carrying capacity prevents excess transport");
        h.Tile = w.Camps[0].Tile + 1; var returned = resolver.Resolve(w, Orders());
        Check(returned.Logistics.Any(l => l.Deposited > 0) && h.CarriedFood == 0, "returning within supply range deposits provisions");
        w = Flat(); h = Hunter(w, 158); var subsistence = resolver.Resolve(w, Orders());
        Check(w.Camps[0].FoodDeficit == 0 && h.Population == 8 && h.FoodDeficit == 0 && subsistence.Demographics.Single().ForageFood > 0 && Math.Abs(subsistence.Demographics.Single().ForageFood - subsistence.Transfers.Where(t => t.Entity == h.Id && t.Kind == "Foraging").Sum(t => t.Food)) < 1e-8, "isolated subsistence is recorded in the forage report without teleporting food to camp");
        foreach (var tile in w.Tiles) { tile.Food = 0; for (int season = 0; season < 4; season++) { var ecology = tile.Seasons[season]; ecology.MaxFood = 0; ecology.FoodRecoveryPerDay = 0; tile.Seasons[season] = ecology; } }
        for (int turn = 0; turn < 3; turn++) resolver.Resolve(w, Orders());
        Check(h.Population < 8, "an isolated expedition still starves after its grace period when local forage and carried food are exhausted");
        w = Flat(); h = Hunter(w, 150); double totalFood = w.Camps[0].Food; var provisionRecord = new TurnRecord();
        Supply.ProvisionDepartures(w, new List<Order> { new Order(OrderKind.Move, h.Id, destination: 152) }, provisionRecord);
        Check(h.CarriedFood > 0 && Math.Abs(w.Camps[0].Food + h.CarriedFood - totalFood) < 1e-8, "expedition provisions are withdrawn, not created");
        w = Flat(); h = Hunter(w, 158, 9.5); h.DeficitTurns = 1; w.Camps[0].GrowthRemainder = .4;
        w.Camps[0].SpeciesExperience.Add(new SpeciesPractice { Species = "camel", Experience = 12.5 });
        w.Tiles[180].Terrain = Terrain.Desert; w.Tiles[180].Oasis = true; w.Tiles[180].OasisInfluence = 1; w.Tiles[180].FreshwaterSource = FreshwaterSource.Oasis;
        var expeditionCopy = SaveCodec.Load(SaveCodec.Save(w));
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(expeditionCopy), "nonzero expedition inventory, oasis fields, stable species practice and fractional growth persist");
        resolver.Resolve(w, Orders()); resolver.Resolve(expeditionCopy, Orders());
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(expeditionCopy), "resuming an isolated expedition preserves consumption and deficit outcomes");
        w = Flat(); w.Config.AnnualHumanGrowthRate = .5; w.Config.GatherPerPerson = 100; w.Camps[0].Food = 0;
        var half = SaveCodec.Load(SaveCodec.Save(w)); half.Config.TurnDays = 45;
        int before = w.Camps[0].Population; var grown = resolver.Resolve(w, Orders()); var partial = resolver.Resolve(half, Orders());
        Check(grown.Growth > partial.Growth && w.Camps[0].GrowthRemainder > 0, "net growth scales by duration and preserves its fraction");
        Check(w.Camps[0].Population == before + grown.Growth - grown.Deaths && grown.PopulationChanges.Any(p => p.Kind == "Net growth"), "growth has explicit conserved population events");
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(SaveCodec.Load(SaveCodec.Save(w))), "growth and logistics survive save/load");
        w = Flat(); w.Config.AnnualHumanGrowthRate = .5; w.Camps[0].Food = 0; foreach (var t in w.Tiles) { t.Food = 0; for (int s = 0; s < 4; s++) t.Seasons[s].FoodRecoveryPerDay = 0; }
        Check(resolver.Resolve(w, Orders()).Growth == 0, "hunger blocks demographic growth");
        var salt = new Tile { WaterBody = WaterKind.Saltwater, Terrain = Terrain.Shallows, Saltwater = 1000, Freshwater = 99, FreshwaterSource = FreshwaterSource.None };
        var claim = new[] { new Demand { Tile = 0, Entity = 1, Kind = "Herd", Freshwater = 8 } };
        Check(new ProportionalAllocation().Allocate(claim, new[] { salt })[0].Freshwater == 0, "no generic stock fallback makes seawater drinkable");
        foreach (FreshwaterSource source in new[] { FreshwaterSource.River, FreshwaterSource.Lake, FreshwaterSource.Oasis })
            Check(new ProportionalAllocation().Allocate(claim, new[] { new Tile { FreshwaterSource = source, Freshwater = 10 } })[0].Freshwater == 8, source + " can satisfy hydration");
        w = Flat(); foreach (var t in w.Tiles) { t.Freshwater = 0; t.FreshwaterSource = FreshwaterSource.None; for (int s = 0; s < 4; s++) t.Seasons[s].MaxFreshwater = 0; }
        Marine(w.Tiles[149]); Check(PlacementStage.Viability(w, w.Camps[0].Tile) == 0, "adjacent sea cannot make a freshwater-viable start");
        var dryHerd = new Herd { Id = w.NextId++, Tile = 151, Animals = 8, Species = "camel" }; w.Herds.Add(dryHerd);
        w.Tiles[149].Food = 1; resolver.Resolve(w, Orders());
        Check(dryHerd.UnmetFreshwater > 0, "camels next to the sea still have unmet freshwater demand");
        Check(w.Tiles[149].Food > 1 && w.Tiles[149].Freshwater == 0, "marine food regenerates independently of potable water");
        w.Tiles[151].FreshwaterSource = FreshwaterSource.River; w.Tiles[151].Freshwater = 30;
        for (int s = 0; s < 4; s++) { w.Tiles[151].Seasons[s].MaxFreshwater = 30; w.Tiles[151].Seasons[s].WaterRecoveryPerDay = .004f; }
        Check(PlacementStage.Viability(w, w.Camps[0].Tile) > 0, "a real river restores freshwater start viability");
        w = Flat(); foreach (var t in w.Tiles) { t.Freshwater = 0; t.FreshwaterSource = FreshwaterSource.None; for (int s = 0; s < 4; s++) t.Seasons[s].MaxFreshwater = 0; }
        int bank = 220, lake = w.Map.Neighbors(bank).First();
        var lakeTile = w.Tiles[lake]; lakeTile.Terrain = Terrain.Lake; lakeTile.WaterBody = WaterKind.Freshwater; lakeTile.FreshwaterSource = FreshwaterSource.Lake; lakeTile.Freshwater = 20;
        for (int s = 0; s < 4; s++) lakeTile.Seasons[s].MaxFreshwater = 30;
        var bankHerd = new Herd { Id = w.NextId++, Tile = bank, Animals = 8, Species = "horse" }; w.Herds.Add(bankHerd);
        var drinking = resolver.Resolve(w, Orders()).Transfers.Where(t => t.Entity == bankHerd.Id && t.Kind == "Herd").ToArray();
        Check(drinking.Any(t => t.Tile == lake && t.Freshwater > 0) && bankHerd.UnmetFreshwater == 0, "land herds drink from the finite neighboring lake without entering water");
        Check(w.Tiles[bankHerd.Tile].Freshwater == 0, "shore drinking never copies freshwater into soil moisture");
        w = Flat(); Marine(w.Tiles[w.Map.At(23, 5)]); w.Tiles[w.Map.At(23, 5)].Terrain = Terrain.Ocean;
        Geography.Classify(w); Check(w.Tiles[w.Map.At(23, 5)].Terrain == Terrain.Shallows, "coast classification honors the wrapped seam");
        var camel = w.Config.GetSpecies("camel"); var desert = new Tile { Terrain = Terrain.Desert, OasisInfluence = 0 }; var oasisDesert = new Tile { Terrain = Terrain.Desert, OasisInfluence = 1, Oasis = true };
        Check(camel.Suitability(oasisDesert) > camel.Suitability(desert) * 3 && camel.Suitability(desert) > camel.Suitability(new Tile { Terrain = Terrain.Woodland }), "camel preferences favor arid oasis habitat");
        Check(w.Config.GetSpecies("horse").Suitability(new Tile { Terrain = Terrain.Grassland }) > w.Config.GetSpecies("horse").Suitability(desert), "horses prefer grasslands");
        w = Flat(); foreach (var t in w.Tiles) { t.Terrain = Terrain.Desert; t.Freshwater = 0; t.FreshwaterSource = FreshwaterSource.None; }
        var camelHerd = new Herd { Id = w.NextId++, Tile = 180, Animals = 8, Species = "camel", UnmetFreshwater = 100 }; w.Herds.Add(camelHerd);
        int oasisTile = w.Map.Neighbors(camelHerd.Tile).First(); w.Tiles[oasisTile].Oasis = true; w.Tiles[oasisTile].OasisInfluence = 1; w.Tiles[oasisTile].Freshwater = 70; w.Tiles[oasisTile].FreshwaterSource = FreshwaterSource.Oasis;
        Check(TurnResolver.HerdDestination(w, camelHerd) == oasisTile, "thirsty camels seek accessible oasis freshwater");
        var depletion = new ProportionalAllocation().Allocate(new[] { new Demand { Tile = oasisTile, Entity = camelHerd.Id, Kind = "Herd", Freshwater = 1000, Food = 1000 } }, w.Tiles)[0];
        Check(depletion.Freshwater == 70 && depletion.Food <= w.Tiles[oasisTile].Food, "oasis extraction remains finite");
        var cfg = SimulationConfig.Small(3); var generated = new WorldGenerator().Generate(7, cfg); var replay = new WorldGenerator().Generate(7, SimulationConfig.Small(3));
        Check(SaveCodec.Fingerprint(generated) == SaveCodec.Fingerprint(replay), "all geography and species placement are deterministic");
        Check(generated.Camps.All(c => generated.Herds.Any(p => generated.Map.Reachable(c.Tile, p.Tile, cfg.VisionRadius, n => generated.Tiles[n].Land))), "every opening camp has a reachable visible wild herd");
        Check(generated.Camps.All(c => c.Food >= cfg.StartingReserveFood), "every generated camp begins with at least one complete generation of sustenance");
        Check(generated.Herds.Select(p => p.Tile).Distinct().Count() == generated.Herds.Count && generated.Herds.All(p => generated.Tiles[p.Tile].Land), "wild placement has no duplicate or aquatic herd locations");
        Geography.Validate(generated); BoundaryRiverStage.Validate(generated); OasisStage.Validate(generated);
        var corners = new HexCorners(cfg.Width, cfg.Height);
        Check(generated.Rivers.All(e => HexCorners.EdgeKey(corners.Corner(e.TileA, e.SideA), corners.Corner(e.TileA, (e.SideA + 1) % 6)) == HexCorners.EdgeKey(e.FromVertex, e.ToVertex)
            && (e.TileB < 0 || HexCorners.EdgeKey(corners.Corner(e.TileB, e.SideB), corners.Corner(e.TileB, (e.SideB + 1) % 6)) == HexCorners.EdgeKey(e.FromVertex, e.ToVertex))), "both banks reference the actual same corner edge");
        Check(generated.Generation.ReliefCounts[(int)Relief.Hills] > 0 && generated.Tiles.Any(t => t.Relief == Relief.Hills && t.Terrain != Terrain.Mountain), "biome and relief remain separate");
        var saved = SaveCodec.Load(SaveCodec.Save(generated)); Check(SaveCodec.Fingerprint(saved) == SaveCodec.Fingerprint(generated), "water types, edges, oases and stable species IDs round trip");
        var observer = new AgentObservationBuilder(); var view = observer.Build(generated, 0);
        Check(view.Herds.Count > 0 && view.Herds.All(p => !string.IsNullOrEmpty(p.Species)), "observations contain stable species definitions");
        foreach (var t in view.Tiles.Values)
            Check(t.Oasis == generated.Tiles[t.Id].Oasis && t.Relief == generated.Tiles[t.Id].Relief && t.WaterBody == generated.Tiles[t.Id].WaterBody && t.Rivers.Count == (generated.Tiles[t.Id].RiverEdges?.Count(e => e >= 0) ?? 0), "layered observation matches only visible semantic data");
        Console.WriteLine("PASS: " + assertions + " milestone-two assertions");
    }
}
