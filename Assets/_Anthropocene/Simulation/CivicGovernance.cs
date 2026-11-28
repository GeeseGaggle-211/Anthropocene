using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Anthropocene.Simulation
{
    public enum ImmigrationPolicy { Neutral = 0, Open = 1, Restricted = 2 }
    public enum CivicFocus { Auto = 0, Communal = 1, Centralized = 2 }

    public static class CivicPrecedents
    {
        public const string CeremonialIntegration = "CeremonialIntegration";
        public const string CivicOrganization = "CivicOrganization";
        public const string TokensAndAccounting = "TokensAndAccounting";
        public const string PlannedQuartersAndDrainage = "PlannedQuartersAndDrainage";
        public static readonly string[] All =
        {
            CeremonialIntegration,
            CivicOrganization,
            TokensAndAccounting,
            PlannedQuartersAndDrainage
        };
    }

    // Null in legacy configurations. Enabled only by an explicit new-game profile
    // (Assets/_Anthropocene/Resources/CivicGovernanceOpeningConfig.json).
    // Settings match Docs/CivicGovernance.md §5 Choice #11 A & B.
    [Serializable]
    public sealed class CivicGovernanceSettings
    {
        public bool Enabled = false;
        public bool SanitationEnabled = false;
        public double Tier1ComplexityCeiling = 0.30;
        public double Tier2ComplexityCeiling = 0.75;
        public double Tier3ComplexityCeiling = 1.00;
        public double ComplexityRelaxationRate = 0.025;
        public double StratificationRelaxationRate = 0.020;
        // Absent old-save fields remain zero: existing worlds retain linear relaxation.
        [System.Runtime.Serialization.OptionalField] public double StratificationFeedbackStrength = 0.0;
        public double TradeVolumeRelaxationRate = 0.025;
        public double ComplexityCrowdingScale = 0.64;
        public double FissionPolicySteeringShift = 0.15;
        public double ImmigrationPolicySteeringShift = 0.15;
        public double CivicFocusSteeringShift = 0.45;
        public double DaughterComplexityRetention = 0.50;
        public double DaughterStratificationRetention = 0.50;
        public double DaughterFrontierEgalitarianShift = 0.15;
        public double MinCrowdingMitigation = 0.05;
        public double MinDiseaseMitigation = 0.15;
        public double CrowdingMitigationBase = 0.925;
        public double CrowdingMitigationStratBonus = 0.025;
        public double CrowdingShapeRate = 5.8;
        public double CrowdingShapeExponent = 1.65;
        public double DiseaseMitigationBase = 0.735;
        public double DiseaseMitigationStratBonus = 0.075;
        public double DiseaseHalfScale = 0.60;
        public double CohesionLinearGain = 1.95;
        public double CohesionUrbanGain = 4.45;
        public double CohesionUrbanExponent = 4.2;
        public double CohesionStratFactor = 0.32;
        public double EgalitarianStewardRate = 0.045;
        public double StratifiedStewardRate = 0.105;
        public double StewardScalingKnee = 3500;
        public double EgalitarianStewardExponent = 2.60;
        public double StratifiedStewardExponent = 1.15;
        public double AccountingExponentRetention = 0.20;
        public double AlignmentFrictionRate = 0.35;
        public double EgalitarianCapitalPerPerson = 0.12;
        public double StratifiedCapitalPerPerson = 0.24;
        public double CivicCapitalDecayPerYear = 0.018;
        public double CivicBuildBuilderYears = 4.0;
        public double CivicBuildHousingMaterials = 2.0;
        public double CivicMaintainBuilderYears = 2.0;
        public double CivicMaintainHousingMaterials = 1.0;
        public double SanitationCapitalPerPerson = 0.16;
        public double SanitationCapitalDecayPerYear = 0.025;
        public double SanitationBuildBuilderYears = 5.0;
        public double SanitationBuildHousingMaterials = 2.5;
        public double SanitationMaintainBuilderYears = 2.5;
        public double SanitationMaintainHousingMaterials = 1.0;
        public double SanitationWaterPerPersonYear = 0.02;
        public double QoLEqualityLinear = 0.09;
        public double QoLEqualityQuadratic = 0.01;
        public double StratifiedPublicWorksBonus = 0.35;
        public double StratifiedGranaryStorageBonus = 0.50;
        public double FissionStratificationResistance = 0.045;
        public double FissionExitMismatchReduction = 0.060;
        public double OpenImmigrationCostReduction = 0.030;
        public double OpenIncumbentToleranceBonus = 0.020;
        public double RestrictedImmigrationCostPenalty = 0.150;
        public double RestrictedIncumbentToleranceScale = 0.30;
        public double StratificationImmigrationFriction = 0.025;
        public double ExitMismatchImmigrationReduction = 0.030;
        public double Tier1PracticeThreshold = 40.0;
        public double Tier2PracticeThreshold = 80.0;
        public double Tier3APracticeThreshold = 120.0;
        public double Tier3BPracticeThreshold = 100.0;
        public int TokensTradeRangeBonus = 2;
        public int TokensTradeRequisitionBonus = 2;
        public double TokensMarginReduction = 0.25;
        public double PrecedentDiffusionPerTrade = 0.0;
        public double CivicDeathFoodYears = 6.0;
        public double SanitationDeathFoodYears = 15.0;
        public int DevelopmentMinPopulation = 300;
        public int SanitationDevelopmentMinPopulation = 1200;

        public void Validate()
        {
            foreach (var f in GetType().GetFields().Where(f => f.FieldType == typeof(double)))
            {
                double v = (double)f.GetValue(this);
                if (double.IsNaN(v) || double.IsInfinity(v))
                    throw new ArgumentException("Invalid civic governance setting: " + f.Name);
            }
            if (!(Tier1ComplexityCeiling > 0 && Tier1ComplexityCeiling < Tier2ComplexityCeiling &&
                  Tier2ComplexityCeiling < Tier3ComplexityCeiling && Tier3ComplexityCeiling <= 1))
                throw new ArgumentException("Invalid civic complexity ceilings.");
            if (ComplexityRelaxationRate <= 0 || StratificationRelaxationRate <= 0 ||
                TradeVolumeRelaxationRate <= 0 || ComplexityCrowdingScale <= 0)
                throw new ArgumentException("Invalid civic relaxation or crowding scale.");
            if (StratificationFeedbackStrength < 0 || StratificationFeedbackStrength > 0.30)
                throw new ArgumentException("Invalid civic stratification feedback strength.");
            if (FissionPolicySteeringShift < 0 || FissionPolicySteeringShift > 1 ||
                ImmigrationPolicySteeringShift < 0 || ImmigrationPolicySteeringShift > 1 ||
                CivicFocusSteeringShift < 0 || CivicFocusSteeringShift > 1 ||
                DaughterComplexityRetention < 0 || DaughterComplexityRetention > 1 ||
                DaughterStratificationRetention < 0 || DaughterStratificationRetention > 1 ||
                DaughterFrontierEgalitarianShift < 0 || DaughterFrontierEgalitarianShift > 1)
                throw new ArgumentException("Invalid civic steering or daughter inheritance setting.");
            if (MinCrowdingMitigation <= 0 || MinCrowdingMitigation > 1 ||
                MinDiseaseMitigation <= 0 || MinDiseaseMitigation > 1)
                throw new ArgumentException("Invalid civic mitigation floor.");
            if (CrowdingMitigationBase < 0 || CrowdingMitigationBase > 1 ||
                CrowdingMitigationStratBonus < 0 || CrowdingMitigationStratBonus > 0.5 ||
                CrowdingShapeRate <= 0 || CrowdingShapeExponent <= 0)
                throw new ArgumentException("Invalid civic crowding mitigation calibration.");
            if (DiseaseMitigationBase < 0 || DiseaseMitigationBase > 1 ||
                DiseaseMitigationStratBonus < 0 || DiseaseMitigationStratBonus > 0.5 ||
                DiseaseHalfScale <= 0)
                throw new ArgumentException("Invalid civic disease mitigation calibration.");
            if (CohesionLinearGain < 0 || CohesionUrbanGain < 0 || CohesionUrbanExponent < 1 ||
                CohesionStratFactor < 0 || CohesionStratFactor > 0.9)
                throw new ArgumentException("Invalid civic social cohesion calibration.");
            if (EgalitarianStewardRate <= 0 || StratifiedStewardRate < EgalitarianStewardRate ||
                StewardScalingKnee <= 0 || EgalitarianStewardExponent < 1 || StratifiedStewardExponent < 1 ||
                AccountingExponentRetention <= 0 || AccountingExponentRetention > 1 ||
                AlignmentFrictionRate < 0)
                throw new ArgumentException("Invalid civic steward scaling calibration.");
            if (EgalitarianCapitalPerPerson <= 0 || StratifiedCapitalPerPerson < EgalitarianCapitalPerPerson ||
                CivicCapitalDecayPerYear <= 0 || CivicBuildBuilderYears <= 0 || CivicBuildHousingMaterials < 0 ||
                CivicMaintainBuilderYears <= 0 || CivicMaintainHousingMaterials < 0)
                throw new ArgumentException("Invalid civic capital calibration.");
            if (SanitationCapitalPerPerson <= 0 || SanitationCapitalDecayPerYear <= 0 ||
                SanitationBuildBuilderYears <= 0 || SanitationBuildHousingMaterials < 0 ||
                SanitationMaintainBuilderYears <= 0 || SanitationMaintainHousingMaterials < 0 ||
                SanitationWaterPerPersonYear <= 0)
                throw new ArgumentException("Invalid sanitation capital calibration.");
            if (QoLEqualityLinear < 0 || QoLEqualityLinear > 0.5 ||
                QoLEqualityQuadratic < 0 || QoLEqualityQuadratic > 0.25 ||
                !((QoLEqualityLinear == 0 && QoLEqualityQuadratic == 0) || QoLEqualityLinear > 2 * QoLEqualityQuadratic))
                throw new ArgumentException("Invalid civic QoL equality calibration.");
            if (StratifiedPublicWorksBonus < 0 || StratifiedGranaryStorageBonus < 0)
                throw new ArgumentException("Invalid stratified bonus calibration.");
            if (FissionStratificationResistance < 0 || FissionStratificationResistance > 0.25 ||
                FissionExitMismatchReduction < 0 || FissionExitMismatchReduction > 0.25)
                throw new ArgumentException("Invalid civic fission threshold calibration.");
            if (OpenImmigrationCostReduction < 0 || OpenImmigrationCostReduction > 0.5 ||
                OpenIncumbentToleranceBonus < 0 || OpenIncumbentToleranceBonus > 0.5 ||
                RestrictedImmigrationCostPenalty < 0 || RestrictedImmigrationCostPenalty > 1.0 ||
                RestrictedIncumbentToleranceScale < 0 || RestrictedIncumbentToleranceScale > 1 ||
                StratificationImmigrationFriction < 0 || StratificationImmigrationFriction > 0.25 ||
                ExitMismatchImmigrationReduction < 0 || ExitMismatchImmigrationReduction > 0.25)
                throw new ArgumentException("Invalid civic immigration calibration.");
            if (Tier1PracticeThreshold <= 0 || Tier2PracticeThreshold <= 0 ||
                Tier3APracticeThreshold <= 0 || Tier3BPracticeThreshold <= 0)
                throw new ArgumentException("Invalid civic precedent threshold.");
            if (TokensTradeRangeBonus < 0 || TokensTradeRangeBonus > 10 ||
                TokensTradeRequisitionBonus < 0 || TokensTradeRequisitionBonus > 10 ||
                TokensMarginReduction < 0 || TokensMarginReduction > 0.9 ||
                PrecedentDiffusionPerTrade < 0)
                throw new ArgumentException("Invalid civic trade calibration.");
            if (CivicDeathFoodYears <= 0 || SanitationDeathFoodYears <= 0 ||
                DevelopmentMinPopulation < 1 || SanitationDevelopmentMinPopulation < 1)
                throw new ArgumentException("Invalid civic DemandControl calibration.");
        }
    }

    [Serializable]
    public sealed class SanitationWaterReceipt
    {
        public int Source, Camp;
        public double Withdrawal, Need, Ratio;
    }

    [Serializable]
    public sealed class CivicTurnReport
    {
        public int Camp, Society;
        public double ComplexityBefore, ComplexityAfter;
        public double StratificationBefore, StratificationAfter;
        public double EnvironmentalComplexity, EnvironmentalStratification, Circumscription;
        public double CivicCoverage, SanitationCoverage;
    }

    public sealed class CivicOrder
    {
        public double Construction, Upkeep;
    }

    public readonly struct CivicEnvironmentDrivers
    {
        public readonly double D1, D2, D3, D4;
        public readonly double RawComplexity, EnvironmentalComplexity, EnvironmentalStratification;

        public CivicEnvironmentDrivers(double d1, double d2, double d3, double d4,
            double rawComplexity, double environmentalComplexity, double environmentalStratification)
        {
            D1 = d1; D2 = d2; D3 = d3; D4 = d4;
            RawComplexity = rawComplexity;
            EnvironmentalComplexity = environmentalComplexity;
            EnvironmentalStratification = environmentalStratification;
        }
    }

    public static class CivicGovernance
    {
        private const double Epsilon = 1e-12;

        private static double ClampFinite(double x, double lo, double hi, double fallback = 0)
            => double.IsNaN(x) ? Numbers.Clamp(fallback, lo, hi) : Numbers.Clamp(x, lo, hi);

        private static double NonNegativeFinite(double x)
            => !(x > 0) ? 0 : (double.IsInfinity(x) ? double.MaxValue : x);

        private static double FiniteSeason(float x)
            => float.IsNaN(x) ? 0.0 : (float.IsInfinity(x) ? (x > 0 ? float.MaxValue : -float.MaxValue) : (double)x);

        private static double NonNegativeFiniteSeason(float x)
            => !(x > 0) ? 0.0 : (float.IsInfinity(x) ? (double)float.MaxValue : (double)x);

        private static double SafeFoodPerPersonYear(SimulationConfig cfg)
        {
            double raw = PhysicalDemography.FoodPerPersonYear(cfg);
            return raw > Epsilon && !double.IsInfinity(raw)
                ? raw
                : (double.IsPositiveInfinity(raw) ? double.MaxValue : Epsilon);
        }

        private static double BaseCohesionCrowding(WorldState w, Camp c)
        {
            var n = w.Config.Neolithic;
            var shelter = NeolithicProduction.HousingCapacity(w, c, w.Days);
            double hCamp = NeolithicProduction.HousingShare(n, shelter, c.Population);
            if (double.IsNaN(hCamp))
            {
                double fixedP = NonNegativeFinite(shelter.Fixed);
                double lightRaw = NonNegativeFinite(shelter.Light);
                double perm = NonNegativeFinite(n.LightHousingPermanence);
                double lightP = perm > 0 && lightRaw > 0 ? NonNegativeFinite(perm * lightRaw) : 0;
                hCamp = c.Population > 0
                    ? ClampFinite((fixedP + lightP) / c.Population, 0, 1)
                    : (fixedP + lightRaw > 0 ? 1 : 0);
            }
            double ns0 = NeolithicProduction.CohesionScale(n, hCamp);
            if (double.IsNaN(ns0)) ns0 = NonNegativeFinite(n.CohesionBandScale);
            return Math.Max(0, c.Population) / Math.Max(1.0, ns0);
        }

        private static double LogConvexPositive(double a, double b, double u)
        {
            double t1 = u < 1.0 && a > 0 ? Math.Log(1.0 - u) + Math.Log(a) : double.NegativeInfinity;
            double t2 = u > 0.0 && b > 0 ? Math.Log(u) + Math.Log(b) : double.NegativeInfinity;
            double m = Math.Max(t1, t2);
            if (double.IsNegativeInfinity(m)) return double.NegativeInfinity;
            if (double.IsPositiveInfinity(m)) return double.PositiveInfinity;
            return m + PhysicalDemography.Log1p(Math.Exp(Math.Min(t1, t2) - m));
        }

        private static double ScaledGainTerm(double gain, double factor, double scale)
        {
            if (!(gain > 0) || !(factor > 0) || !(scale > 0)) return 0;
            double fs = factor * scale;
            if (fs >= 1e-290 && !double.IsInfinity(fs))
            {
                double p = gain * fs;
                if (p >= 1e-290 && !double.IsInfinity(p)) return p;
            }
            double logP = Math.Log(gain) + Math.Log(factor) + Math.Log(scale);
            if (double.IsNaN(logP)) return 0;
            if (logP >= 709.782712893384) return double.MaxValue;
            double res = Math.Exp(logP);
            return double.IsInfinity(res) ? double.MaxValue : res;
        }

        // Capability predicates
        public static bool Enabled(SimulationConfig cfg) => cfg?.Civic != null && cfg.Civic.Enabled;
        public static bool SanitationEnabled(SimulationConfig cfg) => Enabled(cfg) && cfg.Civic.SanitationEnabled;

        public static double MaxComplexity(SimulationConfig cfg, Society s)
        {
            if (!Enabled(cfg) || s == null) return 0;
            if (s.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting)) return ClampFinite(cfg.Civic.Tier3ComplexityCeiling, 0, 1);
            if (s.HasCivicPrecedent(CivicPrecedents.CivicOrganization)) return ClampFinite(cfg.Civic.Tier2ComplexityCeiling, 0, 1);
            if (s.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration)) return ClampFinite(cfg.Civic.Tier1ComplexityCeiling, 0, 1);
            return 0;
        }
        public static double MaxComplexity(WorldState w, Camp c) => MaxComplexity(w?.Config, c?.People);

        public static bool StewardAvailable(WorldState w, Camp c)
            => Enabled(w?.Config) && c?.People != null && c.People.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration);

        // Recipe availability checks feature + precedent only, never c.IsVillage (Docs/CivicGovernance.md §3.2).
        public static bool CivicRecipesAvailable(WorldState w, Camp c) => StewardAvailable(w, c);

        public static bool SanitationRecipesAvailable(WorldState w, Camp c)
            => SanitationEnabled(w?.Config) && c?.People != null && c.People.HasCivicPrecedent(CivicPrecedents.PlannedQuartersAndDrainage);

        public static bool IsCivicGood(CraftGood good)
            => good == CraftGood.CivicConstruction || good == CraftGood.CivicMaintenance;

        public static bool IsSanitationGood(CraftGood good)
            => good == CraftGood.SanitationConstruction || good == CraftGood.SanitationMaintenance;

        public static bool IsPublicWorksGood(CraftGood good)
            => NeolithicProduction.IsInfrastructureGood(good);

        internal static double Demand(ProductionOrder order, CraftGood good)
        {
            if (order == null) return 0;
            if (good == CraftGood.CivicConstruction) return order.Civic == null ? 0 : NonNegativeFinite(order.Civic.Construction);
            if (good == CraftGood.CivicMaintenance) return order.Civic == null ? 0 : NonNegativeFinite(order.Civic.Upkeep);
            if (good == CraftGood.SanitationConstruction) return order.Sanitation == null ? 0 : NonNegativeFinite(order.Sanitation.Construction);
            if (good == CraftGood.SanitationMaintenance) return order.Sanitation == null ? 0 : NonNegativeFinite(order.Sanitation.Upkeep);
            return 0;
        }

        internal static double InfrastructureHousingMaterials(SimulationConfig cfg, ProductionOrder order)
        {
            if (!Enabled(cfg) || (order?.Civic == null && order?.Sanitation == null)) return 0;
            var s = cfg.Civic;
            double build = Demand(order, CraftGood.CivicConstruction) * NonNegativeFinite(s.CivicBuildHousingMaterials);
            double maint = Demand(order, CraftGood.CivicMaintenance) * NonNegativeFinite(s.CivicMaintainHousingMaterials);
            double sanit = 0;
            if (SanitationEnabled(cfg) && order.Sanitation != null)
                sanit = Demand(order, CraftGood.SanitationConstruction) * NonNegativeFinite(s.SanitationBuildHousingMaterials)
                    + Demand(order, CraftGood.SanitationMaintenance) * NonNegativeFinite(s.SanitationMaintainHousingMaterials);
            return NonNegativeFinite(build + maint + sanit);
        }

        public static double EffectiveCivicDecayRate(WorldState w, Camp c)
        {
            if (w == null || c == null || !Enabled(w.Config)) return 0;
            double rate = NonNegativeFinite(w.Config.Civic.CivicCapitalDecayPerYear) * AlignmentFriction(w, c);
            return NonNegativeFinite(rate);
        }

        public static double ProjectedLowerBoundCivicCapital(WorldState w, Camp c, double years)
        {
            if (w == null || c == null || !Enabled(w.Config)) return 0;
            double kOpen = NonNegativeFinite(c.CivicCapital);
            if (!(kOpen > 0)) return 0;
            if (!(years > 0) || double.IsNaN(years)) return kOpen;
            double rate = EffectiveCivicDecayRate(w, c);
            if (!(rate > 0)) return kOpen;
            double dtDecay = double.IsInfinity(years) ? double.PositiveInfinity : rate * years;
            return dtDecay > 709.0 ? 0 : kOpen * Math.Exp(-dtDecay);
        }

        public static double GranaryStorageMultiplierLowerBound(WorldState w, Camp c, double years)
            => w == null || c == null || !Enabled(w.Config)
                ? 1
                : GranaryStorageMultiplier(w, c, ProjectedLowerBoundCivicCapital(w, c, years));

        public static double CivicMaintenanceTarget(WorldState w, Camp c, double years,
            double? complexityOverride = null, double? populationOverride = null)
        {
            if (w == null || c == null || !Enabled(w.Config) || !(years > 0) || double.IsNaN(years)) return 0;
            double kOpen = NonNegativeFinite(c.CivicCapital);
            if (!(kOpen > Epsilon)) return 0;
            double pop = populationOverride ?? c.Population;
            double comp = complexityOverride ?? c.CivicComplexity;
            double req = RequiredCivicCapital(w.Config, pop, comp, c.CivicStratification);
            double capped = Math.Min(kOpen, NonNegativeFinite(req));
            if (!(capped > 0)) return 0;
            double rate = EffectiveCivicDecayRate(w, c);
            if (!(rate > 0)) return 0;
            if (double.IsInfinity(years)) return double.MaxValue;
            return NonNegativeFinite(rate * capped * years);
        }

        // Pure semigroup-exact dt-invariant excess-decay retention law (Docs/CivicGovernance.md §5 Choice #3):
        // K_maint = min(K_open, u_paid / (delta_eff * dt))
        // K_retained = K_maint + (K_open - K_maint) * exp(-delta_eff * dt)
        public static double RetainCapital(double opening, double decayRate, double years, double maintPaid)
        {
            double kOpen = NonNegativeFinite(opening);
            if (!(kOpen > 0)) return 0;
            if (!(years > 0) || double.IsNaN(years) || !(decayRate > 0) || double.IsNaN(decayRate)) return kOpen;
            double dtDecay = double.IsInfinity(years) || double.IsInfinity(decayRate) ? double.PositiveInfinity : decayRate * years;
            if (!(dtDecay > 0)) return kOpen;
            double paid = NonNegativeFinite(maintPaid);
            double kMaint = !(paid > 0) || double.IsInfinity(dtDecay) ? 0 : Math.Min(kOpen, paid / dtDecay);
            if (kMaint >= kOpen) return kOpen;
            double decayFactor = dtDecay > 709.0 ? 0.0 : Math.Exp(-dtDecay);
            double lower = kOpen * decayFactor;
            if (!(kMaint > 0)) return NonNegativeFinite(lower);
            double retained = dtDecay <= 0.5
                ? kOpen + (kOpen - kMaint) * PhysicalDemography.Expm1(-dtDecay)
                : kMaint + (kOpen - kMaint) * decayFactor;
            return NonNegativeFinite(Math.Max(lower, Math.Min(kOpen, retained)));
        }

        public static double SanitationDecayRate(SimulationConfig cfg)
            => SanitationEnabled(cfg) ? NonNegativeFinite(cfg.Civic.SanitationCapitalDecayPerYear) : 0;

        // Continuity upkeep target u = delta_sanit * min(K_open, ReqSanitCap(N)) * dt (abandoned surplus drains decay).
        public static double SanitationMaintenanceTarget(WorldState w, Camp c, double years, double? populationOverride = null)
        {
            if (w == null || c == null || !SanitationEnabled(w.Config) || !(years > 0) || double.IsNaN(years)) return 0;
            double kOpen = NonNegativeFinite(c.SanitationCapital);
            if (!(kOpen > Epsilon)) return 0;
            double capped = Math.Min(kOpen, RequiredSanitationCapital(w.Config, populationOverride ?? c.Population));
            double rate = SanitationDecayRate(w.Config);
            if (!(capped > 0) || !(rate > 0)) return 0;
            if (double.IsInfinity(years)) return double.MaxValue;
            return NonNegativeFinite(rate * capped * years);
        }

        // Atomic post-Layer-1 commit (Docs/CivicGovernance.md §5 Choice #3.3). CivicCapital is immediately active;
        // newly built sanitation becomes active at the next boundary once Tier-2 water has been allocated to it,
        // while neglected upkeep reduces the current turn's coverage through ActiveSanitationCapital.
        public static void CommitCapitals(WorldState w, Camp c, double years, double civicMaintPaid, double civicBuilt)
            => CommitCapitals(w, c, years, civicMaintPaid, civicBuilt, 0, 0);

        public static void CommitCapitals(WorldState w, Camp c, double years, double civicMaintPaid, double civicBuilt,
            double sanitMaintPaid, double sanitBuilt)
        {
            if (w == null || c == null || !Enabled(w.Config)) return;
            double retained = RetainCapital(c.CivicCapital, EffectiveCivicDecayRate(w, c), years, civicMaintPaid);
            c.CivicCapital = NonNegativeFinite(retained + NonNegativeFinite(civicBuilt));
            if (!SanitationEnabled(w.Config))
            {
                c.UseActiveSanitationCapital = false;
                return;
            }
            double sanitRetained = RetainCapital(c.SanitationCapital, SanitationDecayRate(w.Config), years, sanitMaintPaid);
            c.ActiveSanitationCapital = NonNegativeFinite(sanitRetained);
            c.UseActiveSanitationCapital = true;
            c.SanitationCapital = NonNegativeFinite(sanitRetained + NonNegativeFinite(sanitBuilt));
        }

        // ---- Phase 2 Tier-2 sanitation water (Docs/CivicGovernance.md §5 Choice #10) ----
        // Cost is O(sanitized villages x 7 local tiles); envelopes are pure projections cached per tile.
        private static double DrinkingPerPersonYear(SimulationConfig cfg)
            => cfg.Agriculture != null ? Math.Max(0, cfg.Agriculture.AnnualDrinkingPerPerson) : 0.10;

        private static bool SanitationSourceTile(Tile t)
            => t != null && t.WaterBody != WaterKind.Saltwater && t.FreshwaterSource != FreshwaterSource.None;

        // Delivered water need over the horizon for the capital the camp already has (opening capital, current N).
        public static double SanitationWaterNeed(WorldState w, Camp c, double years, double? capitalOverride = null)
        {
            if (w == null || c == null || !SanitationEnabled(w.Config) || c.Population <= 0 || !c.IsVillage) return 0;
            if (!(years > 0) || double.IsNaN(years) || double.IsInfinity(years)) return 0;
            double req = RequiredSanitationCapital(w.Config, c.Population);
            double k = NonNegativeFinite(capitalOverride ?? c.SanitationCapital);
            if (!(req > Epsilon) || !(k > Epsilon)) return 0;
            double share = Math.Min(1.0, k / req);
            return NonNegativeFinite(c.Population * share * w.Config.Civic.SanitationWaterPerPersonYear * years);
        }

        private static Dictionary<int, double> HumanDrinkingReservations(WorldState w, double years)
        {
            var reserved = new Dictionary<int, double>();
            double rate = DrinkingPerPersonYear(w.Config);
            foreach (var c in w.Camps)
            {
                if (c.Population <= 0) continue;
                int src = FreshwaterAccess.Source(w, c.Tile);
                reserved[src] = (reserved.TryGetValue(src, out double r) ? r : 0) + c.Population * rate * years;
            }
            return reserved;
        }

        internal static void AllocateSanitationWater(WorldState w, List<Demand> claims, double start, double days,
            TurnRecord record, Action<int> activate)
        {
            if (w == null || !SanitationEnabled(w.Config)) return;
            foreach (var c in w.Camps) { c.SanitationWaterDelivered = 0; c.SanitationWaterRatio = 0; }
            if (!(days > 0) || claims == null) return;
            double years = days / w.Config.DaysPerYear;
            var users = new List<(Camp Camp, double Need)>();
            foreach (var c in w.Camps.OrderBy(c => c.Id))
            {
                double need = SanitationWaterNeed(w, c, years);
                if (need > Epsilon) users.Add((c, need));
            }
            if (users.Count == 0) return;

            // Tier 1 protection: every biological freshwater claim already on the tile, plus human drinking when the
            // agriculture block (which emits Drinking claims) is absent.
            var reserved = new Dictionary<int, double>();
            foreach (var d in claims)
                if (d.Freshwater > 0 && d.Kind != "Irrigation" && d.Kind != "SanitationWater")
                    reserved[d.Tile] = (reserved.TryGetValue(d.Tile, out double r) ? r : 0) + d.Freshwater;
            if (w.Config.Agriculture == null)
                foreach (var p in HumanDrinkingReservations(w, years))
                    reserved[p.Key] = (reserved.TryGetValue(p.Key, out double r) ? r : 0) + p.Value;

            var residual = new Dictionary<int, double>();
            double Residual(int tile)
            {
                if (residual.TryGetValue(tile, out double v)) return v;
                double env = IrrigationEconomy.WaterEnvelope(w, tile, start, days);
                v = Math.Max(0, env - (reserved.TryGetValue(tile, out double r) ? r : 0));
                residual[tile] = v;
                return v;
            }

            var proposals = new List<(Camp Camp, int Tile, double Amount, double Need)>();
            var proposedByTile = new Dictionary<int, double>();
            foreach (var (camp, need) in users)
            {
                var tiles = w.Map.Radius(camp.Tile, 1).Where(t => SanitationSourceTile(w.Tiles[t])).OrderBy(t => t).ToArray();
                double total = 0;
                foreach (int t in tiles) total += Residual(t);
                if (!(total > Epsilon)) continue;
                foreach (int t in tiles)
                {
                    double rs = Residual(t);
                    if (!(rs > Epsilon)) continue;
                    double p = Math.Min(rs, need * rs / total);
                    if (!(p > Epsilon)) continue;
                    proposals.Add((camp, t, p, need));
                    proposedByTile[t] = (proposedByTile.TryGetValue(t, out double q) ? q : 0) + p;
                }
            }
            if (proposals.Count == 0) return;

            var receipts = new List<SanitationWaterReceipt>();
            var receiptCamps = new List<Camp>();
            foreach (var p in proposals.OrderBy(x => x.Tile).ThenBy(x => x.Camp.Id))
            {
                double rs = Residual(p.Tile);
                double sum = proposedByTile[p.Tile];
                double scale = sum > rs ? rs / sum : 1.0;
                double withdrawal = p.Amount * scale * (1 - 1e-12);
                if (!(withdrawal > 0)) continue;
                claims.Add(new Demand { Tile = p.Tile, Entity = p.Camp.Id, Kind = "SanitationWater", Freshwater = withdrawal });
                activate?.Invoke(p.Tile);
                p.Camp.SanitationWaterDelivered += withdrawal;
                receipts.Add(new SanitationWaterReceipt { Source = p.Tile, Camp = p.Camp.Id, Withdrawal = withdrawal, Need = p.Need });
                receiptCamps.Add(p.Camp);
            }
            foreach (var (camp, need) in users)
                camp.SanitationWaterRatio = ClampFinite(camp.SanitationWaterDelivered / need, 0, 1);
            for (int i = 0; i < receipts.Count; i++)
                receipts[i].Ratio = receiptCamps[i].SanitationWaterRatio;
            if (record != null) record.SanitationWater = receipts;
        }

        // Pure planner forecast: the share of a full-coverage sanitation water need the camp's local freshwater could
        // carry after human drinking. Dry oases forecast ~0 so DemandControl never builds unusable drains.
        public static double ForecastSanitationWaterFraction(WorldState w, Camp c, double years, double? population = null)
        {
            if (w == null || c == null || !SanitationEnabled(w.Config) || !(years > 0) || double.IsNaN(years) || double.IsInfinity(years)) return 0;
            double n = population ?? c.Population;
            if (!(n > 0)) return 0;
            double need = n * w.Config.Civic.SanitationWaterPerPersonYear * years;
            if (!(need > Epsilon)) return 0;
            double days = years * w.Config.DaysPerYear;
            int src = FreshwaterAccess.Source(w, c.Tile);
            double drinking = n * DrinkingPerPersonYear(w.Config) * years;
            double available = 0;
            foreach (int t in w.Map.Radius(c.Tile, 1))
            {
                if (!SanitationSourceTile(w.Tiles[t])) continue;
                double env = IrrigationEconomy.WaterEnvelope(w, t, w.Days, days);
                available += Math.Max(0, env - (t == src ? drinking : 0));
            }
            return ClampFinite(available / need, 0, 1);
        }

        // Forecast parity for irrigation planning: the same proportional local split, unscaled by competition.
        internal static IEnumerable<Demand> ForecastSanitationWaterClaims(WorldState w, double years, Func<int, bool> tileKnown = null)
        {
            if (w == null || !SanitationEnabled(w.Config) || !(years > 0)) yield break;
            double days = years * w.Config.DaysPerYear;
            foreach (var c in w.Camps.OrderBy(c => c.Id))
            {
                if (tileKnown != null && !tileKnown(c.Tile)) continue;
                double need = SanitationWaterNeed(w, c, years);
                if (!(need > Epsilon)) continue;
                var tiles = w.Map.Radius(c.Tile, 1).Where(t => SanitationSourceTile(w.Tiles[t]) && (tileKnown == null || tileKnown(t)))
                    .OrderBy(t => t).Select(t => (Tile: t, Env: IrrigationEconomy.WaterEnvelope(w, t, w.Days, days))).ToArray();
                double total = tiles.Sum(x => x.Env);
                if (!(total > Epsilon)) continue;
                foreach (var x in tiles)
                    if (x.Env > Epsilon)
                        yield return new Demand { Tile = x.Tile, Entity = c.Id, Kind = "SanitationWater",
                            Freshwater = Math.Min(x.Env, need * x.Env / total) };
            }
        }

        // Tier 3A trade helpers (pure & thread-safe for Trade.Plan parallel workers).
        public static bool HasAccounting(WorldState w, Camp c)
            => Enabled(w?.Config) && c?.People != null && c.People.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting);

        public static int ExtraTradeRange(WorldState w, Camp c)
            => HasAccounting(w, c) ? Math.Max(0, w.Config.Civic.TokensTradeRangeBonus) : 0;

        public static int ExtraTradeRequisitions(WorldState w, Camp c)
            => HasAccounting(w, c) ? Math.Max(0, w.Config.Civic.TokensTradeRequisitionBonus) : 0;

        public static double TradeMarginMultiplier(SimulationConfig cfg, bool requesterAccounting, bool responderAccounting)
        {
            if (!Enabled(cfg) || (!requesterAccounting && !responderAccounting)) return 1.0;
            double keep = 1.0 - Numbers.Clamp(cfg.Civic.TokensMarginReduction, 0, 0.9);
            double mult = 1.0;
            if (requesterAccounting) mult *= keep;
            if (responderAccounting) mult *= keep;
            return mult;
        }

        // Pure alignment, staffing, capital & coverage laws (Docs/CivicGovernance.md §5 Choice #3)
        public static double AlignmentFriction(SimulationConfig cfg, double stratification, double envStratification)
        {
            if (!Enabled(cfg) || double.IsNaN(stratification) || double.IsNaN(envStratification)) return 1;
            double p = Numbers.Clamp(stratification, -1, 1);
            double pEnv = Numbers.Clamp(envStratification, -1, 1);
            double diff = Math.Abs(p - pEnv);
            double rate = cfg.Civic.AlignmentFrictionRate;
            if (!(diff > 0) || !(rate > 0)) return 1;
            double prod = rate * diff;
            if (double.IsInfinity(prod)) return double.MaxValue;
            double result = 1 + prod;
            return double.IsInfinity(result) ? double.MaxValue : result;
        }
        public static double AlignmentFriction(WorldState w, Camp c)
            => c == null ? 1 : AlignmentFriction(w?.Config, c.CivicStratification, c.LastEnvironmentalStratification);

        public static double RequiredStewards(SimulationConfig cfg, double population, double complexity,
            double stratification, bool hasAccounting, double envStratification)
        {
            if (!Enabled(cfg) || !(population > 0) || !(complexity > Epsilon) || double.IsNaN(stratification)) return 0;
            var s = cfg.Civic;
            double c = Numbers.Clamp(complexity, 0, 1);
            double p = Numbers.Clamp(stratification, -1, 1);
            double u = 0.5 * (p + 1);
            double excessRaw = (1.0 - u) * (s.EgalitarianStewardExponent - 1.0) + u * (s.StratifiedStewardExponent - 1.0);
            if (double.IsInfinity(excessRaw)) excessRaw = double.MaxValue;
            double excess = hasAccounting ? s.AccountingExponentRetention * excessRaw : excessRaw;
            if (double.IsInfinity(excess)) excess = double.MaxValue;
            double rate = (1.0 - u) * s.EgalitarianStewardRate + u * s.StratifiedStewardRate;
            bool rateNormal = rate >= 1e-290 && !double.IsInfinity(rate);
            if (!rateNormal && !(s.EgalitarianStewardRate > 0 && u < 1.0) && !(s.StratifiedStewardRate > 0 && u > 0.0))
                return 0;
            if (double.IsInfinity(population)) return double.MaxValue;

            double logKnee = 0.0;
            if (excess > 0 && s.StewardScalingKnee > 0)
            {
                double logRatio = Math.Log(population) - Math.Log(s.StewardScalingKnee);
                if (logRatio > 30.0)
                    logKnee = excess * logRatio;
                else if (logRatio < -20.0)
                {
                    double logLogKnee = Math.Log(excess) + 2.0 * logRatio - Math.Log(2.0);
                    logKnee = logLogKnee >= 709.0 ? double.PositiveInfinity : Math.Exp(logLogKnee);
                }
                else
                {
                    double r = population / s.StewardScalingKnee;
                    logKnee = excess * (0.5 * PhysicalDemography.Log1p(r * r));
                }
            }

            double friction = AlignmentFriction(cfg, p, envStratification);
            if (rateNormal && logKnee < 709.0)
            {
                double cp = c * population;
                if (cp >= 1e-290 && !double.IsInfinity(cp))
                {
                    double baseTerm = cp * rate;
                    if (baseTerm >= 1e-290 && !double.IsInfinity(baseTerm))
                    {
                        double knee = logKnee > 0 ? Math.Exp(logKnee) : 1.0;
                        double required = baseTerm * knee * friction;
                        if (required >= 1e-290 && !double.IsInfinity(required))
                            return required;
                    }
                }
            }
            double logRate = rateNormal ? Math.Log(rate) : LogConvexPositive(s.EgalitarianStewardRate, s.StratifiedStewardRate, u);
            if (double.IsNegativeInfinity(logRate)) return 0;
            double logTotal = Math.Log(c) + Math.Log(population) + logRate + logKnee + Math.Log(friction);
            if (double.IsNaN(logTotal)) return 0;
            if (logTotal >= 709.782712893384) return double.MaxValue;
            double result = Math.Exp(logTotal);
            return double.IsInfinity(result) ? double.MaxValue : result;
        }
        public static double RequiredStewards(WorldState w, Camp c, double? population = null)
        {
            if (w == null || c == null || !Enabled(w.Config)) return 0;
            double n = population ?? c.Population;
            bool has3A = c.People != null && c.People.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting);
            return RequiredStewards(w.Config, n, c.CivicComplexity, c.CivicStratification, has3A, c.LastEnvironmentalStratification);
        }

        public static double RequiredCivicCapital(SimulationConfig cfg, double population, double complexity, double stratification)
        {
            if (!Enabled(cfg) || !(population > 0) || !(complexity > Epsilon) || double.IsNaN(stratification)) return 0;
            var s = cfg.Civic;
            double c = Numbers.Clamp(complexity, 0, 1);
            double u = 0.5 * (Numbers.Clamp(stratification, -1, 1) + 1);
            double rate = (1.0 - u) * s.EgalitarianCapitalPerPerson + u * s.StratifiedCapitalPerPerson;
            bool rateNormal = rate >= 1e-290 && !double.IsInfinity(rate);
            if (!rateNormal && !(s.EgalitarianCapitalPerPerson > 0 && u < 1.0) && !(s.StratifiedCapitalPerPerson > 0 && u > 0.0))
                return 0;
            if (double.IsInfinity(population)) return double.MaxValue;
            if (rateNormal)
            {
                double cp = c * population;
                if (cp >= 1e-290 && !double.IsInfinity(cp))
                {
                    double required = cp * rate;
                    if (required >= 1e-290 && !double.IsInfinity(required))
                        return required;
                }
            }
            double logRate = rateNormal ? Math.Log(rate) : LogConvexPositive(s.EgalitarianCapitalPerPerson, s.StratifiedCapitalPerPerson, u);
            if (double.IsNegativeInfinity(logRate)) return 0;
            double logTotal = Math.Log(c) + Math.Log(population) + logRate;
            if (double.IsNaN(logTotal)) return 0;
            if (logTotal >= 709.782712893384) return double.MaxValue;
            double result = Math.Exp(logTotal);
            return double.IsInfinity(result) ? double.MaxValue : result;
        }
        public static double RequiredCivicCapital(WorldState w, Camp c, double? population = null)
            => w == null || c == null ? 0 : RequiredCivicCapital(w.Config, population ?? c.Population, c.CivicComplexity, c.CivicStratification);

        public static double RequiredSanitationCapital(SimulationConfig cfg, double population)
        {
            if (!SanitationEnabled(cfg) || !(population > 0)) return 0;
            double rate = cfg.Civic.SanitationCapitalPerPerson;
            if (!(rate > 0)) return 0;
            if (double.IsInfinity(population) || double.IsInfinity(rate)) return double.MaxValue;
            double required = rate * population;
            return double.IsInfinity(required) ? double.MaxValue : required;
        }

        public static double ActiveSanitationCapital(Camp c)
            => c == null ? 0 : NonNegativeFinite(c.UseActiveSanitationCapital ? c.ActiveSanitationCapital : c.SanitationCapital);

        public static double CivicCoverage(WorldState w, Camp c)
        {
            if (w == null || c == null || !Enabled(w.Config) || c.Population <= 0 || !(c.CivicComplexity > Epsilon)) return 0;
            double reqCap = RequiredCivicCapital(w, c);
            double reqStewards = RequiredStewards(w, c);
            if (!(reqCap > Epsilon) || !(reqStewards > Epsilon)) return 0;
            int stewards = c.Workforce != null ? CultivationEconomy.Workers(c, WorkforceRoles.Steward) : 0;
            double capRatio = Math.Min(1.0, NonNegativeFinite(c.CivicCapital) / reqCap);
            double stewardRatio = Math.Min(1.0, Math.Max(0, stewards) / reqStewards);
            return Numbers.Clamp(Math.Min(capRatio, stewardRatio), 0, 1);
        }

        public static double SanitationCoverage(WorldState w, Camp c)
        {
            if (w == null || c == null || !SanitationRecipesAvailable(w, c) || c.Population <= 0) return 0;
            double req = RequiredSanitationCapital(w.Config, c.Population);
            if (!(req > Epsilon)) return 0;
            double capRatio = Math.Min(1.0, ActiveSanitationCapital(c) / req);
            double water = ClampFinite(c.SanitationWaterRatio, 0, 1);
            return Numbers.Clamp(capRatio * water, 0, 1);
        }

        // Mitigation, cohesion, QoL & continuous P trade-offs (Docs/CivicGovernance.md §5 Choices #4 & #5)
        public static double CrowdingMitigationAlpha(SimulationConfig cfg, double complexity, double stratification)
        {
            if (!Enabled(cfg) || !(complexity > Epsilon) || double.IsNaN(stratification)) return 0;
            var s = cfg.Civic;
            double c = Numbers.Clamp(complexity, 0, 1);
            double p = Numbers.Clamp(stratification, -1, 1);
            double cp = Math.Pow(c, s.CrowdingShapeExponent);
            double denom = -PhysicalDemography.Expm1(-s.CrowdingShapeRate);
            double fc = s.CrowdingShapeRate > 1e-16 && denom > 0
                ? Numbers.Clamp((-PhysicalDemography.Expm1(-s.CrowdingShapeRate * cp)) / denom, 0, 1)
                : cp;
            return Numbers.Clamp((s.CrowdingMitigationBase + s.CrowdingMitigationStratBonus * p * c) * fc, 0, 1 - s.MinCrowdingMitigation);
        }
        public static double CrowdingMitigationFor(SimulationConfig cfg, double complexity, double stratification, double coverage)
        {
            if (!Enabled(cfg) || !(complexity > Epsilon) || !(coverage > 0) || double.IsNaN(stratification)) return 1;
            double cov = Numbers.Clamp(coverage, 0, 1);
            double alpha = CrowdingMitigationAlpha(cfg, complexity, stratification);
            return Numbers.Clamp(1 - alpha * cov, cfg.Civic.MinCrowdingMitigation, 1);
        }
        public static double CrowdingMitigation(WorldState w, Camp c)
            => w == null || c == null || !Enabled(w.Config) ? 1 : CrowdingMitigationFor(w.Config, c.CivicComplexity, c.CivicStratification, CivicCoverage(w, c));

        public static double DiseaseMitigationFor(SimulationConfig cfg, double complexity, double stratification, double coverage)
        {
            if (!SanitationEnabled(cfg) || !(complexity > Epsilon) || !(coverage > 0) || double.IsNaN(stratification)) return 1;
            var s = cfg.Civic;
            double c = Numbers.Clamp(complexity, 0, 1);
            double p = Numbers.Clamp(stratification, -1, 1);
            double cov = Numbers.Clamp(coverage, 0, 1);
            double h = s.DiseaseHalfScale;
            double fd = !(h > 0) ? 1.0 : (double.IsInfinity(h) ? c : Numbers.Clamp(c * ((1 + h) / (c + h)), 0, 1));
            double alpha = Numbers.Clamp((s.DiseaseMitigationBase + s.DiseaseMitigationStratBonus * p) * fd, 0, 1 - s.MinDiseaseMitigation);
            return Numbers.Clamp(1 - alpha * cov, s.MinDiseaseMitigation, 1);
        }
        public static double DiseaseMitigation(WorldState w, Camp c)
            => w == null || c == null || !SanitationEnabled(w.Config) ? 1 : DiseaseMitigationFor(w.Config, c.CivicComplexity, c.CivicStratification, SanitationCoverage(w, c));

        public static double CohesionMultiplierFor(SimulationConfig cfg, double complexity, double stratification, double coverage)
        {
            if (!Enabled(cfg) || !(complexity > Epsilon) || !(coverage > 0) || double.IsNaN(stratification)) return 1;
            var s = cfg.Civic;
            double c = Numbers.Clamp(complexity, 0, 1);
            double p = Numbers.Clamp(stratification, -1, 1);
            double cov = Numbers.Clamp(coverage, 0, 1);
            double stratFactor = 1 + s.CohesionStratFactor * p * Math.Pow(c, 1.5);
            double g = (s.CohesionLinearGain * c + s.CohesionUrbanGain * Math.Pow(c, s.CohesionUrbanExponent)) * stratFactor;
            double result = 1 + Math.Max(0, g) * cov;
            if (double.IsInfinity(result))
            {
                double scale = Math.Max(0, stratFactor) * cov;
                double scaled = ScaledGainTerm(s.CohesionLinearGain, c, scale)
                    + ScaledGainTerm(s.CohesionUrbanGain, Math.Pow(c, s.CohesionUrbanExponent), scale);
                result = double.IsInfinity(scaled) ? double.MaxValue : 1 + scaled;
            }
            return double.IsInfinity(result) ? double.MaxValue : result;
        }
        public static double CohesionMultiplier(WorldState w, Camp c)
            => w == null || c == null || !Enabled(w.Config) ? 1 : CohesionMultiplierFor(w.Config, c.CivicComplexity, c.CivicStratification, CivicCoverage(w, c));

        public static double QoLEqualityMultiplierFor(SimulationConfig cfg, double complexity, double stratification, double coverage)
        {
            if (!Enabled(cfg) || !(complexity > Epsilon) || !(coverage > 0) || double.IsNaN(stratification)) return 1;
            var s = cfg.Civic;
            double c = Numbers.Clamp(complexity, 0, 1);
            double p = Numbers.Clamp(stratification, -1, 1);
            double cov = Numbers.Clamp(coverage, 0, 1);
            return Math.Max(0, 1 + (-s.QoLEqualityLinear * p + s.QoLEqualityQuadratic * p * p) * c * cov);
        }
        public static double QoLEqualityMultiplier(WorldState w, Camp c)
            => w == null || c == null || !Enabled(w.Config) ? 1 : QoLEqualityMultiplierFor(w.Config, c.CivicComplexity, c.CivicStratification, CivicCoverage(w, c));

        public static double PublicWorksMobilizationMultiplier(WorldState w, Camp c)
        {
            if (w == null || c == null || !Enabled(w.Config) || !(c.CivicComplexity > Epsilon) || double.IsNaN(c.CivicStratification)) return 1;
            double cov = CivicCoverage(w, c);
            if (!(cov > 0)) return 1;
            double u = 0.5 * (Numbers.Clamp(c.CivicStratification, -1, 1) + 1);
            double result = 1 + w.Config.Civic.StratifiedPublicWorksBonus * u * u * Numbers.Clamp(c.CivicComplexity, 0, 1) * cov;
            return double.IsInfinity(result) ? double.MaxValue : result;
        }

        public static double GranaryStorageMultiplier(WorldState w, Camp c, double? capitalOverride = null)
        {
            if (w == null || c == null || !Enabled(w.Config) || !(c.CivicComplexity > Epsilon) || c.Population <= 0 || double.IsNaN(c.CivicStratification)) return 1;
            double cov;
            if (!capitalOverride.HasValue) cov = CivicCoverage(w, c);
            else
            {
                double reqCap = RequiredCivicCapital(w, c);
                double reqStewards = RequiredStewards(w, c);
                if (!(reqCap > Epsilon) || !(reqStewards > Epsilon)) return 1;
                int stewards = c.Workforce != null ? CultivationEconomy.Workers(c, WorkforceRoles.Steward) : 0;
                double cap = NonNegativeFinite(capitalOverride.Value);
                cov = Numbers.Clamp(Math.Min(cap / reqCap, Math.Max(0, stewards) / reqStewards), 0, 1);
            }
            if (!(cov > 0)) return 1;
            double u = 0.5 * (Numbers.Clamp(c.CivicStratification, -1, 1) + 1);
            double result = 1 + w.Config.Civic.StratifiedGranaryStorageBonus * u * u * Numbers.Clamp(c.CivicComplexity, 0, 1) * cov;
            return double.IsInfinity(result) ? double.MaxValue : result;
        }

        public static double ExitMismatch(Camp c)
        {
            if (c == null || !(c.CivicComplexity > Epsilon) ||
                double.IsNaN(c.CivicStratification) || double.IsNaN(c.LastEnvironmentalStratification) ||
                double.IsNaN(c.LastCircumscription))
                return 0;
            double cVal = Numbers.Clamp(c.CivicComplexity, 0, 1);
            double pDiff = Math.Max(0, Numbers.Clamp(c.CivicStratification, -1, 1) - Numbers.Clamp(c.LastEnvironmentalStratification, -1, 1));
            double openness = 1 - Numbers.Clamp(c.LastCircumscription, 0, 1);
            return cVal * pDiff * openness;
        }

        // Steering, dt-invariant relaxation & display classification (Docs/CivicGovernance.md §5 Choices #2 & #7)
        public static double SteeringOffset(SimulationConfig cfg, FissionPolicy fission, ImmigrationPolicy immigration, CivicFocus focus)
        {
            if (!Enabled(cfg)) return 0;
            var s = cfg.Civic;
            double df = fission == FissionPolicy.Encourage ? -s.FissionPolicySteeringShift
                : fission == FissionPolicy.Discourage ? s.FissionPolicySteeringShift : 0;
            double di = immigration == ImmigrationPolicy.Open ? -s.ImmigrationPolicySteeringShift
                : immigration == ImmigrationPolicy.Restricted ? s.ImmigrationPolicySteeringShift : 0;
            // CivicFocus.Auto contributes exact 0.0 (zero cliffs, zero self-alignment penalty).
            double dc = focus == CivicFocus.Communal ? -s.CivicFocusSteeringShift
                : focus == CivicFocus.Centralized ? s.CivicFocusSteeringShift : 0;
            return df + di + dc;
        }

        public static double TargetStratification(SimulationConfig cfg, double envStratification,
            FissionPolicy fission, ImmigrationPolicy immigration, CivicFocus focus)
        {
            double env = ClampFinite(envStratification, -1, 1);
            return ClampFinite(env + SteeringOffset(cfg, fission, immigration, focus), -1, 1);
        }

        public static double Relax(double current, double target, double rate, double years)
        {
            if (double.IsNaN(current)) current = 0;
            else if (double.IsInfinity(current)) current = current > 0 ? double.MaxValue : -double.MaxValue;
            if (double.IsNaN(target) || !(years > 0) || double.IsInfinity(years) || !(rate > 0) || double.IsInfinity(rate)) return current;
            if (double.IsInfinity(target)) target = target > 0 ? double.MaxValue : -double.MaxValue;
            double rt = rate * years;
            if (rt < 1e-290)
            {
                double diffSmall = target - current;
                if (diffSmall == 0.0) return current;
                double logAbsDiff = double.IsInfinity(diffSmall)
                    ? Math.Log(Math.Abs(0.5 * target - 0.5 * current)) + Math.Log(2.0)
                    : Math.Log(Math.Abs(diffSmall));
                double delta = Math.Exp(logAbsDiff + Math.Log(rate) + Math.Log(years));
                double step = diffSmall > 0 ? delta : -delta;
                double resSmall = current + step;
                return double.IsInfinity(resSmall) ? (resSmall > 0 ? double.MaxValue : -double.MaxValue) : resSmall;
            }
            double w = -PhysicalDemography.Expm1(-rt);
            if (w <= 0.0) return current;
            if (w >= 1.0) return target;
            double diff = target - current;
            double result = double.IsInfinity(diff) ? (1.0 - w) * current + w * target : current + diff * w;
            if (double.IsInfinity(result)) return result > 0 ? double.MaxValue : -double.MaxValue;
            return result;
        }

        // dP/dt = lambda * (E - P + strength*(1-|E|)*clamp(P/0.15,-1,1)).
        // E is the environmental + policy target, frozen over the horizon. The
        // continuous field is affine in three regions; monotonic scalar motion
        // crosses each boundary at most once. Exact event times preserve the
        // semigroup without annual stepping, allocations, or extra saved state.
        public static double AdvanceStratification(SimulationConfig cfg, double current, double target, double years)
        {
            double p = ClampFinite(current, -1, 1), e = ClampFinite(target, -1, 1);
            double rate = cfg?.Civic?.StratificationRelaxationRate ?? 0.020;
            double strength = Enabled(cfg) ? cfg.Civic.StratificationFeedbackStrength : 0;
            if (!(strength > 0)) return ClampFinite(Relax(p, e, rate, years), -1, 1);
            if (!(years > 0) || double.IsInfinity(years) || !(rate > 0) || double.IsInfinity(rate)) return p;
            const double width = 0.15;
            double bias = Math.Min(0.30, strength) * (1 - Math.Abs(e));
            double remaining = rate * years;
            for (int segment = 0; segment < 3; segment++)
            {
                double velocity = e - p + bias * Numbers.Clamp(p / width, -1, 1);
                if (velocity == 0) return p;
                bool lower = p < -width || (p == -width && velocity < 0);
                bool upper = p > width || (p == width && velocity > 0);
                double slope = lower || upper ? -1 : bias / width - 1;
                double intercept = lower ? e - bias : upper ? e + bias : e;
                velocity = intercept + slope * p;
                if (velocity == 0) return p;
                double boundary = lower ? -width : upper ? width : velocity > 0 ? width : -width;
                bool towardBoundary = !lower && !upper || lower && velocity > 0 || upper && velocity < 0;
                double hit = double.PositiveInfinity;
                if (towardBoundary)
                {
                    double travel = (boundary - p) / velocity;
                    double ratio = slope * travel;
                    if (slope == 0) hit = travel;
                    else if (ratio > -1) hit = PhysicalDemography.Log1p(ratio) / slope;
                }
                if (!double.IsInfinity(hit) && hit <= remaining)
                {
                    p = boundary;
                    remaining -= hit;
                    if (!(remaining > 0)) return p;
                    continue;
                }
                double step = slope == 0 ? velocity * remaining
                    : velocity * (PhysicalDemography.Expm1(slope * remaining) / slope);
                return ClampFinite(p + step, -1, 1);
            }
            return p;
        }

        public static double NextComplexity(WorldState w, Camp c, double years)
        {
            if (w == null || c == null || !Enabled(w.Config)) return 0;
            double cMax = MaxComplexity(w, c);
            double cTarget = ClampFinite(c.LastEnvironmentalComplexity, 0, cMax);
            return ClampFinite(Relax(ClampFinite(c.CivicComplexity, 0, 1), cTarget, w.Config.Civic.ComplexityRelaxationRate, years), 0, cMax);
        }

        // UI / Hover display classification only; never feeds P*.
        public static string EffectiveFocusLabel(CivicFocus focus, double envStratification)
        {
            if (focus == CivicFocus.Communal) return "Communal";
            if (focus == CivicFocus.Centralized) return "Centralized";
            if (double.IsNaN(envStratification)) return "Hybrid";
            if (envStratification <= -0.15) return "Communal";
            if (envStratification >= 0.15) return "Centralized";
            return "Hybrid";
        }
        public static string EffectiveFocusLabel(Camp c)
            => c == null ? "Hybrid" : EffectiveFocusLabel(c.CivicFocus, c.LastEnvironmentalStratification);

        // Fission daughter dampening, abandonment & copy helpers (Docs/CivicGovernance.md §3.2 & §5 Choice #2)
        public static (double Complexity, double Stratification) DaughterCoordinates(WorldState w, Camp parent)
        {
            if (w == null || parent == null || !Enabled(w.Config)) return (0, 0);
            var s = w.Config.Civic;
            double cMax = MaxComplexity(w, parent);
            double parentC = ClampFinite(parent.CivicComplexity, 0, 1);
            double parentP = ClampFinite(parent.CivicStratification, -1, 1);
            double c = Numbers.Clamp(s.DaughterComplexityRetention * parentC, 0, cMax);
            double p = Numbers.Clamp(s.DaughterStratificationRetention * parentP - s.DaughterFrontierEgalitarianShift, -1, 1);
            return (c, p);
        }

        public static void InitializeDaughterView(WorldState w, Camp parent, Camp view)
        {
            if (view == null) return;
            view.CivicCapital = 0;
            view.SanitationCapital = 0;
            view.RecentTradeVolume = 0;
            view.SanitationWaterRatio = 0;
            view.SanitationWaterDelivered = 0;
            view.ActiveSanitationCapital = 0;
            view.UseActiveSanitationCapital = false;
            view.ImmigrationPolicy = ImmigrationPolicy.Neutral;
            view.CivicFocus = CivicFocus.Auto;
            view.LastCircumscription = 0;
            if (!Enabled(w?.Config) || parent == null)
            {
                view.CivicComplexity = 0;
                view.CivicStratification = 0;
                view.LastEnvironmentalComplexity = 0;
                view.LastEnvironmentalStratification = 0;
                return;
            }
            var (c, p) = DaughterCoordinates(w, parent);
            view.CivicComplexity = c;
            view.CivicStratification = p;
            view.LastEnvironmentalComplexity = c;
            view.LastEnvironmentalStratification = p;
        }

        public static void ReleaseAbandoned(Camp camp)
        {
            if (camp == null) return;
            camp.CivicCapital = 0;
            camp.SanitationCapital = 0;
            camp.SanitationWaterRatio = 0;
            camp.SanitationWaterDelivered = 0;
            camp.ActiveSanitationCapital = 0;
            camp.UseActiveSanitationCapital = false;
        }

        internal static void CopyCampCivicState(Camp source, Camp destination)
        {
            if (source == null || destination == null) return;
            destination.CivicComplexity = source.CivicComplexity;
            destination.CivicStratification = source.CivicStratification;
            destination.CivicCapital = source.CivicCapital;
            destination.SanitationCapital = source.SanitationCapital;
            destination.ImmigrationPolicy = source.ImmigrationPolicy;
            destination.CivicFocus = source.CivicFocus;
            destination.RecentTradeVolume = source.RecentTradeVolume;
            destination.SanitationWaterRatio = source.SanitationWaterRatio;
            destination.LastEnvironmentalComplexity = source.LastEnvironmentalComplexity;
            destination.LastEnvironmentalStratification = source.LastEnvironmentalStratification;
            destination.LastCircumscription = source.LastCircumscription;
        }

        // Trade volume conversion (Docs/CivicGovernance.md §5 Choice #6)
        // Note: TradeRecord.WantQuantity and GiveQuantity are already the delivered quantities (Trade.cs:1157).
        public static double TradeRecordVolumePersonYears(SimulationConfig cfg, TradeRecord t)
        {
            if (cfg == null || t == null) return 0;
            double foodPerPersonYear = SafeFoodPerPersonYear(cfg);
            double pyWant = TradeLegPersonYears(t.Want, t.WantQuantity > 0 ? t.WantQuantity : 0, foodPerPersonYear);
            double pyGive = TradeLegPersonYears(t.Give, t.GiveQuantity > 0 ? t.GiveQuantity : 0, foodPerPersonYear);
            double vol = 0.5 * pyWant + 0.5 * pyGive;
            return double.IsInfinity(vol) ? double.MaxValue : vol;
        }

        private static double TradeLegPersonYears(TradeGood good, double qty, double foodPerPersonYear)
        {
            if (!(qty > 0)) return 0;
            double py;
            switch (good)
            {
                case TradeGood.Food: py = qty / foodPerPersonYear; break;
                case TradeGood.Cloth:
                case TradeGood.AnimalFabric: py = qty * 20.0; break;
                case TradeGood.HousingMaterial:
                case TradeGood.LightHousing: py = qty * 4.0; break;
                case TradeGood.Pots: py = qty * 5.0; break;
                default: py = qty; break;
            }
            return double.IsInfinity(py) ? double.MaxValue : py;
        }

        // Local clay availability for Tier 3A dry-clay / pottery prerequisite (Docs/CivicGovernance.md §5 Choice #6)
        public static bool HasLocalClay(WorldState w, Camp c)
        {
            if (w == null || c == null || w.Resources == null) return false;
            var resources = w.Resources;
            var matIndex = MaterialAccessIndex.For(w);
            foreach (int t in w.Map.Radius(c.Tile, 1))
            {
                if (!w.Tiles[t].TraversableLand) continue;
                if (resources.Clay != null && t >= 0 && t < resources.Clay.Length &&
                    resources.SurfaceAccessible(t, MaterialId.Clay) > 1e-9)
                    return true;
                foreach (var sr in matIndex.Sites(t))
                    if (sr.Body != null && sr.Body.Components != null &&
                        resources.AccessibleAt(sr.Body, sr.Site, MaterialId.Clay) > 1e-9)
                        return true;
            }
            return false;
        }

        // Inert trade diffusion hook (Docs/CivicGovernance.md §5 Choice #6; strictly no-op when PrecedentDiffusionPerTrade == 0)
        internal static void RecordTradeDiffusion(WorldState w, Society receiver, Society partner, double volume)
        {
            if (w == null || receiver == null || partner == null || !(volume > 0) || double.IsInfinity(volume) ||
                !Enabled(w.Config) || receiver.Progression == null || receiver.Progression.Era != Era.Neolithic)
                return;
            double rate = w.Config.Civic.PrecedentDiffusionPerTrade;
            if (!(rate > 0) || double.IsInfinity(rate) || partner.CivicPrecedents == null || partner.CivicPrecedents.Count == 0) return;
            bool sanitationOn = SanitationEnabled(w.Config);
            double credit = volume * rate;
            if (double.IsInfinity(credit)) credit = double.MaxValue;
            foreach (string precedent in CivicPrecedents.All)
            {
                if (precedent == CivicPrecedents.PlannedQuartersAndDrainage && !sanitationOn) continue;
                if (partner.HasCivicPrecedent(precedent) && !receiver.HasCivicPrecedent(precedent))
                    AddExperience(receiver, precedent, credit);
            }
        }

        // Pure single-camp driver query (reuses CivicLocalIndex)
        public static CivicEnvironmentDrivers EvaluateEnvironment(WorldState w, Camp c)
        {
            if (w == null || c == null || w.Config == null || w.Tiles == null) return default;
            return new CivicLocalIndex(w).Evaluate(c);
        }

        // Close-phase update (Docs/CivicGovernance.md §5 Choice #6 & §11 C)
        // Strict internal order: 1. Trade EMA, 2. Practice & Precedent Unlocks, 3. Local Attractors & dt-Relaxation.
        public static void Close(WorldState w, TurnRecord record, double horizonYears)
        {
            if (w == null || !Enabled(w.Config)) return;
            using var profile = PhaseProfile.Scope("Civic.Close");
            var cfg = w.Config;
            var s = cfg.Civic;
            double dt = horizonYears > 0 && !double.IsInfinity(horizonYears) ? horizonYears : 0;

            // Step 1: Update Camp.RecentTradeVolume EMA once per active camp from delivered TurnRecord.Trades.
            if (dt > 0)
            {
                Dictionary<int, double> turnVolumeByCamp = null;
                if (record?.Trades != null && record.Trades.Count > 0)
                {
                    turnVolumeByCamp = new Dictionary<int, double>();
                    foreach (var t in record.Trades)
                    {
                        if (t == null) continue;
                        double vol = TradeRecordVolumePersonYears(cfg, t);
                        if (vol <= 0) continue;
                        double rqSum = (turnVolumeByCamp.TryGetValue(t.Requester, out double rq) ? rq : 0) + vol;
                        turnVolumeByCamp[t.Requester] = double.IsInfinity(rqSum) ? double.MaxValue : rqSum;
                        if (t.Responder != t.Requester)
                        {
                            double rpSum = (turnVolumeByCamp.TryGetValue(t.Responder, out double rp) ? rp : 0) + vol;
                            turnVolumeByCamp[t.Responder] = double.IsInfinity(rpSum) ? double.MaxValue : rpSum;
                        }
                    }
                }
                double denomDt = Math.Max(1e-6, dt);
                foreach (var c in w.Camps.OrderBy(c => c.Id))
                {
                    if (c.Population <= 0) continue;
                    double turnVol = turnVolumeByCamp != null && turnVolumeByCamp.TryGetValue(c.Id, out double v) ? v : 0;
                    double annualFlow = turnVol / denomDt;
                    if (double.IsInfinity(annualFlow)) annualFlow = double.MaxValue;
                    double nextTrade = Relax(NonNegativeFinite(c.RecentTradeVolume), annualFlow, s.TradeVolumeRelaxationRate, dt);
                    c.RecentTradeVolume = !(nextTrade > 0) ? 0 : (double.IsInfinity(nextTrade) ? double.MaxValue : nextTrade);
                }
            }

            // Step 2: Accrue Civic Practice in canonical camp-ID order, then commit precedent unlocks
            // in canonical society-ID x tier order (lifting C_max BEFORE Step 3 relaxes (C, P)).
            if (dt > 0)
            {
                Dictionary<int, double> builderWorkYears = null;
                if (record?.Production != null && record.Production.Count > 0)
                {
                    double intervalYears = cfg.BaseDays / cfg.DaysPerYear;
                    if (!(intervalYears > 0) || double.IsInfinity(intervalYears)) intervalYears = 0;
                    builderWorkYears = new Dictionary<int, double>();
                    foreach (var p in record.Production)
                    {
                        if (p == null || p.Role != WorkforceRoles.Builder || !(p.WorkerIntervals > 0)) continue;
                        double addBwy = NonNegativeFinite(p.WorkerIntervals) * intervalYears;
                        double bwySum = (builderWorkYears.TryGetValue(p.Camp, out double prior) ? prior : 0) + (double.IsInfinity(addBwy) ? double.MaxValue : addBwy);
                        builderWorkYears[p.Camp] = double.IsInfinity(bwySum) ? double.MaxValue : bwySum;
                    }
                }

                double foodPerPersonYear = SafeFoodPerPersonYear(cfg);
                bool sanitationOn = SanitationEnabled(cfg);
                double denomDt = Math.Max(1e-6, dt);

                foreach (var c in w.Camps.OrderBy(c => c.Id))
                {
                    if (c.Population <= 0) continue;
                    var soc = c.People;
                    if (soc == null) continue;
                    if (soc.CivicExperience == null) soc.CivicExperience = new Dictionary<string, double>();
                    if (soc.CivicPrecedents == null) soc.CivicPrecedents = new List<string>();
                    if (soc.Progression == null || soc.Progression.Era != Era.Neolithic) continue;

                    // Anti-oscillation invariant: unboosted base housing cohesion scale N_{s,0}.
                    double rhoCrowd = BaseCohesionCrowding(w, c);
                    int stewards = c.Workforce != null ? Math.Max(0, CultivationEconomy.Workers(c, WorkforceRoles.Steward)) : 0;
                    double bwy = builderWorkYears != null && builderWorkYears.TryGetValue(c.Id, out double bw) ? bw : 0;

                    bool hasT1 = soc.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration);
                    bool hasT2 = soc.HasCivicPrecedent(CivicPrecedents.CivicOrganization);
                    bool hasT3A = soc.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting);
                    bool hasT3B = soc.HasCivicPrecedent(CivicPrecedents.PlannedQuartersAndDrainage);

                    // Tier 1: CeremonialIntegration (strictly gated on Neolithic AND c.StationaryTurns > 0,
                    // using exact analytical permanence ramp integral over [s0, s1]).
                    if (!hasT1 && soc.Progression.Era == Era.Neolithic && c.StationaryTurns > 0)
                    {
                        double s1 = NonNegativeFinite(c.StationaryYears);
                        double s0 = Math.Max(0, s1 - dt);
                        double a1 = Math.Min(s1, 40.0), a0 = Math.Min(s0, 40.0);
                        double dtEff = (a1 * a1 - a0 * a0) / 80.0 + (s1 > 40.0 ? Math.Min(dt, s1 - 40.0) : 0.0);
                        double dx = dtEff * Numbers.Clamp((rhoCrowd - 0.25) / 0.50, 0, 1);
                        if (dx > 0) AddExperience(soc, CivicPrecedents.CeremonialIntegration, dx);
                    }

                    // Tier 2: CivicOrganization
                    if (hasT1 && !hasT2)
                    {
                        double denom10 = denomDt * 10.0;
                        double bwy10 = double.IsInfinity(denom10) ? (bwy / denomDt) / 10.0 : bwy / denom10;
                        double dx = dt * Math.Min(1.0, stewards / 15.0)
                            * (0.55 * Numbers.Clamp((rhoCrowd - 0.50) / 0.60, 0, 1)
                             + 0.45 * Math.Min(1.0, bwy10));
                        if (dx > 0) AddExperience(soc, CivicPrecedents.CivicOrganization, dx);
                    }

                    // Tier 3A: TokensAndAccounting (Pottery OR Pots OR local Clay OR Resources == null)
                    if (hasT2 && !hasT3A)
                    {
                        bool hasPottery = soc.HasTech(NeolithicTech.Pottery);
                        bool eligible3A = hasPottery || (c.Inventory != null && c.Inventory.Pots > 1e-6) || HasLocalClay(w, c) || w.Resources == null;
                        if (eligible3A)
                        {
                            double pop = Math.Max(1, c.Population);
                            double fYr = pop * foodPerPersonYear;
                            double fYr20 = fYr * 20.0;
                            double totalFood = NonNegativeFinite(NonNegativeFinite(c.PlantFood) + NonNegativeFinite(c.AnimalFood) + NonNegativeFinite(c.AquaticFood));
                            double storeYears;
                            if (!double.IsInfinity(fYr20))
                                storeYears = totalFood / fYr20;
                            else if (totalFood <= 0)
                                storeYears = 0;
                            else
                            {
                                double logSY = Math.Log(totalFood) - Math.Log(pop) - Math.Log(foodPerPersonYear) - Math.Log(20.0);
                                double sy = logSY >= 709.782712893384 ? double.MaxValue : Math.Exp(logSY);
                                storeYears = double.IsInfinity(sy) ? double.MaxValue : sy;
                            }
                            double mPot = hasPottery ? 1.25 : 1.00;
                            double uStore = ClampFinite(storeYears * Numbers.Clamp(c.Population / 1200.0, 0, 1), 0, 1);
                            double tradeVol = NonNegativeFinite(c.RecentTradeVolume);
                            double uTrade = tradeVol / (tradeVol + 0.15 * Math.Max(100, c.Population));
                            double dx = dt * Math.Min(1.0, stewards / 35.0) * mPot * (0.65 * uStore + 0.55 * uTrade);
                            if (dx > 0) AddExperience(soc, CivicPrecedents.TokensAndAccounting, dx);
                        }
                    }

                    // Tier 3B: PlannedQuartersAndDrainage (Phase 2)
                    if (sanitationOn && hasT2 && !hasT3B && soc.HasTech(NeolithicTech.Construction))
                    {
                        double denom20 = denomDt * 20.0;
                        double bwy20 = double.IsInfinity(denom20) ? (bwy / denomDt) / 20.0 : bwy / denom20;
                        double dx = dt * Numbers.Clamp((c.Population - 1200.0) / 1800.0, 0, 1)
                            * (0.55 * Math.Min(1.0, stewards / 35.0)
                             + 0.45 * Math.Min(1.0, bwy20));
                        if (dx > 0) AddExperience(soc, CivicPrecedents.PlannedQuartersAndDrainage, dx);
                    }
                }

                // Canonical society-ID x tier order (1, 2, 3A, 3B) precedent unlock commit.
                foreach (var soc in w.Societies.OrderBy(soc => soc.Id))
                {
                    if (soc.Progression == null || soc.Progression.Era != Era.Neolithic) continue;
                    if (soc.CivicExperience == null || soc.CivicExperience.Count == 0) continue;
                    if (soc.CivicPrecedents == null) soc.CivicPrecedents = new List<string>();

                    if (!soc.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration) &&
                        soc.CivicExperience.TryGetValue(CivicPrecedents.CeremonialIntegration, out double x1) &&
                        x1 >= s.Tier1PracticeThreshold)
                    {
                        soc.CivicPrecedents.Add(CivicPrecedents.CeremonialIntegration);
                        record?.Log("Society " + soc.Id + " established civic precedent " + CivicPrecedents.CeremonialIntegration + ".");
                    }
                    if (soc.HasCivicPrecedent(CivicPrecedents.CeremonialIntegration) &&
                        !soc.HasCivicPrecedent(CivicPrecedents.CivicOrganization) &&
                        soc.CivicExperience.TryGetValue(CivicPrecedents.CivicOrganization, out double x2) &&
                        x2 >= s.Tier2PracticeThreshold)
                    {
                        soc.CivicPrecedents.Add(CivicPrecedents.CivicOrganization);
                        record?.Log("Society " + soc.Id + " established civic precedent " + CivicPrecedents.CivicOrganization + ".");
                    }
                    if (soc.HasCivicPrecedent(CivicPrecedents.CivicOrganization) &&
                        !soc.HasCivicPrecedent(CivicPrecedents.TokensAndAccounting) &&
                        soc.CivicExperience.TryGetValue(CivicPrecedents.TokensAndAccounting, out double x3A) &&
                        x3A >= s.Tier3APracticeThreshold)
                    {
                        soc.CivicPrecedents.Add(CivicPrecedents.TokensAndAccounting);
                        record?.Log("Society " + soc.Id + " established civic precedent " + CivicPrecedents.TokensAndAccounting + ".");
                    }
                    if (sanitationOn &&
                        soc.HasCivicPrecedent(CivicPrecedents.CivicOrganization) &&
                        soc.HasTech(NeolithicTech.Construction) &&
                        !soc.HasCivicPrecedent(CivicPrecedents.PlannedQuartersAndDrainage) &&
                        soc.CivicExperience.TryGetValue(CivicPrecedents.PlannedQuartersAndDrainage, out double x3B) &&
                        x3B >= s.Tier3BPracticeThreshold)
                    {
                        soc.CivicPrecedents.Add(CivicPrecedents.PlannedQuartersAndDrainage);
                        record?.Log("Society " + soc.Id + " established civic precedent " + CivicPrecedents.PlannedQuartersAndDrainage + ".");
                    }
                }
            }

            // Step 3: Build single-pass transient CivicLocalIndex, evaluate 4 drivers, commit & relax (C, P).
            var localIndex = new CivicLocalIndex(w);
            List<CivicTurnReport> reports = record != null ? new List<CivicTurnReport>() : null;
            foreach (var c in w.Camps.OrderBy(c => c.Id))
            {
                if (c.Population <= 0) continue;
                var env = localIndex.Evaluate(c);
                double cBefore = ClampFinite(c.CivicComplexity, 0, 1);
                double pBefore = ClampFinite(c.CivicStratification, -1, 1);
                c.LastEnvironmentalComplexity = env.EnvironmentalComplexity;
                c.LastEnvironmentalStratification = env.EnvironmentalStratification;
                c.LastCircumscription = env.D2;
                double cMax = MaxComplexity(w, c);
                double cTarget = ClampFinite(env.EnvironmentalComplexity, 0, cMax);
                double pTarget = TargetStratification(cfg, env.EnvironmentalStratification, c.FissionPolicy, c.ImmigrationPolicy, c.CivicFocus);
                c.CivicComplexity = ClampFinite(Relax(cBefore, cTarget, s.ComplexityRelaxationRate, dt), 0, cMax);
                c.CivicStratification = AdvanceStratification(cfg, pBefore, pTarget, dt);
                if (reports != null)
                {
                    reports.Add(new CivicTurnReport
                    {
                        Camp = c.Id,
                        Society = c.SocietyId,
                        ComplexityBefore = cBefore,
                        ComplexityAfter = c.CivicComplexity,
                        StratificationBefore = pBefore,
                        StratificationAfter = c.CivicStratification,
                        EnvironmentalComplexity = env.EnvironmentalComplexity,
                        EnvironmentalStratification = env.EnvironmentalStratification,
                        Circumscription = env.D2,
                        CivicCoverage = CivicCoverage(w, c),
                        SanitationCoverage = SanitationCoverage(w, c)
                    });
                }
            }
            if (record != null) record.CivicReports = reports;
        }

        private static void AddExperience(Society soc, string key, double delta)
        {
            if (soc == null || !(delta > 0) || double.IsNaN(delta)) return;
            if (soc.CivicExperience == null) soc.CivicExperience = new Dictionary<string, double>();
            soc.CivicExperience.TryGetValue(key, out double prior);
            double next = NonNegativeFinite(prior) + (double.IsInfinity(delta) ? double.MaxValue : delta);
            soc.CivicExperience[key] = double.IsInfinity(next) ? double.MaxValue : next;
        }

        // Validation helpers for SaveCodec.MigrateGenerationalDefaults & TurnResolver.Invariants
        internal static void ValidateLoadedCamp(Camp c)
        {
            if (!IsValidCampCivic(c, out string message)) throw new InvalidDataException(message);
        }

        internal static void ValidateLoadedSociety(Society s)
        {
            if (!IsValidSocietyCivic(s, out string message)) throw new InvalidDataException(message);
        }

        internal static void ValidateInvariants(WorldState w)
        {
            if (w == null) return;
            foreach (var c in w.Camps)
                if (!IsValidCampCivic(c, out string message)) throw new InvalidOperationException(message);
            foreach (var s in w.Societies)
                if (!IsValidSocietyCivic(s, out string message)) throw new InvalidOperationException(message);
        }

        private static bool FiniteInRange(double v, double lo, double hi) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= lo && v <= hi;
        private static bool FiniteNonNegative(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0;

        private static bool IsValidCampCivic(Camp c, out string message)
        {
            if (c == null) { message = "Missing camp."; return false; }
            if (!FiniteInRange(c.CivicComplexity, 0, 1) ||
                !FiniteInRange(c.CivicStratification, -1, 1) ||
                !FiniteNonNegative(c.CivicCapital) ||
                !FiniteNonNegative(c.SanitationCapital) ||
                !FiniteNonNegative(c.RecentTradeVolume) ||
                !FiniteInRange(c.SanitationWaterRatio, 0, 1) ||
                !FiniteInRange(c.LastEnvironmentalComplexity, 0, 1) ||
                !FiniteInRange(c.LastEnvironmentalStratification, -1, 1) ||
                !FiniteInRange(c.LastCircumscription, 0, 1) ||
                !Enum.IsDefined(typeof(ImmigrationPolicy), c.ImmigrationPolicy) ||
                !Enum.IsDefined(typeof(CivicFocus), c.CivicFocus))
            {
                message = "Invalid camp civic governance state.";
                return false;
            }
            message = null;
            return true;
        }

        private static bool IsValidSocietyCivic(Society s, out string message)
        {
            if (s == null) { message = "Missing society."; return false; }
            if (s.CivicExperience != null)
            {
                foreach (var pair in s.CivicExperience)
                    if (string.IsNullOrWhiteSpace(pair.Key) || !FiniteNonNegative(pair.Value))
                    {
                        message = "Invalid society civic experience.";
                        return false;
                    }
            }
            if (s.CivicPrecedents != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string p in s.CivicPrecedents)
                    if (string.IsNullOrWhiteSpace(p) || !seen.Add(p))
                    {
                        message = "Invalid society civic precedents.";
                        return false;
                    }
            }
            message = null;
            return true;
        }

        // Single-pass transient local index for O(active camps) environmental drivers (Docs/CivicGovernance.md §5 Choice #1).
        // Reads ONLY committed static/local state; zero LazyEcology calls, zero hidden terrain tags, player-ID invariant.
        private sealed class CivicLocalIndex
        {
            private readonly WorldState world;
            private readonly SimulationConfig cfg;
            private readonly Dictionary<int, double> canalSegmentsByOp = new Dictionary<int, double>();
            private readonly Dictionary<int, List<IrrigatedField>> irrigatedByOp = new Dictionary<int, List<IrrigatedField>>();
            private readonly Dictionary<int, long> animalsByCamp = new Dictionary<int, long>();
            private readonly Dictionary<int, Camp> campsByTile = new Dictionary<int, Camp>();
            private readonly Dictionary<int, double> tileProductivity = new Dictionary<int, double>();
            private readonly Dictionary<int, double> ringProductivity = new Dictionary<int, double>();
            private readonly Dictionary<int, bool> tileWaterViable = new Dictionary<int, bool>();
            private readonly MaterialAccessIndex matIndex;

            internal CivicLocalIndex(WorldState world)
            {
                this.world = world;
                cfg = world.Config;
                if (world.Canals != null)
                {
                    foreach (var seg in world.Canals)
                    {
                        if (seg == null || seg.Operator < 0) continue;
                        double built = ClampFinite(seg.Construction, 0, 1);
                        if (!(built > 0)) continue;
                        canalSegmentsByOp[seg.Operator] = (canalSegmentsByOp.TryGetValue(seg.Operator, out double prior) ? prior : 0) + built;
                    }
                }
                if (world.IrrigatedFields != null)
                {
                    foreach (var field in world.IrrigatedFields)
                    {
                        if (field == null || field.Operator < 0) continue;
                        if (!irrigatedByOp.TryGetValue(field.Operator, out var list))
                            irrigatedByOp[field.Operator] = list = new List<IrrigatedField>();
                        list.Add(field);
                    }
                }
                if (world.Herds != null)
                {
                    foreach (var herd in world.Herds)
                    {
                        if (herd == null || herd.DomesticCamp < 0 || herd.Animals <= 0) continue;
                        animalsByCamp[herd.DomesticCamp] = (animalsByCamp.TryGetValue(herd.DomesticCamp, out long prior) ? prior : 0L) + herd.Animals;
                    }
                }
                if (world.Camps != null)
                    foreach (var camp in world.Camps)
                        if (camp != null && camp.Population > 0) campsByTile[camp.Tile] = camp;
                matIndex = world.Resources != null ? MaterialAccessIndex.For(world) : null;
            }

            internal CivicEnvironmentDrivers Evaluate(Camp c)
            {
                int[] f1 = world.Map.Radius(c.Tile, 1).ToArray();
                int l1Count = 0, nOwn = 0;
                double sumQ = 0, sumE = 0;
                foreach (int t in f1)
                {
                    var tile = world.Tiles[t];
                    if (!CultivationEconomy.Cultivable(tile)) continue;
                    l1Count++;
                    if (tile.CultivationOperator == c.Id)
                    {
                        if (tile.CultivationCapital > 1e-9) nOwn++;
                        sumQ += CultivationEconomy.Intensity(cfg, NonNegativeFinite(tile.CultivationCapital));
                        sumE = NonNegativeFinite(sumE + NonNegativeFinite(tile.CultivationCapital));
                    }
                }

                irrigatedByOp.TryGetValue(c.Id, out var campIrrigated);
                canalSegmentsByOp.TryGetValue(c.Id, out double canalSegments);
                animalsByCamp.TryGetValue(c.Id, out long animals);

                // Driver 1: Land-Limited vs. Labor-Limited Capital Intensity
                double irrWeightSum = 0;
                if (campIrrigated != null)
                {
                    foreach (var f in campIrrigated)
                    {
                        if (!ContainsTile(f1, f.Tile)) continue;
                        irrWeightSum += 0.5 + 0.5 * ClampFinite(f.WaterService, 0, 1);
                    }
                }
                double sIrr = Numbers.Clamp(irrWeightSum / Math.Max(1, Math.Max(nOwn, 3)), 0, 1);
                double qBar = sumQ / Math.Max(1, l1Count);
                double sQ = Numbers.Clamp((qBar - 0.25) / 0.40, 0, 1);
                double sPlough = AgriculturalTechnology.Plough(world, c) && nOwn > 0
                    ? (c.Inventory != null && c.Inventory.FarmTools > 1e-6 ? 1.0 : 0.5)
                    : 0.0;
                double plant = NonNegativeFinite(c.PlantFood), animal = NonNegativeFinite(c.AnimalFood), aquatic = NonNegativeFinite(c.AquaticFood);
                double fTot = plant + animal + aquatic;
                double plantShare;
                if (double.IsInfinity(fTot))
                {
                    double qTot = 0.25 * plant + 0.25 * animal + 0.25 * aquatic;
                    plantShare = qTot > 0 ? (0.25 * plant) / qTot : 0;
                }
                else
                    plantShare = fTot > 1e-9 ? plant / fTot : 0;
                double fCrop;
                if (nOwn == 0) fCrop = 0;
                else
                {
                    int farmers = c.Workforce != null ? Math.Max(0, CultivationEconomy.Workers(c, WorkforceRoles.Farmer)) : 0;
                    int herders = c.Workforce != null ? Math.Max(0, CultivationEconomy.Workers(c, WorkforceRoles.Herder)) : 0;
                    int fishers = c.Workforce != null ? Math.Max(0, CultivationEconomy.Workers(c, WorkforceRoles.Fisher)) : 0;
                    int gatherers = Math.Max(0, c.GathererPopulation);
                    double wFood = (double)farmers + herders + fishers + gatherers;
                    double fWorker = farmers / Math.Max(1.0, wFood);
                    double fStock = fTot > 1e-9 ? plantShare : fWorker;
                    fCrop = 0.60 * fWorker + 0.40 * fStock;
                }
                double d1 = Numbers.Clamp(fCrop * (0.55 * sIrr + 0.27 * sQ + 0.18 * sPlough), 0, 1);

                // Driver 2: Circumscription & Exit Cost
                int rExit = Math.Max(1, cfg.FissionRadius);
                int dMin = rExit >= cfg.MinimumCampSpacing ? Math.Max(1, cfg.MinimumCampSpacing) : 1;
                long searchRadius = (long)rExit + Math.Max(0L, (long)cfg.MinimumCampSpacing - 1L);
                int maxMapDist = world.Map.Width / 2 + world.Map.Height;
                var nearbyOtherTiles = new List<int>();
                if (searchRadius >= maxMapDist)
                {
                    foreach (var pair in campsByTile)
                        if (pair.Value.Id != c.Id) nearbyOtherTiles.Add(pair.Key);
                }
                else
                {
                    foreach (int t in world.Map.Radius(c.Tile, (int)searchRadius))
                        if (campsByTile.TryGetValue(t, out var other) && other.Id != c.Id)
                            nearbyOtherTiles.Add(t);
                }

                double piHome = 0;
                foreach (int n in f1)
                {
                    double pn = StaticTileProductivity(n);
                    if (cfg.Agriculture != null && campIrrigated != null)
                    {
                        IrrigatedField field = null;
                        for (int i = 0; i < campIrrigated.Count; i++)
                            if (campIrrigated[i].Tile == n) { field = campIrrigated[i]; break; }
                        if (field != null)
                        {
                            double ws = ClampFinite(field.WaterService, 0, 1);
                            if (ws > 0 && pn > 0)
                            {
                                var seasons = world.Tiles[n].Seasons;
                                double mBar = 0.25 * (FiniteSeason(seasons[0].Moisture) + FiniteSeason(seasons[1].Moisture)
                                    + FiniteSeason(seasons[2].Moisture) + FiniteSeason(seasons[3].Moisture));
                                double moistureScale = Math.Max(1.0, NonNegativeFinite(cfg.Agriculture.TargetMoisture) / Math.Max(0.05, mBar));
                                if (double.IsInfinity(moistureScale)) moistureScale = double.MaxValue;
                                double soilTerm = 1.0 + NonNegativeFinite(cfg.Agriculture.SoilCapacityGain) * NonNegativeFinite(field.Soil);
                                double boost = double.IsInfinity(soilTerm) ? double.MaxValue : NonNegativeFinite(moistureScale * soilTerm - 1.0);
                                double mult = 1.0 + ws * boost;
                                pn = double.IsInfinity(mult) ? double.MaxValue : NonNegativeFinite(pn * mult);
                            }
                        }
                    }
                    piHome = NonNegativeFinite(piHome + pn);
                }

                int ringCount = 0;
                double vSum = 0;
                double piHomeDenom = Math.Max(1e-9, piHome);
                foreach (int t in world.Map.Radius(c.Tile, rExit))
                {
                    if (world.Map.Distance(c.Tile, t) < dMin) continue;
                    ringCount++;
                    if (!world.Tiles[t].TraversableLand) continue;
                    bool blocked = false;
                    for (int i = 0; i < nearbyOtherTiles.Count; i++)
                        if (world.Map.Distance(t, nearbyOtherTiles[i]) < cfg.MinimumCampSpacing) { blocked = true; break; }
                    if (blocked || !StaticFreshwaterViable(t)) continue;
                    double piT = RingProductivity(t);
                    vSum += ClampFinite(piT / piHomeDenom, 0, 1);
                }
                double vEff = vSum / Math.Max(1, ringCount);
                double cRing = Math.Exp(-vEff / 0.18);

                double fixedPeople = NonNegativeFinite(NeolithicProduction.FixedHousingAt(world, c, world.Days) * cfg.Neolithic.HousingPeoplePerUnit);
                double lightPeople = NonNegativeFinite((c.Inventory?.LightHousingUnits ?? 0) * cfg.Neolithic.LightHousingPeoplePerUnit);
                double hDenom = fixedPeople + lightPeople + 2.0 * animals + 0.50 * Math.Max(1, c.Population);
                double hFixed;
                if (double.IsInfinity(hDenom))
                {
                    double qDenom = 0.25 * fixedPeople + 0.25 * lightPeople + 0.50 * animals + 0.125 * Math.Max(1, c.Population);
                    hFixed = qDenom > 0 ? ClampFinite((0.25 * fixedPeople) / qDenom, 0, 1) : 0;
                }
                else
                    hFixed = ClampFinite(fixedPeople / Math.Max(1e-9, hDenom), 0, 1);
                double cInfra = Numbers.Clamp((canalSegments + 0.20 * sumE) / 4.0, 0, 1);
                double cSunk = 0.45 * hFixed + 0.55 * cInfra;
                double d2 = Numbers.Clamp(0.72 * cRing + 0.28 * cSunk, 0, 1);

                // Driver 3: Surplus Appropriability & Chokepoints
                double foodPerPersonYear = SafeFoodPerPersonYear(cfg);
                double pop = Math.Max(1, c.Population);
                double fYr = pop * foodPerPersonYear;
                double sPlant = plantShare;
                double grainYears;
                if (!double.IsInfinity(fYr))
                    grainYears = plant / fYr;
                else if (plant <= 0)
                    grainYears = 0;
                else
                {
                    double logGY = Math.Log(plant) - Math.Log(pop) - Math.Log(foodPerPersonYear);
                    double gy = logGY >= 709.782712893384 ? double.MaxValue : Math.Exp(logGY);
                    grainYears = double.IsInfinity(gy) ? double.MaxValue : gy;
                }
                double pots = NonNegativeFinite(c.Inventory?.Pots ?? 0);
                double potStorage = NonNegativeFinite(cfg.Neolithic.PotStorageFoodPerUnit);
                double potFood = pots * potStorage;
                double potYears;
                if (!double.IsInfinity(potFood) && !double.IsInfinity(fYr))
                    potYears = potFood / fYr;
                else if (pots <= 0 || potStorage <= 0)
                    potYears = 0;
                else
                {
                    double logPY = Math.Log(pots) + Math.Log(potStorage) - Math.Log(pop) - Math.Log(foodPerPersonYear);
                    double py = logPY >= 709.782712893384 ? double.MaxValue : Math.Exp(logPY);
                    potYears = double.IsInfinity(py) ? double.MaxValue : py;
                }
                bool hasPottery = c.People != null && c.People.HasTech(NeolithicTech.Pottery);
                double aGrain = ClampFinite(
                    0.75 * ClampFinite((grainYears - 10.0) / 35.0, 0, 1) * (0.35 + 0.65 * sPlant)
                    + 0.25 * ClampFinite(potYears / 15.0 + (hasPottery ? 0.25 : 0.0), 0, 1) * sPlant,
                    0, 1);

                int partners = 0, nNbr = 0;
                int maxNbrDist = Math.Max(cfg.TradeRange, cfg.FissionRadius + 2);
                var contacts = c.People?.Memory?.Contacts;
                if (contacts != null)
                {
                    foreach (var k in contacts)
                    {
                        if (k == null || k.Moved || k.Tile < 0 || k.Tile >= world.Tiles.Length || k.Camp == c.Id || k.Owner == c.Player)
                            continue;
                        int dist = world.Map.Distance(c.Tile, k.Tile);
                        if (dist <= cfg.TradeRange) partners++;
                        if (dist <= maxNbrDist) nNbr++;
                    }
                }
                double tradeVol = NonNegativeFinite(c.RecentTradeVolume);
                double aTrade = Numbers.Clamp(
                    0.70 * (tradeVol / (tradeVol + 0.15 * Math.Max(50, c.Population)))
                    + 0.30 * Math.Min(1.0, partners / 4.0),
                    0, 1);

                double aRes = 0;
                if (world.Resources != null)
                {
                    var res = world.Resources;
                    double scale = Math.Max(1.0, (double)res.MaximumQuantity);
                    double depositScore = 0, sumClay = 0, sumStone = 0;
                    int landCount = 0;
                    foreach (int t in f1)
                    {
                        if (matIndex != null)
                        {
                            foreach (var sr in matIndex.Sites(t))
                            {
                                if (sr.Body == null || sr.Body.Concentration != MineralConcentration.Deposit || sr.Body.Components == null)
                                    continue;
                                bool highValue = false, building = false;
                                for (int i = 0; i < sr.Body.Components.Length; i++)
                                {
                                    var m = sr.Body.Components[i].Material;
                                    if (IsHighValueMaterial(m)) { highValue = true; break; }
                                    if (IsBuildingMaterial(m)) building = true;
                                }
                                if (highValue) depositScore += 0.50 * (sr.Body.Quality / 100.0);
                                else if (building) depositScore += 0.25 * (sr.Body.Quality / 100.0);
                            }
                        }
                        if (world.Tiles[t].Land)
                        {
                            landCount++;
                            if (res.Clay != null && t >= 0 && t < res.Clay.Length)
                                sumClay += res.SurfaceAccessible(t, MaterialId.Clay) / scale;
                            if (res.Stone != null && t >= 0 && t < res.Stone.Length)
                                sumStone += res.SurfaceAccessible(t, MaterialId.Stone) / scale;
                        }
                    }
                    double fClayBar = landCount > 0 ? sumClay / landCount : 0.0;
                    double fStoneBar = landCount > 0 ? sumStone / landCount : 0.0;
                    aRes = Numbers.Clamp(
                        0.60 * Math.Min(1.0, depositScore)
                        + 0.25 * Numbers.Clamp(fClayBar / 0.25, 0, 1)
                        + 0.15 * Numbers.Clamp(fStoneBar / 0.25, 0, 1),
                        0, 1);
                }
                double d3 = Numbers.Clamp(0.55 * aGrain + 0.25 * aTrade + 0.20 * aRes, 0, 1);

                // Driver 4: Collective Coordination & Volatility
                double sumMAmp = 0, sumFAmp = 0;
                int traversableCount = 0;
                foreach (int t in f1)
                {
                    var tile = world.Tiles[t];
                    if (!tile.TraversableLand) continue;
                    traversableCount++;
                    var seasons = tile.Seasons;
                    double m0 = FiniteSeason(seasons[0].Moisture);
                    double minM = m0, maxM = m0;
                    double f0 = FiniteSeason(seasons[0].MaxPotentialFood);
                    double minF = f0, maxF = f0, sumF = f0;
                    for (int s = 1; s < 4; s++)
                    {
                        double m = FiniteSeason(seasons[s].Moisture);
                        if (m < minM) minM = m;
                        if (m > maxM) maxM = m;
                        double f = FiniteSeason(seasons[s].MaxPotentialFood);
                        if (f < minF) minF = f;
                        if (f > maxF) maxF = f;
                        sumF += f;
                    }
                    sumMAmp += maxM - minM;
                    double meanF = 0.25 * sumF;
                    sumFAmp += (maxF - minF) / Math.Max(1e-6, meanF);
                }
                double mAmpBar = traversableCount > 0 ? sumMAmp / traversableCount : 0.0;
                double fAmpBar = traversableCount > 0 ? sumFAmp / traversableCount : 0.0;
                double vSeason = ClampFinite(0.55 * (mAmpBar / 0.40) + 0.45 * (fAmpBar / 1.20), 0, 1);
                double vCanal = ClampFinite(canalSegments / 4.0, 0, 1);
                double vNbr = ClampFinite(nNbr / 5.0, 0, 1);
                double d4 = ClampFinite(0.35 * vSeason + 0.40 * vCanal + 0.25 * vNbr, 0, 1);

                // Combined Attractors (C_env, P_env)
                double sP = 0.36 * d1 + 0.36 * d2 + 0.18 * d3 + 0.10 * d4;
                double pEnv = ClampFinite((sP - 0.42) / 0.42, -1, 1);

                double rhoCrowd = BaseCohesionCrowding(world, c);
                double gN = Numbers.Clamp((c.Population - 150.0) / 350.0, 0, 1);
                double crowdScale = cfg.Civic != null && cfg.Civic.ComplexityCrowdingScale > 0 && !double.IsInfinity(cfg.Civic.ComplexityCrowdingScale)
                    ? cfg.Civic.ComplexityCrowdingScale : 0.64;
                double cRaw = ClampFinite(gN * ((-PhysicalDemography.Expm1(-rhoCrowd / crowdScale)) + 0.20 * d4), 0, 1);
                double cEnv = Math.Min(MaxComplexity(world, c), cRaw);
                return new CivicEnvironmentDrivers(d1, d2, d3, d4, cRaw, cEnv, pEnv);
            }

            private double StaticTileProductivity(int n)
            {
                if (tileProductivity.TryGetValue(n, out double cached)) return cached;
                var tile = world.Tiles[n];
                double sum = 0;
                if (tile.TraversableLand)
                {
                    for (int s = 0; s < 4; s++)
                        sum += NonNegativeFiniteSeason(tile.Seasons[s].MaxFood) + 0.10 * NonNegativeFiniteSeason(tile.Seasons[s].MaxPotentialFood);
                    sum *= 0.25;
                }
                else if (tile.Aquatic && tile.ElevationLevel < 10)
                {
                    for (int s = 0; s < 4; s++)
                        sum += 2.0 * NonNegativeFiniteSeason(tile.Seasons[s].MaxFood);
                    sum *= 0.25;
                }
                tileProductivity[n] = sum;
                return sum;
            }

            private double RingProductivity(int t)
            {
                if (ringProductivity.TryGetValue(t, out double cached)) return cached;
                double sum = 0;
                foreach (int n in world.Map.Radius(t, 1)) sum += StaticTileProductivity(n);
                ringProductivity[t] = sum;
                return sum;
            }

            private bool StaticFreshwaterViable(int t)
            {
                if (tileWaterViable.TryGetValue(t, out bool cached)) return cached;
                double annualWater = 0;
                double w0 = 0, w1 = 0, w2 = 0, w3 = 0;
                foreach (int n in world.Map.Radius(t, 1))
                {
                    var tile = world.Tiles[n];
                    if (tile.Environment == EnvironmentKind.Ocean || tile.ElevationLevel >= 10 ||
                        tile.WaterBody == WaterKind.Saltwater || tile.FreshwaterSource == FreshwaterSource.None)
                        continue;
                    w0 += NonNegativeFiniteSeason(tile.Seasons[0].MaxFreshwater);
                    w1 += NonNegativeFiniteSeason(tile.Seasons[1].MaxFreshwater);
                    w2 += NonNegativeFiniteSeason(tile.Seasons[2].MaxFreshwater);
                    w3 += NonNegativeFiniteSeason(tile.Seasons[3].MaxFreshwater);
                    for (int s = 0; s < 4; s++)
                        annualWater += NonNegativeFiniteSeason(tile.Seasons[s].MaxFreshwater) * NonNegativeFiniteSeason(tile.Seasons[s].WaterRecoveryPerDay) * cfg.BaseDays;
                }
                double minWater = cfg.StartFreshwaterMinimum;
                bool viable = annualWater / 4.0 >= minWater && w0 >= minWater && w1 >= minWater && w2 >= minWater && w3 >= minWater;
                tileWaterViable[t] = viable;
                return viable;
            }

            private static bool ContainsTile(int[] tiles, int tile)
            {
                for (int i = 0; i < tiles.Length; i++) if (tiles[i] == tile) return true;
                return false;
            }

            private static bool IsHighValueMaterial(MaterialId m)
                => m == MaterialId.Copper || m == MaterialId.Tin || m == MaterialId.Gold || m == MaterialId.Lead ||
                   m == MaterialId.Silver || m == MaterialId.Salt || m == MaterialId.Gemstones;

            private static bool IsBuildingMaterial(MaterialId m)
                => m == MaterialId.Limestone || m == MaterialId.Silica || m == MaterialId.Gypsum ||
                   m == MaterialId.Stone || m == MaterialId.Clay;
        }
    }
}
