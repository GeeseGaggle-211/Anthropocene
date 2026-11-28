using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Anthropocene.Simulation;

namespace Anthropocene.CivicGovernanceE2E
{
    internal static class Tier1CoreTests
    {
        public static List<E2ETestCase> All()
        {
            return new List<E2ETestCase>
            {
                // F01 (5)
                new E2ETestCase("T1-F01-01", 1, "F01", "Null-default legacy parity (multi-turn)", M2Status.PassOnM2, T1_F01_01),
                new E2ETestCase("T1-F01-02", 1, "F01", "Explicit Enabled=false parity vs null Civic", M2Status.PassOnM2, T1_F01_02),
                new E2ETestCase("T1-F01-03", 1, "F01", "CivicGovernanceOpeningConfig.json load & 73-field parity", M2Status.PassOnM2, T1_F01_03),
                new E2ETestCase("T1-F01-04", 1, "F01", "Independent Phase-1 only toggle (Enabled=true, SanitationEnabled=false)", M2Status.PassOnM2, T1_F01_04),
                new E2ETestCase("T1-F01-05", 1, "F01", "Deterministic replay under enabled civics", M2Status.PassOnM2, T1_F01_05),

                // F02 (6)
                new E2ETestCase("T1-F02-01", 1, "F02", "Open rain-fed plains Egalitarian attractor (P_env < -0.35)", M2Status.PassOnM2, T1_F02_01),
                new E2ETestCase("T1-F02-02", 1, "F02", "Circumscribed irrigated valley Stratified attractor (P_env > +0.35)", M2Status.PassOnM2, T1_F02_02),
                new E2ETestCase("T1-F02-03", 1, "F02", "Coastal fishing village open Egalitarian regime (D1 == 0)", M2Status.PassOnM2, T1_F02_03),
                new E2ETestCase("T1-F02-04", 1, "F02", "Dense-neighbor social circumscription hybrid shift", M2Status.PassOnM2, T1_F02_04),
                new E2ETestCase("T1-F02-05", 1, "F02", "Complexity attractor C_env monotonic crowding response", M2Status.PassOnM2, T1_F02_05),
                new E2ETestCase("T1-F02-06", 1, "F02", "Resource chokepoint & trade hub Driver 3 lift", M2Status.PassOnM2, T1_F02_06),

                // F03 (5)
                new E2ETestCase("T1-F03-01", 1, "F03", "Three-lever policy orders commit & shift P positive", M2Status.PassOnM2, T1_F03_01),
                new E2ETestCase("T1-F03-02", 1, "F03", "Communal steering trio shifts valley toward Egalitarian", M2Status.PassOnM2, T1_F03_02),
                new E2ETestCase("T1-F03-03", 1, "F03", "Multi-policy + ordinary Economy order coexistence", M2Status.PassOnM2, T1_F03_03),
                new E2ETestCase("T1-F03-04", 1, "F03", "Alignment mismatch friction increases live Steward & upkeep demand", M2Status.FailUntilM3, T1_F03_04),
                new E2ETestCase("T1-F03-05", 1, "F03", "Same-turn CivicFocus/ImmigrationPolicy overlay & PlanningDraft invalidation", M2Status.PassOnM2, T1_F03_05),

                // F04 (5)
                new E2ETestCase("T1-F04-01", 1, "F04", "(C, P) analytical relaxation semigroup invariance (1x20y == 4x5y == 20x1y)", M2Status.PassOnM2, T1_F04_01),
                new E2ETestCase("T1-F04-02", 1, "F04", "Trade volume EMA semigroup invariance (1x20y == 4x5y == 20x1y)", M2Status.PassOnM2, T1_F04_02),
                new E2ETestCase("T1-F04-03", 1, "F04", "Tier-1 permanence ramp exact integral (2x20y == 8x5y == 40x1y)", M2Status.PassOnM2, T1_F04_03),
                new E2ETestCase("T1-F04-04", 1, "F04", "CivicCapital upkeep & decay dt-invariance (1x20y vs 4x5y)", M2Status.FailUntilM3, T1_F04_04),
                new E2ETestCase("T1-F04-05", 1, "F04", "SmoothCalendar 1-yr compression preserves civic investment with zero Turn-1 idle Stewards", M2Status.FailUntilM3, T1_F04_05),

                // F05 (5)
                new E2ETestCase("T1-F05-01", 1, "F05", "WorkforceRoles.Steward registry inclusion/exclusion contract", M2Status.PassOnM2, T1_F05_01),
                new E2ETestCase("T1-F05-02", 1, "F05", "Adaptive P-spectrum Steward labels", M2Status.PassOnM2, T1_F05_02),
                new E2ETestCase("T1-F05-03", 1, "F05", "Manual & automatic Steward staffing after Tier-1 unlock", M2Status.FailUntilM3, T1_F05_03),
                new E2ETestCase("T1-F05-04", 1, "F05", "Steward conservation under casualties & turn resolution", M2Status.PassOnM2, T1_F05_04),
                new E2ETestCase("T1-F05-05", 1, "F05", "Steward excluded from EquipmentTarget inflation", M2Status.FailUntilM3, T1_F05_05),

                // F06 (6)
                new E2ETestCase("T1-F06-01", 1, "F06", "Builder + HousingMaterial + Tools pipeline builds CivicCapital", M2Status.FailUntilM3, T1_F06_01),
                new E2ETestCase("T1-F06-02", 1, "F06", "Lock 1 wired: PhysicalDemography.CrowdingMitigation cuts crowding deaths", M2Status.FailUntilM3, T1_F06_02),
                new E2ETestCase("T1-F06-03", 1, "F06", "Lock 2 wired: SocialCohesion boosted across welfare & QoLModel parity", M2Status.FailUntilM3, T1_F06_03),
                new E2ETestCase("T1-F06-04", 1, "F06", "Organic Tier-1 climb (~1,000 -> ~1,600)", M2Status.FailUntilM3, T1_F06_04),
                new E2ETestCase("T1-F06-05", 1, "F06", "Organic Tier-2 climb (~2,800-3,400) hitting Phase-1 disease ceiling", M2Status.FailUntilM3, T1_F06_05),
                new E2ETestCase("T1-F06-06", 1, "F06", "CivicCapital analytical upkeep prevents decay vs unmaintained exponential decay", M2Status.FailUntilM3, T1_F06_06),

                // F07 (6)
                new E2ETestCase("T1-F07-01", 1, "F07", "Egalitarian lower Steward & capital overhead at mid-scale", M2Status.FailUntilM3, T1_F07_01),
                new E2ETestCase("T1-F07-02", 1, "F07", "Welfare-only Q_civic raises Egalitarian QoL while isolated from PhysicalDemography", M2Status.FailUntilM3, T1_F07_02),
                new E2ETestCase("T1-F07-03", 1, "F07", "Stratified public-works Builder mobilization bonus M_build with conserved tool wear", M2Status.FailUntilM3, T1_F07_03),
                new E2ETestCase("T1-F07-04", 1, "F07", "Stratified centralized granary storage bonus M_storage", M2Status.FailUntilM3, T1_F07_04),
                new E2ETestCase("T1-F07-05", 1, "F07", "Voluntary immigration pull favors Open Egalitarian over Restricted Stratified", M2Status.FailUntilM3, T1_F07_05),
                new E2ETestCase("T1-F07-06", 1, "F07", "Fission resistance vs dispersal across continuous P", M2Status.FailUntilM3, T1_F07_06),

                // F08 (6)
                new E2ETestCase("T1-F08-01", 1, "F08", "Tier-1 CeremonialIntegration practice & unlock lifts C_max to 0.30", M2Status.PassOnM2, T1_F08_01),
                new E2ETestCase("T1-F08-02", 1, "F08", "Tier-2 CivicOrganization practice & unlock lifts C_max to 0.75", M2Status.PassOnM2, T1_F08_02),
                new E2ETestCase("T1-F08-03", 1, "F08", "Tier-3A TokensAndAccounting unlocks for solo Player 0 via storage + Clay", M2Status.PassOnM2, T1_F08_03),
                new E2ETestCase("T1-F08-04", 1, "F08", "Tier-3A trade acceleration & live +2 range / reduced-margin TradeRecords", M2Status.FailUntilM3, T1_F08_04),
                new E2ETestCase("T1-F08-05", 1, "F08", "Tier-3B PlannedQuartersAndDrainage practice & unlock", M2Status.PassOnM2, T1_F08_05),
                new E2ETestCase("T1-F08-06", 1, "F08", "Fission daughter deep-copy precedent inheritance & dampened (C, P) with zero fixed capital", M2Status.PassOnM2, T1_F08_06),

                // F09 (5)
                new E2ETestCase("T1-F09-01", 1, "F09", "DemandControl priority hierarchy (Protected > Continuity > Development)", M2Status.FailUntilM3, T1_F09_01),
                new E2ETestCase("T1-F09-02", 1, "F09", "Young village (N=500) prioritizes food expansion over Civic Development", M2Status.FailUntilM3, T1_F09_02),
                new E2ETestCase("T1-F09-03", 1, "F09", "Severe famine starves civic upkeep/Stewards before Protected survival", M2Status.FailUntilM3, T1_F09_03),
                new E2ETestCase("T1-F09-04", 1, "F09", "Egalitarian Trypillia-style dispersal under urban shock", M2Status.FailUntilM3, T1_F09_04),
                new E2ETestCase("T1-F09-05", 1, "F09", "Stratified reserve holdout under equivalent shock", M2Status.FailUntilM3, T1_F09_05),

                // F10 (5)
                new E2ETestCase("T1-F10-01", 1, "F10", "SanitationCapital build & conserved Tier-2 SanitationWater allocation", M2Status.FailUntilM4, T1_F10_01),
                new E2ETestCase("T1-F10-02", 1, "F10", "3-tier conserved freshwater priority (Drinking/Herd > SanitationWater > Irrigation)", M2Status.FailUntilM4, T1_F10_02),
                new E2ETestCase("T1-F10-03", 1, "F10", "Multi-tile freshwater pooling above single-tile saturation at N=4,000", M2Status.FailUntilM4, T1_F10_03),
                new E2ETestCase("T1-F10-04", 1, "F10", "PhysicalDemography.DiseaseMitigation cuts live disease deaths", M2Status.FailUntilM4, T1_F10_04),
                new E2ETestCase("T1-F10-05", 1, "F10", "Breaking ~3,400 disease wall to >= 5,500+ with Stratified peaking above Egalitarian", M2Status.FailUntilM4, T1_F10_05),

                // F11 (5)
                new E2ETestCase("T1-F11-01", 1, "F11", "SaveCodec full civic roundtrip & bit-exact continuation", M2Status.PassOnM2, T1_F11_01),
                new E2ETestCase("T1-F11-02", 1, "F11", "Binary tile record preserved at exact 365 bytes/tile", M2Status.PassOnM2, T1_F11_02),
                new E2ETestCase("T1-F11-03", 1, "F11", "ObservationContract SchemaVersion=23 JSON roundtrip", M2Status.PassOnM2, T1_F11_03),
                new E2ETestCase("T1-F11-04", 1, "F11", "Owner-only civic observation & TileDetails.Describe CIVIC summary without rival leakage", M2Status.FailUntilM3, T1_F11_04),
                new E2ETestCase("T1-F11-05", 1, "F11", "SocialRelocationForecastCache.AuditBounds zero violations under active civic mitigation", M2Status.FailUntilM3, T1_F11_05),

                // F12 (5)
                new E2ETestCase("T1-F12-01", 1, "F12", "Zero policy orders emitted when CivicGovernanceEnabled == false", M2Status.PassOnM2, T1_F12_01),
                new E2ETestCase("T1-F12-02", 1, "F12", "AI Elder emits ImmigrationPolicy.Open on low-crowding Egalitarian camp", M2Status.PassOnM2, T1_F12_02),
                new E2ETestCase("T1-F12-03", 1, "F12", "AI Elder emits ImmigrationPolicy.Restricted on crowded Stratified camp", M2Status.PassOnM2, T1_F12_03),
                new E2ETestCase("T1-F12-04", 1, "F12", "CivicFocus.Auto exact 0.0 push & pure endogenous drift", M2Status.PassOnM2, T1_F12_04),
                new E2ETestCase("T1-F12-05", 1, "F12", "Multi-turn AI endogenous geographic divergence", M2Status.FailUntilM3, T1_F12_05)
            };
        }

        // ==================== F01 ====================

        private static void T1_F01_01()
        {
            var wA = WorldFixtures.CreatePlainsWorld(population: 400, civicEnabled: false, fission: false, immigration: false, trade: false);
            WorldFixtures.AddVillage(wA, 1, wA.Map.At(5, 5), 350, foodYears: 12.0);
            WorldFixtures.CultivateFootprint(wA, wA.Camps[1], capital: 0.9);
            WorldFixtures.ValidateWorld(wA);

            var wB = WorldFixtures.CloneWorld(wA);
            Assert.Check(wA.Config.Civic == null && wB.Config.Civic == null, "Baseline worlds must start with Civic == null.");

            var rA = new TurnResolver();
            var rB = new TurnResolver();
            for (int t = 0; t < 5; t++)
            {
                var recA = WorldFixtures.StepTurn(wA, rA);
                var recB = WorldFixtures.StepTurn(wB, rB);
                Assert.Check(recA.CivicReports == null && recB.CivicReports == null, "CivicReports must stay null when Civic == null.");
                Assert.Check(recA.SanitationWater == null && recB.SanitationWater == null, "SanitationWater must stay null when Civic == null.");
            }

            Assert.Check(SaveCodec.Fingerprint(wA) == SaveCodec.Fingerprint(wB), "Null-default legacy worlds must have bit-identical Fingerprints.");
            foreach (var c in wA.Camps)
            {
                Assert.NearAbs(PhysicalDemography.CrowdingMitigation(wA, c), 1.0, 1e-15, "CrowdingMitigation must be 1.0 when Civic == null.");
                Assert.NearAbs(PhysicalDemography.DiseaseMitigation(wA, c), 1.0, 1e-15, "DiseaseMitigation must be 1.0 when Civic == null.");
            }
        }

        private static void T1_F01_02()
        {
            var wNull = WorldFixtures.CreatePlainsWorld(population: 500, civicEnabled: false, fission: false, immigration: false, trade: false);
            var wDis = WorldFixtures.CloneWorld(wNull);
            wDis.Config.Civic = new CivicGovernanceSettings { Enabled = false, SanitationEnabled = true };
            wDis.Config.Validate();

            Assert.Check(!CivicGovernance.Enabled(wDis.Config), "CivicGovernance.Enabled must be false when Enabled=false.");
            Assert.Check(!CivicGovernance.SanitationEnabled(wDis.Config), "CivicGovernance.SanitationEnabled must be false when Enabled=false.");

            var r1 = new TurnResolver();
            var r2 = new TurnResolver();
            for (int t = 0; t < 5; t++)
            {
                var recNull = WorldFixtures.StepTurn(wNull, r1);
                var recDis = WorldFixtures.StepTurn(wDis, r2);
                Assert.Check(recDis.CivicReports == null, "Disabled Civic must emit null CivicReports.");
                Assert.Check(recDis.SanitationWater == null, "Disabled Civic must emit null SanitationWater.");
                Assert.Check(recNull.Demographics.Count == recDis.Demographics.Count, "Demographic report count parity.");
            }

            var cNull = wNull.Camps[0];
            var cDis = wDis.Camps[0];
            Assert.Check(cNull.Population == cDis.Population, $"Disabled Civic population ({cDis.Population}) must equal null Civic ({cNull.Population}).");
            Assert.NearAbs(cDis.QoL, cNull.QoL, 1e-12, "Disabled Civic QoL must equal null Civic.");
            Assert.NearAbs(cDis.Food, cNull.Food, 1e-9, "Disabled Civic Food must equal null Civic.");
            Assert.NearAbs(cDis.CivicComplexity, 0.0, 1e-15, "Disabled Civic must keep CivicComplexity == 0.");
            Assert.NearAbs(cDis.CivicStratification, 0.0, 1e-15, "Disabled Civic must keep CivicStratification == 0.");
        }

        private static void T1_F01_03()
        {
            string json = ConfigLoader.ReadProfileText("CivicGovernanceOpeningConfig.json");
            using (var doc = JsonDocument.Parse(json))
            {
                Assert.Check(doc.RootElement.TryGetProperty("Civic", out var civicElem), "Profile JSON must contain top-level Civic block.");
                int jsonCount = civicElem.EnumerateObject().Count();
                Assert.Check(jsonCount == 73, $"Profile JSON Civic block must have exactly 73 fields (found {jsonCount}).");
            }

            var cfg = ConfigLoader.LoadProfile("CivicGovernanceOpeningConfig.json");
            Assert.Check(cfg.Civic != null && cfg.Civic.Enabled && cfg.Civic.SanitationEnabled, "Loaded profile must have Civic Enabled & SanitationEnabled.");
            Assert.Check(cfg.SmoothCalendar != null, "Loaded profile must have SmoothCalendar configured.");
            Assert.Check(cfg.Agriculture != null && cfg.Agriculture.PloughEnabled && cfg.Agriculture.IrrigationEnabled, "Loaded profile must have Plough & Irrigation enabled.");

            var defaults = new CivicGovernanceSettings();
            var fields = typeof(CivicGovernanceSettings).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Assert.Check(fields.Length == 73, $"CivicGovernanceSettings must expose 73 public fields (found {fields.Length}).");

            foreach (var f in fields)
            {
                if (f.Name == nameof(CivicGovernanceSettings.Enabled) || f.Name == nameof(CivicGovernanceSettings.SanitationEnabled))
                    continue;
                object defVal = f.GetValue(defaults);
                object jsonVal = f.GetValue(cfg.Civic);
                if (f.FieldType == typeof(double))
                    Assert.NearAbs((double)jsonVal, (double)defVal, 1e-12, $"Field {f.Name} in profile JSON must match C# default.");
                else
                    Assert.Check(Equals(jsonVal, defVal), $"Field {f.Name} mismatch: JSON={jsonVal}, C#={defVal}.");
            }
        }

        private static void T1_F01_04()
        {
            var w = WorldFixtures.CreatePlainsWorld(
                population: 3000,
                civicEnabled: true,
                sanitationEnabled: false,
                fission: false,
                immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            WorldFixtures.AssignExactStewards(c, 40);
            Assert.Check(CivicGovernance.Enabled(w.Config) && !CivicGovernance.SanitationEnabled(w.Config), "Phase-1 only toggle check.");

            var rec = WorldFixtures.StepTurn(w);
            Assert.Check(rec.CivicReports != null && rec.CivicReports.Count == 1, "Phase-1 enabled must emit CivicReports.");
            Assert.Check(c.CivicComplexity > 0.0, $"Expected CivicComplexity > 0 after turn with T1+T2, got {c.CivicComplexity}.");
            Assert.Check(rec.SanitationWater == null, "SanitationWater must remain null when SanitationEnabled=false.");
            double t3b = c.People.CivicExperience != null && c.People.CivicExperience.TryGetValue(CivicPrecedents.PlannedQuartersAndDrainage, out double v) ? v : 0.0;
            Assert.NearAbs(t3b, 0.0, 1e-15, "Tier-3B must NOT accrue when SanitationEnabled=false.");
        }

        private static void T1_F01_05()
        {
            var w1 = WorldFixtures.CreatePlainsWorld(
                population: 900,
                civicEnabled: true,
                sanitationEnabled: true,
                fission: true,
                immigration: true,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var w2 = WorldFixtures.CloneWorld(w1);

            var r1 = new TurnResolver();
            var r2 = new TurnResolver();
            for (int t = 0; t < 6; t++)
            {
                var rec1 = WorldFixtures.StepTurn(w1, r1);
                var rec2 = WorldFixtures.StepTurn(w2, r2);
                Assert.Check(rec1.CivicReports != null && rec2.CivicReports != null && rec1.CivicReports.Count == rec2.CivicReports.Count,
                    $"CivicReports count mismatch on turn {t + 1}.");
                for (int i = 0; i < rec1.CivicReports.Count; i++)
                {
                    Assert.NearAbs(rec1.CivicReports[i].ComplexityAfter, rec2.CivicReports[i].ComplexityAfter, 1e-15, "Deterministic C replay.");
                    Assert.NearAbs(rec1.CivicReports[i].StratificationAfter, rec2.CivicReports[i].StratificationAfter, 1e-15, "Deterministic P replay.");
                }
            }
            Assert.Check(SaveCodec.Fingerprint(w1) == SaveCodec.Fingerprint(w2), "Enabled-civic deterministic replay must match Fingerprint bit-for-bit.");
        }

        // ==================== F02 ====================

        private static void T1_F02_01()
        {
            var w = WorldFixtures.CreatePlainsWorld(
                population: 1000,
                civicEnabled: true,
                fission: false,
                immigration: false,
                grantPlough: true,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];
            var resolver = new TurnResolver();
            double prevP = c.CivicStratification;
            for (int t = 0; t < 5; t++)
            {
                WorldFixtures.StepTurn(w, resolver);
                Assert.Check(c.LastEnvironmentalStratification < -0.35,
                    $"Turn {t + 1}: open rain-fed plains P_env should be < -0.35, got {c.LastEnvironmentalStratification:F4}.");
                Assert.Check(c.CivicStratification < prevP,
                    $"Turn {t + 1}: plains P should drift monotonically negative ({c.CivicStratification:F4} vs prev {prevP:F4}).");
                prevP = c.CivicStratification;
            }
        }

        private static void T1_F02_02()
        {
            var w = WorldFixtures.CreateValleyWorld(
                population: 1000,
                civicEnabled: true,
                fission: false,
                immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];
            var drivers = CivicGovernance.EvaluateEnvironment(w, c);
            Assert.Check(drivers.D1 > 0.50, $"Valley D1 should be > 0.50, got {drivers.D1:F4}.");
            Assert.Check(drivers.D2 > 0.65, $"Valley D2 should be > 0.65, got {drivers.D2:F4}.");

            var resolver = new TurnResolver();
            double prevP = c.CivicStratification;
            for (int t = 0; t < 5; t++)
            {
                WorldFixtures.StepTurn(w, resolver);
                Assert.Check(c.LastEnvironmentalStratification > 0.35,
                    $"Turn {t + 1}: circumscribed valley P_env should be > +0.35, got {c.LastEnvironmentalStratification:F4}.");
                Assert.Check(c.CivicStratification > prevP,
                    $"Turn {t + 1}: valley P should drift monotonically positive ({c.CivicStratification:F4} vs prev {prevP:F4}).");
                prevP = c.CivicStratification;
            }
        }

        private static void T1_F02_03()
        {
            var w = WorldFixtures.CreateCoastalWorld(population: 800, civicEnabled: true);
            var c = w.Camps[0];
            var drivers = CivicGovernance.EvaluateEnvironment(w, c);
            Assert.NearAbs(drivers.D1, 0.0, 1e-12, "Coastal fishing village without crops must have D1 == 0.");

            var resolver = new TurnResolver();
            for (int t = 0; t < 3; t++)
                WorldFixtures.StepTurn(w, resolver);

            Assert.Check(c.LastEnvironmentalStratification < -0.40,
                $"Coastal fishing village P_env should be < -0.40, got {c.LastEnvironmentalStratification:F4}.");
            Assert.Check(c.CivicStratification < -0.15,
                $"Coastal fishing village P should relax negative, got {c.CivicStratification:F4}.");
        }

        private static void T1_F02_04()
        {
            var wEmpty = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false, width: 22, height: 16);
            var wDense = WorldFixtures.CloneWorld(wEmpty);
            int center = wDense.Camps[0].Tile;

            // Place 6 foreign camps occupying the exit ring around center at distance 3..4.
            int added = 0;
            var usedTiles = new HashSet<int> { center };
            foreach (int t in wDense.Map.Radius(center, wDense.Config.FissionRadius))
            {
                if (added >= 6) break;
                int dist = wDense.Map.Distance(center, t);
                if (dist < wDense.Config.MinimumCampSpacing) continue;
                if (usedTiles.Any(u => wDense.Map.Distance(u, t) < wDense.Config.MinimumCampSpacing)) continue;
                var nbr = WorldFixtures.AddVillage(wDense, added + 1, t, 500, foodYears: 10.0);
                WorldFixtures.EstablishContacts(wDense, wDense.Camps[0], nbr);
                usedTiles.Add(t);
                added++;
            }
            Assert.Check(added >= 5, $"Expected >= 5 exit-ring neighbor camps placed, got {added}.");
            WorldFixtures.ValidateWorld(wDense);

            WorldFixtures.StepTurn(wEmpty);
            WorldFixtures.StepTurn(wDense);

            var cEmpty = wEmpty.Camps[0];
            var cDense = wDense.Camps[0];
            Assert.Check(cDense.LastCircumscription > cEmpty.LastCircumscription + 0.10,
                $"Dense neighbors must strictly raise LastCircumscription ({cDense.LastCircumscription:F4} vs {cEmpty.LastCircumscription:F4}).");
            Assert.Check(cDense.LastEnvironmentalStratification > cEmpty.LastEnvironmentalStratification + 0.15,
                $"Dense neighbors must shift P_env positively ({cDense.LastEnvironmentalStratification:F4} vs {cEmpty.LastEnvironmentalStratification:F4}).");
        }

        private static void T1_F02_05()
        {
            double RunAndGetCEnv(int pop, out double cAfter)
            {
                var w = WorldFixtures.CreatePlainsWorld(
                    population: pop,
                    civicEnabled: true,
                    fission: false,
                    immigration: false,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                var camp = w.Camps[0];
                camp.CivicComplexity = 0.0;
                CivicGovernance.Close(w, new TurnRecord(), 20.0);
                cAfter = camp.CivicComplexity;
                return camp.LastEnvironmentalComplexity;
            }

            double cEnv200 = RunAndGetCEnv(200, out double c200);
            double cEnv800 = RunAndGetCEnv(800, out double c800);
            double cEnv1600 = RunAndGetCEnv(1600, out double c1600);

            Assert.Check(cEnv200 < cEnv800 && cEnv800 < cEnv1600,
                $"C_env must strictly increase with population: {cEnv200:F4} < {cEnv800:F4} < {cEnv1600:F4}.");
            Assert.Check(c200 > 0.0 && c800 > c200 && c1600 > c800,
                $"Relaxed CivicComplexity must track C_env monotonically: {c200:F4} < {c800:F4} < {c1600:F4}.");
        }

        private static void T1_F02_06()
        {
            var wBase = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false, withResources: false, width: 22, height: 16);
            var wHub = WorldFixtures.CloneWorld(wBase);
            var cHub = wHub.Camps[0];
            WorldFixtures.AddSurfaceResources(wHub, wHub.Map.Radius(cHub.Tile, 1), stone: 6000, clay: 6000, wood: 6000, copperDepositTile: cHub.Tile);
            cHub.RecentTradeVolume = 300.0;

            int contacts = 0;
            var used = new HashSet<int> { cHub.Tile };
            foreach (int t in wHub.Map.Radius(cHub.Tile, wHub.Config.TradeRange))
            {
                if (contacts >= 4) break;
                if (wHub.Map.Distance(cHub.Tile, t) < 3) continue;
                if (used.Any(u => wHub.Map.Distance(u, t) < 3)) continue;
                var partner = WorldFixtures.AddVillage(wHub, contacts + 1, t, 400, foodYears: 10.0);
                WorldFixtures.EstablishContacts(wHub, cHub, partner);
                used.Add(t);
                contacts++;
            }
            Assert.Check(contacts == 4, $"Expected 4 trade contacts, got {contacts}.");

            var envBase = CivicGovernance.EvaluateEnvironment(wBase, wBase.Camps[0]);
            var envHub = CivicGovernance.EvaluateEnvironment(wHub, cHub);
            Assert.Check(envHub.D3 > envBase.D3 + 0.25, $"Copper deposit + trade hub must lift D3 ({envHub.D3:F4} vs {envBase.D3:F4}).");
            Assert.Check(envHub.EnvironmentalStratification > envBase.EnvironmentalStratification + 0.10,
                $"Copper deposit + trade hub must lift P_env ({envHub.EnvironmentalStratification:F4} vs {envBase.EnvironmentalStratification:F4}).");
        }

        // ==================== F03 ====================

        private static void T1_F03_01()
        {
            var wSteered = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false);
            var wControl = WorldFixtures.CloneWorld(wSteered);
            var camp = wSteered.Camps[0];

            var orders = new Dictionary<int, List<Order>>
            {
                [camp.Player] = new List<Order>
                {
                    new Order(OrderKind.FissionPolicy, camp.Id, (int)FissionPolicy.Discourage),
                    new Order(OrderKind.ImmigrationPolicy, camp.Id, (int)ImmigrationPolicy.Restricted),
                    new Order(OrderKind.CivicFocus, camp.Id, (int)CivicFocus.Centralized)
                }
            };

            WorldFixtures.StepTurn(wSteered, orders: orders);
            WorldFixtures.StepTurn(wControl);

            Assert.Check(camp.FissionPolicy == FissionPolicy.Discourage, "FissionPolicy.Discourage must commit.");
            Assert.Check(camp.ImmigrationPolicy == ImmigrationPolicy.Restricted, "ImmigrationPolicy.Restricted must commit.");
            Assert.Check(camp.CivicFocus == CivicFocus.Centralized, "CivicFocus.Centralized must commit.");

            var obs = new AgentObservationBuilder().Build(wSteered, camp.Player);
            var so = obs.Settlements.Single(s => s.Id == camp.Id);
            Assert.Check(so.FissionPolicy == FissionPolicy.Discourage && so.ImmigrationPolicy == ImmigrationPolicy.Restricted && so.CivicFocus == CivicFocus.Centralized,
                "All three committed policies must appear in SettlementObservation.");
            Assert.Check(camp.CivicStratification > wControl.Camps[0].CivicStratification + 0.15,
                $"Centralized trio must shift P strictly more positive ({camp.CivicStratification:F4} vs {wControl.Camps[0].CivicStratification:F4}).");
        }

        private static void T1_F03_02()
        {
            var wSteered = WorldFixtures.CreateValleyWorld(population: 1000, civicEnabled: true, fission: false, immigration: false);
            var wControl = WorldFixtures.CloneWorld(wSteered);
            var camp = wSteered.Camps[0];

            var firstOrders = new Dictionary<int, List<Order>>
            {
                [camp.Player] = new List<Order>
                {
                    new Order(OrderKind.FissionPolicy, camp.Id, (int)FissionPolicy.Encourage),
                    new Order(OrderKind.ImmigrationPolicy, camp.Id, (int)ImmigrationPolicy.Open),
                    new Order(OrderKind.CivicFocus, camp.Id, (int)CivicFocus.Communal)
                }
            };
            var r1 = new TurnResolver();
            var r2 = new TurnResolver();
            WorldFixtures.StepTurn(wSteered, r1, firstOrders);
            WorldFixtures.StepTurn(wControl, r2);
            for (int t = 1; t < 5; t++)
            {
                WorldFixtures.StepTurn(wSteered, r1);
                WorldFixtures.StepTurn(wControl, r2);
            }

            Assert.Check(camp.CivicStratification >= -1.0 && camp.CivicStratification <= 1.0, "P must remain bounded in [-1, +1].");
            Assert.Check(camp.CivicStratification < wControl.Camps[0].CivicStratification - 0.35,
                $"Communal trio over 5 turns must pull valley P strictly below control ({camp.CivicStratification:F4} vs {wControl.Camps[0].CivicStratification:F4}).");
        }

        private static void T1_F03_03()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var camp = w.Camps[0];
            var intent = EconomicIntent.From(camp);
            intent.AutomationTurns = 0;
            intent.Assignments = new List<PopulationAssignment>
            {
                new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 120 },
                new PopulationAssignment { Role = WorkforceRoles.Steward, Population = 15 }
            };
            var econOrder = new Order(OrderKind.Economy, camp.Id) { Economy = intent };

            var orders = new Dictionary<int, List<Order>>
            {
                [camp.Player] = new List<Order>
                {
                    econOrder,
                    new Order(OrderKind.FissionPolicy, camp.Id, (int)FissionPolicy.Discourage),
                    new Order(OrderKind.ImmigrationPolicy, camp.Id, (int)ImmigrationPolicy.Open),
                    new Order(OrderKind.CivicFocus, camp.Id, (int)CivicFocus.Communal)
                }
            };

            WorldFixtures.StepTurn(w, orders: orders);
            Assert.Check(camp.FissionPolicy == FissionPolicy.Discourage, "FissionPolicy must commit alongside Economy.");
            Assert.Check(camp.ImmigrationPolicy == ImmigrationPolicy.Open, "ImmigrationPolicy must commit alongside Economy.");
            Assert.Check(camp.CivicFocus == CivicFocus.Communal, "CivicFocus must commit alongside Economy.");
            Assert.Check(CultivationEconomy.Workers(camp, WorkforceRoles.Steward) > 0, "Economy Steward assignment must commit alongside all 3 policies.");
        }

        private static void T1_F03_04()
        {
            WorldState BuildCamp(double envStrat)
            {
                var w = WorldFixtures.CreatePlainsWorld(
                    population: 1400,
                    civicEnabled: true,
                    fission: false,
                    immigration: false,
                    landBiomassMultiplier: 800.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                var c = w.Camps[0];
                c.CivicComplexity = 0.60;
                c.LastEnvironmentalComplexity = 0.60;
                c.CivicStratification = 0.50;
                c.LastEnvironmentalStratification = envStrat;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c) * 1.1;
                c.Inventory.HousingMaterial = 4000.0;
                c.Inventory.Tools = 4000.0;
                return w;
            }

            var wAligned = BuildCamp(0.50);
            var wCoerced = BuildCamp(-0.50);
            double hmBeforeAligned = wAligned.Camps[0].Inventory.HousingMaterial;
            double hmBeforeCoerced = wCoerced.Camps[0].Inventory.HousingMaterial;

            var recAligned = WorldFixtures.StepTurn(wAligned);
            var recCoerced = WorldFixtures.StepTurn(wCoerced);

            int stewAligned = CultivationEconomy.Workers(wAligned.Camps[0], WorkforceRoles.Steward);
            int stewCoerced = CultivationEconomy.Workers(wCoerced.Camps[0], WorkforceRoles.Steward);
            double maintAligned = recAligned.Production.Where(ProductionReceiptHelpers.IsCivicMaintenance).Sum(r => r.Output);
            double maintCoerced = recCoerced.Production.Where(ProductionReceiptHelpers.IsCivicMaintenance).Sum(r => r.Output);

            Assert.Check(stewAligned > 0 && stewCoerced > 0,
                $"DemandControl must actively staff Stewards on both camps (aligned={stewAligned}, coerced={stewCoerced}).");
            Assert.Check(stewCoerced > stewAligned * 1.20,
                $"Coerced alignment mismatch (|dP|=1.0, F_align=1.35) must staff strictly more Stewards (coerced={stewCoerced} vs aligned={stewAligned}).");
            Assert.Check(maintCoerced > maintAligned * 1.20 && (hmBeforeCoerced - wCoerced.Camps[0].Inventory.HousingMaterial) > (hmBeforeAligned - wAligned.Camps[0].Inventory.HousingMaterial),
                $"Coerced alignment mismatch must increase CivicMaintenance output/HM consumption (coercedMaint={maintCoerced:F3}, alignedMaint={maintAligned:F3}).");
        }

        private static void T1_F03_05()
        {
            var wDraft = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            WorldFixtures.AddVillage(wDraft, 1, wDraft.Map.At(5, 5), 600, foodYears: 12.0, precedents: new[] { CivicPrecedents.CeremonialIntegration });
            WorldFixtures.CultivateFootprint(wDraft, wDraft.Camps[1], capital: 0.9);
            WorldFixtures.ValidateWorld(wDraft);

            var wFresh = WorldFixtures.CloneWorld(wDraft);
            var campDraft = wDraft.Camps[0];
            var campFresh = wFresh.Camps[0];

            var draft = TurnPlanner.Draft(wDraft, excludedPlayer: 0);
            var playerOrders = new List<Order>
            {
                new Order(OrderKind.CivicFocus, campDraft.Id, (int)CivicFocus.Centralized),
                new Order(OrderKind.ImmigrationPolicy, campDraft.Id, (int)ImmigrationPolicy.Restricted)
            };

            var ordersDraft = new Dictionary<int, List<Order>>(draft.Ai.Orders);
            ordersDraft[0] = playerOrders;
            var ordersFresh = new Dictionary<int, List<Order>>(draft.Ai.Orders);
            ordersFresh[0] = playerOrders.Select(o => o.Copy()).ToList();

            var prepDraft = TurnPlanner.Prepare(wDraft, ordersDraft, draft.Ai.Traces, null, draft);
            var prepFresh = TurnPlanner.Prepare(wFresh, ordersFresh, draft.Ai.Traces, null, null);

            new TurnResolver().Resolve(wDraft, prepDraft);
            new TurnResolver().Resolve(wFresh, prepFresh);

            Assert.Check(campDraft.CivicFocus == CivicFocus.Centralized && campDraft.ImmigrationPolicy == ImmigrationPolicy.Restricted,
                "Draft-path prepare must commit updated CivicFocus & ImmigrationPolicy.");
            Assert.Check(SaveCodec.Fingerprint(wDraft) == SaveCodec.Fingerprint(wFresh),
                "Draft-invalidated Prepare(..., draft) must match fresh Prepare(..., null) bit-for-bit.");
        }

        // ==================== F04 ====================

        private static void T1_F04_01()
        {
            // Pure analytical semigroup invariance
            double c0 = 0.10, cStar = 0.72, p0 = -0.35, pStar = 0.55;
            double lc = 0.025, lp = 0.020;
            double c20 = CivicGovernance.Relax(c0, cStar, lc, 20.0);
            double p20 = CivicGovernance.Relax(p0, pStar, lp, 20.0);
            double c5 = c0, p5 = p0, c1 = c0, p1 = p0;
            for (int i = 0; i < 4; i++) { c5 = CivicGovernance.Relax(c5, cStar, lc, 5.0); p5 = CivicGovernance.Relax(p5, pStar, lp, 5.0); }
            for (int i = 0; i < 20; i++) { c1 = CivicGovernance.Relax(c1, cStar, lc, 1.0); p1 = CivicGovernance.Relax(p1, pStar, lp, 1.0); }
            Assert.NearRelative(c5, c20, 1e-12, "Pure Relax C semigroup 4x5y == 1x20y");
            Assert.NearRelative(c1, c20, 1e-12, "Pure Relax C semigroup 20x1y == 1x20y");
            Assert.NearRelative(p5, p20, 1e-12, "Pure Relax P semigroup 4x5y == 1x20y");
            Assert.NearRelative(p1, p20, 1e-12, "Pure Relax P semigroup 20x1y == 1x20y");

            // Full CivicGovernance.Close invariance on static environment across 1x20y, 4x5y, 20x1y
            (double C, double P) RunClose(int steps, double stepYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, fission: false, immigration: false,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                var camp = w.Camps[0];
                camp.CivicComplexity = 0.15;
                camp.CivicStratification = 0.20;
                for (int i = 0; i < steps; i++)
                    CivicGovernance.Close(w, new TurnRecord(), stepYears);
                return (camp.CivicComplexity, camp.CivicStratification);
            }

            var r20 = RunClose(1, 20.0);
            var r5 = RunClose(4, 5.0);
            var r1 = RunClose(20, 1.0);
            Assert.NearRelative(r5.C, r20.C, 1e-11, "Close C semigroup 4x5y == 1x20y");
            Assert.NearRelative(r1.C, r20.C, 1e-11, "Close C semigroup 20x1y == 1x20y");
            Assert.NearAbs(r5.P, r20.P, 1e-11, "Close P semigroup 4x5y == 1x20y");
            Assert.NearAbs(r1.P, r20.P, 1e-11, "Close P semigroup 20x1y == 1x20y");
        }

        private static void T1_F04_02()
        {
            double RunTradeEma(int steps, double stepYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false);
                var camp = w.Camps[0];
                camp.RecentTradeVolume = 40.0;
                for (int i = 0; i < steps; i++)
                {
                    var rec = new TurnRecord();
                    rec.Trades.Add(new TradeRecord
                    {
                        Requester = camp.Id,
                        Responder = camp.Id + 999,
                        Want = TradeGood.Cloth,
                        WantQuantity = 5.0 * stepYears,
                        Give = TradeGood.Cloth,
                        GiveQuantity = 5.0 * stepYears,
                        Delivered = 1.0,
                        GiveDelivered = 1.0
                    });
                    CivicGovernance.Close(w, rec, stepYears);
                }
                return camp.RecentTradeVolume;
            }

            double e20 = RunTradeEma(1, 20.0);
            double e5 = RunTradeEma(4, 5.0);
            double e1 = RunTradeEma(20, 1.0);
            Assert.NearRelative(e5, e20, 1e-11, "Trade EMA 4x5y == 1x20y");
            Assert.NearRelative(e1, e20, 1e-11, "Trade EMA 20x1y == 1x20y");
        }

        private static void T1_F04_03()
        {
            double RunRamp(int steps, double stepYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false);
                var camp = w.Camps[0];
                camp.StationaryYears = 0.0;
                camp.StationaryTurns = 1;
                camp.People.CivicPrecedents.Clear();
                camp.People.CivicExperience.Clear();
                for (int i = 0; i < steps; i++)
                {
                    camp.StationaryYears += stepYears;
                    camp.StationaryTurns++;
                    CivicGovernance.Close(w, new TurnRecord(), stepYears);
                }
                return camp.People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double exp) ? exp : 0.0;
            }

            double x20 = RunRamp(2, 20.0);
            double x5 = RunRamp(8, 5.0);
            double x1 = RunRamp(40, 1.0);
            Assert.Check(x20 > 1.0, $"Expected positive Tier-1 permanence practice over 40 yr, got {x20}.");
            Assert.NearRelative(x5, x20, 1e-10, "Tier-1 permanence integral 8x5y == 2x20y");
            Assert.NearRelative(x1, x20, 1e-10, "Tier-1 permanence integral 40x1y == 2x20y");
        }

        private static void T1_F04_04()
        {
            // Part A: Zero-upkeep analytical exponential decay dt-invariance (1x20y vs 4x5y)
            double RunUnmaintained(int steps, double stepYears, double alignmentFrictionRate)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, fission: false, immigration: false, withResources: false,
                    landBiomassMultiplier: 800.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                w.Config.Civic.AlignmentFrictionRate = alignmentFrictionRate;
                w.Config.Validate();
                WorldFixtures.SetFixedStepYears(w, stepYears);
                var camp = w.Camps[0];
                camp.CivicComplexity = 0.60;
                camp.LastEnvironmentalComplexity = 0.60;
                var env = CivicGovernance.EvaluateEnvironment(w, camp);
                camp.CivicStratification = env.EnvironmentalStratification;
                camp.LastEnvironmentalStratification = env.EnvironmentalStratification;
                camp.CivicCapital = 180.0;
                camp.PlantFood = 1200 * PhysicalDemography.FoodPerPersonYear(w.Config) * 25.0;
                camp.Inventory.HousingMaterial = 0.0;
                camp.Inventory.Stone = 0.0;
                camp.Inventory.Clay = 0.0;
                camp.Inventory.Wood = 0.0;
                var resolver = new TurnResolver();
                for (int i = 0; i < steps; i++)
                    WorldFixtures.StepTurn(w, resolver);
                return camp.CivicCapital;
            }

            double expectedZeroUpkeep = 180.0 * Math.Exp(-0.018 * 20.0);
            double kDecay20Default = RunUnmaintained(1, 20.0, 0.35);
            Assert.Check(kDecay20Default < 170.0, $"Unmaintained CivicCapital must decay over 20yr (got {kDecay20Default:F3}, opening=180).");
            Assert.NearRelative(kDecay20Default, expectedZeroUpkeep, 1e-9,
                "1x20y aligned unmaintained decay (default AlignmentFrictionRate=0.35) must match exact analytical exponential.");

            // Pure semigroup subdivision invariance (isolating decay kernel from live P_env(N(t), Food(t)) drift across substeps)
            double kDecay20 = RunUnmaintained(1, 20.0, 0.0);
            double kDecay5 = RunUnmaintained(4, 5.0, 0.0);
            Assert.NearRelative(kDecay20, expectedZeroUpkeep, 1e-9, "1x20y zero-friction unmaintained decay matches analytical.");
            Assert.NearRelative(kDecay5, kDecay20, 1e-9, "4x5y vs 1x20y unmaintained CivicCapital decay is semigroup-exact.");

            // Part B: Full upkeep retains capital across both 1x20y and 4x5y
            (double Cap, double MaintOut, double KOpen) RunMaintained(int steps, double stepYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1400, civicEnabled: true, fission: false, immigration: false,
                    landBiomassMultiplier: 800.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                WorldFixtures.SetFixedStepYears(w, stepYears);
                var camp = w.Camps[0];
                camp.CivicComplexity = 0.70;
                camp.LastEnvironmentalComplexity = 0.70;
                var env = CivicGovernance.EvaluateEnvironment(w, camp);
                camp.CivicStratification = env.EnvironmentalStratification;
                camp.LastEnvironmentalStratification = env.EnvironmentalStratification;
                double reqCap = CivicGovernance.RequiredCivicCapital(w, camp);
                double kOpen = Math.Min(120.0, reqCap * 0.80); // strictly below ReqCap so excess ruins cannot confound retention
                camp.CivicCapital = kOpen;
                camp.PlantFood = 1400 * PhysicalDemography.FoodPerPersonYear(w.Config) * 25.0;
                camp.Inventory.HousingMaterial = 5000.0;
                camp.Inventory.Tools = 5000.0;
                var resolver = new TurnResolver();
                double maint = 0.0;
                for (int i = 0; i < steps; i++)
                {
                    var rec = WorldFixtures.StepTurn(w, resolver);
                    maint += rec.Production.Where(ProductionReceiptHelpers.IsCivicMaintenance).Sum(r => r.Output);
                }
                return (camp.CivicCapital, maint, kOpen);
            }

            var m20 = RunMaintained(1, 20.0);
            var m5 = RunMaintained(4, 5.0);
            Assert.Check(m20.MaintOut > 10.0 && m5.MaintOut > 10.0, $"Both 1x20y ({m20.MaintOut:F2}) and 4x5y ({m5.MaintOut:F2}) must perform CivicMaintenance.");
            Assert.Check(m20.Cap >= m20.KOpen * 0.99 && m5.Cap >= m5.KOpen * 0.99,
                $"Maintained CivicCapital must be retained (20y={m20.Cap:F2}/{m20.KOpen:F2}, 5y={m5.Cap:F2}/{m5.KOpen:F2}).");
        }

        private static void T1_F04_05()
        {
            WorldState BuildBootstrap(double stepYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1100, civicEnabled: true, fission: false, immigration: false,
                    landBiomassMultiplier: 600.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration });
                WorldFixtures.SetFixedStepYears(w, stepYears);
                var c = w.Camps[0];
                var env = CivicGovernance.EvaluateEnvironment(w, c);
                c.LastEnvironmentalComplexity = env.EnvironmentalComplexity;
                c.LastEnvironmentalStratification = env.EnvironmentalStratification;
                c.CivicComplexity = 0.0;
                c.CivicCapital = 0.0;
                c.PlantFood = 1100 * PhysicalDemography.FoodPerPersonYear(w.Config) * 25.0;
                return w;
            }

            var w1 = BuildBootstrap(1.0);
            var w20 = BuildBootstrap(20.0);
            var c1 = w1.Camps[0];
            var c20 = w20.Camps[0];

            WorldFixtures.StepTurn(w1);
            WorldFixtures.StepTurn(w20);

            Assert.Check(c1.CivicCapital > 0.0, $"1-yr compressed turn must still fund CivicConstruction via T_amort=max(dt,20), got CivicCapital={c1.CivicCapital:F4}.");
            Assert.Check(c20.CivicCapital > 0.0, $"20-yr turn must fund CivicConstruction, got CivicCapital={c20.CivicCapital:F4}.");
            Assert.Check(CultivationEconomy.Workers(c1, WorkforceRoles.Steward) == 0,
                $"Turn-1 at opening C==0 must staff 0 idle Stewards on 1-yr turn (got {CultivationEconomy.Workers(c1, WorkforceRoles.Steward)}).");
            Assert.Check(CultivationEconomy.Workers(c20, WorkforceRoles.Steward) == 0,
                $"Turn-1 at opening C==0 must staff 0 idle Stewards on 20-yr turn (got {CultivationEconomy.Workers(c20, WorkforceRoles.Steward)}).");
        }

        // ==================== F05 ====================

        private static void T1_F05_01()
        {
            Assert.Check(WorkforceRoles.All.Contains(WorkforceRoles.Steward), "WorkforceRoles.All must contain Steward.");
            Assert.Check(!WorkforceRoles.Specialists.Contains(WorkforceRoles.Steward), "WorkforceRoles.Specialists must NOT contain Steward.");
            Assert.Check(!WorkforceRoles.Extractors.Contains(WorkforceRoles.Steward), "WorkforceRoles.Extractors must NOT contain Steward.");
            Assert.Check(EconomicIntents.RequiredTechnology(WorkforceRoles.Steward) == null, "RequiredTechnology(Steward) must be null.");
        }

        private static void T1_F05_02()
        {
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, null) == "Steward", "null camp label must be 'Steward'.");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = -0.50 }) == "Council Arbiter", "P=-0.50 -> Council Arbiter.");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = -0.30 }) == "Council Arbiter", "P=-0.30 -> Council Arbiter.");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = 0.0 }) == "Civic Steward", "P=0.0 -> Civic Steward.");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = 0.30 }) == "Temple Scribe", "P=+0.30 -> Temple Scribe.");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = 0.75 }) == "Temple Scribe", "P=+0.75 -> Temple Scribe.");
        }

        private static void T1_F05_03()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];
            c.CivicComplexity = 0.30;
            c.LastEnvironmentalComplexity = 0.30;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);

            // Manual Economy order assigning Steward must pass OrderRules.Validate
            var obs = new AgentObservationBuilder().Build(w, c.Player);
            var so = obs.Settlements.Single(s => s.Id == c.Id);
            Assert.Check(so.StewardEnabled, "StewardEnabled must be true once CeremonialIntegration is unlocked.");
            var manualIntent = EconomicIntent.From(c);
            manualIntent.AutomationTurns = 0;
            manualIntent.Assignments = new List<PopulationAssignment>
            {
                new PopulationAssignment { Role = WorkforceRoles.Steward, Population = 15 }
            };
            string err = OrderRules.Validate(obs, new Order(OrderKind.Economy, c.Id) { Economy = manualIntent });
            Assert.Check(err == null, $"Manual Steward Economy order must validate cleanly (err='{err}').");

            // Automatic DemandControl must staff Stewards > 0 yielding CivicCoverage > 0.5
            WorldFixtures.StepTurn(w);
            int autoStewards = CultivationEconomy.Workers(c, WorkforceRoles.Steward);
            double cov = CivicGovernance.CivicCoverage(w, c);
            Assert.Check(autoStewards > 0, $"Automatic DemandControl must staff Stewards > 0 (got {autoStewards}).");
            Assert.Check(cov > 0.50, $"Automatic Steward staffing must achieve CivicCoverage > 0.50 (got {cov:F3}).");
        }

        private static void T1_F05_04()
        {
            var ledger = new PopulationLedger();
            ledger.Assign(WorkforceRoles.Farmer, 200, 1000);
            ledger.Assign(WorkforceRoles.Steward, 100, 1000);
            ledger.ApplyCasualties(1000, 200);
            Assert.Check(ledger.Assigned <= 800, "Assigned workers must never exceed surviving population.");
            int remainingStewards = ledger.Assignments.FirstOrDefault(a => a.Role == WorkforceRoles.Steward)?.Population ?? 0;
            Assert.Check(remainingStewards == 80, $"Proportional 20% casualties on 100 Stewards should leave 80, got {remainingStewards}.");

            var w = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];
            c.EconomyAutomationTurns = 0;
            WorldFixtures.AssignExactStewards(c, 40);
            c.PlantFood = 0.0; // induce mortality
            WorldFixtures.StepTurn(w);
            Assert.Check(c.Workforce.Assigned <= c.Population, "Post-turn Assigned must be <= Population.");
        }

        private static void T1_F05_05()
        {
            var wStew = WorldFixtures.CreatePlainsWorld(population: 2200, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var cStew = wStew.Camps[0];
            cStew.CivicComplexity = 0.75;
            cStew.LastEnvironmentalComplexity = 0.75;
            cStew.CivicStratification = 0.50;
            cStew.LastEnvironmentalStratification = 0.50;
            cStew.CivicCapital = CivicGovernance.RequiredCivicCapital(wStew, cStew);
            cStew.PlantFood = 2200 * PhysicalDemography.FoodPerPersonYear(wStew.Config) * 25.0;

            var wZero = WorldFixtures.CloneWorld(wStew);
            var cZero = wZero.Camps[0];
            cZero.CivicComplexity = 0.0;
            cZero.CivicCapital = 0.0;

            var planStew = EconomicAllocationStrategy.Plan(wStew, cStew, EconomicIntent.From(cStew), out _);
            var planZero = EconomicAllocationStrategy.Plan(wZero, cZero, EconomicIntent.From(cZero), out _);

            int staffedStewards = planStew.Assignments.FirstOrDefault(a => a.Role == WorkforceRoles.Steward)?.Population ?? 0;
            Assert.Check(staffedStewards >= 50, $"Expected >= 50 automatically staffed Stewards on N=2200 T2 camp, got {staffedStewards}.");

            int craftWorkersStew = planStew.Assignments.Where(a => a.Role != WorkforceRoles.Steward && a.Role != WorkforceRoles.Gatherer && a.Role != WorkforceRoles.Farmer && a.Role != WorkforceRoles.Herder && a.Role != WorkforceRoles.Fisher).Sum(a => a.Population);
            double equipStew = planStew.Labor?.EquipmentTarget ?? 0.0;
            double equipZero = planZero.Labor?.EquipmentTarget ?? 0.0;
            Assert.Check(equipStew <= craftWorkersStew + 1e-6 + 5.0 && equipStew < equipZero + staffedStewards * 0.5,
                $"Stewards ({staffedStewards}) must NOT inflate EquipmentTarget (equipStew={equipStew:F2}, equipZero={equipZero:F2}, craftWorkersStew={craftWorkersStew}).");
        }

        // ==================== F06 ====================

        private static void T1_F06_01()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];
            c.CivicComplexity = 0.30;
            c.LastEnvironmentalComplexity = 0.30;
            c.CivicCapital = 0.0;
            c.Inventory.HousingMaterial = 2000.0;
            c.Inventory.HandTools = 0.0;
            c.Inventory.Tools = 2000.0;

            double hmBefore = c.Inventory.HousingMaterial;
            var rec = WorldFixtures.StepTurn(w);

            var receipts = rec.Production.Where(ProductionReceiptHelpers.IsCivicConstruction).ToList();
            double built = receipts.Sum(r => r.Output);
            Assert.Check(receipts.Count > 0 && built > 0.0, $"Expected CivicConstruction production receipt(s) with Output > 0 (count={receipts.Count}, built={built:F3}).");
            Assert.Check(c.CivicCapital > 0.0, $"Expected CivicCapital > 0 after turn (got {c.CivicCapital:F3}).");
            Assert.NearRelative(receipts.Sum(r => r.HousingMaterial), built * w.Config.Civic.CivicBuildHousingMaterials, 1e-6,
                "CivicConstruction receipts must consume exact proportional HousingMaterial.");
            Assert.Check(hmBefore - c.Inventory.HousingMaterial >= built * w.Config.Civic.CivicBuildHousingMaterials - 1e-6,
                "Camp HousingMaterial stock must decrease by at least CivicConstruction HM input.");
            double toolOutputs = receipts.Sum(r =>
                r.HandToolsConsumed * ToolCoverageBudget.OutputsPerHandTool +
                r.ToolsConsumed * ToolCoverageBudget.OutputsPerTool);
            Assert.NearRelative(toolOutputs, built, 1e-6,
                "CivicConstruction receipts must consume exact physical Tools (Equipment=true).");
        }

        private static void T1_F06_02()
        {
            var wCivic = WorldFixtures.CreatePlainsWorld(population: 1600, civicEnabled: true, sanitationEnabled: false, fission: false, immigration: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            WorldFixtures.SetFixedStepYears(wCivic, 5.0);
            var cCivic = wCivic.Camps[0];
            cCivic.CivicComplexity = 0.75;
            cCivic.LastEnvironmentalComplexity = 0.75;
            cCivic.CivicStratification = 0.0;
            cCivic.LastEnvironmentalStratification = 0.0;
            cCivic.CivicCapital = CivicGovernance.RequiredCivicCapital(wCivic, cCivic) * 1.2;
            WorldFixtures.AssignExactStewards(cCivic, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wCivic, cCivic)) + 5);

            var wBase = WorldFixtures.CloneWorld(wCivic);
            wBase.Config.Civic = null;
            var cBase = wBase.Camps[0];

            double mcLive = PhysicalDemography.CrowdingMitigation(wCivic, cCivic);
            Assert.Check(mcLive < 0.15, $"Lock 1: PhysicalDemography.CrowdingMitigation must be < 0.15 at C=0.75, I=1 (got {mcLive:F4}).");

            var recCivic = WorldFixtures.StepTurn(wCivic);
            var recBase = WorldFixtures.StepTurn(wBase);
            var dCivic = recCivic.Demographics.Single(d => d.Camp == cCivic.Id);
            var dBase = recBase.Demographics.Single(d => d.Camp == cBase.Id);

            Assert.Check(dCivic.CrowdingDeaths < dBase.CrowdingDeaths * 0.20,
                $"CrowdingDeaths must drop by > 80% (civic={dCivic.CrowdingDeaths:F1} vs base={dBase.CrowdingDeaths:F1}).");
            Assert.Check(cCivic.Population > cBase.Population,
                $"Mitigated camp ending population ({cCivic.Population}) must exceed unmitigated baseline ({cBase.Population}).");
        }

        private static void T1_F06_03()
        {
            var wCivic = WorldFixtures.CreatePlainsWorld(population: 2000, civicEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var cCivic = wCivic.Camps[0];
            cCivic.CivicComplexity = 0.75;
            cCivic.CivicStratification = 0.0;
            cCivic.LastEnvironmentalStratification = 0.0;
            cCivic.CivicCapital = CivicGovernance.RequiredCivicCapital(wCivic, cCivic);
            WorldFixtures.AssignExactStewards(cCivic, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wCivic, cCivic)));

            var wBase = WorldFixtures.CloneWorld(wCivic);
            wBase.Config.Civic = null;

            double sCivic = NeolithicProduction.SocialCohesion(wCivic, cCivic, 2000);
            double sBase = NeolithicProduction.SocialCohesion(wBase, wBase.Camps[0], 2000);
            Assert.Check(sCivic > 0.85, $"Lock 2: SocialCohesion(2000) with C=0.75, I=1 must be > 0.85 (got {sCivic:F4}, base={sBase:F4}).");
            Assert.Check(sBase < 0.35, $"Sanity check: unboosted baseline SocialCohesion(2000) should be < 0.35 (got {sBase:F4}).");

            NeolithicProduction.ResolveNeeds(wCivic, cCivic, 20.0);
            Assert.Check(cCivic.QoL > 0.75, $"Boosted camp QoL at N=2000 should be > 0.75 (got {cCivic.QoL:F4}).");

            var est = Fission.EstimateSplit(wCivic, cCivic, FissionPolicy.Neutral);
            var model = Fission.QoLModelOf(wCivic, cCivic);
            Assert.NearRelative(model.At(cCivic.Population), est.QNow, 1e-9,
                "Fission.QoLModelOf.At(Population) must equal Fission.EstimateSplit.QNow.");
        }

        private static void T1_F06_04()
        {
            var w = WorldFixtures.CreatePlainsWorld(
                population: 900,
                civicEnabled: true,
                sanitationEnabled: false,
                fission: false,
                immigration: false,
                landBiomassMultiplier: 1000.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];
            c.CivicComplexity = 0.25;
            // Cap at Tier 1 by setting very high Tier-2 threshold so village tests Tier-1 equilibrium cleanly.
            w.Config.Civic.Tier2PracticeThreshold = 1e6;

            var resolver = new TurnResolver();
            for (int t = 0; t < 14; t++)
                WorldFixtures.StepTurn(w, resolver);

            double cov = CivicGovernance.CivicCoverage(w, c);
            Assert.Check(c.Population >= 1400 && c.Population <= 1750,
                $"Organic Tier-1 village should climb into [1400, 1750], got N={c.Population} (C={c.CivicComplexity:F3}, cov={cov:F3}).");
            Assert.Check(cov > 0.85, $"Expected CivicCoverage > 0.85 at Tier-1 equilibrium, got {cov:F3}.");
        }

        private static void T1_F06_05()
        {
            var w = WorldFixtures.CreatePlainsWorld(
                population: 1000,
                civicEnabled: true,
                sanitationEnabled: false,
                fission: false,
                immigration: false,
                landBiomassMultiplier: 1000.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.55;
            w.Config.Civic.Tier3APracticeThreshold = 1e6;

            var resolver = new TurnResolver();
            TurnRecord last = null;
            for (int t = 0; t < 20; t++)
                last = WorldFixtures.StepTurn(w, resolver);

            var demo = last.Demographics.Single(d => d.Camp == c.Id);
            Assert.Check(c.Population >= 2600 && c.Population <= 3500,
                $"Organic Tier-2 Phase-1 city should climb into [2600, 3500], got N={c.Population}.");
            Assert.Check(PhysicalDemography.CrowdingMitigation(w, c) < 0.15,
                $"Expected CrowdingMitigation < 0.15, got {PhysicalDemography.CrowdingMitigation(w, c):F4}.");
            Assert.NearAbs(PhysicalDemography.DiseaseMitigation(w, c), 1.0, 1e-12, "DiseaseMitigation must remain 1.0 in Phase 1.");
            Assert.Check(demo.DiseaseDeaths > demo.CrowdingDeaths,
                $"Disease deaths ({demo.DiseaseDeaths:F1}) must overtake crowding deaths ({demo.CrowdingDeaths:F1}) at the Phase-1 ceiling.");
        }

        private static void T1_F06_06()
        {
            var wMaint = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var cMaint = wMaint.Camps[0];
            cMaint.CivicComplexity = 0.75;
            cMaint.LastEnvironmentalComplexity = 0.75;
            cMaint.CivicStratification = 0.0;
            cMaint.LastEnvironmentalStratification = 0.0;
            cMaint.CivicCapital = 150.0;
            cMaint.PlantFood = 1200 * PhysicalDemography.FoodPerPersonYear(wMaint.Config) * 25.0;
            cMaint.Inventory.HousingMaterial = 4000.0;
            cMaint.Inventory.Tools = 4000.0;

            var wUnmaint = WorldFixtures.CloneWorld(wMaint);
            wUnmaint.Resources = null;
            var cUnmaint = wUnmaint.Camps[0];
            cUnmaint.Inventory.HousingMaterial = 0.0;
            cUnmaint.Inventory.Stone = 0.0;
            cUnmaint.Inventory.Clay = 0.0;
            cUnmaint.Inventory.Wood = 0.0;

            WorldFixtures.StepTurn(wMaint);
            WorldFixtures.StepTurn(wUnmaint);

            double expectedUnmaint = 150.0 * Math.Exp(-wUnmaint.Config.Civic.CivicCapitalDecayPerYear * 20.0);
            Assert.Check(cMaint.CivicCapital >= 148.5, $"Maintained CivicCapital should retain >= 99% of 150 (got {cMaint.CivicCapital:F3}).");
            Assert.NearRelative(cUnmaint.CivicCapital, expectedUnmaint, 1e-9,
                $"Unmaintained CivicCapital should decay analytically to ~{expectedUnmaint:F2} (got {cUnmaint.CivicCapital:F3}).");
        }

        // ==================== F07 ====================

        private static void T1_F07_01()
        {
            WorldState BuildAligned(double p)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 2500, civicEnabled: true, fission: false, immigration: false,
                    landBiomassMultiplier: 1000.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                var c = w.Camps[0];
                c.CivicComplexity = 0.75;
                c.LastEnvironmentalComplexity = 0.75;
                c.CivicStratification = p;
                c.LastEnvironmentalStratification = p;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c) * 1.05;
                c.PlantFood = 2500 * PhysicalDemography.FoodPerPersonYear(w.Config) * 25.0;
                return w;
            }

            var wEgal = BuildAligned(-0.85);
            var wStrat = BuildAligned(0.85);
            double reqCapEgal = CivicGovernance.RequiredCivicCapital(wEgal, wEgal.Camps[0]);
            double reqCapStrat = CivicGovernance.RequiredCivicCapital(wStrat, wStrat.Camps[0]);

            WorldFixtures.StepTurn(wEgal);
            WorldFixtures.StepTurn(wStrat);

            int stewEgal = CultivationEconomy.Workers(wEgal.Camps[0], WorkforceRoles.Steward);
            int stewStrat = CultivationEconomy.Workers(wStrat.Camps[0], WorkforceRoles.Steward);

            Assert.Check(stewEgal > 0 && stewStrat > 0, $"Both camps must actively staff Stewards (egal={stewEgal}, strat={stewStrat}).");
            Assert.Check(stewEgal < stewStrat * 0.80, $"Egalitarian must staff strictly fewer Stewards at N=2500 (egal={stewEgal} vs strat={stewStrat}).");
            Assert.Check(reqCapEgal < reqCapStrat * 0.65, $"Egalitarian ReqCap ({reqCapEgal:F1}) must be strictly smaller than Stratified ({reqCapStrat:F1}).");
        }

        private static void T1_F07_02()
        {
            // Part A: Egalitarian Q_civic raises QoL strictly above Stratified at equal N=1,500
            var wEgal = WorldFixtures.CreatePlainsWorld(population: 1500, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var cEgal = wEgal.Camps[0];
            cEgal.CivicComplexity = 0.75;
            cEgal.LastEnvironmentalComplexity = 0.75;
            cEgal.CivicStratification = -0.80;
            cEgal.LastEnvironmentalStratification = -0.80;
            cEgal.CivicCapital = CivicGovernance.RequiredCivicCapital(wEgal, cEgal);
            WorldFixtures.AssignExactStewards(cEgal, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wEgal, cEgal)));

            var wStrat = WorldFixtures.CloneWorld(wEgal);
            var cStrat = wStrat.Camps[0];
            cStrat.CivicStratification = 0.80;
            cStrat.LastEnvironmentalStratification = 0.80;
            cStrat.CivicCapital = CivicGovernance.RequiredCivicCapital(wStrat, cStrat);
            WorldFixtures.AssignExactStewards(cStrat, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wStrat, cStrat)));

            NeolithicProduction.ResolveNeeds(wEgal, cEgal, 20.0);
            NeolithicProduction.ResolveNeeds(wStrat, cStrat, 20.0);

            Assert.Check(cEgal.QoL > cStrat.QoL + 0.04,
                $"Welfare-only Q_civic must raise Egalitarian QoL above Stratified by > 0.04 (egal={cEgal.QoL:F4}, strat={cStrat.QoL:F4}).");

            // Part B: Live two-arm StepTurn counterfactual — ablate Q_civic (0.09/0.01 vs 0.0/0.0) at identical (C, P, M_c, M_d, workforce)
            var wWithQ = WorldFixtures.CreatePlainsWorld(population: 1500, civicEnabled: true, fission: false, immigration: false, trade: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
            var cWithQ = wWithQ.Camps[0];
            cWithQ.EconomyAutomationTurns = 0; // identical fixed manual workforce across both arms
            cWithQ.CivicComplexity = 1.0;
            cWithQ.LastEnvironmentalComplexity = 1.0;
            cWithQ.CivicStratification = 0.90;
            cWithQ.LastEnvironmentalStratification = 0.90;
            cWithQ.CivicCapital = CivicGovernance.RequiredCivicCapital(wWithQ, cWithQ) * 2.0; // stays >= ReqCap after 20yr decay -> I_civic == 1
            cWithQ.PlantFood = 1500 * PhysicalDemography.FoodPerPersonYear(wWithQ.Config) * 30.0;
            cWithQ.Inventory.Cloth = 1500 * 4.0;
            WorldFixtures.AssignExactStewards(cWithQ, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wWithQ, cWithQ)) + 10);

            var wNoQ = WorldFixtures.CloneWorld(wWithQ);
            wNoQ.Config.Civic.QoLEqualityLinear = 0.0;
            wNoQ.Config.Civic.QoLEqualityQuadratic = 0.0;
            wNoQ.Config.Validate();
            var cNoQ = wNoQ.Camps[0];

            var recWithQ = WorldFixtures.StepTurn(wWithQ);
            var recNoQ = WorldFixtures.StepTurn(wNoQ);
            var repWithQ = recWithQ.Demographics.Single(d => d.Camp == cWithQ.Id);
            var repNoQ = recNoQ.Demographics.Single(d => d.Camp == cNoQ.Id);

            Assert.Check(Math.Abs(cWithQ.QoL - cNoQ.QoL) > 0.02 && cWithQ.QoL < cNoQ.QoL - 0.02,
                $"Live StepTurn Q_civic must visibly shift QoL (withQ={cWithQ.QoL:F4}, noQ={cNoQ.QoL:F4}).");
            Assert.Check(cWithQ.Population == cNoQ.Population,
                $"PhysicalDemography ending population must be strictly isolated from Q_civic ({cWithQ.Population} vs {cNoQ.Population}).");
            Assert.Check(repWithQ.GrossBirths == repNoQ.GrossBirths,
                $"GrossBirths must be strictly isolated from Q_civic ({repWithQ.GrossBirths} vs {repNoQ.GrossBirths}).");
            Assert.Check(repWithQ.GrossDeaths == repNoQ.GrossDeaths,
                $"GrossDeaths must be strictly isolated from Q_civic ({repWithQ.GrossDeaths} vs {repNoQ.GrossDeaths}).");
            Assert.NearAbs(repWithQ.ContinuousBirths, repNoQ.ContinuousBirths, 1e-12, "ContinuousBirths must be isolated from Q_civic.");
            Assert.NearAbs(repWithQ.ContinuousDeaths, repNoQ.ContinuousDeaths, 1e-12, "ContinuousDeaths must be isolated from Q_civic.");
            Assert.NearAbs(repWithQ.CrowdingDeaths, repNoQ.CrowdingDeaths, 1e-12, "CrowdingDeaths must be isolated from Q_civic.");
            Assert.NearAbs(repWithQ.DiseaseDeaths, repNoQ.DiseaseDeaths, 1e-12, "DiseaseDeaths must be isolated from Q_civic.");
        }

        private static void T1_F07_03()
        {
            WorldState BuildCamp(double p)
            {
                var w = WorldFixtures.CreateValleyWorld(population: 1600, civicEnabled: true, fission: false, immigration: false,
                    landBiomassMultiplier: 1200.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                WorldFixtures.SetFixedStepYears(w, 5.0);
                var c = w.Camps[0];
                c.CivicComplexity = 0.80;
                c.LastEnvironmentalComplexity = 0.80;
                c.CivicStratification = p;
                c.LastEnvironmentalStratification = p;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c) * 1.05;
                c.PlantFood = 1600 * PhysicalDemography.FoodPerPersonYear(w.Config) * 20.0;
                c.Inventory.HandTools = 0.0; // pure regular Tools for exact ToolsConsumed conservation check
                c.Inventory.Tools = 5000.0;
                c.Inventory.HousingMaterial = 5000.0;
                foreach (var seg in w.Canals) seg.FundedYears = 0.0; // force CanalMaintenance public-works production
                WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)) + 5);
                return w;
            }

            var wStrat = BuildCamp(0.90);
            var wEgal = BuildCamp(-0.90);

            var canalRecipe = NeolithicProduction.Recipes(wStrat.Config).Single(r => r.Good == CraftGood.CanalConstruction);
            double rateStrat = SpecialistProductivity.CraftRate(wStrat, wStrat.Camps[0], canalRecipe, WorkforceRoles.Builder);
            double rateEgal = SpecialistProductivity.CraftRate(wEgal, wEgal.Camps[0], canalRecipe, WorkforceRoles.Builder);
            double expectedRatio = CivicGovernance.PublicWorksMobilizationMultiplier(wStrat, wStrat.Camps[0])
                / CivicGovernance.PublicWorksMobilizationMultiplier(wEgal, wEgal.Camps[0]);

            Assert.Check(rateStrat > rateEgal * 1.20,
                $"Stratified M_build must boost Builder CanalConstruction rate by > 1.20x (strat={rateStrat:F5}, egal={rateEgal:F5}, expectedRatio={expectedRatio:F4}).");
            Assert.NearRelative(rateStrat / rateEgal, expectedRatio, 1e-9, "CraftRate ratio must match exact PublicWorksMobilizationMultiplier ratio.");

            // Live 1-turn public-works production step: verify Builder speedup AND conserved physical ToolsConsumed / Output
            var recStrat = WorldFixtures.StepTurn(wStrat);
            var recEgal = WorldFixtures.StepTurn(wEgal);
            var sReceipts = recStrat.Production.Where(r => r.Good == CraftGood.CanalMaintenance && r.Role == WorkforceRoles.Builder).ToList();
            var eReceipts = recEgal.Production.Where(r => r.Good == CraftGood.CanalMaintenance && r.Role == WorkforceRoles.Builder).ToList();
            Assert.Check(sReceipts.Count > 0 && eReceipts.Count > 0,
                $"Both camps must emit Builder CanalMaintenance public-works receipts (strat={sReceipts.Count}, egal={eReceipts.Count}).");

            double stratProdRate = sReceipts.Sum(r => r.Output) / Math.Max(1e-9, sReceipts.Sum(r => r.WorkerIntervals));
            double egalProdRate = eReceipts.Sum(r => r.Output) / Math.Max(1e-9, eReceipts.Sum(r => r.WorkerIntervals));
            Assert.Check(stratProdRate > egalProdRate * 1.20,
                $"Live public-works output per Builder-interval must be > 1.20x faster under Stratified M_build (strat={stratProdRate:F5}, egal={egalProdRate:F5}).");

            double stratToolsPerUnit = sReceipts.Sum(r => r.ToolsConsumed) / Math.Max(1e-9, sReceipts.Sum(r => r.Output));
            double egalToolsPerUnit = eReceipts.Sum(r => r.ToolsConsumed) / Math.Max(1e-9, eReceipts.Sum(r => r.Output));
            Assert.Check(stratToolsPerUnit > 0.0 && egalToolsPerUnit > 0.0, "Public-works production must consume physical Tools.");
            Assert.NearRelative(stratToolsPerUnit, egalToolsPerUnit, 1e-5,
                $"Physical ToolsConsumed per public-works unit must be conserved under M_build (strat={stratToolsPerUnit:F6}, egal={egalToolsPerUnit:F6}).");
        }

        private static void T1_F07_04()
        {
            WorldState BuildCamp(double p)
            {
                var w = WorldFixtures.CreateValleyWorld(population: 1600, civicEnabled: true, fission: false, immigration: false,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                var c = w.Camps[0];
                c.CivicComplexity = 0.80;
                c.CivicStratification = p;
                c.LastEnvironmentalStratification = p;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
                WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)));
                c.Inventory.Pots = 0.0;
                return w;
            }

            var wStrat = BuildCamp(0.90);
            var wEgal = BuildCamp(-0.90);
            var cStrat = wStrat.Camps[0];
            var cEgal = wEgal.Camps[0];

            double natStrat = NeolithicProduction.NaturalStorageCapacity(wStrat, cStrat, 0);
            double natEgal = NeolithicProduction.NaturalStorageCapacity(wEgal, cEgal, 0);
            double depStrat = NeolithicProduction.StorageCapacityAfterPotDepreciation(wStrat, cStrat, 0, 20.0);
            double depEgal = NeolithicProduction.StorageCapacityAfterPotDepreciation(wEgal, cEgal, 0, 20.0);

            Assert.Check(natStrat > natEgal * 1.30,
                $"Stratified M_storage must lift NaturalStorageCapacity by > 1.30x (strat={natStrat:F1} vs egal={natEgal:F1}).");
            Assert.Check(depStrat > depEgal * 1.20 && depStrat <= natStrat + 1e-6,
                $"StorageCapacityAfterPotDepreciation must reflect lower-bound M_storage (depStrat={depStrat:F1}, depEgal={depEgal:F1}, natStrat={natStrat:F1}).");
        }

        private static void T1_F07_05()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
            var cfg = WorldFixtures.BaseConfig(width: 24, height: 16, players: 3, civicEnabled: true,
                fission: false, immigration: true, trade: false, landBiomassMultiplier: 800.0);
            var w = WorldFixtures.CreateEmptyWorld(cfg, seed: 705);
            for (int i = 0; i < w.Tiles.Length; i++)
                w.Tiles[i] = WorldFixtures.MakeFertileTile(i, cfg);

            int tileSrc = w.Map.At(12, 8);
            int tileB = w.Map.At(15, 8); // distance 3 — Restricted Stratified added FIRST (lower Camp.Id wins M2 tie-break!)
            int tileA = w.Map.At(9, 8);  // distance 3 — Open Egalitarian added SECOND
            Assert.Check(w.Map.Distance(tileSrc, tileA) == w.Map.Distance(tileSrc, tileB), "Destinations A and B must be equidistant.");

            var src = WorldFixtures.AddVillage(w, 0, tileSrc, 1400, housingCoverage: 0.15, foodYears: 3.0, autoEconomy: false, precedents: precs);
            src.CivicComplexity = 0.0;
            src.CivicCapital = 0.0;

            var destB = WorldFixtures.AddVillage(w, 2, tileB, 700, housingCoverage: 1.5, foodYears: 20.0, precedents: precs);
            destB.CivicComplexity = 0.75;
            destB.CivicStratification = 0.80;
            destB.LastEnvironmentalStratification = 0.80;
            destB.ImmigrationPolicy = ImmigrationPolicy.Restricted;
            destB.CivicCapital = CivicGovernance.RequiredCivicCapital(w, destB) * 1.2;
            WorldFixtures.AssignExactStewards(destB, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, destB)) + 5);

            var destA = WorldFixtures.AddVillage(w, 1, tileA, 700, housingCoverage: 1.5, foodYears: 20.0, precedents: precs);
            destA.CivicComplexity = 0.75;
            destA.CivicStratification = -0.80;
            destA.LastEnvironmentalStratification = -0.80;
            destA.ImmigrationPolicy = ImmigrationPolicy.Open;
            destA.CivicCapital = CivicGovernance.RequiredCivicCapital(w, destA) * 1.2;
            WorldFixtures.AssignExactStewards(destA, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, destA)) + 5);

            WorldFixtures.CultivateFootprint(w, src, capital: 0.5);
            WorldFixtures.CultivateFootprint(w, destB, capital: 2.0);
            WorldFixtures.CultivateFootprint(w, destA, capital: 2.0);
            WorldFixtures.EstablishAllContacts(w);
            WorldFixtures.ValidateWorld(w);

            var rec = new TurnRecord { Turn = w.Turn, StartDays = w.Days, Duration = w.Config.TurnDays };
            Immigration.Resolve(w, rec, new PhaseExecution(), new ResolutionMetrics());
            int toA = rec.Migrations.Where(m => m.SourceCamp == src.Id && m.DestinationCamp == destA.Id).Sum(m => m.Migrants);
            int toB = rec.Migrations.Where(m => m.SourceCamp == src.Id && m.DestinationCamp == destB.Id).Sum(m => m.Migrants);
            int reverse = rec.Migrations.Where(m => m.DestinationCamp == src.Id).Sum(m => m.Migrants);

            Assert.Check(reverse == 0, $"Migration must never invert backwards into stressed source (reverse={reverse}).");
            Assert.Check(toA > 0, $"Migrants must flow forward into Open Egalitarian Destination A (toA={toA}, toB={toB}).");
            Assert.Check(toA > toB, $"Open Egalitarian Destination A ({toA}) must attract strictly more migrants than Restricted Stratified B ({toB}).");
        }

        private static void T1_F07_06()
        {
            WorldState BuildFissionArm(double p, double foodYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1800, civicEnabled: true, fission: true, immigration: false, trade: false,
                    width: 22, height: 16, landBiomassMultiplier: 800.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                var c = w.Camps[0];
                c.CivicComplexity = 0.75;
                c.CivicStratification = p;
                c.LastEnvironmentalStratification = p;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c) * 1.2;
                WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)) + 10);
                c.PlantFood = 1800 * PhysicalDemography.FoodPerPersonYear(w.Config) * foodYears;
                c.Inventory.LightHousingUnits = 600.0;
                WorldFixtures.SlashHomeFootprint(w, c, 0.02f);
                return w;
            }

            // Sweep from mild stress (high foodYears) to severe stress (0 foodYears): higher split foodYears = splits at lower stress.
            double egalSplitFoodYears = -1.0;
            double stratSplitFoodYears = -1.0;
            foreach (double fy in new[] { 18.0, 17.0, 16.0, 15.0, 14.0, 13.0, 12.0, 11.0, 10.0, 9.0, 8.0, 7.0, 6.0, 5.0, 4.0, 3.0, 2.0, 1.0, 0.0 })
            {
                if (egalSplitFoodYears < 0.0)
                {
                    var wE = BuildFissionArm(-0.85, fy);
                    var rE = new TurnRecord { Turn = wE.Turn, StartDays = wE.Days, Duration = wE.Config.TurnDays };
                    Fission.Resolve(wE, rE, new PhaseExecution(), new ResolutionMetrics());
                    if (rE.Fissions.Count > 0) egalSplitFoodYears = fy;
                }
                if (stratSplitFoodYears < 0.0)
                {
                    var wS = BuildFissionArm(0.85, fy);
                    var rS = new TurnRecord { Turn = wS.Turn, StartDays = wS.Days, Duration = wS.Config.TurnDays };
                    Fission.Resolve(wS, rS, new PhaseExecution(), new ResolutionMetrics());
                    if (rS.Fissions.Count > 0) stratSplitFoodYears = fy;
                }
            }

            Assert.Check(egalSplitFoodYears >= 0.0, "Egalitarian camp must split as home food stress intensifies.");
            Assert.Check(egalSplitFoodYears > stratSplitFoodYears,
                $"Aligned Egalitarian must disperse at strictly lower stress (higher foodYears={egalSplitFoodYears:F1}) than aligned Stratified ({stratSplitFoodYears:F1}).");
        }

        // ==================== F08 ====================

        private static void T1_F08_01()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false);
            var c = w.Camps[0];
            c.StationaryYears = 50.0;
            c.StationaryTurns = 3;
            c.People.CivicPrecedents.Clear();
            c.People.CivicExperience.Clear();
            Assert.NearAbs(CivicGovernance.MaxComplexity(w, c), 0.0, 1e-15, "MaxComplexity starts at 0 before Tier 1.");

            double prevExp = 0.0;
            bool unlocked = false;
            for (int t = 0; t < 4; t++)
            {
                CivicGovernance.Close(w, new TurnRecord(), 20.0);
                double exp = c.People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double v) ? v : 0.0;
                Assert.Check(exp > prevExp, $"Turn {t + 1}: Tier-1 practice must accrue monotonically ({exp:F2} > {prevExp:F2}).");
                prevExp = exp;
                if (c.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration))
                {
                    unlocked = true;
                    Assert.NearAbs(CivicGovernance.MaxComplexity(w, c), 0.30, 1e-12, "Tier-1 unlock must lift MaxComplexity to 0.30.");
                    Assert.Check(c.CivicComplexity > 0.0, "C must immediately relax upward on the unlock turn.");
                    break;
                }
            }
            Assert.Check(unlocked, $"Tier-1 CeremonialIntegration should unlock within 4x20y turns (exp={prevExp:F2}).");
        }

        private static void T1_F08_02()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1500, civicEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];
            WorldFixtures.AssignExactStewards(c, 20);
            Assert.NearAbs(CivicGovernance.MaxComplexity(w, c), 0.30, 1e-12, "MaxComplexity starts at 0.30 with Tier 1.");

            bool unlocked = false;
            for (int t = 0; t < 6; t++)
            {
                var rec = new TurnRecord();
                rec.Production.Add(new ProductionReceipt { Camp = c.Id, Role = WorkforceRoles.Builder, WorkerIntervals = 800.0 });
                CivicGovernance.Close(w, rec, 20.0);
                if (c.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization))
                {
                    unlocked = true;
                    break;
                }
            }
            Assert.Check(unlocked, "Tier-2 CivicOrganization must unlock within 6x20y turns.");
            Assert.NearAbs(CivicGovernance.MaxComplexity(w, c), 0.75, 1e-12, "Tier-2 unlock must lift MaxComplexity to 0.75.");
        }

        private static void T1_F08_03()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 2000, civicEnabled: true, fission: false, immigration: false, withResources: true,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            Assert.Check(c.Player == 0, "Solo Player 0 check.");
            c.People.NeolithicTechnologies.Remove(NeolithicTech.Pottery);
            c.Inventory.Pots = 0.0;
            c.RecentTradeVolume = 0.0;
            c.PlantFood = 2000 * PhysicalDemography.FoodPerPersonYear(w.Config) * 25.0;
            WorldFixtures.AssignExactStewards(c, 40);
            Assert.Check(CivicGovernance.HasLocalClay(w, c), "Local Clay must be present for arid/solo Tier-3A path.");

            bool unlocked = false;
            for (int t = 0; t < 10; t++)
            {
                CivicGovernance.Close(w, new TurnRecord(), 20.0);
                if (c.People.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting))
                {
                    unlocked = true;
                    break;
                }
            }
            Assert.Check(unlocked, "Solo Player 0 must unlock Tier-3A TokensAndAccounting via storage + Clay (zero deadlock).");
            Assert.NearAbs(CivicGovernance.MaxComplexity(w, c), 1.00, 1e-12, "Tier-3A unlock must lift MaxComplexity to 1.00.");
        }

        private static void T1_F08_04()
        {
            // Part A: Trade + Pottery accelerates Tier-3A accrual by >= 30%
            double Accrue3A(bool hasPottery, double tradeVol)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 2000, civicEnabled: true, fission: false, immigration: false,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                var c = w.Camps[0];
                if (!hasPottery) c.People.NeolithicTechnologies.Remove(NeolithicTech.Pottery);
                c.PlantFood = 2000 * PhysicalDemography.FoodPerPersonYear(w.Config) * 20.0;
                c.RecentTradeVolume = tradeVol;
                WorldFixtures.AssignExactStewards(c, 40);
                CivicGovernance.Close(w, new TurnRecord(), 20.0);
                return c.People.CivicExperience.TryGetValue(CivicPrecedents.TokensAndAccounting, out double v) ? v : 0.0;
            }

            double expHub = Accrue3A(true, 450.0);
            double expSolo = Accrue3A(false, 0.0);
            Assert.Check(expHub > expSolo * 1.30, $"AI trade hub + Pottery must accelerate Tier-3A by >= 30% (hub={expHub:F2}, solo={expSolo:F2}).");

            // Part B: Live +2 TradeRange unlocks real TradeRecords between complementary AI camps at distance TradeRange + 2
            int RunTradeAtExtendedRange(bool grantTier3A)
            {
                var cfg = WorldFixtures.BaseConfig(width: 26, height: 16, players: 3, civicEnabled: true, fission: false, immigration: false, trade: true);
                cfg.TradeFoodEnabled = false;
                var w = WorldFixtures.CreateEmptyWorld(cfg, seed: 804);
                for (int i = 0; i < w.Tiles.Length; i++)
                    w.Tiles[i] = WorldFixtures.MakeFertileTile(i, cfg);

                int dist = cfg.TradeRange + 2; // unreachable without Tier 3A (+2 bonus)
                var human = WorldFixtures.AddVillage(w, 0, w.Map.At(22, 13), 100, foodYears: 10.0);
                var precs = grantTier3A
                    ? new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting }
                    : new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };

                var ai1 = WorldFixtures.AddVillage(w, 1, w.Map.At(4, 7), 160, housingCoverage: 0.0, clothPerCapita: 0.0, housingMaterial: 0.0, foodYears: 12.0, autoEconomy: false, precedents: precs);
                ai1.Inventory.Cloth = 600.0;
                ai1.Inventory.HousingMaterial = 0.0;

                var ai2 = WorldFixtures.AddVillage(w, 2, w.Map.At(4 + dist, 7), 160, housingCoverage: 0.0, clothPerCapita: 0.0, housingMaterial: 0.0, foodYears: 12.0, autoEconomy: false, precedents: precs);
                ai2.Inventory.Cloth = 0.0;
                ai2.Inventory.HousingMaterial = 120.0;

                Assert.Check(w.Map.Distance(ai1.Tile, ai2.Tile) == dist, $"Expected AI camps at distance {dist}.");
                WorldFixtures.CultivateFootprint(w, human);
                WorldFixtures.CultivateFootprint(w, ai1);
                WorldFixtures.CultivateFootprint(w, ai2);
                WorldFixtures.EstablishContacts(w, ai1, ai2);
                WorldFixtures.ValidateWorld(w);

                var rec = WorldFixtures.StepTurn(w);
                return rec.Trades.Count;
            }

            int tradesWithout = RunTradeAtExtendedRange(false);
            int tradesWith = RunTradeAtExtendedRange(true);
            Assert.Check(tradesWithout == 0, $"Without Tier 3A, distance TradeRange+2 must clear 0 trades (got {tradesWithout}).");
            Assert.Check(tradesWith > 0, $"With Tier 3A (+2 range bonus), complementary AI camps at TradeRange+2 must clear >= 1 TradeRecord (got {tradesWith}).");
        }

        private static void T1_F08_05()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 3000, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            WorldFixtures.AssignExactStewards(c, 40);

            bool unlocked = false;
            for (int t = 0; t < 5; t++)
            {
                var rec = new TurnRecord();
                rec.Production.Add(new ProductionReceipt { Camp = c.Id, Role = WorkforceRoles.Builder, WorkerIntervals = 1600.0 });
                CivicGovernance.Close(w, rec, 20.0);
                if (c.People.HasCivicPrecedent(CivicPrecedents.PlannedQuartersAndDrainage))
                {
                    unlocked = true;
                    break;
                }
            }
            Assert.Check(unlocked, "Tier-3B PlannedQuartersAndDrainage must unlock within 5x20y turns at N=3000.");
        }

        private static void T1_F08_06()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1600, civicEnabled: true, sanitationEnabled: true, fission: true, immigration: false, width: 22, height: 16,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var parent = w.Camps[0];
            parent.CivicComplexity = 0.60;
            parent.CivicStratification = 0.60;
            parent.LastEnvironmentalStratification = 0.60;
            parent.CivicCapital = 120.0;
            parent.SanitationCapital = 80.0;
            parent.ImmigrationPolicy = ImmigrationPolicy.Restricted;
            parent.CivicFocus = CivicFocus.Centralized;
            parent.People.CivicExperience[CivicPrecedents.CeremonialIntegration] = 55.0;
            parent.FissionPolicy = FissionPolicy.Encourage;

            // Remove fixed housing so crowding forces real Fission.Resolve immediately
            w.FixedHousing.Clear();
            NeolithicProduction.FixedHousingChanged(w);
            WorldFixtures.ValidateWorld(w);

            var rec = new TurnRecord { Turn = w.Turn, StartDays = w.Days, Duration = w.Config.TurnDays };
            Fission.Resolve(w, rec, new PhaseExecution(), new ResolutionMetrics());

            Assert.Check(rec.Fissions.Count > 0 && w.Camps.Count == 2, $"Expected real Fission.Resolve split (fissions={rec.Fissions.Count}, camps={w.Camps.Count}).");
            var daughter = w.Camps.Single(x => x.Id != parent.Id);

            Assert.Check(daughter.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration) && daughter.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization),
                "Daughter must inherit parent CivicPrecedents.");
            Assert.NearAbs(daughter.CivicComplexity, 0.30, 1e-12, "Daughter C must dampen by 0.50 (0.60 -> 0.30).");
            Assert.NearAbs(daughter.CivicStratification, 0.15, 1e-12, "Daughter P must dampen by 0.50*P - 0.15 (0.60 -> 0.15).");
            Assert.NearAbs(daughter.CivicCapital, 0.0, 1e-15, "Daughter must start with CivicCapital == 0.");
            Assert.NearAbs(daughter.SanitationCapital, 0.0, 1e-15, "Daughter must start with SanitationCapital == 0.");
            Assert.Check(daughter.ImmigrationPolicy == ImmigrationPolicy.Neutral && daughter.CivicFocus == CivicFocus.Auto,
                "Daughter must reset policies to Neutral / Auto.");
            Assert.NearAbs(parent.CivicCapital, 120.0, 1e-12, "Parent must retain 100% of fixed CivicCapital.");
            Assert.NearAbs(parent.SanitationCapital, 80.0, 1e-12, "Parent must retain 100% of fixed SanitationCapital.");
        }

        // ==================== F09 ====================

        private static void T1_F09_01()
        {
            // Constrained vs unconstrained N=1400 T2 village
            WorldState Build(double hmStock, double foodYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1400, civicEnabled: true, fission: false, immigration: false, withResources: false,
                    landBiomassMultiplier: 800.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                var c = w.Camps[0];
                c.CivicComplexity = 0.60;
                c.LastEnvironmentalComplexity = 0.60;
                c.CivicCapital = 60.0; // partial capital -> needs both Continuity maintenance + Development construction
                c.Inventory.HousingMaterial = hmStock;
                c.Inventory.Tools = 1500.0;
                c.Inventory.Stone = 0.0;
                c.Inventory.Clay = 0.0;
                c.Inventory.Wood = 0.0;
                c.PlantFood = 1400 * PhysicalDemography.FoodPerPersonYear(w.Config) * foodYears;
                return w;
            }

            // Tight material budget: 35 HM Protected housing repair + 21.6 HM Continuity CivicMaintenance + ~8.4 HM residual (NOT enough for ~182 HM Development build)
            var wTight = Build(hmStock: 65.0, foodYears: 25.0);
            var wAmple = Build(hmStock: 2000.0, foodYears: 25.0);

            var recTight = WorldFixtures.StepTurn(wTight);
            var recAmple = WorldFixtures.StepTurn(wAmple);

            double buildAmple = recAmple.Production.Where(ProductionReceiptHelpers.IsCivicConstruction).Sum(r => r.Output);
            double maintTight = recTight.Production.Where(ProductionReceiptHelpers.IsCivicMaintenance).Sum(r => r.Output);
            double buildTight = recTight.Production.Where(ProductionReceiptHelpers.IsCivicConstruction).Sum(r => r.Output);

            Assert.Check(buildAmple > 10.0, $"Unconstrained control must fund CivicConstruction Development (built={buildAmple:F2}).");
            Assert.Check(wTight.Camps[0].FoodNeedsMetRatio >= 0.95 && wTight.Camps[0].ClothPerCapita >= 0.70,
                $"Tight budget must keep Protected survival funded (food={wTight.Camps[0].FoodNeedsMetRatio:F3}, cloth={wTight.Camps[0].ClothPerCapita:F3}).");
            Assert.Check(maintTight > 0.0 && buildTight < buildAmple * 0.5,
                $"Tight budget must prioritize CivicMaintenance ({maintTight:F2}) and defer Development ({buildTight:F2} vs ample {buildAmple:F2}).");
        }

        private static void T1_F09_02()
        {
            WorldState BuildDevelopmentCompetition(int pop)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: pop, civicEnabled: true, fission: false, immigration: false,
                    grantPlough: true, landBiomassMultiplier: 600.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration });
                var c = w.Camps[0];
                c.CivicComplexity = 0.25;
                c.LastEnvironmentalComplexity = 0.25;
                c.CivicCapital = 0.0;
                c.PlantFood = pop * PhysicalDemography.FoodPerPersonYear(w.Config) * 12.0;
                // Uniform low cultivation across all 7 footprint tiles so Development food-growth harvest (score ~7.45) binds cleanly on both camps.
                WorldFixtures.CultivateFootprint(w, c, capital: 0.10, maxTiles: 7);
                return w;
            }

            var wYoung = BuildDevelopmentCompetition(500);
            var wMature = BuildDevelopmentCompetition(1200);
            var cYoung = wYoung.Camps[0];
            var cMature = wMature.Camps[0];

            WorldFixtures.StepTurn(wYoung);
            WorldFixtures.StepTurn(wMature);

            Assert.Check(cMature.CivicCapital > 15.0, $"Crowded N=1200 village must outrank food expansion and invest in CivicCapital (got {cMature.CivicCapital:F2}).");
            Assert.Check(cYoung.CivicCapital < cMature.CivicCapital * 0.25,
                $"Young N=500 village should prioritize food expansion over Civic Development (youngCap={cYoung.CivicCapital:F2}, matureCap={cMature.CivicCapital:F2}).");
        }

        private static void T1_F09_03()
        {
            // Healthy control: ample food & normal gathering productivity funds Stewards & full CivicMaintenance
            var wHealthy = WorldFixtures.CreatePlainsWorld(population: 1800, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var cHealthy = wHealthy.Camps[0];
            cHealthy.CivicComplexity = 0.70;
            cHealthy.LastEnvironmentalComplexity = 0.70;
            cHealthy.CivicCapital = 200.0;
            cHealthy.PlantFood = 1800 * PhysicalDemography.FoodPerPersonYear(wHealthy.Config) * 25.0;
            WorldFixtures.AssignExactStewards(cHealthy, 90);

            WorldFixtures.StepTurn(wHealthy);
            int healthyStewards = CultivationEconomy.Workers(cHealthy, WorkforceRoles.Steward);
            Assert.Check(healthyStewards >= 40, $"Healthy control must staff Stewards (got {healthyStewards}).");
            Assert.Check(cHealthy.CivicCapital >= 195.0, $"Healthy control must maintain CivicCapital (got {cHealthy.CivicCapital:F2}).");

            // Genuine labor-bound famine: uncultivated ample-biomass tiles with GatherPerPerson=1.002 & zero reserves so Protected food consumes ~99.8% of workers
            var wFamine = WorldFixtures.CreatePlainsWorld(population: 1800, civicEnabled: true, fission: false, immigration: false,
                grantPlough: false, landBiomassMultiplier: 1200.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var cFamine = wFamine.Camps[0];
            WorldFixtures.ClearFootprintCultivation(wFamine, cFamine);
            wFamine.Config.GatherPerPerson = 1.002;
            wFamine.Config.Validate();
            cFamine.CivicComplexity = 0.70;
            cFamine.LastEnvironmentalComplexity = 0.70;
            cFamine.CivicCapital = 200.0;
            cFamine.PlantFood = 0.0;
            cFamine.AnimalFood = 0.0;
            cFamine.AquaticFood = 0.0;
            WorldFixtures.AssignExactStewards(cFamine, 90);
            WorldFixtures.ValidateWorld(wFamine);

            WorldFixtures.StepTurn(wFamine);
            int famineStewards = CultivationEconomy.Workers(cFamine, WorkforceRoles.Steward);
            Assert.Check(famineStewards < healthyStewards * 0.25,
                $"Labor-bound famine must slash Stewards before Protected survival food (famine={famineStewards} vs healthy={healthyStewards}).");
            Assert.Check(cFamine.CivicCapital < 190.0,
                $"CivicCapital must shed upkeep and decay gracefully under labor-bound famine (got {cFamine.CivicCapital:F2}).");
        }

        private static void T1_F09_04()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 2200, civicEnabled: true, fission: true, immigration: false, width: 24, height: 16,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.75;
            c.CivicStratification = -0.80;
            c.LastEnvironmentalStratification = -0.80;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
            c.Inventory.LightHousingUnits = 500.0;
            WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)));
            c.PlantFood = 2200 * PhysicalDemography.FoodPerPersonYear(w.Config) * 3.0;

            // Slash home footprint food to 2% (keeping outer fertile frontier intact!)
            WorldFixtures.SlashHomeFootprint(w, c, 0.02f);

            Assert.Check(PhysicalDemography.CrowdingMitigation(w, c) < 0.30,
                $"Pre-shock Phase-1 civics must be live (CrowdingMitigation={PhysicalDemography.CrowdingMitigation(w, c):F4}).");

            var resolver = new TurnResolver();
            int totalFissions = 0;
            for (int t = 0; t < 3; t++)
            {
                var rec = WorldFixtures.StepTurn(w, resolver);
                totalFissions += rec.Fissions.Count;
            }
            Assert.Check(totalFissions > 0 && w.Camps.Count > 1,
                $"Egalitarian mega-site under severe food shock must disperse via fission within 3 turns (fissions={totalFissions}, camps={w.Camps.Count}).");
        }

        private static void T1_F09_05()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
            var wEgal = WorldFixtures.CreatePlainsWorld(population: 2200, civicEnabled: true, fission: true, immigration: false,
                width: 24, height: 16, landBiomassMultiplier: 800.0, precedents: precs);
            var cEgal = wEgal.Camps[0];
            cEgal.CivicComplexity = 0.75;
            cEgal.CivicStratification = -0.80;
            cEgal.LastEnvironmentalStratification = -0.80;
            cEgal.CivicFocus = CivicFocus.Communal;
            cEgal.CivicCapital = CivicGovernance.RequiredCivicCapital(wEgal, cEgal);
            cEgal.Inventory.LightHousingUnits = 500.0;
            WorldFixtures.AssignExactStewards(cEgal, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wEgal, cEgal)));
            cEgal.PlantFood = NeolithicProduction.NaturalStorageCapacity(wEgal, cEgal, 0);

            // Give wStrat a viable downstream exit pocket so holding together proves Stratified granary + resistance, NOT desert lock!
            var wStrat = WorldFixtures.CreateValleyWorld(population: 2200, civicEnabled: true, fission: true, immigration: false,
                width: 24, height: 16, landBiomassMultiplier: 1600.0, withViableExitPocket: true, precedents: precs);
            var cStrat = wStrat.Camps[0];
            cStrat.CivicComplexity = 0.75;
            cStrat.CivicStratification = 0.80;
            cStrat.LastEnvironmentalStratification = 0.80;
            cStrat.CivicFocus = CivicFocus.Centralized;
            cStrat.CivicCapital = CivicGovernance.RequiredCivicCapital(wStrat, cStrat);
            cStrat.Inventory.LightHousingUnits = 500.0;
            WorldFixtures.AssignExactStewards(cStrat, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wStrat, cStrat)));

            Assert.Check(PhysicalDemography.CrowdingMitigation(wStrat, cStrat) < 0.30 && PhysicalDemography.CrowdingMitigation(wEgal, cEgal) < 0.30,
                $"Pre-shock Phase-1 civics must be live (M_c strat={PhysicalDemography.CrowdingMitigation(wStrat, cStrat):F4}, egal={PhysicalDemography.CrowdingMitigation(wEgal, cEgal):F4}).");
            var rE = new TurnResolver();
            var rS = new TurnResolver();
            // Two warm-up turns let both cities settle near their mitigated equilibria before the shock, so the
            // Stratified arm does not surge into its granary during shock Turn 1 (the same protocol as T4-04).
            for (int t = 0; t < 2; t++)
            {
                WorldFixtures.StepTurn(wEgal, rE);
                WorldFixtures.StepTurn(wStrat, rS);
            }
            Assert.Check(wEgal.Camps.Count == 1 && wStrat.Camps.Count == 1, "Warm-up must not fission either city.");

            var wStratBase = WorldFixtures.CloneWorld(wStrat);
            wStratBase.Config.Civic = null;
            double stratBaseStorage = NeolithicProduction.NaturalStorageCapacity(wStratBase, wStratBase.Camps[0], 0);
            double stratCivicStorage = NeolithicProduction.NaturalStorageCapacity(wStrat, cStrat, 0);
            Assert.Check(stratCivicStorage > stratBaseStorage * 1.25,
                $"Stratified granary storage ({stratCivicStorage:F0}) must exceed same-tile Civic=null baseline ({stratBaseStorage:F0}) by > 1.25x.");
            cEgal.PlantFood = NeolithicProduction.NaturalStorageCapacity(wEgal, cEgal, 0);
            cStrat.PlantFood = stratCivicStorage;
            cEgal.Inventory.LightHousingUnits = Math.Max(cEgal.Inventory.LightHousingUnits, 500.0);
            cStrat.Inventory.LightHousingUnits = Math.Max(cStrat.Inventory.LightHousingUnits, 500.0);

            WorldFixtures.SlashHomeFootprint(wEgal, cEgal, 0.02f);
            WorldFixtures.SlashHomeFootprint(wStrat, cStrat, 0.02f);

            int egalFissions = 0, stratFissions = 0;
            for (int t = 0; t < 2; t++)
            {
                egalFissions += WorldFixtures.StepTurn(wEgal, rE).Fissions.Count;
                stratFissions += WorldFixtures.StepTurn(wStrat, rS).Fissions.Count;
            }
            Assert.Check(egalFissions > 0 && wEgal.Camps.Count > 1,
                $"Matched Egalitarian control must disperse under shock within 2 turns (fissions={egalFissions}, camps={wEgal.Camps.Count}).");
            Assert.Check(stratFissions == 0 && wStrat.Camps.Count == 1,
                $"Stratified city on granary reserves must hold without fission for turns 1-2 despite viable exit pocket (fissions={stratFissions}).");
            Assert.Check(cStrat.Food > cEgal.Food && cStrat.FoodNeedsMetRatio > cEgal.FoodNeedsMetRatio,
                $"Stratified granary must sustain strictly higher food ({cStrat.Food:F0} vs {cEgal.Food:F0}) and nutrition ({cStrat.FoodNeedsMetRatio:F2} vs {cEgal.FoodNeedsMetRatio:F2}) on Turn 2.");
        }

        // ==================== F10 ====================

        private static void T1_F10_01()
        {
            var w = WorldFixtures.CreateValleyWorld(
                population: 2500,
                civicEnabled: true,
                sanitationEnabled: true,
                fission: false,
                immigration: false,
                landBiomassMultiplier: 1600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.PlannedQuartersAndDrainage });
            var c = w.Camps[0];
            c.CivicComplexity = 0.75;
            c.LastEnvironmentalComplexity = 0.75;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
            c.SanitationCapital = 0.0;
            c.Inventory.HousingMaterial = 5000.0;
            c.Inventory.Tools = 5000.0;

            var resolver = new TurnResolver();
            var rec1 = WorldFixtures.StepTurn(w, resolver);
            Assert.Check(c.SanitationCapital > 0.0, $"Turn 1 must build SanitationCapital > 0 (got {c.SanitationCapital:F3}).");
            Assert.Check(rec1.Production.Any(ProductionReceiptHelpers.IsSanitationConstruction),
                "Turn 1 must emit SanitationConstruction production receipt.");

            var rec2 = WorldFixtures.StepTurn(w, resolver);
            Assert.Check(rec2.SanitationWater != null && rec2.SanitationWater.Count > 0,
                "Turn 2 must emit SanitationWater receipts.");
            Assert.Check(c.SanitationWaterRatio > 0.90,
                $"River valley must achieve SanitationWaterRatio > 0.90 on Turn 2 (got {c.SanitationWaterRatio:F4}).");
        }

        private static void T1_F10_02()
        {
            // Water-constrained single river tile where WaterEnvelope (~5,169) < Drinking (4,000) + Sanitation (800) + Irrigation (10,000+)
            var w = WorldFixtures.CreateValleyWorld(
                population: 2000,
                civicEnabled: true,
                sanitationEnabled: true,
                fission: false,
                immigration: false,
                riverMaxFreshwater: 150.0,
                landBiomassMultiplier: 1200.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.PlannedQuartersAndDrainage });
            WorldFixtures.SetFixedStepYears(w, 20.0);
            var c = w.Camps[0];
            // Leave only ONE river tile (zero out second river tile) so Envelope ~ 5,169 over 20yr
            var rivers = w.Map.Radius(c.Tile, 1).Where(t => w.Tiles[t].FreshwaterSource == FreshwaterSource.River).ToList();
            Assert.Check(rivers.Count >= 2, "Valley fixture must start with 2 river tiles.");
            int droppedRiver = rivers[1];
            w.Tiles[droppedRiver] = WorldFixtures.MakeAridTile(droppedRiver, w.Config);
            w.Canals.RemoveAll(s => s.Source == droppedRiver);
            var validIds = new HashSet<int>(w.Canals.Select(s => s.Id));
            w.IrrigatedFields.RemoveAll(f => !validIds.Contains(f.Endpoint));
            foreach (var t in w.Tiles) t.Irrigated = w.IrrigatedFields.Any(f => f.Tile == t.Id);
            foreach (var f in w.IrrigatedFields) f.Area = 1.0; // guarantee oversubscribed Tier-3 irrigation demand regardless of fixture defaults
            Assert.Check(w.IrrigatedFields.Count > 0, "Single retained river tile must feed at least one irrigated field.");

            c.CivicComplexity = 0.75;
            c.LastEnvironmentalComplexity = 0.75;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
            c.SanitationCapital = CivicGovernance.RequiredSanitationCapital(w.Config, c.Population);
            WorldFixtures.ValidateWorld(w);

            int startPop = c.Population;
            double days = w.Config.TurnDays;
            double years = days / w.Config.DaysPerYear;
            double expectedDrinking = startPop * w.Config.Agriculture.AnnualDrinkingPerPerson * years;
            double kWater = w.Tiles[rivers[0]].Seasons[0].MaxFreshwater;
            double rWater = w.Tiles[rivers[0]].Seasons[0].WaterRecoveryPerDay;
            double x = rWater * days;
            double waterEnvelope = x < 1e-8
                ? kWater + rWater * kWater * days
                : rWater * (kWater + (x > 700.0 ? 0.0 : kWater / (Math.Exp(x) - 1.0))) * days;

            var rec = WorldFixtures.StepTurn(w);

            // Tier 1: Drinking receives exact 100% delivery with zero unmet pressure
            double drinkingDelivered = rec.Transfers
                .Where(tr => tr.Tile == rivers[0] && tr.Entity == c.Id && tr.Kind == "Drinking")
                .Sum(tr => tr.Freshwater);
            double drinkingUnmet = rec.Pressure
                .Where(p => p.Tile == rivers[0] && p.Entity == c.Id && p.Kind == "Drinking")
                .Sum(p => p.Unmet);
            Assert.NearRelative(drinkingDelivered, expectedDrinking, 1e-6,
                $"Tier-1 Drinking transfer must receive exact 100% priority (delivered={drinkingDelivered:F2}, expected={expectedDrinking:F2}).");
            Assert.Check(drinkingUnmet <= 1e-6, $"Tier-1 Drinking must have zero unmet demand (unmet={drinkingUnmet:E2}).");

            // Tier 2: SanitationWater receives full allocation ahead of Tier-3 Irrigation
            Assert.Check(rec.SanitationWater != null && rec.SanitationWater.Count > 0, "Tier-2 SanitationWater receipts must be recorded.");
            Assert.Check(c.SanitationWaterRatio >= 0.95, $"Tier-2 SanitationWater must come ahead of Tier-3 Irrigation (got {c.SanitationWaterRatio:F4}).");

            // Tier 3: Irrigation is strictly throttled below 100% behind Tiers 1 & 2
            Assert.Check(rec.IrrigationWater != null && rec.IrrigationWater.Count > 0, "Tier-3 IrrigationWater receipts must be recorded.");
            Assert.Check(rec.IrrigationWater.All(ir => ir.SupplyFraction < 1.0 - 1e-3),
                $"Tier-3 Irrigation must be throttled behind Tiers 1 & 2 (maxSupply={rec.IrrigationWater.Max(ir => ir.SupplyFraction):F4}).");

            // Strict conservation bound against exact analytical WaterEnvelope
            double riverWithdrawals = rec.Transfers.Where(tr => tr.Tile == rivers[0]).Sum(tr => tr.Freshwater);
            Assert.Check(riverWithdrawals <= waterEnvelope + 1e-7,
                $"River freshwater conservation violated ({riverWithdrawals:F4} > WaterEnvelope {waterEnvelope:F4}).");
            Assert.Check(w.Tiles[rivers[0]].Freshwater >= -1e-7,
                $"River tile freshwater must remain non-negative (got {w.Tiles[rivers[0]].Freshwater:F6}).");
        }

        private static void T1_F10_03()
        {
            var w = WorldFixtures.CreateValleyWorld(
                population: 4000,
                civicEnabled: true,
                sanitationEnabled: true,
                fission: false,
                immigration: false,
                riverMaxFreshwater: 150.0,
                landBiomassMultiplier: 1600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting, CivicPrecedents.PlannedQuartersAndDrainage });
            WorldFixtures.SetFixedStepYears(w, 2.0); // keeps 1-turn growth ~4.5% so end-of-turn SanitationCoverage stays ~0.95 > 0.90 while Drinking (400/yr) still saturates single river tile (258.5/yr)
            var c = w.Camps[0];
            c.CivicComplexity = 0.90;
            c.LastEnvironmentalComplexity = 0.90;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
            c.SanitationCapital = CivicGovernance.RequiredSanitationCapital(w.Config, c.Population);
            c.PlantFood = 4000 * PhysicalDemography.FoodPerPersonYear(w.Config) * 15.0;
            WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)));

            var rec = WorldFixtures.StepTurn(w);
            Assert.Check(rec.SanitationWater != null && rec.SanitationWater.Count > 0, "Multi-tile SanitationWater receipts expected.");
            Assert.Check(c.SanitationWaterRatio >= 0.99, $"N=4,000 with 2 river tiles must pool multi-tile water to SanitationWaterRatio ~ 1.0 (got {c.SanitationWaterRatio:F4}).");
            Assert.Check(CivicGovernance.SanitationCoverage(w, c) > 0.90, $"SanitationCoverage must be > 0.90 (got {CivicGovernance.SanitationCoverage(w, c):F4}).");
        }

        private static void T1_F10_04()
        {
            var wSanit = WorldFixtures.CreateValleyWorld(
                population: 3500,
                civicEnabled: true,
                sanitationEnabled: true,
                fission: false,
                immigration: false,
                landBiomassMultiplier: 1600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting, CivicPrecedents.PlannedQuartersAndDrainage });
            WorldFixtures.SetFixedStepYears(wSanit, 5.0); // avoids 20y population surge distorting cumulative DiseaseDeaths ratio
            var cSanit = wSanit.Camps[0];
            cSanit.CivicComplexity = 0.85;
            cSanit.LastEnvironmentalComplexity = 0.85;
            cSanit.CivicCapital = CivicGovernance.RequiredCivicCapital(wSanit, cSanit);
            cSanit.SanitationCapital = CivicGovernance.RequiredSanitationCapital(wSanit.Config, cSanit.Population);
            cSanit.SanitationWaterRatio = 1.0;
            WorldFixtures.AssignExactStewards(cSanit, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wSanit, cSanit)));

            var wNoSanit = WorldFixtures.CloneWorld(wSanit);
            wNoSanit.Config.Civic.SanitationEnabled = false;

            double mdSanit = PhysicalDemography.DiseaseMitigation(wSanit, cSanit);
            double mdNoSanit = PhysicalDemography.DiseaseMitigation(wNoSanit, wNoSanit.Camps[0]);
            Assert.Check(mdSanit < 0.35, $"PhysicalDemography.DiseaseMitigation must be < 0.35 when SanitationCoverage=1 (got {mdSanit:F4}).");
            Assert.NearAbs(mdNoSanit, 1.0, 1e-12, "DiseaseMitigation must be 1.0 when SanitationEnabled=false.");

            var recSanit = WorldFixtures.StepTurn(wSanit);
            var recNoSanit = WorldFixtures.StepTurn(wNoSanit);
            var dSanit = recSanit.Demographics.Single(d => d.Camp == cSanit.Id);
            var dNoSanit = recNoSanit.Demographics.Single(d => d.Camp == wNoSanit.Camps[0].Id);

            Assert.Check(dSanit.DiseaseDeaths < dNoSanit.DiseaseDeaths * 0.40,
                $"DiseaseDeaths must drop by > 60% with active sanitation (sanit={dSanit.DiseaseDeaths:F1} vs none={dNoSanit.DiseaseDeaths:F1}).");
        }

        private static void T1_F10_05()
        {
            int RunPeak(bool stratified)
            {
                var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting, CivicPrecedents.PlannedQuartersAndDrainage };
                var w = stratified
                    ? WorldFixtures.CreateValleyWorld(population: 2200, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false, landBiomassMultiplier: 2400.0, precedents: precs)
                    : WorldFixtures.CreatePlainsWorld(population: 2200, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false, landBiomassMultiplier: 1200.0, precedents: precs);
                var c = w.Camps[0];
                c.CivicComplexity = 0.85;
                c.LastEnvironmentalComplexity = 0.85;
                c.CivicStratification = stratified ? 0.70 : -0.70;
                c.LastEnvironmentalStratification = stratified ? 0.70 : -0.70;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
                c.SanitationCapital = CivicGovernance.RequiredSanitationCapital(w.Config, c.Population) * 0.5;
                c.Inventory.HousingMaterial = 12000.0;
                c.Inventory.Tools = 12000.0;
                c.Inventory.FarmTools = 2000.0;
                var resolver = new TurnResolver();
                int peak = c.Population;
                for (int t = 0; t < 20; t++)
                {
                    WorldFixtures.StepTurn(w, resolver);
                    if (c.Population > peak) peak = c.Population;
                }
                return peak;
            }

            int stratPeak = RunPeak(stratified: true);
            int egalPeak = RunPeak(stratified: false);
            Assert.Check(stratPeak >= 5500, $"Stratified Phase-2 city must break ~3,400 wall to >= 5,500 (got {stratPeak}).");
            Assert.Check(egalPeak >= 4500, $"Egalitarian Phase-2 city must also break ~3,400 wall (got {egalPeak}).");
            Assert.Check(stratPeak > egalPeak, $"Stratified peak ({stratPeak}) must exceed Egalitarian peak ({egalPeak}).");
        }

        // ==================== F11 ====================

        private static void T1_F11_01()
        {
            var w = WorldFixtures.CreateValleyWorld(
                population: 1400,
                civicEnabled: true,
                sanitationEnabled: true,
                fission: false,
                immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.52;
            c.CivicStratification = 0.44;
            c.CivicCapital = 123.45;
            c.SanitationCapital = 67.89;
            c.ImmigrationPolicy = ImmigrationPolicy.Restricted;
            c.CivicFocus = CivicFocus.Centralized;
            c.RecentTradeVolume = 88.25;
            c.SanitationWaterRatio = 0.76;
            c.LastEnvironmentalComplexity = 0.61;
            c.LastEnvironmentalStratification = 0.48;
            c.LastCircumscription = 0.73;
            c.People.CivicExperience[CivicPrecedents.TokensAndAccounting] = 42.5;
            WorldFixtures.ValidateWorld(w);

            string fpBefore = SaveCodec.Fingerprint(w);
            byte[] bytes = SaveCodec.Save(w);
            var loaded = SaveCodec.Load(bytes);
            Assert.Check(SaveCodec.Fingerprint(loaded) == fpBefore, "SaveCodec roundtrip must preserve exact Fingerprint.");

            var lc = loaded.Camps[0];
            Assert.NearAbs(lc.CivicComplexity, 0.52, 1e-15, "CivicComplexity roundtrip");
            Assert.NearAbs(lc.CivicStratification, 0.44, 1e-15, "CivicStratification roundtrip");
            Assert.NearAbs(lc.CivicCapital, 123.45, 1e-15, "CivicCapital roundtrip");
            Assert.NearAbs(lc.SanitationCapital, 67.89, 1e-15, "SanitationCapital roundtrip");
            Assert.Check(lc.ImmigrationPolicy == ImmigrationPolicy.Restricted, "ImmigrationPolicy roundtrip");
            Assert.Check(lc.CivicFocus == CivicFocus.Centralized, "CivicFocus roundtrip");
            Assert.NearAbs(lc.RecentTradeVolume, 88.25, 1e-15, "RecentTradeVolume roundtrip");
            Assert.NearAbs(lc.SanitationWaterRatio, 0.76, 1e-15, "SanitationWaterRatio roundtrip");
            Assert.NearAbs(lc.LastEnvironmentalComplexity, 0.61, 1e-15, "LastEnvironmentalComplexity roundtrip");
            Assert.NearAbs(lc.LastEnvironmentalStratification, 0.48, 1e-15, "LastEnvironmentalStratification roundtrip");
            Assert.NearAbs(lc.LastCircumscription, 0.73, 1e-15, "LastCircumscription roundtrip");
            Assert.NearAbs(lc.People.CivicExperience[CivicPrecedents.TokensAndAccounting], 42.5, 1e-15, "CivicExperience roundtrip");
            Assert.Check(lc.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration) && lc.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization),
                "CivicPrecedents roundtrip");

            var r1 = new TurnResolver();
            var r2 = new TurnResolver();
            for (int t = 0; t < 3; t++)
            {
                WorldFixtures.StepTurn(w, r1);
                WorldFixtures.StepTurn(loaded, r2);
            }
            Assert.Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded),
                "3-turn continuation after SaveCodec.Load must match unsaved world bit-for-bit.");
        }

        private static void T1_F11_02()
        {
            var w1 = WorldFixtures.CreatePlainsWorld(population: 500, civicEnabled: true, sanitationEnabled: true, withResources: false, width: 12, height: 10);
            var w2 = WorldFixtures.CreatePlainsWorld(population: 500, civicEnabled: true, sanitationEnabled: true, withResources: false, width: 20, height: 10);

            int bytes1 = SaveXmlTools.MeasureTileSectionBytes(SaveCodec.Save(w1), out int tiles1);
            int bytes2 = SaveXmlTools.MeasureTileSectionBytes(SaveCodec.Save(w2), out int tiles2);

            Assert.Check(tiles1 == 120 && tiles2 == 200, $"Expected 120 and 200 tiles (got {tiles1}, {tiles2}).");
            // Tail after tileCount is tileCount * 365 + 1 byte (hasResources = false).
            Assert.Check(bytes1 - 1 == tiles1 * 365, $"Expected 120*365 = {120 * 365} tile bytes, got {bytes1 - 1}.");
            Assert.Check(bytes2 - 1 == tiles2 * 365, $"Expected 200*365 = {200 * 365} tile bytes, got {bytes2 - 1}.");
            Assert.Check((bytes2 - bytes1) == (tiles2 - tiles1) * 365, "Tile byte delta must be exact 365 bytes/tile.");
        }

        private static void T1_F11_03()
        {
            var w = WorldFixtures.CreateValleyWorld(population: 1200, civicEnabled: true, sanitationEnabled: true,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.55;
            c.CivicStratification = 0.38;
            c.CivicCapital = 110.0;
            c.SanitationCapital = 45.0;
            c.SanitationWaterRatio = 0.85;
            c.LastEnvironmentalComplexity = 0.65;
            c.LastEnvironmentalStratification = 0.42;
            c.ImmigrationPolicy = ImmigrationPolicy.Restricted;
            c.CivicFocus = CivicFocus.Centralized;
            WorldFixtures.AssignExactStewards(c, 32);

            var obs = new AgentObservationBuilder().Build(w, c.Player);
            Assert.Check(AgentObservation.SchemaVersion == 23, $"SchemaVersion must be 23 (got {AgentObservation.SchemaVersion}).");
            string json = ObservationContract.Serialize(obs, c.Mandate);
            var dto = ObservationContract.Deserialize(json);

            Assert.Check(dto.Schema == 23, $"DTO Schema must be 23 (got {dto.Schema}).");
            Assert.Check(dto.CivicGovernanceEnabled && dto.SanitationEnabled, "DTO top-level civic flags must roundtrip.");
            var s = dto.Settlements.Single(x => x.Id == c.Id);
            Assert.Check(s.StewardEnabled && s.StewardPopulation == 32, "DTO Steward fields must roundtrip.");
            Assert.NearAbs(s.CivicComplexity, 0.55, 1e-12, "DTO CivicComplexity");
            Assert.NearAbs(s.CivicStratification, 0.38, 1e-12, "DTO CivicStratification");
            Assert.NearAbs(s.LastEnvironmentalComplexity, 0.65, 1e-12, "DTO LastEnvironmentalComplexity");
            Assert.NearAbs(s.LastEnvironmentalStratification, 0.42, 1e-12, "DTO LastEnvironmentalStratification");
            Assert.NearAbs(s.CivicCapital, 110.0, 1e-12, "DTO CivicCapital");
            Assert.NearAbs(s.CivicCoverage, CivicGovernance.CivicCoverage(w, c), 1e-12, "DTO CivicCoverage");
            Assert.NearAbs(s.SanitationCapital, 45.0, 1e-12, "DTO SanitationCapital");
            Assert.NearAbs(s.SanitationCoverage, CivicGovernance.SanitationCoverage(w, c), 1e-12, "DTO SanitationCoverage");
            Assert.NearAbs(s.SanitationWaterRatio, 0.85, 1e-12, "DTO SanitationWaterRatio");
            Assert.Check(s.ImmigrationPolicy == ImmigrationPolicy.Restricted, "DTO ImmigrationPolicy");
            Assert.Check(s.CivicFocus == CivicFocus.Centralized, "DTO CivicFocus");
            Assert.Check(s.CivicPrecedents != null && s.CivicPrecedents.Contains(CivicPrecedents.CeremonialIntegration) && s.CivicPrecedents.Contains(CivicPrecedents.CivicOrganization),
                "DTO CivicPrecedents");
        }

        private static void T1_F11_04()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, sanitationEnabled: true, width: 20, height: 14,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c0 = w.Camps[0];
            c0.CivicComplexity = 0.28;
            c0.CivicStratification = -0.45;
            c0.CivicCapital = 60.0;

            int rivalTile = w.Map.At((c0.Tile % w.Config.Width) + 2, c0.Tile / w.Config.Width); // within vision radius 2!
            var c1 = WorldFixtures.AddVillage(w, 1, rivalTile, 1200, precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            c1.CivicComplexity = 0.72;
            c1.CivicStratification = 0.88;
            c1.CivicCapital = 333.0;
            WorldFixtures.EstablishContacts(w, c0, c1);
            WorldFixtures.ValidateWorld(w);

            var obs0 = new AgentObservationBuilder().Build(w, 0);
            Assert.Check(obs0.Rivals.Any(r => r.Id == c1.Id), "Rival camp within vision radius 2 must be visible in Rivals.");

            string ownHover = TileDetails.Describe(obs0, c0.Tile, Array.Empty<Order>());
            string rivalHover = TileDetails.Describe(obs0, c1.Tile, Array.Empty<Order>());
            Assert.Check(ownHover.IndexOf("CIVIC", StringComparison.OrdinalIgnoreCase) >= 0,
                $"Owner TileDetails.Describe must include owner-only CIVIC summary (got:\n{ownHover}).");
            Assert.Check(rivalHover.IndexOf("CIVIC", StringComparison.OrdinalIgnoreCase) < 0 && !rivalHover.Contains("0.72") && !rivalHover.Contains("333"),
                $"Rival TileDetails.Describe must NOT leak rival CIVIC data (got:\n{rivalHover}).");

            string json0 = ObservationContract.Serialize(obs0, c0.Mandate);
            Assert.Check(ObservationLeak.SettlementIds(json0).All(id => id != c1.Id), "Owner Settlements must not include the rival camp.");
            Assert.Check(!ObservationLeak.RivalEntriesHaveCivicFields(json0, "Rivals", "KnownSettlements"),
                "Serialized rival entries must carry zero civic/sanitation/steward/immigration properties.");
            Assert.Check(!ObservationLeak.ContainsNumber(json0, 333.0) && !ObservationLeak.ContainsNumber(json0, 0.88) && !ObservationLeak.ContainsNumber(json0, 0.72),
                "Serialized ObservationDto must not leak rival civic values (exact numeric match).");
        }

        private static void T1_F11_05()
        {
            bool prevAudit = SocialRelocationForecastCache.AuditBounds;
            SocialRelocationForecastCache.AuditBounds = true;
            try
            {
                long evalsBefore = SocialRelocationForecastCache.BoundEvaluations;
                var w = WorldFixtures.CreatePlainsWorld(population: 1600, civicEnabled: true, sanitationEnabled: true, fission: true, immigration: true, width: 24, height: 16,
                    landBiomassMultiplier: 800.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                var c0 = w.Camps[0];
                c0.CivicComplexity = 0.70;
                c0.CivicStratification = -0.60;
                c0.LastEnvironmentalStratification = -0.60;
                c0.ImmigrationPolicy = ImmigrationPolicy.Open;
                c0.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c0);
                WorldFixtures.AssignExactStewards(c0, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c0)));

                var c1 = WorldFixtures.AddVillage(w, 1, w.Map.At(15, 8), 1100, foodYears: 14.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                c1.CivicComplexity = 0.65;
                c1.CivicStratification = 0.65;
                c1.LastEnvironmentalStratification = 0.65;
                c1.ImmigrationPolicy = ImmigrationPolicy.Restricted;
                c1.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c1);
                WorldFixtures.AssignExactStewards(c1, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c1)));
                WorldFixtures.CultivateFootprint(w, c1);

                var c2 = WorldFixtures.AddVillage(w, 2, w.Map.At(9, 8), 900, foodYears: 16.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration });
                c2.CivicComplexity = 0.28;
                c2.CivicStratification = -0.35;
                c2.ImmigrationPolicy = ImmigrationPolicy.Open;
                c2.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c2);
                WorldFixtures.AssignExactStewards(c2, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c2)));
                WorldFixtures.CultivateFootprint(w, c2);

                WorldFixtures.EstablishAllContacts(w);
                WorldFixtures.ValidateWorld(w);

                Assert.Check(PhysicalDemography.CrowdingMitigation(w, c0) < 0.80,
                    $"Bound audit requires live Phase-1 crowding mitigation (got {PhysicalDemography.CrowdingMitigation(w, c0):F4}).");

                var resolver = new TurnResolver();
                for (int t = 0; t < 5; t++)
                    WorldFixtures.StepTurn(w, resolver);

                Assert.Check(SocialRelocationForecastCache.BoundEvaluations > evalsBefore,
                    "Bound audit must exercise > 0 BoundEvaluations.");
            }
            finally
            {
                SocialRelocationForecastCache.AuditBounds = prevAudit;
            }
        }

        // ==================== F12 ====================

        private static void T1_F12_01()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: false);
            var c = w.Camps[0];
            c.CivicStratification = -0.60;
            c.CrowdingMortalityRate = 0.01;
            c.FoodNeedsMetRatio = 1.0;

            var obs = new AgentObservationBuilder().Build(w, c.Player);
            Assert.Check(!obs.CivicGovernanceEnabled, "Observation must report CivicGovernanceEnabled == false.");
            var decision = new HeuristicTribePolicy().Decide(obs, c.Mandate);
            bool anyPolicy = decision.Orders.Any(o =>
                o.Kind == OrderKind.FissionPolicy || o.Kind == OrderKind.ImmigrationPolicy || o.Kind == OrderKind.CivicFocus);
            Assert.Check(!anyPolicy, "HeuristicTribePolicy must emit ZERO policy orders when CivicGovernanceEnabled == false.");
        }

        private static void T1_F12_02()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true);
            var c = w.Camps[0];
            c.CivicStratification = -0.50;
            c.CrowdingMortalityRate = 0.01;
            c.FoodNeedsMetRatio = 0.98;
            c.ImmigrationPolicy = ImmigrationPolicy.Neutral;

            var obs = new AgentObservationBuilder().Build(w, c.Player);
            var decision = new HeuristicTribePolicy().Decide(obs, c.Mandate);
            var immOrders = decision.Orders.Where(o => o.Kind == OrderKind.ImmigrationPolicy).ToList();
            Assert.Check(immOrders.Count == 1 && immOrders[0].Target == (int)ImmigrationPolicy.Open,
                $"Expected single ImmigrationPolicy.Open order, got count={immOrders.Count}, target={(immOrders.Count > 0 ? immOrders[0].Target : -1)}.");
            Assert.Check(!decision.Orders.Any(o => o.Kind == OrderKind.FissionPolicy || o.Kind == OrderKind.CivicFocus),
                "AI Elder must emit ZERO FissionPolicy/CivicFocus orders.");
        }

        private static void T1_F12_03()
        {
            var w = WorldFixtures.CreateValleyWorld(population: 1400, civicEnabled: true);
            var c = w.Camps[0];
            c.CivicStratification = 0.60;
            c.CrowdingMortalityRate = 0.045;
            c.FoodNeedsMetRatio = 0.95;
            c.ImmigrationPolicy = ImmigrationPolicy.Neutral;

            var obs = new AgentObservationBuilder().Build(w, c.Player);
            var decision = new HeuristicTribePolicy().Decide(obs, c.Mandate);
            var immOrders = decision.Orders.Where(o => o.Kind == OrderKind.ImmigrationPolicy).ToList();
            Assert.Check(immOrders.Count == 1 && immOrders[0].Target == (int)ImmigrationPolicy.Restricted,
                $"Expected single ImmigrationPolicy.Restricted order, got count={immOrders.Count}, target={(immOrders.Count > 0 ? immOrders[0].Target : -1)}.");
        }

        private static void T1_F12_04()
        {
            var wPlain = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false);
            var cfg = wPlain.Config;
            Assert.NearAbs(CivicGovernance.SteeringOffset(cfg, FissionPolicy.Neutral, ImmigrationPolicy.Neutral, CivicFocus.Auto), 0.0, 1e-15,
                "Auto + Neutral SteeringOffset must be exact 0.0.");
            Assert.NearAbs(CivicGovernance.TargetStratification(cfg, -0.47, FissionPolicy.Neutral, ImmigrationPolicy.Neutral, CivicFocus.Auto), -0.47, 1e-15,
                "Auto TargetStratification must equal pure P_env.");
            Assert.NearAbs(CivicGovernance.TargetStratification(cfg, 0.63, FissionPolicy.Neutral, ImmigrationPolicy.Neutral, CivicFocus.Auto), 0.63, 1e-15,
                "Auto TargetStratification must equal pure P_env for valley.");

            Assert.Check(CivicGovernance.EffectiveFocusLabel(new Camp { CivicFocus = CivicFocus.Auto, LastEnvironmentalStratification = -0.25 }) == "Communal",
                "Auto display label at P_env <= -0.15 must be Communal.");
            Assert.Check(CivicGovernance.EffectiveFocusLabel(new Camp { CivicFocus = CivicFocus.Auto, LastEnvironmentalStratification = 0.0 }) == "Hybrid",
                "Auto display label in (-0.15, +0.15) must be Hybrid.");
            Assert.Check(CivicGovernance.EffectiveFocusLabel(new Camp { CivicFocus = CivicFocus.Auto, LastEnvironmentalStratification = 0.25 }) == "Centralized",
                "Auto display label at P_env >= +0.15 must be Centralized.");
        }

        private static void T1_F12_05()
        {
            var wPlain = WorldFixtures.CreatePlainsWorld(population: 1100, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var wValley = WorldFixtures.CreateValleyWorld(population: 1100, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });

            // Run both completely autonomously via HeuristicTribePolicy + DemandControl (no manual orders)
            var rPlain = new TurnResolver();
            var rValley = new TurnResolver();
            for (int t = 0; t < 10; t++)
            {
                WorldFixtures.StepWithAi(wPlain, rPlain, excludedPlayer: -1);
                WorldFixtures.StepWithAi(wValley, rValley, excludedPlayer: -1);
            }

            var cPlain = wPlain.Camps[0];
            var cValley = wValley.Camps[0];
            Assert.Check(cPlain.CivicStratification < -0.25,
                $"Plain AI must diverge Egalitarian (P < -0.25), got {cPlain.CivicStratification:F4}.");
            Assert.Check(cValley.CivicStratification > 0.25,
                $"Valley AI must diverge Stratified (P > +0.25), got {cValley.CivicStratification:F4}.");
            Assert.Check(cPlain.ImmigrationPolicy == ImmigrationPolicy.Open || cPlain.ImmigrationPolicy == ImmigrationPolicy.Neutral,
                $"Plain AI immigration posture should be Open/Neutral, got {cPlain.ImmigrationPolicy}.");
            Assert.Check(cPlain.CivicCapital > 0.0 && cValley.CivicCapital > 0.0,
                $"Both autonomous AI settlements must build live CivicCapital (plain={cPlain.CivicCapital:F2}, valley={cValley.CivicCapital:F2}).");
        }
    }
}
