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
        // Lineage (WP10a): the parent Society (0 = none) and the affinity fixed by the parent camp's fission policy at fission. Inert afterwards.
        [System.Runtime.Serialization.OptionalField] public int ParentSociety;
        [System.Runtime.Serialization.OptionalField] public double KinAffinity;
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
                int tile = w.Map.Radius(camp.Tile, 1).Where(n => technology == Technology.Cultivation ? w.Tiles[n].TraversableLand && w.Ecology(n).Moisture >= w.Config.CultivationMoisture : w.Tiles[n].Terrain == Terrain.Shallows)
                    .OrderByDescending(n => w.Ecology(n).MaxFood).ThenBy(n => n).DefaultIfEmpty(-1).First();
                if (tile < 0) return;
                w.Improvements.Add(new Improvement { Id = w.NextId++, Tile = tile, Society = society.Id, Settlement = camp.Id, Kind = technology == Technology.Cultivation ? ImprovementKind.CultivationSeed : ImprovementKind.FisherySeed, OpeningCompatibility = true });
                if (technology == Technology.Cultivation) w.Tiles[tile].Cultivated = true; else w.Tiles[tile].Fishery = true;
            }
        }
        public static double FoodMultiplier(WorldState w, Tile tile) => w.Config.OpeningCompatibilityEffects && tile.Fishery ? w.Config.OpeningFoodMultiplier : 1;
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

    public static class TechnologyDiscovery
    {
        public static double EffectiveProgress(Society society, Domain domain, SimulationConfig config)
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
                double progress = EffectiveProgress(society, domain, config);
                bool inEra = society.Progression.Era == Era.Neolithic;
                bool acquired = society.Technologies[(int)technology] || inEra && society.Technology == technology;
                status[i] = new TechnologyStatus
                {
                    Domain = domain, Technology = technology, Experience = society.Experience[i],
                    Progress = progress, Threshold = config.ExperienceThreshold,
                    Acquired = acquired, Eligible = !acquired && inEra && progress >= config.ExperienceThreshold,
                    BlockedReason = acquired ? null : !inEra ? "First breakthrough required" : progress < config.ExperienceThreshold
                        ? domain == Domain.Hunting ? "More Hunting practice required" : domain == Domain.Gathering ? "More Gathering practice required" : "More Fishing practice required" : null
                };
            }
            return status;
        }

        public static TechnologyStatus[] Statuses(WorldState world, Society society) => Statuses(world.Config, society);

        // Called only at Close, after all realized food/practice receipts have been credited.
        public static void Resolve(WorldState world, TurnRecord record)
        {
            var hunterSources = new HashSet<int>(world.Hunters.Where(h => h.Population > 0).Select(h => h.SourceCamp));
            var active = new HashSet<int>(world.Camps.Where(c => c.Population > 0 || hunterSources.Contains(c.Id)).Select(c => c.SocietyId));
            // Ownership.Bind sorted societies at the resolution boundary.
            foreach (var society in world.Societies)
            {
                if (!active.Contains(society.Id) || society.Progression.Era != Era.Neolithic) continue;
                for (int i = 0; i < 3; i++)
                {
                    var technology = (Technology)(i + 1);
                    if (society.Technologies[(int)technology] || society.Technology == technology || EffectiveProgress(society, (Domain)i, world.Config) < world.Config.ExperienceThreshold) continue;
                    society.Technologies[(int)technology] = true;
                    record.Log("Society " + society.Id + " discovered " + technology + " through " + (Domain)i + " practice.");
                }
            }
        }
    }
}
