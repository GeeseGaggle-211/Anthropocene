using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    // One transient receiving ledger for every food source. Opening inventory is
    // credited only by FoodBucketTargets, never added to an incoming shipment.
    // Typed production is earmarked once and uses the resolver's meals/clothing/
    // reserve precedence. Pots change capacity, not food income.
    internal sealed class FoodIncomeLedger
    {
        private readonly WorldState world;
        private readonly Camp camp;
        private readonly double intervals, years, meals, plantInput, animalInput, clothingPlant, clothingAnimal;
        private readonly double plantValue, animalValue, housingInput, housingValue, foodValue, growthValue;
        // FoodBucketTargets' camp part does not depend on the opening food.
        private readonly double reserveTarget, maximumGrowthFood;
        internal struct Receipt
        {
            public double Value, Consumption, Reserve, Growth, PlantInput, AnimalInput, IncomingPlantInput, IncomingAnimalInput, RemainingConsumption, RemainingReserve, RemainingGrowth;
        }
        public FoodIncomeLedger(WorldState world, Camp camp, CampProductionDemand demand)
        {
            this.world = world; this.camp = camp;
            intervals = world.Config.GenerationIntervals; years = world.Config.TurnDays / world.Config.DaysPerYear; meals = demand.Meals;
            var n = world.Config.Neolithic;
            var cloth = demand.Claims.FirstOrDefault(x => x.Good == CraftGood.Cloth);
            var fabric = demand.Claims.FirstOrDefault(x => x.Good == CraftGood.AnimalFabric);
            var housing = demand.Claims.FirstOrDefault(x => x.Good == CraftGood.LightHousing);
            double Price(ProductionClaim claim) => claim != null && claim.Valued && claim.Need > 0
                ? claim.FirstUnitValue * Math.Exp(-n.ValueCurveSteepness * claim.Stock / claim.Need) : 0;
            plantInput = Math.Max(0, cloth?.Quantity ?? 0) * n.PlantFoodPerFabricUnit;
            animalInput = Math.Max(0, fabric?.Quantity ?? 0);
            housingInput = Math.Min(animalInput, Math.Max(0, housing?.Quantity ?? 0));
            plantValue = n.PlantFoodPerFabricUnit > 0 ? Price(cloth) / n.PlantFoodPerFabricUnit : 0;
            animalValue = Price(fabric); housingValue = Price(housing);
            var clothing = NeolithicProduction.ClothingNeedFeedstock(n, SubsistenceNeeds.GoodsResidents(world, camp, camp.Population), years,
                camp.Inventory.Cloth - camp.TradeExport(CraftGood.Cloth), camp.Inventory.AnimalFabric - camp.TradeExport(CraftGood.AnimalFabric), plantInput, animalInput);
            clothingPlant = clothing.Plant; clothingAnimal = clothing.Animal;
            foodValue = ExpansionValuation.FoodValue(world.Config, camp);
            double perBirth = ExpansionValuation.FoodPerBirth(world.Config.Consumption * intervals, QoLMortality.MaximumRate(world, camp), years);
            growthValue = perBirth > 0 ? Math.Max(0, ExpansionValuation.MarginalWelfare(world, camp)) * ExpansionValuation.PersonLife(n) / perBirth : 0;
            TurnResolver.FoodBucketBasis(world, camp, intervals, years, meals, out reserveTarget, out maximumGrowthFood);
        }
        // Optional typed demands are part of the ledger, never a second food
        // stock. The same calculation prices both the human and animal axes.
        public Receipt Receive(double plant, double aquatic, double animal)
        {
            plant = Math.Max(0, plant); aquatic = Math.Max(0, aquatic); animal = Math.Max(0, animal);
            double incoming = plant + aquatic + animal;
            double total = Math.Max(0, camp.Food) + incoming;
            double afterMeals = Math.Max(0, total - meals);
            double p = Math.Min(clothingPlant, Math.Min(Math.Max(0, camp.PlantFood) + plant, afterMeals));
            double a = Math.Min(clothingAnimal, Math.Min(Math.Max(0, camp.AnimalFood) + animal, Math.Max(0, afterMeals - p)));
            double otherRoom = Math.Max(0, afterMeals - p - a - reserveTarget);
            double extraP = Math.Min(Math.Max(0, plantInput - p), Math.Min(Math.Max(0, camp.PlantFood) + plant - p, otherRoom));
            p += extraP; otherRoom -= extraP;
            a += Math.Min(Math.Max(0, animalInput - a), Math.Min(Math.Max(0, camp.AnimalFood) + animal - a, otherRoom));
            double openingP = Math.Min(Math.Max(0, camp.PlantFood), p), openingA = Math.Min(Math.Max(0, camp.AnimalFood), a);
            double incomingInput = p + a - openingP - openingA;
            // Removing an earmarked opening input widens the reserve refill; it
            // cannot fund that reserve and the recipe at the same time.
            TurnResolver.SplitFoodBuckets(meals, Math.Max(0, camp.Food - openingP - openingA), reserveTarget, out double consumption, out double reserve);
            double growth = maximumGrowthFood;
            double remaining = Math.Max(0, incoming - incomingInput);
            double paidMeals = Math.Min(consumption, remaining); remaining -= paidMeals;
            double paidReserve = Math.Min(reserve, remaining); remaining -= paidReserve;
            double paidGrowth = Math.Min(growth, remaining);
            double tentInput = Math.Min(a, housingInput);
            return new Receipt
            {
                Value = paidMeals * foodValue + paidReserve * world.Config.Neolithic.FoodRiskWeight * foodValue + paidGrowth * growthValue
                    + p * plantValue + (a - tentInput) * animalValue + tentInput * housingValue,
                Consumption = paidMeals, Reserve = paidReserve, Growth = paidGrowth, PlantInput = p, AnimalInput = a,
                IncomingPlantInput = p - openingP, IncomingAnimalInput = a - openingA,
                RemainingConsumption = consumption - paidMeals, RemainingReserve = reserve - paidReserve, RemainingGrowth = growth - paidGrowth
            };
        }
    }
}
