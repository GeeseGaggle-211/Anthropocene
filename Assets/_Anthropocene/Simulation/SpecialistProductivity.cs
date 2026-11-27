using System;

namespace Anthropocene.Simulation
{
    // One O(1), deterministic concentration rule for every specialist forecast
    // and authoritative receipt. It never applies to Gatherers or
    // Craftspersons, and a specialist qualifies only on the matching task.
    public static class SpecialistProductivity
    {
        public static double Multiplier(NeolithicConfig config, double residentPopulation)
        {
            double aboveOnset = Math.Max(0, residentPopulation - config.SpecialistScaleOnsetPopulation);
            if (!(aboveOnset > 0) || !(config.SpecialistScaleMaxBonus > 0)) return 1;
            return 1 + config.SpecialistScaleMaxBonus * (1 - Math.Exp(-aboveOnset / config.SpecialistScalePopulation));
        }

        public static double CraftRate(WorldState world, Camp camp, CraftRecipe recipe, string role, double residentPopulation = -1)
        {
            bool specialist = role == recipe.Specialist && camp.People.HasTech(recipe.Track);
            if (specialist)
            {
                double rate = recipe.Rate * Multiplier(world.Config.Neolithic, residentPopulation < 0 ? camp.Population : residentPopulation);
                if (role == WorkforceRoles.Builder && NeolithicProduction.IsInfrastructureGood(recipe.Good) && CivicGovernance.Enabled(world.Config))
                    rate *= CivicGovernance.PublicWorksMobilizationMultiplier(world, camp);
                return rate;
            }
            return role == WorkforceRoles.Craftsperson && !recipe.SpecialistOnly
                ? recipe.Rate * world.Config.Neolithic.GeneralistTaskEfficiency
                : 0;
        }

        public static double ExtractionRate(WorldState world, Camp camp, MaterialId material, string role, double residentPopulation = -1)
        {
            var config = world.Config.Neolithic;
            bool specialist = role == MaterialEconomy.Specialist(material) &&
                camp.People.HasTech(EconomicIntents.RequiredTechnology(role));
            if (specialist)
                return config.SpecialistMaterialExtractionRate * Multiplier(config, residentPopulation < 0 ? camp.Population : residentPopulation);
            return role == WorkforceRoles.Gatherer
                ? config.SpecialistMaterialExtractionRate * config.GeneralistTaskEfficiency
                : 0;
        }

        // Pure estimator form for a not-yet-founded daughter. The caller has
        // already proved that the inherited Society owns the matching task.
        internal static double SpecialistRate(NeolithicConfig config, double baseRate, double residentPopulation)
            => baseRate * Multiplier(config, residentPopulation);
    }
}
