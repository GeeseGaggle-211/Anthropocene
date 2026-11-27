using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Anthropocene.Simulation;

internal static class CivicGovernanceChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Civic governance: " + message); }
    private static void Near(double value, double expected, string message, double tolerance = 1e-8)
        => Check(Math.Abs(value - expected) <= tolerance * Math.Max(1.0, Math.Abs(expected)), message + $" ({value} != {expected})");
    private static void ExpectThrows<TException>(Action action, string message) where TException : Exception
    {
        bool threw = false;
        try { action(); }
        catch (TException) { threw = true; }
        Check(threw, message);
    }

    public static int Run()
    {
        checks = 0;
        ConfigAndValidation();
        StewardRoleContract();
        AttractorsRelaxationAndFormulas();
        PrecedentsFissionAndAbandonment();
        OrdersMultiPolicyAndPlanningDraft();
        ObservationsAndPolicy();
        PersistenceAndLegacyCompatibility();
        Console.WriteLine("PASS: " + checks + " civic governance assertions");
        return checks;
    }

    private static string RepoPath(string relative)
    {
        string dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException("Could not locate repo file: " + relative);
    }

    private static WorldState CivicFixture(bool sanitation = true, bool enableCivic = true)
    {
        var w = AgriculturalTechnologyChecks.Fixture();
        w.Config.Civic = enableCivic ? new CivicGovernanceSettings { Enabled = true, SanitationEnabled = sanitation } : null;
        return w;
    }

    private static void ConfigAndValidation()
    {
        var defCfg = new SimulationConfig();
        Check(defCfg.Civic == null, "SimulationConfig.Civic defaults to null");
        Check(!CivicGovernance.Enabled(defCfg) && !CivicGovernance.SanitationEnabled(defCfg), "CivicGovernance disabled when Civic is null");

        var defaults = new CivicGovernanceSettings();
        Check(!defaults.Enabled && !defaults.SanitationEnabled, "default CivicGovernanceSettings has Enabled=false and SanitationEnabled=false");
        defaults.Validate();
        Check(true, "default disabled CivicGovernanceSettings validates cleanly (Unity JsonUtility safety)");

        var fields = typeof(CivicGovernanceSettings).GetFields(BindingFlags.Public | BindingFlags.Instance);
        Check(fields.Length == 73, "CivicGovernanceSettings exposes all 73 public fields (" + fields.Length + ")");

        string profilePath = RepoPath(Path.Combine("Assets", "_Anthropocene", "Resources", "CivicGovernanceOpeningConfig.json"));
        string json = File.ReadAllText(profilePath);
        using (var doc = JsonDocument.Parse(json))
        {
            Check(doc.RootElement.TryGetProperty("Civic", out var civicElem), "CivicGovernanceOpeningConfig.json contains top-level Civic block");
            int jsonFieldCount = civicElem.EnumerateObject().Count();
            Check(jsonFieldCount == 73, "CivicGovernanceOpeningConfig.json Civic block contains all 73 fields (" + jsonFieldCount + ")");
        }

        var loaded = JsonSerializer.Deserialize<SimulationConfig>(json, new JsonSerializerOptions { IncludeFields = true });
        Check(loaded != null && loaded.Civic != null && loaded.Civic.Enabled && loaded.Civic.SanitationEnabled,
            "CivicGovernanceOpeningConfig.json deserializes with Enabled=true and SanitationEnabled=true");
        loaded.Validate();
        foreach (var field in fields)
        {
            if (field.Name == nameof(CivicGovernanceSettings.Enabled) || field.Name == nameof(CivicGovernanceSettings.SanitationEnabled)) continue;
            object expected = field.GetValue(defaults);
            object actual = field.GetValue(loaded.Civic);
            if (field.FieldType == typeof(double))
                Near((double)actual, (double)expected, "profile JSON matches C# default for " + field.Name, 1e-12);
            else
                Check(Equals(actual, expected), "profile JSON matches C# default for " + field.Name + $" ({actual} != {expected})");
        }

        // Verify untouched legacy profiles do not include Civic block.
        foreach (string legacyFile in new[] { "OpeningConfig.json", "AgriculturalTechnologyOpeningConfig.json" })
        {
            string legacyJson = File.ReadAllText(RepoPath(Path.Combine("Assets", "_Anthropocene", "Resources", legacyFile)));
            using var legacyDoc = JsonDocument.Parse(legacyJson);
            Check(!legacyDoc.RootElement.TryGetProperty("Civic", out _), legacyFile + " remains free of Civic block");
        }

        // Invalid settings rejections.
        void ExpectInvalid(Action<CivicGovernanceSettings> mutate, string label)
        {
            var s = new CivicGovernanceSettings { Enabled = true, SanitationEnabled = true };
            mutate(s);
            ExpectThrows<ArgumentException>(() => s.Validate(), "Validate rejects " + label);
        }
        ExpectInvalid(s => s.ComplexityRelaxationRate = double.NaN, "NaN relaxation rate");
        ExpectInvalid(s => s.CrowdingShapeRate = double.PositiveInfinity, "Infinity crowding shape rate");
        ExpectInvalid(s => s.MinCrowdingMitigation = 0, "MinCrowdingMitigation <= 0");
        ExpectInvalid(s => s.MinCrowdingMitigation = 1.1, "MinCrowdingMitigation > 1");
        ExpectInvalid(s => s.MinDiseaseMitigation = 0, "MinDiseaseMitigation <= 0");
        ExpectInvalid(s => s.Tier1ComplexityCeiling = 0, "Tier1ComplexityCeiling <= 0");
        ExpectInvalid(s => s.Tier1ComplexityCeiling = 0.80, "Tier1 >= Tier2 ceiling");
        ExpectInvalid(s => s.Tier2ComplexityCeiling = 1.00, "Tier2 >= Tier3 ceiling");
        ExpectInvalid(s => s.Tier3ComplexityCeiling = 1.05, "Tier3 > 1 ceiling");
        ExpectInvalid(s => s.StratifiedStewardRate = 0.03, "StratifiedStewardRate < EgalitarianStewardRate");
        ExpectInvalid(s => s.StratifiedCapitalPerPerson = 0.10, "StratifiedCapitalPerPerson < EgalitarianCapitalPerPerson");
        ExpectInvalid(s => { s.QoLEqualityLinear = 0.09; s.QoLEqualityQuadratic = 0.05; }, "QoL q1 <= 2*q2 non-monotone");
        ExpectInvalid(s => s.TokensTradeRangeBonus = 11, "TokensTradeRangeBonus > 10");
        ExpectInvalid(s => s.TokensTradeRequisitionBonus = -1, "TokensTradeRequisitionBonus < 0");
        ExpectInvalid(s => s.DevelopmentMinPopulation = 0, "DevelopmentMinPopulation < 1");
        ExpectInvalid(s => s.SanitationDevelopmentMinPopulation = 0, "SanitationDevelopmentMinPopulation < 1");
        ExpectInvalid(s => s.CivicDeathFoodYears = 0, "CivicDeathFoodYears <= 0");
        ExpectInvalid(s => s.SanitationDeathFoodYears = -1, "SanitationDeathFoodYears <= 0");

        // Zero-ablated QoL equality is explicitly allowed.
        var zeroQol = new CivicGovernanceSettings { Enabled = true, QoLEqualityLinear = 0, QoLEqualityQuadratic = 0 };
        zeroQol.Validate();
        Check(true, "Validate allows cleanly zero-ablated QoL equality (q1 == q2 == 0)");
    }

    private static void StewardRoleContract()
    {
        Check(WorkforceRoles.All.Contains(WorkforceRoles.Steward), "WorkforceRoles.All includes Steward");
        Check(!WorkforceRoles.Specialists.Contains(WorkforceRoles.Steward), "WorkforceRoles.Specialists excludes Steward");
        Check(!WorkforceRoles.Extractors.Contains(WorkforceRoles.Steward), "WorkforceRoles.Extractors excludes Steward");
        Check(EconomicIntents.RequiredTechnology(WorkforceRoles.Steward) == null, "Steward requires no craft mastery technology");

        Check(WorkforceRoles.Label(WorkforceRoles.Stonecutter) == "Stone Quarrier", "1-arg Label preserves Stone Quarrier");
        Check(WorkforceRoles.Label(WorkforceRoles.Steward) == "Steward", "1-arg Label returns Steward");
        Check(WorkforceRoles.Label(WorkforceRoles.Steward, null) == "Steward", "2-arg Label(Steward, null) returns Steward");
        Check(WorkforceRoles.Label(WorkforceRoles.Stonecutter, new Camp { CivicStratification = -0.8 }) == "Stone Quarrier", "2-arg Label delegates non-Steward roles");
        Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = -0.30 }) == "Council Arbiter", "Steward label at P=-0.30 is Council Arbiter");
        Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = -0.80 }) == "Council Arbiter", "Steward label at P=-0.80 is Council Arbiter");
        Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = -0.29 }) == "Civic Steward", "Steward label at P=-0.29 is Civic Steward");
        Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = 0.0 }) == "Civic Steward", "Steward label at P=0.0 is Civic Steward");
        Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = 0.29 }) == "Civic Steward", "Steward label at P=+0.29 is Civic Steward");
        Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = 0.30 }) == "Temple Scribe", "Steward label at P=+0.30 is Temple Scribe");
        Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = 0.85 }) == "Temple Scribe", "Steward label at P=+0.85 is Temple Scribe");

        // EconomicIntents.Validate gating for Steward
        var disabledConstraints = new EconomicConstraints { Agriculture = true, StewardEnabled = false, Population = 100 };
        var enabledConstraints = new EconomicConstraints { Agriculture = true, StewardEnabled = true, Population = 100 };
        var stewardIntent = new EconomicIntent
        {
            Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Steward, Population = 10 } }
        };
        Check(EconomicIntents.Validate(disabledConstraints, stewardIntent) != null, "EconomicIntents.Validate rejects Steward when StewardEnabled is false");
        Check(EconomicIntents.Validate(enabledConstraints, stewardIntent) == null, "EconomicIntents.Validate accepts valid Steward assignment when StewardEnabled is true");

        var overPopIntent = new EconomicIntent
        {
            Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Steward, Population = 101 } }
        };
        Check(EconomicIntents.Validate(enabledConstraints, overPopIntent) != null, "EconomicIntents.Validate rejects Steward exceeding population");

        var badGatherers = stewardIntent.Copy();
        badGatherers.Labor = new EconomicLaborPlan { MaterialGatherers = 91 };
        Check(EconomicIntents.Validate(enabledConstraints, badGatherers) != null, "Stewards deduct from MaterialGatherers gatherer pool");

        // PopulationLedger casualties & Gatherer conservation with Stewards
        var ledger = new PopulationLedger();
        ledger.Assign(WorkforceRoles.Farmer, 40, 100);
        ledger.Assign(WorkforceRoles.Steward, 20, 100);
        Check(ledger.Gatherers(100) == 40, "Gatherers excludes assigned Stewards and Farmers");
        ledger.ApplyCasualties(100, 25);
        int remFarmers = ledger.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population);
        int remStewards = ledger.Assignments.Where(a => a.Role == WorkforceRoles.Steward).Sum(a => a.Population);
        Check(remFarmers == 30 && remStewards == 15 && ledger.Gatherers(75) == 30,
            "ApplyCasualties proportionally conserves Farmers, Stewards and Gatherers");

        // WorkforceTransfer.Split: fission converts Stewards -> Gatherer; immigration retains Stewards.
        var baseLedger = new PopulationLedger();
        baseLedger.Assign(WorkforceRoles.Farmer, 40, 100);
        baseLedger.Assign(WorkforceRoles.Steward, 20, 100);
        baseLedger.Assign(WorkforceRoles.Builder, 20, 100);
        var fissionSplit = WorkforceTransfer.Split(baseLedger, 100, 50, daughterFarmersBecomeGatherers: true);
        int dStewardsFission = fissionSplit.Movers.Assignments.Where(a => a.Role == WorkforceRoles.Steward).Sum(a => a.Population);
        int dFarmersFission = fissionSplit.Movers.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population);
        int dBuildersFission = fissionSplit.Movers.Assignments.Where(a => a.Role == WorkforceRoles.Builder).Sum(a => a.Population);
        int sStewardsFission = fissionSplit.Stayers.Assignments.Where(a => a.Role == WorkforceRoles.Steward).Sum(a => a.Population);
        Check(dStewardsFission == 0 && dFarmersFission == 0 && dBuildersFission == 10 && fissionSplit.Movers.Gatherers(50) == 40 && sStewardsFission == 10,
            "WorkforceTransfer.Split converts departing Stewards and Farmers to Gatherer on fission while conserving stayers");

        var immSplit = WorkforceTransfer.Split(baseLedger, 100, 50, daughterFarmersBecomeGatherers: false);
        int dStewardsImm = immSplit.Movers.Assignments.Where(a => a.Role == WorkforceRoles.Steward).Sum(a => a.Population);
        int sStewardsImm = immSplit.Stayers.Assignments.Where(a => a.Role == WorkforceRoles.Steward).Sum(a => a.Population);
        Check(dStewardsImm == 10 && sStewardsImm == 10 && immSplit.Movers.Assigned <= 50,
            "WorkforceTransfer.Split preserves Stewards on immigration");

        // Legacy Fission.SplitWorkforce parity when DemographicModel == LegacyQoLNetGrowth
        var legacySplitWorld = CivicFixture();
        legacySplitWorld.Config.DemographicModel = DemographicModel.LegacyQoLNetGrowth;
        legacySplitWorld.Config.FissionEnabled = true;
        legacySplitWorld.Config.FissionGainThreshold = 0;
        legacySplitWorld.Config.FissionDaughterMargin = 1;
        legacySplitWorld.Config.FissionHousingRelief = 1;
        var parent = legacySplitWorld.Camps[0];
        parent.Population = 800;
        parent.QoL = 0.2;
        parent.PlantFood = 50000;
        parent.Workforce = new PopulationLedger();
        parent.Workforce.Assign(WorkforceRoles.Farmer, 120, 800);
        parent.Workforce.Assign(WorkforceRoles.Steward, 60, 800);
        parent.Workforce.Assign(WorkforceRoles.Builder, 40, 800);
        parent.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        parent.People.CivicExperience[CivicPrecedents.CivicOrganization] = 12.5;
        parent.CivicComplexity = 0.25;
        parent.CivicStratification = 0.60;
        parent.CivicCapital = 50;
        parent.SanitationCapital = 30;
        parent.ImmigrationPolicy = ImmigrationPolicy.Restricted;
        parent.CivicFocus = CivicFocus.Centralized;
        parent.RecentTradeVolume = 12;
        var mem = ContactKnowledge.MemoryOf(legacySplitWorld, parent.People);
        foreach (int t in legacySplitWorld.Map.Radius(parent.Tile, 5)) mem.Explored.Add(t);
        var legacyRecord = new TurnRecord { Turn = legacySplitWorld.Turn, StartDays = legacySplitWorld.Days, Duration = legacySplitWorld.Config.TurnDays };
        legacyRecord.Demographics.Add(new DemographicReport { Camp = parent.Id, StartingPopulation = parent.Population, EndingPopulation = parent.Population });
        Fission.Resolve(legacySplitWorld, legacyRecord, new PhaseExecution(), new ResolutionMetrics());
        Check(legacySplitWorld.Camps.Count == 2, "legacy Fission.Resolve unconditionally founds daughter camp");
        var daughter = legacySplitWorld.Camps.Single(x => x.Id != parent.Id);
        Check(CultivationEconomy.Workers(daughter, WorkforceRoles.Steward) == 0 && CultivationEconomy.Workers(daughter, WorkforceRoles.Farmer) == 0,
            "legacy Fission.SplitWorkforce converts departing Stewards and Farmers to Gatherer");
        Check(daughter.CivicCapital == 0 && daughter.SanitationCapital == 0 && daughter.RecentTradeVolume == 0,
            "fission daughter starts with zero fixed capitals and trade volume");
        Near(daughter.CivicComplexity, 0.125, "fission daughter dampens complexity by 0.50");
        Near(daughter.CivicStratification, 0.15, "fission daughter dampens stratification (0.5*0.6 - 0.15)");
        Check(daughter.ImmigrationPolicy == ImmigrationPolicy.Neutral && daughter.CivicFocus == CivicFocus.Auto,
            "fission daughter resets ImmigrationPolicy and CivicFocus");
        Check(daughter.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration), "fission daughter inherits Society civic precedents");
        Near(daughter.People.CivicExperience[CivicPrecedents.CivicOrganization], 12.5, "fission daughter inherits Society civic experience");
        daughter.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        daughter.People.CivicExperience[CivicPrecedents.CivicOrganization] = 99.0;
        Check(!parent.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization) && Math.Abs(parent.People.CivicExperience[CivicPrecedents.CivicOrganization] - 12.5) < 1e-9,
            "fission daughter Society civic precedents and experience are deep-copied");
    }

    private static void AttractorsRelaxationAndFormulas()
    {
        // Archetype 1: Open rain-fed plains -> Egalitarian P_env < -0.35
        var plains = CivicFixture();
        var pc = plains.Camps[0];
        pc.Population = 800;
        pc.PlantFood = 2000; pc.AnimalFood = 1000; pc.AquaticFood = 500;
        pc.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        var plainsEnv = CivicGovernance.EvaluateEnvironment(plains, pc);
        Check(plainsEnv.EnvironmentalStratification < -0.35,
            "open rain-fed plains has egalitarian attractor P_env < -0.35 (got " + plainsEnv.EnvironmentalStratification + ")");
        Check(plainsEnv.EnvironmentalComplexity > 0 && plainsEnv.EnvironmentalComplexity <= plains.Config.Civic.Tier2ComplexityCeiling,
            "C_env bounded by Tier 2 ceiling");

        // Archetype 2: Circumscribed irrigated valley -> Stratified P_env > +0.35
        var valley = CivicFixture();
        var vc = valley.Camps[0];
        vc.Population = 1400;
        vc.IsVillage = true;
        vc.PlantFood = 250000; vc.AnimalFood = 0; vc.AquaticFood = 0;
        vc.Inventory.Pots = 600;
        vc.Inventory.FarmTools = 50;
        vc.RecentTradeVolume = 400;
        vc.People.NeolithicTechnologies.Add(NeolithicTech.Plough);
        vc.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
        vc.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
        vc.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        vc.Workforce = new PopulationLedger();
        vc.Workforce.Assign(WorkforceRoles.Farmer, 900, vc.Population);
        NeolithicProduction.AddFixedHousing(valley, vc, 200, valley.Days);
        valley.Canals = new List<CanalSegment>();
        valley.IrrigatedFields = new List<IrrigatedField>();
        // Make surrounding ring non-viable desert/peaks so circumscription D2 is high.
        var homeFootprint = new HashSet<int>(valley.Map.Radius(vc.Tile, 1));
        for (int t = 0; t < valley.Tiles.Length; t++)
        {
            if (homeFootprint.Contains(t)) continue;
            valley.Tiles[t].ElevationLevel = 10;
            valley.Tiles[t].FreshwaterSource = FreshwaterSource.None;
            for (int s = 0; s < 4; s++) valley.Tiles[t].Seasons[s].MaxFreshwater = 0;
        }
        int irrAdded = 0;
        foreach (int t in homeFootprint)
        {
            if (!CultivationEconomy.Cultivable(valley.Tiles[t])) continue;
            valley.Tiles[t].CultivationOperator = vc.Id;
            valley.Tiles[t].CultivationCapital = 12.0;
            valley.IrrigatedFields.Add(new IrrigatedField { Tile = t, Operator = vc.Id, Endpoint = t, WaterService = 1.0, Soil = 1.0 });
            valley.Canals.Add(new CanalSegment { Id = 1000 + irrAdded, Operator = vc.Id, Source = vc.Tile, From = vc.Tile, Tile = t, Construction = 1.0 });
            irrAdded++;
        }
        var valleyEnv = CivicGovernance.EvaluateEnvironment(valley, vc);
        Check(valleyEnv.EnvironmentalStratification > 0.35,
            "circumscribed irrigated valley has stratified attractor P_env > +0.35 (got " + valleyEnv.EnvironmentalStratification + ")");

        // Player-ID invariance of EvaluateEnvironment
        int origPlayer = vc.Player;
        vc.Player = 42; vc.People.Player = 42;
        var relabeledEnv = CivicGovernance.EvaluateEnvironment(valley, vc);
        Near(relabeledEnv.D1, valleyEnv.D1, "EvaluateEnvironment D1 is player-ID invariant", 1e-12);
        Near(relabeledEnv.D2, valleyEnv.D2, "EvaluateEnvironment D2 is player-ID invariant", 1e-12);
        Near(relabeledEnv.D3, valleyEnv.D3, "EvaluateEnvironment D3 is player-ID invariant", 1e-12);
        Near(relabeledEnv.D4, valleyEnv.D4, "EvaluateEnvironment D4 is player-ID invariant", 1e-12);
        Near(relabeledEnv.EnvironmentalStratification, valleyEnv.EnvironmentalStratification, "EvaluateEnvironment P_env is player-ID invariant", 1e-12);
        vc.Player = origPlayer; vc.People.Player = origPlayer;

        // Null Resources and empty land/traversable set guards (no exceptions, finite results)
        var degenerate = CivicFixture();
        degenerate.Resources = null;
        var dc = degenerate.Camps[0];
        foreach (int t in degenerate.Map.Radius(dc.Tile, 1))
        {
            degenerate.Tiles[t].Environment = EnvironmentKind.Ocean;
            degenerate.Tiles[t].WaterBody = WaterKind.Saltwater;
            degenerate.Tiles[t].ElevationLevel = 10;
        }
        var degEnv = CivicGovernance.EvaluateEnvironment(degenerate, dc);
        Check(!double.IsNaN(degEnv.D3) && !double.IsNaN(degEnv.D4) && !double.IsNaN(degEnv.EnvironmentalStratification),
            "EvaluateEnvironment handles null Resources and empty land/traversable footprint cleanly");

        // Semigroup dt-invariance for (C, P) relaxation: 1x20y == 4x5y == 20x1y == 7300x1d
        double c0 = 0.12, cStar = 0.72, p0 = -0.35, pStar = 0.55;
        double lc = 0.025, lp = 0.020;
        double c20 = CivicGovernance.Relax(c0, cStar, lc, 20.0);
        double p20 = CivicGovernance.Relax(p0, pStar, lp, 20.0);
        double c5 = c0, p5 = p0;
        for (int i = 0; i < 4; i++) { c5 = CivicGovernance.Relax(c5, cStar, lc, 5.0); p5 = CivicGovernance.Relax(p5, pStar, lp, 5.0); }
        double c1 = c0, p1 = p0;
        for (int i = 0; i < 20; i++) { c1 = CivicGovernance.Relax(c1, cStar, lc, 1.0); p1 = CivicGovernance.Relax(p1, pStar, lp, 1.0); }
        double cd = c0, pd = p0;
        for (int i = 0; i < 7300; i++) { cd = CivicGovernance.Relax(cd, cStar, lc, 1.0 / 365.0); pd = CivicGovernance.Relax(pd, pStar, lp, 1.0 / 365.0); }
        Near(c5, c20, "Complexity relaxation semigroup 4x5y == 1x20y", 1e-12);
        Near(c1, c20, "Complexity relaxation semigroup 20x1y == 1x20y", 1e-12);
        Near(cd, c20, "Complexity relaxation semigroup 7300x1d == 1x20y", 1e-11);
        Near(p5, p20, "Stratification relaxation semigroup 4x5y == 1x20y", 1e-12);
        Near(p1, p20, "Stratification relaxation semigroup 20x1y == 1x20y", 1e-12);
        Near(pd, p20, "Stratification relaxation semigroup 7300x1d == 1x20y", 1e-11);

        // Semigroup dt-invariance for RecentTradeVolume EMA via CivicGovernance.Close
        double RunTradeEma(int steps, double stepYears)
        {
            var w = CivicFixture();
            var camp = w.Camps[0];
            camp.RecentTradeVolume = 50.0;
            for (int i = 0; i < steps; i++)
            {
                // Constant annual flow of 120 person-years/yr -> 120 * stepYears per step.
                // Cloth = 20 person-years/unit, so qty = 6 * stepYears on both legs => V_k = 120 * stepYears.
                var rec = new TurnRecord();
                rec.Trades.Add(new TradeRecord
                {
                    Requester = camp.Id, Responder = camp.Id + 999,
                    Want = TradeGood.Cloth, WantQuantity = 6.0 * stepYears, Delivered = 0.5,
                    Give = TradeGood.Cloth, GiveQuantity = 6.0 * stepYears, GiveDelivered = 0.5
                });
                CivicGovernance.Close(w, rec, stepYears);
            }
            return camp.RecentTradeVolume;
        }
        double ema20 = RunTradeEma(1, 20.0);
        double ema5 = RunTradeEma(4, 5.0);
        double ema1 = RunTradeEma(20, 1.0);
        Near(ema5, ema20, "Trade EMA semigroup 4x5y == 1x20y", 1e-12);
        Near(ema1, ema20, "Trade EMA semigroup 20x1y == 1x20y", 1e-12);
        // TradeRecord.WantQuantity/GiveQuantity are already delivered quantities: Delivered=0.5 must NOT double-scale!
        var tHalf = new TradeRecord { Want = TradeGood.Cloth, WantQuantity = 10, Delivered = 0.5, Give = TradeGood.Pots, GiveQuantity = 40, GiveDelivered = 0.25 };
        var tFull = new TradeRecord { Want = TradeGood.Cloth, WantQuantity = 10, Delivered = 1.0, Give = TradeGood.Pots, GiveQuantity = 40, GiveDelivered = 1.0 };
        Near(CivicGovernance.TradeRecordVolumePersonYears(plains.Config, tHalf), 200.0, "TradeRecord volume does not re-multiply by Delivered", 1e-12);
        Near(CivicGovernance.TradeRecordVolumePersonYears(plains.Config, tHalf), CivicGovernance.TradeRecordVolumePersonYears(plains.Config, tFull),
            "TradeRecordVolumePersonYears depends strictly on delivered WantQuantity/GiveQuantity", 1e-12);

        // Steering shifts & zero-cliff guarantee (Auto == exact 0.0)
        var cfg = plains.Config;
        Near(CivicGovernance.SteeringOffset(cfg, FissionPolicy.Neutral, ImmigrationPolicy.Neutral, CivicFocus.Auto), 0.0, "Auto+Neutral steering is exact 0.0", 1e-15);
        Near(CivicGovernance.SteeringOffset(cfg, FissionPolicy.Encourage, ImmigrationPolicy.Open, CivicFocus.Communal), -0.75, "Max egalitarian steering is -0.75", 1e-12);
        Near(CivicGovernance.SteeringOffset(cfg, FissionPolicy.Discourage, ImmigrationPolicy.Restricted, CivicFocus.Centralized), 0.75, "Max stratified steering is +0.75", 1e-12);
        Check(CivicGovernance.EffectiveFocusLabel(new Camp { CivicFocus = CivicFocus.Auto, LastEnvironmentalStratification = -0.20 }) == "Communal", "Auto display label Communal <= -0.15");
        Check(CivicGovernance.EffectiveFocusLabel(new Camp { CivicFocus = CivicFocus.Auto, LastEnvironmentalStratification = 0.0 }) == "Hybrid", "Auto display label Hybrid in middle");
        Check(CivicGovernance.EffectiveFocusLabel(new Camp { CivicFocus = CivicFocus.Auto, LastEnvironmentalStratification = 0.20 }) == "Centralized", "Auto display label Centralized >= +0.15");
        Check(CivicGovernance.EffectiveFocusLabel(new Camp { CivicFocus = CivicFocus.Centralized, LastEnvironmentalStratification = -0.80 }) == "Centralized", "Explicit focus overrides display label");

        // Spot checks against Docs/CivicGovernance.md §5 Choices #3, #4, #5
        Near(CivicGovernance.CrowdingMitigationFor(cfg, 1.0, -1.0, 1.0), 0.100, "M_c(C=1,P=-1,I=1) = 0.100", 1e-12);
        Near(CivicGovernance.CrowdingMitigationFor(cfg, 1.0, 0.0, 1.0), 0.075, "M_c(C=1,P=0,I=1) = 0.075", 1e-12);
        Near(CivicGovernance.CrowdingMitigationFor(cfg, 1.0, 1.0, 1.0), 0.050, "M_c(C=1,P=+1,I=1) = 0.050 floor", 1e-12);
        Near(CivicGovernance.DiseaseMitigationFor(cfg, 1.0, -1.0, 1.0), 0.340, "M_d(C=1,P=-1,I=1) = 0.340", 1e-12);
        Near(CivicGovernance.DiseaseMitigationFor(cfg, 1.0, 0.0, 1.0), 0.265, "M_d(C=1,P=0,I=1) = 0.265", 1e-12);
        Near(CivicGovernance.DiseaseMitigationFor(cfg, 1.0, 1.0, 1.0), 0.190, "M_d(C=1,P=+1,I=1) = 0.190", 1e-12);
        Near(CivicGovernance.QoLEqualityMultiplierFor(cfg, 1.0, -1.0, 1.0), 1.100, "Q_civic(C=1,P=-1,I=1) = 1.100", 1e-12);
        Near(CivicGovernance.QoLEqualityMultiplierFor(cfg, 1.0, 0.0, 1.0), 1.000, "Q_civic(C=1,P=0,I=1) = 1.000", 1e-12);
        Near(CivicGovernance.QoLEqualityMultiplierFor(cfg, 1.0, 1.0, 1.0), 0.920, "Q_civic(C=1,P=+1,I=1) = 0.920", 1e-12);
        Near(CivicGovernance.AlignmentFriction(cfg, 0.5, -0.5), 1.35, "AlignmentFriction(|dP|=1) = 1.35", 1e-12);

        double sEgal1k = CivicGovernance.RequiredStewards(cfg, 1000, 0.75, -1.0, false, -1.0);
        double sStrat1k = CivicGovernance.RequiredStewards(cfg, 1000, 0.75, 1.0, false, 1.0);
        double sEgal10kNo3A = CivicGovernance.RequiredStewards(cfg, 10000, 0.75, -1.0, false, -1.0);
        double sStrat10kNo3A = CivicGovernance.RequiredStewards(cfg, 10000, 0.75, 1.0, false, 1.0);
        double sEgal10k3A = CivicGovernance.RequiredStewards(cfg, 10000, 0.75, -1.0, true, -1.0);
        Check(sEgal1k < sStrat1k, "Egalitarian requires fewer Stewards at N=1000");
        Check(sEgal10kNo3A > sStrat10kNo3A, "Without Tier 3A, Egalitarian Steward scaling exceeds Stratified at N=10000");
        Check(sEgal10k3A < sStrat10kNo3A, "Tier 3A flattens Egalitarian superlinear Steward scaling");

        var bonusCamp = new Camp { Population = 1000, CivicComplexity = 1.0, CivicStratification = 1.0, LastEnvironmentalStratification = 1.0, CivicCapital = 500 };
        bonusCamp.Workforce.Assign(WorkforceRoles.Steward, 500, 1000);
        Near(CivicGovernance.PublicWorksMobilizationMultiplier(plains, bonusCamp), 1.35, "Stratified PublicWorksMobilizationMultiplier = 1.35", 1e-12);
        Near(CivicGovernance.GranaryStorageMultiplier(plains, bonusCamp), 1.50, "Stratified GranaryStorageMultiplier = 1.50", 1e-12);
        bonusCamp.CivicStratification = -1.0; bonusCamp.LastEnvironmentalStratification = -1.0;
        Near(CivicGovernance.PublicWorksMobilizationMultiplier(plains, bonusCamp), 1.00, "Egalitarian PublicWorksMobilizationMultiplier = 1.00", 1e-12);
        Near(CivicGovernance.GranaryStorageMultiplier(plains, bonusCamp), 1.00, "Egalitarian GranaryStorageMultiplier = 1.00", 1e-12);

        Near(CivicGovernance.ExitMismatch(new Camp { CivicComplexity = 0, CivicStratification = 1, LastEnvironmentalStratification = -0.5, LastCircumscription = 0.15 }), 0.0, "ExitMismatch is 0 at C=0", 1e-12);
        Near(CivicGovernance.ExitMismatch(new Camp { CivicComplexity = 1, CivicStratification = 1, LastEnvironmentalStratification = -0.5, LastCircumscription = 0.15 }), 1.275, "ExitMismatch matches 1 * 1.5 * 0.85 = 1.275", 1e-12);
    }

    private static void PrecedentsFissionAndAbandonment()
    {
        // Tier 1: mobile camp accrues exact 0 even with StationaryYears > 0; Paleolithic accrues 0.
        var mobileW = CivicFixture();
        var mc = mobileW.Camps[0];
        mc.Population = 1000; mc.StationaryTurns = 0; mc.StationaryYears = 20;
        NeolithicProduction.AddFixedHousing(mobileW, mc, 200, mobileW.Days);
        CivicGovernance.Close(mobileW, new TurnRecord(), 20.0);
        Check(!mc.People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double mobExp) || mobExp == 0,
            "mobile camp (StationaryTurns == 0) accrues exact 0 Tier-1 practice");

        var paleoW = CivicFixture();
        var plc = paleoW.Camps[0];
        plc.People.Progression.Era = Era.Paleolithic;
        plc.Population = 1000; plc.StationaryTurns = 5; plc.StationaryYears = 100;
        CivicGovernance.Close(paleoW, new TurnRecord(), 20.0);
        Check(!plc.People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double palExp) || palExp == 0,
            "Paleolithic camp accrues exact 0 Tier-1 practice");

        // Tier 1 analytical permanence ramp integral: 2x20y == 8x5y == 40x1y over [0, 40] yr.
        double RunTier1Ramp(int steps, double dt)
        {
            var w = CivicFixture();
            var c = w.Camps[0];
            c.Population = 1000; c.StationaryTurns = 1; c.StationaryYears = 0;
            NeolithicProduction.AddFixedHousing(w, c, 250, w.Days); // 250 * 4 = 1000 => H = 1.0 => N_{s,0} = 1782
            for (int i = 0; i < steps; i++)
            {
                c.StationaryYears += dt;
                CivicGovernance.Close(w, new TurnRecord(), dt);
            }
            return c.People.CivicExperience[CivicPrecedents.CeremonialIntegration];
        }
        double r20 = RunTier1Ramp(2, 20.0);
        double r5 = RunTier1Ramp(8, 5.0);
        double r1 = RunTier1Ramp(40, 1.0);
        double expectedRamp = 20.0 * ((1000.0 / 1782.0 - 0.25) / 0.50);
        Near(r20, expectedRamp, "Tier-1 analytical permanence integral matches exact closed form (12.446689)", 1e-10);
        Near(r5, r20, "Tier-1 permanence ramp semigroup 8x5y == 2x20y", 1e-12);
        Near(r1, r20, "Tier-1 permanence ramp semigroup 40x1y == 2x20y", 1e-12);

        // Tier 1 unlocks in Step 2 and lifts C_max BEFORE Step 3 relaxes CivicComplexity > 0 in the same turn.
        var unlockW = CivicFixture();
        var uc = unlockW.Camps[0];
        uc.Population = 1000; uc.StationaryTurns = 5; uc.StationaryYears = 100;
        NeolithicProduction.AddFixedHousing(unlockW, uc, 250, unlockW.Days);
        uc.People.CivicExperience[CivicPrecedents.CeremonialIntegration] = 39.0;
        var unlockRec = new TurnRecord();
        CivicGovernance.Close(unlockW, unlockRec, 20.0);
        Check(uc.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration), "Tier 1 unlocks when crossing threshold");
        Check(uc.CivicComplexity > 0 && uc.CivicComplexity <= unlockW.Config.Civic.Tier1ComplexityCeiling,
            "Tier 1 unlock lifts C_max before Step 3 relaxes CivicComplexity > 0 in the same turn");
        Check(unlockRec.CivicReports != null && unlockRec.CivicReports.Count == 1 && unlockRec.CivicReports[0].Camp == uc.Id,
            "CivicGovernance.Close populates TurnRecord.CivicReports when enabled");

        // Tier 2: requires Stewards; Builder production receipts contribute to accrual.
        var t2W = CivicFixture();
        var t2c = t2W.Camps[0];
        t2c.Population = 1500; t2c.StationaryTurns = 5; t2c.StationaryYears = 100;
        t2c.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        CivicGovernance.Close(t2W, new TurnRecord(), 20.0);
        Check(!t2c.People.CivicExperience.TryGetValue(CivicPrecedents.CivicOrganization, out double t2Zero) || t2Zero == 0,
            "Tier 2 accrues 0 when stewards == 0");
        t2c.Workforce.Assign(WorkforceRoles.Steward, 20, t2c.Population);
        t2c.People.CivicExperience[CivicPrecedents.CivicOrganization] = 70.0;
        var t2Rec = new TurnRecord();
        t2Rec.Production.Add(new ProductionReceipt { Camp = t2c.Id, Role = WorkforceRoles.Builder, WorkerIntervals = 800 });
        CivicGovernance.Close(t2W, t2Rec, 20.0);
        Check(t2c.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization), "Tier 2 unlocks with Stewards and crowding/Builder work-years");
        Near(CivicGovernance.MaxComplexity(t2W, t2c), t2W.Config.Civic.Tier2ComplexityCeiling, "Tier 2 raises MaxComplexity to 0.75");

        // Tier 3A: HasLocalClay (surface & deposit) + dry-Clay / Pottery / trade non-deadlocking gate.
        var clayW = CivicFixture();
        int tc = clayW.Tiles.Length;
        clayW.Resources = new WorldResourceState
        {
            MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash,
            Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[tc], History = new ushort[tc],
            Host = new byte[tc], Marine = new byte[tc], Stone = new ushort[tc], Clay = new ushort[tc], Wood = new ushort[tc], WoodRecoveryPerYear = .05
        };
        var cc = clayW.Camps[0];
        cc.Population = 1500; cc.PlantFood = 200000; cc.Inventory.Pots = 0;
        cc.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        cc.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        cc.Workforce.Assign(WorkforceRoles.Steward, 40, cc.Population);
        Check(!CivicGovernance.HasLocalClay(clayW, cc), "HasLocalClay is false when surface and deposit clay are absent");
        CivicGovernance.Close(clayW, new TurnRecord(), 20.0);
        Check(!cc.People.CivicExperience.TryGetValue(CivicPrecedents.TokensAndAccounting, out double noClayExp) || noClayExp == 0,
            "Tier 3A accrues 0 when Resources != null and Pottery, Pots, and local Clay are all absent");
        clayW.Resources.Clay[cc.Tile] = 50;
        Check(CivicGovernance.HasLocalClay(clayW, cc), "HasLocalClay detects SurfaceAccessible Clay");
        CivicGovernance.Close(clayW, new TurnRecord(), 20.0);
        Check(cc.People.CivicExperience.TryGetValue(CivicPrecedents.TokensAndAccounting, out double surfClayExp) && surfClayExp > 0,
            "Tier 3A accrues via dry SurfaceAccessible Clay without Pottery or trade");
        clayW.Resources.Clay[cc.Tile] = 0;
        clayW.Resources.Bodies = new[]
        {
            new DepositBody
            {
                Id = 1, Quantity = 1000, InitialQuantity = 1000, SiteExposureWeightTotal = 10000.0 * 10000.0,
                Concentration = MineralConcentration.Deposit, Quality = 80,
                Components = new[] { new DepositComponent(MaterialId.Clay, 10000) { AccessibleQuantity = 500, InitialAccessibleQuantity = 500 } },
                Sites = new[] { new DepositSite { Tile = cc.Tile, Share = 10000, Exposure = 10000, Burial = BurialDepth.Surface } }
            }
        };
        clayW.MaterialIndex = null;
        Check(CivicGovernance.HasLocalClay(clayW, cc), "HasLocalClay detects DepositSite Clay via AccessibleAt");

        var t3aW = CivicFixture();
        t3aW.Resources = null; // null Resources also allowed for synthetic fixtures
        var t3ac = t3aW.Camps[0];
        t3ac.Population = 1500; t3ac.PlantFood = 200000;
        t3ac.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        t3ac.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        t3ac.Workforce.Assign(WorkforceRoles.Steward, 40, t3ac.Population);
        CivicGovernance.Close(t3aW, new TurnRecord(), 20.0);
        double dryGain = t3ac.People.CivicExperience[CivicPrecedents.TokensAndAccounting];
        Check(dryGain > 0, "Tier 3A accrues for Player 0 without Pottery or trade (no arid deadlock)");
        t3ac.People.NeolithicTechnologies.Add(NeolithicTech.Pottery);
        t3ac.People.CivicExperience[CivicPrecedents.TokensAndAccounting] = 0;
        CivicGovernance.Close(t3aW, new TurnRecord(), 20.0);
        double potGain = t3ac.People.CivicExperience[CivicPrecedents.TokensAndAccounting];
        Near(potGain, dryGain * 1.25, "Pottery gives exact 1.25x multiplier on Tier 3A accrual", 1e-10);
        t3ac.People.CivicExperience[CivicPrecedents.TokensAndAccounting] = 115.0;
        CivicGovernance.Close(t3aW, new TurnRecord(), 20.0);
        Check(t3ac.People.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting), "Tier 3A unlocks at threshold");
        Near(CivicGovernance.MaxComplexity(t3aW, t3ac), t3aW.Config.Civic.Tier3ComplexityCeiling, "Tier 3A raises MaxComplexity to 1.00");

        // Tier 3B: gated on SanitationEnabled && Tier 2 && Construction && N > 1200.
        var t3bDisabled = CivicFixture(sanitation: false);
        var bdc = t3bDisabled.Camps[0];
        bdc.Population = 3000; bdc.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration); bdc.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        bdc.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        bdc.Workforce.Assign(WorkforceRoles.Steward, 50, bdc.Population);
        CivicGovernance.Close(t3bDisabled, new TurnRecord(), 20.0);
        Check(!bdc.People.CivicExperience.ContainsKey(CivicPrecedents.PlannedQuartersAndDrainage), "Tier 3B does not accrue when SanitationEnabled=false");

        var t3bW = CivicFixture(sanitation: true);
        var bc = t3bW.Camps[0];
        bc.Population = 3000; bc.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration); bc.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        bc.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        bc.Workforce.Assign(WorkforceRoles.Steward, 50, bc.Population);
        bc.People.CivicExperience[CivicPrecedents.PlannedQuartersAndDrainage] = 95.0;
        CivicGovernance.Close(t3bW, new TurnRecord(), 20.0);
        Check(bc.People.HasCivicPrecedent(CivicPrecedents.PlannedQuartersAndDrainage), "Tier 3B unlocks when SanitationEnabled + Tier 2 + Construction");

        // Inert RecordTradeDiffusion when PrecedentDiffusionPerTrade == 0
        var recv = new Society { Id = 10 };
        var part = new Society { Id = 11 };
        part.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        CivicGovernance.RecordTradeDiffusion(t3bW, recv, part, 500.0);
        Check(recv.CivicExperience.Count == 0, "RecordTradeDiffusion is a strict no-op when PrecedentDiffusionPerTrade == 0");

        // InitializeDaughterView legacy guard: MUST set (0, 0), NEVER -0.15 when Civic disabled!
        var legW = CivicFixture(enableCivic: false);
        var legParent = legW.Camps[0]; legParent.CivicStratification = 0.0;
        var legDaughter = new Camp { Id = 999, CivicCapital = 10, SanitationCapital = 5, CivicStratification = 0.4 };
        CivicGovernance.InitializeDaughterView(legW, legParent, legDaughter);
        Check(legDaughter.CivicComplexity == 0 && legDaughter.CivicStratification == 0 && legDaughter.CivicCapital == 0 && legDaughter.SanitationCapital == 0,
            "InitializeDaughterView keeps C=0, P=0 (never -0.15) when Civic governance is disabled");

        // SocialRelocationForecastCache.Daughter applies InitializeDaughterView
        var relocW = CivicFixture();
        var rc = relocW.Camps[0];
        rc.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        rc.CivicComplexity = 0.60; rc.CivicStratification = 0.50; rc.CivicCapital = 100; rc.SanitationCapital = 40;
        rc.ImmigrationPolicy = ImmigrationPolicy.Restricted; rc.CivicFocus = CivicFocus.Centralized; rc.RecentTradeVolume = 88;
        var relocCache = new SocialRelocationForecastCache(relocW, relocW.Camps);
        var daughterState = relocCache.Daughter(rc, 50, rc.Tile, new CampInventory(), new PopulationLedger(), 100, 0, 0, default);
        Near(daughterState.View.CivicComplexity, 0.30, "SocialRelocation Daughter dampens C by 0.50");
        Near(daughterState.View.CivicStratification, 0.10, "SocialRelocation Daughter dampens P (0.5*0.5 - 0.15)");
        Check(daughterState.View.CivicCapital == 0 && daughterState.View.SanitationCapital == 0 && daughterState.View.RecentTradeVolume == 0 &&
              daughterState.View.ImmigrationPolicy == ImmigrationPolicy.Neutral && daughterState.View.CivicFocus == CivicFocus.Auto,
            "SocialRelocation Daughter zeroes capitals/trade and resets policies");

        // TurnResolver.Move abandons fixed CivicCapital & SanitationCapital on ANY move (both non-village and village)
        foreach (bool isVillage in new[] { false, true })
        {
            var moveW = CivicFixture();
            var mcamp = moveW.Camps[0];
            mcamp.IsVillage = isVillage;
            mcamp.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
            mcamp.CivicComplexity = 0.25; mcamp.CivicStratification = 0.40;
            mcamp.CivicCapital = 80; mcamp.SanitationCapital = 35; mcamp.SanitationWaterRatio = 0.9;
            int dest = moveW.Map.Neighbors(mcamp.Tile).First(t => moveW.CanEnter(t, false));
            var submitted = new Dictionary<int, List<Order>> { [mcamp.Player] = new List<Order> { new Order(OrderKind.Move, mcamp.Id, destination: dest) } };
            new TurnResolver().Resolve(moveW, submitted);
            Check(mcamp.Tile == dest, $"camp moved to dest (isVillage={isVillage})");
            Check(mcamp.CivicCapital == 0 && mcamp.SanitationCapital == 0 && mcamp.SanitationWaterRatio == 0,
                $"TurnResolver.Move zeroes fixed CivicCapital/SanitationCapital/SanitationWaterRatio (isVillage={isVillage})");
            Check(mcamp.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration), "TurnResolver.Move retains Society precedents");
        }
    }

    private static void OrdersMultiPolicyAndPlanningDraft()
    {
        // Legacy world: ImmigrationPolicy & CivicFocus rejected by OrderRules and omitted from LegalActions.
        var legW = CivicFixture(enableCivic: false);
        var legCamp = legW.Camps[0];
        var legObs = new AgentObservationBuilder().Build(legW, legCamp.Player);
        Check(!legObs.CivicGovernanceEnabled && !legObs.SanitationEnabled, "Legacy observation disables civic flags");
        Check(OrderRules.Validate(legObs, new Order(OrderKind.ImmigrationPolicy, legCamp.Id, target: (int)ImmigrationPolicy.Open)) != null,
            "Legacy OrderRules rejects ImmigrationPolicy");
        Check(OrderRules.Validate(legObs, new Order(OrderKind.CivicFocus, legCamp.Id, target: (int)CivicFocus.Communal)) != null,
            "Legacy OrderRules rejects CivicFocus");
        var legMask = LegalActions.Build(legObs).Single(m => m.Entity == legCamp.Id);
        Check(legMask.Actions.All(a => a.Kind != OrderKind.ImmigrationPolicy && a.Kind != OrderKind.CivicFocus),
            "Legacy LegalActions omits ImmigrationPolicy and CivicFocus");

        // Civic-enabled world: LegalActions & OrderRules contract
        var w = CivicFixture();
        var c = w.Camps[0];
        c.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        c.CivicComplexity = 0.25; c.CivicStratification = 0.20; c.CivicCapital = 30; c.SanitationCapital = 15;
        c.FissionPolicy = FissionPolicy.Discourage; c.ImmigrationPolicy = ImmigrationPolicy.Open; c.CivicFocus = CivicFocus.Communal;
        // Also add a hunter to verify hunters cannot receive policy orders.
        var hunter = new Hunter { Id = w.NextId++, Player = c.Player, SocietyId = c.SocietyId, SourceCamp = c.Id, Tile = c.Tile, Population = 4 };
        w.Hunters.Add(hunter);
        Ownership.Bind(w);

        var obs = new AgentObservationBuilder().Build(w, c.Player);
        var masks = LegalActions.Build(obs);
        var campMask = masks.Single(m => m.Entity == c.Id);
        var hunterMask = masks.Single(m => m.Entity == hunter.Id);
        Check(campMask.Actions.Count(a => a.Kind == OrderKind.ImmigrationPolicy) == 3, "LegalActions enumerates 3 ImmigrationPolicy orders for settlement");
        Check(campMask.Actions.Count(a => a.Kind == OrderKind.CivicFocus) == 3, "LegalActions enumerates 3 CivicFocus orders for settlement");
        Check(hunterMask.Actions.All(a => a.Kind != OrderKind.ImmigrationPolicy && a.Kind != OrderKind.CivicFocus), "Hunters have no Civic policy orders");

        foreach (int target in new[] { -1, 0, 1, 2, 3 })
        {
            var imm = new Order(OrderKind.ImmigrationPolicy, c.Id, target: target);
            var foc = new Order(OrderKind.CivicFocus, c.Id, target: target);
            bool valid = target >= 0 && target <= 2;
            Check((OrderRules.Validate(obs, imm) == null) == valid && campMask.Allows(imm) == valid, "ImmigrationPolicy validation/mask parity for target " + target);
            Check((OrderRules.Validate(obs, foc) == null) == valid && campMask.Allows(foc) == valid, "CivicFocus validation/mask parity for target " + target);
        }
        // Reject smuggled Destination / Population / Economy / hunter entity
        Check(OrderRules.Validate(obs, new Order(OrderKind.ImmigrationPolicy, c.Id, target: 1, destination: c.Tile)) != null, "reject ImmigrationPolicy with Destination");
        Check(OrderRules.Validate(obs, new Order(OrderKind.CivicFocus, c.Id, target: 1, population: 1)) != null, "reject CivicFocus with Population");
        Check(OrderRules.Validate(obs, new Order(OrderKind.CivicFocus, c.Id, target: 1) { Economy = EconomicIntent.From(c) }) != null, "reject CivicFocus with Economy");
        Check(OrderRules.Validate(obs, new Order(OrderKind.ImmigrationPolicy, hunter.Id, target: 1)) != null, "reject ImmigrationPolicy for hunter");

        // EconomicPlanner.Preview preserves all 11 persisted civic fields + FissionPolicy while keeping IsVillage == false
        c.IsVillage = true;
        c.SanitationWaterDelivered = 9; c.ActiveSanitationCapital = 7; c.UseActiveSanitationCapital = true;
        var preview = EconomicPlanner.Preview(c, EconomicIntent.From(c), c.Population);
        Check(!preview.IsVillage, "EconomicPlanner.Preview preserves IsVillage == false");
        Check(preview.FissionPolicy == c.FissionPolicy && preview.ImmigrationPolicy == c.ImmigrationPolicy && preview.CivicFocus == c.CivicFocus &&
              preview.CivicComplexity == c.CivicComplexity && preview.CivicStratification == c.CivicStratification &&
              preview.CivicCapital == c.CivicCapital && preview.SanitationCapital == c.SanitationCapital &&
              !preview.UseActiveSanitationCapital && preview.ActiveSanitationCapital == 0 && preview.SanitationWaterDelivered == 0,
            "EconomicPlanner.Preview copies all persisted civic fields + FissionPolicy and clears transients");
        c.SanitationWaterDelivered = 0; c.ActiveSanitationCapital = 0; c.UseActiveSanitationCapital = false;

        // Multi-policy TurnPlanner.Prepare + TurnResolver.Resolve + submission-order permutation invariance
        var wA = CivicFixture();
        var wB = SaveCodec.Load(SaveCodec.Save(wA));
        var campA = wA.Camps[0];
        var multiA = new List<Order>
        {
            new Order(OrderKind.NoOp, campA.Id),
            new Order(OrderKind.FissionPolicy, campA.Id, target: (int)FissionPolicy.Discourage),
            new Order(OrderKind.ImmigrationPolicy, campA.Id, target: (int)ImmigrationPolicy.Restricted),
            new Order(OrderKind.CivicFocus, campA.Id, target: (int)CivicFocus.Centralized)
        };
        var multiB = multiA.AsEnumerable().Reverse().ToList();
        var prepA = TurnPlanner.Prepare(wA, new Dictionary<int, List<Order>> { [campA.Player] = multiA });
        var prepB = TurnPlanner.Prepare(wB, new Dictionary<int, List<Order>> { [campA.Player] = multiB });
        Check(prepA.Rejections.Count == 0 && prepB.Rejections.Count == 0, "multi-policy orders (NoOp + Fission + Immigration + CivicFocus) accepted with 0 rejections");
        Check(prepA.OrderReceipts.SequenceEqual(prepB.OrderReceipts), "multi-policy order receipts are invariant to submission order");
        Check(campA.FissionPolicy == FissionPolicy.Neutral && campA.ImmigrationPolicy == ImmigrationPolicy.Neutral && campA.CivicFocus == CivicFocus.Auto,
            "TurnPlanner.Prepare overlays policies without mutating authoritative Camp");
        new TurnResolver().Resolve(wA, prepA);
        new TurnResolver().Resolve(wB, prepB);
        Check(campA.FissionPolicy == FissionPolicy.Discourage && campA.ImmigrationPolicy == ImmigrationPolicy.Restricted && campA.CivicFocus == CivicFocus.Centralized,
            "TurnResolver commits all 3 policy OrderKinds");
        Check(SaveCodec.Fingerprint(wA) == SaveCodec.Fingerprint(wB), "multi-policy resolution is deterministic under order permutation");

        // Duplicate same-kind policy rejection
        foreach (var dup in new[]
        {
            new[] { new Order(OrderKind.ImmigrationPolicy, campA.Id, target: 1), new Order(OrderKind.ImmigrationPolicy, campA.Id, target: 2) },
            new[] { new Order(OrderKind.CivicFocus, campA.Id, target: 1), new Order(OrderKind.CivicFocus, campA.Id, target: 2) },
            new[] { new Order(OrderKind.FissionPolicy, campA.Id, target: 1), new Order(OrderKind.FissionPolicy, campA.Id, target: 2) }
        })
        {
            var dupPrep = TurnPlanner.Prepare(wA, new Dictionary<int, List<Order>> { [campA.Player] = dup.ToList() });
            Check(dupPrep.Rejections.Any(r => r.Contains("conflicting orders")), "duplicate policy orders rejected as conflicting (" + dup[0].Kind + ")");
        }

        // PlanningDraft reuse vs policy invalidation
        var draftW = CivicFixture();
        var dc = draftW.Camps[0];
        dc.EconomyAutomationTurns = -1;
        dc.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        string beforeDraft = SaveCodec.Fingerprint(draftW);
        var draft = TurnPlanner.Draft(draftW, dc.Player);
        Check(SaveCodec.Fingerprint(draftW) == beforeDraft, "TurnPlanner.Draft does not mutate authoritative world");
        Check(draft.Intents.ContainsKey(dc.Id), "Draft caches automated intent for camp");

        // Case 1: matching policies -> reused
        TurnDiagnostics.For(draftW).DraftIntentsReused = 0;
        TurnDiagnostics.For(draftW).DraftIntentsRecomputed = 0;
        var prepReuse = TurnPlanner.Prepare(draftW, new Dictionary<int, List<Order>>(), null, null, draft);
        Check(TurnDiagnostics.For(draftW).DraftIntentsReused == 1 && TurnDiagnostics.For(draftW).DraftIntentsRecomputed == 0,
            "PlanningDraft reuses intent when all 3 policies match");

        // Case 2: changed policy (each of FissionPolicy, ImmigrationPolicy, CivicFocus) -> invalidated & recomputed, matching draft: null
        foreach (var policyOrder in new[]
        {
            new Order(OrderKind.FissionPolicy, dc.Id, target: (int)FissionPolicy.Encourage),
            new Order(OrderKind.ImmigrationPolicy, dc.Id, target: (int)ImmigrationPolicy.Open),
            new Order(OrderKind.CivicFocus, dc.Id, target: (int)CivicFocus.Centralized)
        })
        {
            TurnDiagnostics.For(draftW).DraftIntentsReused = 0;
            TurnDiagnostics.For(draftW).DraftIntentsRecomputed = 0;
            var sub = new Dictionary<int, List<Order>> { [dc.Player] = new List<Order> { policyOrder } };
            var withDraft = TurnPlanner.Prepare(draftW, sub, null, null, draft);
            Check(TurnDiagnostics.For(draftW).DraftIntentsRecomputed == 1 && TurnDiagnostics.For(draftW).DraftIntentsReused == 0,
                "PlanningDraft invalidates and recomputes when " + policyOrder.Kind + " changes");
            Check(SaveCodec.Fingerprint(draftW) == beforeDraft, "Prepare with policy change does not mutate authoritative world");
            var withoutDraft = TurnPlanner.Prepare(draftW, sub, null, null, null);
            Check(withDraft.OrderReceipts.SequenceEqual(withoutDraft.OrderReceipts),
                "recomputed Prepare matches Prepare(draft: null) bit-for-bit on " + policyOrder.Kind);
        }

        // Manual Steward economy order accepted by TurnPlanner.Prepare when StewardAvailable
        var stewW = CivicFixture();
        var sc = stewW.Camps[0];
        sc.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        var stewEcon = EconomicIntent.From(sc);
        stewEcon.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Steward, Population = 5 } };
        var stewPrep = TurnPlanner.Prepare(stewW, new Dictionary<int, List<Order>>
        {
            [sc.Player] = new List<Order> { new Order(OrderKind.Economy, sc.Id) { Economy = stewEcon } }
        });
        Check(stewPrep.Rejections.Count == 0, "TurnPlanner.Prepare accepts manual Steward assignment after CeremonialIntegration");
    }

    private static void ObservationsAndPolicy()
    {
        var w = CivicFixture(sanitation: true);
        var own = w.Camps[0];
        own.Population = 600;
        own.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        own.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        own.People.CivicPrecedents.Add(CivicPrecedents.PlannedQuartersAndDrainage);
        own.Workforce.Assign(WorkforceRoles.Steward, 15, own.Population);
        own.CivicComplexity = 0.55;
        own.CivicStratification = -0.40;
        own.LastEnvironmentalComplexity = 0.62;
        own.LastEnvironmentalStratification = -0.45;
        own.CivicCapital = 70;
        own.SanitationCapital = 60;
        own.SanitationWaterRatio = 0.85;
        own.ImmigrationPolicy = ImmigrationPolicy.Open;
        own.CivicFocus = CivicFocus.Communal;

        // Place visible rival camp with distinct civic values
        int rivalTile = w.Map.Neighbors(own.Tile).First(t => w.CanEnter(t, false));
        var rivalSoc = new Society { Id = w.NextId++, Player = 1 };
        rivalSoc.CivicPrecedents.Add(CivicPrecedents.TokensAndAccounting);
        rivalSoc.CivicExperience[CivicPrecedents.TokensAndAccounting] = 77.77;
        w.Societies.Add(rivalSoc);
        var rival = new Camp
        {
            Id = w.NextId++, Tile = rivalTile, Population = 300,
            CivicComplexity = 0.91, CivicStratification = 0.87,
            CivicCapital = 1234.5, SanitationCapital = 678.9,
            ImmigrationPolicy = ImmigrationPolicy.Restricted, CivicFocus = CivicFocus.Centralized
        };
        rival.Bind(rivalSoc);
        w.Camps.Add(rival);
        Ownership.Bind(w);

        string fpBefore = SaveCodec.Fingerprint(w);
        var obs = new AgentObservationBuilder().Build(w, own.Player);
        string json = ObservationContract.Serialize(obs, own.People.Mandate);
        var dto = ObservationContract.Deserialize(json);

        Check(AgentObservation.SchemaVersion == 23 && dto.Schema == 23, "SchemaVersion is 23 on observation and DTO");
        Check(obs.CivicGovernanceEnabled && obs.SanitationEnabled && dto.CivicGovernanceEnabled && dto.SanitationEnabled,
            "observation and DTO expose CivicGovernanceEnabled and SanitationEnabled");
        var sd = dto.Settlements.Single(s => s.Id == own.Id);
        Check(sd.StewardEnabled && sd.StewardPopulation == 15, "DTO roundtrips StewardEnabled and StewardPopulation");
        Near(sd.CivicComplexity, 0.55, "DTO roundtrips CivicComplexity");
        Near(sd.CivicStratification, -0.40, "DTO roundtrips CivicStratification");
        Near(sd.LastEnvironmentalComplexity, 0.62, "DTO roundtrips LastEnvironmentalComplexity");
        Near(sd.LastEnvironmentalStratification, -0.45, "DTO roundtrips LastEnvironmentalStratification");
        Near(sd.CivicCapital, 70, "DTO roundtrips CivicCapital");
        Near(sd.CivicCoverage, CivicGovernance.CivicCoverage(w, own), "DTO roundtrips CivicCoverage");
        Near(sd.SanitationCapital, 60, "DTO roundtrips SanitationCapital");
        Near(sd.SanitationCoverage, CivicGovernance.SanitationCoverage(w, own), "DTO roundtrips SanitationCoverage");
        Near(sd.SanitationWaterRatio, 0.85, "DTO roundtrips SanitationWaterRatio");
        Check(sd.ImmigrationPolicy == ImmigrationPolicy.Open && sd.CivicFocus == CivicFocus.Communal, "DTO roundtrips ImmigrationPolicy and CivicFocus");
        Check(sd.CivicPrecedents != null && sd.CivicPrecedents.Length == 3 && sd.CivicPrecedents.Contains(CivicPrecedents.PlannedQuartersAndDrainage),
            "DTO roundtrips CivicPrecedents");
        Check(SaveCodec.Fingerprint(w) == fpBefore, "building observation does not mutate authoritative state");

        // Schema 22 payload must be rejected
        ExpectThrows<InvalidDataException>(() => ObservationContract.Deserialize(json.Replace("\"Schema\":23", "\"Schema\":22")),
            "ObservationContract.Deserialize rejects Schema 22 payload");

        // Zero rival leakage + fog-of-war safety: mutate rival civic state AND a hidden tile -> bit-identical owner JSON!
        rival.CivicComplexity = 0.12; rival.CivicStratification = -0.99; rival.CivicCapital = 9999.25; rival.SanitationCapital = 8888.5;
        rival.ImmigrationPolicy = ImmigrationPolicy.Open; rival.CivicFocus = CivicFocus.Communal;
        rivalSoc.CivicPrecedents.Clear(); rivalSoc.CivicExperience[CivicPrecedents.CeremonialIntegration] = 123.4;
        int hiddenTile = Enumerable.Range(0, w.Tiles.Length).First(t => !obs.Tiles.ContainsKey(t));
        w.Tiles[hiddenTile].Food = 99999; w.Tiles[hiddenTile].CultivationCapital = 5.0;
        var obsAfter = new AgentObservationBuilder().Build(w, own.Player);
        string jsonAfter = ObservationContract.Serialize(obsAfter, own.People.Mandate);
        Check(json == jsonAfter, "mutating rival civic state and hidden tiles produces byte-identical owner observation JSON");
        Check(!json.Contains("1234.5") && !json.Contains("678.9") && !json.Contains("TokensAndAccounting"),
            "owner observation JSON leaks zero rival civic data");

        // BuildForValidation parity (both with and without Tier 1 precedent)
        foreach (bool hasT1 in new[] { false, true })
        {
            var pvW = CivicFixture();
            var pvc = pvW.Camps[0];
            if (hasT1) pvc.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
            var full = new AgentObservationBuilder().Build(pvW, pvc.Player);
            var view = new AgentObservationBuilder().BuildForValidation(pvW, pvc.Player, new PlanningIndex(pvW));
            var fullMask = LegalActions.Build(full).Single(m => m.Entity == pvc.Id);
            var viewMask = LegalActions.Build(view).Single(m => m.Entity == pvc.Id);
            Check(fullMask.Actions.Select(a => a.ToString()).SequenceEqual(viewMask.Actions.Select(a => a.ToString())),
                $"BuildForValidation action mask matches Build identically (hasT1={hasT1})");
            Check(fullMask.Economy.StewardEnabled == viewMask.Economy.StewardEnabled && fullMask.Economy.StewardEnabled == hasT1,
                $"BuildForValidation Economy.StewardEnabled parity (hasT1={hasT1})");
            var stewEcon = EconomicIntent.From(pvc);
            stewEcon.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Steward, Population = 3 } };
            var testOrders = new List<Order> { new Order(OrderKind.Economy, pvc.Id) { Economy = stewEcon } };
            foreach (int t in new[] { -1, 0, 1, 2, 3 })
            {
                testOrders.Add(new Order(OrderKind.FissionPolicy, pvc.Id, target: t));
                testOrders.Add(new Order(OrderKind.ImmigrationPolicy, pvc.Id, target: t));
                testOrders.Add(new Order(OrderKind.CivicFocus, pvc.Id, target: t));
            }
            foreach (var ord in testOrders)
                Check(OrderRules.Validate(full, ord) == OrderRules.Validate(view, ord),
                    $"OrderRules.Validate parity between Build and BuildForValidation for {ord} (hasT1={hasT1})");
        }

        // HeuristicTribePolicy: disabled emits 0 policy orders; enabled follows owner-only ImmigrationPolicy rule + player-ID invariant.
        var aiDisabled = CivicFixture(enableCivic: false);
        aiDisabled.Camps[0].CivicStratification = -0.80; aiDisabled.Camps[0].CrowdingMortalityRate = 0.005; aiDisabled.Camps[0].FoodNeedsMetRatio = 1.0;
        var aiDisObs = new AgentObservationBuilder().Build(aiDisabled, aiDisabled.Camps[0].Player);
        var aiDisRes = new HeuristicTribePolicy().Decide(aiDisObs, aiDisabled.Camps[0].Mandate);
        Check(aiDisRes.Orders.All(o => !TurnPlanner.IsPolicyOrder(o.Kind)),
            "HeuristicTribePolicy emits zero policy orders when CivicGovernance is disabled");

        var aiW = CivicFixture();
        var ac = aiW.Camps[0];
        var policy = new HeuristicTribePolicy();
        Order CheckImmPolicy(double strat, double crowd, double nut, ImmigrationPolicy current, ImmigrationPolicy? expectedOrder, string label)
        {
            ac.CivicStratification = strat; ac.CrowdingMortalityRate = crowd; ac.FoodNeedsMetRatio = nut; ac.ImmigrationPolicy = current;
            var o = new AgentObservationBuilder().Build(aiW, ac.Player);
            var res = policy.Decide(o, ac.Mandate);
            Check(res.Orders.All(x => x.Kind != OrderKind.FissionPolicy && x.Kind != OrderKind.CivicFocus),
                "HeuristicTribePolicy never emits FissionPolicy or CivicFocus (" + label + ")");
            var immOrders = res.Orders.Where(x => x.Kind == OrderKind.ImmigrationPolicy).ToArray();
            if (expectedOrder == null)
            {
                Check(immOrders.Length == 0, "HeuristicTribePolicy emits no ImmigrationPolicy change (" + label + ")");
                return null;
            }
            Check(immOrders.Length == 1 && immOrders[0].Target == (int)expectedOrder.Value && OrderRules.Validate(o, immOrders[0]) == null,
                "HeuristicTribePolicy emits valid ImmigrationPolicy=" + expectedOrder.Value + " (" + label + ")");
            return immOrders[0];
        }
        CheckImmPolicy(0.0, 0.01, 1.0, ImmigrationPolicy.Neutral, null, "neutral stays neutral");
        CheckImmPolicy(-0.40, 0.01, 0.95, ImmigrationPolicy.Neutral, ImmigrationPolicy.Open, "egalitarian low-crowd fed switches to Open");
        CheckImmPolicy(-0.25, 0.028, 0.88, ImmigrationPolicy.Open, null, "Open holds in hysteresis band");
        CheckImmPolicy(-0.10, 0.01, 0.95, ImmigrationPolicy.Open, ImmigrationPolicy.Neutral, "Open reverts to Neutral outside hysteresis");
        CheckImmPolicy(0.50, 0.04, 0.95, ImmigrationPolicy.Neutral, ImmigrationPolicy.Restricted, "stratified high-crowd switches to Restricted");
        CheckImmPolicy(0.35, 0.03, 0.95, ImmigrationPolicy.Restricted, null, "Restricted holds in hysteresis band");
        CheckImmPolicy(0.20, 0.01, 0.95, ImmigrationPolicy.Restricted, ImmigrationPolicy.Neutral, "Restricted reverts to Neutral outside hysteresis");

        // Player-ID invariance of HeuristicTribePolicy
        ac.CivicStratification = -0.45; ac.CrowdingMortalityRate = 0.01; ac.FoodNeedsMetRatio = 0.95; ac.ImmigrationPolicy = ImmigrationPolicy.Neutral;
        var resP0 = policy.Decide(new AgentObservationBuilder().Build(aiW, ac.Player), ac.Mandate);
        ac.Player = 77; ac.People.Player = 77;
        var resP77 = policy.Decide(new AgentObservationBuilder().Build(aiW, ac.Player), ac.Mandate);
        Check(resP0.Orders.Select(o => o.ToString()).SequenceEqual(resP77.Orders.Select(o => o.ToString())),
            "HeuristicTribePolicy decisions (including ImmigrationPolicy) are player-ID invariant");
    }

    private static void PersistenceAndLegacyCompatibility()
    {
        // 1. Full civic save/load roundtrip & continuation + exact 365 bytes/tile layout
        var w = CivicFixture(sanitation: true);
        w.Resources = null;
        var c = w.Camps[0];
        c.Population = 800; c.StationaryTurns = 4; c.StationaryYears = 80;
        c.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        c.People.CivicExperience[CivicPrecedents.CivicOrganization] = 33.25;
        c.Workforce.Assign(WorkforceRoles.Steward, 12, c.Population);
        c.CivicComplexity = 0.28; c.CivicStratification = -0.32;
        c.CivicCapital = 42.5; c.SanitationCapital = 18.25;
        c.ImmigrationPolicy = ImmigrationPolicy.Open; c.CivicFocus = CivicFocus.Communal;
        c.RecentTradeVolume = 19.75; c.SanitationWaterRatio = 0.72;
        c.LastEnvironmentalComplexity = 0.29; c.LastEnvironmentalStratification = -0.41; c.LastCircumscription = 0.14;
        new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>());
        Check(w.History.Last().CivicReports != null && w.History.Last().CivicReports.Count == 1, "TurnRecord.CivicReports persisted after enabled turn");

        byte[] saved = SaveCodec.Save(w);
        string fp = SaveCodec.Fingerprint(w);
        // Verify exact 365 bytes/tile binary record size preserved
        byte[] raw = Unzip(saved);
        using (var ms = new MemoryStream(raw))
        using (var br = new BinaryReader(ms))
        {
            string magic = br.ReadString();
            int metaLen = br.ReadInt32();
            br.ReadBytes(metaLen);
            int tileCount = br.ReadInt32();
            long remaining = ms.Length - ms.Position; // tile bytes + 1 bool byte (Resources == null)
            Check(magic == "ANTHROPOCENE-11" && tileCount == w.Tiles.Length && remaining == (long)tileCount * 365 + 1,
                $"tile record layout stays exact 365 bytes/tile (remaining={remaining}, expected={(long)tileCount * 365 + 1})");
        }

        var loaded = SaveCodec.Load(saved);
        Check(SaveCodec.Fingerprint(loaded) == fp, "full civic SaveCodec.Save/Load roundtrip preserves exact Fingerprint");
        var lc = loaded.Camps[0];
        Check(loaded.Config.Civic != null && loaded.Config.Civic.Enabled && loaded.Config.Civic.SanitationEnabled, "Config.Civic roundtrips");
        Near(lc.CivicComplexity, c.CivicComplexity, "CivicComplexity roundtrips");
        Near(lc.CivicStratification, c.CivicStratification, "CivicStratification roundtrips");
        Near(lc.CivicCapital, c.CivicCapital, "CivicCapital roundtrips");
        Near(lc.SanitationCapital, c.SanitationCapital, "SanitationCapital roundtrips");
        Check(lc.ImmigrationPolicy == c.ImmigrationPolicy && lc.CivicFocus == c.CivicFocus, "ImmigrationPolicy and CivicFocus roundtrip");
        Near(lc.RecentTradeVolume, c.RecentTradeVolume, "RecentTradeVolume roundtrips");
        Near(lc.SanitationWaterRatio, c.SanitationWaterRatio, "SanitationWaterRatio roundtrips");
        Near(lc.LastEnvironmentalComplexity, c.LastEnvironmentalComplexity, "LastEnvironmentalComplexity roundtrips");
        Near(lc.LastEnvironmentalStratification, c.LastEnvironmentalStratification, "LastEnvironmentalStratification roundtrips");
        Near(lc.LastCircumscription, c.LastCircumscription, "LastCircumscription roundtrips");
        Check(lc.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration), "Society.CivicPrecedents roundtrips");
        Check(CultivationEconomy.Workers(lc, WorkforceRoles.Steward) == 12, "Steward workforce assignment roundtrips");
        Check(loaded.History.Last().CivicReports != null && loaded.History.Last().CivicReports.Count == 1, "TurnRecord.CivicReports roundtrips");

        // Continuation determinism after reload
        new TurnResolver().Resolve(w, new Dictionary<int, List<Order>>());
        new TurnResolver().Resolve(loaded, new Dictionary<int, List<Order>>());
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "post-load civic continuation matches uninterrupted world");

        // 2. Legacy XML omission test: strip ALL civic XML elements from serialized payload -> clean legacy load!
        var stripNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Civic", "CivicComplexity", "CivicStratification", "CivicCapital", "SanitationCapital",
            "ImmigrationPolicy", "CivicFocus", "RecentTradeVolume", "SanitationWaterRatio",
            "LastEnvironmentalComplexity", "LastEnvironmentalStratification", "LastCircumscription",
            "CivicExperience", "CivicPrecedents", "CivicReports", "SanitationWater"
        };
        byte[] strippedSave = StripXmlElements(saved, stripNames);
        var legacyLoaded = SaveCodec.Load(strippedSave);
        Check(legacyLoaded.Config.Civic == null && !CivicGovernance.Enabled(legacyLoaded.Config),
            "stripping Civic XML elements loads with Config.Civic == null");
        var legCamp = legacyLoaded.Camps[0];
        Check(legCamp.CivicComplexity == 0 && legCamp.CivicStratification == 0 && legCamp.CivicCapital == 0 &&
              legCamp.SanitationCapital == 0 && legCamp.RecentTradeVolume == 0 && legCamp.SanitationWaterRatio == 0 &&
              legCamp.LastEnvironmentalComplexity == 0 && legCamp.LastEnvironmentalStratification == 0 && legCamp.LastCircumscription == 0 &&
              legCamp.ImmigrationPolicy == ImmigrationPolicy.Neutral && legCamp.CivicFocus == CivicFocus.Auto,
            "stripping camp civic XML elements loads with exact zero/default values");
        Check(legCamp.People.CivicExperience != null && legCamp.People.CivicExperience.Count == 0 &&
              legCamp.People.CivicPrecedents != null && legCamp.People.CivicPrecedents.Count == 0,
            "stripping society civic XML elements null-coalesces empty CivicExperience and CivicPrecedents");
        Check(legacyLoaded.History.All(h => h.CivicReports == null && h.SanitationWater == null),
            "stripping TurnRecord civic XML leaves CivicReports and SanitationWater null");

        // 3. Reject corrupted loaded civic fields with InvalidDataException
        void ExpectCorruptRejected(Action<WorldState> corrupt, string label)
        {
            var bad = CivicFixture();
            corrupt(bad);
            byte[] bytes = SaveCodec.Save(bad);
            ExpectThrows<InvalidDataException>(() => SaveCodec.Load(bytes), "SaveCodec.Load rejects " + label);
        }
        ExpectCorruptRejected(b => b.Camps[0].CivicComplexity = 1.5, "CivicComplexity > 1");
        ExpectCorruptRejected(b => b.Camps[0].CivicComplexity = double.NaN, "CivicComplexity NaN");
        ExpectCorruptRejected(b => b.Camps[0].CivicStratification = -1.5, "CivicStratification < -1");
        ExpectCorruptRejected(b => b.Camps[0].CivicCapital = -0.1, "negative CivicCapital");
        ExpectCorruptRejected(b => b.Camps[0].SanitationCapital = -0.1, "negative SanitationCapital");
        ExpectCorruptRejected(b => b.Camps[0].SanitationWaterRatio = 1.2, "SanitationWaterRatio > 1");
        ExpectCorruptRejected(b => b.Societies[0].CivicExperience["CeremonialIntegration"] = -1, "negative CivicExperience");
        ExpectCorruptRejected(b => { b.Societies[0].CivicPrecedents.Add("CeremonialIntegration"); b.Societies[0].CivicPrecedents.Add("CeremonialIntegration"); }, "duplicate CivicPrecedents");

        // Invalid enum ordinals rejected by serializer, ValidateLoadedCamp, and TurnResolver.Invariants
        var badImm = CivicFixture(); badImm.Camps[0].ImmigrationPolicy = (ImmigrationPolicy)99;
        ExpectThrows<System.Runtime.Serialization.SerializationException>(() => SaveCodec.Save(badImm), "SaveCodec.Save rejects invalid ImmigrationPolicy ordinal");
        ExpectThrows<InvalidDataException>(() => CivicGovernance.ValidateLoadedCamp(badImm.Camps[0]), "ValidateLoadedCamp rejects invalid ImmigrationPolicy ordinal");
        ExpectThrows<InvalidOperationException>(() => TurnResolver.Invariants(badImm), "TurnResolver.Invariants rejects invalid ImmigrationPolicy ordinal");
        var badFocus = CivicFixture(); badFocus.Camps[0].CivicFocus = (CivicFocus)99;
        ExpectThrows<System.Runtime.Serialization.SerializationException>(() => SaveCodec.Save(badFocus), "SaveCodec.Save rejects invalid CivicFocus ordinal");
        ExpectThrows<InvalidDataException>(() => CivicGovernance.ValidateLoadedCamp(badFocus.Camps[0]), "ValidateLoadedCamp rejects invalid CivicFocus ordinal");
        ExpectThrows<InvalidOperationException>(() => TurnResolver.Invariants(badFocus), "TurnResolver.Invariants rejects invalid CivicFocus ordinal");

        // 4. Legacy world (Civic == null) leaves CivicReports/SanitationWater null and ConfigurationHash distinguishes Civic
        var offW = CivicFixture(enableCivic: false);
        new TurnResolver().Resolve(offW, new Dictionary<int, List<Order>>());
        Check(offW.History.Last().CivicReports == null && offW.History.Last().SanitationWater == null,
            "disabled world keeps TurnRecord.CivicReports and SanitationWater null");
        Check(SaveCodec.ConfigurationHash(offW.Config) != SaveCodec.ConfigurationHash(loaded.Config),
            "SaveCodec.ConfigurationHash distinguishes Civic == null vs enabled");
    }

    private static byte[] Unzip(byte[] bytes)
    {
        using var src = new MemoryStream(bytes);
        using var zip = new GZipStream(src, CompressionMode.Decompress);
        using var dst = new MemoryStream();
        zip.CopyTo(dst);
        return dst.ToArray();
    }

    private static byte[] Zip(byte[] bytes)
    {
        using var dst = new MemoryStream();
        using (var zip = new GZipStream(dst, CompressionLevel.Fastest, true)) zip.Write(bytes, 0, bytes.Length);
        return dst.ToArray();
    }

    private static byte[] StripXmlElements(byte[] savedBytes, HashSet<string> localNames)
    {
        byte[] raw = Unzip(savedBytes);
        using var input = new MemoryStream(raw);
        using var reader = new BinaryReader(input);
        string magic = reader.ReadString();
        int metaLen = reader.ReadInt32();
        byte[] metaBytes = reader.ReadBytes(metaLen);
        byte[] tail = reader.ReadBytes((int)(input.Length - input.Position));

        XDocument doc;
        using (var xmlReader = XmlDictionaryReader.CreateBinaryReader(metaBytes, XmlDictionaryReaderQuotas.Max))
            doc = XDocument.Load(xmlReader);
        foreach (var el in doc.Descendants().Where(e => localNames.Contains(e.Name.LocalName)).ToList())
            el.Remove();

        byte[] newMeta;
        using (var metaOut = new MemoryStream())
        {
            using (var xmlWriter = XmlDictionaryWriter.CreateBinaryWriter(metaOut, null, null, false))
                doc.Save(xmlWriter);
            newMeta = metaOut.ToArray();
        }
        using var outPayload = new MemoryStream();
        using (var writer = new BinaryWriter(outPayload))
        {
            writer.Write(magic);
            writer.Write(newMeta.Length);
            writer.Write(newMeta);
            writer.Write(tail);
            writer.Flush();
        }
        return Zip(outPayload.ToArray());
    }
}
