using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class SettlementWelfareReport
    {
        public int Camp, Society, Lineage, Population, Emigrated, Immigrated, SplitOff;
        public double QoL, PopulationWeightedQoL;
        public bool TransferAffected => Emigrated > 0 || Immigrated > 0 || SplitOff > 0;
    }

    [Serializable]
    public sealed class LineageWelfareReport
    {
        public int Lineage, Population, Emigrated, Immigrated, FissionTransfers;
        // PopulationWeightedQoL is the additive reward-shaped amount; MeanQoL is its
        // descriptive normalization. Keeping both avoids mistaking a transfer-driven
        // change in jurisdiction size for a change in experienced welfare.
        public double PopulationWeightedQoL, MeanQoL;
        public bool TransferAffected => Emigrated > 0 || Immigrated > 0 || FissionTransfers > 0;
    }

    // Pure end-of-turn measurement. It deliberately does not feed the Elder reward, utility, policy or demography.
    public static class WelfareDiagnostics
    {
        public static void Capture(WorldState world, TurnRecord record)
        {
            var societies = world.Societies.ToDictionary(s => s.Id);
            int Root(int society)
            {
                var seen = new HashSet<int>(); int current = society;
                while (current > 0 && seen.Add(current) && societies.TryGetValue(current, out var s) && s.ParentSociety > 0)
                    current = s.ParentSociety;
                return current;
            }
            var demographics = record.Demographics.ToDictionary(d => d.Camp);
            record.SettlementWelfare = world.Camps.OrderBy(c => c.Id).Select(c =>
            {
                demographics.TryGetValue(c.Id, out var d);
                int population = world.LinkedPopulation(c);
                return new SettlementWelfareReport
                {
                    Camp = c.Id, Society = c.SocietyId, Lineage = Root(c.SocietyId), Population = population,
                    QoL = c.QoL, PopulationWeightedQoL = population * c.QoL,
                    Emigrated = d?.Emigrated ?? 0, Immigrated = d?.Immigrated ?? 0, SplitOff = d?.SplitOff ?? 0
                };
            }).ToList();

            var campLineage = world.Camps.ToDictionary(c => c.Id, c => Root(c.SocietyId));
            record.LineageWelfare = record.SettlementWelfare.GroupBy(s => s.Lineage).OrderBy(g => g.Key).Select(g =>
            {
                int population = g.Sum(s => s.Population);
                int emigrated = record.Migrations.Where(m => campLineage.TryGetValue(m.SourceCamp, out int source) && source == g.Key
                    && campLineage.TryGetValue(m.DestinationCamp, out int destination) && destination != source).Sum(m => m.Migrants);
                int immigrated = record.Migrations.Where(m => campLineage.TryGetValue(m.DestinationCamp, out int destination) && destination == g.Key
                    && campLineage.TryGetValue(m.SourceCamp, out int source) && source != destination).Sum(m => m.Migrants);
                return new LineageWelfareReport
                {
                    Lineage = g.Key, Population = population,
                    PopulationWeightedQoL = g.Sum(s => s.PopulationWeightedQoL),
                    MeanQoL = population > 0 ? g.Sum(s => s.PopulationWeightedQoL) / population : 0,
                    Emigrated = emigrated, Immigrated = immigrated,
                    FissionTransfers = record.Fissions.Where(f => Root(f.ParentSociety) == g.Key).Sum(f => f.Daughter)
                };
            }).ToList();
        }
    }
}
