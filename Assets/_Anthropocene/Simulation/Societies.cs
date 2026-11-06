using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public enum Era { Paleolithic, Neolithic }
    public enum ImprovementKind { CultivationSeed, FisherySeed }

    [Serializable]
    public sealed class EraProgression
    {
        public Era Era;
        public double EraStartDays, MajorityEligibilityDays = 73000, ForcedTransitionDays = 87600;
        public double CompletionDays = -1;
        public int CompletionTurn;
        public string CompletionRule;
    }

    [Serializable]
    public sealed class Society
    {
        public int Id, Player, PolicySeed;
        public double[] Experience = new double[3];
        // Practice credited after this Society left the Paleolithic, by Domain (TechnologyDiscovery.AddPractice). Secondary technologies
        // are earned from it; Experience (lifetime practice) still drives the first breakthrough. Absent in older saves: zeros.
#pragma warning disable SYSLIB0050
        [System.Runtime.Serialization.OptionalField] public double[] NeolithicExperience = new double[3];
#pragma warning restore SYSLIB0050
        public List<SpeciesPractice> SpeciesExperience = new List<SpeciesPractice>();
        public Technology Technology;
        public bool[] Technologies = new bool[4];
        public int TransitionTurn, SecondaryDomain = -1;
        public bool ForcedTransition;
        public Mandate Mandate = new Mandate();
        public PolicyState Policy = new PolicyState();
        public EraProgression Progression = new EraProgression();
        public List<TransitionSeed> TransitionSeeds = new List<TransitionSeed>();
        // The initialized bit distinguishes older saves from an intentional false.
#pragma warning disable SYSLIB0050
        [System.Runtime.Serialization.OptionalField] public bool OpeningTransitionProcessed;
        [System.Runtime.Serialization.OptionalField] public bool OpeningTransitionProcessedInitialized = true;
#pragma warning restore SYSLIB0050

        // Neolithic Craft Tracks & Convergence Techs
#pragma warning disable SYSLIB0050 // OptionalField permits older DataContractSerializer metadata.
        [System.Runtime.Serialization.OptionalField] public Dictionary<string, double> CraftExperience = new Dictionary<string, double>();
        [System.Runtime.Serialization.OptionalField] public List<string> UnlockedSpecialists = new List<string>();
        [System.Runtime.Serialization.OptionalField] public List<string> NeolithicTechnologies = new List<string>();
#pragma warning restore SYSLIB0050
        // Remembered explored geography and settlement contacts (WP6). Null before the first breakthrough and in older saves.
        [System.Runtime.Serialization.OptionalField] public ContactMemory Memory;
        // Owner-only visited Fishing-ground economics, stored sparsely by tile.
        // Absent in old saves: an empty memory list is installed by save migration.
        [System.Runtime.Serialization.OptionalField] public List<FishingGroundSnapshot> FishingGroundKnowledge = new List<FishingGroundSnapshot>();
        // Lineage (WP10a): the parent Society (0 = none) and the affinity fixed by the parent camp's fission policy at fission. Inert afterwards.
        [System.Runtime.Serialization.OptionalField] public int ParentSociety;
        [System.Runtime.Serialization.OptionalField] public double KinAffinity;
        public double NeolithicPractice(Domain domain) => NeolithicExperience == null ? 0 : NeolithicExperience[(int)domain];
        public bool HasTech(string tech) => NeolithicTechnologies != null && NeolithicTechnologies.Contains(tech);
    }

    [Serializable]
    public sealed class TransitionSeed
    {
        public Technology Technology;
        public int Settlement, Tile;
        public string Species;
        public double Days;
    }

    [Serializable]
    public sealed class Improvement
    {
        public int Id, Tile, Society, Settlement;
        public ImprovementKind Kind;
        public bool OpeningCompatibility;
    }

    public static class Ownership
    {
        // Explicit construction/load boundary. Never call from observation or tile projection.
        public static void Bind(WorldState world)
        {
            var byId = world.Societies.ToDictionary(s => s.Id); var byPlayer = world.Societies.GroupBy(s => s.Player).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).First());
            var camps = world.Camps.ToDictionary(c => c.Id);
            foreach (var camp in world.Camps.OrderBy(c => c.Id))
            {
                Society society; if (camp.SocietyId > 0) byId.TryGetValue(camp.SocietyId, out society); else byPlayer.TryGetValue(camp.Player, out society);
                if (society == null)
                {
                    society = camp.People;
                    society.Id = world.NextId++; society.Player = camp.Player;
                    society.PolicySeed = (int)Numbers.Hash(world.Seed, 190, camp.Tile);
                    society.Policy = PolicyState.Seeded(society.PolicySeed);
                    society.Progression.MajorityEligibilityDays = world.Config.MajorityEligibilityYears * world.Config.DaysPerYear;
                    society.Progression.ForcedTransitionDays = world.Config.ForcedTransitionYears * world.Config.DaysPerYear;
                    world.Societies.Add(society); byId[society.Id] = society; byPlayer[society.Player] = society;
                }
                camp.Bind(society);
            }
            world.Societies.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var hunter in world.Hunters) hunter.SocietyId = camps[hunter.SourceCamp].SocietyId;
            foreach (var herd in world.Herds.Where(h => h.DomesticCamp >= 0)) herd.OwnerSociety = camps[herd.DomesticCamp].SocietyId;
        }
    }

    public static class OpeningTransitions
    {
        public static void Apply(WorldState w, Camp camp, Technology technology)
        {
            var society = camp.People;
            if (society.OpeningTransitionProcessed) return;
            society.OpeningTransitionProcessed = true;
            society.OpeningTransitionProcessedInitialized = true;
            society.Technologies[(int)technology] = true;
            string species = technology == Technology.AnimalHusbandry ? w.Config.Species.Where(s => s.Domesticable)
                .OrderByDescending(s => society.SpeciesExperience.FirstOrDefault(p => p.Species == s.Id)?.Experience ?? 0)
                .ThenBy(s => s.Id, StringComparer.Ordinal).First().Id : null;
            society.TransitionSeeds.Add(new TransitionSeed { Technology = technology, Settlement = camp.Id, Tile = camp.Tile, Species = species, Days = w.Days });
            if (!w.Config.OpeningCompatibilityEffects) return;
            // Temporary visible opening reward, not a domestication or cultivation simulation.
            if (technology == Technology.AnimalHusbandry)
                w.Herds.Add(new Herd { Id = w.NextId++, Tile = camp.Tile, DomesticCamp = camp.Id, OwnerSociety = society.Id, OpeningCompatibility = true, Species = species, Animals = w.Config.InitialDomesticAnimals });
            else
            {
                // Resident Fishing develops sparse fishery capital through work;
                // retain the historical opening FisherySeed only in feature-off worlds.
                if (technology == Technology.Watercraft && w.Config.NeolithicFishingEnabled) return;
                int tile = w.Map.Radius(camp.Tile, 1).Where(n => technology == Technology.Cultivation ? w.Tiles[n].TraversableLand && w.Ecology(n).Moisture >= w.Config.CultivationMoisture : w.Tiles[n].Terrain == Terrain.Shallows)
                    .OrderByDescending(n => w.Ecology(n).MaxFood).ThenBy(n => n).DefaultIfEmpty(-1).First();
                if (tile < 0) return;
                w.Improvements.Add(new Improvement { Id = w.NextId++, Tile = tile, Society = society.Id, Settlement = camp.Id, Kind = technology == Technology.Cultivation ? ImprovementKind.CultivationSeed : ImprovementKind.FisherySeed, OpeningCompatibility = true });
                if (technology == Technology.Cultivation) w.Tiles[tile].Cultivated = true; else w.Tiles[tile].Fishery = true;
            }
        }
        public static double FoodMultiplier(WorldState w, Tile tile) => w.Config.OpeningCompatibilityEffects && tile.Fishery ? w.Config.OpeningFoodMultiplier : 1;
    }

    // Event-driven handoff from the mobile opening to resident production.
    // Technologies are permanent, so unlock boundaries are the only place
    // that needs to identify the already-existing detachments to recall.
    public static class EconomicTransition
    {
        public static bool Enabled(WorldState world, Society society)
        {
            if (world == null || society == null) return false;
            bool Has(Technology technology) => society.Technology == technology ||
                society.Technologies != null && society.Technologies.Length > (int)technology &&
                society.Technologies[(int)technology];
            return Has(Technology.Cultivation) || Has(Technology.AnimalHusbandry) ||
                world.Config.NeolithicFishingEnabled && Has(Technology.Watercraft);
        }

        public static int Begin(WorldState world, Society society)
        {
            var sourceCamps = world.Camps
                .Where(camp => camp.SocietyId == society.Id)
                .Select(camp => camp.Id).ToHashSet();
            int marked = 0;
            foreach (var hunter in world.Hunters)
            {
                if (hunter.Population <= 0 || !sourceCamps.Contains(hunter.SourceCamp) ||
                    hunter.EconomicTransitionRecall) continue;
                hunter.EconomicTransitionRecall = true;
                hunter.EconomicTransitionRoute = new List<int> { hunter.Tile };
                hunter.EconomicTransitionVisited = new HashSet<int> { hunter.Tile };
                marked++;
            }
            return marked;
        }

        // Apply one visible-search step. The normal path is a stack, so both
        // discovery and backtracking are O(1). A movement-bounded final hop to
        // the source camp may cover more than one adjacent tile.
        internal static void Advance(WorldState world, Hunter hunter, Camp source, int destination)
        {
            var route = hunter.EconomicTransitionRoute;
            var visited = hunter.EconomicTransitionVisited;
            if (source == null || source.Id != hunter.SourceCamp || route == null || route.Count == 0 ||
                route[route.Count - 1] != hunter.Tile || visited == null)
            {
                Cancel(hunter);
                return;
            }
            if (destination == source.Tile)
            {
                visited.Add(destination);
                route.Add(destination);
                return;
            }
            if (world.Map.Distance(hunter.Tile, destination) != 1)
            {
                Cancel(hunter);
                return;
            }
            int last = route.Count - 1;
            if (last > 0 && route[last - 1] == destination)
            {
                route.RemoveAt(last);
                return;
            }
            if (!visited.Add(destination))
            {
                Cancel(hunter);
                return;
            }
            route.Add(destination);
        }

        internal static void Cancel(Hunter hunter)
        {
            hunter.EconomicTransitionRecall = false;
            hunter.EconomicTransitionRoute = null;
            hunter.EconomicTransitionVisited = null;
        }
    }

    // A pure, owned-society view shared by player presentation and AI observations.
    // No map query or turn-count deadline participates in subsequent learning.
    public struct TechnologyStatus
    {
        public Domain Domain;
        public Technology Technology;
        public double Experience, Progress, Threshold;
        public bool Acquired, Eligible;
        public string BlockedReason;
    }

    // Where a practice credit came from. Amounts are decided by the caller; the source only labels the receipt.
    public enum PracticeSource { Gathering, Hunting, Fishing, Trade }

    public static class TechnologyDiscovery
    {
        // Lifetime Experience always accrues (it drives the first breakthrough and the
        // practice bonuses); after the Society leaves the Paleolithic the same credit also accrues to NeolithicExperience, which alone
        // earns secondary technologies. Pure arithmetic on the owning Society; callers fix the amount and the canonical order.
        private static void AccruePractice(Society society, Domain domain, double amount)
        {
            if (amount == 0 || double.IsNaN(amount) || double.IsInfinity(amount)) return;
            society.Experience[(int)domain] += amount;
            if (society.Progression.Era == Era.Paleolithic) return;
            if (society.NeolithicExperience == null) society.NeolithicExperience = new double[3];
            society.NeolithicExperience[(int)domain] += amount;
        }

        // Authoritative simulation entry point. Practice mutates only its owner,
        // then publishes a threshold crossing to the world's sparse Close queue.
        // Permanent planner capabilities therefore change only on an unlock
        // event; Close never scans every Society and technology combination.
        public static void AddPractice(WorldState world, Society society, Domain domain,
            double amount, PracticeSource source)
        {
            AccruePractice(society, domain, amount);
            if (world != null && amount != 0 && !double.IsNaN(amount) && !double.IsInfinity(amount))
                QueueEligible(world, society, domain);
        }

        // Trade hook (inert until trade exists, TradePracticePerValue = 0). A receiving Society learns from what it imports: the caller
        // passes the goods' value (the same value units the exchange used) and the domain the good belongs to, for example imported
        // grain to Gathering (Cultivation), herd products to Hunting (Husbandry) and fish to Fishing (Watercraft). The credit is
        // goodsValue * TradePracticePerValue and flows through AddPractice like any other practice. Returns the points credited.
        public static double RecordTradePractice(WorldState world, Society receiver, Domain domain, double goodsValue)
        {
            double amount = goodsValue * world.Config.TradePracticePerValue;
            if (!(amount > 0)) return 0;
            AddPractice(world, receiver, domain, amount, PracticeSource.Trade);
            return amount;
        }

        // Progress toward a technology. For a Society already in the Neolithic this is its post-breakthrough practice, except for
        // the forced-transition Society's secondary domain, which keeps the catch-up rule on lifetime practice exactly as before.
        // Before the first breakthrough it is lifetime practice.
        public static double EffectiveProgress(Society society, Domain domain, SimulationConfig config)
        {
            if (society.ForcedTransition && society.SecondaryDomain == (int)domain) return LifetimeProgress(society, domain, config);
            return society.Progression.Era == Era.Paleolithic ? society.Experience[(int)domain] : society.NeolithicPractice(domain);
        }

        // Lifetime progress of a domain: lifetime practice with the forced-transition catch-up on the selected secondary domain.
        private static double LifetimeProgress(Society society, Domain domain, SimulationConfig config)
        {
            double experience = society.Experience[(int)domain];
            return society.ForcedTransition && society.SecondaryDomain == (int)domain
                ? Math.Max(config.CarryoverFloor, experience * config.CarryoverMultiplier) : experience;
        }

        public static TechnologyStatus[] Statuses(SimulationConfig config, Society society)
        {
            var status = new TechnologyStatus[3];
            for (int i = 0; i < status.Length; i++)
            {
                var domain = (Domain)i;
                var technology = (Technology)(i + 1);
                bool inEra = society.Progression.Era == Era.Neolithic;
                bool acquired = society.Technologies[(int)technology] || inEra && society.Technology == technology;
                // An acquired technology keeps showing lifetime practice; an unacquired one shows the progress that can earn it.
                double progress = acquired ? LifetimeProgress(society, domain, config) : EffectiveProgress(society, domain, config);
                double threshold = inEra ? config.SecondaryTechnologyThreshold : config.ExperienceThreshold;
                status[i] = new TechnologyStatus
                {
                    Domain = domain, Technology = technology, Experience = inEra && !acquired ? society.NeolithicPractice(domain) : society.Experience[i],
                    Progress = progress, Threshold = threshold,
                    Acquired = acquired, Eligible = !acquired && inEra && progress >= threshold,
                    BlockedReason = acquired ? null : !inEra ? "First breakthrough required" : progress < threshold
                        ? domain == Domain.Hunting ? "More Hunting practice required" : domain == Domain.Gathering ? "More Gathering practice required" : "More Fishing practice required" : null
                };
            }
            return status;
        }

        public static TechnologyStatus[] Statuses(WorldState world, Society society) => Statuses(world.Config, society);

        // Explicit bridge for first breakthroughs, migrations/tests, and other
        // boundary code that installs progress without a practice receipt.
        public static void QueueEligible(WorldState world, Society society)
        {
            if (world == null || society == null) return;
            for (int i = 0; i < 3; i++) QueueEligible(world, society, (Domain)i);
        }

        public static void QueueEligible(WorldState world, Society society, Domain domain)
        {
            if (world == null || society == null || society.Progression.Era != Era.Neolithic) return;
            int i = (int)domain;
            if (i < 0 || i >= 3) return;
            var technology = (Technology)(i + 1);
            if (society.Technologies[(int)technology] || society.Technology == technology ||
                EffectiveProgress(society, domain, world.Config) < world.Config.SecondaryTechnologyThreshold) return;
            world.PendingTechnologyUnlocks ??= new Dictionary<int, int>();
            world.PendingTechnologyUnlocks.TryGetValue(society.Id, out int pending);
            world.PendingTechnologyUnlocks[society.Id] = pending | 1 << i;
        }

        // Shared Close effect. Physical animal acquisition remains the caller's responsibility.
        internal static void Acquire(WorldState world, Society society, Technology technology, TurnRecord record, string source)
        {
            if (society.Technologies[(int)technology] || society.Technology == technology) return;
            bool economyBefore = EconomicTransition.Enabled(world, society);
            society.Technologies[(int)technology] = true;
            if (!economyBefore && EconomicTransition.Enabled(world, society)) EconomicTransition.Begin(world, society);
            record.Log("Society " + society.Id + " discovered " + technology + " through " + source + ".");
        }

        // Called only at Close, after all realized food/practice receipts have been credited.
        public static void Resolve(WorldState world, TurnRecord record)
        {
            var pending = world.PendingTechnologyUnlocks;
            if (pending == null || pending.Count == 0) return;
            var societies = world.Societies.ToDictionary(society => society.Id);
            foreach (var entry in pending.OrderBy(pair => pair.Key))
            {
                if (!societies.TryGetValue(entry.Key, out var society) || society.Progression.Era != Era.Neolithic) continue;
                bool active = world.Camps.Any(camp => camp.SocietyId == society.Id &&
                    (camp.Population > 0 || world.Hunters.Any(hunter => hunter.SourceCamp == camp.Id && hunter.Population > 0)));
                if (!active) continue;
                for (int i = 0; i < 3; i++)
                {
                    if ((entry.Value & 1 << i) == 0) continue;
                    var technology = (Technology)(i + 1);
                    if (society.Technologies[(int)technology] || society.Technology == technology || EffectiveProgress(society, (Domain)i, world.Config) < world.Config.SecondaryTechnologyThreshold) continue;
                    Acquire(world, society, technology, record, (Domain)i + " practice");
                }
            }
            pending.Clear();
        }
    }
}
