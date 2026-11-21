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
            t.CultivationOperator = c.Id; t.CultivationOperatingFraction = 0;
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
                Near(AgriculturalTechnology.MaintenanceEfficiency(w, c), w.Config.Agriculture.PloughMaintenance, "plough sets maintenance efficiency to 1.50");
                if (fraction > 0) Check(labor.HarvestProductivity > primitiveHarvest + 1e-4, "crop labor strictly benefits with tools");
                else Near(labor.HarvestProductivity, primitiveHarvest, "knowledge without tools has no harvest benefit");
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
        double totalDelivered = 0;
        for (int turn = 0; turn < 4; turn++)
        {
            var record = resolver.Resolve(w, new Dictionary<int, List<Order>>());
            resolver.Resolve(copy, new Dictionary<int, List<Order>>());
            Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(copy), "complete-turn deterministic replay");
            Check(c.Inventory.HousingMaterial >= -1e-8 && c.Inventory.Tools >= -1e-8, "shared infrastructure budgets nonnegative");
            Check(record.Production.Sum(p => p.WorkerIntervals) <= c.Population * record.Duration / w.Config.BaseDays + 1e-8, "shared manufacturing workforce bounded");
            foreach (var receipt in record.IrrigationWater ?? new List<IrrigationWaterReceipt>())
            {
                Near(receipt.Withdrawal, receipt.Delivery + receipt.Loss, "complete-turn water conservation");
                totalDelivered += receipt.Delivery;
            }
        }
        TurnResolver.Invariants(w);
        Check((w.Canals?.Count ?? 0) > 0 && totalDelivered > 0, "complete turns build and operate irrigation canals");
        Console.WriteLine("Agricultural complete-turn canals=" + (w.Canals?.Count ?? 0));
    }
    private static void CommissioningMaterialChain()
    {
        foreach (double years in new[] { 1.0 / 365, 10.373449306446295, 13.445341030213397, 20 })
        foreach (double stockFraction in new[] { 0.0, .5 })
        foreach (bool storage in new[] { false, true })
        {
            var w = Fixture(); var c = w.Camps[0]; w.Config.TurnDays = years * w.Config.DaysPerYear;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
            c.Workforce.Assign(WorkforceRoles.Farmer, 0, c.Population);
            w.Config.GoodsStorageEnabled = storage;
            c.Inventory.HousingUnits = 10000;
            c.Inventory.Wood = c.Inventory.Clay = 10000;
            var recipes = NeolithicProduction.Recipes(w.Config);
            var canal = recipes.Single(r => r.Good == CraftGood.CanalConstruction);
            var material = recipes.Single(r => r.Good == CraftGood.HousingMaterial);
            double units = Math.Min(1, years * .1), required = units * canal.HousingMaterial;
            double intervals = w.Config.GenerationIntervals, equipment = 10000;
            // Independently price the paid bundle; no surplus fifth Builder to mask the cap defect.
            double workerIntervals = required * (1 - stockFraction) / (material.Rate * w.Config.Neolithic.ImprovedToolsHousingMultiplier)
                + units / canal.Rate;
            int builders = (int)Math.Ceiling(workerIntervals / intervals);
            c.Workforce.Assign(WorkforceRoles.Builder, builders, c.Population);
            c.Inventory.HousingMaterial = required * stockFraction;
            var order = new ProductionOrder(new[] { CraftGood.HousingMaterial, CraftGood.CanalConstruction },
                new Dictionary<CraftGood, double> { [CraftGood.HousingMaterial] = required, [CraftGood.CanalConstruction] = units })
                { Irrigation = new IrrigationOrder { Project = IrrigationEconomy.Opportunity(w, c), Construction = units } };
            double demand = NeolithicProduction.Demand(w, c, CraftGood.HousingMaterial, years, c.Population, false, false, order: order);
            Near(demand, required * (1 - stockFraction), "manufacture includes commissioning upkeep net of carried stock");
            var forecast = MaterialEconomy.Forecast(w, c, recipes, intervals, years, equipment, false, c.Population, false, false, order: order);
            Near(forecast[CraftGood.CanalConstruction], units, "forecast completes funded construction from missing materials");
            GoodsStorage.PlannedUse(recipes, g => forecast[g], out double equipmentUse, out double housingUse, out double infrastructureUse);
            Near(infrastructureUse, required, "storage exempts exactly the canal commissioning materials");
            if (storage) c.GoodsStorage = GoodsStorage.Begin(w, c, years, c.Population, equipmentUse, housingUse, infrastructureMaterials: infrastructureUse);
            var record = new TurnRecord { StartDays = w.Days, Duration = w.Config.TurnDays };
            var coverage = new ToolCoverageBudget(w, c, intervals);
            double time = NeolithicProduction.Produce(w, c, recipes.Single(r => r.Good == CraftGood.HousingMaterial),
                WorkforceRoles.Builder, builders * intervals, intervals, demand, ref equipment, record, coverage, order: order);
            NeolithicProduction.Produce(w, c, canal, WorkforceRoles.Builder, builders * intervals - time, intervals, units,
                ref equipment, record, coverage, order: order);
            Near(record.Production.Where(p => p.Good == CraftGood.CanalConstruction).Sum(p => p.Output), units, "manufactured commissioning bundle completes authorized work in same interval");
            Near(c.Inventory.HousingMaterial, 0, "commissioning consumes manufactured plus carried materials exactly");
            Near(w.Canals.Sum(s => s.Construction), units, "committed progress agrees with forecast");
            Check(record.Production.Sum(p => p.WorkerIntervals) <= builders * intervals + 1e-8, "manufacture and assembly share Builder budget");
        }
    }
    private static void MixedConstructionCommitment()
    {
        foreach (int builders in new[] { 4, 8 })
        {
            var w = Fixture(); var c = w.Camps[0]; const double years = 10.373449306446295;
            w.Config.TurnDays = years * w.Config.DaysPerYear; w.Config.GoodsStorageEnabled = true;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
            c.Workforce.Assign(WorkforceRoles.Farmer, 0, c.Population);
            c.Workforce.Assign(WorkforceRoles.Builder, builders, c.Population);
            c.Inventory.HousingMaterial = c.Inventory.HousingUnits = 0; c.HousingDemand = 100;
            c.Inventory.FarmTools = 10000; // full store: current construction inputs must be in-use goods
            c.Inventory.Wood = c.Inventory.Clay = 10000;
            var recipes = NeolithicProduction.Recipes(w.Config);
            var canal = recipes.Single(r => r.Good == CraftGood.CanalConstruction);
            var housing = recipes.Single(r => r.Good == CraftGood.Housing);
            var material = recipes.Single(r => r.Good == CraftGood.HousingMaterial);
            var goods = new[] { CraftGood.HousingMaterial, CraftGood.Housing, CraftGood.CanalConstruction };
            var order = new ProductionOrder(goods, new Dictionary<CraftGood, double> {
                [CraftGood.HousingMaterial] = canal.HousingMaterial + housing.HousingMaterial,
                [CraftGood.Housing] = 1, [CraftGood.CanalConstruction] = 1 })
                { Irrigation = new IrrigationOrder { Project = IrrigationEconomy.Opportunity(w, c), Construction = 1 } };
            double intervals = w.Config.GenerationIntervals, equipment = 10000;
            var forecast = MaterialEconomy.Forecast(w, c, recipes, intervals, years, equipment, false, c.Population, false, false, order: order);
            var allocation = new DemandController.Allocation();
            var request = new DemandController.Request { Good = CraftGood.CanalConstruction, Granted = 1, GrantedBeforeProduction = 1 };
            allocation.Requests.Add(request);
            // Cached drafts must survive repeated preparation unchanged.
            var preparedAllocation = allocation.CopyForPreparation();
            var needs = new TurnNeeds { Order = order.CopyForPreparation(), Production = forecast };
            TurnPlanner.ReconcileInfrastructure(needs, preparedAllocation);
            Near(order.Ceiling(CraftGood.CanalConstruction), 1, "preparation leaves cached production ceilings intact");
            Near(order.Irrigation.Construction, 1, "preparation leaves cached construction authorization intact");
            Near(request.Granted, 1, "preparation leaves cached grant diagnostics intact");
            var repeat = new TurnNeeds { Order = order.CopyForPreparation(), Production = forecast };
            TurnPlanner.ReconcileInfrastructure(repeat, allocation.CopyForPreparation());
            Near(repeat.Order.Irrigation.Construction, needs.Order.Irrigation.Construction, "reusing a cached draft gives the same commitment");
            order = needs.Order; request = preparedAllocation.Requests[0];
            Near(request.GrantedBeforeProduction, 1, "original authorization remains visible after reconciliation");
            Near(request.Granted, forecast[CraftGood.CanalConstruction], "final canal grant agrees with executable shared Builder budget");
            Check(builders == 8 ? request.Granted >= 1 - 1e-8 : request.Granted < 1 - 1e-8,
                "mixed fixture distinguishes fully funded work from a constrained Builder pool");
            GoodsStorage.PlannedUse(recipes, g => forecast[g], out double equipmentUse, out double housingUse, out double infrastructureUse);
            c.GoodsStorage = GoodsStorage.Begin(w, c, years, c.Population, equipmentUse, housingUse, infrastructureMaterials: infrastructureUse);
            var record = new TurnRecord { StartDays = w.Days, Duration = w.Config.TurnDays };
            var coverage = new ToolCoverageBudget(w, c, intervals);
            double labor = builders * intervals;
            foreach (var good in goods)
            {
                var recipe = recipes.Single(r => r.Good == good);
                double demand = good == CraftGood.HousingMaterial ? forecast[good] : order.Ceiling(good);
                labor -= NeolithicProduction.Produce(w, c, recipe, WorkforceRoles.Builder, labor, intervals, demand,
                    ref equipment, record, coverage, order: order);
            }
            Near(record.Production.Where(p => p.Good == CraftGood.Housing).Sum(p => p.Output), forecast[CraftGood.Housing], "housing retains its own job quantity");
            Near(record.Production.Where(p => p.Good == CraftGood.CanalConstruction).Sum(p => p.Output), request.Granted, "actual canal work matches the reconciled grant");
            Check(labor >= -1e-8, "mixed construction cannot duplicate Builder time");
            Near(record.Production.Sum(p => p.HousingMaterial) + c.Inventory.HousingMaterial,
                record.Production.Where(p => p.Good == CraftGood.HousingMaterial).Sum(p => p.Output), "housing and canals conserve their shared material stock");
            var maintenance = recipes.Single(r => r.Good == CraftGood.CanalMaintenance);
            GoodsStorage.PlannedUse(recipes, g => g == CraftGood.CanalMaintenance ? years : 0,
                out _, out _, out double upkeepMaterials);
            Near(upkeepMaterials, years * maintenance.HousingMaterial, "continuity repairs receive their exact in-use material exemption");
            var closing = GoodsStorage.Begin(w, c, years, c.Population, equipmentUse, housingUse, endOfTurn: true, infrastructureMaterials: infrastructureUse);
            Near(closing.Limit(c, CraftGood.HousingMaterial, 1), 0, "closing storage does not retain the temporary construction exemption");
        }
    }
    private static void ReviewFixContracts()
    {
        // B2: Tight tool budget (exact 1.0 tool output = 0.25 Regular Tools) must not cap non-equipment HousingMaterial to 1 unit.
        {
            var w = Fixture(); var c = w.Camps[0];
            c.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
            c.Workforce.Assign(WorkforceRoles.Farmer, 0, c.Population);
            c.Workforce.Assign(WorkforceRoles.Builder, 8, c.Population);
            c.Inventory.HousingUnits = 10000;
            c.Inventory.HousingMaterial = 0;
            c.Inventory.HandTools = 0;
            c.Inventory.Tools = 0.25; // exact 1.0 tool output for 1.0 canal segment
            c.Inventory.Wood = c.Inventory.Clay = 10000;
            var recipes = NeolithicProduction.Recipes(w.Config);
            var canal = recipes.Single(r => r.Good == CraftGood.CanalConstruction);
            var material = recipes.Single(r => r.Good == CraftGood.HousingMaterial);
            double years = w.Config.TurnDays / w.Config.DaysPerYear, intervals = w.Config.GenerationIntervals, equipment = 10000;
            var project = IrrigationEconomy.Opportunity(w, c);
            var order = new ProductionOrder(new[] { CraftGood.HousingMaterial, CraftGood.CanalConstruction },
                new Dictionary<CraftGood, double> { [CraftGood.HousingMaterial] = canal.HousingMaterial, [CraftGood.CanalConstruction] = 1 })
                { Irrigation = new IrrigationOrder { Project = project, Construction = 1 } };
            var forecast = MaterialEconomy.Forecast(w, c, recipes, intervals, years, equipment, false, c.Population, false, false, order: order);
            Near(forecast[CraftGood.HousingMaterial], canal.HousingMaterial, "tight tool budget forecasts full canal housing materials");
            Near(forecast[CraftGood.CanalConstruction], 1, "tight tool budget forecasts full 1.0 canal segment");
            var record = new TurnRecord { StartDays = w.Days, Duration = w.Config.TurnDays };
            var coverage = new ToolCoverageBudget(w, c, intervals);
            double used = NeolithicProduction.Produce(w, c, material, WorkforceRoles.Builder, 8 * intervals, intervals, canal.HousingMaterial, ref equipment, record, coverage, order: order);
            NeolithicProduction.Produce(w, c, canal, WorkforceRoles.Builder, 8 * intervals - used, intervals, 1, ref equipment, record, coverage, order: order);
            Near(record.Production.Where(p => p.Good == CraftGood.CanalConstruction).Sum(p => p.Output), 1, "tight tool budget completes full 1.0 canal segment");
            Near(c.Inventory.Tools, 0, "exact 1.0 tool output consumed by canal assembly");
            Near(c.Inventory.HousingMaterial, 0, "all manufactured canal materials consumed");

            // Zero tools must still prevent building without tools.
            var zero = Fixture(); var zc = zero.Camps[0];
            zc.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
            zc.Workforce.Assign(WorkforceRoles.Builder, 8, zc.Population);
            zc.Inventory.HousingMaterial = zc.Inventory.HandTools = zc.Inventory.Tools = 0;
            zc.Inventory.Wood = zc.Inventory.Clay = 10000;
            var zf = MaterialEconomy.Forecast(zero, zc, recipes, intervals, years, equipment, false, zc.Population, false, false, order: order);
            Near(zf[CraftGood.HousingMaterial], 0, "zero tools cannot manufacture irrigation materials");
            Near(zf[CraftGood.CanalConstruction], 0, "zero tools cannot assemble canals");
        }

        // B1 & A1: Maintenance-capped village under Plough staffs harvest Farmers above maintenance cap
        // instead of shedding crop harvest to 1.0x Gatherers, and sustains FarmTools continuity on mature fields.
        {
            void MakeMaintenanceCappedHighYield(WorldState world, Camp camp)
            {
                world.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
                camp.Population = 250; camp.PlantFood = 0;
                camp.Inventory.Wood = camp.Inventory.Stone = 10000;
                var fp = CultivationEconomy.Footprint(world, camp);
                for (int i = 0; i < fp.Length; i++)
                {
                    var t = world.Tiles[fp[i]];
                    if (i == 0)
                    {
                        t.CultivationCapital = 1; t.Cultivation = .4f; t.Cultivated = true;
                        t.CultivationOperator = camp.Id; t.CultivationOperatingFraction = 0;
                        t.PotentialFood = 25000; t.Food = 12000;
                        for (int s = 0; s < 4; s++) { t.Seasons[s].MaxPotentialFood = 25000; t.Seasons[s].MaxFood = 12000; t.Seasons[s].Moisture = .5f; }
                    }
                    else
                    {
                        t.CultivationCapital = 0; t.Cultivation = 0; t.Cultivated = false;
                        t.CultivationOperator = -1; t.CultivationOperatingFraction = 0;
                        t.PotentialFood = 0; t.Food = 0;
                        for (int s = 0; s < 4; s++) { t.Seasons[s].MaxPotentialFood = 0; t.Seasons[s].MaxFood = 0; }
                    }
                }
            }
            var baseWorld = Fixture(); var bc = baseWorld.Camps[0];
            MakeMaintenanceCappedHighYield(baseWorld, bc);
            baseWorld.Config.Agriculture.PloughEnabled = false;
            bc.Inventory.FarmTools = 200;
            var baseIntent = DemandController.Plan(baseWorld, bc, EconomicIntent.From(bc), out _, out var baseAlloc);

            var ploughWorld = Fixture(); var pc = ploughWorld.Camps[0];
            MakeMaintenanceCappedHighYield(ploughWorld, pc);
            pc.People.NeolithicTechnologies.Add(NeolithicTech.Plough);
            pc.Inventory.FarmTools = 200;
            var ploughIntent = DemandController.Plan(ploughWorld, pc, EconomicIntent.From(pc), out _, out var ploughAlloc);

            double ploughMaint = CultivationEconomy.MaintenanceFarmersForPlanning(ploughWorld, pc, ploughWorld.Config.TurnDays / ploughWorld.Config.DaysPerYear);
            int ploughFarmers = ploughIntent.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population);
            double ploughOp = ploughFarmers - (ploughIntent.Labor?.ExpansionFarmers ?? 0);
            int baseFarmers = baseIntent.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population);
            double baseOp = baseFarmers - (baseIntent.Labor?.ExpansionFarmers ?? 0);
            Check(ploughOp > ploughMaint + 1, $"maintenance-capped Plough village staffs harvest Farmers above maintenance floor (ploughOp={ploughOp:F2}, ploughMaint={ploughMaint:F2}, baseOp={baseOp:F2}, ploughFarmers={ploughFarmers}, exp={ploughIntent.Labor?.ExpansionFarmers ?? 0:F2}, gatherers={ploughAlloc.FoodGatherers})");
            Check(ploughOp < baseOp, $"Plough uses strictly fewer operating Farmers than non-Plough for the same survival food (ploughOp={ploughOp:F2}, baseOp={baseOp:F2})");
            Check(ploughAlloc.FoodGatherers == 0, $"maintenance-capped Plough village does not shed crop harvest to survival Gatherers (gatherers={ploughAlloc.FoodGatherers})");

            // A1: FarmTools continuity on a mature village with operating Farmers > maintenance floor
            // must size for operating Farmers (via staffed.Labor.ExpansionFarmers = 0), not collapse to maintenanceFarmers.
            var matWorld = Fixture(); var mc = matWorld.Camps[0];
            MakeMaintenanceCappedHighYield(matWorld, mc);
            mc.People.NeolithicTechnologies.Add(NeolithicTech.Plough);
            mc.Inventory.FarmTools = 0;
            // Make all non-cultivated footprint tiles uncultivable (Elevation/Mountain or aquatic) so no expansion is possible.
            foreach (int id in CultivationEconomy.Footprint(matWorld, mc))
                if (matWorld.Tiles[id].CultivationCapital <= 0) matWorld.Tiles[id].Environment = EnvironmentKind.Ocean;
            double matYears = matWorld.Config.TurnDays / matWorld.Config.DaysPerYear;
            double matMaint = CultivationEconomy.MaintenanceWorkerRequirement(matWorld, mc) / matWorld.Config.Agriculture.PloughMaintenance;
            DemandController.Plan(matWorld, mc, EconomicIntent.From(mc), out _, out var matAlloc);
            var ftReq = matAlloc.Requests.FirstOrDefault(r => r.Good == CraftGood.FarmTools);
            double maintTools = matMaint * matYears / matWorld.Config.Neolithic.FarmToolFarmerYearsPerUnit;
            Check(ftReq != null && ftReq.Units > maintTools * 2, $"Plough FarmTools continuity counts operating Farmers above maintenance on mature fields (req={ftReq?.Units ?? 0:F2}, maintTools={maintTools:F2})");
        }

        // B3, M1 & B2-adv: CommitHorizonExpansion preserves irrigated biomass/food; ReclassifyLandFood
        // neither destroys food expanding above q=0.50 nor destroys standing wild food across q=0 -> q>0.
        {
            var w = Fixture(); var c = w.Camps[0];
            var project = IrrigationEconomy.Opportunity(w, c);
            var order = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>())
                { Irrigation = new IrrigationOrder { Project = project, Construction = project.Remaining } };
            IrrigationEconomy.Produce(w, c, CraftGood.CanalConstruction, project.Remaining, order, 0);
            foreach (var s in w.Canals) { s.ActiveDays = 0; s.FundedYears = 100; }
            var tile = w.Tiles[project.Target];
            double lowCapital = Math.Pow(0.15 / w.Config.ReferenceIntensity, 2);
            tile.CultivationCapital = lowCapital; tile.Cultivation = 0.15f; tile.Cultivated = true;
            double turnDays = w.Config.TurnDays, turnYears = turnDays / w.Config.DaysPerYear;
            var alloc = IrrigationEconomy.Allocate(w, Array.Empty<Demand>(), 0, turnDays);
            var overlay = alloc.Fields[project.Target];
            var integrated = EcologicalPressure.Integrate(w, project.Target, Array.Empty<Demand>(), 0, turnDays, irrigationOverlay: overlay);
            IrrigationEconomy.Commit(w, alloc, turnDays, turnYears);
            integrated.View.Commit(tile);
            double irrigatedFoodBefore = tile.Food, irrigatedBiomassBefore = tile.PotentialFood;
            var dryCap = new EcologyCapacity(w, tile, new EcologyView(tile), 0);
            Check(irrigatedBiomassBefore > dryCap.Biomass * 1.5 && irrigatedFoodBefore > dryCap.Food + 1e-3,
                "irrigated arid tile grows well above dryland biomass and food capacity");
            var exp = new HorizonCultivationAllocation
            {
                CampId = c.Id,
                Tiles = new[] { project.Target },
                StartCapital = new[] { lowCapital },
                StartOperators = new[] { c.Id },
                EndCapital = new[] { Math.Pow(0.18 / w.Config.ReferenceIntensity, 2) },
                BridgeWorkerYearsUsed = 0.1
            };
            CultivationEconomy.CommitHorizonExpansion(w, c, exp, new TurnRecord(), new FarmToolWorkBudget(w, c));
            Check(tile.Food >= irrigatedFoodBefore - 1e-6 && tile.PotentialFood >= irrigatedBiomassBefore - 1e-6,
                "CommitHorizonExpansion preserves irrigated food and biomass instead of clamping to dryland");

            // M1: expanding from q=0.50 to q=0.60 under active irrigation must not destroy standing irrigated food.
            double cap50 = Math.Pow(0.50 / w.Config.ReferenceIntensity, 2);
            double cap60 = Math.Pow(0.60 / w.Config.ReferenceIntensity, 2);
            tile.CultivationCapital = cap50; tile.Cultivation = 0.50f;
            var ov = new IrrigationOverlay(1, 0, 1);
            var view = new EcologyView(tile) { Days = 0, CultivationCapital = cap50 };
            ov.Apply(ref view);
            var capAt50 = new EcologyCapacity(w, tile, view, 0);
            tile.PotentialFood = capAt50.Biomass; tile.Food = capAt50.Food; tile.LastEcologyUpdateDays = 0;
            var grown = EcologicalPressure.Integrate(w, project.Target, Array.Empty<Demand>(), 0, 365,
                averageCultivationCapital: 0.5 * (cap50 + cap60), endingCultivationCapital: cap60, operatingFraction: 1, irrigationOverlay: ov);
            Check(grown.View.Food > tile.Food, "expanding irrigated arid field from q=0.50 to q=0.60 increases standing food");

            // B2-adv: entering cultivation q=0 -> q=0.05 on an irrigated tile must NOT destroy standing wild food.
            double cap05 = Math.Pow(0.05 / w.Config.ReferenceIntensity, 2);
            tile.CultivationCapital = 0; tile.Cultivation = 0; tile.Cultivated = false;
            var dryZero = new EcologyCapacity(w, tile, new EcologyView(tile) { Days = 0, CultivationCapital = 0 }, 0);
            tile.PotentialFood = dryZero.Biomass; tile.Food = dryZero.Food; tile.LastEcologyUpdateDays = 0;
            var reentered = EcologicalPressure.Integrate(w, project.Target, Array.Empty<Demand>(), 0, 30,
                averageCultivationCapital: cap05 * 0.5, endingCultivationCapital: cap05, operatingFraction: 1, irrigationOverlay: ov);
            Check(reentered.View.Food >= dryZero.Food - 1e-6, "entering cultivation from q=0 on an irrigated tile preserves standing wild food");
        }

        // M4: Zeno-free canal grant in the genuine Zeno regime (0 < usefulFood < 0.5 * AnnualFoodGain * years).
        {
            var w = Fixture(); var c = w.Camps[0];
            w.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
            c.Inventory.HousingUnits = 10000;
            c.Inventory.HousingMaterial = 1000;
            c.Inventory.Wood = c.Inventory.Clay = c.Inventory.Stone = 10000;
            c.Inventory.Cloth = 10000;
            var project = IrrigationEconomy.Opportunity(w, c);
            double years = w.Config.TurnDays / w.Config.DaysPerYear;
            double projectFood = project.AnnualFoodGain * years;
            // Find the exact carried food threshold that produces 0 < DevelopmentFoodGap < 0.4 * projectFood.
            c.Food = 0; c.PlantFood = 0;
            DemandController.Plan(w, c, EconomicIntent.From(c), out _, out var zeroAlloc);
            double fullGap = zeroAlloc.DevelopmentFoodGap;
            double targetGap = Math.Min(projectFood * 0.35, fullGap * 0.5);
            double carried = Math.Max(0, fullGap - targetGap);
            c.Food = carried; c.PlantFood = carried;
            DemandController.Plan(w, c, EconomicIntent.From(c), out var order, out var alloc);
            var canalReq = alloc.Requests.FirstOrDefault(r => r.Good == CraftGood.CanalConstruction);
            Check(alloc.DevelopmentFoodGap > 1e-3 && alloc.DevelopmentFoodGap < projectFood * 0.5,
                $"Zeno fixture hits genuine sub-project food gap ({alloc.DevelopmentFoodGap:F2} vs {projectFood:F2})");
            Check(canalReq != null && canalReq.Granted >= project.Remaining - 1e-6,
                "affordable canal development in Zeno regime authorizes full remaining segment");
            Check(order.Irrigation != null && order.Irrigation.Construction >= project.Remaining - 1e-6,
                "production order carries full canal completion grant in Zeno regime");
        }

        // M5, M6, B1-adv (duplicate key), B5-adv (ghost village), B6-adv (asymmetric water-filling).
        {
            var w = Fixture(); var c = w.Camps[0];
            double years = w.Config.TurnDays / w.Config.DaysPerYear;
            var p1 = IrrigationEconomy.Opportunity(w, c);
            int mid = p1.Target;
            int tail = CultivationEconomy.Footprint(w, c).First(id => id != mid && w.Map.Distance(mid, id) == 1);
            w.Tiles[tail].Elevation = w.Tiles[mid].Elevation;
            for (int s = 0; s < 4; s++) w.Tiles[tail].Seasons[s].Moisture = .1f;
            var twoHop = new IrrigationProject { Source = p1.Source, Target = tail, Route = new[] { mid, tail }, Remaining = 2, AnnualFoodGain = 100 };
            var buildOrder = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>())
                { Irrigation = new IrrigationOrder { Project = twoHop, Construction = 2 } };
            IrrigationEconomy.Produce(w, c, CraftGood.CanalConstruction, 2, buildOrder, 0);
            Check(w.Canals.Count == 2 && w.IrrigatedFields.Count == 1, "two-segment canal built");
            foreach (var seg in w.Canals) { seg.ActiveDays = 0; seg.FundedYears = 0; }
            Near(IrrigationEconomy.UpkeepDemand(w, c, years), 2 * years, "two active segments require 2*years upkeep");
            var upkeepOrder = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>())
                { Irrigation = new IrrigationOrder { Upkeep = years } };
            // Symmetric partial upkeep: 1 * years across 2 empty segments -> 0.5 * years each.
            IrrigationEconomy.Produce(w, c, CraftGood.CanalMaintenance, years, upkeepOrder, 0);
            Near(w.Canals[0].FundedYears, 0.5 * years, "segment 1 receives proportional 50% upkeep");
            Near(w.Canals[1].FundedYears, 0.5 * years, "segment 2 receives proportional 50% upkeep");
            var partialWater = IrrigationEconomy.Allocate(w, Array.Empty<Demand>(), 0, w.Config.TurnDays);
            Check(partialWater.Delivery > 0, "proportional partial upkeep delivers water instead of stranding downstream segments");
            // Asymmetric carryover water-filling: [0.5*years, 0] + 0.75*years -> [0.625*years, 0.625*years].
            w.Canals[0].FundedYears = 0.5 * years; w.Canals[1].FundedYears = 0;
            IrrigationEconomy.Produce(w, c, CraftGood.CanalMaintenance, 0.75 * years, upkeepOrder, 0);
            Near(w.Canals[0].FundedYears, 0.625 * years, "water-filling equalizes asymmetric segment 1 upward");
            Near(w.Canals[1].FundedYears, 0.625 * years, "water-filling equalizes asymmetric segment 2 upward");
            // B5-adv: Depopulated ghost village must NOT withdraw finite river water.
            int savedPop = c.Population; c.Population = 0;
            Near(IrrigationEconomy.Allocate(w, Array.Empty<Demand>(), 0, w.Config.TurnDays).Delivery, 0, "depopulated village withdraws zero water");
            Near(IrrigationEconomy.UpkeepDemand(w, c, years), 0, "depopulated village demands zero upkeep");
            c.Population = savedPop;
            // B1-adv: Duplicate (Source, Tile) segments owned by same camp (e.g. after abandonment + ownership transfer) must not crash Produce/CompatibleRoute.
            w.Canals.Add(new CanalSegment { Id = w.NextId++, Operator = c.Id, Source = p1.Source, From = p1.Source, Tile = mid,
                Construction = 1, FundedYears = years, Throughput = w.Config.Agriculture.AnnualThroughput });
            var dupOrder = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>())
                { Irrigation = new IrrigationOrder { Project = p1, Construction = 0.1 } };
            IrrigationEconomy.Produce(w, c, CraftGood.CanalConstruction, 0.1, dupOrder, 0);
            // Abandonment releases ownership and stops Continuity upkeep leak.
            IrrigationEconomy.ReleaseAbandoned(w, c.Id);
            Check(w.Canals.All(s => s.Operator < 0) && w.IrrigatedFields.All(f => f.Operator < 0), "abandonment releases canal and field ownership");
            Near(IrrigationEconomy.UpkeepDemand(w, c, years), 0, "abandoned canals demand zero upkeep from departed camp");
        }

        // M7a & B4: Minimum-cost routing branches off existing canal; fog-of-war hides unseen water claims.
        {
            var w = Fixture(); var c = w.Camps[0];
            var first = IrrigationEconomy.Opportunity(w, c);
            int source = first.Source;
            var neighbors = CultivationEconomy.Footprint(w, c).Where(id => w.Map.Distance(source, id) == 1).OrderBy(id => id).ToArray();
            Check(neighbors.Length >= 2, "routing fixture has at least two source-adjacent footprint neighbors");
            int a = neighbors[0], b = neighbors[1];
            int target = CultivationEconomy.Footprint(w, c).FirstOrDefault(id => id != a && id != b &&
                w.Map.Distance(source, id) == 2 && w.Map.Distance(a, id) == 1 && w.Map.Distance(b, id) == 1);
            Check(target > 0, "routing fixture has a shared two-hop dry target");
            foreach (int id in CultivationEconomy.Footprint(w, c))
                for (int s = 0; s < 4; s++) w.Tiles[id].Seasons[s].Moisture = id == target ? .1f : .6f;
            // Build existing segment on the higher-ID neighbor b so naive ID-order BFS would try unbuilt a first.
            var existing = new CanalSegment { Id = w.NextId++, Operator = c.Id, Source = source, From = source, Tile = b,
                Construction = 1, FundedYears = 20, Throughput = w.Config.Agriculture.AnnualThroughput };
            w.Canals = new List<CanalSegment> { existing };
            var chosen = IrrigationEconomy.Opportunity(w, c);
            Check(chosen != null && chosen.Target == target && chosen.Route.Length == 2 && chosen.Route[0] == b,
                "minimum-cost routing branches from existing canal segment instead of building redundant parallel path");
            Near(chosen.Remaining, 1, "branching off completed segment charges only 1 remaining segment");
            // Fog-of-war check: unseen camp at source must be excluded when tileKnown returns false.
            var rival = new Camp { Id = w.NextId++, Player = 1, Tile = source, Population = 100000 };
            w.Camps.Add(rival); Ownership.Bind(w);
            var hiddenClaims = IrrigationEconomy.EssentialForecastClaimsForSources(w, 20, new[] { source }, id => id != source);
            Check(!hiddenClaims.Any(d => d.Entity == rival.Id), "fog-of-war excludes unseen rival water claims");
            var visibleClaims = IrrigationEconomy.EssentialForecastClaimsForSources(w, 20, new[] { source }, _ => true);
            Check(visibleClaims.Any(d => d.Entity == rival.Id), "visible rival water claims are included locally");
        }

        // River-valley diversion up to ValleyMaxSlopeDegrees (25 deg / ~1200 m bank) allows gentle
        // valley terraces while rejecting steep gorges, excessive cumulative climbs, and mountain climbs.
        {
            var w = Fixture(); var c = w.Camps[0];
            w.Config.ElevationMetersPerUnit = 12000;
            var p = IrrigationEconomy.Opportunity(w, c);
            int source = p.Source, target = p.Target;
            w.Tiles[source].Elevation = .40f; w.Tiles[source].Relief = Relief.Lowland;
            w.Tiles[target].Relief = Relief.Lowland;
            double maxRise = IrrigationEconomy.ValleyRiseTolerance(w);
            Check(maxRise > 0.04 && maxRise < 0.05, "25 deg over 1200 m bank maps to ~0.0466 normalized rise");
            // Gentle valley terrace (~15 deg / +0.025 normalized / +300 m) is accepted.
            w.Tiles[target].Elevation = (float)(w.Tiles[source].Elevation + 0.025);
            Check(IrrigationEconomy.RouteValid(w, source, new[] { target }), "gentle river valley terrace within 25 deg is accepted");
            // Steep gorge (>25 deg / +0.06 normalized / +720 m) is rejected.
            w.Tiles[target].Elevation = (float)(w.Tiles[source].Elevation + 0.06);
            Check(!IrrigationEconomy.RouteValid(w, source, new[] { target }), "steep gorge rise above 25 deg is rejected");
            // Mountain wall climb is rejected even within 25 deg.
            w.Tiles[target].Elevation = (float)(w.Tiles[source].Elevation + 0.02);
            w.Tiles[target].Relief = Relief.Mountain;
            Check(!IrrigationEconomy.RouteValid(w, source, new[] { target }), "uphill climb onto Mountain relief is rejected");
            w.Tiles[target].Relief = Relief.Lowland; w.Tiles[source].Relief = Relief.Mountain;
            Check(!IrrigationEconomy.RouteValid(w, source, new[] { target }), "uphill climb out of Mountain gorge is rejected");
            w.Tiles[source].Relief = Relief.Lowland;
            // Cumulative multi-hop climb above source cannot stair-step past 25 deg.
            int hop2 = CultivationEconomy.Footprint(w, c).First(id => id != source && id != target && w.Map.Distance(target, id) == 1);
            w.Tiles[hop2].Relief = Relief.Lowland;
            w.Tiles[target].Elevation = (float)(w.Tiles[source].Elevation + 0.03);
            w.Tiles[hop2].Elevation = (float)(w.Tiles[source].Elevation + 0.055);
            Check(!IrrigationEconomy.RouteValid(w, source, new[] { target, hop2 }), "cumulative multi-hop climb above 25 deg is rejected");
            w.Tiles[hop2].Elevation = (float)(w.Tiles[source].Elevation + 0.04);
            Check(IrrigationEconomy.RouteValid(w, source, new[] { target, hop2 }), "two-hop gentle valley terrace within cumulative 25 deg is accepted");
            // ValleyMaxSlopeDegrees = 0 preserves strict downhill-only HydraulicTolerance behavior.
            w.Config.Agriculture.ValleyMaxSlopeDegrees = 0;
            Check(!IrrigationEconomy.RouteValid(w, source, new[] { target }), "ValleyMaxSlopeDegrees=0 preserves strict downhill-only routing");
        }
    }
    public static int Run()
    {
        checks = 0; Plough(); Discovery(); GatesAndCosts(); WaterAndSoil(); AdditionalContracts(); ElderGatesAndSharing(); CompleteTurns(); CommissioningMaterialChain(); MixedConstructionCommitment(); ReviewFixContracts();
        Console.WriteLine("PASS: " + checks + " agricultural technology assertions"); return checks;
    }
}
