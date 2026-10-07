using System;

namespace Anthropocene.Simulation
{
    // Pottery storage for finished goods (Docs/PlannerTradeStorageDesign.md §3, phase S), only while
    // SimulationConfig.GoodsStorageEnabled. Space is measured in food units, as Pots already store food
    // (PotStorageFoodPerUnit). Food keeps its natural storage; goods keep a base allowance of
    // GoodsBaseStoragePerResident units per resident; Pots are one pool shared by both. Every counted good takes
    // GoodsStorageSpacePerUnit of space; raw materials (Stone, Clay, Wood), Pots (the containers) and fixed Housing
    // (a structure) are not counted.
    //
    // Goods in use this turn take no space: clothing up to this turn's consumption, Housing Material up to the
    // Housing assembled, tents up to the people fixed Housing leaves unsheltered, Hand/Regular Tools up to this turn's
    // equipment outputs, Farm and Herding Tools up to this turn's need, Copper Tools (durable worker coverage) always.
    //
    // Additions are blocked, never destroyed: a goods addition is cut to what fits, and stock already above capacity
    // (Pots decay, a loaded save, a trade refund) is kept but receives nothing more that raises occupancy. Goods never
    // take the pot space the food operating reserve needs (reserve guard, §7 O1). Pure: reads the camp, writes nothing.
    internal sealed class GoodsStorage
    {
        internal const int Cloth = 0, AnimalFabric = 1, HousingMaterial = 2, LightHousing = 3, HandTools = 4, Tools = 5, FarmTools = 6, HerdingTools = 7, Ceramics = 8, FishingTools = 9, Count = 10;
        private readonly double space, baseUnits, naturalFood, reserveFood, potFood;
        private readonly double clothing, housingMaterial, tents, toolOutputs, farmTools, herdingTools, fishingTools;

        private GoodsStorage(double space, double baseUnits, double naturalFood, double reserveFood, double potFood,
            double clothing, double housingMaterial, double tents, double toolOutputs, double farmTools, double herdingTools, double fishingTools)
        {
            this.space = space; this.baseUnits = baseUnits; this.naturalFood = naturalFood; this.reserveFood = reserveFood; this.potFood = potFood;
            this.clothing = clothing; this.housingMaterial = housingMaterial; this.tents = tents; this.toolOutputs = toolOutputs; this.farmTools = farmTools; this.herdingTools = herdingTools; this.fishingTools = fishingTools;
        }

        public static bool Enabled(SimulationConfig cfg) => cfg != null && cfg.GoodsStorageEnabled;

        // The camp's storage terms for one turn. equipmentOutputs are the equipment and housing-work outputs this turn
        // consumes tools for; housingAssembly the fixed Housing units assembled (each consumes Housing Material);
        // endOfTurn drops the allowances for goods consumed during the turn (clothing, Housing Material, tool outputs).
        public static GoodsStorage Begin(WorldState w, Camp c, double years, double residents, double equipmentOutputs, double housingAssembly, bool endOfTurn = false)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            residents = Math.Max(0, residents < 0 ? c.Population : residents);
            double clothing = endOfTurn ? 0 : residents * n.FabricConsumptionPerPersonTurn * Math.Max(0, years) / n.ReferenceTurnYears;
            double material = endOfTurn ? 0 : Math.Max(0, housingAssembly) * n.HousingMaterialUnitsPerHousing;
            double fixedPeople = NeolithicProduction.HousingCapacity(w, c, w.Days).Fixed;
            double tents = n.LightHousingPeoplePerUnit > 0 ? Math.Max(0, residents - fixedPeople) / n.LightHousingPeoplePerUnit : 0;
            double farm = CultivationEconomy.Enabled(c) ? Math.Max(0, CultivationEconomy.ForecastFarmToolWorkerYears(w, c, years)) / n.FarmToolFarmerYearsPerUnit : 0;
            double herders = 0; foreach (var a in c.Workforce.Assignments) if (a.Role == WorkforceRoles.Herder) herders += Math.Max(0, a.Population);
            double animals = HerdingTooling.DomesticAnimals(w, c);
            double herding = animals > 0 ? Math.Min(herders, animals / Math.Max(1e-12, n.StoneHerdingToolAnimalsPerHerder)) * Math.Max(0, years) / n.HerdingToolCoverageYearsPerUnit : 0;
            double fishers = CultivationEconomy.Workers(c, WorkforceRoles.Fisher);
            double fishingTools = !endOfTurn && FishingEconomy.CanFish(w, c)
                ? Math.Min(Math.Max(0, c.Inventory.FishingTools), Math.Max(0, fishers) * Math.Max(0, years) / n.FishingToolFisherYearsPerUnit)
                : 0;
            return new GoodsStorage(cfg.GoodsStorageSpacePerUnit, cfg.GoodsBaseStoragePerResident * residents,
                NeolithicProduction.NaturalStorageCapacity(w, c, 0), NeolithicProduction.DesiredReserveFood(w, NeolithicProduction.ReservePopulation(w, c)), n.PotStorageFoodPerUnit,
                clothing, material, tents, endOfTurn ? 0 : Math.Max(0, equipmentOutputs), farm, herding, fishingTools);
        }

        // The counted stock vector (goods index; uncounted goods have index -1).
        public static int Index(CraftGood good)
        {
            switch (good)
            {
                case CraftGood.Cloth: return Cloth; case CraftGood.AnimalFabric: return AnimalFabric; case CraftGood.HousingMaterial: return HousingMaterial;
                case CraftGood.LightHousing: return LightHousing; case CraftGood.HandTools: return HandTools; case CraftGood.Tools: return Tools;
                case CraftGood.FarmTools: return FarmTools; case CraftGood.HerdingTools: return HerdingTools;
                case CraftGood.FishingTools: return FishingTools;
                default: return -1;
            }
        }
        public static double[] StockOf(Camp c)
        {
            var i = c.Inventory;
            return new[] { Math.Max(0, i.Cloth), Math.Max(0, i.AnimalFabric), Math.Max(0, i.HousingMaterial), Math.Max(0, i.LightHousingUnits),
                Math.Max(0, i.HandTools), Math.Max(0, i.Tools), Math.Max(0, i.FarmTools), Math.Max(0, i.HerdingTools), Math.Max(0, i.ImprovedCeramics), Math.Max(0, i.FishingTools) };
        }

        // Space the stock occupies beyond what is in use this turn.
        public double Occupied(double[] s)
        {
            double exemptHand = Math.Min(s[HandTools], toolOutputs);
            double exemptTools = Math.Min(s[Tools], Math.Max(0, toolOutputs - exemptHand) / ToolCoverageBudget.OutputsPerTool);
            double units = Math.Max(0, s[Cloth] + s[AnimalFabric] - clothing) + Math.Max(0, s[HousingMaterial] - housingMaterial) + Math.Max(0, s[LightHousing] - tents)
                + (s[HandTools] - exemptHand) + (s[Tools] - exemptTools) + Math.Max(0, s[FarmTools] - farmTools) + Math.Max(0, s[HerdingTools] - herdingTools) + s[Ceramics] + Math.Max(0, s[FishingTools] - fishingTools);
            return space * units;
        }
        // Pot space the food holds back: the larger of the stored food and the operating reserve, above natural storage.
        private double FoodPot(double food, double pots)
        {
            double p = Math.Max(0, pots) * potFood;
            return Math.Min(p, Math.Max(0, Math.Max(food, reserveFood) - naturalFood));
        }
        // Space for goods: the base allowance plus the pots the food does not hold back.
        public double Capacity(double food, double pots) => space * baseUnits + Math.Max(0, Math.Max(0, pots) * potFood - FoodPot(food, pots));
        public double Capacity(Camp c) => Capacity(c.Food, c.Inventory.Pots);
        // Food's end-of-turn capacity: natural storage plus the pots goods do not occupy, never less than the reserve's share.
        public double FoodCapacity(double pots, double goodsOccupied)
        {
            double p = Math.Max(0, pots) * potFood;
            double goodsPot = Math.Max(0, goodsOccupied - space * baseUnits);
            return naturalFood + Math.Max(p - goodsPot, Math.Min(p, Math.Max(0, reserveFood - naturalFood)));
        }

        // The largest a in [0, amount] such that making a units of `good`, consuming fabricPerUnit Animal Fabric and
        // materialPerUnit Housing Material per unit, keeps occupancy within capacity, or (already over capacity) does
        // not raise it. Occupancy is piecewise linear and convex or monotone in a, so the feasible set is an interval
        // from zero and bisection finds its end.
        public double Limit(double[] stock, double capacity, CraftGood good, double amount, double fabricPerUnit = 0, double materialPerUnit = 0)
        {
            int index = Index(good);
            if (!(amount > 0) || index < 0 && !(fabricPerUnit > 0) && !(materialPerUnit > 0)) return Math.Max(0, amount);
            double before = Occupied(stock), allowed = before >= capacity ? before : capacity + 1e-12 * Math.Max(1, capacity);
            var trial = new double[Count];
            double After(double a)
            {
                Array.Copy(stock, trial, Count);
                if (index >= 0) trial[index] += a;
                trial[AnimalFabric] = Math.Max(0, trial[AnimalFabric] - a * fabricPerUnit);
                trial[HousingMaterial] = Math.Max(0, trial[HousingMaterial] - a * materialPerUnit);
                return Occupied(trial);
            }
            if (After(amount) <= allowed) return amount;
            double lo = 0, hi = amount;
            for (int i = 0; i < 60; i++) { double mid = (lo + hi) / 2; if (After(mid) <= allowed) lo = mid; else hi = mid; }
            return lo;
        }
        public double Limit(Camp c, CraftGood good, double amount, double fabricPerUnit = 0, double materialPerUnit = 0)
            => Limit(StockOf(c), Capacity(c), good, amount, fabricPerUnit, materialPerUnit);
        // Free room in goods units (negative when over capacity).
        public double RoomUnits(Camp c) => space > 0 ? (Capacity(c) - Occupied(StockOf(c))) / space : double.PositiveInfinity;

        // Records a step's addition on a virtual stock vector (forecasts).
        public static void Add(double[] stock, CraftGood good, double amount, double fabricPerUnit = 0, double materialPerUnit = 0)
        {
            int index = Index(good);
            if (index >= 0) stock[index] += amount;
            stock[AnimalFabric] = Math.Max(0, stock[AnimalFabric] - amount * fabricPerUnit);
            stock[HousingMaterial] = Math.Max(0, stock[HousingMaterial] - amount * materialPerUnit);
        }

        // Equipment and housing-work outputs, and Housing assembled, in a production plan (by CraftGood).
        public static void PlannedUse(CraftRecipe[] recipes, Func<CraftGood, double> planned, out double equipmentOutputs, out double housingAssembly)
        {
            equipmentOutputs = 0; housingAssembly = 0;
            // Only equipment and housing work is read, so a forecast never runs the tool replacement schedule here.
            foreach (var r in recipes)
            {
                bool equipment = r.Equipment || ToolCoverageBudget.IsHousingWork(r.Good);
                if (!equipment && r.Good != CraftGood.Housing) continue;
                double q = Math.Max(0, planned(r.Good));
                if (equipment) equipmentOutputs += q;
                if (r.Good == CraftGood.Housing) housingAssembly += q;
            }
        }
    }
}
