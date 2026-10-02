using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // A local portfolio service inside EconomicPlanner, shared by all capable
    // camps. No lifestyle planner, trial world mutation or per-year transition.
    // Operation choices are induced by the existing 32 q bands and tool/staffing
    // breakpoints. Forecasts are cached once; price queries visit only totals.
    internal sealed class SharedAllocation
    {
        internal sealed class Choice
        {
            public CampProductionDemand Demand;
            public HerdLadder Herd;
            public int Herders, Farmers, Workers, Target;
            public double AnimalFood, Value, CurrentWork, FutureWork, Operation, ExpansionYears;
        }
        private sealed class Portfolio
        {
            public CampProductionDemand Demand;
            public HerdLadder Herd;
            public SharedExpansionSchedule[] Expansion;
            public double Value, CurrentWork, FutureWork, Operation;
        }
        private readonly WorldState world;
        private readonly Camp camp;
        private readonly Portfolio[] portfolios;
        private readonly double years, currentLife;
        internal int OperationChoices => portfolios.Length;
        internal int HerdChoices => portfolios.Sum(p => (p.Herd?.Steps ?? 0) + 1);
        private double[] priceCrossings;
        internal double[] PriceCrossings
        {
            get
            {
                if (priceCrossings != null) return priceCrossings;
                var values = new List<double>(); var work = new List<double>();
                foreach (var p in portfolios)
                    for (int k = p.Herd?.IndexOf(p.Herd.MinimumHerders) ?? 0; k <= (p.Herd?.Steps ?? 0); k++)
                    {
                        values.Add(p.Value + (p.Herd?.TotalValue[k] ?? 0));
                        work.Add(p.CurrentWork + p.FutureWork + (p.Herd?.WorkerYears[k] ?? 0));
                    }
                var envelope = new HerdLadder { Value = new double[values.Count - 1], Capacity = new double[values.Count],
                    TotalValue = values.ToArray(), WorkerYears = work.ToArray() };
                return priceCrossings = envelope.PriceCrossings;
            }
        }
        internal SharedAllocation(WorldState world, Camp camp, CampProductionDemand demand, double breeding)
        {
            this.world = world; this.camp = camp;
            var cfg = world.Config; var n = cfg.Neolithic;
            years = cfg.TurnDays / cfg.DaysPerYear;
            double future = Math.Max(0, n.PlanningHorizonYears - years), r = n.DiscountRatePerYear;
            currentLife = HerdLabour.DiscountedYears(r, years);
            double futureLife = Math.Exp(-r * years) * HerdLabour.DiscountedYears(r, future);
            double raw = demand.CultivationEnabled ? CultivationEconomy.MaintenanceWorkerRequirement(world, camp) : 0;
            double toolWorkers = Math.Max(0, camp.Inventory.FarmTools) * n.FarmToolFarmerYearsPerUnit / years;
            double efficiency = CultivationEconomy.FarmToolFieldWorkEfficiency(n);
            double Workers(double fraction)
            {
                double required = raw * fraction;
                double covered = Math.Min(toolWorkers, required / efficiency);
                return covered + Math.Max(0, required - covered * efficiency);
            }
            var staffing = new SortedSet<int> { 0, (int)Math.Min(camp.Population, Math.Ceiling(Workers(1) - 1e-9)) };
            if (raw > 0)
            {
                double q = demand.Operated.Length > 0 ? demand.Operated.Average(id => CultivationEconomy.Intensity(cfg, world.Tiles[id].CultivationCapital)) : 0;
                double decay = Math.Log(2) / n.DecayHalfLifeYears * Math.Max(years, future);
                for (int band = 1; band < 32 && q > 0 && decay > 0; band++)
                {
                    double fraction = 1 + 2 * Math.Log((band / 32.0) / q) / decay;
                    if (fraction > 0 && fraction < 1) staffing.Add((int)Math.Min(camp.Population, Math.Ceiling(Workers(fraction) - 1e-9)));
                }
                staffing.Add((int)Math.Min(camp.Population, Math.Ceiling(Math.Min(toolWorkers, raw / efficiency))));
            }
            var choices = new List<Portfolio>();
            foreach (int farmers in staffing)
            {
                double covered = Math.Min(farmers, Math.Min(toolWorkers, raw / efficiency));
                double operation = raw > 0 ? Math.Min(1, (covered * efficiency + Math.Max(0, farmers - covered)) / raw) : 0;
                var d = demand.AllocationCopy(); d.OperationFarmers = farmers; d.Upkeep = farmers;
                d.PhysicalExpansion = null; d.Expansion = null; d.HerdLadder = null;
                double land = 0, futureLand = 0, futureMaintenance = 0, residual = 0, futureBiomass = 0;
                foreach (int id in d.Eligible.Where(id => world.Tiles[id].Land))
                {
                    var tile = world.Tiles[id]; var opening = LazyEcology.ProjectEcology(world, id, world.Days);
                    bool owned = d.Operated.Contains(id);
                    double start = Math.Max(0, opening.CultivationCapital);
                    double op = owned ? operation : tile.CultivationOperatingFraction;
                    double Mean(double duration)
                    {
                        double rate = Math.Log(2) / n.DecayHalfLifeYears * (1 - op);
                        return rate * duration > 1e-10 ? start * (1 - Math.Exp(-rate * duration)) / (rate * duration) : start;
                    }
                    double end = CultivationEconomy.Decay(cfg, start, op, years);
                    double request = LazyEcology.AccessibleAvailability(world, id, world.Days, cfg.TurnDays, op, Mean(years));
                    var current = FoodHorizonForecast.Tile(world, id, world.Days, cfg.TurnDays,
                        new[] { new Demand { Tile = id, Entity = camp.Id, Kind = "Gathering", Food = request } }, Mean(years), end, op, false, opening);
                    land += current.HumanFoodReceipts;
                    // Conservative repeatable refill: an emptied food pool at the
                    // horizon's mean retained capital. No gross renewal is sold.
                    double terminalCapital = CultivationEconomy.Decay(cfg, start, op, Math.Max(years, n.PlanningHorizonYears));
                    var empty = opening; empty.Food = 0; empty.CultivationCapital = Mean(Math.Max(years, n.PlanningHorizonYears));
                    empty.PotentialFood = Math.Max(0, current.ClosingBiomass - current.ClosingFood);
                    var refill = FoodHorizonForecast.Tile(world, id, world.Days, cfg.TurnDays, null,
                        empty.CultivationCapital, terminalCapital, op, false, empty);
                    futureLand += refill.ClosingFood;
                    futureBiomass += refill.ClosingBiomass;
                    residual += refill.ClosingFood;
                    if (owned && raw > 0)
                    {
                        CultivationEconomy.Factors(cfg, tile, out _, out double maintenance);
                        futureMaintenance += n.MaintenanceWorkersPerTileE(cfg.MaintenanceCoefficient) * maintenance * op
                            * .5 * (CultivationEconomy.EffortForCapital(cfg, start) + CultivationEconomy.EffortForCapital(cfg, terminalCapital));
                    }
                }
                double aquatic = Math.Max(0, demand.ForageSupply - demand.PlantSupply);
                d.PlantSupply = land; d.ForageSupply = land + aquatic;
                d.TerrestrialAfterHerds = Math.Max(0, land - demand.ManagedFood.HumanFoodDisplaced);
                d.ForageAfterHerds = d.TerrestrialAfterHerds + aquatic;
                d.FutureForageAfterHerds = Math.Max(0, futureLand - demand.ManagedFood.HumanFoodDisplaced) + aquatic;
                d.FutureUpkeep = futureMaintenance;
                double forageNeed = d.ManagedFood.FoodConsumption * cfg.GenerationIntervals;
                d.FuturePastureCapacity = forageNeed > 0
                    ? Math.Max(0, futureBiomass - Math.Min(futureLand, d.Meals)) / forageNeed : double.PositiveInfinity;
                double maximumNeed = Math.Max(0, (d.Meals + d.Biological) / years - d.FutureForageAfterHerds / years);
                d.PhysicalExpansion = d.CultivationEnabled && maximumNeed > 0
                    ? CultivationEconomy.ExpansionLadder(world, camp, years, d.Footprint, maximumNeed, operation)
                    : Array.Empty<CultivationEconomy.ExpansionStep>();
                d.SetFoodEnvelopes(world, camp, demand.ManagedFood.AnimalFood);
                var ledger = new FoodIncomeLedger(world, camp, d);
                var incoming = ledger.Receive(d.TerrestrialAfterHerds, aquatic, 0);
                double vFood = ExpansionValuation.FoodValue(cfg, camp);
                double annualFood = d.FutureForageAfterHerds / years;
                double futureMeals = Math.Min(d.Meals / years, annualFood);
                double usefulCurrent = incoming.Consumption + incoming.Reserve + incoming.Growth + incoming.IncomingPlantInput;
                double gatherRate = cfg.GatherPerPerson * cfg.DaysPerYear / cfg.BaseDays;
                double harvestRate = gatherRate * Math.Max(1, d.HarvestProductivity);
                double currentHarvest = Math.Min(d.TerrestrialAfterHerds, farmers * harvestRate * years);
                double currentWorkers = farmers + Math.Max(0, usefulCurrent - currentHarvest) / years / gatherRate;
                double futureHarvest = Math.Min(futureMeals, futureMaintenance * harvestRate);
                double futureWorkers = futureMaintenance + Math.Max(0, futureMeals - futureHarvest) / gatherRate;
                double value = Math.Exp(-r * years) * (incoming.Value - ledger.Receive(0, 0, 0).Value)
                    + futureLife * futureMeals * vFood
                    + Math.Exp(-r * n.PlanningHorizonYears) * n.FoodRiskWeight * vFood * Math.Min(residual, d.Meals);
                var herd = HerdLabour.Applies(world, camp) ? HerdLabour.Build(world, camp, d, breeding) : null;
                choices.Add(new Portfolio { Demand = d, Herd = herd, Expansion = new SharedExpansionSchedule[(herd?.Steps ?? 0) + 1], Value = value,
                    CurrentWork = currentWorkers * currentLife, FutureWork = futureWorkers * futureLife, Operation = operation });
            }
            portfolios = choices.ToArray();
        }
        // Current spare work can have zero price without making its committed
        // future work free. The full craft planner runs only for the selected
        // portfolio, not for each herder or a population Cartesian product.
        internal Choice Select(double currentPrice, double futurePrice, int residentBudget)
        {
            currentPrice = double.IsNaN(currentPrice) ? 0 : Math.Max(0, currentPrice);
            futurePrice = double.IsNaN(futurePrice) ? 0 : Math.Max(0, futurePrice);
            Choice best = null; Portfolio selected = null; SharedExpansionSchedule selectedExpansion = null; double bestNet = double.NegativeInfinity;
            foreach (var p in portfolios)
            {
                var h = p.Herd;
                int first = h == null ? 0 : h.IndexOf(h.MinimumHerders);
                for (int k = first; k <= (h?.Steps ?? 0); k++)
                {
                    double current = p.CurrentWork + (h?.CurrentWorkerYears[k] ?? 0);
                    double future = p.FutureWork + (h == null ? 0 : h.WorkerYears[k] - h.CurrentWorkerYears[k]);
                    int fixedWorkers = p.Demand.OperationFarmers + (h?.CountAt(k) ?? p.Demand.ReservedHerders);
                    int workers = fixedWorkers + (int)Math.Max(0, Math.Ceiling(current / Math.Max(1e-12, currentLife) - fixedWorkers - 1e-9));
                    if (workers > residentBudget) continue;
                    double animal = h == null ? p.Demand.ManagedFood.AnimalFood : h.CurrentAnimalFood[k] + h.SlaughterFood[k];
                    var expansion = p.Expansion[k] ?? (p.Expansion[k] = ExpansionValuation.SharedSchedule(world, camp, p.Demand, animal, h?.FutureAnnualFood[k]));
                    expansion.Purchase(currentPrice, futurePrice, (residentBudget - workers) * years, out double expansionYears, out double expansionValue);
                    double value = p.Value + (h?.TotalValue[k] ?? 0);
                    double net = value - currentPrice * current - futurePrice * future + expansionValue;
                    if (net <= bestNet + 1e-9) continue;
                    bestNet = net; selected = p; selectedExpansion = expansion; best = new Choice { Herd = h, Herders = h?.CountAt(k) ?? p.Demand.ReservedHerders,
                        Farmers = p.Demand.OperationFarmers, Workers = workers, Target = h?.Target(h.CountAt(k)) ?? 0,
                        AnimalFood = animal, Value = value, CurrentWork = current, FutureWork = future, Operation = p.Operation, ExpansionYears = expansionYears };
                }
            }
            if (best != null) best.Demand = selected.Demand.WithSharedExpansion(world, camp, best.AnimalFood, selectedExpansion.Price(futurePrice));
            // Minimum seed staffing is still bounded by residents. A distressed
            // zero-fulfilment plan may be short of food; it never creates workers.
            if (best == null)
            {
                var p = portfolios[0]; var h = p.Herd; int k = h?.IndexOf(h.MinimumHerders) ?? 0;
                double animal = h == null ? p.Demand.ManagedFood.AnimalFood : h.CurrentAnimalFood[k] + h.SlaughterFood[k];
                best = new Choice { Demand = p.Demand.WithAnimalFood(world, camp, animal), Herd = h,
                    Herders = h?.CountAt(k) ?? p.Demand.ReservedHerders, Target = h?.Target(h.CountAt(k)) ?? 0, AnimalFood = animal };
            }
            return best;
        }
    }
}
