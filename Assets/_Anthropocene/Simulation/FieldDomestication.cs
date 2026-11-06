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
            public HashSet<int> ContactTiles;
        }
        private readonly Dictionary<int, List<Attraction>> targets = new Dictionary<int, List<Attraction>>();
        internal IEnumerable<(int Herd, int Society)> SelectedContacts => targets
            .SelectMany(pair => pair.Value.Select(t => (Herd: pair.Key, Society: t.Camp.SocietyId))).Distinct();

        public FieldDomestication(WorldState world, PlanningIndex index)
        {
            if (!world.Config.FieldDomesticationEnabled) return;
            // Reuse flood scratch across villages; only selected contact footprints
            // survive the construction barrier for the immutable movement readers.
            var landSteps = new Dictionary<int, int>(); var queue = new Queue<int>();
            foreach (var camp in world.Camps.OrderBy(c => c.Id))
            {
                if (!Eligible(camp)) continue;
                var field = world.Map.Radius(camp.Tile, 1)
                    .Where(n => world.Tiles[n].TraversableLand && world.Tiles[n].CultivationOperator == camp.Id)
                    .Select(n => new { Tile = n, Q = CultivationEconomy.Intensity(world.Config, index.Ecology(n).CultivationCapital) })
                    .OrderByDescending(f => f.Q).ThenBy(f => f.Tile).FirstOrDefault();
                if (field == null || field.Q <= 0) continue;
                FillLandDistances(world, camp.Tile, world.Config.FieldDetectionRadius, landSteps, queue);
                var herd = index.VisibleHerds(landSteps.Keys)
                    .Where(h => h.DomesticCamp < 0 && h.Animals > 0 && world.Config.GetSpecies(h.Species).Domesticable
                        && world.Config.GetSpecies(h.Species).CultivationAffinity)
                    // Keep the same observable herd while contact remains possible.
                    .OrderByDescending(h => h.FieldContacts?.Any(c => c.Society == camp.SocietyId && c.LastTurn == world.Turn - 1) == true)
                    .ThenBy(h => landSteps[h.Tile]).ThenBy(h => h.Id).FirstOrDefault();
                if (herd == null) continue;
                if (!targets.TryGetValue(herd.Id, out var list)) targets.Add(herd.Id, list = new List<Attraction>());
                var contactTiles = new HashSet<int>(landSteps.Where(p => p.Value <= world.Config.FieldContactRadius).Select(p => p.Key));
                list.Add(new Attraction { Camp = camp, Field = field.Tile, Intensity = field.Q, ContactTiles = contactTiles });
            }
        }

        // Unit-cost shortest paths through the same traversable-land tiles used
        // by herd movement. Longitude wrapping comes from HexMap.Neighbors.
        internal static Dictionary<int, int> LandDistances(WorldState world, int origin, int radius)
        {
            var distances = new Dictionary<int, int>();
            FillLandDistances(world, origin, radius, distances, new Queue<int>());
            return distances;
        }

        private static void FillLandDistances(WorldState world, int origin, int radius, Dictionary<int, int> distances, Queue<int> queue)
        {
            distances.Clear(); queue.Clear();
            if (!world.Tiles[origin].TraversableLand) return;
            distances.Add(origin, 0); queue.Enqueue(origin);
            var map = world.Map;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue(), nextDistance = distances[current] + 1;
                if (nextDistance > radius) continue;
                for (int direction = 0; direction < 6; direction++)
                {
                    int next = map.Neighbor(current, direction);
                    if (next < 0 || !world.Tiles[next].TraversableLand || distances.ContainsKey(next)) continue;
                    distances.Add(next, nextDistance); queue.Enqueue(next);
                }
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
                // Admitted candidates are land neighbors of the selected,
                // reachable herd, so extending its path by one step is valid.
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
                        // Contact range is separate from player/scout vision.
                        // Terrain is fixed during resolution, so reuse the local
                        // pre-movement paths and test the herd's endpoint.
                        && t.ContactTiles.Contains(herd.Tile)
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
                // Missing reachable contact, fields, village, selection or peace breaks the streak.
                herd.FieldContacts = contacts?.OrderBy(c => c.Society).ToList();
            }
        }
    }
}
