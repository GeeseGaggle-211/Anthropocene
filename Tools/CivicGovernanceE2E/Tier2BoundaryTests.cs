using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

namespace Anthropocene.CivicGovernanceE2E
{
    internal static class Tier2BoundaryTests
    {
        public static List<E2ETestCase> All()
        {
            return new List<E2ETestCase>
            {
                // F01 (5)
                new E2ETestCase("T2-F01-01", 2, "F01", "CivicGovernanceSettings.Validate rejects NaN/Inf/inverted ceilings/floors/QoL", M2Status.PassOnM2, T2_F01_01),
                new E2ETestCase("T2-F01-02", 2, "F01", "Enabled=false with SanitationEnabled=true stays completely inert", M2Status.PassOnM2, T2_F01_02),
                new E2ETestCase("T2-F01-03", 2, "F01", "Legacy JSON configs (OpeningConfig, AgriculturalTechnology) have null Civic", M2Status.PassOnM2, T2_F01_03),
                new E2ETestCase("T2-F01-04", 2, "F01", "Mid-campaign disabled-to-enabled migration initializes cleanly", M2Status.PassOnM2, T2_F01_04),
                new E2ETestCase("T2-F01-05", 2, "F01", "Zero-ablated QoL (q1=0, q2=0) validates cleanly to exact 1.0", M2Status.PassOnM2, T2_F01_05),

                // F02 (5)
                new E2ETestCase("T2-F02-01", 2, "F02", "WorldState.Resources == null safety in EvaluateEnvironment & Close", M2Status.PassOnM2, T2_F02_01),
                new E2ETestCase("T2-F02-02", 2, "F02", "Player-ID invariance (zero Player == 0 branch)", M2Status.PassOnM2, T2_F02_02),
                new E2ETestCase("T2-F02-03", 2, "F02", "Degenerate all-water / zero-land neighbor ring guard", M2Status.PassOnM2, T2_F02_03),
                new E2ETestCase("T2-F02-04", 2, "F02", "Anti-oscillation invariant (C_raw uses unboosted N_{s,0})", M2Status.PassOnM2, T2_F02_04),
                new E2ETestCase("T2-F02-05", 2, "F02", "Small camp below N=150 yields C_raw == 0 and relaxes downward", M2Status.PassOnM2, T2_F02_05),

                // F03 (6)
                new E2ETestCase("T2-F03-01", 2, "F03", "Policy orders rejected & absent from LegalActions when civics disabled", M2Status.PassOnM2, T2_F03_01),
                new E2ETestCase("T2-F03-02", 2, "F03", "Out-of-range enum targets & non-zero payload rejected", M2Status.PassOnM2, T2_F03_02),
                new E2ETestCase("T2-F03-03", 2, "F03", "Duplicate same-kind policy order rejected cleanly in TurnPlanner.Prepare", M2Status.PassOnM2, T2_F03_03),
                new E2ETestCase("T2-F03-04", 2, "F03", "Extreme steering clamps strictly inside [-1, +1]", M2Status.PassOnM2, T2_F03_04),
                new E2ETestCase("T2-F03-05", 2, "F03", "Pre-civic C=0 keeps exact legacy FissionGainThreshold with C=0.8 positive control", M2Status.FailUntilM3, T2_F03_05),
                new E2ETestCase("T2-F03-06", 2, "F03", "Alignment mismatch exit friction overcomes Stratified fission resistance on open frontier", M2Status.FailUntilM3, T2_F03_06),

                // F04 (5)
                new E2ETestCase("T2-F04-01", 2, "F04", "Ultra-small 7300x1d vs 1x20y Relax & permanence ramp integral", M2Status.PassOnM2, T2_F04_01),
                new E2ETestCase("T2-F04-02", 2, "F04", "Sub-normal / tiny rate*years < 1e-290 & non-positive / NaN / Inf Relax safety", M2Status.PassOnM2, T2_F04_02),
                new E2ETestCase("T2-F04-03", 2, "F04", "Mobile camp moving every 1-yr turn accrues exact 0 Tier-1 practice vs stationary control", M2Status.PassOnM2, T2_F04_03),
                new E2ETestCase("T2-F04-04", 2, "F04", "Over-capitalized camp after population dip maintains only min(K_open, ReqCap) across dt", M2Status.FailUntilM3, T2_F04_04),
                new E2ETestCase("T2-F04-05", 2, "F04", "SmoothCalendar mid-compression save/load boundary validation & parity", M2Status.PassOnM2, T2_F04_05),

                // F05 (5)
                new E2ETestCase("T2-F05-01", 2, "F05", "Pre-Tier-1 Steward assignment rejected", M2Status.PassOnM2, T2_F05_01),
                new E2ETestCase("T2-F05-02", 2, "F05", "Zero-population RequiredStewards == 0 & exact +/-0.30 label boundaries", M2Status.PassOnM2, T2_F05_02),
                new E2ETestCase("T2-F05-03", 2, "F05", "Over-assigning Steward > Population rejected cleanly", M2Status.PassOnM2, T2_F05_03),
                new E2ETestCase("T2-F05-04", 2, "F05", "Turn-1 zero idle Stewards when C==0 after Tier-1 unlock, staffing on Turn 2", M2Status.FailUntilM3, T2_F05_04),
                new E2ETestCase("T2-F05-05", 2, "F05", "Abandoning village via Move zeroes fixed capitals while retaining Society precedents", M2Status.PassOnM2, T2_F05_05),

                // F06 (5)
                new E2ETestCase("T2-F06-01", 2, "F06", "Two-input min() coverage bottleneck (capital-only vs steward-only vs both control)", M2Status.FailUntilM3, T2_F06_01),
                new E2ETestCase("T2-F06-02", 2, "F06", "Non-village camp builds zero CivicCapital vs village positive control", M2Status.FailUntilM3, T2_F06_02),
                new E2ETestCase("T2-F06-03", 2, "F06", "CivicCapital decays analytically even when camp economy is disabled", M2Status.FailUntilM3, T2_F06_03),
                new E2ETestCase("T2-F06-04", 2, "F06", "Hard positive M_c >= 0.05 floor & solver convergence", M2Status.FailUntilM3, T2_F06_04),
                new E2ETestCase("T2-F06-05", 2, "F06", "Zero HousingMaterial blocks CivicConstruction without phantom capital vs positive control", M2Status.FailUntilM3, T2_F06_05),

                // F07 (5)
                new E2ETestCase("T2-F07-01", 2, "F07", "Superlinear Steward crossover above N_knee=3,500 without Tier 3A & flattening with Tier 3A", M2Status.FailUntilM3, T2_F07_01),
                new E2ETestCase("T2-F07-02", 2, "F07", "KernelSplit tie-collapse guard at N=10,000", M2Status.FailUntilM3, T2_F07_02),
                new E2ETestCase("T2-F07-03", 2, "F07", "M_build exclusively boosts public works, NEVER private Housing/LightHousing/HM", M2Status.FailUntilM3, T2_F07_03),
                new E2ETestCase("T2-F07-04", 2, "F07", "Granary storage lower-bound invariant (StorageCapacityAfterPotDepreciation <= ApplyStorage)", M2Status.FailUntilM3, T2_F07_04),
                new E2ETestCase("T2-F07-05", 2, "F07", "Zero coverage (I_civic=0) collapses all P-modifiers to identity 1.0 vs positive control", M2Status.FailUntilM3, T2_F07_05),

                // F08 (6)
                new E2ETestCase("T2-F08-01", 2, "F08", "Paleolithic era accrues exact 0 Tier-1 practice vs Neolithic positive control", M2Status.PassOnM2, T2_F08_01),
                new E2ETestCase("T2-F08-02", 2, "F08", "Zero Stewards accrues exact 0 Tier-2 practice vs staffed positive control", M2Status.PassOnM2, T2_F08_02),
                new E2ETestCase("T2-F08-03", 2, "F08", "Missing Pottery/Pots/Clay blocks Tier-3A when Resources != null vs local Clay control", M2Status.PassOnM2, T2_F08_03),
                new E2ETestCase("T2-F08-04", 2, "F08", "Four individual negative gates for Tier-3B vs positive control", M2Status.PassOnM2, T2_F08_04),
                new E2ETestCase("T2-F08-05", 2, "F08", "Inert trade diffusion hook (PrecedentDiffusionPerTrade == 0 default vs > 0 opt-in)", M2Status.FailUntilM3, T2_F08_05),
                new E2ETestCase("T2-F08-06", 2, "F08", "Daughter society mutation does not alias parent precedents", M2Status.PassOnM2, T2_F08_06),

                // F09 (5)
                new E2ETestCase("T2-F09-01", 2, "F09", "DevelopmentMinPopulation=300 gate with N=1,000 positive control", M2Status.FailUntilM3, T2_F09_01),
                new E2ETestCase("T2-F09-02", 2, "F09", "Timber/HM shortage does not block material-free core stewards", M2Status.FailUntilM3, T2_F09_02),
                new E2ETestCase("T2-F09-03", 2, "F09", "Civic Continuity trims before farm/herding/fishing tools under tight workforce ties", M2Status.FailUntilM3, T2_F09_03),
                new E2ETestCase("T2-F09-04", 2, "F09", "Extreme cold + famine protects clothing/shelter strictly ahead of civics", M2Status.FailUntilM3, T2_F09_04),
                new E2ETestCase("T2-F09-05", 2, "F09", "Post-collapse recovery rebuilds food first then civics cleanly", M2Status.FailUntilM3, T2_F09_05),

                // F10 (6)
                new E2ETestCase("T2-F10-01", 2, "F10", "Arid desert oasis builds zero dry sewers vs river positive control", M2Status.FailUntilM4, T2_F10_01),
                new E2ETestCase("T2-F10-02", 2, "F10", "SanitationDevelopmentMinPopulation=1200 gate with N=2,200 positive control", M2Status.FailUntilM4, T2_F10_02),
                new E2ETestCase("T2-F10-03", 2, "F10", "Non-retroactive same-turn SanitationCapital activation (Turn 1 M_d==1, Turn 2 M_d<0.6)", M2Status.FailUntilM4, T2_F10_03),
                new E2ETestCase("T2-F10-04", 2, "F10", "Agriculture == null still reserves human drinking before sanitation on oasis vs river", M2Status.FailUntilM4, T2_F10_04),
                new E2ETestCase("T2-F10-05", 2, "F10", "Two competing cities sharing one constrained river tile split proportionally with conservation", M2Status.FailUntilM4, T2_F10_05),
                new E2ETestCase("T2-F10-06", 2, "F10", "Hard M_d >= 0.15 floor & stale SanitationWaterRatio / LegacyQoLNetGrowth reset", M2Status.FailUntilM4, T2_F10_06),

                // F11 (6)
                new E2ETestCase("T2-F11-01", 2, "F11", "Legacy XML migration (stripping all civic elements loads cleanly with Civic == null)", M2Status.PassOnM2, T2_F11_01),
                new E2ETestCase("T2-F11-02", 2, "F11", "Corrupt / out-of-range civic XML payloads rejected by SaveCodec.Load", M2Status.PassOnM2, T2_F11_02),
                new E2ETestCase("T2-F11-03", 2, "F11", "Old Schema != 23 JSON rejected by ObservationContract.Deserialize", M2Status.PassOnM2, T2_F11_03),
                new E2ETestCase("T2-F11-04", 2, "F11", "BuildForValidation parity with full AgentObservationBuilder.Build", M2Status.PassOnM2, T2_F11_04),
                new E2ETestCase("T2-F11-05", 2, "F11", "Fog-of-war zero hidden tile leakage in civic observations", M2Status.PassOnM2, T2_F11_05),
                new E2ETestCase("T2-F11-06", 2, "F11", "Bound audit corner stress across extreme (C, P) & policies with active mitigation", M2Status.FailUntilM3, T2_F11_06),

                // F12 (5)
                new E2ETestCase("T2-F12-01", 2, "F12", "HeuristicTribePolicy deadband hysteresis (no flip-flopping)", M2Status.PassOnM2, T2_F12_01),
                new E2ETestCase("T2-F12-02", 2, "F12", "AI Elder reverts Open to Neutral on nutrition drop or crowding spike", M2Status.PassOnM2, T2_F12_02),
                new E2ETestCase("T2-F12-03", 2, "F12", "AI Elder ignores dead (Population==0) or rival settlements", M2Status.PassOnM2, T2_F12_03),
                new E2ETestCase("T2-F12-04", 2, "F12", "AI Elder emits zero redundant same-policy orders", M2Status.PassOnM2, T2_F12_04),
                new E2ETestCase("T2-F12-05", 2, "F12", "All AI-emitted civic orders pass OrderRules.Validate & LegalActions.Allows", M2Status.PassOnM2, T2_F12_05)
            };
        }

        // ==================== F01 ====================

        private static void T2_F01_01()
        {
            void ExpectBad(Action<CivicGovernanceSettings> mutate, string label)
            {
                var s = new CivicGovernanceSettings { Enabled = true, SanitationEnabled = true };
                mutate(s);
                Assert.ExpectThrows<ArgumentException>(() => s.Validate(), "Validate must reject: " + label);
            }

            ExpectBad(s => s.ComplexityRelaxationRate = double.NaN, "NaN ComplexityRelaxationRate");
            ExpectBad(s => s.StratificationRelaxationRate = double.PositiveInfinity, "+Inf StratificationRelaxationRate");
            ExpectBad(s => s.MinCrowdingMitigation = 0.0, "MinCrowdingMitigation <= 0");
            ExpectBad(s => s.MinDiseaseMitigation = -0.05, "MinDiseaseMitigation <= 0");
            ExpectBad(s => { s.Tier1ComplexityCeiling = 0.75; s.Tier2ComplexityCeiling = 0.75; }, "Tier1 >= Tier2");
            ExpectBad(s => { s.Tier2ComplexityCeiling = 1.00; s.Tier3ComplexityCeiling = 1.00; }, "Tier2 >= Tier3");
            ExpectBad(s => { s.EgalitarianStewardRate = 0.12; s.StratifiedStewardRate = 0.10; }, "StratifiedStewardRate < EgalitarianStewardRate");
            ExpectBad(s => { s.QoLEqualityLinear = 0.10; s.QoLEqualityQuadratic = 0.06; }, "q1 <= 2*q2 with q1 > 0");
            ExpectBad(s => { s.QoLEqualityLinear = 0.0; s.QoLEqualityQuadratic = 0.05; }, "q1 == 0 with q2 > 0");
            ExpectBad(s => s.DevelopmentMinPopulation = 0, "DevelopmentMinPopulation < 1");
            ExpectBad(s => s.SanitationDevelopmentMinPopulation = 0, "SanitationDevelopmentMinPopulation < 1");
            ExpectBad(s => s.SanitationWaterPerPersonYear = 0.0, "SanitationWaterPerPersonYear <= 0");
        }

        private static void T2_F01_02()
        {
            var cfg = WorldFixtures.BaseConfig(civicEnabled: false);
            cfg.Civic = new CivicGovernanceSettings { Enabled = false, SanitationEnabled = true };
            cfg.Validate();

            Assert.Check(!CivicGovernance.Enabled(cfg), "Enabled must be false.");
            Assert.Check(!CivicGovernance.SanitationEnabled(cfg), "SanitationEnabled must be false when Enabled=false.");

            var w = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: false, fission: false, immigration: false);
            w.Config.Civic = new CivicGovernanceSettings { Enabled = false, SanitationEnabled = true };
            var rec = WorldFixtures.StepTurn(w);
            Assert.Check(rec.CivicReports == null && rec.SanitationWater == null, "Inert config must emit no civic/sanitation reports.");
            Assert.NearAbs(w.Camps[0].CivicComplexity, 0.0, 1e-15, "Inert config keeps C == 0.");
        }

        private static void T2_F01_03()
        {
            foreach (string file in new[] { "OpeningConfig.json", "AgriculturalTechnologyOpeningConfig.json" })
            {
                string raw = ConfigLoader.ReadProfileText(file);
                using (var doc = JsonDocument.Parse(raw))
                {
                    Assert.Check(!doc.RootElement.TryGetProperty("Civic", out _), $"{file} must NOT contain a Civic block.");
                }
                Assert.Check(!Regex.IsMatch(raw, "\"Civic\"\\s*:\\s*\\{"), $"Unity JsonUtility regex guard must return false on {file}.");
                var cfg = ConfigLoader.LoadProfile(file);
                Assert.Check(cfg.Civic == null, $"{file} deserialized SimulationConfig.Civic must be null.");
            }
        }

        private static void T2_F01_04()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 900, civicEnabled: false, fission: false, immigration: false);
            var resolver = new TurnResolver();
            for (int t = 0; t < 3; t++)
                WorldFixtures.StepTurn(w, resolver);

            var loaded = SaveCodec.Load(SaveCodec.Save(w));
            Assert.Check(loaded.Config.Civic == null, "Saved legacy world loads with Civic == null.");
            loaded.Config.Civic = new CivicGovernanceSettings { Enabled = true, SanitationEnabled = true };
            loaded.Config.Validate();

            for (int t = 0; t < 3; t++)
            {
                var rec = WorldFixtures.StepTurn(loaded, resolver);
                Assert.Check(rec.CivicReports != null && rec.CivicReports.Count == 1, $"Post-migration turn {t + 1} emits CivicReports.");
            }
            var c = loaded.Camps[0];
            Assert.Check(Assert.IsFinite(c.CivicComplexity) && Assert.IsFinite(c.CivicStratification), "Migrated (C, P) must be finite.");
            Assert.Check(c.CivicStratification < 0.0, $"Migrated plain village should begin drifting Egalitarian (P={c.CivicStratification:F4}).");
        }

        private static void T2_F01_05()
        {
            var cfg = WorldFixtures.BaseConfig(civicEnabled: true);
            cfg.Civic.QoLEqualityLinear = 0.0;
            cfg.Civic.QoLEqualityQuadratic = 0.0;
            cfg.Validate();

            foreach (double p in new[] { -1.0, -0.5, 0.0, 0.5, 1.0 })
            {
                double q = CivicGovernance.QoLEqualityMultiplierFor(cfg, 1.0, p, 1.0);
                Assert.NearAbs(q, 1.0, 1e-15, $"Zero-ablated QoL multiplier must equal exact 1.0 at P={p}.");
            }
        }

        // ==================== F02 ====================

        private static void T2_F02_01()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false, withResources: false);
            Assert.Check(w.Resources == null, "Fixture must have Resources == null.");
            var env = CivicGovernance.EvaluateEnvironment(w, w.Camps[0]);
            Assert.Check(Assert.IsFinite(env.D3) && env.D3 >= 0.0 && env.D3 <= 1.0, "Finite D3 when Resources == null.");
            Assert.Check(!CivicGovernance.HasLocalClay(w, w.Camps[0]), "HasLocalClay must safely return false when Resources == null.");
            CivicGovernance.Close(w, new TurnRecord(), 20.0);
            Assert.Check(Assert.IsFinite(w.Camps[0].LastEnvironmentalStratification), "Close completes cleanly when Resources == null.");
        }

        private static void T2_F02_02()
        {
            var w0 = WorldFixtures.CreateValleyWorld(population: 1200, civicEnabled: true, fission: false, immigration: false, trade: false);
            var w5 = WorldFixtures.CloneWorld(w0);
            w5.Camps[0].Player = 5;
            w5.Camps[0].People.Player = 5;
            w5.PlayerCount = 6;
            w5.Config.Players = 6;
            Ownership.Bind(w5);

            var e0 = CivicGovernance.EvaluateEnvironment(w0, w0.Camps[0]);
            var e5 = CivicGovernance.EvaluateEnvironment(w5, w5.Camps[0]);
            Assert.NearAbs(e0.D1, e5.D1, 1e-15, "D1 player-ID invariant");
            Assert.NearAbs(e0.D2, e5.D2, 1e-15, "D2 player-ID invariant");
            Assert.NearAbs(e0.D3, e5.D3, 1e-15, "D3 player-ID invariant");
            Assert.NearAbs(e0.D4, e5.D4, 1e-15, "D4 player-ID invariant");
            Assert.NearAbs(e0.EnvironmentalComplexity, e5.EnvironmentalComplexity, 1e-15, "C_env player-ID invariant");
            Assert.NearAbs(e0.EnvironmentalStratification, e5.EnvironmentalStratification, 1e-15, "P_env player-ID invariant");

            CivicGovernance.Close(w0, new TurnRecord(), 20.0);
            CivicGovernance.Close(w5, new TurnRecord(), 20.0);
            Assert.NearAbs(w0.Camps[0].CivicComplexity, w5.Camps[0].CivicComplexity, 1e-15, "Post-Close C player-ID invariant");
            Assert.NearAbs(w0.Camps[0].CivicStratification, w5.Camps[0].CivicStratification, 1e-15, "Post-Close P player-ID invariant");
        }

        private static void T2_F02_03()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 600, civicEnabled: true, withResources: true);
            var c = w.Camps[0];
            foreach (int t in w.Map.Radius(c.Tile, 1))
            {
                w.Tiles[t].Environment = EnvironmentKind.Ocean;
                w.Tiles[t].WaterBody = WaterKind.Saltwater;
                w.Tiles[t].FreshwaterSource = FreshwaterSource.None;
                w.Tiles[t].Freshwater = 0.0;
                w.Tiles[t].ElevationLevel = 10;
            }
            var env = CivicGovernance.EvaluateEnvironment(w, c);
            Assert.Check(Assert.IsFinite(env.D1) && Assert.IsFinite(env.D2) && Assert.IsFinite(env.D3) && Assert.IsFinite(env.D4),
                "Degenerate all-water ring must produce finite drivers.");
            Assert.Check(env.EnvironmentalStratification >= -1.0 && env.EnvironmentalStratification <= 1.0,
                "Degenerate P_env must stay clamped in [-1, +1].");
            Assert.Check(env.EnvironmentalComplexity >= 0.0 && env.EnvironmentalComplexity <= 1.0,
                "Degenerate C_env must stay clamped in [0, 1].");
        }

        private static void T2_F02_04()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.75;
            c.CivicCapital = 0.0;
            var eZero = CivicGovernance.EvaluateEnvironment(w, c);

            c.CivicCapital = 500.0;
            WorldFixtures.AssignExactStewards(c, 150);
            Assert.NearAbs(CivicGovernance.CivicCoverage(w, c), 1.0, 1e-12, "Full coverage setup.");
            var eFull = CivicGovernance.EvaluateEnvironment(w, c);

            Assert.NearAbs(eZero.RawComplexity, eFull.RawComplexity, 1e-15,
                "Anti-oscillation invariant: RawComplexity must use unboosted N_{s,0} and remain bit-identical across CivicCoverage=0 vs 1.");
        }

        private static void T2_F02_05()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 100, civicEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.40;
            var env = CivicGovernance.EvaluateEnvironment(w, c);
            Assert.NearAbs(env.RawComplexity, 0.0, 1e-15, "Below N=150, RawComplexity must be exact 0.");

            CivicGovernance.Close(w, new TurnRecord(), 20.0);
            Assert.NearAbs(c.LastEnvironmentalComplexity, 0.0, 1e-15, "LastEnvironmentalComplexity must be exact 0 below N=150.");
            Assert.Check(c.CivicComplexity < 0.40, $"Pre-existing C=0.40 must relax downward below N=150 (got {c.CivicComplexity:F4}).");
        }

        // ==================== F03 ====================

        private static void T2_F03_01()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 800, civicEnabled: false);
            var c = w.Camps[0];
            var obs = new AgentObservationBuilder().Build(w, c.Player);
            var masks = LegalActions.Build(obs);
            var campMask = masks.Single(m => m.Entity == c.Id);

            Assert.Check(!campMask.Actions.Any(a => a.Kind == OrderKind.ImmigrationPolicy || a.Kind == OrderKind.CivicFocus),
                "Disabled civics must omit ImmigrationPolicy and CivicFocus from LegalActions.");

            var imm = new Order(OrderKind.ImmigrationPolicy, c.Id, (int)ImmigrationPolicy.Open);
            var foc = new Order(OrderKind.CivicFocus, c.Id, (int)CivicFocus.Communal);
            Assert.Check(OrderRules.Validate(obs, imm) != null, "OrderRules must reject ImmigrationPolicy when civics disabled.");
            Assert.Check(OrderRules.Validate(obs, foc) != null, "OrderRules must reject CivicFocus when civics disabled.");

            var orders = new Dictionary<int, List<Order>> { [c.Player] = new List<Order> { imm, foc } };
            WorldFixtures.StepTurn(w, orders: orders);
            Assert.Check(c.ImmigrationPolicy == ImmigrationPolicy.Neutral && c.CivicFocus == CivicFocus.Auto,
                "Rejected policy orders must not mutate camp state.");
        }

        private static void T2_F03_02()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 800, civicEnabled: true);
            var c = w.Camps[0];
            var obs = new AgentObservationBuilder().Build(w, c.Player);

            var badOrders = new[]
            {
                new Order(OrderKind.ImmigrationPolicy, c.Id, -1),
                new Order(OrderKind.ImmigrationPolicy, c.Id, 3),
                new Order(OrderKind.ImmigrationPolicy, c.Id, 1, destination: 5),
                new Order(OrderKind.ImmigrationPolicy, c.Id, 1, population: 10),
                new Order(OrderKind.CivicFocus, c.Id, -1),
                new Order(OrderKind.CivicFocus, c.Id, 3),
                new Order(OrderKind.CivicFocus, c.Id, 1, destination: 2),
                new Order(OrderKind.CivicFocus, c.Id, 1, population: 5),
                new Order(OrderKind.CivicFocus, 999999, 1)
            };

            foreach (var bad in badOrders)
            {
                string err = OrderRules.Validate(obs, bad);
                Assert.Check(err != null, $"OrderRules must reject invalid policy order {bad}.");
            }
        }

        private static void T2_F03_03()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 800, civicEnabled: true, fission: false, immigration: false);
            var c = w.Camps[0];
            Assert.Check(c.CivicFocus == CivicFocus.Auto && c.ImmigrationPolicy == ImmigrationPolicy.Neutral,
                "Precondition: camp starts with Auto/Neutral policies.");

            // Case 1: Both policy kinds duplicated on the same camp
            var orders = new Dictionary<int, List<Order>>
            {
                [c.Player] = new List<Order>
                {
                    new Order(OrderKind.CivicFocus, c.Id, (int)CivicFocus.Communal),
                    new Order(OrderKind.CivicFocus, c.Id, (int)CivicFocus.Centralized),
                    new Order(OrderKind.ImmigrationPolicy, c.Id, (int)ImmigrationPolicy.Open),
                    new Order(OrderKind.ImmigrationPolicy, c.Id, (int)ImmigrationPolicy.Restricted)
                }
            };
            var rec = WorldFixtures.StepTurn(w, orders: orders);
            Assert.Check(rec.Events.Any(e => e.Contains("Rejected conflicting orders for " + c.Id, StringComparison.Ordinal)),
                $"Duplicate same-kind policy orders must log a conflict rejection event for camp {c.Id} (events: {string.Join(" | ", rec.Events)}).");
            Assert.Check(!rec.Orders.Any(o => o.Contains("CivicFocus:" + c.Id + ":", StringComparison.Ordinal)
                || o.Contains("ImmigrationPolicy:" + c.Id + ":", StringComparison.Ordinal)),
                $"Conflicting duplicate policy orders must produce zero accepted policy order receipts (orders: {string.Join(" | ", rec.Orders)}).");
            Assert.Check(c.CivicFocus == CivicFocus.Auto && c.ImmigrationPolicy == ImmigrationPolicy.Neutral,
                $"Rejected duplicate policy orders must leave camp policies unchanged (focus={c.CivicFocus}, imm={c.ImmigrationPolicy}).");

            // Case 2: Single duplicated policy kind alongside a valid other policy kind still rejects the conflicting group cleanly
            var orders2 = new Dictionary<int, List<Order>>
            {
                [c.Player] = new List<Order>
                {
                    new Order(OrderKind.CivicFocus, c.Id, (int)CivicFocus.Communal),
                    new Order(OrderKind.CivicFocus, c.Id, (int)CivicFocus.Centralized),
                    new Order(OrderKind.ImmigrationPolicy, c.Id, (int)ImmigrationPolicy.Open)
                }
            };
            var rec2 = WorldFixtures.StepTurn(w, orders: orders2);
            Assert.Check(rec2.Events.Any(e => e.Contains("Rejected conflicting orders for " + c.Id, StringComparison.Ordinal)),
                $"Single duplicated policy kind must still trigger conflict rejection for camp {c.Id}.");
            Assert.Check(c.CivicFocus == CivicFocus.Auto && c.ImmigrationPolicy == ImmigrationPolicy.Neutral,
                $"Conflict rejection must leave both CivicFocus ({c.CivicFocus}) and ImmigrationPolicy ({c.ImmigrationPolicy}) unchanged.");
        }

        private static void T2_F03_04()
        {
            var w = WorldFixtures.CreateValleyWorld(population: 1000, civicEnabled: true, fission: false, immigration: false);
            var cfg = w.Config;
            double high = CivicGovernance.TargetStratification(cfg, 0.90, FissionPolicy.Discourage, ImmigrationPolicy.Restricted, CivicFocus.Centralized);
            double low = CivicGovernance.TargetStratification(cfg, -0.90, FissionPolicy.Encourage, ImmigrationPolicy.Open, CivicFocus.Communal);
            Assert.NearAbs(high, 1.0, 1e-15, "TargetStratification must clamp strictly at +1.0.");
            Assert.NearAbs(low, -1.0, 1e-15, "TargetStratification must clamp strictly at -1.0.");

            var c = w.Camps[0];
            c.CivicStratification = 0.95;
            c.FissionPolicy = FissionPolicy.Discourage;
            c.ImmigrationPolicy = ImmigrationPolicy.Restricted;
            c.CivicFocus = CivicFocus.Centralized;
            CivicGovernance.Close(w, new TurnRecord(), 200.0);
            Assert.Check(c.CivicStratification >= -1.0 && c.CivicStratification <= 1.0,
                $"Post-Close P must stay in [-1, +1] (got {c.CivicStratification:F6}).");
        }

        private static void T2_F03_05()
        {
            // Part A: Pre-civic C=0 keeps exact custom FissionGainThreshold=0.01 (NOT clamped up to 0.02!) and ExitMismatch == 0
            WorldState BuildZero(double threshold)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 790, civicEnabled: true, fission: true, immigration: false, width: 22, height: 16,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                w.Config.LandBiomassMultiplier = 63.0;
                w.Config.FissionGainThreshold = threshold;
                var c = w.Camps[0];
                foreach (int id in CultivationEconomy.Footprint(w, c))
                {
                    FoodEcology.ConfigurePotential(w.Tiles[id], w.Config, initializeStock: true);
                    w.Tiles[id].Food = w.Tiles[id].Seasons[0].MaxFood;
                }
                c.CivicComplexity = 0.0;
                c.CivicStratification = 1.0;
                c.LastEnvironmentalStratification = -1.0;
                c.LastCircumscription = 0.0;
                c.PlantFood = 790 * PhysicalDemography.FoodPerPersonYear(w.Config) * 20.0;
                w.FixedHousing.Clear();
                NeolithicProduction.FixedHousingChanged(w);
                NeolithicProduction.AddFixedHousing(w, c, 790 * 0.50 / w.Config.Neolithic.HousingPeoplePerUnit, w.Days);
                return w;
            }

            var wZero01 = BuildZero(0.01);
            var wZero02 = BuildZero(0.02);
            Assert.NearAbs(CivicGovernance.ExitMismatch(wZero01.Camps[0]), 0.0, 1e-15, "Pre-civic C==0 must have exact ExitMismatch == 0.");

            var recZero01 = new TurnRecord { Turn = wZero01.Turn, StartDays = wZero01.Days, Duration = wZero01.Config.TurnDays };
            Fission.Resolve(wZero01, recZero01, new PhaseExecution(), new ResolutionMetrics());
            var recZero02 = new TurnRecord { Turn = wZero02.Turn, StartDays = wZero02.Days, Duration = wZero02.Config.TurnDays };
            Fission.Resolve(wZero02, recZero02, new PhaseExecution(), new ResolutionMetrics());

            Assert.Check(recZero01.Fissions.Count > 0 && recZero02.Fissions.Count == 0,
                $"Pre-civic C=0 camp with mover gain ~0.014 in (0.01, 0.02) must split at 0.01 ({recZero01.Fissions.Count}) and hold at 0.02 ({recZero02.Fissions.Count}), proving 0.01 is NOT clamped to 0.02.");

            // Part B: Paired positive control — live C=0.80 Stratified resistance suppresses fission vs matched C=0 baseline!
            WorldState BuildMatchedControl(double complexity)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, fission: true, immigration: false, width: 22, height: 16,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                w.Config.LandBiomassMultiplier = 63.0;
                w.Config.FissionGainThreshold = 0.08;
                var c = w.Camps[0];
                foreach (int id in CultivationEconomy.Footprint(w, c))
                {
                    FoodEcology.ConfigurePotential(w.Tiles[id], w.Config, initializeStock: true);
                    w.Tiles[id].Food = w.Tiles[id].Seasons[0].MaxFood;
                }
                c.CivicComplexity = complexity;
                c.CivicStratification = 0.90;
                c.LastEnvironmentalStratification = 0.90;
                c.CivicCapital = complexity > 0.0 ? CivicGovernance.RequiredCivicCapital(w, c) * 1.2 : 0.0;
                if (complexity > 0.0)
                    WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)) + 10);
                c.PlantFood = 1200 * PhysicalDemography.FoodPerPersonYear(w.Config) * 20.0;
                w.FixedHousing.Clear();
                NeolithicProduction.FixedHousingChanged(w);
                NeolithicProduction.AddFixedHousing(w, c, 1200 * 0.30 / w.Config.Neolithic.HousingPeoplePerUnit, w.Days);
                return w;
            }

            var wBase = BuildMatchedControl(0.0);
            var wPos = BuildMatchedControl(0.80);
            Assert.Check(PhysicalDemography.CrowdingMitigation(wPos, wPos.Camps[0]) < 0.25,
                $"Positive control at C=0.80 must have live M3 CrowdingMitigation < 0.25 (got {PhysicalDemography.CrowdingMitigation(wPos, wPos.Camps[0]):F4}).");

            var recBase = new TurnRecord { Turn = wBase.Turn, StartDays = wBase.Days, Duration = wBase.Config.TurnDays };
            Fission.Resolve(wBase, recBase, new PhaseExecution(), new ResolutionMetrics());
            var recPos = new TurnRecord { Turn = wPos.Turn, StartDays = wPos.Days, Duration = wPos.Config.TurnDays };
            Fission.Resolve(wPos, recPos, new PhaseExecution(), new ResolutionMetrics());

            Assert.Check(recBase.Fissions.Count > 0, $"Matched C=0 control must split under stress at FissionGainThreshold=0.08 (got {recBase.Fissions.Count}).");
            Assert.Check(recPos.Fissions.Count == 0, $"Aligned Stratified C=0.80 camp must resist fission under identical stress (got {recPos.Fissions.Count}).");
        }

        private static void T2_F03_06()
        {
            WorldState BuildCoercedOrAligned(double pEnv, double circumscription, double foodYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1800, civicEnabled: true, fission: true, immigration: false, width: 22, height: 16,
                    landBiomassMultiplier: 800.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                var c = w.Camps[0];
                c.CivicComplexity = 0.90;
                c.CivicStratification = 0.95;
                c.LastEnvironmentalStratification = pEnv;
                c.LastCircumscription = circumscription;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c) * 1.2;
                WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)) + 15);
                c.PlantFood = 1800 * PhysicalDemography.FoodPerPersonYear(w.Config) * foodYears;
                c.Inventory.LightHousingUnits = 600.0;
                WorldFixtures.SlashHomeFootprint(w, c, 0.02f);
                return w;
            }

            var wCoercedCheck = BuildCoercedOrAligned(-0.50, 0.15, 10.0);
            Assert.Check(CivicGovernance.ExitMismatch(wCoercedCheck.Camps[0]) > 1.0,
                $"Coerced open-frontier ExitMismatch must be > 1.0 (got {CivicGovernance.ExitMismatch(wCoercedCheck.Camps[0]):F4}).");
            Assert.Check(PhysicalDemography.CrowdingMitigation(wCoercedCheck, wCoercedCheck.Camps[0]) < 0.25,
                "Positive control: live M3 CrowdingMitigation must be active.");

            // Sweep from mild stress (high foodYears) to severe stress (0 foodYears): higher split foodYears = splits at lower stress.
            // Coerced (theta_eff ~ 0.085) and Aligned (theta_eff ~ 0.152) have identical (N, C, P, M_c, N_s, Q_civic) and differ ONLY in ExitMismatch!
            double coercedSplitFoodYears = -1.0;
            double alignedSplitFoodYears = -1.0;
            foreach (double fy in new[] { 16.0, 15.0, 14.0, 13.0, 12.0, 11.0, 10.0, 9.0, 8.0, 7.0, 6.0, 5.0, 4.0, 3.0, 2.0, 1.0, 0.0 })
            {
                if (coercedSplitFoodYears < 0.0)
                {
                    var wC = BuildCoercedOrAligned(-0.50, 0.15, fy);
                    var rC = new TurnRecord { Turn = wC.Turn, StartDays = wC.Days, Duration = wC.Config.TurnDays };
                    Fission.Resolve(wC, rC, new PhaseExecution(), new ResolutionMetrics());
                    if (rC.Fissions.Count > 0) coercedSplitFoodYears = fy;
                }
                if (alignedSplitFoodYears < 0.0)
                {
                    var wA = BuildCoercedOrAligned(0.95, 0.85, fy);
                    var rA = new TurnRecord { Turn = wA.Turn, StartDays = wA.Days, Duration = wA.Config.TurnDays };
                    Fission.Resolve(wA, rA, new PhaseExecution(), new ResolutionMetrics());
                    if (rA.Fissions.Count > 0) alignedSplitFoodYears = fy;
                }
            }

            Assert.Check(coercedSplitFoodYears >= 0.0, "Coerced camp must split on open frontier as home food stress intensifies.");
            Assert.Check(coercedSplitFoodYears > alignedSplitFoodYears,
                $"Open-frontier alignment mismatch exit friction must trigger fission at strictly lower stress (higher foodYears={coercedSplitFoodYears:F1}) than aligned Stratified ({alignedSplitFoodYears:F1}).");
        }

        // ==================== F04 ====================

        private static void T2_F04_01()
        {
            double c20 = CivicGovernance.Relax(0.15, 0.72, 0.025, 20.0);
            double p20 = CivicGovernance.Relax(-0.40, 0.65, 0.020, 20.0);
            double t20 = CivicGovernance.Relax(25.0, 180.0, 0.025, 20.0);

            double cd = 0.15, pd = -0.40, td = 25.0;
            double dtDay = 20.0 / 7300.0;
            for (int i = 0; i < 7300; i++)
            {
                cd = CivicGovernance.Relax(cd, 0.72, 0.025, dtDay);
                pd = CivicGovernance.Relax(pd, 0.65, 0.020, dtDay);
                td = CivicGovernance.Relax(td, 180.0, 0.025, dtDay);
            }
            Assert.NearRelative(cd, c20, 1e-11, "7300x1d vs 1x20y Relax(C)");
            Assert.NearRelative(pd, p20, 1e-11, "7300x1d vs 1x20y Relax(P)");
            Assert.NearRelative(td, t20, 1e-11, "7300x1d vs 1x20y Relax(Trade)");

            // Permanence ramp integral 1x40y vs 14600x1d (pure analytic check via Close with 100 steps vs 2x20y)
            double RunRamp(int steps, double stepYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true);
                var c = w.Camps[0];
                c.StationaryYears = 0.0;
                c.StationaryTurns = 1;
                c.People.CivicPrecedents.Clear();
                c.People.CivicExperience.Clear();
                for (int i = 0; i < steps; i++)
                {
                    c.StationaryYears += stepYears;
                    c.StationaryTurns++;
                    CivicGovernance.Close(w, new TurnRecord(), stepYears);
                }
                return c.People.CivicExperience[CivicPrecedents.CeremonialIntegration];
            }
            double x2 = RunRamp(2, 20.0);
            double x400 = RunRamp(400, 0.1);
            Assert.NearRelative(x400, x2, 1e-10, "Permanence ramp integral 400x0.1y == 2x20y");
        }

        private static void T2_F04_02()
        {
            Assert.NearAbs(CivicGovernance.Relax(0.42, 0.90, 0.025, 0.0), 0.42, 1e-15, "Relax(years=0) returns current.");
            Assert.NearAbs(CivicGovernance.Relax(0.42, 0.90, 0.025, -5.0), 0.42, 1e-15, "Relax(years<0) returns current.");
            Assert.NearAbs(CivicGovernance.Relax(0.42, 0.90, 0.025, double.NaN), 0.42, 1e-15, "Relax(NaN) returns current.");
            Assert.NearAbs(CivicGovernance.Relax(0.42, 0.90, 0.025, double.PositiveInfinity), 0.42, 1e-15, "Relax(+Inf) returns current.");

            double sub = CivicGovernance.Relax(0.0, double.MaxValue, 1e-160, 1e-160);
            Assert.Check(Assert.IsFinite(sub) && sub > 0.0, $"Tiny rate*years < 1e-290 must avoid underflow/NaN (got {sub:R}).");
            double tiny = CivicGovernance.Relax(0.50, 0.90, 0.025, 1e-305);
            Assert.Check(Assert.IsFinite(tiny) && tiny >= 0.50 && tiny <= 0.90, "Relax(years=1e-305) stays finite and bounded.");
        }

        private static void T2_F04_03()
        {
            var wMobile = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false);
            WorldFixtures.SetFixedStepYears(wMobile, 1.0);
            var cMob = wMobile.Camps[0];
            cMob.StationaryTurns = 0;
            cMob.StationaryYears = 0.0;
            cMob.IsVillage = false;
            cMob.People.CivicPrecedents.Clear();
            cMob.People.CivicExperience.Clear();

            var wStat = WorldFixtures.CloneWorld(wMobile);
            var cStat = wStat.Camps[0];
            cStat.IsVillage = true;

            int home = cMob.Tile;
            int adj = wMobile.Map.Neighbors(home).First();

            var rMob = new TurnResolver();
            var rStat = new TurnResolver();
            for (int t = 0; t < 4; t++)
            {
                int dest = (cMob.Tile == home) ? adj : home;
                var orders = new Dictionary<int, List<Order>>
                {
                    [cMob.Player] = new List<Order> { new Order(OrderKind.Move, cMob.Id, destination: dest) }
                };
                WorldFixtures.StepTurn(wMobile, rMob, orders);
                WorldFixtures.StepTurn(wStat, rStat);
            }

            double expMob = cMob.People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double vm) ? vm : 0.0;
            double expStat = cStat.People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double vs) ? vs : 0.0;
            Assert.Check(cMob.StationaryTurns == 0, $"Mobile camp moving every turn must have StationaryTurns == 0 (got {cMob.StationaryTurns}).");
            Assert.NearAbs(expMob, 0.0, 1e-15, "Mobile camp moving every 1-yr turn must accrue exact 0 Tier-1 practice.");
            Assert.Check(expStat > 0.0, $"Stationary positive control must accrue > 0 Tier-1 practice (got {expStat:F4}).");
        }

        private static void T2_F04_04()
        {
            (double EndCap, double ReqNow) RunOverCap(double stepYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1400, civicEnabled: true, fission: false, immigration: false,
                    landBiomassMultiplier: 600.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                WorldFixtures.SetFixedStepYears(w, stepYears);
                var c = w.Camps[0];
                c.CivicComplexity = 0.70;
                c.LastEnvironmentalComplexity = 0.70;
                c.CivicStratification = 0.0;
                c.LastEnvironmentalStratification = 0.0;
                double reqNow = CivicGovernance.RequiredCivicCapital(w, c);
                c.CivicCapital = reqNow * 2.0; // excess ruins from prior N=2,800 peak!
                c.PlantFood = 1400 * PhysicalDemography.FoodPerPersonYear(w.Config) * 25.0;
                c.Inventory.HousingMaterial = 4000.0;
                c.Inventory.Tools = 4000.0;

                var rec = WorldFixtures.StepTurn(w);
                double maint = rec.Production.Where(ProductionReceiptHelpers.IsCivicMaintenance).Sum(r => r.Output);
                double maxNeeded = w.Config.Civic.CivicCapitalDecayPerYear * reqNow * stepYears * 1.15;
                Assert.Check(maint > 0.0, $"Over-capitalized camp (dt={stepYears}y) must still maintain active ReqCap (maint={maint:F3}).");
                Assert.Check(maint <= maxNeeded,
                    $"Over-capitalized camp (dt={stepYears}y) must NOT maintain excess ruins beyond ReqCap (maint={maint:F3} > maxNeeded={maxNeeded:F3}).");
                return (c.CivicCapital, reqNow);
            }

            var r1 = RunOverCap(1.0);
            var r20 = RunOverCap(20.0);
            Assert.Check(r1.EndCap < r1.ReqNow * 2.0 && r1.EndCap >= r1.ReqNow - 1e-5,
                $"1-yr over-capitalized camp must let excess ruins decay while retaining active ReqCap (end={r1.EndCap:F3}, req={r1.ReqNow:F3}).");
            Assert.Check(r20.EndCap < r20.ReqNow * 2.0 * 0.90 && r20.EndCap >= r20.ReqNow - 1e-5,
                $"20-yr over-capitalized camp must decay unmaintained ruins (< 90% of opening 2*req) while retaining >= ReqCap (end={r20.EndCap:F3}, req={r20.ReqNow:F3}).");
        }

        private static void T2_F04_05()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, smoothCalendar: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            WorldFixtures.ConfigureSmoothCalendarStarted(w, startDays: 0.0);
            var wFull = WorldFixtures.CloneWorld(w);

            var rA = new TurnResolver();
            var rB = new TurnResolver();
            for (int t = 0; t < 2; t++)
            {
                WorldFixtures.StepTurn(w, rA);
                WorldFixtures.StepTurn(wFull, rB);
            }

            var reloaded = SaveCodec.Load(SaveCodec.Save(w));
            SmoothCalendar.RequireCurrentBoundary(reloaded);

            for (int t = 0; t < 2; t++)
            {
                WorldFixtures.StepTurn(reloaded, rA);
                WorldFixtures.StepTurn(wFull, rB);
            }
            Assert.Check(SaveCodec.Fingerprint(reloaded) == SaveCodec.Fingerprint(wFull),
                "SmoothCalendar mid-compression save/load must match uninterrupted run bit-for-bit.");
        }

        // ==================== F05 ====================

        private static void T2_F05_01()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true);
            var c = w.Camps[0];
            c.People.CivicPrecedents.Clear();
            var obs = new AgentObservationBuilder().Build(w, c.Player);
            var so = obs.Settlements.Single(s => s.Id == c.Id);
            Assert.Check(!so.StewardEnabled, "StewardEnabled must be false before CeremonialIntegration.");

            var intent = EconomicIntent.From(c);
            intent.AutomationTurns = 0;
            intent.Assignments = new List<PopulationAssignment>
            {
                new PopulationAssignment { Role = WorkforceRoles.Steward, Population = 5 }
            };
            Assert.Check(OrderRules.Validate(obs, new Order(OrderKind.Economy, c.Id) { Economy = intent }) != null,
                "OrderRules must reject Steward assignment before Tier-1 unlock.");
            Assert.Check(EconomicIntents.Validate(obs, so, intent) != null,
                "EconomicIntents.Validate must reject Steward assignment before Tier-1 unlock.");
        }

        private static void T2_F05_02()
        {
            var cfg = WorldFixtures.BaseConfig(civicEnabled: true);
            Assert.NearAbs(CivicGovernance.RequiredStewards(cfg, 0, 0.75, 0.5, false, 0.5), 0.0, 1e-15, "RequiredStewards(N=0) == 0.");
            Assert.NearAbs(CivicGovernance.RequiredStewards(cfg, 1000, 0.0, 0.5, false, 0.5), 0.0, 1e-15, "RequiredStewards(C=0) == 0.");

            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = -0.3000001 }) == "Council Arbiter", "-0.3000001");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = -0.30 }) == "Council Arbiter", "-0.30 exact");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = -0.2999999 }) == "Civic Steward", "-0.2999999");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = 0.2999999 }) == "Civic Steward", "+0.2999999");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = 0.30 }) == "Temple Scribe", "+0.30 exact");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, new Camp { CivicStratification = 0.3000001 }) == "Temple Scribe", "+0.3000001");
        }

        private static void T2_F05_03()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 100, civicEnabled: true,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];
            var obs = new AgentObservationBuilder().Build(w, c.Player);
            var intent = EconomicIntent.From(c);
            intent.AutomationTurns = 0;
            intent.Assignments = new List<PopulationAssignment>
            {
                new PopulationAssignment { Role = WorkforceRoles.Steward, Population = c.Population + 1 }
            };
            Assert.Check(OrderRules.Validate(obs, new Order(OrderKind.Economy, c.Id) { Economy = intent }) != null,
                "Over-assigning Steward > Population must be rejected by OrderRules.Validate.");
        }

        private static void T2_F05_04()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1100, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];
            var env = CivicGovernance.EvaluateEnvironment(w, c);
            c.LastEnvironmentalComplexity = env.EnvironmentalComplexity;
            c.LastEnvironmentalStratification = env.EnvironmentalStratification;
            c.CivicComplexity = 0.0; // freshly unlocked, before first relaxation
            c.CivicCapital = 0.0;
            c.PlantFood = 1100 * PhysicalDemography.FoodPerPersonYear(w.Config) * 25.0;

            var resolver = new TurnResolver();
            WorldFixtures.StepTurn(w, resolver);
            int turn1Stewards = CultivationEconomy.Workers(c, WorkforceRoles.Steward);
            Assert.Check(c.CivicCapital > 0.0, $"Turn 1 after Tier-1 unlock must build durable CivicCapital > 0 (got {c.CivicCapital:F3}).");
            Assert.Check(turn1Stewards == 0, $"Turn 1 with opening C==0 must staff exact 0 idle Stewards (got {turn1Stewards}).");
            Assert.Check(c.CivicComplexity > 0.0, $"Close on Turn 1 must relax C > 0 (got {c.CivicComplexity:F4}).");

            WorldFixtures.StepTurn(w, resolver);
            int turn2Stewards = CultivationEconomy.Workers(c, WorkforceRoles.Steward);
            Assert.Check(turn2Stewards > 0, $"Turn 2 with C > 0 and CivicCapital > 0 must immediately staff Stewards > 0 (got {turn2Stewards}).");
        }

        private static void T2_F05_05()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.50;
            c.CivicCapital = 150.0;
            c.SanitationCapital = 100.0;
            c.SanitationWaterRatio = 0.90;
            c.EconomyAutomationTurns = 0;
            WorldFixtures.AssignExactStewards(c, 40);

            int dest = w.Map.Neighbors(c.Tile).First();
            var orders = new Dictionary<int, List<Order>>
            {
                [c.Player] = new List<Order> { new Order(OrderKind.Move, c.Id, destination: dest) }
            };
            WorldFixtures.StepTurn(w, orders: orders);

            Assert.Check(c.Tile == dest, "Camp must move to neighboring hex.");
            Assert.NearAbs(c.CivicCapital, 0.0, 1e-15, "Abandoning village via Move must zero CivicCapital.");
            Assert.NearAbs(c.SanitationCapital, 0.0, 1e-15, "Abandoning village via Move must zero SanitationCapital.");
            Assert.NearAbs(c.SanitationWaterRatio, 0.0, 1e-15, "Abandoning village via Move must zero SanitationWaterRatio.");
            Assert.NearAbs(CivicGovernance.CivicCoverage(w, c), 0.0, 1e-15, "Abandoned village has CivicCoverage == 0.");
            Assert.Check(c.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration) && c.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization),
                "Society CivicPrecedents must survive village abandonment.");
        }

        // ==================== F06 ====================

        private static void T2_F06_01()
        {
            WorldState Build(bool hasCapital, bool hasStewards)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1600, civicEnabled: true, fission: false, immigration: false,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                var c = w.Camps[0];
                c.CivicComplexity = 0.75;
                c.CivicStratification = 0.0;
                c.LastEnvironmentalStratification = 0.0;
                c.CivicCapital = hasCapital ? CivicGovernance.RequiredCivicCapital(w, c) : 0.0;
                WorldFixtures.AssignExactStewards(c, hasStewards ? (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)) : 0);
                return w;
            }

            var wCapOnly = Build(true, false);
            var wStewOnly = Build(false, true);
            var wBoth = Build(true, true);

            // Negative arms: zero coverage => unmitigated M_c == 1 and unboosted SocialCohesion
            Assert.NearAbs(CivicGovernance.CivicCoverage(wCapOnly, wCapOnly.Camps[0]), 0.0, 1e-15, "Capital-only has CivicCoverage == 0.");
            Assert.NearAbs(CivicGovernance.CivicCoverage(wStewOnly, wStewOnly.Camps[0]), 0.0, 1e-15, "Steward-only has CivicCoverage == 0.");
            Assert.NearAbs(PhysicalDemography.CrowdingMitigation(wCapOnly, wCapOnly.Camps[0]), 1.0, 1e-15, "Capital-only M_c == 1.0.");
            Assert.NearAbs(PhysicalDemography.CrowdingMitigation(wStewOnly, wStewOnly.Camps[0]), 1.0, 1e-15, "Steward-only M_c == 1.0.");

            // Paired positive control: BOTH inputs active must yield live M_c < 0.25 and live boosted SocialCohesion!
            Assert.NearAbs(CivicGovernance.CivicCoverage(wBoth, wBoth.Camps[0]), 1.0, 1e-12, "Both inputs achieve CivicCoverage == 1.0.");
            Assert.Check(PhysicalDemography.CrowdingMitigation(wBoth, wBoth.Camps[0]) < 0.25,
                $"Positive control must achieve live PhysicalDemography.CrowdingMitigation < 0.25 (got {PhysicalDemography.CrowdingMitigation(wBoth, wBoth.Camps[0]):F4}).");
            double sZero = NeolithicProduction.SocialCohesion(wCapOnly, wCapOnly.Camps[0], 1600);
            double sBoth = NeolithicProduction.SocialCohesion(wBoth, wBoth.Camps[0], 1600);
            Assert.Check(sBoth > sZero + 0.30, $"Positive control must boost live SocialCohesion ({sBoth:F4} vs {sZero:F4}).");
        }

        private static void T2_F06_02()
        {
            var wNonVillage = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var cNV = wNonVillage.Camps[0];
            cNV.IsVillage = false;
            cNV.VillageTurns = 0;
            cNV.StationaryTurns = 0;
            cNV.StationaryYears = 0.0;
            cNV.CivicComplexity = 0.25;
            cNV.LastEnvironmentalComplexity = 0.25;
            cNV.CivicCapital = 0.0;

            var wVillage = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var cV = wVillage.Camps[0];
            cV.IsVillage = true;
            cV.CivicComplexity = 0.25;
            cV.LastEnvironmentalComplexity = 0.25;
            cV.CivicCapital = 0.0;

            WorldFixtures.StepTurn(wNonVillage);
            WorldFixtures.StepTurn(wVillage);

            Assert.NearAbs(cNV.CivicCapital, 0.0, 1e-15, $"Non-village camp must build exact 0 CivicCapital (got {cNV.CivicCapital:F4}).");
            Assert.Check(cV.CivicCapital > 0.0, $"Paired village positive control must build CivicCapital > 0 (got {cV.CivicCapital:F4}).");
        }

        private static void T2_F06_03()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 400, civicEnabled: true, fission: false, immigration: false);
            var c = w.Camps[0];
            // Disable settlement economy on this camp (Paleolithic / no agriculture / no husbandry / no fishing)
            c.Technology = Technology.None;
            for (int i = 0; i < c.Technologies.Length; i++) c.Technologies[i] = false;
            c.People.Progression.Era = Era.Paleolithic;
            c.CivicCapital = 100.0;
            c.CivicStratification = 0.0;
            c.LastEnvironmentalStratification = 0.0;
            Assert.Check(!CultivationEconomy.EconomyEnabled(w, c), "Camp economy must be disabled for this test.");

            WorldFixtures.StepTurn(w);
            double expected = 100.0 * Math.Exp(-w.Config.Civic.CivicCapitalDecayPerYear * 20.0);
            Assert.Check(c.CivicCapital < 95.0, $"CivicCapital must decay even when EconomyEnabled == false (got {c.CivicCapital:F3}).");
            Assert.NearRelative(c.CivicCapital, expected, 1e-9, $"Economy-disabled decay should match analytical ~{expected:F2} (got {c.CivicCapital:F3}).");
        }

        private static void T2_F06_04()
        {
            var w = WorldFixtures.CreateValleyWorld(population: 2500, civicEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
            w.Config.Civic.CrowdingMitigationBase = 1.0;
            w.Config.Validate();

            var c = w.Camps[0];
            c.CivicComplexity = 1.0;
            c.CivicStratification = 1.0;
            c.LastEnvironmentalStratification = 1.0;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
            WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)));

            double mc = PhysicalDemography.CrowdingMitigation(w, c);
            Assert.Check(mc >= w.Config.Civic.MinCrowdingMitigation - 1e-12 && mc < 0.10,
                $"Live PhysicalDemography.CrowdingMitigation must hit hard floor 0.05 (got {mc:F6}).");
            Assert.NearAbs(mc, 0.05, 1e-12, "M_c must clamp at MinCrowdingMitigation = 0.05.");

            var res = PhysicalDemography.AdvanceBudget(w.Config, 1.0, 20.0, 1e8, 1e8, 1e5,
                crowdingMitigation: mc, diseaseMitigation: 0.15, fertilityFactor: 1.0, additionalMortality: 0.0);
            Assert.Check(Assert.IsFinite(res.FinalPopulation) && res.FinalPopulation > 0.0 && res.RootEvaluations <= 128,
                $"Solver must converge cleanly at floor (N_eq={res.EquilibriumPopulation:F1}, rootEvals={res.RootEvaluations}).");
        }

        private static void T2_F06_05()
        {
            var wNoHm = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, fission: false, immigration: false, withResources: false,
                landBiomassMultiplier: 600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var cNo = wNoHm.Camps[0];
            cNo.CivicComplexity = 0.30;
            cNo.LastEnvironmentalComplexity = 0.30;
            cNo.CivicCapital = 0.0;
            cNo.Inventory.HousingMaterial = 0.0;
            cNo.Inventory.Stone = 0.0;
            cNo.Inventory.Clay = 0.0;
            cNo.Inventory.Wood = 0.0;

            var wPos = WorldFixtures.CloneWorld(wNoHm);
            wPos.Camps[0].Inventory.HousingMaterial = 500.0;

            WorldFixtures.StepTurn(wNoHm);
            WorldFixtures.StepTurn(wPos);

            Assert.NearAbs(cNo.CivicCapital, 0.0, 1e-12, $"Zero HousingMaterial/resources must build 0 CivicCapital (got {cNo.CivicCapital:F4}).");
            Assert.Check(wPos.Camps[0].CivicCapital > 0.0, $"Paired positive control with HousingMaterial=500 must build CivicCapital > 0 (got {wPos.Camps[0].CivicCapital:F4}).");
        }

        // ==================== F07 ====================

        private static void T2_F07_01()
        {
            int PlanStewards(int pop, double p, bool has3A)
            {
                var precs = has3A
                    ? new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting }
                    : new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
                var w = WorldFixtures.CreatePlainsWorld(population: pop, civicEnabled: true, fission: false, immigration: false,
                    landBiomassMultiplier: 1000.0, precedents: precs);
                var c = w.Camps[0];
                c.CivicComplexity = 0.75;
                c.LastEnvironmentalComplexity = 0.75;
                c.CivicStratification = p;
                c.LastEnvironmentalStratification = p;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c) * 1.1;
                c.PlantFood = pop * PhysicalDemography.FoodPerPersonYear(w.Config) * 25.0;
                var plan = EconomicAllocationStrategy.Plan(w, c, EconomicIntent.From(c), out _);
                return plan.Assignments.FirstOrDefault(a => a.Role == WorkforceRoles.Steward)?.Population ?? 0;
            }

            int egal3k = PlanStewards(3000, -1.0, false);
            int strat3k = PlanStewards(3000, 1.0, false);
            int egal6500No3A = PlanStewards(6500, -1.0, false);
            int strat6500No3A = PlanStewards(6500, 1.0, false);
            int egal6500With3A = PlanStewards(6500, -1.0, true);
            int strat6500With3A = PlanStewards(6500, 1.0, true);

            Assert.Check(egal3k > 0 && strat3k > 0 && egal6500No3A > 0 && strat6500No3A > 0,
                $"DemandControl must actively staff Stewards across scales (3k: {egal3k}/{strat3k}, 6.5k: {egal6500No3A}/{strat6500No3A}).");
            Assert.Check(egal3k < strat3k, $"At N=3,000 Egalitarian ({egal3k}) must require fewer Stewards than Stratified ({strat3k}).");
            Assert.Check(egal6500No3A > strat6500No3A,
                $"Without Tier 3A at N=6,500 (> knee 3,500), superlinear Egalitarian ({egal6500No3A}) must exceed Stratified ({strat6500No3A}).");
            Assert.Check(egal6500With3A < strat6500With3A && egal6500With3A < egal6500No3A * 0.75,
                $"With Tier 3A at N=6,500, Egalitarian ({egal6500With3A}) must flatten back below Stratified ({strat6500With3A}).");
        }

        private static void T2_F07_02()
        {
            var w = WorldFixtures.CreateValleyWorld(
                population: 10000,
                civicEnabled: true,
                sanitationEnabled: true,
                fission: false,
                immigration: false,
                landBiomassMultiplier: 2400.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting, CivicPrecedents.PlannedQuartersAndDrainage });
            WorldFixtures.SetFixedStepYears(w, 2.0); // keeps effective population ~9,800 on both M3 and M4 so S(10,000) < 1e-12 tie-collapse is genuinely guarded
            w.Config.ElderKernelSplit = true;
            w.Config.ElderSplitObjective = 2;
            var c = w.Camps[0];
            c.CivicComplexity = 1.0;
            c.LastEnvironmentalComplexity = 1.0;
            c.CivicStratification = 0.80;
            c.LastEnvironmentalStratification = 0.80;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
            c.SanitationCapital = CivicGovernance.RequiredSanitationCapital(w.Config, c.Population);
            c.SanitationWaterRatio = 1.0;
            c.Inventory.Cloth = 0.0; // force KernelSplit to actively decide clothing & housing production!
            c.Inventory.HousingMaterial = 12000.0;
            c.Inventory.Tools = 15000.0;
            c.Inventory.FarmTools = 3000.0;
            WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)));

            Assert.Check(NeolithicProduction.SocialCohesion(w, c, 10000) > 0.50,
                $"Live CohesionScale at N=10,000 must be boosted (got S={NeolithicProduction.SocialCohesion(w, c, 10000):E3}).");

            // Opaque-box limit: the Elder's committed split is internal, and the Recovery pass can refill clothing/shelter with
            // idle labour even after a tie-collapse, so the post-step coverage check below is a weak guard. The cohesion
            // precondition above (S(10,000) > 0.5, so BaseQoL cannot underflow to ~1e-14) is the discriminating check here;
            // the internal ElderAction/ProtectedClothingUnits regression lives in Tools/Headless/CivicGovernanceChecks.cs.
            WorldFixtures.StepTurn(w);
            Assert.Check(c.ClothPerCapita > 0.70 && c.HousingPerCapita > 0.70,
                $"KernelSplit at N=10,000 must NOT tie-collapse Protected clothing/shelter (cloth={c.ClothPerCapita:F3}, housing={c.HousingPerCapita:F3}).");
        }

        private static void T2_F07_03()
        {
            WorldState Build(double p)
            {
                var w = WorldFixtures.CreateValleyWorld(population: 1600, civicEnabled: true, fission: false, immigration: false,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                var c = w.Camps[0];
                c.CivicComplexity = 1.0;
                c.CivicStratification = p;
                c.LastEnvironmentalStratification = p;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
                WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)));
                return w;
            }

            var wStrat = Build(1.0);
            var wEgal = Build(-1.0);
            var recipes = NeolithicProduction.Recipes(wStrat.Config);

            foreach (var pubGood in new[] { CraftGood.CanalConstruction, CraftGood.CanalMaintenance })
            {
                var r = recipes.Single(x => x.Good == pubGood);
                double rs = SpecialistProductivity.CraftRate(wStrat, wStrat.Camps[0], r, WorkforceRoles.Builder);
                double re = SpecialistProductivity.CraftRate(wEgal, wEgal.Camps[0], r, WorkforceRoles.Builder);
                Assert.NearRelative(rs / re, 1.35, 1e-6, $"Public works {pubGood} must receive 1.35x Stratified mobilization bonus.");
            }

            foreach (var privGood in new[] { CraftGood.Housing, CraftGood.LightHousing, CraftGood.HousingMaterial })
            {
                var r = recipes.Single(x => x.Good == privGood);
                double rs = SpecialistProductivity.CraftRate(wStrat, wStrat.Camps[0], r, r.Specialist);
                double re = SpecialistProductivity.CraftRate(wEgal, wEgal.Camps[0], r, r.Specialist);
                Assert.NearAbs(rs, re, 1e-15, $"Private good {privGood} must NEVER receive M_build mobilization bonus.");
            }
        }

        private static void T2_F07_04()
        {
            var w = WorldFixtures.CreateValleyWorld(population: 1600, civicEnabled: true, fission: false, immigration: false, withResources: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
            WorldFixtures.SetFixedStepYears(w, 20.0);
            var c = w.Camps[0];
            c.EconomyAutomationTurns = 0; // keep exact opening Stewards through ApplyStorage
            c.CivicComplexity = 1.0;
            c.LastEnvironmentalComplexity = 1.0;
            c.CivicStratification = 1.0;
            c.LastEnvironmentalStratification = 1.0;
            double kOpen = CivicGovernance.RequiredCivicCapital(w, c);
            c.CivicCapital = kOpen;
            WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)) + 10);
            // Zero upkeep materials -> maximum capital decay during turn!
            c.Inventory.HousingMaterial = 0.0;
            c.Inventory.Stone = 0.0;
            c.Inventory.Clay = 0.0;
            c.Inventory.Wood = 0.0;
            c.Inventory.Pots = 0.0;
            c.Inventory.Cloth = 1600 * 4.0;

            int startPop = c.Population;
            double deltaEff = w.Config.Civic.CivicCapitalDecayPerYear * CivicGovernance.AlignmentFriction(w.Config, c.CivicStratification, c.LastEnvironmentalStratification);
            double baseNatWithoutBonus = NeolithicProduction.NaturalStorageCapacity(
                new WorldState { Config = WorldFixtures.BaseConfig(civicEnabled: false), Tiles = w.Tiles }, c, 0, residents: startPop);
            double preTurnNat = NeolithicProduction.NaturalStorageCapacity(w, c, 0, residents: startPop);
            double preTurnDep = NeolithicProduction.StorageCapacityAfterPotDepreciation(w, c, 0, 20.0);

            Assert.Check(preTurnDep > baseNatWithoutBonus * 1.15,
                $"Positive control: M3 StorageCapacityAfterPotDepreciation ({preTurnDep:F1}) must reflect GranaryStorageMultiplier over base ({baseNatWithoutBonus:F1}).");
            Assert.Check(preTurnDep <= preTurnNat + 1e-5,
                $"Pre-turn StorageCapacityAfterPotDepreciation ({preTurnDep:F1}) must not exceed opening NaturalStorageCapacity ({preTurnNat:F1}).");

            // Clone maintained control before zero-upkeep run
            var wMaint = WorldFixtures.CloneWorld(w);
            var cMaint = wMaint.Camps[0];
            cMaint.EconomyAutomationTurns = -1;
            cMaint.Inventory.HousingMaterial = 5000.0;
            cMaint.Inventory.Tools = 5000.0;
            cMaint.PlantFood = preTurnDep * 2.5;

            // Surplus opening food forces ApplyStorage to clamp c.Food == actualApplyCap (zero tautological || disjunct!)
            c.PlantFood = preTurnDep * 2.5;
            var rec = WorldFixtures.StepTurn(w);
            var demo = rec.Demographics.Single(d => d.Camp == c.Id);

            double expectedKLower = kOpen * Math.Exp(-deltaEff * 20.0);
            Assert.Check(c.CivicCapital >= expectedKLower - 1e-9,
                $"Post-turn CivicCapital ({c.CivicCapital:F4}) must satisfy analytical lower bound ({expectedKLower:F4}).");
            Assert.Check(demo.DiscardedInventory > 0.0,
                $"Surplus opening food must force ApplyStorage clamping (DiscardedInventory={demo.DiscardedInventory:F2}).");
            Assert.Check(c.Food >= preTurnDep - 1e-5,
                $"Lower-bound invariant: ApplyStorage clamped capacity c.Food ({c.Food:F1}) must never fall below StorageCapacityAfterPotDepreciation ({preTurnDep:F1}).");

            var recMaint = WorldFixtures.StepTurn(wMaint);
            var demoMaint = recMaint.Demographics.Single(d => d.Camp == cMaint.Id);
            Assert.Check(demoMaint.DiscardedInventory > 0.0 && cMaint.Food >= preTurnDep - 1e-5 && cMaint.Food > c.Food + 1.0,
                $"Maintained capital must retain strictly higher ApplyStorage capacity than unmaintained decay (maint={cMaint.Food:F1}, unmaint={c.Food:F1}, lower={preTurnDep:F1}).");
        }

        private static void T2_F07_05()
        {
            var wZero = WorldFixtures.CreateValleyWorld(population: 1600, civicEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
            var cZero = wZero.Camps[0];
            cZero.CivicComplexity = 1.0;
            cZero.CivicStratification = 1.0;
            cZero.LastEnvironmentalStratification = 1.0;
            cZero.CivicCapital = 0.0; // I_civic == 0!

            var wFull = WorldFixtures.CloneWorld(wZero);
            var cFull = wFull.Camps[0];
            cFull.CivicCapital = CivicGovernance.RequiredCivicCapital(wFull, cFull);
            WorldFixtures.AssignExactStewards(cFull, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wFull, cFull)));

            // Negative arm: I_civic == 0 collapses all modifiers to exact 1.0
            Assert.NearAbs(CivicGovernance.CivicCoverage(wZero, cZero), 0.0, 1e-15, "I_civic == 0.");
            Assert.NearAbs(CivicGovernance.QoLEqualityMultiplier(wZero, cZero), 1.0, 1e-15, "Q_civic == 1.0 at I=0.");
            Assert.NearAbs(CivicGovernance.PublicWorksMobilizationMultiplier(wZero, cZero), 1.0, 1e-15, "M_build == 1.0 at I=0.");
            Assert.NearAbs(CivicGovernance.GranaryStorageMultiplier(wZero, cZero), 1.0, 1e-15, "M_storage == 1.0 at I=0.");
            Assert.NearAbs(PhysicalDemography.CrowdingMitigation(wZero, cZero), 1.0, 1e-15, "M_c == 1.0 at I=0.");
            Assert.NearAbs(CivicGovernance.CohesionMultiplier(wZero, cZero), 1.0, 1e-15, "CohesionMultiplier == 1.0 at I=0.");

            // Paired positive control: I_civic == 1 activates LIVE M3 simulation hooks!
            Assert.Check(PhysicalDemography.CrowdingMitigation(wFull, cFull) < 0.15,
                $"Positive control live M_c must be < 0.15 (got {PhysicalDemography.CrowdingMitigation(wFull, cFull):F4}).");
            Assert.Check(NeolithicProduction.NaturalStorageCapacity(wFull, cFull, 0) > NeolithicProduction.NaturalStorageCapacity(wZero, cZero, 0) * 1.35,
                "Positive control live NaturalStorageCapacity must apply M_storage > 1.35x.");
            var canalRecipe = NeolithicProduction.Recipes(wFull.Config).Single(r => r.Good == CraftGood.CanalConstruction);
            Assert.Check(SpecialistProductivity.CraftRate(wFull, cFull, canalRecipe, WorkforceRoles.Builder) >
                SpecialistProductivity.CraftRate(wZero, cZero, canalRecipe, WorkforceRoles.Builder) * 1.25,
                "Positive control live CraftRate must apply M_build > 1.25x.");
        }

        // ==================== F08 ====================

        private static void T2_F08_01()
        {
            var wPaleo = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true);
            var cP = wPaleo.Camps[0];
            cP.People.Progression.Era = Era.Paleolithic;
            cP.People.CivicPrecedents.Clear();
            cP.People.CivicExperience.Clear();

            var wNeo = WorldFixtures.CloneWorld(wPaleo);
            wNeo.Camps[0].People.Progression.Era = Era.Neolithic;

            for (int t = 0; t < 3; t++)
            {
                CivicGovernance.Close(wPaleo, new TurnRecord(), 20.0);
                CivicGovernance.Close(wNeo, new TurnRecord(), 20.0);
            }

            double expP = cP.People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double vp) ? vp : 0.0;
            double expN = wNeo.Camps[0].People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double vn) ? vn : 0.0;
            Assert.NearAbs(expP, 0.0, 1e-15, "Paleolithic era must accrue exact 0 Tier-1 practice.");
            Assert.Check(!cP.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration), "Paleolithic must not unlock Tier 1.");
            Assert.Check(expN > 0.0, $"Neolithic positive control must accrue Tier-1 practice > 0 (got {expN:F2}).");
        }

        private static void T2_F08_02()
        {
            var wZero = WorldFixtures.CreatePlainsWorld(population: 1600, civicEnabled: true,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var cZ = wZero.Camps[0];
            WorldFixtures.AssignExactStewards(cZ, 0);

            var wStaffed = WorldFixtures.CloneWorld(wZero);
            WorldFixtures.AssignExactStewards(wStaffed.Camps[0], 15);

            for (int t = 0; t < 3; t++)
            {
                var rZ = new TurnRecord();
                rZ.Production.Add(new ProductionReceipt { Camp = cZ.Id, Role = WorkforceRoles.Builder, WorkerIntervals = 800.0 });
                CivicGovernance.Close(wZero, rZ, 20.0);

                var rS = new TurnRecord();
                rS.Production.Add(new ProductionReceipt { Camp = wStaffed.Camps[0].Id, Role = WorkforceRoles.Builder, WorkerIntervals = 800.0 });
                CivicGovernance.Close(wStaffed, rS, 20.0);
            }

            double expZ = cZ.People.CivicExperience.TryGetValue(CivicPrecedents.CivicOrganization, out double vz) ? vz : 0.0;
            double expS = wStaffed.Camps[0].People.CivicExperience.TryGetValue(CivicPrecedents.CivicOrganization, out double vs) ? vs : 0.0;
            Assert.NearAbs(expZ, 0.0, 1e-15, "Zero Stewards must accrue exact 0 Tier-2 practice.");
            Assert.Check(expS > 20.0, $"Positive control with 15 Stewards must accrue Tier-2 practice > 20 (got {expS:F2}).");
        }

        private static void T2_F08_03()
        {
            var wNoClay = WorldFixtures.CreatePlainsWorld(population: 2000, civicEnabled: true, withResources: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var cNo = wNoClay.Camps[0];
            // Resources != null, but zero Clay anywhere around camp!
            WorldFixtures.AddSurfaceResources(wNoClay, wNoClay.Map.Radius(cNo.Tile, 1), stone: 4000, clay: 0, wood: 4000);
            cNo.People.NeolithicTechnologies.Remove(NeolithicTech.Pottery);
            cNo.Inventory.Pots = 0.0;
            cNo.PlantFood = 2000 * PhysicalDemography.FoodPerPersonYear(wNoClay.Config) * 25.0;
            WorldFixtures.AssignExactStewards(cNo, 40);
            Assert.Check(!CivicGovernance.HasLocalClay(wNoClay, cNo), "Negative arm must have zero local Clay.");

            var wClay = WorldFixtures.CloneWorld(wNoClay);
            wClay.Resources.Clay[wClay.Camps[0].Tile] = 4000;
            Assert.Check(CivicGovernance.HasLocalClay(wClay, wClay.Camps[0]), "Positive control must have local Clay.");

            for (int t = 0; t < 3; t++)
            {
                CivicGovernance.Close(wNoClay, new TurnRecord(), 20.0);
                CivicGovernance.Close(wClay, new TurnRecord(), 20.0);
            }

            double expNo = cNo.People.CivicExperience.TryGetValue(CivicPrecedents.TokensAndAccounting, out double vn) ? vn : 0.0;
            double expYes = wClay.Camps[0].People.CivicExperience.TryGetValue(CivicPrecedents.TokensAndAccounting, out double vy) ? vy : 0.0;
            Assert.NearAbs(expNo, 0.0, 1e-15, "Missing Pottery, Pots, and Clay (with Resources != null) must accrue exact 0 Tier-3A practice.");
            Assert.Check(expYes > 20.0, $"Positive control with local Clay must accrue Tier-3A practice > 20 (got {expYes:F2}).");
        }

        private static void T2_F08_04()
        {
            double Run3B(bool sanitEnabled, bool hasT2, bool hasConstruction, int pop)
            {
                var precs = hasT2
                    ? new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization }
                    : new[] { CivicPrecedents.CeremonialIntegration };
                var w = WorldFixtures.CreatePlainsWorld(population: pop, civicEnabled: true, sanitationEnabled: sanitEnabled, precedents: precs);
                var c = w.Camps[0];
                if (!hasConstruction) c.People.NeolithicTechnologies.Remove(NeolithicTech.Construction);
                WorldFixtures.AssignExactStewards(c, 40);
                var rec = new TurnRecord();
                rec.Production.Add(new ProductionReceipt { Camp = c.Id, Role = WorkforceRoles.Builder, WorkerIntervals = 1600.0 });
                CivicGovernance.Close(w, rec, 20.0);
                return c.People.CivicExperience.TryGetValue(CivicPrecedents.PlannedQuartersAndDrainage, out double v) ? v : 0.0;
            }

            Assert.NearAbs(Run3B(false, true, true, 2500), 0.0, 1e-15, "SanitationEnabled=false blocks Tier 3B.");
            Assert.NearAbs(Run3B(true, false, true, 2500), 0.0, 1e-15, "Missing Tier 2 blocks Tier 3B.");
            Assert.NearAbs(Run3B(true, true, false, 2500), 0.0, 1e-15, "Missing Construction tech blocks Tier 3B.");
            Assert.NearAbs(Run3B(true, true, true, 1200), 0.0, 1e-15, "Population <= 1200 blocks Tier 3B.");
            double pos = Run3B(true, true, true, 2500);
            Assert.Check(pos > 5.0, $"Positive control meeting all 4 gates must accrue Tier-3B practice > 5 (got {pos:F2}).");
        }

        private static void T2_F08_05()
        {
            double RunTradeDiffusion(double diffusionRate)
            {
                var cfg = WorldFixtures.BaseConfig(width: 22, height: 14, players: 3, civicEnabled: true, fission: false, immigration: false, trade: true);
                cfg.TradeFoodEnabled = false;
                cfg.Civic.PrecedentDiffusionPerTrade = diffusionRate;
                cfg.Validate();

                var w = WorldFixtures.CreateEmptyWorld(cfg, seed: 2805);
                for (int i = 0; i < w.Tiles.Length; i++)
                    w.Tiles[i] = WorldFixtures.MakeFertileTile(i, cfg);

                var human = WorldFixtures.AddVillage(w, 0, w.Map.At(18, 11), 100);
                var partner3A = WorldFixtures.AddVillage(w, 1, w.Map.At(6, 6), 160, housingCoverage: 0.0, clothPerCapita: 0.0, housingMaterial: 0.0, autoEconomy: false,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                partner3A.Inventory.Cloth = 600.0;
                partner3A.Inventory.HousingMaterial = 0.0;

                var receiverT1 = WorldFixtures.AddVillage(w, 2, w.Map.At(9, 6), 160, housingCoverage: 0.0, clothPerCapita: 0.0, housingMaterial: 0.0, autoEconomy: false,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration });
                receiverT1.Inventory.Cloth = 0.0;
                receiverT1.Inventory.HousingMaterial = 120.0;
                receiverT1.People.CivicExperience.Clear();

                WorldFixtures.CultivateFootprint(w, human);
                WorldFixtures.CultivateFootprint(w, partner3A);
                WorldFixtures.CultivateFootprint(w, receiverT1);
                WorldFixtures.EstablishContacts(w, partner3A, receiverT1);
                WorldFixtures.ValidateWorld(w);

                var rec = WorldFixtures.StepTurn(w);
                Assert.Check(rec.Trades.Count > 0, $"Expected active AI-to-AI trades (diffusionRate={diffusionRate}, trades={rec.Trades.Count}).");
                return receiverT1.People.CivicExperience.TryGetValue(CivicPrecedents.CivicOrganization, out double v) ? v : 0.0;
            }

            double defaultDiff = RunTradeDiffusion(0.0);
            double optInDiff = RunTradeDiffusion(0.25);
            Assert.NearAbs(defaultDiff, 0.0, 1e-15, "Default PrecedentDiffusionPerTrade == 0 must transfer exact 0 precedent experience.");
            Assert.Check(optInDiff > 0.0, $"Opt-in PrecedentDiffusionPerTrade=0.25 must diffuse Tier-2 experience > 0 via real trade (got {optInDiff:F4}).");
        }

        private static void T2_F08_06()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1600, civicEnabled: true, fission: true, immigration: false, width: 22, height: 16,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var parent = w.Camps[0];
            parent.FissionPolicy = FissionPolicy.Encourage;
            parent.People.CivicExperience[CivicPrecedents.TokensAndAccounting] = 15.0;
            w.FixedHousing.Clear();
            NeolithicProduction.FixedHousingChanged(w);
            WorldFixtures.ValidateWorld(w);

            var rec = new TurnRecord { Turn = w.Turn, StartDays = w.Days, Duration = w.Config.TurnDays };
            Fission.Resolve(w, rec, new PhaseExecution(), new ResolutionMetrics());
            Assert.Check(w.Camps.Count == 2, "Expected daughter founded by Fission.Resolve.");

            var daughter = w.Camps.Single(x => x.Id != parent.Id);
            daughter.People.CivicPrecedents.Add(CivicPrecedents.TokensAndAccounting);
            daughter.People.CivicExperience[CivicPrecedents.TokensAndAccounting] = 999.0;

            Assert.Check(!parent.People.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting),
                "Mutating daughter CivicPrecedents must NOT alias parent.");
            Assert.NearAbs(parent.People.CivicExperience[CivicPrecedents.TokensAndAccounting], 15.0, 1e-15,
                "Mutating daughter CivicExperience must NOT alias parent.");
        }

        // ==================== F09 ====================

        private static void T2_F09_01()
        {
            var wSmall = WorldFixtures.CreatePlainsWorld(population: 250, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            wSmall.Camps[0].CivicComplexity = 0.25;
            wSmall.Camps[0].LastEnvironmentalComplexity = 0.25;
            wSmall.Camps[0].CivicCapital = 0.0;

            var wLarge = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 600.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            wLarge.Camps[0].CivicComplexity = 0.25;
            wLarge.Camps[0].LastEnvironmentalComplexity = 0.25;
            wLarge.Camps[0].CivicCapital = 0.0;

            WorldFixtures.StepTurn(wSmall);
            WorldFixtures.StepTurn(wLarge);

            Assert.NearAbs(wSmall.Camps[0].CivicCapital, 0.0, 1e-12,
                $"Below DevelopmentMinPopulation=300, N=250 must build exact 0 CivicCapital (got {wSmall.Camps[0].CivicCapital:F4}).");
            Assert.Check(wLarge.Camps[0].CivicCapital > 0.0,
                $"Paired positive control at N=1,000 must build CivicCapital > 0 (got {wLarge.Camps[0].CivicCapital:F4}).");
        }

        private static void T2_F09_02()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1400, civicEnabled: true, fission: false, immigration: false, withResources: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.50;
            c.LastEnvironmentalComplexity = 0.50;
            c.CivicCapital = 100.0;
            c.Inventory.HousingMaterial = 0.0;
            c.Inventory.Stone = 0.0;
            c.Inventory.Clay = 0.0;
            c.Inventory.Wood = 0.0;

            var rec = WorldFixtures.StepTurn(w);
            int stewards = CultivationEconomy.Workers(c, WorkforceRoles.Steward);
            double maint = rec.Production.Where(ProductionReceiptHelpers.IsCivicMaintenance).Sum(r => r.Output);
            Assert.Check(stewards > 0, $"Material/timber shortage must NOT block material-free core Stewards (got {stewards}).");
            Assert.NearAbs(maint, 0.0, 1e-12, $"Zero materials must cleanly shed CivicMaintenance to 0 (got {maint:F4}).");
        }

        private static void T2_F09_03()
        {
            WorldState Build(double housingCoverage, double hmStock)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 800, civicEnabled: true, fission: false, immigration: false,
                    grantPlough: false,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                w.Config.LandBiomassMultiplier = 63.0;
                WorldFixtures.SetFixedStepYears(w, 1.0);
                var c = w.Camps[0];
                foreach (int id in CultivationEconomy.Footprint(w, c))
                {
                    var t = w.Tiles[id];
                    for (int s = 0; s < 4; s++)
                    {
                        var sd = t.Seasons[s];
                        sd.Temperature = -4.0f;
                        t.Seasons[s] = sd;
                    }
                    FoodEcology.ConfigurePotential(t, w.Config, initializeStock: true);
                    var ecap = new EcologyCapacity(w, t, new EcologyView(t), 0);
                    t.Food = Math.Min(t.PotentialFood, ecap.Food);
                }
                c.CivicComplexity = 1.0;
                c.LastEnvironmentalComplexity = 1.0;
                c.CivicStratification = 1.0;
                c.LastEnvironmentalStratification = -1.0; // alignment friction raises ReqStewards to ~143
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
                c.Inventory.FarmTools = 0.0;
                c.Inventory.Cloth = 0.0;
                c.Inventory.Tools = 4000.0;
                c.Inventory.HousingMaterial = hmStock;
                c.Inventory.Wood = 4000.0;
                c.Inventory.Clay = 4000.0;
                c.PlantFood = 800 * PhysicalDemography.FoodPerPersonYear(w.Config) * 10.0;
                w.FixedHousing.Clear();
                NeolithicProduction.FixedHousingChanged(w);
                NeolithicProduction.AddFixedHousing(w, c, Math.Ceiling(800 * housingCoverage / w.Config.Neolithic.HousingPeoplePerUnit), w.Days);
                WorldFixtures.ValidateWorld(w);
                return w;
            }

            // Positive control: ample free labor after Protected (~416 workers) funds full FarmTools + full Civic Continuity (~143 Stewards)
            var wAmple = Build(housingCoverage: 1.00, hmStock: 4000.0);
            var recAmple = WorldFixtures.StepTurn(wAmple);
            double farmToolsAmple = recAmple.Production.Where(r => r.Good == CraftGood.FarmTools).Sum(r => r.Output);
            int stewardsAmple = CultivationEconomy.Workers(wAmple.Camps[0], WorkforceRoles.Steward);
            double maintAmple = recAmple.Production.Where(ProductionReceiptHelpers.IsCivicMaintenance).Sum(r => r.Output);

            Assert.Check(farmToolsAmple > 0.0, $"Unconstrained control must produce FarmTools (got {farmToolsAmple:F3}).");
            Assert.Check(stewardsAmple >= 100 && maintAmple > 0.0,
                $"Unconstrained control must fully fund Civic Continuity (stewards={stewardsAmple}, maint={maintAmple:F3}).");

            // Tight workforce scarcity arm: cold shelter rebuild consumes ~384 Protected workers, leaving only ~44.6 workers for Continuity (<< ~156 needed)
            var wTight = Build(housingCoverage: 0.30, hmStock: 0.0);
            var recTight = WorldFixtures.StepTurn(wTight);
            double farmToolsTight = recTight.Production.Where(r => r.Good == CraftGood.FarmTools).Sum(r => r.Output);
            int stewardsTight = CultivationEconomy.Workers(wTight.Camps[0], WorkforceRoles.Steward);

            Assert.Check(farmToolsTight > 0.0,
                $"Under tight Continuity workforce rationing (~45 free workers), FarmTools Continuity must stay funded ahead of Civic Continuity trim (farmToolsTight={farmToolsTight:F3}, stewardsTight={stewardsTight}).");
            Assert.Check(stewardsTight < stewardsAmple * 0.50,
                $"Tight workforce arm must genuinely trim Civic Continuity below unconstrained control (tight={stewardsTight} vs ample={stewardsAmple}).");
            Assert.Check(wTight.Camps[0].FoodNeedsMetRatio >= 0.95,
                $"Workforce tie must be clean labor rationing without starvation confounding (foodMet={wTight.Camps[0].FoodNeedsMetRatio:F3}).");
        }

        private static void T2_F09_04()
        {
            // Genuine labor-bound cold + famine: uncultivated ample-biomass tiles with GatherPerPerson=1.02 & zero food/cloth reserves so Protected food + clothing consumes ~100% of workforce
            var w = WorldFixtures.CreatePlainsWorld(population: 1500, civicEnabled: true, fission: false, immigration: false,
                grantPlough: false, landBiomassMultiplier: 2000.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            foreach (int t in w.Map.Radius(c.Tile, 1))
            {
                for (int s = 0; s < 4; s++)
                {
                    var sd = w.Tiles[t].Seasons[s];
                    sd.Temperature = -2.0f;
                    w.Tiles[t].Seasons[s] = sd;
                }
                FoodEcology.ConfigurePotential(w.Tiles[t], w.Config, initializeStock: true);
            }
            WorldFixtures.ClearFootprintCultivation(w, c);
            w.Config.GatherPerPerson = 1.02;
            w.Config.Validate();

            c.CivicComplexity = 0.70;
            c.LastEnvironmentalComplexity = 0.70;
            c.CivicCapital = 180.0; // below ReqCap(1500, 0.70, 0) = 189 so CivicConstruction WOULD run if Development got free workers!
            c.Inventory.Cloth = 0.0;
            c.PlantFood = 0.0;
            c.AnimalFood = 0.0;
            c.AquaticFood = 0.0;
            WorldFixtures.AssignExactStewards(c, 50);
            WorldFixtures.ValidateWorld(w);

            // Verify M3 civics are actually wired before checking priority under cold/famine
            Assert.Check(PhysicalDemography.CrowdingMitigation(w, c) < 0.70,
                $"Positive control: M3 CrowdingMitigation must be live (got {PhysicalDemography.CrowdingMitigation(w, c):F4}).");

            double turnYears = w.Config.TurnDays / w.Config.DaysPerYear;
            var rec = WorldFixtures.StepTurn(w);
            double clothOut = rec.Production.Where(r => r.Good == CraftGood.Cloth).Sum(r => r.Output);
            double civicBuildOut = rec.Production.Where(ProductionReceiptHelpers.IsCivicConstruction).Sum(r => r.Output);
            Assert.Check(clothOut > 0.0 && c.ClothPerCapita > 0.30,
                $"Cold labor squeeze must prioritize Protected clothing (clothOut={clothOut:F2}, clothCov={c.ClothPerCapita:F3}).");
            // Integer staffing may leave a fraction of an already-paid Builder idle; Development may use that slack, but it
            // must never take a whole worker away from survival service.
            double civicBuildPersons = civicBuildOut * w.Config.Civic.CivicBuildBuilderYears / turnYears;
            Assert.Check(civicBuildPersons < 1.0,
                $"Extreme cold + labor-bound famine must not divert a whole worker into CivicConstruction Development (got {civicBuildOut:F2} units = {civicBuildPersons:F2} persons).");
            Assert.Check(c.CivicCapital < 189.0 * 1.05,
                $"Famine city must not meaningfully expand civic capital (got {c.CivicCapital:F1}).");
        }

        private static void T2_F09_05()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1800, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 1000.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.65;
            c.LastEnvironmentalComplexity = 0.65;
            c.CivicCapital = 180.0;

            // Simulate post-collapse state: population dropped to 900, depleted capital & food
            c.Population = 900;
            c.CivicCapital = 20.0;
            c.PlantFood = 900 * PhysicalDemography.FoodPerPersonYear(w.Config) * 2.0;
            WorldFixtures.ValidateWorld(w);

            var resolver = new TurnResolver();
            for (int t = 0; t < 5; t++)
                WorldFixtures.StepTurn(w, resolver);

            Assert.Check(c.Population > 1100, $"Post-collapse recovery must restore population > 1100 (got {c.Population}).");
            Assert.Check(c.CivicCapital > 40.0, $"Post-collapse recovery must rebuild CivicCapital cleanly (got {c.CivicCapital:F2}).");
            Assert.Check(CultivationEconomy.Workers(c, WorkforceRoles.Steward) > 0, "Post-collapse recovery must restaff Stewards.");
        }

        // ==================== F10 ====================

        private static void T2_F10_01()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.PlannedQuartersAndDrainage };
            var w = WorldFixtures.CreateOasisAndRiverDualWorld(oasisPop: 2200, riverPop: 2200, sanitationEnabled: true, agriculture: true,
                landBiomassMultiplier: 1000.0, precedents: precs);
            var oasis = w.Camps[0];
            var river = w.Camps[1];
            oasis.CivicComplexity = 0.75;
            oasis.LastEnvironmentalComplexity = 0.75;
            river.CivicComplexity = 0.75;
            river.LastEnvironmentalComplexity = 0.75;
            oasis.CivicCapital = CivicGovernance.RequiredCivicCapital(w, oasis);
            river.CivicCapital = CivicGovernance.RequiredCivicCapital(w, river);

            var resolver = new TurnResolver();
            for (int t = 0; t < 3; t++)
                WorldFixtures.StepTurn(w, resolver);

            Assert.Check(river.SanitationCapital > 0.0 && river.SanitationWaterRatio > 0.90,
                $"River positive control must build SanitationCapital > 0 & achieve SanitationWaterRatio > 0.90 (cap={river.SanitationCapital:F2}, ratio={river.SanitationWaterRatio:F3}).");
            Assert.NearAbs(oasis.SanitationCapital, 0.0, 1e-12,
                $"Arid oasis must build exact 0 dry sewers (got {oasis.SanitationCapital:F3}).");
            Assert.NearAbs(oasis.SanitationWaterRatio, 0.0, 1e-12,
                $"Arid oasis must have SanitationWaterRatio == 0 (got {oasis.SanitationWaterRatio:F3}).");
        }

        private static void T2_F10_02()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.PlannedQuartersAndDrainage };
            var wSmall = WorldFixtures.CreateValleyWorld(population: 900, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 1200.0, precedents: precs);
            wSmall.Camps[0].CivicComplexity = 0.70;
            wSmall.Camps[0].LastEnvironmentalComplexity = 0.70;
            wSmall.Camps[0].CivicCapital = CivicGovernance.RequiredCivicCapital(wSmall, wSmall.Camps[0]);
            wSmall.Camps[0].SanitationCapital = 0.0;

            var wLarge = WorldFixtures.CreateValleyWorld(population: 2200, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 1200.0, precedents: precs);
            wLarge.Camps[0].CivicComplexity = 0.70;
            wLarge.Camps[0].LastEnvironmentalComplexity = 0.70;
            wLarge.Camps[0].CivicCapital = CivicGovernance.RequiredCivicCapital(wLarge, wLarge.Camps[0]);
            wLarge.Camps[0].SanitationCapital = 0.0;

            WorldFixtures.StepTurn(wSmall);
            WorldFixtures.StepTurn(wLarge);

            Assert.NearAbs(wSmall.Camps[0].SanitationCapital, 0.0, 1e-12,
                $"Below SanitationDevelopmentMinPopulation=1200, N=900 must build 0 SanitationCapital (got {wSmall.Camps[0].SanitationCapital:F3}).");
            Assert.Check(wLarge.Camps[0].SanitationCapital > 0.0,
                $"Paired positive control at N=2,200 must build SanitationCapital > 0 (got {wLarge.Camps[0].SanitationCapital:F3}).");
        }

        private static void T2_F10_03()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.PlannedQuartersAndDrainage };
            var w = WorldFixtures.CreateValleyWorld(population: 2500, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 1600.0, precedents: precs);
            var c = w.Camps[0];
            c.CivicComplexity = 0.75;
            c.LastEnvironmentalComplexity = 0.75;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
            c.SanitationCapital = 0.0;
            c.SanitationWaterRatio = 0.0;
            c.Inventory.HousingMaterial = 6000.0;
            c.Inventory.Tools = 6000.0;

            var resolver = new TurnResolver();
            var rec1 = WorldFixtures.StepTurn(w, resolver);
            var d1 = rec1.Demographics.Single(d => d.Camp == c.Id);
            Assert.Check(c.SanitationCapital > 0.0, $"Turn 1 must build SanitationCapital > 0 (got {c.SanitationCapital:F3}).");
            Assert.NearAbs(c.SanitationWaterRatio, 0.0, 1e-12, "Turn 1 built sanitation must NOT retroactively allocate water on Turn 1.");

            var rec2 = WorldFixtures.StepTurn(w, resolver);
            var d2 = rec2.Demographics.Single(d => d.Camp == c.Id);
            Assert.Check(c.SanitationWaterRatio > 0.90, $"Turn 2 must allocate SanitationWaterRatio > 0.90 (got {c.SanitationWaterRatio:F3}).");
            Assert.Check(PhysicalDemography.DiseaseMitigation(w, c) < 0.60,
                $"Turn 2 active sanitation must cut DiseaseMitigation < 0.60 (got {PhysicalDemography.DiseaseMitigation(w, c):F4}).");
            Assert.Check(d2.DiseaseMortalityRate < d1.DiseaseMortalityRate * 0.75,
                $"Turn 2 disease rate ({d2.DiseaseMortalityRate:F5}) must drop well below Turn 1 non-retroactive rate ({d1.DiseaseMortalityRate:F5}).");
        }

        private static void T2_F10_04()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.PlannedQuartersAndDrainage };
            var w = WorldFixtures.CreateOasisAndRiverDualWorld(oasisPop: 2000, riverPop: 2200, sanitationEnabled: true, agriculture: false,
                landBiomassMultiplier: 1000.0, precedents: precs);
            Assert.Check(w.Config.Agriculture == null, "Config.Agriculture must be null for this test.");
            var oasis = w.Camps[0];
            var river = w.Camps[1];
            oasis.CivicComplexity = 0.75;
            oasis.LastEnvironmentalComplexity = 0.75;
            river.CivicComplexity = 0.75;
            river.LastEnvironmentalComplexity = 0.75;
            oasis.CivicCapital = CivicGovernance.RequiredCivicCapital(w, oasis);
            river.CivicCapital = CivicGovernance.RequiredCivicCapital(w, river);

            var resolver = new TurnResolver();
            for (int t = 0; t < 3; t++)
                WorldFixtures.StepTurn(w, resolver);

            Assert.Check(river.SanitationCapital > 0.0 && river.SanitationWaterRatio > 0.90,
                $"River control with Agriculture == null must still build & run Sanitation (cap={river.SanitationCapital:F2}, ratio={river.SanitationWaterRatio:F3}).");
            Assert.NearAbs(oasis.SanitationCapital, 0.0, 1e-12,
                $"Oasis with Agriculture == null must still reserve 0.10*N drinking first and build 0 dry sewers (got {oasis.SanitationCapital:F3}).");
            Assert.NearAbs(oasis.SanitationWaterRatio, 0.0, 1e-12, "Oasis SanitationWaterRatio must be 0.");
        }

        private static void T2_F10_05()
        {
            var cfg = WorldFixtures.BaseConfig(width: 20, height: 14, players: 2, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false, trade: false,
                landBiomassMultiplier: 800.0);
            var w = WorldFixtures.CreateEmptyWorld(cfg, seed: 2105);
            for (int i = 0; i < w.Tiles.Length; i++)
                w.Tiles[i] = WorldFixtures.MakeAridTile(i, cfg);

            cfg.MinimumCampSpacing = 2;
            cfg.Validate();

            int sharedRiver = w.Map.At(10, 7);
            var nbs = w.Map.Neighbors(sharedRiver).ToArray();
            int tileA = nbs[0];
            int tileB = nbs[3];
            Assert.Check(w.Map.Distance(tileA, tileB) == 2 && w.Map.Distance(tileA, sharedRiver) == 1 && w.Map.Distance(tileB, sharedRiver) == 1,
                "Both cities must share the single river tile while respecting spacing.");

            // Shared river MaxFreshwater=220 (~379/yr), Drinking for two N=1600 cities = 320/yr -> residual ~59/yr < combined sanitation need 64/yr!
            w.Tiles[sharedRiver] = WorldFixtures.MakeFertileTile(sharedRiver, cfg, moisture: 0.65f, waterSource: FreshwaterSource.River, maxFreshwater: 220.0f);
            foreach (int t in w.Map.Radius(tileA, 1).Concat(w.Map.Radius(tileB, 1)).Distinct())
            {
                if (t == sharedRiver) continue;
                w.Tiles[t] = WorldFixtures.MakeFertileTile(t, cfg, moisture: 0.60f, waterSource: FreshwaterSource.None, maxFreshwater: 0.0f);
            }

            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.PlannedQuartersAndDrainage };
            var cA = WorldFixtures.AddVillage(w, 0, tileA, 1600, precedents: precs);
            var cB = WorldFixtures.AddVillage(w, 1, tileB, 1600, precedents: precs);
            cA.CivicComplexity = 0.75; cB.CivicComplexity = 0.75;
            cA.LastEnvironmentalComplexity = 0.75; cB.LastEnvironmentalComplexity = 0.75;
            cA.CivicCapital = CivicGovernance.RequiredCivicCapital(w, cA);
            cB.CivicCapital = CivicGovernance.RequiredCivicCapital(w, cB);
            cA.SanitationCapital = CivicGovernance.RequiredSanitationCapital(w.Config, cA.Population);
            cB.SanitationCapital = CivicGovernance.RequiredSanitationCapital(w.Config, cB.Population);
            WorldFixtures.CultivateFootprint(w, cA, capital: 2.0);
            WorldFixtures.CultivateFootprint(w, cB, capital: 2.0);
            WorldFixtures.ValidateWorld(w);

            var rec = WorldFixtures.StepTurn(w);
            Assert.Check(rec.SanitationWater != null && rec.SanitationWater.Count >= 2,
                $"Both competing cities must receive proportional SanitationWater receipts (count={rec.SanitationWater?.Count ?? 0}).");
            Assert.Check(cA.SanitationWaterRatio > 0.10 && cB.SanitationWaterRatio > 0.10 && cA.SanitationWaterRatio < 0.99,
                $"Constrained shared river must split proportionally below 1.0 (ratioA={cA.SanitationWaterRatio:F3}, ratioB={cB.SanitationWaterRatio:F3}).");
            Assert.NearRelative(cA.SanitationWaterRatio, cB.SanitationWaterRatio, 1e-9, "Symmetric competing cities must receive symmetric shares.");
            Assert.Check(w.Tiles[sharedRiver].Freshwater >= -1e-7, "Shared river freshwater must remain non-negative.");
        }

        private static void T2_F10_06()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting, CivicPrecedents.PlannedQuartersAndDrainage };
            var w = WorldFixtures.CreateValleyWorld(population: 3000, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 800.0, precedents: precs);
            w.Config.Civic.DiseaseMitigationBase = 1.0;
            w.Config.Validate();

            var c = w.Camps[0];
            c.CivicComplexity = 1.0;
            c.CivicStratification = 1.0;
            c.SanitationCapital = CivicGovernance.RequiredSanitationCapital(w.Config, c.Population);
            c.SanitationWaterRatio = 1.0;

            double md = PhysicalDemography.DiseaseMitigation(w, c);
            Assert.NearAbs(md, w.Config.Civic.MinDiseaseMitigation, 1e-12,
                $"Live PhysicalDemography.DiseaseMitigation must clamp at hard floor 0.15 (got {md:F6}).");

            // Separate toggle check: SanitationEnabled == false must return exact DiseaseMitigation == 1.0
            var wDisabled = WorldFixtures.CloneWorld(w);
            wDisabled.Config.Civic.SanitationEnabled = false;
            Assert.NearAbs(PhysicalDemography.DiseaseMitigation(wDisabled, wDisabled.Camps[0]), 1.0, 1e-12,
                "Disabling SanitationEnabled must return exact DiseaseMitigation == 1.0.");

            // Stale SanitationWaterRatio reset & LegacyQoLNetGrowth safety while SanitationEnabled remains true
            w.Config.DemographicModel = DemographicModel.LegacyQoLNetGrowth;
            c.SanitationCapital = 0.0;
            c.IsVillage = false;
            c.SanitationWaterRatio = 1.0; // stale prior ratio must be cleared by AllocateSanitationWater
            WorldFixtures.StepTurn(w);
            Assert.NearAbs(c.SanitationWaterRatio, 0.0, 1e-12,
                $"Camp losing sanitation/village status under SanitationEnabled=true + LegacyQoLNetGrowth must reset stale SanitationWaterRatio to 0 (got {c.SanitationWaterRatio:F4}).");
            Assert.NearAbs(PhysicalDemography.DiseaseMitigation(w, c), 1.0, 1e-12,
                "Post-reset camp with zero sanitation must have DiseaseMitigation == 1.0.");
        }

        // ==================== F11 ====================

        private static void T2_F11_01()
        {
            var w = WorldFixtures.CreateValleyWorld(population: 1200, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.55;
            c.CivicStratification = 0.45;
            c.CivicCapital = 100.0;
            c.SanitationCapital = 50.0;
            c.ImmigrationPolicy = ImmigrationPolicy.Restricted;
            c.CivicFocus = CivicFocus.Centralized;

            byte[] saved = SaveCodec.Save(w);
            byte[] stripped = SaveXmlTools.StripCivicXmlElements(saved);
            var loaded = SaveCodec.Load(stripped);

            Assert.Check(loaded.Config.Civic == null, "Stripped legacy save must load with Config.Civic == null.");
            var lc = loaded.Camps[0];
            Assert.NearAbs(lc.CivicComplexity, 0.0, 1e-15, "Legacy load zeroes CivicComplexity.");
            Assert.NearAbs(lc.CivicStratification, 0.0, 1e-15, "Legacy load zeroes CivicStratification.");
            Assert.NearAbs(lc.CivicCapital, 0.0, 1e-15, "Legacy load zeroes CivicCapital.");
            Assert.NearAbs(lc.SanitationCapital, 0.0, 1e-15, "Legacy load zeroes SanitationCapital.");
            Assert.Check(lc.People.CivicExperience != null && lc.People.CivicPrecedents != null,
                "Legacy load must initialize non-null empty Society civic collections.");

            WorldFixtures.StepTurn(loaded);
            TurnResolver.Invariants(loaded);
        }

        private static void T2_F11_02()
        {
            var w = WorldFixtures.CreateValleyWorld(population: 1000, civicEnabled: true, sanitationEnabled: true,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];
            c.CivicComplexity = 0.25;
            c.CivicStratification = 0.30;
            c.CivicCapital = 20.0;
            c.CivicFocus = CivicFocus.Centralized;
            byte[] clean = SaveCodec.Save(w);

            var corruptions = new (string Elem, string BadVal)[]
            {
                ("CivicComplexity", "1.5"),
                ("CivicStratification", "-2.0"),
                ("CivicCapital", "-5"),
                ("CivicComplexity", "NaN"),
                ("CivicFocus", "99")
            };

            foreach (var item in corruptions)
            {
                byte[] mutated = SaveXmlTools.MutateCampXmlElement(clean, item.Elem, item.BadVal);
                Assert.ExpectThrowsAny(() => SaveCodec.Load(mutated),
                    $"SaveCodec.Load must reject corrupt {item.Elem}={item.BadVal}.");
            }
        }

        private static void T2_F11_03()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 800, civicEnabled: true);
            var obs = new AgentObservationBuilder().Build(w, 0);
            string json = ObservationContract.Serialize(obs, w.Camps[0].Mandate);
            Assert.Check(json.Contains("\"Schema\":23"), "Serialized observation must carry Schema 23.");
            string tampered = json.Replace("\"Schema\":23", "\"Schema\":22");
            Assert.ExpectThrows<InvalidDataException>(() => ObservationContract.Deserialize(tampered),
                "ObservationContract.Deserialize must reject Schema != 23 with InvalidDataException.");
        }

        private static void T2_F11_04()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 900, civicEnabled: true, sanitationEnabled: true,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c = w.Camps[0];

            var builder = new AgentObservationBuilder();
            var fullObs = builder.Build(w, c.Player);
            // Contract-specified verification hook: BuildForValidation
            var valObs = builder.BuildForValidation(w, c.Player, new PlanningIndex(w));

            Assert.Check(fullObs.CivicGovernanceEnabled == valObs.CivicGovernanceEnabled && fullObs.SanitationEnabled == valObs.SanitationEnabled,
                "BuildForValidation must match Build on top-level civic flags.");

            var goodEcon = EconomicIntent.From(c);
            goodEcon.AutomationTurns = 0;
            goodEcon.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Steward, Population = 10 } };

            var badEcon = EconomicIntent.From(c);
            badEcon.AutomationTurns = 0;
            badEcon.Assignments = new List<PopulationAssignment> { new PopulationAssignment { Role = WorkforceRoles.Steward, Population = c.Population + 10 } };

            var testOrders = new[]
            {
                new Order(OrderKind.ImmigrationPolicy, c.Id, (int)ImmigrationPolicy.Open),
                new Order(OrderKind.ImmigrationPolicy, c.Id, 99),
                new Order(OrderKind.CivicFocus, c.Id, (int)CivicFocus.Communal),
                new Order(OrderKind.CivicFocus, c.Id, -1),
                new Order(OrderKind.FissionPolicy, c.Id, (int)FissionPolicy.Encourage),
                new Order(OrderKind.Economy, c.Id) { Economy = goodEcon },
                new Order(OrderKind.Economy, c.Id) { Economy = badEcon }
            };

            foreach (var ord in testOrders)
            {
                bool fullOk = OrderRules.Validate(fullObs, ord) == null;
                bool valOk = OrderRules.Validate(valObs, ord) == null;
                Assert.Check(fullOk == valOk, $"BuildForValidation parity failed for order {ord}: fullOk={fullOk}, valOk={valOk}.");
            }
        }

        private static void T2_F11_05()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, width: 24, height: 16,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            var c0 = w.Camps[0];
            c0.LastEnvironmentalComplexity = 0.27;
            c0.LastEnvironmentalStratification = -0.51;

            // Place distant rival well outside vision radius 2 (distance > 6)
            var rival = WorldFixtures.AddVillage(w, 1, w.Map.At(2, 2), 1500,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            rival.CivicComplexity = 0.74;
            rival.CivicStratification = 0.91;
            rival.CivicCapital = 777.0;
            WorldFixtures.ValidateWorld(w);

            var obs0 = new AgentObservationBuilder().Build(w, 0);
            var so0 = obs0.Settlements.Single(s => s.Id == c0.Id);
            Assert.NearAbs(so0.LastEnvironmentalComplexity, 0.27, 1e-15, "Build must return committed LastEnvironmentalComplexity (no fog scan).");
            Assert.NearAbs(so0.LastEnvironmentalStratification, -0.51, 1e-15, "Build must return committed LastEnvironmentalStratification (no fog scan).");
            Assert.Check(!obs0.Rivals.Any(r => r.Id == rival.Id), "Fogged rival must not appear in Rivals.");

            string json = ObservationContract.Serialize(obs0, c0.Mandate);
            Assert.Check(ObservationLeak.SettlementIds(json).All(id => id != rival.Id), "Owner Settlements must not include the fogged rival.");
            Assert.Check(!ObservationLeak.ContainsNumber(json, 777.0) && !ObservationLeak.ContainsNumber(json, 0.91) && !ObservationLeak.ContainsNumber(json, 0.74),
                "Serialized ObservationDto must leak zero fogged rival civic values (exact numeric match).");
        }

        private static void T2_F11_06()
        {
            bool prevAudit = SocialRelocationForecastCache.AuditBounds;
            SocialRelocationForecastCache.AuditBounds = true;
            try
            {
                long before = SocialRelocationForecastCache.BoundEvaluations;
                var w = WorldFixtures.CreatePlainsWorld(population: 2600, civicEnabled: true, sanitationEnabled: true, fission: true, immigration: true, width: 26, height: 16,
                    landBiomassMultiplier: 800.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                var c0 = w.Camps[0];
                c0.CivicComplexity = 1.0;
                c0.CivicStratification = 1.0;
                c0.LastEnvironmentalStratification = -1.0;
                c0.ImmigrationPolicy = ImmigrationPolicy.Restricted;
                c0.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c0);
                WorldFixtures.AssignExactStewards(c0, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c0)));

                var c1 = WorldFixtures.AddVillage(w, 1, w.Map.At(16, 8), 800, foodYears: 20.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting });
                c1.CivicComplexity = 1.0;
                c1.CivicStratification = -1.0;
                c1.LastEnvironmentalStratification = -1.0;
                c1.ImmigrationPolicy = ImmigrationPolicy.Open;
                c1.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c1);
                WorldFixtures.AssignExactStewards(c1, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c1)));
                WorldFixtures.CultivateFootprint(w, c1);

                var c2 = WorldFixtures.AddVillage(w, 2, w.Map.At(10, 11), 450, foodYears: 12.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration });
                c2.CivicComplexity = 0.30;
                c2.CivicStratification = 0.0;
                c2.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c2) * 0.5;
                WorldFixtures.AssignExactStewards(c2, 10);
                WorldFixtures.CultivateFootprint(w, c2);

                WorldFixtures.EstablishAllContacts(w);
                WorldFixtures.ValidateWorld(w);

                Assert.Check(PhysicalDemography.CrowdingMitigation(w, c0) < 0.50,
                    $"Bound audit corner stress requires active M3 crowding mitigation (got {PhysicalDemography.CrowdingMitigation(w, c0):F4}).");

                var resolver = new TurnResolver();
                for (int t = 0; t < 4; t++)
                    WorldFixtures.StepTurn(w, resolver);

                Assert.Check(SocialRelocationForecastCache.BoundEvaluations > before, "Corner stress must exercise BoundEvaluations > 0.");
            }
            finally
            {
                SocialRelocationForecastCache.AuditBounds = prevAudit;
            }
        }

        // ==================== F12 ====================

        private static void T2_F12_01()
        {
            var policy = new HeuristicTribePolicy();
            var builder = new AgentObservationBuilder();

            // Hold Open inside hysteresis band (P=-0.25, crowd=0.028, nutr=0.88) -> would NOT enter from Neutral, MUST hold Open!
            var wOpen = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true);
            var cOpen = wOpen.Camps[0];
            cOpen.CivicStratification = -0.25;
            cOpen.CrowdingMortalityRate = 0.028;
            cOpen.FoodNeedsMetRatio = 0.88;
            cOpen.ImmigrationPolicy = ImmigrationPolicy.Open;
            var dOpenHold = policy.Decide(builder.Build(wOpen, 0), cOpen.Mandate);
            Assert.Check(!dOpenHold.Orders.Any(o => o.Kind == OrderKind.ImmigrationPolicy),
                "AI Elder must hold Open inside deadband hysteresis without oscillating.");

            cOpen.ImmigrationPolicy = ImmigrationPolicy.Neutral;
            var dOpenNoEnter = policy.Decide(builder.Build(wOpen, 0), cOpen.Mandate);
            Assert.Check(!dOpenNoEnter.Orders.Any(o => o.Kind == OrderKind.ImmigrationPolicy),
                "From Neutral inside deadband, AI Elder must NOT prematurely enter Open.");

            // Hold Restricted inside hysteresis band (P=+0.35, crowd=0.028) -> would NOT enter from Neutral, MUST hold Restricted!
            var wRest = WorldFixtures.CreateValleyWorld(population: 1200, civicEnabled: true);
            var cRest = wRest.Camps[0];
            cRest.CivicStratification = 0.35;
            cRest.CrowdingMortalityRate = 0.028;
            cRest.ImmigrationPolicy = ImmigrationPolicy.Restricted;
            var dRestHold = policy.Decide(builder.Build(wRest, 0), cRest.Mandate);
            Assert.Check(!dRestHold.Orders.Any(o => o.Kind == OrderKind.ImmigrationPolicy),
                "AI Elder must hold Restricted inside deadband hysteresis.");
        }

        private static void T2_F12_02()
        {
            var policy = new HeuristicTribePolicy();
            var builder = new AgentObservationBuilder();

            // Nutrition drop reverts Open -> Neutral
            var wNutr = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true);
            var cN = wNutr.Camps[0];
            cN.CivicStratification = -0.50;
            cN.CrowdingMortalityRate = 0.010;
            cN.FoodNeedsMetRatio = 0.80;
            cN.ImmigrationPolicy = ImmigrationPolicy.Open;
            var decN = policy.Decide(builder.Build(wNutr, 0), cN.Mandate);
            Assert.Check(decN.Orders.Any(o => o.Kind == OrderKind.ImmigrationPolicy && o.Target == (int)ImmigrationPolicy.Neutral),
                "Nutrition drop below 0.85 must revert Open -> Neutral.");

            // Crowding spike reverts Open -> Neutral
            var wCrowd = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true);
            var cC = wCrowd.Camps[0];
            cC.CivicStratification = -0.50;
            cC.CrowdingMortalityRate = 0.035;
            cC.FoodNeedsMetRatio = 0.98;
            cC.ImmigrationPolicy = ImmigrationPolicy.Open;
            var decC = policy.Decide(builder.Build(wCrowd, 0), cC.Mandate);
            Assert.Check(decC.Orders.Any(o => o.Kind == OrderKind.ImmigrationPolicy && o.Target == (int)ImmigrationPolicy.Neutral),
                "Crowding spike above 0.030 must revert Open -> Neutral.");
        }

        private static void T2_F12_03()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, width: 20, height: 14);
            var live = w.Camps[0];
            live.CivicStratification = 0.0;
            live.ImmigrationPolicy = ImmigrationPolicy.Neutral;

            var dead = WorldFixtures.AddVillage(w, 0, w.Map.At(4, 4), 0, housingCoverage: 0.0, foodYears: 0.0);
            dead.CivicStratification = -0.80;
            dead.FoodNeedsMetRatio = 1.0;

            var rival = WorldFixtures.AddVillage(w, 1, w.Map.At(12, 7), 1000);
            rival.CivicStratification = -0.80;
            rival.FoodNeedsMetRatio = 1.0;
            Ownership.Bind(w);

            var obs0 = new AgentObservationBuilder().Build(w, 0);
            var dec0 = new HeuristicTribePolicy().Decide(obs0, live.Mandate);
            Assert.Check(!dec0.Orders.Any(o => o.Entity == dead.Id), "AI Elder must never issue orders for dead (Population==0) camp.");
            Assert.Check(!dec0.Orders.Any(o => o.Entity == rival.Id), "AI Elder must never target rival camp.");
        }

        private static void T2_F12_04()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true);
            var c = w.Camps[0];
            c.CivicStratification = -0.60;
            c.CrowdingMortalityRate = 0.01;
            c.FoodNeedsMetRatio = 1.0;
            c.ImmigrationPolicy = ImmigrationPolicy.Open; // already at desired!

            var dec = new HeuristicTribePolicy().Decide(new AgentObservationBuilder().Build(w, 0), c.Mandate);
            Assert.Check(!dec.Orders.Any(o => o.Kind == OrderKind.ImmigrationPolicy),
                "AI Elder must emit ZERO redundant ImmigrationPolicy order when camp already matches desired policy.");
        }

        private static void T2_F12_05()
        {
            var policy = new HeuristicTribePolicy();
            var builder = new AgentObservationBuilder();
            var states = new (double P, double Crowd, double Nutr, ImmigrationPolicy Pol)[]
            {
                (-0.70, 0.008, 0.99, ImmigrationPolicy.Neutral),
                (-0.40, 0.020, 0.92, ImmigrationPolicy.Open),
                (-0.20, 0.035, 0.70, ImmigrationPolicy.Open),
                (0.00, 0.020, 0.95, ImmigrationPolicy.Neutral),
                (0.55, 0.045, 0.95, ImmigrationPolicy.Neutral),
                (0.60, 0.040, 0.90, ImmigrationPolicy.Restricted),
                (0.10, 0.010, 0.95, ImmigrationPolicy.Restricted),
                (-0.85, 0.005, 1.00, ImmigrationPolicy.Restricted)
            };

            foreach (var st in states)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true);
                var c = w.Camps[0];
                c.CivicStratification = st.P;
                c.CrowdingMortalityRate = st.Crowd;
                c.FoodNeedsMetRatio = st.Nutr;
                c.ImmigrationPolicy = st.Pol;

                var obs = builder.Build(w, 0);
                var masks = LegalActions.Build(obs).ToDictionary(m => m.Entity);
                var dec = policy.Decide(obs, c.Mandate);
                foreach (var ord in dec.Orders)
                {
                    string err = OrderRules.Validate(obs, ord);
                    Assert.Check(err == null, $"AI order {ord} failed OrderRules.Validate: {err}");
                    Assert.Check(masks.TryGetValue(ord.Entity, out var mask) && mask.Allows(ord),
                        $"AI order {ord} not allowed by LegalActions mask.");
                }
            }
        }
    }
}
