using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Anthropocene.Simulation
{
    public static class Knowledge
    {
        public static readonly string[] Bands = { "Depleted", "Poor", "Decent", "Ample", "Abundant" };
        public static int Band(double value, double capacity, SimulationConfig config)
        {
            double ratio = value / Math.Max(1, capacity);
            for (int i = 0; i < config.BandThresholds.Length; i++) if (ratio < config.BandThresholds[i]) return i;
            return 4;
        }
        public static HashSet<int> Visible(WorldState world, int player)
        {
            var result = new HashSet<int>();
            foreach (var camp in world.Camps.Where(c => c.Player == player && world.LinkedPopulation(c) > 0)) result.UnionWith(world.Map.Radius(camp.Tile, world.Config.VisionRadius));
            foreach (var hunter in world.Hunters.Where(h => h.Player == player && h.Population > 0)) result.UnionWith(world.Map.Radius(hunter.Tile, world.Config.VisionRadius));
            return result;
        }
    }
    public sealed class TileObservation
    {
        public readonly int Id, Food, PotentialFood, Freshwater, Productivity, Moisture, Condition, ElevationLevel, RidgeMask, FoodMaximum, PotentialMaximum, GatherablePercent;
        public readonly Terrain Terrain;
        public readonly EnvironmentKind Environment;
        public readonly VegetationTier NaturalVegetationPotential, CurrentVegetation;
        public readonly bool River, Cultivated, Fishery, Oasis, Irrigated, Cultivable;
        public readonly double Cultivation;
        public readonly Relief Relief;
        public readonly float Elevation;
        public readonly WaterKind WaterBody;
        public readonly FreshwaterSource FreshwaterSource;
        public readonly IReadOnlyList<RiverObservation> Rivers;
        public bool Land => Terrain != Terrain.Ocean && Terrain != Terrain.Shallows && Terrain != Terrain.Lake;
        public bool Aquatic => Terrain == Terrain.Shallows || Terrain == Terrain.Lake;
        public TileObservation(Tile tile, SeasonData ecology, SimulationConfig config, IReadOnlyList<RiverEdge> rivers, EcologyView? projected = null)
        {
            var view = projected ?? new EcologyView(tile);
            Cultivable = tile.TraversableLand && tile.Environment != EnvironmentKind.Ice && tile.Seasons.Any(s => s.MaxPotentialFood > 0);
            Environment = tile.Environment; NaturalVegetationPotential = tile.NaturalVegetationPotential; CurrentVegetation = view.CurrentVegetation;
            double accessibility = FoodEcology.GatherableFraction(tile, config, view.CurrentVegetation) / FoodEcology.GatherableFraction(tile, config, tile.NaturalVegetationPotential);
            Id = tile.Id; Terrain = Vegetation.LegacyTerrain(tile.Environment, view.CurrentVegetation); River = tile.River; Cultivated = tile.Cultivated; Fishery = tile.Fishery; ElevationLevel = tile.ElevationLevel; RidgeMask = tile.RidgeMask; FoodMaximum = Knowledge.Band(ecology.MaxFood * accessibility, 70, config);
            Cultivation = CultivationEconomy.Intensity(config, view.CultivationCapital); Irrigated = tile.Irrigated;
            PotentialFood = Knowledge.Band(view.PotentialFood, Math.Max(1, ecology.MaxPotentialFood), config); PotentialMaximum = Knowledge.Band(ecology.MaxPotentialFood, 700, config); GatherablePercent = (int)Math.Round(100 * ecology.MaxFood * accessibility / Math.Max(1e-9, ecology.MaxPotentialFood));
            Oasis = tile.Oasis; Relief = tile.Relief; Elevation = tile.Elevation; WaterBody = tile.WaterBody; FreshwaterSource = tile.FreshwaterSource;
            Rivers = Array.AsReadOnly(tile.RiverEdges == null ? new RiverObservation[0] : tile.RiverEdges.Select((id, side) => new { id, side }).Where(x => x.id >= 0).Select(x => new RiverObservation(x.side, rivers[x.id], tile.Id, new HexCorners(config.Width, config.Height))).ToArray());
            // Food bands describe absolute usable yield; a nearly full barren tile must not appear abundant.
            Food = Knowledge.Band(view.Food, 70, config); Freshwater = Knowledge.Band(view.Freshwater, 70, config);
            double condition = tile.Aquatic || tile.WaterBody == WaterKind.Saltwater ? view.AquaticCondition : view.LandCondition;
            Condition = Knowledge.Band(condition, 1, config); Productivity = Knowledge.Band(ecology.MaxFood * accessibility * ecology.FoodRecoveryPerDay * config.BaseDays * condition, 30, config); Moisture = Knowledge.Band(ecology.Moisture, 1, config);
        }
    }
    public sealed class RiverObservation
    {
        public readonly int Side, FlowBand;
        public readonly bool Forward, Mouth;
        public readonly float Width;
        public RiverObservation(int side, RiverEdge river, int tile, HexCorners corners) { Side = side; FlowBand = river.Width < 1.8 ? 1 : river.Width < 3 ? 2 : 3; Width = river.Width; Mouth = river.Mouth; Forward = river.FromVertex == corners.Corner(tile, side); }
    }
    public sealed class UnitObservation
    {
        public readonly int Id, Tile, Population, Player, SourceCamp;
        public readonly bool InSupply;
        public readonly int Provisions, DeficitTurns;
        public UnitObservation(int id, int tile, int population, int player, bool inSupply = false, int provisions = 0, int deficitTurns = 0, int sourceCamp = -1) { SourceCamp = sourceCamp; Id = id; Tile = tile; Population = population; Player = player; InSupply = inSupply; Provisions = provisions; DeficitTurns = deficitTurns; }
    }
    public sealed class HerdObservation
    {
        public readonly int Id, Tile, Size, Owner, DomesticCamp, DeficitTurns, StartingAnimals, SurvivingAnimals, NewbornAnimals, StarvationDeaths;
        public readonly string Species, Name;
        public readonly bool Domestic, OwnedDomestic;
        public readonly double DeficitSeverity, DeathRemainder, FoodNutrition, WaterNutrition, Nutrition, ManagementFulfillment, PotentialFood, AcceptedFood;
        public readonly HerdCountEstimate Count;
        public HerdObservation(Herd herd, SimulationConfig config, int owner = -1, int viewer = -2)
        {
            Id = herd.Id; Tile = herd.Tile; Species = herd.Species; Name = config.GetSpecies(Species).Name; Domestic = herd.DomesticCamp >= 0; Owner = owner;
            OwnedDomestic = Domestic && owner == viewer; DomesticCamp = OwnedDomestic ? herd.DomesticCamp : -1;
            DeficitTurns = OwnedDomestic ? herd.DeficitTurns : 0; DeficitSeverity = OwnedDomestic ? herd.DeficitSeverity : 0; DeathRemainder = OwnedDomestic ? herd.DeathRemainder : 0;
            StartingAnimals = OwnedDomestic ? herd.StartingAnimals : 0; SurvivingAnimals = OwnedDomestic ? herd.SurvivingAnimals : 0; NewbornAnimals = OwnedDomestic ? herd.NewbornAnimals : 0; StarvationDeaths = OwnedDomestic ? herd.StarvationDeaths : 0;
            FoodNutrition = OwnedDomestic ? herd.LastFoodNutrition : 0; WaterNutrition = OwnedDomestic ? herd.LastWaterNutrition : 0; Nutrition = OwnedDomestic ? herd.LastNutrition : 0; ManagementFulfillment = OwnedDomestic ? herd.ManagementFulfillment : 0;
            PotentialFood = OwnedDomestic ? herd.AnimalFoodPotential : 0; AcceptedFood = OwnedDomestic ? herd.AnimalFoodProduced : 0;
            Count = HerdCountKnowledge.Observe(herd.Animals, config, OwnedDomestic);
            Size = Count.Representative < config.HerdSmallCountThreshold ? 1 : Count.Representative <= 20 ? 2 : 3;
        }
    }
    public sealed class AgentObservation
    {
        public const int SchemaVersion = 11;
        public readonly int Player, CampId, CampTile, CampPopulation, GathererPopulation, LinkedPopulation, FoodCondition, DeficitTurns, Turn, PolicySeed;
        public readonly double HuntingFieldcraft, HuntingInclination, GatheringInclination, FishingInclination, Days;
        public readonly IReadOnlyList<SettlementObservation> Settlements;
        public readonly bool Watercraft;
        public readonly IReadOnlyDictionary<int, TileObservation> Tiles;
        public readonly IReadOnlyDictionary<int, double> RecentOwnExtraction;
        public readonly IReadOnlyList<UnitObservation> Hunters, Rivals, ForeignHunters;
        public readonly IReadOnlyList<HerdObservation> Herds;
        public readonly IReadOnlyDictionary<int, int> LikelyHerdDestinations;
        public readonly IReadOnlyList<double> Experience;
        public readonly HexMap Map;
        public readonly int MinimumCamp, HunterMovement, CampMovement, WaterMovementCost, SupplyRange, SustainableHuntPopulation;
        public readonly double Inertia;
        internal AgentObservation(WorldState w, Camp camp, PlanningIndex index)
        {
            var visible = index.Visible(camp.Player);
            Days = w.Days; Settlements = Array.AsReadOnly(index.OwnCamps(camp.Player).Select(c => new SettlementObservation(c, index)).ToArray());
            var preferences = camp.People.Policy; HuntingInclination = preferences.HuntingInclination; GatheringInclination = preferences.GatheringInclination; FishingInclination = preferences.FishingInclination;
            Player = camp.Player; CampId = camp.Id; CampTile = camp.Tile; CampPopulation = camp.Population; GathererPopulation = camp.GathererPopulation; LinkedPopulation = index.Population(camp);
            FoodCondition = Knowledge.Band(camp.Food, Math.Max(1, LinkedPopulation * w.Config.Consumption * w.Config.GenerationIntervals), w.Config);
            DeficitTurns = camp.DeficitTurns; Turn = w.Turn; PolicySeed = camp.People.PolicySeed;
            Watercraft = camp.Technologies[(int)Technology.Watercraft]; Map = w.Map;
            WaterMovementCost = Math.Max(1, w.Config.HunterMovement / w.Config.WaterMovement); MinimumCamp = w.Config.MinimumCamp; HunterMovement = w.Config.HunterMovement; CampMovement = w.Config.CampMovement; Inertia = w.Config.RelocationInertia;
            SupplyRange = w.Config.SupplyRange; SustainableHuntPopulation = w.Config.SustainableHuntPopulation;
            Tiles = new ReadOnlyDictionary<int, TileObservation>(visible.OrderBy(n => n).ToDictionary(n => n, n => new TileObservation(w.Tiles[n], w.Ecology(n), w.Config, w.Rivers, index.Ecology(n))));
            Hunters = Array.AsReadOnly(index.OwnHunters(Player).Select(h => new UnitObservation(h.Id, h.Tile, h.Population, h.Player, Supply.Connected(w, h), Knowledge.Band(h.CarriedFood, Supply.Capacity(w, h), w.Config), h.DeficitTurns, h.SourceCamp)).ToArray());
            Rivals = Array.AsReadOnly(index.VisibleCamps(visible).Where(c => c.Player != Player).Select(c => new UnitObservation(c.Id, c.Tile, 0, c.Player)).ToArray());
            ForeignHunters = Array.AsReadOnly(index.VisibleHunters(visible).Where(h => h.Player != Player).Select(h => new UnitObservation(h.Id, h.Tile, 0, h.Player)).ToArray());
            Herds = Array.AsReadOnly(index.VisibleHerds(visible).Select(h => new HerdObservation(h, w.Config, h.DomesticCamp < 0 ? -1 : index.Camps[h.DomesticCamp].Player, Player)).ToArray());
            HuntingFieldcraft = PracticeEffects.Ramp(w.Config, camp, Domain.Hunting);
            var observedHunters = Hunters.Concat(ForeignHunters).ToArray();
            LikelyHerdDestinations = new ReadOnlyDictionary<int, int>(HuntingFieldcraft <= 0 ? new Dictionary<int, int>() : Herds.Where(h => !h.Domestic).ToDictionary(h => h.Id, h => PredictHerdDestination(h, Tiles, observedHunters, Map)));
            var ownIds = new HashSet<int>(Hunters.Select(h => h.Id)) { CampId };
            var recent = w.History.LastOrDefault();
            if (recent != null) ownIds.UnionWith(recent.Logistics.Where(l => l.Camp == CampId).Select(l => l.Hunter));
            RecentOwnExtraction = new ReadOnlyDictionary<int, double>(index.Extraction(ownIds).Where(t => visible.Contains(t.Tile)).GroupBy(t => t.Tile).ToDictionary(g => g.Key, g => g.Sum(t => t.Food)));
            Experience = Array.AsReadOnly((double[])camp.Experience.Clone());
        }
        private static int PredictHerdDestination(HerdObservation herd, IReadOnlyDictionary<int, TileObservation> tiles, IReadOnlyList<UnitObservation> hunters, HexMap map)
        {
            var candidates = map.Radius(herd.Tile, 1).Where(n => tiles.TryGetValue(n, out var tile) && tile.Land && tile.ElevationLevel < 10).ToArray();
            if (candidates.Length == 0) return herd.Tile;
            bool threatened = hunters.Any(h => map.Distance(herd.Tile, h.Tile) <= 1);
            if (threatened)
                return candidates.OrderByDescending(n => hunters.Min(h => map.Distance(n, h.Tile))).ThenByDescending(n => tiles[n].Freshwater).ThenByDescending(n => tiles[n].PotentialFood).ThenBy(n => n).First();
            var current = tiles[herd.Tile];
            if (current.Freshwater >= 2 && current.PotentialFood >= 2) return herd.Tile;
            return candidates.OrderByDescending(n => tiles[n].Freshwater + tiles[n].PotentialFood).ThenBy(n => n == herd.Tile ? 0 : 1).ThenBy(n => n).First();
        }
    }
    public sealed class AgentObservationBuilder
    {
        public AgentObservation Build(WorldState world, int player, PlanningIndex index = null)
        { index = index ?? new PlanningIndex(world); return new AgentObservation(world, index.OwnCamps(player).First(), index); }
    }
    [Serializable]
    public sealed class CandidateTrace
    {
        public string Plan, Components;
        public double Score;
        public bool Selected;
    }
    [Serializable]
    public sealed class DecisionTrace
    {
        public int Player, Turn;
        public List<CandidateTrace> Candidates = new List<CandidateTrace>();
    }
    public sealed class PolicyDecision
    {
        public List<Order> Orders = new List<Order>();
        public DecisionTrace Trace = new DecisionTrace();
    }
    public interface IAgentPolicy { PolicyDecision Decide(AgentObservation observation, Mandate mandate); }
    public static class ManagedBreedingPolicy
    {
        public static double Recommend(AgentObservation o, SettlementObservation settlement)
        {
            var herds = o.Herds.Where(h => h.OwnedDomestic && h.DomesticCamp == settlement.Id && (h.Count.Representative > 0 || h.StartingAnimals > 0)).ToArray();
            if (herds.Length == 0) return .55;
            double starting = herds.Sum(h => Math.Max(0, h.StartingAnimals));
            double loss = starting <= 0 ? 0 : herds.Sum(h => Math.Max(0, h.StartingAnimals - h.SurvivingAnimals)) / starting;
            double nutrition = herds.Min(h => h.Nutrition), management = herds.Min(h => h.ManagementFulfillment);
            double burden = settlement.Population <= 0 ? 1 : settlement.Economy.Assignments.Where(a => a.Role == WorkforceRoles.Herder).Sum(a => a.Population) / (double)settlement.Population;
            // These policy levels are first-pass AI heuristics, not biological constants.
            // They use only the observer's own herd diagnostics and visible traversable land.
            var local = o.Map.Radius(settlement.Tile, 1).Where(id => id != settlement.Tile && o.Tiles.TryGetValue(id, out var t) && t.Land && t.ElevationLevel < 10).Select(id => o.Tiles[id]).ToArray();
            bool room = local.Count(t => t.PotentialFood >= 2 && t.Freshwater >= 2) >= 3;
            bool occupiedAdequate = herds.All(h => o.Tiles.TryGetValue(h.Tile, out var t) && t.PotentialFood >= 2 && t.Freshwater >= 2);
            bool occupiedAmple = herds.All(h => o.Tiles.TryGetValue(h.Tile, out var t) && t.PotentialFood >= 3 && t.Freshwater >= 3);
            if (management >= .8 && burden <= .6 && occupiedAmple && room && (loss >= .25 || settlement.RecentlyMoved)) return .65;
            if (nutrition < .95 || management < .8 || burden > .6 || !occupiedAdequate || !room) return .45;
            return .55;
        }
    }
    public sealed class ScriptedTestPolicy : IAgentPolicy
    {
        private readonly Func<AgentObservation, List<Order>> script;
        public ScriptedTestPolicy(Func<AgentObservation, List<Order>> script) { this.script = script; }
        public PolicyDecision Decide(AgentObservation observation, Mandate mandate) => new PolicyDecision { Orders = script(observation) };
    }
    public sealed class HeuristicTribePolicy : IAgentPolicy
    {
        private sealed class Candidate
        {
            public Order Order;
            public double Score;
            public string Components;
        }
        private static double Gathering(AgentObservation o, int tile)
        {
            double score = 0;
            foreach (int n in o.Map.Radius(tile, 1))
                if (o.Tiles.TryGetValue(n, out var t) && t.Terrain != Terrain.Ocean) score += t.Food * .22 + t.Productivity * .12 + t.Condition * .04;
            return score;
        }
        private static double HerdSupport(AgentObservation o, int tile) => o.Herds.Where(h => !h.Domestic).Select(h => h.Size * .9 + Math.Max(0, 4 - o.Map.Distance(tile, h.Tile)) * .65).DefaultIfEmpty(0).Max();
        private static double FishingSupport(AgentObservation o, int tile) => o.Map.Radius(tile, 1).Where(n => o.Tiles.TryGetValue(n, out var observed) && observed.Aquatic).Select(n => o.Tiles[n].Food * .8 + o.Tiles[n].Productivity * .2 + o.Tiles[n].Condition * .1).DefaultIfEmpty(0).Max();
        private static bool Enter(AgentObservation o, int tile, bool hunter) => o.Tiles.TryGetValue(tile, out var t) && t.ElevationLevel < 10 && (t.Land || hunter && o.Watercraft && t.Terrain == Terrain.Shallows);
        private static bool Reach(AgentObservation o, int start, int target, bool hunter) => o.Map.Reachable(start, target, hunter ? o.HunterMovement : o.CampMovement, n => Enter(o, n, hunter), n => o.Tiles[n].Aquatic ? 2 : 1);
        private static double Practice(AgentObservation o, Domain domain, Mandate m) => o.Experience[(int)domain] / Math.Max(1, o.Experience.Sum()) * m.Persistence;
        public PolicyDecision Decide(AgentObservation o, Mandate m)
        {
            var result = new PolicyDecision { Trace = new DecisionTrace { Player = o.Player, Turn = o.Turn } };
            if (o.LinkedPopulation <= 0) return result;
            bool knownFishingWater = o.Tiles.Values.Any(tile => tile.Aquatic && tile.Food >= 1);
            double huntPreference = m.Hunting * o.HuntingInclination + Practice(o, Domain.Hunting, m);
            double gatherPreference = m.Gathering * o.GatheringInclination + Practice(o, Domain.Gathering, m);
            double fishPreference = knownFishingWater ? m.Fishing * o.FishingInclination + Practice(o, Domain.Fishing, m) : 0;
            bool huntingFocus = huntPreference > Math.Max(gatherPreference, fishPreference);
            bool fishingFocus = fishPreference > Math.Max(huntPreference, gatherPreference);
            double need = 1 + o.DeficitTurns * .5 + (o.FoodCondition <= 1 ? .5 : 0);
            var campCandidates = new List<Candidate>();
            double current = Gathering(o, o.CampTile);
            if (o.Settlements.Any(s => s.Id == o.CampId && s.IsVillage))
            {
                campCandidates.Add(new Candidate { Order = new Order(OrderKind.NoOp, o.CampId), Score = 100, Components = "VillageContinuity" });
            }
            else
            {
                foreach (int n in o.Map.Radius(o.CampTile, o.CampMovement).OrderBy(n => n))
                {
                    if (!Reach(o, o.CampTile, n, false) || o.Rivals.Any(r => r.Tile == n)) continue;
                    double food = Gathering(o, n) * need * m.Gathering;
                    double continuity = n == o.CampTile ? Math.Max(.25, current * o.Inertia) : -(1 - m.Mobility) * .1;
                    double herdSupport = HerdSupport(o, n) * m.Hunting * (huntingFocus ? 4 : .35 + .85 * o.HuntingFieldcraft);
                    double fishingSupport = FishingSupport(o, n) * m.Fishing * (fishingFocus ? 3 : .2);
                    campCandidates.Add(new Candidate { Order = n == o.CampTile ? new Order(OrderKind.NoOp, o.CampId) : new Order(OrderKind.Move, o.CampId, destination: n), Score = food + continuity + herdSupport + fishingSupport, Components = "GatheringOpportunity + HerdSupport + FishingSupport + SurvivalNeed + RelocationInertia" });
                }
            }
            var campChoice = Select(o, campCandidates, result.Trace);
            int plannedCamp = campChoice.Destination >= 0 ? campChoice.Destination : o.CampTile;
            bool huntOpportunity = o.Herds.Any(h => !h.Domestic && o.Map.Distance(o.CampTile, h.Tile) <= 2);
            bool fishingOpportunity = o.Map.Radius(o.CampTile, 2).Any(n => o.Tiles.TryGetValue(n, out var t) && t.Aquatic && t.Food >= 1);
            bool opportunity = huntingFocus || fishingFocus || huntOpportunity || fishingOpportunity;
            int assignedHunters = o.Hunters.Sum(h => h.Population);
            double hunterShare = huntingFocus ? .55 + m.Risk * .1 : fishingFocus ? .30 : huntOpportunity ? .35 + m.Risk * .15 : .25 + m.Risk * .1;
            int desiredHunters = opportunity ? Math.Max(8, (int)Math.Round(o.LinkedPopulation * hunterShare)) : 0;
            int dispatchable = Math.Min(o.GathererPopulation, o.CampPopulation - o.MinimumCamp - (huntingFocus ? 0 : 4));
            int allocation = Math.Min(8, Math.Min(Math.Max(0, desiredHunters - assignedHunters), dispatchable));
            if ((campChoice.Kind == OrderKind.NoOp || huntingFocus || fishingFocus && fishingOpportunity) && allocation >= 4 && opportunity && o.DeficitTurns == 0 && o.FoodCondition >= 1)
            { campChoice = new Order(OrderKind.Dispatch, o.CampId, population: allocation); result.Trace.Candidates.Add(new CandidateTrace { Plan = campChoice.ToString(), Components = "Growing population and visible subsistence opportunity; hunter share expands while preserving the camp reserve", Score = 1, Selected = true }); }
            var agriculturalCamp = o.Settlements.FirstOrDefault(c => c.Id == o.CampId && (c.Agriculture || c.Husbandry));
            if (agriculturalCamp != null) { var economy = agriculturalCamp.RecommendedEconomy.Copy(); if (agriculturalCamp.Husbandry) economy.ManagedBreedingIntensity = ManagedBreedingPolicy.Recommend(o, agriculturalCamp); campChoice = new Order(OrderKind.Economy, o.CampId) { Economy = economy }; plannedCamp = o.CampTile; }
            result.Orders.Add(campChoice);
            var observedHunters = o.Hunters.Concat(o.ForeignHunters).ToArray();
            var wildHerds = o.Herds.Where(h => !h.Domestic).OrderByDescending(h => h.Size).ThenBy(h => o.Map.Distance(o.CampTile, h.Tile)).ThenBy(h => h.Id).ToArray();
            int preferredHerd = wildHerds.Select(h => h.Id).DefaultIfEmpty(-1).First();
            int hunterIndex = 0;
            foreach (var hunter in o.Hunters)
            {
                int distributedHerd = wildHerds.Length == 0 ? -1 : wildHerds[hunterIndex++ % wildHerds.Length].Id;
                double logisticsUrgency = !hunter.InSupply ? hunter.Provisions <= 1 || hunter.DeficitTurns > 0 ? 4 : hunter.Provisions >= 3 ? 2.5 : .8 : 0;
                var candidates = new List<Candidate> { new Candidate { Order = new Order(OrderKind.NoOp, hunter.Id), Score = -.5, Components = "Wait" } };
                foreach (var herd in o.Herds.Where(h => !h.Domestic))
                {
                    // Hunters choose positions, not a separate attack order. Sharing a post-movement tile with wild prey triggers hunting automatically.
                    // Score every visible one-step herd destination so multiple groups can cover a migration rather than betting on one opaque prediction.
                    bool threatened = observedHunters.Any(h => o.Map.Distance(h.Tile, herd.Tile) <= 1);
                    int maximumEscape = threatened ? o.Map.Radius(herd.Tile, 1).Where(n => Enter(o, n, false)).Max(n => observedHunters.Min(h => o.Map.Distance(h.Tile, n))) : 0;
                    foreach (int target in o.Map.Radius(herd.Tile, 1).Where(n => Enter(o, n, false)).OrderBy(n => n))
                    {
                        if (!Reach(o, hunter.Tile, target, true)) continue;
                        double likely = threatened ? observedHunters.Min(h => o.Map.Distance(h.Tile, target)) == maximumEscape ? 2 + o.Tiles[target].Freshwater * .3 + o.Tiles[target].PotentialFood * .05 - target * 1e-6 : 0 : (target == herd.Tile ? .55 : .05) + o.Tiles[target].Freshwater * .035 + o.Tiles[target].PotentialFood * .025;
                        if (o.LikelyHerdDestinations.TryGetValue(herd.Id, out int tracked) && tracked == target) likely += .8 + 1.2 * o.HuntingFieldcraft;
                        var pursuit = target == hunter.Tile ? new Order(OrderKind.NoOp, hunter.Id) : new Order(OrderKind.Move, hunter.Id, destination: target);
                        double concentration = assignedHunters > o.SustainableHuntPopulation ? herd.Id == preferredHerd ? 2.5 : 0 : huntingFocus && wildHerds.Length > 1 && herd.Id == distributedHerd ? 2.5 : 0;
                        double huntingPreference = huntingFocus ? 1.6 : fishingFocus ? .15 : 1;
                        candidates.Add(new Candidate { Order = pursuit, Score = (2.3 + herd.Size * .35 + likely + concentration) * m.Hunting * huntingPreference + Practice(o, Domain.Hunting, m) - o.Map.Distance(hunter.Tile, target) * .15 - need * (1 - m.Risk) * .25, Components = "HuntOpportunity + MigrationCoverage + GroupConcentration + SpecializationContinuity - TravelCost - PopulationRisk" });
                    }
                }
                if (!huntingFocus)
                    foreach (int shore in o.Map.Radius(hunter.Tile, o.HunterMovement).OrderBy(n => n))
                    {
                        if (!Reach(o, hunter.Tile, shore, true)) continue;
                        foreach (int fish in o.Map.Radius(shore, 1))
                            if (o.Tiles.TryGetValue(fish, out var tile) && tile.Aquatic)
                                candidates.Add(new Candidate { Order = new Order(OrderKind.Fish, hunter.Id, fish, shore), Score = (tile.Food * .8 + tile.Productivity * .15) * m.Fishing * need * (fishingFocus ? 5 : 1) + Practice(o, Domain.Fishing, m) - o.Map.Distance(hunter.Tile, shore) * .12, Components = "FishingOpportunity + SurvivalNeed + SpecializationContinuity - TravelCost" });
                    }
                if (hunter.Tile == o.CampTile && plannedCamp == o.CampTile)
                    candidates.Add(new Candidate { Order = new Order(OrderKind.Reintegrate, hunter.Id, o.CampId), Score = o.DeficitTurns > 0 ? 3.3 : huntingFocus ? -1 : 1.4, Components = "ReintegrationValue + PopulationRisk" });
                foreach (int n in o.Map.Radius(hunter.Tile, o.HunterMovement).OrderBy(n => n))
                {
                    if (n == hunter.Tile || !Reach(o, hunter.Tile, n, true)) continue;
                    double returnValue = o.Map.Distance(hunter.Tile, plannedCamp) - o.Map.Distance(n, plannedCamp);
                    int unknown = o.Map.Radius(n, 2).Count(t => !o.Tiles.ContainsKey(t));
                    bool nomadicScouting = huntingFocus && wildHerds.Length == 0 && (hunter.InSupply || hunter.Provisions >= 2);
                    double returnWeight = nomadicScouting ? .1 : .5 + logisticsUrgency;
                    double explorationWeight = nomadicScouting ? .18 : .035;
                    double waterSearch = fishingFocus ? FishingSupport(o, n) * 1.5 : 0;
                    candidates.Add(new Candidate { Order = new Order(OrderKind.Move, hunter.Id, destination: n), Score = returnValue * returnWeight + unknown * explorationWeight * m.Exploration + waterSearch - need * .15, Components = "SupplyReturn + ReintegrationValue + ExplorationValue + FishingSupport - SurvivalNeed" });
                }
                result.Orders.Add(Select(o, candidates, result.Trace));
            }
            return result;
        }
        private static Order Select(AgentObservation o, List<Candidate> candidates, DecisionTrace trace)
        {
            candidates = candidates.Where(c => OrderRules.Validate(o, c.Order) == null).ToList();
            if (candidates.Count == 0) return new Order(OrderKind.NoOp, o.CampId);
            var best = candidates.OrderByDescending(c => c.Score).ThenBy(c => Numbers.Hash(o.PolicySeed, 91, c.Order.Entity ^ c.Order.Target ^ c.Order.Destination, o.Turn)).ThenBy(c => c.Order.ToString(), StringComparer.Ordinal).First();
            foreach (var c in candidates) trace.Candidates.Add(new CandidateTrace { Plan = c.Order.ToString(), Score = c.Score, Components = c.Components, Selected = c == best });
            return best.Order;
        }
    }
}
