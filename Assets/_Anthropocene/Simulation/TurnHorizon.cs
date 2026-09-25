using System;

namespace Anthropocene.Simulation
{
    /// <summary>
    /// The duration of one authoritative transition. ReferenceIntervals converts
    /// legacy per-BaseDays rates; it never selects a number of resolution passes.
    /// </summary>
    public readonly struct TurnHorizon
    {
        public double StartDays { get; }
        public double Days { get; }
        public double EndDays => StartDays + Days;
        public double Years { get; }
        public double ReferenceIntervals { get; }
        public double PracticeScale { get; }

        public TurnHorizon(SimulationConfig config, double startDays, double days)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (double.IsNaN(startDays) || double.IsInfinity(startDays) || startDays < 0)
                throw new ArgumentOutOfRangeException(nameof(startDays));
            if (double.IsNaN(days) || double.IsInfinity(days) || days < 0)
                throw new ArgumentOutOfRangeException(nameof(days));
            StartDays = startDays;
            Days = days;
            Years = days / config.DaysPerYear;
            ReferenceIntervals = days / config.BaseDays;
            PracticeScale = days / config.PracticeDays;
        }

        public static TurnHorizon For(WorldState world, TurnRecord record)
            => new TurnHorizon(world.Config, record.StartDays, record.Duration);

        public static TurnHorizon Next(WorldState world)
            => new TurnHorizon(world.Config, world.Days, world.Config.TurnDays);
    }
}
