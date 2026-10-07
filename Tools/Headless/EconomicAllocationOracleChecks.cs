using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropocene.Simulation;

// Diagnostic strategy experiment requested by the economic-planning audit.
// This deliberately expensive one-person grid is never called by simulation
// production code. It asks whether DemandControl's bounded three-ray walk is
// leaving value on the table under its own objective, then sends both choices
// through the ordinary turn planner and authoritative resolver for five turns.
internal static class EconomicAllocationOracleChecks
{
    private const double Epsilon = 1e-8;

    private sealed class StateCase
    {
        public string Name, Source;
        public WorldState World;
        public int CampId;
    }

    public sealed class StateSnapshot
    {
        public string Name, Source, Fingerprint, SaveSha256, ConfigHash,
            PrimaryTechnology;
        public int Turn, CampId, Population, Animals;
        public double Days, FieldCapital, FixedHousing, PlantFood, AnimalFood;
        public bool IsVillage, Cultivation, Husbandry, Fishing;
    }

    public sealed class PlannerChoice
    {
        public double Useful, PricedWork, FieldBudget, HerdBudget;
        public int Farmers, Herders, Gatherers, HerdTarget;
        public string Ray;
    }

    public sealed class RegretResult
    {
        public string State;
        public PlannerChoice Bounded, Dense;
        public int DenseEvaluations, DenseUniqueOutcomes;
        public double DenseGridStep, DenseMilliseconds,
            BoundedMilliseconds, BestUsefulAtBoundedWork,
            MinimumWorkAtBoundedUseful, UsefulRegretAtSameWork,
            ExcessWorkAtSameUseful;
        public bool BoundedDominated, PlanningPure;
    }

    public sealed class RolloutTurn
    {
        public int Turn, Population, Animals, Farmers, Herders, Gatherers,
            HerdTarget, Deaths, Growth, Slaughtered;
        public double QoL, Food, FieldCapital, GatheringFood,
            CultivationFood, AnimalProductFood, SlaughterFood,
            SelectedUseful, SelectedWork;
    }

    public sealed class RolloutResult
    {
        public string State;
        public List<RolloutTurn> Bounded = new List<RolloutTurn>();
        public List<RolloutTurn> Dense = new List<RolloutTurn>();
        public string BoundedFinalFingerprint, DenseFinalFingerprint;
        public bool FinalFingerprintEqual;
    }

    public sealed class AuditReport
    {
        public int Schema = 1;
        public string ProductionCandidateRevision, DiagnosticRevision,
            LoadedSavePath, LoadedSaveSha256, Runtime;
        public int DenseReferenceMaximumPopulation;
        public string DenseReferenceRule;
        public List<StateSnapshot> StateBank = new List<StateSnapshot>();
        public List<RegretResult> Regret = new List<RegretResult>();
        public List<RolloutResult> Rollouts = new List<RolloutResult>();
        public double BoundedPlannerMedianMilliseconds,
            BoundedPlannerP75Milliseconds;
    }

    private sealed class DenseTrace
    {
        private readonly double boundedUseful, boundedWork;
        private readonly HashSet<string> outcomes = new HashSet<string>(
            StringComparer.Ordinal);
        public int Evaluations;
        public double GridStep = 1, BestUsefulAtBoundedWork,
            MinimumWorkAtBoundedUseful = double.PositiveInfinity;
        public DemandController.DevelopmentChoice Selected;

        public DenseTrace(double boundedUseful = 0, double boundedWork = 0)
        {
            this.boundedUseful = Math.Max(0, boundedUseful);
            this.boundedWork = Math.Max(0, boundedWork);
        }

        public int UniqueOutcomes => outcomes.Count;

        private static bool Better(DemandController.DevelopmentChoice value,
            DemandController.DevelopmentChoice incumbent)
        {
            if (incumbent == null) return true;
            if (value.Useful > incumbent.Useful + Epsilon) return true;
            if (value.Useful < incumbent.Useful - Epsilon) return false;
            if (value.PricedWork < incumbent.PricedWork - Epsilon) return true;
            if (value.PricedWork > incumbent.PricedWork + Epsilon) return false;
            double valueBudget = value.FieldBudget + value.HerdBudget;
            double incumbentBudget = incumbent.FieldBudget +
                incumbent.HerdBudget;
            if (valueBudget < incumbentBudget - Epsilon) return true;
            if (valueBudget > incumbentBudget + Epsilon) return false;
            if (value.FieldBudget < incumbent.FieldBudget - Epsilon)
                return true;
            return Math.Abs(value.FieldBudget - incumbent.FieldBudget) <=
                Epsilon && value.HerdBudget < incumbent.HerdBudget - Epsilon;
        }

        private void Observe(DemandController.DevelopmentChoice candidate)
        {
            if (candidate == null || !candidate.Feasible) return;
            Evaluations++;
            outcomes.Add(candidate.FieldBudget.ToString("R",
                    CultureInfo.InvariantCulture) + ":" +
                candidate.HerdBudget.ToString("R",
                    CultureInfo.InvariantCulture) + ":" +
                candidate.Useful.ToString("R", CultureInfo.InvariantCulture) +
                ":" + candidate.PricedWork.ToString("R",
                    CultureInfo.InvariantCulture));
            if (candidate.PricedWork <= boundedWork + Epsilon)
                BestUsefulAtBoundedWork = Math.Max(
                    BestUsefulAtBoundedWork, candidate.Useful);
            if (candidate.Useful + Epsilon >= boundedUseful)
                MinimumWorkAtBoundedUseful = Math.Min(
                    MinimumWorkAtBoundedUseful, candidate.PricedWork);
            if (Better(candidate, Selected)) Selected = candidate;
        }

        public DemandController.DevelopmentChoice Search(
            DemandController.DevelopmentSearchContext context)
        {
            int residents = Math.Max(0, context.Residents);
            // State-bank populations are kept at or below this ceiling. A
            // loaded outlier remains bounded by a reported coarser diagnostic
            // step; this never changes the production selector.
            GridStep = residents <= 180 ? 1 : Math.Ceiling(residents / 180.0);
            var fieldBudgets = new SortedSet<double> { 0 };
            double maximumField = Math.Min(residents,
                context.FieldRequests.Sum(request => Math.Max(0,
                    request.Units)));
            for (double field = GridStep;
                field <= maximumField + Epsilon; field += GridStep)
                fieldBudgets.Add(Math.Min(maximumField, field));
            double prefix = 0;
            foreach (var request in context.FieldRequests)
            {
                prefix = Math.Min(residents,
                    prefix + Math.Max(0, request.Units));
                fieldBudgets.Add(prefix);
                fieldBudgets.Add(Math.Max(0, Math.Floor(prefix)));
                fieldBudgets.Add(Math.Min(residents, Math.Ceiling(prefix)));
            }

            foreach (double field in fieldBudgets)
            {
                double herdLimit = context.HerdRequests.Count > 0
                    ? Math.Max(0, residents - field) : 0;
                for (double herd = 0; herd <= herdLimit + Epsilon;
                    herd += GridStep)
                    Observe(context.Evaluate(field,
                        Math.Min(herdLimit, herd)));
                if (herdLimit > Epsilon &&
                    Math.Abs(herdLimit / GridStep -
                        Math.Round(herdLimit / GridStep)) > Epsilon)
                    Observe(context.Evaluate(field, herdLimit));
            }

            if (Selected == null) Observe(context.Evaluate(0, 0));
            return Selected;
        }
    }

    private static string Argument(string[] args, string prefix,
        string fallback = null)
    {
        var value = args.FirstOrDefault(argument => argument.StartsWith(
            prefix, StringComparison.Ordinal));
        return value == null ? fallback : value.Substring(prefix.Length);
    }

    private static string Sha256(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
    }

    private static Camp CampOf(StateCase state)
        => state.World.Camps.Single(camp => camp.Id == state.CampId);

    private static double FieldCapital(WorldState world, Camp camp)
        => world.Tiles.Where(tile => tile.CultivationOperator == camp.Id)
            .Sum(tile => Math.Max(0, tile.CultivationCapital));

    private static int Animals(WorldState world, Camp camp)
        => world.Herds.Where(herd => herd.DomesticCamp == camp.Id)
            .Sum(herd => Math.Max(0, herd.Animals));

    private static EconomicIntent Settings(WorldState world, Camp camp)
    {
        var settings = EconomicIntent.From(camp);
        if (CultivationEconomy.HusbandryEnabled(camp) &&
            Animals(world, camp) > 0)
            settings.ManagedBreedingIntensity =
                ManagedBreedingPolicy.Recommend(world, camp, camp.Tile,
                    new PlanningIndex(world));
        return settings;
    }

    private static int Role(EconomicIntent intent, string role)
        => intent?.Assignments?.Where(assignment => assignment != null &&
                assignment.Role == role)
            .Sum(assignment => assignment.Population) ?? 0;

    private static PlannerChoice Choice(EconomicIntent intent,
        DemandController.Allocation allocation)
        => new PlannerChoice
        {
            Useful = allocation.SelectedDevelopmentUseful,
            PricedWork = allocation.SelectedDevelopmentWork,
            FieldBudget = allocation.SelectedFieldBudget,
            HerdBudget = allocation.SelectedHerdBudget,
            Farmers = Role(intent, WorkforceRoles.Farmer),
            Herders = Role(intent, WorkforceRoles.Herder),
            Gatherers = allocation.FoodGatherers,
            HerdTarget = intent.HerdTargetSet ? intent.HerdTarget : -1,
            Ray = allocation.SelectedDevelopmentRay
        };

    private static RegretResult Compare(StateCase state)
    {
        var world = state.World; var camp = CampOf(state);
        string before = SaveCodec.Fingerprint(world);
        var boundedTimer = Stopwatch.StartNew();
        var boundedIntent = DemandController.Plan(world, camp,
            Settings(world, camp), out _, out var boundedAllocation);
        boundedTimer.Stop();
        var trace = new DenseTrace(
            boundedAllocation.SelectedDevelopmentUseful,
            boundedAllocation.SelectedDevelopmentWork);
        var denseTimer = Stopwatch.StartNew();
        var denseIntent = DemandController.Plan(world, camp,
            Settings(world, camp), null, out _, out var denseAllocation,
            trace.Search);
        denseTimer.Stop();
        double minimumWork = double.IsPositiveInfinity(
            trace.MinimumWorkAtBoundedUseful)
                ? boundedAllocation.SelectedDevelopmentWork
                : trace.MinimumWorkAtBoundedUseful;
        double usefulRegret = Math.Max(0,
            trace.BestUsefulAtBoundedWork -
            boundedAllocation.SelectedDevelopmentUseful);
        double excessWork = Math.Max(0,
            boundedAllocation.SelectedDevelopmentWork - minimumWork);
        return new RegretResult
        {
            State = state.Name,
            Bounded = Choice(boundedIntent, boundedAllocation),
            Dense = Choice(denseIntent, denseAllocation),
            DenseEvaluations = trace.Evaluations,
            DenseUniqueOutcomes = trace.UniqueOutcomes,
            DenseGridStep = trace.GridStep,
            DenseMilliseconds = denseTimer.Elapsed.TotalMilliseconds,
            BoundedMilliseconds = boundedTimer.Elapsed.TotalMilliseconds,
            BestUsefulAtBoundedWork = trace.BestUsefulAtBoundedWork,
            MinimumWorkAtBoundedUseful = minimumWork,
            UsefulRegretAtSameWork = usefulRegret,
            ExcessWorkAtSameUseful = excessWork,
            BoundedDominated = usefulRegret > Epsilon ||
                excessWork > Epsilon,
            PlanningPure = before == SaveCodec.Fingerprint(world)
        };
    }

    private static StateSnapshot Snapshot(StateCase state, string bankPath)
    {
        var camp = CampOf(state); byte[] bytes = SaveCodec.Save(state.World);
        if (bankPath != null && state.Source == "synthetic")
            File.WriteAllBytes(Path.Combine(bankPath, state.Name + ".save"),
                bytes);
        return new StateSnapshot
        {
            Name = state.Name, Source = state.Source,
            Fingerprint = SaveCodec.Fingerprint(state.World),
            SaveSha256 = Sha256(bytes),
            ConfigHash = SaveCodec.ConfigurationHash(state.World.Config),
            Turn = state.World.Turn, Days = state.World.Days,
            CampId = camp.Id, Population = camp.Population,
            Animals = Animals(state.World, camp),
            FieldCapital = FieldCapital(state.World, camp),
            FixedHousing = NeolithicProduction.FixedHousingAt(state.World,
                camp, state.World.Days), PlantFood = camp.PlantFood,
            AnimalFood = camp.AnimalFood, IsVillage = camp.IsVillage,
            PrimaryTechnology = camp.Technology.ToString(),
            Cultivation = CultivationEconomy.Enabled(camp),
            Husbandry = CultivationEconomy.HusbandryEnabled(camp),
            Fishing = FishingEconomy.FeatureEnabled(state.World, camp)
        };
    }

    private static IEnumerable<StateCase> SyntheticStates()
    {
        WorldState State(string name, WorldState world)
        {
            world.Config.EconomicAllocation =
                EconomicAllocationStrategy.DemandControl;
            return world;
        }
        StateCase Wrap(string name, WorldState world) => new StateCase
        {
            Name = name, Source = "synthetic", World = State(name, world),
            CampId = world.Camps.OrderBy(camp => camp.Id).First().Id
        };

        yield return Wrap("mature-fields-no-herd",
            EconomicSettlementChecks.DiagnosticVillage(0));
        yield return Wrap("small-complementary-herd",
            EconomicSettlementChecks.DiagnosticVillage(8));
        yield return Wrap("oversized-acquired-herd",
            EconomicSettlementChecks.DiagnosticVillage(400));
        var gross = EconomicSettlementChecks.DiagnosticVillage(4000);
        gross.Camps[0].PlantFood = 200000; gross.Camps[0].AnimalFood = 0;
        yield return Wrap("grossly-oversized-herd", gross);
        yield return Wrap("marginal-mobile-mixed",
            EconomicSettlementChecks.DiagnosticMobileMixed(true));
        yield return Wrap("productive-mobile-fields",
            EconomicSettlementChecks.DiagnosticMobileMixed(true, true, 0));
        yield return Wrap("productive-mixed-village",
            EconomicSettlementChecks.DiagnosticProductiveVillage());
        var stocked = EconomicSettlementChecks.DiagnosticVillage(7);
        stocked.Camps[0].PlantFood = 200000;
        stocked.Camps[0].AnimalFood = 0;
        stocked.Camps[0].Inventory.Cloth = 1000;
        yield return Wrap("saturated-reserves", stocked);
        var equipment = EconomicSettlementChecks.DiagnosticVillage(80);
        equipment.Camps[0].Inventory.HandTools = 0;
        equipment.Camps[0].Inventory.FarmTools = 0;
        equipment.Camps[0].Inventory.HerdingTools = 0;
        yield return Wrap("equipment-threshold", equipment);
    }

    private static IEnumerable<StateCase> LoadedStates(WorldState world)
    {
        if (world == null) yield break;
        var eligible = world.Camps.Where(camp => camp.Population > 0 &&
                CultivationEconomy.EconomyEnabled(world, camp))
            .OrderBy(camp => camp.Id).ToArray();
        var activity = new Dictionary<int, DemandController.Allocation>();
        foreach (var camp in eligible)
        {
            DemandController.Plan(world, camp, Settings(world, camp), out _,
                out var allocation);
            activity[camp.Id] = allocation;
        }
        var chosen = new List<Camp>();
        foreach (var group in eligible.GroupBy(camp =>
            (FieldCapital(world, camp) > Epsilon ? "field" : "no-field") +
            "/" + (Animals(world, camp) > 0 ? "herd" : "no-herd") +
            "/" + (camp.IsVillage ? "village" : "camp"))
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var candidate = group.OrderBy(camp => camp.Population > 180 ? 1 : 0)
                .ThenBy(camp => camp.Population).ThenBy(camp => camp.Id)
                .First();
            if (!chosen.Contains(candidate)) chosen.Add(candidate);
        }
        foreach (var candidate in eligible.OrderByDescending(camp =>
                    activity[camp.Id].SelectedDevelopmentUseful)
                .ThenByDescending(camp =>
                    activity[camp.Id].SelectedFieldBudget)
                .ThenBy(camp => camp.Id).Take(3))
            if (!chosen.Contains(candidate)) chosen.Add(candidate);
        foreach (var candidate in eligible.OrderByDescending(camp =>
                    activity[camp.Id].SelectedHerdBudget)
                .ThenByDescending(camp =>
                    activity[camp.Id].SelectedDevelopmentUseful)
                .ThenBy(camp => camp.Id).Take(2))
            if (!chosen.Contains(candidate)) chosen.Add(candidate);
        foreach (var candidate in eligible
            .OrderByDescending(camp => Animals(world, camp))
            .ThenBy(camp => camp.Id).Take(2))
            if (!chosen.Contains(candidate)) chosen.Add(candidate);
        foreach (var camp in chosen.Take(12))
            yield return new StateCase
            {
                Name = "loaded-camp-" + camp.Id,
                Source = "loaded", World = world, CampId = camp.Id
            };
    }

    private static RolloutTurn Advance(WorldState world, bool dense)
    {
        var camp = world.Camps.OrderBy(value => value.Id).First();
        DenseTrace trace = null;
        DemandController.Allocation boundedAllocation = null;
        PreparedTurn prepared;
        if (dense)
        {
            DemandController.DevelopmentChoice Search(
                DemandController.DevelopmentSearchContext context)
            {
                trace = new DenseTrace();
                return trace.Search(context);
            }
            prepared = TurnPlanner.PrepareWithDevelopmentSearch(world,
                new Dictionary<int, List<Order>>(), Search);
        }
        else
        {
            DemandController.Plan(world, camp, Settings(world, camp), out _,
                out boundedAllocation);
            prepared = TurnPlanner.Prepare(world,
                new Dictionary<int, List<Order>>());
        }
        var economy = prepared.Orders.FirstOrDefault(order =>
            order.Kind == OrderKind.Economy && order.Entity == camp.Id);
        int farmers = Role(economy?.Economy, WorkforceRoles.Farmer);
        int herders = Role(economy?.Economy, WorkforceRoles.Herder);
        int gatherers = Role(economy?.Economy, WorkforceRoles.Gatherer);
        int herdTarget = economy?.Economy?.HerdTargetSet == true
            ? economy.Economy.HerdTarget : -1;
        double selectedUseful = dense ? trace?.Selected?.Useful ?? 0 :
            boundedAllocation?.SelectedDevelopmentUseful ?? 0;
        double selectedWork = dense ? trace?.Selected?.PricedWork ?? 0 :
            boundedAllocation?.SelectedDevelopmentWork ?? 0;
        var record = new TurnResolver().Resolve(world, prepared);
        camp = world.Camps.Single(value => value.Id == camp.Id);
        var demographic = record.Demographics.FirstOrDefault(value =>
            value.Camp == camp.Id);
        return new RolloutTurn
        {
            Turn = record.Turn, Population = camp.Population,
            Animals = Animals(world, camp), Farmers = farmers,
            Herders = herders, Gatherers = gatherers,
            HerdTarget = herdTarget, QoL = camp.QoL, Food = camp.Food,
            FieldCapital = FieldCapital(world, camp),
            Deaths = demographic?.Deaths ?? 0,
            Growth = demographic?.Additions ?? 0,
            Slaughtered = record.ManagedHerds.Where(value =>
                value.Camp == camp.Id).Sum(value => value.Slaughtered),
            GatheringFood = record.Transfers.Where(value =>
                value.Entity == camp.Id && value.Kind == "Gathering")
                .Sum(value => value.Food),
            CultivationFood = record.Transfers.Where(value =>
                value.Entity == camp.Id && value.Kind == "Cultivation")
                .Sum(value => value.Food),
            AnimalProductFood = record.Transfers.Where(value =>
                value.Entity == camp.Id && value.Kind == "Animal Product")
                .Sum(value => value.Food),
            SlaughterFood = record.Transfers.Where(value =>
                value.Entity == camp.Id && value.Kind == "Slaughter")
                .Sum(value => value.Food),
            SelectedUseful = selectedUseful, SelectedWork = selectedWork
        };
    }

    private static RolloutResult Rollout(string name, WorldState source,
        int turns = 5)
    {
        var bounded = SaveCodec.Load(SaveCodec.Save(source));
        var dense = SaveCodec.Load(SaveCodec.Save(source));
        var result = new RolloutResult { State = name };
        for (int turn = 0; turn < turns; turn++)
        {
            result.Bounded.Add(Advance(bounded, false));
            result.Dense.Add(Advance(dense, true));
        }
        result.BoundedFinalFingerprint = SaveCodec.Fingerprint(bounded);
        result.DenseFinalFingerprint = SaveCodec.Fingerprint(dense);
        result.FinalFingerprintEqual = result.BoundedFinalFingerprint ==
            result.DenseFinalFingerprint;
        return result;
    }

    private static (double Median, double P75) Benchmark(
        IReadOnlyList<StateCase> states)
    {
        foreach (var state in states)
        {
            var camp = CampOf(state);
            DemandController.Plan(state.World, camp,
                Settings(state.World, camp), out _, out _);
        }
        var samples = new List<double>();
        foreach (var state in states)
        {
            var camp = CampOf(state);
            for (int repetition = 0; repetition < 25; repetition++)
            {
                var timer = Stopwatch.StartNew();
                DemandController.Plan(state.World, camp,
                    Settings(state.World, camp), out _, out _);
                timer.Stop(); samples.Add(timer.Elapsed.TotalMilliseconds);
            }
        }
        samples.Sort();
        double Percentile(double fraction) => samples.Count == 0 ? 0 :
            samples[(int)Math.Round((samples.Count - 1) * fraction)];
        return (Percentile(.5), Percentile(.75));
    }

    public static void Run(string[] args)
    {
        string output = Argument(args, "--oracle-output=",
            "/tmp/economic-allocation-oracle/report.json");
        string loadedPath = Argument(args, "--oracle-save=");
        string productionRevision = Argument(args,
            "--oracle-production-revision=", "unknown");
        string diagnosticRevision = Argument(args,
            "--oracle-diagnostic-revision=", "working-tree");
        string directory = Path.GetDirectoryName(output);
        if (string.IsNullOrEmpty(directory)) directory = ".";
        Directory.CreateDirectory(directory);
        string bankPath = Path.Combine(directory, "state-bank");
        Directory.CreateDirectory(bankPath);

        WorldState loaded = null; string loadedHash = null;
        if (!string.IsNullOrEmpty(loadedPath) && File.Exists(loadedPath))
        {
            byte[] bytes = File.ReadAllBytes(loadedPath);
            loadedHash = Sha256(bytes); loaded = SaveCodec.Load(bytes);
        }
        var synthetic = SyntheticStates().ToList();
        var states = synthetic.Concat(LoadedStates(loaded)).ToList();
        var report = new AuditReport
        {
            ProductionCandidateRevision = productionRevision,
            DiagnosticRevision = diagnosticRevision,
            LoadedSavePath = loadedPath,
            LoadedSaveSha256 = loadedHash,
            Runtime = System.Runtime.InteropServices.RuntimeInformation
                .FrameworkDescription,
            DenseReferenceMaximumPopulation = 180,
            DenseReferenceRule = "one-person field/herder grid; maximize the existing useful-service objective, then minimize its existing complete recurring work price"
        };
        foreach (var state in states)
        {
            report.StateBank.Add(Snapshot(state, bankPath));
            var regret = Compare(state); report.Regret.Add(regret);
            Console.WriteLine("ORACLE state=" + state.Name +
                " pop=" + CampOf(state).Population +
                " step=" + regret.DenseGridStep.ToString("F0",
                    CultureInfo.InvariantCulture) +
                " evaluations=" + regret.DenseEvaluations +
                " bounded=" + regret.Bounded.Useful.ToString("F3",
                    CultureInfo.InvariantCulture) + "/" +
                    regret.Bounded.PricedWork.ToString("F3",
                        CultureInfo.InvariantCulture) +
                " dense=" + regret.Dense.Useful.ToString("F3",
                    CultureInfo.InvariantCulture) + "/" +
                    regret.Dense.PricedWork.ToString("F3",
                        CultureInfo.InvariantCulture) +
                " sameWorkRegret=" +
                    regret.UsefulRegretAtSameWork.ToString("F3",
                        CultureInfo.InvariantCulture) +
                " workExcess=" + regret.ExcessWorkAtSameUseful.ToString(
                    "F3", CultureInfo.InvariantCulture) +
                " dominated=" + regret.BoundedDominated +
                " pure=" + regret.PlanningPure);
        }

        report.Rollouts.Add(Rollout("small-complementary-herd",
            EconomicSettlementChecks.DiagnosticVillage(8)));
        report.Rollouts.Add(Rollout("oversized-acquired-herd",
            EconomicSettlementChecks.DiagnosticVillage(400)));
        report.Rollouts.Add(Rollout("marginal-mobile-mixed",
            EconomicSettlementChecks.DiagnosticMobileMixed(true)));
        report.Rollouts.Add(Rollout("productive-mixed-village",
            EconomicSettlementChecks.DiagnosticProductiveVillage()));
        report.Rollouts.Add(Rollout("productive-mobile-fields",
            EconomicSettlementChecks.DiagnosticMobileMixed(true, true, 0)));
        var timing = Benchmark(synthetic);
        report.BoundedPlannerMedianMilliseconds = timing.Median;
        report.BoundedPlannerP75Milliseconds = timing.P75;

        var options = new JsonSerializerOptions
        {
            IncludeFields = true, WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, options));
        Console.WriteLine("ORACLE_REPORT " + Path.GetFullPath(output));
        Console.WriteLine("ORACLE_BOUNDED_TIMING medianMs=" +
            timing.Median.ToString("F6", CultureInfo.InvariantCulture) +
            " p75Ms=" + timing.P75.ToString("F6",
                CultureInfo.InvariantCulture));
    }
}
