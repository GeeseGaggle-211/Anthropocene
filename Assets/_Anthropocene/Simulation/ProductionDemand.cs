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
        // worker-years per unit; FirstUnitValue is V. Valued = false means "not
        // valued": the claim keeps the uniform share.
        public double Stock, Need, LabourPerUnit, FirstUnitValue;
        // True for the goods the value model prices: the four need goods (clothing,
        // shelter), Pots (food security, WP4a) and, when a farm-labour price exists,
        // the labour-saving tools.
        public bool Valued;
        // True for Farm and Herding Tools (WP4a): V is the labour they free times the
        // farm-labour price, they carry no Need curve, and their share is a price test.
        public bool LabourSaving;
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
    // Pots are need claims on food security (WP4a). A labour-saving tool (Farm and
    // Herding Tools, priced only when a farm-labour price exists) is pursued in full
    // when its value per worker-year V / l is at least w(s): share 0 at s <= 0, 1 at
    // s >= 1, else 1 if V / l >= w(s) and 0 otherwise. Tools never set the path's
    // bounds. Goods the rule does not price take the uniform share s, as does a priced
    // good with no usable labour estimate; a priced good whose value is zero
    // (priority 0, or B = 0) gets share 0.
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
        public static bool IsTool(ProductionClaim c)
            => c.LabourSaving && c.FirstUnitValue > 0 && c.LabourPerUnit > 0
               && !double.IsInfinity(c.LabourPerUnit) && !double.IsInfinity(c.FirstUnitValue);
        // V * exp(-lambda * c) at the coverage reached by `share` of the useful quantity.
        public double MarginalValue(ProductionClaim c, double share)
            => c.FirstUnitValue * Math.Exp(-Steepness * (c.Stock + share * c.Quantity) / c.Need);
        // Path ends. Craft claims set both ends exactly as before. The optional
        // expansion ladder (WP4b) can only raise hi (so its share is 0 at s = 0 and
        // Evaluate(0) stays feasible): it never lowers lo, because a ladder's total
        // build effort can exceed every resident and would drag the path to a price
        // where the plan cannot fit, wrecking the search and every other claim's
        // share. It rides the craft path by price; only with no craft claim at all
        // does it own both ends (lo = its last step's value).
        private static bool Bounds(IReadOnlyList<ProductionClaim> claims, ExpansionLadderClaim ladder, double steepness, out double hi, out double lo, out bool ladderOwnsPath)
        {
            hi = 0; lo = double.PositiveInfinity; bool any = false; ladderOwnsPath = false;
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
                double full = first * Math.Exp(-steepness * (c.Stock + c.Quantity) / c.Need);
                if (full < lo) lo = full;
            }
            if (ladder != null && ladder.Any)
            {
                if (ladder.Value[0] > hi) hi = ladder.Value[0];
                if (!any)
                {
                    any = true; ladderOwnsPath = true;
                    lo = ladder.Value[ladder.Value.Length - 1];
                }
            }
            return any;
        }
        // The labour price w at path position s; NaN when no claim is valued.
        public double LaborPrice(IReadOnlyList<ProductionClaim> claims, double s) => LaborPrice(claims, null, s);
        public double LaborPrice(IReadOnlyList<ProductionClaim> claims, ExpansionLadderClaim ladder, double s)
        {
            if (!Bounds(claims, ladder, Steepness, out double hi, out double lo, out _)) return double.NaN;
            return hi * Math.Pow(lo / hi, Numbers.Clamp(s, 0, 1));
        }
        // Largest path position at which the price is still at least `price`
        // (the inverse of LaborPrice): 1 when the whole path stays above it, 0
        // when it starts below it or no claim is valued.
        public double PositionAtPrice(IReadOnlyList<ProductionClaim> claims, ExpansionLadderClaim ladder, double price)
        {
            if (!Bounds(claims, ladder, Steepness, out double hi, out double lo, out _) || !(price > 0) || !(hi > 0)) return 0;
            if (price >= hi) return 0;
            if (!(lo > 0) || price <= lo || !(lo < hi)) return 1;
            return Numbers.Clamp(Math.Log(price / hi) / Math.Log(lo / hi), 0, 1);
        }
        public void Shares(IReadOnlyList<ProductionClaim> claims, double s, double[] shares)
            => Shares(claims, null, s, shares, out _);
        // Share of one need claim at path position s and labour price `price`.
        private double NeedShare(ProductionClaim c, double price, double s)
        {
            // The path ends are exact: floating error must not leave residue at s = 0 or a gap at s = 1.
            if (s <= 0) return 0;
            if (s >= 1) return 1;
            double coverage = Math.Log(c.FirstUnitValue / (price * c.LabourPerUnit)) / Steepness;
            return Numbers.Clamp((coverage * c.Need - c.Stock) / c.Quantity, 0, 1);
        }
        // Expansion worker-years bought at labour price w: the build effort of the
        // ladder prefix whose value per worker-year is at least w (the ladder is
        // the marginal-value curve of expansion). 0 at s <= 0 and nondecreasing in s.
        private static double LadderWorkerYears(ExpansionLadderClaim ladder, double price, double s, bool exactEnds)
        {
            if (s <= 0) return 0;
            if (exactEnds && s >= 1) return ladder.TotalWorkerYears;
            double sum = 0;
            for (int i = 0; i < ladder.Value.Length && ladder.Value[i] >= price; i++) sum += ladder.WorkerYears[i];
            return sum;
        }
        // With an expansion ladder its worker-years at s are returned in
        // expansionWorkerYears. It is 0 at s = 0 and nondecreasing in s, and it
        // reaches its total at s = 1 only when it is the only claim (see Bounds).
        public void Shares(IReadOnlyList<ProductionClaim> claims, ExpansionLadderClaim ladder, double s, double[] shares, out double expansionWorkerYears)
        {
            expansionWorkerYears = 0;
            if (!Bounds(claims, ladder, Steepness, out double hi, out double lo, out bool ladderOwnsPath))
            {
                for (int i = 0; i < claims.Count; i++) shares[i] = s;
                return;
            }
            double price = hi * Math.Pow(lo / hi, Numbers.Clamp(s, 0, 1));
            for (int i = 0; i < claims.Count; i++)
            {
                var c = claims[i];
                if (IsTool(c)) { shares[i] = s <= 0 ? 0 : s >= 1 ? 1 : c.FirstUnitValue / c.LabourPerUnit >= price ? 1 : 0; continue; }
                if (!IsNeed(c)) { shares[i] = c.Valued && c.FirstUnitValue <= 0 ? 0 : s; continue; }
                shares[i] = NeedShare(c, price, s);
            }
            if (ladder != null && ladder.Any) expansionWorkerYears = LadderWorkerYears(ladder, price, s, ladderOwnsPath);
        }
    }

    // The expansion ladder as a marginal-value curve (WP4b): consecutive steps of
    // the allocator's greedy ladder, each with its build effort in worker-years
    // and its value per worker-year (person-QoL-years), nonincreasing. Transient.
    public sealed class ExpansionLadderClaim
    {
        public double[] WorkerYears = Array.Empty<double>(), Value = Array.Empty<double>();
        public double TotalWorkerYears;
        public bool Any => Value.Length > 0;
        // The value of the first step whose build effort is not covered by
        // `coveredWorkerYears`; 0 when the ladder is exhausted.
        public double ValueAfter(double coveredWorkerYears)
        {
            double sum = 0;
            for (int i = 0; i < Value.Length; i++)
            {
                sum += WorkerYears[i];
                if (sum > coveredWorkerYears + 1e-9) return Value[i];
            }
            return 0;
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
        // WP4b expansion labour (Value rule only; null when no shortfall is forecast
        // or no field step has a positive value). Transient, never saved. Units: see
        // ExpansionValuation.
        public ExpansionLadderClaim Expansion;
        // WP4a start-of-turn farm-labour price estimate w_farm0 (person-QoL-years per
        // worker-year): the ladder's value after last turn's expansion farmers; 0 with no ladder.
        public double FarmLabourPrice0;
        // Diagnostics of the valuation: per-year food gaps (security = consumption +
        // reserve refill; total adds growth and Cloth feedstock), the two food
        // values (person-QoL-years per food unit), food and welfare per birth, and
        // the discounted capital and person lives (years).
        public double GapConsumptionPerYear, GapSecurityPerYear, GapTotalPerYear, FoodValue, GrowthFoodValue, FoodPerBirth, MarginalWelfarePerBirth, CapitalLife, PersonLife;
        // Ladder steps computed (recording stops at the total food gap) and their build worker-years (diagnostics).
        public int LadderSteps;
        public double LadderWorkerYearsUntruncated;
        public double Meals, OperatingReserve, SparePlant, PlantEnvelope, AnimalEnvelope, FoodRoomEnvelope, HousingMultiplier;
        // The same envelopes measured after this turn's meals only (not the operating reserve):
        // they fund just the clothing-need feedstock, NeolithicProduction.ClothingNeedFeedstock.
        public double SparePlantClothing, PlantEnvelopeClothing, AnimalEnvelopeClothing, FoodRoomEnvelopeClothing, ClothingNeedUnits;

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
        // expansionClaim = false omits the WP4b expansion claim (the pre-WP4b Value plan, a test reference).
        public static CampProductionDemand Build(WorldState w, Camp c, EconomicIntent e, bool expansionClaim = true)
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
            d.SparePlantClothing = Math.Min(c.PlantFood, Math.Max(0, c.Food - d.Meals));
            d.PlantEnvelopeClothing = Math.Min(d.TerrestrialAfterHerds + d.SparePlantClothing, Math.Max(0, c.Food + d.Supply - d.Meals));
            d.AnimalEnvelopeClothing = d.HusbandryEnabled
                ? Math.Min(Math.Max(0, c.AnimalFood) + d.ManagedFood.AnimalFood, Math.Max(0, Math.Max(0, c.Food + d.Supply - d.Meals) - d.SparePlantClothing)) : 0;
            d.FoodRoomEnvelopeClothing = d.HusbandryEnabled ? Math.Max(0, c.Food + d.Supply - d.Meals) : 0;
            d.ClothingNeedUnits = NeolithicProduction.ClothingNeedUnits(n, d.Preview.Population, years, c.Inventory.Cloth, c.Inventory.AnimalFabric);
            d.HousingMultiplier = NeolithicProduction.HousingMultiplier(w, c, c.Population);
            ClaimValuation.Apply(w, c, d);
            if (expansionClaim) ExpansionValuation.Apply(w, c, d);
            ClaimValuation.ApplyTools(w, c, d);
            return d;
        }
    }

    // Closed-form value inputs for the four need goods (clothing and shelter),
    // from committed camp terms; see ValueModelDesign.md, "Anchoring V in QoL".
    internal static class ClaimValuation
    {
        private static readonly Camp DefaultPriorities = new Camp { CraftingPriorities = new CraftingPriorities() };
        public static bool IsNeedGood(CraftGood g) => g == CraftGood.Cloth || g == CraftGood.AnimalFabric || g == CraftGood.Housing || g == CraftGood.LightHousing;
        // Farmer-years of labour one Farm Tool frees: its farmer-years of coverage times the
        // share of field work it removes, 1 - 1 / efficiency (FarmToolFieldWorkEfficiency).
        public static double FarmToolSavedYears(NeolithicConfig n)
            => n.FarmToolFarmerYearsPerUnit * (1 - 1 / CultivationEconomy.FarmToolFieldWorkEfficiency(n));
        // Herder-years one Herding Tool frees. A covered herder-year looks after
        // StoneHerdingToolAnimalsPerHerder animals that would need S / AnimalsPerHerder uncovered
        // herders (HerdingTooling.RequiredHerders), so it frees S / A - 1 herder-years.
        public static double HerdingToolSavedYears(SimulationConfig cfg)
            => cfg.Neolithic.HerdingToolCoverageYearsPerUnit * (cfg.Neolithic.StoneHerdingToolAnimalsPerHerder / cfg.AnimalsPerHerder - 1);
        // Discounted service life of a Pot in years: 1 / (k + r), k its decay rate per year.
        public static double PotLife(NeolithicConfig n)
            => 1 / (-Math.Log(1 - n.PotDecayPerReferenceTurn) / n.ReferenceTurnYears + n.DiscountRatePerYear);
        // Value of one Pot (person-QoL-years, before the priority multiplier): the operating reserve it
        // lets the camp retain, credited once at rho * v_food per food unit, exactly as the expansion
        // valuation credits reserve refill. Without shocks a reserve is filled once and then held; the
        // recurring drawdowns that would earn it again are what rho stands in for, so the Pot's life
        // does not multiply its value (a per-turn credit over L_pot overvalued Pots 3.3x and cut QoL).
        public static double PotValue(NeolithicConfig n, double foodValue)
            => n.FoodRiskWeight * n.PotStorageFoodPerUnit * foodValue;
        private static double PriorityMultiplier(ProductionClaim claim)
        {
            double defaultPriority = NeolithicProduction.Priority(DefaultPriorities, claim.Good);
            return defaultPriority > 0 ? claim.Priority / defaultPriority : (claim.Priority > 0 ? 1 : 0);
        }
        // Phase 1 prices need goods and Pots; ApplyTools (after the expansion ladder) prices the tools and builds the order.
        public static void Apply(WorldState w, Camp c, CampProductionDemand d) => Apply(w, c, d, false);
        public static void ApplyTools(WorldState w, Camp c, CampProductionDemand d) => Apply(w, c, d, true);
        private static void Apply(WorldState w, Camp c, CampProductionDemand d, bool toolsPhase)
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
            if (toolsPhase)
            {
                if (!value) return;
                // w_farm0: the ladder's value after the expansion farmers last turn's plan staffed.
                double wFarm0 = d.Expansion == null ? 0 : d.Expansion.ValueAfter((c.EconomicLabor?.ExpansionFarmers ?? 0) * years);
                d.FarmLabourPrice0 = wFarm0;
                if (wFarm0 > 0)
                    foreach (var claim in d.Claims)
                    {
                        double saved = claim.Good == CraftGood.FarmTools ? FarmToolSavedYears(n) : claim.Good == CraftGood.HerdingTools ? HerdingToolSavedYears(cfg) : 0;
                        if (!(saved > 0)) continue;
                        double toolLabour = Labour(claim.Recipe), v = saved * wFarm0 * PriorityMultiplier(claim);
                        if (!(toolLabour > 0) || !(v > 0)) continue;
                        claim.LabourSaving = true; claim.Valued = true;
                        claim.LabourPerUnit = toolLabour; claim.FirstUnitValue = v; claim.Value = v;
                    }
                d.Order = ProductionOrder.Build(cfg, d.Claims, d.Recipes, good => NeolithicProduction.Priority(d.Preview, good));
                return;
            }
            foreach (var claim in d.Claims)
            {
                if (claim.Good == CraftGood.Pots)
                {
                    // Pots: the reserve the desired storage needs beyond natural storage, less the pots
                    // that survive this turn's decay; Quantity is the existing gap.
                    double potNeed = NeolithicProduction.TargetPots(w, d.Preview, years);
                    double potValue = PotValue(n, ExpansionValuation.FoodValue(cfg, c)) * PriorityMultiplier(claim);
                    if (!(potNeed > 0) || !(potValue > 0)) continue;
                    double potLabour = Labour(claim.Recipe);
                    if (!(potLabour > 0)) continue;
                    claim.Valued = true; claim.Need = potNeed; claim.LabourPerUnit = potLabour; claim.FirstUnitValue = potValue;
                    claim.Stock = Math.Max(0, c.Inventory.Pots) * Math.Pow(1 - n.PotDecayPerReferenceTurn, years / n.ReferenceTurnYears);
                    if (value) claim.Value = potValue;
                    continue;
                }
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
        }
    }

    // WP4b: expansion labour valued on the allocator's step ladder with a
    // bucketed food value (ValueModelDesign.md, "Revised WP4 design"). Units:
    //   ladder   the resolver allocator's own greedy steps from the start-of-turn
    //            state (CultivationEconomy.ExpansionLadder): per step the build
    //            effort B_i (worker-years), its score g_i (food per year per
    //            worker-year) and its annual retained food dF_i (food per year);
    //   buckets  food the camp is short of, per year: security (consumption +
    //            reserve refill) then growth (maximum growth food + Cloth
    //            feedstock). Food beyond consumption + reserve only funds births,
    //            so it is worth far less than the average food value;
    //   v_food   person-QoL-years per food unit = N Q / (F D_year) (security bucket);
    //   v_growth person-QoL-years per food unit = max(0, dW/dN) L_person / food per
    //            birth (growth bucket), W(N) = N Q(N), Q(N) = F Wtr S(N) sum_k w_k
    //            (1 - E (1 - min(1, c_k N0 / N))) for clothing and housing at their
    //            committed coverages. The design's labour term (+w per new worker)
    //            is omitted: it is circular with the price being set;
    //   u_i      person-QoL-years per build worker-year = g_i L_cap v_i, v_i the
    //            piecewise average of the bucket values over the step's food
    //            interval [Fc, Fc + dF_i] (0 beyond the total gap), then made
    //            nonincreasing by a running minimum. The ladder is truncated once
    //            the cumulative food reaches the total gap.
    // Present only under the Value rule, cultivation enabled, and the planner's
    // existing expansion gate: forecast demand above supply with a retained gain.
    // Ladder effort is worker-years; Farm Tool efficiency and the bridge pass are
    // ignored (documented in EconomicAutomation.md).
    internal static class ExpansionValuation
    {
        public static double FoodValue(SimulationConfig cfg, Camp c)
        {
            double n = c.Population;
            double dYear = n * cfg.Consumption * cfg.DaysPerYear / cfg.BaseDays;
            if (!(dYear > 0)) return 0;
            return n * c.QoL / (Math.Max(c.FoodNeedsMetRatio, 1e-6) * dYear);
        }
        public static double CapitalLife(NeolithicConfig n) => 1 / (Math.Log(2) / n.DecayHalfLifeYears + n.DiscountRatePerYear);
        public static double PersonLife(NeolithicConfig n) => 1 / (1 / n.PersonLifeYears + n.DiscountRatePerYear);
        // Food per additional birth at maximum growth: the derivative of GrowthFood
        // with respect to final population (x = maxRate * years). Limit 0.5 C_turn as x -> 0.
        public static double FoodPerBirth(double turnConsumption, double maxRate, double years)
        {
            double x = maxRate * years;
            if (!(x > 1e-12) || !(turnConsumption > 0)) return 0;
            // (x e^x - e^x + 1) / (x^2 e^x) = (x - 1 + e^-x) / x^2
            double f = x < 1e-4 ? .5 - x / 6 + x * x / 24 : (x - 1 + Math.Exp(-x)) / (x * x);
            return turnConsumption * f;
        }
        // W(N) = N Q(N) at the committed coverages; population n0 is the camp's present cohort.
        public static double Welfare(NeolithicConfig n, Camp c, double population, double n0)
        {
            double e = c.RudimentaryExpectation > 0 ? c.RudimentaryExpectation
                : n.RudimentaryExpectationFloor + (1 - n.RudimentaryExpectationFloor) * Numbers.Clamp(c.StationaryYears / n.RudimentaryExpectationRampYears, 0, 1);
            double dilution = population > 0 ? Math.Min(1, n0 / population) : 1;
            double clothing = 1 - e * (1 - Math.Min(1, c.ClothPerCapita * dilution));
            double housing = 1 - e * (1 - Math.Min(1, c.HousingPerCapita * dilution));
            double q = c.FoodNeedsMetRatio * c.WaterNeedsMetRatio * NeolithicProduction.SocialCohesion(n, c, population)
                * (n.RudimentaryWeightClothing * clothing + n.RudimentaryWeightHousing * housing);
            return population * q;
        }
        public static double MarginalWelfare(NeolithicConfig n, Camp c)
            => Welfare(n, c, c.Population + 1, c.Population) - Welfare(n, c, c.Population, c.Population);
        // Pure ladder valuation: bucket-average value per step, truncation at the total gap,
        // running minimum. Inputs per year; values per food unit; lCap years.
        public static ExpansionLadderClaim Value(CultivationEconomy.ExpansionStep[] steps, double gapSecurity, double gapTotal, double vFood, double vGrowth, double lCap)
            => Value(steps, gapSecurity, gapSecurity, gapTotal, vFood, vFood, vGrowth, lCap);
        // Three buckets in the order supply fills them: the consumption gap
        // [0, gapConsumption] at vFood, the reserve refill [gapConsumption,
        // gapSecurity] at vReserve (rho * vFood: reserves protect against shocks,
        // which the model does not yet produce), growth [gapSecurity, gapTotal] at vGrowth.
        public static ExpansionLadderClaim Value(CultivationEconomy.ExpansionStep[] steps, double gapConsumption, double gapSecurity, double gapTotal, double vFood, double vReserve, double vGrowth, double lCap)
        {
            gapConsumption = Math.Min(gapConsumption, gapSecurity);
            var b = new List<double>(); var u = new List<double>();
            double fc = 0, floor = double.PositiveInfinity;
            foreach (var step in steps)
            {
                if (!(fc < gapTotal - 1e-12) || !(step.AnnualGain > 0)) break;
                double end = fc + step.AnnualGain;
                double inConsumption = Math.Max(0, Math.Min(end, gapConsumption) - fc);
                double inReserve = Math.Max(0, Math.Min(end, gapSecurity) - Math.Max(fc, gapConsumption));
                double inGrowth = Math.Max(0, Math.Min(end, gapTotal) - Math.Max(fc, gapSecurity));
                double v = (inConsumption * vFood + inReserve * vReserve + inGrowth * vGrowth) / step.AnnualGain;
                double value = step.Score * lCap * v;
                fc = end;
                if (!(value > 0)) break;
                floor = Math.Min(floor, value);
                b.Add(step.WorkerYears); u.Add(floor);
            }
            return new ExpansionLadderClaim { WorkerYears = b.ToArray(), Value = u.ToArray(), TotalWorkerYears = b.Sum() };
        }
        public static void Apply(WorldState w, Camp c, CampProductionDemand d)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            if (!n.ValueFulfilmentSelected || !d.CultivationEnabled || c.Population <= 0) return;
            double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals;
            double cloth = d.Goals.TryGetValue(CraftGood.Cloth, out double goal) ? goal : 0;
            // Same forecast as the planner's expansion gate.
            double forecast = d.Biological * d.NextPopulation / Math.Max(1, c.Population) + cloth * n.PlantFoodPerFabricUnit;
            if (!(forecast > d.Supply) || !d.HasRetainedExpansionGain() || !(years > 0)) return;
            // Buckets from the very targets that size the planning demand (the
            // planning cohort, as PlanningFoodDemand), per year.
            TurnResolver.FoodBucketTargets(w, c, intervals, years, d.Consumers * cfg.Consumption * intervals, c.Food,
                out double consumption, out _, out double reserveFill, out double maximumGrowthFood);
            double security = consumption + reserveFill, growth = maximumGrowthFood + cloth * n.PlantFoodPerFabricUnit;
            d.GapConsumptionPerYear = Math.Max(0, consumption - d.Supply) / years;
            d.GapSecurityPerYear = Math.Max(0, security - d.Supply) / years;
            d.GapTotalPerYear = Math.Max(0, security + growth - d.Supply) / years;
            d.FoodValue = FoodValue(cfg, c);
            double maxRate = c.DeficitTurns <= 0 ? cfg.AnnualHumanGrowthRate * c.DemographicGrowthMultiplier : 0;
            d.FoodPerBirth = FoodPerBirth(cfg.Consumption * intervals, maxRate, years);
            d.MarginalWelfarePerBirth = MarginalWelfare(n, c);
            d.PersonLife = PersonLife(n);
            d.GrowthFoodValue = d.FoodPerBirth > 0 ? Math.Max(0, d.MarginalWelfarePerBirth) * d.PersonLife / d.FoodPerBirth : 0;
            d.CapitalLife = CapitalLife(n);
            var steps = CultivationEconomy.ExpansionLadder(w, c, years, d.Footprint, d.GapTotalPerYear);
            d.LadderSteps = steps.Length; d.LadderWorkerYearsUntruncated = steps.Sum(x => x.WorkerYears);
            var ladder = Value(steps, d.GapConsumptionPerYear, d.GapSecurityPerYear, d.GapTotalPerYear, d.FoodValue, n.FoodRiskWeight * d.FoodValue, d.GrowthFoodValue, d.CapitalLife);
            if (ladder.Any) d.Expansion = ladder;
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
    //   Farm and Herding Tools: their value per unit, labour freed times w_farm0 (WP4a);
    //   Pots: V_Pot * exp(-lambda * Stock / Need), a need claim on food security (WP4a);
    //   any good without a usable value (priority 0, no labour estimate, no farm-labour
    //     price for tools): after all of those, in crafting-priority order.
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
            // Labour-saving tools are keyed by their value per unit (labour freed at w_farm0).
            foreach (var claim in claims)
                if (claim.Valued && ValueFulfilment.IsTool(claim)) valued[claim.Good] = claim.FirstUnitValue;
            if (valued.TryGetValue(CraftGood.Housing, out double housingKey)) valued[CraftGood.HousingMaterial] = housingKey;
            // The value of the outputs a unit of coverage lets the camp make, at the
            // best equipment need good; coverage from tools made now starts next turn.
            double best = double.NaN;
            foreach (var recipe in recipes)
                if (recipe.Equipment && ClaimValuation.IsNeedGood(recipe.Good) && valued.TryGetValue(recipe.Good, out double key) && !(key <= best)) best = key;
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
