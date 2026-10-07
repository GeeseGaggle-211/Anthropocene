using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Physical acquisition, not a technology-unlock reward. Capture commits only
    // after this horizon's animal/human production. Wild animals must exist locally.
    public static class HusbandryAcquisition
    {
        public readonly struct Opportunity
        {
            public readonly int CampId, HerdId;
            public Opportunity(int campId, int herdId) { CampId = campId; HerdId = herdId; }
        }

        public static int ResolveTaming(WorldState world,
            IEnumerable<Opportunity> opportunities, TurnRecord record)
        {
            if (world == null || record == null || !world.Config.HusbandryTamingEnabled)
                return 0;
            var camps = world.Camps.ToDictionary(c => c.Id);
            var herds = world.Herds.ToDictionary(h => h.Id);
            var wildByTile = world.Herds.Where(h => h.DomesticCamp < 0 && h.Animals > 0)
                .ToLookup(h => h.Tile);
            var owned = world.Herds.Where(h => h.DomesticCamp >= 0)
                .GroupBy(h => h.DomesticCamp)
                .ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToList());
            var candidates = opportunities == null ? new List<Opportunity>() : opportunities.ToList();

            // Resident camps no longer depend on hunter detachments. Accumulated
            // species familiarity represents prior contact; actual capture still
            // needs a live nearby herd at this boundary. Seven local tile lookups,
            // not a full herd scan per settlement.
            foreach (var camp in world.Camps.Where(c => c.Population > 0 &&
                    c.People.Progression.Era == Era.Neolithic &&
                    CultivationEconomy.HusbandryEnabled(c)).OrderBy(c => c.Id))
                foreach (int tile in world.Map.Radius(camp.Tile, 1))
                    foreach (var herd in wildByTile[tile])
                        candidates.Add(new Opportunity(camp.Id, herd.Id));

            int captured = 0;
            foreach (var opportunity in candidates.GroupBy(o => (o.CampId, o.HerdId))
                .Select(g => g.First()).OrderBy(o => o.CampId).ThenBy(o => o.HerdId))
            {
                if (!camps.TryGetValue(opportunity.CampId, out var camp) ||
                    !herds.TryGetValue(opportunity.HerdId, out var wild)) continue;
                if (!owned.TryGetValue(camp.Id, out var domestic))
                    owned[camp.Id] = domestic = new List<Herd>();
                captured += Capture(world, camp, wild, domestic, record);
            }
            return captured;
        }

        internal static int TryTame(WorldState world, Camp camp, Herd wild, TurnRecord record)
        {
            if (world == null || camp == null) return 0;
            var owned = world.Herds.Where(h => h.DomesticCamp == camp.Id)
                .OrderBy(h => h.Id).ToList();
            return Capture(world, camp, wild, owned, record);
        }

        private static int Capture(WorldState world, Camp camp, Herd wild,
            List<Herd> domesticHerds, TurnRecord record)
        {
            if (wild == null || record == null || !world.Config.HusbandryTamingEnabled ||
                camp.Population <= 0 || camp.People.Progression.Era != Era.Neolithic ||
                wild.DomesticCamp >= 0 || wild.Animals <= 0 ||
                !CultivationEconomy.HusbandryEnabled(camp) ||
                world.Map.Distance(camp.Tile, wild.Tile) > 1) return 0;
            var species = world.Config.GetSpecies(wild.Species);
            if (!species.Domesticable) return 0;
            double familiarity = camp.People.SpeciesExperience
                .FirstOrDefault(p => p.Species == wild.Species)?.Experience ?? 0;
            if (familiarity + 1e-9 < world.Config.TamingSpeciesPracticeThreshold) return 0;

            // One small bootstrap herd per settlement, not five animals per
            // species. Once livestock has existed, the retained target wins:
            // a zero-sized retained herd is not permission to repeatedly refill
            // animals the economic planner deliberately liquidated.
            int held = domesticHerds.Sum(h => Math.Max(0, h.Animals));
            int target = Math.Max(0, world.Config.InitialDomesticAnimals);
            if (domesticHerds.Count > 0 && camp.HerdTargetSet)
                target = Math.Min(target, Math.Max(0, camp.HerdTarget));
            int needed = Math.Max(0, target - held);
            int reserve = Math.Min(wild.Animals, Math.Max(species.MinimumAnimals,
                (int)Math.Ceiling(wild.Animals * world.Config.HuntBreedingReserveFraction)));
            int captured = Math.Min(needed, Math.Max(0, wild.Animals - reserve));
            if (captured <= 0) return 0;

            double fraction = captured / (double)wild.Animals;
            double growth = wild.GrowthRemainder * fraction;
            double death = wild.DeathRemainder * fraction;
            wild.GrowthRemainder -= growth; wild.DeathRemainder -= death;
            wild.Animals -= captured;
            var domestic = domesticHerds.FirstOrDefault(h => h.Species == wild.Species);
            if (domestic == null)
            {
                domestic = new Herd
                {
                    Id = world.NextId++, Tile = camp.Tile, Species = wild.Species,
                    Animals = captured, Capacity = wild.Capacity,
                    DomesticCamp = camp.Id, OwnerSociety = camp.SocietyId,
                    GrowthRemainder = growth, DeathRemainder = death,
                    NutritionInitialized = true
                };
                world.Herds.Add(domestic); domesticHerds.Add(domestic);
            }
            else
            {
                domestic.Animals += captured;
                domestic.GrowthRemainder += growth; domestic.DeathRemainder += death;
            }
            record.Log("Camp " + camp.Id + " tamed " + captured + " " +
                species.Name + " from wild herd " + wild.Id + ".");
            return captured;
        }
    }
}
