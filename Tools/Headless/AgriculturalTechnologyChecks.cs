using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class AgriculturalTechnologyChecks
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("Agricultural technology: " + message); }
    private static void Near(double value, double expected, string message, double tolerance = 1e-8)
        => Check(Math.Abs(value - expected) <= tolerance * Math.Max(1, Math.Abs(expected)), message + $" ({value} != {expected})");
    internal static WorldState Fixture()
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0];
        w.Config.Agriculture = new AgriculturalTechnologySettings { PloughEnabled = true, IrrigationEnabled = true };
        w.Config.CropHarvestingOnly = true; w.Config.CropMoistureReference = .5;
        w.Config.SharedCropHerdAllocationEnabled = true;
        c.IsVillage = true; c.EconomicLabor = new EconomicLaborPlan();
        c.People.NeolithicTechnologies.AddRange(new[] { NeolithicTech.Carpentry, NeolithicTech.Construction });
        c.Workforce.Assign(WorkforceRoles.Farmer, 40, c.Population);
        c.Inventory.FarmTools = 40; c.Inventory.HousingMaterial = 1000; c.Inventory.Tools = 1000;
        foreach (int id in CultivationEconomy.Footprint(w, c))
        {
            var t = w.Tiles[id]; t.Elevation = .1f;
            t.CultivationCapital = 1; t.Cultivation = .4f; t.Cultivated = true;
            t.CultivationOperator = c.Id; t.CultivationOperatingFraction = 1;
        }
        int source = w.Map.Neighbors(c.Tile).First(); w.Tiles[source].FreshwaterSource = FreshwaterSource.River;
        w.Tiles[source].Elevation = .2f;
        for (int season = 0; season < 4; season++) w.Tiles[c.Tile].Seasons[season].Moisture = .1f;
        return w;
    }
    private static void Plough()
    {
        foreach (double years in new[] { 1.0 / 365, 10.0 / 365, 1, 10, 20 })
            foreach (double fraction in new[] { 0.0, .5, 1.0 })
            {
                var w = Fixture(); var c = w.Camps[0]; w.Config.TurnDays = years * w.Config.DaysPerYear;
                c.Inventory.FarmTools = 40 * years / w.Config.Neolithic.FarmToolFarmerYearsPerUnit * fraction;
                double primitiveHarvest = CultivationEconomy.HarvestProductivity(w, c);
                c.People.NeolithicTechnologies.Add(NeolithicTech.Plough);
                var budget = new FarmToolWorkBudget(w, c);
                var labor = CultivationEconomy.PrepareHorizon(w, c, w.Days, years, budget);
                Near(labor.OperatingCoveredWorkerYears, 40 * years * fraction, "service is actual covered worker time");
                Near(budget.ReservedMaintenanceWorkerYears, labor.OperatingCoveredWorkerYears, "overlap charged once");
                Check(labor.HarvestProductivity >= primitiveHarvest - 1e-10, "crop labor benefits with tools");
                if (fraction == 0) Near(labor.HarvestProductivity, primitiveHarvest, "knowledge without tools has no harvest benefit");
                var allocation = CultivationEconomy.PlanHorizonExpansion(w, c, years, 0, 0, budget, labor.Footprint);
                CultivationEconomy.CommitHorizonExpansion(w, c, allocation, new TurnRecord(), budget);
                Near(c.Inventory.FarmTools, 0, "service debits once and remains nonnegative");
            }
        var world = Fixture(); var camp = world.Camps[0];
        camp.People.NeolithicTechnologies.Add(NeolithicTech.Plough);
        camp.EconomicLabor.ExpansionFarmers = 15;
        var l = CultivationEconomy.PrepareHorizon(world, camp, 0, 20, new FarmToolWorkBudget(world, camp));
        Near(l.OperatingFarmers, 25, "expansion excluded from operation");
        Near(l.OperatingCoveredWorkerYears, 500, "expansion excluded from plough service");
    }
    private static void Discovery()
    {
        var w = Fixture(); var c = w.Camps[0];
        var farm = NeolithicProduction.Recipes(w.Config).Single(r => r.Good == CraftGood.FarmTools);
        AgriculturalTechnology.RecordCraft(w, c, farm, WorkforceRoles.Carpenter, .125);
        Near(c.People.CraftExperience[NeolithicTech.Plough], .125, "fractional useful tool practice");
        Check(!c.People.HasTech(NeolithicTech.Plough), "no immediate knowledge unlock");
        AgriculturalTechnology.RecordCraft(w, c, farm, WorkforceRoles.Gatherer, 100);
        Near(c.People.CraftExperience[NeolithicTech.Plough], .125, "unrelated role gives no practice");
        c.People.CraftExperience[NeolithicTech.Plough] = w.Config.Neolithic.CraftMasteryThreshold;
        AgriculturalTechnology.Close(w, new TurnRecord());
        Check(c.People.HasTech(NeolithicTech.Plough), "attainment commits at Close");
        var loaded = SaveCodec.Load(SaveCodec.Save(w));
        Near(loaded.Camps[0].People.CraftExperience[NeolithicTech.Plough], w.Config.Neolithic.CraftMasteryThreshold, "practice persists");
    }
    private static void GatesAndCosts()
    {
        var w = Fixture(); var c = w.Camps[0];
        Check(IrrigationEconomy.FertileGate(w, c), "mature natural footprint eligible");
        int fertile = CultivationEconomy.Footprint(w, c).First(id => id != c.Tile);
        w.Tiles[fertile].CultivationCapital = 0;
        Check(!IrrigationEconomy.FertileGate(w, c), "one undeveloped fertile tile blocks");
        foreach (int id in CultivationEconomy.Footprint(w, c))
            for (int s = 0; s < 4; s++) w.Tiles[id].Seasons[s].Moisture = .1f;
        Check(IrrigationEconomy.FertileGate(w, c), "empty fertile set permits consideration");
        var project = IrrigationEconomy.Opportunity(w, c);
        Check(project != null, "known dry opportunity does not require existing canal");
        Check(IrrigationEconomy.RouteValid(w, project.Source, project.Route), "gravity route feasible");
        Check(!IrrigationEconomy.RouteValid(w, project.Source, project.Route.Concat(new[] { project.Route[0] }).ToArray()), "cycles rejected");
        w.Tiles[project.Source].WaterBody = WaterKind.Saltwater;
        Check(!IrrigationEconomy.RouteValid(w, project.Source, project.Route), "saline source rejected");
        w.Tiles[project.Source].WaterBody = WaterKind.None;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
        var order = new ProductionOrder(new[] { CraftGood.CanalConstruction }, new Dictionary<CraftGood, double> { [CraftGood.CanalConstruction] = .5 });
        order.Irrigation = new IrrigationOrder { Project = project, Construction = .5 };
        var recipe = NeolithicProduction.Recipes(w.Config).Single(r => r.Good == CraftGood.CanalConstruction);
        var record = new TurnRecord { StartDays = 0, Duration = w.Config.TurnDays };
        double tools = 1000, before = c.Inventory.HousingMaterial;
        NeolithicProduction.Produce(w, c, recipe, WorkforceRoles.Builder, 10000, w.Config.GenerationIntervals, .5,
            ref tools, record, new ToolCoverageBudget(w, c, w.Config.GenerationIntervals), order: order);
        Near(before - c.Inventory.HousingMaterial, .5 * recipe.HousingMaterial, "construction and commissioned service consume real housing materials");
        Near(w.Canals.Sum(s => s.Construction), .5, "partial construction persists");
        Check(w.IrrigatedFields.Count == 0, "incomplete construction gives no field service");
    }
    private static void WaterAndSoil()
    {
        var w = Fixture(); var c = w.Camps[0]; var project = IrrigationEconomy.Opportunity(w, c);
        var order = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>())
            { Irrigation = new IrrigationOrder { Project = project, Construction = project.Remaining } };
        IrrigationEconomy.Produce(w, c, CraftGood.CanalConstruction, project.Remaining, order, 0);
        Check(w.Canals.All(s => s.FundedYears > 0), "completed infrastructure activates with paid commissioning service");
        foreach (var s in w.Canals) s.FundedYears = 1000;
        var allocation = IrrigationEconomy.Allocate(w, Array.Empty<Demand>(), 0, 365);
        Check(allocation.Delivery > 0, "paid connected canal delivers water");
        Near(allocation.Withdrawal, allocation.Delivery + allocation.Loss, "transport conservation");
        var source = project.Source;
        var native = EcologicalPressure.Integrate(w, source, allocation.Withdrawals, 0, 365);
        Near(native.Transfers.Sum(t => t.Freshwater), allocation.Withdrawal, "native source funds exact envelope", 1e-7);
        var dry = IrrigationEconomy.Allocate(w, new[] { new Demand { Tile = source, Entity = c.Id, Kind = "Drinking", Freshwater = 1e9 } }, 0, 365);
        Near(dry.Delivery, 0, "essential needs protected");
        w.Tiles[source].FreshwaterSource = FreshwaterSource.Lake;
        var lake = IrrigationEconomy.Allocate(w, Array.Empty<Demand>(), 0, 365);
        Check(lake.Delivery > 0 && lake.EndingSoil.Values.All(v => v == 0), "lake irrigates without soil development");
        var cfg = w.Config.Agriculture;
        foreach (var pair in new[] { (100.0, .2), (250.0, .5), (500.0, 1.0), (600.0, 1.0) })
            Near(IrrigationEconomy.SoilAdvance(0, pair.Item1, 1, 1, .4, cfg), pair.Item2, "soil milestone");
        Near(IrrigationEconomy.SoilAdvance(.7, 100, 0, 1, .4, cfg), .7, "shutdown retains soil");
        Near(IrrigationEconomy.SoilAdvance(0, 1000, .5, 1, .4, cfg), 1, "half supply matures at 1000 years");
        Near(IrrigationEconomy.SoilAdvance(0, 1.0 / 365, 1, 1, .4, cfg), 1.0 / (365 * 500), "one-day fraction");
        double split = 0; for (int i = 0; i < 365; i++) split = IrrigationEconomy.SoilAdvance(split, 1.0 / 365, 1, 1, .4, cfg);
        Near(split, IrrigationEconomy.SoilAdvance(0, 1, 1, 1, .4, cfg), "soil clock subdivision");
        var field = w.IrrigatedFields[0]; field.Soil = .7;
        var loaded = SaveCodec.Load(SaveCodec.Save(w));
        Near(loaded.IrrigatedFields[0].Soil, .7, "soil saved");
        Near(loaded.Canals[0].FundedYears, 1000, "paid canal service saved");
        var overlay = new IrrigationOverlay(1, .5, 1);
        var t = w.Tiles[c.Tile]; double food = t.Food, biomass = t.PotentialFood; var seasons = t.Seasons.ToArray();
        var a = EcologicalPressure.Integrate(w, c.Tile, Array.Empty<Demand>(), 0, 365, irrigationOverlay: overlay);
        Near(t.Food, food, "forecast creates no inventory"); Near(t.PotentialFood, biomass, "forecast pure");
        Check(t.Seasons.SequenceEqual(seasons), "immutable natural climate");
        Check(a.View.Food <= a.View.PotentialFood + 1e-8, "accessible food within biomass");
    }
    private static void AdditionalContracts()
    {
        var w = Fixture(); var c = w.Camps[0];
        var p = IrrigationEconomy.Opportunity(w, c);
        Check(p != null, "paid investment has a positive native retained-food forecast");
        var order = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>())
            { Irrigation = new IrrigationOrder { Project = p, Construction = p.Remaining } };
        bool rejected = false;
        try { IrrigationEconomy.Produce(w, c, CraftGood.CanalConstruction, 1, null, 0); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "no direct construction override");
        double elevation = w.Tiles[p.Target].Elevation;
        w.Tiles[p.Target].Elevation = 1;
        Check(!IrrigationEconomy.RouteValid(w, p.Source, p.Route), "uphill rejected");
        w.Tiles[p.Target].Elevation = (float)elevation;
        int ridge = w.Tiles[p.Source].RidgeMask; w.Tiles[p.Source].RidgeMask = 63;
        Check(!IrrigationEconomy.RouteValid(w, p.Source, p.Route), "blocked intake rejected");
        w.Tiles[p.Source].RidgeMask = ridge;
        var house = NeolithicProduction.Recipes(w.Config).Single(r => r.Good == CraftGood.Housing);
        AgriculturalTechnology.RecordCraft(w, c, house, WorkforceRoles.Builder, .125);
        Near(c.People.CraftExperience[NeolithicTech.Irrigation], .125, "useful housing practice qualifies before q gate");
        Check(!c.People.HasTech(NeolithicTech.Irrigation), "irrigation discovery also waits for Close");
        IrrigationEconomy.Produce(w, c, CraftGood.CanalConstruction, p.Remaining, order, 10);
        foreach (var segment in w.Canals) segment.FundedYears = .5;
        Near(IrrigationEconomy.Allocate(w, Array.Empty<Demand>(), 0, 365).Delivery, 0, "construction cannot activate retroactively");
        foreach (var segment in w.Canals) segment.ActiveDays = 0;
        w.Config.Agriculture.AnnualApplication = 10;
        var half = IrrigationEconomy.Allocate(w, Array.Empty<Demand>(), 0, 365);
        foreach (var segment in w.Canals) segment.FundedYears = 1;
        var full = IrrigationEconomy.Allocate(w, Array.Empty<Demand>(), 0, 365);
        Near(half.Delivery, full.Delivery / 2, "funded service exhausts fractionally");
        var field = w.IrrigatedFields[0];
        var overlay = full.Fields[field.Tile];
        var claims = new[] { new Demand { Tile = field.Tile, Entity = c.Id, Kind = "Cultivation", Food = 10 } };
        var forecast = FoodHorizonForecast.Tile(w, field.Tile, 0, 365, claims, irrigationOverlay: overlay);
        var resolution = EcologicalPressure.Integrate(w, field.Tile, claims, 0, 365, irrigationOverlay: overlay);
        Near(forecast.ClosingFood, resolution.View.Food, "food forecast uses authoritative irrigation transition");
        Near(forecast.ClosingBiomass, resolution.View.PotentialFood, "biomass forecast uses authoritative irrigation transition");
        Near(forecast.HumanFoodReceipts, resolution.Transfers.Sum(t => t.Food), "forecast receipts agree");
        Near(IrrigationEconomy.UpkeepDemand(w, c, 1), w.Canals.Count, "reserve one next interval after spending current funding");
        foreach (var segment in w.Canals) segment.FundedYears = 0;
        Near(IrrigationEconomy.UpkeepDemand(w, c, 1), w.Canals.Count, "unfunded continuity does not charge double service");
        field.Soil = .6;
        var successor = new Camp { Id = w.NextId++, Player = c.Player, Tile = c.Tile, Population = c.Population, IsVillage = true };
        successor.Bind(c.People); w.Camps.Clear(); w.Camps.Add(successor);
        foreach (int tile in CultivationEconomy.Footprint(w, successor)) w.Tiles[tile].CultivationOperator = successor.Id;
        IrrigationEconomy.ReconcileOwnership(w);
        Near(field.Soil, .6, "ownership transfer retains accumulated development");
        Check(field.Operator == successor.Id && w.Canals.All(x => x.Operator == successor.Id), "legal successor receives network continuity obligations");
        var saved = SaveCodec.Load(SaveCodec.Save(w));
        Check(saved.IrrigatedFields[0].Operator == successor.Id, "successor persists");
        var smooth = Fixture(); smooth.Config.SmoothCalendar = new SmoothCalendarSettings { Started = true };
        smooth.Days = 7300; smooth.History.Add(new TurnRecord { StartDays = 0, Duration = 7300 });
        smooth.Config.TurnDays = SmoothCalendar.ExpectedDays(smooth);
        var hunter = new Hunter { Id = smooth.NextId++, Player = 0, SourceCamp = smooth.Camps[0].Id, Tile = smooth.Camps[0].Tile, Population = 5 };
        hunter.CarriedFood = hunter.Population * Math.Max(smooth.Config.CarryingCapacityPerHunter,
            smooth.Config.Consumption * 7300 / smooth.Config.BaseDays * smooth.Config.HunterCarryGenerations);
        smooth.Hunters.Add(hunter); Ownership.Bind(smooth);
        Check(Supply.CommittedCapacity(smooth, hunter) >= hunter.CarriedFood, "shrinking calendar retains legally carried stock");
        Near(SaveCodec.Load(SaveCodec.Save(smooth)).Hunters[0].CarriedFood, hunter.CarriedFood, "smoothed-boundary provisions save roundtrip");
        var legacy = NeolithicChecks.Fixture();
        Check(SaveCodec.Load(SaveCodec.Save(legacy)).Config.Agriculture == null, "legacy save stays opt-out");
    }
    private static void ElderGatesAndSharing()
    {
        var w = Fixture(); var c = w.Camps[0];
        c.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
        string before = SaveCodec.Fingerprint(w); IrrigationEconomy.Opportunity(w, c);
        Check(SaveCodec.Fingerprint(w) == before, "prospective network forecast is pure");
        c.PlantFood = 1e9;
        DemandController.Plan(w, c, EconomicIntent.From(c), out var richOrder, out var richAllocation);
        Check(!richAllocation.Requests.Any(r => r.Good == CraftGood.CanalConstruction && r.Units > 0), "no investment without development need");
        c.PlantFood = 0; c.Inventory.HousingMaterial = 0;
        DemandController.Plan(w, c, EconomicIntent.From(c), out var poorOrder, out var poorAllocation);
        Check(!poorAllocation.Requests.Any(r => r.Good == CraftGood.CanalConstruction && r.Granted > 1e-8), "unfunded material bundle cannot build");
        var p = IrrigationEconomy.Opportunity(w, c);
        var order = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>())
            { Irrigation = new IrrigationOrder { Project = p, Construction = p.Remaining } };
        IrrigationEconomy.Produce(w, c, CraftGood.CanalConstruction, p.Remaining, order, 0);
        int other = w.Map.Neighbors(p.Source).First(id => id != p.Target && CultivationEconomy.Footprint(w, c).Contains(id));
        w.Tiles[other].Elevation = .1f;
        for (int season = 0; season < 4; season++) w.Tiles[other].Seasons[season].Moisture = .1f;
        var second = new CanalSegment { Id = w.NextId++, Operator = c.Id, Source = p.Source, From = p.Source,
            Tile = other, Construction = 1, FundedYears = 1000, Throughput = w.Config.Agriculture.AnnualThroughput };
        w.Canals.Add(second); w.IrrigatedFields.Add(new IrrigatedField { Tile = other, Operator = c.Id, Endpoint = second.Id });
        w.IrrigatedFields.Sort((a, b) => a.Tile.CompareTo(b.Tile));
        var sharing = IrrigationEconomy.Allocate(w, Array.Empty<Demand>(), 0, 365);
        Check(sharing.Receipts.Count == 2, "two fields share one finite source");
        Near(sharing.Receipts[0].Delivery, sharing.Receipts[1].Delivery, "equal water demand shares proportionally");
        var native = EcologicalPressure.Integrate(w, p.Source, sharing.Withdrawals, 0, 365);
        Near(native.Transfers.Sum(t => t.Freshwater), sharing.Withdrawal, "competing source debited once", 1e-7);
        int fertile = CultivationEconomy.Footprint(w, c).First(id => w.Tiles[id].Seasons[0].Moisture >= .5);
        w.Tiles[fertile].CultivationCapital = 0;
        foreach (var segment in w.Canals) segment.FundedYears = 0;
        c.Inventory.HousingMaterial = 1000;
        DemandController.Plan(w, c, EconomicIntent.From(c), out var maintenanceOrder, out var maintenanceAllocation);
        Check(!IrrigationEconomy.FertileGate(w, c), "continuity fixture blocks new investment");
        Check(maintenanceAllocation.Requests.Any(r => r.Good == CraftGood.CanalMaintenance && r.Units > 0), "continuity remains requested below cultivation gate");
        Check(!maintenanceAllocation.Requests.Any(r => r.Good == CraftGood.CanalConstruction), "individual fertile-tile gate suppresses new construction");
    }
    private static void CompleteTurns()
    {
        var w = Fixture(); var c = w.Camps[0];
        w.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        c.EconomyAutomationTurns = -1;
        c.People.NeolithicTechnologies.AddRange(new[] { NeolithicTech.Plough, NeolithicTech.Irrigation });
        c.Inventory.Stone = c.Inventory.Wood = c.Inventory.Clay = 10000;
        c.PlantFood = 0;
        var copy = SaveCodec.Load(SaveCodec.Save(w));
        var resolver = new TurnResolver();
        for (int turn = 0; turn < 4; turn++)
        {
            var record = resolver.Resolve(w, new Dictionary<int, List<Order>>());
            resolver.Resolve(copy, new Dictionary<int, List<Order>>());
            Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(copy), "complete-turn deterministic replay");
            Check(c.Inventory.HousingMaterial >= -1e-8 && c.Inventory.Tools >= -1e-8, "shared infrastructure budgets nonnegative");
            Check(record.Production.Sum(p => p.WorkerIntervals) <= c.Population * record.Duration / w.Config.BaseDays + 1e-8, "shared manufacturing workforce bounded");
            foreach (var receipt in record.IrrigationWater ?? new List<IrrigationWaterReceipt>())
                Near(receipt.Withdrawal, receipt.Delivery + receipt.Loss, "complete-turn water conservation");
        }
        TurnResolver.Invariants(w);
        Console.WriteLine("Agricultural complete-turn canals=" + (w.Canals?.Count ?? 0));
    }
    public static int Run()
    {
        checks = 0; Plough(); Discovery(); GatesAndCosts(); WaterAndSoil(); AdditionalContracts(); ElderGatesAndSharing(); CompleteTurns();
        Console.WriteLine("PASS: " + checks + " agricultural technology assertions"); return checks;
    }
}
