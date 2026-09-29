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
        // Fulfilment weight for value-based rules. The current uniform rule
        // ignores it; it defaults to Priority until a value model is chosen.
        public double Value;
        // Role that performs the work and its output per worker-interval.
        public string Role;
        public double Rate;
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
            d.Goods = d.Recipes.Where(r => r.Good != CraftGood.HandTools && r.Good != CraftGood.Tools && (!r.HusbandryOnly || d.HusbandryEnabled)).ToArray();
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
            return d;
        }
    }
}
