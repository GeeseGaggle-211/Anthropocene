using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace Anthropocene.Simulation
{
    // Per-camp fission policy (QoLFissionTradeDesign 3.6). The enum's zero is Neutral so that an absent saved field means Neutral.
    public enum FissionPolicy { Neutral = 0, Discourage = 1, Encourage = 2 }
    public enum FissionRegime { Paleolithic = 0, Nomadic = 1, Agricultural = 2 }
    public enum KinRelation { Parent, Daughter, Sibling }
    // What moved a split past its threshold. Zero is Crowding so an absent saved field means Crowding (every pre-distress split was one).
    public enum FissionTrigger { Crowding = 0, Distress = 1 }

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
    // Temporal abstraction: one evaluation per turn boundary from the frozen post-demography snapshot; QoL is the committed
    // value of the turn just resolved and recomputes next turn. Nothing is integrated over sub-turn time.
    // Cost: O(camps) push tests; each camp with push > 0 and a feasible daughter validates its cache (one footprint, at most 7
    // tile projections); only a camp whose expected gain is at least FissionSearchFraction of its threshold and with no valid cache searches (at most the tiles from
    // the camp spacing out to FissionRadius, about 3 r^2 of them, each candidate one footprint). The daughter's shelter estimate is O(1) closed forms. Reconcile and commit are proportional to proposals. Nothing scales
    // with elapsed years, map size or the number of Societies.
    public static class Fission
    {
        public const int PolicySeedStream = 500;
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
        // the destination's support (1 before a destination is known). DaughterExpected uses the parent's expectation E on both sides
        // (like with like: the post-move expectation reset is a benefit the parent could get by moving itself and must not by itself cause a
        // split); DaughterGain = Q_d / Q_now - 1, and 1e9 (satisfied) when Q_now is zero and Q_d is positive. DaughterQoL keeps the
        // post-move expectation and F = W = 1: the veto and the daughter's founding QoL, as before.
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
            double e = camp.RudimentaryExpectation > 0 ? camp.RudimentaryExpectation : floor + (1 - floor) * Numbers.Clamp(camp.StationaryYears / n.RudimentaryExpectationRampYears, 0, 1);
            var shelter = NeolithicProduction.HousingCapacity(w, camp, w.Days); double fixedPeople = shelter.Fixed, portable = shelter.Light;
            double Housing(double people, double capacity) => people > 0 ? Math.Min(1, capacity / people) : 1;
            double Share(double people, double fixedCapacity, double portableCapacity) => NeolithicProduction.HousingShare(n, new NeolithicProduction.ShelterCapacity(fixedCapacity, portableCapacity), people);
            double Coverage(double expectation, double housing) => n.RudimentaryWeightClothing * (1 - expectation * (1 - camp.ClothPerCapita)) + n.RudimentaryWeightHousing * (1 - expectation * (1 - housing));
            double Ratio(double a, double b) => b > 0 ? a / b : (a > 0 ? 1e9 : 0);
            double b0 = camp.FoodNeedsMetRatio * camp.WaterNeedsMetRatio;
            double stayFood = QoLModel.FoodAt(camp.FoodNeedsMetRatio, residents, 0, stay) * QoLModel.WaterAt(camp.WaterNeedsMetRatio, residents, stay);
            // The parent keeps all fixed housing and splits portable housing in proportion; FissionHousingRelief weights how much of the
            // stayers' relief counts (1 = all, 0 = none) for both housing coverage and the cohesion housing share H. At relief 0 the
            // stayers' H is exactly the present H, so only the headcount changes S (Docs/SocialCohesion.md).
            double relief = cfg.FissionHousingRelief;
            double housingNow = Housing(residents, fixedPeople + portable), housingRelieved = Housing(stay, fixedPeople + portable * (1 - f));
            double housingAfter = housingNow + relief * (housingRelieved - housingNow);
            double shareNow = Share(residents, fixedPeople, portable), shareRelieved = Share(stay, fixedPeople, portable * (1 - f));
            double shareAfter = shareNow + relief * (shareRelieved - shareNow);
            double sNow = NeolithicProduction.SocialCohesion(n, shareNow, residents), sAfter = NeolithicProduction.SocialCohesion(n, shareAfter, stay);
            double qNow = b0 * Coverage(e, housingNow) * sNow;
            double qAfter = stayFood * Coverage(e, housingAfter) * sAfter;
            // Q_now = 0 (no food or no coverage at all) leaves a relative gain undefined: the fallback is the cohesion gain S_after / S_now - 1,
            // the QoL a unit-coverage camp would gain, so a destitute crowded camp still feels its crowding.
            double gain = qNow > 1e-9 ? qAfter / qNow - 1 : Ratio(sAfter, sNow) - 1;
            // The daughter at a viable site: clothing coverage unchanged, housing from her carried and built shelter alone, and the
            // expectation the game applies after a move (stationary years reset, then one turn elapses) for the veto.
            double years = cfg.TurnDays / cfg.DaysPerYear;
            double ed = floor + (1 - floor) * Numbers.Clamp(years / n.RudimentaryExpectationRampYears, 0, 1);
            var own = DaughterShelter(w, camp, nd);
            double housingD = Housing(nd, own.Total), cohesionD = NeolithicProduction.SocialCohesion(n, Share(nd, own.Fixed, own.Light), nd);
            double qd = Numbers.Clamp(vetoFood, 0, 1) * Coverage(ed, housingD) * cohesionD;
            double qdExpected = Numbers.Clamp(daughterFood, 0, 1) * Numbers.Clamp(daughterWater, 0, 1) * Coverage(e, housingD) * cohesionD;
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
        //   multipliers from ToolCoverageBudget on her carried tools). The Builders first assemble fixed Housing (needs Cultivation; one
        //   carried Housing Material or one Wood/Clay unit each, material-making labour included), then sew Light Housing from carried
        //   Animal Fabric (needs Husbandry), each only up to the people still unsheltered. Nothing is gathered at the site, so a daughter
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
            if (inv == null || need <= 1e-9 || !(labor > 0) || !CultivationEconomy.EconomyEnabled(parent)) return new NeolithicProduction.ShelterCapacity(fixedPeople, lightPeople);
            bool specialist = parent.People.HasTech(NeolithicTech.Construction);
            double Rate(double baseRate) => specialist
                ? SpecialistProductivity.SpecialistRate(n, baseRate, daughter)
                : baseRate * n.GeneralistTaskEfficiency;
            double copper = parent.People.HasTech(NeolithicTech.CopperWorking) ? Math.Max(0, inv.CopperTools) * f * intervals / n.ToolCoveragePerWorker : 0;
            var tools = new ToolCoverageBudget(n, Math.Max(0, inv.HandTools) * f, Math.Max(0, inv.Tools) * f * ToolCoverageBudget.OutputsPerTool, copper);
            if (CultivationEconomy.Enabled(parent))
            {
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
            if (CultivationEconomy.HusbandryEnabled(parent) && need > 1e-9 && labor > 0)
            {
                double lightRate = Rate(n.LightHousingBuilderOutputPerBaseDays);
                double sewn = Math.Min(need / n.LightHousingPeoplePerUnit, Math.Max(0, inv.AnimalFabric) * f / n.AnimalFabricPerLightHousing);
                sewn = Math.Min(sewn, tools.Capacity(CraftGood.LightHousing, lightRate, labor));
                if (sewn > 0) lightPeople += sewn * n.LightHousingPeoplePerUnit;
            }
            return new NeolithicProduction.ShelterCapacity(fixedPeople, lightPeople);
        }

        // The camp's committed QoL terms as a pure function of headcount (WP10b; the same formulas as EstimateSplit and ResolveNeeds):
        // F, W and the expectation E unchanged; clothing coverage and housing per head diluted because incoming people bring neither;
        // social cohesion S evaluated at the new headcount. At(Residents) equals EstimateSplit(...).QNow.
        public readonly struct QoLModel
        {
            private readonly NeolithicConfig n; private readonly Camp camp; private readonly double b0, expectation; private readonly NeolithicProduction.ShelterCapacity shelter;
            public readonly int Residents;
            internal QoLModel(WorldState w, Camp camp)
            {
                n = w.Config.Neolithic; this.camp = camp; Residents = camp.Population;
                double floor = n.RudimentaryExpectationFloor;
                expectation = camp.RudimentaryExpectation > 0 ? camp.RudimentaryExpectation : floor + (1 - floor) * Numbers.Clamp(camp.StationaryYears / n.RudimentaryExpectationRampYears, 0, 1);
                shelter = NeolithicProduction.HousingCapacity(w, camp, w.Days);
                b0 = camp.FoodNeedsMetRatio * camp.WaterNeedsMetRatio;
            }
            public double At(int people) => people <= 0 ? 0 : b0 * Shape(people);
            // Clothing coverage, housing per head and social cohesion at a headcount (everything in QoL except food and water).
            private double Shape(int people)
            {
                double cloth = Math.Min(1, camp.ClothPerCapita * Residents / people), housing = NeolithicProduction.HousingCoverage(shelter, people);
                double coverage = n.RudimentaryWeightClothing * (1 - expectation * (1 - cloth)) + n.RudimentaryWeightHousing * (1 - expectation * (1 - housing));
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
            private readonly Lazy<PlanningIndex> index;
            public PlanningIndex Index => index.Value;
            public Context(WorldState w)
            {
                World = w; CampTiles = new HashSet<int>(w.Camps.Select(c => c.Tile));
                index = new Lazy<PlanningIndex>(() => new PlanningIndex(w), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
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

        private sealed class Evaluation
        {
            public Camp Camp;
            public double Push, Desirability, Score, Gain, DaughterQoL, DaughterGain;
            public FissionTrigger Trigger;
            public int Daughter, Tile = -1;
            public bool ClearTarget, Propose, DaughterTested, DaughterBlocked, Screened, NoCandidates, NoViable, WithDestination, BelowThreshold, StayersBlocked;
            public FissionTarget NewTarget;
            public double SiteHuman, SiteHerd;
            public int Validations, Searches, Candidates, TileProjections, Invalidations;
        }

        // excluded: camps that emitted an immigration intent or received arrivals this turn (WP10b); they emit no fission intent.
        public static void Resolve(WorldState w, TurnRecord record, PhaseExecution execution, ResolutionMetrics metrics, HashSet<int> excluded = null)
        {
            if (!w.Config.FissionEnabled) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            // Frozen post-demography snapshot: nothing below the parallel map writes to the world.
            var camps = w.Camps.Where(c => c.Population > 0 && (excluded == null || !excluded.Contains(c.Id))).OrderBy(c => c.Id).ToArray();
            var context = new Context(w);
            var evaluations = (execution ?? new PhaseExecution()).Map(camps, c => Evaluate(context, c));
            var committed = new List<int>(); int spacing = Math.Max(1, w.Config.MinimumCampSpacing);
            Dictionary<int, List<MaterialKnowledge>> knowledge = null; Dictionary<int, DemographicReport> demographics = null;
            // Canonical reconcile and commit in camp ID order. A conflicting proposal is dropped this turn and keeps its cache.
            foreach (var e in evaluations)
            {
                metrics.FissionCampsEvaluated++;
                metrics.FissionValidations += e.Validations; metrics.FissionSearches += e.Searches; metrics.FissionCandidatesScored += e.Candidates;
                metrics.FissionTileProjections += e.TileProjections; metrics.FissionCacheInvalidations += e.Invalidations;
                if (e.ClearTarget) e.Camp.FissionDestination = null;
                if (e.NewTarget != null) e.Camp.FissionDestination = e.NewTarget;
                if (e.DaughterTested) { metrics.FissionDaughterChecks++; if (e.DaughterBlocked) metrics.FissionDaughterBlocked++; }
                if (e.Screened) metrics.FissionScreened++;
                if (e.NoCandidates) metrics.FissionSearchesNoCandidates++;
                if (e.NoViable) metrics.FissionSearchesNoViable++;
                if (e.WithDestination) metrics.FissionWithDestination++;
                if (e.BelowThreshold) metrics.FissionBelowThreshold++;
                if (e.StayersBlocked) metrics.FissionStayersBlocked++;
                if (!e.Propose) continue;
                metrics.FissionProposals++; if (e.Trigger == FissionTrigger.Distress) metrics.FissionDistressProposals++; else metrics.FissionCrowdingProposals++;
                if (committed.Any(t => w.Map.Distance(t, e.Tile) < spacing)) { metrics.FissionDropped++; continue; }
                if (knowledge == null)
                {
                    knowledge = new Dictionary<int, List<MaterialKnowledge>>();
                    foreach (var k in w.MaterialKnowledge) { if (!knowledge.TryGetValue(k.Society, out var list)) knowledge[k.Society] = list = new List<MaterialKnowledge>(); list.Add(k); }
                    demographics = new Dictionary<int, DemographicReport>();
                    foreach (var d in record.Demographics) demographics[d.Camp] = d;
                }
                Found(w, e, record, knowledge, demographics);
                committed.Add(e.Tile); metrics.FissionCommits++; if (e.Trigger == FissionTrigger.Distress) { metrics.FissionDistressCommits++; metrics.FissionDistressDaughters += e.Daughter; } else metrics.FissionCrowdingCommits++;
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
        private static Evaluation Evaluate(Context context, Camp camp)
        {
            var w = context.World; var cfg = w.Config; var e = new Evaluation { Camp = camp };
            var regime = RegimeOf(w, camp); var policy = camp.FissionPolicy; var cached = camp.FissionDestination;
            // A change of subsistence regime invalidates the cached destination regardless of the push.
            if (cached != null && cached.Regime != (int)regime) { e.ClearTarget = true; e.Invalidations++; cached = null; }
            int residents = camp.Population, n0 = Kinship.DaughterSize(residents, policy), minimum = cfg.FissionMinimumDaughter;
            int largest = Math.Min((residents - 1) / 2, residents - cfg.MinimumCamp);
            // The daughter is a smaller group than the parent that remains and meets the minimum viable population.
            bool crowdLegal = n0 >= minimum && n0 <= largest;
            double met = camp.FoodNeedsMetRatio * camp.WaterNeedsMetRatio;
            int short0 = met < 1 - 1e-9 ? (int)Math.Ceiling(residents * (1 - met) - 1e-9) : 0;
            bool distressLegal = short0 > 0 && largest >= minimum;
            int n1 = distressLegal ? Math.Max(minimum, Math.Min(largest, Math.Max(n0, short0))) : n0;
            e.Daughter = crowdLegal ? n0 : n1;
            if (!crowdLegal && !distressLegal) return e;
            double threshold = Kinship.GainThreshold(cfg, policy), screen = cfg.FissionSearchFraction * threshold;
            Estimate crowd = crowdLegal ? EstimateSplit(w, camp, n0, 1, 1) : default;
            // Optimistic screen (F_d = W_d = 1): gates the search exactly like the crowding screen; identical to the crowding estimate for a fed camp.
            Estimate optimistic = !distressLegal ? default : n1 == n0 && crowdLegal ? crowd : EstimateSplit(w, camp, n1, 1, 1);
            bool crowdScreen = crowdLegal && crowd.Gain >= screen, distressScreen = distressLegal && optimistic.DaughterGain >= screen;
            var shown = crowdLegal ? crowd : optimistic;
            e.Gain = shown.Gain; e.DaughterQoL = shown.DaughterQoL; e.DaughterGain = shown.DaughterGain; e.Push = Push(crowdLegal ? crowd.Gain : optimistic.DaughterGain, threshold);
            if (!crowdScreen && !distressScreen) return e;
            e.Screened = true;
            int size = distressScreen ? n1 : n0;
            var projections = new Dictionary<int, double>(); double siteRatio = 0, siteHuman = 0, siteHerd = double.PositiveInfinity;
            if (cached != null)
            {
                e.Validations++;
                double d = 0, ratio = 0; siteHuman = 0; siteHerd = double.PositiveInfinity;
                if (!context.Blocked(cached.Tile) && w.Tiles[cached.Tile].TraversableLand) { d = Desirability(context, camp, cached.Tile, size, regime, projections, e, out ratio); siteHuman = e.SiteHuman; siteHerd = e.SiteHerd; }
                // Relative destination test: a site stays a destination while it is habitable (its freshwater gate passes and it supports some food);
                // whether the daughter is better off than at home is the veto below, not an absolute viability bar.
                if (!(ratio > 0)) { e.ClearTarget = true; e.Invalidations++; cached = null; }
                else { e.Desirability = d; siteRatio = ratio; }
            }
            if (cached == null)
            {
                e.Searches++;
                var known = new Known(context, camp);
                int best = -1, bestDistance = 0; double bestScore = -1, bestRatio = -1, bestHuman = 0, bestHerd = double.PositiveInfinity;
                // Order: D (capped at 1) descending, then the uncapped support ratio descending, then hex distance, then tile ID
                // (candidates arrive in tile order, so a full tie keeps the lowest ID).
                foreach (int tile in w.Map.Radius(camp.Tile, cfg.FissionRadius).OrderBy(n => n))
                {
                    if (!w.Tiles[tile].TraversableLand || !known.Contains(tile) || context.Blocked(tile)) continue;
                    e.Candidates++;
                    double d = Desirability(context, camp, tile, size, regime, projections, e, out double ratio);
                    int distance = w.Map.Distance(camp.Tile, tile);
                    if (best < 0 || d > bestScore || d == bestScore && (ratio > bestRatio || ratio == bestRatio && distance < bestDistance)) { best = tile; bestScore = d; bestRatio = ratio; bestDistance = distance; bestHuman = e.SiteHuman; bestHerd = e.SiteHerd; }
                }
                if (best >= 0 && bestScore > 0 && bestRatio > 0)
                {
                    e.NewTarget = new FissionTarget { Tile = best, Regime = (int)regime, Turn = w.Turn }; e.ClearTarget = false;
                    e.Desirability = bestScore; siteRatio = bestRatio; siteHuman = bestHuman; siteHerd = bestHerd; cached = e.NewTarget;
                }
            }
            if (cached == null) { if (e.Candidates == 0) e.NoCandidates = true; else e.NoViable = true; return e; }
            e.WithDestination = true;
            e.Tile = cached.Tile;
            // With the destination known: the leaving group's F_d is the site's support ratio for its cohort (capped at one; for herded nomads
            // it already includes herd forage and water, so it stands for F_d W_d); W_d = 1 (freshwater is a gate of the footprint).
            double foodD = Numbers.Clamp(siteRatio, 0, 1);
            // The veto's F_d: the site's support ratio plus the person-equivalents of the turn's need her share of the stored food covers
            // (the same carried term as immigration), capped at one. Carried human food does not feed herds, so a herded daughter's herd forage and water coverage caps it. The daughter must be better off there than at home, not at an absolute bar.
            double vetoFood = Math.Min(1, Math.Min(siteHerd, Math.Max(0, siteHuman) + Immigration.CarriedPerPerson(cfg, camp)));
            Estimate chosen = default; bool use = false;
            if (distressLegal)
            {
                var distress = EstimateSplit(w, camp, n1, foodD, 1, vetoFood);
                if (distress.DaughterGain >= threshold) { chosen = distress; use = true; e.Trigger = FissionTrigger.Distress; }
            }
            if (!use && crowdLegal && crowd.Gain >= threshold) { chosen = EstimateSplit(w, camp, n0, foodD, 1, vetoFood); use = true; e.Trigger = FissionTrigger.Crowding; }
            if (!use) { e.BelowThreshold = true; return e; }
            e.Daughter = chosen.Daughter; e.Gain = chosen.Gain; e.DaughterQoL = chosen.DaughterQoL; e.DaughterGain = chosen.DaughterGain;
            e.Push = Push(e.Trigger == FissionTrigger.Distress ? chosen.DaughterGain : chosen.Gain, threshold); e.Score = e.Push * e.Desirability;
            // "Would part of this population be better off": the daughter's estimated QoL must not fall more than the margin below the parent's,
            // and the stayers must not be worse off (true by construction: fewer mouths and no housing taken).
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
        private static double Desirability(Context context, Camp camp, int tile, int daughter, FissionRegime regime, Dictionary<int, double> projections, Evaluation e, out double ratio)
        {
            ratio = 0; e.SiteHuman = 0; e.SiteHerd = double.PositiveInfinity;
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
                return Numbers.Clamp(support / need, 0, 1);
            }
            // Keep uncapped physical richness for destination ordering. The
            // daughter food/veto estimate is bounded by obtainable harvest.
            ratio = (land + aquatic) * PlacementStage.WorkingShare(w, daughter) / need;
            e.SiteHuman = cfg.CropHarvestingOnly ? harvest / need : ratio;
            double food = Numbers.Clamp(harvest / need, 0, 1);
            if (regime == FissionRegime.Paleolithic || regime != FissionRegime.Nomadic && !cfg.CropHarvestingOnly) return food;
            double forageNeed = 0, waterNeed = 0; int residents = camp.Population;
            foreach (var herd in index.CampHerds(camp.Id))
            {
                int moved = (int)((long)herd.Animals * daughter / residents);
                if (moved <= 0) continue;
                var species = cfg.GetSpecies(herd.Species);
                forageNeed += moved * species.FoodConsumption * cfg.GenerationIntervals; waterNeed += moved * species.FreshwaterConsumption * cfg.GenerationIntervals;
            }
            if (forageNeed <= 0 && waterNeed <= 0) return food;
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
            return Math.Min(food, Math.Min(Numbers.Clamp(rawForage, 0, 1), Numbers.Clamp(rawWater, 0, 1)));
        }

        // Commits one daughter settlement. Every ledger is conserved exactly: people, typed food, inventory, herds and fractional
        // progress move in the daughter's people fraction; the parent keeps its site, fields, fixed Housing and controller.
        private static void Found(WorldState w, Evaluation e, TurnRecord record, Dictionary<int, List<MaterialKnowledge>> knowledge, Dictionary<int, DemographicReport> demographics)
        {
            var parent = e.Camp; var ps = parent.People; int before = parent.Population, n = e.Daughter; double f = n / (double)before;
            int societyId = w.NextId++;
            var society = new Society
            {
                Id = societyId, Player = w.AllocatePlayer(), PolicySeed = (int)Numbers.Hash(w.Seed, PolicySeedStream, parent.Id, w.Turn),
                Experience = (double[])ps.Experience.Clone(), NeolithicExperience = ps.NeolithicExperience == null ? new double[3] : (double[])ps.NeolithicExperience.Clone(), SpeciesExperience = ps.SpeciesExperience.Select(p => new SpeciesPractice { Species = p.Species, Experience = p.Experience }).ToList(),
                Technology = ps.Technology, Technologies = (bool[])ps.Technologies.Clone(), TransitionTurn = ps.TransitionTurn, SecondaryDomain = ps.SecondaryDomain, ForcedTransition = ps.ForcedTransition,
                Mandate = new Mandate { Risk = ps.Mandate.Risk, Mobility = ps.Mandate.Mobility, Exploration = ps.Mandate.Exploration, Reserve = ps.Mandate.Reserve, Hunting = ps.Mandate.Hunting, Gathering = ps.Mandate.Gathering, Fishing = ps.Mandate.Fishing, Persistence = ps.Mandate.Persistence },
                Policy = new PolicyState { HuntingInclination = ps.Policy.HuntingInclination, GatheringInclination = ps.Policy.GatheringInclination, FishingInclination = ps.Policy.FishingInclination },
                Progression = new EraProgression { Era = ps.Progression.Era, EraStartDays = ps.Progression.EraStartDays, MajorityEligibilityDays = ps.Progression.MajorityEligibilityDays, ForcedTransitionDays = ps.Progression.ForcedTransitionDays, CompletionDays = ps.Progression.CompletionDays, CompletionTurn = ps.Progression.CompletionTurn, CompletionRule = ps.Progression.CompletionRule },
                TransitionSeeds = ps.TransitionSeeds.Select(t => new TransitionSeed { Technology = t.Technology, Settlement = t.Settlement, Tile = t.Tile, Species = t.Species, Days = t.Days }).ToList(),
                OpeningTransitionProcessed = ps.OpeningTransitionProcessed, OpeningTransitionProcessedInitialized = ps.OpeningTransitionProcessedInitialized,
                CraftExperience = new Dictionary<string, double>(ps.CraftExperience), UnlockedSpecialists = new List<string>(ps.UnlockedSpecialists), NeolithicTechnologies = new List<string>(ps.NeolithicTechnologies),
                ParentSociety = ps.Id, KinAffinity = Kinship.PolicyAffinity(parent.FissionPolicy)
            };
            w.Societies.Add(society);
            var camp = new Camp { Id = w.NextId++, Tile = e.Tile, Population = n, DemographicGrowthMultiplier = parent.DemographicGrowthMultiplier };
            camp.Bind(society); w.Camps.Add(camp);
            // A daughter has no resolved needs until her first turn; her committed QoL is the estimate that justified the split (never the unset 1.0),
            // so QoL mortality and the planner see a real value in that first turn. Fall back to the parent's committed QoL if no usable estimate exists.
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
            SplitWorkforce(parent, camp, before, n);
            parent.Population = before - n;
            // Same economic settings; automation continuous so the AI economy runs. Fission policy Neutral, no destination.
            camp.CraftingPriorities = new CraftingPriorities { HandTools = parent.CraftingPriorities.HandTools, Pots = parent.CraftingPriorities.Pots, Cloth = parent.CraftingPriorities.Cloth, FarmTools = parent.CraftingPriorities.FarmTools, Housing = parent.CraftingPriorities.Housing, Tools = parent.CraftingPriorities.Tools, AnimalFabric = parent.CraftingPriorities.AnimalFabric, LightHousing = parent.CraftingPriorities.LightHousing, HerdingTools = parent.CraftingPriorities.HerdingTools };
            camp.StonePriority = parent.StonePriority; camp.ClayPriority = parent.ClayPriority; camp.WoodPriority = parent.WoodPriority;
            camp.PotsTarget = parent.PotsTarget * f; camp.FarmToolsTarget = parent.FarmToolsTarget * f;
            camp.ManagedBreedingIntensity = parent.ManagedBreedingIntensity; camp.ManagedBreedingPolicyInitialized = true;
            camp.CultivationFocusTile = -1; camp.EconomyAutomationTurns = -1;
            camp.FissionPolicy = FissionPolicy.Neutral; camp.FissionDestination = null; parent.FissionDestination = null;
            // Domestic herds move in the same fraction (whole animals, rounded down; the parent keeps the remainder).
            int animals = 0;
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp == parent.Id).OrderBy(h => h.Id).ToArray())
            {
                int moved = (int)((long)herd.Animals * n / before); if (moved <= 0) continue;
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
            record.Fissions.Add(new FissionReport
            {
                Turn = w.Turn, ParentCamp = parent.Id, DaughterCamp = camp.Id, ParentSociety = ps.Id, DaughterSociety = society.Id, DaughterPlayer = society.Player, Tile = camp.Tile, ParentBefore = before, Daughter = n, Regime = (int)RegimeOf(w, parent),
                AnimalsMoved = animals, Policy = parent.FissionPolicy, Push = e.Push, Desirability = e.Desirability, Score = e.Score, Affinity = society.KinAffinity, Gain = e.Gain, DaughterQoL = e.DaughterQoL, Trigger = e.Trigger, DaughterGain = e.DaughterGain
            });
            record.Log("Camp " + parent.Id + " (tribe " + parent.Player + ") split: " + n + " of " + before + " people founded camp " + camp.Id + " (tribe " + society.Player + ") at tile " + camp.Tile + ".");
        }

        // Largest-remainder split of the residents over gatherers and every assigned role. Farmers stay on the parent's fields
        // (the daughter has none), so its share of farmers become gatherers.
        private static void SplitWorkforce(Camp parent, Camp daughter, int residents, int take)
        {
            var ledger = parent.Workforce; var roles = ledger.Assignments.Where(a => a != null && a.Population > 0).OrderBy(a => a.Role, StringComparer.Ordinal).ToArray();
            var counts = new[] { ledger.Gatherers(residents) }.Concat(roles.Select(a => a.Population)).ToArray();
            long total = counts.Sum(c => (long)c);
            var share = counts.Select(c => total <= 0 ? 0 : (int)(take * (long)c / total)).ToArray();
            int remainder = take - share.Sum();
            foreach (int i in Enumerable.Range(0, counts.Length).OrderByDescending(i => total <= 0 ? 0 : take * (long)counts[i] % total).ThenBy(i => i))
                if (remainder > 0 && share[i] < counts[i]) { share[i]++; remainder--; }
            daughter.Workforce = new PopulationLedger();
            for (int i = 0; i < roles.Length; i++)
            {
                roles[i].Population -= share[i + 1];
                if (share[i + 1] > 0 && roles[i].Role != WorkforceRoles.Farmer) daughter.Workforce.Assignments.Add(new PopulationAssignment { Role = roles[i].Role, Population = share[i + 1] });
            }
            ledger.Normalize(residents - take); daughter.Workforce.Normalize(take);
        }
    }
}
