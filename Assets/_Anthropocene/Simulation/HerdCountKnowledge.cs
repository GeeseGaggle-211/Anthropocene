using System;
using System.Globalization;

namespace Anthropocene.Simulation
{
    public sealed class HerdCountEstimate
    {
        public readonly int Representative;
        public readonly bool Exact;
        public readonly string Label, Compact;
        public HerdCountEstimate(int representative, bool exact, string label, string compact) { Representative = representative; Exact = exact; Label = label; Compact = compact; }
    }
    public static class HerdCountKnowledge
    {
        public static HerdCountEstimate Observe(int animals, SimulationConfig config, bool ownDomestic = false)
        {
            if (ownDomestic) { string exact = animals.ToString(CultureInfo.InvariantCulture); return new HerdCountEstimate(animals, true, exact, exact); }
            if (animals < config.HerdSmallCountThreshold) return new HerdCountEstimate(config.HerdSmallCountThreshold / 2, false, "fewer than " + config.HerdSmallCountThreshold, "<" + config.HerdSmallCountThreshold);
            if (animals <= config.HerdBroadCountSize)
            {
                int rounded = (int)Math.Round(animals / (double)config.HerdCountRounding, MidpointRounding.AwayFromZero) * config.HerdCountRounding;
                string label = "~" + rounded.ToString(CultureInfo.InvariantCulture); return new HerdCountEstimate(rounded, false, label, label);
            }
            int lower = animals / config.HerdBroadCountSize * config.HerdBroadCountSize;
            string broad = lower.ToString(CultureInfo.InvariantCulture) + "+"; return new HerdCountEstimate(lower, false, broad, broad);
        }
    }
}
