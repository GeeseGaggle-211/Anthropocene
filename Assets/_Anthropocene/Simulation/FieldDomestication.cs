using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class HerdContact
    {
        public int Society, Turns, LastTurn;
    }

    // One bounded selection per village, from the committed movement snapshot.
    // This is ecological attraction, not player knowledge or an ownership claim.
    public sealed class FieldDomestication
    {
        private sealed class Attraction
        {
            public Camp Camp;
            public int Field;
            public double Intensity;
        }
        private readonly Dictionary<int, List<Attraction>> targets = new Dictionary<int, List<Attraction>>();
        internal IEnumerable<(int Herd, int Society)> SelectedContacts => targets
            .SelectMany(pair => pair.Value.Select(t => (Herd: pair.Key, Society: t.Camp.SocietyId))).Distinct();

        public FieldDomestication(WorldState world, PlanningIndex index)
        {
            if (!world.Config.FieldDomesticationEnabled) return;
            foreach (var camp in world.Camps.OrderBy(c => c.Id))
            {
                if (!Eligible(camp)) continue;
                var field = world.Map.Radius(camp.Tile, 1)
                    .Where(n => world.Tiles[n].TraversableLand && world.Tiles[n].CultivationOperator == camp.Id)
                    .Select(n => new { Tile = n, Q = CultivationEconomy.Intensity(world.Config, index.Ecology(n).CultivationCapital) })
                    .OrderByDescending(f => f.Q).ThenBy(f => f.Tile).FirstOrDefault();
                if (field == null || field.Q <= 0) continue;
                var herd = index.VisibleHerds(world.Map.Radius(camp.Tile, world.Config.FieldDetectionRadius))
                    .Where(h => h.DomesticCamp < 0 && h.Animals > 0 && world.Config.GetSpecies(h.Species).Domesticable
                        && world.Config.GetSpecies(h.Species).CultivationAffinity)
                    // Keep the same observable herd while contact remains possible.
                    .OrderByDescending(h => h.FieldContacts?.Any(c => c.Society == camp.SocietyId && c.LastTurn == world.Turn - 1) == true)
                    .ThenBy(h => world.Map.Distance(camp.Tile, h.Tile)).ThenBy(h => h.Id).FirstOrDefault();
                if (herd == null) continue;
                if (!targets.TryGetValue(herd.Id, out var list)) targets.Add(herd.Id, list = new List<Attraction>());
                list.Add(new Attraction { Camp = camp, Field = field.Tile, Intensity = field.Q });
            }
        }

        private static bool Eligible(Camp camp) => camp.Population > 0 && camp.IsVillage
            && camp.People.Progression.Era == Era.Neolithic
            && (camp.Technology == Technology.Cultivation || camp.Technologies[(int)Technology.Cultivation]);

        // Binary species eligibility; q is the entire score amplitude. Distance
        // attenuates it without a new protection, food or habituation multiplier.
        public double Score(WorldState world, Herd herd, int candidate)
        {
            if (!targets.TryGetValue(herd.Id, out var list)) return 0;
            double score = 0;
            foreach (var target in list)
                score = Math.Max(score, target.Intensity / (1 + world.Map.Distance(candidate, target.Field)));
            return score;
        }

        // Close only: no same-turn claims, products, staffing or newborn needs.
        public void Resolve(WorldState world, TurnRecord record)
        {
            if (!world.Config.FieldDomesticationEnabled) return;
            foreach (var herd in world.Herds.OrderBy(h => h.Id))
            {
                if (herd.DomesticCamp >= 0) { herd.FieldContacts = null; continue; }
                List<HerdContact> contacts = null;
                if (herd.Animals > 0 && herd.LastHostileTurnPlusOne != world.Turn + 1
                    && targets.TryGetValue(herd.Id, out var list))
                {
                    foreach (var group in list.Where(t => Eligible(t.Camp)
                        && world.Map.Distance(t.Camp.Tile, herd.Tile) <= world.Config.FieldDetectionRadius
                        // Actual village sight, not a remote scout's or another
                        // settlement's shared Society visibility.
                        && world.Map.Distance(t.Camp.Tile, herd.Tile) <= world.Config.VisionRadius
                        && world.Tiles[t.Field].CultivationOperator == t.Camp.Id
                        && world.Tiles[t.Field].CultivationCapital > 0)
                        .GroupBy(t => t.Camp.SocietyId).OrderBy(g => g.Min(t => t.Camp.Id)))
                    {
                        var prior = herd.FieldContacts?.FirstOrDefault(c => c.Society == group.Key);
                        int turns = prior != null && prior.LastTurn == world.Turn - 1 ? prior.Turns + 1 : 1;
                        // Repeated Close calls at one boundary may not create extra contact.
                        if (prior != null && prior.LastTurn == world.Turn) turns = prior.Turns;
                        if (turns >= world.Config.FieldContactTurns)
                        {
                            var camp = group.OrderBy(t => t.Camp.Id).First().Camp;
                            TechnologyDiscovery.Acquire(world, camp.People, Technology.AnimalHusbandry, record, "sustained peaceful field contact");
                            herd.DomesticCamp = camp.Id; herd.OwnerSociety = camp.SocietyId;
                            herd.OpeningCompatibility = false;
                            record.Log("Society " + camp.SocietyId + " domesticated herd " + herd.Id + " (" + herd.Animals + " " + herd.Species + ") after " + turns + " peaceful contact turns.");
                            contacts = null;
                            break;
                        }
                        contacts ??= new List<HerdContact>();
                        contacts.Add(new HerdContact { Society = group.Key, Turns = turns, LastTurn = world.Turn });
                    }
                }
                // Missing sight, fields, village, selection or peace breaks the streak.
                herd.FieldContacts = contacts?.OrderBy(c => c.Society).ToList();
            }
        }
    }
}
