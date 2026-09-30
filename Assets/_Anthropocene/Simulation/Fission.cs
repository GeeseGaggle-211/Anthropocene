using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Anthropocene.Simulation
{
    // Per-camp fission policy (QoLFissionTradeDesign 3.6). The enum's zero is Neutral so that an absent saved field means Neutral.
    public enum FissionPolicy { Neutral = 0, Discourage = 1, Encourage = 2 }
    public enum FissionRegime { Paleolithic = 0, Nomadic = 1, Agricultural = 2 }
    public enum KinRelation { Parent, Daughter, Sibling }

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
        public static double QoLShift(FissionPolicy policy) => policy == FissionPolicy.Discourage ? -.05 : policy == FissionPolicy.Encourage ? .05 : 0;
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
    // tile projections); only a camp with push above FissionSearchPush and no valid cache searches (at most the ring at
    // distance FissionRadius, each candidate one footprint). Reconcile and commit are proportional to proposals. Nothing scales
    // with elapsed years, map size or the number of Societies.
    public static class Fission
    {
        public const int PolicySeedStream = 500;
        private static readonly FieldInfo[] InventoryFields = typeof(CampInventory).GetFields().Where(f => f.FieldType == typeof(double)).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();

        public static double Push(SimulationConfig config, double qol, FissionPolicy policy)
            => Numbers.Clamp((config.FissionQoL + Kinship.QoLShift(policy) - qol) / config.FissionQoLWidth, 0, 1);
        public static FissionRegime RegimeOf(Camp camp)
            => camp.Technology == Technology.None ? FissionRegime.Paleolithic : CultivationEconomy.Enabled(camp) ? FissionRegime.Agricultural : FissionRegime.Nomadic;

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
            public double Push, Desirability, Score;
            public int Daughter, Tile = -1;
            public bool ClearTarget, Propose;
            public FissionTarget NewTarget;
            public int Validations, Searches, Candidates, TileProjections, Invalidations;
        }

        public static void Resolve(WorldState w, TurnRecord record, PhaseExecution execution, ResolutionMetrics metrics)
        {
            if (!w.Config.FissionEnabled) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            // Frozen post-demography snapshot: nothing below the parallel map writes to the world.
            var camps = w.Camps.Where(c => c.Population > 0).OrderBy(c => c.Id).ToArray();
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
                if (!e.Propose) continue;
                metrics.FissionProposals++;
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
            }
            metrics.FissionMilliseconds = timer.Elapsed.TotalMilliseconds;
        }

        private static Evaluation Evaluate(Context context, Camp camp)
        {
            var w = context.World; var cfg = w.Config; var e = new Evaluation { Camp = camp };
            var regime = RegimeOf(camp); var policy = camp.FissionPolicy; var cached = camp.FissionDestination;
            // A change of subsistence regime invalidates the cached destination regardless of the push.
            if (cached != null && cached.Regime != (int)regime) { e.ClearTarget = true; e.Invalidations++; cached = null; }
            e.Push = Push(cfg, camp.QoL, policy);
            int residents = camp.Population, daughter = Kinship.DaughterSize(residents, policy);
            e.Daughter = daughter;
            // The daughter is a smaller group than the parent that remains and meets the minimum viable population.
            if (e.Push <= 0 || daughter < cfg.FissionMinimumDaughter || daughter >= residents - daughter || residents - daughter < cfg.MinimumCamp) return e;
            double viable = cfg.StartViability / 100.0; var projections = new Dictionary<int, double>();
            if (cached != null)
            {
                e.Validations++;
                double d = context.Blocked(cached.Tile) || !w.Tiles[cached.Tile].TraversableLand ? 0 : Desirability(context, camp, cached.Tile, daughter, regime, projections, e);
                if (d < viable) { e.ClearTarget = true; e.Invalidations++; cached = null; }
                else e.Desirability = d;
            }
            if (cached == null && e.Push > cfg.FissionSearchPush)
            {
                e.Searches++;
                var known = new Known(context, camp);
                int best = -1; double bestScore = -1;
                foreach (int tile in w.Map.Radius(camp.Tile, cfg.FissionRadius).OrderBy(n => n))
                {
                    if (!w.Tiles[tile].TraversableLand || !known.Contains(tile) || context.Blocked(tile)) continue;
                    e.Candidates++;
                    double d = Desirability(context, camp, tile, daughter, regime, projections, e);
                    if (d > bestScore) { best = tile; bestScore = d; }
                }
                if (best >= 0 && bestScore >= viable)
                {
                    e.NewTarget = new FissionTarget { Tile = best, Regime = (int)regime, Turn = w.Turn }; e.ClearTarget = false;
                    e.Desirability = bestScore; cached = e.NewTarget;
                }
            }
            if (cached == null) return e;
            e.Tile = cached.Tile; e.Score = e.Push * e.Desirability;
            e.Propose = e.Score > cfg.FissionThreshold;
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
        private static double Desirability(Context context, Camp camp, int tile, int daughter, FissionRegime regime, Dictionary<int, double> projections, Evaluation e)
        {
            var w = context.World; var cfg = w.Config; var index = context.Index;
            double Repeatable(int n)
            {
                if (projections.TryGetValue(n, out double cachedValue)) return cachedValue;
                var t = w.Tiles[n]; var now = index.Ecology(n); var view = now;
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
            if (regime == FissionRegime.Agricultural)
            {
                double q = cfg.Neolithic.PrimitiveAgricultureSupportedIntensity, managed = 0;
                foreach (int n in w.Map.Radius(tile, 1))
                {
                    var t = w.Tiles[n];
                    if (!t.TraversableLand || t.Environment == EnvironmentKind.Ice || !t.Seasons.Any(s => s.MaxPotentialFood > 0)) continue;
                    double flow = 0;
                    for (int s = 0; s < 4; s++) flow += EcologyMath.ManagedRegenerationFlow(cfg, q, t.Seasons[s].MaxPotentialFood, t.Seasons[s].BiomassRecoveryPerDay, 1);
                    managed += flow / 4 * cfg.TurnDays;
                }
                double support = Math.Min(land + aquatic + managed, daughter * cfg.FarmerProductivity * cfg.GatherPerPerson * cfg.GenerationIntervals);
                return Numbers.Clamp(support / need, 0, 1);
            }
            double food = Numbers.Clamp(harvest / need, 0, 1);
            if (regime != FissionRegime.Nomadic) return food;
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
                forage += biomass / 4 * cfg.TurnDays; water += recharge / 4 * cfg.TurnDays;
            }
            double dForage = forageNeed <= 0 ? 1 : Numbers.Clamp((forage - harvest) / forageNeed, 0, 1), dWater = waterNeed <= 0 ? 1 : Numbers.Clamp(water / waterNeed, 0, 1);
            return Math.Min(food, Math.Min(dForage, dWater));
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
                Experience = (double[])ps.Experience.Clone(), SpeciesExperience = ps.SpeciesExperience.Select(p => new SpeciesPractice { Species = p.Species, Experience = p.Experience }).ToList(),
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
                Turn = w.Turn, ParentCamp = parent.Id, DaughterCamp = camp.Id, ParentSociety = ps.Id, DaughterSociety = society.Id, DaughterPlayer = society.Player, Tile = camp.Tile, ParentBefore = before, Daughter = n, Regime = (int)RegimeOf(parent),
                AnimalsMoved = animals, Policy = parent.FissionPolicy, Push = e.Push, Desirability = e.Desirability, Score = e.Score, Affinity = society.KinAffinity
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
