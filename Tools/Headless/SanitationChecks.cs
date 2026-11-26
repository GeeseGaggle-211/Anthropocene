using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using Anthropocene.Simulation;

internal static class SanitationChecks
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("Sanitation: " + message); }
    private static void Near(double value, double expected, string message, double tolerance = 1e-8)
        => Check(Math.Abs(value - expected) <= tolerance * Math.Max(1, Math.Abs(expected)), message + $" ({value} != {expected})");

    private static WorldState VillageFixture(int population = 1000, bool irrigationTech = true, bool sanitationEnabled = true)
    {
        var w = AgriculturalTechnologyChecks.Fixture();
        w.Config.DemographicModel = DemographicModel.PhysicalMortalityV1;
        w.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        w.Config.ElderKernelSplit = true;
        w.Config.SurplusFundedBirths = true;
        w.Config.Agriculture.SanitationEnabled = sanitationEnabled;
        var c = w.Camps[0];
        c.Population = population;
        c.IsVillage = true;
        if (irrigationTech && !c.People.HasTech(NeolithicTech.Irrigation))
            c.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
        if (!irrigationTech)
            c.People.NeolithicTechnologies.Remove(NeolithicTech.Irrigation);
        if (!c.People.HasTech(NeolithicTech.Toolmaking)) c.People.NeolithicTechnologies.Add(NeolithicTech.Toolmaking);
        if (!c.People.HasTech(NeolithicTech.Weaving)) c.People.NeolithicTechnologies.Add(NeolithicTech.Weaving);
        if (!c.People.HasTech(NeolithicTech.Pottery)) c.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
        // Supply ample baseline housing, cloth, tools, wood, and food so tests can isolate Sanitation cleanly.
        NeolithicProduction.AddFixedHousing(w, c, population / w.Config.Neolithic.HousingPeoplePerUnit, w.Days);
        c.Inventory.Cloth = population * 2;
        c.Inventory.Tools = 2000;
        c.Inventory.HandTools = 2000;
        c.Inventory.Wood = 5000;
        c.Inventory.Clay = 5000;
        c.Inventory.Stone = 5000;
        c.Inventory.HousingMaterial = 0;
        c.Inventory.Pots = 500;
        c.PlantFood = population * PhysicalDemography.FoodPerPersonYear(w.Config) * 40;
        return w;
    }

    private static void ConfigAndEnum()
    {
        Check((int)CraftGood.Sanitation == 13, "CraftGood.Sanitation ordinal must be 13");
        Check((int)CraftGood.CanalMaintenance == 12, "existing CraftGood ordinals preserved");
        var ag = new AgriculturalTechnologySettings();
        Check(!ag.SanitationEnabled, "SanitationEnabled defaults false on new settings");
        Near(ag.SanitationDiseaseReduction, .50, "default SanitationDiseaseReduction");
        Near(ag.SanitationPeoplePerUnit, 20, "default SanitationPeoplePerUnit");
        Near(ag.SanitationHousingMaterialsPerUnit, 1.0, "default SanitationHousingMaterialsPerUnit");
        Near(ag.SanitationBuilderOutputPerBaseDays, .03125, "default SanitationBuilderOutputPerBaseDays");
        Near(ag.SanitationDecayPerReferenceTurn, .10, "default SanitationDecayPerReferenceTurn");
        Near(ag.SanitationWaterPerPersonYear, .15, "default SanitationWaterPerPersonYear");
        ag.Validate();

        void Reject(Action<AgriculturalTechnologySettings> mutate, string label)
        {
            var bad = new AgriculturalTechnologySettings();
            mutate(bad);
            bool threw = false;
            try { bad.Validate(); } catch (ArgumentException) { threw = true; }
            Check(threw, "Validate rejects " + label);
        }
        Reject(s => s.SanitationDiseaseReduction = 1.05, "SanitationDiseaseReduction > 1");
        Reject(s => s.SanitationDiseaseReduction = -0.1, "SanitationDiseaseReduction < 0");
        Reject(s => s.SanitationPeoplePerUnit = 0, "SanitationPeoplePerUnit <= 0");
        Reject(s => s.SanitationBuilderOutputPerBaseDays = 0, "SanitationBuilderOutputPerBaseDays <= 0");
        Reject(s => s.SanitationDecayPerReferenceTurn = 1.0, "SanitationDecayPerReferenceTurn >= 1");
        Reject(s => s.SanitationWaterPerPersonYear = -0.01, "SanitationWaterPerPersonYear < 0");
        Reject(s => s.SanitationHousingMaterialsPerUnit = -1, "SanitationHousingMaterialsPerUnit < 0");

        var jsonOpts = new JsonSerializerOptions { IncludeFields = true };
        string repoRoot = FindRepoRoot();
        var agCfg = JsonSerializer.Deserialize<SimulationConfig>(
            File.ReadAllText(Path.Combine(repoRoot, "Assets/_Anthropocene/Resources/AgriculturalTechnologyOpeningConfig.json")), jsonOpts);
        Check(agCfg.Agriculture != null && agCfg.Agriculture.SanitationEnabled, "AgriculturalTechnologyOpeningConfig enables Sanitation");
        Near(agCfg.Agriculture.SanitationDiseaseReduction, .50, "JSON SanitationDiseaseReduction");
        Near(agCfg.Agriculture.SanitationPeoplePerUnit, 20, "JSON SanitationPeoplePerUnit");
        Near(agCfg.Agriculture.SanitationHousingMaterialsPerUnit, 1.0, "JSON SanitationHousingMaterialsPerUnit");
        Near(agCfg.Agriculture.SanitationBuilderOutputPerBaseDays, .03125, "JSON SanitationBuilderOutputPerBaseDays");
        Near(agCfg.Agriculture.SanitationDecayPerReferenceTurn, .10, "JSON SanitationDecayPerReferenceTurn");
        Near(agCfg.Agriculture.SanitationWaterPerPersonYear, .15, "JSON SanitationWaterPerPersonYear");
        agCfg.Validate();

        var baseCfg = JsonSerializer.Deserialize<SimulationConfig>(
            File.ReadAllText(Path.Combine(repoRoot, "Assets/_Anthropocene/Resources/OpeningConfig.json")), jsonOpts);
        Check(baseCfg.Agriculture == null, "OpeningConfig.json remains untouched (Agriculture == null)");

        var w = VillageFixture(500, irrigationTech: true, sanitationEnabled: true);
        var c = w.Camps[0];
        Check(AgriculturalTechnology.Sanitation(w, c), "village with Irrigation + SanitationEnabled unlocks Sanitation");
        c.IsVillage = false;
        Check(!AgriculturalTechnology.Sanitation(w, c), "mobile camp cannot unlock Sanitation");
        c.IsVillage = true;
        c.People.NeolithicTechnologies.Remove(NeolithicTech.Irrigation);
        Check(!AgriculturalTechnology.Sanitation(w, c), "village without Irrigation cannot unlock Sanitation");
        c.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
        w.Config.Agriculture.SanitationEnabled = false;
        Check(!AgriculturalTechnology.Sanitation(w, c), "disabled SanitationEnabled locks Sanitation");
    }

    private static string FindRepoRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6; i++)
        {
            if (File.Exists(Path.Combine(dir, "Assets/_Anthropocene/Resources/OpeningConfig.json"))) return dir;
            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }
        return Directory.GetCurrentDirectory();
    }

    private static void EquilibriumAndSeparation()
    {
        var cfg = SimulationConfig.Small(1);
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.Agriculture = new AgriculturalTechnologySettings { SanitationEnabled = true, SanitationDiseaseReduction = .50 };

        var base1000 = PhysicalDemography.Rates(cfg, 1000, 1, 1, 1000);
        Near(base1000.Net, 0, "baseline equilibrium at N=1000", 1e-10);
        var san1000 = PhysicalDemography.Rates(cfg, 1000, 1, 1, 1000, sanitationCapacity: 1000, diseaseReduction: .50);
        Near(san1000.Crowding, base1000.Crowding, "crowding mortality untouched by sanitation", 1e-12);
        Near(san1000.Exposure, base1000.Exposure, "exposure mortality untouched by sanitation", 1e-12);
        Near(san1000.Disease, 0.50 * base1000.Disease, "disease mortality halved at full coverage", 1e-12);
        Check(san1000.Net > 0.005, "positive net growth at N=1000 with full sanitation");

        var advBelow = PhysicalDemography.AdvanceFixed(cfg, 500, 20, 1, 1, 2000, sanitationCapacity: 2000, diseaseReduction: .50);
        var advAbove = PhysicalDemography.AdvanceFixed(cfg, 1500, 20, 1, 1, 2000, sanitationCapacity: 2000, diseaseReduction: .50);
        Check(advBelow.HasPositiveEquilibrium && advAbove.HasPositiveEquilibrium, "unique positive equilibrium exists");
        Near(advBelow.EquilibriumPopulation, 1141.845454, "exact equilibrium N* = 1141.845454", 1e-5);
        Near(advAbove.EquilibriumPopulation, advBelow.EquilibriumPopulation, "same equilibrium from above and below", 1e-7);
        Near(advBelow.AverageSanitationCoverage, 1.0, "full coverage tracked accurately", 1e-9);
        double popBelow = 500, popAbove = 1500;
        for (int t = 0; t < 50; t++)
        {
            popBelow = PhysicalDemography.AdvanceFixed(cfg, popBelow, 20, 1, 1, 2000, sanitationCapacity: 2000, diseaseReduction: .50).FinalPopulation;
            popAbove = PhysicalDemography.AdvanceFixed(cfg, popAbove, 20, 1, 1, 2000, sanitationCapacity: 2000, diseaseReduction: .50).FinalPopulation;
        }
        Near(popBelow, 1141.845454, "long horizon from N=500 converges to 1141.845454", 1e-3);
        Near(popAbove, 1141.845454, "long horizon from N=1500 converges to 1141.845454", 1e-3);
        var stationary = PhysicalDemography.AdvanceFixed(cfg, advBelow.EquilibriumPopulation, 20, 1, 1, 2000, sanitationCapacity: 2000, diseaseReduction: .50);
        Near(stationary.FinalPopulation, advBelow.EquilibriumPopulation, "equilibrium remains stationary", 1e-6);

        // Non-finite diseaseReduction must throw cleanly even when years == 0.
        bool nanFixedThrew = false;
        try { PhysicalDemography.AdvanceFixed(cfg, 1000, 0, 1, 1, 1000, sanitationCapacity: 1000, diseaseReduction: double.NaN); }
        catch (ArgumentException) { nanFixedThrew = true; }
        Check(nanFixedThrew, "AdvanceFixed rejects NaN diseaseReduction when years == 0");
        bool infBudgetThrew = false;
        try { PhysicalDemography.AdvanceBudget(cfg, 1000, 0, 1000, 1000, 1000, sanitationCapacity: 1000, diseaseReduction: double.PositiveInfinity); }
        catch (ArgumentException) { infBudgetThrew = true; }
        Check(infBudgetThrew, "AdvanceBudget rejects non-finite diseaseReduction when years == 0");
        bool nanRatesThrew = false;
        try { PhysicalDemography.Rates(cfg, 1000, 1, 1, 1000, sanitationCapacity: 1000, diseaseReduction: double.NaN); }
        catch (DemographicNumericalException) { nanRatesThrew = true; }
        Check(nanRatesThrew, "Rates rejects NaN diseaseReduction");

        // Verify QoL, SocialCohesion, HousingShare, QoLMortality untouched.
        var wNo = VillageFixture(1000, irrigationTech: true, sanitationEnabled: true);
        var wYes = VillageFixture(1000, irrigationTech: true, sanitationEnabled: true);
        NeolithicProduction.AddFixedSanitation(wYes, wYes.Camps[0], 60, wYes.Days);
        Near(NeolithicProduction.SocialCohesion(wYes, wYes.Camps[0], 1000),
            NeolithicProduction.SocialCohesion(wNo, wNo.Camps[0], 1000), "SocialCohesion untouched");
        Near(NeolithicProduction.HousingShare(wYes.Config.Neolithic, NeolithicProduction.HousingCapacity(wYes, wYes.Camps[0], wYes.Days), 1000),
            NeolithicProduction.HousingShare(wNo.Config.Neolithic, NeolithicProduction.HousingCapacity(wNo, wNo.Camps[0], wNo.Days), 1000),
            "HousingShare untouched");
        Near(QoLMortality.MaximumRate(wYes, wYes.Camps[0]), QoLMortality.MaximumRate(wNo, wNo.Camps[0]), "QoLMortality.MaximumRate untouched");
        NeolithicProduction.ResolveNeeds(wNo, wNo.Camps[0], 20, 1000);
        NeolithicProduction.ResolveNeeds(wYes, wYes.Camps[0], 20, 1000);
        Near(wYes.Camps[0].QoL, wNo.Camps[0].QoL, "QoL untouched by Sanitation");
    }

    private static void MonotonicityAndConservation()
    {
        var cfg = SimulationConfig.Small(1);
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.Agriculture = new AgriculturalTechnologySettings { SanitationEnabled = true, SanitationDiseaseReduction = .50 };

        double prev = double.PositiveInfinity;
        for (int k = 0; k <= 200; k++)
        {
            double n = 10 * Math.Pow(10000.0, k / 200.0);
            double net = PhysicalDemography.Rates(cfg, n, 1, 1, 600, sanitationCapacity: 500, diseaseReduction: .50).Net;
            Check(net < prev, $"R(ln N) strictly decreasing across N={n:F2} (prev={prev}, net={net})");
            prev = net;
        }

        // Cross the S_cap = 500 kink both growing and declining.
        var grow = PhysicalDemography.AdvanceFixed(cfg, 200, 60, 1, 1, 1000, sanitationCapacity: 500, diseaseReduction: .50);
        var shrink = PhysicalDemography.AdvanceFixed(cfg, 1200, 60, .6, .5, 1000, sanitationCapacity: 500, diseaseReduction: .50);
        foreach (var r in new[] { grow, shrink })
        {
            Check(r.CauseCorrection <= cfg.DemographicCauseCorrectionTolerance, "cause correction within tolerance across kink");
            Check(Math.Abs(r.ConservationError) < 1e-10, "exact conservation across kink");
            Near(r.Deaths, r.ExposureDeaths + r.CrowdingDeaths + r.DiseaseDeaths + r.LegacyStarvationDeaths + r.AdditionalDeaths, "cause sum equals total deaths", 1e-10);
            Near(r.FinalPopulation - r.InitialPopulation, r.Births - r.Deaths, "net population change equals births - deaths", 1e-10);
            Check(r.AverageSanitationCoverage > 0 && r.AverageSanitationCoverage < 1, "partial sanitation coverage in (0,1) when crossing S_cap");
        }

        // PlanningBudget agrees with AdvanceBudget with sanitation active.
        double food = 1000 * PhysicalDemography.FoodPerPersonYear(cfg) * 25;
        double cloth = 1000 * PhysicalDemography.ClothingPerPersonYear(cfg) * 25;
        var exact = PhysicalDemography.AdvanceBudget(cfg, 1000, 20, food, cloth, 1200, sanitationCapacity: 1100, diseaseReduction: .50);
        var fast = PhysicalDemography.PlanningBudget(cfg, 1000, 20, food, cloth, 1200, sanitationCapacity: 1100, diseaseReduction: .50);
        Near(fast.PersonYears, exact.PersonYears, "PlanningBudget agrees with AdvanceBudget on PersonYears", 1e-5);
        Near(fast.FinalPopulation, exact.FinalPopulation, "PlanningBudget agrees with AdvanceBudget on FinalPopulation", 1e-5);
        Near(fast.AverageSanitationCoverage, exact.AverageSanitationCoverage, "PlanningBudget agrees on AverageSanitationCoverage", 1e-5);
    }

    private static void DtSubdivisionInvariance()
    {
        var cfg = SimulationConfig.Small(1);
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.Agriculture = new AgriculturalTechnologySettings { SanitationEnabled = true, SanitationDiseaseReduction = .50 };

        // 1. Demography AdvanceFixed dt-subdivision across S_cap = 500: 1 x 20 yr vs 20 x 1 yr, and 1 x 1 yr vs 365 x 1 d.
        var big20 = PhysicalDemography.AdvanceFixed(cfg, 350, 20, 1, 1, 1000, sanitationCapacity: 500, diseaseReduction: .50);
        double pop20 = 350, py20 = 0, dis20 = 0, sanPy20 = 0;
        for (int i = 0; i < 20; i++)
        {
            var step = PhysicalDemography.AdvanceFixed(cfg, pop20, 1, 1, 1, 1000, sanitationCapacity: 500, diseaseReduction: .50);
            pop20 = step.FinalPopulation; py20 += step.PersonYears; dis20 += step.DiseaseDeaths; sanPy20 += step.AverageSanitationCoverage * step.PersonYears;
        }
        Near(pop20, big20.FinalPopulation, "20x1yr vs 1x20yr FinalPopulation", 1e-4);
        Near(py20, big20.PersonYears, "20x1yr vs 1x20yr PersonYears", 1e-4);
        Near(dis20, big20.DiseaseDeaths, "20x1yr vs 1x20yr DiseaseDeaths", 2e-4);
        Near(sanPy20 / py20, big20.AverageSanitationCoverage, "20x1yr vs 1x20yr AverageSanitationCoverage", 1e-4);

        var big1 = PhysicalDemography.AdvanceFixed(cfg, 495, 1, 1, 1, 1000, sanitationCapacity: 500, diseaseReduction: .50);
        double pop1 = 495, py1 = 0, dis1 = 0;
        for (int d = 0; d < 365; d++)
        {
            var step = PhysicalDemography.AdvanceFixed(cfg, pop1, 1.0 / 365, 1, 1, 1000, sanitationCapacity: 500, diseaseReduction: .50);
            pop1 = step.FinalPopulation; py1 += step.PersonYears; dis1 += step.DiseaseDeaths;
        }
        Near(pop1, big1.FinalPopulation, "365x1d vs 1x1yr FinalPopulation", 1e-4);
        Near(py1, big1.PersonYears, "365x1d vs 1x1yr PersonYears", 1e-4);
        Near(dis1, big1.DiseaseDeaths, "365x1d vs 1x1yr DiseaseDeaths", 2e-4);

        // 2. FixedSanitationStock exponential depreciation dt-invariance: 1x20yr vs 20x1yr, and 1x1yr vs 365x1d.
        var w20 = VillageFixture(1000); var w1 = VillageFixture(1000);
        NeolithicProduction.AddFixedSanitation(w20, w20.Camps[0], 50, 0);
        NeolithicProduction.AddFixedSanitation(w1, w1.Camps[0], 50, 0);
        NeolithicProduction.DepreciateSanitation(w20, w20.Camps[0], 20);
        for (int i = 0; i < 20; i++) { NeolithicProduction.DepreciateSanitation(w1, w1.Camps[0], 1); w1.Days += w1.Config.DaysPerYear; }
        w20.Days += 20 * w20.Config.DaysPerYear;
        Near(NeolithicProduction.FixedSanitationAt(w20, w20.Camps[0], w20.Days), 50 * 0.90, "20yr decay is exact 10%", 1e-10);
        Near(NeolithicProduction.FixedSanitationAt(w1, w1.Camps[0], w1.Days),
            NeolithicProduction.FixedSanitationAt(w20, w20.Camps[0], w20.Days), "20x1yr decay == 1x20yr decay", 1e-10);

        var wYear = VillageFixture(1000); var wDay = VillageFixture(1000);
        NeolithicProduction.AddFixedSanitation(wYear, wYear.Camps[0], 50, 0);
        NeolithicProduction.AddFixedSanitation(wDay, wDay.Camps[0], 50, 0);
        NeolithicProduction.DepreciateSanitation(wYear, wYear.Camps[0], 1); wYear.Days += wYear.Config.DaysPerYear;
        for (int d = 0; d < 365; d++) { NeolithicProduction.DepreciateSanitation(wDay, wDay.Camps[0], 1.0 / 365); wDay.Days += 1; }
        Near(NeolithicProduction.FixedSanitationAt(wDay, wDay.Camps[0], wDay.Days),
            NeolithicProduction.FixedSanitationAt(wYear, wYear.Camps[0], wYear.Days), "365x1d decay == 1x1yr decay", 1e-10);
    }

    private static void CoupledPools()
    {
        var cfg = SimulationConfig.Small(1);
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.Agriculture = new AgriculturalTechnologySettings { SanitationEnabled = true, SanitationDiseaseReduction = .50 };
        double foodRate = PhysicalDemography.FoodPerPersonYear(cfg);
        double clothRate = PhysicalDemography.ClothingPerPersonYear(cfg);

        // 1. Homogeneous 2-pool split reduces exactly to scalar AdvanceBudget and agrees with RK4.
        var p0 = new DemographicServicePoolInput(new[] { new DemographicCohortInput(600) }, 600 * foodRate * 40, 600 * clothRate * 40, 600, 600);
        var p1 = new DemographicServicePoolInput(new[] { new DemographicCohortInput(400) }, 400 * foodRate * 40, 400 * clothRate * 40, 400, 400);
        var reduced = PhysicalDemography.AdvanceCoupledBudget(cfg, new[] { p0, p1 }, 20);
        var scalar = PhysicalDemography.AdvanceBudget(cfg, 1000, 20, 1000 * foodRate * 40, 1000 * clothRate * 40, 1000, sanitationCapacity: 1000);
        var rk4 = PhysicalDemography.AdvanceCoupledFixed(cfg, new[] { p0, p1 }, 20, new[] { 1.0, 1.0 }, new[] { 1.0, 1.0 });
        Check(reduced.UsedExactScalarReduction, "homogeneous sanitation pools use exact scalar reduction");
        Near(reduced.Total.FinalPopulation, scalar.FinalPopulation, "reduced matches scalar FinalPopulation", 1e-12);
        Near(reduced.Total.DiseaseDeaths, scalar.DiseaseDeaths, "reduced matches scalar DiseaseDeaths", 1e-12);
        Near(reduced.Total.AverageSanitationCoverage, scalar.AverageSanitationCoverage, "reduced matches scalar AverageSanitationCoverage", 1e-12);
        Near(rk4.Total.FinalPopulation, scalar.FinalPopulation, "RK4 agrees with scalar FinalPopulation", 2e-4);
        Near(rk4.Total.DiseaseDeaths, scalar.DiseaseDeaths, "RK4 agrees with scalar DiseaseDeaths", 5e-4);
        Near(rk4.Total.AverageSanitationCoverage, scalar.AverageSanitationCoverage, "RK4 agrees with scalar AverageSanitationCoverage", 2e-4);

        // 2. Home camp (Sanitation = 1200) vs detached expedition (Sanitation = 0) sharing density.
        var home = new DemographicServicePoolInput(new[] { new DemographicCohortInput(900) }, 900 * foodRate * 40, 900 * clothRate * 40, 1200, 1200);
        var exp = new DemographicServicePoolInput(new[] { new DemographicCohortInput(100) }, 100 * foodRate * 40, 100 * clothRate * 40, 100, 0);
        var mixed = PhysicalDemography.AdvanceCoupledBudget(cfg, new[] { home, exp }, 20);
        Check(!mixed.UsedExactScalarReduction, "heterogeneous sanitation disables scalar reduction");
        Near(mixed.Pools[0].AverageSanitationCoverage, 1.0, "home camp has full sanitation coverage", 1e-9);
        Near(mixed.Pools[1].AverageSanitationCoverage, 0.0, "detached expedition has zero sanitation coverage", 1e-12);
        double homeDiseaseRate = mixed.Pools[0].DiseaseDeaths / mixed.Pools[0].PersonYears;
        double expDiseaseRate = mixed.Pools[1].DiseaseDeaths / mixed.Pools[1].PersonYears;
        Near(homeDiseaseRate / expDiseaseRate, 0.50, "home disease rate is half of expedition disease rate under shared density", 1e-2);
        Check(Math.Abs(mixed.Total.ConservationError) < 1e-9, "coupled total conservation holds");
    }

    private static void FreshwaterCoupling()
    {
        var w = VillageFixture(1000);
        var c = w.Camps[0];
        NeolithicProduction.AddFixedSanitation(w, c, 50, w.Days); // 1000 capacity

        // Fallback to WaterNeedsMetRatio when no sanitation water claim was submitted yet.
        c.HasSanitationWaterRatio = false;
        c.WaterNeedsMetRatio = 0.6;
        Near(NeolithicProduction.EffectiveSanitationCapacity(w, c, w.Days), 600, "falls back to WaterNeedsMetRatio when no claim submitted", 1e-8);
        c.WaterNeedsMetRatio = 1.0;

        // Verify SanitationClaims formula: min(pop, cap) * 0.15 * years on FreshwaterAccess.Source.
        var claims = IrrigationEconomy.SanitationClaims(w, 20, w.Days).ToArray();
        Check(claims.Length == 1, "one Sanitation freshwater claim emitted");
        int sourceTile = FreshwaterAccess.Source(w, c.Tile);
        Check(claims[0].Tile == sourceTile && claims[0].Kind == "Sanitation" && claims[0].Entity == c.Id, "claim targets FreshwaterAccess.Source");
        Near(claims[0].Freshwater, 1000 * 0.15 * 20, "claim volume is min(pop,cap)*0.15*years", 1e-8);

        // Verify Irrigation residual protection: scarce source where Drinking + Sanitation > WaterEnvelope -> Irrigation Delivery == 0.
        var project = IrrigationEconomy.Opportunity(w, c);
        Check(project != null, "freshwater coupling fixture has an irrigation project");
        if (project != null)
        {
            var irrOrder = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>())
                { Irrigation = new IrrigationOrder { Project = project, Construction = project.Remaining } };
            IrrigationEconomy.Produce(w, c, CraftGood.CanalConstruction, project.Remaining, irrOrder, w.Days);
            foreach (var s in w.Canals) s.FundedYears = 1000;
            sourceTile = project.Source;
            // Make sure camp's FreshwaterAccess.Source picks the same scarce source tile.
            foreach (int id in w.Map.Radius(c.Tile, 1))
            {
                if (id == sourceTile) continue;
                w.Tiles[id].Freshwater = 0;
                for (int s = 0; s < 4; s++) { w.Tiles[id].Seasons[s].MaxFreshwater = 0; w.Tiles[id].Seasons[s].WaterRecoveryPerDay = 0; }
            }
        }
        w.Tiles[sourceTile].Freshwater = 1000;
        for (int s = 0; s < 4; s++) { w.Tiles[sourceTile].Seasons[s].MaxFreshwater = 1000; w.Tiles[sourceTile].Seasons[s].WaterRecoveryPerDay = 0.0002f; }
        var forecastClaims = IrrigationEconomy.EssentialForecastClaimsForSources(w, 20, new HashSet<int> { sourceTile });
        Check(forecastClaims.Any(d => d.Kind == "Sanitation"), "EssentialForecastClaimsForSources includes Sanitation");
        var essential = IrrigationEconomy.DrinkingClaims(w, 20).Concat(IrrigationEconomy.SanitationClaims(w, 20, w.Days)).ToList();
        var irrAlloc = IrrigationEconomy.Allocate(w, essential, w.Days, 20 * w.Config.DaysPerYear);
        Near(irrAlloc.Delivery, 0, "Sanitation + Drinking protected ahead of Irrigation residual", 1e-8);

        // Resolve full turn on scarce source -> HasSanitationWaterRatio == true, 0 < SanitationWaterRatio < 1, water conserved.
        double startWater = w.Tiles[sourceTile].Freshwater;
        var resolver = new TurnResolver();
        var record = resolver.Resolve(w, TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>()));
        Check(c.HasSanitationWaterRatio, "CommitHorizonResources records HasSanitationWaterRatio");
        Check(c.SanitationWaterRatio > 0 && c.SanitationWaterRatio < 1, $"scarce source scales SanitationWaterRatio into (0,1) (got {c.SanitationWaterRatio})");
        Near(NeolithicProduction.EffectiveSanitationCapacity(w, c, w.Days),
            NeolithicProduction.SanitationCapacity(w, c, w.Days) * c.SanitationWaterRatio,
            "EffectiveSanitationCapacity equals installed * SanitationWaterRatio", 1e-8);
        Check(record.Transfers.Any(t => t.Kind == "Sanitation" && t.Freshwater > 0), "Sanitation freshwater transfer recorded");
        foreach (var bal in record.EcologyBalances.Where(b => b.Tile == sourceTile))
            Near(bal.StartingWater + bal.WaterRecharge - bal.WaterHarvest - bal.WaterOverflow, bal.EndingWater, "exact freshwater conservation at source tile", 1e-5);
        Check(startWater >= 0, "source tile water started nonnegative");
    }

    private static void ProductionAndStorage()
    {
        // 1. Builder + HousingMaterial + ToolCoverageBudget (Hand vs Improved Tools) + GoodsStorage exemption.
        var wHand = VillageFixture(400);
        wHand.Config.GoodsStorageEnabled = true;
        var cHand = wHand.Camps[0];
        cHand.Inventory.Pots = 0; // Zero Pots -> strict GoodsStorage test!
        cHand.Inventory.Tools = 0;
        cHand.Inventory.HandTools = 200;
        cHand.Inventory.HousingMaterial = 0;
        cHand.Inventory.Wood = 500;
        cHand.Workforce.Assign(WorkforceRoles.Builder, 20, cHand.Population);

        var wImp = VillageFixture(400);
        wImp.Config.GoodsStorageEnabled = true;
        var cImp = wImp.Camps[0];
        cImp.Inventory.Pots = 0;
        cImp.Inventory.HandTools = 0;
        cImp.Inventory.Tools = 50; // 200 tool outputs at 1.5x Builder multiplier
        cImp.Inventory.HousingMaterial = 0;
        cImp.Inventory.Wood = 500;
        cImp.Workforce.Assign(WorkforceRoles.Builder, 20, cImp.Population);

        var order = new ProductionOrder(new[] { CraftGood.HousingMaterial, CraftGood.Sanitation },
            new Dictionary<CraftGood, double> { [CraftGood.HousingMaterial] = 20, [CraftGood.Sanitation] = 20 });
        Check(order.HasSharedHousingMaterialConsumers, "ProductionOrder.HasSharedHousingMaterialConsumers true when Sanitation ceiling > 0");

        var recipes = NeolithicProduction.Recipes(wHand.Config);
        GoodsStorage.PlannedUse(recipes, g => g == CraftGood.Sanitation ? 10 : 0, out double eqOut, out double hAsm, out double infraMat);
        Near(eqOut, 10, "GoodsStorage.PlannedUse counts Sanitation in equipmentOutputs");
        Near(hAsm, 0, "GoodsStorage.PlannedUse does not miscount Sanitation as Housing assembly");
        Near(infraMat, 10 * wHand.Config.Agriculture.SanitationHousingMaterialsPerUnit, "GoodsStorage.PlannedUse exempts Sanitation HousingMaterial");
        Check(GoodsStorage.Index(CraftGood.Sanitation) == -1, "installed Sanitation is uncounted in Pot GoodsStorage");

        // Tight tool budget with order.Irrigation == null (B2 pattern): 10 tool outputs, need 10 HousingMaterial + 10 Sanitation.
        var wTight = VillageFixture(400);
        wTight.Config.GoodsStorageEnabled = true;
        var cTight = wTight.Camps[0];
        cTight.Inventory.Pots = 0;
        cTight.Inventory.HandTools = 10;
        cTight.Inventory.Tools = 0;
        cTight.Inventory.HousingMaterial = 0;
        cTight.Inventory.Wood = 500;
        cTight.Workforce.Assign(WorkforceRoles.Builder, 20, cTight.Population);
        var tightOrder = new ProductionOrder(new[] { CraftGood.HousingMaterial, CraftGood.Sanitation },
            new Dictionary<CraftGood, double> { [CraftGood.HousingMaterial] = 10, [CraftGood.Sanitation] = 10 });
        var forecast = MaterialEconomy.Forecast(wTight, cTight, NeolithicProduction.Recipes(wTight.Config),
            wTight.Config.GenerationIntervals, 20, 10, false, 400, true, true, order: tightOrder);
        Near(forecast[CraftGood.HousingMaterial], 10, "Forecast produces full HousingMaterial for Sanitation under tight tools");
        Near(forecast[CraftGood.Sanitation], 10, "Forecast produces full Sanitation under tight tools");

        // Run ResolveCampNeolithic with GoodsStorage active and verify exact debit + FixedSanitation installation.
        var needs = new TurnNeeds { Residents = 400, Order = tightOrder, Production = forecast };
        cTight.GoodsStorage = GoodsStorage.Begin(wTight, cTight, 20, 400, 10, 0, false, 10);
        var rec = new TurnRecord { StartDays = wTight.Days, Duration = wTight.Config.TurnDays };
        NeolithicProduction.ResolveCampNeolithic(wTight, cTight, wTight.Config.GenerationIntervals, 20, rec, 0, needs, resolveNeeds: false);
        Near(NeolithicProduction.FixedSanitationAt(wTight, cTight, wTight.Days + wTight.Config.TurnDays), 10, "Sanitation installs onto FixedSanitationStock even with Pots=0", 1e-6);
        Near(cTight.Inventory.HandTools, 0, "Sanitation debited exact HandTools", 1e-6);
        Near(cTight.Inventory.HousingMaterial, 0, "Sanitation consumed manufactured HousingMaterial", 1e-6);

        // Verify Improved Tools 1.5x faster than Hand Tools for Sanitation.
        var sanRecipe = recipes.Single(r => r.Good == CraftGood.Sanitation);
        cHand.Inventory.HousingMaterial = 100; cImp.Inventory.HousingMaterial = 100;
        double eq1 = 1000, eq2 = 1000;
        double laborHand = NeolithicProduction.Produce(wHand, cHand, sanRecipe, WorkforceRoles.Builder, 80, 80, 100, ref eq1, new TurnRecord { StartDays = 0, Duration = wHand.Config.TurnDays });
        double laborImp = NeolithicProduction.Produce(wImp, cImp, sanRecipe, WorkforceRoles.Builder, 80, 80, 100, ref eq2, new TurnRecord { StartDays = 0, Duration = wImp.Config.TurnDays });
        double outHand = NeolithicProduction.FixedSanitationAt(wHand, cHand, wHand.Config.TurnDays);
        double outImp = NeolithicProduction.FixedSanitationAt(wImp, cImp, wImp.Config.TurnDays);
        Check(laborHand > 0 && laborImp > 0, "Builders spent labor on Sanitation");
        Near(outImp / outHand, wHand.Config.Neolithic.ImprovedToolsHousingMultiplier / wHand.Config.Neolithic.HandToolsHousingMultiplier,
            "Improved Tools multiply Sanitation output via IsHousingWork", 1e-6);

        // SpecialistOnly = true: Craftsperson cannot build Sanitation.
        var wCraft = VillageFixture(400);
        wCraft.Camps[0].Inventory.HousingMaterial = 100;
        double eq3 = 1000;
        double usedCraft = NeolithicProduction.Produce(wCraft, wCraft.Camps[0], sanRecipe, WorkforceRoles.Craftsperson, 80, 80, 10, ref eq3, new TurnRecord { StartDays = 0, Duration = wCraft.Config.TurnDays });
        Near(usedCraft, 0, "Craftsperson cannot build SpecialistOnly Sanitation");
    }

    private static void AbandonmentAndFission()
    {
        var w = VillageFixture(1000);
        var c = w.Camps[0];
        int oldTile = c.Tile;
        int newTile = w.Map.Neighbors(oldTile).First(t => w.CanEnter(t, false));
        NeolithicProduction.AddFixedSanitation(w, c, 50, w.Days);
        Near(NeolithicProduction.FixedSanitationAt(w, c, w.Days), 50, "installed at origin site");

        // Camp abandons origin site for newTile: FixedSanitation remains at oldTile and is 0 at newTile.
        c.Tile = newTile;
        Near(NeolithicProduction.FixedSanitationAt(w, c, w.Days), 0, "abandoned sanitation is not carried to new tile");
        Near(NeolithicProduction.FixedSanitationAt(w, c.Id, oldTile, w.Days), 50, "abandoned sanitation remains on old tile");
        double futureDays = w.Days + 20 * w.Config.DaysPerYear;
        Near(NeolithicProduction.FixedSanitationAt(w, c.Id, oldTile, futureDays), 50 * 0.90, "abandoned sanitation depreciates naturally", 1e-9);

        // Direct in-place edit invalidated by FixedSanitationChanged.
        w.FixedSanitation[0].Tile = newTile;
        NeolithicProduction.FixedSanitationChanged(w);
        Near(NeolithicProduction.FixedSanitationAt(w, c, w.Days), 50, "FixedSanitationChanged invalidates transient index");

        // Daughter settlement starts at 0 sanitation and DiseaseMitigation == 1.0 even when parent is sanitized.
        var daughter = new Camp { Id = w.NextId++, Player = w.AllocatePlayer(), SocietyId = c.SocietyId, Tile = oldTile, Population = 200, IsVillage = false };
        daughter.Bind(c.People);
        w.Camps.Add(daughter);
        Check(PhysicalDemography.DiseaseMitigation(w, c) < 0.6, "sanitized parent has reduced DiseaseMitigation");
        Near(PhysicalDemography.DiseaseMitigation(w, daughter), 1.0, "daughter settlement has unmitigated DiseaseMitigation == 1.0");
        Near(NeolithicProduction.SanitationCapacity(w, daughter, w.Days), 0, "daughter settlement starts with 0 sanitation capacity");
    }

    private static void PersistenceAndLegacyOptOut()
    {
        var w = VillageFixture(1000);
        var c = w.Camps[0];
        NeolithicProduction.AddFixedSanitation(w, c, 42.5, w.Days);
        c.HasSanitationWaterRatio = true;
        c.SanitationWaterRatio = 0.75;

        // Roundtrip ANTHROPOCENE-11 and ANTHROPOCENE-12.
        string fpBefore = SaveCodec.Fingerprint(w);
        byte[] bytes11 = SaveCodec.Save(w);
        var load11 = SaveCodec.Load(bytes11);
        Check(SaveCodec.Fingerprint(load11) == fpBefore, "ANTHROPOCENE-11 fingerprint preserved");
        Near(NeolithicProduction.FixedSanitationAt(load11, load11.Camps[0], load11.Days), 42.5, "FixedSanitation survives ANTHROPOCENE-11");
        Check(load11.Camps[0].HasSanitationWaterRatio, "HasSanitationWaterRatio survives save/load");
        Near(load11.Camps[0].SanitationWaterRatio, 0.75, "SanitationWaterRatio survives save/load");

        using var ms = new MemoryStream();
        SaveCodec.Save(w, ms);
        ms.Position = 0;
        var loadStream = SaveCodec.Load(ms);
        Check(SaveCodec.Fingerprint(loadStream) == fpBefore, "streaming Save/Load preserves fingerprint");

        // Next-turn deterministic continuation after reload.
        var r1 = new TurnResolver(); var r2 = new TurnResolver();
        r1.Resolve(w, TurnPlanner.Prepare(w, new Dictionary<int, List<Order>>()));
        r2.Resolve(load11, TurnPlanner.Prepare(load11, new Dictionary<int, List<Order>>()));
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(load11), "post-load continuation fingerprint matches bit-for-bit");

        // ConfigurationHash sensitivity.
        string hOn = SaveCodec.ConfigurationHash(w.Config);
        w.Config.Agriculture.SanitationDiseaseReduction = 0.40;
        string hMod = SaveCodec.ConfigurationHash(w.Config);
        Check(hOn != hMod, "ConfigurationHash changes with SanitationDiseaseReduction");
        w.Config.Agriculture.SanitationDiseaseReduction = 0.50;

        // NormalizeFixedSanitation sorting & rejection.
        var wNorm = VillageFixture(500);
        int otherTile = wNorm.Map.Neighbors(wNorm.Camps[0].Tile).First();
        wNorm.FixedSanitation = new List<FixedSanitationStock>
        {
            new FixedSanitationStock { CampId = wNorm.Camps[0].Id, Tile = Math.Max(wNorm.Camps[0].Tile, otherTile), Units = 5, LastUpdatedDays = 0 },
            new FixedSanitationStock { CampId = wNorm.Camps[0].Id, Tile = Math.Min(wNorm.Camps[0].Tile, otherTile), Units = 3, LastUpdatedDays = 0 }
        };
        NeolithicProduction.NormalizeFixedSanitation(wNorm);
        Check(wNorm.FixedSanitation[0].Tile < wNorm.FixedSanitation[1].Tile, "NormalizeFixedSanitation canonically sorts by (CampId, Tile)");

        void RejectNorm(FixedSanitationStock bad, string label)
        {
            var wt = VillageFixture(500);
            wt.FixedSanitation = new List<FixedSanitationStock> { bad };
            bool threw = false;
            try { NeolithicProduction.NormalizeFixedSanitation(wt); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "NormalizeFixedSanitation rejects " + label);
        }
        RejectNorm(new FixedSanitationStock { CampId = wNorm.Camps[0].Id, Tile = wNorm.Camps[0].Tile, Units = -1, LastUpdatedDays = 0 }, "negative units");
        RejectNorm(new FixedSanitationStock { CampId = wNorm.Camps[0].Id, Tile = wNorm.Camps[0].Tile, Units = 1, LastUpdatedDays = 999 }, "future LastUpdatedDays");
        RejectNorm(new FixedSanitationStock { CampId = 99999, Tile = wNorm.Camps[0].Tile, Units = 1, LastUpdatedDays = 0 }, "unknown CampId");
        {
            var wt = VillageFixture(500);
            wt.FixedSanitation = new List<FixedSanitationStock>
            {
                new FixedSanitationStock { CampId = wt.Camps[0].Id, Tile = wt.Camps[0].Tile, Units = 1, LastUpdatedDays = 0 },
                new FixedSanitationStock { CampId = wt.Camps[0].Id, Tile = wt.Camps[0].Tile, Units = 2, LastUpdatedDays = 0 }
            };
            bool threw = false;
            try { NeolithicProduction.NormalizeFixedSanitation(wt); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "NormalizeFixedSanitation rejects duplicate (CampId, Tile)");
        }

        // Legacy PR #49 save simulation: deserialize AgriculturalTechnologySettings XML lacking Sanitation* and Valley* elements.
        var ser = new System.Runtime.Serialization.DataContractSerializer(typeof(AgriculturalTechnologySettings));
        var sample = new AgriculturalTechnologySettings { PloughEnabled = true, IrrigationEnabled = true, SanitationEnabled = true };
        string xml;
        using (var sw = new StringWriter())
        using (var xw = XmlWriter.Create(sw))
        {
            ser.WriteObject(xw, sample);
            xw.Flush();
            xml = sw.ToString();
        }
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        foreach (string tag in new[] { "SanitationEnabled", "SanitationDiseaseReduction", "SanitationPeoplePerUnit",
            "SanitationHousingMaterialsPerUnit", "SanitationBuilderOutputPerBaseDays", "SanitationDecayPerReferenceTurn",
            "SanitationWaterPerPersonYear", "ValleyMaxSlopeDegrees", "ValleyBankWidthMeters" })
        {
            var nodes = doc.GetElementsByTagName(tag);
            for (int i = nodes.Count - 1; i >= 0; i--) nodes[i].ParentNode.RemoveChild(nodes[i]);
        }
        AgriculturalTechnologySettings legacy;
        using (var sr = new StringReader(doc.OuterXml))
        using (var xr = XmlReader.Create(sr))
            legacy = (AgriculturalTechnologySettings)ser.ReadObject(xr);
        legacy.Validate();
        Check(!legacy.SanitationEnabled, "legacy PR #49 save loads SanitationEnabled == false");
        Near(legacy.SanitationDiseaseReduction, .50, "legacy deserialization populates SanitationDiseaseReduction");
        Near(legacy.SanitationPeoplePerUnit, 20, "legacy deserialization populates SanitationPeoplePerUnit");
        Near(legacy.SanitationBuilderOutputPerBaseDays, .03125, "legacy deserialization populates SanitationBuilderOutputPerBaseDays");
        Near(legacy.ValleyMaxSlopeDegrees, 0, "legacy deserialization preserves ValleyMaxSlopeDegrees == 0");
        Near(legacy.ValleyBankWidthMeters, 0, "legacy deserialization preserves ValleyBankWidthMeters == 0");
    }

    private static void ElderAndDiagnostics()
    {
        // 1. Mature Irrigation village at N=1000 organically funds Sanitation via Elder and grows past 1,000 toward 1,142.
        var wOn = VillageFixture(1000, irrigationTech: true, sanitationEnabled: true);
        var wOff = VillageFixture(1000, irrigationTech: true, sanitationEnabled: false);
        var wNoTech = VillageFixture(1000, irrigationTech: false, sanitationEnabled: true);

        foreach (var w in new[] { wOn, wOff, wNoTech })
        {
            var c = w.Camps[0];
            c.EconomyAutomationTurns = -1;
            c.Inventory.Wood = 50000;
            c.Inventory.Clay = 50000;
            // Keep ample freshwater on camp tile so water is abundant.
            w.Tiles[c.Tile].Freshwater = 50000;
            w.Tiles[c.Tile].FreshwaterSource = FreshwaterSource.River;
            for (int s = 0; s < 4; s++) { w.Tiles[c.Tile].Seasons[s].MaxFreshwater = 50000; w.Tiles[c.Tile].Seasons[s].WaterRecoveryPerDay = 100; }
        }

        // Verify DemandController.Plan requests Sanitation Development for wOn and NOT for wOff / wNoTech.
        DemandController.Plan(wOn, wOn.Camps[0], EconomicIntent.From(wOn.Camps[0]), out var orderOn, out var allocOn);
        Check(allocOn.Requests.Any(q => q.Service == "sanitation development" && q.Granted > 0), "Elder grants sanitation development on unlocked village");
        Check(orderOn.Ceiling(CraftGood.Sanitation) > 0, "ProductionOrder carries positive Sanitation ceiling");
        DemandController.Plan(wOff, wOff.Camps[0], EconomicIntent.From(wOff.Camps[0]), out _, out var allocOff);
        Check(!allocOff.Requests.Any(q => q.Good == CraftGood.Sanitation && q.Granted > 0), "Elder does not build Sanitation when disabled");
        DemandController.Plan(wNoTech, wNoTech.Camps[0], EconomicIntent.From(wNoTech.Camps[0]), out _, out var allocNoTech);
        Check(!allocNoTech.Requests.Any(q => q.Good == CraftGood.Sanitation && q.Granted > 0), "Elder does not build Sanitation without Irrigation tech");

        // Run 4 turns (80 years) on wOn and wOff with ample food/cloth replenishment so demographic ceiling is cleanly tested.
        TurnRecord lastRecordOn = null;
        for (int t = 0; t < 4; t++)
        {
            foreach (var w in new[] { wOn, wOff })
            {
                var c = w.Camps[0];
                c.PlantFood = Math.Max(c.PlantFood, c.Population * PhysicalDemography.FoodPerPersonYear(w.Config) * 40);
                c.Inventory.Cloth = Math.Max(c.Inventory.Cloth, c.Population * 2);
                c.Inventory.Tools = Math.Max(c.Inventory.Tools, 2000);
                c.Inventory.Wood = Math.Max(c.Inventory.Wood, 50000);
                // Add fixed housing matching population so housing doesn't obscure the sanitation lift.
                double needHousing = Math.Max(0, (c.Population + 200) / w.Config.Neolithic.HousingPeoplePerUnit - NeolithicProduction.FixedHousingAt(w, c, w.Days));
                if (needHousing > 0) NeolithicProduction.AddFixedHousing(w, c, needHousing, w.Days);
                var ai = AiPlanning.Decide(w);
                var rec = new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, ai.Orders, ai.Traces));
                if (ReferenceEquals(w, wOn)) lastRecordOn = rec;
            }
        }
        Check(NeolithicProduction.FixedSanitationAt(wOn, wOn.Camps[0], wOn.Days) > 20, "Elder built substantial FixedSanitation");
        Check(wOn.Camps[0].Population > 1030, $"Sanitized village grew past 1,000 ceiling (pop={wOn.Camps[0].Population})");
        Check(wOff.Camps[0].Population <= 1005, $"Unsanitized village stayed near 1,000 ceiling (pop={wOff.Camps[0].Population})");
        Check(wOn.Camps[0].Population > wOff.Camps[0].Population + 25, "Sanitized village materially exceeds unsanitized control");

        // Verify Continuity replacement request appears once FixedSanitation exists.
        DemandController.Plan(wOn, wOn.Camps[0], EconomicIntent.From(wOn.Camps[0]), out _, out var allocCont);
        Check(allocCont.Requests.Any(q => q.Service == "sanitation replacement" && q.Class == DemandController.ServiceClass.Continuity && q.Granted > 0),
            "Elder schedules Sanitation Continuity replacement for existing stock");

        // Verify diagnostics & UI status string.
        var demo = lastRecordOn.Demographics.First(d => d.Camp == wOn.Camps[0].Id);
        Check(demo.AverageSanitationCoverage > 0.5, $"DemographicReport.AverageSanitationCoverage populated ({demo.AverageSanitationCoverage})");
        var welfare = lastRecordOn.SettlementWelfare.First(s => s.Camp == wOn.Camps[0].Id);
        Check(welfare.SanitationPeople > 400, $"SettlementWelfareReport.SanitationPeople populated ({welfare.SanitationPeople})");
        Check(PhysicalDemography.DiseaseMitigation(wOn, wOn.Camps[0]) < 0.7, "DiseaseMitigation reflects active Sanitation");
        var projOn = PhysicalDemography.ProjectedSettlementRates(wOn, wOn.Camps[0], 1000, 1, 1);
        var projOff = PhysicalDemography.ProjectedSettlementRates(wOff, wOff.Camps[0], 1000, 1, 1);
        Check(projOn.Disease < projOff.Disease * 0.7, "ProjectedSettlementRates reflects active Sanitation");
        string status = IrrigationEconomy.Status(wOn, wOn.Camps[0]);
        Check(status.Contains("Sanitation:") && status.Contains("active disease reduction"), "IrrigationEconomy.Status exposes Sanitation readout");

        // Verify SocialRelocation UtilityUpperBound >= Forecast (--bound-audit soundness) on sanitized camp and unsanitized daughter.
        var camp = wOn.Camps[0];
        var cache = new SocialRelocationForecastCache(wOn, wOn.Camps);
        var policy = new SocialRelocationPolicy(0, 0, 0, 0, mortalityWeight: 1, jobWeight: 0, welfareWeight: 1);
        var parentState = SocialRelocationForecastCache.Existing(camp, camp.Population, camp.PlantFood, camp.AnimalFood, camp.AquaticFood, camp.Workforce, camp.Inventory);
        var parentForecast = cache.Forecast(parentState);
        double parentUtility = policy.WelfareWeight * parentForecast.QoL - policy.MortalityWeight * parentForecast.MortalityRisk;
        double parentBound = cache.UtilityUpperBound(parentState, EmploymentOpportunityMatch.Zero, policy, 0);
        Check(parentUtility <= parentBound + 1e-6, $"SocialRelocation bound sound on sanitized camp ({parentUtility} <= {parentBound})");

        int daughterTile = wOn.Map.Neighbors(camp.Tile).First(t => wOn.CanEnter(t, false));
        var daughterState = cache.Daughter(camp, 150, daughterTile, new CampInventory(), new PopulationLedger(), 1000, 0, 0, new SocialNewSiteCapacity(5000, 0));
        var daughterForecast = cache.Forecast(daughterState);
        double daughterUtility = policy.WelfareWeight * daughterForecast.QoL - policy.MortalityWeight * daughterForecast.MortalityRisk;
        double daughterBound = cache.UtilityUpperBound(daughterState, EmploymentOpportunityMatch.Zero, policy, 0);
        Check(daughterUtility <= daughterBound + 1e-6, $"SocialRelocation bound sound on unsanitized daughter ({daughterUtility} <= {daughterBound})");
        Near(PhysicalDemography.DiseaseMitigation(wOn, daughterState.View), 1.0, "SocialRelocation daughter view has DiseaseMitigation == 1.0");
    }

    private static void UnceilingedAndPlannerEdgeRegressions()
    {
        WorldState ManualFixture(bool sanitationEnabled, int builders, double handTools, bool goodsStorage, double pots, double cloth)
        {
            var w = VillageFixture(400, irrigationTech: true, sanitationEnabled: sanitationEnabled);
            w.Config.Agriculture.IrrigationEnabled = false;
            w.Config.GoodsStorageEnabled = goodsStorage;
            var c = w.Camps[0];
            w.FixedHousing.Clear();
            NeolithicProduction.FixedHousingChanged(w);
            w.FixedSanitation.Clear();
            NeolithicProduction.FixedSanitationChanged(w);
            c.EconomyAutomationTurns = 0;
            c.Inventory.HousingMaterial = 0;
            c.Inventory.Wood = 5000;
            c.Inventory.Clay = 5000;
            c.Inventory.Stone = 5000;
            c.Inventory.Tools = 0;
            c.Inventory.HandTools = handTools;
            c.Inventory.Pots = pots;
            c.Inventory.Cloth = cloth;
            c.Workforce.Assign(WorkforceRoles.Builder, builders, c.Population);
            w.Tiles[c.Tile].Freshwater = 50000;
            w.Tiles[c.Tile].FreshwaterSource = FreshwaterSource.River;
            for (int s = 0; s < 4; s++) { w.Tiles[c.Tile].Seasons[s].MaxFreshwater = 50000; w.Tiles[c.Tile].Seasons[s].WaterRecoveryPerDay = 100; }
            return w;
        }

        // Repro A: SocialRelocationForecastCache.Baseline (order == null) on 400-res village with 0 FixedHousing/FixedSanitation,
        // 2 Builders, 200 HandTools must match SanitationEnabled = false identically (no Builder starvation / QoL collapse).
        {
            var wOn = ManualFixture(sanitationEnabled: true, builders: 2, handTools: 200, goodsStorage: false, pots: 500, cloth: 800);
            var wOff = ManualFixture(sanitationEnabled: false, builders: 2, handTools: 200, goodsStorage: false, pots: 500, cloth: 800);
            var bOn = new SocialRelocationForecastCache(wOn, wOn.Camps).Baseline(wOn.Camps[0]);
            var bOff = new SocialRelocationForecastCache(wOff, wOff.Camps).Baseline(wOff.Camps[0]);
            Near(bOn.ShelterCapacity, 20.0, "Repro A: Baseline ShelterCapacity == 20.0", 1e-6);
            Near(bOn.HousingCoverage, 0.05, "Repro A: Baseline HousingCoverage == 0.05", 1e-6);
            Near(bOn.ShelterCapacity, bOff.ShelterCapacity, "Repro A: Baseline ShelterCapacity matches SanitationEnabled=false", 1e-10);
            Near(bOn.HousingCoverage, bOff.HousingCoverage, "Repro A: Baseline HousingCoverage matches SanitationEnabled=false", 1e-10);
            Near(bOn.QoL, bOff.QoL, "Repro A: Baseline QoL matches SanitationEnabled=false", 1e-10);
            Near(bOn.ExposureRate, bOff.ExposureRate, "Repro A: Baseline ExposureRate matches SanitationEnabled=false", 1e-10);
        }

        // Repro B: Manual player turn (EconomyAutomationTurns = 0, 2 Builders, 200 HandTools, GoodsStorageEnabled = false)
        // produces HM == 5.0 and H == 5.0 (matching SanitationEnabled = false, not HM = 10, H = 0).
        {
            var wOn = ManualFixture(sanitationEnabled: true, builders: 2, handTools: 200, goodsStorage: false, pots: 500, cloth: 800);
            var wOff = ManualFixture(sanitationEnabled: false, builders: 2, handTools: 200, goodsStorage: false, pots: 500, cloth: 800);
            var recOn = new TurnResolver().Resolve(wOn, TurnPlanner.Prepare(wOn, new Dictionary<int, List<Order>>()));
            var recOff = new TurnResolver().Resolve(wOff, TurnPlanner.Prepare(wOff, new Dictionary<int, List<Order>>()));
            double hmOn = recOn.Production.Where(p => p.Good == CraftGood.HousingMaterial).Sum(p => p.Output);
            double hOn = recOn.Production.Where(p => p.Good == CraftGood.Housing).Sum(p => p.Output);
            double hmOff = recOff.Production.Where(p => p.Good == CraftGood.HousingMaterial).Sum(p => p.Output);
            double hOff = recOff.Production.Where(p => p.Good == CraftGood.Housing).Sum(p => p.Output);
            Near(hmOn, 5.0, "Repro B: Manual turn HM == 5.0", 1e-6);
            Near(hOn, 5.0, "Repro B: Manual turn H == 5.0", 1e-6);
            Near(hmOn, hmOff, "Repro B: Manual turn HM matches SanitationEnabled=false", 1e-8);
            Near(hOn, hOff, "Repro B: Manual turn H matches SanitationEnabled=false", 1e-8);
            Near(wOn.Camps[0].Inventory.HousingMaterial, 0, "Repro B: No stranded HousingMaterial", 1e-6);
        }

        // Repro C: Manual player turn (EconomyAutomationTurns = 0, 20 Builders, 10 HandTools, GoodsStorageEnabled = true, Pots = 0, Cloth = 5000)
        // must not deadlock: Forecast and Actual both equal HM == 10.0, H == 10.0.
        {
            var wOn = ManualFixture(sanitationEnabled: true, builders: 20, handTools: 10, goodsStorage: true, pots: 0, cloth: 5000);
            var wOff = ManualFixture(sanitationEnabled: false, builders: 20, handTools: 10, goodsStorage: true, pots: 0, cloth: 5000);
            var prepOn = TurnPlanner.Prepare(wOn, new Dictionary<int, List<Order>>());
            var prepOff = TurnPlanner.Prepare(wOff, new Dictionary<int, List<Order>>());
            var fOn = prepOn.Needs[wOn.Camps[0].Id].Production;
            var fOff = prepOff.Needs[wOff.Camps[0].Id].Production;
            var recOn = new TurnResolver().Resolve(wOn, prepOn);
            var recOff = new TurnResolver().Resolve(wOff, prepOff);
            double aHmOn = recOn.Production.Where(p => p.Good == CraftGood.HousingMaterial).Sum(p => p.Output);
            double aHOn = recOn.Production.Where(p => p.Good == CraftGood.Housing).Sum(p => p.Output);
            double aHmOff = recOff.Production.Where(p => p.Good == CraftGood.HousingMaterial).Sum(p => p.Output);
            double aHOff = recOff.Production.Where(p => p.Good == CraftGood.Housing).Sum(p => p.Output);
            Near(fOn[CraftGood.HousingMaterial], 10.0, "Repro C: Forecast HM == 10.0", 1e-6);
            Near(fOn[CraftGood.Housing], 10.0, "Repro C: Forecast H == 10.0", 1e-6);
            Near(aHmOn, 10.0, "Repro C: Actual HM == 10.0", 1e-6);
            Near(aHOn, 10.0, "Repro C: Actual H == 10.0", 1e-6);
            Near(fOn[CraftGood.HousingMaterial], fOff[CraftGood.HousingMaterial], "Repro C: Forecast HM matches SanitationEnabled=false", 1e-8);
            Near(fOn[CraftGood.Housing], fOff[CraftGood.Housing], "Repro C: Forecast H matches SanitationEnabled=false", 1e-8);
            Near(aHmOn, aHmOff, "Repro C: Actual HM matches SanitationEnabled=false", 1e-8);
            Near(aHOn, aHOff, "Repro C: Actual H matches SanitationEnabled=false", 1e-8);
        }

        // Attack 6A: SocialRelocationForecastCache.Baseline on 400-resident Irrigation+Construction village
        // (IrrigationEnabled = false, 2 Builders, 200 HandTools) with opening HousingMaterial = 5 and FH in {0, 98}
        // must be bit-identical between SanitationEnabled = true and false (no Sanitation priority theft / QoL collapse).
        foreach (double fixedHousing in new[] { 0.0, 98.0 })
        {
            var wOn = ManualFixture(sanitationEnabled: true, builders: 2, handTools: 200, goodsStorage: false, pots: 500, cloth: 800);
            var wOff = ManualFixture(sanitationEnabled: false, builders: 2, handTools: 200, goodsStorage: false, pots: 500, cloth: 800);
            if (fixedHousing > 0)
            {
                NeolithicProduction.AddFixedHousing(wOn, wOn.Camps[0], fixedHousing, wOn.Days);
                NeolithicProduction.AddFixedHousing(wOff, wOff.Camps[0], fixedHousing, wOff.Days);
            }
            wOn.Camps[0].Inventory.HousingMaterial = 5.0;
            wOff.Camps[0].Inventory.HousingMaterial = 5.0;
            var bOn = new SocialRelocationForecastCache(wOn, wOn.Camps).Baseline(wOn.Camps[0]);
            var bOff = new SocialRelocationForecastCache(wOff, wOff.Camps).Baseline(wOff.Camps[0]);
            Check(BitConverter.DoubleToInt64Bits(bOn.ShelterCapacity) == BitConverter.DoubleToInt64Bits(bOff.ShelterCapacity),
                $"Attack 6A (FH={fixedHousing}, HM=5): Baseline ShelterCapacity bit-identical ({bOn.ShelterCapacity:R} vs {bOff.ShelterCapacity:R})");
            Check(BitConverter.DoubleToInt64Bits(bOn.HousingCoverage) == BitConverter.DoubleToInt64Bits(bOff.HousingCoverage),
                $"Attack 6A (FH={fixedHousing}, HM=5): Baseline HousingCoverage bit-identical ({bOn.HousingCoverage:R} vs {bOff.HousingCoverage:R})");
            Check(BitConverter.DoubleToInt64Bits(bOn.QoL) == BitConverter.DoubleToInt64Bits(bOff.QoL),
                $"Attack 6A (FH={fixedHousing}, HM=5): Baseline QoL bit-identical ({bOn.QoL:R} vs {bOff.QoL:R})");
            Check(BitConverter.DoubleToInt64Bits(bOn.ExposureRate) == BitConverter.DoubleToInt64Bits(bOff.ExposureRate),
                $"Attack 6A (FH={fixedHousing}, HM=5): Baseline ExposureRate bit-identical ({bOn.ExposureRate:R} vs {bOff.ExposureRate:R})");
        }

        // Attack 6B: Manual player turn (EconomyAutomationTurns = 0, FH = 110, HM = 0, 20 Builders, 200 HandTools)
        // under BOTH GoodsStorageEnabled = false and GoodsStorageEnabled = true (Pots = 0, Cloth = 5000):
        // Forecast == Actual == (HM = 1.0, H = 1.0, San = 0.0) and LeftHM == 0.
        foreach (bool goodsStorage in new[] { false, true })
        {
            double pots = goodsStorage ? 0 : 500;
            double cloth = goodsStorage ? 5000 : 800;
            var wMan = ManualFixture(sanitationEnabled: true, builders: 20, handTools: 200, goodsStorage: goodsStorage, pots: pots, cloth: cloth);
            NeolithicProduction.AddFixedHousing(wMan, wMan.Camps[0], 110.0, wMan.Days);
            var prep = TurnPlanner.Prepare(wMan, new Dictionary<int, List<Order>>());
            var fc = prep.Needs[wMan.Camps[0].Id].Production;
            var rec = new TurnResolver().Resolve(wMan, prep);
            double aHm = rec.Production.Where(p => p.Good == CraftGood.HousingMaterial).Sum(p => p.Output);
            double aH = rec.Production.Where(p => p.Good == CraftGood.Housing).Sum(p => p.Output);
            double aSan = rec.Production.Where(p => p.Good == CraftGood.Sanitation).Sum(p => p.Output);
            Near(fc[CraftGood.HousingMaterial], 1.0, $"Attack 6B (GS={goodsStorage}): Forecast HM == 1.0", 1e-8);
            Near(fc[CraftGood.Housing], 1.0, $"Attack 6B (GS={goodsStorage}): Forecast H == 1.0", 1e-8);
            Near(fc[CraftGood.Sanitation], 0.0, $"Attack 6B (GS={goodsStorage}): Forecast San == 0.0", 1e-12);
            Near(aHm, 1.0, $"Attack 6B (GS={goodsStorage}): Actual HM == 1.0", 1e-8);
            Near(aH, 1.0, $"Attack 6B (GS={goodsStorage}): Actual H == 1.0", 1e-8);
            Near(aSan, 0.0, $"Attack 6B (GS={goodsStorage}): Actual San == 0.0", 1e-12);
            Near(fc[CraftGood.HousingMaterial], aHm, $"Attack 6B (GS={goodsStorage}): Forecast HM == Actual HM", 1e-8);
            Near(fc[CraftGood.Housing], aH, $"Attack 6B (GS={goodsStorage}): Forecast H == Actual H", 1e-8);
            Near(fc[CraftGood.Sanitation], aSan, $"Attack 6B (GS={goodsStorage}): Forecast San == Actual San", 1e-12);
            Near(wMan.Camps[0].Inventory.HousingMaterial, 0.0, $"Attack 6B (GS={goodsStorage}): LeftHM == 0", 1e-8);
        }

        // Dry-water continuity & development gate: village with FixedSanitation = 30 and zero water grants
        // 0 Continuity replacement and 0 Development expansion under both ElderKernelSplit = true and false.
        foreach (bool kernelSplit in new[] { true, false })
        {
            foreach (bool useSanitationWaterRatio in new[] { false, true })
            {
                var wDry = VillageFixture(1000, irrigationTech: true, sanitationEnabled: true);
                wDry.Config.ElderKernelSplit = kernelSplit;
                var cDry = wDry.Camps[0];
                cDry.EconomyAutomationTurns = -1;
                NeolithicProduction.AddFixedSanitation(wDry, cDry, 30, wDry.Days);
                if (useSanitationWaterRatio)
                {
                    cDry.WaterNeedsMetRatio = 1.0;
                    cDry.HasSanitationWaterRatio = true;
                    cDry.SanitationWaterRatio = 0.0;
                }
                else
                {
                    cDry.HasSanitationWaterRatio = false;
                    cDry.WaterNeedsMetRatio = 0.0;
                }
                DemandController.Plan(wDry, cDry, EconomicIntent.From(cDry), out var orderDry, out var allocDry);
                double contGranted = allocDry.Requests.Where(q => q.Good == CraftGood.Sanitation && q.Class == DemandController.ServiceClass.Continuity).Sum(q => q.Granted);
                double devGranted = allocDry.Requests.Where(q => q.Good == CraftGood.Sanitation && q.Class == DemandController.ServiceClass.Development).Sum(q => q.Granted);
                Near(contGranted, 0, $"Dry-water village grants 0 Continuity replacement (split={kernelSplit}, sanRatio={useSanitationWaterRatio})");
                Near(devGranted, 0, $"Dry-water village grants 0 Development expansion (split={kernelSplit}, sanRatio={useSanitationWaterRatio})");
                Near(orderDry.Ceiling(CraftGood.Sanitation), 0, $"Dry-water village sets 0 Sanitation ceiling (split={kernelSplit}, sanRatio={useSanitationWaterRatio})");
            }
        }

        // Zero-opening-tools (Tools = 0, HandTools = 0) Elder Sanitation expansion crafts tools on demand and builds Sanitation.
        {
            var wZero = VillageFixture(1000, irrigationTech: true, sanitationEnabled: true);
            wZero.Config.Agriculture.IrrigationEnabled = false;
            var cZero = wZero.Camps[0];
            cZero.EconomyAutomationTurns = -1;
            cZero.Inventory.Tools = 0;
            cZero.Inventory.HandTools = 0;
            cZero.Inventory.Wood = 50000;
            cZero.Inventory.Clay = 50000;
            cZero.Inventory.Stone = 50000;
            wZero.Tiles[cZero.Tile].Freshwater = 50000;
            wZero.Tiles[cZero.Tile].FreshwaterSource = FreshwaterSource.River;
            for (int s = 0; s < 4; s++) { wZero.Tiles[cZero.Tile].Seasons[s].MaxFreshwater = 50000; wZero.Tiles[cZero.Tile].Seasons[s].WaterRecoveryPerDay = 100; }
            NeolithicProduction.AddFixedSanitation(wZero, cZero, 50, wZero.Days);

            DemandController.Plan(wZero, cZero, EconomicIntent.From(cZero), out var orderZero, out var allocZero);
            double sanCeiling = orderZero.Ceiling(CraftGood.Sanitation);
            Check(sanCeiling > 5.0, $"Zero-tools Elder schedules Sanitation Continuity+Development (ceiling={sanCeiling:F3})");
            Check(orderZero.Ceiling(CraftGood.Tools) > 0 || orderZero.Ceiling(CraftGood.HandTools) > 0, "Zero-tools Elder schedules on-demand tool crafting");
            Check(allocZero.Requests.Any(q => q.Service == "sanitation development" && q.Granted > 0), "Zero-tools Elder grants Sanitation Development");

            var ai = AiPlanning.Decide(wZero);
            var recZero = new TurnResolver().Resolve(wZero, TurnPlanner.Prepare(wZero, ai.Orders, ai.Traces));
            double builtSan = recZero.Production.Where(p => p.Good == CraftGood.Sanitation).Sum(p => p.Output);
            double builtTools = recZero.Production.Where(p => p.Good == CraftGood.Tools || p.Good == CraftGood.HandTools).Sum(p => p.Output);
            Check(builtTools > 0, $"Zero-tools turn crafted tools on demand ({builtTools:F3})");
            Near(builtSan, sanCeiling, "Zero-tools turn built full planned Sanitation ceiling", 1e-4);
            Check(NeolithicProduction.FixedSanitationAt(wZero, cZero, wZero.Days) > 50, "Zero-tools turn expanded net FixedSanitation above 50");
        }
    }

    public static void Run()
    {
        checks = 0;
        ConfigAndEnum();
        EquilibriumAndSeparation();
        MonotonicityAndConservation();
        DtSubdivisionInvariance();
        CoupledPools();
        FreshwaterCoupling();
        ProductionAndStorage();
        AbandonmentAndFission();
        PersistenceAndLegacyOptOut();
        ElderAndDiagnostics();
        UnceilingedAndPlannerEdgeRegressions();
        Console.WriteLine($"PASS: SanitationChecks ({checks} checks)");
    }
}
