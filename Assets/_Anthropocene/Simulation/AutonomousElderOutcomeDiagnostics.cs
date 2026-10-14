using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Closed-turn analysis sidecar; never part of actor observations or rewards.
    public sealed class AutonomousElderOutcomeDiagnostics
    {
        public int SchemaVersion = 1;
        public int OpeningPopulation, EndingPopulation, ResidentGrowth, StarvationDeaths;
        public int Immigrants, Emigrants, FissionHeadcount, OtherResidentChange;
        public Dictionary<string, int> FissionByTrigger;
        public double FoodReserveYears, MeanFieldIntensity;

        internal static AutonomousElderOutcomeDiagnostics Capture(WorldState world, int id)
        {
            if (world.AutonomousReceipts == null || !world.AutonomousReceipts.TryGetValue(id, out var receipt) ||
                !receipt.HasDecision) throw new InvalidOperationException("Closed owner receipt required.");
            var last = world.History.Last();
            if (last.StartDays + last.Duration != receipt.EndDays)
                throw new InvalidOperationException("Owner receipt does not match the last closed turn.");
            var camp = world.Camps.FirstOrDefault(c => c.Id == id);
            var residentChanges = last.PopulationChanges.Where(p => p.Entity == id).ToArray();
            var splits = last.Fissions.Where(f => f.ParentCamp == id).ToArray();
            var result = new AutonomousElderOutcomeDiagnostics {
                OpeningPopulation = receipt.OpeningPopulation, EndingPopulation = receipt.EndingPopulation,
                ResidentGrowth = residentChanges.Where(p => p.Count > 0).Sum(p => p.Count),
                StarvationDeaths = -residentChanges.Where(p => p.Kind == "Starvation" && p.Count < 0).Sum(p => p.Count),
                Immigrants = last.Migrations.Where(m => m.DestinationCamp == id).Sum(m => m.Migrants),
                Emigrants = last.Migrations.Where(m => m.SourceCamp == id).Sum(m => m.Migrants),
                FissionHeadcount = splits.Sum(f => f.Daughter),
                FissionByTrigger = Enum.GetNames(typeof(FissionTrigger)).ToDictionary(name => name,
                    name => splits.Where(f => f.Trigger.ToString() == name).Sum(f => f.Daughter)),
                FoodReserveYears = camp == null || camp.Population <= 0 ? 0 :
                    camp.Food * world.Config.BaseDays / (camp.Population * world.Config.Consumption * world.Config.DaysPerYear)
            };
            var footprint = camp == null ? Array.Empty<int>() : CultivationEconomy.Footprint(world, camp).ToArray();
            result.MeanFieldIntensity = footprint.Length == 0 ? 0 : footprint.Average(tile =>
                CultivationEconomy.Intensity(world.Config, world.Tiles[tile].CultivationCapital));
            result.OtherResidentChange = result.EndingPopulation - result.OpeningPopulation - result.ResidentGrowth +
                result.StarvationDeaths - result.Immigrants + result.Emigrants + result.FissionHeadcount;
            if (result.Immigrants != receipt.Immigrated || result.Emigrants != receipt.Emigrated)
                throw new InvalidOperationException("Native migration report/owner receipt mismatch.");
            return result;
        }
    }
}
