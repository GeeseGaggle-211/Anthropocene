using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Stable semantic order for the compact institutional trace bank. This is
    // deliberately not a mirror of the 512 actor features.
    public enum ElderMemorySignal
    {
        QoL = 0,
        FoodSecurity = 1,
        WaterSecurity = 2,
        FoodReserves = 3,
        PopulationPressure = 4,
        RealizedMigration = 5,
        FieldPressure = 6,
        HerdPressure = 7,
        FishingPressure = 8,
        RealizedTradeUtility = 9,
        RealizedDevelopment = 10
    }

    [Serializable]
    public sealed class ElderInstitutionMemory
    {
        public const int StorageSchemaVersion = 1;
        public const int SignalCount = 11;
        public static readonly double[] TimeConstantsYears = { 20, 60, 180 };

        public int SchemaVersion = StorageSchemaVersion;
        public bool Initialized;
        public double InitializedDays, LastIntegratedDays;
        public double[] CurrentSignals = new double[SignalCount];
        // Signal-major: signal * horizon-count + horizon.
        public double[] ExponentialMeans =
            new double[SignalCount * 3];

        public bool HasLastDecision;
        public double LastDecisionDays, LastDecisionElapsedYears;
        public ElderDirective LastRequested, LastAccepted, LastRealized;
        public int LastInterventionCount;

        public bool HasDeficitStart, HasLastMovement, HasLastFission;
        public double DeficitStartDays, LastMovementDays, LastFissionDays;

        public bool HasFrontierState;
        public double LastCultivationKneeQ, LastFishingKneeQ;
        public string LastCultivationFrontier, LastFishingFrontier;
        public bool CultivationFrontierChangedAtLastBoundary,
            FishingFrontierChangedAtLastBoundary;
        public bool HasCultivationFrontierChange,
            HasFishingFrontierChange;
        public double LastCultivationFrontierChangeDays,
            LastFishingFrontierChangeDays;

        public void Normalize()
        {
            if (SchemaVersion == 0) SchemaVersion = StorageSchemaVersion;
            if (SchemaVersion != StorageSchemaVersion)
                throw new InvalidOperationException(
                    "Unsupported Elder institutional-memory schema.");
            if (CurrentSignals == null || CurrentSignals.Length != SignalCount)
                CurrentSignals = Resize(CurrentSignals, SignalCount);
            int values = SignalCount * TimeConstantsYears.Length;
            if (ExponentialMeans == null || ExponentialMeans.Length != values)
                ExponentialMeans = Resize(ExponentialMeans, values);
        }

        // Exact lazy solution for a signal that is constant between observed
        // physical event boundaries: m(t+dt)=a*m(t)+(1-a)*x.
        public void AdvanceTo(double days, double daysPerYear)
        {
            Normalize();
            if (!Initialized || !(days > LastIntegratedDays)) return;
            double dt = (days - LastIntegratedDays) /
                Math.Max(1e-12, daysPerYear);
            for (int signal = 0; signal < SignalCount; signal++)
                for (int horizon = 0;
                    horizon < TimeConstantsYears.Length; horizon++)
                {
                    int index = signal * TimeConstantsYears.Length + horizon;
                    double a = Math.Exp(-dt /
                        TimeConstantsYears[horizon]);
                    ExponentialMeans[index] = a * ExponentialMeans[index] +
                        (1 - a) * CurrentSignals[signal];
                }
            LastIntegratedDays = days;
        }

        // Event-separated update: integrate the old signal up to the event,
        // then install the new piecewise-constant signal.
        public void Observe(double days, double daysPerYear,
            IReadOnlyList<double> signals)
        {
            if (signals == null || signals.Count != SignalCount)
                throw new ArgumentException("The Elder trace bank requires " +
                    SignalCount + " signals.", nameof(signals));
            Normalize();
            if (!Initialized)
            {
                Initialized = true;
                InitializedDays = LastIntegratedDays = days;
                for (int signal = 0; signal < SignalCount; signal++)
                {
                    double value = Safe(signals[signal]);
                    CurrentSignals[signal] = value;
                    for (int horizon = 0;
                        horizon < TimeConstantsYears.Length; horizon++)
                        ExponentialMeans[signal *
                            TimeConstantsYears.Length + horizon] = value;
                }
                return;
            }
            if (days + 1e-8 < LastIntegratedDays)
                throw new InvalidOperationException(
                    "Institutional memory cannot move backward in physical time.");
            AdvanceTo(days, daysPerYear);
            for (int signal = 0; signal < SignalCount; signal++)
                CurrentSignals[signal] = Safe(signals[signal]);
        }

        public double Mean(ElderMemorySignal signal, int horizon)
        {
            Normalize();
            if (horizon < 0 || horizon >= TimeConstantsYears.Length)
                throw new ArgumentOutOfRangeException(nameof(horizon));
            return ExponentialMeans[(int)signal *
                TimeConstantsYears.Length + horizon];
        }

        public double Maturity(int horizon, double nowDays,
            double daysPerYear)
        {
            if (!Initialized) return 0;
            double years = Math.Max(0, nowDays - InitializedDays) /
                Math.Max(1e-12, daysPerYear);
            return 1 - Math.Exp(-years / TimeConstantsYears[horizon]);
        }

        private static double Safe(double value)
            => double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;

        private static double[] Resize(double[] source, int length)
        {
            var result = new double[length];
            if (source != null)
                Array.Copy(source, result, Math.Min(source.Length, length));
            return result;
        }
    }

    // Native update substrate. Only settlements that have opted into Elder
    // memory are visited at a turn boundary, so cost is O(active Elder camps *
    // local footprint) and never O(World.History).
    public static class ElderInstitutionMemorySystem
    {
        public static ElderInstitutionMemory EnsureInitialized(
            WorldState world, Camp camp)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (camp == null) throw new ArgumentNullException(nameof(camp));
            var memory = camp.ElderMemory ??=
                new ElderInstitutionMemory();
            memory.Normalize();
            if (!memory.Initialized)
            {
                UpdateEventTimes(world, camp, null, memory);
                UpdateFrontiers(world, camp, memory, false);
                memory.Observe(world.Days, world.Config.DaysPerYear,
                    CaptureSignals(world, camp,
                        world.History.LastOrDefault(), memory));
            }
            return memory;
        }

        internal static ElderInstitutionMemory TransientView(
            WorldState world, Camp camp, TurnRecord recent)
        {
            var memory = new ElderInstitutionMemory();
            UpdateEventTimes(world, camp, recent, memory);
            UpdateFrontiers(world, camp, memory, false);
            memory.Observe(world.Days, world.Config.DaysPerYear,
                CaptureSignals(world, camp, recent, memory));
            return memory;
        }

        public static void RecordDecision(WorldState world,
            ElderCompiledDecision decision, double decisionDays)
        {
            if (world == null || decision == null) return;
            var camp = world.Camps.FirstOrDefault(item => item.Id ==
                decision.ControlledCampId);
            if (camp?.ElderMemory == null) return;
            var memory = camp.ElderMemory;
            memory.HasLastDecision = decision.Requested != null &&
                decision.Accepted != null;
            memory.LastDecisionDays = decisionDays;
            memory.LastDecisionElapsedYears = 0;
            memory.LastRequested = decision.Requested?.Copy();
            memory.LastAccepted = decision.Accepted?.Copy();
            memory.LastRealized = decision.Realized?.Copy();
            memory.LastInterventionCount =
                decision.Interventions?.Length ?? 0;
        }

        public static void ObserveBoundary(WorldState world, Camp camp,
            TurnRecord record)
        {
            if (world == null || camp?.ElderMemory == null) return;
            var memory = camp.ElderMemory;
            memory.Normalize();
            if (memory.HasLastDecision)
                memory.LastDecisionElapsedYears = Math.Max(0,
                    world.Days - memory.LastDecisionDays) /
                    Math.Max(1e-12, world.Config.DaysPerYear);
            UpdateEventTimes(world, camp, record, memory);
            UpdateFrontiers(world, camp, memory, true);
            memory.Observe(world.Days, world.Config.DaysPerYear,
                CaptureSignals(world, camp, record, memory));
        }

        internal static double[] CaptureSignals(WorldState world, Camp camp,
            TurnRecord record, ElderInstitutionMemory memory)
        {
            double years = Math.Max(0, record?.Duration ?? 0) /
                Math.Max(1e-12, world.Config.DaysPerYear);
            int priorPopulation = record?.Demographics?.FirstOrDefault(
                report => report.Camp == camp.Id)?.StartingPopulation ??
                Math.Max(1, camp.Population);
            double annualNeed = Math.Max(1e-12,
                Math.Max(1, camp.Population) * world.Config.Consumption *
                world.Config.DaysPerYear / world.Config.BaseDays);
            double migration = 0;
            foreach (var flow in record?.Migrations ??
                Enumerable.Empty<MigrationReport>())
            {
                if (flow.DestinationCamp == camp.Id) migration += flow.Migrants;
                if (flow.SourceCamp == camp.Id) migration -= flow.Migrants;
            }
            double tradeUtility = 0;
            foreach (var trade in record?.Trades ??
                Enumerable.Empty<TradeRecord>())
            {
                if (trade.Requester == camp.Id)
                    tradeUtility += trade.RequesterRealizedUtility;
                else if (trade.Responder == camp.Id)
                    tradeUtility += trade.ResponderRealizedUtility;
            }
            double fieldPressure = CultivationEconomy.Enabled(camp)
                ? CultivationEconomy.MaintenanceWorkerRequirement(world, camp) /
                    Math.Max(1, camp.Population) : 0;
            var herds = world.Herds.Where(herd =>
                herd.DomesticCamp == camp.Id && herd.Animals > 0).ToArray();
            double animals = herds.Sum(herd => (double)herd.Animals);
            double herdPressure = animals > 0 ? herds.Sum(herd =>
                Math.Max(0, 1 - ElderRLContract.Unit(
                    herd.ManagementFulfillment)) * herd.Animals) / animals : 0;
            double fishingYears = years > 0 ? years : Math.Max(0,
                world.Config.TurnDays / world.Config.DaysPerYear);
            double fishingPressure = FishingMaintenanceWorkerYears(world,
                camp, fishingYears) /
                Math.Max(1e-12, Math.Max(1, camp.Population) *
                    Math.Max(1e-12, fishingYears));
            return new[]
            {
                ElderRLContract.Unit(camp.QoL),
                ElderRLContract.Unit(camp.FoodNeedsMetRatio),
                ElderRLContract.Unit(camp.WaterNeedsMetRatio),
                Math.Max(0, camp.Food) / annualNeed,
                Math.Max(0, world.LinkedPopulation(camp) - camp.Population) /
                    (double)Math.Max(1, camp.Population),
                years > 0 ? migration / Math.Max(1, priorPopulation) / years : 0,
                fieldPressure,
                herdPressure,
                fishingPressure,
                years > 0 ? tradeUtility /
                    Math.Max(1, priorPopulation) / years : 0,
                memory?.LastRealized?.DevelopmentBudget ?? 0
            };
        }

        public static double FishingMaintenanceWorkerYears(WorldState world,
            Camp camp, double years)
        {
            if (world?.FisherySites == null || camp == null || years <= 0)
                return 0;
            double total = 0;
            foreach (int tile in (camp.LastSelectedFishingGrounds ??
                new List<int>()).Distinct())
            {
                var site = world.FisherySites.FirstOrDefault(item =>
                    item.TileId == tile && item.OperatorCampId == camp.Id);
                if (site == null) continue;
                double capital = FishingEconomy.ProjectedCapital(world, site,
                    world.Days);
                total += world.Config.Neolithic.MaintenanceWorkersPerTileE(
                        world.Config.MaintenanceCoefficient) *
                    FishingEconomy.EffortForCapital(world, camp, capital) *
                    years;
            }
            return total;
        }

        private static void UpdateEventTimes(WorldState world, Camp camp,
            TurnRecord record, ElderInstitutionMemory memory)
        {
            if (camp.DeficitTurns > 0)
            {
                if (!memory.HasDeficitStart)
                {
                    memory.HasDeficitStart = true;
                    // The native economy resolves deficit state over a horizon;
                    // initialization can only date first institutional notice.
                    memory.DeficitStartDays = world.Days;
                }
            }
            else memory.HasDeficitStart = false;

            if (camp.Moves > 0)
            {
                double moved = Math.Max(0, world.Days -
                    Math.Max(0, camp.StationaryYears) *
                    world.Config.DaysPerYear);
                if (!memory.HasLastMovement ||
                    moved > memory.LastMovementDays + 1e-8)
                {
                    memory.HasLastMovement = true;
                    memory.LastMovementDays = moved;
                }
            }
            if ((record?.Fissions ?? Enumerable.Empty<FissionReport>()).Any(
                fission => fission.ParentCamp == camp.Id))
            {
                memory.HasLastFission = true;
                memory.LastFissionDays = record.StartDays + record.Duration;
            }
        }

        private static void UpdateFrontiers(WorldState world, Camp camp,
            ElderInstitutionMemory memory, bool detectChange)
        {
            var cultivation = SettlementProductionFrontier
                .ResolveCultivation(world, camp);
            var fishing = SettlementProductionFrontier
                .ResolveFishing(world, camp);
            memory.CultivationFrontierChangedAtLastBoundary = false;
            memory.FishingFrontierChangedAtLastBoundary = false;
            if (detectChange && memory.HasFrontierState)
            {
                if (Math.Abs(cultivation.KneeQ -
                        memory.LastCultivationKneeQ) > 1e-12 ||
                    !string.Equals(cultivation.Identity,
                        memory.LastCultivationFrontier,
                        StringComparison.Ordinal))
                {
                    memory.CultivationFrontierChangedAtLastBoundary = true;
                    memory.HasCultivationFrontierChange = true;
                    memory.LastCultivationFrontierChangeDays = world.Days;
                }
                if (Math.Abs(fishing.KneeQ - memory.LastFishingKneeQ) >
                        1e-12 || !string.Equals(fishing.Identity,
                            memory.LastFishingFrontier,
                            StringComparison.Ordinal))
                {
                    memory.FishingFrontierChangedAtLastBoundary = true;
                    memory.HasFishingFrontierChange = true;
                    memory.LastFishingFrontierChangeDays = world.Days;
                }
            }
            memory.HasFrontierState = true;
            memory.LastCultivationKneeQ = cultivation.KneeQ;
            memory.LastFishingKneeQ = fishing.KneeQ;
            memory.LastCultivationFrontier = cultivation.Identity;
            memory.LastFishingFrontier = fishing.Identity;
        }
    }
}
