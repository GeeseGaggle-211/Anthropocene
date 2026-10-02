using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

internal static class DemographicStructureChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Demographic structure: " + message); }
    private static void Near(double actual, double expected, string message, double tolerance = 1e-9)
        => Check(Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Abs(expected)), message + " (" + actual + " vs " + expected + ")");

    public static int Run()
    {
        checks = 0;
        PortableHousingParameters();
        SpecialistScale();
        LegacyMigration();
        Console.WriteLine("PASS: " + checks + " demographic-structure assertions");
        return checks;
    }

    private static WorldState Husbandry(int population)
    {
        var w = NeolithicChecks.Fixture(false); var c = w.Camps[0];
        c.Population = population; c.Technology = Technology.AnimalHusbandry;
        c.Technologies[(int)Technology.AnimalHusbandry] = true; c.People.Progression.Era = Era.Neolithic;
        c.Inventory.HandTools = 100000; c.AnimalFood = 100000;
        return w;
    }

    private static void PortableHousingParameters()
    {
        var w = Husbandry(100); var c = w.Camps[0]; var n = w.Config.Neolithic;
        n.SpecialistScaleMaxBonus = 0;
        n.HousingDecayPerTurn = .10; n.LightHousingDecayPerReferenceTurn = .40;
        c.Inventory.LightHousingUnits = 10;
        NeolithicProduction.AddFixedHousing(w, c, 10, w.Days);
        NeolithicProduction.DepreciateHousing(w, c, n.ReferenceTurnYears);
        Near(NeolithicProduction.FixedHousingAt(w, c, w.Days + n.ReferenceTurnYears * w.Config.DaysPerYear), 9,
            "fixed Housing keeps its independent ten-percent decay");
        Near(c.Inventory.LightHousingUnits, 6, "Light Housing uses its independent forty-percent decay");

        n.AnimalFabricPerLightHousing = 1.5; n.LightHousingBuilderOutputPerBaseDays = n.BuilderHousingOutputPerBaseDays * .5;
        var recipe = NeolithicProduction.Recipes(w.Config).Single(r => r.Good == CraftGood.LightHousing);
        Near(recipe.AnimalFabric, 1.5, "the portable recipe exposes its Animal Fabric input");
        Near(recipe.Rate, n.BuilderHousingOutputPerBaseDays * .5, "the portable recipe exposes its construction rate");
        c.Inventory.AnimalFabric = 3; c.Inventory.LightHousingUnits = 0;
        double equipment = 100000; var record = new TurnRecord();
        NeolithicProduction.Produce(w, c, recipe, WorkforceRoles.Craftsperson, 10000, w.Config.GenerationIntervals, 10,
            ref equipment, record, new ToolCoverageBudget(w, c, w.Config.GenerationIntervals));
        var receipt = record.Production.Single();
        Near(receipt.Output, 2, "a Craftsperson can still construct portable Housing at the configured rate and input ratio");
        Near(receipt.AnimalFabric, 3, "portable Housing debits the configured Animal Fabric quantity");
    }

    private static void SpecialistScale()
    {
        var w = Husbandry(500); var c = w.Camps[0]; var n = w.Config.Neolithic;
        Near(new NeolithicConfig().SpecialistScaleMaxBonus, 0,
            "the rejected nonzero calibration remains disabled for new worlds");
        n.SpecialistScaleMaxBonus = .5; n.SpecialistScaleOnsetPopulation = 100; n.SpecialistScalePopulation = 400;
        Near(SpecialistProductivity.Multiplier(n, 24), 1, "a camp of a few dozen receives no specialist scale bonus");
        Near(SpecialistProductivity.Multiplier(n, 100), 1, "the scale multiplier is one at onset");
        Check(SpecialistProductivity.Multiplier(n, 300) > 1.15 && SpecialistProductivity.Multiplier(n, 1000) > 1.4 &&
            SpecialistProductivity.Multiplier(n, 1000) > SpecialistProductivity.Multiplier(n, 300),
            "a few hundred residents begin to benefit and a concentrated settlement gains a meaningful monotonic advantage");
        Check(SpecialistProductivity.Multiplier(n, 100000) < 1.500000001,
            "specialist scale saturates at the configured bound");

        c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        var fabric = NeolithicProduction.Recipes(w.Config).Single(r => r.Good == CraftGood.AnimalFabric);
        double specialist = SpecialistProductivity.CraftRate(w, c, fabric, WorkforceRoles.Weaver);
        double generalist = SpecialistProductivity.CraftRate(w, c, fabric, WorkforceRoles.Craftsperson);
        Near(specialist / fabric.Rate, SpecialistProductivity.Multiplier(n, c.Population), "a Weaver on the matching recipe receives the shared multiplier");
        Near(generalist / fabric.Rate, n.GeneralistTaskEfficiency, "Craftsperson productivity is unchanged by settlement size");
        Check(SpecialistProductivity.CraftRate(w, c, fabric, WorkforceRoles.Builder) == 0,
            "a specialist receives no bonus on another specialist's recipe");

        c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        double stoneSpecialist = SpecialistProductivity.ExtractionRate(w, c, MaterialId.Stone, WorkforceRoles.Stonecutter);
        double gatherer = SpecialistProductivity.ExtractionRate(w, c, MaterialId.Stone, WorkforceRoles.Gatherer);
        Near(stoneSpecialist / n.SpecialistMaterialExtractionRate, SpecialistProductivity.Multiplier(n, c.Population),
            "material specialists use the same concentration multiplier");
        Near(gatherer / n.SpecialistMaterialExtractionRate, n.GeneralistTaskEfficiency,
            "Gatherer material extraction remains the generalist fallback");

        c.Workforce.Assign(WorkforceRoles.Weaver, 1, c.Population);
        c.CraftingPriorities = new CraftingPriorities { AnimalFabric = 1 };
        double intervals = w.Config.GenerationIntervals;
        var forecast = MaterialEconomy.Forecast(w, c, NeolithicProduction.Recipes(w.Config), intervals,
            n.ReferenceTurnYears, 100000, false, c.Population, false, false);
        double available = 100000; var resolved = new TurnRecord();
        NeolithicProduction.Produce(w, c, fabric, WorkforceRoles.Weaver, intervals, intervals, 100000,
            ref available, resolved, new ToolCoverageBudget(w, c, intervals));
        Near(resolved.Production.Single(r => r.Good == CraftGood.AnimalFabric).Output, forecast[CraftGood.AnimalFabric],
            "specialist crafting forecast and resolution use the same scaled rate");
    }

    private static void LegacyMigration()
    {
        var serializer = new DataContractSerializer(typeof(NeolithicConfig));
        var old = new NeolithicConfig { HousingDecayPerTurn = .23, BuilderHousingOutputPerBaseDays = .047 };
        string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, old); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        xml = Regex.Replace(xml, "<(LightHousingBuilderOutputPerBaseDays|AnimalFabricPerLightHousing|LightHousingDecayPerReferenceTurn|SpecialistScaleMaxBonus|SpecialistScaleOnsetPopulation|SpecialistScalePopulation)>[^<]*</\\1>", "");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var migrated = (NeolithicConfig)serializer.ReadObject(input);
        Near(migrated.LightHousingDecayPerReferenceTurn, .23, "an old save preserves the historical shared portable-housing decay");
        Near(migrated.LightHousingBuilderOutputPerBaseDays, .047, "an old save preserves the historical shared portable-housing rate");
        Near(migrated.AnimalFabricPerLightHousing, 1, "an old save preserves the historical one-for-one portable-housing input");
        Near(migrated.SpecialistScaleMaxBonus, 0, "an old save preserves fixed specialist productivity");
        migrated.Validate(); checks++;
    }
}
