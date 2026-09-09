using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class DemographicReport
    {
        public int Camp, StartingPopulation, Additions, Deaths, EndingPopulation, SurplusSteps, DeficitSteps, RecoverySteps;
        public double Years, FoodProduced, FoodReceived, TerrestrialFood, MarineFood, LakeFood, HuntFood, FishFood;
        public double FoodRequired, FoodConsumed, InitialReserves, FinalReserves, DiscardedInventory, FractionalProgress, NetPercent;
        public string LimitingFactor;
        public string Explanation => "Population " + StartingPopulation + " + " + Additions + " additions - " + Deaths + " deaths = " + EndingPopulation + " (" + NetPercent.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) + "%). " + LimitingFactor + ".";
    }
    public static class PassiveGathering
    {
        public static bool Eligible(WorldState w, Camp camp, int tile) => w.Map.Distance(camp.Tile, tile) <= w.Config.GatherRadius && w.Tiles[tile].Terrain != Terrain.Ocean && w.Tiles[tile].ElevationLevel < 10;
        public static string Kind(Tile tile) => tile.WaterBody == WaterKind.Saltwater ? "Marine gathering" : tile.Aquatic ? "Aquatic gathering" : "Gathering";
    }
    public sealed partial class TurnResolver
    {
        private void ResolveEcology(WorldState w, List<Order> orders, TurnRecord record, Action<int> touch)
        {
            double end = record.StartDays + record.Duration;
            foreach (var camp in w.Camps.OrderBy(c => c.Id)) record.Demographics.Add(new DemographicReport { Camp = camp.Id, StartingPopulation = w.LinkedPopulation(camp), Years = record.Duration / w.Config.DaysPerYear, InitialReserves = camp.Food + w.Hunters.Where(h => h.SourceCamp == camp.Id).Sum(h => h.CarriedFood) });
            while (w.Days < end - 1e-8)
            {
                // Player movement is committed once. Subsistence is a continuing strategy over deterministic seasonal intervals.
                double boundary = (Math.Floor(w.Days / w.Config.BaseDays + 1e-9) + 1) * w.Config.BaseDays;
                double days = Math.Min(end - w.Days, boundary - w.Days), time = days / w.Config.BaseDays, practice = days / w.Config.PracticeDays;
                var recovering = new HashSet<int>(w.Camps.Where(c => c.DeficitTurns > 0).Select(c => c.Id));
                foreach (var camp in w.Camps.OrderBy(c => c.Id)) camp.StepFoodIncome = 0;
                Hunt(w, orders, record, time);
                var fishing = new List<Demand>();
                foreach (var order in orders.Where(o => o.Kind == OrderKind.Fish).OrderBy(o => o.Entity))
                {
                    var hunter = w.Hunters.FirstOrDefault(h => h.Id == order.Entity); if (hunter == null || hunter.Population <= 0) continue;
                    var camp = w.Camps.First(c => c.Id == hunter.SourceCamp); camp.Experience[(int)Domain.Fishing] += hunter.Population * practice;
                    if (w.Map.Distance(hunter.Tile, order.Target) <= 1) fishing.Add(new Demand { Tile = order.Target, Entity = hunter.Id, Kind = "Fishing", Food = hunter.Population * w.Config.FishPerPerson * time });
                }
                CommitResources(w, fishing, record, touch);
                var demands = new List<Demand>();
                foreach (var camp in w.Camps.OrderBy(c => c.Id))
                {
                    if (camp.Population <= 0) continue;
                    var requests = new List<Demand>(); double aquatic = 0, total = 0;
                    foreach (int id in w.Map.Radius(camp.Tile, w.Config.GatherRadius).OrderBy(n => n))
                    {
                        if (!PassiveGathering.Eligible(w, camp, id)) continue;
                        var tile = w.Tiles[id];
                        double food = tile.Food * Math.Min(1, Numbers.Fraction(w.Config.GatherFraction, time) * camp.Population / w.Config.PopulationScale);
                        requests.Add(new Demand { Entity = camp.Id, Tile = id, Kind = PassiveGathering.Kind(tile), Food = food }); total += food; if (tile.Aquatic) aquatic += food;
                    }
                    double scale = Math.Min(1, camp.Population * w.Config.GatherPerPerson * time / Math.Max(1e-12, total));
                    foreach (var request in requests) { request.Food *= scale; demands.Add(request); }
                    double share = aquatic / Math.Max(1e-12, total);
                    camp.Experience[(int)Domain.Gathering] += camp.Population * practice * (1 - share);
                    camp.Experience[(int)Domain.Fishing] += camp.Population * practice * share;
                }
                foreach (var herd in w.Herds.OrderBy(h => h.Id))
                {
                    var species = w.Config.GetSpecies(herd.Species);
                    herd.UnmetFood = herd.Animals * species.FoodConsumption * time; herd.UnmetFreshwater = herd.Animals * species.FreshwaterConsumption * time;
                    int source = FreshwaterAccess.Source(w, herd.Tile);
                    demands.Add(new Demand { Tile = herd.Tile, Entity = herd.Id, Kind = "Herd", Food = herd.UnmetFood, Freshwater = source == herd.Tile ? herd.UnmetFreshwater : 0 });
                    if (source != herd.Tile) demands.Add(new Demand { Tile = source, Entity = herd.Id, Kind = "Herd", Freshwater = herd.UnmetFreshwater });
                }
                CommitResources(w, demands, record, touch);
                foreach (var camp in w.Camps.OrderBy(c => c.Id))
                {
                    var summary = record.Demographics.First(d => d.Camp == camp.Id);
                    summary.FoodRequired += w.LinkedPopulation(camp) * w.Config.Consumption * time;
                }
                Sustain(w, record, time);
                foreach (var camp in w.Camps.OrderBy(c => c.Id))
                {
                    var summary = record.Demographics.First(d => d.Camp == camp.Id);
                    summary.FoodConsumed += camp.LastConsumed + w.Hunters.Where(h => h.SourceCamp == camp.Id && !Supply.Connected(w, h)).Sum(h => h.LastConsumed);
                    if (camp.FoodDeficit > 1e-8 || w.Hunters.Any(h => h.SourceCamp == camp.Id && h.FoodDeficit > 1e-8)) summary.DeficitSteps++;
                    GrowGeneration(w, camp, record, summary, days, recovering.Contains(camp.Id));
                }
                Supply.Finish(w, record);
                foreach (var herd in w.Herds.OrderBy(h => h.Id))
                {
                    var species = w.Config.GetSpecies(herd.Species); double demand = herd.Animals * time;
                    double intake = Math.Min(1 - herd.UnmetFood / Math.Max(1e-9, demand * species.FoodConsumption), 1 - herd.UnmetFreshwater / Math.Max(1e-9, demand * species.FreshwaterConsumption));
                    double progress = herd.Animals * Numbers.Fraction(species.GrowthRate, time) * Math.Max(0, intake) + herd.GrowthRemainder;
                    herd.Animals += Math.Max(0, Math.Min(herd.Capacity - herd.Animals, (int)progress)); herd.GrowthRemainder = progress - Math.Floor(progress);
                }
                int season = w.Season;
                foreach (var tile in w.Tiles) Regenerate(tile, tile.Seasons[season], w.Config, time);
                w.Days += days;
                if (w.Season != season)
                    foreach (var tile in w.Tiles)
                    {
                        var next = tile.Seasons[w.Season]; tile.Food = Math.Min(tile.Food, next.MaxFood * (tile.Cultivated || tile.Fishery ? w.Config.CultivationMultiplier : 1)); tile.Freshwater = Math.Min(tile.Freshwater, next.MaxFreshwater); tile.Saltwater = next.MaxSaltwater;
                    }
            }
            // Preserve exact aggregate resource records without retaining one object per claimant per seasonal substep.
            record.Transfers = record.Transfers.GroupBy(t => (t.Tile, t.Entity, t.Kind)).OrderBy(g => g.Key.Tile).ThenBy(g => g.Key.Entity).ThenBy(g => g.Key.Kind, StringComparer.Ordinal)
                .Select(g => new Transfer { Tile = g.Key.Tile, Entity = g.Key.Entity, Kind = g.Key.Kind, Food = g.Sum(t => t.Food), Freshwater = g.Sum(t => t.Freshwater) }).ToList();
            foreach (var summary in record.Demographics)
            {
                var camp = w.Camps.First(c => c.Id == summary.Camp); var ids = new HashSet<int>(w.Hunters.Where(h => h.SourceCamp == camp.Id).Select(h => h.Id)) { camp.Id };
                var harvest = record.Transfers.Where(t => ids.Contains(t.Entity)).ToArray();
                summary.TerrestrialFood = harvest.Where(t => t.Kind == "Gathering").Sum(t => t.Food); summary.MarineFood = harvest.Where(t => t.Kind == "Marine gathering").Sum(t => t.Food); summary.LakeFood = harvest.Where(t => t.Kind == "Aquatic gathering").Sum(t => t.Food);
                summary.HuntFood = harvest.Where(t => t.Kind == "Hunting").Sum(t => t.Food); summary.FishFood = harvest.Where(t => t.Kind == "Fishing").Sum(t => t.Food);
                summary.FoodProduced = summary.TerrestrialFood + summary.MarineFood + summary.LakeFood + summary.HuntFood + summary.FishFood;
                summary.FoodReceived = summary.FoodProduced - record.Logistics.Where(l => l.Camp == camp.Id).Sum(l => l.HarvestOverflow);
                summary.DiscardedInventory = record.Logistics.Where(l => l.Camp == camp.Id).Sum(l => l.InventoryLoss);
                summary.Additions = record.PopulationChanges.Where(p => p.Camp == camp.Id && p.Count > 0).Sum(p => p.Count); summary.Deaths = -record.PopulationChanges.Where(p => p.Camp == camp.Id && p.Count < 0).Sum(p => p.Count);
                summary.EndingPopulation = w.LinkedPopulation(camp); summary.NetPercent = 100.0 * (summary.EndingPopulation - summary.StartingPopulation) / Math.Max(1, summary.StartingPopulation);
                summary.FinalReserves = camp.Food + w.Hunters.Where(h => h.SourceCamp == camp.Id).Sum(h => h.CarriedFood); summary.FractionalProgress = camp.GrowthRemainder;
                if (Math.Abs(summary.InitialReserves + summary.FoodReceived - summary.FoodConsumed - summary.DiscardedInventory - summary.FinalReserves) > 1e-5) throw new InvalidOperationException("Unexplained food balance in demographic report.");
                summary.LimitingFactor = summary.DeficitSteps > 0 ? "Food deficits limited growth" : summary.Additions > 0 ? "Growth supported by food surplus; demand rose with population" : summary.SurplusSteps > 0 ? "Demographic rounding; fractional progress " + summary.FractionalProgress.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : summary.RecoverySteps > 0 ? "Starvation recovery" : "No sustainable food surplus or insufficient reserves";
                record.Log(summary.Explanation);
            }
        }
        private static void GrowGeneration(WorldState w, Camp camp, TurnRecord record, DemographicReport summary, double days, bool recovering)
        {
            if (camp.Population <= 0 || camp.FoodDeficit > 1e-8) return;
            if (recovering) { summary.RecoverySteps++; return; }
            double surplus = Numbers.Clamp((camp.StepFoodIncome - camp.LastConsumed) / Math.Max(1e-9, camp.LastConsumed * w.Config.GrowthSurplusRatio), 0, 1);
            double reserves = Numbers.Clamp(camp.Food / Math.Max(1, camp.Population * w.Config.Consumption * w.Config.GrowthReserveTurns), 0, 1);
            double rate = w.Config.AnnualHumanGrowthRate * Math.Min(surplus, reserves);
            if (rate <= 0) return; summary.SurplusSteps++;
            double progress = camp.GrowthRemainder + (camp.Population + camp.GrowthRemainder) * (Math.Exp(rate * days / w.Config.DaysPerYear) - 1);
            int added = (int)Math.Floor(progress); camp.GrowthRemainder = progress - added; camp.Population += added; record.Growth += added;
            if (added > 0) record.PopulationChanges.Add(new PopulationChange { Camp = camp.Id, Entity = camp.Id, Kind = "Net growth", Count = added });
        }
        private static void Regenerate(Tile tile, SeasonData ecology, SimulationConfig config, double time)
        {
            double max = ecology.MaxFood * (tile.Cultivated || tile.Fishery ? config.CultivationMultiplier : 1);
            double moisture = tile.WaterBody == WaterKind.Saltwater ? tile.Saltwater / Math.Max(1, ecology.MaxSaltwater) : tile.OasisInfluence > 0 || tile.WaterBody == WaterKind.Freshwater ? tile.DrinkableWater / Math.Max(1, ecology.MaxFreshwater) : ecology.Moisture;
            tile.Food = Numbers.Clamp(tile.Food + Math.Max(0, max - tile.Food) * Numbers.Fraction(ecology.FoodRate * (.25 + .75 * moisture), time), 0, max);
            tile.Freshwater = Numbers.Clamp(tile.Freshwater + Math.Max(0, ecology.MaxFreshwater - tile.Freshwater) * Numbers.Fraction(ecology.FreshwaterRate, time), 0, ecology.MaxFreshwater); tile.Saltwater = ecology.MaxSaltwater;
        }
    }
}
