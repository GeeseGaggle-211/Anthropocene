using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Anthropocene.Simulation;

internal static class NeolithicChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Neolithic: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-8) => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);
    internal static WorldState Fixture(bool agriculture = true)
    {
        var cfg = SimulationConfig.Small(1); cfg.Width = 24; cfg.Height = 16; cfg.AnnualHumanGrowthRate = 0; cfg.LandUseDamagePerYear = 0; cfg.ConditionRecoveryPerYear = 0;
        var w = new WorldState { Seed = 101, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
        for (int i = 0; i < w.Tiles.Length; i++)
        {
            var t = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool, Freshwater = 100, PotentialFood = 800, Food = 80 };
            for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Temperature = 18, Moisture = .55f, RegionalMoisture = .5f, MaxFood = 80, MaxPotentialFood = 800, MaxFreshwater = 100, FoodRecoveryPerDay = .004f, BiomassRecoveryPerDay = .004f, WaterRecoveryPerDay = .004f, Habitat = .6f };
            w.Tiles[i] = t;
        }
        var c = new Camp { Id = w.NextId++, Player = 0, Tile = 200, Population = 100, PlantFood = 12000 };
        w.Camps.Add(c); Ownership.Bind(w);
        if (agriculture) { c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic; }
        return w;
    }
    private static void AddResources(WorldState w)
    {
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05 };
        int tile = w.Camps[0].Tile; w.Resources.Stone[tile] = 100; w.Resources.Clay[tile] = 100; w.Resources.Wood[tile] = 100;
    }
    public static int Run()
    {
        checks = 0; Cultivation(); Recipes(); Needs(); Materials(); IntentsAndSaves(); AdditionalContracts(); Smoke();
        Console.WriteLine("PASS: " + checks + " Neolithic realignment assertions"); return checks;
    }
    private static void Cultivation()
    {
        var w = Fixture(false); var c = w.Camps[0]; var r = new TurnRecord();
        c.IsVillage = true; c.Workforce.Assign(WorkforceRoles.Farmer, 100, c.Population);
        CultivationEconomy.Expand(w, c, 2000, false, r); CultivationEconomy.Expand(w, c, 2000, true, r);
        Check(w.Tiles.All(t => t.CultivationCapital == 0), "no Agriculture means no q growth even in a village");
        Near(CultivationEconomy.Prepare(w, c, 0).OperatingFarmers, 0, "no farming capability before Agriculture");
        Near(new EcologyCapacity(w, w.Tiles[c.Tile], new EcologyView(w.Tiles[c.Tile]), 0).ManagedFlow, 0, "q zero has no managed production");
        c.Technology = Technology.Cultivation;
        c.CultivationFocusTile = c.Tile;
        double cost = w.Config.Neolithic.BuildWorkerYearsPerTileE;
        CultivationEconomy.Expand(w, c, cost, false, r); Near(w.Tiles[c.Tile].CultivationCapital, 1, "absolute build worker-years");
        Near(w.Tiles[c.Tile].Cultivation, .4, "q derives from E", 1e-7);
        var p = CultivationEconomy.Prepare(w, c, 0);
        Near(p.OperatingFarmers, 250.0 * .4 / 7, "absolute upkeep independent of population");
        Near(p.OperatingFarmers + p.ExpansionFarmers, 100, "farmer labor partitions without duplication");
        c.Workforce.Assignments.Clear(); c.IsVillage = true;
        Near(CultivationEconomy.Prepare(w, c, 0).OperatingFarmers, 0, "village cannot create phantom farmers");
        double e = 1; for (int i = 0; i < 20; i++) e = CultivationEconomy.Decay(w.Config, e, .3, 1);
        Near(e, CultivationEconomy.Decay(w.Config, 1, .3, 20), "partial decay time partition");
        Near(CultivationEconomy.Decay(w.Config, 1, 0, 80), .5, "abandonment half-life");
        var a = Fixture(); var b = Fixture(); a.Camps[0].CultivationFocusTile = a.Camps[0].Tile; b.Camps[0].CultivationFocusTile = b.Camps[0].Tile;
        CultivationEconomy.Expand(a, a.Camps[0], 1000, false, new TurnRecord());
        for (int i = 0; i < 20; i++) CultivationEconomy.Expand(b, b.Camps[0], 50, false, new TurnRecord());
        Near(a.Tiles[200].CultivationCapital, b.Tiles[200].CultivationCapital, "build time partition");
        var t = w.Tiles[c.Tile]; t.CurrentVegetation = VegetationTier.High; t.Environment = EnvironmentKind.Grassland;
        CultivationEconomy.Factors(w.Config, t, out double vb, out double vm); Near(vb, 1, "lush grassland is not forest"); Near(vm, 1, "open high vegetation upkeep");
        t.Environment = EnvironmentKind.Temperate; CultivationEconomy.Factors(w.Config, t, out vb, out vm); Near(vb, 1.5, "canonical forest build surcharge"); Near(vm, 1.2, "canonical forest maintenance");
        t.Environment = EnvironmentKind.Grassland; t.CurrentVegetation = VegetationTier.Low;
        t.CultivationCapital = 1; t.CultivationOperatingFraction = 1;
        var full = EcologicalPressure.Integrate(w, t.Id, new[] { new Demand { Tile = t.Id, Entity = c.Id, Kind = "Cultivation", Food = 730 } }, 0, 7300);
        double harvest = 0;
        for (int i = 0; i < 20; i++) { var part = EcologicalPressure.Integrate(w, t.Id, new[] { new Demand { Tile = t.Id, Entity = c.Id, Kind = "Cultivation", Food = 36.5 } }, i * 365, 365); harvest += part.Transfers.Sum(x => x.Food); part.View.Commit(t); }
        Near(harvest, full.Transfers.Sum(x => x.Food), "managed ecology time partition", 1e-6); Near(t.Food, full.View.Food, "managed ecology standing stock partition", 1e-6);
        Near(full.Balance.StartingBiomass + full.Balance.BiomassRecharge - full.Balance.BiomassHarvest - full.Balance.BiomassOverflow, full.Balance.EndingBiomass, "managed biomass balance", 1e-6);
        Check(full.View.Food <= full.View.PotentialFood && full.View.PotentialFood <= 800, "shared biomass ceilings");
        t.CultivationOperatingFraction = 0;
        var abandoned = LazyEcology.ProjectEcology(w, t.Id, t.LastEcologyUpdateDays + 80 * 365); Near(abandoned.CultivationCapital, .5, "abandoned physical capital decays lazily");
        Near(t.CultivationCapital, 1, "projection is pure");
        var overlapping = Fixture(); var first = overlapping.Camps[0]; var second = new Camp { Id = overlapping.NextId++, Player = 0, SocietyId = first.SocietyId, Tile = overlapping.Map.Neighbors(first.Tile).First(), Population = 100 };
        overlapping.Camps.Add(second); Ownership.Bind(overlapping); first.Workforce.Assign(WorkforceRoles.Farmer, 100, 100); second.Workforce.Assign(WorkforceRoles.Farmer, 100, 100);
        first.CultivationFocusTile = first.Tile; second.CultivationFocusTile = first.Tile;
        CultivationEconomy.Expand(overlapping, first, cost, false, new TurnRecord()); CultivationEconomy.Expand(overlapping, second, cost, false, new TurnRecord());
        Check(overlapping.Tiles[first.Tile].CultivationOperator == first.Id, "one operator wins shared field"); Near(overlapping.Tiles[first.Tile].CultivationCapital, 1, "losing claimant invests elsewhere");
        first.Population = 0; first.Workforce.Assignments.Clear(); CultivationEconomy.Prepare(overlapping, second, 0);
        Check(overlapping.Tiles[first.Tile].CultivationOperator == second.Id && Math.Abs(overlapping.Tiles[first.Tile].CultivationCapital - 1) < 1e-8 && CultivationEconomy.Prepare(overlapping, first, 0).Footprint.Length == 0, "abandoned dead-settlement fields are reusable without erasing physical capital");
        var bridge = Fixture(); var bc = bridge.Camps[0]; bc.IsVillage = true; bc.CultivationFocusTile = bc.Tile;
        CultivationEconomy.Expand(bridge, bc, cost * 100, true, new TurnRecord()); Check(bridge.Tiles.All(tile => tile.Cultivation <= .25000001), "bridge cap applies in villages");
        var zero = Fixture(); zero.Camps[0].Workforce.Assign(WorkforceRoles.Farmer, 100, 100); var before = zero.Camps[0].PlantFood;
        var plan = CultivationEconomy.Prepare(zero, zero.Camps[0], 0); Near(plan.OperatingFarmers, 0, "expansion-only farmers cannot harvest"); Near(zero.Camps[0].PlantFood, before, "cultivation planning never creates inventory food");
    }
    private static double Make(WorldState w, CraftGood good, string role, double workers, double demand, out TurnRecord record)
    {
        var c = w.Camps[0]; record = new TurnRecord(); double equipment = (c.Inventory.HandTools + c.Inventory.Tools + c.Inventory.CopperTools) * 80;
        var recipe = NeolithicProduction.Recipes(w.Config).Single(x => x.Good == good);
        NeolithicProduction.Produce(w, c, recipe, role, workers * 80, 80, demand, ref equipment, record);
        return record.Production.Sum(x => x.Output);
    }
    private static void Recipes()
    {
        var w = Fixture(); var c = w.Camps[0]; c.Inventory.HandTools = 100; c.Inventory.Wood = 100; c.Inventory.Clay = 100; c.Inventory.Stone = 100;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving); double food = c.PlantFood;
        Near(Make(w, CraftGood.Cloth, WorkforceRoles.Weaver, 1, 1, out var r), 1, "one useful Fabric output"); Near(food - c.PlantFood, 1, "Fabric debits exactly one PlantFood");
        Near(r.Production.Single().WorkerIntervals, 1, "Weaver specialist BaseDays output"); Near(Make(w, CraftGood.Cloth, WorkforceRoles.Craftsperson, 1, 1000, out _), 20, "generalist textiles at 25 percent");
        Near(Make(w, CraftGood.Cloth, WorkforceRoles.Potter, 1, 1000, out _), 0, "unrelated specialist has no textile bonus");
        c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        double material = c.Inventory.Wood + c.Inventory.Clay;
        Near(Make(w, CraftGood.Housing, WorkforceRoles.Builder, 1, 1000, out r), 2.5, "Builder 2.5 Housing per generation"); Near(material - c.Inventory.Wood - c.Inventory.Clay, 2.5, "one total material per Housing");
        Near(c.People.CraftExperience[NeolithicTech.Construction], 80, "construction progress normalized by Builder rate");
        c.Inventory.Wood = .3; c.Inventory.Clay = .7;
        Near(Make(w, CraftGood.Housing, WorkforceRoles.Builder, 1, 1, out _), 1, "mixed Wood Clay recipe"); Near(c.Inventory.Wood + c.Inventory.Clay, 0, "mixed input debited atomically");
        c.Inventory.Clay = 10; c.Inventory.Wood = 0; Near(Make(w, CraftGood.Housing, WorkforceRoles.Builder, 1, 1, out _), 1, "Clay-only housing");
        c.Inventory.Wood = 10; c.Inventory.Tools = 10; Near(Make(w, CraftGood.Housing, WorkforceRoles.Builder, 1, 1000, out _), 3.75, "improved tool housing multiplier");
        c.Inventory.Wood = 20; c.Inventory.CopperTools = 10; c.People.NeolithicTechnologies.Add(NeolithicTech.CopperWorking);
        Near(Make(w, CraftGood.Housing, WorkforceRoles.Builder, 1, 1000, out _), 5, "copper housing multiplier");
        Near(Make(w, CraftGood.Cloth, WorkforceRoles.Weaver, 1, 1000, out _), 80, "housing tool multipliers stay recipe-local");
        c.Inventory.HandTools = c.Inventory.Tools = c.Inventory.CopperTools = 0; Near(Make(w, CraftGood.Cloth, WorkforceRoles.Weaver, 1, 10, out _), 0, "missing tool coverage prevents output");
        c.Inventory.HandTools = .25; Near(Make(w, CraftGood.Cloth, WorkforceRoles.Weaver, 1, 1000, out _), 20, "partial tool coverage constrains first");
        c.PlantFood = 0; c.AnimalFood = c.AquaticFood = 100; Near(Make(w, CraftGood.Cloth, WorkforceRoles.Weaver, 1, 1000, out _), 0, "dietary animal or aquatic food is not plant feedstock");
        c.Inventory.Stone = 0; Near(Make(w, CraftGood.HandTools, WorkforceRoles.Craftsperson, 1, 10, out _), 0, "no stone no tools");
        c.Inventory.Stone = 100; double exp = c.People.CraftExperience.Values.Sum(); Near(Make(w, CraftGood.HandTools, WorkforceRoles.Craftsperson, 1, 0, out _), 0, "no useful demand no output"); Near(c.People.CraftExperience.Values.Sum(), exp, "no progress for unused capacity");
    }
    private static void Needs()
    {
        var w = Fixture(); var c = w.Camps[0]; c.Inventory.HousingUnits = 25; c.Population = 110;
        NeolithicProduction.DepreciateHousing(w, c, 20); Near(c.Inventory.HousingUnits, 22.5, "housing depreciates once"); Near(c.HousingDemand, 5, "single replacement plus growth deficit"); Near(c.HousingReplacementDemand, 2.5, "replacement decomposition"); Near(c.HousingGrowthDemand, 2.5, "growth decomposition");
        c.Population = 100; c.Inventory.Cloth = 150; c.Inventory.HousingUnits = 25; c.StationaryYears = 100;
        NeolithicProduction.ResolveNeeds(w, c, 20); Near(c.FabricConsumed, 100, "recurring Fabric need"); Near(c.Inventory.Cloth, 50, "stock bridges and is debited"); Near(c.ClothPerCapita, 1, "full physical clothing saturation");
        NeolithicProduction.ResolveNeeds(w, c, 20); Near(c.ClothPerCapita, .5, "stockpile cannot be reused"); Near(c.QoL, .75, "compensable rudimentary utility"); Near(c.Population, 100, "QoL deficit not fatal"); Check(w.Camps.Count == 1, "QoL does not fission");
        c.Inventory.Cloth = 80; NeolithicProduction.ResolveNeeds(w, c, 20); Near(c.QoL, .9, "80 percent clothing is graded");
        c.FoodNeedsMetRatio = .5; c.WaterNeedsMetRatio = .5; c.Inventory.Cloth = 100; NeolithicProduction.ResolveNeeds(w, c, 20); Near(c.QoL, .25, "basic food and water gate QoL");
        c.StationaryYears = 0; c.Inventory.Cloth = 0; c.Inventory.HousingUnits = 0; c.FoodNeedsMetRatio = c.WaterNeedsMetRatio = 1; NeolithicProduction.ResolveNeeds(w, c, 0);
        Near(c.RudimentaryExpectation, .25, "mobile expectation floor"); Near(c.HousingPerCapita, 0, "expectation cannot fake physical housing");
        Near(.1 / (4 * 2.5), .01, "Builder steady maintenance one percent"); Near(.1 / (4 * 2.5 * .25), .04, "generalist steady maintenance four percent");
    }
    private static void Materials()
    {
        var w = Fixture(); var c = w.Camps[0]; var r = new TurnRecord();
        Check(MaterialEconomy.KnownSources(w, c, 0).Length == 0, "absent layer has no source");
        double equipment = 0; MaterialEconomy.Gather(w, c, NeolithicProduction.Recipes(w.Config), 80, 20, 800, ref equipment, r); Near(c.Inventory.Stone + c.Inventory.Clay + c.Inventory.Wood, 0, "population cannot generate materials");
        AddResources(w); var source = MaterialEconomy.KnownSources(w, c, 0).First(s => s.Material == MaterialId.Stone);
        MaterialEconomy.Extract(w, c, source, WorkforceRoles.Gatherer, 4, 100, 0, r); Near(c.Inventory.Stone, 1, "generalist extraction .25"); Near(r.MaterialTransfers.Single().Before - r.MaterialTransfers.Single().After, 1, "source receipt reconciles");
        c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking); MaterialEconomy.Extract(w, c, source, WorkforceRoles.Stonecutter, 4, 100, 0, r); Near(c.Inventory.Stone, 5, "matching specialist extraction 1");
        MaterialEconomy.Extract(w, c, source, WorkforceRoles.Woodcutter, 4, 100, 0, r); Near(c.Inventory.Stone, 5, "unrelated extractor cannot gain specialist output"); Near(c.People.CraftExperience.Count, 0, "stockpile-only sourcing no research");
        var remote = new KnownMaterialSource { Id = "surface:0:1", Tile = 0, Material = MaterialId.Stone }; Near(MaterialEconomy.Extract(w, c, remote, WorkforceRoles.Gatherer, 4, 100, 0, r), 0, "caller cannot invent a source");
        var timber = MaterialEconomy.KnownSources(w, c, 0).First(s => s.Material == MaterialId.Wood);
        MaterialEconomy.Extract(w, c, timber, WorkforceRoles.Gatherer, 4, 100, 0, r); Check(r.Pressure.Any(p => p.Kind == "Timber extraction" && p.Realized == 1), "wood has ecological receipt"); Check(w.Tiles[c.Tile].LandCondition < 1, "wood extraction changes ecological condition");
        Near(r.MaterialTransfers.Sum(x => x.Amount), c.Inventory.Stone + c.Inventory.Wood, "every incoming material unit has a receipt");
        var copy = SaveCodec.Load(SaveCodec.Save(w)); Near(copy.MaterialSources.Sum(s => s.Remaining), w.MaterialSources.Sum(s => s.Remaining), "fractional depletion survives save");
    }
    private static void IntentsAndSaves()
    {
        var w = Fixture(); var c = w.Camps[0]; var builder = new AgentObservationBuilder(); var o = builder.Build(w, 0);
        var e = EconomicIntents.Automatic(w, c); var order = new Order(OrderKind.Economy, c.Id) { Economy = e };
        Check(OrderRules.Validate(o, order) == null, "automatic policy uses legal economic intent");
        var clone = order.Copy(); e.Assignments.Clear(); Check(clone.Economy.Assignments.Count > 0, "submitted intents deep copied");
        e.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 101 }); Check(OrderRules.Validate(o, order) != null, "overassignment rejected");
        e.Assignments.Clear(); e.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Builder, Population = 1 }); Check(OrderRules.Validate(o, order) != null, "unmastered specialist rejected");
        e.Assignments.Clear(); e.Priorities.Cloth = double.NaN; Check(OrderRules.Validate(o, order) != null, "nonfinite priority rejected");
        e.Priorities.Cloth = 0; e.Focus = 0; Check(OrderRules.Validate(o, order) != null, "remote focus rejected");
        c.QoL = c.ClothPerCapita = c.HousingPerCapita = c.FoodNeedsMetRatio = c.WaterNeedsMetRatio = c.RudimentaryExpectation = 0;
        c.CraftingPriorities.Cloth = 0; w.Config.Neolithic.FabricConsumptionPerPersonTurn = 0;
        c.StonePriority = c.ClayPriority = c.WoodPriority = 0;
        w.Tiles[c.Tile].CultivationCapital = 1; w.Tiles[c.Tile].Cultivation = .4f; w.Tiles[c.Tile].CultivationOperator = c.Id;
        var loaded = SaveCodec.Load(SaveCodec.Save(w)); var lc = loaded.Camps[0];
        Near(lc.QoL + lc.ClothPerCapita + lc.HousingPerCapita + lc.FoodNeedsMetRatio + lc.WaterNeedsMetRatio + lc.RudimentaryExpectation, 0, "legitimate zero needs and expectation survive");
        Near(loaded.Config.Neolithic.FabricConsumptionPerPersonTurn, 0, "zero sweep setting survives"); Near(loaded.Tiles[c.Tile].CultivationCapital, 1, "capital saved"); Check(loaded.Tiles[c.Tile].CultivationOperator == c.Id, "operator saved");
        Near(lc.StonePriority + lc.ClayPriority + lc.WoodPriority, 0, "explicit zero material priorities survive new-save loading");
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "complete save fingerprint roundtrip");
        string before = SaveCodec.Fingerprint(w); ObservationContract.Serialize(builder.Build(w, 0), c.Mandate); Check(before == SaveCodec.Fingerprint(w), "observations and legal masks are pure");
        Check(!NeolithicProduction.CanIrrigateTile(w, c, c.Tile, out _), "irrigation control disabled");
    }
    private static void AdditionalContracts()
    {
        var w = Fixture(); AddResources(w); var c = w.Camps[0];
        c.Workforce.Assign(WorkforceRoles.Craftsperson, 4, c.Population);
        c.Inventory.CopperTools = 4; Near(NeolithicProduction.Demand(w, c, CraftGood.HandTools, 20), 4, "locked Copper Tools cannot suppress usable tool demand"); c.Inventory.CopperTools = 0;
        c.CraftingPriorities = new CraftingPriorities { HandTools = 0, Pots = 0, Cloth = 0, FarmTools = 0, Housing = 1, Tools = 0 };
        double equipment = 0; var r = new TurnRecord();
        MaterialEconomy.Gather(w, c, NeolithicProduction.Recipes(w.Config), 80, 20, 800, ref equipment, r);
        Check(r.MaterialTransfers.Count == 0, "uncovered housing cannot create a current production forecast");
        c.CraftingPriorities.Housing = 0; c.CraftingPriorities.Tools = 1;
        MaterialEconomy.Gather(w, c, NeolithicProduction.Recipes(w.Config), 80, 20, 800, ref equipment, r);
        Check(r.MaterialTransfers.Count == 0, "generalists cannot forecast specialist-only Tools");
        c.CraftingPriorities.Tools = 0; c.CraftingPriorities.Housing = 1; c.Inventory.HandTools = 4; equipment = 320;
        w.Resources.Wood[c.Tile] = 0;
        MaterialEconomy.Gather(w, c, NeolithicProduction.Recipes(w.Config), 80, 20, 800, ref equipment, r);
        Near(c.Inventory.Clay, 5, "housing forecast and reserve accept clay-only supply"); Near(c.Inventory.Wood, 0, "missing wood does not block clay substitution or conjure wood");
        Check(typeof(MaterialSourceObservation).GetField("Initial") == null && typeof(MaterialSourceObservation).GetField("Share") == null, "public source observations omit unobserved body totals and geometry");
        var source = MaterialEconomy.KnownSources(w, c, 0).First(s => s.Material == MaterialId.Stone);
        double initial = source.Available;
        MaterialEconomy.Extract(w, c, source, WorkforceRoles.Gatherer, initial * 4, initial, 0, r);
        Near(MaterialEconomy.KnownSources(w, c, 0).First(s => s.Material == MaterialId.Stone).Available, 0, "finite source depletes exactly");
        Near(MaterialEconomy.Extract(w, c, source, WorkforceRoles.Gatherer, 100, 100, 0, r), 0, "stale source handle cannot extract exhausted stock");
        var saved = SaveCodec.Load(SaveCodec.Save(w)); Near(saved.MaterialSources.Single(s => s.Material == MaterialId.Stone).Remaining, 0, "zero depleted source survives load");
        var builder = new AgentObservationBuilder(); var o = builder.Build(w, 0); var plan = EconomicIntent.From(c); plan.Priorities.Cloth = .37;
        var order = new Order(OrderKind.Economy, c.Id) { Economy = plan };
        Check(LegalActions.Build(o).Single(m => m.Entity == c.Id).Allows(order) && OrderRules.Validate(o, order) == null, "parameterized mask and resolver accept the same economic plan");
        plan.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = c.Population });
        Check(!LegalActions.Build(o).Single(m => m.Entity == c.Id).Allows(order), "mask rejects invalid parameterized plan");
        plan.Assignments = null; plan.Priorities = null;
        string stamp = order.ToString(); Check(stamp != null && OrderRules.Validate(o, order) != null, "malformed queued economic intent logs safely and is rejected");
        var invalid = Fixture(); invalid.Camps[0].Inventory.Cloth = double.NaN; bool rejected = false;
        try { SaveCodec.Load(SaveCodec.Save(invalid)); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "invalid numeric inventory is rejected on load");
        var legacy = Fixture(); legacy.Camps[0].Inventory.Houses = 3; legacy.Camps[0].Inventory.HousingUnits = 5;
        legacy.Tiles[legacy.Camps[0].Tile].Cultivation = .2f;
        var migrated = SaveCodec.Load(Legacy10(legacy));
        Near(migrated.Camps[0].Inventory.HousingUnits, 7, "legacy houses plus partial construction migrate once");
        Near(migrated.Camps[0].StonePriority + migrated.Camps[0].ClayPriority + migrated.Camps[0].WoodPriority, 3, "absent legacy sourcing fields receive real defaults");
        Check(migrated.Camps[0].CultivationFocusTile == -1, "absent legacy focus is not tile zero");
        var preAgriculture = Fixture(false); preAgriculture.Camps[0].Workforce.Assign(WorkforceRoles.Farmer, 10, 100);
        Check(SaveCodec.Load(Legacy10(preAgriculture)).Camps[0].Workforce.Assigned == 0, "impossible legacy pre-Agriculture jobs are released to real gatherers");
        Near(migrated.Tiles[legacy.Camps[0].Tile].CultivationCapital, .25, "legacy q migrates to physical E", 1e-7);
        Near(SaveCodec.Load(SaveCodec.Save(migrated)).Camps[0].Inventory.HousingUnits, 7, "new-format reload never repeats legacy conversion");

        var covered = Fixture(); var cc = covered.Camps[0]; cc.Inventory.HandTools = cc.Inventory.Tools = 1; cc.Inventory.Wood = 100; cc.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        var coverage = new ToolCoverageBudget(covered, cc, 80); double toolTime = coverage.Remaining;
        var housingRecipe = NeolithicProduction.Recipes(covered.Config).Single(x => x.Good == CraftGood.Housing); var production = new TurnRecord();
        NeolithicProduction.Produce(covered, cc, housingRecipe, WorkforceRoles.Builder, 80, 80, 100, ref toolTime, production, coverage);
        NeolithicProduction.Produce(covered, cc, housingRecipe, WorkforceRoles.Craftsperson, 80, 80, 100, ref toolTime, production, coverage);
        Near(cc.Inventory.HousingUnits, 3.75 + .625, "Improved Tool time cannot be reused by another housing role"); Near(toolTime, 0, "shared coverage exhausted exactly");
        var bootstrap = Fixture(); bootstrap.Camps[0].Inventory.Stone = 10; var noCoverage = new ToolCoverageBudget(bootstrap, bootstrap.Camps[0], 80); toolTime = 0;
        NeolithicProduction.Produce(bootstrap, bootstrap.Camps[0], NeolithicProduction.Recipes(bootstrap.Config)[0], WorkforceRoles.Craftsperson, 80, 80, 1, ref toolTime, new TurnRecord(), noCoverage);
        Near(noCoverage.Remaining + toolTime, 0, "newly produced tools cannot backdate coverage");

        var whole = Fixture(); var split = Fixture(); int field = whole.Camps[0].Tile;
        foreach (var example in new[] { whole, split }) { example.Tiles[field].CultivationCapital = 1; example.Tiles[field].CultivationOperatingFraction = .3; }
        var full = EcologicalPressure.Integrate(whole, field, new[] { new Demand { Entity = whole.Camps[0].Id, Tile = field, Kind = "Cultivation", Food = 100 } }, 0, 7300);
        for (int year = 0; year < 20; year++) EcologicalPressure.Integrate(split, field, new[] { new Demand { Entity = split.Camps[0].Id, Tile = field, Kind = "Cultivation", Food = 5 } }, year * 365, 365).View.Commit(split.Tiles[field]);
        Near(split.Tiles[field].CultivationCapital, full.View.CultivationCapital, "operated E decay partitioned by year");
        Near(split.Tiles[field].Food, full.View.Food, "decaying E and shared food ecology partition together");
        Near(split.Tiles[field].PotentialFood, full.View.PotentialFood, "decaying E and biomass ecology partition together");
        var building = Fixture(); var yearly = Fixture();
        foreach (var example in new[] { building, yearly }) { example.Config.MaintenanceCoefficient = 0; example.Config.Mortality = 0; example.Camps[0].Workforce.Assign(WorkforceRoles.Farmer, 100, 100); }
        yearly.Config.TurnDays = 365;
        new TurnResolver().Resolve(building, new Dictionary<int, List<Order>>());
        for (int year = 0; year < 20; year++) new TurnResolver().Resolve(yearly, new Dictionary<int, List<Order>>());
        foreach (int tile in CultivationEconomy.Footprint(building, building.Camps[0]))
        {
            Near(yearly.Tiles[tile].CultivationCapital, building.Tiles[tile].CultivationCapital, "constant construction plus abandonment is duration partition invariant");
            Near(LazyEcology.ProjectEcology(yearly, tile, yearly.Days).Food, LazyEcology.ProjectEcology(building, tile, building.Days).Food, "construction and ecology use the same seasonal work barriers");
        }

        var maintained = Fixture(); var annualMaintenance = Fixture();
        foreach (var example in new[] { maintained, annualMaintenance })
        {
            example.Config.Mortality = 0;
            example.Camps[0].Workforce.Assign(WorkforceRoles.Farmer, 100, 100);
        }
        annualMaintenance.Config.TurnDays = 365;
        new TurnResolver().Resolve(maintained, new Dictionary<int, List<Order>>());
        for (int year = 0; year < 20; year++) new TurnResolver().Resolve(annualMaintenance, new Dictionary<int, List<Order>>());
        foreach (int tile in CultivationEconomy.Footprint(maintained, maintained.Camps[0]))
            Near(annualMaintenance.Tiles[tile].CultivationCapital, maintained.Tiles[tile].CultivationCapital, "seasonal upkeep and expansion split is invariant for a constant farmer assignment");

        var urban = Fixture(); var town = urban.Camps[0]; town.Population = 5000;
        // Food already delivered by an upstream logistics system enters ordinary stock.
        // Its provenance is outside this pass; local cultivation is deliberately absent.
        town.PlantFood = 5000 * urban.Config.GenerationIntervals * 2;
        foreach (var tile in urban.Tiles) { tile.Food = tile.PotentialFood = 0; for (int season = 0; season < 4; season++) tile.Seasons[season].MaxFood = tile.Seasons[season].MaxPotentialFood = 0; }
        var urbanTurn = new TurnResolver().Resolve(urban, new Dictionary<int, List<Order>>());
        Check(town.Population == 5000 && town.FoodNeedsMetRatio == 1, "import-fed 5000-person town needs no local agricultural self-sufficiency");
        Near(urbanTurn.Transfers.Sum(t => t.Food), 0, "import compatibility does not create local yield");
        Near(town.PlantFood, 5000 * urban.Config.GenerationIntervals, "imported food is consumed and retained in ordinary reserve budget");

        var textileTown = Fixture(); var textileCamp = textileTown.Camps[0];
        textileCamp.PlantFood = 0; textileCamp.AnimalFood = 16000;
        textileCamp.Workforce.Assign(WorkforceRoles.Craftsperson, 5, 100); textileCamp.Inventory.HandTools = 5;
        textileCamp.Inventory.HousingUnits = 25; textileTown.Config.Neolithic.UsefulFinishedGoodsReserveTurns = 0;
        Near(TurnResolver.UsefulCampFoodDemand(textileTown, textileCamp, 80, 20), 100, "real Fabric need adds plant feedstock to the shared useful food budget");
        var textileTurn = new TurnResolver().Resolve(textileTown, new Dictionary<int, List<Order>>());
        Near(textileTurn.Transfers.Where(t => t.Entity == textileCamp.Id).Sum(t => t.Food), 100, "non-plant reserves cannot mask useful plant-feedstock gathering");
        Near(textileCamp.FabricConsumed, 100, "gathered plant feedstock survives ordinary food accounting until actual Fabric consumption");
        Near(textileCamp.PlantFood, 0, "exactly one gathered PlantFood per Fabric is debited");
        Near(textileCamp.Food, 8000, "manufacturing preserves the biological reserve without a second food pool");
        Near(textileTurn.Demographics.Single().ManufacturingFood, 100, "Fabric inputs appear in the conserved food report");
        Near(textileTurn.Demographics.Single().DiscardedInventory, 0, "industrial food acceptance cannot cause harvest waste");
        var uncoveredTextile = Fixture(); uncoveredTextile.Camps[0].PlantFood = 0; uncoveredTextile.Camps[0].AnimalFood = 16000;
        uncoveredTextile.Camps[0].Workforce.Assign(WorkforceRoles.Craftsperson, 5, 100);
        Near(TurnResolver.UsefulCampFoodDemand(uncoveredTextile, uncoveredTextile.Camps[0], 80, 20), 0, "missing tool coverage cannot create speculative textile feedstock demand");

        foreach (double consumption in new[] { .25, .5, 1, 2, 4 })
        {
            var sweep = Fixture(); var person = sweep.Camps[0]; sweep.Config.Neolithic.FabricConsumptionPerPersonTurn = consumption;
            person.Inventory.Cloth = person.Population * consumption; NeolithicProduction.ResolveNeeds(sweep, person, 20);
            Near(person.Inventory.Cloth, 0, "Fabric sweep inventory debit " + consumption); Near(person.ClothPerCapita, 1, "Fabric sweep saturation " + consumption);
        }
        foreach (double peoplePerUnit in new[] { 3.0, 4, 5, 6 }) foreach (double decay in new[] { .05, .10, .15, .20 })
        {
            var sweep = Fixture(); var person = sweep.Camps[0]; sweep.Config.Neolithic.HousingPeoplePerUnit = peoplePerUnit; sweep.Config.Neolithic.HousingDecayPerTurn = decay;
            person.Inventory.HousingUnits = person.Population / peoplePerUnit; NeolithicProduction.DepreciateHousing(sweep, person, 20);
            Near(person.HousingDemand, person.Population / peoplePerUnit * decay, "Housing replacement sweep"); Near(person.HousingGrowthDemand, 0, "Housing sweep no double-counted growth");
        }
    }
    private static void Smoke()
    {
        var a = Fixture(); AddResources(a); var c = a.Camps[0]; c.Inventory.HandTools = 20; c.Inventory.HousingUnits = 25; c.Inventory.Cloth = 100;
        c.Workforce.Assign(WorkforceRoles.Farmer, 20, 100); c.Workforce.Assign(WorkforceRoles.Craftsperson, 10, 100);
        var b = SaveCodec.Load(SaveCodec.Save(a));
        for (int turn = 0; turn < 5; turn++)
        {
            var oa = AiPlanning.Decide(a); var ob = AiPlanning.Decide(b);
            var ra = new TurnResolver().Resolve(a, oa.Orders);
            new TurnResolver(new PhaseExecution(2, 1, true, 1)).Resolve(b, ob.Orders);
            Check(SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(b), "scalar parallel deterministic Neolithic turn " + turn);
            Check(ra.Demographics.All(d => d.DiscardedInventory < 1e-6), "one useful budget no harvest waste " + turn);
            Check(a.Camps.All(x => x.Workforce.Assigned <= x.Population), "no phantom workforce " + turn);
            Check(a.Tiles.All(t => t.Food <= t.PotentialFood + 1e-8), "food biomass invariant " + turn);
            b = SaveCodec.Load(SaveCodec.Save(b));
        }
        Console.WriteLine("NEOLITHIC_SMOKE seed=101 turns=5 population=" + a.Camps[0].Population + " q=" + a.Tiles.Max(t => t.Cultivation) + " QoL=" + a.Camps[0].QoL + " receipts=" + a.History.Sum(r => r.MaterialTransfers.Count));
    }
    private static byte[] Legacy10(WorldState world)
    {
        // Historical tile layout: 97 bytes through q, then four 64-byte seasons.
        // Envelope 11 inserts E (double) and operator (int) between those sections.
        using (var input = new MemoryStream(SaveCodec.Save(world)))
        using (var unzip = new GZipStream(input, CompressionMode.Decompress))
        using (var reader = new BinaryReader(unzip))
        using (var output = new MemoryStream())
        {
            using (var zip = new GZipStream(output, CompressionLevel.Fastest, true))
            using (var writer = new BinaryWriter(zip))
            {
                reader.ReadString(); writer.Write("ANTHROPOCENE-10"); int header = reader.ReadInt32();
                using (var xml = XmlDictionaryReader.CreateBinaryReader(reader.ReadBytes(header), XmlDictionaryReaderQuotas.Max))
                using (var metadata = new MemoryStream())
                {
                    var document = XDocument.Load(xml);
                    string[] absent = { "Neolithic", "MaterialSources", "MaterialKnowledge", "StonePriority", "ClayPriority", "WoodPriority", "CultivationFocusTile", "WaterNeedsMetRatio", "FarmerProductivity", "ReferenceIntensity", "CultivationRegenFlux", "MaintenanceCoefficient", "GathererBridgeCeiling", "GathererBridgeEfficiency", "CopperTools", "Production", "MaterialTransfers", "ManufacturingFood", "Economy", "StationaryYears", "RudimentaryExpectation", "FabricConsumed", "HousingDemand", "HousingGrowthDemand", "HousingReplacementDemand", "PotsTarget", "FarmToolsTarget" };
                    document.Descendants().Where(element => absent.Contains(element.Name.LocalName)).Remove();
                    document.Descendants().Where(element => element.Name.LocalName == "Tools" && element.Parent.Name.LocalName == "CraftingPriorities").Remove();
                    using (var binary = XmlDictionaryWriter.CreateBinaryWriter(metadata, null, null, false)) document.WriteTo(binary);
                    writer.Write((int)metadata.Length); writer.Write(metadata.ToArray());
                }
                int tiles = reader.ReadInt32(); writer.Write(tiles);
                for (int i = 0; i < tiles; i++) { writer.Write(reader.ReadBytes(97)); reader.ReadDouble(); reader.ReadInt32(); writer.Write(reader.ReadBytes(256)); }
                unzip.CopyTo(zip);
            }
            return output.ToArray();
        }
    }
}
