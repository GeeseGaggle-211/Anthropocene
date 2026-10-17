using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Anthropocene.Simulation;

// Opt-in native GO-gate audit. It uses the real generated world, native heuristic, economy, ecology,
// migration and fission for three independent seeds and thirty post-opening decisions per seed.
internal static class DemographyLiveAudit
{
    private sealed class RecoveryState
    {
        public bool Distressed;
        public int HealthyTurns;
    }

    private sealed class Totals
    {
        public int Seeds, Turns, OpeningTurns, ContinuationTurns, StartingCamps, FinalCamps, Survivors;
        public int Births, Deaths, Migrations, Migrants, Fissions, SplitPopulation, HigherQolWorseMortality;
        public int NegativeNetDaughters, NegativeNetDestinations;
        public int FissionNegativeNetProposals, ImmigrationNegativeNetGroups;
        public int Recoveries, RapidPairReversals, ViableTerminal, TargetTerminal, Q04Terminal;
        public int Cultivation, Husbandry, Watercraft, Mixed;
        public long CampTurns, PopulationTurns, FinalPopulation;
        public double Exposure, Crowding, Disease, Starvation, WorstConservation, TurnMilliseconds;
    }

    private sealed class SocialControl
    {
        public int Camps, Survivors, Population, Fissions, Migrations, Migrants, RapidPairReversals;
        public long CampTurns, PopulationTurns;
        public double Milliseconds;
    }

    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("Demography live audit: " + message);
    }

    public static int Run(int continuations = 30)
    {
        if (continuations < 25 || continuations > 50)
            throw new ArgumentOutOfRangeException(nameof(continuations), "The v1.1 native gate requires 25-50 decision continuations.");
        assertions = 0;
        totalHigherQolWorseMortality = totalSplitPopulation = 0; totalWorstConservation = 0;
        var total = new Totals();
        foreach (int seed in new[] { 17, 53, 991 }) RunSeed(seed, continuations, total);
        SocialControl legacy = RunLegacySocialControls(continuations);
        total.HigherQolWorseMortality = totalHigherQolWorseMortality;
        total.SplitPopulation = totalSplitPopulation;
        total.WorstConservation = totalWorstConservation;
        bool routeCoverage = total.Cultivation > 0 && total.Husbandry > 0 && total.Watercraft > 0 && total.Mixed > 0;
        string socialGate = total.RapidPairReversals == 0 ? "PASS" : "REVIEW_REVERSAL";
        Console.WriteLine("DEMOGRAPHY_LIVE_TOTAL seeds=" + total.Seeds + " turns=" + total.Turns
            + " openingTurns=" + total.OpeningTurns + " continuationTurns=" + total.ContinuationTurns
            + " camps=" + total.StartingCamps + "->" + total.FinalCamps + " survivors=" + total.Survivors
            + " births=" + total.Births + " deaths=" + total.Deaths
            + " exposure=" + total.Exposure.ToString("F3") + " crowding=" + total.Crowding.ToString("F3")
            + " disease=" + total.Disease.ToString("F3") + " starvation=" + total.Starvation.ToString("F3")
            + " migrations=" + total.Migrations + " migrants=" + total.Migrants
            + " fissions=" + total.Fissions + " splitPopulation=" + total.SplitPopulation
            + " committedNegativeNetDaughters=" + total.NegativeNetDaughters
            + " committedNegativeNetDestinations=" + total.NegativeNetDestinations
            + " fissionNegativeNetProposals=" + total.FissionNegativeNetProposals
            + " immigrationNegativeNetGroups=" + total.ImmigrationNegativeNetGroups
            + " higherQolWorseMortality=" + total.HigherQolWorseMortality
            + " rapidPairReversals=" + total.RapidPairReversals + " recoveries=" + total.Recoveries
            + " viableTerminal=" + total.ViableTerminal + " targetTerminal=" + total.TargetTerminal
            + " q04Terminal=" + total.Q04Terminal + " routes=" + total.Cultivation + "/" + total.Husbandry
            + "/" + total.Watercraft + " mixed=" + total.Mixed
            + " finalPopulation=" + total.FinalPopulation
            + " fissionsPer1000CampTurns=" + (1000.0 * total.Fissions / Math.Max(1, total.CampTurns)).ToString("F3")
            + " migrantsPer1000PersonTurns=" + (1000.0 * total.Migrants / Math.Max(1, total.PopulationTurns)).ToString("F3")
            + " worstConservation=" + total.WorstConservation.ToString("G6")
            + " turnMs=" + total.TurnMilliseconds.ToString("F1"));
        Console.WriteLine("DEMOGRAPHY_LIVE_GATE nativeConservation=PASS replay=PASS"
            + " routeCoverage=" + (routeCoverage ? "PASS" : "INSUFFICIENT")
            + " economicViability=" + (total.ViableTerminal > 0 ? "OBSERVED" : "NOT_OBSERVED")
            + " migrationFissionPathology=" + socialGate
            + " learnedComparison=UNAVAILABLE training=NO_GO");
        Console.WriteLine("DEMOGRAPHY_SOCIAL_CONTROL model=legacy-qol seeds=3 camps=24->" + legacy.Camps
            + " survivors=" + legacy.Survivors + " population=" + legacy.Population
            + " migrations=" + legacy.Migrations + " migrants=" + legacy.Migrants
            + " fissions=" + legacy.Fissions + " rapidPairReversals=" + legacy.RapidPairReversals
            + " elapsedMs=" + legacy.Milliseconds.ToString("F1")
            + " fissionsPer1000CampTurns=" + (1000.0 * legacy.Fissions / Math.Max(1, legacy.CampTurns)).ToString("F3")
            + " migrantsPer1000PersonTurns=" + (1000.0 * legacy.Migrants / Math.Max(1, legacy.PopulationTurns)).ToString("F3")
            + " physicalToLegacyFissionRatio=" + (total.Fissions / (double)Math.Max(1, legacy.Fissions)).ToString("F3")
            + " physicalToLegacyMigrantRatio=" + (total.Migrants / (double)Math.Max(1, legacy.Migrants)).ToString("F3"));
        Check(total.Seeds == 3 && total.ContinuationTurns == 3 * continuations,
            "all three seeds complete the requested continuation horizon");
        Check(total.WorstConservation <= 1e-7, "continuous cause accounting remains conserved");
        Check(total.Survivors > 0, "the native matrix retains at least one living lineage");
        Console.WriteLine("PASS: " + assertions + " native demographic live-audit assertions");
        return assertions;
    }

    private static SocialControl RunLegacySocialControls(int continuations)
    {
        var total = new SocialControl(); var timer = Stopwatch.StartNew();
        foreach (int seed in new[] { 17, 53, 991 })
        {
            var cfg = SimulationConfig.Small(8);
            cfg.DemographicModel = DemographicModel.LegacyQoLNetGrowth;
            cfg.QoLMortalityEnabled = true;
            cfg.FissionEnabled = true; cfg.ImmigrationEnabled = true; cfg.TradeEnabled = true;
            cfg.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
            cfg.Validate();
            var world = new WorldGenerator().Generate(seed, cfg); var resolver = new TurnResolver();
            int opening = 0; var last = new Dictionary<(int Source, int Destination), int>();
            while (!world.Finished && opening++ < 30)
                ResolveLegacyControl(world, resolver, last, total);
            Check(world.Finished, "legacy social control seed " + seed + " completes its opening");
            for (int i = 0; i < continuations; i++) ResolveLegacyControl(world, resolver, last, total);
            total.Camps += world.Camps.Count;
            total.Survivors += world.Camps.Count(c => world.LinkedPopulation(c) > 0);
            total.Population += world.Camps.Sum(world.LinkedPopulation);
        }
        timer.Stop(); total.Milliseconds = timer.Elapsed.TotalMilliseconds; return total;
    }

    private static void ResolveLegacyControl(WorldState world, TurnResolver resolver,
        Dictionary<(int Source, int Destination), int> last, SocialControl total)
    {
        int before = world.Camps.Sum(world.LinkedPopulation);
        PlanningResult plan = AiPlanning.Decide(world);
        TurnRecord record = resolver.Resolve(world, plan.Orders, plan.Traces);
        Check(world.Camps.Sum(world.LinkedPopulation) == before + record.Growth - record.Deaths,
            "legacy matched social control conserves global population at turn " + record.Turn);
        foreach (MigrationReport migration in record.Migrations)
        {
            var reverse = (migration.DestinationCamp, migration.SourceCamp);
            if (last.TryGetValue(reverse, out int turn)
                && record.Turn - turn <= world.Config.ImmigrationReturnCooldownTurns)
                total.RapidPairReversals++;
            last[(migration.SourceCamp, migration.DestinationCamp)] = record.Turn;
        }
        total.Migrations += record.Migrations.Count; total.Migrants += record.Migrations.Sum(m => m.Migrants);
        total.Fissions += record.Fissions.Count;
        total.CampTurns += world.Camps.Count; total.PopulationTurns += world.Camps.Sum(world.LinkedPopulation);
    }

    private static void RunSeed(int seed, int continuations, Totals total)
    {
        var cfg = SimulationConfig.Small(8);
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1;
        cfg.QoLMortalityEnabled = false;
        cfg.FissionEnabled = true; cfg.ImmigrationEnabled = true; cfg.TradeEnabled = true;
        cfg.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        cfg.Validate();
        WorldState world = new WorldGenerator().Generate(seed, cfg);
        total.Seeds++; total.StartingCamps += world.Camps.Count;
        var resolver = new TurnResolver();
        var recoveries = new Dictionary<int, RecoveryState>();
        var lastDirectedMigration = new Dictionary<(int Source, int Destination), int>();
        var daughters = new HashSet<int>();
        int seedBirths = 0, seedDeaths = 0, seedMigrations = 0, seedMigrants = 0, seedFissions = 0;
        int seedNegativeNetDaughters = 0, seedNegativeNetDestinations = 0;
        int seedFissionNegativeNet = 0, seedImmigrationNegativeNet = 0;
        double seedExposure = 0, seedCrowding = 0, seedDisease = 0, seedStarvation = 0;
        int opening = 0, continuation = 0, rapidReversals = 0;
        long seedCampTurns = 0, seedPopulationTurns = 0;
        var clock = Stopwatch.StartNew();

        while (!world.Finished && opening < 30)
        {
            ResolveAndAudit(world, resolver, seed, recoveries, lastDirectedMigration, daughters,
                ref seedBirths, ref seedDeaths, ref seedMigrations, ref seedMigrants, ref seedFissions,
                ref seedExposure, ref seedCrowding, ref seedDisease, ref seedStarvation, ref rapidReversals,
                ref seedNegativeNetDaughters, ref seedNegativeNetDestinations,
                ref seedFissionNegativeNet, ref seedImmigrationNegativeNet);
            opening++;
            seedCampTurns += world.Camps.Count; seedPopulationTurns += world.Camps.Sum(world.LinkedPopulation);
        }
        Check(world.Finished, "seed " + seed + " completes its opening within thirty turns");

        // Save/load at the promotion boundary, then prove a matched native continuation remains exact.
        WorldState replay = SaveCodec.Load(SaveCodec.Save(world));
        for (int i = 0; i < continuations; i++)
        {
            PlanningResult plan = AiPlanning.Decide(world);
            PlanningResult replayPlan = AiPlanning.Decide(replay);
            Check(PlansEqual(plan, replayPlan), "seed " + seed + " has identical native decisions after save/load at continuation " + i);
            ResolveAndAudit(world, resolver, seed, recoveries, lastDirectedMigration, daughters,
                ref seedBirths, ref seedDeaths, ref seedMigrations, ref seedMigrants, ref seedFissions,
                ref seedExposure, ref seedCrowding, ref seedDisease, ref seedStarvation, ref rapidReversals,
                ref seedNegativeNetDaughters, ref seedNegativeNetDestinations,
                ref seedFissionNegativeNet, ref seedImmigrationNegativeNet, plan);
            new TurnResolver().Resolve(replay, replayPlan.Orders, replayPlan.Traces);
            Check(SaveCodec.Fingerprint(world) == SaveCodec.Fingerprint(replay),
                "seed " + seed + " save continuation fingerprint at decision " + i);
            continuation++;
            seedCampTurns += world.Camps.Count; seedPopulationTurns += world.Camps.Sum(world.LinkedPopulation);
        }
        clock.Stop();

        Camp[] living = world.Camps.Where(c => world.LinkedPopulation(c) > 0).OrderBy(c => c.Id).ToArray();
        int viable = living.Count(c => c.FoodNeedsMetRatio >= .8 && c.ClothPerCapita >= .8 && c.HousingPerCapita >= .8);
        int target = living.Count(c => world.LinkedPopulation(c) >= 600 && world.LinkedPopulation(c) <= 1000
            && c.FoodNeedsMetRatio >= .8 && c.ClothPerCapita >= .8 && c.HousingPerCapita >= .8);
        int q04 = living.Count(c =>
        {
            double[] q = CultivationEconomy.Footprint(world, c).Where(id => world.Tiles[id].Land)
                .Select(id => CultivationEconomy.Intensity(world.Config, world.Tiles[id].CultivationCapital)).ToArray();
            return q.Length > 0 && q.Average() >= .32 && q.Average() <= .48;
        });
        int cultivation = living.Count(c => CultivationEconomy.Enabled(c));
        int husbandry = living.Count(c => CultivationEconomy.HusbandryEnabled(c));
        int watercraft = living.Count(c => c.Technologies[(int)Technology.Watercraft]);
        int mixed = living.Count(c => new[]
        {
            CultivationEconomy.Enabled(c), CultivationEconomy.HusbandryEnabled(c), c.Technologies[(int)Technology.Watercraft]
        }.Count(x => x) >= 2);
        int recovered = recoveries.Values.Count(r => !r.Distressed && r.HealthyTurns >= 3);
        int[] populations = living.Select(world.LinkedPopulation).OrderBy(n => n).ToArray();
        double meanNutrition = living.Length == 0 ? 0 : living.Average(c => c.FoodNeedsMetRatio);
        double meanClothing = living.Length == 0 ? 0 : living.Average(c => c.ClothPerCapita);
        double meanHousing = living.Length == 0 ? 0 : living.Average(c => c.HousingPerCapita);
        Console.WriteLine("DEMOGRAPHY_LIVE seed=" + seed + " opening=" + opening + " continuations=" + continuation
            + " camps=" + cfg.Players + "->" + world.Camps.Count + " survivors=" + living.Length
            + " populationMin=" + (populations.Length == 0 ? 0 : populations[0])
            + " populationMedian=" + Percentile(populations, .5)
            + " populationMax=" + (populations.Length == 0 ? 0 : populations[^1])
            + " births=" + seedBirths + " deaths=" + seedDeaths
            + " exposure=" + seedExposure.ToString("F3") + " crowding=" + seedCrowding.ToString("F3")
            + " disease=" + seedDisease.ToString("F3") + " starvation=" + seedStarvation.ToString("F3")
            + " nutrition=" + meanNutrition.ToString("F3") + " clothing=" + meanClothing.ToString("F3")
            + " housing=" + meanHousing.ToString("F3") + " viable=" + viable + " target=" + target
            + " q04=" + q04 + " recoveries=" + recovered + " migrations=" + seedMigrations
            + " migrants=" + seedMigrants + " fissions=" + seedFissions
            + " negativeNetDaughters=" + seedNegativeNetDaughters
            + " negativeNetDestinations=" + seedNegativeNetDestinations
            + " fissionNegativeNetProposals=" + seedFissionNegativeNet
            + " immigrationNegativeNetGroups=" + seedImmigrationNegativeNet
            + " rapidReversals=" + rapidReversals
            + " routes=" + cultivation + "/" + husbandry + "/" + watercraft + " mixed=" + mixed
            + " elapsedMs=" + clock.Elapsed.TotalMilliseconds.ToString("F1"));

        Check(living.All(c => c.Workforce.Assigned <= c.Population), "seed " + seed + " terminal workforce is bounded by residents");
        Check(world.Camps.Select(c => c.Id).Distinct().Count() == world.Camps.Count,
            "seed " + seed + " has unique settlement ownership after social transfers");
        total.Turns += opening + continuation; total.OpeningTurns += opening; total.ContinuationTurns += continuation;
        total.FinalCamps += world.Camps.Count; total.Survivors += living.Length;
        total.Births += seedBirths; total.Deaths += seedDeaths; total.Migrations += seedMigrations;
        total.Migrants += seedMigrants; total.Fissions += seedFissions; total.RapidPairReversals += rapidReversals;
        total.NegativeNetDaughters += seedNegativeNetDaughters;
        total.NegativeNetDestinations += seedNegativeNetDestinations;
        total.FissionNegativeNetProposals += seedFissionNegativeNet;
        total.ImmigrationNegativeNetGroups += seedImmigrationNegativeNet;
        total.Exposure += seedExposure; total.Crowding += seedCrowding; total.Disease += seedDisease;
        total.Starvation += seedStarvation; total.Recoveries += recovered; total.ViableTerminal += viable;
        total.TargetTerminal += target; total.Q04Terminal += q04; total.Cultivation += cultivation;
        total.Husbandry += husbandry; total.Watercraft += watercraft; total.Mixed += mixed;
        total.CampTurns += seedCampTurns; total.PopulationTurns += seedPopulationTurns;
        total.FinalPopulation += world.Camps.Sum(world.LinkedPopulation);
        total.TurnMilliseconds += clock.Elapsed.TotalMilliseconds;
    }

    private static void ResolveAndAudit(WorldState world, TurnResolver resolver, int seed,
        Dictionary<int, RecoveryState> recoveries,
        Dictionary<(int Source, int Destination), int> lastDirectedMigration, HashSet<int> daughters,
        ref int births, ref int deaths, ref int migrations, ref int migrants, ref int fissions,
        ref double exposure, ref double crowding, ref double disease, ref double starvation,
        ref int rapidReversals, ref int negativeNetDaughters, ref int negativeNetDestinations,
        ref int fissionNegativeNet, ref int immigrationNegativeNet,
        PlanningResult plan = null)
    {
        int before = world.Camps.Sum(world.LinkedPopulation);
        plan ??= AiPlanning.Decide(world);
        TurnRecord record = resolver.Resolve(world, plan.Orders, plan.Traces);
        fissionNegativeNet += (int)resolver.LastMetrics.FissionPhysicalDaughterNegativeNet;
        immigrationNegativeNet += (int)resolver.LastMetrics.ImmigrationPhysicalDestinationNegativeNet;
        int after = world.Camps.Sum(world.LinkedPopulation);
        Check(after == before + record.Growth - record.Deaths,
            "seed " + seed + " turn " + record.Turn + " global population conservation");
        Check(record.Demographics.Sum(d => d.GrossBirths) == record.Growth
            && record.Demographics.Sum(d => d.GrossDeaths) == record.Deaths,
            "seed " + seed + " turn " + record.Turn + " gross ledger totals");
        foreach (DemographicReport d in record.Demographics)
        {
            double causes = d.ExposureDeaths + d.CrowdingDeaths + d.DiseaseDeaths + d.StarvationDeaths + d.AdditionalDeaths;
            double error = Math.Abs(d.ContinuousDeaths - causes);
            Check(error <= 1e-7 * Math.Max(1, d.ContinuousDeaths),
                "seed " + seed + " turn " + record.Turn + " cause attribution for camp " + d.Camp);
            Check(d.CauseCorrection <= world.Config.DemographicCauseCorrectionTolerance + 1e-12,
                "seed " + seed + " turn " + record.Turn + " cause correction bound");
            Camp camp = world.Camps.FirstOrDefault(c => c.Id == d.Camp);
            if (camp != null)
            {
                Check(camp.Workforce.Assigned <= camp.Population,
                    "seed " + seed + " turn " + record.Turn + " workforce bound for camp " + camp.Id);
                Check(FiniteNonnegative(camp.Food) && FiniteNonnegative(camp.Inventory.Cloth)
                    && FiniteNonnegative(camp.Inventory.AnimalFabric) && FiniteNonnegative(camp.Inventory.LightHousingUnits),
                    "seed " + seed + " turn " + record.Turn + " finite nonnegative physical stocks");
                bool healthy = camp.FoodNeedsMetRatio >= .8 && camp.ClothPerCapita >= .8 && camp.HousingPerCapita >= .8;
                if (!recoveries.TryGetValue(camp.Id, out RecoveryState state)) recoveries[camp.Id] = state = new RecoveryState();
                if (!healthy) { state.Distressed = true; state.HealthyTurns = 0; }
                else if (state.Distressed)
                {
                    state.HealthyTurns++;
                    if (state.HealthyTurns >= 3) state.Distressed = false;
                }
            }
            exposure += d.ExposureDeaths; crowding += d.CrowdingDeaths;
            disease += d.DiseaseDeaths; starvation += d.StarvationDeaths;
        }
        foreach (MigrationReport migration in record.Migrations)
        {
            var reverse = (migration.DestinationCamp, migration.SourceCamp);
            if (lastDirectedMigration.TryGetValue(reverse, out int turn)
                && record.Turn - turn <= world.Config.ImmigrationReturnCooldownTurns)
                rapidReversals++;
            lastDirectedMigration[(migration.SourceCamp, migration.DestinationCamp)] = record.Turn;
            if (migration.HigherQoLWorseMortality) totalHigherQolWorseMortality++;
            if (!migration.DestinationNetNonnegative) negativeNetDestinations++;
        }
        foreach (FissionReport split in record.Fissions)
        {
            daughters.Add(split.DaughterCamp);
            if (!split.DaughterNetNonnegative) negativeNetDaughters++;
        }
        births += record.Growth; deaths += record.Deaths;
        migrations += record.Migrations.Count; migrants += record.Migrations.Sum(m => m.Migrants);
        fissions += record.Fissions.Count;
        totalSplitPopulation += record.Fissions.Sum(f => f.Daughter);
        totalWorstConservation = Math.Max(totalWorstConservation,
            record.Demographics.Count == 0 ? 0 : record.Demographics.Max(d => Math.Abs(d.ConservationError)));
    }

    // Turn-local accumulators are folded into Totals after each seed without exposing mutable global simulator state.
    private static int totalHigherQolWorseMortality, totalSplitPopulation;
    private static double totalWorstConservation;

    private static bool PlansEqual(PlanningResult a, PlanningResult b)
    {
        string[] left = a.Orders.OrderBy(p => p.Key).SelectMany(p => p.Value.Select(o => p.Key + ":" + o)).ToArray();
        string[] right = b.Orders.OrderBy(p => p.Key).SelectMany(p => p.Value.Select(o => p.Key + ":" + o)).ToArray();
        return left.SequenceEqual(right);
    }

    private static bool FiniteNonnegative(double value) => value >= -1e-9 && !double.IsNaN(value) && !double.IsInfinity(value);
    private static int Percentile(IReadOnlyList<int> sorted, double fraction)
        => sorted.Count == 0 ? 0 : sorted[(int)Math.Round((sorted.Count - 1) * fraction)];
}
