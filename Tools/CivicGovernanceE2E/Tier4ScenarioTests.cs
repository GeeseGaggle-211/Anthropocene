using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

namespace Anthropocene.CivicGovernanceE2E
{
    internal static class Tier4ScenarioTests
    {
        public static List<E2ETestCase> All()
        {
            return new List<E2ETestCase>
            {
                new E2ETestCase("T4-01", 4, "F06", "Trypillia Rain-Fed Mega-Site (Phase 1, 25 turns)", M2Status.FailUntilM3, T4_01),
                new E2ETestCase("T4-02", 4, "F07", "Uruk Circumscribed Irrigated Stratified City (Phase 1, 25 turns)", M2Status.FailUntilM3, T4_02),
                new E2ETestCase("T4-03", 4, "F10", "Arid Desert Oasis Hard Water Limit vs River Control (Phase 2, 12 turns)", M2Status.FailUntilM4, T4_03),
                new E2ETestCase("T4-04", 4, "F09", "Severe Famine Shock: Trypillia Dispersal vs Stratified Reserve Holdout", M2Status.FailUntilM3, T4_04),
                new E2ETestCase("T4-05", 4, "F04", "Multi-Step SmoothCalendar Campaign + Mid-Run SaveCodec Roundtrip (15 turns)", M2Status.FailUntilM3, T4_05),
                new E2ETestCase("T4-06", 4, "F08", "Frontier Fission Diaspora & Daughter Institutional Adaptation (10 turns)", M2Status.FailUntilM3, T4_06),
                new E2ETestCase("T4-07", 4, "F08", "Tier 3A Accounting & Inter-Tribal Trade Hub vs Solo Player 0 (12 turns)", M2Status.FailUntilM3, T4_07),
                new E2ETestCase("T4-08", 4, "F10", "Phase-2 Chalcolithic Metropolis (5,000-10,000+ Residents, River vs Rain-Fed)", M2Status.FailUntilM4, T4_08),
                new E2ETestCase("T4-09", 4, "F03", "Coerced Regime Against Environment (Player Steering vs Open Frontier Exit + AuditBounds)", M2Status.FailUntilM3, T4_09),
                new E2ETestCase("T4-10", 4, "F11", "Full Generated-World Multi-Tribe Autonomous Campaign (64x32, 6 players, 14 turns)", M2Status.FailUntilM3, T4_10)
            };
        }

        private static void T4_01()
        {
            var w = WorldFixtures.CreatePlainsWorld(
                population: 650,
                civicEnabled: true,
                sanitationEnabled: false,
                fission: false,
                immigration: false,
                trade: false,
                grantPlough: true,
                withResources: true,
                width: 22,
                height: 14,
                landBiomassMultiplier: 1000.0);
            var c = w.Camps[0];
            c.People.CivicPrecedents.Clear();
            c.People.CivicExperience.Clear();
            c.Inventory.HousingMaterial = 4000.0;
            c.Inventory.Tools = 4000.0;

            var resolver = new TurnResolver();
            bool sawT1 = false, sawT2 = false;
            int peakPop = c.Population;
            for (int t = 0; t < 25; t++)
            {
                WorldFixtures.StepTurn(w, resolver);
                if (c.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration)) sawT1 = true;
                if (c.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization)) sawT2 = true;
                if (c.Population > peakPop) peakPop = c.Population;
            }

            double stewShare = CultivationEconomy.Workers(c, WorkforceRoles.Steward) / (double)Math.Max(1, c.Population);
            Assert.Check(sawT1 && sawT2, $"Trypillia mega-site must organically unlock T1 and T2 (sawT1={sawT1}, sawT2={sawT2}).");
            Assert.Check(c.CivicStratification <= -0.30,
                $"Trypillia plain must drift Egalitarian to P <= -0.30 (got P={c.CivicStratification:F3}).");
            Assert.Check(c.CivicCapital > 100.0, $"Trypillia must build substantial CivicCapital (got {c.CivicCapital:F1}).");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, c) == "Council Arbiter",
                $"Trypillia Steward label must be 'Council Arbiter' at P={c.CivicStratification:F3} (got '{WorkforceRoles.Label(WorkforceRoles.Steward, c)}').");
            Assert.Check(stewShare > 0.01 && stewShare < 0.085, $"Trypillia Steward share should stay lean < 8.5% (got {stewShare:P2}).");
            Assert.Check(peakPop >= 2400 && peakPop <= 3600, $"Phase-1 Trypillia must climb into [2400, 3600] disease ceiling (peak={peakPop}, end={c.Population}).");
            Assert.NearAbs(PhysicalDemography.DiseaseMitigation(w, c), 1.0, 1e-12, "Phase-1 DiseaseMitigation == 1.0.");
            Assert.Check(c.QoL > 0.70, $"Trypillia egalitarian QoL should remain > 0.70 (got {c.QoL:F4}).");
        }

        private static void T4_02()
        {
            var w = WorldFixtures.CreateValleyWorld(
                population: 650,
                civicEnabled: true,
                sanitationEnabled: false,
                fission: false,
                immigration: false,
                trade: false,
                withResources: true,
                withCopperDeposit: true,
                width: 22,
                height: 14,
                landBiomassMultiplier: 1600.0);
            var c = w.Camps[0];
            c.People.CivicPrecedents.Clear();
            c.People.CivicExperience.Clear();
            c.Inventory.HousingMaterial = 6000.0;
            c.Inventory.Tools = 6000.0;

            var resolver = new TurnResolver();
            int peakPop = c.Population;
            for (int t = 0; t < 25; t++)
            {
                WorldFixtures.StepTurn(w, resolver);
                if (c.Population > peakPop) peakPop = c.Population;
            }

            Assert.Check(c.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration) && c.People.HasCivicPrecedent(CivicPrecedents.CivicOrganization),
                "Uruk valley city must organically unlock T1 and T2.");
            Assert.Check(c.CivicStratification > 0.30, $"Uruk circumscribed valley must drift Stratified P > +0.30 (got {c.CivicStratification:F3}).");
            Assert.Check(WorkforceRoles.Label(WorkforceRoles.Steward, c) == "Temple Scribe",
                $"Uruk Stewards must be labeled Temple Scribe (got {WorkforceRoles.Label(WorkforceRoles.Steward, c)}).");
            Assert.Check(c.CivicCapital > 150.0, $"Uruk must build CivicCapital > 150 (got {c.CivicCapital:F1}).");
            Assert.Check(CivicGovernance.PublicWorksMobilizationMultiplier(w, c) > 1.12,
                $"Uruk M_build must exceed 1.12 (got {CivicGovernance.PublicWorksMobilizationMultiplier(w, c):F3}).");
            Assert.Check(CivicGovernance.GranaryStorageMultiplier(w, c) > 1.18,
                $"Uruk M_storage must exceed 1.18 (got {CivicGovernance.GranaryStorageMultiplier(w, c):F3}).");
            Assert.Check(peakPop >= 2600 && peakPop <= 3700,
                $"Phase-1 Uruk city must climb organically into [2600, 3700] disease ceiling (peak={peakPop}, end={c.Population}).");
        }

        private static void T4_03()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting, CivicPrecedents.PlannedQuartersAndDrainage };
            var w = WorldFixtures.CreateOasisAndRiverDualWorld(oasisPop: 1800, riverPop: 1800, sanitationEnabled: true, agriculture: true,
                landBiomassMultiplier: 1000.0, precedents: precs);
            var oasis = w.Camps[0];
            var river = w.Camps[1];
            oasis.CivicComplexity = 0.80;
            oasis.LastEnvironmentalComplexity = 0.80;
            river.CivicComplexity = 0.80;
            river.LastEnvironmentalComplexity = 0.80;
            oasis.CivicCapital = CivicGovernance.RequiredCivicCapital(w, oasis);
            river.CivicCapital = CivicGovernance.RequiredCivicCapital(w, river);
            oasis.Inventory.HousingMaterial = 6000.0;
            river.Inventory.HousingMaterial = 6000.0;
            oasis.Inventory.Tools = 6000.0;
            river.Inventory.Tools = 6000.0;

            var resolver = new TurnResolver();
            int riverPeak = river.Population;
            int oasisPeak = oasis.Population;
            for (int t = 0; t < 12; t++)
            {
                WorldFixtures.StepTurn(w, resolver);
                if (river.Population > riverPeak) riverPeak = river.Population;
                if (oasis.Population > oasisPeak) oasisPeak = oasis.Population;
            }

            Assert.Check(river.SanitationCapital > 100.0 && river.SanitationWaterRatio > 0.90,
                $"River control must operate full Sanitation (cap={river.SanitationCapital:F1}, ratio={river.SanitationWaterRatio:F3}).");
            Assert.Check(riverPeak >= 3800, $"River control must surge past 3,800 (peak={riverPeak}).");
            Assert.NearAbs(oasis.SanitationCapital, 0.0, 1e-12,
                $"Arid oasis must build exact 0 dry sewers across 12 turns (got {oasis.SanitationCapital:F3}).");
            Assert.NearAbs(oasis.SanitationWaterRatio, 0.0, 1e-12, "Oasis SanitationWaterRatio must stay 0.");
            Assert.Check(oasisPeak < 3500, $"Water-starved oasis must stay capped below Phase-1 disease wall (< 3500, peak={oasisPeak}).");
        }

        private static void T4_04()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
            var wEgal = WorldFixtures.CreatePlainsWorld(population: 2200, civicEnabled: true, sanitationEnabled: false, fission: true, immigration: false,
                width: 24, height: 16, landBiomassMultiplier: 800.0, precedents: precs);
            var cEgal = wEgal.Camps[0];
            cEgal.CivicComplexity = 0.75;
            cEgal.CivicStratification = -0.80;
            cEgal.LastEnvironmentalStratification = -0.80;
            cEgal.CivicFocus = CivicFocus.Communal;
            cEgal.CivicCapital = CivicGovernance.RequiredCivicCapital(wEgal, cEgal);
            cEgal.Inventory.LightHousingUnits = 500.0;
            WorldFixtures.AssignExactStewards(cEgal, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wEgal, cEgal)));

            // Viable downstream exit pocket prevents desert-locked vacuity so holdout proves Stratified granary + resistance!
            var wStrat = WorldFixtures.CreateValleyWorld(population: 2200, civicEnabled: true, sanitationEnabled: false, fission: true, immigration: false,
                width: 24, height: 16, landBiomassMultiplier: 1600.0, withViableExitPocket: true, precedents: precs);
            var cStrat = wStrat.Camps[0];
            cStrat.CivicComplexity = 0.75;
            cStrat.CivicStratification = 0.80;
            cStrat.LastEnvironmentalStratification = 0.80;
            cStrat.CivicFocus = CivicFocus.Centralized;
            cStrat.CivicCapital = CivicGovernance.RequiredCivicCapital(wStrat, cStrat);
            cStrat.Inventory.LightHousingUnits = 500.0;
            WorldFixtures.AssignExactStewards(cStrat, (int)Math.Ceiling(CivicGovernance.RequiredStewards(wStrat, cStrat)));

            var rE = new TurnResolver();
            var rS = new TurnResolver();
            // 2 warm-up turns (also verifies M3 crowding mitigation & granary reserves)
            Assert.Check(PhysicalDemography.CrowdingMitigation(wStrat, cStrat) < 0.30 && PhysicalDemography.CrowdingMitigation(wEgal, cEgal) < 0.30,
                $"Pre-shock Phase-1 civics must be live (M_c strat={PhysicalDemography.CrowdingMitigation(wStrat, cStrat):F4}, egal={PhysicalDemography.CrowdingMitigation(wEgal, cEgal):F4}).");
            for (int t = 0; t < 2; t++)
            {
                WorldFixtures.StepTurn(wEgal, rE);
                WorldFixtures.StepTurn(wStrat, rS);
            }
            var wStratBase = WorldFixtures.CloneWorld(wStrat);
            wStratBase.Config.Civic = null;
            double stratBaseStorage = NeolithicProduction.NaturalStorageCapacity(wStratBase, wStratBase.Camps[0], 0);
            double stratCivicStorage = NeolithicProduction.NaturalStorageCapacity(wStrat, cStrat, 0);
            Assert.Check(stratCivicStorage > stratBaseStorage * 1.25,
                $"Pre-shock Stratified granary ({stratCivicStorage:F0}) must exceed same-tile Civic=null baseline ({stratBaseStorage:F0}) by > 1.25x.");
            cEgal.PlantFood = NeolithicProduction.NaturalStorageCapacity(wEgal, cEgal, 0);
            cStrat.PlantFood = stratCivicStorage;
            cEgal.Inventory.LightHousingUnits = Math.Max(cEgal.Inventory.LightHousingUnits, 500.0);
            cStrat.Inventory.LightHousingUnits = Math.Max(cStrat.Inventory.LightHousingUnits, 500.0);

            WorldFixtures.SlashHomeFootprint(wEgal, cEgal, 0.02f);
            WorldFixtures.SlashHomeFootprint(wStrat, cStrat, 0.02f);

            int egalFirstSplitTurn = int.MaxValue;
            int stratFirstSplitTurn = int.MaxValue;
            double egalT2Food = 0, stratT2Food = 0, egalT2FoodMet = 0, stratT2FoodMet = 0;
            for (int t = 1; t <= 5; t++)
            {
                var recE = WorldFixtures.StepTurn(wEgal, rE);
                var recS = WorldFixtures.StepTurn(wStrat, rS);
                if (recE.Fissions.Count > 0 && egalFirstSplitTurn == int.MaxValue) egalFirstSplitTurn = t;
                if (recS.Fissions.Count > 0 && stratFirstSplitTurn == int.MaxValue) stratFirstSplitTurn = t;
                if (t == 2)
                {
                    egalT2Food = cEgal.Food; stratT2Food = cStrat.Food;
                    egalT2FoodMet = cEgal.FoodNeedsMetRatio; stratT2FoodMet = cStrat.FoodNeedsMetRatio;
                }
            }

            Assert.Check(egalFirstSplitTurn <= 2 && wEgal.Camps.Count > 1,
                $"Egalitarian mega-site must disperse early during shock (splitTurn={egalFirstSplitTurn}, camps={wEgal.Camps.Count}).");
            Assert.Check(egalFirstSplitTurn < stratFirstSplitTurn,
                $"Stratified granary city must hold strictly longer than Egalitarian (egalSplit={egalFirstSplitTurn}, stratSplit={stratFirstSplitTurn}).");
            Assert.Check(stratT2Food > egalT2Food && stratT2FoodMet > egalT2FoodMet,
                $"Stratified granary must sustain higher Turn-2 reserves ({stratT2Food:F0} vs {egalT2Food:F0}) and nutrition ({stratT2FoodMet:F2} vs {egalT2FoodMet:F2}).");
            Assert.Check(stratFirstSplitTurn <= 5 && wStrat.Camps.Count > 1,
                $"Non-vacuity proof: Stratified city must eventually break onto its viable downstream exit pocket once granary reserves exhaust (stratSplit={stratFirstSplitTurn}, camps={wStrat.Camps.Count}).");
        }

        private static void T4_05()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
            var wA = WorldFixtures.CreatePlainsWorld(population: 1200, civicEnabled: true, sanitationEnabled: true, smoothCalendar: true, fission: false, immigration: false,
                width: 20, height: 14, landBiomassMultiplier: 600.0, precedents: precs);
            WorldFixtures.ConfigureSmoothCalendarStarted(wA, startDays: 0.0);
            var cA = wA.Camps[0];
            cA.CivicComplexity = 0.0;
            cA.CivicCapital = 0.0;

            var rA = new TurnResolver();
            for (int t = 0; t < 7; t++)
                WorldFixtures.StepTurn(wA, rA);

            var wB = SaveCodec.Load(SaveCodec.Save(wA));
            SmoothCalendar.RequireCurrentBoundary(wB);
            var rB = new TurnResolver();

            for (int t = 7; t < 15; t++)
            {
                WorldFixtures.StepTurn(wA, rA);
                WorldFixtures.StepTurn(wB, rB);
            }

            Assert.Check(SaveCodec.Fingerprint(wA) == SaveCodec.Fingerprint(wB),
                "15-turn SmoothCalendar campaign mid-run SaveCodec roundtrip must match uninterrupted run bit-for-bit.");
            Assert.Check(cA.CivicCapital > 20.0 && cA.CivicComplexity > 0.20,
                $"SmoothCalendar campaign must accumulate CivicCapital ({cA.CivicCapital:F1}) and C ({cA.CivicComplexity:F3}).");
            Assert.Check(CultivationEconomy.Workers(cA, WorkforceRoles.Steward) > 0, "Campaign must staff Stewards after C > 0.");

            var obs = new AgentObservationBuilder().Build(wB, 0);
            var dto = ObservationContract.Deserialize(ObservationContract.Serialize(obs, wB.Camps[0].Mandate));
            Assert.Check(dto.Schema == 23 && dto.CivicGovernanceEnabled && dto.SanitationEnabled,
                "Turn-15 ObservationContract JSON roundtrip must succeed.");
        }

        private static void T4_06()
        {
            var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting };
            var cfg = WorldFixtures.BaseConfig(width: 26, height: 16, players: 1, civicEnabled: true, sanitationEnabled: false, fission: true, immigration: false, trade: false,
                landBiomassMultiplier: 600.0);
            var w = WorldFixtures.CreateEmptyWorld(cfg, seed: 406);
            // Open fertile frontier everywhere, with parent on a small river valley node in the middle
            for (int i = 0; i < w.Tiles.Length; i++)
                w.Tiles[i] = WorldFixtures.MakeFertileTile(i, cfg, moisture: 0.65f, waterSource: FreshwaterSource.RainPool, maxFreshwater: 25.0f);

            int center = w.Map.At(13, 8);
            var parent = WorldFixtures.AddVillage(w, 0, center, 2400, foodYears: 14.0, precedents: precs);
            parent.CivicComplexity = 0.72;
            parent.CivicStratification = 0.75;
            parent.LastEnvironmentalStratification = 0.75;
            parent.CivicCapital = 350.0;
            parent.FissionPolicy = FissionPolicy.Neutral;
            WorldFixtures.AssignExactStewards(parent, 150);
            WorldFixtures.CultivateFootprint(w, parent, capital: 2.0);
            WorldFixtures.AddSurfaceResources(w, Enumerable.Range(0, w.Tiles.Length), stone: 4000, clay: 4000, wood: 4000);
            // Distress fission onto the fertile frontier: the parent's home footprint fails and its granary is empty, while
            // the leaving group carries portable shelter. (Crowding alone cannot split an M3-mitigated parent toward an
            // unmitigated, unhoused daughter.)
            parent.PlantFood = 0.0;
            parent.Inventory.LightHousingUnits = 800.0;
            WorldFixtures.SlashHomeFootprint(w, parent, 0.02f);
            WorldFixtures.ValidateWorld(w);

            var recFis = new TurnRecord { Turn = w.Turn, StartDays = w.Days, Duration = w.Config.TurnDays };
            Fission.Resolve(w, recFis, new PhaseExecution(), new ResolutionMetrics());
            Assert.Check(w.Camps.Count == 2, $"Parent must fission a frontier daughter (camps={w.Camps.Count}).");
            var daughter = w.Camps.Single(x => x.Id != parent.Id);

            Assert.Check(daughter.People.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting), "Daughter inherits {T1, T2, T3A}.");
            Assert.NearAbs(daughter.CivicComplexity, 0.36, 1e-9, "Daughter dampened C == 0.36.");
            Assert.NearAbs(daughter.CivicStratification, 0.225, 1e-9, "Daughter dampened P == 0.225.");
            Assert.NearAbs(daughter.CivicCapital, 0.0, 1e-12, "Daughter starts with CivicCapital == 0.");
            Assert.Check(CultivationEconomy.Workers(daughter, WorkforceRoles.Steward) == 0, "Daughter converts departing Stewards to Gatherers.");

            double startDaughterP = daughter.CivicStratification;
            daughter.IsVillage = true;
            daughter.Inventory.HousingMaterial = 2500.0;
            daughter.Inventory.Tools = 2500.0;
            w.Config.FissionEnabled = false;
            WorldFixtures.CultivateFootprint(w, daughter, capital: 1.5);
            WorldFixtures.ValidateWorld(w);

            var resolver = new TurnResolver();
            for (int t = 0; t < 10; t++)
                WorldFixtures.StepTurn(w, resolver);

            Assert.Check(daughter.CivicStratification < startDaughterP - 0.25,
                $"Frontier daughter must adapt Egalitarian below starting P_d={startDaughterP:F3} (end={daughter.CivicStratification:F3}).");
            Assert.Check(daughter.CivicCapital > 10.0,
                $"Frontier daughter must rebuild communal CivicCapital using inherited precedents (got {daughter.CivicCapital:F1}).");
        }

        private static void T4_07()
        {
            var cfg = WorldFixtures.BaseConfig(width: 28, height: 16, players: 4, civicEnabled: true,
                sanitationEnabled: false, fission: false, immigration: false, trade: true, landBiomassMultiplier: 1000.0);
            cfg.TradeFoodEnabled = false;
            var w = WorldFixtures.CreateEmptyWorld(cfg, seed: 407);
            for (int i = 0; i < w.Tiles.Length; i++)
                w.Tiles[i] = WorldFixtures.MakeFertileTile(i, cfg);

            var precsT2 = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
            var solo = WorldFixtures.AddVillage(w, 0, w.Map.At(24, 13), 2000, grantPottery: false, housingCoverage: 1.0,
                foodYears: 25.0, autoEconomy: false, precedents: precsT2);
            solo.CivicComplexity = 0.70;
            solo.CivicCapital = CivicGovernance.RequiredCivicCapital(w, solo);
            WorldFixtures.AssignExactStewards(solo, 45);

            var ai1 = WorldFixtures.AddVillage(w, 1, w.Map.At(4, 7), 2000, housingCoverage: 0.25, clothPerCapita: 0.0,
                housingMaterial: 0.0, foodYears: 25.0, autoEconomy: false, precedents: precsT2);
            ai1.Inventory.Cloth = 3000.0;
            ai1.CivicComplexity = 0.70;
            ai1.CivicCapital = CivicGovernance.RequiredCivicCapital(w, ai1);
            WorldFixtures.AssignExactStewards(ai1, 45);

            var ai2 = WorldFixtures.AddVillage(w, 2, w.Map.At(8, 7), 2000, housingCoverage: 0.25, clothPerCapita: 0.0,
                housingMaterial: 0.0, foodYears: 25.0, autoEconomy: false, precedents: precsT2);
            ai2.Inventory.HousingMaterial = 1200.0;
            ai2.CivicComplexity = 0.70;
            ai2.CivicCapital = CivicGovernance.RequiredCivicCapital(w, ai2);
            WorldFixtures.AssignExactStewards(ai2, 45);

            var ai3 = WorldFixtures.AddVillage(w, 3, w.Map.At(4 + cfg.TradeRange + 2, 7), 2000, housingCoverage: 0.25, clothPerCapita: 0.0,
                housingMaterial: 0.0, foodYears: 25.0, autoEconomy: false, precedents: precsT2);
            ai3.Inventory.HousingMaterial = 1200.0;
            ai3.CivicComplexity = 0.70;
            ai3.CivicCapital = CivicGovernance.RequiredCivicCapital(w, ai3);
            WorldFixtures.AssignExactStewards(ai3, 45);

            foreach (var camp in new[] { solo, ai1, ai2, ai3 })
                WorldFixtures.CultivateFootprint(w, camp, capital: 1.5);
            WorldFixtures.AddSurfaceResources(w, w.Map.Radius(solo.Tile, 1).Concat(w.Map.Radius(ai1.Tile, 1)), stone: 5000, clay: 5000, wood: 5000);
            WorldFixtures.EstablishContacts(w, ai1, ai2);
            WorldFixtures.EstablishContacts(w, ai1, ai3);
            WorldFixtures.ValidateWorld(w);

            var resolver = new TurnResolver();
            int ai1UnlockTurn = int.MaxValue;
            int soloUnlockTurn = int.MaxValue;
            bool sawExtendedTrade = false;
            for (int t = 1; t <= 12; t++)
            {
                // Maintain complementary trade deficits and Steward staffing so multi-turn hub trading stays active.
                ai1.Inventory.Cloth = 3000.0;
                ai1.Inventory.HousingMaterial = 0.0;
                // Once ai1 holds Tier 3A, the near partner (d=4) runs dry so ai1's HousingMaterial need must spill over to
                // the distant partner (d = TradeRange + 2), which only the extended accounting range can reach.
                ai2.Inventory.HousingMaterial = ai1UnlockTurn == int.MaxValue ? 1200.0 : 0.0;
                ai2.Inventory.Cloth = 0.0;
                ai3.Inventory.HousingMaterial = 1200.0;
                ai3.Inventory.Cloth = 0.0;
                solo.PlantFood = Math.Max(solo.PlantFood, solo.Population * 4.0 * 25.0);
                // Manual-economy solo city: keep it clothed and its civic capital maintained so its Tier-3A clock measures
                // storage + Clay practice, not an unmaintained collapse (zero-margin hazard under M3 capital decay).
                solo.Inventory.Cloth = Math.Max(solo.Inventory.Cloth, solo.Population * 4.0);
                solo.CivicCapital = Math.Max(solo.CivicCapital, CivicGovernance.RequiredCivicCapital(w, solo));
                ai1.PlantFood = Math.Max(ai1.PlantFood, ai1.Population * 4.0 * 25.0);
                WorldFixtures.AssignExactStewards(solo, 45);
                WorldFixtures.AssignExactStewards(ai1, 45);

                var rec = WorldFixtures.StepTurn(w, resolver);
                if (ai1.People.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting) && ai1UnlockTurn == int.MaxValue) ai1UnlockTurn = t;
                if (solo.People.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting) && soloUnlockTurn == int.MaxValue) soloUnlockTurn = t;
                if (rec.Trades.Any(tr => tr.Distance > cfg.TradeRange)) sawExtendedTrade = true;
            }

            Assert.Check(ai1.RecentTradeVolume > 0.0, $"AI trade hub must build RecentTradeVolume > 0 (got {ai1.RecentTradeVolume:F2}).");
            Assert.Check(ai1UnlockTurn < soloUnlockTurn, $"AI trade hub must unlock Tier 3A earlier than solo Player 0 (ai1={ai1UnlockTurn}, solo={soloUnlockTurn}).");
            Assert.Check(soloUnlockTurn <= 12, $"Solo Player 0 must still unlock Tier 3A cleanly via storage+Clay within 12 turns (turn={soloUnlockTurn}).");
            Assert.Check(sawExtendedTrade, "After Tier 3A unlock, AI hub must execute live extended-range TradeRecords (Distance > TradeRange).");
        }

        private static void T4_08()
        {
            bool prevAudit = SocialRelocationForecastCache.AuditBounds;
            SocialRelocationForecastCache.AuditBounds = true;
            try
            {
                var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization, CivicPrecedents.TokensAndAccounting, CivicPrecedents.PlannedQuartersAndDrainage };

                var wPlain = WorldFixtures.CreatePlainsWorld(population: 1800, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                    width: 20, height: 14, landBiomassMultiplier: 1200.0, precedents: precs);
                var cPlain = wPlain.Camps[0];
                cPlain.CivicComplexity = 0.85;
                cPlain.LastEnvironmentalComplexity = 0.85;
                cPlain.CivicStratification = -0.60;
                cPlain.LastEnvironmentalStratification = -0.60;
                cPlain.CivicCapital = CivicGovernance.RequiredCivicCapital(wPlain, cPlain);
                cPlain.Inventory.HousingMaterial = 15000.0;
                cPlain.Inventory.Tools = 15000.0;
                cPlain.Inventory.FarmTools = 2000.0;

                var wRiver = WorldFixtures.CreateValleyWorld(population: 1800, civicEnabled: true, sanitationEnabled: true, fission: false, immigration: false,
                    width: 20, height: 14, landBiomassMultiplier: 2400.0, precedents: precs);
                var cRiver = wRiver.Camps[0];
                cRiver.CivicComplexity = 0.85;
                cRiver.LastEnvironmentalComplexity = 0.85;
                cRiver.CivicStratification = 0.65;
                cRiver.LastEnvironmentalStratification = 0.65;
                cRiver.CivicCapital = CivicGovernance.RequiredCivicCapital(wRiver, cRiver);
                cRiver.Inventory.HousingMaterial = 15000.0;
                cRiver.Inventory.Tools = 15000.0;
                cRiver.Inventory.FarmTools = 2000.0;

                var rP = new TurnResolver();
                var rR = new TurnResolver();
                int peakPlain = cPlain.Population, peakRiver = cRiver.Population;
                double midStewSharePlain = -1.0, midStewShareRiver = -1.0;

                for (int t = 0; t < 25; t++)
                {
                    WorldFixtures.StepTurn(wPlain, rP);
                    WorldFixtures.StepTurn(wRiver, rR);
                    if (cPlain.Population > peakPlain) peakPlain = cPlain.Population;
                    if (cRiver.Population > peakRiver) peakRiver = cRiver.Population;

                    if (midStewSharePlain < 0 && cPlain.Population >= 2200 && cPlain.Population <= 3600)
                        midStewSharePlain = CultivationEconomy.Workers(cPlain, WorkforceRoles.Steward) / (double)cPlain.Population;
                    if (midStewShareRiver < 0 && cRiver.Population >= 2200 && cRiver.Population <= 3600)
                        midStewShareRiver = CultivationEconomy.Workers(cRiver, WorkforceRoles.Steward) / (double)cRiver.Population;
                }

                Assert.Check(cPlain.SanitationCapital > 0.0 && cPlain.SanitationWaterRatio > 0.90 && PhysicalDemography.DiseaseMitigation(wPlain, cPlain) < 0.40,
                    $"Plain metropolis must run full Sanitation (cap={cPlain.SanitationCapital:F1}, ratio={cPlain.SanitationWaterRatio:F3}, M_d={PhysicalDemography.DiseaseMitigation(wPlain, cPlain):F3}).");
                Assert.Check(cRiver.SanitationCapital > 0.0 && cRiver.SanitationWaterRatio > 0.90 && PhysicalDemography.DiseaseMitigation(wRiver, cRiver) < 0.35,
                    $"River metropolis must run full Sanitation (cap={cRiver.SanitationCapital:F1}, ratio={cRiver.SanitationWaterRatio:F3}, M_d={PhysicalDemography.DiseaseMitigation(wRiver, cRiver):F3}).");
                Assert.Check(peakPlain >= 5000, $"Plain Egalitarian Phase-2 metropolis must break ~3,400 wall to >= 5,000 (peak={peakPlain}).");
                Assert.Check(peakRiver >= 6500 && peakRiver > peakPlain,
                    $"River Stratified Phase-2 metropolis must peak >= 6,500 and strictly above Egalitarian (river={peakRiver} vs plain={peakPlain}).");
                Assert.Check(midStewSharePlain > 0.0 && midStewShareRiver > 0.0 && midStewSharePlain < midStewShareRiver,
                    $"Egalitarian Steward % at mid-scale ({midStewSharePlain:P2}) must be strictly lower than Stratified ({midStewShareRiver:P2}).");
                Assert.Check(cRiver.ClothPerCapita > 0.70 && cRiver.HousingPerCapita > 0.70,
                    $"KernelSplit must keep Protected clothing/shelter active above 5,000 (cloth={cRiver.ClothPerCapita:F3}, housing={cRiver.HousingPerCapita:F3}).");
            }
            finally
            {
                SocialRelocationForecastCache.AuditBounds = prevAudit;
            }
        }

        private static void T4_09()
        {
            bool prevAudit = SocialRelocationForecastCache.AuditBounds;
            SocialRelocationForecastCache.AuditBounds = true;
            try
            {
                long evalsBefore = SocialRelocationForecastCache.BoundEvaluations;
                var precs = new[] { CivicPrecedents.CeremonialIntegration, CivicPrecedents.CivicOrganization };
                var w = WorldFixtures.CreatePlainsWorld(population: 1400, civicEnabled: true, sanitationEnabled: false, fission: true, immigration: true, trade: false,
                    width: 24, height: 16, landBiomassMultiplier: 800.0, precedents: precs);
                var p0 = w.Camps[0];
                p0.CivicComplexity = 0.65;
                p0.CivicCapital = CivicGovernance.RequiredCivicCapital(w, p0);
                p0.Inventory.HousingMaterial = 6000.0;
                p0.Inventory.Tools = 6000.0;

                var openNbr = WorldFixtures.AddVillage(w, 1, w.Map.At(15, 8), 700, foodYears: 18.0, precedents: precs);
                openNbr.CivicComplexity = 0.65;
                openNbr.CivicStratification = -0.75;
                openNbr.LastEnvironmentalStratification = -0.75;
                openNbr.ImmigrationPolicy = ImmigrationPolicy.Open;
                openNbr.CivicCapital = CivicGovernance.RequiredCivicCapital(w, openNbr);
                WorldFixtures.CultivateFootprint(w, openNbr, capital: 2.0);
                WorldFixtures.EstablishContacts(w, p0, openNbr);
                WorldFixtures.ValidateWorld(w);

                var steer = new Dictionary<int, List<Order>>
                {
                    [0] = new List<Order>
                    {
                        new Order(OrderKind.FissionPolicy, p0.Id, (int)FissionPolicy.Discourage),
                        new Order(OrderKind.ImmigrationPolicy, p0.Id, (int)ImmigrationPolicy.Restricted),
                        new Order(OrderKind.CivicFocus, p0.Id, (int)CivicFocus.Centralized)
                    }
                };

                var resolver = new TurnResolver();
                int totalEmigratedOrSplit = 0;
                double turn1PEnv = 0.0;
                double peakAlignmentFriction = 0.0;
                double peakExitMismatch = 0.0;
                for (int t = 0; t < 10; t++)
                {
                    var rec = WorldFixtures.StepTurn(w, resolver, t == 0 ? steer : null);
                    if (t == 0) turn1PEnv = p0.LastEnvironmentalStratification;
                    peakAlignmentFriction = Math.Max(peakAlignmentFriction, CivicGovernance.AlignmentFriction(w, p0));
                    peakExitMismatch = Math.Max(peakExitMismatch, CivicGovernance.ExitMismatch(p0));
                    totalEmigratedOrSplit += rec.Migrations.Where(m => m.SourceCamp == p0.Id).Sum(m => m.Migrants);
                    totalEmigratedOrSplit += rec.Fissions.Where(f => f.ParentCamp == p0.Id).Sum(f => f.Daughter);
                }

                Assert.Check(turn1PEnv < -0.25, $"Initial open plain P_env must be Egalitarian < -0.25 (got {turn1PEnv:F3}).");
                Assert.Check(p0.CivicStratification > 0.0, $"Centralized steering trio drags P positive ({p0.CivicStratification:F3}).");
                Assert.Check(peakAlignmentFriction > 1.15,
                    $"Coerced camp must pay measurable peak AlignmentFriction > 1.15 (got {peakAlignmentFriction:F3}).");
                Assert.Check(peakExitMismatch > 0.25,
                    $"Coerced camp on open plain must incur peak ExitMismatch > 0.25 (got {peakExitMismatch:F3}).");
                Assert.Check(PhysicalDemography.CrowdingMitigation(w, p0) < 0.70, "Phase-1 civics active.");
                Assert.Check(totalEmigratedOrSplit > 0, $"Coerced regime on open plain must lose residents via voluntary emigration/fission (lost={totalEmigratedOrSplit}).");
                Assert.Check(SocialRelocationForecastCache.BoundEvaluations > evalsBefore, "AuditBounds must evaluate > 0 bounds with 0 violations.");
            }
            finally
            {
                SocialRelocationForecastCache.AuditBounds = prevAudit;
            }
        }

        private static void T4_10()
        {
            bool prevAudit = SocialRelocationForecastCache.AuditBounds;
            SocialRelocationForecastCache.AuditBounds = true;
            try
            {
                long evalsBefore = SocialRelocationForecastCache.BoundEvaluations;
                var w = WorldFixtures.CreateCompactGeneratedWorld(seed: 11, civicEnabled: true, sanitationEnabled: true, width: 64, height: 32, players: 6);
                var resolver = new TurnResolver();
                WorldState midSaved = null;

                for (int t = 1; t <= 14; t++)
                {
                    WorldFixtures.StepWithAi(w, resolver, excludedPlayer: -1, useDraft: true);
                    if (t == 7)
                        midSaved = SaveCodec.Load(SaveCodec.Save(w));
                }

                // Replay Turns 8..14 from Turn-7 SaveCodec snapshot and verify bit-exact Fingerprint parity!
                Assert.Check(midSaved != null, "Mid-run save snapshot captured at Turn 7.");
                var rReplay = new TurnResolver();
                for (int t = 8; t <= 14; t++)
                    WorldFixtures.StepWithAi(midSaved, rReplay, excludedPlayer: -1, useDraft: true);

                Assert.Check(SaveCodec.Fingerprint(midSaved) == SaveCodec.Fingerprint(w),
                    "Generated-world mid-run SaveCodec roundtrip must match uninterrupted Turn-14 Fingerprint bit-for-bit.");
                Assert.Check(SocialRelocationForecastCache.BoundEvaluations > evalsBefore,
                    $"Generated-world campaign AuditBounds must evaluate > 0 bounds with 0 violations (delta={SocialRelocationForecastCache.BoundEvaluations - evalsBefore}).");

                // Verify ObservationContract JSON (Schema 23, zero rival leakage) & TileDetails.Describe owner-only CIVIC summary for every live player
                var builder = new AgentObservationBuilder();
                int liveChecked = 0;
                foreach (int player in w.Camps.Where(c => c.Population > 0).Select(c => c.Player).Distinct().OrderBy(p => p))
                {
                    var obs = builder.Build(w, player);
                    var myCamp = w.Camps.First(c => c.Player == player && c.Population > 0);
                    string json = ObservationContract.Serialize(obs, myCamp.Mandate);
                    var dto = ObservationContract.Deserialize(json);
                    Assert.Check(dto.Schema == 23 && dto.CivicGovernanceEnabled && dto.SanitationEnabled,
                        $"Player {player} DTO must report Schema=23 and enabled civic flags.");

                    string ownDesc = TileDetails.Describe(obs, myCamp.Tile, Array.Empty<Order>());
                    Assert.Check(ownDesc.IndexOf("CIVIC", StringComparison.OrdinalIgnoreCase) >= 0,
                        $"Player {player} TileDetails.Describe on own camp must include owner-only CIVIC summary (got:\n{ownDesc}).");

                    foreach (var rival in obs.Rivals)
                    {
                        string rivalDesc = TileDetails.Describe(obs, rival.Tile, Array.Empty<Order>());
                        Assert.Check(rivalDesc.IndexOf("CIVIC", StringComparison.OrdinalIgnoreCase) < 0,
                            $"Player {player} TileDetails.Describe on rival camp {rival.Id} must NOT leak CIVIC text.");
                    }
                    liveChecked++;
                }
                Assert.Check(liveChecked > 0, "Expected at least one live player at Turn 14.");
            }
            finally
            {
                SocialRelocationForecastCache.AuditBounds = prevAudit;
            }
        }
    }
}
