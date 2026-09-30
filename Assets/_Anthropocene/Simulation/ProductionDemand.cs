using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // One manufactured good's useful demand for a planning turn, with the
    // per-unit terms a fulfilment rule needs. Inputs per unit come from Recipe.
    public sealed class ProductionClaim
    {
        public CraftGood Good;
        public CraftRecipe Recipe;
        // Useful demand at full fulfilment. Farm Tools stay zero here: they are
        // sized later from the bounded field workforce.
        public double Quantity;
        // Crafting priority on the prepared preview camp.
        public double Priority;
        // Fulfilment weight for value-based rules. The uniform rule ignores it;
        // it stays Priority unless the Value rule is configured, when it is the
        // first-unit value V (person-QoL-years).
        public double Value;
        // Value-model inputs, always computed by the demand pass (pure, O(1) per
        // claim) but read only by ValueFulfilment. Stock and Need (D = Stock +
        // Quantity) are in units of the good; LabourPerUnit is the closed-form
        // worker-years per unit; FirstUnitValue is V. Zero means "not valued":
        // Pots, Farm Tools and Herding Tools keep the uniform share in WP3.
        public double Stock, Need, LabourPerUnit, FirstUnitValue;
        // True for the four need goods the value model prices (clothing, shelter).
        public bool Valued;
        // Role that performs the work and its output per worker-interval.
        public string Role;
        public double Rate;
    }

    // A fulfilment rule maps a position s along one path from "pursue nothing"
    // (s = 0) to "pursue all useful demand" (s = 1) to the share of each claim's
    // quantity the planner pursues. Shares must be nondecreasing in s so the
    // planner can search that path for the largest position the residents can
    // staff. Rules are pure and read only the demand pass.
    public interface IFulfilmentRule
    {
        string Name { get; }
        void Shares(IReadOnlyList<ProductionClaim> claims, double s, double[] shares);
    }

    // Every claim is pursued to the same share of its useful demand.
    public sealed class UniformFulfilment : IFulfilmentRule
    {
        public static readonly UniformFulfilment Instance = new UniformFulfilment();
        public string Name => "Uniform";
        public void Shares(IReadOnlyList<ProductionClaim> claims, double s, double[] shares)
        {
            for (int i = 0; i < claims.Count; i++) shares[i] = s;
        }
    }

    // Value-based fulfilment (ValueModelDesign.md, "Fulfilment rule: search on w").
    // Each need claim buys coverage c*(w) = ln(V / (w * l)) / lambda at labour
    // price w, so its output is clamp(c* * D - Stock, 0, Quantity). Coverage is
    // measured against the need level D (this turn's requirement), which useful
    // demand may exceed. The path position s maps to w on a log scale from w_hi
    // (nothing worth making) to w_lo (every claim at its full useful demand), so
    // shares are nondecreasing in s.
    // Goods the value model does not price (Pots, Farm and Herding Tools) take
    // the uniform share s, as does a priced good with no usable labour estimate;
    // a priced good whose value is zero (priority 0, or B = 0) gets share 0.
    // With no need claim at all the rule is uniform. Pure: reads only the claims.
    public sealed class ValueFulfilment : IFulfilmentRule
    {
        public readonly double Steepness;
        public ValueFulfilment(double steepness)
        {
            if (!(steepness > 0)) throw new ArgumentException("Value curve steepness must be positive.");
            Steepness = steepness;
        }
        public string Name => "Value";
        public static bool IsNeed(ProductionClaim c)
            => c.Quantity > 0 && c.Need > 0 && c.FirstUnitValue > 0 && c.LabourPerUnit > 0
               && !double.IsInfinity(c.LabourPerUnit) && !double.IsInfinity(c.FirstUnitValue);
        // V * exp(-lambda * c) at the coverage reached by `share` of the useful quantity.
        public double MarginalValue(ProductionClaim c, double share)
            => c.FirstUnitValue * Math.Exp(-Steepness * (c.Stock + share * c.Quantity) / c.Need);
        private bool Bounds(IReadOnlyList<ProductionClaim> claims, out double hi, out double lo)
        {
            hi = 0; lo = double.PositiveInfinity; bool any = false;
            for (int i = 0; i < claims.Count; i++)
            {
                var c = claims[i];
                if (!IsNeed(c)) continue;
                any = true;
                double first = c.FirstUnitValue / c.LabourPerUnit;
                if (first > hi) hi = first;
                // Full useful demand may exceed the need level (clothing carries a
                // finished-goods reserve beyond this turn's requirement), so the
                // path's end is the price at which every claim reaches Stock + Quantity.
                double full = first * Math.Exp(-Steepness * (c.Stock + c.Quantity) / c.Need);
                if (full < lo) lo = full;
            }
            return any;
        }
        // The labour price w at path position s; NaN when no claim is valued.
        public double LaborPrice(IReadOnlyList<ProductionClaim> claims, double s)
        {
            if (!Bounds(claims, out double hi, out double lo)) return double.NaN;
            return hi * Math.Pow(lo / hi, Numbers.Clamp(s, 0, 1));
        }
        public void Shares(IReadOnlyList<ProductionClaim> claims, double s, double[] shares)
        {
            if (!Bounds(claims, out double hi, out double lo))
            {
                for (int i = 0; i < claims.Count; i++) shares[i] = s;
                return;
            }
            double price = hi * Math.Pow(lo / hi, Numbers.Clamp(s, 0, 1));
            for (int i = 0; i < claims.Count; i++)
            {
                var c = claims[i];
                if (!IsNeed(c)) { shares[i] = c.Valued && c.FirstUnitValue <= 0 ? 0 : s; continue; }
                // The path ends are exact: floating error must not leave residue at s = 0 or a gap at s = 1.
                if (s <= 0) { shares[i] = 0; continue; }
                if (s >= 1) { shares[i] = 1; continue; }
                double coverage = Math.Log(c.FirstUnitValue / (price * c.LabourPerUnit)) / Steepness;
                shares[i] = Numbers.Clamp((coverage * c.Need - c.Stock) / c.Quantity, 0, 1);
            }
        }
    }

    // Demand pass of the economic planner: everything a plan needs that does not
    // depend on the fulfilment level, built once per automated camp from the
    // prepared boundary. The fulfilment search reads it and never rebuilds it.
    internal sealed class CampProductionDemand
    {
        public bool CultivationEnabled, HusbandryEnabled, Established;
        public int[] Footprint, Operated, Eligible;
        public double Upkeep, ForageSupply, PlantSupply, ForageAfterHerds, TerrestrialAfterHerds, Supply, Biological, NextPopulation;
        public int ReservedHerders, Consumers;
        public ManagedFoodForecast ManagedFood;
        public KnownMaterialSource[] Sources;
        public Dictionary<MaterialId, double> Accessible;
        public CraftRecipe[] Recipes, Goods;
        public Camp Preview;
        public Dictionary<CraftGood, double> Goals;
        public IReadOnlyList<ProductionClaim> Claims;
        // Value-rule production order (WP3b); null under the Uniform rule.
        public ProductionOrder Order;
        public double Meals, OperatingReserve, SparePlant, PlantEnvelope, AnimalEnvelope, FoodRoomEnvelope, HousingMultiplier;

        private WorldState world;
        private bool? retainedExpansionPossible;
        // Lazily evaluated: only expansion decisions ask, and the answer is pure.
        public bool HasRetainedExpansionGain()
        {
            if (!retainedExpansionPossible.HasValue)
                retainedExpansionPossible = Operated.Any(id => EconomicPlanner.ExpansionGain(world, id) > EconomicPlanner.Epsilon);
            return retainedExpansionPossible.Value;
        }

        // Normalizes the plan's focus to the camp footprint before the preview
        // is built from it; every other read is of the committed boundary.
        public static CampProductionDemand Build(WorldState w, Camp c, EconomicIntent e)
        {
            var d = new CampProductionDemand { world = w };
            var cfg = w.Config; var n = cfg.Neolithic;
            double intervals = cfg.GenerationIntervals, years = cfg.TurnDays / cfg.DaysPerYear;
            d.CultivationEnabled = CultivationEconomy.Enabled(c);
            d.HusbandryEnabled = CultivationEconomy.HusbandryEnabled(c);
            // The configured one-tile work radius lies wholly inside the owner's
            // two-tile vision. No global own-group visibility scan is needed here.
            var footprint = d.Footprint = d.CultivationEnabled ? CultivationEconomy.Footprint(w, c).ToArray() : Array.Empty<int>();
            if (!footprint.Contains(e.Focus)) e.Focus = -1;
            var operated = d.Operated = footprint.Where(id => w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == c.Id).ToArray();
            d.Upkeep = d.CultivationEnabled ? CultivationEconomy.MaintenanceFarmersForPlanning(w, c, years) : 0;
            d.Established = d.CultivationEnabled && operated.Any(id => LazyEcology.ProjectEcology(w, id, w.Days).CultivationCapital > 0);
            d.Eligible = w.Map.Radius(c.Tile, cfg.GatherRadius).Where(id => PassiveGathering.Eligible(w, c, id)).OrderBy(id => id).ToArray();
            // Ask the same finite-stock kernel that resolves actual claims for
            // the no-contention supply envelope. Aquatic food keeps its own
            // horizon flow; land can only yield its opening accessible stock.
            double FoodAvailability(int id)
            {
                double request = LazyEcology.AccessibleAvailability(w, id, w.Days,
                    cfg.TurnDays, operated.Contains(id) ? 1 : 0);
                if (request <= 0) return 0;
                var claim = new Demand { Tile = id, Entity = c.Id,
                    Kind = PassiveGathering.Kind(w.Tiles[id]), Food = request };
                return FoodHorizonForecast.Tile(w, id, w.Days, cfg.TurnDays,
                    new[] { claim }, null, null, operated.Contains(id) ? 1 : (double?)null)
                    .HumanFoodReceipts;
            }
            d.ForageSupply = d.Eligible.Sum(FoodAvailability); d.PlantSupply = d.Eligible.Where(id => w.Tiles[id].Land).Sum(FoodAvailability);
            d.ReservedHerders = d.HusbandryEnabled ? EconomicPlanner.ManagedHerderReservation(w, c, years) : 0;
            d.ManagedFood = d.HusbandryEnabled ? TurnResolver.ForecastManagedAnimalFood(w, c, d.ReservedHerders, intervals, years, c.Tile) : new ManagedFoodForecast();
            d.ForageAfterHerds = Math.Max(0, d.ForageSupply - d.ManagedFood.HumanFoodDisplaced);
            d.TerrestrialAfterHerds = Math.Max(0, d.PlantSupply - d.ManagedFood.HumanFoodDisplaced);
            d.Supply = d.ForageAfterHerds + d.ManagedFood.AnimalFood;
            d.Biological = TurnResolver.PlanningFoodDemand(w, c);
            // This plan belongs to the population present at the interturn boundary.
            // Births are added after its goods and QoL have been resolved.
            d.NextPopulation = c.Population;
            var sources = d.Sources = MaterialEconomy.KnownSources(w, c, w.Days);
            double Priority(MaterialId m) => m == MaterialId.Stone ? e.StonePriority : m == MaterialId.Clay ? e.ClayPriority : e.WoodPriority;
            d.Accessible = MaterialEconomy.Materials.ToDictionary(m => m, m => Priority(m) <= 0 || n.SpecialistMaterialExtractionRate <= 0 ? 0 : sources.Where(s => s.Material == m).GroupBy(s => s.Id).Sum(g => g.Sum(s => s.Available)));
            d.Recipes = NeolithicProduction.Recipes(cfg);
            var previewIntent = e.Copy();
            if (d.ReservedHerders > 0) previewIntent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = d.ReservedHerders });
            // A single prospective builder/crafter makes downstream shelter
            // demand visible to the ordered Animal Fabric recipe. This is only
            // a demand probe; actual staffing still comes from the shared
            // material, tool, food and resident budgets in the fulfilment search.
            if (d.HusbandryEnabled && NeolithicProduction.Priority(c, CraftGood.LightHousing) > 0 && c.Population > d.ReservedHerders)
                previewIntent.Assignments.Add(new PopulationAssignment { Role = c.People.HasTech(NeolithicTech.Construction) ? WorkforceRoles.Builder : WorkforceRoles.Craftsperson, Population = 1 });
            var preview = d.Preview = EconomicPlanner.Preview(c, previewIntent, d.NextPopulation);
            NeolithicProduction.DepreciateHousing(w, preview, years);
            d.Goods = d.Recipes.Where(r => r.Good != CraftGood.HandTools && r.Good != CraftGood.Tools && r.Good != CraftGood.HousingMaterial && (!r.HusbandryOnly || d.HusbandryEnabled)).ToArray();
            var goals = d.Goals = d.Goods.ToDictionary(r => r.Good, r => NeolithicProduction.Priority(preview, r.Good) > 0 ? NeolithicProduction.Demand(w, preview, r.Good, years, -1, false, false, sources) : 0);
            // FarmTools are sized only after a proposal has a bounded field
            // workforce. The empty preview cannot answer that question without
            // inventing labor, so the normal FarmTools demand helper is called
            // on the resolver-shaped candidate.
            if (goals.ContainsKey(CraftGood.FarmTools)) goals[CraftGood.FarmTools] = 0;
            d.Claims = d.Goods.Select(r =>
            {
                bool specialist = c.People.HasTech(r.Track);
                double priority = NeolithicProduction.Priority(preview, r.Good);
                return new ProductionClaim { Good = r.Good, Recipe = r, Quantity = goals[r.Good], Priority = priority, Value = priority,
                    Role = specialist ? r.Specialist : WorkforceRoles.Craftsperson, Rate = r.Rate * (specialist ? 1 : n.GeneralistTaskEfficiency) };
            }).ToArray();
            int consumers = d.Consumers = c.Population + w.Hunters.Where(h => h.SourceCamp == c.Id && Simulation.Supply.InRange(w, c, h.Tile)).Sum(h => h.Population);
            d.Meals = consumers * cfg.Consumption * intervals;
            d.OperatingReserve = NeolithicProduction.DesiredReserveFood(w, consumers);
            d.SparePlant = Math.Min(c.PlantFood, Math.Max(0, c.Food - d.Meals - d.OperatingReserve));
            d.PlantEnvelope = Math.Min(d.TerrestrialAfterHerds + d.SparePlant, Math.Max(0, c.Food + d.Supply - consumers * cfg.Consumption * intervals));
            double animalSurplus = Math.Max(0, c.Food + d.Supply - d.Meals - d.OperatingReserve);
            d.AnimalEnvelope = d.HusbandryEnabled ? Math.Min(Math.Max(0, c.AnimalFood) + d.ManagedFood.AnimalFood, Math.Max(0, animalSurplus - d.SparePlant)) : 0;
            if (d.HusbandryEnabled)
            {
                double reserve = Math.Min(NeolithicProduction.DesiredReserveFood(w, consumers, intervals), NeolithicProduction.StorageCapacityAfterPotDepreciation(w, c, intervals, years));
                d.FoodRoomEnvelope = Math.Max(0, c.Food + d.Supply - consumers * cfg.Consumption * intervals - reserve);
            }
            d.HousingMultiplier = NeolithicProduction.HousingMultiplier(w, c, c.Population);
            ClaimValuation.Apply(w, c, d);
            return d;
        }
    }

    // Closed-form value inputs for the four need goods (clothing and shelter),
    // from committed camp terms; see ValueModelDesign.md, "Anchoring V in QoL".
    internal static class ClaimValuation
    {
        private static readonly Camp DefaultPriorities = new Camp { CraftingPriorities = new CraftingPriorities() };
        public static bool IsNeedGood(CraftGood g) => g == CraftGood.Cloth || g == CraftGood.AnimalFabric || g == CraftGood.Housing || g == CraftGood.LightHousing;
        public static void Apply(WorldState w, Camp c, CampProductionDemand d)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear, perYear = cfg.DaysPerYear / cfg.BaseDays;
            // Camp terms come from the committed camp: B = F * W, S at the
            // present cohort and E from the last resolved needs (or its formula
            // for a camp that has not yet resolved any).
            double b = c.FoodNeedsMetRatio * c.WaterNeedsMetRatio;
            double s = NeolithicProduction.SocialCohesion(n, c, c.Population);
            double e = c.RudimentaryExpectation > 0 ? c.RudimentaryExpectation
                : n.RudimentaryExpectationFloor + (1 - n.RudimentaryExpectationFloor) * Numbers.Clamp(c.StationaryYears / n.RudimentaryExpectationRampYears, 0, 1);
            double k = -Math.Log(1 - n.HousingDecayPerTurn) / n.ReferenceTurnYears;
            double life = 1 / (k + n.DiscountRatePerYear);
            double housingSupply = NeolithicProduction.HousingSupply(w, d.Preview, w.Days + years * cfg.DaysPerYear);
            double clothingV = n.RudimentaryWeightClothing * e * b * s * (n.ReferenceTurnYears / n.FabricConsumptionPerPersonTurn);
            double housingV = n.HousingPeoplePerUnit * n.RudimentaryWeightHousing * e * b * s * life;
            double gather = cfg.GatherPerPerson * perYear;
            double Rate(CraftRecipe r) => r.Rate * (c.People.HasTech(r.Track) ? 1 : n.GeneralistTaskEfficiency);
            double Extraction(MaterialId m, double perUnit)
            {
                if (perUnit <= 0 || n.SpecialistMaterialExtractionRate <= 0) return 0;
                bool specialist = c.People.HasTech(EconomicIntents.RequiredTechnology(MaterialEconomy.Specialist(m)));
                return perUnit / (n.SpecialistMaterialExtractionRate * (specialist ? 1 : n.GeneralistTaskEfficiency) * perYear);
            }
            // Worker-years per unit: craft time (divided by the tool multiplier the
            // staffing pass uses for shelter), input extraction, plant feedstock
            // gathering and, for Light Housing, its Animal Fabric input. Animal
            // Food is managed-herd output and costs no gathering labour here.
            double Labour(CraftRecipe r)
            {
                double rate = Rate(r);
                if (rate <= 0) return 0;
                double m = ToolCoverageBudget.IsHousingWork(r.Good) ? d.HousingMultiplier : 1;
                if (m <= 0) return 0;
                double l = 1 / (rate * perYear * m);
                l += Extraction(MaterialId.Stone, r.Stone) + Extraction(MaterialId.Clay, r.Clay) + Extraction(MaterialId.Wood, r.Wood + r.ConstructionMaterial);
                if (gather > 0) l += r.PlantFood / gather;
                if (r.AnimalFabric > 0)
                {
                    var input = d.Recipes.FirstOrDefault(x => x.Good == CraftGood.AnimalFabric);
                    double inputLabour = input == null ? 0 : Labour(input);
                    if (inputLabour <= 0) return 0;
                    l += r.AnimalFabric * inputLabour;
                }
                if (r.HousingMaterial > 0)
                {
                    // Housing Material is planned with its Housing claim, not as a claim of its own.
                    var input = d.Recipes.FirstOrDefault(x => x.Good == CraftGood.HousingMaterial);
                    double inputLabour = input == null ? 0 : Labour(input);
                    if (inputLabour <= 0) return 0;
                    l += r.HousingMaterial * inputLabour;
                }
                return l;
            }
            bool value = n.ValueFulfilmentSelected;
            // Clothing coverage is measured against this turn's requirement for the
            // prior cohort, with Cloth and Animal Fabric counted as one clothing
            // stock. Useful demand also carries the finished-goods reserve; reserve
            // units lie beyond coverage 1, where the curve values them little.
            // Shelter's useful demand is this turn's requirement already.
            double clothingNeed = d.Preview.Population * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears;
            double clothingStock = Math.Max(0, c.Inventory.Cloth) + Math.Max(0, c.Inventory.AnimalFabric);
            foreach (var claim in d.Claims)
            {
                if (!IsNeedGood(claim.Good)) continue;
                bool clothing = claim.Good == CraftGood.Cloth || claim.Good == CraftGood.AnimalFabric;
                double stock = clothing ? clothingStock : housingSupply;
                double defaultPriority = NeolithicProduction.Priority(DefaultPriorities, claim.Good);
                double multiplier = defaultPriority > 0 ? claim.Priority / defaultPriority : (claim.Priority > 0 ? 1 : 0);
                claim.Valued = true;
                claim.Stock = stock;
                claim.Need = clothing ? clothingNeed : stock + claim.Quantity;
                claim.LabourPerUnit = Labour(claim.Recipe);
                claim.FirstUnitValue = (claim.Good == CraftGood.Cloth || claim.Good == CraftGood.AnimalFabric ? clothingV : housingV) * multiplier;
                if (value) claim.Value = claim.FirstUnitValue;
            }
            if (value) d.Order = ProductionOrder.Build(cfg, d.Claims, d.Recipes, good => NeolithicProduction.Priority(d.Preview, good));
        }
    }

    // The Value rule's production order for one camp and turn (WP3b): the order in
    // which recipes claim every shared scarce input (tool coverage, Stone, Clay,
    // Wood, feedstock, Animal Fabric). Computed once at preparation from the
    // committed start-of-turn stocks, then fixed: nothing recomputes it mid-turn.
    // Transient: never saved, observed or fingerprinted, and absent (null) for
    // manual camps and under the Uniform rule, which keep the crafting-priority
    // and role order exactly.
    //   need goods (Cloth, Animal Fabric, Housing, Light Housing): V * exp(-lambda * c0),
    //     c0 = Stock / Need, the value of the next unit at start-of-turn coverage;
    //   Housing Material: its Housing's key (it precedes the assembly on ties);
    //   Hand and Regular Tools: outputs covered per tool (1 and 4) times the best
    //     need-good key among equipment goods, discounted one turn by exp(-r * years),
    //     because tools made now serve the next interval, never this one;
    //   goods the model does not price (Pots, Farm and Herding Tools): after all of
    //     those, in crafting-priority order (WP4 prices them).
    // Ties break by ScheduleKey. Animal Fabric always precedes Light Housing and
    // Housing Material always precedes Housing, whatever their keys.
    public sealed class ProductionOrder
    {
        public static readonly string[] Roles = WorkforceRoles.Specialists.Concat(new[] { WorkforceRoles.Craftsperson }).ToArray();
        private readonly Dictionary<CraftGood, int> rank = new Dictionary<CraftGood, int>();
        private readonly Dictionary<CraftGood, double> keys = new Dictionary<CraftGood, double>();
        // Goods from first to last in access order.
        public readonly CraftGood[] Goods;
        private ProductionOrder(CraftGood[] goods, Dictionary<CraftGood, double> valued)
        {
            Goods = goods;
            for (int i = 0; i < goods.Length; i++) rank[goods[i]] = i;
            foreach (var pair in valued) keys[pair.Key] = pair.Value;
        }
        // The value key of a priced good; NaN for goods placed by priority.
        public double Key(CraftGood good) => keys.TryGetValue(good, out double key) ? key : double.NaN;
        public int Rank(CraftGood good) => rank.TryGetValue(good, out int r) ? r : int.MaxValue;
        // True when `first` is reached before `second` in access order.
        public bool Outranks(CraftGood first, CraftGood second) => Rank(first) < Rank(second);
        public override string ToString() => string.Join(">", Goods);

        internal static ProductionOrder Build(SimulationConfig cfg, IReadOnlyList<ProductionClaim> claims, CraftRecipe[] recipes, Func<CraftGood, double> priority)
        {
            var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear;
            var valued = new Dictionary<CraftGood, double>();
            foreach (var claim in claims)
                if (claim.Valued && ValueFulfilment.IsNeed(claim))
                    valued[claim.Good] = claim.FirstUnitValue * Math.Exp(-n.ValueCurveSteepness * claim.Stock / claim.Need);
            if (valued.TryGetValue(CraftGood.Housing, out double housingKey)) valued[CraftGood.HousingMaterial] = housingKey;
            // The value of the outputs a unit of coverage lets the camp make, at the
            // best equipment need good; coverage from tools made now starts next turn.
            double best = double.NaN;
            foreach (var recipe in recipes)
                if (recipe.Equipment && valued.TryGetValue(recipe.Good, out double key) && !(key <= best)) best = key;
            if (!double.IsNaN(best))
            {
                double discount = Math.Exp(-n.DiscountRatePerYear * years);
                valued[CraftGood.HandTools] = ToolCoverageBudget.OutputsPerHandTool * best * discount;
                valued[CraftGood.Tools] = ToolCoverageBudget.OutputsPerTool * best * discount;
            }
            var pricedRecipes = recipes.Where(r => valued.ContainsKey(r.Good))
                .OrderByDescending(r => valued[r.Good]).ThenBy(r => NeolithicProduction.ScheduleKey(r.Good)).ToList();
            var rest = recipes.Where(r => !valued.ContainsKey(r.Good))
                .OrderByDescending(r => priority(r.Good)).ThenBy(r => NeolithicProduction.ScheduleKey(r.Good));
            var order = pricedRecipes.Concat(rest).Select(r => r.Good).ToList();
            MoveBefore(order, CraftGood.AnimalFabric, CraftGood.LightHousing);
            MoveBefore(order, CraftGood.HousingMaterial, CraftGood.Housing);
            return new ProductionOrder(order.ToArray(), valued);
        }
        // Sequencing rule: an input recipe is reached before the recipe that consumes it.
        private static void MoveBefore(List<CraftGood> order, CraftGood input, CraftGood consumer)
        {
            int i = order.IndexOf(input), j = order.IndexOf(consumer);
            if (i < 0 || j < 0 || i < j) return;
            order.RemoveAt(i); order.Insert(j, input);
        }

        // Recipes in access order: the crafting-priority order when there is no
        // Value order (exactly today's sort), the value order otherwise.
        public static CraftRecipe[] Sort(IEnumerable<CraftRecipe> recipes, Func<CraftGood, double> priority, ProductionOrder order)
            => order == null
                ? recipes.OrderByDescending(r => priority(r.Good)).ThenBy(r => NeolithicProduction.ScheduleKey(r.Good)).ToArray()
                : recipes.OrderBy(r => order.Rank(r.Good)).ThenByDescending(r => priority(r.Good)).ThenBy(r => NeolithicProduction.ScheduleKey(r.Good)).ToArray();

        // The (role, recipe) sequence of one production layer over recipes already
        // in access order. Without a Value order the roles run in turn (today's
        // schedule); with one every recipe is visited in value order and, for each,
        // its specialist role first and then the Craftsperson, so shared tool
        // coverage goes to the more valuable recipe whichever role makes it. Each
        // role keeps its own labour pool; pairs that cannot work are skipped by
        // the caller's ordinary guards.
        public static IEnumerable<(string Role, CraftRecipe Recipe)> Steps(CraftRecipe[] ordered, bool byValue)
        {
            if (!byValue)
            {
                foreach (string role in Roles)
                    foreach (var recipe in ordered) yield return (role, recipe);
                yield break;
            }
            foreach (var recipe in ordered)
            {
                if (recipe.Specialist != null && Array.IndexOf(Roles, recipe.Specialist) >= 0) yield return (recipe.Specialist, recipe);
                yield return (WorkforceRoles.Craftsperson, recipe);
            }
        }
    }
}
