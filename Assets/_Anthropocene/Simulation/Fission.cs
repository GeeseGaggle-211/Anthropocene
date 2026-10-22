using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace Anthropocene.Simulation
{
    // Per-camp fission policy (QoLFissionTradeDesign 3.6). The enum's zero is Neutral so that an absent saved field means Neutral.
    public enum FissionPolicy { Neutral = 0, Discourage = 1, Encourage = 2 }
    // Experimental selector used only to compare fission pressure laws against the same modern
    // destination search, daughter estimate and settlement constraints. Zero preserves current saves/behavior.
    public enum FissionPressureRule { Current = 0, LegacyQoL = 1, CurrentPlusExitOpportunity = 2 }
    public enum FissionRegime { Paleolithic = 0, Nomadic = 1, Agricultural = 2 }
    public enum KinRelation { Parent, Daughter, Sibling }
    // Append-only: saved reports store numeric enum values.
    public enum FissionTrigger { Crowding = 0, Distress = 1, LegacyQoL = 2, ExitOpportunity = 3 }

    // The cached potential fission destination of one camp. Saved, optional, null when none.
    [Serializable]
    public sealed class FissionTarget
    {
        public int Tile, Regime, Turn;
    }

    // One settlement founded by fission, recorded canonically in parent-camp order.
    [Serializable]
    public sealed class FissionReport
    {
        public int Turn, ParentCamp, DaughterCamp, ParentSociety, DaughterSociety, DaughterPlayer, Tile, ParentBefore, Daughter, Regime, AnimalsMoved;
        public FissionPolicy Policy;
        public double Push, Desirability, Score, Affinity;
        // Expected gain of the stayers' QoL (relative) and the estimated daughter QoL at the decision (WP10c); absent in older saves.
#pragma warning disable SYSLIB0050
        [OptionalField] public double Gain, DaughterQoL;
        // Distress fission: the trigger that fired and the relative QoL gain the leaving group expected at the parent's expectation
        // (Docs/Fission.md, Distress fission); absent in older saves, which read as Crowding and zero.
        [OptionalField] public FissionTrigger Trigger;
        [OptionalField] public double DaughterGain;
        // Bounded realized pressure for an ExitOpportunity trigger. This is the
        // actor-safe quantity; DaughterGain remains a historical diagnostic.
        [OptionalField] public double ExitOpportunityPressure;
        [OptionalField] public double ParentMortalityBefore, ParentMortalityAfter, DaughterMortality, DaughterNetRate;
        [OptionalField] public double DaughterNutrition, DaughterClothingCoverage, DaughterHousingCoverage;
        [OptionalField] public bool DaughterNetNonnegative, HigherQoLWorseMortality;
        [OptionalField] public double MoverUtilityChange, SourceUtilityChange, AggregateWelfareChange;
        [OptionalField] public double ForecastParentQoL, ForecastDaughterQoL;
        [OptionalField] public double ForecastParentNutrition, ForecastParentClothing, ForecastParentHousing;
        [OptionalField] public double ForecastDaughterMortalityRisk, ForecastDaughterStability;
        [OptionalField] public double ForecastDaughterRenewableFoodRatio;
        [OptionalField] public double ForecastDaughterPlantFoodProduction, ForecastDaughterAnimalFoodProduction, ForecastDaughterAquaticFoodProduction;
        [OptionalField] public double ForecastDaughterHerdForageCoverage, ForecastDaughterHerdWaterCoverage;
        [OptionalField] public double ForecastParentExposureRate, ForecastParentCrowdingRate, ForecastParentDiseaseRate, ForecastParentStarvationRate;
        [OptionalField] public double ForecastDaughterExposureRate, ForecastDaughterCrowdingRate, ForecastDaughterDiseaseRate, ForecastDaughterStarvationRate;
        [OptionalField] public int ForecastConfidence, MatchedOpportunities;
#pragma warning restore SYSLIB0050
    }

    // Kin of the observing Society, derived from lineage. Identity and affinity only: no location, stores or population.
    public sealed class KinObservation
    {
        public readonly int Society, Player;
        public readonly KinRelation Relation;
        public readonly double Affinity;
        internal KinObservation(int society, int player, KinRelation relation, double affinity) { Society = society; Player = player; Relation = relation; Affinity = affinity; }
    }

    // Policy levers, affinity and kin derivation.
    public static class Kinship
    {
        // Expected-gain threshold G*(policy): the relative rise of the stayers' QoL a split must deliver (SimulationConfig.FissionGain*).
        public static double GainThreshold(SimulationConfig config, FissionPolicy policy)
            => policy == FissionPolicy.Discourage ? config.FissionGainDiscourage : policy == FissionPolicy.Encourage ? config.FissionGainEncourage : config.FissionGainThreshold;
        // Percent of the parent's residents (integer arithmetic keeps the fraction exact).
        public static int DaughterPercent(FissionPolicy policy) => policy == FissionPolicy.Discourage ? 15 : policy == FissionPolicy.Encourage ? 40 : 30;
        public static double PolicyAffinity(FissionPolicy policy) => policy == FissionPolicy.Discourage ? .5 : policy == FissionPolicy.Encourage ? 1 : .9;
        public static int DaughterSize(int residents, FissionPolicy policy) => (int)((long)Math.Max(0, residents) * DaughterPercent(policy) / 100);

        // Parent-daughter affinity is the daughter's stored KinAffinity; two daughters of one parent take the lower of
        // their two stored affinities (derived here, never stored). Kinship is first degree only: anything else is zero.
        public static double Affinity(Society a, Society b)
        {
            if (a == null || b == null || a.Id == b.Id) return 0;
            if (a.ParentSociety == b.Id) return a.KinAffinity;
            if (b.ParentSociety == a.Id) return b.KinAffinity;
            if (a.ParentSociety > 0 && a.ParentSociety == b.ParentSociety) return Math.Min(a.KinAffinity, b.KinAffinity);
            return 0;
        }

        internal static KinObservation[] Relations(Society society, PlanningIndex index)
        {
            var result = new List<KinObservation>();
            if (society.ParentSociety > 0 && index.SocietyById(society.ParentSociety) is Society parent)
            {
                result.Add(new KinObservation(parent.Id, parent.Player, KinRelation.Parent, Affinity(society, parent)));
                foreach (var sibling in index.Daughters(parent.Id).Where(s => s.Id != society.Id))
                    result.Add(new KinObservation(sibling.Id, sibling.Player, KinRelation.Sibling, Affinity(society, sibling)));
            }
            foreach (var daughter in index.Daughters(society.Id)) result.Add(new KinObservation(daughter.Id, daughter.Player, KinRelation.Daughter, Affinity(society, daughter)));
            return result.ToArray();
        }
    }

    // Fission pass (QoLFissionTradeDesign 3.1-3.6, 9). Runs once per turn in the Close phase after demography.
    //
    // Authoritative state: Camp.FissionPolicy, Camp.FissionDestination, Society.ParentSociety/KinAffinity, WorldState.PlayerCount.
    // Temporal abstraction: one evaluation per turn boundary from the frozen post-demography snapshot. Physical-v1 worlds use
    // the same SocialRelocationEvaluator as immigration; crowding and distress are classifications of the source state, not
    // separate QoL or mortality laws. The candidate forecast is bounded and never runs the real economy or demographic integrator.
    // Site search remains the existing known radius-limited footprint scan, and reconcile/commit remain canonical and proportional
    // to proposals. Legacy-demography saves retain the prior QoL decision path for save-stable behavior.
    public static class Fission
    {
        public const int PolicySeedStream = 500;
        internal const int SiteShortlistLimit = 4;
        private static readonly FieldInfo[] InventoryFields = typeof(CampInventory).GetFields().Where(f => f.FieldType == typeof(double)).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();

        // Diagnostic push P = clamp(G / G*, 0, 1). The decision itself is G >= G*; a zero threshold is always met.
        public static double Push(double gain, double threshold)
            => threshold <= 0 ? (gain >= 0 ? 1 : 0) : Numbers.Clamp(gain / threshold, 0, 1);

        // Crowding + expected gain (WP10c) and the distress pull. Pure, from the camp's committed terms and the same formulas as
        // NeolithicProduction.ResolveNeeds: the expectation E unchanged; clothing coverage unchanged (Cloth and Animal
        // Fabric split in proportion); social cohesion S at the stayers; housing per head recomputed, the parent keeping all fixed
        // Housing and splitting portable Light Housing in proportion (weighted by FissionHousingRelief, default 1). Both Q_now and Q_after are evaluated at the post-demography
        // snapshot, so the gain isolates the split and carries no births-versus-cohort bias.
        // Stayers (distress fission): the local supply is shared by fewer mouths, so their food and water met rise exactly as in the
        // immigration estimator, F_hat = min(1, F N / N_stay) and W_hat likewise (QoLModel.FoodAt/WaterAt; leavers take their share of
        // stores, so per-head stores are unchanged). This is the land-limited upper bound: it assumes the local flow does not shrink with the
        // headcount. Q_after = F_hat W_hat * coverage(housing after) * S(N_stay), so a starving camp's stayers feel their relief.
        // Daughter (the distress trigger): Q_d = F_d W_d * coverage * S at the daughter's headcount and shelter (DaughterShelter), with F_d and W_d
        // the destination's support (1 before a destination is known). DaughterExpected and DaughterQoL both retain the
        // parent's expectation E: population transfer cannot manufacture welfare through an expectation reset.
        // DaughterGain = Q_d / Q_now - 1, and 1e9 (satisfied) when Q_now is zero and Q_d is positive; DaughterQoL uses
        // F = W = 1 for the veto/founding baseline as before.
        public struct Estimate
        {
            public int Residents, Daughter;
            public double QNow, QAfter, Gain, DaughterQoL, StayFood, DaughterExpected, DaughterGain;
            public bool DaughterAcceptable(double margin) => DaughterQoL >= QNow - margin;
            public bool StayersAcceptable(double margin) => QAfter >= QNow - margin;
        }
        public static Estimate EstimateSplit(WorldState w, Camp camp, FissionPolicy policy)
            => EstimateSplit(w, camp, Kinship.DaughterSize(camp.Population, policy), 1, 1);
        // daughterFood/daughterWater: the destination's support for the distress gain (DaughterExpected). vetoFood: the food factor F_d of the
        // veto estimate DaughterQoL (1 = the daughter is fully fed, as before the relative destination test; Docs/Fission.md).
        public static Estimate EstimateSplit(WorldState w, Camp camp, int daughterSize, double daughterFood, double daughterWater, double vetoFood = 1)
        {
            var cfg = w.Config; var n = cfg.Neolithic; int residents = camp.Population, nd = daughterSize, stay = residents - nd;
            double f = residents > 0 ? nd / (double)residents : 0;
            double floor = n.RudimentaryExpectationFloor;
            double e = ExpectationOneDiagnostic ? 1 : camp.RudimentaryExpectation > 0 ? camp.RudimentaryExpectation : NeolithicProduction.SettledExpectation(n, camp);
            var shelter = NeolithicProduction.HousingCapacity(w, camp, w.Days); double fixedPeople = shelter.Fixed, portable = shelter.Light;
            double Housing(double people, double capacity) => people > 0 ? Math.Min(1, capacity / people) : 1;
            double Share(double people, double fixedCapacity, double portableCapacity) => NeolithicProduction.HousingShare(n, new NeolithicProduction.ShelterCapacity(fixedCapacity, portableCapacity), people);
            double Coverage(double expectation, double housing, double cloth, int people, double automatic, double physicalCloth, double physicalHousing)
            {
                double clothingQuality = 1 - expectation * (1 - cloth), housingQuality = 1 - expectation * (1 - housing);
                if (n.SubsistenceNeedsCovered)
                {
                    double eligible = automatic / SubsistenceNeeds.GearBaseline;
                    clothingQuality = SubsistenceNeeds.GearQuality(people, eligible, physicalCloth, cloth, expectation);
                    housingQuality = SubsistenceNeeds.GearQuality(people, eligible, physicalHousing, housing, expectation);
                }
                return n.RudimentaryWeightClothing * clothingQuality + n.RudimentaryWeightHousing * housingQuality;
            }
            double Ratio(double a, double b) => b > 0 ? a / b : (a > 0 ? 1e9 : 0);
            double b0 = camp.FoodNeedsMetRatio * camp.WaterNeedsMetRatio;
            double stayFood = QoLModel.FoodAt(camp.FoodNeedsMetRatio, residents, 0, stay) * QoLModel.WaterAt(camp.WaterNeedsMetRatio, residents, stay);
            // The parent keeps all fixed housing and splits portable housing in proportion; FissionHousingRelief weights how much of the
            // stayers' relief counts (1 = all, 0 = none) for both housing coverage and the cohesion housing share H. At relief 0 the
            // stayers' H is exactly the present H, so only the headcount changes S (Docs/SocialCohesion.md).
            double relief = cfg.FissionHousingRelief;
            double housingNow = Housing(residents, fixedPeople + portable), housingRelieved = Housing(stay, fixedPeople + portable * (1 - f));
            double clothNow = camp.ClothPerCapita, clothAfter = camp.ClothPerCapita, clothD = camp.ClothPerCapita;
            double automaticNow = 0, automaticAfter = 0, automaticD = 0, physicalClothing = 0;
            if (n.SubsistenceNeedsCovered)
            {
                automaticNow = SubsistenceNeeds.ProjectCovered(w, camp, residents);
                automaticAfter = SubsistenceNeeds.ProjectCovered(w, camp, stay);
                automaticD = SubsistenceNeeds.ProjectCovered(w, camp, nd, true);
                double physical = SubsistenceNeeds.PhysicalClothingFraction(camp);
                physicalClothing = physical;
                clothNow = SubsistenceNeeds.Coverage(residents, automaticNow, physical * residents);
                clothAfter = SubsistenceNeeds.Coverage(stay, automaticAfter, physical * stay);
                clothD = SubsistenceNeeds.Coverage(nd, automaticD, physical * nd);
                housingNow = SubsistenceNeeds.Coverage(residents, automaticNow, fixedPeople + portable);
                housingRelieved = SubsistenceNeeds.Coverage(stay, automaticAfter, fixedPeople + portable * (1 - f));
            }
            double housingAfter = housingNow + relief * (housingRelieved - housingNow);
            double shareNow = Share(residents, fixedPeople, portable), shareRelieved = Share(stay, fixedPeople, portable * (1 - f));
            double shareAfter = shareNow + relief * (shareRelieved - shareNow);
            double sNow = NeolithicProduction.SocialCohesion(n, shareNow, residents), sAfter = NeolithicProduction.SocialCohesion(n, shareAfter, stay);
            double qNow = b0 * Coverage(e, housingNow, clothNow, residents, automaticNow, physicalClothing * residents, fixedPeople + portable) * sNow;
            double qAfter = stayFood * Coverage(e, housingAfter, clothAfter, stay, automaticAfter, physicalClothing * stay, fixedPeople + portable * (1 - f)) * sAfter;
            // Q_now = 0 (no food or no coverage at all) leaves a relative gain undefined: the fallback is the cohesion gain S_after / S_now - 1,
            // the QoL a unit-coverage camp would gain, so a destitute crowded camp still feels its crowding.
            double gain = qNow > 1e-9 ? qAfter / qNow - 1 : Ratio(sAfter, sNow) - 1;
            // Physical demography closes the former transfer reset: the daughter's estimate and saved state retain the
            // parent's current expectation. Legacy saves preserve their historical one-turn post-move expectation here.
            double daughterExpectation = PhysicalDemography.Enabled(cfg)
                ? e
                : floor + (1 - floor) * Numbers.Clamp((cfg.TurnDays / cfg.DaysPerYear) / n.RudimentaryExpectationRampYears, 0, 1);
            var own = DaughterShelter(w, camp, nd);
            double housingD = Housing(nd, own.Total), cohesionD = NeolithicProduction.SocialCohesion(n, Share(nd, own.Fixed, own.Light), nd);
            if (n.SubsistenceNeedsCovered) housingD = SubsistenceNeeds.Coverage(nd, automaticD, own.Total);
            double qd = Numbers.Clamp(vetoFood, 0, 1) * Coverage(daughterExpectation, housingD, clothD, nd, automaticD, physicalClothing * nd, own.Total) * cohesionD;
            double qdExpected = Numbers.Clamp(daughterFood, 0, 1) * Numbers.Clamp(daughterWater, 0, 1) * Coverage(e, housingD, clothD, nd, automaticD, physicalClothing * nd, own.Total) * cohesionD;
            double daughterGain = qNow > 1e-9 ? qdExpected / qNow - 1 : (qdExpected > 1e-9 ? 1e9 : -1);
            return new Estimate { Residents = residents, Daughter = nd, QNow = qNow, QAfter = qAfter, Gain = gain, DaughterQoL = qd, StayFood = stayFood, DaughterExpected = qdExpected, DaughterGain = daughterGain };
        }
        // Shelter the daughter has at the end of her first turn (B1, Docs/Fission.md). Pure, no planner run, O(1) closed forms.
        //   Carried: the commit splits every double inventory field in the daughter's people fraction f, so she holds f of the parent's
        //   Light Housing units (LightHousingPeoplePerUnit people each), Housing Material, Wood, Clay, Animal Fabric and tools; the parent
        //   keeps all fixed Housing, so she carries none.
        //   Built: FissionDaughterBuilderShare of her people work as Builders (the planner staffs shelter with a handful of Builders
        //   because a Builder-turn makes several units; the share bounds that staffing), at the production rates and tool waterfall the
        //   resolver uses (Specialist when the Society holds Construction, else GeneralistTaskEfficiency; equipment limits and grade
        //   multipliers from ToolCoverageBudget on her carried tools). The parent's retained physical shelter/operating capital chooses
        //   the first route: fixed Housing when fixed settlement capital exists, portable Light Housing when portable/herd capital exists.
        //   Capabilities only make those recipes feasible, and the other route remains a bounded fallback. Fixed Housing consumes one
        //   carried Housing Material or one Wood/Clay unit each (material-making labour included); Light Housing consumes carried Animal
        //   Fabric. Each route builds only up to the people still unsheltered. Nothing is gathered at the site, so a daughter
        //   with no carried materials, tools or Builders keeps exactly her tents. Ignores decay of carried tents within the turn.
        // Returns people sheltered: Fixed (assembled Housing) and Light (carried plus sewn tents) in people, for HousingShare/Coverage.
        public static NeolithicProduction.ShelterCapacity DaughterShelter(WorldState w, Camp parent, int daughter)
        {
            var cfg = w.Config; var n = cfg.Neolithic; int residents = parent.Population;
            double f = residents > 0 ? daughter / (double)residents : 0, intervals = cfg.GenerationIntervals;
            var inv = parent.Inventory;
            double carriedTents = Math.Max(0, inv?.LightHousingUnits ?? 0) * f, lightPeople = carriedTents * n.LightHousingPeoplePerUnit, fixedPeople = 0;
            double need = daughter - lightPeople;
            double builders = daughter * cfg.FissionDaughterBuilderShare, labor = builders * intervals;
            if (inv == null || need <= 1e-9 || !(labor > 0) || !CultivationEconomy.EconomyEnabled(w, parent)) return new NeolithicProduction.ShelterCapacity(fixedPeople, lightPeople);
            bool specialist = parent.People.HasTech(NeolithicTech.Construction);
            double Rate(double baseRate) => specialist
                ? SpecialistProductivity.SpecialistRate(n, baseRate, daughter)
                : baseRate * n.GeneralistTaskEfficiency;
            double copper = parent.People.HasTech(NeolithicTech.CopperWorking) ? Math.Max(0, inv.CopperTools) * f * intervals / n.ToolCoveragePerWorker : 0;
            var tools = new ToolCoverageBudget(n, Math.Max(0, inv.HandTools) * f, Math.Max(0, inv.Tools) * f * ToolCoverageBudget.OutputsPerTool, copper);
            bool fixedFeasible = CultivationEconomy.Enabled(parent) ||
                NeolithicCapabilities.FeatureFishingEnabled(w, parent);
            // Plant-cloth tents (Docs/PlantTents.md): a farming or fishing parent with Weaving and Construction sews tents from
            // carried Cloth beyond the daughter's first-turn clothing.
            bool plantTents = NeolithicProduction.PlantTentRoute(w, parent);
            bool lightFeasible = CultivationEconomy.HusbandryEnabled(parent) || plantTents;
            bool fixedCapital = parent.IsVillage ||
                NeolithicProduction.FixedHousingAt(w, parent, w.Days) > 1e-9 ||
                (parent.Workforce?.Assignments?.Any(a =>
                    a.Role == WorkforceRoles.Farmer && a.Population > 0) ?? false);
            bool portableCapital = Math.Max(0, inv.LightHousingUnits) > 1e-9 ||
                (parent.Workforce?.Assignments?.Any(a =>
                    a.Role == WorkforceRoles.Herder && a.Population > 0) ?? false) ||
                w.Herds.Any(h => h.DomesticCamp == parent.Id && h.Animals > 0);

            void BuildFixed()
            {
                if (!fixedFeasible || need <= 1e-9 || !(labor > 0)) return;
                // Housing Material and its assembly share the Builder pool and the same step rate (2x the single-step rate), as in the resolver.
                double step = 2 * Rate(n.BuilderHousingOutputPerBaseDays), stock = Math.Max(0, inv.HousingMaterial) * f;
                double raw = (Math.Max(0, inv.Wood) + Math.Max(0, inv.Clay)) * f / Math.Max(1e-12, n.HousingMaterialUnitsPerHousing);
                double hi = Math.Min(need / n.HousingPeoplePerUnit, stock + raw), ceiling = tools.Capacity(CraftGood.Housing, step, double.PositiveInfinity);
                hi = Math.Min(hi, ceiling);
                double Labour(double a) => tools.Time(CraftGood.Housing, step, a) + tools.Time(CraftGood.Housing, step, Math.Max(0, a - stock));
                double built = 0;
                if (hi > 0)
                {
                    if (Labour(hi) <= labor) built = hi;
                    else { double lo = 0, top = hi; for (int i = 0; i < 48; i++) { double mid = (lo + top) / 2; if (Labour(mid) <= labor) lo = mid; else top = mid; } built = lo; }
                }
                if (built > 0)
                {
                    labor -= Labour(built); tools.Spend(CraftGood.Housing, step, built);
                    fixedPeople = built * n.HousingPeoplePerUnit; need -= fixedPeople;
                }
            }

            void BuildLight()
            {
                if (!lightFeasible || need <= 1e-9 || !(labor > 0)) return;
                double lightRate = Rate(n.LightHousingBuilderOutputPerBaseDays);
                double fibre = plantTents
                    ? Math.Max(0, Math.Max(0, inv.Cloth) * f - daughter * n.FabricConsumptionPerPersonTurn * (cfg.TurnDays / cfg.DaysPerYear) / n.ReferenceTurnYears) / n.ClothPerLightHousing
                    : Math.Max(0, inv.AnimalFabric) * f / n.AnimalFabricPerLightHousing;
                double sewn = Math.Min(need / n.LightHousingPeoplePerUnit, fibre);
                sewn = Math.Min(sewn, tools.Capacity(CraftGood.LightHousing, lightRate, labor));
                if (sewn > 0)
                {
                    labor -= tools.Time(CraftGood.LightHousing, lightRate, sewn);
                    tools.Spend(CraftGood.LightHousing, lightRate, sewn);
                    double sheltered = sewn * n.LightHousingPeoplePerUnit;
                    lightPeople += sheltered; need -= sheltered;
                }
            }

            // In the absence of either retained form, fixed is the neutral
            // route when feasible. That preserves the old single-capability
            // behavior without allowing a newly acquired capability to flip an
            // already portable or fixed operating policy.
            bool preferFixed = fixedCapital || !portableCapital;
            if (preferFixed) { BuildFixed(); BuildLight(); }
            else { BuildLight(); BuildFixed(); }
            return new NeolithicProduction.ShelterCapacity(fixedPeople, lightPeople);
        }

        // The camp's committed QoL terms as a pure function of headcount (WP10b; the same formulas as EstimateSplit and ResolveNeeds):
        // F, W and the expectation E unchanged; clothing coverage and housing per head diluted because incoming people bring neither;
        // social cohesion S evaluated at the new headcount. At(Residents) equals EstimateSplit(...).QNow.
        public readonly struct QoLModel
        {
            private readonly NeolithicConfig n; private readonly WorldState world; private readonly Camp camp; private readonly double b0, expectation; private readonly NeolithicProduction.ShelterCapacity shelter;
            public readonly int Residents;
            internal QoLModel(WorldState w, Camp camp)
            {
                n = w.Config.Neolithic; world = w; this.camp = camp; Residents = camp.Population;
                double floor = n.RudimentaryExpectationFloor;
                expectation = ExpectationOneDiagnostic ? 1 : camp.RudimentaryExpectation > 0 ? camp.RudimentaryExpectation : NeolithicProduction.SettledExpectation(n, camp);
                shelter = NeolithicProduction.HousingCapacity(w, camp, w.Days);
                b0 = camp.FoodNeedsMetRatio * camp.WaterNeedsMetRatio;
            }
            public double At(int people) => people <= 0 ? 0 : b0 * Shape(people);
            // Clothing coverage, housing per head and social cohesion at a headcount (everything in QoL except food and water).
            private double Shape(int people)
            {
                double cloth = Math.Min(1, camp.ClothPerCapita * Residents / people), housing = NeolithicProduction.HousingCoverage(shelter, people);
                double clothQuality = 1 - expectation * (1 - cloth), housingQuality = 1 - expectation * (1 - housing);
                if (n.SubsistenceNeedsCovered)
                {
                    double covered = SubsistenceNeeds.ProjectCovered(world, camp, people);
                    cloth = SubsistenceNeeds.Coverage(people, covered, SubsistenceNeeds.PhysicalClothingFraction(camp) * Residents);
                    housing = SubsistenceNeeds.Coverage(people, covered, shelter.Total);
                    double eligible = covered / SubsistenceNeeds.GearBaseline;
                    clothQuality = SubsistenceNeeds.GearQuality(people, eligible, SubsistenceNeeds.PhysicalClothingFraction(camp) * Residents, cloth, expectation);
                    housingQuality = SubsistenceNeeds.GearQuality(people, eligible, shelter.Total, housing, expectation);
                }
                double coverage = n.RudimentaryWeightClothing * clothQuality + n.RudimentaryWeightHousing * housingQuality;
                // Housing stays with the camp: arrivals dilute H (they have to be housed) and leavers raise it (they leave housing behind).
                return coverage * NeolithicProduction.SocialCohesion(n, NeolithicProduction.HousingShare(n, shelter, people), people);
            }
            // Forward-looking QoL at a headcount (WP10b-2): At() plus the food and water dilution that a change of headcount implies.
            //   F_hat(N') = min(1, (F * N + carried) / N'), W_hat(N') = min(1, W * N / N') when W < 1 and 1 otherwise,
            // N the present residents and `carried` the person-equivalents of the turn's food need that arrivals bring (their per-head share of
            // stored food over the per-head requirement; 0 for a source, whose leavers take their share of stores so per-head stores are unchanged).
            // ForwardAt(N, 0) equals At(N).
            public double ForwardAt(int people, double carried = 0)
            {
                if (people <= 0) return 0;
                return FoodAt(camp.FoodNeedsMetRatio, Residents, carried, people) * WaterAt(camp.WaterNeedsMetRatio, Residents, people) * Shape(people);
            }
            // The shared dilution rule of fission (stayers), immigration (both ends) and the distress estimate: local supply over a new headcount.
            public static double FoodAt(double met, int residents, double carried, int people) => Math.Min(1, (met * residents + Math.Max(0, carried)) / people);
            public static double WaterAt(double met, int residents, int people) => met < 1 ? Math.Min(1, met * residents / people) : 1;
        }
        public static QoLModel QoLModelOf(WorldState w, Camp camp) => new QoLModel(w, camp);

        public static FissionRegime RegimeOf(WorldState world, Camp camp)
        {
            if (camp.People.Progression.Era == Era.Paleolithic)
                return FissionRegime.Paleolithic;
            // This legacy label is a transient site-evaluation shape, not an
            // economic identity. Select it from retained physical field
            // capital; learning Cultivation alone cannot change destination
            // policy, and a husbandry-first settlement with the same fields is
            // evaluated identically.
            bool retainedFields = world.Map.Radius(camp.Tile, 1).Any(id =>
                world.Tiles[id].CultivationOperator == camp.Id &&
                world.Tiles[id].CultivationCapital > 1e-9);
            return retainedFields ? FissionRegime.Agricultural :
                FissionRegime.Nomadic;
        }

        private sealed class Context
        {
            public readonly WorldState World;
            public readonly HashSet<int> CampTiles;
            public readonly SocialRelocationForecastCache Forecasts;
            public readonly IEmploymentOpportunityProvider Opportunities;
            private readonly Dictionary<long, SourceProjection> sourceProjections
                = new Dictionary<long, SourceProjection>();
            private readonly Lazy<PlanningIndex> index;
            public PlanningIndex Index => index.Value;
            public Context(WorldState w, IEmploymentOpportunityProvider opportunities, SocialRelocationForecastCache forecasts)
            {
                World = w; CampTiles = new HashSet<int>(w.Camps.Select(c => c.Tile));
                Opportunities = opportunities ?? NeutralEmploymentOpportunityProvider.Instance;
                Forecasts = forecasts ?? (PhysicalDemography.Enabled(w.Config)
                    ? new SocialRelocationForecastCache(w, w.Camps) : null);
                index = new Lazy<PlanningIndex>(() => new PlanningIndex(w), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
            }
            public SocialServiceForecast Baseline(Camp camp) => Forecasts.Baseline(camp);
            public SourceProjection Source(Camp parent, int daughter)
            {
                long key = ((long)parent.Id << 32) ^ (uint)daughter;
                lock (sourceProjections)
                {
                    if (sourceProjections.TryGetValue(key, out var cached)) return cached;
                    int residents = parent.Population;
                    double fraction = daughter / (double)residents;
                    var workforce = WorkforceTransfer.Split(parent.Workforce, residents, daughter, true);
                    var inventory = SocialRelocationForecastCache.RemainingInventory(parent.Inventory, fraction);
                    var state = SocialRelocationForecastCache.Existing(parent, residents - daughter,
                        parent.PlantFood * (1 - fraction), parent.AnimalFood * (1 - fraction),
                        parent.AquaticFood * (1 - fraction), workforce.Stayers, inventory,
                        densityPopulationOffset: Forecasts.DensityOffset(parent),
                        managedAnimalFraction: 1 - fraction);
                    return sourceProjections[key] = new SourceProjection
                    {
                        Workforce = workforce,
                        Forecast = Forecasts.Forecast(state)
                    };
                }
            }
            // Ground truth: a settling group finds a neighbouring camp before it founds a site next to it. Candidate tiles
            // are still restricted to tiles the Society knows, and the owner's observation carries only the chosen tile.
            public bool Blocked(int tile)
            {
                int reach = World.Config.MinimumCampSpacing - 1;
                foreach (int n in World.Map.Radius(tile, Math.Max(0, reach))) if (CampTiles.Contains(n)) return true;
                return false;
            }
        }

        // Diagnostic only: when non-null, Resolve records one row per camp it considered (Tools/Headless/ClothingProbe.cs).
        // Never read by the simulation; null in ordinary play.
        internal sealed class Diag
        {
            public string Stage = "";
            public bool DistressLegal, MoverScreen, SourceScreen, MoverPass, AggregatePass, SourcePass;
            public double BaselineUtility, BaselineNutrition, Threshold, MoverChange, SourceChange, AggregateChange;
            public double DaughterQoL, DaughterNutrition, DaughterClothing, DaughterHousing, DaughterRisk, DaughterRenewableFood;
            public double SourceNoMoveQoL, SourceNoMoveRisk;
            public int Daughter;
        }
        internal static Dictionary<int, Diag> Diagnostic;
        // Measurement only (Docs/FissionRecalibrationCase.md): when non-null, Resolve re-evaluates the same frozen
        // snapshot with every fission-decision read of the settled expectation forced to 1 (ExpectationOneDiagnostic)
        // and records those rows here. Pure: a fresh context, nothing committed; null in ordinary play.
        internal static Dictionary<int, Diag> CounterfactualDiagnostic;
        internal static volatile bool ExpectationOneDiagnostic;
        private static Diag Describe(Evaluation e)
        {
            var diag = new Diag
            {
                DistressLegal = e.DistressLegal, MoverScreen = e.MoverScreenPassed, SourceScreen = e.SourceScreenPassed,
                BaselineUtility = e.BaselineUtility, BaselineNutrition = e.BaselineNutrition, Threshold = e.Threshold, Daughter = e.Daughter,
                Stage = !e.LegalChecked ? "not-legal-size"
                    : !e.MoverScreenPassed ? "screen-mover-bound" : !e.SourceScreenPassed ? "screen-source-harm"
                    : e.NoCandidates ? "no-known-site" : e.Decision == null ? "no-viable-site" : e.Propose ? "proposed" : "decided"
            };
            if (e.Decision != null)
            {
                var dd = e.Decision;
                diag.MoverPass = dd.MoverPass; diag.AggregatePass = dd.AggregatePass; diag.SourcePass = dd.SourcePass;
                diag.MoverChange = dd.MoverUtilityChange; diag.SourceChange = dd.SourceUtilityChange; diag.AggregateChange = dd.AggregateWelfareChange;
                diag.DaughterQoL = dd.DestinationAfter.QoL; diag.DaughterNutrition = dd.DestinationAfter.Nutrition;
                diag.DaughterClothing = dd.DestinationAfter.ClothingCoverage; diag.DaughterHousing = dd.DestinationAfter.HousingCoverage;
                diag.DaughterRisk = dd.DestinationAfter.MortalityRisk; diag.DaughterRenewableFood = dd.DestinationAfter.RenewableFoodRatio;
                diag.SourceNoMoveQoL = dd.SourceNoMove.QoL; diag.SourceNoMoveRisk = dd.SourceNoMove.MortalityRisk;
                if (!e.Propose) diag.Stage = !dd.MoverPass ? "reject-mover" : !dd.AggregatePass ? "reject-aggregate" : !dd.SourcePass ? "reject-source" : "not-proposed";
            }
            return diag;
        }
        // Diagnostic counterfactual only (default off): a distress camp also offers 3/4, 1/2 and 1/4 of its shortfall cohort.
        internal static volatile bool DistressSizeLadderDiagnostic;
        private static int[] DistressSizes(int n1, int n0, int minimum, int largest, bool distressLegal)
        {
            var sizes = new List<int> { distressLegal ? n1 : n0 };
            if (DistressSizeLadderDiagnostic && distressLegal)
                foreach (double f in new[] { .75, .5, .25 }) sizes.Add((int)Math.Round(n1 * f));
            sizes.Add(n0);
            return sizes.Where(n => n >= minimum && n <= largest).Distinct().ToArray();
        }

        private sealed class Evaluation
        {
            public Camp Camp;
            public double Push, Desirability, Score, Gain, DaughterQoL,
                DaughterGain, ExitOpportunityPressure;
            public FissionTrigger Trigger;
            public int Daughter, Tile = -1;
            public bool ClearTarget, Propose, DaughterTested, DaughterBlocked, PhysicalDaughterNegativeNet, Screened, NoCandidates, NoViable, WithDestination, BelowThreshold, StayersBlocked;
            public FissionTarget NewTarget;
            public double SiteHuman, SiteHerd;
            public double DaughterNutrition, DaughterClothing, DaughterHousing;
            public DemographicRates DaughterRates;
            public SocialRelocationEvaluation Decision;
            public WorkforceTransfer Workforce;
            // Diagnostic only (Fission.Diagnostic): screening detail; no decision reads these.
            public bool LegalChecked, DistressLegal, MoverScreenPassed, SourceScreenPassed;
            public double BaselineUtility, BaselineNutrition, Threshold;
            public int Validations, Searches, Candidates, TileProjections, Invalidations;
            public int SocialEvaluations, MoverRejected, AggregateRejected, SourceRejected;
            public int Shortlists, ShortlistedSites, ShortlistAcceptedSites,
                ShortlistAlternativeSelections, ShortlistNoAccepted;
        }

        private sealed class SiteCandidate
        {
            public int Tile, Distance;
            public double Desirability, Ratio;
            public SocialNewSiteCapacity Capacity;
        }

        private sealed class EvaluatedSite
        {
            public SiteCandidate Site;
            public int Daughter;
            public double Coverage;
            public SocialRelocationEvaluation Decision;
            public WorkforceTransfer Workforce;
        }

        private sealed class SourceProjection
        {
            public WorkforceTransfer Workforce;
            public SocialServiceForecast Forecast;
        }

        // excluded: camps that emitted an immigration intent or received arrivals this turn (WP10b); they emit no fission intent.
        public static void Resolve(WorldState w, TurnRecord record, PhaseExecution execution, ResolutionMetrics metrics,
            HashSet<int> excluded = null, IEmploymentOpportunityProvider opportunities = null)
            => Resolve(w, record, execution, metrics, excluded, opportunities, null);

        internal static void Resolve(WorldState w, TurnRecord record, PhaseExecution execution, ResolutionMetrics metrics,
            HashSet<int> excluded, IEmploymentOpportunityProvider opportunities, SocialRelocationForecastCache forecasts)
        {
            if (!w.Config.FissionEnabled) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            bool legacyDecision = !PhysicalDemography.Enabled(w.Config);
            // Frozen post-demography snapshot: nothing below the parallel map writes to the world. Physical daughters mature
            // through the configured number of complete post-founding turns before they can enter this evaluation map.
            var candidates = w.Camps.Where(c => c.Population > 0 && (excluded == null || !excluded.Contains(c.Id))).OrderBy(c => c.Id).ToArray();
            if (!legacyDecision) metrics.FissionCooldownBlocked += candidates.LongCount(c => w.Turn < c.FissionEligibleTurn);
            var camps = legacyDecision ? candidates : candidates.Where(c => w.Turn >= c.FissionEligibleTurn).ToArray();
            if (Diagnostic != null)
            {
                foreach (var c in w.Camps.Where(c => c.Population > 0 && excluded != null && excluded.Contains(c.Id))) Diagnostic[c.Id] = new Diag { Stage = "excluded-immigration" };
                foreach (var c in candidates.Except(camps)) Diagnostic[c.Id] = new Diag { Stage = "cooldown" };
            }
            var context = new Context(w, opportunities, forecasts);
            var evaluations = (execution ?? new PhaseExecution()).Map(camps,
                c => legacyDecision ? EvaluateLegacy(context, c) : Evaluate(context, c));
            if (CounterfactualDiagnostic != null && !legacyDecision)
            {
                ExpectationOneDiagnostic = true;
                try
                {
                    var counterfactual = new Context(w, opportunities, null);
                    foreach (var c in camps) CounterfactualDiagnostic[c.Id] = Describe(Evaluate(counterfactual, c));
                }
                finally { ExpectationOneDiagnostic = false; }
            }
            var committed = new List<int>(); int spacing = Math.Max(1, w.Config.MinimumCampSpacing);
            Dictionary<int, List<MaterialKnowledge>> knowledge = null; Dictionary<int, DemographicReport> demographics = null;
            // Canonical reconcile and commit in camp ID order. A conflicting proposal is dropped this turn and keeps its cache.
            foreach (var e in evaluations)
            {
                metrics.FissionCampsEvaluated++;
                metrics.FissionValidations += e.Validations; metrics.FissionSearches += e.Searches; metrics.FissionCandidatesScored += e.Candidates;
                metrics.FissionTileProjections += e.TileProjections; metrics.FissionCacheInvalidations += e.Invalidations;
                metrics.FissionShortlists += e.Shortlists;
                metrics.FissionShortlistedSites += e.ShortlistedSites;
                metrics.FissionShortlistAcceptedSites += e.ShortlistAcceptedSites;
                metrics.FissionShortlistAlternativeSelections += e.ShortlistAlternativeSelections;
                metrics.FissionShortlistNoAccepted += e.ShortlistNoAccepted;
                if (e.ClearTarget) e.Camp.FissionDestination = null;
                if (e.NewTarget != null) e.Camp.FissionDestination = e.NewTarget;
                if (e.DaughterTested) { metrics.FissionDaughterChecks++; if (e.DaughterBlocked) metrics.FissionDaughterBlocked++; }
                if (e.PhysicalDaughterNegativeNet) metrics.FissionPhysicalDaughterNegativeNet++;
                if (e.Screened) metrics.FissionScreened++;
                if (e.NoCandidates) metrics.FissionSearchesNoCandidates++;
                if (e.NoViable) metrics.FissionSearchesNoViable++;
                if (e.WithDestination) metrics.FissionWithDestination++;
                if (e.BelowThreshold) metrics.FissionBelowThreshold++;
                if (e.StayersBlocked) metrics.FissionStayersBlocked++;
                if (PhysicalDemography.Enabled(w.Config))
                {
                    metrics.SocialCandidatesEvaluated += e.SocialEvaluations;
                    metrics.SocialMoverRejected += e.MoverRejected;
                    metrics.SocialAggregateRejected += e.AggregateRejected;
                    metrics.SocialSourceRejected += e.SourceRejected;
                }
                Diag diag = null;
                if (Diagnostic != null)
                {
                    diag = Describe(e);
                    Diagnostic[e.Camp.Id] = diag;
                }
                if (!e.Propose) continue;
                if (diag != null) diag.Stage = committed.Any(t => w.Map.Distance(t, e.Tile) < spacing) ? "dropped-spacing" : "committed-" + e.Trigger;
                metrics.FissionProposals++;
                if (e.Trigger == FissionTrigger.Distress) metrics.FissionDistressProposals++;
                else if (e.Trigger == FissionTrigger.Crowding) metrics.FissionCrowdingProposals++;
                if (committed.Any(t => w.Map.Distance(t, e.Tile) < spacing)) { metrics.FissionDropped++; continue; }
                if (knowledge == null)
                {
                    knowledge = new Dictionary<int, List<MaterialKnowledge>>();
                    foreach (var k in w.MaterialKnowledge) { if (!knowledge.TryGetValue(k.Society, out var list)) knowledge[k.Society] = list = new List<MaterialKnowledge>(); list.Add(k); }
                    demographics = new Dictionary<int, DemographicReport>();
                    foreach (var d in record.Demographics) demographics[d.Camp] = d;
                }
                Found(w, e, record, knowledge, demographics);
                committed.Add(e.Tile); metrics.FissionCommits++;
                if (e.Decision != null) metrics.SocialMatchedOpportunities += e.Decision.Opportunity.Matched;
                if (e.Trigger == FissionTrigger.Distress) { metrics.FissionDistressCommits++; metrics.FissionDistressDaughters += e.Daughter; }
                else if (e.Trigger == FissionTrigger.Crowding) metrics.FissionCrowdingCommits++;
            }
            metrics.FissionMilliseconds = timer.Elapsed.TotalMilliseconds;
        }

        // Distress fission: a camp splits when the stayers gain from fewer mouths (crowding, now with food and water relief) OR, for a camp that
        // cannot feed itself (F W < 1), when the leaving group would be better off at a viable destination (the distress pull), both against the
        // same policy threshold G*. A fed camp (F = W = 1) has no distress branch: a daughter's cohesion advantage over the stayers is crowding,
        // already measured by the stayers' branch, and counting it again would move the calibrated crowding onset (Docs/Fission.md).
        // Sizes: the policy size n0 for the crowding test; when the camp cannot feed itself (F W < 1) the leaving group is the people the
        // local subsistence cannot carry, n_short = ceil(N (1 - F W)), n1 = clamp(max(n0, n_short), FissionMinimumDaughter, the largest n with
        // n < N - n and N - n >= MinimumCamp). The destination search and validation use n1 whenever the distress screen passes (it is the
        // larger, harder-to-support group), n0 otherwise. If the distress branch passes the split uses n1, else a passing crowding branch uses n0.
        // The trigger is Distress exactly when the distress branch (n_short > 0) was the one used.
        private static double LegacyQoLShift(FissionPolicy policy)
            => policy == FissionPolicy.Discourage ? -.05 : policy == FissionPolicy.Encourage ? .05 : 0;

        private static double LegacyPush(SimulationConfig cfg, Camp camp, FissionPolicy policy)
            => Numbers.Clamp((cfg.FissionLegacyQoL + LegacyQoLShift(policy) - camp.QoL) /
                Math.Max(1e-12, cfg.FissionLegacyQoLWidth), 0, 1);

        // Generalized escape pressure: bad home conditions matter, but only when the daughter's
        // credible outcome is materially better. This is additive to the current crowding/distress rule.
        internal static double ExitOpportunityPressure(double qHome, double qDaughter)
            => Math.Max(0, 1 - Numbers.Clamp(qHome, 0, 1)) *
               Math.Max(0, Numbers.Clamp(qDaughter, 0, 1) - Numbers.Clamp(qHome, 0, 1));
        private static SocialRelocationEvaluation RelocationAt(Context context, Camp parent, int tile,
            int daughter, SocialNewSiteCapacity siteCapacity, out WorkforceTransfer workforce)
        {
            var w = context.World; var cfg = w.Config; int residents = parent.Population;
            double fraction = daughter / (double)residents;
            var source = context.Source(parent, daughter);
            workforce = source.Workforce;
            var daughterInventory = SocialRelocationForecastCache.ProportionalInventory(parent.Inventory, fraction);
            var daughterState = context.Forecasts.Daughter(parent, daughter, tile, daughterInventory,
                workforce.Movers, parent.PlantFood * fraction, parent.AnimalFood * fraction,
                parent.AquaticFood * fraction, siteCapacity);
            var request = new EmploymentOpportunityRequest(parent.Id, daughterState.View.Id, daughter,
                SocialRelocationDestination.NewDaughter, workforce.MoverComposition);
            var opportunity = EmploymentOpportunityMatcher.Match(context.Opportunities, request);
            var daughterAfter = context.Forecasts.Forecast(daughterState, opportunity);
            var policy = new SocialRelocationPolicy(Kinship.GainThreshold(cfg, parent.FissionPolicy), 0,
                cfg.FissionDaughterMargin, cfg.FissionDaughterMargin);
            return SocialRelocationEvaluator.Evaluate(SocialRelocationDestination.NewDaughter,
                context.Baseline(parent), source.Forecast,
                SocialServiceForecast.Empty(daughterState.View.Id, cfg.TurnDays / cfg.DaysPerYear),
                daughterAfter, policy, opportunity);
        }

        // Save-stable pre-v1.1 social decision.  It is retained only for worlds
        // whose serialized demographic selector is LegacyQoLNetGrowth.  The
        // physical model below is the implementation of the v0.2 contract.
        private static Evaluation EvaluateLegacy(Context context, Camp camp)
        {
            // Elder-line pressure rules apply only to the save-stable legacy demography.
            if (context.World.Config.FissionPressureRule == FissionPressureRule.LegacyQoL)
                return EvaluateLegacyQoL(context, camp);
            var w = context.World; var cfg = w.Config; var e = new Evaluation { Camp = camp };
            var regime = RegimeOf(w, camp); var policy = camp.FissionPolicy; var cached = camp.FissionDestination;
            if (cached != null && cached.Regime != (int)regime) { e.ClearTarget = true; e.Invalidations++; cached = null; }
            int residents = camp.Population, n0 = Kinship.DaughterSize(residents, policy), minimum = cfg.FissionMinimumDaughter;
            int largest = Math.Min((residents - 1) / 2, residents - cfg.MinimumCamp);
            bool crowdLegal = n0 >= minimum && n0 <= largest;
            double met = camp.FoodNeedsMetRatio * camp.WaterNeedsMetRatio;
            int short0 = met < 1 - 1e-9 ? (int)Math.Ceiling(residents * (1 - met) - 1e-9) : 0;
            bool distressLegal = short0 > 0 && largest >= minimum;
            int n1 = distressLegal ? Math.Max(minimum, Math.Min(largest, Math.Max(n0, short0))) : n0;
            e.Daughter = crowdLegal ? n0 : n1;
            if (!crowdLegal && !distressLegal) return e;
            double threshold = Kinship.GainThreshold(cfg, policy), screen = cfg.FissionSearchFraction * threshold;
            Estimate crowd = crowdLegal ? EstimateSplit(w, camp, n0, 1, 1) : default;
            Estimate optimistic = !distressLegal ? default : n1 == n0 && crowdLegal ? crowd : EstimateSplit(w, camp, n1, 1, 1);
            bool crowdScreen = crowdLegal && crowd.Gain >= screen, distressScreen = distressLegal && optimistic.DaughterGain >= screen;
            // Elder-line exit-opportunity pressure (FissionPressureRule): additive to the crowding/distress rule.
            bool exitEnabled = cfg.FissionPressureRule == FissionPressureRule.CurrentPlusExitOpportunity;
            double qHomeForExit = crowdLegal ? crowd.QNow : optimistic.QNow;
            double exitScreenValue = exitEnabled && crowdLegal ? ExitOpportunityPressure(qHomeForExit, 1) : 0;
            bool exitScreen = exitEnabled && crowdLegal &&
                exitScreenValue >= cfg.FissionExitSearchFraction * cfg.FissionExitThreshold;
            var shown = crowdLegal ? crowd : optimistic;
            e.Gain = shown.Gain; e.DaughterQoL = shown.DaughterQoL; e.DaughterGain = shown.DaughterGain;
            e.Push = exitScreen && !crowdScreen && !distressScreen
                ? Push(exitScreenValue, cfg.FissionExitThreshold)
                : Push(crowdLegal ? crowd.Gain : optimistic.DaughterGain, threshold);
            if (!crowdScreen && !distressScreen && !exitScreen) return e;
            e.Screened = true;
            int size = distressScreen ? n1 : n0;
            var projections = new Dictionary<int, double>(); double siteRatio = 0, siteHuman = 0, siteHerd = double.PositiveInfinity;
            if (cached != null)
            {
                e.Validations++;
                double d = 0, ratio = 0; siteHuman = 0; siteHerd = double.PositiveInfinity;
                if (!context.Blocked(cached.Tile) && w.Tiles[cached.Tile].TraversableLand)
                { d = Desirability(context, camp, cached.Tile, size, regime, projections, e, out ratio, out _); siteHuman = e.SiteHuman; siteHerd = e.SiteHerd; }
                if (!(ratio > 0)) { e.ClearTarget = true; e.Invalidations++; cached = null; }
                else { e.Desirability = d; siteRatio = ratio; }
            }
            if (cached == null)
            {
                e.Searches++;
                var known = new Known(context, camp);
                int best = -1, bestDistance = 0; double bestScore = -1, bestRatio = -1, bestHuman = 0, bestHerd = double.PositiveInfinity;
                foreach (int tile in w.Map.Radius(camp.Tile, cfg.FissionRadius).OrderBy(n => n))
                {
                    if (!w.Tiles[tile].TraversableLand || !known.Contains(tile) || context.Blocked(tile)) continue;
                    e.Candidates++;
                    double d = Desirability(context, camp, tile, size, regime, projections, e, out double ratio, out _);
                    int distance = w.Map.Distance(camp.Tile, tile);
                    if (best < 0 || d > bestScore || d == bestScore && (ratio > bestRatio || ratio == bestRatio && distance < bestDistance))
                    { best = tile; bestScore = d; bestRatio = ratio; bestDistance = distance; bestHuman = e.SiteHuman; bestHerd = e.SiteHerd; }
                }
                if (best >= 0 && bestScore > 0 && bestRatio > 0)
                {
                    e.NewTarget = new FissionTarget { Tile = best, Regime = (int)regime, Turn = w.Turn };
                    e.ClearTarget = false; e.Desirability = bestScore; siteRatio = bestRatio;
                    siteHuman = bestHuman; siteHerd = bestHerd; cached = e.NewTarget;
                }
            }
            if (cached == null) { if (e.Candidates == 0) e.NoCandidates = true; else e.NoViable = true; return e; }
            e.WithDestination = true; e.Tile = cached.Tile;
            double foodD = Numbers.Clamp(siteRatio, 0, 1);
            double vetoFood = Math.Min(1, Math.Min(siteHerd,
                Math.Max(0, siteHuman) + Immigration.CarriedPerPerson(cfg, camp)));
            Estimate chosen = default; bool use = false;
            if (distressLegal)
            {
                var distress = EstimateSplit(w, camp, n1, foodD, 1, vetoFood);
                if (distress.DaughterGain >= threshold) { chosen = distress; use = true; e.Trigger = FissionTrigger.Distress; }
            }
            if (!use && crowdLegal && crowd.Gain >= threshold)
            { chosen = EstimateSplit(w, camp, n0, foodD, 1, vetoFood); use = true; e.Trigger = FissionTrigger.Crowding; }
            if (!use && exitEnabled && crowdLegal)
            {
                var exit = EstimateSplit(w, camp, n0, foodD, 1, vetoFood);
                // Compared at the parent's expectation on both sides; the daughter's expectation reset stays in the veto.
                double exitPressure = ExitOpportunityPressure(exit.QNow, exit.DaughterExpected);
                if (exitPressure >= cfg.FissionExitThreshold)
                { chosen = exit; use = true; e.Trigger = FissionTrigger.ExitOpportunity; }
            }
            if (!use) { e.BelowThreshold = true; return e; }
            e.Daughter = chosen.Daughter; e.Gain = chosen.Gain; e.DaughterQoL = chosen.DaughterQoL;
            e.DaughterGain = chosen.DaughterGain;
            e.ExitOpportunityPressure = ExitOpportunityPressure(chosen.QNow, chosen.DaughterExpected);
            double selectedPressure = e.Trigger == FissionTrigger.Distress ? chosen.DaughterGain
                : e.Trigger == FissionTrigger.ExitOpportunity ? e.ExitOpportunityPressure : chosen.Gain;
            e.Push = Push(selectedPressure, e.Trigger == FissionTrigger.ExitOpportunity ? cfg.FissionExitThreshold : threshold);
            e.Score = e.Push * e.Desirability;
            e.DaughterTested = true; e.DaughterBlocked = !chosen.DaughterAcceptable(cfg.FissionDaughterMargin);
            bool stayersAcceptable = chosen.StayersAcceptable(cfg.FissionDaughterMargin);
            e.StayersBlocked = !e.DaughterBlocked && !stayersAcceptable;
            e.Propose = !e.DaughterBlocked && stayersAcceptable;
            return e;
        }

        // Healthy/crowding and distress fission share the same physical
        // counterfactual.  The trigger records the parent's forecast circumstance;
        // it does not select another utility, QoL or mortality equation.  The
        // current policy daughter and distress shortfall sizes remain unchanged.
        private static Evaluation Evaluate(Context context, Camp camp)
        {
            var w = context.World; var cfg = w.Config;
            var e = new Evaluation { Camp = camp };
            var regime = RegimeOf(w, camp); var policy = camp.FissionPolicy; var cached = camp.FissionDestination;
            // A change of subsistence regime invalidates the cached destination regardless of the push.
            if (cached != null && cached.Regime != (int)regime) { e.ClearTarget = true; e.Invalidations++; cached = null; }
            int residents = camp.Population, n0 = Kinship.DaughterSize(residents, policy), minimum = cfg.FissionMinimumDaughter;
            int largest = Math.Min((residents - 1) / 2, residents - cfg.MinimumCamp);
            // The daughter is a smaller group than the parent that remains and meets the minimum viable population.
            bool crowdLegal = n0 >= minimum && n0 <= largest;
            if (!crowdLegal && largest < minimum) return e;
            var baseline = context.Baseline(camp);
            double met = baseline.Nutrition * baseline.WaterCoverage;
            int short0 = met < 1 - 1e-9 ? (int)Math.Ceiling(residents * (1 - met) - 1e-9) : 0;
            bool distressLegal = short0 > 0 && largest >= minimum;
            int n1 = distressLegal ? Math.Max(minimum, Math.Min(largest, Math.Max(n0, short0))) : n0;
            e.Daughter = crowdLegal ? n0 : n1;
            e.DistressLegal = distressLegal; e.BaselineNutrition = met;
            if (!crowdLegal && !distressLegal) return e;
            e.LegalChecked = true;

            // Provably permissive preselection against the shared four gates.
            // Q is bounded by one and physical mortality risk is nonnegative, so
            // a fully served, risk-free daughter is an upper bound on mover
            // utility. The source branch is the exact common forecast. We skip a
            // site search only when every supported daughter size must therefore
            // fail either the mover gate or the source-harm gate. Last-turn
            // realized F/C/H/W are not inputs. FissionSearchFraction only lowers
            // this valid upper-bound threshold to preserve early cache searches;
            // it cannot screen a candidate that could meet the final mover gate.
            double threshold = Kinship.GainThreshold(cfg, policy);
            double searchThreshold = cfg.FissionSearchFraction * threshold;
            double baselineUtility = baseline.QoL - baseline.MortalityRisk;
            var candidateSizes = DistressSizes(n1, n0, minimum, largest, distressLegal);
            double bestSourceRelief = double.NegativeInfinity;
            bool candidateCanPass = false;
            double years = cfg.TurnDays / cfg.DaysPerYear;
            foreach (int candidateSize in candidateSizes)
            {
                // Full F/C/H/W and zero linked-settlement density are the best
                // physically possible new-site services at this headcount.
                double daughterQoLUpper = NeolithicProduction.SocialCohesion(cfg.Neolithic, 1, candidateSize);
                var daughterRatesLower = PhysicalDemography.Rates(cfg, candidateSize, 1, 1,
                    candidateSize, false);
                double daughterRiskLower = 1 - Math.Exp(-Math.Max(0, daughterRatesLower.Death) * years);
                bool moverCanRequestSearch = daughterQoLUpper - daughterRiskLower - baselineUtility
                    + 1e-12 >= searchThreshold;
                if (!moverCanRequestSearch) continue;
                e.MoverScreenPassed = true;
                var source = context.Source(camp, candidateSize).Forecast;
                double relief = (source.QoL - source.MortalityRisk) - baselineUtility;
                bestSourceRelief = Math.Max(bestSourceRelief, relief);
                bool sourceCanPass = relief + cfg.FissionDaughterMargin + 1e-12 >= 0;
                if (sourceCanPass) { candidateCanPass = true; e.SourceScreenPassed = true; }
            }
            e.BaselineUtility = baselineUtility; e.Threshold = threshold;
            e.Gain = double.IsNegativeInfinity(bestSourceRelief) ? 0 : bestSourceRelief;
            e.Push = Push(e.Gain, threshold);
            if (!candidateCanPass) return e;
            e.Screened = true;
            int size = distressLegal ? n1 : n0;
            var projections = new Dictionary<int, double>(); double siteRatio = 0, siteHuman = 0, siteHerd = double.PositiveInfinity;
            SocialNewSiteCapacity siteCapacity = default;
            List<SiteCandidate> shortlist = null;
            if (cached != null)
            {
                e.Validations++;
                double d = 0, ratio = 0; siteHuman = 0; siteHerd = double.PositiveInfinity;
                if (!context.Blocked(cached.Tile) && w.Tiles[cached.Tile].TraversableLand) { d = Desirability(context, camp, cached.Tile, size, regime, projections, e, out ratio, out siteCapacity); siteHuman = e.SiteHuman; siteHerd = e.SiteHerd; }
                // Relative destination test: a site stays cached while it is habitable (its freshwater gate passes and it supports some food).
                // Welfare remains the calibrated relative veto below; physical mode later records explicit rates without adding a gate.
                if (!(ratio > 0)) { e.ClearTarget = true; e.Invalidations++; cached = null; }
                else { e.Desirability = d; siteRatio = ratio; }
            }
            if (cached == null)
            {
                e.Searches++;
                var known = new Known(context, camp);
                int best = -1, bestDistance = 0; double bestScore = -1, bestRatio = -1, bestHuman = 0, bestHerd = double.PositiveInfinity;
                bool shortlistMode = cfg.FissionSiteSelection != FissionSiteSelection.EcologicalSupport;
                var ranked = shortlistMode ? new List<SiteCandidate>() : null;
                // Order: D (capped at 1) descending, then the uncapped support ratio descending, then hex distance, then tile ID
                // (candidates arrive in tile order, so a full tie keeps the lowest ID).
                foreach (int tile in w.Map.Radius(camp.Tile, cfg.FissionRadius).OrderBy(n => n))
                {
                    if (!w.Tiles[tile].TraversableLand || !known.Contains(tile) || context.Blocked(tile)) continue;
                    e.Candidates++;
                    double d = Desirability(context, camp, tile, size, regime, projections, e, out double ratio, out var candidateCapacity);
                    int distance = w.Map.Distance(camp.Tile, tile);
                    if (shortlistMode && d > 0 && ratio > 0)
                        ranked.Add(new SiteCandidate { Tile = tile, Distance = distance,
                            Desirability = d, Ratio = ratio, Capacity = candidateCapacity });
                    if (best < 0 || d > bestScore || d == bestScore && (ratio > bestRatio || ratio == bestRatio && distance < bestDistance)) { best = tile; bestScore = d; bestRatio = ratio; bestDistance = distance; bestHuman = e.SiteHuman; bestHerd = e.SiteHerd; siteCapacity = candidateCapacity; }
                }
                if (best >= 0 && bestScore > 0 && bestRatio > 0)
                {
                    e.NewTarget = new FissionTarget { Tile = best, Regime = (int)regime, Turn = w.Turn }; e.ClearTarget = false;
                    e.Desirability = bestScore; siteRatio = bestRatio; siteHuman = bestHuman; siteHerd = bestHerd; cached = e.NewTarget;
                    if (shortlistMode)
                    {
                        shortlist = ranked.OrderByDescending(c => c.Desirability)
                            .ThenByDescending(c => c.Ratio).ThenBy(c => c.Distance).ThenBy(c => c.Tile)
                            .Take(SiteShortlistLimit).ToList();
                        e.Shortlists = 1; e.ShortlistedSites = shortlist.Count;
                    }
                }
            }
            if (cached == null) { if (e.Candidates == 0) e.NoCandidates = true; else e.NoViable = true; return e; }
            e.WithDestination = true;
            e.Tile = cached.Tile;
            SocialRelocationEvaluation chosenDecision = null; WorkforceTransfer chosenWorkforce = null;
            int chosenSize = 0; double chosenCoverage = e.Desirability;
            void CountDecision(SocialRelocationEvaluation decision)
            {
                e.SocialEvaluations++;
                if (!decision.MoverPass) e.MoverRejected++;
                if (!decision.AggregatePass) e.AggregateRejected++;
                if (!decision.SourcePass) e.SourceRejected++;
            }
            // Distress keeps its calibrated shortfall cohort.  If that larger
            // group is not supportable, the ordinary policy cohort may still be
            // assessed by exactly the same equation at the selected known site.
            var prioritizedSizes = DistressSizes(n1, n0, minimum, largest, distressLegal);
            if (shortlist == null)
            {
                foreach (int candidateSize in prioritizedSizes)
                {
                    SocialNewSiteCapacity candidateCapacity;
                    double coverage;
                    if (candidateSize == size)
                    {
                        coverage = e.Desirability; candidateCapacity = siteCapacity;
                    }
                    else
                        coverage = Desirability(context, camp, cached.Tile, candidateSize, regime,
                            projections, e, out _, out candidateCapacity);
                    var decision = RelocationAt(context, camp, cached.Tile, candidateSize,
                        candidateCapacity, out var transfer);
                    CountDecision(decision);
                    if (!decision.Accepted) { if (chosenDecision == null) chosenDecision = decision; continue; }
                    chosenDecision = decision; chosenWorkforce = transfer; chosenSize = candidateSize;
                    chosenCoverage = coverage; break;
                }
            }
            else
            {
                SocialRelocationEvaluation firstDecision = null;
                foreach (int candidateSize in prioritizedSizes)
                {
                    var accepted = new List<EvaluatedSite>();
                    foreach (SiteCandidate candidate in shortlist)
                    {
                        double coverage; SocialNewSiteCapacity candidateCapacity;
                        if (candidateSize == size)
                        {
                            coverage = candidate.Desirability; candidateCapacity = candidate.Capacity;
                        }
                        else
                            coverage = Desirability(context, camp, candidate.Tile, candidateSize,
                                regime, projections, e, out _, out candidateCapacity);
                        var decision = RelocationAt(context, camp, candidate.Tile, candidateSize,
                            candidateCapacity, out var transfer);
                        CountDecision(decision); if (firstDecision == null) firstDecision = decision;
                        if (decision.Accepted)
                        {
                            e.ShortlistAcceptedSites++;
                            accepted.Add(new EvaluatedSite { Site = candidate, Daughter = candidateSize,
                                Coverage = coverage, Decision = decision, Workforce = transfer });
                        }
                    }
                    if (accepted.Count == 0) continue;
                    IOrderedEnumerable<EvaluatedSite> ordered = cfg.FissionSiteSelection
                        == FissionSiteSelection.AcceptedAggregateWelfare
                        ? accepted.OrderByDescending(c => c.Decision.AggregateWelfareChange)
                            .ThenByDescending(c => c.Decision.DestinationAfter.RenewableFoodRatio)
                            .ThenByDescending(c => c.Decision.MoverUtilityChange)
                        : accepted.OrderByDescending(c => c.Decision.DestinationAfter.RenewableFoodRatio)
                            .ThenByDescending(c => c.Decision.MoverUtilityChange)
                            .ThenByDescending(c => c.Decision.AggregateWelfareChange);
                    var selected = ordered.ThenBy(c => c.Site.Distance).ThenBy(c => c.Site.Tile).First();
                    chosenDecision = selected.Decision; chosenWorkforce = selected.Workforce;
                    chosenSize = selected.Daughter; chosenCoverage = selected.Coverage;
                    e.Tile = selected.Site.Tile; siteCapacity = selected.Site.Capacity;
                    if (e.Tile != cached.Tile) e.ShortlistAlternativeSelections++;
                    e.NewTarget = new FissionTarget { Tile = e.Tile, Regime = (int)regime, Turn = w.Turn };
                    break;
                }
                if (chosenDecision == null)
                {
                    chosenDecision = firstDecision; e.ShortlistNoAccepted++;
                }
            }
            e.Decision = chosenDecision; e.DaughterTested = chosenDecision != null;
            if (chosenDecision == null) { e.NoViable = true; return e; }
            e.Daughter = chosenSize > 0 ? chosenSize : size;
            if (chosenSize > 0) e.Desirability = chosenCoverage;
            e.Trigger = distressLegal ? FissionTrigger.Distress : FissionTrigger.Crowding;
            e.Gain = chosenDecision.SourceUtilityChange; e.DaughterGain = chosenDecision.MoverUtilityChange;
            e.DaughterQoL = chosenDecision.DestinationAfter.QoL;
            e.Push = Push(chosenDecision.MoverUtilityChange, threshold); e.Score = e.Push * e.Desirability;
            e.DaughterNutrition = chosenDecision.DestinationAfter.Nutrition;
            e.DaughterClothing = chosenDecision.DestinationAfter.ClothingCoverage;
            e.DaughterHousing = chosenDecision.DestinationAfter.HousingCoverage;
            double additional = Math.Max(0, chosenDecision.DestinationAfter.MortalityRate
                - chosenDecision.DestinationAfter.ExposureRate - chosenDecision.DestinationAfter.CrowdingRate
                - chosenDecision.DestinationAfter.DiseaseRate - chosenDecision.DestinationAfter.StarvationRate);
            e.DaughterRates = new DemographicRates(chosenDecision.DestinationAfter.BirthRate,
                chosenDecision.DestinationAfter.ExposureRate, chosenDecision.DestinationAfter.CrowdingRate,
                chosenDecision.DestinationAfter.DiseaseRate, chosenDecision.DestinationAfter.StarvationRate, additional);
            e.DaughterBlocked = !chosenDecision.MoverPass || !chosenDecision.AggregatePass;
            e.StayersBlocked = !e.DaughterBlocked && !chosenDecision.SourcePass;
            e.BelowThreshold = !chosenDecision.MoverPass || !chosenDecision.AggregatePass;
            e.PhysicalDaughterNegativeNet = chosenDecision.DestinationAfter.BirthRate
                - chosenDecision.DestinationAfter.MortalityRate < 0;
            e.Workforce = chosenWorkforce;
            e.Propose = chosenDecision.Accepted;
            return e;
        }

        // WP10a trigger replayed against the current destination machinery. This intentionally
        // restores only the historical pressure law; radius/spacing, current ecology projection,
        // richer-site ordering, carried resources and daughter/stayer vetoes remain modern.
        private static Evaluation EvaluateLegacyQoL(Context context, Camp camp)
        {
            var w = context.World; var cfg = w.Config; var e = new Evaluation { Camp = camp };
            var regime = RegimeOf(w, camp); var policy = camp.FissionPolicy; var cached = camp.FissionDestination;
            if (cached != null && cached.Regime != (int)regime) { e.ClearTarget = true; e.Invalidations++; cached = null; }

            int residents = camp.Population, daughter = Kinship.DaughterSize(residents, policy), minimum = cfg.FissionMinimumDaughter;
            e.Daughter = daughter; e.Push = LegacyPush(cfg, camp, policy);
            bool legal = daughter >= minimum && daughter < residents - daughter && residents - daughter >= cfg.MinimumCamp;
            if (!(e.Push > 0) || !legal) return e;

            var projections = new Dictionary<int, double>(); double siteRatio = 0, siteHuman = 0, siteHerd = double.PositiveInfinity;
            if (cached != null)
            {
                e.Validations++;
                double d = 0, ratio = 0; siteHuman = 0; siteHerd = double.PositiveInfinity;
                if (!context.Blocked(cached.Tile) && w.Tiles[cached.Tile].TraversableLand)
                {
                    d = Desirability(context, camp, cached.Tile, daughter, regime, projections, e, out ratio, out _);
                    siteHuman = e.SiteHuman; siteHerd = e.SiteHerd;
                }
                if (!(ratio > 0)) { e.ClearTarget = true; e.Invalidations++; cached = null; }
                else { e.Desirability = d; siteRatio = ratio; }
            }

            if (cached == null && e.Push > cfg.FissionLegacySearchPush)
            {
                e.Screened = true; e.Searches++;
                var known = new Known(context, camp);
                int best = -1, bestDistance = 0; double bestScore = -1, bestRatio = -1, bestHuman = 0, bestHerd = double.PositiveInfinity;
                foreach (int tile in w.Map.Radius(camp.Tile, cfg.FissionRadius).OrderBy(n => n))
                {
                    if (!w.Tiles[tile].TraversableLand || !known.Contains(tile) || context.Blocked(tile)) continue;
                    e.Candidates++;
                    double d = Desirability(context, camp, tile, daughter, regime, projections, e, out double ratio, out _);
                    int distance = w.Map.Distance(camp.Tile, tile);
                    if (best < 0 || d > bestScore || d == bestScore && (ratio > bestRatio || ratio == bestRatio && distance < bestDistance))
                    {
                        best = tile; bestScore = d; bestRatio = ratio; bestDistance = distance;
                        bestHuman = e.SiteHuman; bestHerd = e.SiteHerd;
                    }
                }
                if (best >= 0 && bestScore > 0 && bestRatio > 0)
                {
                    e.NewTarget = new FissionTarget { Tile = best, Regime = (int)regime, Turn = w.Turn };
                    e.Desirability = bestScore; siteRatio = bestRatio; siteHuman = bestHuman; siteHerd = bestHerd;
                    cached = e.NewTarget;
                }
            }

            if (cached == null)
            {
                if (e.Searches > 0) { if (e.Candidates == 0) e.NoCandidates = true; else e.NoViable = true; }
                return e;
            }

            e.WithDestination = true; e.Tile = cached.Tile;
            double foodD = Numbers.Clamp(siteRatio, 0, 1);
            double vetoFood = Math.Min(1, Math.Min(siteHerd, Math.Max(0, siteHuman) + Immigration.CarriedPerPerson(cfg, camp)));
            var chosen = EstimateSplit(w, camp, daughter, foodD, 1, vetoFood);
            e.Trigger = FissionTrigger.LegacyQoL; e.Gain = chosen.Gain; e.DaughterQoL = chosen.DaughterQoL; e.DaughterGain = chosen.DaughterGain;
            e.Score = e.Push * e.Desirability;
            if (!(e.Score > cfg.FissionLegacyThreshold)) { e.BelowThreshold = true; return e; }
            e.DaughterTested = true; e.DaughterBlocked = !chosen.DaughterAcceptable(cfg.FissionDaughterMargin);
            e.Propose = !e.DaughterBlocked && chosen.StayersAcceptable(cfg.FissionDaughterMargin);
            e.StayersBlocked = !e.DaughterBlocked && !e.Propose;
            return e;
        }

        // Tiles the Society can know. Paleolithic: current vision only. After the first breakthrough: current vision plus explored tiles.
        private sealed class Known
        {
            private readonly HashSet<int> visible; private readonly ExploredTiles explored;
            public Known(Context context, Camp camp)
            {
                visible = context.Index.VisibleCached(camp.Player);
                explored = ContactKnowledge.Remembers(camp.People) ? camp.People.Memory?.Explored : null;
            }
            public bool Contains(int tile) => visible.Contains(tile) || explored != null && explored.Contains(tile);
        }

        // D = clamp(SiteSupport(daughter cohort) / TargetSupport, 0, 1) with TargetSupport the daughter's own need, so D = 1 when fully
        // supported. Support is regime specific over the radius-one footprint and reuses the start-viability footprint evaluation
        // (four-season freshwater gate and mean recharge, repeatable land stock, aquatic renewal, working share and labor ceiling):
        //   Paleolithic, and Nomadic without herds: gatherable food and freshwater (exactly start viability for the daughter cohort);
        //   Nomadic with herds: the least of human food, herd forage and herd water coverage for the daughter's share of the herds;
        //   Agricultural: repeatable wild and aquatic food plus the managed flow cultivable footprint land yields at the primitive
        //   supported intensity, limited by the daughter's farm labor ceiling.
        // A planning estimate from current projected ecology, not a forecast of realized harvest.
        private static double Desirability(Context context, Camp camp, int tile, int daughter,
            FissionRegime regime, Dictionary<int, double> projections, Evaluation e,
            out double ratio, out SocialNewSiteCapacity siteCapacity)
        {
            ratio = 0; siteCapacity = default; e.SiteHuman = 0; e.SiteHerd = double.PositiveInfinity;
            var w = context.World; var cfg = w.Config; var index = context.Index;
            double Repeatable(int n)
            {
                if (projections.TryGetValue(n, out double cachedValue)) return cachedValue;
                var t = w.Tiles[n]; var now = index.Ecology(n);
                if (cfg.CropHarvestingOnly)
                {
                    // A daughter inherits no field capital. Evaluate natural
                    // accessible stock, without borrowing another camp's crops
                    // or assuming primitive fields have already been built.
                    now.CultivationCapital = 0;
                    var natural = new EcologyCapacity(w, t, now, (int)Math.Floor(w.Days / cfg.BaseDays) % 4);
                    now.Food = Math.Min(now.Food, natural.Food);
                }
                var view = now;
                view.PotentialFood = Math.Max(0, view.PotentialFood - view.Food); view.Food = 0;
                LazyEcology.RecoverLandAfterHarvest(w, t, ref view, w.Days, cfg.TurnDays, null, null, null);
                LazyEcology.ClampCapacity(w, t, ref view);
                double value = Math.Min(now.Food, view.Food); projections[n] = value; e.TileProjections++;
                return value;
            }
            if (!PlacementStage.Footprint(w, tile, Repeatable, out double land, out double aquatic, out double annualWater)) return 0;
            double need = daughter * cfg.Consumption * cfg.GenerationIntervals;
            if (need <= 0) return 0;
            double harvest = PlacementStage.Harvest(w, daughter, land, aquatic);
            if (regime == FissionRegime.Agricultural && !cfg.CropHarvestingOnly)
            {
                double q = cfg.Neolithic.PrimitiveAgricultureSupportedIntensity, managed = 0;
                foreach (int n in w.Map.Radius(tile, 1))
                {
                    var t = w.Tiles[n];
                    if (!CultivationEconomy.Cultivable(t)) continue;
                    double flow = 0;
                    for (int s = 0; s < 4; s++) flow += EcologyMath.ManagedRegenerationFlow(cfg, q, t.Seasons[s].MaxPotentialFood, t.Seasons[s].BiomassRecoveryPerDay, 1);
                    managed += flow / 4 * cfg.TurnDays;
                }
                double support = Math.Min(land + aquatic + managed, daughter * cfg.FarmerProductivity * cfg.GatherPerPerson * cfg.GenerationIntervals);
                ratio = (land + aquatic + managed) / need; e.SiteHuman = ratio;
                siteCapacity = new SocialNewSiteCapacity(land, aquatic, managed);
                return Numbers.Clamp(support / need, 0, 1);
            }
            // Keep uncapped physical richness for destination ordering. The
            // daughter food/veto estimate is bounded by obtainable harvest.
            ratio = (land + aquatic) * PlacementStage.WorkingShare(w, daughter) / need;
            e.SiteHuman = cfg.CropHarvestingOnly ? harvest / need : ratio;
            double food = Numbers.Clamp(harvest / need, 0, 1);
            if (regime == FissionRegime.Paleolithic || regime != FissionRegime.Nomadic && !cfg.CropHarvestingOnly)
            {
                siteCapacity = new SocialNewSiteCapacity(land, aquatic);
                return food;
            }
            double forageNeed = 0, waterNeed = 0; int residents = camp.Population;
            foreach (var herd in index.CampHerds(camp.Id))
            {
                int moved = (int)((long)herd.Animals * daughter / residents);
                if (moved <= 0) continue;
                var species = cfg.GetSpecies(herd.Species);
                forageNeed += moved * species.FoodConsumption * cfg.GenerationIntervals; waterNeed += moved * species.FreshwaterConsumption * cfg.GenerationIntervals;
            }
            if (forageNeed <= 0 && waterNeed <= 0)
            {
                siteCapacity = new SocialNewSiteCapacity(land, aquatic);
                return food;
            }
            double forage = 0, water = 0;
            foreach (int n in w.Map.Radius(tile, 1))
            {
                var t = w.Tiles[n];
                if (t.ElevationLevel >= 10) continue;
                bool drinkable = t.WaterBody != WaterKind.Saltwater && t.FreshwaterSource != FreshwaterSource.None;
                double biomass = 0, recharge = 0;
                for (int s = 0; s < 4; s++)
                {
                    if (t.TraversableLand) biomass += t.Seasons[s].MaxPotentialFood * t.Seasons[s].BiomassRecoveryPerDay * cfg.SustainableProductionFraction;
                    if (drinkable) recharge += t.Seasons[s].MaxFreshwater * t.Seasons[s].WaterRecoveryPerDay;
                }
                var opening = index.Ecology(n);
                forage += cfg.CropHarvestingOnly ? (t.TraversableLand ? Math.Max(0, opening.PotentialFood) : 0) : biomass / 4 * cfg.TurnDays;
                water += recharge / 4 * cfg.TurnDays + (cfg.CropHarvestingOnly && drinkable ? Math.Max(0, opening.Freshwater) : 0);
            }
            // Ratios before the cap at one (the destination order's second key); only a herd need constrains them.
            double rawForage = forageNeed <= 0 ? double.PositiveInfinity : (forage - Math.Min(harvest, land)) / forageNeed, rawWater = waterNeed <= 0 ? double.PositiveInfinity : water / waterNeed;
            e.SiteHerd = Math.Min(rawForage, rawWater); ratio = Math.Min(ratio, e.SiteHerd);
            siteCapacity = new SocialNewSiteCapacity(land, aquatic, 0,
                forageNeed <= 0 ? 1 : rawForage, waterNeed <= 0 ? 1 : rawWater);
            return Math.Min(food, Math.Min(Numbers.Clamp(rawForage, 0, 1), Numbers.Clamp(rawWater, 0, 1)));
        }

        // Commits one daughter settlement. Every ledger is conserved exactly: people, typed food, inventory, herds and fractional
        // progress move in the daughter's people fraction; the parent keeps its site, fields, fixed Housing and controller.
        private static void Found(WorldState w, Evaluation e, TurnRecord record, Dictionary<int, List<MaterialKnowledge>> knowledge, Dictionary<int, DemographicReport> demographics)
        {
            var parent = e.Camp; var ps = parent.People; int before = parent.Population, n = e.Daughter; double f = n / (double)before;
            double parentMortalityBefore = PhysicalDemography.Enabled(w.Config)
                ? e.Decision.SourceNoMove.MortalityRate : 0;
            int societyId = w.NextId++;
            var society = new Society
            {
                Id = societyId, Player = w.AllocatePlayer(), PolicySeed = (int)Numbers.Hash(w.Seed, PolicySeedStream, parent.Id, w.Turn),
                Experience = (double[])ps.Experience.Clone(), NeolithicExperience = ps.NeolithicExperience == null ? new double[3] : (double[])ps.NeolithicExperience.Clone(), TechnologyExposure = ps.TechnologyExposure == null ? new double[3] : (double[])ps.TechnologyExposure.Clone(), SpeciesExperience = ps.SpeciesExperience.Select(p => new SpeciesPractice { Species = p.Species, Experience = p.Experience }).ToList(),
                Technology = ps.Technology, Technologies = (bool[])ps.Technologies.Clone(), TransitionTurn = ps.TransitionTurn, SecondaryDomain = ps.SecondaryDomain, ForcedTransition = ps.ForcedTransition,
                Mandate = new Mandate { Risk = ps.Mandate.Risk, Mobility = ps.Mandate.Mobility, Exploration = ps.Mandate.Exploration, Reserve = ps.Mandate.Reserve, Hunting = ps.Mandate.Hunting, Gathering = ps.Mandate.Gathering, Fishing = ps.Mandate.Fishing, Persistence = ps.Mandate.Persistence },
                Policy = new PolicyState { HuntingInclination = ps.Policy.HuntingInclination, GatheringInclination = ps.Policy.GatheringInclination, FishingInclination = ps.Policy.FishingInclination },
                Progression = new EraProgression { Era = ps.Progression.Era, EraStartDays = ps.Progression.EraStartDays, MajorityEligibilityDays = ps.Progression.MajorityEligibilityDays, ForcedTransitionDays = ps.Progression.ForcedTransitionDays, CompletionDays = ps.Progression.CompletionDays, CompletionTurn = ps.Progression.CompletionTurn, CompletionRule = ps.Progression.CompletionRule },
                TransitionSeeds = ps.TransitionSeeds.Select(t => new TransitionSeed { Technology = t.Technology, Settlement = t.Settlement, Tile = t.Tile, Species = t.Species, Days = t.Days }).ToList(),
                OpeningTransitionProcessed = ps.OpeningTransitionProcessed, OpeningTransitionProcessedInitialized = ps.OpeningTransitionProcessedInitialized,
                CraftExperience = new Dictionary<string, double>(ps.CraftExperience), UnlockedSpecialists = new List<string>(ps.UnlockedSpecialists), NeolithicTechnologies = new List<string>(ps.NeolithicTechnologies),
                FishingGroundKnowledge = (ps.FishingGroundKnowledge ?? new List<FishingGroundSnapshot>()).OrderBy(s => s.TileId).Select(s => s.Copy()).ToList(),
                ParentSociety = ps.Id, KinAffinity = Kinship.PolicyAffinity(parent.FissionPolicy)
            };
            w.Societies.Add(society);
            var camp = new Camp { Id = w.NextId++, Tile = e.Tile, Population = n, DemographicGrowthMultiplier = parent.DemographicGrowthMultiplier };
            if (PhysicalDemography.Enabled(w.Config))
            {
                // Settlement formation transfers people; physical survival and welfare history cannot be reset by fission.
                // The legacy model deliberately retains its pre-v1.1 initialization semantics.
                camp.StationaryYears = parent.StationaryYears;
                camp.RudimentaryExpectation = parent.RudimentaryExpectation;
                camp.FoodDeficitYears = parent.FoodDeficitYears;
                camp.DeficitTurns = parent.DeficitTurns;
                camp.DeficitSeverity = parent.DeficitSeverity;
                camp.FoodNeedsMetRatio = parent.FoodNeedsMetRatio;
                camp.WaterNeedsMetRatio = parent.WaterNeedsMetRatio;
                camp.ClothPerCapita = parent.ClothPerCapita;
                camp.HousingPerCapita = parent.HousingPerCapita;
                camp.EffectiveFertilityRate = parent.EffectiveFertilityRate;
                camp.EffectiveMortalityRate = parent.EffectiveMortalityRate;
                camp.ExposureMortalityRate = parent.ExposureMortalityRate;
                camp.CrowdingMortalityRate = parent.CrowdingMortalityRate;
                camp.DiseaseMortalityRate = parent.DiseaseMortalityRate;
                camp.StarvationMortalityRate = parent.StarvationMortalityRate;
                camp.FissionEligibleTurn = checked(w.Turn + 1 + w.Config.FissionDaughterCooldownTurns);
            }
            // One-time settling grace: the daughter inherits her parent's settling progress and, once established, the
            // settled expectation; her site tenure starts at her new tile (Docs/SettlingGrace.md).
            camp.SettledExpectationEstablished = parent.SettledExpectationEstablished;
            camp.SettlingYears = parent.SettlingYears; camp.SiteYears = 0;
            if (camp.SettledExpectationEstablished && camp.RudimentaryExpectation > 0) camp.RudimentaryExpectation = 1;
            camp.Bind(society); w.Camps.Add(camp);
            // A daughter has no newly resolved needs until her first turn; her committed QoL is the estimate that justified the split
            // (never the unset 1.0). It remains a welfare/planning signal and is not a mortality input.
            camp.QoL = e.DaughterQoL > 0 && e.DaughterQoL <= 1 ? e.DaughterQoL : parent.QoL;
            // Food (typed), portable stores and fractional demographic progress.
            double plant = parent.PlantFood * f, animal = parent.AnimalFood * f, aquatic = parent.AquaticFood * f;
            camp.PlantFood = plant; camp.AnimalFood = animal; camp.AquaticFood = aquatic;
            parent.PlantFood -= plant; parent.AnimalFood -= animal; parent.AquaticFood -= aquatic;
            foreach (var field in InventoryFields)
            {
                double value = (double)field.GetValue(parent.Inventory), moved = value * f;
                field.SetValue(camp.Inventory, moved); field.SetValue(parent.Inventory, value - moved);
            }
            double growth = parent.GrowthRemainder * f, death = parent.DeathRemainder * f;
            camp.GrowthRemainder = growth; parent.GrowthRemainder -= growth; camp.DeathRemainder = death; parent.DeathRemainder -= death;
            if (PhysicalDemography.Enabled(w.Config))
            {
                parent.Workforce = e.Workforce.Stayers; camp.Workforce = e.Workforce.Movers;
            }
            else SplitWorkforce(parent, camp, before, n);
            parent.Population = before - n;
            if (w.Config.Neolithic.SubsistenceNeedsCovered)
            {
                camp.PhysicalClothingPerCapita = SubsistenceNeeds.PhysicalClothingFraction(parent);
                double covered = SubsistenceNeeds.CoveredResidents(w, camp, n);
                camp.ClothPerCapita = SubsistenceNeeds.Coverage(n, covered, camp.PhysicalClothingPerCapita.Value * n);
                camp.HousingPerCapita = SubsistenceNeeds.Coverage(n, covered, NeolithicProduction.HousingCapacity(w, camp, w.Days).Total);
            }
            // Same economic settings; automation continuous so the AI economy runs. Fission policy Neutral, no destination.
            camp.CraftingPriorities = new CraftingPriorities { HandTools = parent.CraftingPriorities.HandTools, Pots = parent.CraftingPriorities.Pots, Cloth = parent.CraftingPriorities.Cloth, FarmTools = parent.CraftingPriorities.FarmTools, Housing = parent.CraftingPriorities.Housing, Tools = parent.CraftingPriorities.Tools, AnimalFabric = parent.CraftingPriorities.AnimalFabric, LightHousing = parent.CraftingPriorities.LightHousing, HerdingTools = parent.CraftingPriorities.HerdingTools, FishingTools = parent.CraftingPriorities.FishingTools };
            camp.StonePriority = parent.StonePriority; camp.ClayPriority = parent.ClayPriority; camp.WoodPriority = parent.WoodPriority;
            camp.PotsTarget = parent.PotsTarget * f; camp.FarmToolsTarget = parent.FarmToolsTarget * f;
            camp.ManagedBreedingIntensity = parent.ManagedBreedingIntensity; camp.ManagedBreedingPolicyInitialized = true;
            camp.CultivationFocusTile = -1; camp.EconomyAutomationTurns = -1;
            camp.FissionPolicy = FissionPolicy.Neutral; camp.FissionDestination = null; parent.FissionDestination = null;
            // Domestic herds move in the same fraction (whole animals, rounded down; the parent keeps the remainder).
            int animals = 0;
            // Animal textile need: a herding parent's daughter leaves with at least one animal of a herd that keeps
            // one, so a pastoral split never founds a camp that cannot make Animal Fabric.
            bool textileHerd = w.Config.AnimalTextileNeedFirst && CultivationEconomy.HusbandryEnabled(parent);
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp == parent.Id).OrderBy(h => h.Id).ToArray())
            {
                int moved = (int)((long)herd.Animals * n / before);
                if (moved <= 0 && textileHerd && animals == 0 && herd.Animals >= 2 && n > 0) moved = 1;
                if (moved <= 0) continue;
                double hf = moved / (double)herd.Animals;
                var split = new Herd
                {
                    Id = w.NextId++, Tile = camp.Tile, Animals = moved, Capacity = herd.Capacity, DomesticCamp = camp.Id, OwnerSociety = society.Id, OpeningCompatibility = herd.OpeningCompatibility, Species = herd.Species,
                    GrowthRemainder = herd.GrowthRemainder * hf, UnmetFood = herd.UnmetFood * hf, UnmetFreshwater = herd.UnmetFreshwater * hf, DeficitTurns = herd.DeficitTurns, DeficitSeverity = herd.DeficitSeverity,
                    DeathRemainder = herd.DeathRemainder * hf, LastNutrition = herd.LastNutrition, LastFoodNutrition = herd.LastFoodNutrition, LastWaterNutrition = herd.LastWaterNutrition, ManagementFulfillment = herd.ManagementFulfillment,
                    AnimalFoodPotential = herd.AnimalFoodPotential * hf, AnimalFoodProduced = herd.AnimalFoodProduced * hf,
                    StartingAnimals = (int)((long)herd.StartingAnimals * moved / herd.Animals), SurvivingAnimals = (int)((long)herd.SurvivingAnimals * moved / herd.Animals), NewbornAnimals = (int)((long)herd.NewbornAnimals * moved / herd.Animals), StarvationDeaths = (int)((long)herd.StarvationDeaths * moved / herd.Animals), NutritionInitialized = true
                };
                herd.GrowthRemainder -= split.GrowthRemainder; herd.UnmetFood -= split.UnmetFood; herd.UnmetFreshwater -= split.UnmetFreshwater; herd.DeathRemainder -= split.DeathRemainder;
                herd.AnimalFoodPotential -= split.AnimalFoodPotential; herd.AnimalFoodProduced -= split.AnimalFoodProduced;
                herd.StartingAnimals -= split.StartingAnimals; herd.SurvivingAnimals -= split.SurvivingAnimals; herd.NewbornAnimals -= split.NewbornAnimals; herd.StarvationDeaths -= split.StarvationDeaths;
                herd.Animals -= moved; w.Herds.Add(split); animals += moved;
            }
            // Material discovery is knowledge; the daughter knows what the parent knew at fission.
            if (knowledge.TryGetValue(ps.Id, out var known))
                foreach (var k in known.ToArray()) { var copy = new MaterialKnowledge { Society = society.Id, Tile = k.Tile, Source = k.Source }; w.MaterialKnowledge.Add(copy); }
            ContactKnowledge.RecordKin(w, ps, society, parent, camp);
            if (demographics.TryGetValue(parent.Id, out var report)) report.SplitOff += n;
            var parentRates = PhysicalDemography.Enabled(w.Config)
                ? new DemographicRates(e.Decision.SourceAfter.BirthRate, e.Decision.SourceAfter.ExposureRate,
                    e.Decision.SourceAfter.CrowdingRate, e.Decision.SourceAfter.DiseaseRate,
                    e.Decision.SourceAfter.StarvationRate, Math.Max(0, e.Decision.SourceAfter.MortalityRate
                        - e.Decision.SourceAfter.ExposureRate - e.Decision.SourceAfter.CrowdingRate
                        - e.Decision.SourceAfter.DiseaseRate - e.Decision.SourceAfter.StarvationRate)) : default;
            var daughterRates = PhysicalDemography.Enabled(w.Config) ? e.DaughterRates : default;
            var fission = new FissionReport
            {
                Turn = w.Turn, ParentCamp = parent.Id, DaughterCamp = camp.Id, ParentSociety = ps.Id, DaughterSociety = society.Id, DaughterPlayer = society.Player, Tile = camp.Tile, ParentBefore = before, Daughter = n, Regime = (int)RegimeOf(w, parent),
                AnimalsMoved = animals, Policy = parent.FissionPolicy, Push = e.Push, Desirability = e.Desirability, Score = e.Score, Affinity = society.KinAffinity, Gain = e.Gain, DaughterQoL = e.DaughterQoL, Trigger = e.Trigger, DaughterGain = e.DaughterGain,
                ParentMortalityBefore = parentMortalityBefore, ParentMortalityAfter = parentRates.Death,
                DaughterMortality = daughterRates.Death, DaughterNetRate = daughterRates.Net,
                DaughterNutrition = e.DaughterNutrition, DaughterClothingCoverage = e.DaughterClothing,
                DaughterHousingCoverage = e.DaughterHousing,
                DaughterNetNonnegative = !PhysicalDemography.Enabled(w.Config) || daughterRates.Net >= 0,
                HigherQoLWorseMortality = PhysicalDemography.Enabled(w.Config)
                    && e.DaughterQoL > e.Decision.SourceNoMove.QoL
                    && daughterRates.Death > parentMortalityBefore + 1e-12,
                ExitOpportunityPressure = e.ExitOpportunityPressure
            };
            if (e.Decision != null)
            {
                fission.MoverUtilityChange = e.Decision.MoverUtilityChange;
                fission.SourceUtilityChange = e.Decision.SourceUtilityChange;
                fission.AggregateWelfareChange = e.Decision.AggregateWelfareChange;
                fission.ForecastParentQoL = e.Decision.SourceAfter.QoL;
                fission.ForecastDaughterQoL = e.Decision.DestinationAfter.QoL;
                fission.ForecastParentNutrition = e.Decision.SourceAfter.Nutrition;
                fission.ForecastParentClothing = e.Decision.SourceAfter.ClothingCoverage;
                fission.ForecastParentHousing = e.Decision.SourceAfter.HousingCoverage;
                fission.ForecastDaughterMortalityRisk = e.Decision.DestinationAfter.MortalityRisk;
                fission.ForecastDaughterStability = e.Decision.DestinationAfter.Stability;
                fission.ForecastDaughterRenewableFoodRatio = e.Decision.DestinationAfter.RenewableFoodRatio;
                fission.ForecastDaughterPlantFoodProduction = e.Decision.DestinationAfter.PlantFoodProduction;
                fission.ForecastDaughterAnimalFoodProduction = e.Decision.DestinationAfter.AnimalFoodProduction;
                fission.ForecastDaughterAquaticFoodProduction = e.Decision.DestinationAfter.AquaticFoodProduction;
                fission.ForecastDaughterHerdForageCoverage = e.Decision.DestinationAfter.HerdForageCoverage;
                fission.ForecastDaughterHerdWaterCoverage = e.Decision.DestinationAfter.HerdWaterCoverage;
                fission.ForecastParentExposureRate = e.Decision.SourceAfter.ExposureRate;
                fission.ForecastParentCrowdingRate = e.Decision.SourceAfter.CrowdingRate;
                fission.ForecastParentDiseaseRate = e.Decision.SourceAfter.DiseaseRate;
                fission.ForecastParentStarvationRate = e.Decision.SourceAfter.StarvationRate;
                fission.ForecastDaughterExposureRate = e.Decision.DestinationAfter.ExposureRate;
                fission.ForecastDaughterCrowdingRate = e.Decision.DestinationAfter.CrowdingRate;
                fission.ForecastDaughterDiseaseRate = e.Decision.DestinationAfter.DiseaseRate;
                fission.ForecastDaughterStarvationRate = e.Decision.DestinationAfter.StarvationRate;
                fission.ForecastConfidence = (int)e.Decision.DestinationAfter.Confidence;
                fission.MatchedOpportunities = e.Decision.Opportunity.Matched;
            }
            record.Fissions.Add(fission);
            record.Log("Camp " + parent.Id + " (tribe " + parent.Player + ") split: " + n + " of " + before + " people founded camp " + camp.Id + " (tribe " + society.Player + ") at tile " + camp.Tile + ".");
        }

        // Exact pre-v1.1 largest-remainder split. Farmers stay with the parent's
        // fields, so their daughter share becomes unassigned gatherers. This path
        // is intentionally separate from Physical-v1's conserved role transfer.
        private static void SplitWorkforce(Camp parent, Camp daughter, int residents, int take)
        {
            var ledger = parent.Workforce;
            var roles = ledger.Assignments.Where(a => a != null && a.Population > 0)
                .OrderBy(a => a.Role, StringComparer.Ordinal).ToArray();
            var counts = new[] { ledger.Gatherers(residents) }
                .Concat(roles.Select(a => a.Population)).ToArray();
            long total = counts.Sum(c => (long)c);
            var share = counts.Select(c => total <= 0 ? 0 : (int)(take * (long)c / total)).ToArray();
            int remainder = take - share.Sum();
            foreach (int i in Enumerable.Range(0, counts.Length)
                .OrderByDescending(i => total <= 0 ? 0 : take * (long)counts[i] % total).ThenBy(i => i))
                if (remainder > 0 && share[i] < counts[i]) { share[i]++; remainder--; }
            daughter.Workforce = new PopulationLedger();
            for (int i = 0; i < roles.Length; i++)
            {
                roles[i].Population -= share[i + 1];
                if (share[i + 1] > 0 && roles[i].Role != WorkforceRoles.Farmer)
                    daughter.Workforce.Assignments.Add(new PopulationAssignment
                        { Role = roles[i].Role, Population = share[i + 1] });
            }
            ledger.Normalize(residents - take); daughter.Workforce.Normalize(take);
        }

    }
}
