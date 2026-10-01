using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Permanent planner regressions for the Stone Farm Tool field-work contract.
// These are deliberately separate from FarmToolChecks so pure planning failures
// cannot be hidden by resolver-side field-work coverage tests.
public static class FarmPlannerChecks
{
    private static int checks;

    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception("FarmPlanner: " + message);
    }

    private static WorldState Fixture(double capital, int farmers = 0, double years = 20)
    {
        var w = NeolithicChecks.Fixture();
        w.Config.TurnDays = years * w.Config.DaysPerYear;
        w.Config.GathererBridgeEfficiency = 0;
        var c = w.Camps[0];
        c.PlantFood = 1_000_000;
        c.AnimalFood = c.AquaticFood = 0;
        c.CraftingPriorities = new CraftingPriorities { HandTools = 0, Tools = 0, Pots = 0, Cloth = 0, FarmTools = 0, Housing = 0 };
        c.Workforce.Assignments.Clear();
        if (farmers > 0) c.Workforce.Assign(WorkforceRoles.Farmer, farmers, c.Population);
        foreach (int id in CultivationEconomy.Footprint(w, c))
        {
            var tile = w.Tiles[id];
            tile.CultivationCapital = capital;
            tile.Cultivation = (float)CultivationEconomy.Intensity(w.Config, capital);
            tile.Cultivated = capital > 0;
            tile.CultivationOperator = capital > 0 ? c.Id : -1;
        }
        return w;
    }

    private static void AddSources(WorldState w, double stone = 1000, double wood = 1000)
    {
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState
        {
            MaximumQuantity = 100000,
            CatalogHash = ResourceCatalog.Hash,
            ProfileHash = ResourceCatalog.Hash,
            ConfigurationHash = ResourceCatalog.Hash,
            Provinces = Array.Empty<GeologicalProvince>(),
            Province = new ushort[count],
            History = new ushort[count],
            Host = new byte[count],
            Marine = new byte[count],
            Stone = new ushort[count],
            Clay = new ushort[count],
            Wood = new ushort[count],
            WoodRecoveryPerYear = .05
        };
        int tile = w.Camps[0].Tile;
        w.Resources.Stone[tile] = (ushort)Math.Min(ushort.MaxValue, stone / w.Resources.SurfaceUnit);
        w.Resources.Wood[tile] = (ushort)Math.Min(ushort.MaxValue, wood / w.Resources.SurfaceUnit);
    }

    private static int Assigned(EconomicIntent plan, string role)
        => plan.Assignments.Where(a => a.Role == role).Sum(a => a.Population);

    private static void MaintenanceStockCases()
    {
        foreach (int existing in new[] { 0, 20 })
        foreach (double cycles in new[] { 0d, 1, 2 })
        {
            var w = Fixture(.1, existing);
            var c = w.Camps[0];
            c.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 0 };
            c.Inventory.FarmTools = cycles * 10 / 1.265625;
            c.Inventory.HandTools = 1000;
            c.Inventory.Wood = 1000;
            c.CraftingPriorities.FarmTools = 1;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
            string before = SaveCodec.Fingerprint(w);
            var plan = EconomicPlanner.Plan(w, c, EconomicIntent.From(c));
            Check(before == SaveCodec.Fingerprint(w), "maintenance/tool-stock forecast is pure existing=" + existing + " cycles=" + cycles);
            int farmers = Assigned(plan, WorkforceRoles.Farmer);
            int carpenters = Assigned(plan, WorkforceRoles.Carpenter);
            Check(farmers == (cycles == 0 ? 10 : 8), "maintenance staffing respects held Farm Tools existing=" + existing + " cycles=" + cycles);
            if (cycles < 2) Check(carpenters >= 1, "future Farm Tool replacement has a producer existing=" + existing + " cycles=" + cycles);
            else Check(carpenters == 0, "held Farm Tools bridge useful field work existing=" + existing + " cycles=" + cycles);
            Check(plan.Assignments.Sum(a => a.Population) <= c.Population, "maintenance plan overstaffs residents");
        }
    }

    private static void PlannedExpansionCases()
    {
        foreach (string scenario in new[] { "supplied", "noWood", "noTools", "stocked" })
        {
            var w = Fixture(0, 0);
            var c = w.Camps[0];
            c.PlantFood = 0;
            c.Inventory.HandTools = 1000;
            c.Inventory.Wood = 1000;
            c.CraftingPriorities.FarmTools = 1;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
            foreach (var tile in w.Tiles)
            {
                tile.Food = 1;
                for (int season = 0; season < 4; season++) tile.Seasons[season].MaxFood = 1;
            }
            if (scenario == "noWood") c.Inventory.Wood = 0;
            if (scenario == "noTools") c.Inventory.HandTools = 0;
            if (scenario == "stocked") c.Inventory.FarmTools = 1000;
            var plan = EconomicPlanner.Plan(w, c, EconomicIntent.From(c));
            Check(plan.Labor != null && plan.Labor.ExpansionFarmers > 0, scenario + " retains intended field expansion");
            int carpenters = Assigned(plan, WorkforceRoles.Carpenter);
            if (scenario == "supplied") Check(carpenters >= 1, "supplied expansion forecasts useful Farm Tool production");
            else Check(carpenters == 0, scenario + " does not staff an infeasible or redundant Farm Tool producer");
            Check(plan.Assignments.Sum(a => a.Population) <= c.Population, scenario + " expansion plan is resident-bounded");
        }
    }

    private static void BootstrapCases()
    {
        foreach (bool gather in new[] { true, false })
        {
            var w = Fixture(.1, 0);
            var c = w.Camps[0];
            c.Inventory.Stone = 10;
            c.Inventory.Wood = 2;
            c.Inventory.HandTools = c.Inventory.Tools = 0;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
            c.CraftingPriorities = new CraftingPriorities { HandTools = 1, Tools = 1, FarmTools = 1, Pots = 0, Cloth = 0, Housing = 0 };
            if (!gather) c.StonePriority = c.WoodPriority = 0;
            string before = SaveCodec.Fingerprint(w);
            var plan = EconomicPlanner.Plan(w, c, EconomicIntent.From(c));
            Check(Assigned(plan, WorkforceRoles.Toolmaker) > 0, "field-tool bootstrap retains manufacturing-tool work gather=" + gather);
            Check(before == SaveCodec.Fingerprint(w), "bootstrap forecast remains pure gather=" + gather);
            Check(plan.Assignments.Sum(a => a.Population) <= c.Population, "bootstrap is resident-bounded gather=" + gather);
        }
    }

    private static void SourcedFieldCase()
    {
        var w = Fixture(.1, 0);
        var c = w.Camps[0];
        c.Inventory.HandTools = 1000;
        c.Inventory.Wood = 0;
        c.CraftingPriorities.FarmTools = 1;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
        AddSources(w);
        var plan = EconomicPlanner.Plan(w, c, EconomicIntent.From(c));
        Check(Assigned(plan, WorkforceRoles.Woodcutter) > 0, "sourced field plan gathers required Wood");
        EconomicIntents.Apply(c, plan);
        var record = new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>());
        Check(record.Production.Any(p => p.Good == CraftGood.FarmTools && p.Output > 0), "sourced field plan realizes Farm Tool production");
    }

    // Pins the Uniform planner: FieldPriorityCase asserts the priority-ordered Wood split (Farm Tools 2 over Housing 1) that
    // the Uniform rule defines; the value-ordered split is covered by ProductionOrderChecks.
    private static WorldState UniformPriority(WorldState w) { w.Config.Neolithic.PlannerFulfilment = "Uniform"; return w; }

    private static void FieldPriorityCase()
    {
        // With no existing cultivation, there is no intended field expansion.
        // The small Wood reserve must therefore flow to useful Housing rather
        // than inventing a Farm Tool producer for a zero field-work forecast.
        var idle = UniformPriority(Fixture(0, 0));
        var idleCamp = idle.Camps[0];
        idleCamp.Inventory.HandTools = 1000;
        idleCamp.Inventory.Wood = 2;
        idleCamp.Inventory.Clay = 0;
        idleCamp.CraftingPriorities.FarmTools = 2;
        idleCamp.CraftingPriorities.Housing = 1;
        idleCamp.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
        idleCamp.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        var idlePlan = EconomicPlanner.Plan(idle, idleCamp, EconomicIntent.From(idleCamp));
        Check(Assigned(idlePlan, WorkforceRoles.Builder) > 0, "idle E0 reserve assigns scarce Wood to useful Housing");
        Check(Assigned(idlePlan, WorkforceRoles.Carpenter) == 0, "idle E0 reserve does not invent Farm Tool production");

        // Once all seven radius-one fields carry physical E=.1, useful Farm
        // Tool maintenance demand and Housing can share a 20-unit Wood stock.
        var established = UniformPriority(Fixture(.1, 0));
        var establishedCamp = established.Camps[0];
        Check(CultivationEconomy.Footprint(established, establishedCamp).Count(id => established.Tiles[id].CultivationCapital > 0) == 7, "established priority fixture has seven fields");
        establishedCamp.Inventory.HandTools = 1000;
        establishedCamp.Inventory.Wood = 20;
        establishedCamp.Inventory.Clay = 0;
        establishedCamp.CraftingPriorities.FarmTools = 2;
        establishedCamp.CraftingPriorities.Housing = 1;
        establishedCamp.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
        establishedCamp.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        var establishedPlan = EconomicPlanner.Plan(established, establishedCamp, EconomicIntent.From(establishedCamp));
        Check(Assigned(establishedPlan, WorkforceRoles.Carpenter) > 0, "established fields retain useful Farm Tool production");
        Check(Assigned(establishedPlan, WorkforceRoles.Builder) > 0, "established fields retain remaining Wood for Housing");

        // A large held stock bridges the current field cycle.  It must not be
        // treated as a single imaginary producer that consumes all residents
        // and prevents a small useful Housing job.
        var held = UniformPriority(Fixture(.1, 0));
        var heldCamp = held.Camps[0];
        heldCamp.Inventory.FarmTools = 100;
        heldCamp.Inventory.HandTools = 1000;
        heldCamp.Inventory.Wood = 2;
        heldCamp.Inventory.Clay = 0;
        heldCamp.CraftingPriorities.FarmTools = 2;
        heldCamp.CraftingPriorities.Housing = 1;
        heldCamp.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
        heldCamp.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        var heldPlan = EconomicPlanner.Plan(held, heldCamp, EconomicIntent.From(heldCamp));
        Check(Assigned(heldPlan, WorkforceRoles.Builder) > 0, "held Farm Tools leave scarce Wood for useful Housing");
        Check(Assigned(heldPlan, WorkforceRoles.Carpenter) == 0, "held Farm Tools do not staff redundant current production");
    }

    private static void PlanMatrix()
    {
        int cases = 0;
        foreach (int people in new[] { 1, 2, 7, 24, 100 })
        foreach (double capital in new[] { 0d, .1, .7 })
        foreach (double stock in new[] { 0d, 1, 100 })
        foreach (double material in new[] { 0d, 2, 1000 })
        foreach (bool other in new[] { false, true })
        foreach (bool scarce in new[] { false, true })
        {
            var w = Fixture(capital, 0);
            var c = w.Camps[0];
            c.Population = people;
            c.Inventory.FarmTools = stock;
            c.Inventory.HandTools = stock;
            c.Inventory.Wood = c.Inventory.Stone = c.Inventory.Clay = material;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
            c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
            c.CraftingPriorities.FarmTools = c.CraftingPriorities.HandTools = c.CraftingPriorities.Tools = 1;
            if (other) c.CraftingPriorities.Cloth = c.CraftingPriorities.Housing = c.CraftingPriorities.Pots = 1;
            if (scarce)
            {
                c.PlantFood = 0;
                foreach (var tile in w.Tiles)
                {
                    tile.Food = 1;
                    for (int s = 0; s < 4; s++) tile.Seasons[s].MaxFood = 1;
                }
            }
            var constraints = new EconomicConstraints { Agriculture = true, Population = people, Technologies = c.People.NeolithicTechnologies.ToArray(), FocusTiles = CultivationEconomy.Footprint(w, c) };
            string before = SaveCodec.Fingerprint(w);
            var plan = EconomicPlanner.Plan(w, c, EconomicIntent.From(c));
            string error = EconomicIntents.Validate(constraints, plan);
            Check(error == null, "plan matrix validation case=" + cases + " error=" + error);
            Check(before == SaveCodec.Fingerprint(w), "plan matrix forecast mutates state case=" + cases);
            cases++;
        }
        Check(cases == 540, "plan matrix covers the intended small workforce/material envelope");
    }

    private static void FieldLifecycle()
    {
        var w = Fixture(.1, 0);
        var c = w.Camps[0];
        c.Inventory.HandTools = c.Inventory.Tools = c.Inventory.Wood = c.Inventory.Stone = 0;
        c.CraftingPriorities.FarmTools = c.CraftingPriorities.HandTools = c.CraftingPriorities.Tools = 1;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        c.EconomyAutomationTurns = -1;
        AddSources(w);
        bool farm = false, manufacturing = false;
        for (int turn = 0; turn < 8; turn++)
        {
            var replay = SaveCodec.Load(SaveCodec.Save(w));
            var record = new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>());
            new TurnResolver().Resolve(replay, new Dictionary<int, List<Order>>());
            Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(replay), "field bootstrap lifecycle diverges after save turn=" + turn);
            farm |= record.Production.Any(p => p.Good == CraftGood.FarmTools && p.Output > 1e-8);
            manufacturing |= record.Production.Any(p => (p.Good == CraftGood.HandTools || p.Good == CraftGood.Tools) && p.Output > 1e-8);
            var d = record.Demographics.Single();
            Check(Math.Abs(d.InitialReserves + d.FoodReceived - d.FoodConsumed - d.ManufacturingFood - d.DiscardedInventory - d.FinalReserves) <= 1e-7, "field bootstrap food ledger turn=" + turn);
        }
        Check(farm && manufacturing, "sourced field lifecycle bootstraps manufacturing and Farm Tools within eight turns");
    }

    private static void ManagedLaborEnvelope()
    {
        var mixed = Fixture(.1, 0);
        var c = mixed.Camps[0]; c.Population = 24;
        c.Technology = Technology.Cultivation; c.Technologies[(int)Technology.Cultivation] = true;
        c.Technologies[(int)Technology.AnimalHusbandry] = true; c.People.Progression.Era = Era.Neolithic;
        c.CraftingPriorities = new CraftingPriorities { HandTools = 1, Tools = 1, Pots = 1, Cloth = 1, FarmTools = 1, Housing = 1 };
        c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking); c.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving); c.People.NeolithicTechnologies.Add(NeolithicTech.Carpentry); c.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        mixed.Herds.Add(new Herd { Id = mixed.NextId++, Tile = c.Tile, Species = "antelope", Animals = 50, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        var observation = new AgentObservationBuilder().Build(mixed, c.Player); var settlement = observation.Settlements.Single();
        mixed.Config.HerdLabourEnabled = false; // pins the legacy unconditional reservation; the labour-price staffing is covered by HerdLabourChecks
        var plan = EconomicPlanner.Plan(mixed, c, EconomicIntent.From(c));
        Check(Assigned(plan, WorkforceRoles.Herder) == 24, "managed-herd planner reserves the resident-bounded Herder envelope");
        Check(plan.Assignments.Sum(a => a.Population) <= c.Population, "mixed Agriculture/Husbandry proposal stays resident-bounded");
        Check(plan.Labor == null || plan.Labor.MaterialGatherers <= c.Population - plan.Assignments.Where(a => a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population) + 1e-8, "material labor is computed after Herder reservation");
        Check(EconomicIntents.Validate(observation, settlement, plan) == null, "mixed planner proposal validates with Herder labor reserved before crafts");

        var agricultureOnly = Fixture(.1, 0); var ac = agricultureOnly.Camps[0]; ac.Population = 24;
        var before = SaveCodec.Fingerprint(agricultureOnly); var noHerd = EconomicPlanner.Plan(agricultureOnly, ac, EconomicIntent.From(ac));
        Check(Assigned(noHerd, WorkforceRoles.Herder) == 0 && before == SaveCodec.Fingerprint(agricultureOnly), "Agriculture-only planner remains unchanged without managed herds");
    }

    public static int Run()
    {
        checks = 0;
        MaintenanceStockCases();
        PlannedExpansionCases();
        BootstrapCases();
        SourcedFieldCase();
        FieldPriorityCase();
        PlanMatrix();
        FieldLifecycle();
        ManagedLaborEnvelope();
        Console.WriteLine("PASS: " + checks + " Stone Farm Tool planner assertions");
        return checks;
    }
}
