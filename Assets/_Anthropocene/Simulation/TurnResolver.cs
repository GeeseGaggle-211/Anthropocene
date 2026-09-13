using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class Demand
    {
        public int Tile, Entity;
        public string Kind;
        public double Food, PotentialFood, Freshwater;
        public double ImpactMultiplier = 1;
    }
    public interface IAllocationPolicy { List<Transfer> Allocate(IEnumerable<Demand> demands, Tile[] tiles); }
    public sealed class ProportionalAllocation : IAllocationPolicy
    {
        public List<Transfer> Allocate(IEnumerable<Demand> demands, Tile[] tiles)
        {
            var result = new List<Transfer>();
            foreach (var group in demands.GroupBy(d => d.Tile).OrderBy(g => g.Key))
            {
                var sorted = group.OrderBy(d => d.Entity).ThenBy(d => d.Kind, StringComparer.Ordinal).ToArray();
                double food = sorted.Sum(d => d.Food), potential = sorted.Sum(d => d.PotentialFood), water = sorted.Sum(d => d.Freshwater);
                double supplyFood = Math.Min(tiles[group.Key].Food, food);
                // Human-accessible food is part of total biomass. Reserve the amount actually gathered before animals draw on the wider pool.
                double supplyPotential = Math.Min(Math.Max(0, tiles[group.Key].PotentialFood - supplyFood), potential);
                double supplyWater = Math.Min(tiles[group.Key].DrinkableWater, water);
                double leftFood = supplyFood, leftPotential = supplyPotential, leftWater = supplyWater;
                int lastFood = Array.FindLastIndex(sorted, d => d.Food > 0), lastPotential = Array.FindLastIndex(sorted, d => d.PotentialFood > 0), lastWater = Array.FindLastIndex(sorted, d => d.Freshwater > 0);
                for (int i = 0; i < sorted.Length; i++)
                {
                    var d = sorted[i];
                    double f = food <= 0 ? 0 : Math.Min(leftFood, supplyFood * d.Food / food);
                    double p = potential <= 0 ? 0 : Math.Min(leftPotential, supplyPotential * d.PotentialFood / potential);
                    double w = water <= 0 ? 0 : Math.Min(leftWater, supplyWater * d.Freshwater / water);
                    // Stable last claimant absorbs only floating-point rounding residue.
                    if (i == lastFood) f = leftFood; if (i == lastPotential) p = leftPotential; if (i == lastWater) w = leftWater;
                    leftFood -= f; leftPotential -= p; leftWater -= w;
                    result.Add(new Transfer { Tile = d.Tile, Entity = d.Entity, Kind = d.Kind, Food = f, PotentialFood = p, Freshwater = w });
                }
            }
            return result;
        }
    }
    public sealed partial class TurnResolver
    {

        public string Validate(WorldState w, int player, Order order, HashSet<int> visible = null) => OrderRules.Validate(new AgentObservationBuilder().Build(w, player), order);

        public TurnRecord Resolve(WorldState w, IReadOnlyDictionary<int, List<Order>> submitted, IEnumerable<DecisionTrace> traces = null)
        {
            Ownership.Bind(w);
            if (w.Finished) throw new InvalidOperationException("The Paleolithic opening has ended.");
            w.Config.Validate();
            var planning = new PlanningIndex(w);
            int peopleBefore = w.Camps.Sum(w.LinkedPopulation);
            var record = new TurnRecord { Turn = w.Turn, Seed = w.Seed, StartDays = w.Days, Duration = w.Config.TurnDays };
            double time = record.Duration / w.Config.BaseDays;
            if (traces != null) record.Decisions.AddRange(traces.OrderBy(t => t.Player));
            // Groups formed during this resolution cannot act until the next planning phase.
            var establishedHunters = new HashSet<int>(w.Hunters.Where(h => h.Population > 0).Select(h => h.Id));
            var locked = new List<Order>();
            foreach (var camp in w.Camps.GroupBy(c => c.Player).OrderBy(g => g.Key).Select(g => g.OrderBy(c => c.Id).First()))
            {
                var observation = new AgentObservationBuilder().Build(w, camp.Player, planning);
                if (!submitted.TryGetValue(camp.Player, out var orders)) continue;
                foreach (var group in orders.Where(o => o != null).GroupBy(o => o.Entity).OrderBy(g => g.Key))
                {
                    // Conflicting duplicates are all rejected, independent of submission order.
                    if (group.Count() != 1) { record.Log("Rejected conflicting orders for " + group.Key); continue; }
                    var order = group.First(); string error = OrderRules.Validate(observation, order);
                    record.Orders.Add(camp.Player + ":" + order + ":" + (error ?? "accepted"));
                    if (error != null) record.Log("Rejected " + order.Entity + ": " + error); else locked.Add(order.Copy());
                }
            }
            // Herd intentions are computed before dispatch or movement reveals new hunter positions.
            var herdMoves = w.Herds.OrderBy(h => h.Id).ToDictionary(h => h.Id, h => HerdDestination(w, h, planning));
            foreach (var order in locked.Where(o => o.Kind == OrderKind.Dispatch).OrderBy(o => o.Entity))
            {
                var camp = w.Camps.First(c => c.Id == order.Entity); camp.Population -= order.Population;
                var hunter = new Hunter { Id = w.NextId++, SourceCamp = camp.Id, SocietyId = camp.SocietyId, Player = camp.Player, Population = order.Population, Tile = camp.Tile };
                w.Hunters.Add(hunter); record.Log("Tribe " + camp.Player + " dispatched " + hunter.Population + " hunters [" + hunter.Id + "].");
            }
            Supply.ProvisionDepartures(w, locked, record);
            Move(w, locked, herdMoves, record);
            Supply.DepositReturns(w, record);
            var touched = new Dictionary<int, TileChange>();
            Action<int> touch = id => { if (!touched.ContainsKey(id)) touched[id] = new TileChange { Tile = id, BeforeFood = w.Tiles[id].Food, BeforePotentialFood = w.Tiles[id].PotentialFood, BeforeFreshwater = w.Tiles[id].Freshwater, BeforeLandCondition = w.Tiles[id].LandCondition, BeforeAquaticCondition = w.Tiles[id].AquaticCondition }; };
            ResolveEcology(w, locked, establishedHunters, record, touch);
            foreach (var order in locked.Where(o => o.Kind == OrderKind.Reintegrate).OrderBy(o => o.Entity))
            {
                var hunter = w.Hunters.FirstOrDefault(h => h.Id == order.Entity); if (hunter == null) continue;
                var camp = w.Camps.First(c => c.Id == hunter.SourceCamp);
                if (hunter.Tile != camp.Tile) { record.Log("Reintegration deferred: camp moved away from " + hunter.Id + "."); continue; }
                camp.Population += hunter.Population; w.Hunters.Remove(hunter); record.Log("Hunters " + hunter.Id + " rejoined their camp.");
            }
            w.Hunters.RemoveAll(h => h.Population <= 0);
            Ownership.Bind(w);
            foreach (var camp in w.Camps.OrderBy(c => c.Id)) Breakthrough(w, camp, record);
            foreach (var change in touched.Values.OrderBy(t => t.Tile))
            { change.AfterFood = w.Tiles[change.Tile].Food; change.AfterPotentialFood = w.Tiles[change.Tile].PotentialFood; change.AfterFreshwater = w.Tiles[change.Tile].Freshwater; change.AfterLandCondition = w.Tiles[change.Tile].LandCondition; change.AfterAquaticCondition = w.Tiles[change.Tile].AquaticCondition; record.Changes.Add(change); }
            // Untouched tiles follow the recorded configuration/clock regeneration rule; replay does not need world-sized snapshots each turn.
            Invariants(w, peopleBefore + record.Growth - record.Deaths, touched.Keys.Select(id => w.Tiles[id]));
            w.History.Add(record); w.Turn++;
            return record;
        }

        public static int HerdDestination(WorldState w, Herd herd, PlanningIndex index = null)
        {
            if (herd.DomesticCamp >= 0) return herd.Tile;
            var map = w.Map; index = index ?? new PlanningIndex(w); var hunters = index.VisibleHunters(map.Radius(herd.Tile, 3)).ToArray();
            bool threat = hunters.Any(h => map.Distance(herd.Tile, h.Tile) <= 1);
            var species = w.Config.GetSpecies(herd.Species);
            double foodDemand = herd.Animals * species.FoodConsumption * Math.Min(w.Config.TurnDays, w.Config.BaseDays) / w.Config.BaseDays;
            double waterDemand = herd.Animals * species.FreshwaterConsumption * Math.Min(w.Config.TurnDays, w.Config.BaseDays) / w.Config.BaseDays;
            bool thirsty = FreshwaterAccess.Available(w, herd.Tile, index) < waterDemand || herd.UnmetFreshwater > waterDemand * .3;
            bool hungry = index.Ecology(herd.Tile).PotentialFood < foodDemand || herd.UnmetFood > foodDemand * .3;
            var candidates = map.Radius(herd.Tile, 1).Where(n => w.Tiles[n].TraversableLand).ToArray();
            if (candidates.Length == 0) return herd.Tile;
            if (threat)
                return candidates.OrderByDescending(n => hunters.Min(h => map.Distance(n, h.Tile)))
                    .ThenByDescending(n => Knowledge.Band(FreshwaterAccess.Available(w, n, index), 70, w.Config))
                    .ThenByDescending(n => Knowledge.Band(index.Ecology(n).PotentialFood, Math.Max(1, w.Ecology(n).MaxPotentialFood), w.Config))
                    .ThenBy(n => n).First();
            if (!thirsty && !hungry && Numbers.Random(w.Seed, 71, herd.Id, w.Turn) >= w.Config.WanderChance) return herd.Tile;
            return candidates.OrderByDescending(n => (thirsty ? FreshwaterAccess.Available(w, n, index) / Math.Max(1, waterDemand) : hungry ? index.Ecology(n).PotentialFood / Math.Max(1, foodDemand) : Numbers.Random(w.Seed, 72, herd.Id ^ n, w.Turn)) + species.Suitability(w.Tiles[n]) * species.MovementHabitatWeight)
                .ThenBy(n => n == herd.Tile ? 0 : 1).ThenBy(n => n).First();
        }
        private static void Move(WorldState w, List<Order> orders, Dictionary<int, int> herds, TurnRecord record)
        {
            var destinations = w.Camps.ToDictionary(c => c.Id, c => c.Tile);
            foreach (var order in orders.Where(o => o.Destination >= 0))
                if (destinations.ContainsKey(order.Entity)) destinations[order.Entity] = order.Destination;
            // Stationary camp has priority, then lowest ID. Roll back losers and repeat for cascading blocked moves.
            bool changed;
            do
            {
                changed = false;
                foreach (var group in w.Camps.GroupBy(c => destinations[c.Id]).Where(g => g.Count() > 1).ToArray())
                {
                    var winner = group.OrderBy(c => c.Tile == group.Key ? 0 : 1).ThenBy(c => c.Id).First();
                    foreach (var loser in group.Where(c => c != winner))
                        if (destinations[loser.Id] != loser.Tile) { destinations[loser.Id] = loser.Tile; changed = true; record.Log("Camp " + loser.Id + " movement blocked by another camp."); }
                }
            } while (changed);
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                int target = destinations[camp.Id];
                if (target != camp.Tile) { camp.Moves++; camp.StationaryTurns = 0; record.Log("Camp " + camp.Id + " moved from " + camp.Tile + " to " + target + "."); }
                else camp.StationaryTurns++;
                camp.Tile = target;
            }
            foreach (var hunter in w.Hunters.OrderBy(h => h.Id))
            {
                var order = orders.FirstOrDefault(o => o.Entity == hunter.Id);
                if (order != null && order.Destination >= 0) { record.Log("Hunters " + hunter.Id + " moved to " + order.Destination + "."); hunter.Tile = order.Destination; }
            }
            foreach (var herd in w.Herds.OrderBy(h => h.Id))
            {
                int target = herd.DomesticCamp >= 0 ? w.Camps.First(c => c.Id == herd.DomesticCamp).Tile : herds[herd.Id];
                if (herd.Tile != target) record.Log("Herd " + herd.Id + " moved to " + target + "."); herd.Tile = target;
            }
        }
        private static void Hunt(WorldState w, List<Order> orders, HashSet<int> establishedHunters, IReadOnlyDictionary<int, int> startingHerdPopulation, TurnRecord record, double time)
        {
            double practiceTime = time * w.Config.BaseDays / w.Config.PracticeDays;
            var requests = new List<Tuple<Hunter, Herd, int>>();
            var committedElsewhere = new HashSet<int>(orders.Where(o => o.Kind == OrderKind.Fish || o.Kind == OrderKind.Reintegrate).Select(o => o.Entity));
            foreach (var hunter in w.Hunters.Where(h => establishedHunters.Contains(h.Id) && h.Population > 0 && !committedElsewhere.Contains(h.Id)).OrderBy(h => h.Id))
            {
                var herd = w.Herds.Where(h => h.DomesticCamp < 0 && h.Tile == hunter.Tile)
                    .OrderByDescending(h => Math.Max(0, h.Animals - Math.Max(w.Config.GetSpecies(h.Species).MinimumAnimals, (int)Math.Ceiling(h.Animals * w.Config.HuntBreedingReserveFraction))))
                    .ThenByDescending(h => h.Animals).ThenBy(h => h.Id).FirstOrDefault();
                if (herd == null) continue;
                var camp = w.Camps.First(c => c.Id == hunter.SourceCamp);
                var practice = camp.SpeciesExperience.FirstOrDefault(p => p.Species == herd.Species);
                if (practice == null) { practice = new SpeciesPractice { Species = herd.Species }; camp.SpeciesExperience.Add(practice); }
                practice.Experience += hunter.Population * practiceTime;
                double efficiency = w.Config.HuntEfficiency * (w.Tiles[hunter.Tile].Terrain == Terrain.Woodland ? .85 : 1);
                double expectation = hunter.Population * efficiency * time * (1 - w.Config.HuntVariance + Numbers.Random(w.Seed, 80, hunter.Id, w.Turn) * w.Config.HuntVariance * 2);
                int requested = (int)expectation + (Numbers.Random(w.Seed, 81, hunter.Id, w.Turn) < expectation % 1 ? 1 : 0);
                double fieldcraft = PracticeEffects.Ramp(w.Config, camp, Domain.Hunting);
                if (fieldcraft > 0)
                {
                    double yieldPerAnimal = w.Config.GetSpecies(herd.Species).FoodYield * PracticeEffects.HuntingYield(w.Config, camp);
                    double usefulFood;
                    if (Supply.Connected(w, hunter))
                    {
                        double generationNeed = w.LinkedPopulation(camp) * w.Config.Consumption * time;
                        usefulFood = Math.Max(0, generationNeed * (1 + StorageGenerations(w, camp.Tile)) - camp.Food);
                    }
                    else usefulFood = Math.Max(0, Supply.Capacity(w, hunter) - hunter.CarriedFood);
                    requested = Math.Min(requested, (int)Math.Ceiling(usefulFood / Math.Max(1e-9, yieldPerAnimal)));
                }
                requests.Add(Tuple.Create(hunter, herd, requested));
            }
            foreach (var group in requests.GroupBy(r => r.Item2.Id).OrderBy(g => g.Key))
            {
                var sorted = group.OrderBy(r => r.Item1.Id).ToArray(); var herd = sorted[0].Item2;
                var species = w.Config.GetSpecies(herd.Species);
                int breedingReserve = Math.Min(herd.Animals, Math.Max(species.MinimumAnimals, (int)Math.Ceiling(herd.Animals * w.Config.HuntBreedingReserveFraction)));
                int huntingPopulation = sorted.Sum(r => r.Item1.Population);
                int protectedPopulation = breedingReserve;
                if (huntingPopulation <= w.Config.SustainableHuntPopulation && startingHerdPopulation.TryGetValue(herd.Id, out int startingAnimals))
                {
                    int naturalCeiling = Math.Min(herd.Capacity, herd.Animals);
                    int retainedGrowth = (int)Math.Ceiling(Math.Max(0, naturalCeiling - startingAnimals) * w.Config.SustainableHerdGrowthRetention);
                    protectedPopulation = Math.Max(protectedPopulation, Math.Min(herd.Capacity, startingAnimals + retainedGrowth));
                }
                int total = sorted.Sum(r => r.Item3), available = Math.Min(total, Math.Max(0, herd.Animals - protectedPopulation));
                int[] kills = sorted.Select(r => total == 0 ? 0 : (int)((long)available * r.Item3 / total)).ToArray();
                int remainder = available - kills.Sum();
                foreach (int i in Enumerable.Range(0, sorted.Length).OrderByDescending(i => total == 0 ? 0 : (long)available * sorted[i].Item3 % total).ThenBy(i => sorted[i].Item1.Id))
                    if (remainder > 0 && kills[i] < sorted[i].Item3) { kills[i]++; remainder--; }
                for (int i = 0; i < sorted.Length; i++)
                {
                    var hunter = sorted[i].Item1; var camp = w.Camps.First(c => c.Id == hunter.SourceCamp); double yield = kills[i] * species.FoodYield * PracticeEffects.HuntingYield(w.Config, camp);
                    Supply.Award(w, hunter, yield, record);
                    record.Transfers.Add(new Transfer { Entity = hunter.Id, Tile = hunter.Tile, Kind = "Hunting", Food = yield });
                    record.Log("Hunters " + hunter.Id + " killed " + kills[i] + " animals from herd " + herd.Id + ".");
                }
                herd.Animals -= available;
                if (available < total) record.Log("Herd " + herd.Id + " retained " + herd.Animals + " breeding animals after the generation's harvest.");
                if (huntingPopulation <= w.Config.SustainableHuntPopulation && herd.Animals > startingHerdPopulation[herd.Id]) record.Log("Limited hunting pressure allowed herd " + herd.Id + " to keep growing.");
            }
            w.Herds.RemoveAll(h => h.Animals <= 0);
        }
        public static void Breakthrough(WorldState w, Camp camp, TurnRecord record)
        {
            Ownership.Bind(w);
            if (w.LinkedPopulation(camp) <= 0) return;
            var progression = camp.People.Progression;
            if (progression.Era != Era.Paleolithic) return;
            double elapsed = w.Days - progression.EraStartDays;
            int best = Enumerable.Range(0, 3).OrderByDescending(i => camp.Experience[i]).ThenBy(i => i).First();
            double total = camp.Experience.Sum();
            if (camp.Technology == Technology.None)
            {
                bool threshold = camp.Experience[best] >= w.Config.ExperienceThreshold;
                bool majority = (elapsed >= progression.MajorityEligibilityDays && elapsed < progression.ForcedTransitionDays) && camp.Experience[best] > total * .5;
                if (!threshold && !majority && elapsed < progression.ForcedTransitionDays) return;
                camp.ForcedTransition = elapsed >= progression.ForcedTransitionDays && !threshold && !majority;
                camp.Technology = (Technology)(best + 1); camp.TransitionTurn = w.Turn;
                if (camp.ForcedTransition)
                {
                    int second = Enumerable.Range(0, 3).Where(i => i != best).OrderByDescending(i => camp.Experience[i]).ThenBy(i => i).First();
                    camp.SecondaryDomain = second;
                    // Keep accumulated experience intact; carryover is stored as progress separately at transition display/save.
                }
                progression.Era = Era.Neolithic; progression.CompletionDays = w.Days; progression.CompletionTurn = w.Turn; progression.CompletionRule = threshold ? "Mastery" : majority ? "Maturity" : "Forced";
                OpeningTransitions.Apply(w, camp, camp.Technology); record.Log("Tribe " + camp.Player + " discovered " + camp.Technology + " and entered the Neolithic.");
            }
        }
        public static double NeolithicProgress(WorldState w, Camp camp, Domain domain)
        {
            double experience = camp.Experience[(int)domain];
            return camp.ForcedTransition && camp.SecondaryDomain == (int)domain ? Math.Max(w.Config.CarryoverFloor, experience * w.Config.CarryoverMultiplier) : experience;
        }
        public static void Invariants(WorldState w, int expectedPeople = -1, IEnumerable<Tile> ecologyScope = null)
        {
            if (w.Camps.Select(c => c.Tile).Distinct().Count() != w.Camps.Count) throw new InvalidOperationException("Rival camps overlap.");
            var ids = w.Camps.Select(c => c.Id).Concat(w.Hunters.Select(h => h.Id)).Concat(w.Herds.Select(h => h.Id)).ToArray();
            if (ids.Distinct().Count() != ids.Length || ids.Any(i => i >= w.NextId)) throw new InvalidOperationException("Invalid stable IDs.");
            if (expectedPeople >= 0 && w.Camps.Sum(w.LinkedPopulation) != expectedPeople) throw new InvalidOperationException("Unexplained population change.");
            if (w.Camps.Any(c => c.Population < 0 || c.Food < -1e-8 || c.DemographicGrowthMultiplier <= 0 || double.IsNaN(c.DemographicGrowthMultiplier) || c.Workforce == null || c.Workforce.Assignments.Any(a => a == null || string.IsNullOrWhiteSpace(a.Role) || a.Population <= 0) || c.Workforce.Assignments.GroupBy(a => a.Role, StringComparer.Ordinal).Any(g => g.Count() != 1) || c.Workforce.Assigned > c.Population || !w.CanEnter(c.Tile, false)) || w.Hunters.Any(h => h.Population < 0 || !w.CanEnter(h.Tile, true, w.Camps.First(c => c.Id == h.SourceCamp))))
                throw new InvalidOperationException("Invalid people state or location.");
            if (w.Hunters.Any(h => h.CarriedFood < -1e-8 || h.CarriedFood > Supply.Capacity(w, h) + 1e-8) || w.Camps.Any(c => c.GrowthRemainder < 0 || c.GrowthRemainder >= 1)) throw new InvalidOperationException("Invalid carried provisions or demographic remainder.");
            if (w.Herds.Any(h => !w.Tiles[h.Tile].TraversableLand || h.Animals < 0)) throw new InvalidOperationException("Invalid herd population or peak/aquatic location.");
            foreach (var tile in ecologyScope ?? w.Tiles)
            {
                var projected = LazyEcology.ProjectEcology(w, tile.Id, w.Days);
                var ecology = w.Ecology(tile.Id); double multiplier = OpeningTransitions.FoodMultiplier(w, tile);
                if (tile.WaterBody == WaterKind.Saltwater && (tile.Freshwater > 0 || ecology.MaxFreshwater > 0 || tile.FreshwaterSource != FreshwaterSource.None)) throw new InvalidOperationException("Seawater is not drinkable.");
                if (tile.Saltwater < 0 || tile.Saltwater > ecology.MaxSaltwater + 1e-6 || tile.FreshwaterSource == FreshwaterSource.None && tile.Freshwater > 1e-8) throw new InvalidOperationException("Water stock lacks a valid source or capacity.");
                if (double.IsNaN(projected.LandCondition) || double.IsNaN(projected.AquaticCondition) || projected.LandCondition < -1e-8 || projected.LandCondition > 1 + 1e-8 || projected.AquaticCondition < -1e-8 || projected.AquaticCondition > 1 + 1e-8 || double.IsNaN(projected.Food) || double.IsInfinity(projected.Food) || projected.Food < -1e-8 || double.IsNaN(projected.PotentialFood) || double.IsInfinity(projected.PotentialFood) || projected.PotentialFood + 1e-8 < projected.Food || projected.PotentialFood > ecology.MaxPotentialFood * multiplier + 1e-5 || projected.Freshwater < -1e-8 || double.IsNaN(projected.Freshwater) ||
                    projected.Food > ecology.MaxFood * multiplier + 1e-6 || projected.Freshwater > ecology.MaxFreshwater + 1e-6)
                    throw new InvalidOperationException("Invalid ecology or capacity overflow.");
            }
        }
    }
}
