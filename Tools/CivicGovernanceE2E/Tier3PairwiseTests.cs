using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

namespace Anthropocene.CivicGovernanceE2E
{
    internal static class Tier3PairwiseTests
    {
        public static List<E2ETestCase> All()
        {
            return new List<E2ETestCase>
            {
                new E2ETestCase("T3-01", 3, "F01", "F01xF11: Opt-in toggle x contracts & legacy XML persistence", M2Status.PassOnM2, T3_01),
                new E2ETestCase("T3-02", 3, "F02", "F02xF03: Environmental attractor x opposing steering + alignment friction", M2Status.FailUntilM3, T3_02),
                new E2ETestCase("T3-03", 3, "F02", "F02xF08: Environmental complexity attractor x precedent ceiling progression", M2Status.FailUntilM3, T3_03),
                new E2ETestCase("T3-04", 3, "F03", "F03xF11: Multi-policy orders x PlanningDraft invalidation x ObservationDto x SaveCodec", M2Status.PassOnM2, T3_04),
                new E2ETestCase("T3-05", 3, "F04", "F04xF06: SmoothCalendar time compression x CivicCapital buildout & amortization", M2Status.FailUntilM3, T3_05),
                new E2ETestCase("T3-06", 3, "F04", "F04xF08: SmoothCalendar x Tier-1 permanence ramp & trade EMA unlocks", M2Status.PassOnM2, T3_06),
                new E2ETestCase("T3-07", 3, "F05", "F05xF06: Steward workforce x CivicCapital Builder/Material/Tool pipeline", M2Status.FailUntilM3, T3_07),
                new E2ETestCase("T3-08", 3, "F05", "F05xF08: Steward role handling across Fission vs Immigration", M2Status.FailUntilM3, T3_08),
                new E2ETestCase("T3-09", 3, "F06", "F06xF07: Two-lock crowding mitigation x continuous P trade-off profile", M2Status.FailUntilM3, T3_09),
                new E2ETestCase("T3-10", 3, "F07", "F07xF09: Welfare-only Q_civic x DemandControl KernelSplit & PhysicalDemography isolation", M2Status.FailUntilM3, T3_10),
                new E2ETestCase("T3-11", 3, "F07", "F07xF08: Tier-3A TokensAndAccounting x superlinear Steward flattening & trade capacity", M2Status.FailUntilM3, T3_11),
                new E2ETestCase("T3-12", 3, "F03", "F03xF07: ImmigrationPolicy x P-pull x open-frontier exit mismatch + AuditBounds", M2Status.FailUntilM3, T3_12),
                new E2ETestCase("T3-13", 3, "F08", "F08xF09: Fission daughter precedent inheritance x survival-first DemandControl", M2Status.FailUntilM3, T3_13),
                new E2ETestCase("T3-14", 3, "F06", "F06xF10: Phase-1 crowding (M_c) x Phase-2 sanitation (M_d) wall-break comparison", M2Status.FailUntilM4, T3_14),
                new E2ETestCase("T3-15", 3, "F09", "F09xF10: Survival-first DemandControl x Phase-2 sanitation under drought/famine", M2Status.FailUntilM4, T3_15),
                new E2ETestCase("T3-16", 3, "F10", "F10xF07: Phase-2 sanitation x Stratified mobilization (M_build) & Tier-3 irrigation conservation", M2Status.FailUntilM4, T3_16),
                new E2ETestCase("T3-17", 3, "F10", "F10xF11: Phase-2 sanitation x SaveCodec roundtrip x ObservationDto x AuditBounds", M2Status.FailUntilM4, T3_17),
                new E2ETestCase("T3-18", 3, "F12", "F12xF02xF03: AI Elder endogenous agency x attractors x hysteresis & BuildForValidation parity", M2Status.FailUntilM3, T3_18)
            };
        }

        private static void T3_01()
        {
            var wDis = WorldFixtures.CreatePlainsWorld(population: 800, civicEnabled: false, fission: false, immigration: false);
            var wEn = WorldFixtures.CloneWorld(wDis);
            wEn.Config.Civic = new CivicGovernanceSettings { Enabled = true, SanitationEnabled = true };
            wEn.Config.Validate();

            var obsDis = new AgentObservationBuilder().Build(wDis, 0);
            var obsEn = new AgentObservationBuilder().Build(wEn, 0);
            var maskDis = LegalActions.Build(obsDis).Single(m => m.Entity == wDis.Camps[0].Id);
            var maskEn = LegalActions.Build(obsEn).Single(m => m.Entity == wEn.Camps[0].Id);

            Assert.Check(!maskDis.Actions.Any(a => a.Kind == OrderKind.ImmigrationPolicy || a.Kind == OrderKind.CivicFocus),
                "Disabled arm must keep legacy action mask without civic policy orders.");
            Assert.Check(maskEn.Actions.Count(a => a.Kind == OrderKind.ImmigrationPolicy) == 3
                && maskEn.Actions.Count(a => a.Kind == OrderKind.CivicFocus) == 3,
                "Enabled arm must expose all 3 ImmigrationPolicy and all 3 CivicFocus actions.");

            var dtoDis = ObservationContract.Deserialize(ObservationContract.Serialize(obsDis, wDis.Camps[0].Mandate));
            var dtoEn = ObservationContract.Deserialize(ObservationContract.Serialize(obsEn, wEn.Camps[0].Mandate));
            Assert.Check(dtoDis.Schema == 23 && !dtoDis.CivicGovernanceEnabled, "Disabled DTO reports Schema 23 & CivicGovernanceEnabled=false.");
            Assert.Check(dtoEn.Schema == 23 && dtoEn.CivicGovernanceEnabled && dtoEn.SanitationEnabled, "Enabled DTO reports civic flags.");

            var recDis = WorldFixtures.StepTurn(wDis);
            var recEn = WorldFixtures.StepTurn(wEn);
            Assert.Check(recDis.CivicReports == null && recEn.CivicReports != null, "CivicReports presence matches opt-in toggle.");

            var roundClean = SaveCodec.Load(SaveCodec.Save(wDis));
            var roundDis = SaveCodec.Load(SaveXmlTools.StripCivicXmlElements(SaveCodec.Save(wDis)));
            Assert.Check(SaveCodec.Fingerprint(roundDis) == SaveCodec.Fingerprint(roundClean),
                "Disabled arm stripped legacy XML must roundtrip bit-identically to standard SaveCodec roundtrip.");
            Assert.Check(roundDis.Config.Civic == null && roundDis.Camps[0].Population == wDis.Camps[0].Population,
                "Disabled arm legacy XML roundtrip preserves Civic==null and exact state.");
        }

        private static void T3_02()
        {
            var wCoerced = WorldFixtures.CreatePlainsWorld(population: 1400, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 800.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var wAligned = WorldFixtures.CloneWorld(wCoerced);

            var cCoerced = wCoerced.Camps[0];
            var cAligned = wAligned.Camps[0];
            cCoerced.CivicComplexity = 0.60;
            cCoerced.LastEnvironmentalComplexity = 0.60;
            cAligned.CivicComplexity = 0.60;
            cAligned.LastEnvironmentalComplexity = 0.60;
            cCoerced.CivicCapital = 200.0;
            cAligned.CivicCapital = 200.0;
            cCoerced.Inventory.HousingMaterial = 6000.0;
            cAligned.Inventory.HousingMaterial = 6000.0;
            cCoerced.Inventory.Tools = 6000.0;
            cAligned.Inventory.Tools = 6000.0;

            var rCoerced = new TurnResolver();
            var rAligned = new TurnResolver();
            var steerOrders = new Dictionary<int, List<Order>>
            {
                [cCoerced.Player] = new List<Order>
                {
                    new Order(OrderKind.FissionPolicy, cCoerced.Id, (int)FissionPolicy.Discourage),
                    new Order(OrderKind.ImmigrationPolicy, cCoerced.Id, (int)ImmigrationPolicy.Restricted),
                    new Order(OrderKind.CivicFocus, cCoerced.Id, (int)CivicFocus.Centralized)
                }
            };

            double maintCoerced = 0.0, maintAligned = 0.0;
            for (int t = 0; t < 8; t++)
            {
                var recC = WorldFixtures.StepTurn(wCoerced, rCoerced, t == 0 ? steerOrders : null);
                var recA = WorldFixtures.StepTurn(wAligned, rAligned);
                maintCoerced += recC.Production.Where(ProductionReceiptHelpers.IsCivicMaintenance).Sum(r => r.Output);
                maintAligned += recA.Production.Where(ProductionReceiptHelpers.IsCivicMaintenance).Sum(r => r.Output);
            }

            Assert.Check(cCoerced.LastEnvironmentalStratification < -0.35, $"Plain P_env must stay Egalitarian ({cCoerced.LastEnvironmentalStratification:F3}).");
            Assert.Check(cCoerced.CivicStratification > 0.0, $"Centralized steering trio over 8 turns must drag P positive ({cCoerced.CivicStratification:F3}).");
            double friction = CivicGovernance.AlignmentFriction(wCoerced, cCoerced);
            Assert.Check(friction > 1.20, $"Coerced camp must incur AlignmentFriction > 1.20 (got {friction:F3}).");
            Assert.Check(CivicGovernance.ExitMismatch(cCoerced) > 0.25, $"Coerced open-plain camp must incur ExitMismatch > 0.25 (got {CivicGovernance.ExitMismatch(cCoerced):F3}).");
            Assert.Check(CultivationEconomy.Workers(cCoerced, WorkforceRoles.Steward) > CultivationEconomy.Workers(cAligned, WorkforceRoles.Steward)
                && maintCoerced > maintAligned,
                $"Coerced camp must staff more Stewards ({CultivationEconomy.Workers(cCoerced, WorkforceRoles.Steward)} vs {CultivationEconomy.Workers(cAligned, WorkforceRoles.Steward)}) and spend more CivicMaintenance ({maintCoerced:F2} vs {maintAligned:F2}).");
        }

        private static void T3_03()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1400, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 1000.0);
            var c = w.Camps[0];
            c.People.CivicPrecedents.Clear();
            c.People.CivicExperience.Clear();
            c.CivicComplexity = 0.0;
            c.CivicCapital = 0.0;

            var resolver = new TurnResolver();
            bool sawZeroBeforeT1 = true;
            bool sawTier1Cap = false;
            bool unlockedT1 = false;
            bool unlockedT2 = false;
            for (int t = 0; t < 16; t++)
            {
                bool hadT1Before = c.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration);
                bool hadT2Before = c.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization);
                if (!hadT1Before && c.CivicComplexity > 1e-12) sawZeroBeforeT1 = false;
                if (hadT1Before && !hadT2Before && c.CivicComplexity > 0.0 && c.CivicComplexity <= 0.30 + 1e-12) sawTier1Cap = true;

                WorldFixtures.StepTurn(w, resolver);

                if (c.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration)) unlockedT1 = true;
                if (c.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization)) unlockedT2 = true;
                if (unlockedT1 && !unlockedT2)
                    Assert.Check(c.CivicComplexity <= 0.30 + 1e-12, $"Before Tier 2, C must stay capped at <= 0.30 (got {c.CivicComplexity:F4}).");
            }

            Assert.Check(sawZeroBeforeT1, "C must remain strictly 0 before Tier 1 unlocks.");
            Assert.Check(unlockedT1 && sawTier1Cap, "Tier 1 must unlock and cap C <= 0.30 until Tier 2 unlocks.");
            Assert.Check(unlockedT2 && c.CivicComplexity > 0.35,
                $"Automatic Steward staffing must organically unlock Tier 2 and push C > 0.35 (unlockedT2={unlockedT2}, C={c.CivicComplexity:F4}).");
        }

        private static void T3_04()
        {
            var w = WorldFixtures.CreatePlainsWorld(population: 1000, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                precedents: new[] { CivicPrecedents.CeremonialIntegration });
            WorldFixtures.AddVillage(w, 1, w.Map.At(5, 5), 600, precedents: new[] { CivicPrecedents.CeremonialIntegration });
            WorldFixtures.CultivateFootprint(w, w.Camps[1]);
            WorldFixtures.ValidateWorld(w);

            var c0 = w.Camps[0];
            var draft = TurnPlanner.Draft(w, excludedPlayer: 0);
            var econ = EconomicIntent.From(c0);
            econ.AutomationTurns = 0;
            econ.Assignments = new List<PopulationAssignment>
            {
                new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = 150 },
                new PopulationAssignment { Role = WorkforceRoles.Steward, Population = 18 }
            };
            var p0Orders = new List<Order>
            {
                new Order(OrderKind.Economy, c0.Id) { Economy = econ },
                new Order(OrderKind.FissionPolicy, c0.Id, (int)FissionPolicy.Discourage),
                new Order(OrderKind.ImmigrationPolicy, c0.Id, (int)ImmigrationPolicy.Restricted),
                new Order(OrderKind.CivicFocus, c0.Id, (int)CivicFocus.Centralized)
            };

            var orders = new Dictionary<int, List<Order>>(draft.Ai.Orders);
            orders[0] = p0Orders;
            var prep = TurnPlanner.Prepare(w, orders, draft.Ai.Traces, null, draft);
            new TurnResolver().Resolve(w, prep);
            TurnResolver.Invariants(w);

            var reloaded = SaveCodec.Load(SaveCodec.Save(w));
            Assert.Check(SaveCodec.Fingerprint(reloaded) == SaveCodec.Fingerprint(w), "SaveCodec roundtrip matches after draft+multi-policy turn.");

            var obs = new AgentObservationBuilder().Build(reloaded, 0);
            var dto = ObservationContract.Deserialize(ObservationContract.Serialize(obs, reloaded.Camps[0].Mandate));
            var so = dto.Settlements.Single(s => s.Id == c0.Id);
            Assert.Check(so.FissionPolicy == FissionPolicy.Discourage
                && so.ImmigrationPolicy == ImmigrationPolicy.Restricted
                && so.CivicFocus == CivicFocus.Centralized
                && so.StewardPopulation > 0,
                "Multi-policy + Economy commits must survive Draft -> Prepare -> Resolve -> SaveCodec -> ObservationDto.");
        }

        private static void T3_05()
        {
            (double Cap, double Cov, double Mc, int Pop) RunSubdivision(int steps, double stepYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, sanitationEnabled: false, fission: false, immigration: false,
                    landBiomassMultiplier: 1000.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration });
                WorldFixtures.SetFixedStepYears(w, stepYears);
                var c = w.Camps[0];
                c.CivicComplexity = 0.25;
                c.LastEnvironmentalComplexity = 0.25;
                c.CivicCapital = 0.0;
                var resolver = new TurnResolver();
                for (int i = 0; i < steps; i++)
                    WorldFixtures.StepTurn(w, resolver);
                return (c.CivicCapital, CivicGovernance.CivicCoverage(w, c), PhysicalDemography.CrowdingMitigation(w, c), c.Population);
            }

            var r20 = RunSubdivision(1, 20.0);
            var r5 = RunSubdivision(4, 5.0);
            var r1 = RunSubdivision(20, 1.0);

            Assert.Check(r20.Cap > 15.0 && r5.Cap > 15.0 && r1.Cap > 15.0,
                $"All 3 step sizes must build substantial CivicCapital (20y={r20.Cap:F1}, 5y={r5.Cap:F1}, 1y={r1.Cap:F1}).");
            Assert.Check(r20.Cov > 0.60 && r5.Cov > 0.60 && r1.Cov > 0.60,
                $"All 3 step sizes must achieve CivicCoverage > 0.60 (20y={r20.Cov:F3}, 5y={r5.Cov:F3}, 1y={r1.Cov:F3}).");
            Assert.Check(r20.Mc < 0.70 && r5.Mc < 0.70 && r1.Mc < 0.70,
                $"All 3 step sizes must actively cut live CrowdingMitigation (20y={r20.Mc:F3}, 5y={r5.Mc:F3}, 1y={r1.Mc:F3}).");
            Assert.NearRelative(r1.Pop, r20.Pop, 0.15, "20x1y ending population stays consistent with 1x20y (zero 1-yr collapse).");
        }

        private static void T3_06()
        {
            (double T1Exp, double TradeVol, double T3AExp) Run(int steps, double stepYears)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 1400, civicEnabled: true, fission: false, immigration: false);
                var c = w.Camps[0];
                c.StationaryYears = 0.0;
                c.StationaryTurns = 1;
                c.RecentTradeVolume = 30.0;
                c.People.CivicPrecedents.Clear();
                c.People.CivicExperience.Clear();
                for (int i = 0; i < steps; i++)
                {
                    c.StationaryYears += stepYears;
                    c.StationaryTurns++;
                    var rec = new TurnRecord();
                    rec.Trades.Add(new TradeRecord
                    {
                        Requester = c.Id,
                        Responder = c.Id + 777,
                        Want = TradeGood.Cloth,
                        WantQuantity = 4.0 * stepYears,
                        Give = TradeGood.Cloth,
                        GiveQuantity = 4.0 * stepYears
                    });
                    CivicGovernance.Close(w, rec, stepYears);
                }
                double t1 = c.People.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double v1) ? v1 : 0.0;
                double tradeVol = c.RecentTradeVolume;
                // Also verify Tier-3A accrual consistency given Tier-2 unlocked at constant trade flow
                c.People.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
                WorldFixtures.AssignExactStewards(c, 40);
                var rec3 = new TurnRecord();
                rec3.Trades.Add(new TradeRecord
                {
                    Requester = c.Id,
                    Responder = c.Id + 777,
                    Want = TradeGood.Cloth,
                    WantQuantity = 4.0 * stepYears,
                    Give = TradeGood.Cloth,
                    GiveQuantity = 4.0 * stepYears
                });
                c.RecentTradeVolume = 80.0; // exact steady-state for 4*20=80 PY/yr flow -> invariant across stepYears!
                CivicGovernance.Close(w, rec3, stepYears);
                double t3a = c.People.CivicExperience.TryGetValue(CivicPrecedents.TokensAndAccounting, out double v3) ? v3 : 0.0;
                return (t1, tradeVol, t3a / stepYears);
            }

            var a = Run(2, 20.0);
            var b = Run(8, 5.0);
            Assert.NearRelative(a.T1Exp, b.T1Exp, 1e-9, "Tier-1 permanence ramp 2x20y == 8x5y.");
            Assert.NearRelative(a.TradeVol, b.TradeVol, 1e-9, "Trade EMA 2x20y == 8x5y.");
            Assert.NearRelative(a.T3AExp, b.T3AExp, 1e-9, "Annualized Tier-3A accrual matches across 2x20y vs 8x5y.");
        }

        private static void T3_07()
        {
            var w = WorldFixtures.CreateValleyWorld(population: 1400, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 1200.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var c = w.Camps[0];
            c.CivicComplexity = 0.55;
            c.LastEnvironmentalComplexity = 0.55;
            c.CivicStratification = 0.15; // Civic Steward initially -> drifts past +0.30 into Temple Scribe!
            c.CivicCapital = 40.0;
            c.Inventory.HousingMaterial = 4000.0;
            c.Inventory.Tools = 4000.0;

            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, c) == "Civic Steward", "Starts as Civic Steward at P=0.15.");
            double hmBefore = c.Inventory.HousingMaterial;
            double toolsBefore = c.Inventory.Tools;

            var resolver = new TurnResolver();
            TurnRecord last = null;
            for (int t = 0; t < 4; t++)
                last = WorldFixtures.StepTurn(w, resolver);

            int stewards = CultivationEconomy.Workers(c, WorkforceRoles.Steward);
            Assert.Check(c.CivicCapital > 80.0, $"Pipeline must build CivicCapital > 80 (got {c.CivicCapital:F2}).");
            Assert.Check(stewards > 20, $"Pipeline must staff Stewards > 20 (got {stewards}).");
            Assert.Check(CivicGovernance.CivicCoverage(w, c) > 0.75, $"Live CivicCoverage must exceed 0.75 (got {CivicGovernance.CivicCoverage(w, c):F3}).");
            Assert.Check(c.Inventory.HousingMaterial < hmBefore && c.Inventory.Tools < toolsBefore, "Real HousingMaterial and Tools must be consumed.");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, c) == "Temple Scribe",
                $"As valley P drifts >= +0.30 (P={c.CivicStratification:F3}), Steward label must adapt to Temple Scribe.");
            Assert.Check(last != null && c.Workforce.Assigned <= c.Population, "Workforce conservation holds.");
        }

        private static void T3_08()
        {
            // Part A: Fission converts departing Stewards to Gatherers (daughter Stewards == 0)
            var wFis = WorldFixtures.CreatePlainsWorld(population: 1600, civicEnabled: true, fission: true, immigration: false, width: 22, height: 16,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var parent = wFis.Camps[0];
            parent.CivicComplexity = 0.70;
            parent.CivicCapital = CivicGovernance.RequiredCivicCapital(wFis, parent);
            WorldFixtures.AssignExactStewards(parent, 120);
            // Distress fission (food-short parent with portable shelter for the leaving group): crowding alone no longer
            // splits a parent whose M3 mitigation (M_c ~ 0.1-0.2) beats an unmitigated, unhoused daughter.
            parent.FissionPolicy = FissionPolicy.Neutral;
            parent.PlantFood = 0.0;
            parent.Inventory.LightHousingUnits = 600.0;
            WorldFixtures.SlashHomeFootprint(wFis, parent, 0.02f);
            WorldFixtures.ValidateWorld(wFis);

            Assert.Check(PhysicalDemography.CrowdingMitigation(wFis, parent) < 0.30,
                $"M3 CrowdingMitigation must be live on active civic parent (got {PhysicalDemography.CrowdingMitigation(wFis, parent):F4}).");

            var recFis = new TurnRecord { Turn = wFis.Turn, StartDays = wFis.Days, Duration = wFis.Config.TurnDays };
            Fission.Resolve(wFis, recFis, new PhaseExecution(), new ResolutionMetrics());
            Assert.Check(wFis.Camps.Count == 2, "Expected fission daughter.");
            var daughter = wFis.Camps.Single(x => x.Id != parent.Id);
            Assert.Check(CultivationEconomy.Workers(daughter, WorkforceRoles.Steward) == 0,
                $"Fission daughter must convert departing Stewards to Gatherers (got {CultivationEconomy.Workers(daughter, WorkforceRoles.Steward)}).");

            // Part B: Immigration preserves Stewards on arrival into an existing civic destination (clean placement, zero src.Tile teleportation).
            var cfgImm = WorldFixtures.BaseConfig(width: 24, height: 16, players: 2, civicEnabled: true,
                fission: false, immigration: true, trade: false, landBiomassMultiplier: 800.0);
            var wImm = WorldFixtures.CreateEmptyWorld(cfgImm, seed: 308);
            for (int i = 0; i < wImm.Tiles.Length; i++)
                wImm.Tiles[i] = WorldFixtures.MakeFertileTile(i, cfgImm);

            var precsImm = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
            var src = WorldFixtures.AddVillage(wImm, 0, wImm.Map.At(10, 8), 1600, housingCoverage: 0.15, foodYears: 3.0, precedents: precsImm);
            src.CivicComplexity = 0.70;
            src.CivicStratification = 0.80;
            src.LastEnvironmentalStratification = -0.50;
            src.CivicCapital = CivicGovernance.RequiredCivicCapital(wImm, src);
            WorldFixtures.AssignExactStewards(src, 320);

            var dst = WorldFixtures.AddVillage(wImm, 1, wImm.Map.At(13, 8), 600, housingCoverage: 1.5, foodYears: 20.0, precedents: precsImm);
            dst.CivicComplexity = 0.70;
            dst.CivicStratification = -0.80;
            dst.LastEnvironmentalStratification = -0.80;
            dst.ImmigrationPolicy = ImmigrationPolicy.Open;
            dst.CivicCapital = CivicGovernance.RequiredCivicCapital(wImm, dst);
            WorldFixtures.AssignExactStewards(dst, 20);

            WorldFixtures.CultivateFootprint(wImm, src, capital: 0.5);
            WorldFixtures.CultivateFootprint(wImm, dst, capital: 2.0);
            WorldFixtures.EstablishContacts(wImm, src, dst);
            WorldFixtures.ValidateWorld(wImm);

            int dstStewBefore = CultivationEconomy.Workers(dst, WorkforceRoles.Steward);
            var recImm = new TurnRecord { Turn = wImm.Turn, StartDays = wImm.Days, Duration = wImm.Config.TurnDays };
            Immigration.Resolve(wImm, recImm, new PhaseExecution(), new ResolutionMetrics());
            int migrants = recImm.Migrations.Where(m => m.SourceCamp == src.Id && m.DestinationCamp == dst.Id).Sum(m => m.Migrants);
            Assert.Check(migrants > 0, $"Expected immigration from stressed source to Open Egalitarian destination (migrants={migrants}).");
            Assert.Check(CultivationEconomy.Workers(dst, WorkforceRoles.Steward) > dstStewBefore,
                $"Migrating Stewards must preserve their role on arrival ({CultivationEconomy.Workers(dst, WorkforceRoles.Steward)} > {dstStewBefore}).");
        }

        private static void T3_09()
        {
            WorldState Build(double p, bool strat)
            {
                var w = WorldFixtures.CreatePlainsWorld(population: 2400, civicEnabled: true, sanitationEnabled: false,
                    fission: false, immigration: false, landBiomassMultiplier: 1200.0,
                    precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
                WorldFixtures.SetFixedStepYears(w, 5.0);
                var c = w.Camps[0];
                c.CivicComplexity = 0.75;
                c.LastEnvironmentalComplexity = 0.75;
                c.CivicStratification = p;
                c.LastEnvironmentalStratification = p;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
                c.FissionPolicy = strat ? FissionPolicy.Discourage : FissionPolicy.Encourage;
                c.ImmigrationPolicy = strat ? ImmigrationPolicy.Restricted : ImmigrationPolicy.Open;
                c.CivicFocus = strat ? CivicFocus.Centralized : CivicFocus.Communal;
                return w;
            }

            var wEgal = Build(-0.85, strat: false);
            var wStrat = Build(0.85, strat: true);
            var rE = new TurnResolver();
            var rS = new TurnResolver();
            for (int t = 0; t < 2; t++)
            {
                WorldFixtures.StepTurn(wEgal, rE);
                WorldFixtures.StepTurn(wStrat, rS);
            }

            var cE = wEgal.Camps[0];
            var cS = wStrat.Camps[0];
            double shareE = CultivationEconomy.Workers(cE, WorkforceRoles.Steward) / (double)cE.Population;
            double shareS = CultivationEconomy.Workers(cS, WorkforceRoles.Steward) / (double)cS.Population;

            Assert.Check(shareE > 0.01 && shareS > 0.01 && shareE < shareS,
                $"Egalitarian Steward share ({shareE:P2}) must be lower than Stratified ({shareS:P2}).");
            Assert.Check(cE.QoL > cS.QoL, $"Egalitarian QoL ({cE.QoL:F4}) must exceed Stratified QoL ({cS.QoL:F4}).");
            Assert.Check(NeolithicProduction.NaturalStorageCapacity(wStrat, cS, 0) > NeolithicProduction.NaturalStorageCapacity(wEgal, cE, 0) * 1.15,
                "Stratified must achieve higher NaturalStorageCapacity.");
            var canalRecipe = NeolithicProduction.Recipes(wStrat.Config).Single(r => r.Good == CraftGood.CanalConstruction);
            Assert.Check(SpecialistProductivity.CraftRate(wStrat, cS, canalRecipe, WorkforceRoles.Builder) >
                SpecialistProductivity.CraftRate(wEgal, cE, canalRecipe, WorkforceRoles.Builder) * 1.15,
                "Stratified must achieve faster public-works Builder CraftRate.");
        }

        private static void T3_10()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting };

            // Part 1: KernelSplit at N=6,000 with ample food must fund non-zero Protected clothing & shelter (no tie-collapse)
            var wKernel = WorldFixtures.CreatePlainsWorld(population: 6000, civicEnabled: true, sanitationEnabled: false,
                fission: false, immigration: false, trade: false, landBiomassMultiplier: 1200.0, precedents: precs);
            wKernel.Config.ElderKernelSplit = true;
            wKernel.Config.ElderSplitObjective = 2;
            var cKernel = wKernel.Camps[0];
            cKernel.CivicComplexity = 1.0;
            cKernel.LastEnvironmentalComplexity = 1.0;
            cKernel.CivicStratification = 0.80;
            cKernel.LastEnvironmentalStratification = 0.80;
            cKernel.CivicCapital = CivicGovernance.RequiredCivicCapital(wKernel, cKernel) * 2.0;
            cKernel.PlantFood = 6000 * PhysicalDemography.FoodPerPersonYear(wKernel.Config) * 35.0;
            cKernel.Inventory.Cloth = 0.0;
            cKernel.Inventory.HousingMaterial = 12000.0;
            cKernel.Inventory.Tools = 12000.0;
            wKernel.FixedHousing.Clear();
            NeolithicProduction.FixedHousingChanged(wKernel);
            NeolithicProduction.AddFixedHousing(wKernel, cKernel, Math.Ceiling(6000 * 0.70 / wKernel.Config.Neolithic.HousingPeoplePerUnit), wKernel.Days);
            WorldFixtures.AssignExactStewards(cKernel, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wKernel, cKernel)) + 10);

            Assert.Check(NeolithicProduction.SocialCohesion(wKernel, cKernel, 6000) > 0.75,
                $"Boosted SocialCohesion at N=6,000 must exceed 0.75 (got {NeolithicProduction.SocialCohesion(wKernel, cKernel, 6000):E3}).");
            var recKernel = WorldFixtures.StepTurn(wKernel);
            double clothOut = recKernel.Production.Where(r => r.Good == CraftGood.Cloth).Sum(r => r.Output);
            double houseOut = recKernel.Production.Where(r => r.Good == CraftGood.Housing).Sum(r => r.Output);
            Assert.Check(clothOut > 0.0 && houseOut > 0.0,
                $"KernelSplit at N=6,000 must actively produce Protected cloth ({clothOut:F1}) and shelter ({houseOut:F1}).");
            Assert.Check(cKernel.ClothPerCapita > 0.70 && cKernel.HousingPerCapita > 0.70,
                $"KernelSplit at N=6,000 must keep Protected clothing & shelter active (cloth={cKernel.ClothPerCapita:F3}, housing={cKernel.HousingPerCapita:F3}).");

            // Part 2: Live two-arm StepTurn Q_civic isolation at N=6,000 (both P=+0.80 drag and P=-0.80 lift)
            void VerifyQoLIsolation(double p, bool expectLift)
            {
                var wWithQ = WorldFixtures.CreatePlainsWorld(population: 6000, civicEnabled: true, sanitationEnabled: false,
                    fission: false, immigration: false, trade: false, landBiomassMultiplier: 1200.0, precedents: precs);
                var cWithQ = wWithQ.Camps[0];
                cWithQ.EconomyAutomationTurns = 0; // matched fixed workforce -> only Q_civic differs
                cWithQ.CivicComplexity = 1.0;
                cWithQ.LastEnvironmentalComplexity = 1.0;
                cWithQ.CivicStratification = p;
                cWithQ.LastEnvironmentalStratification = p;
                cWithQ.CivicCapital = CivicGovernance.RequiredCivicCapital(wWithQ, cWithQ) * 2.0;
                cWithQ.PlantFood = 6000 * PhysicalDemography.FoodPerPersonYear(wWithQ.Config) * 35.0;
                cWithQ.Inventory.Cloth = 6000 * 4.0;
                WorldFixtures.AssignExactStewards(cWithQ, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wWithQ, cWithQ)) + 15);

                var wNoQ = WorldFixtures.CloneWorld(wWithQ);
                wNoQ.Config.Civic.QoLEqualityLinear = 0.0;
                wNoQ.Config.Civic.QoLEqualityQuadratic = 0.0;
                wNoQ.Config.Validate();
                var cNoQ = wNoQ.Camps[0];

                var recWithQ = WorldFixtures.StepTurn(wWithQ);
                var recNoQ = WorldFixtures.StepTurn(wNoQ);
                var repWithQ = recWithQ.Demographics.Single(d => d.Camp == cWithQ.Id);
                var repNoQ = recNoQ.Demographics.Single(d => d.Camp == cNoQ.Id);

                if (expectLift)
                    Assert.Check(cWithQ.QoL > cNoQ.QoL + 0.02, $"At N=6,000 P={p:F2}, Q_civic must lift QoL by > 0.02 (withQ={cWithQ.QoL:F4}, noQ={cNoQ.QoL:F4}).");
                else
                    Assert.Check(cWithQ.QoL < cNoQ.QoL - 0.02, $"At N=6,000 P={p:F2}, Q_civic must lower QoL by > 0.02 (withQ={cWithQ.QoL:F4}, noQ={cNoQ.QoL:F4}).");

                Assert.Check(cWithQ.Population == cNoQ.Population, $"N=6,000 P={p:F2} Population isolated from Q_civic.");
                Assert.Check(repWithQ.GrossBirths == repNoQ.GrossBirths, $"N=6,000 P={p:F2} GrossBirths isolated from Q_civic.");
                Assert.Check(repWithQ.GrossDeaths == repNoQ.GrossDeaths, $"N=6,000 P={p:F2} GrossDeaths isolated from Q_civic.");
                Assert.NearAbs(repWithQ.ContinuousBirths, repNoQ.ContinuousBirths, 1e-12, $"N=6,000 P={p:F2} ContinuousBirths isolated.");
                Assert.NearAbs(repWithQ.ContinuousDeaths, repNoQ.ContinuousDeaths, 1e-12, $"N=6,000 P={p:F2} ContinuousDeaths isolated.");
                Assert.NearAbs(repWithQ.CrowdingDeaths, repNoQ.CrowdingDeaths, 1e-12, $"N=6,000 P={p:F2} CrowdingDeaths isolated.");
                Assert.NearAbs(repWithQ.DiseaseDeaths, repNoQ.DiseaseDeaths, 1e-12, $"N=6,000 P={p:F2} DiseaseDeaths isolated.");
            }

            VerifyQoLIsolation(0.80, expectLift: false);
            VerifyQoLIsolation(-0.80, expectLift: true);
        }

        private static void T3_11()
        {
            // Part A: Steward share at N=6,000 Egalitarian drops sharply after Tier-3A unlock
            var wNo3A = WorldFixtures.CreatePlainsWorld(population: 6000, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 1000.0,
                precedents: new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization });
            var cNo = wNo3A.Camps[0];
            cNo.CivicComplexity = 0.75;
            cNo.LastEnvironmentalComplexity = 0.75;
            cNo.CivicStratification = -1.0;
            cNo.LastEnvironmentalStratification = -1.0;
            cNo.CivicCapital = CivicGovernance.RequiredCivicCapital(wNo3A, cNo);
            cNo.PlantFood = 6000 * PhysicalDemography.FoodPerPersonYear(wNo3A.Config) * 25.0;

            var w3A = WorldFixtures.CloneWorld(wNo3A);
            w3A.Camps[0].People.CivicPrecedents.Add(CivicPrecedents.TokensAndAccounting);

            var planNo = EconomicAllocationStrategy.Plan(wNo3A, cNo, EconomicIntent.From(cNo), out _);
            var planYes = EconomicAllocationStrategy.Plan(w3A, w3A.Camps[0], EconomicIntent.From(w3A.Camps[0]), out _);
            int stewNo = planNo.Assignments.FirstOrDefault(a => a.Role == WorkforceRoles.Steward)?.Population ?? 0;
            int stewYes = planYes.Assignments.FirstOrDefault(a => a.Role == WorkforceRoles.Steward)?.Population ?? 0;

            Assert.Check(stewNo > 500 && stewYes > 200 && stewYes < stewNo * 0.65,
                $"Tier 3A must flatten N=6,000 Egalitarian Steward staffing (before={stewNo}, after={stewYes}).");

            // Part B: Extended-range TradeRecords clear with Tier 3A
            var cfg = WorldFixtures.BaseConfig(width: 26, height: 16, players: 3, civicEnabled: true, fission: false, immigration: false, trade: true);
            cfg.TradeFoodEnabled = false;
            var wT = WorldFixtures.CreateEmptyWorld(cfg, seed: 311);
            for (int i = 0; i < wT.Tiles.Length; i++) wT.Tiles[i] = WorldFixtures.MakeFertileTile(i, cfg);
            WorldFixtures.AddVillage(wT, 0, wT.Map.At(22, 13), 100);
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting };
            var a1 = WorldFixtures.AddVillage(wT, 1, wT.Map.At(4, 7), 160, housingCoverage: 0.0, clothPerCapita: 0.0, housingMaterial: 0.0, autoEconomy: false, precedents: precs);
            a1.Inventory.Cloth = 600.0;
            var a2 = WorldFixtures.AddVillage(wT, 2, wT.Map.At(4 + cfg.TradeRange + 2, 7), 160, housingCoverage: 0.0, clothPerCapita: 0.0, housingMaterial: 0.0, autoEconomy: false, precedents: precs);
            a2.Inventory.HousingMaterial = 120.0;
            WorldFixtures.EstablishContacts(wT, a1, a2);
            WorldFixtures.ValidateWorld(wT);
            var recT = WorldFixtures.StepTurn(wT);
            Assert.Check(recT.Trades.Count > 0 && recT.Trades.Any(t => t.Distance > cfg.TradeRange),
                $"Tier 3A must clear extended-range TradeRecords at distance > {cfg.TradeRange} (count={recT.Trades.Count}).");
        }

        private static void T3_12()
        {
            bool prevAudit = SocialRelocationForecastCache.AuditBounds;
            SocialRelocationForecastCache.AuditBounds = true;
            try
            {
                long evalsBefore = SocialRelocationForecastCache.BoundEvaluations;
                var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
                var cfg = WorldFixtures.BaseConfig(width: 24, height: 16, players: 3, civicEnabled: true,
                    fission: false, immigration: true, trade: false, landBiomassMultiplier: 800.0);
                var w = WorldFixtures.CreateEmptyWorld(cfg, seed: 312);
                for (int i = 0; i < w.Tiles.Length; i++)
                    w.Tiles[i] = WorldFixtures.MakeFertileTile(i, cfg);

                int tileSrc = w.Map.At(12, 8);
                int tileRest = w.Map.At(15, 8); // distance 3 — Restricted Stratified added FIRST (lower Camp.Id wins M2 tie-break!)
                int tileOpen = w.Map.At(9, 8);  // distance 3 — Open Egalitarian added SECOND
                Assert.Check(w.Map.Distance(tileSrc, tileOpen) == w.Map.Distance(tileSrc, tileRest), "Receivers must be equidistant from source.");

                var src = WorldFixtures.AddVillage(w, 0, tileSrc, 1600, housingCoverage: 0.15, foodYears: 3.0, autoEconomy: false, precedents: precs);
                src.CivicComplexity = 0.75;
                src.CivicStratification = 0.90;
                src.LastEnvironmentalStratification = -0.60;
                src.LastCircumscription = 0.10;
                src.CivicCapital = CivicGovernance.RequiredCivicCapital(w, src) * 1.2;
                WorldFixtures.AssignExactStewards(src, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, src)) + 5);

                var restStrat = WorldFixtures.AddVillage(w, 2, tileRest, 700, housingCoverage: 1.5, foodYears: 20.0, precedents: precs);
                restStrat.CivicComplexity = 0.75;
                restStrat.CivicStratification = 0.85;
                restStrat.LastEnvironmentalStratification = 0.85;
                restStrat.ImmigrationPolicy = ImmigrationPolicy.Restricted;
                restStrat.CivicCapital = CivicGovernance.RequiredCivicCapital(w, restStrat) * 1.2;
                WorldFixtures.AssignExactStewards(restStrat, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, restStrat)) + 5);

                var openEgal = WorldFixtures.AddVillage(w, 1, tileOpen, 700, housingCoverage: 1.5, foodYears: 20.0, precedents: precs);
                openEgal.CivicComplexity = 0.75;
                openEgal.CivicStratification = -0.85;
                openEgal.LastEnvironmentalStratification = -0.85;
                openEgal.ImmigrationPolicy = ImmigrationPolicy.Open;
                openEgal.CivicCapital = CivicGovernance.RequiredCivicCapital(w, openEgal) * 1.2;
                WorldFixtures.AssignExactStewards(openEgal, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, openEgal)) + 5);

                WorldFixtures.CultivateFootprint(w, src, capital: 0.5);
                WorldFixtures.CultivateFootprint(w, restStrat, capital: 2.0);
                WorldFixtures.CultivateFootprint(w, openEgal, capital: 2.0);
                WorldFixtures.EstablishAllContacts(w);
                WorldFixtures.ValidateWorld(w);

                Assert.Check(PhysicalDemography.CrowdingMitigation(w, src) < 0.30, "Active M3 civics required.");
                var rec = new TurnRecord { Turn = w.Turn, StartDays = w.Days, Duration = w.Config.TurnDays };
                Immigration.Resolve(w, rec, new PhaseExecution(), new ResolutionMetrics());
                int toOpen = rec.Migrations.Where(m => m.SourceCamp == src.Id && m.DestinationCamp == openEgal.Id).Sum(m => m.Migrants);
                int toRest = rec.Migrations.Where(m => m.SourceCamp == src.Id && m.DestinationCamp == restStrat.Id).Sum(m => m.Migrants);
                int reverse = rec.Migrations.Where(m => m.DestinationCamp == src.Id).Sum(m => m.Migrants);

                Assert.Check(reverse == 0, $"Migration must never invert backwards into stressed source (reverse={reverse}).");
                Assert.Check(toOpen > 0 && toOpen > toRest,
                    $"Coerced source must send migrants primarily to Open Egalitarian receiver (toOpen={toOpen} vs toRest={toRest}).");
                Assert.Check(SocialRelocationForecastCache.BoundEvaluations > evalsBefore, "AuditBounds evaluated > 0 bounds with 0 violations.");
            }
            finally
            {
                SocialRelocationForecastCache.AuditBounds = prevAudit;
            }
        }

        private static void T3_13()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
            var w = WorldFixtures.CreatePlainsWorld(population: 1800, civicEnabled: true, fission: true, immigration: false, width: 24, height: 16,
                landBiomassMultiplier: 600.0, precedents: precs);
            var parent = w.Camps[0];
            parent.CivicComplexity = 0.70;
            parent.CivicStratification = 0.40;
            parent.CivicCapital = CivicGovernance.RequiredCivicCapital(w, parent);
            parent.FissionPolicy = FissionPolicy.Encourage;
            w.FixedHousing.Clear();
            NeolithicProduction.FixedHousingChanged(w);
            WorldFixtures.ValidateWorld(w);

            var recF = new TurnRecord { Turn = w.Turn, StartDays = w.Days, Duration = w.Config.TurnDays };
            Fission.Resolve(w, recF, new PhaseExecution(), new ResolutionMetrics());
            Assert.Check(w.Camps.Count == 2, "Expected daughter founded.");
            var daughter = w.Camps.Single(x => x.Id != parent.Id);
            Assert.NearAbs(daughter.CivicCapital, 0.0, 1e-15, "Daughter starts with CivicCapital == 0.");
            Assert.Check(daughter.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization), "Daughter inherits Tier 2.");

            // Step daughter at N=220 (< DevelopmentMinPopulation=300) -> must suppress Civic Development
            daughter.Population = 220;
            daughter.Workforce.Normalize(220);
            daughter.IsVillage = true;
            daughter.Inventory.HousingMaterial = 2000.0;
            daughter.Inventory.Tools = 2000.0;
            w.Config.FissionEnabled = false;
            WorldFixtures.CultivateFootprint(w, daughter, capital: 1.5);
            WorldFixtures.ValidateWorld(w);

            var resolver = new TurnResolver();
            WorldFixtures.StepTurn(w, resolver);
            Assert.NearAbs(daughter.CivicCapital, 0.0, 1e-12, $"Daughter below N=300 must suppress Civic Development (got {daughter.CivicCapital:F3}).");

            // Now grow daughter to N=1,200 with proportional food reserves -> immediately builds Tier-2-capable CivicCapital without re-earning Tier 1/2!
            daughter.Population = 1200;
            daughter.PlantFood = 1200 * PhysicalDemography.FoodPerPersonYear(w.Config) * 18.0;
            daughter.LastEnvironmentalComplexity = 0.70;
            NeolithicProduction.AddFixedHousing(w, daughter, 300.0, w.Days);
            WorldFixtures.ValidateWorld(w);
            WorldFixtures.StepTurn(w, resolver);
            Assert.Check(daughter.CivicCapital > 10.0,
                $"Daughter at N=1,200 must immediately build CivicCapital using inherited Tier 2 (got {daughter.CivicCapital:F2}).");
        }

        private static void T3_14()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting, CivicPrecedents.PlannedQuartersAndDrainage };
            var wPh1 = WorldFixtures.CreateValleyWorld(population: 2200, civicEnabled: true, sanitationEnabled: false, fission: false, immigration: false,
                landBiomassMultiplier: 2400.0, precedents: precs);
            var wPh2 = WorldFixtures.CreateValleyWorld(population: 2200, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 2400.0, precedents: precs);

            foreach (var w in new[] { wPh1, wPh2 })
            {
                var c = w.Camps[0];
                c.CivicComplexity = 0.85;
                c.LastEnvironmentalComplexity = 0.85;
                c.CivicStratification = 0.60;
                c.LastEnvironmentalStratification = 0.60;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
                c.Inventory.HousingMaterial = 10000.0;
                c.Inventory.Tools = 10000.0;
                c.Inventory.FarmTools = 2000.0;
            }

            var r1 = new TurnResolver();
            var r2 = new TurnResolver();
            for (int t = 0; t < 16; t++)
            {
                WorldFixtures.StepTurn(wPh1, r1);
                WorldFixtures.StepTurn(wPh2, r2);
            }

            var c1 = wPh1.Camps[0];
            var c2 = wPh2.Camps[0];
            Assert.Check(c1.Population >= 2600 && c1.Population <= 3600,
                $"Phase-1 arm must plateau at disease ceiling [2600, 3600] (got {c1.Population}).");
            Assert.NearAbs(PhysicalDemography.DiseaseMitigation(wPh1, c1), 1.0, 1e-12, "Phase-1 DiseaseMitigation == 1.0.");
            Assert.Check(c2.SanitationCapital > 0.0 && PhysicalDemography.DiseaseMitigation(wPh2, c2) < 0.35,
                $"Phase-2 arm must activate sanitation (cap={c2.SanitationCapital:F1}, M_d={PhysicalDemography.DiseaseMitigation(wPh2, c2):F3}).");
            Assert.Check(c2.Population >= 5000, $"Phase-2 arm must surge past ~3,400 wall to >= 5,000 (got {c2.Population}).");
        }

        private static void T3_15()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting, CivicPrecedents.PlannedQuartersAndDrainage };
            var w = WorldFixtures.CreateValleyWorld(population: 4500, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 5000.0, precedents: precs);
            var c = w.Camps[0];
            c.CivicComplexity = 0.90;
            c.LastEnvironmentalComplexity = 0.90;
            c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
            c.SanitationCapital = CivicGovernance.RequiredSanitationCapital(w.Config, c.Population);
            c.SanitationWaterRatio = 1.0;
            WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)));

            Assert.Check(PhysicalDemography.DiseaseMitigation(w, c) < 0.40,
                $"Sanitation must be active before famine shock (M_d={PhysicalDemography.DiseaseMitigation(w, c):F4}).");

            // Genuine labor-bound famine: uncultivated ample-biomass tiles with GatherPerPerson=1.002 & zero food reserves so Protected survival food consumes ~99.8% of workers
            c.People.NeolithicTechnologies.Remove(NeolithicTech.Plough);
            WorldFixtures.ClearFootprintCultivation(w, c);
            w.Config.GatherPerPerson = 1.002;
            w.Config.Validate();
            c.PlantFood = 0.0;
            c.AnimalFood = 0.0;
            c.AquaticFood = 0.0;
            WorldFixtures.ValidateWorld(w);

            var rec = WorldFixtures.StepTurn(w);
            double sanitBuild = rec.Production.Where(ProductionReceiptHelpers.IsSanitationConstruction).Sum(r => r.Output);
            Assert.NearAbs(sanitBuild, 0.0, 1e-9, "Labor-bound famine must not divert labor into SanitationConstruction.");
            Assert.Check(c.SanitationCapital < CivicGovernance.RequiredSanitationCapital(w.Config, 4500) * 0.90,
                $"Sanitation upkeep must shed gracefully under labor-bound famine (cap={c.SanitationCapital:F2}, req={CivicGovernance.RequiredSanitationCapital(w.Config, 4500):F2}).");
        }

        private static void T3_16()
        {
            WorldState Build(double p)
            {
                var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting, CivicPrecedents.PlannedQuartersAndDrainage };
                var w = WorldFixtures.CreateValleyWorld(population: 3500, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                    landBiomassMultiplier: 1600.0, precedents: precs);
                var c = w.Camps[0];
                c.CivicComplexity = 0.90;
                c.LastEnvironmentalComplexity = 0.90;
                c.CivicStratification = p;
                c.LastEnvironmentalStratification = p;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
                c.SanitationCapital = 0.0;
                c.Inventory.HousingMaterial = 6000.0;
                c.Inventory.HandTools = 0.0;
                c.Inventory.Tools = 6000.0;
                WorldFixtures.AssignExactStewards(c, (int)Math.Ceiling(CivicGovernance.RequiredStewards(w, c)));
                return w;
            }

            var wStrat = Build(0.90);
            var wEgal = Build(-0.90);
            var recS1 = WorldFixtures.StepTurn(wStrat);
            var recE1 = WorldFixtures.StepTurn(wEgal);

            var sReceipts = recS1.Production.Where(ProductionReceiptHelpers.IsSanitationConstruction).ToList();
            var eReceipts = recE1.Production.Where(ProductionReceiptHelpers.IsSanitationConstruction).ToList();
            Assert.Check(sReceipts.Count > 0 && eReceipts.Count > 0, "Both cities must emit SanitationConstruction receipts.");

            double sRate = sReceipts.Sum(r => r.Output) / Math.Max(1e-9, sReceipts.Sum(r => r.WorkerIntervals));
            double eRate = eReceipts.Sum(r => r.Output) / Math.Max(1e-9, eReceipts.Sum(r => r.WorkerIntervals));
            Assert.Check(sRate > eRate * 1.20, $"Stratified M_build must build SanitationCapital faster per Builder-interval (strat={sRate:F4} vs egal={eRate:F4}).");

            double sToolsPerUnit = sReceipts.Sum(r => r.ToolsConsumed) / Math.Max(1e-9, sReceipts.Sum(r => r.Output));
            double eToolsPerUnit = eReceipts.Sum(r => r.ToolsConsumed) / Math.Max(1e-9, eReceipts.Sum(r => r.Output));
            Assert.NearRelative(sToolsPerUnit, eToolsPerUnit, 1e-5, "Physical Tools consumed per SanitationCapital unit must be conserved.");

            var recS2 = WorldFixtures.StepTurn(wStrat);
            Assert.Check(recS2.SanitationWater != null && recS2.SanitationWater.Count > 0, "Turn 2 allocates conserved Tier-2 SanitationWater ahead of irrigation.");
        }

        private static void T3_17()
        {
            bool prevAudit = SocialRelocationForecastCache.AuditBounds;
            SocialRelocationForecastCache.AuditBounds = true;
            try
            {
                long evalsBefore = SocialRelocationForecastCache.BoundEvaluations;
                var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.PlannedQuartersAndDrainage };
                var w = WorldFixtures.CreateValleyWorld(population: 2600, civicEnabled: true, sanitationEnabled: true, fission: true, immigration: true,
                    width: 24, height: 16, landBiomassMultiplier: 1600.0, precedents: precs);
                var c = w.Camps[0];
                c.CivicComplexity = 0.75;
                c.LastEnvironmentalComplexity = 0.75;
                c.CivicCapital = CivicGovernance.RequiredCivicCapital(w, c);
                c.SanitationCapital = 0.0;
                c.Inventory.HousingMaterial = 6000.0;
                c.Inventory.Tools = 6000.0;

                // Add viable second camp c2 within ImmigrationRadius (distance 4 from At(12, 8)) so AuditBounds genuinely evaluates bounds
                int t2 = w.Map.At(8, 8);
                foreach (int nb in w.Map.Radius(t2, 1))
                    w.Tiles[nb] = WorldFixtures.MakeFertileTile(nb, w.Config, moisture: 0.65f, waterSource: FreshwaterSource.RainPool, maxFreshwater: 25.0f);
                var c2 = WorldFixtures.AddVillage(w, 1, t2, 700, foodYears: 20.0, precedents: precs);
                c2.ImmigrationPolicy = ImmigrationPolicy.Open;
                WorldFixtures.CultivateFootprint(w, c2, capital: 1.5);
                WorldFixtures.EstablishContacts(w, c, c2);
                WorldFixtures.ValidateWorld(w);

                // Turn 1 builds SanitationCapital (still non-retroactive)
                var rA = new TurnResolver();
                WorldFixtures.StepTurn(w, rA);
                Assert.Check(c.SanitationCapital > 0.0, $"Turn 1 must build SanitationCapital > 0 (got {c.SanitationCapital:F2}).");

                // Save & reload mid-transition before Turn 2 water activation!
                var reloaded = SaveCodec.Load(SaveCodec.Save(w));
                var rB = new TurnResolver();
                for (int t = 0; t < 2; t++)
                {
                    WorldFixtures.StepTurn(w, rA);
                    WorldFixtures.StepTurn(reloaded, rB);
                }

                Assert.Check(SaveCodec.Fingerprint(reloaded) == SaveCodec.Fingerprint(w),
                    "Mid-transition Sanitation save/reload must match uninterrupted run bit-for-bit.");
                Assert.Check(SocialRelocationForecastCache.BoundEvaluations > evalsBefore,
                    $"AuditBounds must actively evaluate relocation bounds during sanitation transition (delta={SocialRelocationForecastCache.BoundEvaluations - evalsBefore}).");

                var obs = new AgentObservationBuilder().Build(reloaded, 0);
                var dto = ObservationContract.Deserialize(ObservationContract.Serialize(obs, reloaded.Camps[0].Mandate));
                var so = dto.Settlements.Single(s => s.Id == c.Id);
                Assert.Check(so.SanitationCoverage > 0.0 && so.SanitationWaterRatio > 0.0,
                    $"ObservationDto must report active SanitationCoverage ({so.SanitationCoverage:F3}) and SanitationWaterRatio ({so.SanitationWaterRatio:F3}).");
            }
            finally
            {
                SocialRelocationForecastCache.AuditBounds = prevAudit;
            }
        }

        private static void T3_18()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
            var wPlain = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 800.0, precedents: precs);
            var wValley = WorldFixtures.CreateValleyWorld(population: 1200, civicEnabled: true, fission: false, immigration: false,
                landBiomassMultiplier: 1200.0, precedents: precs);
            var wCoast = WorldFixtures.CreateCoastalWorld(population: 900, civicEnabled: true);
            wCoast.Camps[0].People.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);

            var worlds = new[] { wPlain, wValley, wCoast };
            var builder = new AgentObservationBuilder();
            var policy = new HeuristicTribePolicy();
            var resolver = new TurnResolver();

            for (int t = 0; t < 6; t++)
            {
                foreach (var w in worlds)
                {
                    var obs = builder.Build(w, 0);
                    var valObs = builder.BuildForValidation(w, 0, new PlanningIndex(w));
                    var dec = policy.Decide(obs, w.Camps[0].Mandate);
                    foreach (var ord in dec.Orders)
                    {
                        Assert.Check(OrderRules.Validate(obs, ord) == null && OrderRules.Validate(valObs, ord) == null,
                            $"AI order {ord} must pass Build & BuildForValidation parity.");
                        Assert.Check(ord.Kind != OrderKind.CivicFocus && ord.Kind != OrderKind.FissionPolicy,
                            "AI must leave CivicFocus=Auto and FissionPolicy untouched.");
                    }
                    WorldFixtures.StepWithAi(w, resolver, excludedPlayer: -1, useDraft: true);
                }
            }

            Assert.Check(wPlain.Camps[0].CivicFocus == CivicFocus.Auto && wValley.Camps[0].CivicFocus == CivicFocus.Auto,
                "AI camps keep CivicFocus.Auto.");
            Assert.Check(wPlain.Camps[0].CivicStratification < -0.25 && wCoast.Camps[0].CivicStratification < -0.25,
                $"Plain ({wPlain.Camps[0].CivicStratification:F3}) & Coast ({wCoast.Camps[0].CivicStratification:F3}) AI diverge Egalitarian.");
            Assert.Check(wValley.Camps[0].CivicStratification > 0.25,
                $"Valley AI ({wValley.Camps[0].CivicStratification:F3}) diverges Stratified.");
            Assert.Check(wPlain.Camps[0].CivicCapital > 0.0 && wValley.Camps[0].CivicCapital > 0.0,
                $"M3 DemandControl must fund CivicCapital across AI archetypes (plain={wPlain.Camps[0].CivicCapital:F1}, valley={wValley.Camps[0].CivicCapital:F1}).");
        }
    }
}
