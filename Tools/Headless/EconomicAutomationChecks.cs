using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class EconomicAutomationChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Automation: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-7) => Check(Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);
    private static WorldState Fixture(bool sources = true)
    {
        var w = NeolithicChecks.Fixture();
        if (sources)
        {
            int count = w.Tiles.Length;
            w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05 };
            int tile = w.Camps[0].Tile; w.Resources.Stone[tile] = 1000; w.Resources.Clay[tile] = 1000; w.Resources.Wood[tile] = 1000;
        }
        return w;
    }
    private static TurnRecord Resolve(WorldState w) => new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>());
    private static void Field(WorldState w, double e = .1)
    {
        var c = w.Camps[0]; var t = w.Tiles[c.Tile]; t.CultivationCapital = e; t.Cultivation = (float)CultivationEconomy.Intensity(w.Config, e); t.CultivationOperator = c.Id;
    }
    private static void Budget(WorldState w, EconomicIntent e)
    {
        var c = w.Camps[0]; var observation = new AgentObservationBuilder().Build(w, c.Player);
        Check(OrderRules.Validate(observation, new Order(OrderKind.Economy, c.Id) { Economy = e }) == null, "automatic intent is legal");
        Check(e.Assignments.Sum(a => a.Population) <= c.Population, "one resident ledger");
    }
    public static int Run()
    {
        checks = 0; RolesAndRates(); Labor(); Planning(); Persistence(); FabricDoesNotFundBirths(); FabricUsesPostGrowthResidents(); PlanningResidentPopulationIgnoresStaleIncome();
        Console.WriteLine("PASS: " + checks + " demand automation assertions"); return checks;
    }
    private static void RolesAndRates()
    {
        Check(EconomicIntents.RequiredTechnology(WorkforceRoles.ClayMiner) == NeolithicTech.Pottery, "Pottery unlocks Clay Miner");
        Check(WorkforceRoles.Label(WorkforceRoles.Stonecutter) == "Stone Quarrier" && WorkforceRoles.Stonecutter == "Stonecutter", "display rename retains saved role identifier");
        foreach (bool specialist in new[] { false, true })
        {
            var w = Fixture(); var c = w.Camps[0]; var r = new TurnRecord();
            string extraction = specialist ? WorkforceRoles.ClayMiner : WorkforceRoles.Gatherer;
            string manufacture = specialist ? WorkforceRoles.Potter : WorkforceRoles.Craftsperson;
            if (specialist) c.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
            c.Inventory.HandTools = 1;
            var source = MaterialEconomy.KnownSources(w, c, 0).First(s => s.Material == MaterialId.Clay);
            Near(MaterialEconomy.Extract(w, c, source, extraction, 80, 1000, 0, r), 80, "one extractor's actual time");
            double material = c.Inventory.Clay, equipment = 80;
            Near(material, specialist ? 80 : 20, "matching specialist/generalist extraction throughput");
            NeolithicProduction.Produce(w, c, NeolithicProduction.Recipes(w.Config).First(p => p.Good == CraftGood.Pots), manufacture, 80, 80, 1000, ref equipment, r);
            Near(c.Inventory.Pots, material, "one extractor supplies one matching manufacturer's single-input throughput");
            Near(c.Inventory.Clay, 0, "every produced pot pays clay input");
            Check(r.MaterialTransfers.All(t => Math.Abs(t.Before - t.After - t.Amount) < 1e-7), "source receipts reconcile");
        }
        var missing = Fixture(); var mc = missing.Camps[0]; var mr = new TurnRecord();
        var clay = MaterialEconomy.KnownSources(missing, mc, 0).First(s => s.Material == MaterialId.Clay);
        Near(MaterialEconomy.Extract(missing, mc, clay, WorkforceRoles.ClayMiner, 80, 80, 0, mr), 0, "locked extractor cannot operate");
        mc.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
        var stone = MaterialEconomy.KnownSources(missing, mc, 0).First(s => s.Material == MaterialId.Stone);
        Near(MaterialEconomy.Extract(missing, mc, stone, WorkforceRoles.ClayMiner, 80, 80, 0, mr), 0, "Clay Miner cannot quarry Stone");
    }
    private static void Labor()
    {
        var w = Fixture(); var c = w.Camps[0]; Field(w);
        c.Workforce.Assign(WorkforceRoles.Farmer, 60, 100);
        c.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 20, MaterialGatherers = 10 };
        var labor = CultivationEconomy.Prepare(w, c, 0);
        Near(labor.OperatingFarmers, 40, "harvesting farmers are not capped at maintenance minimum");
        Near(labor.ExpansionFarmers, 20, "expansion time separated"); Near(labor.FoodGatherers, 30, "material gathering time protected");
        Near(w.Tiles[c.Tile].CultivationOperatingFraction, 1, "extra harvesters do not multiply ecological operation");
        var record = Resolve(w);
        double gather = record.Transfers.Where(t => t.Kind.EndsWith("gathering") || t.Kind == "Gathering").Sum(t => t.Food) / w.Config.GatherPerPerson;
        double raw = record.MaterialTransfers.Where(t => t.Role == WorkforceRoles.Gatherer).Sum(t => t.WorkerIntervals);
        Check(gather <= 30 * 80 + 1e-6 && gather + raw <= 40 * 80 + 1e-6, "food and materials never duplicate gatherer time");
        Check(record.Transfers.Any(t => t.Kind == "Cultivation" && t.Tile != c.Tile && t.Food > 0), "farm operators harvest shared accessible footprint beyond their field");
        var zero = Fixture(); var zc = zero.Camps[0]; zc.Workforce.Assign(WorkforceRoles.Farmer, 40, 100); zc.EconomicLabor = new EconomicLaborPlan();
        Near(CultivationEconomy.Prepare(zero, zc, 0).OperatingFarmers, 0, "q zero remains expansion-only");
        Near(CultivationEconomy.Prepare(zero, zc, 0).ExpansionFarmers, 0, "explicit zero expansion is respected at q zero");
        zero.Config.MaintenanceCoefficient = 0; Field(zero); Near(CultivationEconomy.Prepare(zero, zc, 0).OperatingFarmers, 40, "zero upkeep does not erase explicit harvest labor");
        var paleo = NeolithicChecks.Fixture(false); var pe = EconomicPlanner.Plan(paleo, paleo.Camps[0], new EconomicIntent { AutomationTurns = -1 });
        Check(pe.Assignments.Count == 0 && pe.AutomationTurns == 0, "no Agriculture no farming automation");
    }
    private static void Planning()
    {
        var w = Fixture(); var c = w.Camps[0]; Field(w);
        c.Inventory.HandTools = 100;
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        c.PotsTarget = 80; c.FarmToolsTarget = 80;
        string before = SaveCodec.Fingerprint(w);
        var e = EconomicIntents.Automatic(w, c, true); Budget(w, e);
        Check(before == SaveCodec.Fingerprint(w), "planner and observation do not mutate world or stocks");
        Check(e.Assignments.Any(a => a.Role == WorkforceRoles.Weaver) && e.Assignments.Any(a => a.Role == WorkforceRoles.Builder), "unlocked manufacturers adopted for useful needs");
        Check(e.Assignments.Any(a => a.Role == WorkforceRoles.ClayMiner), "pottery demand recruits Clay Miners");
        Check(!e.Assignments.Any(a => a.Role == WorkforceRoles.Craftsperson), "matching specialists replace generalist craft quotas");
        Check(e.Assignments.Any(a => a.Role == WorkforceRoles.Farmer), "established farmers chosen before supplementary gathering");
        c.CultivationFocusTile = 0; c.EconomyAutomationTurns = 1;
        var moved = EconomicIntents.Automatic(w, c, true); Budget(w, moved); Check(moved.Focus == -1, "stale field focus clears after relocation");
        Resolve(w); Check(c.EconomyAutomationTurns == 0 && c.CultivationFocusTile == -1, "invalid old focus cannot crash continuing automation");
        var locked = Fixture(); var lc = locked.Camps[0]; lc.Inventory.HandTools = 100;
        var le = EconomicIntents.Automatic(locked, lc, true); Budget(locked, le);
        Check(le.Assignments.All(a => EconomicIntents.RequiredTechnology(a.Role) == null), "no locked specialists in proposals");
        Check(le.Labor.MaterialGatherers > 0, "generalist extraction is budgeted explicitly");
        var noSource = Fixture(false); var nc = noSource.Camps[0]; nc.Inventory.HandTools = 100; nc.CraftingPriorities.Cloth = 0;
        var ne = EconomicIntents.Automatic(noSource, nc, true); Budget(noSource, ne);
        Check(!ne.Assignments.Any(a => a.Role == WorkforceRoles.Craftsperson || WorkforceRoles.Specialists.Contains(a.Role) || WorkforceRoles.Extractors.Contains(a.Role)), "missing materials do not recruit idle manufacturers or extractors");
        var stocked = Fixture(); var sc = stocked.Camps[0]; sc.Inventory.HandTools = 100; sc.Inventory.Cloth = 10000; sc.Inventory.HousingUnits = 1000;
        var se = EconomicIntents.Automatic(stocked, sc, true); Budget(stocked, se);
        Check(!se.Assignments.Any(a => a.Role == WorkforceRoles.Craftsperson || WorkforceRoles.Specialists.Contains(a.Role)), "finished stock bridging removes redundant manufacture");
        sc.AnimalFood = 1000000; Near(TurnResolver.PlanningFoodDemand(stocked, sc), 0, "imported/stored food shares the useful demand budget");
        var scarce = Fixture(); var fc = scarce.Camps[0]; Field(scarce); fc.PlantFood = 0; fc.Inventory.HandTools = 100; fc.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        foreach (var t in scarce.Tiles) { t.Food = 0; for (int season = 0; season < t.Seasons.Length; season++) { t.Seasons[season].MaxFood = 40; t.Seasons[season].FoodRecoveryPerDay = .002f; t.Seasons[season].BiomassRecoveryPerDay = 0; } }
        var shortage = EconomicIntents.Automatic(scarce, fc, true); Budget(scarce, shortage);
        Check(!shortage.Assignments.Any(a => a.Role == WorkforceRoles.Weaver), "food needed for meals cannot also justify idle Weavers");
        foreach (int population in new[] { 1, 2, 7, 24, 250, 1000 })
        { var tiny = Fixture(); tiny.Camps[0].Population = population; Budget(tiny, EconomicIntents.Automatic(tiny, tiny.Camps[0], true)); }
    }
    private static void Persistence()
    {
        var w = Fixture(); var c = w.Camps[0]; c.EconomyAutomationTurns = 2;
        var first = Resolve(w); Check(c.EconomyAutomationTurns == 1 && first.Orders.Any(s => s.Contains(":automation:")), "countdown runs on empty submission");
        Check(first.Production.Any(p => p.Good == CraftGood.HandTools && p.Output > 0), "zero-tool economy bootstraps through sourced useful production");
        var copy = SaveCodec.Load(SaveCodec.Save(w)); Check(SaveCodec.Fingerprint(copy) == SaveCodec.Fingerprint(w), "active automation and labor reservations save exactly");
        Resolve(w); Resolve(copy); Check(c.EconomyAutomationTurns == 0 && SaveCodec.Fingerprint(copy) == SaveCodec.Fingerprint(w), "countdown/save continuation deterministic");
        string assignments = string.Join(",", c.Workforce.Assignments.Select(a => a.Role + a.Population));
        var third = Resolve(w); Check(!third.Orders.Any(s => s.Contains(":automation:")) && string.Join(",", c.Workforce.Assignments.Select(a => a.Role + a.Population)) == assignments, "expired automation retains manual assignments");
        c.EconomyAutomationTurns = -1; Resolve(w); Check(c.EconomyAutomationTurns == -1, "continuous automation does not expire");
        var off = EconomicIntent.From(c); off.AutomationTurns = 0;
        new TurnResolver().Resolve(w, new Dictionary<int, List<Order>> { [c.Player] = new List<Order> { new Order(OrderKind.Economy, c.Id) { Economy = off } } });
        Check(c.EconomyAutomationTurns == 0 && !w.History.Last().Orders.Any(s => s.Contains(":automation:")), "explicit off takes effect before current turn automation");
        var bad = EconomicIntent.From(c); bad.AutomationTurns = -2;
        Check(new TurnResolver().Validate(w, c.Player, new Order(OrderKind.Economy, c.Id) { Economy = bad }) != null, "invalid countdown rejected");
        bad.AutomationTurns = 0; bad.Labor = new EconomicLaborPlan { MaterialGatherers = double.NaN };
        Check(new TurnResolver().Validate(w, c.Player, new Order(OrderKind.Economy, c.Id) { Economy = bad }) != null, "nonfinite labor reservation rejected");
        bad.Labor.MaterialGatherers = c.Population + 1;
        Check(new TurnResolver().Validate(w, c.Player, new Order(OrderKind.Economy, c.Id) { Economy = bad }) != null, "phantom material workforce rejected");
        c.EconomicLabor = new EconomicLaborPlan(); c.EconomyAutomationTurns = 0;
        var zero = SaveCodec.Load(SaveCodec.Save(w)).Camps[0]; Check(zero.EconomicLabor != null && zero.EconomicLabor.Values().All(v => v == 0) && zero.EconomyAutomationTurns == 0, "explicit zero plan and off survive saving");
    }
    private static void FabricDoesNotFundBirths()
    {
        var w = Fixture(); var c = w.Camps[0]; Field(w); w.Config.AnnualHumanGrowthRate = .01145;
        c.PlantFood = 8000; c.Inventory.HandTools = 2; c.Inventory.Cloth = 100; c.Inventory.HousingUnits = 100;
        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        c.Workforce.Assign(WorkforceRoles.Farmer, 40, 100); c.Workforce.Assign(WorkforceRoles.Weaver, 2, 100);
        c.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 2.5, MaterialGatherers = 58 };
        var r = Resolve(w);
        Near(r.Transfers.Where(t => t.Kind == "Cultivation").Sum(t => t.Food), 8100, "controlled harvest funds consumption plus Fabric only", 1e-5);
        Check(r.Growth == 0 && c.Population == 100, "manufacturing feedstock is not reused to fund births");
        Near(r.Demographics.Single().ManufacturingFood, 100, "planned plant input actually consumed");
        Near(c.Food, 8000, "full operating reserve retained after manufacturing", 1e-5);
    }
    private static void FabricUsesPostGrowthResidents()
    {
        WorldState GrowingFixture(bool hunter)
        {
            var w = Fixture(); var c = w.Camps[0]; Field(w); w.Config.MaintenanceCoefficient = 0; w.Config.AnnualHumanGrowthRate = .01145;
            c.Population = 10; c.PlantFood = c.AnimalFood = c.AquaticFood = 0; c.Inventory.HandTools = 1; c.Inventory.HousingUnits = 10;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
            c.Workforce.Assign(WorkforceRoles.Farmer, 9, c.Population); c.Workforce.Assign(WorkforceRoles.Weaver, 1, c.Population);
            c.EconomicLabor = new EconomicLaborPlan();
            if (hunter)
            {
                // Give the linked group a stock buffer so this case isolates
                // reproductive population from Clothing's resident population.
                c.AnimalFood = 4000;
                w.Hunters.Add(new Hunter { Id = w.NextId++, Player = c.Player, SourceCamp = c.Id, SocietyId = c.SocietyId, Tile = c.Tile, Population = 4 });
            }
            return w;
        }

        var w = GrowingFixture(false); var c = w.Camps[0]; var record = Resolve(w);
        Check(record.Growth > 0 && c.Population > 10, "zero-reserve fixture reaches positive post-growth resident population");
        Near(record.Demographics.Single().ManufacturingFood, 2 * c.Population, "zero reserve accepts exactly useful Fabric feedstock for post-growth residents");
        Near(c.FabricConsumed + c.Inventory.Cloth, 2 * c.Population, "post-growth Fabric production splits into current use and retained reserve");
        Check(c.PlantFood >= 0 && c.Food >= c.PlantFood, "plant stock remains part of the conserved food reserve after Fabric consumption");

        var withHunter = GrowingFixture(true); var camp = withHunter.Camps[0]; var hunterRecord = Resolve(withHunter);
        Check(hunterRecord.Growth > 0 && camp.Population > 10, "connected-hunter fixture grows at the camp");
        Near(hunterRecord.Demographics.Single().ManufacturingFood, 2 * camp.Population, "connected hunters affect biological growth but do not add Clothing demand");
        Check(withHunter.Hunters.Single().Population == 4, "connected hunters remain residents only for biological demand and birth placement");
    }
    private static void PlanningResidentPopulationIgnoresStaleIncome()
    {
        WorldState Make(bool stale, double remainder)
        {
            var w = Fixture(); var c = w.Camps[0];
            w.Config.AnnualHumanGrowthRate = .01145;
            c.PlantFood = 8000; c.GrowthRemainder = remainder; c.Inventory.HandTools = 10;
            c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
            c.Workforce.Assign(WorkforceRoles.Weaver, 10, c.Population);
            c.StepFoodIncome = stale ? 10000 : 0;
            return w;
        }
        foreach (double remainder in new[] { 0d, .37 })
        {
            var clean = Make(false, remainder); var stale = Make(true, remainder);
            double cleanPopulation = TurnResolver.PlanningResidentPopulation(clean, clean.Camps[0], 10000);
            double stalePopulation = TurnResolver.PlanningResidentPopulation(stale, stale.Camps[0], 10000);
            Check(cleanPopulation > clean.Camps[0].Population, "resident planning preserves positive growth with a stock buffer");
            Near(stalePopulation, cleanPopulation, "turn-start resident planning ignores stale prior-turn income");
        }
    }
}
