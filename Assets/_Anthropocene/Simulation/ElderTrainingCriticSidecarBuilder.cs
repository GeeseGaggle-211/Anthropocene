using System;

namespace Anthropocene.Simulation
{
    public static class ElderTrainingCriticSidecarBuilder
    {
        public static ElderTrainingCriticSidecar Build(WorldState world,
            Camp camp)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (camp == null) throw new ArgumentNullException(nameof(camp));
            var cultivation = SettlementProductionFrontier
                .ResolveCultivation(world, camp);
            var fishing = SettlementProductionFrontier
                .ResolveFishing(world, camp);
            var memory = camp.ElderMemory;
            return new ElderTrainingCriticSidecar
            {
                EffectiveCultivationEffortKneeQ = cultivation.KneeQ,
                EffectiveFishingEffortKneeQ = fishing.KneeQ,
                CultivationFrontierIdentity = cultivation.Identity,
                FishingFrontierIdentity = fishing.Identity,
                CultivationRegimeChanged = memory?
                    .CultivationFrontierChangedAtLastBoundary ?? false,
                FishingRegimeChanged = memory?
                    .FishingFrontierChangedAtLastBoundary ?? false,
                HasCultivationRegimeChange = memory?
                    .HasCultivationFrontierChange ?? false,
                HasFishingRegimeChange = memory?
                    .HasFishingFrontierChange ?? false,
                CultivationRegimeChangeDays = memory?
                    .LastCultivationFrontierChangeDays ?? 0,
                FishingRegimeChangeDays = memory?
                    .LastFishingFrontierChangeDays ?? 0
            };
        }
    }
}
