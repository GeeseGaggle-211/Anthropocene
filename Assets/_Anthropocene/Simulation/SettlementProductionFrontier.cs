using System;

namespace Anthropocene.Simulation
{
    // Settlement-local production capability resolution. Stored capital E and
    // q(E) remain physical state; this resolver changes only the labor needed
    // to maintain or add that state. Cost is O(1) per query and independent of
    // elapsed history or the planning horizon.
    public static class SettlementProductionFrontier
    {
        public const string Primitive = "primitive";
        public const string Plough = "plough";
        public const string Sailing = "sailing";
        public const string Copper = "copper-working";
        public const string Experimental = "experimental-override";

        public static double EffectiveCultivationEffortKnee(
            WorldState world, Camp camp)
            => ResolveCultivation(world, camp).KneeQ;

        public static double EffectiveFishingEffortKnee(
            WorldState world, Camp camp)
            => ResolveFishing(world, camp).KneeQ;

        public static ProductionFrontierResolution ResolveCultivation(
            WorldState world, Camp camp)
        {
            Require(world, camp);
            var n = world.Config.Neolithic;
            if (n.ExperimentalCultivationEffortKneeOverride)
                return new ProductionFrontierResolution(
                    n.CultivationMarginalEffortKneeQ, Experimental, true);

            double knee = n.PrimitiveAgricultureSupportedIntensity;
            string identity = Primitive;
            // Existing legal capability state supplies the seam; no new
            // technology tree or automatic physical-capital grant is created.
            if (camp.People.HasTech(NeolithicTech.Plough))
            {
                knee = Math.Max(knee, n.PloughSupportedIntensity);
                identity = Plough;
            }
            if (camp.People.HasTech(NeolithicTech.CopperWorking))
            {
                knee = Math.Max(knee, n.CopperFarmToolsSupportedIntensity);
                identity = Copper;
            }
            return new ProductionFrontierResolution(
                Numbers.Clamp(knee, 1e-12, 1), identity, false);
        }

        public static ProductionFrontierResolution ResolveFishing(
            WorldState world, Camp camp)
        {
            Require(world, camp);
            var n = world.Config.Neolithic;
            if (n.ExperimentalFishingEffortKneeOverride)
                return new ProductionFrontierResolution(
                    n.FishingMarginalEffortKneeQ, Experimental, true);

            double knee = n.PrimitiveAgricultureSupportedIntensity;
            string identity = Primitive;
            if (camp.People.HasTech(NeolithicTech.Sailing))
            {
                knee = Math.Max(knee, n.PloughSupportedIntensity);
                identity = Sailing;
            }
            if (camp.People.HasTech(NeolithicTech.CopperWorking))
            {
                knee = Math.Max(knee, n.CopperFarmToolsSupportedIntensity);
                identity = Copper;
            }
            return new ProductionFrontierResolution(
                Numbers.Clamp(knee, 1e-12, 1), identity, false);
        }

        private static void Require(WorldState world, Camp camp)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (camp == null) throw new ArgumentNullException(nameof(camp));
        }
    }

    public readonly struct ProductionFrontierResolution
    {
        public readonly double KneeQ;
        public readonly string Identity;
        public readonly bool ExperimentalOverride;

        public ProductionFrontierResolution(double kneeQ, string identity,
            bool experimentalOverride)
        {
            KneeQ = kneeQ;
            Identity = identity ?? SettlementProductionFrontier.Primitive;
            ExperimentalOverride = experimentalOverride;
        }
    }
}
