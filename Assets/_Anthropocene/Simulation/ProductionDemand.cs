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
        // Marginal field productivity from the same retained-yield/build/upkeep forecast.
        // The herd comparison reads this, never the established farmer's average rate.
        internal double[] FoodPerWorkerYear = Array.Empty<double>();
        internal int[] Tiles = Array.Empty<int>();
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

    // Cached price-independent totals for one physical expansion ladder and
    // one food portfolio. Price queries visit coefficients, not ecology kernels
    // or demand/recipe/ledger reconstructions.
    internal sealed class SharedExpansionSchedule
    {
        internal double[] Work = Array.Empty<double>(), Benefit = Array.Empty<double>(), FutureWork = Array.Empty<double>(), Productivity = Array.Empty<double>();
        internal int[] Tiles = Array.Empty<int>();
        internal void Purchase(double currentPrice, double futurePrice, double budget, out double years, out double net)
        {
            years = net = 0; double floor = double.PositiveInfinity;
            for (int i = 0; i < Work.Length; i++)
            {
                floor = Math.Min(floor, Benefit[i] - futurePrice * FutureWork[i]);
                if (!(floor > currentPrice)) break;
                double bought = Math.Min(Work[i], Math.Max(0, budget - years));
                years += bought; net += bought * (floor - currentPrice);
                if (bought < Work[i]) break;
            }
        }
        internal ExpansionLadderClaim Price(double futurePrice)
        {
            int count = 0; double floor = double.PositiveInfinity;
            var values = new double[Work.Length];
            for (int i = 0; i < Work.Length; i++)
            {
                floor = Math.Min(floor, Benefit[i] - futurePrice * FutureWork[i]);
                if (!(floor > 0)) break;
                values[count++] = floor;
            }
            if (count == 0) return null;
            return new ExpansionLadderClaim { WorkerYears = Work.Take(count).ToArray(), Value = values.Take(count).ToArray(),
                FoodPerWorkerYear = Productivity.Take(count).ToArray(), Tiles = Tiles.Take(count).ToArray(), TotalWorkerYears = Work.Take(count).Sum() };
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
        // Value rule, cultivation and husbandry enabled (Docs/HusbandrySettling.md): the food (per turn) the camp's herds are expected to
        // add after one more turn of breeding at the planned intensity, net of the human forage the larger herd displaces, and the
        // herd's forecast food after that growth. Both 0 otherwise. HerdGrowthFood closes the food gap before any field is valued.
        public double HerdGrowthFood, GrownHerdFood;
        // Herd-labour coupling (Docs/HerdLabour.md; Value rule, husbandry, HerdLabourEnabled): the grown-herd forecast the ladder reuses and the
        // herder ladder itself. Null otherwise. Transient, never saved.
        internal ManagedFoodForecast GrownHerd;
        internal HerdLadder HerdLadder;
        // Value rule: settling cost of a physically mobile herd-bearing camp
        // (person-QoL-years), and the expansion worker-years it may spend this
        // turn without crossing into a village (PositiveInfinity = no limit).
        // Diagnostics and planner cap; transient.
        public double SettlingCost, ExpansionEffortCap = double.PositiveInfinity;
        public bool SettlesByExpansion;
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
        internal CultivationEconomy.ExpansionStep[] PhysicalExpansion;
        internal double FutureForageAfterHerds, FutureUpkeep;
        internal double FuturePastureCapacity = double.PositiveInfinity;
        internal int OperationFarmers;
        internal CampProductionDemand AllocationCopy() => (CampProductionDemand)MemberwiseClone();
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
        public double Meals, OperatingReserve, SparePlant, PlantEnvelope, AnimalEnvelope, FoodRoomEnvelope, HousingMultiplier, HarvestProductivity;
        // Food captured by the shared coastal-gathering path is kept separate by type. Aquatic food can satisfy meals,
        // but is never a CraftRecipe feedstock. Resident Fishing's selected grounds and catch envelope are separate
        // so overlapping passive and resident claims are not added twice by the planner.
        public double AquaticSupply, AquaticAfterHerds, SelectedFishingPassiveSupply;
        public FishingAccessPlan FishingAccess;
        public bool FishingEnabled;
        // Harvest productivity of the field state the future work terms assume.
        // Defaults to the committed productivity; the shared allocation sets it
        // per field-operation portfolio.
        internal double FutureHarvestProductivity;
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

        internal CampProductionDemand WithAnimalFood(WorldState w, Camp c, double animalFood, double futurePrice = 0, double? futureAnnualAnimalFood = null)
        {
            var result = (CampProductionDemand)MemberwiseClone();
            result.SetFoodEnvelopes(w, c, animalFood);
            result.Expansion = null; result.ExpansionEffortCap = double.PositiveInfinity;
            if (w.Config.SharedCropHerdAllocationEnabled) ExpansionValuation.ApplyShared(w, c, result, animalFood, futurePrice, futureAnnualAnimalFood);
            else ExpansionValuation.Apply(w, c, result);
            return result;
        }
        internal CampProductionDemand WithSharedExpansion(WorldState w, Camp c, double animalFood, ExpansionLadderClaim expansion)
        {
            var result = AllocationCopy(); result.SetFoodEnvelopes(w, c, animalFood);
            result.Expansion = expansion; result.ExpansionEffortCap = double.PositiveInfinity;
            return result;
        }
        internal void SetFoodEnvelopes(WorldState w, Camp c, double animalFood)
        {
            Supply = ForageAfterHerds + animalFood;
            SparePlant = Math.Min(c.PlantFood, Math.Max(0, c.Food - Meals - OperatingReserve));
            PlantEnvelope = Math.Min(TerrestrialAfterHerds + SparePlant, Math.Max(0, c.Food + Supply - Meals));
            double surplus = Math.Max(0, c.Food + Supply - Meals - OperatingReserve);
            AnimalEnvelope = HusbandryEnabled ? Math.Min(Math.Max(0, c.AnimalFood) + animalFood, Math.Max(0, surplus - SparePlant)) : 0;
            double years = w.Config.TurnDays / w.Config.DaysPerYear;
            double reserve = Math.Min(NeolithicProduction.DesiredReserveFood(w, Consumers, w.Config.GenerationIntervals), NeolithicProduction.StorageCapacityAfterPotDepreciation(w, c, w.Config.GenerationIntervals, years));
            FoodRoomEnvelope = HusbandryEnabled ? Math.Max(0, c.Food + Supply - Meals - reserve) : 0;
            SparePlantClothing = Math.Min(c.PlantFood, Math.Max(0, c.Food - Meals));
            PlantEnvelopeClothing = Math.Min(TerrestrialAfterHerds + SparePlantClothing, Math.Max(0, c.Food + Supply - Meals));
            AnimalEnvelopeClothing = HusbandryEnabled ? Math.Min(Math.Max(0, c.AnimalFood) + animalFood, Math.Max(0, Math.Max(0, c.Food + Supply - Meals) - SparePlantClothing)) : 0;
            FoodRoomEnvelopeClothing = HusbandryEnabled ? Math.Max(0, c.Food + Supply - Meals) : 0;
        }

        internal void ApplyManagedHerd(WorldState w, Camp c, int herders)
        {
            double intervals = w.Config.GenerationIntervals;
            double years = w.Config.TurnDays / w.Config.DaysPerYear;
            ReservedHerders = HusbandryEnabled ? Math.Min(c.Population,
                Math.Max(0, herders)) : 0;
            ManagedFood = HusbandryEnabled
                ? TurnResolver.ForecastManagedAnimalFood(w, c, ReservedHerders,
                    intervals, years, c.Tile)
                : new ManagedFoodForecast();
            ForageAfterHerds = Math.Max(0, ForageSupply - ManagedFood.HumanFoodDisplaced);
            TerrestrialAfterHerds = Math.Max(0, PlantSupply - ManagedFood.HumanFoodDisplaced);
            double displacementPastPlants = Math.Max(0,
                ManagedFood.HumanFoodDisplaced - PlantSupply);
            AquaticAfterHerds = Math.Max(0,
                AquaticSupply - displacementPastPlants);
            SelectedFishingPassiveSupply = Math.Min(
                SelectedFishingPassiveSupply, AquaticAfterHerds);
            Supply = ForageAfterHerds + ManagedFood.AnimalFood;
            SetFoodEnvelopes(w, c, ManagedFood.AnimalFood);
        }

        // Normalizes the plan's focus to the camp footprint before the preview
        // is built from it; every other read is of the committed boundary.
        // expansionClaim = false omits the WP4b expansion claim (the pre-WP4b Value plan, a test reference).
        public static CampProductionDemand Build(WorldState w, Camp c, EconomicIntent e, bool expansionClaim = true, bool herdCapital = true)
        {
            var d = BuildPhysical(w, c, e, true, herdCapital);
            var cfg = w.Config;
            var preview = d.Preview; var goals = d.Goals;
            d.Claims = d.Goods.Select(r =>
            {
                bool specialist = c.People.HasTech(r.Track);
                double priority = NeolithicProduction.Priority(preview, r.Good);
                return new ProductionClaim { Good = r.Good, Recipe = r, Quantity = goals[r.Good], Priority = priority, Value = priority,
                    Role = specialist ? r.Specialist : WorkforceRoles.Craftsperson,
                    Rate = SpecialistProductivity.CraftRate(w, c, r, specialist ? r.Specialist : WorkforceRoles.Craftsperson) };
            }).ToArray();
            ClaimValuation.Apply(w, c, d);
            if (expansionClaim && !cfg.SharedCropHerdAllocationEnabled) ExpansionValuation.Apply(w, c, d, herdCapital && HerdLabour.Applies(w, c));
            ClaimValuation.ApplyTools(w, c, d);
            // The herder ladder reads the priced claims (feedstock values), so it comes after every claim is valued.
            if (herdCapital && HerdLabour.Applies(w, c) && !cfg.SharedCropHerdAllocationEnabled) d.HerdLadder = HerdLabour.Build(w, c, d, e.ManagedBreedingIntensity);
            return d;
        }
        // The physical demand and capability projection, without claims or valuation (Docs/EconomicControlContract.md §2):
        // footprint, field upkeep and harvest productivity, forage supply, the current herd's reservation and food, material
        // sources, the preview camp, each good's useful demand and the food envelopes. `legacyProbes` keeps the Value planner's
        // two extras: the grown-herd forecast (Value rule only) and the prospective-builder shelter probe. DemandControl
        // derives shelter demand without fictitious staffing.
        internal static CampProductionDemand BuildPhysical(WorldState w, Camp c,
            EconomicIntent e, bool legacyProbes, bool herdCapital = true,
            int reservedHerdersOverride = -1,
            FishingAccessPlan fishingAccess = null)
        {
            var d = new CampProductionDemand { world = w };
            var cfg = w.Config; var n = cfg.Neolithic;
            double intervals = cfg.GenerationIntervals, years = cfg.TurnDays / cfg.DaysPerYear;
            d.CultivationEnabled = CultivationEconomy.Enabled(c);
            d.HusbandryEnabled = CultivationEconomy.HusbandryEnabled(c);
            d.FishingEnabled = FishingEconomy.FeatureEnabled(w, c);
            if (d.FishingEnabled)
            {
                // TurnPlanning's transient access plan is reusable only for the exact accepted destination and horizon.
                if (fishingAccess != null && (fishingAccess.CampId != c.Id || fishingAccess.CampTile != c.Tile ||
                    fishingAccess.Residents != c.Population || fishingAccess.StartDays != w.Days || fishingAccess.HorizonDays != cfg.TurnDays))
                    fishingAccess = null;
                d.FishingAccess = fishingAccess ?? FishingEconomy.BuildAccess(w, c, c.Tile, c.Population, w.Days, cfg.TurnDays);
            }
            // The configured one-tile work radius lies wholly inside the owner's
            // two-tile vision. No global own-group visibility scan is needed here.
            var footprint = d.Footprint = d.CultivationEnabled ? CultivationEconomy.Footprint(w, c).ToArray() : Array.Empty<int>();
            if (!footprint.Contains(e.Focus)) e.Focus = -1;
            var operated = d.Operated = footprint.Where(id => w.Tiles[id].CultivationOperator < 0 || w.Tiles[id].CultivationOperator == c.Id).ToArray();
            d.Upkeep = d.CultivationEnabled ? CultivationEconomy.MaintenanceFarmersForPlanning(w, c, years) : 0;
            d.Established = d.CultivationEnabled && operated.Any(id => LazyEcology.ProjectEcology(w, id, w.Days).CultivationCapital > 0);
            d.HarvestProductivity = d.CultivationEnabled ? CultivationEconomy.HarvestProductivity(w, c, footprint) : 1;
            d.FutureHarvestProductivity = d.HarvestProductivity;
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
            if (d.FishingAccess == null)
            {
                // Keep the feature-off calculation path byte-for-byte equivalent to the established planner.
                d.ForageSupply = d.Eligible.Sum(FoodAvailability);
                d.PlantSupply = d.Eligible.Where(id => w.Tiles[id].Land).Sum(FoodAvailability);
                d.AquaticSupply = Math.Max(0, d.ForageSupply - d.PlantSupply);
            }
            else
            {
                var selectedGrounds = new HashSet<int>(d.FishingAccess.GroundIds);
                foreach (int id in d.Eligible)
                {
                    double available = FoodAvailability(id);
                    d.ForageSupply += available;
                    if (w.Tiles[id].Land) d.PlantSupply += available;
                    else d.AquaticSupply += available;
                    if (selectedGrounds.Contains(id)) d.SelectedFishingPassiveSupply += available;
                }
            }
            d.ReservedHerders = d.HusbandryEnabled
                ? reservedHerdersOverride >= 0
                    ? Math.Min(c.Population, reservedHerdersOverride)
                    : EconomicPlanner.ManagedHerderReservation(w, c, years)
                : 0;
            d.ManagedFood = d.HusbandryEnabled ? TurnResolver.ForecastManagedAnimalFood(w, c, d.ReservedHerders, intervals, years, c.Tile) : new ManagedFoodForecast();
            if (legacyProbes && n.ValueFulfilmentSelected && d.HusbandryEnabled && d.CultivationEnabled && c.Population > 0)
            {
                var grown = TurnResolver.ForecastGrownManagedAnimalFood(w, c, e.ManagedBreedingIntensity, intervals, years, c.Tile);
                d.GrownHerd = grown;
                if (grown.Animals > 0)
                {
                    d.GrownHerdFood = grown.AnimalFood;
                    // HV2 prices offspring in the herd asset. Keep the physical grown-herd
                    // envelope, but do not also credit its offspring against the field gap.
                    if (!herdCapital || !HerdLabour.Applies(w, c))
                        d.HerdGrowthFood = Math.Max(0, (grown.AnimalFood - d.ManagedFood.AnimalFood) - (grown.HumanFoodDisplaced - d.ManagedFood.HumanFoodDisplaced));
                }
            }
            d.ForageAfterHerds = Math.Max(0, d.ForageSupply - d.ManagedFood.HumanFoodDisplaced);
            d.TerrestrialAfterHerds = Math.Max(0, d.PlantSupply - d.ManagedFood.HumanFoodDisplaced);
            double displacementPastPlants = Math.Max(0, d.ManagedFood.HumanFoodDisplaced - d.PlantSupply);
            d.AquaticAfterHerds = Math.Max(0, d.AquaticSupply - displacementPastPlants);
            d.SelectedFishingPassiveSupply = Math.Min(d.SelectedFishingPassiveSupply, d.AquaticAfterHerds);
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
            if (legacyProbes && d.HusbandryEnabled && NeolithicProduction.Priority(c, CraftGood.LightHousing) > 0 && c.Population > d.ReservedHerders)
                previewIntent.Assignments.Add(new PopulationAssignment { Role = c.People.HasTech(NeolithicTech.Construction) ? WorkforceRoles.Builder : WorkforceRoles.Craftsperson, Population = 1 });
            var preview = d.Preview = EconomicPlanner.Preview(c, previewIntent, d.NextPopulation);
            NeolithicProduction.DepreciateHousing(w, preview, years);
            d.Goods = d.Recipes.Where(r => r.Good != CraftGood.HandTools && r.Good != CraftGood.Tools && r.Good != CraftGood.HousingMaterial &&
                NeolithicProduction.RecipeAvailable(w, c, r)).ToArray();
            var goals = d.Goals = d.Goods.ToDictionary(r => r.Good, r => NeolithicProduction.Priority(preview, r.Good) > 0 ? NeolithicProduction.Demand(w, preview, r.Good, years, -1, false, false, sources) : 0);
            // FarmTools are sized only after a proposal has a bounded field
            // workforce. The empty preview cannot answer that question without
            // inventing labor, so the normal FarmTools demand helper is called
            // on the resolver-shaped candidate.
            if (goals.ContainsKey(CraftGood.FarmTools)) goals[CraftGood.FarmTools] = 0;
            int consumers = d.Consumers = c.Population + w.Hunters.Where(h => h.SourceCamp == c.Id && Simulation.Supply.InRange(w, c, h.Tile)).Sum(h => h.Population);
            d.Meals = consumers * cfg.Consumption * intervals;
            d.OperatingReserve = NeolithicProduction.DesiredReserveFood(w, consumers);
            d.SetFoodEnvelopes(w, c, d.ManagedFood.AnimalFood);
            d.ClothingNeedUnits = NeolithicProduction.ClothingNeedUnits(n, d.Preview.Population, years, c.Inventory.Cloth - c.TradeExport(CraftGood.Cloth), c.Inventory.AnimalFabric - c.TradeExport(CraftGood.AnimalFabric));
            d.HousingMultiplier = NeolithicProduction.HousingMultiplier(w, c, c.Population);
            d.Claims = Array.Empty<ProductionClaim>();
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
        // The camp's need-good value terms from the committed camp: B = F * W, S at
        // the present cohort and E from the last resolved needs (or its formula).
        internal static void CampTerms(WorldState w, Camp c, out double e, out double b, out double s)
        {
            var n = w.Config.Neolithic;
            b = c.FoodNeedsMetRatio * c.WaterNeedsMetRatio;
            s = NeolithicProduction.SocialCohesion(w, c, c.Population);
            e = c.RudimentaryExpectation > 0 ? c.RudimentaryExpectation
                : n.RudimentaryExpectationFloor + (1 - n.RudimentaryExpectationFloor) * Numbers.Clamp(c.StationaryYears / n.RudimentaryExpectationRampYears, 0, 1);
        }
        internal static double HousingLife(NeolithicConfig n, CraftGood good)
        {
            double decay = good == CraftGood.LightHousing ? n.LightHousingDecayPerReferenceTurn : n.HousingDecayPerTurn;
            double k = -Math.Log(1 - decay) / n.ReferenceTurnYears;
            return 1 / (k + n.DiscountRatePerYear);
        }
        // The clothing and fixed-Housing curves Apply prices (default priority),
        // evaluated on the committed camp itself: trade reads these (Trade.Value).
        internal static void NeedGoodTerms(WorldState w, Camp c, out double clothingV, out double clothingNeed, out double clothingStock,
            out double housingV, out double housingStock)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear;
            CampTerms(w, c, out double e, out double b, out double s);
            clothingV = n.RudimentaryWeightClothing * e * b * s * (n.ReferenceTurnYears / n.FabricConsumptionPerPersonTurn);
            clothingNeed = c.Population * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears;
            clothingStock = Math.Max(0, c.Inventory.Cloth) + Math.Max(0, c.Inventory.AnimalFabric);
            double housingValuePerPerson = n.RudimentaryWeightHousing * e * b * s * HousingLife(n, CraftGood.Housing);
            housingV = n.HousingPeoplePerUnit * housingValuePerPerson;
            housingStock = NeolithicProduction.HousingCapacity(w, c, w.Days + years * cfg.DaysPerYear).Total / n.HousingPeoplePerUnit;
        }
        // Worker-years per unit of a recipe for this camp: craft time (divided by the tool multiplier the
        // staffing pass uses for shelter), input extraction, plant feedstock gathering and, for Light Housing
        // and Housing, their Animal Fabric and Housing Material inputs. Animal Food is managed-herd output and
        // costs no gathering labour here. Shared by the claim valuation and trade's production cost.
        internal static double LabourPerUnit(WorldState w, Camp c, CraftRecipe r, IReadOnlyList<CraftRecipe> recipes, double housingMultiplier)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            double perYear = cfg.DaysPerYear / cfg.BaseDays;
            double gather = cfg.GatherPerPerson * perYear;
            double Rate(CraftRecipe x) => SpecialistProductivity.CraftRate(w, c, x,
                c.People.HasTech(x.Track) ? x.Specialist : WorkforceRoles.Craftsperson);
            double Extraction(MaterialId m, double perUnit)
            {
                if (perUnit <= 0 || n.SpecialistMaterialExtractionRate <= 0) return 0;
                bool specialist = c.People.HasTech(EconomicIntents.RequiredTechnology(MaterialEconomy.Specialist(m)));
                double rate = SpecialistProductivity.ExtractionRate(w, c, m,
                    specialist ? MaterialEconomy.Specialist(m) : WorkforceRoles.Gatherer);
                return perUnit / (rate * perYear);
            }
            double Labour(CraftRecipe x)
            {
                double rate = Rate(x);
                if (rate <= 0) return 0;
                double m = ToolCoverageBudget.IsHousingWork(x.Good) ? housingMultiplier : 1;
                if (m <= 0) return 0;
                double l = 1 / (rate * perYear * m);
                l += Extraction(MaterialId.Stone, x.Stone) + Extraction(MaterialId.Clay, x.Clay) + Extraction(MaterialId.Wood, x.Wood + x.ConstructionMaterial);
                if (gather > 0) l += x.PlantFood / gather;
                if (x.AnimalFabric > 0)
                {
                    var input = recipes.FirstOrDefault(y => y.Good == CraftGood.AnimalFabric);
                    double inputLabour = input == null ? 0 : Labour(input);
                    if (inputLabour <= 0) return 0;
                    l += x.AnimalFabric * inputLabour;
                }
                if (x.HousingMaterial > 0)
                {
                    // Housing Material is planned with its Housing claim, not as a claim of its own.
                    var input = recipes.FirstOrDefault(y => y.Good == CraftGood.HousingMaterial);
                    double inputLabour = input == null ? 0 : Labour(input);
                    if (inputLabour <= 0) return 0;
                    l += x.HousingMaterial * inputLabour;
                }
                return l;
            }
            return Labour(r);
        }
        public static void Apply(WorldState w, Camp c, CampProductionDemand d) => Apply(w, c, d, false);
        public static void ApplyTools(WorldState w, Camp c, CampProductionDemand d) => Apply(w, c, d, true);
        private static void Apply(WorldState w, Camp c, CampProductionDemand d, bool toolsPhase)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear, perYear = cfg.DaysPerYear / cfg.BaseDays;
            // Camp terms come from the committed camp: B = F * W, S at the
            // present cohort and E from the last resolved needs (or its formula
            // for a camp that has not yet resolved any).
            CampTerms(w, c, out double e, out double b, out double s);
            double HousingLife(CraftGood good) => ClaimValuation.HousingLife(n, good);
            double shelterPeople = NeolithicProduction.HousingCapacity(w, d.Preview, w.Days + years * cfg.DaysPerYear).Total;
            double clothingV = n.RudimentaryWeightClothing * e * b * s * (n.ReferenceTurnYears / n.FabricConsumptionPerPersonTurn);
            double Labour(CraftRecipe r) => LabourPerUnit(w, c, r, d.Recipes, d.HousingMultiplier);
            bool value = n.ValueFulfilmentSelected;
            // Clothing coverage is measured against this turn's requirement for the
            // prior cohort, with Cloth and Animal Fabric counted as one clothing
            // stock. Useful demand also carries the finished-goods reserve; reserve
            // units lie beyond coverage 1, where the curve values them little.
            // Shelter's useful demand is this turn's requirement already.
            double clothingNeed = d.Preview.Population * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears;
            // Agreed exports leave after production (Docs/Trade.md): the position falls by them, so the units that
            // fill the delivery sit at the top of the curve and are staffed first.
            double clothingStock = Math.Max(0, c.Inventory.Cloth) + Math.Max(0, c.Inventory.AnimalFabric) - c.TradeExport(CraftGood.Cloth) - c.TradeExport(CraftGood.AnimalFabric);
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
                double peoplePerUnit = claim.Good == CraftGood.LightHousing ? n.LightHousingPeoplePerUnit : n.HousingPeoplePerUnit;
                double stock = clothing ? clothingStock : shelterPeople / peoplePerUnit;
                double defaultPriority = NeolithicProduction.Priority(DefaultPriorities, claim.Good);
                double multiplier = defaultPriority > 0 ? claim.Priority / defaultPriority : (claim.Priority > 0 ? 1 : 0);
                claim.Valued = true;
                claim.Stock = stock;
                claim.Need = clothing ? clothingNeed : stock + claim.Quantity;
                claim.LabourPerUnit = Labour(claim.Recipe);
                double housingValuePerPerson = n.RudimentaryWeightHousing * e * b * s * HousingLife(claim.Good);
                claim.FirstUnitValue = (clothing ? clothingV : peoplePerUnit * housingValuePerPerson) * multiplier;
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
        internal static void ApplyShared(WorldState w, Camp c, CampProductionDemand d, double animalFood, double futurePrice, double? futureAnnualAnimalFood = null)
            => d.Expansion = SharedSchedule(w, c, d, animalFood, futureAnnualAnimalFood).Price(futurePrice);
        // ledger: optional FoodIncomeLedger already built for (w, c, d).
        internal static SharedExpansionSchedule SharedSchedule(WorldState w, Camp c, CampProductionDemand d, double animalFood, double? futureAnnualAnimalFood = null,
            FoodIncomeLedger ledger = null)
        {
            var schedule = new SharedExpansionSchedule();
            if (!d.CultivationEnabled || c.Population <= 0) return schedule;
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear;
            double life = Math.Exp(-n.DiscountRatePerYear * years) * HerdLabour.DiscountedYears(n.DiscountRatePerYear, Math.Max(0, n.PlanningHorizonYears - years));
            if (!(life > 0)) return schedule;
            ledger = ledger ?? new FoodIncomeLedger(w, c, d);
            var receipt = ledger.Receive(d.TerrestrialAfterHerds, Math.Max(0, d.ForageAfterHerds - d.TerrestrialAfterHerds), animalFood);
            // Slaughter is an endpoint receipt, never a repeating milk stream.
            double human = d.FutureForageAfterHerds / years, animal = Math.Max(0, futureAnnualAnimalFood ?? animalFood / years);
            double consumption = Math.Max(0, d.Meals / years - human - animal);
            double security = consumption + receipt.RemainingReserve / life;
            double total = security + receipt.RemainingGrowth / years + Math.Max(0, d.ClothingNeedUnits * n.PlantFoodPerFabricUnit / years - Math.Max(0, human - d.Meals / years));
            if (!(total > 0)) return schedule;
            double vFood = FoodValue(cfg, c), perBirth = FoodPerBirth(cfg.Consumption * cfg.GenerationIntervals, QoLMortality.MaximumRate(w, c), years);
            double vGrowth = perBirth > 0 ? Math.Max(0, MarginalWelfare(w, c)) * PersonLife(n) / perBirth : 0;
            var steps = d.PhysicalExpansion ?? Array.Empty<CultivationEconomy.ExpansionStep>();
            var worker = new List<double>(); var values = new List<double>(); var costs = new List<double>(); var productivity = new List<double>(); var tiles = new List<int>();
            double filled = 0, upkeep = d.FutureUpkeep;
            var tool = d.Claims.FirstOrDefault(claim => claim.Good == CraftGood.FarmTools);
            double toolLabour = tool?.LabourPerUnit ?? 0;
            if (!(toolLabour > 0) && tool != null)
            {
                double rate = tool.Rate * cfg.DaysPerYear / cfg.BaseDays;
                bool extractionSpecialist = c.People.HasTech(EconomicIntents.RequiredTechnology(MaterialEconomy.Specialist(MaterialId.Wood)));
                double extraction = SpecialistProductivity.ExtractionRate(w, c, MaterialId.Wood,
                    extractionSpecialist ? MaterialEconomy.Specialist(MaterialId.Wood) : WorkforceRoles.Gatherer) * cfg.DaysPerYear / cfg.BaseDays;
                toolLabour = rate > 0 && extraction > 0 ? 1 / rate + tool.Recipe.Wood / extraction : double.PositiveInfinity;
            }
            // The baseline is the portfolio's future harvest, at its future field productivity.
            double baseHarvest = Math.Min(human, d.Meals / years) / Math.Max(1e-12, d.FutureHarvestProductivity * cfg.GatherPerPerson * cfg.DaysPerYear / cfg.BaseDays);
            foreach (var step in steps)
            {
                if (!(filled < total) || !(step.WorkerYears > 0)) break;
                double gain = step.AnnualGain;
                double benefit = life * (BucketValue(filled + gain, consumption, security, total, vFood, n.FoodRiskWeight * vFood, vGrowth)
                    - BucketValue(filled, consumption, security, total, vFood, n.FoodRiskWeight * vFood, vGrowth));
                double harvest = gain / Math.Max(1e-12, d.HarvestProductivity * cfg.GatherPerPerson * cfg.DaysPerYear / cfg.BaseDays);
                // The resolver overlaps maintenance and harvesting. Charge the
                // incremental maximum, not two payments for the same worker.
                double futureWorkers = Math.Max(upkeep + step.MaintenancePerYear, baseHarvest + harvest) - Math.Max(upkeep, baseHarvest);
                double replacement = step.MaintenancePerYear * life / Math.Max(1e-12, n.FarmToolFarmerYearsPerUnit);
                double covered = Math.Min(1, Math.Max(0, c.Inventory.FarmTools) * n.FarmToolFarmerYearsPerUnit / Math.Max(1e-12, (upkeep + step.MaintenancePerYear) * years));
                double cost = futureWorkers * life + (covered > 0 && replacement > 0 ? covered * replacement * toolLabour : 0);
                double value = benefit / step.WorkerYears;
                filled += gain;
                if (!(value > 0)) break;
                worker.Add(step.WorkerYears); values.Add(value); costs.Add(cost / step.WorkerYears); productivity.Add(step.Score); tiles.Add(step.Tile);
                baseHarvest += harvest; upkeep += step.MaintenancePerYear;
            }
            schedule.Work = worker.ToArray(); schedule.Benefit = values.ToArray(); schedule.FutureWork = costs.ToArray();
            schedule.Productivity = productivity.ToArray(); schedule.Tiles = tiles.ToArray();
            return schedule;
        }
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
        public static double Welfare(WorldState w, Camp c, double population, double n0)
        {
            var n = w.Config.Neolithic;
            double e = c.RudimentaryExpectation > 0 ? c.RudimentaryExpectation
                : n.RudimentaryExpectationFloor + (1 - n.RudimentaryExpectationFloor) * Numbers.Clamp(c.StationaryYears / n.RudimentaryExpectationRampYears, 0, 1);
            double dilution = population > 0 ? Math.Min(1, n0 / population) : 1;
            double clothing = 1 - e * (1 - Math.Min(1, c.ClothPerCapita * dilution));
            double housing = 1 - e * (1 - Math.Min(1, c.HousingPerCapita * dilution));
            double q = c.FoodNeedsMetRatio * c.WaterNeedsMetRatio * NeolithicProduction.SocialCohesion(w, c, population)
                * (n.RudimentaryWeightClothing * clothing + n.RudimentaryWeightHousing * housing);
            return population * q;
        }
        public static double MarginalWelfare(WorldState w, Camp c)
            => Welfare(w, c, c.Population + 1, c.Population) - Welfare(w, c, c.Population, c.Population);
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
            var b = new List<double>(); var u = new List<double>(); var productivity = new List<double>(); var tiles = new List<int>();
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
                b.Add(step.WorkerYears); u.Add(floor); productivity.Add(step.Score); tiles.Add(step.Tile);
            }
            return new ExpansionLadderClaim { WorkerYears = b.ToArray(), Value = u.ToArray(), FoodPerWorkerYear = productivity.ToArray(), Tiles = tiles.ToArray(), TotalWorkerYears = b.Sum() };
        }
        public static void Apply(WorldState w, Camp c, CampProductionDemand d, bool prepareHerdChoices = false)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            if (!n.ValueFulfilmentSelected || !d.CultivationEnabled || c.Population <= 0) return;
            double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals;
            double cloth = d.Goals.TryGetValue(CraftGood.Cloth, out double goal) ? goal : 0;
            // Same forecast as the planner's expansion gate.
            double forecast = d.Biological * d.NextPopulation / Math.Max(1, c.Population) + cloth * n.PlantFoodPerFabricUnit;
            // Herd food the herds are expected to add closes the food gap before any field is valued (Docs/HusbandrySettling.md).
            double supply = prepareHerdChoices ? d.ForageAfterHerds : d.Supply + d.HerdGrowthFood;
            if (!(forecast > supply) || !d.HasRetainedExpansionGain() || !(years > 0)) return;
            // Buckets from the very targets that size the planning demand (the
            // planning cohort, as PlanningFoodDemand), per year.
            TurnResolver.FoodBucketTargets(w, c, intervals, years, d.Consumers * cfg.Consumption * intervals, c.Food,
                out double consumption, out _, out double reserveFill, out double maximumGrowthFood);
            double security = consumption + reserveFill, growth = maximumGrowthFood + cloth * n.PlantFoodPerFabricUnit;
            d.GapConsumptionPerYear = Math.Max(0, consumption - supply) / years;
            d.GapSecurityPerYear = Math.Max(0, security - supply) / years;
            d.GapTotalPerYear = Math.Max(0, security + growth - supply) / years;
            d.FoodValue = FoodValue(cfg, c);
            double maxRate = QoLMortality.MaximumRate(w, c);
            d.FoodPerBirth = FoodPerBirth(cfg.Consumption * intervals, maxRate, years);
            d.MarginalWelfarePerBirth = MarginalWelfare(w, c);
            d.PersonLife = PersonLife(n);
            d.GrowthFoodValue = d.FoodPerBirth > 0 ? Math.Max(0, d.MarginalWelfarePerBirth) * d.PersonLife / d.FoodPerBirth : 0;
            d.CapitalLife = CapitalLife(n);
            // A physically mobile, herd-bearing camp can forfeit herd value
            // when field investment makes it a village; its ladder is then
            // read in full so the crossing step can be found and charged.
            // First-breakthrough identity never participates.
            bool settling = SettlingApplies(w, c, d);
            var steps = d.PhysicalExpansion ?? CultivationEconomy.ExpansionLadder(w, c, years, d.Footprint, settling ? double.PositiveInfinity : d.GapTotalPerYear);
            d.PhysicalExpansion = steps;
            d.LadderSteps = steps.Length; d.LadderWorkerYearsUntruncated = steps.Sum(x => x.WorkerYears);
            double vReserve = n.FoodRiskWeight * d.FoodValue;
            var ladder = Value(steps, d.GapConsumptionPerYear, d.GapSecurityPerYear, d.GapTotalPerYear, d.FoodValue, vReserve, d.GrowthFoodValue, d.CapitalLife);
            if (settling) ladder = SettlingTerms(w, c, d, steps, ladder, consumption, security, growth, years, vReserve);
            if (ladder != null && ladder.Any) d.Expansion = ladder;
        }

        // Not yet a village, with managed animals to lose. Equivalent physical
        // camps receive the same transition term regardless of which capability
        // happened to be learned first. Existing villages and a zero penalty
        // remain unaffected.
        private static bool SettlingApplies(WorldState w, Camp c, CampProductionDemand d)
            => w.Config.SettledHerdGrowthPenalty > 0 && !c.IsVillage &&
                d.GrownHerdFood > 0;

        // The food value of `food` per year filled into the buckets from their start (consumption, reserve refill, growth), with the
        // bucket sizes given per year (raw gaps, before the herd credit): the same bucket values the ladder uses, per year.
        internal static double BucketValue(double food, double gapConsumption, double gapSecurity, double gapTotal, double vFood, double vReserve, double vGrowth)
        {
            gapConsumption = Math.Min(gapConsumption, gapSecurity);
            double inC = Math.Max(0, Math.Min(food, gapConsumption));
            double inR = Math.Max(0, Math.Min(food, gapSecurity) - gapConsumption);
            double inG = Math.Max(0, Math.Min(food, gapTotal) - gapSecurity);
            return inC * vFood + inR * vReserve + inG * vGrowth;
        }

        // Opportunity cost of settling (person-QoL-years) = SettledHerdGrowthPenalty x the food value of the herd's forecast (grown)
        // annual food over the capital life. Applied to the step at which the footprint average reaches the village threshold:
        // settling is worth it only if the value of that step and every later one exceeds the cost, in which case the tail
        // is scaled down by the cost (ordering and monotonicity are kept); otherwise the ladder stops before the crossing step and
        // ExpansionEffortCap limits the effort the planner may spend so spare residents do not cross it either.
        private static ExpansionLadderClaim SettlingTerms(WorldState w, Camp c, CampProductionDemand d, CultivationEconomy.ExpansionStep[] steps,
            ExpansionLadderClaim ladder, double consumption, double security, double growth, double years, double vReserve)
        {
            int cross = -1;
            for (int i = 0; i < steps.Length; i++)
                if (steps[i].VillageAverage + 1e-10 >= CultivationEconomy.VillageIntensity) { cross = i; break; }
            if (cross < 0) return ladder;
            // Raw (pre-credit) gaps value the herd's food: the herd is part of supply and the loss widens the gap at its start.
            double rawSupply = d.Supply;
            double gc = Math.Max(0, consumption - rawSupply) / years, gs = Math.Max(0, security - rawSupply) / years, gt = Math.Max(0, security + growth - rawSupply) / years;
            double herdFoodPerYear = d.GrownHerdFood / years;
            double cost = w.Config.SettledHerdGrowthPenalty * d.CapitalLife
                * BucketValue(herdFoodPerYear, gc, gs, gt, d.FoodValue, vReserve, d.GrowthFoodValue);
            d.SettlingCost = cost;
            double tail = 0;
            for (int i = cross; i < ladder.Value.Length; i++) tail += ladder.Value[i] * ladder.WorkerYears[i];
            if (tail > cost && tail > 0)
            {
                d.SettlesByExpansion = true;
                double scale = 1 - cost / tail;
                var value = (double[])ladder.Value.Clone();
                var productivity = (double[])ladder.FoodPerWorkerYear.Clone();
                for (int i = cross; i < value.Length; i++) { value[i] *= scale; productivity[i] *= scale; }
                return new ExpansionLadderClaim { WorkerYears = ladder.WorkerYears, Value = value,
                    FoodPerWorkerYear = productivity, Tiles = ladder.Tiles, TotalWorkerYears = ladder.TotalWorkerYears };
            }
            double prefix = 0;
            for (int i = 0; i < cross; i++) prefix += steps[i].WorkerYears;
            d.ExpansionEffortCap = prefix;
            if (cross >= ladder.Value.Length) return ladder;
            var worker = ladder.WorkerYears.Take(cross).ToArray(); var val = ladder.Value.Take(cross).ToArray();
            return new ExpansionLadderClaim { WorkerYears = worker, Value = val,
                FoodPerWorkerYear = ladder.FoodPerWorkerYear.Take(cross).ToArray(), Tiles = ladder.Tiles.Take(cross).ToArray(), TotalWorkerYears = worker.Sum() };
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
        // DemandControl (Docs/EconomicControlContract.md §15): the allocated output per good this turn. The forecast and the
        // resolver produce no more than it; goods without a ceiling are unbounded (every Legacy order).
        private readonly Dictionary<CraftGood, double> ceilings;
        public double Ceiling(CraftGood good) => ceilings != null && ceilings.TryGetValue(good, out double c) ? c : double.PositiveInfinity;
        public bool HasCeilings => ceilings != null;
        // Diagnostic prototype only (DemandController.KernelSplitDiagnostic): clothing units
        // the Elder committed ahead of meals; negative (the default) means none.
        internal double ProtectedClothingUnits = -1;
        internal IrrigationOrder Irrigation;
        // An explicit access order with output ceilings (DemandControl).
        internal ProductionOrder(CraftGood[] goods, IReadOnlyDictionary<CraftGood, double> allocated)
        {
            Goods = goods;
            for (int i = 0; i < goods.Length; i++) rank[goods[i]] = i;
            ceilings = new Dictionary<CraftGood, double>();
            foreach (var pair in allocated) ceilings[pair.Key] = Math.Max(0, pair.Value);
        }
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
