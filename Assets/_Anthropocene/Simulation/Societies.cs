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
        public static double FoodMultiplier(WorldState w, Tile tile) => w.Config.OpeningCompatibilityEffects && (tile.Cultivated || tile.Fishery) ? w.Config.OpeningFoodMultiplier : 1;
    }
}
