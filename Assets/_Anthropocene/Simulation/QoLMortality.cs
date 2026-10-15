using System;

namespace Anthropocene.Simulation
{
    // QoL-dependent mortality (Docs/QoLMortality.md).
    //
    // Authoritative inputs: SimulationConfig.BaseBirthRate (b0), AnnualHumanGrowthRate (g), QoLMortalityElasticity (a), the camp's committed
    // Camp.QoL from its last resolved needs, and the two per-Society/camp hook factors below. No new saved state.
    // Temporal abstraction: annual rates. A turn integrates growth analytically as exp(r T); r is the net annual growth ceiling and only
    // its value changes here. Deaths implied by d(Q) are not recorded (the existing net-rate convention); starvation deaths are unchanged.
    // Scaling: O(1) per camp per call, no scans, no allocations.
    //
    //   d(Q) = d0 * MortalityFactor * Q^-a,   d0 = b0 - g
    //   r    = max(0, b0 * FertilityFactor - d(Q))
    //
    // At Q = 1 and both factors 1, r = g exactly (the pre-existing ceiling). Growth stops at Q* = (d0/b0)^(1/a).
    public static class QoLMortality
    {
        // Q is floored here so that Q -> 0 gives a finite, enormous mortality and therefore r = 0.
        public const double MinimumQoL = 1e-6;

        // Extension points for later eras. Medicine, sanitation and vaccination multiply mortality (below 1); education, urbanisation and
        // contraception multiply fertility (below 1). Both must stay pure, deterministic and O(1) per call; they return 1 now.
        public static double MortalityFactor(WorldState w, Camp camp) => 1;
        public static double FertilityFactor(WorldState w, Camp camp) => 1;

        // Camps whose QoL the goods economy resolves. A Paleolithic camp produces no clothing or housing, so its resolved QoL is capped at
        // 1 - E (about 0.75 times food and cohesion) by construction and says nothing about wellbeing: its growth is left unchanged.
        public static bool Applies(WorldState w, Camp camp) => w.Config.DemographicModel == DemographicModel.LegacyQoLNetGrowth
            && w.Config.QoLMortalityEnabled && CultivationEconomy.EconomyEnabled(camp);

        // Annual deaths per head at QoL q for explicit factors.
        public static double DeathRate(SimulationConfig cfg, double qol, double mortalityFactor = 1)
            => (cfg.BaseBirthRate - cfg.AnnualHumanGrowthRate) * mortalityFactor * Math.Pow(Math.Max(qol, MinimumQoL), -cfg.QoLMortalityElasticity);

        // Net annual growth ceiling at QoL q for explicit factors (before DemographicGrowthMultiplier and the deficit rule).
        public static double CeilingRate(SimulationConfig cfg, double qol, double fertilityFactor = 1, double mortalityFactor = 1)
            => Math.Max(0, cfg.BaseBirthRate * fertilityFactor - DeathRate(cfg, qol, mortalityFactor));

        // The QoL at which the ceiling reaches zero (both factors 1).
        public static double ZeroGrowthQoL(SimulationConfig cfg)
            => Math.Pow((cfg.BaseBirthRate - cfg.AnnualHumanGrowthRate) / cfg.BaseBirthRate, 1 / cfg.QoLMortalityElasticity);

        // The camp's annual net growth ceiling before the deficit rule, without the DemographicGrowthMultiplier.
        public static double AnnualCeiling(WorldState w, Camp camp)
            => Applies(w, camp) ? CeilingRate(w.Config, camp.QoL, FertilityFactor(w, camp), MortalityFactor(w, camp)) : w.Config.AnnualHumanGrowthRate;

        // Reduction of the ceiling caused by QoL mortality: r(1) - r(Q), reported in the demographic explanation. Zero when it does not apply.
        public static double Reduction(WorldState w, Camp camp)
            => Applies(w, camp) ? Math.Max(0, w.Config.AnnualHumanGrowthRate - AnnualCeiling(w, camp)) : 0;

        // The one maximum growth rate every planner, forecast and resolver site uses (deficit rule and multiplier included).
        public static double MaximumRate(WorldState w, Camp camp)
            // PhysicalMortalityV1 budgets against the birth-only upper envelope. Food deficiency and every physical
            // death hazard are reconciled by PhysicalDemography; neither QoL nor the legacy recovery prohibition may
            // suppress gross births at planning time.
            => w.Config.DemographicModel == DemographicModel.PhysicalMortalityV1
                ? w.Config.BaseBirthRate * PhysicalDemography.FertilityFactor(w, camp)
                : camp.DeficitTurns > 0 ? 0 : AnnualCeiling(w, camp) * camp.DemographicGrowthMultiplier;
    }
}
