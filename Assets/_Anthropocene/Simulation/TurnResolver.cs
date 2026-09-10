using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public sealed class Demand
    {
        public int Tile, Entity;
        public string Kind;
        public double Food, Freshwater;
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
                double food = sorted.Sum(d => d.Food), water = sorted.Sum(d => d.Freshwater);
                double supplyFood = Math.Min(tiles[group.Key].Food, food), supplyWater = Math.Min(tiles[group.Key].DrinkableWater, water);
                double leftFood = supplyFood, leftWater = supplyWater;
                for (int i = 0; i < sorted.Length; i++)
                {
                    var d = sorted[i];
                    double f = food <= 0 ? 0 : Math.Min(leftFood, supplyFood * d.Food / food);
                    double w = water <= 0 ? 0 : Math.Min(leftWater, supplyWater * d.Freshwater / water);
                    // Stable last claimant absorbs only floating-point rounding residue.
                    if (i == sorted.Length - 1) { if (d.Food > 0) f = leftFood; if (d.Freshwater > 0) w = leftWater; }
                    leftFood -= f; leftWater -= w;
                    result.Add(new Transfer { Tile = d.Tile, Entity = d.Entity, Kind = d.Kind, Food = f, Freshwater = w });
                }
            }
            return result;
        }
    }
    public sealed partial class TurnResolver
    {
        private readonly IAllocationPolicy allocation;
        public TurnResolver(IAllocationPolicy allocation = null) { this.allocation = allocation ?? new ProportionalAllocation(); }

        public string Validate(WorldState w, int player, Order order, HashSet<int> visible = null)
        {
            if (order == null) return "Missing order.";
            var camp = w.Camps.FirstOrDefault(c => c.Id == order.Entity && c.Player == player);
            var hunter = w.Hunters.FirstOrDefault(h => h.Id == order.Entity && h.Player == player);
            if (camp == null && hunter == null) return "Entity is not controlled by this tribe.";
            var source = camp ?? w.Camps.First(c => c.Id == hunter.SourceCamp);
            if (w.LinkedPopulation(source) == 0) return "The tribe has no surviving people.";
            visible = visible ?? Knowledge.Visible(w, player);
            int tile = camp != null ? camp.Tile : hunter.Tile;
            if (order.Kind == OrderKind.NoOp) return null;
            if (order.Kind == OrderKind.Dispatch)
                return camp == null || order.Population <= 0 || camp.Population - order.Population < w.Config.MinimumCamp ? "Dispatch must preserve the camp's minimum population." : null;
            if (order.Kind == OrderKind.Reintegrate)
                return hunter == null || order.Target != source.Id || hunter.Tile != source.Tile ? "Hunters must be at their source camp to reintegrate." : null;
            if (order.Kind == OrderKind.Move || order.Destination >= 0)
            {
                if (!visible.Contains(order.Destination)) return "Destination is outside current sight.";
                int budget = hunter != null ? w.Config.HunterMovement : w.Config.CampMovement;
                if (!w.Map.Reachable(tile, order.Destination, budget, n => visible.Contains(n) && w.CanEnter(n, hunter != null, source),
                    n => hunter != null && w.Tiles[n].Aquatic ? Math.Max(1, w.Config.HunterMovement / w.Config.WaterMovement) : 1))
                    return "Destination is unreachable within this turn's movement allowance.";
            }
            if (order.Kind == OrderKind.Move) return null;
            if (hunter == null) return "This order requires a hunter group.";
            if (order.Kind == OrderKind.Hunt)
            {
                var herd = w.Herds.FirstOrDefault(h => h.Id == order.Target && h.DomesticCamp < 0);
                return herd == null || !visible.Contains(herd.Tile) ? "Hunt target is not a currently visible wild herd." : null;
            }
            if (order.Kind == OrderKind.Fish)
            {
                int final = order.Destination >= 0 ? order.Destination : tile;
                return !visible.Contains(order.Target) || !w.Tiles[order.Target].Aquatic || w.Map.Distance(final, order.Target) > 1 ? "Fishing requires an accessible adjacent freshwater or shallow tile." : null;
            }
            return "Unknown order.";
        }

        public TurnRecord Resolve(WorldState w, IReadOnlyDictionary<int, List<Order>> submitted, IEnumerable<DecisionTrace> traces = null)
        {
            if (w.Finished || w.Turn > 12) throw new InvalidOperationException("The Paleolithic opening has ended.");
            w.Config.Validate();
            int peopleBefore = w.Camps.Sum(w.LinkedPopulation);
            var record = new TurnRecord { Turn = w.Turn, Seed = w.Seed, StartDays = w.Days, Duration = w.Config.TurnDays };
            double time = record.Duration / w.Config.BaseDays;
            if (traces != null) record.Decisions.AddRange(traces.OrderBy(t => t.Player));
            var locked = new List<Order>();
            foreach (var camp in w.Camps.OrderBy(c => c.Player))
            {
                var visible = Knowledge.Visible(w, camp.Player);
                if (!submitted.TryGetValue(camp.Player, out var orders)) continue;
                foreach (var group in orders.Where(o => o != null).GroupBy(o => o.Entity).OrderBy(g => g.Key))
                {
                    // Conflicting duplicates are all rejected, independent of submission order.
                    if (group.Count() != 1) { record.Log("Rejected conflicting orders for " + group.Key); continue; }
                    var order = group.First(); string error = Validate(w, camp.Player, order, visible);
                    record.Orders.Add(camp.Player + ":" + order + ":" + (error ?? "accepted"));
                    if (error != null) record.Log("Rejected " + order.Entity + ": " + error); else locked.Add(order.Copy());
                }
            }
            // Herd intentions are computed before dispatch or movement reveals new hunter positions.
            var herdMoves = w.Herds.OrderBy(h => h.Id).ToDictionary(h => h.Id, h => HerdDestination(w, h));
            foreach (var order in locked.Where(o => o.Kind == OrderKind.Dispatch).OrderBy(o => o.Entity))
            {
                var camp = w.Camps.First(c => c.Id == order.Entity); camp.Population -= order.Population;
                var hunter = new Hunter { Id = w.NextId++, SourceCamp = camp.Id, Player = camp.Player, Population = order.Population, Tile = camp.Tile };
                w.Hunters.Add(hunter); record.Log("Tribe " + camp.Player + " dispatched " + hunter.Population + " hunters [" + hunter.Id + "].");
            }
            Supply.ProvisionDepartures(w, locked, record);
            Move(w, locked, herdMoves, record);
            Supply.DepositReturns(w, record);
            var touched = new Dictionary<int, TileChange>();
            Action<int> touch = id => { if (!touched.ContainsKey(id)) touched[id] = new TileChange { Tile = id, BeforeFood = w.Tiles[id].Food, BeforeFreshwater = w.Tiles[id].Freshwater }; };
            ResolveEcology(w, locked, record, touch);
            foreach (var order in locked.Where(o => o.Kind == OrderKind.Reintegrate).OrderBy(o => o.Entity))
            {
                var hunter = w.Hunters.FirstOrDefault(h => h.Id == order.Entity); if (hunter == null) continue;
                var camp = w.Camps.First(c => c.Id == hunter.SourceCamp);
                if (hunter.Tile != camp.Tile) { record.Log("Reintegration deferred: camp moved away from " + hunter.Id + "."); continue; }
                camp.Population += hunter.Population; w.Hunters.Remove(hunter); record.Log("Hunters " + hunter.Id + " rejoined their camp.");
            }
            w.Hunters.RemoveAll(h => h.Population <= 0);
            foreach (var camp in w.Camps.OrderBy(c => c.Id)) Breakthrough(w, camp, record);
            foreach (var change in touched.Values.OrderBy(t => t.Tile))
            { change.AfterFood = w.Tiles[change.Tile].Food; change.AfterFreshwater = w.Tiles[change.Tile].Freshwater; record.Changes.Add(change); }
            // Untouched tiles follow the recorded configuration/clock regeneration rule; replay does not need world-sized snapshots each turn.
            Invariants(w, peopleBefore + record.Growth - record.Deaths);
            w.History.Add(record); w.Turn++;
            return record;
        }

        public static int HerdDestination(WorldState w, Herd herd)
        {
            if (herd.DomesticCamp >= 0) return herd.Tile;
            var map = w.Map; var hunters = w.Hunters.OrderBy(h => h.Id).ToArray();
            bool threat = hunters.Any(h => map.Distance(herd.Tile, h.Tile) <= 1);
            var species = w.Config.GetSpecies(herd.Species);
            double foodDemand = herd.Animals * species.FoodConsumption * Math.Min(w.Config.TurnDays, w.Config.BaseDays) / w.Config.BaseDays;
            double waterDemand = herd.Animals * species.FreshwaterConsumption * Math.Min(w.Config.TurnDays, w.Config.BaseDays) / w.Config.BaseDays;
            bool thirsty = FreshwaterAccess.Available(w, herd.Tile) < waterDemand || herd.UnmetFreshwater > waterDemand * .3;
            bool hungry = w.Tiles[herd.Tile].Food < foodDemand || herd.UnmetFood > foodDemand * .3;
            var candidates = map.Radius(herd.Tile, 1).Where(n => w.Tiles[n].TraversableLand).ToArray();
            if (candidates.Length == 0) return herd.Tile;
            if (threat)
                return candidates.OrderByDescending(n => hunters.Min(h => map.Distance(n, h.Tile)))
                    .ThenByDescending(n => Math.Min(1, FreshwaterAccess.Available(w, n) / Math.Max(1e-9, waterDemand)))
                    .ThenByDescending(n => Math.Min(1, w.Tiles[n].Food / Math.Max(1e-9, foodDemand)))
                    .ThenBy(n => Numbers.Hash(w.Seed, 70, herd.Id ^ n, w.Turn)).ThenBy(n => n).First();
            if (!thirsty && !hungry && Numbers.Random(w.Seed, 71, herd.Id, w.Turn) >= w.Config.WanderChance) return herd.Tile;
            return candidates.OrderByDescending(n => (thirsty ? FreshwaterAccess.Available(w, n) / Math.Max(1, waterDemand) : hungry ? w.Tiles[n].Food / Math.Max(1, foodDemand) : Numbers.Random(w.Seed, 72, herd.Id ^ n, w.Turn)) + species.Suitability(w.Tiles[n]) * species.MovementHabitatWeight)
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
        private static void Hunt(WorldState w, List<Order> orders, TurnRecord record, double time)
        {
            double practiceTime = time * w.Config.BaseDays / w.Config.PracticeDays;
            var requests = new List<Tuple<Hunter, Herd, int>>();
            foreach (var order in orders.Where(o => o.Kind == OrderKind.Hunt).OrderBy(o => o.Entity))
            {
                var hunter = w.Hunters.First(h => h.Id == order.Entity); var camp = w.Camps.First(c => c.Id == hunter.SourceCamp);
                var herd = w.Herds.FirstOrDefault(h => h.Id == order.Target);
                camp.Experience[(int)Domain.Hunting] += hunter.Population * practiceTime;
                if (herd != null)
                {
                    var practice = camp.SpeciesExperience.FirstOrDefault(p => p.Species == herd.Species);
                    if (practice == null) { practice = new SpeciesPractice { Species = herd.Species }; camp.SpeciesExperience.Add(practice); }
                    practice.Experience += hunter.Population * practiceTime;
                }
                if (herd == null || hunter.Tile != herd.Tile) { record.Log("Hunters " + hunter.Id + " lost contact; hunting practice recorded."); continue; }
                double efficiency = w.Config.HuntEfficiency * (w.Tiles[hunter.Tile].Terrain == Terrain.Woodland ? .85 : 1);
                double expectation = hunter.Population * efficiency * time * (1 - w.Config.HuntVariance + Numbers.Random(w.Seed, 80, hunter.Id, w.Turn) * w.Config.HuntVariance * 2);
                int requested = (int)expectation + (Numbers.Random(w.Seed, 81, hunter.Id, w.Turn) < expectation % 1 ? 1 : 0);
                requests.Add(Tuple.Create(hunter, herd, requested));
            }
            foreach (var group in requests.GroupBy(r => r.Item2.Id).OrderBy(g => g.Key))
            {
                var sorted = group.OrderBy(r => r.Item1.Id).ToArray(); var herd = sorted[0].Item2;
                var species = w.Config.GetSpecies(herd.Species);
                int breedingReserve = Math.Min(herd.Animals, Math.Max(species.MinimumAnimals, (int)Math.Ceiling(herd.Animals * w.Config.HuntBreedingReserveFraction)));
                int total = sorted.Sum(r => r.Item3), available = Math.Min(total, Math.Max(0, herd.Animals - breedingReserve));
                int[] kills = sorted.Select(r => total == 0 ? 0 : (int)((long)available * r.Item3 / total)).ToArray();
                int remainder = available - kills.Sum();
                foreach (int i in Enumerable.Range(0, sorted.Length).OrderByDescending(i => total == 0 ? 0 : (long)available * sorted[i].Item3 % total).ThenBy(i => sorted[i].Item1.Id))
                    if (remainder > 0 && kills[i] < sorted[i].Item3) { kills[i]++; remainder--; }
                for (int i = 0; i < sorted.Length; i++)
                {
                    var hunter = sorted[i].Item1; var camp = w.Camps.First(c => c.Id == hunter.SourceCamp); double yield = kills[i] * species.FoodYield;
                    Supply.Award(w, hunter, yield, record); camp.Experience[(int)Domain.Hunting] += yield * w.Config.HarvestExperienceWeight;
                    record.Transfers.Add(new Transfer { Entity = hunter.Id, Tile = hunter.Tile, Kind = "Hunting", Food = yield });
                    record.Log("Hunters " + hunter.Id + " killed " + kills[i] + " animals from herd " + herd.Id + ".");
                }
                herd.Animals -= available;
                if (available < total) record.Log("Herd " + herd.Id + " retained " + herd.Animals + " breeding animals after the generation's harvest.");
            }
            w.Herds.RemoveAll(h => h.Animals <= 0);
        }
        private void CommitResources(WorldState w, List<Demand> demands, TurnRecord record, Action<int> touch)
        {
            var herds = w.Herds.ToDictionary(h => h.Id); var camps = w.Camps.ToDictionary(c => c.Id); var hunters = w.Hunters.ToDictionary(h => h.Id);
            foreach (var transfer in allocation.Allocate(demands, w.Tiles))
            {
                touch(transfer.Tile); var tile = w.Tiles[transfer.Tile];
                tile.Food = Math.Max(0, tile.Food - transfer.Food); tile.Freshwater = Math.Max(0, tile.Freshwater - transfer.Freshwater);
                if (transfer.Kind == "Herd")
                {
                    var herd = herds[transfer.Entity];
                    herd.UnmetFood = Math.Max(0, herd.UnmetFood - transfer.Food); herd.UnmetFreshwater = Math.Max(0, herd.UnmetFreshwater - transfer.Freshwater);
                }
                else
                {
                    var camp = transfer.Kind == "Fishing" ? camps[hunters[transfer.Entity].SourceCamp] : camps[transfer.Entity];
                    if (transfer.Kind == "Fishing") Supply.Award(w, hunters[transfer.Entity], transfer.Food, record);
                    else { camp.Food += transfer.Food; camp.StepFoodIncome += transfer.Food; }
                    camp.Experience[transfer.Kind == "Gathering" ? (int)Domain.Gathering : (int)Domain.Fishing] += transfer.Food * w.Config.HarvestExperienceWeight * Math.Min(1, w.Config.BaseDays / w.Config.PracticeDays);
                }
                record.Transfers.Add(transfer);
            }
        }
        private static void Sustain(WorldState w, TurnRecord record, double time)
        {
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var linked = w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).OrderBy(h => h.Id).ToArray();
                int total = camp.Population + linked.Sum(h => h.Population); double required = total * w.Config.Consumption * time;
                camp.LastConsumed = Math.Min(required, camp.Food); camp.Food -= camp.LastConsumed; camp.FoodDeficit = required - camp.LastConsumed;
                if (camp.FoodDeficit > 1e-8) { camp.DeficitTurns++; camp.DeficitSeverity += camp.FoodDeficit / Math.Max(1, required) * time; }
                else { camp.DeficitTurns = 0; camp.DeficitSeverity = 0; }
                if (camp.DeficitTurns > w.Config.StarvationGraceTurns)
                {
                    double lossProgress = camp.DeathRemainder + total * Numbers.Fraction(w.Config.Mortality, time) * camp.FoodDeficit / Math.Max(1e-9, required);
                    int losses = Math.Min(total, (int)Math.Floor(lossProgress)); camp.DeathRemainder = lossProgress - Math.Floor(lossProgress);
                    // Largest-remainder apportionment preserves conservation and avoids always sacrificing hunters first.
                    int[] counts = new[] { camp.Population }.Concat(linked.Select(h => h.Population)).ToArray();
                    int[] deaths = counts.Select(n => total == 0 ? 0 : (int)((long)n * losses / total)).ToArray();
                    int remainder = losses - deaths.Sum();
                    foreach (int i in Enumerable.Range(0, counts.Length).OrderByDescending(i => total == 0 ? 0 : (long)counts[i] * losses % total).ThenBy(i => i))
                        if (remainder > 0 && deaths[i] < counts[i]) { deaths[i]++; remainder--; }
                    camp.Population -= deaths[0]; for (int i = 0; i < linked.Length; i++) linked[i].Population -= deaths[i + 1];
                    record.PopulationChanges.Add(new PopulationChange { Camp = camp.Id, Entity = camp.Id, Kind = "Starvation", Count = -deaths[0] });
                    for (int i = 0; i < linked.Length; i++) record.PopulationChanges.Add(new PopulationChange { Camp = camp.Id, Entity = linked[i].Id, Kind = "Starvation", Count = -deaths[i + 1] });
                    record.Deaths += losses; record.Log("Tribe " + camp.Player + " lost " + losses + " people to sustained hunger.");
                }
                foreach (var h in linked) { h.DeficitTurns = camp.DeficitTurns; h.FoodDeficit = camp.FoodDeficit * h.Population / Math.Max(1, total); h.LastConsumed = Math.Max(0, h.Population * w.Config.Consumption * time - h.FoodDeficit); }
            }
            foreach (var hunter in w.Hunters.Where(h => !Supply.Connected(w, h)).OrderBy(h => h.Id))
            {
                double required = hunter.Population * w.Config.Consumption * time;
                hunter.LastConsumed = Math.Min(required, hunter.CarriedFood); hunter.CarriedFood -= hunter.LastConsumed; hunter.FoodDeficit = required - hunter.LastConsumed;
                if (hunter.FoodDeficit > 1e-8) { hunter.DeficitTurns++; hunter.DeficitSeverity += hunter.FoodDeficit / Math.Max(1, required) * time; }
                else { hunter.DeficitTurns = 0; hunter.DeficitSeverity = 0; }
                if (hunter.DeficitTurns > w.Config.StarvationGraceTurns)
                {
                    double lossProgress = hunter.DeathRemainder + hunter.Population * Numbers.Fraction(w.Config.Mortality, time) * hunter.FoodDeficit / Math.Max(1e-9, required);
                    int deaths = Math.Min(hunter.Population, (int)Math.Floor(lossProgress)); hunter.DeathRemainder = lossProgress - Math.Floor(lossProgress);
                    hunter.Population -= deaths; record.Deaths += deaths;
                    record.PopulationChanges.Add(new PopulationChange { Camp = hunter.SourceCamp, Entity = hunter.Id, Kind = "Starvation", Count = -deaths });
                    record.Log("Expedition " + hunter.Id + " lost " + deaths + " people to hunger beyond supply range.");
                }
            }
        }
        public static void Breakthrough(WorldState w, Camp camp, TurnRecord record)
        {
            if (w.LinkedPopulation(camp) <= 0) return;
            int best = Enumerable.Range(0, 3).OrderByDescending(i => camp.Experience[i]).ThenBy(i => i).First();
            double total = camp.Experience.Sum();
            if (camp.Technology == Technology.None)
            {
                bool threshold = camp.Experience[best] >= w.Config.ExperienceThreshold;
                bool majority = (w.Turn == 10 || w.Turn == 11) && camp.Experience[best] > total * .5;
                if (!threshold && !majority && w.Turn < 12) return;
                camp.ForcedTransition = w.Turn == 12 && !threshold && !majority;
                camp.Technology = (Technology)(best + 1); camp.TransitionTurn = w.Turn;
                if (camp.ForcedTransition)
                {
                    int second = Enumerable.Range(0, 3).Where(i => i != best).OrderByDescending(i => camp.Experience[i]).ThenBy(i => i).First();
                    camp.SecondaryDomain = second;
                    // Keep accumulated experience intact; carryover is stored as progress separately at transition display/save.
                }
                Unlock(w, camp, camp.Technology); record.Log("Tribe " + camp.Player + " discovered " + camp.Technology + " and entered the Neolithic.");
            }
        }
        public static double NeolithicProgress(WorldState w, Camp camp, Domain domain)
        {
            double experience = camp.Experience[(int)domain];
            return camp.ForcedTransition && camp.SecondaryDomain == (int)domain ? Math.Max(w.Config.CarryoverFloor, experience * w.Config.CarryoverMultiplier) : experience;
        }
        private static void Unlock(WorldState w, Camp camp, Technology tech)
        {
            camp.Technologies[(int)tech] = true;
            if (tech == Technology.AnimalHusbandry)
            {
                string species = w.Config.Species.Where(s => s.Domesticable).OrderByDescending(s => camp.SpeciesExperience.FirstOrDefault(p => p.Species == s.Id)?.Experience ?? 0).ThenBy(s => s.Id, StringComparer.Ordinal).First().Id;
                w.Herds.Add(new Herd { Id = w.NextId++, Tile = camp.Tile, DomesticCamp = camp.Id, Species = species, Animals = w.Config.InitialDomesticAnimals });
            }
            else
            {
                var eligible = w.Map.Radius(camp.Tile, 1).Where(n => tech == Technology.Cultivation ? w.Tiles[n].Land && w.Ecology(n).Moisture >= w.Config.CultivationMoisture : w.Tiles[n].Terrain == Terrain.Shallows)
                    .OrderByDescending(n => w.Ecology(n).MaxFood).ThenBy(n => n).ToArray();
                if (eligible.Length > 0) { if (tech == Technology.Cultivation) w.Tiles[eligible[0]].Cultivated = true; else w.Tiles[eligible[0]].Fishery = true; }
            }
        }
        public static void Invariants(WorldState w, int expectedPeople = -1)
        {
            if (w.Camps.Select(c => c.Tile).Distinct().Count() != w.Camps.Count) throw new InvalidOperationException("Rival camps overlap.");
            var ids = w.Camps.Select(c => c.Id).Concat(w.Hunters.Select(h => h.Id)).Concat(w.Herds.Select(h => h.Id)).ToArray();
            if (ids.Distinct().Count() != ids.Length || ids.Any(i => i >= w.NextId)) throw new InvalidOperationException("Invalid stable IDs.");
            if (expectedPeople >= 0 && w.Camps.Sum(w.LinkedPopulation) != expectedPeople) throw new InvalidOperationException("Unexplained population change.");
            if (w.Camps.Any(c => c.Population < 0 || c.Food < -1e-8 || !w.CanEnter(c.Tile, false)) || w.Hunters.Any(h => h.Population < 0 || !w.CanEnter(h.Tile, true, w.Camps.First(c => c.Id == h.SourceCamp))))
                throw new InvalidOperationException("Invalid people state or location.");
            if (w.Hunters.Any(h => h.CarriedFood < -1e-8 || h.CarriedFood > Supply.Capacity(w, h) + 1e-8) || w.Camps.Any(c => c.GrowthRemainder < 0 || c.GrowthRemainder >= 1)) throw new InvalidOperationException("Invalid carried provisions or demographic remainder.");
            if (w.Herds.Any(h => !w.Tiles[h.Tile].TraversableLand || h.Animals < 0)) throw new InvalidOperationException("Invalid herd population or peak/aquatic location.");
            foreach (var tile in w.Tiles)
            {
                var ecology = w.Ecology(tile.Id);
                if (tile.WaterBody == WaterKind.Saltwater && (tile.Freshwater > 0 || ecology.MaxFreshwater > 0 || tile.FreshwaterSource != FreshwaterSource.None)) throw new InvalidOperationException("Seawater is not drinkable.");
                if (tile.Saltwater < 0 || tile.Saltwater > ecology.MaxSaltwater + 1e-6 || tile.FreshwaterSource == FreshwaterSource.None && tile.Freshwater > 1e-8) throw new InvalidOperationException("Water stock lacks a valid source or capacity.");
                if (double.IsNaN(tile.Food) || double.IsInfinity(tile.Food) || tile.Food < -1e-8 || tile.Freshwater < -1e-8 || double.IsNaN(tile.Freshwater) ||
                    tile.Food > ecology.MaxFood * (tile.Cultivated || tile.Fishery ? w.Config.CultivationMultiplier : 1) + 1e-6 || tile.Freshwater > ecology.MaxFreshwater + 1e-6)
                    throw new InvalidOperationException("Invalid ecology or capacity overflow.");
            }
        }
    }
}
