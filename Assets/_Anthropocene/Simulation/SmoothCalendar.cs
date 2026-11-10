using System;
using System.Runtime.Serialization;

namespace Anthropocene.Simulation
{
    // Save identity: a null SimulationConfig.SmoothCalendar retains fixed TurnDays.
    public enum SmoothCalendarCurve { EarlyExponential = 1, HistoricalExponential = 2 }

    [Serializable]
    public sealed class SmoothCalendarSettings
    {
        public int Version = 1;
        public SmoothCalendarCurve Curve = SmoothCalendarCurve.HistoricalExponential;
        // Presentation/calibration epoch, not a technology unlock or real-world date claim.
        public double NeolithicStartYear = -5000;
        public bool Started;
        public double EpochDays;
        [OnDeserializing]
        private void Defaults(StreamingContext context)
        { Version = 1; Curve = SmoothCalendarCurve.HistoricalExponential; NeolithicStartYear = -5000; }
        public void Validate(SimulationConfig config)
        {
            if (Version != 1 || (Curve != SmoothCalendarCurve.EarlyExponential && Curve != SmoothCalendarCurve.HistoricalExponential))
                throw new ArgumentException("Unsupported smooth-calendar version/curve.");
            if (!SmoothCalendar.Finite(EpochDays) || EpochDays < 0 || !SmoothCalendar.Finite(NeolithicStartYear))
                throw new ArgumentException("Invalid smooth-calendar epoch.");
            if (config.DaysPerYear != 365 || config.MinDays > 1 || config.MaxDays < 20 * config.DaysPerYear)
                throw new ArgumentException("Smooth calendar v1 requires 365 days/year and bounds covering 1 day to 20 years.");
            if (NeolithicStartYear >= 1750 || NeolithicStartYear < -50000)
                throw new ArgumentException("Neolithic calendar epoch must precede the industrial join.");
            if (Curve == SmoothCalendarCurve.HistoricalExponential)
            {
                double first = Math.Log(20 * config.DaysPerYear) - SmoothCalendar.EarlyRate * (1750 - NeolithicStartYear);
                double last = Math.Log(100.0);
                double secant = (first - last) / 50.0;
                // Positive normalized tangents in this radius-three disk give a monotone cubic.
                double alpha = SmoothCalendar.EarlyRate / secant, beta = SmoothCalendar.ModernRate / secant;
                if (!(secant > 0) || alpha * alpha + beta * beta > 9)
                    throw new ArgumentException("Calendar epoch does not admit the monotone industrial join; recalibrate its anchors explicitly.");
            }
        }
    }

    // Value snapshot, not object identity: edits to an existing config invalidate plans.
    public readonly struct CalendarBoundaryStamp : IEquatable<CalendarBoundaryStamp>
    {
        private readonly bool enabled, started;
        private readonly int version, curve;
        private readonly double epoch, startYear, duration, yearDays;
        internal CalendarBoundaryStamp(SimulationConfig c)
        {
            var s = c.SmoothCalendar; enabled = s != null; started = s != null && s.Started;
            version = s == null ? 0 : s.Version; curve = s == null ? 0 : (int)s.Curve;
            epoch = s == null ? 0 : s.EpochDays; startYear = s == null ? 0 : s.NeolithicStartYear;
            duration = c.TurnDays; yearDays = c.DaysPerYear;
        }
        public bool Equals(CalendarBoundaryStamp b) => enabled == b.enabled && started == b.started &&
            version == b.version && curve == b.curve && epoch == b.epoch && startYear == b.startYear &&
            duration == b.duration && yearDays == b.yearDays;
        public override bool Equals(object other) => other is CalendarBoundaryStamp b && Equals(b);
        public override int GetHashCode() => version ^ curve ^ duration.GetHashCode() ^ epoch.GetHashCode();
    }

    public static class SmoothCalendar
    {
        public static readonly double EarlyRate = Math.Log(2.0) / 1200.0;
        public static readonly double ModernRate = Math.Log(10.0) / 100.0;
        internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        public static CalendarBoundaryStamp Stamp(WorldState world) => new CalendarBoundaryStamp(world.Config);
        private static double Hermite(double x, double x0, double x1, double y0, double y1, double m0, double m1)
        {
            double h = x1-x0, t = (x-x0)/h, t2=t*t, t3=t2*t;
            return (2*t3-3*t2+1)*y0+(t3-2*t2+t)*h*m0+(-2*t3+3*t2)*y1+(t3-t2)*h*m1;
        }
        // Pure O(1) calendar evaluation. No world tick, stock mutation, RNG or era scan.
        public static double DaysAtElapsedYears(SimulationConfig config, double elapsedYears)
        {
            var s = config.SmoothCalendar;
            if (s == null) return config.TurnDays;
            if (!Finite(elapsedYears) || elapsedYears < 0) throw new ArgumentOutOfRangeException(nameof(elapsedYears));
            s.Validate(config);
            double early = Math.Log(20 * config.DaysPerYear) - EarlyRate * elapsedYears;
            if (s.Curve == SmoothCalendarCurve.EarlyExponential) return Math.Max(1, Math.Exp(early));
            double year = s.NeolithicStartYear + elapsedYears;
            double logDays;
            if (year <= 1750) logDays = early;
            else if (year < 1800)
            {
                double left = Math.Log(20 * config.DaysPerYear) - EarlyRate * (1750-s.NeolithicStartYear);
                logDays = Hermite(year,1750,1800,left,Math.Log(100),-EarlyRate,-ModernRate);
            }
            else if (year <= 1990) logDays = Math.Log(10) - ModernRate * (year-1900);
            else if (year < 2000) logDays = Hermite(year,1990,2000,ModernRate*10,0,-ModernRate,0);
            else return 1;
            return Math.Max(1, Math.Min(20*config.DaysPerYear, Math.Exp(logDays)));
        }
        public static double ExpectedDays(WorldState world)
        {
            var s = world.Config.SmoothCalendar;
            if (s == null) return world.Config.TurnDays;
            if (!Finite(world.Days) || world.Days < 0 || (s.Started && world.Days < s.EpochDays))
                throw new InvalidOperationException("World precedes its smooth-calendar epoch.");
            return DaysAtElapsedYears(world.Config, s.Started ? (world.Days-s.EpochDays)/world.Config.DaysPerYear : 0);
        }
        public static double CalendarYear(WorldState world)
        {
            var s = world.Config.SmoothCalendar;
            if (s == null) throw new InvalidOperationException("Fixed-time worlds have no calendar epoch.");
            return s.NeolithicStartYear + (s.Started ? (world.Days-s.EpochDays)/world.Config.DaysPerYear : 0);
        }
        public static void RequireCurrentBoundary(WorldState world)
        {
            if (world.Config.SmoothCalendar == null) return;
            double expected = ExpectedDays(world);
            if (Math.Abs(expected-world.Config.TurnDays) > 1e-9*Math.Max(1,expected))
                throw new InvalidOperationException("Smooth-calendar duration is stale or was edited outside the committed boundary.");
        }
        // Called ONCE after the old horizon has committed, including social and knowledge passes.
        // It prepares next-turn duration; it never reinterprets the completed TurnRecord.
        internal static void CommitNextBoundary(WorldState world)
        {
            var s = world.Config.SmoothCalendar;
            if (s == null) return;
            if (!s.Started && world.Finished) { s.Started = true; s.EpochDays = world.Days; }
            world.Config.TurnDays = ExpectedDays(world);
        }
    }
}
