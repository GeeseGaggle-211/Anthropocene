using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public static class Supply
    {
        public static bool InRange(WorldState w, Camp camp, int tile) => w.Map.Reachable(camp.Tile, tile, w.Config.SupplyRange, n => w.CanEnter(n, true, camp));
        public static bool Connected(WorldState w, Hunter h) => InRange(w, w.Camps.First(c => c.Id == h.SourceCamp), h.Tile);
        public static double Capacity(WorldState w, Hunter h) => h.Population * Math.Max(w.Config.CarryingCapacityPerHunter, w.Config.Consumption * w.Config.GenerationIntervals * w.Config.HunterCarryGenerations);
        private static LogisticsRecord Entry(TurnRecord record, Hunter h)
        {
            var entry = record.Logistics.FirstOrDefault(e => e.Hunter == h.Id);
            if (entry == null) { entry = new LogisticsRecord { Hunter = h.Id, Camp = h.SourceCamp }; record.Logistics.Add(entry); }
            return entry;
        }
        public static void ProvisionDepartures(WorldState w, List<Order> orders, TurnRecord record)
            => ProvisionDepartures(w, orders, record, null);
        internal static void ProvisionDepartures(WorldState w, List<Order> orders, TurnRecord record, CampPlacement placement)
        {
            foreach (var hunter in w.Hunters.OrderBy(h => h.Id))
            {
                var camp = w.Camps.First(c => c.Id == hunter.SourceCamp);
                bool moving = orders.Any(o => o.Entity == hunter.Id && o.Destination >= 0) ||
                    (placement == null ? orders.Any(o => o.Entity == camp.Id && o.Destination >= 0) :
                        placement.Destinations[camp.Id] != camp.Tile);
                if (!moving || !Connected(w, hunter)) continue;
                // Withdraw while the old planning-position supply path still exists, before movement commits.
                double desired = Math.Min(Capacity(w, hunter), hunter.Population * w.Config.Consumption * w.Config.GenerationIntervals * w.Config.ExpeditionProvisionTurns);
                double reserve = camp.Population * w.Config.Consumption * w.Config.GenerationIntervals * w.Config.CampProvisionReserveTurns;
                double loaded = Math.Min(Math.Max(0, desired - hunter.CarriedFood), Math.Max(0, camp.Food - reserve));
                camp.Food -= loaded; hunter.CarriedFood += loaded; Entry(record, hunter).ProvisionsLoaded += loaded;
            }
        }
        public static void DepositReturns(WorldState w, TurnRecord record)
        {
            foreach (var hunter in w.Hunters.OrderBy(h => h.Id))
            {
                var entry = Entry(record, hunter); entry.InSupply = Connected(w, hunter);
                if (!entry.InSupply || hunter.CarriedFood <= 0) continue;
                var camp = w.Camps.First(c => c.Id == hunter.SourceCamp);
                entry.Deposited += hunter.CarriedFood; camp.Food += hunter.CarriedFood; hunter.CarriedFood = 0;
            }
        }
        public static void Award(WorldState w, Hunter hunter, double food, TurnRecord record, string kind = null)
        {
            var entry = Entry(record, hunter); entry.InSupply = Connected(w, hunter);
            if (entry.InSupply)
            {
                var camp = w.Camps.First(c => c.Id == hunter.SourceCamp);
                if (kind == "Hunting") camp.AnimalFood += food;
                else if (kind == "Fishing") camp.AquaticFood += food;
                else camp.PlantFood += food;
                camp.StepFoodIncome += food;
            }
            else
            {
                double retained = Math.Min(food, Math.Max(0, Capacity(w, hunter) - hunter.CarriedFood));
                hunter.CarriedFood += retained; entry.RetainedHarvest += retained; entry.Overflow += food - retained; entry.HarvestOverflow += food - retained;
            }
        }
        public static void Finish(WorldState w, TurnRecord record)
        {
            foreach (var hunter in w.Hunters.OrderBy(h => h.Id))
            {
                var entry = Entry(record, hunter); entry.InSupply = Connected(w, hunter);
                if (hunter.CarriedFood > Capacity(w, hunter)) { entry.InventoryLoss += hunter.CarriedFood - Capacity(w, hunter); entry.Overflow += hunter.CarriedFood - Capacity(w, hunter); hunter.CarriedFood = Capacity(w, hunter); }
                entry.EndingCarriedFood = hunter.CarriedFood;
            }
        }
    }
}
