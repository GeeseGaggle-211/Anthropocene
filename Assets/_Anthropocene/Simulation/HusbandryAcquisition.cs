using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Physical livestock acquisition for a Society that already knows Husbandry.
    // Knowledge never creates animals: taming transfers living animals out of a
    // nearby wild herd into an owned managed herd.
    public static class HusbandryAcquisition
    {
        public static int TryTame(WorldState world, Camp camp, Herd wild, TurnRecord record)
        {
            if (world == null || camp == null || wild == null || record == null ||
                !world.Config.HusbandryTamingEnabled || wild.DomesticCamp >= 0 ||
                wild.Animals <= 0 || !CultivationEconomy.HusbandryEnabled(camp) ||
                world.Map.Distance(camp.Tile, wild.Tile) > 1)
                return 0;

            var species = world.Config.GetSpecies(wild.Species);
            if (!species.Domesticable) return 0;
            double practice = camp.People.SpeciesExperience
                .FirstOrDefault(p => p.Species == wild.Species)?.Experience ?? 0;
            if (practice + 1e-9 < world.Config.TamingSpeciesPracticeThreshold) return 0;

            int owned = world.Herds.Where(h => h.DomesticCamp == camp.Id &&
                    h.Species == wild.Species && h.Animals > 0)
                .Sum(h => h.Animals);
            int target = Math.Max(1, world.Config.InitialDomesticAnimals);
            int needed = Math.Max(0, target - owned);
            if (needed <= 0) return 0;

            int reserve = Math.Min(wild.Animals, Math.Max(species.MinimumAnimals,
                (int)Math.Ceiling(wild.Animals * world.Config.HuntBreedingReserveFraction)));
            int available = Math.Max(0, wild.Animals - reserve);
            int captured = Math.Min(needed, available);
            if (captured <= 0) return 0;

            wild.Animals -= captured;
            var domestic = world.Herds.Where(h => h.DomesticCamp == camp.Id &&
                    h.Species == wild.Species)
                .OrderBy(h => h.Id).FirstOrDefault();
            if (domestic == null)
            {
                domestic = new Herd
                {
                    Id = world.NextId++, Tile = camp.Tile, Species = wild.Species,
                    Animals = captured, Capacity = wild.Capacity,
                    DomesticCamp = camp.Id, OwnerSociety = camp.SocietyId,
                    NutritionInitialized = true
                };
                world.Herds.Add(domestic);
            }
            else domestic.Animals += captured;

            record.Log("Camp " + camp.Id + " tamed " + captured + " " +
                species.Name + " from wild herd " + wild.Id + ".");
            return captured;
        }
    }
}
