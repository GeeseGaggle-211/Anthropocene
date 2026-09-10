using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    internal sealed class GenerationResource
    {
        public int Tile;
        public double AvailableFood, AvailablePotentialFood, AvailableFreshwater;
    }

    internal sealed class GrowthPlan
    {
        public double Required, FinalPopulation;
    }

    public sealed partial class TurnResolver
    {
        private void ResolveEcology(WorldState w, List<Order> orders, TurnRecord record, Action<int> touch)
        {
            double intervals = record.Duration / w.Config.BaseDays;
            double years = record.Duration / w.Config.DaysPerYear;
            double generationScale = record.Duration / w.Config.PracticeDays;
            var weights = SeasonWeights(record.StartDays, record.Duration, w.Config.BaseDays);
            var resources = new Dictionary<int, GenerationResource>();
            var foraged = new Dictionary<int, double>();
            var recovering = new HashSet<int>(w.Camps.Where(c => c.DeficitTurns > 0).Select(c => c.Id));

            GenerationResource Resource(int tile)
            {
                if (resources.TryGetValue(tile, out var value)) return value;
                var source = w.Tiles[tile]; double foodProduction = 0, potentialProduction = 0, freshwaterProduction = 0;
                for (int season = 0; season < 4; season++)
                {
                    var ecology = source.Seasons[season]; double weight = weights[season];
                    double multiplier = source.Cultivated || source.Fishery ? w.Config.CultivationMultiplier : 1;
                    double maxFood = ecology.MaxFood * multiplier;
                    double maxPotential = ecology.MaxPotentialFood * multiplier;
                    double moisture = source.WaterBody == WaterKind.Saltwater ? 1 : source.OasisInfluence > 0 || source.WaterBody == WaterKind.Freshwater ? 1 : ecology.Moisture;
                    double gatherableDeficit = maxFood <= 0 ? 0 : Numbers.Clamp((maxFood - source.Food) / maxFood, 0, 1);
                    foodProduction += maxFood * ecology.FoodRate * (.25 + .75 * moisture) * weight * (1 + w.Config.GatherableRecoveryBias * gatherableDeficit);
                    potentialProduction += maxPotential * ecology.PotentialFoodRate * (.25 + .75 * moisture) * weight;
                    freshwaterProduction += ecology.MaxFreshwater * ecology.FreshwaterRate * weight;
                }
                double availablePotential = Math.Max(0, source.PotentialFood + potentialProduction);
                value = new GenerationResource
                {
                    Tile = tile,
                    AvailableFood = Math.Min(availablePotential, Math.Max(0, source.Food + foodProduction)),
                    AvailablePotentialFood = availablePotential,
                    AvailableFreshwater = source.WaterBody == WaterKind.Saltwater || source.FreshwaterSource == FreshwaterSource.None ? 0 : Math.Max(0, source.Freshwater + freshwaterProduction)
                };
                resources.Add(tile, value); return value;
            }

            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                record.Demographics.Add(new DemographicReport { Camp = camp.Id, StartingPopulation = w.LinkedPopulation(camp), Years = years, InitialReserves = camp.Food + w.Hunters.Where(h => h.SourceCamp == camp.Id).Sum(h => h.CarriedFood) });
                camp.StepFoodIncome = 0;
            }

            // Movement happens once. Herd reproduction is realized before the generation's hunting harvest.
            ReproduceHerds(w, intervals, years);
            Hunt(w, orders, record, intervals);
            var demands = new List<Demand>();
            foreach (var order in orders.Where(o => o.Kind == OrderKind.Fish).OrderBy(o => o.Entity))
            {
                var hunter = w.Hunters.FirstOrDefault(h => h.Id == order.Entity); if (hunter == null || hunter.Population <= 0) continue;
                var camp = w.Camps.First(c => c.Id == hunter.SourceCamp); camp.Experience[(int)Domain.Fishing] += hunter.Population * generationScale;
                if (w.Map.Distance(hunter.Tile, order.Target) <= 1)
                {
                    Resource(order.Target);
                    demands.Add(new Demand { Tile = order.Target, Entity = hunter.Id, Kind = "Fishing", Food = hunter.Population * w.Config.FishPerPerson * intervals });
                }
            }
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                if (camp.Population <= 0) continue;
                var requests = new List<Demand>(); double aquatic = 0, total = 0;
                foreach (int id in w.Map.Radius(camp.Tile, w.Config.GatherRadius).OrderBy(n => n))
                {
                    if (!PassiveGathering.Eligible(w, camp, id)) continue;
                    var available = Resource(id); var tile = w.Tiles[id];
                    double food = available.AvailableFood * Math.Min(1, Numbers.Fraction(w.Config.GatherFraction, intervals) * camp.Population / w.Config.PopulationScale);
                    requests.Add(new Demand { Entity = camp.Id, Tile = id, Kind = PassiveGathering.Kind(tile), Food = food }); total += food; if (tile.Aquatic) aquatic += food;
                }
                double scale = Math.Min(1, camp.Population * w.Config.GatherPerPerson * intervals / Math.Max(1e-12, total));
                foreach (var request in requests) { request.Food *= scale; demands.Add(request); }
                double share = aquatic / Math.Max(1e-12, total);
                camp.Experience[(int)Domain.Gathering] += camp.Population * generationScale * (1 - share);
                camp.Experience[(int)Domain.Fishing] += camp.Population * generationScale * share;
            }
            foreach (var hunter in w.Hunters.Where(h => h.Population > 0 && !Supply.Connected(w, h)).OrderBy(h => h.Id))
            {
                Resource(hunter.Tile);
                double need = hunter.Population * w.Config.Consumption * intervals;
                demands.Add(new Demand { Tile = hunter.Tile, Entity = hunter.Id, Kind = "Foraging", Food = Math.Min(need, hunter.Population * w.Config.HunterForagePerPerson * intervals) });
            }
            foreach (var herd in w.Herds.OrderBy(h => h.Id))
            {
                var species = w.Config.GetSpecies(herd.Species);
                herd.UnmetFood = herd.Animals * species.FoodConsumption * intervals; herd.UnmetFreshwater = herd.Animals * species.FreshwaterConsumption * intervals;
                int source = FreshwaterAccess.Source(w, herd.Tile); Resource(herd.Tile); if (source != herd.Tile) Resource(source);
                demands.Add(new Demand { Tile = herd.Tile, Entity = herd.Id, Kind = "Herd", PotentialFood = herd.UnmetFood, Freshwater = source == herd.Tile ? herd.UnmetFreshwater : 0 });
                if (source != herd.Tile) demands.Add(new Demand { Tile = source, Entity = herd.Id, Kind = "Herd", Freshwater = herd.UnmetFreshwater });
            }

            foreach (var resource in resources.Values.OrderBy(r => r.Tile))
            {
                touch(resource.Tile); w.Tiles[resource.Tile].Food = resource.AvailableFood; w.Tiles[resource.Tile].PotentialFood = resource.AvailablePotentialFood; w.Tiles[resource.Tile].Freshwater = resource.AvailableFreshwater;
            }
            CommitGenerationResources(w, demands, record, touch, foraged);

            var plans = w.Camps.OrderBy(c => c.Id).ToDictionary(c => c.Id, c => PlanGrowth(w, c, intervals, years));
            foreach (var camp in w.Camps.OrderBy(c => c.Id)) record.Demographics.First(d => d.Camp == camp.Id).FoodRequired += plans[camp.Id].Required;
            foreach (var hunter in w.Hunters.Where(h => !Supply.Connected(w, h))) record.Demographics.First(d => d.Camp == hunter.SourceCamp).FoodRequired += hunter.Population * w.Config.Consumption * intervals;
            SustainGeneration(w, record, intervals, generationScale, plans, foraged, recovering);
            Supply.Finish(w, record);
            ApplyStorage(w, record, intervals);
            SplitHerds(w, record);

            w.Days += record.Duration;
            AdvanceEcology(w, resources, weights);
            FinishDemographicReports(w, record);
        }

        private void CommitGenerationResources(WorldState w, List<Demand> demands, TurnRecord record, Action<int> touch, Dictionary<int, double> foraged)
        {
            var herds = w.Herds.ToDictionary(h => h.Id); var camps = w.Camps.ToDictionary(c => c.Id); var hunters = w.Hunters.ToDictionary(h => h.Id);
            foreach (var transfer in allocation.Allocate(demands, w.Tiles))
            {
                touch(transfer.Tile); var tile = w.Tiles[transfer.Tile];
                if (transfer.Food > 0) { tile.Food = Math.Max(0, tile.Food - transfer.Food); tile.PotentialFood = Math.Max(0, tile.PotentialFood - transfer.Food); }
                if (transfer.PotentialFood > 0)
                {
                    double nonGatherable = Math.Max(0, tile.PotentialFood - tile.Food);
                    tile.Food = Math.Max(0, tile.Food - Math.Max(0, transfer.PotentialFood - nonGatherable));
                    tile.PotentialFood = Math.Max(0, tile.PotentialFood - transfer.PotentialFood);
                }
                tile.Freshwater = Math.Max(0, tile.Freshwater - transfer.Freshwater);
                if (transfer.Kind == "Herd")
                {
                    var herd = herds[transfer.Entity]; herd.UnmetFood = Math.Max(0, herd.UnmetFood - transfer.PotentialFood); herd.UnmetFreshwater = Math.Max(0, herd.UnmetFreshwater - transfer.Freshwater);
                }
                else if (transfer.Kind == "Foraging")
                {
                    foraged[transfer.Entity] = (foraged.TryGetValue(transfer.Entity, out var existing) ? existing : 0) + transfer.Food;
                    camps[hunters[transfer.Entity].SourceCamp].Experience[(int)Domain.Gathering] += transfer.Food * w.Config.HarvestExperienceWeight * Math.Min(1, w.Config.BaseDays / w.Config.PracticeDays);
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

        private static double[] SeasonWeights(double startDays, double duration, double baseDays)
        {
            var weights = new double[4]; double position = startDays / baseDays, remaining = duration / baseDays;
            double first = Math.Min(remaining, Math.Ceiling(position + 1e-10) - position);
            if (first > 1e-10) { weights[((int)Math.Floor(position) % 4 + 4) % 4] += first; position += first; remaining -= first; }
            double cycles = Math.Floor(remaining / 4); for (int i = 0; i < 4; i++) weights[i] += cycles; position += cycles * 4; remaining -= cycles * 4;
            while (remaining > 1e-10)
            {
                double part = Math.Min(1, remaining); weights[((int)Math.Floor(position + 1e-9) % 4 + 4) % 4] += part; position += part; remaining -= part;
            }
            return weights;
        }

        private static GrowthPlan PlanGrowth(WorldState w, Camp camp, double intervals, double years)
        {
            int connectedHunters = w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).Sum(h => h.Population);
            double staticRequired = connectedHunters * w.Config.Consumption * intervals;
            double campBase = camp.Population * w.Config.Consumption * intervals;
            double baseRequired = staticRequired + campBase;
            double surplus = Numbers.Clamp((camp.StepFoodIncome - baseRequired) / Math.Max(1e-9, baseRequired * w.Config.GrowthSurplusRatio), 0, 1);
            double reserves = Numbers.Clamp(camp.Food / Math.Max(1, baseRequired * w.Config.GrowthReserveTurns), 0, 1);
            double rate = w.Config.AnnualHumanGrowthRate * Math.Min(surplus, reserves);
            double growthBudget = Math.Min(camp.Food, camp.StepFoodIncome);
            for (int iteration = 0; iteration < 12 && rate > 0; iteration++)
            {
                double required = GrowthRequirement(staticRequired, campBase, rate, years);
                if (required <= growthBudget + 1e-8) break;
                rate *= growthBudget / Math.Max(required, 1e-9);
            }
            return new GrowthPlan { Required = GrowthRequirement(staticRequired, campBase, rate, years), FinalPopulation = camp.Population * Math.Exp(rate * years) };
        }

        private static double GrowthRequirement(double staticRequired, double campBase, double rate, double years)
        {
            if (rate <= 1e-12 || years <= 0) return staticRequired + campBase;
            double x = rate * years; return staticRequired + campBase * (Math.Exp(x) - 1) / x;
        }

        private static void SustainGeneration(WorldState w, TurnRecord record, double intervals, double generationScale, Dictionary<int, GrowthPlan> plans, Dictionary<int, double> foraged, HashSet<int> recovering)
        {
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                var linked = w.Hunters.Where(h => h.SourceCamp == camp.Id && Supply.Connected(w, h)).OrderBy(h => h.Id).ToArray();
                int total = camp.Population + linked.Sum(h => h.Population); double required = plans[camp.Id].Required;
                camp.LastConsumed = Math.Min(required, camp.Food); camp.Food -= camp.LastConsumed; camp.FoodDeficit = required - camp.LastConsumed;
                if (camp.FoodDeficit > 1e-8) { camp.DeficitTurns++; camp.DeficitSeverity += camp.FoodDeficit / Math.Max(1, required) * generationScale; }
                else { camp.DeficitTurns = 0; camp.DeficitSeverity = 0; }
                if (camp.DeficitTurns > w.Config.StarvationGraceTurns)
                {
                    double lossProgress = camp.DeathRemainder + total * Numbers.Fraction(w.Config.Mortality, generationScale) * camp.FoodDeficit / Math.Max(1e-9, required);
                    int losses = Math.Min(total, (int)Math.Floor(lossProgress)); camp.DeathRemainder = lossProgress - Math.Floor(lossProgress);
                    ApplySharedDeaths(camp, linked, losses, record); record.Log("Tribe " + camp.Player + " lost " + losses + " people to sustained hunger.");
                }
                foreach (var h in linked) { h.DeficitTurns = camp.DeficitTurns; h.FoodDeficit = camp.FoodDeficit * h.Population / Math.Max(1, total); h.LastConsumed = Math.Max(0, h.Population * w.Config.Consumption * intervals - h.FoodDeficit); }
                var summary = record.Demographics.First(d => d.Camp == camp.Id); summary.FoodConsumed += camp.LastConsumed;
                if (camp.FoodDeficit > 1e-8) summary.DeficitSteps++;
                else if (recovering.Contains(camp.Id)) summary.RecoverySteps++;
                else AddGrowth(w, camp, plans[camp.Id], record, summary);
            }
            foreach (var hunter in w.Hunters.Where(h => !Supply.Connected(w, h)).OrderBy(h => h.Id))
            {
                double required = hunter.Population * w.Config.Consumption * intervals;
                double local = Math.Min(required, foraged.TryGetValue(hunter.Id, out var gathered) ? gathered : 0);
                double carried = Math.Min(required - local, hunter.CarriedFood); hunter.CarriedFood -= carried;
                hunter.LastConsumed = local + carried; hunter.FoodDeficit = required - hunter.LastConsumed;
                if (hunter.FoodDeficit > 1e-8) { hunter.DeficitTurns++; hunter.DeficitSeverity += hunter.FoodDeficit / Math.Max(1, required) * generationScale; }
                else { hunter.DeficitTurns = 0; hunter.DeficitSeverity = 0; }
                if (hunter.DeficitTurns > w.Config.StarvationGraceTurns)
                {
                    double lossProgress = hunter.DeathRemainder + hunter.Population * Numbers.Fraction(w.Config.Mortality, generationScale) * hunter.FoodDeficit / Math.Max(1e-9, required);
                    int deaths = Math.Min(hunter.Population, (int)Math.Floor(lossProgress)); hunter.DeathRemainder = lossProgress - Math.Floor(lossProgress);
                    hunter.Population -= deaths; record.Deaths += deaths; record.PopulationChanges.Add(new PopulationChange { Camp = hunter.SourceCamp, Entity = hunter.Id, Kind = "Starvation", Count = -deaths });
                    record.Log("Expedition " + hunter.Id + " lost " + deaths + " people to hunger beyond supply range.");
                }
                var summary = record.Demographics.First(d => d.Camp == hunter.SourceCamp); summary.FoodConsumed += hunter.LastConsumed; if (hunter.FoodDeficit > 1e-8) summary.DeficitSteps++;
            }
        }

        private static void ApplySharedDeaths(Camp camp, Hunter[] linked, int losses, TurnRecord record)
        {
            int total = camp.Population + linked.Sum(h => h.Population); int[] counts = new[] { camp.Population }.Concat(linked.Select(h => h.Population)).ToArray();
            int[] deaths = counts.Select(n => total == 0 ? 0 : (int)((long)n * losses / total)).ToArray(); int remainder = losses - deaths.Sum();
            foreach (int i in Enumerable.Range(0, counts.Length).OrderByDescending(i => total == 0 ? 0 : (long)counts[i] * losses % total).ThenBy(i => i)) if (remainder > 0 && deaths[i] < counts[i]) { deaths[i]++; remainder--; }
            camp.Population -= deaths[0]; record.PopulationChanges.Add(new PopulationChange { Camp = camp.Id, Entity = camp.Id, Kind = "Starvation", Count = -deaths[0] });
            for (int i = 0; i < linked.Length; i++) { linked[i].Population -= deaths[i + 1]; record.PopulationChanges.Add(new PopulationChange { Camp = camp.Id, Entity = linked[i].Id, Kind = "Starvation", Count = -deaths[i + 1] }); }
            record.Deaths += losses;
        }

        private static void AddGrowth(WorldState w, Camp camp, GrowthPlan plan, TurnRecord record, DemographicReport summary)
        {
            double progress = camp.GrowthRemainder + Math.Max(0, plan.FinalPopulation - camp.Population);
            int added = (int)Math.Floor(progress); camp.GrowthRemainder = progress - added; camp.Population += added; record.Growth += added;
            if (plan.FinalPopulation > camp.Population || added > 0) summary.SurplusSteps++;
            if (added > 0) record.PopulationChanges.Add(new PopulationChange { Camp = camp.Id, Entity = camp.Id, Kind = "Net growth", Count = added });
        }

        public static double StorageGenerations(WorldState w, int tile)
        {
            double temperature = w.Tiles[tile].Seasons.Average(s => s.Temperature), moisture = w.Tiles[tile].Seasons.Average(s => s.Moisture);
            double cold = Numbers.Clamp((12 - temperature) / 30, 0, 1), dry = Numbers.Clamp((.45 - moisture) / .45, 0, 1);
            return w.Config.MinimumStorageGenerations + (w.Config.MaximumStorageGenerations - w.Config.MinimumStorageGenerations) * Math.Max(cold, dry);
        }

        private static void ApplyStorage(WorldState w, TurnRecord record, double intervals)
        {
            foreach (var camp in w.Camps.OrderBy(c => c.Id))
            {
                double cap = w.LinkedPopulation(camp) * w.Config.Consumption * intervals * StorageGenerations(w, camp.Tile);
                double loss = Math.Max(0, camp.Food - cap); camp.Food -= loss;
                var summary = record.Demographics.First(d => d.Camp == camp.Id); summary.DiscardedInventory += loss;
            }
        }

        private static void ReproduceHerds(WorldState w, double intervals, double years)
        {
            foreach (var herd in w.Herds.OrderBy(h => h.Id).ToArray())
            {
                var species = w.Config.GetSpecies(herd.Species); double demand = herd.Animals * intervals;
                double intake = Math.Min(1 - herd.UnmetFood / Math.Max(1e-9, demand * species.FoodConsumption), 1 - herd.UnmetFreshwater / Math.Max(1e-9, demand * species.FreshwaterConsumption));
                double target = herd.Animals * Math.Exp(species.GrowthRate * years * Math.Max(0, intake)) + herd.GrowthRemainder;
                int grown = Math.Max(0, (int)Math.Floor(target) - herd.Animals); herd.GrowthRemainder = target - Math.Floor(target); herd.Animals += grown;
            }
        }

        private static void SplitHerds(WorldState w, TurnRecord record)
        {
            foreach (var herd in w.Herds.Where(h => h.DomesticCamp < 0 && h.Animals > h.Capacity).OrderBy(h => h.Id).ToArray())
            {
                int groups = (int)Math.Ceiling(herd.Animals / (double)herd.Capacity), total = herd.Animals, baseSize = total / groups, remainder = total % groups;
                var species = w.Config.GetSpecies(herd.Species);
                var destinations = w.Map.Radius(herd.Tile, 1).Where(n => w.Tiles[n].TraversableLand).OrderByDescending(n => species.Suitability(w.Tiles[n])).ThenBy(n => Numbers.Hash(w.Seed, 151, herd.Id ^ n, w.Turn)).ThenBy(n => n).ToArray();
                herd.Animals = baseSize + (remainder > 0 ? 1 : 0);
                for (int i = 1; i < groups; i++)
                {
                    int tile = destinations.Length == 0 ? herd.Tile : destinations[i % destinations.Length]; int animals = baseSize + (i < remainder ? 1 : 0);
                    w.Herds.Add(new Herd { Id = w.NextId++, Tile = tile, Species = herd.Species, Animals = animals, Capacity = herd.Capacity });
                }
                record.Log("Herd " + herd.Id + " grew beyond " + herd.Capacity + " animals and divided into " + groups + " herds.");
            }
        }

        private static void AdvanceEcology(WorldState w, Dictionary<int, GenerationResource> resources, double[] weights)
        {
            int endSeason = w.Season; double intervals = weights.Sum();
            foreach (var tile in w.Tiles)
            {
                var end = tile.Seasons[endSeason]; double multiplier = tile.Cultivated || tile.Fishery ? w.Config.CultivationMultiplier : 1;
                if (!resources.ContainsKey(tile.Id))
                {
                    double averageMax = 0, averagePotentialMax = 0, averageFresh = 0, foodRate = 0, potentialRate = 0, freshRate = 0;
                    for (int season = 0; season < 4; season++)
                    {
                        var ecology = tile.Seasons[season]; double weight = weights[season];
                        averageMax += ecology.MaxFood * multiplier * weight; averagePotentialMax += ecology.MaxPotentialFood * multiplier * weight; averageFresh += ecology.MaxFreshwater * weight; foodRate += ecology.FoodRate * weight; potentialRate += ecology.PotentialFoodRate * weight; freshRate += ecology.FreshwaterRate * weight;
                    }
                    averageMax /= Math.Max(1e-9, intervals); averagePotentialMax /= Math.Max(1e-9, intervals); averageFresh /= Math.Max(1e-9, intervals);
                    double deficit = averageMax <= 0 ? 0 : Numbers.Clamp((averageMax - tile.Food) / averageMax, 0, 1);
                    tile.PotentialFood += Math.Max(0, averagePotentialMax - tile.PotentialFood) * Numbers.Fraction(potentialRate / Math.Max(1e-9, intervals), intervals);
                    tile.Food += Math.Max(0, Math.Min(averageMax, tile.PotentialFood) - tile.Food) * Numbers.Fraction(foodRate / Math.Max(1e-9, intervals) * (1 + w.Config.GatherableRecoveryBias * deficit), intervals);
                    if (tile.WaterBody != WaterKind.Saltwater && tile.FreshwaterSource != FreshwaterSource.None) tile.Freshwater += Math.Max(0, averageFresh - tile.Freshwater) * Numbers.Fraction(freshRate / Math.Max(1e-9, intervals), intervals);
                }
                tile.PotentialFood = Numbers.Clamp(tile.PotentialFood, 0, end.MaxPotentialFood * multiplier);
                tile.Food = Numbers.Clamp(tile.Food, 0, Math.Min(end.MaxFood * multiplier, tile.PotentialFood));
                tile.Freshwater = tile.WaterBody == WaterKind.Saltwater || tile.FreshwaterSource == FreshwaterSource.None ? 0 : Numbers.Clamp(tile.Freshwater, 0, end.MaxFreshwater);
                tile.Saltwater = end.MaxSaltwater;
            }
        }

        private static void FinishDemographicReports(WorldState w, TurnRecord record)
        {
            record.Transfers = record.Transfers.GroupBy(t => (t.Tile, t.Entity, t.Kind)).OrderBy(g => g.Key.Tile).ThenBy(g => g.Key.Entity).ThenBy(g => g.Key.Kind, StringComparer.Ordinal)
                .Select(g => new Transfer { Tile = g.Key.Tile, Entity = g.Key.Entity, Kind = g.Key.Kind, Food = g.Sum(t => t.Food), PotentialFood = g.Sum(t => t.PotentialFood), Freshwater = g.Sum(t => t.Freshwater) }).ToList();
            foreach (var summary in record.Demographics)
            {
                var camp = w.Camps.First(c => c.Id == summary.Camp); var ids = new HashSet<int>(w.Hunters.Where(h => h.SourceCamp == camp.Id).Select(h => h.Id)) { camp.Id };
                var harvest = record.Transfers.Where(t => ids.Contains(t.Entity)).ToArray();
                summary.TerrestrialFood = harvest.Where(t => t.Kind == "Gathering").Sum(t => t.Food); summary.MarineFood = harvest.Where(t => t.Kind == "Marine gathering").Sum(t => t.Food); summary.LakeFood = harvest.Where(t => t.Kind == "Aquatic gathering").Sum(t => t.Food);
                summary.HuntFood = harvest.Where(t => t.Kind == "Hunting").Sum(t => t.Food); summary.FishFood = harvest.Where(t => t.Kind == "Fishing").Sum(t => t.Food); double forage = harvest.Where(t => t.Kind == "Foraging").Sum(t => t.Food);
                summary.FoodProduced = summary.TerrestrialFood + summary.MarineFood + summary.LakeFood + summary.HuntFood + summary.FishFood + forage;
                summary.FoodReceived = summary.FoodProduced - record.Logistics.Where(l => l.Camp == camp.Id).Sum(l => l.HarvestOverflow);
                summary.DiscardedInventory += record.Logistics.Where(l => l.Camp == camp.Id).Sum(l => l.InventoryLoss);
                summary.Additions = record.PopulationChanges.Where(p => p.Camp == camp.Id && p.Count > 0).Sum(p => p.Count); summary.Deaths = -record.PopulationChanges.Where(p => p.Camp == camp.Id && p.Count < 0).Sum(p => p.Count);
                summary.EndingPopulation = w.LinkedPopulation(camp); summary.NetPercent = 100.0 * (summary.EndingPopulation - summary.StartingPopulation) / Math.Max(1, summary.StartingPopulation);
                summary.FinalReserves = camp.Food + w.Hunters.Where(h => h.SourceCamp == camp.Id).Sum(h => h.CarriedFood); summary.FractionalProgress = camp.GrowthRemainder;
                if (Math.Abs(summary.InitialReserves + summary.FoodReceived - summary.FoodConsumed - summary.DiscardedInventory - summary.FinalReserves) > 1e-5) throw new InvalidOperationException("Unexplained food balance in demographic report.");
                summary.LimitingFactor = summary.DeficitSteps > 0 ? "Food deficits limited growth" : summary.Additions > 0 ? "Growth supported by food surplus; demand rose with population" : summary.SurplusSteps > 0 ? "Demographic rounding; fractional progress " + summary.FractionalProgress.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : summary.RecoverySteps > 0 ? "Starvation recovery" : "No sustainable food surplus or insufficient reserves";
                record.Log(summary.Explanation);
            }
        }
    }
}
