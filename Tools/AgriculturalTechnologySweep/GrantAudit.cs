using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropocene.Simulation;

// Replay the recorded world without changing rules; inspect the eleven underfilled grants.
internal static class GrantAudit
{
    private static readonly JsonSerializerOptions J = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() } };
    internal static void Fixture()
    {
        foreach (var test in new[] { (4, false), (5, false), (5, true) })
        {
            int builders = test.Item1; bool storage = test.Item2;
            var cfg = SimulationConfig.Small(1); cfg.Width = 24; cfg.Height = 16;
            cfg.GoodsStorageEnabled = storage;
            cfg.Agriculture = new AgriculturalTechnologySettings { IrrigationEnabled = true };
            double years = 10.373449306446295; cfg.TurnDays = years * cfg.DaysPerYear;
            var w = new WorldState { Seed = 101, Config = cfg, Generation = new GenerationReport(), Tiles = new Tile[cfg.Width * cfg.Height] };
            for (int i = 0; i < w.Tiles.Length; i++)
            {
                var t = w.Tiles[i] = new Tile { Id = i, ElevationLevel = 1, Elevation = .1f, Terrain = Terrain.Grassland, FreshwaterSource = FreshwaterSource.RainPool };
                for (int s = 0; s < 4; s++) t.Seasons[s] = new SeasonData { Moisture = .1f, Temperature = 18, MaxFood = 80, MaxPotentialFood = 800, Habitat = .6f };
            }
            var c = new Camp { Id = w.NextId++, Player = 0, Tile = 200, Population = 100, PlantFood = 100000, IsVillage = true };
            w.Camps.Add(c); Ownership.Bind(w); c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true; c.People.Progression.Era = Era.Neolithic;
            c.People.NeolithicTechnologies.AddRange(new[] { NeolithicTech.Construction, NeolithicTech.Irrigation });
            c.Inventory.Tools = 1000; c.Inventory.Wood = c.Inventory.Clay = 10000; c.Inventory.HousingUnits = 10000;
            c.Workforce.Assign(WorkforceRoles.Builder, builders, c.Population);
            var recipes = NeolithicProduction.Recipes(cfg); var canal = recipes.Single(r => r.Good == CraftGood.CanalConstruction);
            var material = recipes.Single(r => r.Good == CraftGood.HousingMaterial);
            int source = w.Map.Neighbors(c.Tile).First(); w.Tiles[source].FreshwaterSource = FreshwaterSource.River; w.Tiles[source].Elevation = .2f;
            var order = new ProductionOrder(new[] { CraftGood.HousingMaterial, CraftGood.CanalConstruction },
                new Dictionary<CraftGood, double> { [CraftGood.HousingMaterial] = canal.HousingMaterial, [CraftGood.CanalConstruction] = 1 })
                { Irrigation = new IrrigationOrder { Construction = 1, Project = new IrrigationProject { Source = source, Target = c.Tile, Route = new[] { c.Tile }, Remaining = 1 } } };
            var forecast = MaterialEconomy.Forecast(w, c, recipes, cfg.GenerationIntervals, years, 10000, false, c.Population, false, false, order: order);
            GoodsStorage.PlannedUse(recipes, g => forecast[g], out double equipmentUse, out double housingUse);
            if (storage) c.GoodsStorage = GoodsStorage.Begin(w, c, years, c.Population, equipmentUse, housingUse);
            double sameMaterialForHousing = GoodsStorage.Begin(w, c, years, c.Population, equipmentUse, canal.HousingMaterial / cfg.Neolithic.HousingMaterialUnitsPerHousing)
                .Limit(c, CraftGood.HousingMaterial, canal.HousingMaterial);
            var record = new TurnRecord { Duration = cfg.TurnDays }; var coverage = new ToolCoverageBudget(w, c, cfg.GenerationIntervals);
            double equipment = 10000, budget = builders * cfg.GenerationIntervals;
            double used = NeolithicProduction.Produce(w, c, material, WorkforceRoles.Builder, budget, cfg.GenerationIntervals, canal.HousingMaterial, ref equipment, record, coverage, order: order);
            used += NeolithicProduction.Produce(w, c, canal, WorkforceRoles.Builder, budget-used, cfg.GenerationIntervals, 1, ref equipment, record, coverage, order: order);
            Console.WriteLine(JsonSerializer.Serialize(new { builders, storage, years, materialRequired = canal.HousingMaterial,
                sameMaterialForHousing,
                materialForecast = forecast[CraftGood.HousingMaterial], canalForecast = forecast[CraftGood.CanalConstruction],
                receipts = record.Production, unusedBuilderYears = (budget-used) * cfg.BaseDays/cfg.DaysPerYear }, J));
        }
    }
    internal static void Run()
    {
        string root = "TestResults/ConstructionGrantAudit"; Directory.CreateDirectory(root);
        var targets = new Dictionary<int, int[]> { [77] = new[] { 4058 }, [84] = new[] { 4605 }, [91] = new[] { 3546, 4681 },
            [93] = new[] { 3546 }, [94] = new[] { 4698 }, [95] = new[] { 2921 }, [96] = new[] { 2921, 3630 },
            [97] = new[] { 3630 }, [107] = new[] { 5671 } };
        var w = SaveCodec.Load(File.ReadAllBytes("TestResults/AgriculturalGameplay80/opening.anthro"));
        while (w.Turn <= 107)
        {
            int turn = w.Turn; targets.TryGetValue(turn, out var ids);
            ResolutionMetrics.CaptureDemandAllocations = ids != null;
            ResolutionMetrics.CaptureCraftWorkDiagnostics = ids != null;
            var ai = AiPlanning.Decide(w); var prepared = TurnPlanner.Prepare(w, ai.Orders, ai.Traces);
            if (ids != null) foreach (int id in ids)
            {
                var c = w.Camps.Single(c => c.Id == id); var a = prepared.DemandAllocations[id]; var n = prepared.Needs[id];
                var o = prepared.Orders.SingleOrDefault(o => o.Entity == id && o.Economy != null);
                File.WriteAllText(Path.Combine(root, $"{turn}-{id}-before.json"), JsonSerializer.Serialize(new {
                    turn, camp = id, days = w.Days, years = prepared.Horizon.Years, c.Population, c.Inventory,
                    c.PlantFood, c.AnimalFood, c.EconomicLabor, workforce = c.Workforce.Assignments,
                    assigned = o?.Economy, staff = a.Staff, ceilings = a.Ceilings,
                    a.PlannedStoneExtraction, a.PlannedClayExtraction, a.PlannedWoodExtraction,
                    a.MaterialGatherers, a.FinalCompiledPersons, a.ResidentGoodsBudget, a.ElderAction,
                    requests = a.Requests.Select(r => new { r.Service, r.Class, r.Good, r.Granted, r.Units, r.Unmet,
                        r.ReservedMaterial, roleYearsPerUnit = r.Bundle?.RoleYears, outputsPerUnit = r.Bundle?.Outputs }),
                    needs = n, production = n.Production, order = n.Order.Goods,
                    usefulMaterialDemand = NeolithicProduction.Demand(w, c, CraftGood.HousingMaterial, prepared.Horizon.Years, c.Population, false, false, order: n.Order)
                }, J));
            }
            var record = new TurnResolver().Resolve(w, prepared);
            if (ids != null) foreach (int id in ids)
            {
                File.WriteAllText(Path.Combine(root, $"{turn}-{id}-after.json"), JsonSerializer.Serialize(new {
                    turn, camp = id, production = record.Production.Where(p => p.Camp == id),
                    limits = record.ProductionLimits.Where(p => p.Camp == id),
                    materials = record.MaterialTransfers.Where(p => p.Camp == id),
                    events = record.Events.Where(e => e.Contains(id.ToString()))
                }, J));
                Console.WriteLine($"AUDIT {turn}/{id} grant={prepared.DemandAllocations[id].Requests.Where(r=>r.Good==CraftGood.CanalConstruction).Sum(r=>r.Granted):R} forecast={prepared.Needs[id].Production[CraftGood.CanalConstruction]:R} actual={record.Production.Where(p=>p.Camp==id && p.Good==CraftGood.CanalConstruction).Sum(p=>p.Output):R}");
            }
            Console.WriteLine($"TURN {turn} population={w.Camps.Sum(c=>c.Population)+w.Hunters.Sum(h=>h.Population)} year={w.Days/w.Config.DaysPerYear:R}");
        }
        ResolutionMetrics.CaptureDemandAllocations = false; ResolutionMetrics.CaptureCraftWorkDiagnostics = false;
    }
}
