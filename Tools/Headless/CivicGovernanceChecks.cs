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
    private static void NearRelative(double value, double expected, string message, double relTolerance = 1e-12)
        => Check(expected != 0.0 && !double.IsNaN(expected) && !double.IsInfinity(expected)
            && !double.IsNaN(value) && !double.IsInfinity(value)
            && relTolerance >= 0.0 && !double.IsNaN(relTolerance) && !double.IsInfinity(relTolerance)
            && Math.Abs(value - expected) <= relTolerance * Math.Abs(expected),
            message + $" ({value} != {expected})");
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
        NumericalAndNonFiniteSafety();
        Phase1OrganicGrowthTo3000Gate();
        NaturalGeographicDivergenceGate();
        WelfareOnlyQoLIsolationGate();
        ToolAndStorageConservationGates();
        FamineShockCollapseDivergenceGate();
        DemandControlProtectedAndQoLRegressionGates();
        Phase1BFeatureContractsAndZeroRegression();
        Phase2SanitationContracts();
        Phase2OrganicWallBreakGate();
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

        // Regression (Bug #3): tiny valid CrowdingShapeRate > 0 converges to exact C^p limit (never collapses to 0),
        // and DiseaseMitigationFor handles tiny/huge valid DiseaseHalfScale > 0 cleanly without NaN or overflow.
        foreach (double tinyRate in new[] { 1e-13, double.Epsilon })
        {
            var tinyCrowdCfg = new SimulationConfig
            {
                Civic = new CivicGovernanceSettings { Enabled = true, SanitationEnabled = true, CrowdingShapeRate = tinyRate }
            };
            tinyCrowdCfg.Validate();
            Near(CivicGovernance.CrowdingMitigationFor(tinyCrowdCfg, 1.0, 1.0, 1.0), 0.050,
                $"M_c at tiny CrowdingShapeRate={tinyRate} and (C=1,P=1,cov=1) matches exact floor 0.050", 1e-11);
            double expectedHalf = 1.0 - 0.925 * Math.Pow(0.5, tinyCrowdCfg.Civic.CrowdingShapeExponent);
            Near(CivicGovernance.CrowdingMitigationFor(tinyCrowdCfg, 0.5, 0.0, 1.0), expectedHalf,
                $"M_c at tiny CrowdingShapeRate={tinyRate} and (C=0.5,P=0,cov=1) matches exact C^p limit", 1e-11);
        }
        foreach (double tinyH in new[] { 1e-13, double.Epsilon })
        {
            var tinyDisCfg = new SimulationConfig
            {
                Civic = new CivicGovernanceSettings { Enabled = true, SanitationEnabled = true, DiseaseHalfScale = tinyH }
            };
            tinyDisCfg.Validate();
            double mdTiny = CivicGovernance.DiseaseMitigationFor(tinyDisCfg, 0.5, 0.0, 1.0);
            Check(!double.IsNaN(mdTiny) && !double.IsInfinity(mdTiny), $"M_d finite for tiny DiseaseHalfScale={tinyH}");
            Near(mdTiny, 1.0 - 0.735, $"M_d at tiny DiseaseHalfScale={tinyH} approaches f_d=1 limit", 1e-11);
        }
        foreach (double hugeH in new[] { 1e100, 1e300 })
        {
            var hugeDisCfg = new SimulationConfig
            {
                Civic = new CivicGovernanceSettings { Enabled = true, SanitationEnabled = true, DiseaseHalfScale = hugeH }
            };
            hugeDisCfg.Validate();
            double mdHuge = CivicGovernance.DiseaseMitigationFor(hugeDisCfg, 0.5, 0.0, 1.0);
            Check(!double.IsNaN(mdHuge) && !double.IsInfinity(mdHuge), $"M_d finite for huge DiseaseHalfScale={hugeH}");
            Near(mdHuge, 1.0 - 0.735 * 0.5, $"M_d at huge DiseaseHalfScale={hugeH} approaches f_d=C limit", 1e-11);
        }

        double sEgal1k = CivicGovernance.RequiredStewards(cfg, 1000, 0.75, -1.0, false, -1.0);
        double sStrat1k = CivicGovernance.RequiredStewards(cfg, 1000, 0.75, 1.0, false, 1.0);
        double sEgal10kNo3A = CivicGovernance.RequiredStewards(cfg, 10000, 0.75, -1.0, false, -1.0);
        double sStrat10kNo3A = CivicGovernance.RequiredStewards(cfg, 10000, 0.75, 1.0, false, 1.0);
        double sEgal10k3A = CivicGovernance.RequiredStewards(cfg, 10000, 0.75, -1.0, true, -1.0);
        Check(sEgal1k < sStrat1k, "Egalitarian requires fewer Stewards at N=1000");
        Check(sEgal10kNo3A > sStrat10kNo3A, "Without Tier 3A, Egalitarian Steward scaling exceeds Stratified at N=10000");
        Check(sEgal10k3A < sStrat10kNo3A, "Tier 3A flattens Egalitarian superlinear Steward scaling");

        // Verify log-space RequiredStewards matches exact Math.Pow(1 + r^2, 0.5 * (beta - 1)) in normal range to < 1e-15.
        foreach (double nNorm in new[] { 1000.0, 3500.0, 10000.0 })
        {
            double rNorm = nNorm / cfg.Civic.StewardScalingKnee;
            double expectedEgal = 0.75 * nNorm * cfg.Civic.EgalitarianStewardRate * Math.Pow(1.0 + rNorm * rNorm, 0.5 * (cfg.Civic.EgalitarianStewardExponent - 1.0));
            Near(CivicGovernance.RequiredStewards(cfg, nNorm, 0.75, -1.0, false, -1.0), expectedEgal,
                $"log-space RequiredStewards matches exact formula at N={nNorm}", 1e-15);
        }

        // Regression (Iter 3 Bug #1a): tiny valid StewardScalingKnee must NOT prematurely overflow kneeRatio^2 and collapse CivicCoverage.
        var tinyKneeWorld = CivicFixture(sanitation: true);
        tinyKneeWorld.Config.Civic = new CivicGovernanceSettings
        {
            Enabled = true,
            SanitationEnabled = true,
            StewardScalingKnee = 1e-160,
            EgalitarianStewardExponent = 1.001,
            StratifiedStewardExponent = 1.001
        };
        tinyKneeWorld.Config.Validate();
        var tinyKneeCamp = tinyKneeWorld.Camps[0];
        tinyKneeCamp.Population = 1000;
        tinyKneeCamp.CivicComplexity = 0.75;
        tinyKneeCamp.CivicStratification = 0.0;
        tinyKneeCamp.LastEnvironmentalStratification = 0.0;
        tinyKneeCamp.CivicCapital = 1000.0;
        tinyKneeCamp.Workforce = new PopulationLedger();
        tinyKneeCamp.Workforce.Assign(WorkforceRoles.Steward, 100, 1000);
        double reqTinyKnee = CivicGovernance.RequiredStewards(tinyKneeWorld, tinyKneeCamp);
        double covTinyKnee = CivicGovernance.CivicCoverage(tinyKneeWorld, tinyKneeCamp);
        Check(!double.IsNaN(reqTinyKnee) && !double.IsInfinity(reqTinyKnee), "RequiredStewards finite at StewardScalingKnee=1e-160, beta=1.001");
        Near(reqTinyKnee, 81.86957328273559, "RequiredStewards matches exact value 81.86957328273559 at knee=1e-160", 1e-9);
        Near(covTinyKnee, 1.0, "CivicCoverage remains 1.0 (no premature overflow collapse) at knee=1e-160", 1e-12);

        // Regression (Iter 3 Bug #1b): default exponents across tiny valid StewardScalingKnee values down to double.Epsilon.
        var tinyKneeCases = new (double Knee, double P, bool Has3A, string Label)[]
        {
            (1e-152, -1.0, false, "knee=1e-152 P=-1 no3A"),
            (1e-160,  1.0, false, "knee=1e-160 P=+1 no3A"),
            (1e-200,  1.0, false, "knee=1e-200 P=+1 no3A"),
            (1e-300,  1.0, true,  "knee=1e-300 P=+1 has3A"),
            (double.Epsilon, 1.0, true, "knee=double.Epsilon P=+1 has3A")
        };
        foreach (var tcCase in tinyKneeCases)
        {
            var tkCfg = new SimulationConfig { Civic = new CivicGovernanceSettings { Enabled = true, StewardScalingKnee = tcCase.Knee } };
            tkCfg.Validate();
            double actualReq = CivicGovernance.RequiredStewards(tkCfg, 1000.0, 0.75, tcCase.P, tcCase.Has3A, tcCase.P);
            double uCase = 0.5 * (tcCase.P + 1.0);
            double betaRawCase = tkCfg.Civic.EgalitarianStewardExponent + (tkCfg.Civic.StratifiedStewardExponent - tkCfg.Civic.EgalitarianStewardExponent) * uCase;
            double excessCase = tcCase.Has3A ? tkCfg.Civic.AccountingExponentRetention * (betaRawCase - 1.0) : (betaRawCase - 1.0);
            double rateCase = tkCfg.Civic.EgalitarianStewardRate + (tkCfg.Civic.StratifiedStewardRate - tkCfg.Civic.EgalitarianStewardRate) * uCase;
            double expectedReq = Math.Exp(Math.Log(0.75) + Math.Log(1000.0) + Math.Log(rateCase) + excessCase * (Math.Log(1000.0) - Math.Log(tcCase.Knee)));
            Check(!double.IsNaN(actualReq) && !double.IsInfinity(actualReq), "RequiredStewards finite for " + tcCase.Label);
            Near(actualReq, expectedReq, "RequiredStewards matches exact log-space ground truth for " + tcCase.Label, 1e-9);
        }

        // Defensive finite saturation sweep for extreme valid parameters across pure helpers.
        var extremeW = CivicFixture(sanitation: true);
        extremeW.Config.Civic = new CivicGovernanceSettings
        {
            Enabled = true,
            SanitationEnabled = true,
            AlignmentFrictionRate = double.MaxValue,
            EgalitarianCapitalPerPerson = 1e308,
            StratifiedCapitalPerPerson = double.MaxValue,
            SanitationCapitalPerPerson = double.MaxValue,
            CohesionLinearGain = double.MaxValue,
            CohesionUrbanGain = double.MaxValue,
            StratifiedPublicWorksBonus = double.MaxValue,
            StratifiedGranaryStorageBonus = double.MaxValue
        };
        extremeW.Config.Validate();
        var extCamp = extremeW.Camps[0];
        extCamp.Population = 1000;
        extCamp.CivicComplexity = 1.0;
        extCamp.CivicStratification = 1.0;
        extCamp.LastEnvironmentalStratification = -1.0;
        extCamp.CivicCapital = double.MaxValue;
        extCamp.Workforce = new PopulationLedger();
        extCamp.Workforce.Assign(WorkforceRoles.Steward, 1000, 1000);
        double extFriction = CivicGovernance.AlignmentFriction(extremeW.Config, 1.0, -1.0);
        double extReqCap = CivicGovernance.RequiredCivicCapital(extremeW.Config, 1000, 1.0, 1.0);
        double extReqSan = CivicGovernance.RequiredSanitationCapital(extremeW.Config, 1000);
        double extCohesion = CivicGovernance.CohesionMultiplierFor(extremeW.Config, 1.0, 1.0, 1.0);
        double extPw = CivicGovernance.PublicWorksMobilizationMultiplier(extremeW, extCamp);
        double extGranary = CivicGovernance.GranaryStorageMultiplier(extremeW, extCamp);
        double extRelaxInfY = CivicGovernance.Relax(0.42, 0.90, 0.025, double.PositiveInfinity);
        double extRelaxInfR = CivicGovernance.Relax(0.42, 0.90, double.PositiveInfinity, 20.0);
        double extTradeVol = CivicGovernance.TradeRecordVolumePersonYears(cfg, new TradeRecord
        {
            Want = TradeGood.Cloth, WantQuantity = double.MaxValue / 20.0,
            Give = TradeGood.Cloth, GiveQuantity = double.MaxValue / 20.0
        });
        Check(!double.IsInfinity(extFriction) && extFriction == double.MaxValue, "AlignmentFriction saturates cleanly to double.MaxValue");
        Check(!double.IsInfinity(extReqCap) && extReqCap == double.MaxValue, "RequiredCivicCapital saturates cleanly to double.MaxValue");
        Check(!double.IsInfinity(extReqSan) && extReqSan == double.MaxValue, "RequiredSanitationCapital saturates cleanly to double.MaxValue");
        Check(!double.IsInfinity(extCohesion) && extCohesion == double.MaxValue, "CohesionMultiplierFor saturates cleanly to double.MaxValue");
        Check(!double.IsInfinity(extPw) && extPw >= 1.0, "PublicWorksMobilizationMultiplier stays finite under extreme bonus");
        Check(!double.IsInfinity(extGranary) && extGranary >= 1.0, "GranaryStorageMultiplier stays finite under extreme bonus");
        Near(extRelaxInfY, 0.42, "Relax with +Infinity years safely returns current", 1e-15);
        Near(extRelaxInfR, 0.42, "Relax with +Infinity rate safely returns current", 1e-15);
        Check(!double.IsInfinity(extTradeVol) && extTradeVol > 0, "TradeRecordVolumePersonYears avoids intermediate sum overflow");

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

        // Regression (Bug #1a): Pre-seeded Paleolithic society MUST NOT unlock any civic precedent in Close (no cascade).
        var paleoSeedW = CivicFixture(sanitation: true);
        var psc = paleoSeedW.Camps[0];
        psc.People.Progression.Era = Era.Paleolithic;
        psc.Population = 1500; psc.StationaryTurns = 5; psc.StationaryYears = 100;
        psc.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
        psc.People.CivicExperience[CivicPrecedents.CeremonialIntegration] = 50.0;
        psc.People.CivicExperience[CivicPrecedents.CivicOrganization] = 100.0;
        psc.People.CivicExperience[CivicPrecedents.TokensAndAccounting] = 150.0;
        psc.People.CivicExperience[CivicPrecedents.PlannedQuartersAndDrainage] = 150.0;
        CivicGovernance.Close(paleoSeedW, new TurnRecord(), 20.0);
        Check(psc.People.CivicPrecedents.Count == 0 && !psc.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration),
            "pre-seeded Paleolithic society unlocks 0 civic precedents in Close");

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
        recv.Progression.Era = Era.Neolithic;
        var part = new Society { Id = 11 };
        part.Progression.Era = Era.Neolithic;
        part.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        CivicGovernance.RecordTradeDiffusion(t3bW, recv, part, 500.0);
        Check(recv.CivicExperience.Count == 0, "RecordTradeDiffusion is a strict no-op when PrecedentDiffusionPerTrade == 0");

        // Regression (Bug #1b): RecordTradeDiffusion onto Paleolithic receiver accrues 0 experience and unlocks 0 precedents.
        var diffPaleoW = CivicFixture(sanitation: true);
        diffPaleoW.Config.Civic.PrecedentDiffusionPerTrade = 1.0;
        var paleoRecv = diffPaleoW.Camps[0].People;
        paleoRecv.Progression.Era = Era.Paleolithic;
        var allPrecPartner = new Society { Id = 777 };
        allPrecPartner.Progression.Era = Era.Neolithic;
        allPrecPartner.CivicPrecedents.AddRange(CivicPrecedents.All);
        CivicGovernance.RecordTradeDiffusion(diffPaleoW, paleoRecv, allPrecPartner, 200.0);
        Check(paleoRecv.CivicExperience.Count == 0, "RecordTradeDiffusion accrues 0 experience onto Paleolithic receiver");
        CivicGovernance.Close(diffPaleoW, new TurnRecord(), 20.0);
        Check(paleoRecv.CivicPrecedents.Count == 0, "Paleolithic receiver unlocks 0 precedents after trade diffusion + Close");

        // Regression (Bug #1c): RecordTradeDiffusion with SanitationEnabled=false filters PlannedQuartersAndDrainage
        // while still diffusing Tier 1/2/3A cleanly to a Neolithic receiver.
        var diffNoSanW = CivicFixture(sanitation: false);
        diffNoSanW.Config.Civic.PrecedentDiffusionPerTrade = 1.0;
        var neoRecv = new Society { Id = 778 };
        neoRecv.Progression.Era = Era.Neolithic;
        CivicGovernance.RecordTradeDiffusion(diffNoSanW, neoRecv, allPrecPartner, 200.0);
        Check(!neoRecv.CivicExperience.ContainsKey(CivicPrecedents.PlannedQuartersAndDrainage),
            "RecordTradeDiffusion does not accrue PlannedQuartersAndDrainage when SanitationEnabled=false");
        Near(neoRecv.CivicExperience[CivicPrecedents.CeremonialIntegration], 200.0, "Tier 1 diffuses to Neolithic receiver when SanitationEnabled=false", 1e-12);
        Near(neoRecv.CivicExperience[CivicPrecedents.CivicOrganization], 200.0, "Tier 2 diffuses to Neolithic receiver when SanitationEnabled=false", 1e-12);
        Near(neoRecv.CivicExperience[CivicPrecedents.TokensAndAccounting], 200.0, "Tier 3A diffuses to Neolithic receiver when SanitationEnabled=false", 1e-12);
        var neoSanRecv = new Society { Id = 779 };
        neoSanRecv.Progression.Era = Era.Neolithic;
        CivicGovernance.RecordTradeDiffusion(diffPaleoW, neoSanRecv, allPrecPartner, 200.0);
        Near(neoSanRecv.CivicExperience[CivicPrecedents.PlannedQuartersAndDrainage], 200.0,
            "RecordTradeDiffusion diffuses PlannedQuartersAndDrainage to Neolithic receiver when SanitationEnabled=true", 1e-12);

        // Regression (Iter 3 Bug #2a): RecordTradeDiffusion single-call product overflow AND two-call cumulative sum overflow
        // MUST saturate cleanly to finite <= double.MaxValue without corrupting ValidateInvariants.
        var ovProdW = CivicFixture(sanitation: true);
        ovProdW.Config.Civic.PrecedentDiffusionPerTrade = 10.0;
        ovProdW.Config.Validate();
        var ovProdSoc = ovProdW.Camps[0].People;
        ovProdSoc.Progression.Era = Era.Neolithic;
        CivicGovernance.RecordTradeDiffusion(ovProdW, ovProdSoc, allPrecPartner, double.MaxValue);
        Check(ovProdSoc.CivicExperience.Count == CivicPrecedents.All.Length &&
              ovProdSoc.CivicExperience.Values.All(v => !double.IsNaN(v) && !double.IsInfinity(v) && v > 0 && v <= double.MaxValue),
            "RecordTradeDiffusion single-call overflow saturates CivicExperience cleanly to finite double");
        CivicGovernance.ValidateInvariants(ovProdW);
        Check(true, "ValidateInvariants passes after RecordTradeDiffusion single-call overflow");

        var ovSumW = CivicFixture(sanitation: true);
        ovSumW.Config.Civic.PrecedentDiffusionPerTrade = 1.0;
        ovSumW.Config.Validate();
        var ovSumSoc = ovSumW.Camps[0].People;
        ovSumSoc.Progression.Era = Era.Neolithic;
        CivicGovernance.RecordTradeDiffusion(ovSumW, ovSumSoc, allPrecPartner, 1.2e308);
        CivicGovernance.RecordTradeDiffusion(ovSumW, ovSumSoc, allPrecPartner, 1.2e308);
        Check(ovSumSoc.CivicExperience.Count == CivicPrecedents.All.Length &&
              ovSumSoc.CivicExperience.Values.All(v => !double.IsNaN(v) && !double.IsInfinity(v) && v == double.MaxValue),
            "RecordTradeDiffusion two-call cumulative overflow saturates CivicExperience to double.MaxValue");
        CivicGovernance.ValidateInvariants(ovSumW);
        Check(true, "ValidateInvariants passes after RecordTradeDiffusion cumulative overflow");

        // Regression (Iter 3 Bug #2b): Close(horizonYears = +Infinity) treats dt as 0, keeps CivicExperience finite, and passes ValidateInvariants.
        var infCloseW = CivicFixture(sanitation: true);
        var infCamp = infCloseW.Camps[0];
        infCamp.Population = 1200;
        infCamp.StationaryTurns = 4;
        infCamp.StationaryYears = 80;
        infCamp.CivicComplexity = 0.25;
        infCamp.CivicStratification = 0.20;
        infCamp.RecentTradeVolume = 45.0;
        infCamp.People.Progression.Era = Era.Neolithic;
        infCamp.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        infCamp.People.CivicExperience[CivicPrecedents.CivicOrganization] = 12.0;
        infCamp.Workforce = new PopulationLedger();
        infCamp.Workforce.Assign(WorkforceRoles.Steward, 20, infCamp.Population);
        CivicGovernance.Close(infCloseW, new TurnRecord(), double.PositiveInfinity);
        Near(infCamp.People.CivicExperience[CivicPrecedents.CivicOrganization], 12.0,
            "Close(+Infinity) treats dt=0 and leaves CivicExperience finite and unchanged", 1e-12);
        Near(infCamp.CivicComplexity, 0.25, "Close(+Infinity) leaves CivicComplexity unchanged", 1e-12);
        Near(infCamp.CivicStratification, 0.20, "Close(+Infinity) leaves CivicStratification unchanged", 1e-12);
        Near(infCamp.RecentTradeVolume, 45.0, "Close(+Infinity) leaves RecentTradeVolume unchanged", 1e-12);
        CivicGovernance.ValidateInvariants(infCloseW);
        Check(true, "ValidateInvariants passes after Close(horizonYears = +Infinity)");

        // Step 1 Trade EMA saturation under extreme TradeRecord volume and tiny dt preserves finite RecentTradeVolume & invariants.
        var emaOvW = CivicFixture(sanitation: true);
        var emaOvCamp = emaOvW.Camps[0];
        var emaOvRec = new TurnRecord();
        emaOvRec.Trades.Add(new TradeRecord { Requester = emaOvCamp.Id, Responder = emaOvCamp.Id + 999, Want = TradeGood.Cloth, WantQuantity = double.MaxValue, Give = TradeGood.Cloth, GiveQuantity = double.MaxValue });
        emaOvRec.Trades.Add(new TradeRecord { Requester = emaOvCamp.Id, Responder = emaOvCamp.Id + 999, Want = TradeGood.Cloth, WantQuantity = double.MaxValue, Give = TradeGood.Cloth, GiveQuantity = double.MaxValue });
        CivicGovernance.Close(emaOvW, emaOvRec, 1e-6);
        Check(!double.IsNaN(emaOvCamp.RecentTradeVolume) && !double.IsInfinity(emaOvCamp.RecentTradeVolume) && emaOvCamp.RecentTradeVolume >= 0,
            "Close Step 1 Trade EMA stays finite non-negative under extreme TradeRecord overflow");
        CivicGovernance.ValidateInvariants(emaOvW);
        Check(true, "ValidateInvariants passes after extreme TradeRecord Close");

        // InitializeDaughterView legacy guard: MUST set (0, 0), NEVER -0.15 when Civic disabled!
        var legW = CivicFixture(enableCivic: false);
        var legParent = legW.Camps[0]; legParent.CivicStratification = 0.0;
        var legDaughter = new Camp { Id = 999, CivicCapital = 10, SanitationCapital = 5, CivicStratification = 0.4 };
        CivicGovernance.InitializeDaughterView(legW, legParent, legDaughter);
        Check(legDaughter.CivicComplexity == 0 && legDaughter.CivicStratification == 0 && legDaughter.CivicCapital == 0 && legDaughter.SanitationCapital == 0,
            "InitializeDaughterView keeps C=0, P=0 (never -0.15) when Civic governance is disabled");

        // Regression (Bug #2): InitializeDaughterView on freshly constructed unbound Camp matches DaughterCoordinates exactly.
        var unboundW = CivicFixture(sanitation: true);
        var uParent = unboundW.Camps[0];
        uParent.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        uParent.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        uParent.CivicComplexity = 0.60;
        uParent.CivicStratification = 0.40;
        var unboundView = new Camp { Id = 77, CivicCapital = 25, SanitationCapital = 10, RecentTradeVolume = 15, ImmigrationPolicy = ImmigrationPolicy.Restricted, CivicFocus = CivicFocus.Centralized };
        CivicGovernance.InitializeDaughterView(unboundW, uParent, unboundView);
        var expectedDaughter = CivicGovernance.DaughterCoordinates(unboundW, uParent);
        Near(unboundView.CivicComplexity, expectedDaughter.Complexity, "unbound InitializeDaughterView matches DaughterCoordinates.Complexity (0.30)", 1e-12);
        Near(unboundView.LastEnvironmentalComplexity, expectedDaughter.Complexity, "unbound InitializeDaughterView sets LastEnvironmentalComplexity (0.30)", 1e-12);
        Near(unboundView.CivicStratification, expectedDaughter.Stratification, "unbound InitializeDaughterView matches DaughterCoordinates.Stratification (0.05)", 1e-12);
        Near(unboundView.LastEnvironmentalStratification, expectedDaughter.Stratification, "unbound InitializeDaughterView sets LastEnvironmentalStratification (0.05)", 1e-12);
        Check(unboundView.CivicComplexity > 0 && unboundView.LastEnvironmentalComplexity > 0 &&
              unboundView.CivicCapital == 0 && unboundView.SanitationCapital == 0 && unboundView.RecentTradeVolume == 0 &&
              unboundView.ImmigrationPolicy == ImmigrationPolicy.Neutral && unboundView.CivicFocus == CivicFocus.Auto,
            "unbound InitializeDaughterView produces non-zero dampened complexity and zeroes capitals/policies");

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

    private static void NumericalAndNonFiniteSafety()
    {
        static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // =========================================================================
        // Regression (Iter 4 Bug #1): Convex interpolation eliminates catastrophic
        // cancellation on asymmetric StewardExponent, StewardRate & CapitalPerPerson.
        // =========================================================================
        double r10k = 10000.0 / 3500.0;
        double logBase10k = 0.5 * Math.Log(1.0 + r10k * r10k);
        var asymExpCases = new (double BetaE, double BetaS, double P, bool Has3A, double Rate, string Label)[]
        {
            (1e10,            1.15,   1.0, false, 0.105, "betaE=1e10, betaS=1.15 at P=+1"),
            (1e17,            1.15,   1.0, false, 0.105, "betaE=1e17, betaS=1.15 at P=+1"),
            (1e17,            2.60,   1.0, false, 0.105, "betaE=1e17, betaS=2.60 at P=+1"),
            (1e100,          100.0,   1.0, false, 0.105, "betaE=1e100, betaS=100 at P=+1"),
            (double.MaxValue, 100.0,  1.0, false, 0.105, "betaE=MaxValue, betaS=100 at P=+1"),
            (1e100,           2.60,   1.0, true,  0.105, "betaE=1e100, betaS=2.60 at P=+1 has3A"),
            (1.15,           1e100,  -1.0, false, 0.045, "betaE=1.15, betaS=1e100 at P=-1"),
            (1.15, double.MaxValue,  -1.0, false, 0.045, "betaE=1.15, betaS=MaxValue at P=-1"),
            (2.60,           1e100,  -1.0, true,  0.045, "betaE=2.60, betaS=1e100 at P=-1 has3A")
        };
        foreach (var tc in asymExpCases)
        {
            var cfgExp = new SimulationConfig
            {
                Civic = new CivicGovernanceSettings
                {
                    Enabled = true,
                    EgalitarianStewardExponent = tc.BetaE,
                    StratifiedStewardExponent = tc.BetaS
                }
            };
            cfgExp.Validate();
            double actual = CivicGovernance.RequiredStewards(cfgExp, 10000.0, 1.0, tc.P, tc.Has3A, tc.P);
            double activeBeta = tc.P > 0 ? tc.BetaS : tc.BetaE;
            double excess = (activeBeta - 1.0) * (tc.Has3A ? cfgExp.Civic.AccountingExponentRetention : 1.0);
            double expected = Math.Exp(Math.Log(10000.0) + Math.Log(tc.Rate) + excess * logBase10k);
            Check(IsFinite(actual), "RequiredStewards finite for " + tc.Label);
            Near(actual, expected, "convex RequiredStewards matches exact analytical value for " + tc.Label, 1e-12);
        }

        // Exact chal_iter3_1 Bug #1 numeric spot values
        var spotCfg1 = new SimulationConfig { Civic = new CivicGovernanceSettings { Enabled = true, EgalitarianStewardExponent = 1e17, StratifiedStewardExponent = 1.15 } };
        Near(CivicGovernance.RequiredStewards(spotCfg1, 10000.0, 1.0, 1.0, false, 1.0), 1239.7755539961836, "Bug #1 spot 1e17/1.15 -> 1239.7755539961836", 1e-12);
        var spotCfg2 = new SimulationConfig { Civic = new CivicGovernanceSettings { Enabled = true, EgalitarianStewardExponent = 1e17, StratifiedStewardExponent = 2.60 } };
        Near(CivicGovernance.RequiredStewards(spotCfg2, 10000.0, 1.0, 1.0, false, 1.0), 6177.738313977794, "Bug #1 spot 1e17/2.60 -> 6177.738313977794", 1e-12);
        var spotCfg3 = new SimulationConfig { Civic = new CivicGovernanceSettings { Enabled = true, EgalitarianStewardExponent = 1e100, StratifiedStewardExponent = 100.0 } };
        Near(CivicGovernance.RequiredStewards(spotCfg3, 10000.0, 1.0, 1.0, false, 1.0), 4.392150542805956e+50, "Bug #1 spot 1e100/100 -> 4.392150542805956e+50", 1e-12);

        // Symmetric endpoint checks for StewardRate and CapitalPerPerson across 40 orders of magnitude
        var asymRateCfg = new SimulationConfig
        {
            Civic = new CivicGovernanceSettings
            {
                Enabled = true,
                EgalitarianStewardRate = 1e-20,
                StratifiedStewardRate = 1e20,
                EgalitarianStewardExponent = 1.0,
                StratifiedStewardExponent = 1.0,
                EgalitarianCapitalPerPerson = 1e-20,
                StratifiedCapitalPerPerson = 1e20
            }
        };
        asymRateCfg.Validate();
        NearRelative(CivicGovernance.RequiredStewards(asymRateCfg, 1000.0, 1.0, -1.0, false, -1.0), 1000.0 * 1e-20, "StewardRate exact egalitarian endpoint at P=-1", 1e-12);
        Near(CivicGovernance.RequiredStewards(asymRateCfg, 1000.0, 1.0,  1.0, false,  1.0), 1000.0 * 1e20,  "StewardRate exact stratified endpoint at P=+1", 1e-12);
        NearRelative(CivicGovernance.RequiredCivicCapital(asymRateCfg, 1000.0, 1.0, -1.0), 1000.0 * 1e-20, "CapitalPerPerson exact egalitarian endpoint at P=-1", 1e-12);
        Near(CivicGovernance.RequiredCivicCapital(asymRateCfg, 1000.0, 1.0,  1.0), 1000.0 * 1e20,  "CapitalPerPerson exact stratified endpoint at P=+1", 1e-12);

        // Subnormal midpoint convex rate must not underflow 0.5*double.Epsilon + 0.5*double.Epsilon to 0
        var epsRateCfg = new SimulationConfig
        {
            Civic = new CivicGovernanceSettings
            {
                Enabled = true,
                EgalitarianStewardRate = double.Epsilon,
                StratifiedStewardRate = double.Epsilon,
                EgalitarianStewardExponent = 1.0,
                StratifiedStewardExponent = 1.0,
                EgalitarianCapitalPerPerson = double.Epsilon,
                StratifiedCapitalPerPerson = double.Epsilon
            }
        };
        epsRateCfg.Validate();
        double expectedEpsMid = Math.Exp(Math.Log(1.0) + Math.Log(1e308) + Math.Log(double.Epsilon));
        NearRelative(CivicGovernance.RequiredStewards(epsRateCfg, 1e308, 1.0, 0.0, false, 0.0), expectedEpsMid, "RequiredStewards midpoint at double.Epsilon does not underflow to 0", 1e-12);
        NearRelative(CivicGovernance.RequiredCivicCapital(epsRateCfg, 1e308, 1.0, 0.0), expectedEpsMid, "RequiredCivicCapital midpoint at double.Epsilon does not underflow to 0", 1e-12);

        // =========================================================================
        // Regression (Iter 4 Bug #2): Subnormal quantization guard on direct fast paths
        // =========================================================================
        // Reproducer #1: human-scale CivicCoverage must stay 1.0 (not collapse to 0.508)
        var subW1 = CivicFixture(sanitation: true);
        subW1.Config.Civic = new CivicGovernanceSettings
        {
            Enabled = true,
            SanitationEnabled = true,
            StewardScalingKnee = 1e-188,
            EgalitarianStewardRate = 2.5e-316,
            StratifiedStewardRate = 2.5e-316,
            EgalitarianStewardExponent = 2.60,
            StratifiedStewardExponent = 2.60,
            AlignmentFrictionRate = 1e20
        };
        subW1.Config.Validate();
        var subC1 = subW1.Camps[0];
        subC1.Population = 1000;
        subC1.CivicComplexity = 1e-11;
        subC1.CivicStratification = -1.0;
        subC1.LastEnvironmentalStratification = 1.0;
        subC1.CivicCapital = 1000.0;
        subC1.Workforce = new PopulationLedger();
        subC1.Workforce.Assign(WorkforceRoles.Steward, 200, 1000);
        double subReq1 = CivicGovernance.RequiredStewards(subW1, subC1);
        double subCov1 = CivicGovernance.CivicCoverage(subW1, subC1);
        Near(subReq1, 199.05358399107098, "Bug #2 reproducer #1 RequiredStewards matches exact 199.05358399107098", 1e-12);
        Near(subCov1, 1.0, "Bug #2 reproducer #1 CivicCoverage remains 1.0 (no subnormal collapse)", 1e-12);

        // Reproducer #2: strict monotonicity and < 1e-12 precision across subnormal rates with default AlignmentFrictionRate=0.35
        double EvalSubRate(double rate)
        {
            var cRate = new SimulationConfig
            {
                Civic = new CivicGovernanceSettings
                {
                    Enabled = true,
                    StewardScalingKnee = 1e-188,
                    EgalitarianStewardRate = rate,
                    StratifiedStewardRate = rate,
                    EgalitarianStewardExponent = 2.60,
                    StratifiedStewardExponent = 2.60
                }
            };
            cRate.Validate();
            return CivicGovernance.RequiredStewards(cRate, 1000.0, 1e-11, -1.0, false, -1.0);
        }
        double r25 = EvalSubRate(2.5e-316);
        double r74 = EvalSubRate(7.4e-316);
        double r305 = EvalSubRate(1e-305);
        NearRelative(r25, 9.952679199553523e-19, "Bug #2 reproducer #2 rate=2.5e-316 exact to < 1e-12", 1e-12);
        NearRelative(r74, 2.9459930532956414e-18, "Bug #2 reproducer #2 rate=7.4e-316 exact to < 1e-12", 1e-12);
        Check(r74 > r25, "Bug #2 reproducer #2 RequiredStewards strictly monotone across subnormal rates (7.4e-316 > 2.5e-316)");
        NearRelative(r305, 3.9810717055348015e-8, "Bug #2 reproducer #2 rate=1e-305 exact to < 1e-12", 1e-12);

        // Reproducer #3: default StewardScalingKnee=3500, C=0.75, rate=double.Epsilon, AlignmentFrictionRate=1e100
        var subCfg3 = new SimulationConfig
        {
            Civic = new CivicGovernanceSettings
            {
                Enabled = true,
                EgalitarianStewardRate = double.Epsilon,
                AlignmentFrictionRate = 1e100
            }
        };
        subCfg3.Validate();
        NearRelative(CivicGovernance.RequiredStewards(subCfg3, 1000.0, 0.75, -1.0, false, 1.0), 7.89113998452051e-221,
            "Bug #2 reproducer #3 default knee + double.Epsilon rate matches exact 7.89113998452051e-221", 1e-12);

        // Subnormal cp = c * population intermediate guard in both RequiredStewards and RequiredCivicCapital
        var subCpCfg = new SimulationConfig
        {
            Civic = new CivicGovernanceSettings
            {
                Enabled = true,
                EgalitarianStewardRate = 1e305,
                StratifiedStewardRate = 1e305,
                EgalitarianStewardExponent = 1.0,
                StratifiedStewardExponent = 1.0,
                EgalitarianCapitalPerPerson = 1e305,
                StratifiedCapitalPerPerson = 1e305
            }
        };
        subCpCfg.Validate();
        NearRelative(CivicGovernance.RequiredStewards(subCpCfg, 1e-300, 1e-11, 0.0, false, 0.0), 1e-6,
            "RequiredStewards guards subnormal cp = c * population via log-space fallback", 1e-12);
        NearRelative(CivicGovernance.RequiredCivicCapital(subCpCfg, 1e-300, 1e-11, 0.0), 1e-6,
            "RequiredCivicCapital guards subnormal cp = c * population via log-space fallback", 1e-12);

        var subCapRateCfg = new SimulationConfig
        {
            Civic = new CivicGovernanceSettings
            {
                Enabled = true,
                EgalitarianCapitalPerPerson = 2.5e-316,
                StratifiedCapitalPerPerson = 2.5e-316
            }
        };
        subCapRateCfg.Validate();
        double expectedSubCap = Math.Exp(Math.Log(1e-11) + Math.Log(1e308) + Math.Log(2.5e-316));
        NearRelative(CivicGovernance.RequiredCivicCapital(subCapRateCfg, 1e308, 1e-11, 0.0), expectedSubCap,
            "RequiredCivicCapital guards subnormal rate via log-space fallback", 1e-12);

        // =========================================================================
        // Regression (Iter 4 Bug #3): Comprehensive non-finite (NaN / ±Infinity)
        // safety matrix across EVERY public pure helper in CivicGovernance.cs.
        // =========================================================================
        var w = CivicFixture(sanitation: true);
        var cfg = w.Config;
        double[] badDoubles = { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, -0.0, double.Epsilon, -double.Epsilon, 0.5, -0.5, 1.0, -1.0, double.MaxValue, -double.MaxValue };
        double[] rateYearVals = { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, -0.0, -1.0, double.Epsilon, 0.025, 100.0, double.MaxValue };

        // 3a. Relax: exact chal_iter3_1 reproducers + exhaustive 4D non-finite matrix
        Near(CivicGovernance.Relax(0.5, double.PositiveInfinity, double.Epsilon, double.Epsilon), 0.5, "Relax(0.5, +Inf, eps, eps) finite == 0.5", 1e-12);
        Near(CivicGovernance.Relax(double.PositiveInfinity, 0.5, 100.0, 20.0), 0.5, "Relax(+Inf, 0.5, 100, 20) finite == 0.5", 1e-12);
        Check(CivicGovernance.Relax(double.PositiveInfinity, double.PositiveInfinity, 0.025, 20.0) == double.MaxValue, "Relax(+Inf, +Inf, 0.025, 20) saturates to double.MaxValue");
        Check(IsFinite(CivicGovernance.Relax(double.NegativeInfinity, double.PositiveInfinity, 0.025, 20.0)), "Relax(-Inf, +Inf, 0.025, 20) is finite");
        Check(CivicGovernance.Relax(double.PositiveInfinity, 0.5, 0.0, 20.0) == double.MaxValue, "Relax(+Inf, 0.5, 0, 20) saturates to double.MaxValue");
        Near(CivicGovernance.Relax(0.5, double.NaN, 0.025, 20.0), 0.5, "Relax(0.5, NaN, 0.025, 20) returns current 0.5", 1e-12);
        Check(IsFinite(CivicGovernance.Relax(double.NaN, 0.5, 0.025, 20.0)), "Relax(NaN, 0.5, 0.025, 20) is finite");
        string relaxFail = null;
        foreach (double cur in badDoubles)
            foreach (double tgt in badDoubles)
                foreach (double r in rateYearVals)
                    foreach (double y in rateYearVals)
                    {
                        double v = CivicGovernance.Relax(cur, tgt, r, y);
                        if (!IsFinite(v)) { relaxFail = $"Relax({cur},{tgt},{r},{y})={v}"; break; }
                    }
        Check(relaxFail == null, "Relax returns finite across entire 4D non-finite matrix" + (relaxFail == null ? "" : " (" + relaxFail + ")"));

        // 3b. AlignmentFriction: exact NaN/±Inf checks + full 2D matrix
        Near(CivicGovernance.AlignmentFriction(cfg, double.NaN, 0.0), 1.0, "AlignmentFriction(NaN, 0) == 1", 1e-12);
        Near(CivicGovernance.AlignmentFriction(cfg, 0.0, double.NaN), 1.0, "AlignmentFriction(0, NaN) == 1", 1e-12);
        Near(CivicGovernance.AlignmentFriction(cfg, double.PositiveInfinity, double.NegativeInfinity), 1.0 + 2.0 * cfg.Civic.AlignmentFrictionRate,
            "AlignmentFriction(+Inf, -Inf) clamps to ±1", 1e-12);
        string afFail = null;
        foreach (double p in badDoubles)
            foreach (double pe in badDoubles)
            {
                double v = CivicGovernance.AlignmentFriction(cfg, p, pe);
                if (!IsFinite(v) || v < 1.0) { afFail = $"AlignmentFriction({p},{pe})={v}"; break; }
            }
        Check(afFail == null, "AlignmentFriction finite >= 1 across 2D non-finite matrix" + (afFail == null ? "" : " (" + afFail + ")"));

        // 3c. Four *For helpers: exact NaN spot checks + full 3D matrix
        Near(CivicGovernance.CrowdingMitigationFor(cfg, double.NaN, 0, 0.5), 1.0, "CrowdingMitigationFor(NaN,0,0.5) == 1", 1e-12);
        Near(CivicGovernance.CrowdingMitigationFor(cfg, 0.5, double.NaN, 0.5), 1.0, "CrowdingMitigationFor(0.5,NaN,0.5) == 1", 1e-12);
        Near(CivicGovernance.CrowdingMitigationFor(cfg, 0.5, 0, double.NaN), 1.0, "CrowdingMitigationFor(0.5,0,NaN) == 1", 1e-12);
        Near(CivicGovernance.DiseaseMitigationFor(cfg, double.NaN, 0, 0.5), 1.0, "DiseaseMitigationFor(NaN,0,0.5) == 1", 1e-12);
        Near(CivicGovernance.DiseaseMitigationFor(cfg, 0.5, double.NaN, 0.5), 1.0, "DiseaseMitigationFor(0.5,NaN,0.5) == 1", 1e-12);
        Near(CivicGovernance.DiseaseMitigationFor(cfg, 0.5, 0, double.NaN), 1.0, "DiseaseMitigationFor(0.5,0,NaN) == 1", 1e-12);
        Near(CivicGovernance.CohesionMultiplierFor(cfg, double.NaN, 0, 0.5), 1.0, "CohesionMultiplierFor(NaN,0,0.5) == 1", 1e-12);
        Near(CivicGovernance.CohesionMultiplierFor(cfg, 0.5, double.NaN, 0.5), 1.0, "CohesionMultiplierFor(0.5,NaN,0.5) == 1", 1e-12);
        Near(CivicGovernance.CohesionMultiplierFor(cfg, 0.5, 0, double.NaN), 1.0, "CohesionMultiplierFor(0.5,0,NaN) == 1", 1e-12);
        Near(CivicGovernance.QoLEqualityMultiplierFor(cfg, double.NaN, 0, 0.5), 1.0, "QoLEqualityMultiplierFor(NaN,0,0.5) == 1", 1e-12);
        Near(CivicGovernance.QoLEqualityMultiplierFor(cfg, 0.5, double.NaN, 0.5), 1.0, "QoLEqualityMultiplierFor(0.5,NaN,0.5) == 1", 1e-12);
        Near(CivicGovernance.QoLEqualityMultiplierFor(cfg, 0.5, 0, double.NaN), 1.0, "QoLEqualityMultiplierFor(0.5,0,NaN) == 1", 1e-12);
        string forFail = null;
        foreach (double cv in badDoubles)
            foreach (double pv in badDoubles)
                foreach (double cov in badDoubles)
                {
                    double mc = CivicGovernance.CrowdingMitigationFor(cfg, cv, pv, cov);
                    double md = CivicGovernance.DiseaseMitigationFor(cfg, cv, pv, cov);
                    double coh = CivicGovernance.CohesionMultiplierFor(cfg, cv, pv, cov);
                    double qol = CivicGovernance.QoLEqualityMultiplierFor(cfg, cv, pv, cov);
                    if (!IsFinite(mc) || mc < cfg.Civic.MinCrowdingMitigation || mc > 1.0) { forFail = $"Mc({cv},{pv},{cov})={mc}"; break; }
                    if (!IsFinite(md) || md < cfg.Civic.MinDiseaseMitigation || md > 1.0) { forFail = $"Md({cv},{pv},{cov})={md}"; break; }
                    if (!IsFinite(coh) || coh < 1.0) { forFail = $"Coh({cv},{pv},{cov})={coh}"; break; }
                    if (!IsFinite(qol) || qol < 0.0) { forFail = $"QoL({cv},{pv},{cov})={qol}"; break; }
                }
        Check(forFail == null, "All four *For helpers finite and bounded across 3D non-finite matrix" + (forFail == null ? "" : " (" + forFail + ")"));

        // 3d. RequiredStewards, RequiredCivicCapital, RequiredSanitationCapital non-finite matrix
        Check(CivicGovernance.RequiredStewards(cfg, double.PositiveInfinity, 0.5, 0.0, false, 0.0) == double.MaxValue, "RequiredStewards(+Inf pop) == double.MaxValue");
        Check(CivicGovernance.RequiredCivicCapital(cfg, double.PositiveInfinity, 0.5, 0.0) == double.MaxValue, "RequiredCivicCapital(+Inf pop) == double.MaxValue");
        Check(CivicGovernance.RequiredSanitationCapital(cfg, double.PositiveInfinity) == double.MaxValue, "RequiredSanitationCapital(+Inf pop) == double.MaxValue");
        Check(CivicGovernance.RequiredSanitationCapital(cfg, double.NaN) == 0.0 && CivicGovernance.RequiredSanitationCapital(cfg, double.NegativeInfinity) == 0.0,
            "RequiredSanitationCapital(NaN / -Inf) == 0");
        double[] reqVals = { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, -0.0, double.Epsilon, 1000.0, double.MaxValue };
        string reqFail = null;
        foreach (double pop in reqVals)
        {
            double rs = CivicGovernance.RequiredSanitationCapital(cfg, pop);
            if (!IsFinite(rs) || rs < 0.0) { reqFail = $"ReqSanit({pop})={rs}"; break; }
            foreach (double comp in reqVals)
                foreach (double strat in reqVals)
                {
                    double rcap = CivicGovernance.RequiredCivicCapital(cfg, pop, comp, strat);
                    if (!IsFinite(rcap) || rcap < 0.0) { reqFail = $"ReqCap({pop},{comp},{strat})={rcap}"; break; }
                    foreach (double envS in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0 })
                        foreach (bool has3A in new[] { false, true })
                        {
                            double rst = CivicGovernance.RequiredStewards(cfg, pop, comp, strat, has3A, envS);
                            if (!IsFinite(rst) || rst < 0.0) { reqFail = $"ReqStew({pop},{comp},{strat},{has3A},{envS})={rst}"; break; }
                        }
                }
        }
        Check(reqFail == null, "RequiredStewards/RequiredCivicCapital/RequiredSanitationCapital finite >= 0 across non-finite matrix" + (reqFail == null ? "" : " (" + reqFail + ")"));

        // 3e. TargetStratification & SteeringOffset non-finite matrix
        string steerFail = null;
        foreach (FissionPolicy fp in new[] { FissionPolicy.Neutral, FissionPolicy.Encourage, FissionPolicy.Discourage, (FissionPolicy)99 })
            foreach (ImmigrationPolicy ip in new[] { ImmigrationPolicy.Neutral, ImmigrationPolicy.Open, ImmigrationPolicy.Restricted, (ImmigrationPolicy)99 })
                foreach (CivicFocus cf in new[] { CivicFocus.Auto, CivicFocus.Communal, CivicFocus.Centralized, (CivicFocus)99 })
                {
                    double off = CivicGovernance.SteeringOffset(cfg, fp, ip, cf);
                    if (!IsFinite(off)) { steerFail = $"SteeringOffset({fp},{ip},{cf})={off}"; break; }
                    foreach (double envS in badDoubles)
                    {
                        double ts = CivicGovernance.TargetStratification(cfg, envS, fp, ip, cf);
                        if (!IsFinite(ts) || ts < -1.0 || ts > 1.0) { steerFail = $"TargetStratification({envS},{fp},{ip},{cf})={ts}"; break; }
                    }
                }
        Check(steerFail == null, "SteeringOffset and TargetStratification finite and bounded across non-finite matrix" + (steerFail == null ? "" : " (" + steerFail + ")"));

        // 3f. All Camp-based public pure helpers under corrupted/non-finite Camp fields (including 0 * +Inf in DaughterCoordinates)
        var campW = CivicFixture(sanitation: true);
        var cc = campW.Camps[0];
        cc.Population = 1000;
        cc.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        cc.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        cc.People.CivicPrecedents.Add(CivicPrecedents.TokensAndAccounting);
        cc.People.CivicPrecedents.Add(CivicPrecedents.PlannedQuartersAndDrainage);
        cc.Workforce = new PopulationLedger();
        cc.Workforce.Assign(WorkforceRoles.Steward, 200, 1000);

        var zeroRetW = CivicFixture(sanitation: true);
        zeroRetW.Config.Civic.DaughterComplexityRetention = 0.0;
        zeroRetW.Config.Civic.DaughterStratificationRetention = 0.0;
        zeroRetW.Config.Validate();

        double[] campBad = { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, 0.5, double.MaxValue, -double.MaxValue };
        string campFail = null;
        foreach (double v in campBad)
        {
            cc.CivicComplexity = v;
            cc.CivicStratification = v;
            cc.LastEnvironmentalComplexity = v;
            cc.LastEnvironmentalStratification = v;
            cc.LastCircumscription = v;
            cc.CivicCapital = v;
            cc.SanitationCapital = v;
            cc.ActiveSanitationCapital = v;
            cc.UseActiveSanitationCapital = false;
            cc.SanitationWaterRatio = v;
            cc.RecentTradeVolume = v;

            double cov = CivicGovernance.CivicCoverage(campW, cc);
            double scov = CivicGovernance.SanitationCoverage(campW, cc);
            double acap = CivicGovernance.ActiveSanitationCapital(cc);
            double mc = CivicGovernance.CrowdingMitigation(campW, cc);
            double md = CivicGovernance.DiseaseMitigation(campW, cc);
            double coh = CivicGovernance.CohesionMultiplier(campW, cc);
            double qol = CivicGovernance.QoLEqualityMultiplier(campW, cc);
            double pw = CivicGovernance.PublicWorksMobilizationMultiplier(campW, cc);
            double gr = CivicGovernance.GranaryStorageMultiplier(campW, cc);
            double grOv = CivicGovernance.GranaryStorageMultiplier(campW, cc, v);
            double em = CivicGovernance.ExitMismatch(cc);
            double nc = CivicGovernance.NextComplexity(campW, cc, v);
            var dc1 = CivicGovernance.DaughterCoordinates(campW, cc);
            var dcZero = CivicGovernance.DaughterCoordinates(zeroRetW, cc);
            double af = CivicGovernance.AlignmentFriction(campW, cc);
            double rs = CivicGovernance.RequiredStewards(campW, cc, v);
            double rc = CivicGovernance.RequiredCivicCapital(campW, cc, v);
            string lbl = CivicGovernance.EffectiveFocusLabel(cc);

            if (!IsFinite(cov) || cov < 0 || cov > 1 ||
                !IsFinite(scov) || scov < 0 || scov > 1 ||
                !IsFinite(acap) || acap < 0 ||
                !IsFinite(mc) || mc < campW.Config.Civic.MinCrowdingMitigation || mc > 1 ||
                !IsFinite(md) || md < campW.Config.Civic.MinDiseaseMitigation || md > 1 ||
                !IsFinite(coh) || coh < 1 ||
                !IsFinite(qol) || qol < 0 ||
                !IsFinite(pw) || pw < 1 ||
                !IsFinite(gr) || gr < 1 ||
                !IsFinite(grOv) || grOv < 1 ||
                !IsFinite(em) || em < 0 ||
                !IsFinite(nc) || nc < 0 || nc > 1 ||
                !IsFinite(dc1.Complexity) || dc1.Complexity < 0 || dc1.Complexity > 1 ||
                !IsFinite(dc1.Stratification) || dc1.Stratification < -1 || dc1.Stratification > 1 ||
                !IsFinite(dcZero.Complexity) || dcZero.Complexity < 0 || dcZero.Complexity > 1 ||
                !IsFinite(dcZero.Stratification) || dcZero.Stratification < -1 || dcZero.Stratification > 1 ||
                !IsFinite(af) || af < 1 ||
                !IsFinite(rs) || rs < 0 ||
                !IsFinite(rc) || rc < 0 ||
                string.IsNullOrEmpty(lbl))
            {
                campFail = $"v={v} cov={cov} scov={scov} acap={acap} em={em} nc={nc} dc=({dc1.Complexity},{dc1.Stratification}) dc0=({dcZero.Complexity},{dcZero.Stratification})";
                break;
            }
        }
        Check(campFail == null, "All camp-based public pure helpers stay finite & bounded on non-finite Camp fields" + (campFail == null ? "" : " (" + campFail + ")"));

        // Single-field NaN isolation checks on Camp helpers that previously leaked NaN
        cc.CivicComplexity = 0.5; cc.CivicStratification = 0.3; cc.LastEnvironmentalComplexity = 0.5;
        cc.LastEnvironmentalStratification = -0.2; cc.LastCircumscription = 0.2;
        cc.CivicCapital = 100; cc.SanitationCapital = 100; cc.SanitationWaterRatio = 0.8; cc.RecentTradeVolume = 10;
        cc.CivicCapital = double.NaN;
        Check(CivicGovernance.CivicCoverage(campW, cc) == 0.0, "CivicCoverage with NaN CivicCapital returns 0 (no NaN)");
        cc.CivicCapital = 100;
        cc.SanitationWaterRatio = double.NaN;
        Check(CivicGovernance.SanitationCoverage(campW, cc) == 0.0, "SanitationCoverage with NaN SanitationWaterRatio returns 0 (no NaN)");
        cc.SanitationWaterRatio = 0.8;
        cc.LastCircumscription = double.NaN;
        Check(CivicGovernance.ExitMismatch(cc) == 0.0, "ExitMismatch with NaN LastCircumscription returns 0 (no NaN)");
        cc.LastCircumscription = 0.2;
        cc.LastEnvironmentalComplexity = double.NaN;
        Check(IsFinite(CivicGovernance.NextComplexity(campW, cc, 20.0)), "NextComplexity with NaN LastEnvironmentalComplexity stays finite");
        cc.LastEnvironmentalComplexity = 0.5;
        cc.RecentTradeVolume = double.PositiveInfinity;
        var envInfTrade = CivicGovernance.EvaluateEnvironment(campW, cc);
        Check(IsFinite(envInfTrade.D1) && IsFinite(envInfTrade.D2) && IsFinite(envInfTrade.D3) && IsFinite(envInfTrade.D4) &&
              IsFinite(envInfTrade.EnvironmentalComplexity) && IsFinite(envInfTrade.EnvironmentalStratification),
            "EvaluateEnvironment stays finite with +Infinity RecentTradeVolume");

        // TradeRecordVolumePersonYears non-finite quantities across all TradeGoods
        string tradeFail = null;
        foreach (TradeGood good in Enum.GetValues(typeof(TradeGood)))
            foreach (double qty in badDoubles)
            {
                double tv = CivicGovernance.TradeRecordVolumePersonYears(cfg, new TradeRecord { Want = good, WantQuantity = qty, Give = good, GiveQuantity = qty });
                if (!IsFinite(tv) || tv < 0) { tradeFail = $"TradeVol({good},{qty})={tv}"; break; }
            }
        Check(tradeFail == null, "TradeRecordVolumePersonYears finite >= 0 across all TradeGoods and non-finite quantities" + (tradeFail == null ? "" : " (" + tradeFail + ")"));

        // =========================================================================
        // Regression (Iter 5 Groups A-E): Irrigated boost, deep subnormal cp,
        // UseActiveSanitationCapital, F2-F6 edge cases, and Relax/TargetStratification.
        // =========================================================================
        static bool ValidEnv(CivicEnvironmentDrivers e)
            => IsFinite(e.D1) && e.D1 >= 0 && e.D1 <= 1
            && IsFinite(e.D2) && e.D2 >= 0 && e.D2 <= 1
            && IsFinite(e.D3) && e.D3 >= 0 && e.D3 <= 1
            && IsFinite(e.D4) && e.D4 >= 0 && e.D4 <= 1
            && IsFinite(e.RawComplexity) && e.RawComplexity >= 0 && e.RawComplexity <= 1
            && IsFinite(e.EnvironmentalComplexity) && e.EnvironmentalComplexity >= 0 && e.EnvironmentalComplexity <= 1
            && IsFinite(e.EnvironmentalStratification) && e.EnvironmentalStratification >= -1 && e.EnvironmentalStratification <= 1;

        // -------------------------------------------------------------------------
        // Group A (F1): CivicLocalIndex.Evaluate irrigated boost overflow & 0 * +Inf
        // -------------------------------------------------------------------------
        // A1: Valid SoilCapacityGain = 2e307, WaterService = 0, Moisture = 0.05f ->
        // SaveCodec roundtrip + baseline parity + live TurnResolver.Resolve + invariants.
        var wA1 = CivicFixture(sanitation: true);
        wA1.Config.Agriculture = new AgriculturalTechnologySettings
        {
            IrrigationEnabled = false,
            TargetMoisture = 0.50,
            SoilCapacityGain = 2e307
        };
        wA1.Config.Validate();
        var cA1 = wA1.Camps[0];
        int nA1 = cA1.Tile;
        for (int s = 0; s < 4; s++)
        {
            wA1.Tiles[nA1].Seasons[s].Moisture = 0.05f;
            if (wA1.Tiles[nA1].Seasons[s].MaxFood <= 0f) wA1.Tiles[nA1].Seasons[s].MaxFood = 200f;
        }
        wA1.Canals = new List<CanalSegment>
        {
            new CanalSegment
            {
                Id = 1, Source = nA1, Tile = nA1, From = nA1, Parent = -1,
                Operator = cA1.Id, Construction = 1.0, Condition = 1.0, Throughput = 1000
            }
        };
        wA1.IrrigatedFields = new List<IrrigatedField>
        {
            new IrrigatedField
            {
                Tile = nA1, Endpoint = 1, Operator = cA1.Id,
                Soil = 1.0, Area = 1.0, WaterService = 0.0
            }
        };
        IrrigationEconomy.Validate(wA1);
        TurnResolver.Invariants(wA1);
        var loadedA1 = SaveCodec.Load(SaveCodec.Save(wA1));
        var baseA1 = SaveCodec.Load(SaveCodec.Save(wA1));
        baseA1.Config.Agriculture.SoilCapacityGain = 0.0;
        var envA1 = CivicGovernance.EvaluateEnvironment(loadedA1, loadedA1.Camps[0]);
        var baseEnvA1 = CivicGovernance.EvaluateEnvironment(baseA1, baseA1.Camps[0]);
        Check(ValidEnv(envA1), "F1 A1 EvaluateEnvironment valid & finite for valid SoilCapacityGain=2e307, WaterService=0");
        Check(baseEnvA1.D2 > 0.0 && baseEnvA1.D2 < 0.72, "F1 A1 fixture has viable unblocked exit ring (0 < D2 < 0.72)");
        NearRelative(envA1.D2, baseEnvA1.D2, "F1 A1 WaterService=0 matches unboosted SoilCapacityGain=0 D2 exactly", 1e-12);
        new TurnResolver().Resolve(loadedA1, TurnPlanner.Prepare(loadedA1, new Dictionary<int, List<Order>>()));
        CivicGovernance.ValidateInvariants(loadedA1);
        TurnResolver.Invariants(loadedA1);
        var lastRecA1 = loadedA1.History[loadedA1.History.Count - 1];
        Check(lastRecA1.CivicReports != null && lastRecA1.CivicReports.Count >= 1
            && IsFinite(lastRecA1.CivicReports[0].Circumscription)
            && IsFinite(lastRecA1.CivicReports[0].EnvironmentalStratification),
            "F1 A1 live TurnResolver.Resolve commits finite CivicReports");
        Check(SaveCodec.Fingerprint(SaveCodec.Load(SaveCodec.Save(loadedA1))) == SaveCodec.Fingerprint(loadedA1),
            "F1 A1 post-turn SaveCodec roundtrip fingerprint matches exactly");

        // A2: Valid IrrigationEnabled=true, SoilCapacityGain=double.MaxValue, arid moisture {0.10f, 0.20f}
        foreach (float aridM in new[] { 0.10f, 0.20f })
        {
            var wA2 = CivicFixture(sanitation: true);
            wA2.Config.Agriculture = new AgriculturalTechnologySettings
            {
                IrrigationEnabled = true,
                TargetMoisture = 0.50,
                SoilCapacityGain = double.MaxValue
            };
            wA2.Config.Validate();
            var cA2 = wA2.Camps[0];
            int nA2 = cA2.Tile;
            for (int s = 0; s < 4; s++)
            {
                wA2.Tiles[nA2].Seasons[s].Moisture = aridM;
                if (wA2.Tiles[nA2].Seasons[s].MaxFood <= 0f) wA2.Tiles[nA2].Seasons[s].MaxFood = 200f;
            }
            wA2.Canals = new List<CanalSegment>
            {
                new CanalSegment
                {
                    Id = 1, Source = nA2, Tile = nA2, From = nA2, Parent = -1,
                    Operator = cA2.Id, Construction = 1.0, Condition = 1.0, Throughput = 1000
                }
            };
            wA2.IrrigatedFields = new List<IrrigatedField>
            {
                new IrrigatedField
                {
                    Tile = nA2, Endpoint = 1, Operator = cA2.Id,
                    Soil = 1.0, Area = 1.0, WaterService = 0.0
                }
            };
            IrrigationEconomy.Validate(wA2);
            var loadedA2 = SaveCodec.Load(SaveCodec.Save(wA2));
            var envA2 = CivicGovernance.EvaluateEnvironment(loadedA2, loadedA2.Camps[0]);
            var zeroA2 = SaveCodec.Load(SaveCodec.Save(wA2));
            zeroA2.Config.Agriculture.SoilCapacityGain = 0.0;
            var zeroEnvA2 = CivicGovernance.EvaluateEnvironment(zeroA2, zeroA2.Camps[0]);
            Check(ValidEnv(envA2), $"F1 A2 valid SoilCapacityGain=MaxValue aridM={aridM} stays finite");
            NearRelative(envA2.D2, zeroEnvA2.D2, $"F1 A2 WaterService=0 matches zero-gain D2 at aridM={aridM}", 1e-12);
            var recA2 = new TurnRecord();
            CivicGovernance.Close(loadedA2, recA2, 20.0);
            CivicGovernance.ValidateInvariants(loadedA2);
            Check(recA2.CivicReports != null && recA2.CivicReports.Count >= 1
                && IsFinite(recA2.CivicReports[0].Circumscription)
                && IsFinite(recA2.CivicReports[0].EnvironmentalStratification),
                $"F1 A2 Close commits finite report at aridM={aridM}");
        }

        // A3: Default config (SoilCapacityGain=0.50) + non-finite/extreme Soil + zero/non-finite WaterService
        var wA3 = CivicFixture(sanitation: true);
        wA3.Config.Agriculture = new AgriculturalTechnologySettings { IrrigationEnabled = true, TargetMoisture = 0.50, SoilCapacityGain = 0.50 };
        var cA3 = wA3.Camps[0];
        int nA3 = cA3.Tile;
        for (int s = 0; s < 4; s++)
        {
            wA3.Tiles[nA3].Seasons[s].Moisture = 0.10f;
            if (wA3.Tiles[nA3].Seasons[s].MaxFood <= 0f) wA3.Tiles[nA3].Seasons[s].MaxFood = 200f;
        }
        wA3.Canals = new List<CanalSegment>
        {
            new CanalSegment
            {
                Id = 1, Source = nA3, Tile = nA3, From = nA3, Parent = -1,
                Operator = cA3.Id, Construction = 1.0, Condition = 1.0, Throughput = 1000
            }
        };
        var fA3 = new IrrigatedField { Tile = nA3, Endpoint = 1, Operator = cA3.Id, Soil = 0.0, Area = 1.0, WaterService = 0.0 };
        wA3.IrrigatedFields = new List<IrrigatedField> { fA3 };
        double unboostedD2A3 = CivicGovernance.EvaluateEnvironment(wA3, cA3).D2;
        Check(unboostedD2A3 > 0.0 && unboostedD2A3 < 0.72, "F1 A3 unboosted D2 has active exit ring");
        string a3Fail = null;
        foreach (double soilVal in new[] { double.PositiveInfinity, double.MaxValue, double.NaN, double.NegativeInfinity })
            foreach (double wsVal in new[] { 0.0, double.NaN, -1.0, double.NegativeInfinity })
            {
                fA3.Soil = soilVal;
                fA3.WaterService = wsVal;
                var envA3 = CivicGovernance.EvaluateEnvironment(wA3, cA3);
                if (!ValidEnv(envA3) || Math.Abs(envA3.D2 - unboostedD2A3) > 1e-12 * Math.Abs(unboostedD2A3))
                {
                    a3Fail = $"soil={soilVal},ws={wsVal},D2={envA3.D2}!=base {unboostedD2A3}";
                    break;
                }
            }
        Check(a3Fail == null, "F1 A3 default config + extreme/non-finite Soil + zero/non-finite WaterService matches unboosted D2" + (a3Fail == null ? "" : " (" + a3Fail + ")"));
        foreach (double wsClose in new[] { 0.0, double.NaN })
        {
            fA3.Soil = double.PositiveInfinity;
            fA3.WaterService = wsClose;
            CivicGovernance.Close(wA3, new TurnRecord(), 20.0);
            CivicGovernance.ValidateInvariants(wA3);
        }
        Check(true, "F1 A3 Close + ValidateInvariants pass for Soil=+Inf with WaterService in {0, NaN}");

        // A4: Barren arid tile (pn == 0) parity across WaterService values
        var wA4 = CivicFixture(sanitation: true);
        var cA4 = wA4.Camps[0];
        int nA4 = cA4.Tile;
        for (int s = 0; s < 4; s++)
        {
            wA4.Tiles[nA4].Seasons[s].Moisture = 0.10f;
            wA4.Tiles[nA4].Seasons[s].MaxFood = 0f;
            wA4.Tiles[nA4].Seasons[s].MaxPotentialFood = 0f;
        }
        wA4.Canals = new List<CanalSegment>
        {
            new CanalSegment
            {
                Id = 1, Source = nA4, Tile = nA4, From = nA4, Parent = -1,
                Operator = cA4.Id, Construction = 1.0, Condition = 1.0, Throughput = 1000
            }
        };
        var fA4 = new IrrigatedField { Tile = nA4, Endpoint = 1, Operator = cA4.Id, Soil = 1.0, Area = 1.0, WaterService = 0.0 };
        wA4.IrrigatedFields = new List<IrrigatedField> { fA4 };
        wA4.Config.Agriculture = new AgriculturalTechnologySettings { IrrigationEnabled = true, TargetMoisture = 0.50, SoilCapacityGain = 0.50 };
        double barrenDryD2 = CivicGovernance.EvaluateEnvironment(wA4, cA4).D2;
        Check(barrenDryD2 > 0.0, "F1 A4 barren tile baseline D2 > 0");
        string a4Fail = null;
        foreach (var (gainVal, soilVal) in new[] { (double.MaxValue, 1.0), (0.50, double.PositiveInfinity) })
        {
            wA4.Config.Agriculture.SoilCapacityGain = gainVal;
            fA4.Soil = soilVal;
            foreach (double wsVal in new[] { 0.0, 0.5, 1.0, double.NaN, double.PositiveInfinity })
            {
                fA4.WaterService = wsVal;
                var envA4 = CivicGovernance.EvaluateEnvironment(wA4, cA4);
                if (!ValidEnv(envA4) || Math.Abs(envA4.D2 - barrenDryD2) > 1e-12 * Math.Abs(barrenDryD2))
                {
                    a4Fail = $"gain={gainVal},soil={soilVal},ws={wsVal},D2={envA4.D2}!=barren {barrenDryD2}";
                    break;
                }
            }
            if (a4Fail != null) break;
        }
        Check(a4Fail == null, "F1 A4 barren pn==0 tile preserves exact dry D2 across all WaterService & extreme boost inputs" + (a4Fail == null ? "" : " (" + a4Fail + ")"));
        wA4.Config.Agriculture.SoilCapacityGain = double.MaxValue;
        fA4.Soil = 1.0;
        fA4.WaterService = 1.0;
        CivicGovernance.Close(wA4, new TurnRecord(), 20.0);
        CivicGovernance.ValidateInvariants(wA4);
        Check(true, "F1 A4 barren tile Close + ValidateInvariants pass");

        // A5: Fertile (pn > 0) overflow saturation + normal-domain monotonicity
        var wA5 = CivicFixture(sanitation: true);
        wA5.Config.Agriculture = new AgriculturalTechnologySettings { IrrigationEnabled = true, TargetMoisture = 0.50, SoilCapacityGain = 0.50 };
        var cA5 = wA5.Camps[0];
        int nA5 = cA5.Tile;
        for (int s = 0; s < 4; s++)
        {
            wA5.Tiles[nA5].Seasons[s].Moisture = 0.10f;
            if (wA5.Tiles[nA5].Seasons[s].MaxFood <= 0f) wA5.Tiles[nA5].Seasons[s].MaxFood = 200f;
        }
        wA5.Canals = new List<CanalSegment>
        {
            new CanalSegment
            {
                Id = 1, Source = nA5, Tile = nA5, From = nA5, Parent = -1,
                Operator = cA5.Id, Construction = 1.0, Condition = 1.0, Throughput = 1000
            }
        };
        var fA5 = new IrrigatedField { Tile = nA5, Endpoint = 1, Operator = cA5.Id, Soil = 1.0, Area = 1.0, WaterService = 0.0 };
        wA5.IrrigatedFields = new List<IrrigatedField> { fA5 };
        double unboostedD2A5 = CivicGovernance.EvaluateEnvironment(wA5, cA5).D2;
        foreach (var (gainVal, soilVal, label) in new[] { (double.MaxValue, 1.0, "gain=MaxValue,soil=1"), (0.50, double.PositiveInfinity, "gain=0.5,soil=+Inf") })
        {
            wA5.Config.Agriculture.SoilCapacityGain = gainVal;
            fA5.Soil = soilVal;
            fA5.WaterService = 1.0;
            var envSat = CivicGovernance.EvaluateEnvironment(wA5, cA5);
            Check(ValidEnv(envSat) && envSat.D2 >= 0.72 - 1e-12 && envSat.D2 > unboostedD2A5,
                $"F1 A5 fertile overflow saturation yields high circumscription D2 >= 0.72 and > unboosted ({label}, D2={envSat.D2}, base={unboostedD2A5})");
            CivicGovernance.Close(wA5, new TurnRecord(), 20.0);
            CivicGovernance.ValidateInvariants(wA5);
        }
        // Normal-domain monotonicity: D2(0) < D2(0.6) < D2(1.0)
        wA5.Config.Agriculture.SoilCapacityGain = 0.50;
        for (int s = 0; s < 4; s++) wA5.Tiles[nA5].Seasons[s].Moisture = 0.20f;
        fA5.Soil = 0.80;
        fA5.WaterService = 0.0; var envM0 = CivicGovernance.EvaluateEnvironment(wA5, cA5);
        fA5.WaterService = 0.6; var envM60 = CivicGovernance.EvaluateEnvironment(wA5, cA5);
        fA5.WaterService = 1.0; var envM100 = CivicGovernance.EvaluateEnvironment(wA5, cA5);
        Check(ValidEnv(envM0) && ValidEnv(envM60) && ValidEnv(envM100) && envM0.D2 < envM60.D2 && envM60.D2 < envM100.D2,
            $"F1 A5 normal-domain irrigated boost strict monotonicity D2(0)<D2(0.6)<D2(1.0) ({envM0.D2} < {envM60.D2} < {envM100.D2})");

        // A6: 448-case Cartesian matrix checking all 7 ValidEnv fields
        string a6Fail = null;
        int a6Count = 0;
        foreach (bool fertile in new[] { true, false })
        {
            for (int s = 0; s < 4; s++)
            {
                wA5.Tiles[nA5].Seasons[s].Moisture = 0.10f;
                wA5.Tiles[nA5].Seasons[s].MaxFood = fertile ? 200f : 0f;
                wA5.Tiles[nA5].Seasons[s].MaxPotentialFood = fertile ? 200f : 0f;
            }
            foreach (double gainVal in new[] { 0.0, 0.5, 2e307, double.MaxValue })
            {
                wA5.Config.Agriculture.SoilCapacityGain = gainVal;
                foreach (double soilVal in new[] { double.NaN, double.NegativeInfinity, -1.0, 0.0, 1.0, double.MaxValue, double.PositiveInfinity })
                    foreach (double wsVal in new[] { double.NaN, double.NegativeInfinity, -1.0, 0.0, 0.5, 1.0, 2.0, double.PositiveInfinity })
                    {
                        a6Count++;
                        fA5.Soil = soilVal;
                        fA5.WaterService = wsVal;
                        var envMat = CivicGovernance.EvaluateEnvironment(wA5, cA5);
                        if (!ValidEnv(envMat))
                        {
                            a6Fail = $"fertile={fertile},gain={gainVal},soil={soilVal},ws={wsVal},D2={envMat.D2},P={envMat.EnvironmentalStratification}";
                            break;
                        }
                    }
                if (a6Fail != null) break;
            }
            if (a6Fail != null) break;
        }
        Check(a6Count == 448 && a6Fail == null, "F1 A6 448-case Cartesian irrigated-boost matrix satisfies all 7 ValidEnv invariants" + (a6Fail == null ? "" : " (" + a6Fail + ")"));

        // -------------------------------------------------------------------------
        // Group B: Deep subnormal cp = c * population regression (closes L1478/L1480 blind spot)
        // -------------------------------------------------------------------------
        var subCpDeepCfg = new SimulationConfig
        {
            Civic = new CivicGovernanceSettings
            {
                Enabled = true,
                EgalitarianStewardRate = 1e308,
                StratifiedStewardRate = 1e308,
                EgalitarianStewardExponent = 1.0,
                StratifiedStewardExponent = 1.0,
                EgalitarianCapitalPerPerson = 1e308,
                StratifiedCapitalPerPerson = 1e308
            }
        };
        subCpDeepCfg.Validate();
        NearRelative(CivicGovernance.RequiredStewards(subCpDeepCfg, 5.0e-308, 1e-11, 0.0, false, 0.0), 5.0e-11,
            "RequiredStewards guards deep subnormal cp (5e-319) via log-space fallback", 1e-12);
        NearRelative(CivicGovernance.RequiredCivicCapital(subCpDeepCfg, 5.0e-308, 1e-11, 0.0), 5.0e-11,
            "RequiredCivicCapital guards deep subnormal cp (5e-319) via log-space fallback", 1e-12);
        NearRelative(CivicGovernance.RequiredStewards(subCpDeepCfg, 2.5e-308, 2.0e-12, 0.0, false, 0.0), 5.0e-12,
            "RequiredStewards guards deep subnormal cp (5e-320) via log-space fallback", 1e-12);
        NearRelative(CivicGovernance.RequiredCivicCapital(subCpDeepCfg, 2.5e-308, 2.0e-12, 0.0), 5.0e-12,
            "RequiredCivicCapital guards deep subnormal cp (5e-320) via log-space fallback", 1e-12);

        // -------------------------------------------------------------------------
        // Group C: UseActiveSanitationCapital = true non-finite coverage
        // -------------------------------------------------------------------------
        string activeSanFail = null;
        cc.UseActiveSanitationCapital = true;
        cc.SanitationWaterRatio = 1.0;
        foreach (double v in campBad)
        {
            cc.ActiveSanitationCapital = v;
            double acap = CivicGovernance.ActiveSanitationCapital(cc);
            double scov = CivicGovernance.SanitationCoverage(campW, cc);
            if (!IsFinite(acap) || acap < 0.0 || !IsFinite(scov) || scov < 0.0 || scov > 1.0)
            {
                activeSanFail = $"v={v} acap={acap} scov={scov}";
                break;
            }
        }
        cc.UseActiveSanitationCapital = false;
        Check(activeSanFail == null, "Group C UseActiveSanitationCapital=true stays finite & bounded across non-finite ActiveSanitationCapital" + (activeSanFail == null ? "" : " (" + activeSanFail + ")"));

        // -------------------------------------------------------------------------
        // Group D (F2-F6): Additional arithmetic, overflow & non-finite regressions
        // -------------------------------------------------------------------------
        // D-F2a: Valid Consumption = 1e308 + PotStorageFoodPerUnit = 1e308 (Inf/Inf guard + log-space parity)
        var wF2a = CivicFixture(sanitation: true);
        wF2a.Config.Consumption = 1e308;
        wF2a.Config.Neolithic.PotStorageFoodPerUnit = 1e308;
        wF2a.Config.Validate();
        wF2a.Resources = null;
        var cF2a = wF2a.Camps[0];
        cF2a.Population = 100;
        cF2a.PlantFood = 1e308;
        cF2a.AnimalFood = 0;
        cF2a.AquaticFood = 0;
        cF2a.Inventory.Pots = 10.0;
        cF2a.RecentTradeVolume = 0;
        cF2a.People.NeolithicTechnologies.Remove(NeolithicTech.Pottery);
        cF2a.People.Memory = null;
        var envF2a = CivicGovernance.EvaluateEnvironment(wF2a, cF2a);
        double expectedPotYearsF2a = Math.Exp(Math.Log(10.0) + Math.Log(1e308) - Math.Log(100.0) - Math.Log(double.MaxValue));
        double expectedD3F2a = 0.55 * (0.25 * (expectedPotYearsF2a / 15.0));
        Check(ValidEnv(envF2a), "F2a valid Consumption=1e308 & PotStorageFoodPerUnit=1e308 yields ValidEnv");
        NearRelative(envF2a.D3, expectedD3F2a, "F2a log-space potYears matches exact D3", 1e-12);
        CivicGovernance.Close(wF2a, new TurnRecord(), 20.0);
        CivicGovernance.ValidateInvariants(wF2a);
        Check(true, "F2a Close + ValidateInvariants pass on Consumption=1e308");

        // D-F2b: Valid BaseDays = 1e-305 (default Consumption & PotStorage) + Pots = 1e307 -> exact log-space potYears
        var wF2b = CivicFixture(sanitation: true);
        wF2b.Config.BaseDays = 1e-305;
        wF2b.Config.Validate();
        wF2b.Resources = null;
        var cF2b = wF2b.Camps[0];
        cF2b.Population = 100;
        cF2b.PlantFood = 1e308;
        cF2b.AnimalFood = 0;
        cF2b.AquaticFood = 0;
        cF2b.Inventory.Pots = 1e307;
        cF2b.RecentTradeVolume = 0;
        cF2b.People.NeolithicTechnologies.Remove(NeolithicTech.Pottery);
        cF2b.People.Memory = null;
        var envF2b = CivicGovernance.EvaluateEnvironment(wF2b, cF2b);
        double expectedD3F2b = 0.55 * (0.25 * ((80.0 / 365.0) / 15.0));
        Check(ValidEnv(envF2b), "F2b valid BaseDays=1e-305 & Pots=1e307 yields ValidEnv");
        NearRelative(envF2b.D3, expectedD3F2b, "F2b log-space potYears matches exact D3 = 0.55*0.25*(80/365)/15", 1e-12);
        CivicGovernance.Close(wF2b, new TurnRecord(), 20.0);
        CivicGovernance.ValidateInvariants(wF2b);
        Check(true, "F2b Close + ValidateInvariants pass on BaseDays=1e-305");

        // D-F2c: Close Step 2 Tier 3A uStore log-space fallback when fYr * 20 overflows
        var wF2c = CivicFixture(sanitation: true);
        wF2c.Config.BaseDays = 1e-305;
        wF2c.Config.Validate();
        var cF2c = wF2c.Camps[0];
        cF2c.Population = 1200;
        cF2c.PlantFood = 1e308;
        cF2c.AnimalFood = 0;
        cF2c.AquaticFood = 0;
        cF2c.Inventory.Pots = 10.0;
        cF2c.RecentTradeVolume = 0;
        cF2c.Workforce = new PopulationLedger();
        cF2c.Workforce.Assign(WorkforceRoles.Steward, 35, 1200);
        cF2c.People.Progression.Era = Era.Neolithic;
        cF2c.People.NeolithicTechnologies.Remove(NeolithicTech.Pottery);
        cF2c.People.CivicPrecedents = new List<string> { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
        cF2c.People.CivicExperience = new Dictionary<string, double>();
        CivicGovernance.Close(wF2c, new TurnRecord(), 20.0);
        CivicGovernance.ValidateInvariants(wF2c);
        double expectedT3A = 20.0 * 0.65 * (1.0 / 8760.0);
        Check(cF2c.People.CivicExperience.TryGetValue(CivicPrecedents.TokensAndAccounting, out double actualT3A) && actualT3A > 0,
            "F2c Close Tier 3A accrues non-zero experience when fYr * 20 overflows");
        NearRelative(actualT3A, expectedT3A, "F2c Close Tier 3A uStore log-space matches exact 20*0.65/8760", 1e-12);

        // D-F3: Valid LightHousingPermanence=0.0 + overflowing shelter.Light must NOT zero cRaw or Tier-1/Tier-2 accrual
        var wF3 = CivicFixture(sanitation: true);
        wF3.Config.Neolithic.LightHousingPermanence = 0.0;
        wF3.Config.Neolithic.LightHousingPeoplePerUnit = 1e308;
        wF3.Config.Validate();
        var cF3 = wF3.Camps[0];
        cF3.Population = 1000;
        cF3.Inventory.LightHousingUnits = 10.0;
        wF3.FixedHousing.Clear();
        NeolithicProduction.FixedHousingChanged(wF3);
        cF3.StationaryTurns = 5;
        cF3.StationaryYears = 100.0;
        cF3.People.Progression.Era = Era.Neolithic;
        cF3.People.CivicPrecedents = new List<string>();
        cF3.People.CivicExperience = new Dictionary<string, double>();
        var envF3 = CivicGovernance.EvaluateEnvironment(wF3, cF3);
        Check(ValidEnv(envF3) && envF3.RawComplexity > 0.99,
            $"F3 LightHousingPermanence=0 + LightHousingPeoplePerUnit=1e308 preserves cRaw > 0.99 (got {envF3.RawComplexity})");
        CivicGovernance.Close(wF3, new TurnRecord(), 20.0);
        CivicGovernance.ValidateInvariants(wF3);
        Check(cF3.People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double f3T1) && f3T1 > 19.0,
            $"F3 Close accrues Tier-1 experience despite 0 * +Inf shelter.Light (got {f3T1})");
        cF3.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        cF3.People.CivicExperience.Clear();
        cF3.Workforce = new PopulationLedger();
        cF3.Workforce.Assign(WorkforceRoles.Steward, 15, 1000);
        CivicGovernance.Close(wF3, new TurnRecord(), 20.0);
        CivicGovernance.ValidateInvariants(wF3);
        Check(cF3.People.CivicExperience.TryGetValue(CivicPrecedents.CivicOrganization, out double f3T2) && f3T2 > 10.0,
            $"F3 Close accrues Tier-2 experience despite 0 * +Inf shelter.Light (got {f3T2})");

        // D-F4: Poisoned Tile.Seasons (Inf - Inf, Inf / Inf, NaN) stays ValidEnv and passes Close + ValidateInvariants
        var wF4 = CivicFixture(sanitation: true);
        var cF4 = wF4.Camps[0];
        int ringTileF4 = wF4.Map.Radius(cF4.Tile, wF4.Config.FissionRadius)
            .First(t => wF4.Map.Distance(cF4.Tile, t) >= wF4.Config.MinimumCampSpacing && wF4.Tiles[t].TraversableLand);
        for (int s = 0; s < 4; s++) wF4.Tiles[cF4.Tile].Seasons[s].Moisture = float.PositiveInfinity;
        wF4.Tiles[cF4.Tile].Seasons[0].MaxPotentialFood = 0f;
        wF4.Tiles[cF4.Tile].Seasons[1].MaxPotentialFood = float.PositiveInfinity;
        wF4.Tiles[cF4.Tile].Seasons[0].MaxFood = float.NaN;
        wF4.Tiles[cF4.Tile].Seasons[1].MaxFood = float.PositiveInfinity;
        wF4.Tiles[ringTileF4].Seasons[0].MaxFood = float.PositiveInfinity;
        wF4.Tiles[ringTileF4].Seasons[0].MaxFreshwater = float.PositiveInfinity;
        wF4.Tiles[ringTileF4].Seasons[0].WaterRecoveryPerDay = 0f;
        var envF4 = CivicGovernance.EvaluateEnvironment(wF4, cF4);
        Check(ValidEnv(envF4), "F4 poisoned Tile.Seasons (Moisture=+Inf, MaxPotentialFood=0/+Inf, MaxFood=NaN/+Inf) yields ValidEnv");
        CivicGovernance.Close(wF4, new TurnRecord(), 20.0);
        CivicGovernance.ValidateInvariants(wF4);
        Check(true, "F4 poisoned Tile.Seasons Close + ValidateInvariants pass");

        // D-F5: Valid MinimumCampSpacing = int.MaxValue completes immediately and blocks exit ring
        var wF5Open = CivicFixture(sanitation: true);
        wF5Open.Config.MinimumCampSpacing = int.MaxValue;
        wF5Open.Config.Validate();
        var envF5Open = CivicGovernance.EvaluateEnvironment(wF5Open, wF5Open.Camps[0]);
        var wF5Blocked = CivicFixture(sanitation: true);
        wF5Blocked.Config.MinimumCampSpacing = int.MaxValue;
        wF5Blocked.Config.Validate();
        int rivalTileF5 = wF5Blocked.Map.Radius(wF5Blocked.Camps[0].Tile, 3)
            .First(t => t != wF5Blocked.Camps[0].Tile && wF5Blocked.Tiles[t].TraversableLand);
        wF5Blocked.Camps.Add(new Camp { Id = 999, Player = 1, SocietyId = 1, Tile = rivalTileF5, Population = 100 });
        var envF5Blocked = CivicGovernance.EvaluateEnvironment(wF5Blocked, wF5Blocked.Camps[0]);
        Check(ValidEnv(envF5Open) && ValidEnv(envF5Blocked)
            && envF5Blocked.D2 >= 0.72 - 1e-12 && envF5Blocked.D2 > envF5Open.D2,
            $"F5 MinimumCampSpacing=int.MaxValue completes without overflow/hang and blocks exit ring (blocked D2={envF5Blocked.D2} > open D2={envF5Open.D2})");

        // D-F6: hFixed quarter-scaling on 1e308 overflow, animalsByCamp long sum, StationaryYears = double.MaxValue
        // F6b: FixedHousing & LightHousing both 1e308 -> hFixed == 0.5 -> D2 == 0.72 + 0.28 * 0.45 * 0.5 = 0.783
        var wF6b = CivicFixture(sanitation: true);
        wF6b.Config.MinimumCampSpacing = int.MaxValue;
        wF6b.Config.Neolithic.HousingPeoplePerUnit = 1e308;
        wF6b.Config.Neolithic.LightHousingPeoplePerUnit = 1e308;
        wF6b.Config.Validate();
        var cF6b = wF6b.Camps[0];
        foreach (int t in wF6b.Map.Radius(cF6b.Tile, 1)) wF6b.Tiles[t].CultivationCapital = 0;
        wF6b.Canals?.Clear();
        wF6b.Herds?.Clear();
        wF6b.FixedHousing = new List<FixedHousingStock> { new FixedHousingStock { CampId = cF6b.Id, Tile = cF6b.Tile, Units = 1.0, LastUpdatedDays = wF6b.Days } };
        NeolithicProduction.FixedHousingChanged(wF6b);
        cF6b.Inventory.LightHousingUnits = 1.0;
        int rivalTileF6b = wF6b.Map.Radius(cF6b.Tile, 3).First(t => t != cF6b.Tile && wF6b.Tiles[t].TraversableLand);
        wF6b.Camps.Add(new Camp { Id = 998, Player = 1, SocietyId = 1, Tile = rivalTileF6b, Population = 100 });
        var envF6b = CivicGovernance.EvaluateEnvironment(wF6b, cF6b);
        Check(ValidEnv(envF6b), "F6b hFixed overflow case yields ValidEnv");
        NearRelative(envF6b.D2, 0.783, "F6b equal 1e308 fixed & light housing yields hFixed=0.5 (D2=0.783, not 0.720)", 1e-12);

        // F6a: Two domestic herds with 1,500,000,000 animals each (3e9 > int.MaxValue) do not wrap negative
        var wF6a = CivicFixture(sanitation: true);
        wF6a.Config.MinimumCampSpacing = int.MaxValue;
        wF6a.Config.Neolithic.HousingPeoplePerUnit = 1.0;
        wF6a.Config.Validate();
        var cF6a = wF6a.Camps[0];
        cF6a.Population = 100;
        foreach (int t in wF6a.Map.Radius(cF6a.Tile, 1)) wF6a.Tiles[t].CultivationCapital = 0;
        wF6a.Canals?.Clear();
        wF6a.FixedHousing = new List<FixedHousingStock> { new FixedHousingStock { CampId = cF6a.Id, Tile = cF6a.Tile, Units = 1000.0, LastUpdatedDays = wF6a.Days } };
        NeolithicProduction.FixedHousingChanged(wF6a);
        cF6a.Inventory.LightHousingUnits = 0.0;
        wF6a.Herds = new List<Herd>
        {
            new Herd { Id = 1, DomesticCamp = cF6a.Id, Tile = cF6a.Tile, Animals = 1_500_000_000 },
            new Herd { Id = 2, DomesticCamp = cF6a.Id, Tile = cF6a.Tile, Animals = 1_500_000_000 }
        };
        int rivalTileF6a = wF6a.Map.Radius(cF6a.Tile, 3).First(t => t != cF6a.Tile && wF6a.Tiles[t].TraversableLand);
        wF6a.Camps.Add(new Camp { Id = 997, Player = 1, SocietyId = 1, Tile = rivalTileF6a, Population = 100 });
        var envF6a = CivicGovernance.EvaluateEnvironment(wF6a, cF6a);
        double expectedD2F6a = 0.72 + 0.28 * 0.45 * (1000.0 / (1000.0 + 2.0 * 3_000_000_000.0 + 50.0));
        Check(ValidEnv(envF6a), "F6a 3e9 summed domestic herd animals yields ValidEnv");
        NearRelative(envF6a.D2, expectedD2F6a, "F6a animalsByCamp uses 64-bit long (no signed int wrap-around)", 1e-12);

        // F6d: StationaryYears = double.MaxValue with dt = 20 accrues full dt = 20 Tier-1 experience
        var wF6d = CivicFixture(sanitation: true);
        var cF6d = wF6d.Camps[0];
        cF6d.Population = 1000;
        wF6d.FixedHousing.Clear();
        NeolithicProduction.FixedHousingChanged(wF6d);
        cF6d.Inventory.LightHousingUnits = 0.0;
        cF6d.StationaryTurns = 100;
        cF6d.StationaryYears = double.MaxValue;
        cF6d.People.Progression.Era = Era.Neolithic;
        cF6d.People.CivicPrecedents = new List<string>();
        cF6d.People.CivicExperience = new Dictionary<string, double>();
        CivicGovernance.Close(wF6d, new TurnRecord(), 20.0);
        CivicGovernance.ValidateInvariants(wF6d);
        Check(cF6d.People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double f6dT1),
            "F6d StationaryYears=double.MaxValue accrues Tier-1 experience");
        NearRelative(f6dT1, 20.0, "F6d StationaryYears=double.MaxValue with dt=20 accrues full dt=20 weight (no s1-s0 cancellation)", 1e-12);

        // F6d: Extreme dt = 1e308 overflows denomDt * 10 / denomDt * 20; builder contribution must NOT collapse to 0
        var wF6dB = CivicFixture(sanitation: true);
        var cF6dB = wF6dB.Camps[0];
        cF6dB.Population = 100;
        wF6dB.FixedHousing?.Clear();
        NeolithicProduction.FixedHousingChanged(wF6dB);
        cF6dB.Inventory.LightHousingUnits = 0.0;
        cF6dB.Workforce = new PopulationLedger();
        cF6dB.Workforce.Assign(WorkforceRoles.Steward, 15, 100);
        cF6dB.People.Progression.Era = Era.Neolithic;
        cF6dB.People.CivicPrecedents = new List<string> { CivicPrecedents.CeremonialIntegration };
        cF6dB.People.CivicExperience = new Dictionary<string, double>();
        var recF6dB2 = new TurnRecord();
        recF6dB2.Production.Add(new ProductionReceipt { Camp = cF6dB.Id, Role = WorkforceRoles.Builder, WorkerIntervals = 1e308 });
        CivicGovernance.Close(wF6dB, recF6dB2, 1e308);
        CivicGovernance.ValidateInvariants(wF6dB);
        Check(cF6dB.People.CivicExperience.TryGetValue(CivicPrecedents.CivicOrganization, out double f6dT2),
            "F6d extreme dt=1e308 accrues Tier-2 builder experience without denomDt*10 overflow");
        NearRelative(f6dT2, 1.125e306, "F6d Tier-2 builder term matches exact 1e308 * 0.45 * 0.025", 1e-12);

        cF6dB.Population = 3000;
        cF6dB.Workforce = new PopulationLedger();
        cF6dB.People.CivicPrecedents = new List<string> { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
        cF6dB.People.CivicExperience = new Dictionary<string, double>();
        var recF6dB3 = new TurnRecord();
        recF6dB3.Production.Add(new ProductionReceipt { Camp = cF6dB.Id, Role = WorkforceRoles.Builder, WorkerIntervals = 1e308 });
        CivicGovernance.Close(wF6dB, recF6dB3, 1e308);
        CivicGovernance.ValidateInvariants(wF6dB);
        Check(cF6dB.People.CivicExperience.TryGetValue(CivicPrecedents.PlannedQuartersAndDrainage, out double f6dT3B),
            "F6d extreme dt=1e308 accrues Tier-3B builder experience without denomDt*20 overflow");
        NearRelative(f6dT3B, 5.625e305, "F6d Tier-3B builder term matches exact 1e308 * 0.45 * 0.0125", 1e-12);

        // -------------------------------------------------------------------------
        // Group E (E1 & E2): Relax subnormal log-space accuracy & TargetStratification pre-clamp
        // -------------------------------------------------------------------------
        NearRelative(CivicGovernance.Relax(0.0, double.MaxValue, 1e-160, 1e-160), 1.7976931348623157e-12,
            "E1 Relax(0, MaxValue, 1e-160, 1e-160) avoids subnormal rt quantization", 1e-12);
        NearRelative(CivicGovernance.Relax(0.0, double.MaxValue, 1e-160, 1e-164), 1.7976931348623157e-16,
            "E1 Relax(0, MaxValue, 1e-160, 1e-164) avoids rt underflow to 0", 1e-12);
        NearRelative(CivicGovernance.Relax(0.0, double.MaxValue, double.Epsilon, 0.5), 4.440892098500626e-16,
            "E1 Relax(0, MaxValue, double.Epsilon, 0.5) avoids rt underflow to 0", 1e-12);
        var semiCfgE1 = new SimulationConfig
        {
            Civic = new CivicGovernanceSettings
            {
                Enabled = true,
                TradeVolumeRelaxationRate = 1e-321
            }
        };
        semiCfgE1.Validate();
        double rateE1 = semiCfgE1.Civic.TradeVolumeRelaxationRate;
        double oneStepE1 = CivicGovernance.Relax(0.0, double.MaxValue, rateE1, 20.0);
        double expectedOneStepE1 = Math.Exp(Math.Log(double.MaxValue) + Math.Log(rateE1) + Math.Log(20.0));
        NearRelative(oneStepE1, expectedOneStepE1, "E1 Relax 1x20y at TradeVolumeRelaxationRate=1e-321 matches exact analytical value", 1e-12);
        double dailyE1 = 0.0;
        double dtDayE1 = 20.0 / 7300.0;
        for (int i = 0; i < 7300; i++)
            dailyE1 = CivicGovernance.Relax(dailyE1, double.MaxValue, rateE1, dtDayE1);
        NearRelative(dailyE1, oneStepE1, "E1 Relax SmoothCalendar 1x20y vs 7300x1d semigroup invariance at TradeVolumeRelaxationRate=1e-321", 1e-11);

        NearRelative(CivicGovernance.TargetStratification(cfg, double.PositiveInfinity, FissionPolicy.Encourage, ImmigrationPolicy.Open, CivicFocus.Communal), 0.25,
            "E2 TargetStratification(+Infinity) pre-clamps to +1 before applying -0.75 steering offset", 1e-12);
        NearRelative(CivicGovernance.TargetStratification(cfg, double.NegativeInfinity, FissionPolicy.Discourage, ImmigrationPolicy.Restricted, CivicFocus.Centralized), -0.25,
            "E2 TargetStratification(-Infinity) pre-clamps to -1 before applying +0.75 steering offset", 1e-12);
    }

    // =========================================================================
    // Phase 1B (Milestone M3) Fixture & Exit Gates (Docs/CivicGovernance.md §11 E)
    // =========================================================================

    private static WorldState Phase1VillageFixture(bool enableCivic = true, bool sanitation = false,
        int population = 600, bool aridValley = false, int players = 1)
    {
        var cfg = SimulationConfig.Small(players);
        cfg.Width = 24;
        cfg.Height = 16;
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        cfg.SurplusFundedBirths = true;
        cfg.ElderKernelSplit = true;
        cfg.ElderSplitObjective = 2;
        cfg.ElderMixedRoutes = true;
        cfg.CropHarvestingOnly = true;
        cfg.CropMoistureReference = 0.5;
        cfg.SharedCropHerdAllocationEnabled = true;
        cfg.HerdLabourEnabled = true;
        cfg.FissionEnabled = false;
        cfg.ImmigrationEnabled = false;
        cfg.TradeEnabled = false;
        cfg.LandUseDamagePerYear = 0;
        cfg.ConditionRecoveryPerYear = 0.05;
        cfg.Agriculture = new AgriculturalTechnologySettings { PloughEnabled = true, IrrigationEnabled = true };
        cfg.Civic = enableCivic ? new CivicGovernanceSettings { Enabled = true, SanitationEnabled = sanitation } : null;

        int count = cfg.Width * cfg.Height;
        var w = new WorldState
        {
            Seed = 1729,
            Config = cfg,
            Generation = new GenerationReport(),
            Tiles = new Tile[count]
        };

        int homeTile = 8 * cfg.Width + 12; // tile 204, centered in 24x16
        double landFraction = cfg.LandFoodRegenerationInitialized ? cfg.LandFoodRegeneration : cfg.FoodRegeneration;
        float lushTemp = 22f, lushMoist = 0.88f;
        double lushWarmth = Numbers.Clamp((lushTemp + 8) / 30.0, 0.015, 1.0);
        float lushRecovery = (float)EcologyMath.DailyRate(landFraction * (0.2 + lushMoist) * lushWarmth, cfg.BaseDays);
        float waterRecovery = (float)EcologyMath.DailyRate(cfg.WaterRecovery, cfg.BaseDays);
        for (int i = 0; i < count; i++)
        {
            var t = new Tile
            {
                Id = i,
                ElevationLevel = 1,
                Elevation = 0.10f,
                Terrain = Terrain.Grassland,
                FreshwaterSource = FreshwaterSource.RainPool,
                Freshwater = 25
            };
            for (int s = 0; s < 4; s++)
            {
                t.Seasons[s] = new SeasonData
                {
                    Temperature = lushTemp,
                    Moisture = lushMoist,
                    RegionalMoisture = lushMoist,
                    Rain = 0.7f,
                    MaxFreshwater = 25f,
                    AccessibleFreshwater = 25f,
                    FoodRecoveryPerDay = lushRecovery,
                    BiomassRecoveryPerDay = lushRecovery,
                    WaterRecoveryPerDay = waterRecovery,
                    Habitat = (float)(lushMoist * lushWarmth)
                };
            }
            FoodEcology.ConfigurePotential(t, cfg, true);
            t.Food = t.Seasons[0].MaxFood * 0.9;
            t.Freshwater = t.Seasons[0].MaxFreshwater * 0.9;
            w.Tiles[i] = t;
        }

        // Rich local resources on footprint & nearby tiles so raw materials don't artificially stall 3k growth.
        var res = new WorldResourceState
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
            WoodRecoveryPerYear = 0.25
        };
        for (int i = 0; i < count; i++)
        {
            res.Stone[i] = 5000;
            res.Clay[i] = 5000;
            res.Wood[i] = 5000;
        }
        w.Resources = res;

        var c = new Camp
        {
            Id = w.NextId++,
            Player = 0,
            Tile = homeTile,
            Population = population,
            PlantFood = population * 40.0,
            IsVillage = true,
            StationaryTurns = 3,
            StationaryYears = 60.0,
            EconomyAutomationTurns = -1,
            EconomicLabor = new EconomicLaborPlan()
        };
        w.Camps.Add(c);
        Ownership.Bind(w);
        c.Technology = Technology.Cultivation;
        c.Technologies[(int)Technology.Cultivation] = true;
        c.People.Progression.Era = Era.Neolithic;
        c.People.NeolithicTechnologies.AddRange(new[]
        {
            NeolithicTech.Construction,
            NeolithicTech.Toolmaking,
            NeolithicTech.Weaving,
            NeolithicTech.Pottery,
            NeolithicTech.Carpentry,
            NeolithicTech.Plough
        });
        c.Inventory.Tools = Math.Max(200, population * 0.4);
        c.Inventory.FarmTools = Math.Max(100, population * 0.25);
        c.Inventory.Cloth = Math.Max(800, population * 1.5);
        NeolithicProduction.AddFixedHousing(w, c, population / cfg.Neolithic.HousingPeoplePerUnit, w.Days);

        var footprint = w.Map.Radius(homeTile, 1).OrderBy(id => id).ToArray();
        var neighbors = w.Map.Neighbors(homeTile).OrderBy(id => id).ToArray();
        int riverTile = neighbors[0];
        w.Tiles[riverTile].FreshwaterSource = FreshwaterSource.River;
        w.Tiles[riverTile].Flow = 500f;
        for (int s = 0; s < 4; s++)
        {
            w.Tiles[riverTile].Seasons[s].MaxFreshwater = 220f;
            w.Tiles[riverTile].Seasons[s].AccessibleFreshwater = 220f;
            w.Tiles[riverTile].Seasons[s].WaterRecoveryPerDay = waterRecovery;
        }
        w.Tiles[riverTile].Freshwater = 220 * 0.9;

        if (aridValley)
        {
            c.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
            int riverTile2 = neighbors[3];
            w.Tiles[riverTile2].FreshwaterSource = FreshwaterSource.River;
            w.Tiles[riverTile2].Flow = 500f;
            foreach (int rTile in new[] { riverTile, riverTile2 })
            {
                w.Tiles[rTile].Elevation = 0.20f;
                for (int s = 0; s < 4; s++)
                {
                    w.Tiles[rTile].Seasons[s].MaxFreshwater = 700f;
                    w.Tiles[rTile].Seasons[s].AccessibleFreshwater = 700f;
                    w.Tiles[rTile].Seasons[s].WaterRecoveryPerDay = waterRecovery;
                }
                w.Tiles[rTile].Freshwater = 700 * 0.9;
            }
            var footprintSet = new HashSet<int>(footprint);
            float dryRecovery = (float)EcologyMath.DailyRate(landFraction * (0.2 + 0.12) * lushWarmth, cfg.BaseDays);
            foreach (int id in footprint)
            {
                if (id != riverTile && id != riverTile2) w.Tiles[id].Elevation = 0.10f;
                for (int s = 0; s < 4; s++)
                {
                    w.Tiles[id].Seasons[s].Moisture = 0.12f;
                    w.Tiles[id].Seasons[s].RegionalMoisture = 0.12f;
                    if (id != riverTile && id != riverTile2)
                    {
                        w.Tiles[id].Seasons[s].MaxFreshwater = 10f;
                        w.Tiles[id].Seasons[s].AccessibleFreshwater = 10f;
                    }
                    w.Tiles[id].Seasons[s].FoodRecoveryPerDay = dryRecovery;
                    w.Tiles[id].Seasons[s].BiomassRecoveryPerDay = dryRecovery;
                }
                FoodEcology.ConfigurePotential(w.Tiles[id], cfg, true);
                w.Tiles[id].Food = w.Tiles[id].Seasons[0].MaxFood * 0.9;
                if (id != riverTile && id != riverTile2) w.Tiles[id].Freshwater = 9;
            }
            for (int i = 0; i < count; i++)
            {
                if (footprintSet.Contains(i)) continue;
                var t = w.Tiles[i];
                t.ElevationLevel = 10;
                t.Elevation = 0.90f;
                t.FreshwaterSource = FreshwaterSource.None;
                t.Freshwater = 0;
                t.Food = 0;
                t.PotentialFood = 0;
                for (int s = 0; s < 4; s++)
                {
                    t.Seasons[s].MaxFreshwater = 0f;
                    t.Seasons[s].MaxFood = 0f;
                    t.Seasons[s].MaxPotentialFood = 0f;
                    t.Seasons[s].Moisture = 0.05f;
                }
            }
        }

        foreach (int id in footprint)
        {
            var t = w.Tiles[id];
            t.CultivationCapital = 1.0;
            t.Cultivation = (float)CultivationEconomy.Intensity(cfg, 1.0);
            t.Cultivated = true;
            t.CultivationOperator = c.Id;
            t.CultivationOperatingFraction = 0;
        }

        c.Workforce.Assign(WorkforceRoles.Farmer, Math.Max(20, population / 3), c.Population);
        return w;
    }

    private static void Phase1OrganicGrowthTo3000Gate()
    {
        var civicWorld = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 600);
        var controlWorld = Phase1VillageFixture(enableCivic: false, sanitation: false, population: 600);
        // Neolithic SmoothCalendar cadence (5-year turns) on unmodified WorldGenerator land ecology
        // gives 7 cultivated tiles ~3,600 carrying capacity so the Phase-1 disease ceiling (~2,800-3,400)
        // is genuinely tested rather than a 20-year single-harvest standing-stock cap.
        civicWorld.Config.TurnDays = 5.0 * civicWorld.Config.DaysPerYear;
        controlWorld.Config.TurnDays = 5.0 * controlWorld.Config.DaysPerYear;
        var civicCamp = civicWorld.Camps[0];
        var controlCamp = controlWorld.Camps[0];

        // Verify zero pre-seeded civic state on entry (100% genuine organic climb).
        Check(civicCamp.People.CivicPrecedents.Count == 0 && civicCamp.People.CivicExperience.Count == 0,
            "Phase-1 gate starts with zero pre-seeded civic precedents or experience");
        Check(civicCamp.CivicComplexity == 0 && civicCamp.CivicCapital == 0 && CultivationEconomy.Workers(civicCamp, WorkforceRoles.Steward) == 0,
            "Phase-1 gate starts with zero complexity, zero capital, and zero Stewards");

        var resolver = new TurnResolver();
        int peakCivic = civicCamp.Population, peakControl = controlCamp.Population;
        var trace = new System.Text.StringBuilder();
        for (int turn = 0; turn < 100; turn++)
        {
            var recC = resolver.Resolve(civicWorld, new Dictionary<int, List<Order>>());
            resolver.Resolve(controlWorld, new Dictionary<int, List<Order>>());
            if (civicCamp.Population > peakCivic) peakCivic = civicCamp.Population;
            if (controlCamp.Population > peakControl) peakControl = controlCamp.Population;
            if (turn == 0 || (turn + 1) % 20 == 0)
            {
                var dem = recC.Demographics[0];
                int farm = CultivationEconomy.Workers(civicCamp, WorkforceRoles.Farmer);
                int stew = CultivationEconomy.Workers(civicCamp, WorkforceRoles.Steward);
                int bld = CultivationEconomy.Workers(civicCamp, WorkforceRoles.Builder);
                double avgQ = civicWorld.Map.Radius(civicCamp.Tile, 1).Average(id => (double)civicWorld.Tiles[id].Cultivation);
                double expF = civicCamp.EconomicLabor?.ExpansionFarmers ?? 0;
                double tFood = civicWorld.Tiles[civicCamp.Tile].Food;
                double tPot = civicWorld.Tiles[civicCamp.Tile].PotentialFood;
                trace.Append($"[t={turn + 1}:N={civicCamp.Population},ctrl={controlCamp.Population},C={civicCamp.CivicComplexity:F2}/{CivicGovernance.MaxComplexity(civicWorld, civicCamp):F2},K={civicCamp.CivicCapital:F1},S={stew},Fm={farm}(exp={expF:F0}),Bl={bld},Ga={civicCamp.GathererPopulation},q={avgQ:F2},tF={tFood:F0}/{tPot:F0},prod={dem.FoodProduced:F0},req={dem.FoodRequired:F0},cons={dem.FoodConsumed:F0},b={civicCamp.EffectiveFertilityRate:F4},d={civicCamp.EffectiveMortalityRate:F4},food={civicCamp.Food:F0}] ");
            }
        }

        TurnResolver.Invariants(civicWorld);
        TurnResolver.Invariants(controlWorld);
        CivicGovernance.ValidateInvariants(civicWorld);

        // Control stalls near the ~1,000 baseline.
        Check(peakControl >= 850 && peakControl <= 1250,
            "Civic==null control stalls near ~1,000 baseline (peak=" + peakControl + ", end=" + controlCamp.Population + ")");
        Check(PhysicalDemography.CrowdingMitigation(controlWorld, controlCamp) == 1.0,
            "Civic==null control retains exact CrowdingMitigation == 1.0");
        Check(PhysicalDemography.DiseaseMitigation(controlWorld, controlCamp) == 1.0,
            "Civic==null control retains exact DiseaseMitigation == 1.0");

        // Phase-1 Civic unlocks Tier 1 + Tier 2 organically and builds real capital & Stewards.
        Check(civicCamp.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration),
            "Phase-1 village organically unlocks Tier 1 (CeremonialIntegration) " + trace);
        Check(civicCamp.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization),
            "Phase-1 village organically unlocks Tier 2 (CivicOrganization) " + trace);
        Check(civicCamp.CivicCapital > 100.0 && CultivationEconomy.Workers(civicCamp, WorkforceRoles.Steward) > 20,
            $"Phase-1 village builds real CivicCapital ({civicCamp.CivicCapital:F1}) and staffs real Stewards ({CultivationEconomy.Workers(civicCamp, WorkforceRoles.Steward)}) " + trace);
        double coverage = CivicGovernance.CivicCoverage(civicWorld, civicCamp);
        double mc = PhysicalDemography.CrowdingMitigation(civicWorld, civicCamp);
        double md = PhysicalDemography.DiseaseMitigation(civicWorld, civicCamp);
        Check(coverage >= 0.85, $"Phase-1 village achieves high CivicCoverage >= 0.85 ({coverage:F3})");
        Check(mc < 0.25 && mc >= civicWorld.Config.Civic.MinCrowdingMitigation,
            $"Phase-1 village lowers CrowdingMitigation below 0.25 ({mc:F4})");
        Check(md == 1.0, $"Phase-1 (SanitationEnabled=false) keeps DiseaseMitigation == 1.0 ({md})");

        // Phase-1 Civic climbs past ~1,000 & Tier 1 (~1,600) to the natural Phase-1 disease ceiling (~2,800-3,400).
        Check(civicCamp.Population >= 2600 && civicCamp.Population <= 3550,
            $"Phase-1 village organically reaches natural ~2,800-3,400 disease ceiling (end={civicCamp.Population}, peak={peakCivic}) " + trace);
        Check(peakCivic >= 2750 && peakCivic <= 3550,
            $"Phase-1 village peak stays in [2750, 3550] disease ceiling band (peak={peakCivic}) " + trace);

        // Deterministic save/reload replay over 3 more turns.
        byte[] saved = SaveCodec.Save(civicWorld);
        var reloaded = SaveCodec.Load(saved);
        for (int t = 0; t < 3; t++)
        {
            resolver.Resolve(civicWorld, new Dictionary<int, List<Order>>());
            resolver.Resolve(reloaded, new Dictionary<int, List<Order>>());
        }
        Check(SaveCodec.Fingerprint(civicWorld) == SaveCodec.Fingerprint(reloaded),
            "Phase-1 civic city save/reload deterministic replay matches bit-for-bit");
    }

    private static void NaturalGeographicDivergenceGate()
    {
        var plains = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 900, aridValley: false);
        var valley = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 900, aridValley: true);
        plains.Config.TurnDays = 5.0 * plains.Config.DaysPerYear;
        valley.Config.TurnDays = 5.0 * valley.Config.DaysPerYear;
        var plainsCamp = plains.Camps[0];
        var valleyCamp = valley.Camps[0];
        valleyCamp.PlantFood = 900 * 60.0;
        valleyCamp.Inventory.HousingMaterial = 200.0;
        valleyCamp.Inventory.Pots = 300.0;

        Check(plainsCamp.CivicFocus == CivicFocus.Auto && valleyCamp.CivicFocus == CivicFocus.Auto,
            "Geographic divergence starts both settlements on CivicFocus.Auto");
        Check(plainsCamp.CivicStratification == 0 && valleyCamp.CivicStratification == 0,
            "Geographic divergence starts both settlements at P == 0");
        Check((valley.Canals?.Count ?? 0) == 0, "Arid river valley starts with 0 pre-seeded canals (purely organic build)");

        var resolver = new TurnResolver();
        for (int turn = 0; turn < 35; turn++)
        {
            resolver.Resolve(plains, new Dictionary<int, List<Order>>());
            resolver.Resolve(valley, new Dictionary<int, List<Order>>());
        }

        var vDrivers = CivicGovernance.EvaluateEnvironment(valley, valleyCamp);
        var pDrivers = CivicGovernance.EvaluateEnvironment(plains, plainsCamp);
        Check(plainsCamp.CivicFocus == CivicFocus.Auto && valleyCamp.CivicFocus == CivicFocus.Auto,
            "CivicFocus remains Auto throughout geographic divergence (zero manual steering)");
        Check(valley.Canals.Count > 0 && valley.IrrigatedFields.Count > 0,
            $"Arid river valley organically builds active canals ({valley.Canals.Count}) and irrigated fields ({valley.IrrigatedFields.Count})");
        Check(plainsCamp.LastEnvironmentalStratification < -0.30 && plainsCamp.CivicStratification < -0.25,
            $"Open rain-fed plains naturally diverges to Egalitarian (P_env={plainsCamp.LastEnvironmentalStratification:F3}, P={plainsCamp.CivicStratification:F3}, D=({pDrivers.D1:F2},{pDrivers.D2:F2},{pDrivers.D3:F2},{pDrivers.D4:F2}))");
        Check(valleyCamp.LastEnvironmentalStratification > 0.30 && valleyCamp.CivicStratification > 0.25,
            $"Circumscribed irrigated valley naturally diverges to Stratified (P_env={valleyCamp.LastEnvironmentalStratification:F3}, P={valleyCamp.CivicStratification:F3}, canals={valley.Canals.Count}, irr={valley.IrrigatedFields.Count}, D=({vDrivers.D1:F2},{vDrivers.D2:F2},{vDrivers.D3:F2},{vDrivers.D4:F2}))");
        Check(CivicGovernance.EffectiveFocusLabel(plainsCamp) == "Communal",
            "Plains EffectiveFocusLabel classifies Auto as Communal (" + CivicGovernance.EffectiveFocusLabel(plainsCamp) + ")");
        Check(CivicGovernance.EffectiveFocusLabel(valleyCamp) == "Centralized",
            "Valley EffectiveFocusLabel classifies Auto as Centralized (" + CivicGovernance.EffectiveFocusLabel(valleyCamp) + ")");
        if (plainsCamp.CivicStratification <= -0.30)
            Check(WorkforceRoles.Label(WorkforceRoles.Steward, plainsCamp) == "Council Arbiter",
                "Egalitarian plains Steward UI label is Council Arbiter");
        if (valleyCamp.CivicStratification >= 0.30)
            Check(WorkforceRoles.Label(WorkforceRoles.Steward, valleyCamp) == "Temple Scribe",
                "Stratified valley Steward UI label is Temple Scribe");
    }

    private static void WelfareOnlyQoLIsolationGate()
    {
        var resolver = new TurnResolver();
        foreach (double pCoord in new[] { -0.80, 0.80 })
        {
            var wWith = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1800);
            var wZero = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1800);
            wZero.Config.Civic.QoLEqualityLinear = 0.0;
            wZero.Config.Civic.QoLEqualityQuadratic = 0.0;
            wZero.Config.Validate();

            foreach (var w in new[] { wWith, wZero })
            {
                var c = w.Camps[0];
                c.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
                c.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
                c.CivicComplexity = 0.75;
                c.CivicStratification = pCoord;
                c.LastEnvironmentalComplexity = 0.75;
                c.LastEnvironmentalStratification = pCoord;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c) * 1.5;
                int reqStew = (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c));
                c.EconomyAutomationTurns = 0;
                c.Workforce = new PopulationLedger();
                c.Workforce.Assign(WorkforceRoles.Farmer, 500, c.Population);
                c.Workforce.Assign(WorkforceRoles.Builder, 60, c.Population);
                c.Workforce.Assign(WorkforceRoles.Weaver, 40, c.Population);
                c.Workforce.Assign(WorkforceRoles.Steward, reqStew, c.Population);
            }

            var ordWith = new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.Economy, wWith.Camps[0].Id) { Economy = EconomicIntent.From(wWith.Camps[0]) } } };
            var ordZero = new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.Economy, wZero.Camps[0].Id) { Economy = EconomicIntent.From(wZero.Camps[0]) } } };

            var recWith = resolver.Resolve(wWith, ordWith);
            var recZero = resolver.Resolve(wZero, ordZero);
            var cWith = wWith.Camps[0];
            var cZero = wZero.Camps[0];
            var dWith = recWith.Demographics[0];
            var dZero = recZero.Demographics[0];

            // Exact 0 physical demographic delta.
            Check(cWith.Population == cZero.Population, $"QoL isolation P={pCoord}: identical Population ({cWith.Population} vs {cZero.Population})");
            Check(dWith.Additions == dZero.Additions, $"QoL isolation P={pCoord}: identical Additions ({dWith.Additions} vs {dZero.Additions})");
            Check(dWith.Deaths == dZero.Deaths, $"QoL isolation P={pCoord}: identical Deaths ({dWith.Deaths} vs {dZero.Deaths})");
            Check(cWith.EffectiveFertilityRate == cZero.EffectiveFertilityRate, $"QoL isolation P={pCoord}: bit-exact EffectiveFertilityRate");
            Check(cWith.EffectiveMortalityRate == cZero.EffectiveMortalityRate, $"QoL isolation P={pCoord}: bit-exact EffectiveMortalityRate");
            Check(cWith.CrowdingMortalityRate == cZero.CrowdingMortalityRate, $"QoL isolation P={pCoord}: bit-exact CrowdingMortalityRate");
            Check(cWith.DiseaseMortalityRate == cZero.DiseaseMortalityRate, $"QoL isolation P={pCoord}: bit-exact DiseaseMortalityRate");
            Check(cWith.ExposureMortalityRate == cZero.ExposureMortalityRate, $"QoL isolation P={pCoord}: bit-exact ExposureMortalityRate");
            Check(cWith.StarvationMortalityRate == cZero.StarvationMortalityRate, $"QoL isolation P={pCoord}: bit-exact StarvationMortalityRate");

            // Welfare QoL reflects Q_civic cleanly without clamping distortion (evaluated at the pre-Close
            // (C, P, I_civic=1) active during CommitExperiencedWelfare before Close relaxes P).
            Check(cZero.QoL > 0.2 && cZero.QoL < 0.85, $"QoL isolation P={pCoord}: non-clamped baseQoL ({cZero.QoL:F4})");
            double qMultAtCommit = CivicGovernance.QoLEqualityMultiplierFor(wWith.Config, 0.75, pCoord, 1.0);
            if (pCoord < 0)
            {
                Check(cWith.QoL > cZero.QoL, $"Egalitarian P={pCoord} raises welfare QoL ({cWith.QoL:F4} > {cZero.QoL:F4})");
                NearRelative(cWith.QoL, Numbers.Clamp(cZero.QoL * qMultAtCommit, 0, 1), $"Egalitarian QoL equals baseQoL * Q_civic", 1e-12);
            }
            else
            {
                Check(cWith.QoL < cZero.QoL, $"Stratified P={pCoord} lowers welfare QoL ({cWith.QoL:F4} < {cZero.QoL:F4})");
                NearRelative(cWith.QoL, cZero.QoL * qMultAtCommit, $"Stratified QoL equals baseQoL * Q_civic", 1e-12);
            }

            // Direct PhysicalDemography solvers ignore QoLEqualityLinear bit-for-bit.
            double mc = CivicGovernance.CrowdingMitigation(wWith, cWith);
            var r1 = PhysicalDemography.AdvanceBudget(wWith.Config, 1800, 20, 144000, 1800, 1800, 0, true, 0, mc, 1, 1, 0);
            var r2 = PhysicalDemography.AdvanceBudget(wZero.Config, 1800, 20, 144000, 1800, 1800, 0, true, 0, mc, 1, 1, 0);
            Check(r1.FinalPopulation == r2.FinalPopulation && r1.Deaths == r2.Deaths && r1.Births == r2.Births,
                $"AdvanceBudget bit-identical regardless of QoLEqualityLinear (P={pCoord})");
        }
    }

    private static void ToolAndStorageConservationGates()
    {
        // 4A — Public-Works Mobilization (M_build <= 1.35x) & Tool Wear Conservation (4 M3 public-works recipes).
        WorldState MakeTier3Camp(double pCoord)
        {
            var w = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1200);
            var c = w.Camps[0];
            c.People.NeolithicTechnologies.Add(NeolithicTech.Irrigation);
            c.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
            c.CivicComplexity = 1.0;
            c.CivicStratification = pCoord;
            c.LastEnvironmentalComplexity = 1.0;
            c.LastEnvironmentalStratification = pCoord;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c) * 2.0;
            c.Workforce = new PopulationLedger();
            c.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)) + 5, c.Population);
            c.Workforce.Assign(WorkforceRoles.Builder, 50, c.Population);
            c.Workforce.Assign(WorkforceRoles.Craftsperson, 50, c.Population);
            return w;
        }

        var wStrat = MakeTier3Camp(1.0);
        var wEgal = MakeTier3Camp(-1.0);
        var cStrat = wStrat.Camps[0];
        var cEgal = wEgal.Camps[0];

        NearRelative(CivicGovernance.PublicWorksMobilizationMultiplier(wStrat, cStrat), 1.35, "Stratified P=+1 M_build == 1.35", 1e-12);
        NearRelative(CivicGovernance.PublicWorksMobilizationMultiplier(wEgal, cEgal), 1.00, "Egalitarian P=-1 M_build == 1.00", 1e-12);

        var recipes = NeolithicProduction.Recipes(wStrat.Config).ToDictionary(r => r.Good);
        foreach (var good in new[] { CraftGood.CanalConstruction, CraftGood.CanalMaintenance, CraftGood.CivicConstruction, CraftGood.CivicMaintenance })
        {
            double rStrat = SpecialistProductivity.CraftRate(wStrat, cStrat, recipes[good], WorkforceRoles.Builder);
            double rEgal = SpecialistProductivity.CraftRate(wEgal, cEgal, recipes[good], WorkforceRoles.Builder);
            NearRelative(rStrat, 1.35 * rEgal, $"M_build boosts specialist Builder by exact 1.35x on {good}", 1e-12);
        }
        foreach (var good in new[] { CraftGood.Housing, CraftGood.LightHousing, CraftGood.HousingMaterial })
        {
            double rStrat = SpecialistProductivity.CraftRate(wStrat, cStrat, recipes[good], WorkforceRoles.Builder);
            double rEgal = SpecialistProductivity.CraftRate(wEgal, cEgal, recipes[good], WorkforceRoles.Builder);
            NearRelative(rStrat, rEgal, $"M_build does NOT boost private {good}", 1e-12);
        }
        foreach (var good in new[] { CraftGood.CivicConstruction, CraftGood.CivicMaintenance })
        {
            double rStrat = SpecialistProductivity.CraftRate(wStrat, cStrat, recipes[good], WorkforceRoles.Craftsperson);
            double rEgal = SpecialistProductivity.CraftRate(wEgal, cEgal, recipes[good], WorkforceRoles.Craftsperson);
            NearRelative(rStrat, rEgal, $"M_build does NOT boost generalist Craftsperson on {good}", 1e-12);
        }

        // Exact tool-wear and HousingMaterial conservation on CivicConstruction AND CanalConstruction.
        foreach (var good in new[] { CraftGood.CivicConstruction, CraftGood.CanalConstruction })
        {
            var ws = MakeTier3Camp(1.0);
            var we = MakeTier3Camp(-1.0);
            var cs = ws.Camps[0];
            var ce = we.Camps[0];
            var recipe = NeolithicProduction.Recipes(ws.Config).Single(r => r.Good == good);

            double units = 4.0;
            ProductionOrder MakeOrder(WorldState w, Camp c)
            {
                var ord = new ProductionOrder(new[] { good }, new Dictionary<CraftGood, double> { [good] = units });
                if (good == CraftGood.CivicConstruction)
                    ord.Civic = new CivicOrder { Construction = units };
                else
                {
                    int src = w.Map.Neighbors(c.Tile).First();
                    w.Tiles[src].FreshwaterSource = FreshwaterSource.River;
                    w.Tiles[src].Elevation = 0.2f;
                    for (int s = 0; s < 4; s++) w.Tiles[c.Tile].Seasons[s].Moisture = 0.1f;
                    var opp = IrrigationEconomy.Opportunity(w, c);
                    ord.Irrigation = new IrrigationOrder { Project = opp, Construction = units };
                }
                return ord;
            }

            cs.Inventory.HandTools = 0; cs.Inventory.Tools = 10.0; cs.Inventory.HousingMaterial = 100.0;
            ce.Inventory.HandTools = 0; ce.Inventory.Tools = 10.0; ce.Inventory.HousingMaterial = 100.0;
            double eqS = 1000, eqE = 1000;
            var recS = new TurnRecord { StartDays = 0, Duration = ws.Config.TurnDays };
            var recE = new TurnRecord { StartDays = 0, Duration = we.Config.TurnDays };
            double intervals = ws.Config.GenerationIntervals;
            double usedS = NeolithicProduction.Produce(ws, cs, recipe, WorkforceRoles.Builder, 5000, intervals, units, ref eqS, recS, new ToolCoverageBudget(ws, cs, intervals), order: MakeOrder(ws, cs));
            double usedE = NeolithicProduction.Produce(we, ce, recipe, WorkforceRoles.Builder, 5000, intervals, units, ref eqE, recE, new ToolCoverageBudget(we, ce, intervals), order: MakeOrder(we, ce));

            NearRelative(usedS, usedE / 1.35, $"{good}: Stratified Builder time reduced by exact 1/1.35", 1e-12);
            double expectedTools = units / ToolCoverageBudget.OutputsPerTool;
            NearRelative(10.0 - cs.Inventory.Tools, expectedTools, $"{good}: Stratified physical tool wear conserved == units/OutputsPerTool", 1e-12);
            NearRelative(10.0 - ce.Inventory.Tools, expectedTools, $"{good}: Egalitarian physical tool wear conserved == units/OutputsPerTool", 1e-12);
            double expectedHm = units * recipe.HousingMaterial;
            NearRelative(100.0 - cs.Inventory.HousingMaterial, expectedHm, $"{good}: Stratified HousingMaterial conserved", 1e-12);
            NearRelative(100.0 - ce.Inventory.HousingMaterial, expectedHm, $"{good}: Egalitarian HousingMaterial conserved", 1e-12);
        }

        // 4B — Granary Storage (M_storage <= 1.50x) & StorageCapacityAfterPotDepreciation <= ApplyStorage.
        NearRelative(CivicGovernance.GranaryStorageMultiplier(wStrat, cStrat), 1.50, "Stratified P=+1 M_storage == 1.50", 1e-12);
        NearRelative(CivicGovernance.GranaryStorageMultiplier(wEgal, cEgal), 1.00, "Egalitarian P=-1 M_storage == 1.00", 1e-12);
        NearRelative(NeolithicProduction.NaturalStorageCapacity(wStrat, cStrat, 0),
            1.50 * NeolithicProduction.NaturalStorageCapacity(wEgal, cEgal, 0),
            "NaturalStorageCapacity scales by exact 1.50x at P=+1 vs P=-1", 1e-12);

        foreach (double dt in new[] { 20.0, 5.0, 1.0 })
        {
            foreach (int regime in new[] { 0, 1, 2 }) // 0=zero upkeep, 1=partial upkeep, 2=full upkeep + build
            {
                var w = MakeTier3Camp(0.8);
                var c = w.Camps[0];
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c) * 0.75; // strictly below saturation so decay changes M_storage!
                c.Inventory.Pots = 100.0;
                c.Inventory.HousingMaterial = 500.0;
                c.Inventory.Tools = 500.0;
                double intervals = dt * w.Config.DaysPerYear / w.Config.BaseDays;
                double projected = NeolithicProduction.StorageCapacityAfterPotDepreciation(w, c, intervals, dt);
                double fullUpkeep = CivicGovernance.CivicMaintenanceTarget(w, c, dt);
                double maintOrder = regime == 0 ? 0 : regime == 1 ? 0.5 * fullUpkeep : fullUpkeep;
                double buildOrder = regime == 2 ? 20.0 : 0;
                var prod = new Dictionary<CraftGood, double>();
                foreach (var r in NeolithicProduction.Recipes(w.Config)) prod[r.Good] = 0;
                prod[CraftGood.CivicMaintenance] = maintOrder;
                prod[CraftGood.CivicConstruction] = buildOrder;
                var order = new ProductionOrder(new[] { CraftGood.CivicMaintenance, CraftGood.CivicConstruction }, prod)
                {
                    Civic = new CivicOrder { Upkeep = maintOrder, Construction = buildOrder }
                };
                var needs = new TurnNeeds { Residents = c.Population, Order = order, Production = prod };
                NeolithicProduction.ResolveCampNeolithic(w, c, intervals, dt, new TurnRecord { StartDays = w.Days, Duration = dt * w.Config.DaysPerYear }, 0, needs, resolveNeeds: false);
                double actual = NeolithicProduction.StorageCapacity(w, c, intervals);
                Check(projected <= actual + 1e-9,
                    $"StorageCapacityAfterPotDepreciation ({projected:F4}) <= ApplyStorage ({actual:F4}) at dt={dt}, regime={regime}");
            }
        }

        // GoodsStorage integration with boosted natural storage and civic infrastructure HousingMaterial exemption.
        var wGs = MakeTier3Camp(1.0);
        wGs.Config.GoodsStorageEnabled = true;
        var cGs = wGs.Camps[0];
        var recipesGs = NeolithicProduction.Recipes(wGs.Config);
        GoodsStorage.PlannedUse(recipesGs, g => g == CraftGood.CivicConstruction ? 10.0 : g == CraftGood.CivicMaintenance ? 5.0 : 0,
            out double eqOut, out double housingAssm, out double infraMat);
        NearRelative(eqOut, 15.0, "GoodsStorage.PlannedUse counts CivicConstruction+CivicMaintenance equipment outputs", 1e-12);
        Check(housingAssm == 0, "GoodsStorage.PlannedUse does not count Civic works as housing assembly");
        NearRelative(infraMat, 10.0 * wGs.Config.Civic.CivicBuildHousingMaterials + 5.0 * wGs.Config.Civic.CivicMaintainHousingMaterials,
            "GoodsStorage.PlannedUse exempts Civic infrastructure HousingMaterial", 1e-12);
        var gsStrat = GoodsStorage.Begin(wGs, cGs, 20.0, cGs.Population, eqOut, housingAssm, endOfTurn: false, infrastructureMaterials: infraMat);
        cGs.Inventory.Pots = 500;
        double foodCapStrat = gsStrat.FoodCapacity(cGs.Inventory.Pots, 0);
        var wGsEgal = MakeTier3Camp(-1.0);
        wGsEgal.Config.GoodsStorageEnabled = true;
        wGsEgal.Camps[0].Inventory.Pots = 500;
        var gsEgal = GoodsStorage.Begin(wGsEgal, wGsEgal.Camps[0], 20.0, wGsEgal.Camps[0].Population, eqOut, housingAssm, endOfTurn: false, infrastructureMaterials: infraMat);
        double foodCapEgal = gsEgal.FoodCapacity(wGsEgal.Camps[0].Inventory.Pots, 0);
        Check(foodCapStrat > foodCapEgal, $"GoodsStorage FoodCapacity reflects Stratified granary bonus ({foodCapStrat:F1} > {foodCapEgal:F1})");
    }

    private static void FamineShockCollapseDivergenceGate()
    {
        // 5A — Survival-First DemandControl (Protected never starved by Civic Continuity/Development).
        WorldState MakeFamineCamp(bool withCivic)
        {
            var w = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 800);
            var c = w.Camps[0];
            c.PlantFood = 0; c.AnimalFood = 0; c.AquaticFood = 0;
            c.Inventory.Cloth = 0; c.Inventory.AnimalFabric = 0;
            // Remove fields so gathering is at base GatherPerPerson and add a large connected hunter cohort
            // drawing camp food so survival food gathering genuinely exhausts all 800 residents (budget == 0).
            foreach (int id in w.Map.Radius(c.Tile, 1))
            {
                w.Tiles[id].CultivationCapital = 0;
                w.Tiles[id].Cultivation = 0;
                w.Tiles[id].Cultivated = false;
                w.Tiles[id].CultivationOperator = -1;
                w.Tiles[id].Food = 25000;
                w.Tiles[id].PotentialFood = 250000;
                for (int s = 0; s < 4; s++)
                {
                    w.Tiles[id].Seasons[s].MaxFood = 25000f;
                    w.Tiles[id].Seasons[s].MaxPotentialFood = 250000f;
                }
            }
            w.Hunters.Add(new Hunter { Id = w.NextId++, Player = c.Player, SourceCamp = c.Id, Tile = c.Tile, Population = 700 });
            if (withCivic)
            {
                c.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                c.CivicComplexity = 0.75;
                c.CivicStratification = 0.50;
                c.LastEnvironmentalComplexity = 0.75;
                c.LastEnvironmentalStratification = 0.50;
                c.CivicCapital = 100.0;
            }
            return w;
        }

        var wfCivic = MakeFamineCamp(true);
        var wfNone = MakeFamineCamp(false);
        DemandController.Plan(wfCivic, wfCivic.Camps[0], EconomicIntent.From(wfCivic.Camps[0]), out _, out var allocCivic);
        DemandController.Plan(wfNone, wfNone.Camps[0], EconomicIntent.From(wfNone.Camps[0]), out _, out var allocNone);
        Check(allocCivic.ElderAction != null && allocNone.ElderAction != null, "Famine shock runs kernel-scored ElderAction");
        Check(allocCivic.ElderAction.ClothingLevel >= allocNone.ElderAction.ClothingLevel,
            $"Famine shock: civic presence never reduces Protected ClothingLevel ({allocCivic.ElderAction.ClothingLevel} >= {allocNone.ElderAction.ClothingLevel})");
        Check(allocCivic.ElderAction.ShelterLevel >= allocNone.ElderAction.ShelterLevel,
            $"Famine shock: civic presence never reduces Protected ShelterLevel ({allocCivic.ElderAction.ShelterLevel} >= {allocNone.ElderAction.ShelterLevel})");
        var civicDevReq = allocCivic.Requests.FirstOrDefault(r => r.Service == "civic development");
        Check(civicDevReq == null || civicDevReq.Granted <= 1e-6,
            $"Famine shock: civic development gets 0 grant ({civicDevReq?.Granted ?? 0}, budget={allocCivic.ResidentGoodsBudget:F1})");

        // 5B — Egalitarian dispersal vs. Stratified granary holdout under live famine shock + AuditBounds = true.
        WorldState MakeShockWorld(double pCoord)
        {
            var w = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 500);
            w.Config.TurnDays = 5.0 * w.Config.DaysPerYear;
            w.Config.FissionEnabled = true;
            w.Config.FissionGainThreshold = 0.113;
            var c = w.Camps[0];
            c.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            c.StationaryYears = 30.0;
            c.CivicComplexity = 0.75;
            c.CivicStratification = pCoord;
            c.LastEnvironmentalComplexity = 0.75;
            c.LastEnvironmentalStratification = pCoord;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
            c.Inventory.HousingMaterial = 120.0;
            c.Inventory.Tools = 200.0;
            c.Workforce = new PopulationLedger();
            c.Workforce.Assign(WorkforceRoles.Farmer, 150, c.Population);
            c.Workforce.Assign(WorkforceRoles.Builder, 40, c.Population);
            c.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)), c.Population);
            // Populate explored exit ring across full FissionRadius and provide genuine river freshwater
            // on the exit ring so a founding daughter camp is not dehydration-blocked on tiny RainPools.
            var mem = ContactKnowledge.MemoryOf(w, c.People);
            int minDist = Math.Max(3, w.Config.MinimumCampSpacing);
            int maxDist = Math.Max(minDist + 2, w.Config.FissionRadius);
            foreach (int id in w.Map.Radius(c.Tile, maxDist))
            {
                mem.Explored.Add(id);
                if (w.Map.Distance(c.Tile, id) >= minDist)
                {
                    w.Tiles[id].FreshwaterSource = FreshwaterSource.River;
                    w.Tiles[id].Freshwater = 220 * 0.9;
                    for (int s = 0; s < 4; s++)
                    {
                        w.Tiles[id].Seasons[s].MaxFreshwater = 220f;
                        w.Tiles[id].Seasons[s].AccessibleFreshwater = 220f;
                    }
                }
            }
            // Start acute famine shock at matched 32% of each settlement's NaturalStorageCapacity
            // (Stratified holds ~1.32x larger reserve than Egalitarian, cleanly bridging an extra forward turn).
            c.PlantFood = 0.32 * NeolithicProduction.NaturalStorageCapacity(w, c, 0);
            // Impose severe local famine shock on the home footprint (5% of normal food/biomass), leaving exit ring viable.
            foreach (int id in w.Map.Radius(c.Tile, 1))
            {
                w.Tiles[id].Food *= 0.05;
                w.Tiles[id].PotentialFood *= 0.05;
                for (int s = 0; s < 4; s++)
                {
                    w.Tiles[id].Seasons[s].MaxFood *= 0.05f;
                    w.Tiles[id].Seasons[s].MaxPotentialFood *= 0.05f;
                }
            }
            return w;
        }

        var wEgalShock = MakeShockWorld(-0.85);
        var wStratShock = MakeShockWorld(0.85);
        Check(wStratShock.Camps[0].PlantFood > wEgalShock.Camps[0].PlantFood * 1.25,
            $"Stratified starts famine shock with larger granary reserve ({wStratShock.Camps[0].PlantFood:F0} vs {wEgalShock.Camps[0].PlantFood:F0})");

        double stratInitialCap = wStratShock.Camps[0].CivicCapital;
        int egalFissionTurn = 0, stratFissionTurn = 0;
        var resolver = new TurnResolver();
        bool priorAudit = SocialRelocationForecastCache.AuditBounds;
        var priorDiag = Fission.Diagnostic;
        var shockTrace = new System.Text.StringBuilder();
        SocialRelocationForecastCache.AuditBounds = true;
        try
        {
            for (int turn = 1; turn <= 6; turn++)
            {
                Fission.Diagnostic = new Dictionary<int, Fission.Diag>();
                var rE = resolver.Resolve(wEgalShock, new Dictionary<int, List<Order>>());
                if (Fission.Diagnostic.TryGetValue(wEgalShock.Camps[0].Id, out var dE))
                    shockTrace.Append($"[t{turn}:E={dE.Stage}(N={wEgalShock.Camps[0].Population},F={dE.BaselineNutrition:F2},mv={dE.MoverChange:F3}/{dE.Threshold:F3},src={dE.SourceChange:F3},agg={dE.AggregateChange:F3},dN={dE.DaughterNutrition:F2},dQ={dE.DaughterQoL:F2})]");
                Fission.Diagnostic = new Dictionary<int, Fission.Diag>();
                var rS = resolver.Resolve(wStratShock, new Dictionary<int, List<Order>>());
                if (egalFissionTurn == 0 && rE.Fissions != null && rE.Fissions.Count > 0) egalFissionTurn = turn;
                if (stratFissionTurn == 0 && rS.Fissions != null && rS.Fissions.Count > 0) stratFissionTurn = turn;
            }
        }
        finally
        {
            SocialRelocationForecastCache.AuditBounds = priorAudit;
            Fission.Diagnostic = priorDiag;
        }
        Check(egalFissionTurn > 0,
            $"Famine shock triggers Egalitarian dispersal (egalPop={wEgalShock.Camps[0].Population}, egalFood={wEgalShock.Camps[0].Food:F0}, stratPop={wStratShock.Camps[0].Population}, stratFission={stratFissionTurn}) {shockTrace}");
        Check(stratFissionTurn == 0 || egalFissionTurn < stratFissionTurn,
            $"Egalitarian disperses strictly earlier than Stratified granary holdout (egalTurn={egalFissionTurn}, stratTurn={stratFissionTurn})");
        Check(wStratShock.Camps[0].CivicCapital < stratInitialCap,
            $"Stratified CivicCapital declines when famine starves civic upkeep ({wStratShock.Camps[0].CivicCapital:F1} < {stratInitialCap:F1})");
    }

    private static void DemandControlProtectedAndQoLRegressionGates()
    {
        // ---- Bug #1 Regression: Lower-class civic requests must NEVER steal freeMaterial before KernelSplit / Protected ----
        WorldState MakeScarceHmMixedVillage(bool withCivic, bool mixedRoutes, double hmStock, double woodStock, double toolsStock, double stoneStock, bool survivalTight, double openingC)
        {
            var w = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 500);
            w.Config.ElderKernelSplit = true;
            w.Config.ElderMixedRoutes = mixedRoutes;
            var c = w.Camps[0];
            c.Technologies[(int)Technology.AnimalHusbandry] = true;
            c.PlantFood = 12000;
            c.AnimalFood = 400;
            c.AquaticFood = 0;
            c.Inventory.Cloth = 800;
            c.Inventory.AnimalFabric = 400;
            c.Inventory.HousingMaterial = hmStock;
            c.Inventory.Wood = woodStock;
            c.Inventory.Clay = 0;
            c.Inventory.Stone = stoneStock;
            c.Inventory.Tools = toolsStock;
            c.Inventory.HandTools = 0;
            c.Inventory.FarmTools = 200;
            c.RudimentaryExpectation = 1.0;
            w.FixedHousing.Clear();
            NeolithicProduction.FixedHousingChanged(w);
            for (int i = 0; i < w.Tiles.Length; i++)
            {
                w.Resources.Wood[i] = 0;
                w.Resources.Clay[i] = 0;
                if (stoneStock <= 0) w.Resources.Stone[i] = 0;
            }
            w.Herds.Add(new Herd { Id = w.NextId++, Tile = c.Tile, Animals = 80, DomesticCamp = c.Id });
            if (survivalTight)
            {
                // Saturate Survival food gathering so zero labor slack remains beyond Protected shelter.
                c.People.NeolithicTechnologies.Remove(NeolithicTech.Plough);
                foreach (int id in w.Map.Radius(c.Tile, 1))
                {
                    w.Tiles[id].CultivationCapital = 0;
                    w.Tiles[id].Cultivation = 0;
                    w.Tiles[id].Cultivated = false;
                    w.Tiles[id].CultivationOperator = -1;
                    w.Tiles[id].Food = 25000;
                    w.Tiles[id].PotentialFood = 250000;
                    for (int s = 0; s < 4; s++) { w.Tiles[id].Seasons[s].MaxFood = 25000f; w.Tiles[id].Seasons[s].MaxPotentialFood = 250000f; }
                }
                w.Hunters.Add(new Hunter { Id = w.NextId++, Player = c.Player, SourceCamp = c.Id, Tile = c.Tile, Population = 500 });
            }
            if (withCivic)
            {
                c.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                c.CivicComplexity = openingC;
                c.CivicStratification = 0.50;
                c.LastEnvironmentalComplexity = 0.75;
                c.LastEnvironmentalStratification = 0.50;
                c.CivicCapital = openingC > 0 ? 50.0 : 15.0;
                // Zero Stewards => I_civic == 0 (identical crowding/cohesion/QoL to NoCiv at planning time).
            }
            return w;
        }

        // 1A: Shipped defaults (ElderMixedRoutes=true, I_civic=0, HM=40, Wood=63).
        // Case (0.0, tools=62.5/OutputsPerTool, stone=0): exact 62.5 tool outputs for Protected shelter -> both civic HM requests
        // receive Granted == 0 and 0 Stewards are staffed -> 100% bit-exact Plan AND live Resolve parity.
        // Case (0.75, tools=0, stone=500): exact chal_iter1_1 reproducer parameters -> Protected shelter stays bit-exact
        // 62.50/62.50 (ReservedMaterial=40.0, never slashed to 50.40), fixed Housing bit-exact, deaths never worse.
        foreach (var tc in new[] { (openingC: 0.0, tools: 62.5 / ToolCoverageBudget.OutputsPerTool, stone: 0.0), (openingC: 0.75, tools: 0.0, stone: 500.0) })
        {
            double openingC = tc.openingC;
            var wNo = MakeScarceHmMixedVillage(withCivic: false, mixedRoutes: true, hmStock: 40, woodStock: 63, toolsStock: tc.tools, stoneStock: tc.stone, survivalTight: true, openingC: openingC);
            var wCiv = MakeScarceHmMixedVillage(withCivic: true, mixedRoutes: true, hmStock: 40, woodStock: 63, toolsStock: tc.tools, stoneStock: tc.stone, survivalTight: true, openingC: openingC);
            Check(CivicGovernance.CivicCoverage(wCiv, wCiv.Camps[0]) == 0.0, $"Bug #1A (C={openingC}) setup has I_civic == 0");

            DemandController.Plan(wNo, wNo.Camps[0], EconomicIntent.From(wNo.Camps[0]), out _, out var aNo);
            DemandController.Plan(wCiv, wCiv.Camps[0], EconomicIntent.From(wCiv.Camps[0]), out _, out var aCiv);
            var pNo = aNo.Requests.FirstOrDefault(r => r.Service == "shelter protected");
            var pCiv = aCiv.Requests.FirstOrDefault(r => r.Service == "shelter protected");
            Check(pNo != null && pCiv != null && pNo.Granted > 60.0,
                $"Bug #1A (mixed=true, C={openingC}): Protected shelter requested and fulfilled ({pNo?.Granted:F3} vs {pCiv?.Granted:F3})");
            Check(pCiv.Granted == pNo.Granted && pCiv.ReservedMaterial == pNo.ReservedMaterial && pCiv.Unmet == pNo.Unmet,
                $"Bug #1A (mixed=true, C={openingC}): Protected shelter bit-exact identical with civic ({pCiv.Granted:F3} vs {pNo.Granted:F3}, resHM={pCiv.ReservedMaterial:F1} vs {pNo.ReservedMaterial:F1}, unmet='{pCiv.Unmet}')");
            Check(aCiv.ElderAction.ShelterRoute == aNo.ElderAction.ShelterRoute &&
                  aCiv.ElderAction.ShelterLevel == aNo.ElderAction.ShelterLevel &&
                  aCiv.ElderAction.ClothingLevel == aNo.ElderAction.ClothingLevel,
                $"Bug #1A (mixed=true, C={openingC}): ElderAction bit-exact identical with civic");

            // Continuity execution ordering: tools/canal/shelter replacement strictly before civic maintenance (and before core stewards when C>0).
            int idxMaint = aCiv.Requests.FindIndex(r => r.Service == "civic maintenance");
            int idxDev = aCiv.Requests.FindIndex(r => r.Service == "civic development");
            Check(idxMaint >= 0 && idxDev >= 0, $"Bug #1A (C={openingC}) creates both civic maintenance and civic development requests");
            if (openingC > 0)
            {
                int idxCore = aCiv.Requests.FindIndex(r => r.Service == "core stewards");
                Check(idxCore > idxMaint, $"Bug #1A (C={openingC}) places core stewards after civic maintenance");
            }
            foreach (var r in aCiv.Requests.Where(r => r.Class == DemandController.ServiceClass.Continuity &&
                r.Service != "civic maintenance" && r.Service != "core stewards"))
                Check(aCiv.Requests.IndexOf(r) < idxMaint,
                    $"Bug #1A (C={openingC}) Continuity order: '{r.Service}' precedes 'civic maintenance'");
            if (openingC == 0.0)
            {
                double civicGranted = aCiv.Requests
                    .Where(r => r.Service == "civic maintenance" || r.Service == "core stewards" || r.Service == "civic development" || r.Service == "civic stewards")
                    .Sum(r => r.Granted);
                Check(civicGranted <= 1e-7, $"Bug #1A (C=0, exact tools): all civic requests receive ~0 grant within bisection epsilon (got {civicGranted})");
            }

            var resolver = new TurnResolver();
            var recNo = resolver.Resolve(wNo, new Dictionary<int, List<Order>>());
            var recCiv = resolver.Resolve(wCiv, new Dictionary<int, List<Order>>());
            var cNo = wNo.Camps[0];
            var cCiv = wCiv.Camps[0];
            Check(NeolithicProduction.FixedHousingAt(wCiv, cCiv, wCiv.Days) == NeolithicProduction.FixedHousingAt(wNo, cNo, wNo.Days),
                $"Bug #1A (C={openingC}) live Resolve: bit-exact fixed Housing built");
            if (openingC == 0.0)
            {
                Check(recCiv.Demographics[0].Deaths == recNo.Demographics[0].Deaths && cCiv.Population == cNo.Population,
                    $"Bug #1A (C=0) live Resolve: bit-exact deaths ({recCiv.Demographics[0].Deaths} vs {recNo.Demographics[0].Deaths}) and pop ({cCiv.Population} vs {cNo.Population})");
                Check(cCiv.ExposureMortalityRate == cNo.ExposureMortalityRate && cCiv.HousingPerCapita == cNo.HousingPerCapita,
                    $"Bug #1A (C=0) live Resolve: bit-exact ExposureMortalityRate ({cCiv.ExposureMortalityRate:F6} vs {cNo.ExposureMortalityRate:F6}) and HousingPerCapita");
            }
            else
            {
                Check(recCiv.Demographics[0].Deaths <= recNo.Demographics[0].Deaths,
                    $"Bug #1A (C=0.75) live Resolve: civic presence never increases deaths ({recCiv.Demographics[0].Deaths} <= {recNo.Demographics[0].Deaths})");
                Check(cCiv.EffectiveMortalityRate <= cNo.EffectiveMortalityRate + 1e-12,
                    $"Bug #1A (C=0.75) live Resolve: EffectiveMortalityRate never increases ({cCiv.EffectiveMortalityRate:F6} <= {cNo.EffectiveMortalityRate:F6})");
            }
        }

        // 1B: ElderMixedRoutes=false, I_civic=0, HM=200, Wood=80 — RouteFeasible must not flip Housing -> LightHousing.
        foreach (var tc in new[] { (openingC: 0.0, tools: 125.0 / ToolCoverageBudget.OutputsPerTool, stone: 0.0), (openingC: 0.75, tools: 200.0, stone: 500.0) })
        {
            double openingC = tc.openingC;
            var wNo = MakeScarceHmMixedVillage(withCivic: false, mixedRoutes: false, hmStock: 200, woodStock: 80, toolsStock: tc.tools, stoneStock: tc.stone, survivalTight: true, openingC: openingC);
            var wCiv = MakeScarceHmMixedVillage(withCivic: true, mixedRoutes: false, hmStock: 200, woodStock: 80, toolsStock: tc.tools, stoneStock: tc.stone, survivalTight: true, openingC: openingC);
            DemandController.Plan(wNo, wNo.Camps[0], EconomicIntent.From(wNo.Camps[0]), out _, out var aNo);
            DemandController.Plan(wCiv, wCiv.Camps[0], EconomicIntent.From(wCiv.Camps[0]), out _, out var aCiv);
            Check(aNo.ElderAction.ShelterRoute == CraftGood.Housing && aCiv.ElderAction.ShelterRoute == CraftGood.Housing,
                $"Bug #1B (mixed=false, C={openingC}): ShelterRoute stays Housing ({aCiv.ElderAction.ShelterRoute} vs {aNo.ElderAction.ShelterRoute})");
            Check(aCiv.ElderAction.ShelterLevel == aNo.ElderAction.ShelterLevel &&
                  aCiv.ElderAction.ClothingLevel == aNo.ElderAction.ClothingLevel,
                $"Bug #1B (mixed=false, C={openingC}): ElderAction levels bit-exact identical");
            var pNo = aNo.Requests.FirstOrDefault(r => r.Service == "shelter protected");
            var pCiv = aCiv.Requests.FirstOrDefault(r => r.Service == "shelter protected");
            Check(pNo != null && pCiv != null && pCiv.Granted == pNo.Granted && pCiv.ReservedMaterial == pNo.ReservedMaterial,
                $"Bug #1B (mixed=false, C={openingC}): Protected shelter bit-exact ({pCiv?.Granted:F3} vs {pNo?.Granted:F3})");

            var resolver = new TurnResolver();
            var recNo = resolver.Resolve(wNo, new Dictionary<int, List<Order>>());
            var recCiv = resolver.Resolve(wCiv, new Dictionary<int, List<Order>>());
            Check(NeolithicProduction.FixedHousingAt(wCiv, wCiv.Camps[0], wCiv.Days) == NeolithicProduction.FixedHousingAt(wNo, wNo.Camps[0], wNo.Days),
                $"Bug #1B (mixed=false, C={openingC}) live Resolve: bit-exact fixed Housing");
            if (openingC == 0.0)
            {
                Check(recCiv.Demographics[0].Deaths == recNo.Demographics[0].Deaths &&
                      wCiv.Camps[0].ExposureMortalityRate == wNo.Camps[0].ExposureMortalityRate,
                    $"Bug #1B (mixed=false, C=0) live Resolve: bit-exact deaths ({recCiv.Demographics[0].Deaths} vs {recNo.Demographics[0].Deaths}) and exposure mortality");
            }
            else
            {
                Check(recCiv.Demographics[0].Deaths <= recNo.Demographics[0].Deaths &&
                      wCiv.Camps[0].EffectiveMortalityRate <= wNo.Camps[0].EffectiveMortalityRate + 1e-12,
                    $"Bug #1B (mixed=false, C=0.75) live Resolve: deaths and effective mortality never increase ({recCiv.Demographics[0].Deaths} <= {recNo.Demographics[0].Deaths})");
            }
        }

        // 1C: civic development (Development) must NEVER steal freeMaterial from Protected, Continuity, or Recovery
        // (tested both with ElderKernelSplit=true and ElderKernelSplit=false).
        foreach (bool kernelSplitOn in new[] { true, false })
        {
            WorldState MakeDevNoStealWorld(bool withCivic)
            {
                var w = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 400);
                w.Config.ElderKernelSplit = kernelSplitOn;
                var c = w.Camps[0];
                w.FixedHousing.Clear();
                NeolithicProduction.FixedHousingChanged(w);
                c.Inventory.HousingMaterial = 100.0; // exact stock needed for 400/4 = 100 fixed Housing units
                c.Inventory.Wood = 0; c.Inventory.Clay = 0;
                c.Inventory.Tools = 200; c.Inventory.FarmTools = 200;
                for (int i = 0; i < w.Tiles.Length; i++) { w.Resources.Wood[i] = 0; w.Resources.Clay[i] = 0; }
                if (withCivic)
                {
                    c.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                    c.CivicComplexity = 0.75; c.LastEnvironmentalComplexity = 0.75;
                    c.CivicCapital = 0.0; // zero upkeep -> only civic development competes for HM
                }
                return w;
            }
            var wNo = MakeDevNoStealWorld(false);
            var wCiv = MakeDevNoStealWorld(true);
            DemandController.Plan(wNo, wNo.Camps[0], EconomicIntent.From(wNo.Camps[0]), out _, out var aNo);
            DemandController.Plan(wCiv, wCiv.Camps[0], EconomicIntent.From(wCiv.Camps[0]), out _, out var aCiv);
            double shelterNo = aNo.Requests.Where(r => r.Good == CraftGood.Housing).Sum(r => r.Granted);
            double shelterCiv = aCiv.Requests.Where(r => r.Good == CraftGood.Housing).Sum(r => r.Granted);
            var devReq = aCiv.Requests.FirstOrDefault(r => r.Service == "civic development");
            Check(shelterNo > 99.9 && shelterCiv == shelterNo,
                $"Bug #1C (kernelSplit={kernelSplitOn}): civic development never steals freeMaterial from Protected/Recovery shelter ({shelterCiv:F3} vs {shelterNo:F3})");
            Check(devReq != null && devReq.ReservedMaterial == 0.0 && devReq.Granted == 0.0,
                $"Bug #1C (kernelSplit={kernelSplitOn}): civic development reserves 0 HM and grants 0 when higher classes claim stock (res={devReq?.ReservedMaterial}, grant={devReq?.Granted})");
        }

        // 1D: Strict Continuity > Recovery freeMaterial priority when ElderKernelSplit=false (civic maintenance uses stock HM ahead of shelter deficit).
        {
            var wOff = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 400);
            wOff.Config.ElderKernelSplit = false;
            var cOff = wOff.Camps[0];
            wOff.FixedHousing.Clear();
            NeolithicProduction.FixedHousingChanged(wOff);
            cOff.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            cOff.CivicComplexity = 0.75; cOff.LastEnvironmentalComplexity = 0.75;
            cOff.CivicCapital = 50.0;
            cOff.Inventory.HousingMaterial = 40.0;
            cOff.Inventory.Wood = 0; cOff.Inventory.Clay = 0;
            cOff.Inventory.Tools = 200; cOff.Inventory.FarmTools = 200;
            for (int i = 0; i < wOff.Tiles.Length; i++) { wOff.Resources.Wood[i] = 0; wOff.Resources.Clay[i] = 0; }
            DemandController.Plan(wOff, cOff, EconomicIntent.From(cOff), out _, out var aOff);
            var cmReq = aOff.Requests.FirstOrDefault(r => r.Service == "civic maintenance");
            Check(cmReq != null && cmReq.Units > 0, "Bug #1D (kernelSplit=false): civic maintenance created");
            NearRelative(cmReq.Granted, cmReq.Units, "Bug #1D (kernelSplit=false): Continuity civic maintenance funded from stock HM ahead of Recovery shelter deficit", 1e-12);
        }

        // ---- Bug #2 Regression: KernelSplit welfare-only QoL isolation & zero upper-tail tie-collapse ----
        WorldState MakeAutoQoLWorld(int pop, double dtYears, double cCoord, double pCoord, double q1, double q2, int objective = 2)
        {
            var w = Phase1VillageFixture(enableCivic: true, sanitation: false, population: pop);
            w.Config.TurnDays = dtYears * w.Config.DaysPerYear;
            w.Config.ElderKernelSplit = true;
            w.Config.ElderSplitObjective = objective;
            w.Config.Civic.QoLEqualityLinear = q1;
            w.Config.Civic.QoLEqualityQuadratic = q2;
            w.Config.Validate();
            var c = w.Camps[0];
            c.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
            c.CivicComplexity = cCoord;
            c.CivicStratification = pCoord;
            c.LastEnvironmentalComplexity = cCoord;
            c.LastEnvironmentalStratification = pCoord;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c) * 2.0;
            int reqStew = (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)) + 5;
            c.Workforce = new PopulationLedger();
            c.Workforce.Assign(WorkforceRoles.Farmer, Math.Max(20, pop / 3), c.Population);
            c.Workforce.Assign(WorkforceRoles.Steward, reqStew, c.Population);
            c.Inventory.Cloth = 0;
            c.Inventory.AnimalFabric = 0;
            c.RudimentaryExpectation = 1.0;
            c.EconomyAutomationTurns = -1; // full automatic DemandController path!
            return w;
        }

        var qolScenarios = new[]
        {
            (pop: 300, dt: 20.0, c: 1.0),
            (pop: 400, dt: 20.0, c: 0.2),
            (pop: 400, dt: 5.0, c: 1.0),
            (pop: 1200, dt: 5.0, c: 1.0),
            (pop: 2000, dt: 5.0, c: 1.0)
        };
        var qolPs = new[] { -1.0, -0.5, 0.5, 1.0 };
        var qolArms = new[] { (q1: 0.09, q2: 0.01, label: "default"), (q1: 0.25, q2: 0.01, label: "high") };
        var autoResolver = new TurnResolver();
        int autoQoLChecked = 0;

        foreach (var sc in qolScenarios)
        {
            foreach (double pCoord in qolPs)
            {
                var wBase = MakeAutoQoLWorld(sc.pop, sc.dt, sc.c, pCoord, 0.0, 0.0);
                DemandController.Plan(wBase, wBase.Camps[0], EconomicIntent.From(wBase.Camps[0]), out var prodBase, out var allocBase);
                var recBase = autoResolver.Resolve(wBase, new Dictionary<int, List<Order>>());
                var cBase = wBase.Camps[0];
                var dBase = recBase.Demographics[0];

                foreach (var arm in qolArms)
                {
                    var wArm = MakeAutoQoLWorld(sc.pop, sc.dt, sc.c, pCoord, arm.q1, arm.q2);
                    double qMultPlan = CivicGovernance.QoLEqualityMultiplier(wArm, wArm.Camps[0]);
                    DemandController.Plan(wArm, wArm.Camps[0], EconomicIntent.From(wArm.Camps[0]), out var prodArm, out var allocArm);

                    // Bit-exact 0 delta in ElderAction decision & ProtectedClothingUnits.
                    var eb = allocBase.ElderAction;
                    var ea = allocArm.ElderAction;
                    Check(ea.ClothingRoute == eb.ClothingRoute && ea.ClothingLevel == eb.ClothingLevel && ea.ClothingUnits == eb.ClothingUnits &&
                          ea.ShelterRoute == eb.ShelterRoute && ea.ShelterLevel == eb.ShelterLevel && ea.ShelterPeople == eb.ShelterPeople &&
                          ea.PersonYears == eb.PersonYears && prodArm.ProtectedClothingUnits == prodBase.ProtectedClothingUnits,
                        $"Bug #2 plan isolation ({arm.label} q1={arm.q1}, N={sc.pop}, dt={sc.dt}, C={sc.c}, P={pCoord}): bit-exact ElderAction ({ea.ClothingLevel},{ea.ShelterLevel},{ea.ShelterRoute}) == ({eb.ClothingLevel},{eb.ShelterLevel},{eb.ShelterRoute})");
                    if (eb.QoL > 0)
                        NearRelative(ea.QoL, Numbers.Clamp(eb.QoL * qMultPlan, 0, 1),
                            $"Bug #2 ElderAction.QoL reflects Q_civic ({arm.label}, N={sc.pop}, P={pCoord})", 1e-12);

                    // Live TurnResolver.Resolve under automatic DemandController: bit-exact 0 physical demography delta.
                    var recArm = autoResolver.Resolve(wArm, new Dictionary<int, List<Order>>());
                    var cArm = wArm.Camps[0];
                    var dArm = recArm.Demographics[0];
                    Check(cArm.Population == cBase.Population && dArm.Additions == dBase.Additions && dArm.Deaths == dBase.Deaths &&
                          cArm.ExposureMortalityRate == cBase.ExposureMortalityRate &&
                          cArm.CrowdingMortalityRate == cBase.CrowdingMortalityRate &&
                          cArm.DiseaseMortalityRate == cBase.DiseaseMortalityRate &&
                          cArm.EffectiveMortalityRate == cBase.EffectiveMortalityRate &&
                          cArm.EffectiveFertilityRate == cBase.EffectiveFertilityRate &&
                          cArm.ClothPerCapita == cBase.ClothPerCapita &&
                          cArm.HousingPerCapita == cBase.HousingPerCapita,
                        $"Bug #2 live Resolve isolation ({arm.label} q1={arm.q1}, N={sc.pop}, dt={sc.dt}, C={sc.c}, P={pCoord}): bit-exact physical demography (pop {cArm.Population} vs {cBase.Population}, expMort {cArm.ExposureMortalityRate:F6} vs {cBase.ExposureMortalityRate:F6})");

                    if (cBase.QoL > 0)
                    {
                        double expectedWelfareQoL = Numbers.Clamp(cBase.QoL * qMultPlan, 0, 1);
                        NearRelative(cArm.QoL, expectedWelfareQoL,
                            $"Bug #2 live Camp.QoL shifts by exact Q_civic ({arm.label}, N={sc.pop}, P={pCoord})", 1e-12);
                    }
                    autoQoLChecked++;
                }
            }
        }
        Check(autoQoLChecked == qolScenarios.Length * qolPs.Length * qolArms.Length,
            $"Bug #2 full automatic QoL isolation grid executed ({autoQoLChecked} cases)");

        // Verify bit-exact QoLEqualityLinear isolation across ALL QoL-sensitive ElderSplitObjective modes (1, 2, 3).
        foreach (int obj in new[] { 1, 2, 3 })
        {
            var wObj0 = MakeAutoQoLWorld(300, 20.0, 1.0, -1.0, 0.0, 0.0, obj);
            var wObjHi = MakeAutoQoLWorld(300, 20.0, 1.0, -1.0, 0.25, 0.01, obj);
            DemandController.Plan(wObj0, wObj0.Camps[0], EconomicIntent.From(wObj0.Camps[0]), out _, out var aObj0);
            DemandController.Plan(wObjHi, wObjHi.Camps[0], EconomicIntent.From(wObjHi.Camps[0]), out _, out var aObjHi);
            Check(aObjHi.ElderAction.ClothingLevel == aObj0.ElderAction.ClothingLevel &&
                  aObjHi.ElderAction.ShelterLevel == aObj0.ElderAction.ShelterLevel &&
                  aObjHi.ElderAction.ShelterRoute == aObj0.ElderAction.ShelterRoute &&
                  aObjHi.ElderAction.ClothingRoute == aObj0.ElderAction.ClothingRoute,
                $"Bug #2 ElderSplitObjective={obj}: bit-exact ElderAction between q1=0 and q1=0.25");
        }
    }

    private static void Phase1BFeatureContractsAndZeroRegression()
    {
        // ---- 6.1 HasInfrastructure, CopyForPreparation, ReconcileInfrastructure & dt-invariant CivicCapital ----
        var oNone = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>());
        Check(!oNone.HasInfrastructure, "HasInfrastructure is false when Irrigation==null and Civic==null");
        var oIrr = new ProductionOrder(Array.Empty<CraftGood>(), new Dictionary<CraftGood, double>()) { Irrigation = new IrrigationOrder { Construction = 1 } };
        Check(oIrr.HasInfrastructure, "HasInfrastructure is true when Irrigation!=null");
        var oCiv = new ProductionOrder(new[] { CraftGood.CivicConstruction, CraftGood.CivicMaintenance },
            new Dictionary<CraftGood, double> { [CraftGood.CivicConstruction] = 10, [CraftGood.CivicMaintenance] = 5 })
        {
            Civic = new CivicOrder { Construction = 10, Upkeep = 5 }
        };
        Check(oCiv.HasInfrastructure, "HasInfrastructure is true when Civic!=null");
        var oCivCopy = oCiv.CopyForPreparation();
        Check(oCivCopy.HasInfrastructure && oCivCopy.Civic != null && !ReferenceEquals(oCivCopy.Civic, oCiv.Civic),
            "ProductionOrder.CopyForPreparation deep-copies CivicOrder");
        oCivCopy.Civic.Construction = 2;
        Check(oCiv.Civic.Construction == 10, "Mutating copied CivicOrder does not mutate original draft");

        // ReconcileInfrastructure trims CivicConstruction & CivicMaintenance against prepared forecast without mutating draft.
        var reconNeeds = new TurnNeeds
        {
            Order = oCiv.CopyForPreparation(),
            Production = new Dictionary<CraftGood, double> { [CraftGood.CivicConstruction] = 4.0, [CraftGood.CivicMaintenance] = 3.0 }
        };
        var reconAlloc = new DemandController.Allocation();
        reconAlloc.Requests.Add(new DemandController.Request { Service = "civic development", Good = CraftGood.CivicConstruction, Units = 10, Granted = 10 });
        reconAlloc.Requests.Add(new DemandController.Request { Service = "civic maintenance", Good = CraftGood.CivicMaintenance, Units = 5, Granted = 5 });
        TurnPlanner.ReconcileInfrastructure(reconNeeds, reconAlloc);
        NearRelative(reconNeeds.Order.Civic.Construction, 4.0, "ReconcileInfrastructure caps Civic.Construction to forecast", 1e-12);
        NearRelative(reconNeeds.Order.Civic.Upkeep, 3.0, "ReconcileInfrastructure caps Civic.Upkeep to forecast", 1e-12);
        NearRelative(reconNeeds.Order.Ceiling(CraftGood.CivicConstruction), 4.0, "ReconcileInfrastructure caps CivicConstruction ceiling", 1e-12);
        NearRelative(reconAlloc.Requests[0].Granted, 4.0, "ReconcileInfrastructure updates Allocation civic development Granted", 1e-12);
        Check(reconAlloc.Requests[0].Unmet == "prepared production capacity", "ReconcileInfrastructure sets Unmet cause");
        Check(oCiv.Civic.Construction == 10 && oCiv.Civic.Upkeep == 5, "Original draft ProductionOrder untouched by ReconcileInfrastructure");

        // PlanningDraft reuse with Civic != null (Irrigation == null) clones cleanly.
        {
            var wDraft = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1100);
            var cDraft = wDraft.Camps[0];
            cDraft.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
            cDraft.CivicComplexity = 0.25;
            cDraft.LastEnvironmentalComplexity = 0.30;
            cDraft.CivicCapital = 10.0;
            var draft = TurnPlanner.Draft(wDraft, -1);
            Check(draft.Intents.TryGetValue(cDraft.Id, out var cachedIntent) && cachedIntent.Production?.Civic != null && cachedIntent.Production?.Irrigation == null,
                "Draft builds reusable Civic-only ProductionOrder");
            double origBuild = cachedIntent.Production.Civic.Construction;
            double origUpkeep = cachedIntent.Production.Civic.Upkeep;
            var prepared = TurnPlanner.Prepare(wDraft, new Dictionary<int, List<Order>>(), null, null, draft);
            Check(prepared.ProductionOrders.TryGetValue(cDraft.Id, out var prepOrder) && !ReferenceEquals(prepOrder, cachedIntent.Production),
                "Prepare clones Civic-only cached ProductionOrder from PlanningDraft");
            Check(cachedIntent.Production.Civic.Construction == origBuild && cachedIntent.Production.Civic.Upkeep == origUpkeep,
                "Reusable PlanningDraft CivicOrder is never mutated by Prepare/ReconcileInfrastructure");
        }

        // CivicCapital analytical dt-invariance via ResolveCampNeolithic (ruin decay, full upkeep, alignment friction, !EconomyEnabled).
        double RunCapitalDecay(double yearsTotal, int steps, bool fullUpkeep, double pDiff, bool economyEnabled)
        {
            var w = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1000);
            var c = w.Camps[0];
            c.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            c.CivicComplexity = 0.75;
            c.CivicStratification = pDiff;
            c.LastEnvironmentalStratification = 0.0;
            c.CivicCapital = 100.0;
            c.Inventory.HousingMaterial = 1000.0;
            c.Inventory.Tools = 1000.0;
            c.Workforce = new PopulationLedger();
            c.Workforce.Assign(WorkforceRoles.Builder, 50, c.Population);
            if (!economyEnabled)
            {
                c.Technology = Technology.None;
                c.Technologies[(int)Technology.Cultivation] = false;
                c.People.Progression.Era = Era.Paleolithic;
            }
            double dt = yearsTotal / steps;
            double intervals = dt * w.Config.DaysPerYear / w.Config.BaseDays;
            for (int i = 0; i < steps; i++)
            {
                double u = fullUpkeep ? CivicGovernance.CivicMaintenanceTarget(w, c, dt) : 0;
                var prod = new Dictionary<CraftGood, double>();
                foreach (var r in NeolithicProduction.Recipes(w.Config)) prod[r.Good] = 0;
                prod[CraftGood.CivicMaintenance] = u;
                var order = fullUpkeep
                    ? new ProductionOrder(new[] { CraftGood.CivicMaintenance }, prod) { Civic = new CivicOrder { Upkeep = u } }
                    : null;
                var needs = new TurnNeeds { Residents = c.Population, Order = order, Production = prod };
                NeolithicProduction.ResolveCampNeolithic(w, c, intervals, dt, new TurnRecord { StartDays = w.Days, Duration = dt * w.Config.DaysPerYear }, 0, needs, resolveNeeds: false);
            }
            return c.CivicCapital;
        }

        double expectedDecay20 = 100.0 * Math.Exp(-0.018 * 20.0);
        double ruin1x20 = RunCapitalDecay(20.0, 1, false, 0.0, true);
        double ruin4x5 = RunCapitalDecay(20.0, 4, false, 0.0, true);
        double ruin20x1 = RunCapitalDecay(20.0, 20, false, 0.0, true);
        NearRelative(ruin1x20, expectedDecay20, "CivicCapital ruin decay 1x20y matches exp(-delta*20)", 1e-12);
        NearRelative(ruin4x5, ruin1x20, "CivicCapital ruin decay 4x5y == 1x20y", 1e-12);
        NearRelative(ruin20x1, ruin1x20, "CivicCapital ruin decay 20x1y == 1x20y", 1e-12);

        NearRelative(RunCapitalDecay(20.0, 1, true, 0.0, true), 100.0, "CivicCapital full upkeep 1x20y preserves K_0", 1e-12);
        NearRelative(RunCapitalDecay(20.0, 4, true, 0.0, true), 100.0, "CivicCapital full upkeep 4x5y preserves K_0", 1e-12);
        NearRelative(RunCapitalDecay(20.0, 20, true, 0.0, true), 100.0, "CivicCapital full upkeep 20x1y preserves K_0", 1e-12);

        double expectedAlignedFriction = 100.0 * Math.Exp(-0.018 * 1.35 * 20.0);
        NearRelative(RunCapitalDecay(20.0, 1, false, 1.0, true), expectedAlignedFriction,
            "CivicCapital decay with |P - P_env|=1.0 applies 1.35x alignment friction", 1e-12);
        NearRelative(RunCapitalDecay(20.0, 1, false, 0.0, false), expectedDecay20,
            "CivicCapital still decays analytically when !CultivationEconomy.EconomyEnabled", 1e-12);

        // ---- 6.2 All 8 SocialCohesion & Q_civic sites + KernelSplit N=10,000 no tie-collapse + QoLModel parity ----
        {
            var w10k = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 10000);
            var c10k = w10k.Camps[0];
            c10k.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
            c10k.CivicComplexity = 1.0;
            c10k.CivicStratification = 0.0;
            c10k.LastEnvironmentalComplexity = 1.0;
            c10k.LastEnvironmentalStratification = 0.0;
            c10k.CivicCapital = CivicGovernance.RequiredCivicCapital(w10k, c10k) * 2.0;
            c10k.Workforce = new PopulationLedger();
            c10k.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w10k, c10k)) + 20, c10k.Population);
            c10k.Workforce.Assign(WorkforceRoles.Farmer, 3000, c10k.Population);
            c10k.PlantFood = 500000;
            c10k.Inventory.Cloth = 0; // force KernelSplit to choose positive Protected clothing!
            c10k.RudimentaryExpectation = 1.0;
            w10k.FixedHousing.Clear(); // force shelter gap too!
            NeolithicProduction.FixedHousingChanged(w10k);
            // Boost footprint capacity so 10k city is fed cleanly while testing KernelSplit tie-collapse fix.
            foreach (int id in w10k.Map.Radius(c10k.Tile, 1))
            {
                w10k.Tiles[id].Food = 25000;
                w10k.Tiles[id].PotentialFood = 250000;
                for (int s = 0; s < 4; s++) { w10k.Tiles[id].Seasons[s].MaxFood = 25000f; w10k.Tiles[id].Seasons[s].MaxPotentialFood = 250000f; }
            }
            DemandController.Plan(w10k, c10k, EconomicIntent.From(c10k), out _, out var alloc10k);
            Check(alloc10k.ElderAction != null && (alloc10k.ElderAction.ClothingLevel > 0 || alloc10k.ElderAction.ShelterLevel > 0),
                $"KernelSplit at N=10,000 avoids ElderSplitObjective==2 tie-collapse (cloth={alloc10k.ElderAction?.ClothingLevel}, shelter={alloc10k.ElderAction?.ShelterLevel})");

            // Check Fission.QoLModel.At(N) == EstimateSplit.QNow across P in {-1, 0, +1}, including b0 = 0.5 with eqMult = 1.10!
            foreach (double pCoord in new[] { -1.0, 0.0, 1.0 })
            {
                var wq = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1500);
                var cq = wq.Camps[0];
                cq.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                cq.CivicComplexity = 1.0;
                cq.CivicStratification = pCoord;
                cq.LastEnvironmentalStratification = pCoord;
                cq.CivicCapital = CivicGovernance.RequiredCivicCapital(wq, cq) * 2.0;
                cq.Workforce = new PopulationLedger();
                cq.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wq, cq)) + 10, cq.Population);
                cq.FoodNeedsMetRatio = 0.50; // critical corner where inner clamping inside Shape would fail for eqMult=1.10!
                cq.WaterNeedsMetRatio = 1.0;
                cq.ClothPerCapita = 1.0;
                cq.HousingPerCapita = 1.0;
                cq.RudimentaryExpectation = 1.0;
                var qModel = Fission.QoLModelOf(wq, cq);
                var split = Fission.EstimateSplit(wq, cq, cq.FissionPolicy);
                NearRelative(qModel.At(cq.Population), split.QNow, $"Fission.QoLModel.At(N) == EstimateSplit.QNow at P={pCoord}, F=0.5", 1e-12);
                NearRelative(qModel.ForwardAt(cq.Population, 0), qModel.At(cq.Population), $"Fission.QoLModel.ForwardAt(N,0) == At(N) at P={pCoord}", 1e-12);

                // ExpansionValuation.Welfare reflects both CohesionMultiplier and QoLEqualityMultiplier.
                double wBase = cq.Population * cq.FoodNeedsMetRatio * cq.WaterNeedsMetRatio * NeolithicProduction.SocialCohesion(wq, cq, cq.Population);
                double expectedWelfare = cq.Population * Numbers.Clamp((wBase / cq.Population) * CivicGovernance.QoLEqualityMultiplier(wq, cq), 0, 1);
                NearRelative(ExpansionValuation.Welfare(wq, cq, cq.Population, cq.Population), expectedWelfare,
                    $"ExpansionValuation.Welfare applies CohesionMultiplier + QoLEqualityMultiplier at P={pCoord}", 1e-12);

                // ResolveNeeds & CommitExperiencedWelfare both match boosted cohesion * Q_civic.
                NeolithicProduction.CommitExperiencedWelfare(wq, cq, cq.Population, 0.50, 1.0, 1.0);
                NearRelative(cq.QoL, split.QNow, $"CommitExperiencedWelfare matches Fission QNow at P={pCoord}", 1e-12);

                // SocialRelocationForecastCache.UtilityUpperBound >= Forecast.
                var cache = new SocialRelocationForecastCache(wq, wq.Camps);
                var state = SocialRelocationForecastCache.Existing(cq, cq.Population, cq.PlantFood, cq.AnimalFood, cq.AquaticFood, cq.Workforce, null, 0, 0);
                var pol = new SocialRelocationPolicy(0.05, 0, 0.05, 0.05);
                double bound = cache.UtilityUpperBound(state, EmploymentOpportunityMatch.Zero, pol, 0);
                var forecast = cache.Forecast(state, EmploymentOpportunityMatch.Zero);
                double exactU = pol.WelfareWeight * forecast.QoL - pol.MortalityWeight * forecast.MortalityRisk;
                Check(bound >= exactU - 1e-9, $"SocialRelocation UtilityUpperBound ({bound:F6}) >= exact Forecast utility ({exactU:F6}) at P={pCoord}");
            }
        }

        // ---- 6.3 DemandControl PriorityScore, Zero-Idle Stewards, SmoothCalendar, Material Immunity & EquipmentTarget ----
        {
            double MeasureCivicPriorityScore(int pop, double pCoord, double cNow, double cEnv, double kOpen, double dtYears, bool tier2)
            {
                var w = Phase1VillageFixture(enableCivic: true, sanitation: false, population: pop);
                w.Config.TurnDays = dtYears * w.Config.DaysPerYear;
                var c = w.Camps[0];
                c.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
                if (tier2) c.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
                c.CivicComplexity = cNow;
                c.CivicStratification = pCoord;
                c.LastEnvironmentalComplexity = cEnv;
                c.LastEnvironmentalStratification = pCoord;
                c.CivicCapital = kOpen;
                c.Inventory.HousingMaterial = 0; // force full build+HM bundle matching table!
                DemandController.Plan(w, c, EconomicIntent.From(c), out _, out var alloc);
                var req = alloc.Requests.FirstOrDefault(r => r.Service == "civic development");
                return req != null ? req.PriorityScore : -1;
            }

            // PriorityScore calibration checks against Docs/CivicGovernance.md §5 Choice #7 table.
            foreach (double pCoord in new[] { -1.0, 0.0, 1.0 })
            {
                double s600 = MeasureCivicPriorityScore(600, pCoord, 0.30, 0.30, 0, 20.0, false);
                Check(s600 > 0 && s600 < 7.20, $"PriorityScore at N=600, P={pCoord} stays < 7.20 ({s600:F2})");
            }
            double s1000 = MeasureCivicPriorityScore(1000, 0.0, 0.30, 0.30, 0, 20.0, false);
            Check(s1000 > 7.20, $"PriorityScore at N=1000, P=0, T1 crosses 7.20 ({s1000:F2})");
            double s1600 = MeasureCivicPriorityScore(1600, 0.0, 0.75, 0.75, 0, 20.0, true);
            Check(s1600 > 7.70, $"PriorityScore at N=1600, P=0, T2 crosses fertile field threshold ({s1600:F2})");

            // SmoothCalendar Turn 2+ (dt = 1 yr, K_open > ReqCap(N, C, P)): PriorityScore stays > 4.5 (no collapse!).
            double sSmooth = MeasureCivicPriorityScore(1000, 0.0, 0.05, 0.30, 15.0, 1.0, false);
            Check(sSmooth > 4.5, $"SmoothCalendar dt=1yr Turn 2+ PriorityScore does not collapse ({sSmooth:F2})");

            // Zero-idle Stewards at C == 0 (Turn 1): grants CivicConstruction > 0 AND assigns exact 0 Stewards!
            var wT1 = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1000);
            var cT1 = wT1.Camps[0];
            cT1.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
            cT1.CivicComplexity = 0.0;
            cT1.LastEnvironmentalComplexity = 0.30;
            cT1.CivicCapital = 0.0;
            var intentT1 = DemandController.Plan(wT1, cT1, EconomicIntent.From(cT1), out var prodT1, out var allocT1);
            Check(prodT1?.Civic != null && prodT1.Civic.Construction > 0, "Turn 1 at C==0 grants CivicConstruction > 0");
            int stewardsT1 = intentT1.Assignments.Where(a => a.Role == WorkforceRoles.Steward).Sum(a => a.Population);
            Check(stewardsT1 == 0, $"Turn 1 at C==0 staffs exact 0 idle Stewards (got {stewardsT1})");

            // Continuity material-shortage immunity: zero Wood/Clay/HousingMaterial trims civic maintenance,
            // while core stewards receives 100% (Granted == Units)! Also test zero Stone/Tools!
            foreach (bool stoneShortage in new[] { false, true })
            {
                var wMat = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1000);
                var cMat = wMat.Camps[0];
                cMat.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
                cMat.CivicComplexity = 0.30;
                cMat.LastEnvironmentalComplexity = 0.30;
                cMat.CivicCapital = CivicGovernance.RequiredCivicCapital(wMat, cMat);
                cMat.Inventory.HousingMaterial = 0;
                if (stoneShortage)
                {
                    cMat.Inventory.HandTools = 0; cMat.Inventory.Tools = 0; cMat.Inventory.Stone = 0;
                    for (int i = 0; i < wMat.Tiles.Length; i++) wMat.Resources.Stone[i] = 0;
                }
                else
                {
                    cMat.Inventory.Wood = 0; cMat.Inventory.Clay = 0;
                    for (int i = 0; i < wMat.Tiles.Length; i++) { wMat.Resources.Wood[i] = 0; wMat.Resources.Clay[i] = 0; }
                }
                DemandController.Plan(wMat, cMat, EconomicIntent.From(cMat), out _, out var allocMat);
                var upkeepReq = allocMat.Requests.FirstOrDefault(r => r.Service == "civic maintenance");
                var stewReq = allocMat.Requests.FirstOrDefault(r => r.Service == "core stewards");
                Check(upkeepReq != null && upkeepReq.Granted < upkeepReq.Units - 1e-6,
                    $"Continuity {(stoneShortage ? "stone/tool" : "wood/clay")} shortage trims civic maintenance");
                Check(stewReq != null && stewReq.Units > 0, "core stewards request created");
                NearRelative(stewReq.Granted, stewReq.Units,
                    $"Continuity core stewards receives 100% under {(stoneShortage ? "stone/tool" : "wood/clay")} shortage", 1e-12);
            }

            // Partial civicBuild under tight material cap: civicStewards is strictly capped to activeTarget(civicBuild.Granted)
            // with descriptive Unmet="civic capital" and zero orphaned development Stewards.
            {
                var wPart = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1000);
                var cPart = wPart.Camps[0];
                cPart.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
                cPart.CivicComplexity = 0.30; cPart.LastEnvironmentalComplexity = 0.30;
                cPart.CivicCapital = 0;
                cPart.Inventory.Wood = 0; cPart.Inventory.Clay = 0;
                for (int i = 0; i < wPart.Tiles.Length; i++) { wPart.Resources.Wood[i] = 0; wPart.Resources.Clay[i] = 0; }
                // Cover Continuity tool/housing needs, and cap CivicConstruction at ~50% via finite Wood stock.
                cPart.Inventory.FarmTools = 5000;
                cPart.Inventory.Tools = 5000;
                double reqCapPart = CivicGovernance.RequiredCivicCapital(wPart, cPart);
                double reqStewPart = CivicGovernance.RequiredStewards(wPart, cPart);
                double housingReplace = NeolithicProduction.Demand(wPart, cPart, CraftGood.Housing, 20.0, cPart.Population);
                cPart.Inventory.HousingMaterial = housingReplace;
                cPart.Inventory.Wood = 0.5 * reqCapPart * wPart.Config.Civic.CivicBuildHousingMaterials;
                var intentPart = DemandController.Plan(wPart, cPart, EconomicIntent.From(cPart), out _, out var allocPart);
                var bReq = allocPart.Requests.FirstOrDefault(r => r.Service == "civic development");
                var sReq = allocPart.Requests.FirstOrDefault(r => r.Service == "civic stewards");
                Check(bReq != null && bReq.Granted > 1.0 && bReq.Granted < bReq.Units - 1.0,
                    $"Material-capped civic development grants partial build ({bReq?.Granted:F2}/{bReq?.Units:F2})");
                double expectedStewCap = Math.Min(1.0, bReq.Granted / reqCapPart) * reqStewPart;
                Check(sReq != null && sReq.Granted <= expectedStewCap + 1e-6 && sReq.Granted >= expectedStewCap - 1e-6,
                    $"Partial civicBuild caps civic stewards to exact activeTarget ({sReq?.Granted:F2} vs {expectedStewCap:F2})");
                Check(sReq.Unmet == "civic capital", $"Capped civic stewards reports descriptive Unmet='civic capital' (got '{sReq.Unmet}')");
                int staffedStewPart = intentPart.Assignments.Where(a => a.Role == WorkforceRoles.Steward).Sum(a => a.Population);
                Check(staffedStewPart <= (int)Math.Ceiling(expectedStewCap) + 1 && staffedStewPart >= (int)Math.Floor(expectedStewCap),
                    $"Zero orphaned development Stewards staffed ({staffedStewPart} vs expected ~{expectedStewCap:F2})");
            }

            // Steward EquipmentTarget exclusion.
            var wEq = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1000);
            var cEq = wEq.Camps[0];
            cEq.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
            cEq.CivicComplexity = 0.30; cEq.LastEnvironmentalComplexity = 0.30;
            cEq.CivicCapital = CivicGovernance.RequiredCivicCapital(wEq, cEq);
            var iWithStew = DemandController.Plan(wEq, cEq, EconomicIntent.From(cEq), out _, out var aWithStew);
            int eqStew = aWithStew.Staff.TryGetValue(WorkforceRoles.Steward, out int st) ? st : 0;
            int expectedEqTarget = Math.Min(cEq.Population, aWithStew.Staff
                .Where(p => !WorkforceRoles.Extractors.Contains(p.Key) && p.Key != WorkforceRoles.Herder && p.Key != WorkforceRoles.Farmer && p.Key != WorkforceRoles.Steward)
                .Sum(p => p.Value));
            Check(eqStew > 0 && iWithStew.Labor.EquipmentTarget == expectedEqTarget,
                $"EquipmentTarget ({iWithStew.Labor.EquipmentTarget}) excludes Stewards ({eqStew})");
        }

        // ---- 6.4 Tier 3A Trade (range, requisitions, margin reduction, RecordTradeDiffusion) ----
        {
            WorldState MakeTradeWorld(int distance, int extraPartners, bool req3A, bool resp3A, double diffusionRate = 0.0, bool paleolithicReceiver = false)
            {
                int totalCamps = 2 + extraPartners;
                var w = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 400, players: totalCamps + 1);
                w.Config.TradeEnabled = true;
                w.Config.TradeRange = 4;
                w.Config.TradeRequisitionsPerCamp = 2;
                w.Config.TradeMargin = 0.20;
                w.Config.TradeDistanceCostPerHex = 0.01;
                w.Config.Civic.PrecedentDiffusionPerTrade = diffusionRate;
                w.Camps.Clear();
                w.Societies.Clear();

                // Player 0 dummy camp far away (Trade excludes Player 0).
                var p0 = new Camp { Id = w.NextId++, Player = 0, Tile = 0, Population = 100, PlantFood = 5000 };
                w.Camps.Add(p0);

                // Requester (Player 1) at (4, 8): abundant Cloth, scarce HousingMaterial.
                int reqTile = 8 * w.Config.Width + 4;
                var req = new Camp { Id = w.NextId++, Player = 1, Tile = reqTile, Population = 400, PlantFood = 16000, IsVillage = true };
                req.Inventory.Cloth = 800; req.Inventory.HousingMaterial = 0; req.Inventory.Tools = 100;
                w.Camps.Add(req);

                // Partners (Players 2..): scarce Cloth, abundant HousingMaterial.
                var partners = new List<Camp>();
                for (int i = 0; i < 1 + extraPartners; i++)
                {
                    int pTile = 8 * w.Config.Width + 4 + distance + (i == 0 ? 0 : 0);
                    if (i > 0) pTile = (8 + (i % 2 == 0 ? i / 2 : -(i + 1) / 2)) * w.Config.Width + 4 + Math.Min(distance, 2);
                    var resp = new Camp { Id = w.NextId++, Player = 2 + i, Tile = pTile, Population = 400, PlantFood = 16000, IsVillage = true };
                    resp.Inventory.Cloth = 0; resp.Inventory.HousingMaterial = 600; resp.Inventory.Tools = 100;
                    w.Camps.Add(resp);
                    partners.Add(resp);
                }
                Ownership.Bind(w);

                foreach (var camp in w.Camps)
                {
                    camp.Technology = Technology.Cultivation;
                    camp.Technologies[(int)Technology.Cultivation] = true;
                    camp.People.Progression.Era = Era.Neolithic;
                    camp.People.NeolithicTechnologies.AddRange(new[] { NeolithicTech.Weaving, NeolithicTech.Construction, NeolithicTech.Toolmaking });
                }
                if (paleolithicReceiver)
                    req.People.Progression.Era = Era.Paleolithic;
                if (req3A)
                    req.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                if (resp3A)
                    foreach (var p in partners)
                        p.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });

                foreach (var p in partners)
                {
                    ContactKnowledge.RecordTrade(w, req.People, p);
                    ContactKnowledge.RecordTrade(w, p.People, req);
                }
                return w;
            }

            // Range bonus (+2 hexes): distance 6 > base TradeRange 4, <= 4 + 2 = 6.
            var wRangeOff = MakeTradeWorld(distance: 6, extraPartners: 0, req3A: false, resp3A: false);
            var wRangeOn = MakeTradeWorld(distance: 6, extraPartners: 0, req3A: true, resp3A: false);
            Check(Trade.Plan(wRangeOff).Agreements.Count == 0, "Without Tier 3A, partner at TradeRange+2 is out of trade range");
            Check(Trade.Plan(wRangeOn).Agreements.Count > 0, "With Tier 3A, partner at TradeRange+2 trades via TokensTradeRangeBonus");

            // Requisition bonus (+2): base 2 + bonus 2 = 4 partners contacted instead of 2.
            // Give each partner modest HousingMaterial (20) and one-way contact from req so partners do not pre-empt req as responders.
            var wReqOff = MakeTradeWorld(distance: 2, extraPartners: 4, req3A: false, resp3A: false);
            var wReqOn = MakeTradeWorld(distance: 2, extraPartners: 4, req3A: true, resp3A: false);
            foreach (var tw in new[] { wReqOff, wReqOn })
            {
                for (int pi = 2; pi < tw.Camps.Count; pi++)
                {
                    tw.Camps[pi].People.Memory.Contacts.Clear();
                    NeolithicProduction.AddFixedHousing(tw, tw.Camps[pi],
                        tw.Camps[pi].Population / tw.Config.Neolithic.HousingPeoplePerUnit, tw.Days);
                    tw.Camps[pi].Inventory.HousingMaterial = 25;
                }
            }
            var planOff = Trade.Plan(wReqOff);
            var planOn = Trade.Plan(wReqOn);
            int pOff = planOff.Agreements.Count(a => a.Requester == wReqOff.Camps[1].Id);
            int pOn = planOn.Agreements.Count(a => a.Requester == wReqOn.Camps[1].Id);
            Check(planOff.Summary.Requisitions == 2 && planOn.Summary.Requisitions == 4,
                $"Tier 3A increases trade requisitions from 2 to 4 (got off={planOff.Summary.Requisitions}, on={planOn.Summary.Requisitions})");
            Check(pOff <= 2 && pOn > 2, $"Tier 3A closes more partner agreements beyond base limit ({pOn} > 2 vs {pOff} <= 2)");

            // Margin reduction: exact multiplier checks 1.0 / 0.75 / 0.5625.
            NearRelative(CivicGovernance.TradeMarginMultiplier(wRangeOn.Config, false, false), 1.0, "TradeMarginMultiplier(false,false) == 1.0", 1e-12);
            NearRelative(CivicGovernance.TradeMarginMultiplier(wRangeOn.Config, true, false), 0.75, "TradeMarginMultiplier(true,false) == 0.75", 1e-12);
            NearRelative(CivicGovernance.TradeMarginMultiplier(wRangeOn.Config, false, true), 0.75, "TradeMarginMultiplier(false,true) == 0.75", 1e-12);
            NearRelative(CivicGovernance.TradeMarginMultiplier(wRangeOn.Config, true, true), 0.5625, "TradeMarginMultiplier(true,true) == 0.5625", 1e-12);

            // Borderline trade margin test: give both camps moderate comparative advantage so the 0-side, 1-side
            // (0.75x) and 2-side (0.5625x) break-even TradeMargin values sit cleanly in (0, 1).
            WorldState MakeModerateTradeWorld(bool req3A, bool resp3A)
            {
                var tw = MakeTradeWorld(distance: 1, extraPartners: 0, req3A: req3A, resp3A: resp3A);
                tw.Camps[1].Inventory.Cloth = 420; tw.Camps[1].Inventory.HousingMaterial = 90;
                tw.Camps[2].Inventory.Cloth = 390; tw.Camps[2].Inventory.HousingMaterial = 110;
                return tw;
            }
            var wMarginBase = MakeModerateTradeWorld(false, false);
            var wMarginOne = MakeModerateTradeWorld(true, false);
            var wMarginBoth = MakeModerateTradeWorld(true, true);
            double FindMaxAcceptedMargin(WorldState tw)
            {
                double lo = 0.0, hi = 0.5;
                for (int grow = 0; grow < 25; grow++)
                {
                    tw.Config.TradeMargin = hi;
                    if (Trade.Plan(tw).Agreements.Count == 0) break;
                    lo = hi; hi *= 2.0;
                }
                for (int iter = 0; iter < 40; iter++)
                {
                    double mid = 0.5 * (lo + hi);
                    tw.Config.TradeMargin = mid;
                    if (Trade.Plan(tw).Agreements.Count > 0) lo = mid; else hi = mid;
                }
                return lo;
            }
            double m0 = FindMaxAcceptedMargin(wMarginBase);
            double m1 = FindMaxAcceptedMargin(wMarginOne);
            double m2 = FindMaxAcceptedMargin(wMarginBoth);
            Check(m0 > 1e-4, $"Moderate comparative-advantage pair trades at positive margin (m0={m0:F4})");
            NearRelative(m1, m0 / 0.75, $"1-side Tier 3A raises max acceptable TradeMargin by exact 1/0.75 (m0={m0:F4}, m1={m1:F4})", 1e-3);
            NearRelative(m2, m0 / 0.5625, $"2-side Tier 3A raises max acceptable TradeMargin by exact 1/0.5625 (m0={m0:F4}, m2={m2:F4})", 1e-3);
            double mid01 = 0.5 * (m0 + m1);
            wMarginBase.Config.TradeMargin = mid01; wMarginOne.Config.TradeMargin = mid01; wMarginBoth.Config.TradeMargin = mid01;
            Check(Trade.Plan(wMarginBase).Agreements.Count == 0 && Trade.Plan(wMarginOne).Agreements.Count > 0,
                $"Margin {mid01:F4} rejected at 0 sides and accepted at 1 side (0.75x)");
            double mid12 = 0.5 * (m1 + m2);
            wMarginOne.Config.TradeMargin = mid12; wMarginBoth.Config.TradeMargin = mid12;
            Check(Trade.Plan(wMarginOne).Agreements.Count == 0 && Trade.Plan(wMarginBoth).Agreements.Count > 0,
                $"Margin {mid12:F4} rejected at 1 side (0.75x) and accepted at 2 sides (0.5625x)");

            // RecordTradeDiffusion in Trade.Finish: inert at 0, active at 0.25 for Neolithic, 0 for Paleolithic.
            var wDiff0 = MakeTradeWorld(distance: 2, extraPartners: 0, req3A: false, resp3A: true, diffusionRate: 0.0);
            var pDiff0 = Trade.Plan(wDiff0);
            var recDiff0 = new TurnRecord();
            var sDiff0 = Trade.Settle(wDiff0, pDiff0, recDiff0);
            Trade.SettleAfterProduction(wDiff0, sDiff0, recDiff0);
            Trade.Finish(wDiff0, sDiff0, recDiff0);
            Check(recDiff0.Trades.Count > 0 && wDiff0.Camps[1].People.CivicExperience.Count == 0,
                "Trade.Finish accrues exact 0 civic diffusion when PrecedentDiffusionPerTrade == 0");

            var wDiffOn = MakeTradeWorld(distance: 2, extraPartners: 0, req3A: false, resp3A: true, diffusionRate: 0.25);
            var pDiffOn = Trade.Plan(wDiffOn);
            var recDiffOn = new TurnRecord();
            var sDiffOn = Trade.Settle(wDiffOn, pDiffOn, recDiffOn);
            Trade.SettleAfterProduction(wDiffOn, sDiffOn, recDiffOn);
            Trade.Finish(wDiffOn, sDiffOn, recDiffOn);
            double diffXp = 0;
            bool hasDiffXp = wDiffOn.Camps[1].People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out diffXp);
            Check(recDiffOn.Trades.Count > 0 && hasDiffXp && diffXp > 0,
                $"Trade.Finish diffuses CeremonialIntegration experience ({diffXp:F2}) when PrecedentDiffusionPerTrade == 0.25");

            var wDiffPaleo = MakeTradeWorld(distance: 2, extraPartners: 0, req3A: false, resp3A: true, diffusionRate: 0.25, paleolithicReceiver: true);
            CivicGovernance.RecordTradeDiffusion(wDiffPaleo, wDiffPaleo.Camps[1].People, wDiffPaleo.Camps[2].People, 100.0);
            Check(wDiffPaleo.Camps[1].People.CivicExperience.Count == 0,
                "RecordTradeDiffusion gives exact 0 civic experience to Paleolithic receiver");
        }

        // ---- 6.5 Immigration.PolicyFor, Fission.EffectiveMoverThreshold & live --bound-audit ----
        {
            var wPol = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 1000, players: 2);
            var src = wPol.Camps[0];
            var dst = new Camp { Id = wPol.NextId++, Player = 1, Tile = src.Tile + 2, Population = 1000, PlantFood = 40000, IsVillage = true };
            wPol.Camps.Add(dst);
            Ownership.Bind(wPol);
            dst.Technology = Technology.Cultivation; dst.Technologies[(int)Technology.Cultivation] = true;
            dst.People.Progression.Era = Era.Neolithic;
            foreach (var c in new[] { src, dst })
            {
                c.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                c.CivicComplexity = 1.0;
                c.CivicStratification = 0.0;
                c.LastEnvironmentalStratification = 0.0;
                c.LastCircumscription = 0.0;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(wPol, c) * 2.0;
                c.Workforce = new PopulationLedger();
                c.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wPol, c)) + 10, c.Population);
            }

            // Immigration.PolicyFor exact costs & incumbent tolerances.
            dst.ImmigrationPolicy = ImmigrationPolicy.Neutral;
            var pNeut = Immigration.PolicyFor(wPol, src, dst, 2, 0);
            dst.ImmigrationPolicy = ImmigrationPolicy.Open;
            var pOpen = Immigration.PolicyFor(wPol, src, dst, 2, 0);
            dst.ImmigrationPolicy = ImmigrationPolicy.Restricted;
            var pRest = Immigration.PolicyFor(wPol, src, dst, 2, 0);
            NearRelative(pNeut.PrivateCost - pOpen.PrivateCost, 0.030, "Open lowers PrivateCost by 0.030", 1e-12);
            NearRelative(pOpen.IncumbentHarmTolerance, wPol.Config.FissionDaughterMargin + 0.020, "Open raises IncumbentHarmTolerance by 0.020", 1e-12);
            NearRelative(pRest.PrivateCost - pNeut.PrivateCost, 0.150, "Restricted raises PrivateCost by 0.150", 1e-12);
            NearRelative(pRest.IncumbentHarmTolerance, wPol.Config.FissionDaughterMargin * 0.30, "Restricted scales IncumbentHarmTolerance by 0.30", 1e-12);

            // P pull on destination (Egalitarian P=-1 vs Stratified P=+1 -> exact 0.050 * C * I_civic difference).
            dst.ImmigrationPolicy = ImmigrationPolicy.Neutral;
            dst.CivicStratification = -1.0; dst.LastEnvironmentalStratification = -1.0;
            dst.CivicCapital = CivicGovernance.RequiredCivicCapital(wPol, dst) * 2.0;
            dst.Workforce = new PopulationLedger();
            dst.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wPol, dst)) + 10, dst.Population);
            var pEgalDst = Immigration.PolicyFor(wPol, src, dst, 2, 0);
            dst.CivicStratification = 1.0; dst.LastEnvironmentalStratification = 1.0;
            dst.CivicCapital = CivicGovernance.RequiredCivicCapital(wPol, dst) * 2.0;
            dst.Workforce = new PopulationLedger();
            dst.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wPol, dst)) + 10, dst.Population);
            var pStratDst = Immigration.PolicyFor(wPol, src, dst, 2, 0);
            NearRelative(pStratDst.PrivateCost - pEgalDst.PrivateCost, 0.050,
                "Stratified vs Egalitarian destination PrivateCost delta == 0.050 * C * I_civic", 1e-12);

            // Source ExitMismatch push (-0.030 * M_exit).
            src.CivicStratification = 1.0; src.LastEnvironmentalStratification = -0.5; src.LastCircumscription = 0.15;
            double mExit = CivicGovernance.ExitMismatch(src);
            NearRelative(mExit, 1.275, "Coerced open-frontier ExitMismatch == 1.275", 1e-12);
            var pCoercedSrc = Immigration.PolicyFor(wPol, src, dst, 2, 0);
            NearRelative(pStratDst.PrivateCost - pCoercedSrc.PrivateCost, 0.030 * mExit,
                "Source ExitMismatch lowers PrivateCost by 0.030 * M_exit", 1e-12);

            // Fission.EffectiveMoverThreshold exact values & C==0 / custom-G* preservation.
            wPol.Config.FissionGainThreshold = 0.113;
            src.FissionPolicy = FissionPolicy.Neutral;
            src.CivicStratification = -1.0; src.LastEnvironmentalStratification = -1.0; src.LastCircumscription = 0.0;
            src.CivicCapital = CivicGovernance.RequiredCivicCapital(wPol, src) * 2.0;
            src.Workforce = new PopulationLedger();
            src.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wPol, src)) + 10, src.Population);
            NearRelative(Fission.EffectiveMoverThreshold(wPol, src), 0.068, "Aligned Egalitarian EffectiveMoverThreshold == 0.068", 1e-12);
            src.CivicStratification = 1.0; src.LastEnvironmentalStratification = 1.0;
            src.CivicCapital = CivicGovernance.RequiredCivicCapital(wPol, src) * 2.0;
            src.Workforce = new PopulationLedger();
            src.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wPol, src)) + 10, src.Population);
            NearRelative(Fission.EffectiveMoverThreshold(wPol, src), 0.158, "Aligned Stratified EffectiveMoverThreshold == 0.158", 1e-12);
            src.CivicStratification = 1.0; src.LastEnvironmentalStratification = -0.5; src.LastCircumscription = 0.15;
            src.CivicCapital = CivicGovernance.RequiredCivicCapital(wPol, src) * 2.0;
            src.Workforce = new PopulationLedger();
            src.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wPol, src)) + 10, src.Population);
            NearRelative(Fission.EffectiveMoverThreshold(wPol, src), 0.0815, "Coerced Stratified open-frontier EffectiveMoverThreshold == 0.0815", 1e-12);

            // C == 0 preserves custom FissionGainThreshold (0 and 5.0) bit-identically; null camp never throws.
            src.CivicComplexity = 0.0;
            wPol.Config.FissionGainThreshold = 0.0;
            Check(Fission.EffectiveMoverThreshold(wPol, src) == 0.0, "EffectiveMoverThreshold preserves G*=0 when C==0");
            wPol.Config.FissionGainThreshold = 5.0;
            Check(Fission.EffectiveMoverThreshold(wPol, src) == 5.0, "EffectiveMoverThreshold preserves G*=5 when C==0");
            Check(Fission.EffectiveMoverThreshold(wPol, null) == Kinship.GainThreshold(wPol.Config, FissionPolicy.Neutral),
                "EffectiveMoverThreshold(w, null) safely returns Neutral threshold without throwing");

            // Live multi-camp Immigration.Resolve + Fission.Resolve under AuditBounds = true (0 bound violations).
            wPol.Config.FissionGainThreshold = 0.113;
            wPol.Config.ImmigrationEnabled = true;
            wPol.Config.FissionEnabled = true;
            src.CivicComplexity = 0.75; src.CivicStratification = 0.8; src.LastEnvironmentalStratification = -0.5; src.LastCircumscription = 0.1;
            dst.CivicComplexity = 0.75; dst.CivicStratification = -0.8; dst.LastEnvironmentalStratification = -0.8; dst.ImmigrationPolicy = ImmigrationPolicy.Open;
            ContactKnowledge.RecordTrade(wPol, src.People, dst);
            ContactKnowledge.RecordTrade(wPol, dst.People, src);
            foreach (var c in new[] { src, dst })
            {
                var mem = ContactKnowledge.MemoryOf(wPol, c.People);
                foreach (int id in wPol.Map.Radius(c.Tile, 5)) mem.Explored.Add(id);
            }
            bool savedAudit = SocialRelocationForecastCache.AuditBounds;
            SocialRelocationForecastCache.AuditBounds = true;
            var liveMetrics = new ResolutionMetrics();
            try
            {
                var rLive = new TurnRecord();
                var liveForecasts = new SocialRelocationForecastCache(wPol, wPol.Camps);
                var sat = Immigration.Resolve(wPol, rLive, null, liveMetrics, null, liveForecasts);
                Fission.Resolve(wPol, rLive, null, liveMetrics, sat, null, liveForecasts);
            }
            finally { SocialRelocationForecastCache.AuditBounds = savedAudit; }
            Check(liveMetrics.SocialCandidatesEvaluated > 0,
                $"Live multi-camp Immigration.Resolve + Fission.Resolve evaluated {liveMetrics.SocialCandidatesEvaluated} social candidates with AuditBounds=true (0 bound violations)");
        }

        // ---- 6.6 HoverDetails owner-only CIVIC line, zero rival leakage & multi-policy determinism ----
        {
            var wHov = Phase1VillageFixture(enableCivic: false, sanitation: false, population: 600, players: 2);
            var own = wHov.Camps[0];
            var rival = new Camp { Id = wHov.NextId++, Player = 1, Tile = wHov.Map.Neighbors(own.Tile).First(), Population = 700, PlantFood = 10000, IsVillage = true };
            wHov.Camps.Add(rival);
            Ownership.Bind(wHov);
            var obsBuilder = new AgentObservationBuilder();

            // Civic == null -> zero CIVIC line.
            var obsOff = obsBuilder.Build(wHov, 0);
            string descOff = TileDetails.Describe(obsOff, own.Tile, Array.Empty<Order>());
            Check(!descOff.Contains("CIVIC ·"), "HoverDetails omits CIVIC line when Civic==null");

            // Civic.Enabled=true, SanitationEnabled=false.
            wHov.Config.Civic = new CivicGovernanceSettings { Enabled = true, SanitationEnabled = false };
            own.People.CivicPrecedents.AddRange(new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            own.CivicComplexity = 0.55; own.CivicStratification = -0.40;
            own.LastEnvironmentalStratification = -0.40; own.CivicFocus = CivicFocus.Auto; own.ImmigrationPolicy = ImmigrationPolicy.Open;
            own.CivicCapital = CivicGovernance.RequiredCivicCapital(wHov, own);
            own.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wHov, own)), own.Population);
            rival.CivicComplexity = 0.91; rival.CivicStratification = 0.87;
            rival.LastEnvironmentalStratification = 0.87; rival.ImmigrationPolicy = ImmigrationPolicy.Restricted;

            var obsPh1 = obsBuilder.Build(wHov, 0);
            string descOwn1 = TileDetails.Describe(obsPh1, own.Tile, Array.Empty<Order>());
            string descRival1 = TileDetails.Describe(obsPh1, rival.Tile, Array.Empty<Order>());
            Check(descOwn1.Contains("CIVIC ·") && descOwn1.Contains("C 0.55") && descOwn1.Contains("P -0.40") &&
                  descOwn1.Contains("Communal") && descOwn1.Contains("I_civic") && descOwn1.Contains("Immigration Open"),
                "HoverDetails renders owner CIVIC summary line (" + descOwn1 + ")");
            Check(!descOwn1.Contains("Sanit"), "HoverDetails omits Sanit segment when SanitationEnabled=false");
            Check(!descRival1.Contains("CIVIC ·") && !descRival1.Contains("0.91") && !descRival1.Contains("+0.87") && !descRival1.Contains("Restricted"),
                "HoverDetails never leaks rival civic fields");

            // Mutating rival civic state produces byte-identical hover on both own and rival tiles!
            rival.CivicComplexity = 0.12; rival.CivicStratification = -0.66; rival.ImmigrationPolicy = ImmigrationPolicy.Open;
            var obsPh1Mut = obsBuilder.Build(wHov, 0);
            Check(TileDetails.Describe(obsPh1Mut, own.Tile, Array.Empty<Order>()) == descOwn1,
                "Mutating rival civic state leaves own hover byte-identical");
            Check(TileDetails.Describe(obsPh1Mut, rival.Tile, Array.Empty<Order>()) == descRival1,
                "Mutating rival civic state leaves rival hover byte-identical");

            // SanitationEnabled=true adds Sanit & water to owner tile.
            wHov.Config.Civic.SanitationEnabled = true;
            own.SanitationWaterRatio = 0.85;
            var obsPh2 = obsBuilder.Build(wHov, 0);
            string descOwn2 = TileDetails.Describe(obsPh2, own.Tile, Array.Empty<Order>());
            Check(descOwn2.Contains("Sanit") && descOwn2.Contains("water 85%"), "HoverDetails includes Sanit and water when SanitationEnabled=true");

            // Multi-policy queued order permutation invariance.
            var qForward = new[]
            {
                new Order(OrderKind.Economy, own.Id) { Economy = EconomicIntent.From(own) },
                new Order(OrderKind.FissionPolicy, own.Id, target: (int)FissionPolicy.Encourage),
                new Order(OrderKind.ImmigrationPolicy, own.Id, target: (int)ImmigrationPolicy.Open),
                new Order(OrderKind.CivicFocus, own.Id, target: (int)CivicFocus.Communal)
            };
            var qReverse = qForward.Reverse().ToArray();
            Check(TileDetails.Describe(obsPh1, own.Tile, qForward) == TileDetails.Describe(obsPh1, own.Tile, qReverse),
                "HoverDetails queued multi-policy order output is permutation-invariant");
        }

        // ---- 6.7 Bit-Exact Zero-Regression When Civic == null ----
        {
            var wZeroA = Phase1VillageFixture(enableCivic: false, sanitation: false, population: 600);
            wZeroA.Config.FissionEnabled = true;
            wZeroA.Config.ImmigrationEnabled = true;
            wZeroA.Config.TradeEnabled = true;
            var c0 = wZeroA.Camps[0];
            Check(CivicGovernance.CrowdingMitigation(wZeroA, c0) == 1.0 &&
                  CivicGovernance.DiseaseMitigation(wZeroA, c0) == 1.0 &&
                  CivicGovernance.CohesionMultiplier(wZeroA, c0) == 1.0 &&
                  CivicGovernance.QoLEqualityMultiplier(wZeroA, c0) == 1.0 &&
                  CivicGovernance.PublicWorksMobilizationMultiplier(wZeroA, c0) == 1.0 &&
                  CivicGovernance.GranaryStorageMultiplier(wZeroA, c0) == 1.0 &&
                  CivicGovernance.GranaryStorageMultiplierLowerBound(wZeroA, c0, 20.0) == 1.0,
                "All civic multipliers return exact 1.0 when Civic==null");
            Check(Fission.EffectiveMoverThreshold(wZeroA, c0) == Kinship.GainThreshold(wZeroA.Config, c0.FissionPolicy),
                "EffectiveMoverThreshold matches Kinship.GainThreshold bit-for-bit when Civic==null");

            var wZeroB = SaveCodec.Load(SaveCodec.Save(wZeroA));
            var resolver = new TurnResolver();
            TurnRecord lastA = null;
            for (int t = 0; t < 3; t++)
            {
                lastA = resolver.Resolve(wZeroA, new Dictionary<int, List<Order>>());
                resolver.Resolve(wZeroB, new Dictionary<int, List<Order>>());
            }
            Check(lastA.CivicReports == null && lastA.SanitationWater == null, "Civic==null leaves CivicReports and SanitationWater null");
            Check(SaveCodec.Fingerprint(wZeroA) == SaveCodec.Fingerprint(wZeroB), "Civic==null multi-turn save/reload fingerprint matches bit-for-bit");
        }
    }

    // ---- M4 Phase 2 sanitation contracts (Docs/CivicGovernance.md §5 Choice #10) ----
    private static void Phase2SanitationContracts()
    {
        // Recipes 15/16 exist only with SanitationEnabled and are usable only after Tier 3B.
        var off = Phase1VillageFixture(enableCivic: true, sanitation: false, population: 3000);
        Check(!NeolithicProduction.Recipes(off.Config).Any(r => CivicGovernance.IsSanitationGood(r.Good)),
            "Sanitation recipes are absent when SanitationEnabled=false");
        Check(PhysicalDemography.DiseaseMitigation(off, off.Camps[0]) == 1.0, "SanitationEnabled=false keeps DiseaseMitigation == 1");
        var w = Phase1VillageFixture(enableCivic: true, sanitation: true, population: 3000);
        var c = w.Camps[0];
        var sanitRecipes = NeolithicProduction.Recipes(w.Config).Where(r => CivicGovernance.IsSanitationGood(r.Good)).ToArray();
        Check(sanitRecipes.Length == 2 && sanitRecipes.All(r => r.Specialist == WorkforceRoles.Builder && r.SpecialistOnly && r.Rate > 0),
            "SanitationConstruction/Maintenance are Builder-only recipes when SanitationEnabled");
        Check((int)CraftGood.SanitationConstruction == 15 && (int)CraftGood.SanitationMaintenance == 16, "CraftGood sanitation IDs are append-only (15/16)");
        Check(sanitRecipes.All(r => !NeolithicProduction.RecipeAvailable(w, c, r)), "Sanitation recipes unavailable before Tier 3B");
        c.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        c.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        c.People.CivicPrecedents.Add(CivicPrecedents.PlannedQuartersAndDrainage);
        Check(sanitRecipes.All(r => NeolithicProduction.RecipeAvailable(w, c, r)), "Sanitation recipes available after Tier 3B");

        // M_d floor and monotonicity.
        var cfg = w.Config;
        double mdFull = CivicGovernance.DiseaseMitigationFor(cfg, 1, 1, 1);
        Check(mdFull >= cfg.Civic.MinDiseaseMitigation - 1e-15 && Math.Abs(cfg.Civic.MinDiseaseMitigation - 0.15) < 1e-15,
            "Disease mitigation respects the 0.15 floor (" + mdFull + ")");
        Check(CivicGovernance.DiseaseMitigationFor(cfg, 1, 0, 0) == 1.0 && CivicGovernance.DiseaseMitigationFor(cfg, 0, 0, 1) == 1.0,
            "Zero coverage or zero complexity gives M_d == 1");
        Check(CivicGovernance.DiseaseMitigationFor(cfg, 0.8, 0, 0.5) > CivicGovernance.DiseaseMitigationFor(cfg, 0.8, 0, 1.0),
            "More sanitation coverage lowers M_d");

        // dt-invariant retention: two 10-year halves equal one 20-year turn (unpaid and proportionally paid).
        double d = CivicGovernance.SanitationDecayRate(cfg);
        double k0 = 480;
        NearRelative(CivicGovernance.RetainCapital(CivicGovernance.RetainCapital(k0, d, 10, 0), d, 10, 0),
            CivicGovernance.RetainCapital(k0, d, 20, 0), "Unpaid sanitation decay is dt-invariant", 1e-12);
        double half = 0.5 * d * k0;
        NearRelative(CivicGovernance.RetainCapital(CivicGovernance.RetainCapital(k0, d, 10, half * 10), d, 10, half * 10),
            CivicGovernance.RetainCapital(k0, d, 20, half * 20), "Partially paid sanitation retention is dt-invariant", 1e-12);
        c.SanitationCapital = CivicGovernance.RequiredSanitationCapital(cfg, c.Population);
        double target = CivicGovernance.SanitationMaintenanceTarget(w, c, 20);
        Near(CivicGovernance.RetainCapital(c.SanitationCapital, d, 20, target), c.SanitationCapital,
            "Full sanitation upkeep retains the stock exactly", 1e-9);

        // Tier-2 water: conserved per tile, after biological claims, works with and without the agriculture block.
        foreach (bool withAgriculture in new[] { true, false })
        {
            var ww = Phase1VillageFixture(enableCivic: true, sanitation: true, population: 3000);
            if (!withAgriculture) ww.Config.Agriculture = null;
            var wc = ww.Camps[0];
            wc.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
            wc.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
            wc.People.CivicPrecedents.Add(CivicPrecedents.PlannedQuartersAndDrainage);
            wc.SanitationCapital = CivicGovernance.RequiredSanitationCapital(ww.Config, wc.Population);
            double days = 20 * ww.Config.DaysPerYear;
            double need = CivicGovernance.SanitationWaterNeed(ww, wc, 20);
            Check(need > 0, "Sanitation water need is positive for a capitalised Tier-3B village");
            var claims = new List<Demand>();
            var record = new TurnRecord();
            var touched = new HashSet<int>();
            CivicGovernance.AllocateSanitationWater(ww, claims, ww.Days, days, record, t => touched.Add(t));
            string tag = withAgriculture ? " (agriculture)" : " (Agriculture == null)";
            Check(claims.Count > 0 && claims.All(x => x.Kind == "SanitationWater" && x.Entity == wc.Id && x.Freshwater > 0),
                "Sanitation water emits positive SanitationWater claims" + tag);
            Check(claims.All(x => touched.Contains(x.Tile)), "Every sanitation source tile is activated" + tag);
            double reservePerTile = withAgriculture ? 0 : wc.Population * 0.10 * 20;
            int src = FreshwaterAccess.Source(ww, wc.Tile);
            foreach (var g in claims.GroupBy(x => x.Tile))
            {
                double env = IrrigationEconomy.WaterEnvelope(ww, g.Key, ww.Days, days);
                double reserve = g.Key == src ? reservePerTile : 0;
                Check(g.Sum(x => x.Freshwater) <= Math.Max(0, env - reserve) * (1 + 1e-9) + 1e-9,
                    "Sanitation withdrawals stay within the tile envelope after drinking" + tag);
            }
            double delivered = claims.Sum(x => x.Freshwater);
            Check(delivered <= need * (1 + 1e-9) && wc.SanitationWaterRatio > 0 && wc.SanitationWaterRatio <= 1,
                "Sanitation delivery is bounded by need and sets a ratio in (0,1]" + tag);
            Near(wc.SanitationWaterRatio, Math.Min(1, delivered / need), "Sanitation ratio equals delivered/need" + tag, 1e-9);
            Check(record.SanitationWater != null && Math.Abs(record.SanitationWater.Sum(r => r.Withdrawal) - delivered) < 1e-9,
                "Sanitation receipts reconcile with claims" + tag);

            // Biological claims saturating every local source leave nothing for sanitation.
            var saturated = new List<Demand>();
            foreach (int t in ww.Map.Radius(wc.Tile, 1))
                saturated.Add(new Demand { Tile = t, Entity = -1, Kind = "Drinking", Freshwater = IrrigationEconomy.WaterEnvelope(ww, t, ww.Days, days) + 1 });
            int before = saturated.Count;
            CivicGovernance.AllocateSanitationWater(ww, saturated, ww.Days, days, null, null);
            Check(saturated.Count == before && wc.SanitationWaterRatio == 0,
                "Drinking/biological water ranks before sanitation" + tag);
        }

        // Dry oasis: no local freshwater means wAvail ~ 0 and DemandControl never builds drains.
        var dry = Phase1VillageFixture(enableCivic: true, sanitation: true, population: 3000);
        var dc = dry.Camps[0];
        dc.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        dc.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        dc.People.CivicPrecedents.Add(CivicPrecedents.PlannedQuartersAndDrainage);
        dc.CivicComplexity = 0.8;
        Check(CivicGovernance.ForecastSanitationWaterFraction(dry, dc, 20) > 0.5, "Wet village forecasts usable sanitation water");
        foreach (int t in dry.Map.Radius(dc.Tile, 1)) dry.Tiles[t].FreshwaterSource = FreshwaterSource.None;
        Check(CivicGovernance.ForecastSanitationWaterFraction(dry, dc, 20) < 0.05, "Dry oasis forecasts wAvail ~ 0");
        new TurnResolver().Resolve(dry, new Dictionary<int, List<Order>>());
        Check(dc.SanitationCapital == 0 && dc.SanitationWaterRatio == 0 && CivicGovernance.SanitationCoverage(dry, dc) == 0,
            "Dry oasis builds no sanitation capital and has zero coverage");

        // Fission daughters start with zero sanitation state.
        var daughter = new Camp { SanitationCapital = 99, SanitationWaterRatio = 0.7 };
        c.SanitationWaterRatio = 0.8;
        CivicGovernance.InitializeDaughterView(w, c, daughter);
        Check(daughter.SanitationCapital == 0 && daughter.SanitationWaterRatio == 0 && CivicGovernance.ActiveSanitationCapital(daughter) == 0,
            "Fission daughter starts with zero sanitation capital, water and active stock");

        // The ~3,400 disease wall: physical equilibrium with best crowding mitigation and no sanitation vs full sanitation.
        double Equilibrium(double md)
        {
            double lo = 500, hi = 60000;
            double mc = cfg.Civic.MinCrowdingMitigation;
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5 * (lo + hi);
                double net = PhysicalDemography.Rates(cfg, mid, 1, 1, mid, false, 0, mc, md).Net;
                if (net > 0) lo = mid; else hi = mid;
            }
            return 0.5 * (lo + hi);
        }
        double wall = Equilibrium(1.0);
        double sanitised = Equilibrium(CivicGovernance.DiseaseMitigationFor(cfg, 1, 0, 1));
        Check(wall >= 2800 && wall <= 4000, "Unsanitised disease wall sits near ~3,400 (" + wall.ToString("F0") + ")");
        Check(sanitised >= 5000 && sanitised <= 12000, "Full sanitation lifts the equilibrium into 5k-10k (" + sanitised.ToString("F0") + ")");
    }

    // Phase-2 gate: from the Phase-1 ceiling, a well-watered, well-fed village with sanitation climbs past the ~3,400
    // disease wall while the matched SanitationEnabled=false arm stays at it. Land is widened to radius 2 so food
    // capacity is not the binding limit; civic state is seeded at the Phase-1 endpoint, Tier 3B accrues organically.
    private static WorldState Phase2Fixture(bool sanitation)
    {
        var w = Phase1VillageFixture(enableCivic: true, sanitation: sanitation, population: 3000);
        w.Config.TurnDays = 5.0 * w.Config.DaysPerYear;
        var c = w.Camps[0];
        foreach (int id in w.Map.Radius(c.Tile, 2))
        {
            var t = w.Tiles[id];
            t.CultivationCapital = 1.0;
            t.Cultivation = (float)CultivationEconomy.Intensity(w.Config, 1.0);
            t.Cultivated = true;
            t.CultivationOperator = c.Id;
            t.CultivationOperatingFraction = 0;
        }
        c.People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
        c.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
        c.CivicComplexity = CivicGovernance.MaxComplexity(w, c);
        c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
        c.Workforce.Assign(WorkforceRoles.Steward, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)), c.Population);
        c.PlantFood = c.Population * 60.0;
        return w;
    }

    private static void Phase2OrganicWallBreakGate()
    {
        var san = Phase2Fixture(true);
        var ctl = Phase2Fixture(false);
        var sc = san.Camps[0];
        var cc = ctl.Camps[0];
        var resolver = new TurnResolver();
        int peakS = sc.Population, peakC = cc.Population;
        var trace = new System.Text.StringBuilder();
        for (int turn = 0; turn < 120; turn++)
        {
            resolver.Resolve(san, new Dictionary<int, List<Order>>());
            resolver.Resolve(ctl, new Dictionary<int, List<Order>>());
            peakS = Math.Max(peakS, sc.Population);
            peakC = Math.Max(peakC, cc.Population);
            if ((turn + 1) % 20 == 0)
                trace.Append($"[t={turn + 1}:N={sc.Population},ctl={cc.Population},3B={sc.People.HasCivicPrecedent(CivicPrecedents.PlannedQuartersAndDrainage)},KS={sc.SanitationCapital:F0},w={sc.SanitationWaterRatio:F2},cov={CivicGovernance.SanitationCoverage(san, sc):F2},Md={PhysicalDemography.DiseaseMitigation(san, sc):F3},Mc={PhysicalDemography.CrowdingMitigation(san, sc):F3},food={sc.Food:F0}] ");
        }
        Console.WriteLine("PHASE2_GATE peakSanitation=" + peakS + " endSanitation=" + sc.Population + " peakControl=" + peakC + " endControl=" + cc.Population + " " + trace);
        TurnResolver.Invariants(san);
        CivicGovernance.ValidateInvariants(san);
        Check(sc.People.HasCivicPrecedent(CivicPrecedents.PlannedQuartersAndDrainage), "Phase-2 village organically unlocks Tier 3B " + trace);
        Check(sc.SanitationCapital > 0 && CivicGovernance.SanitationCoverage(san, sc) > 0.5, "Phase-2 village builds watered sanitation " + trace);
        Check(peakC <= 3700, "SanitationEnabled=false arm stays at the ~3,400 wall (peak=" + peakC + ")");
        Check(peakS >= 5000, "Sanitation arm breaks the wall to >= 5,000 (peak=" + peakS + ") " + trace);
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
