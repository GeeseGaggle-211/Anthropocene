using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    public static class WorkforceRoles
    {
        public const string Gatherer = "Gatherer";
        public const string Farmer = "Farmer";
        public const string Craftsperson = "Craftsperson";
        public const string Toolmaker = "Toolmaker";
        public const string Potter = "Potter";
        public const string Weaver = "Weaver";
        public const string Carpenter = "Carpenter";
        public const string Builder = "Builder";
        public const string Woodcutter = "Woodcutter", Stonecutter = "Stonecutter", ClayMiner = "ClayMiner";
        // Keep the saved Stonecutter identifier stable; only its player-facing label changes.
        public static string Label(string role) => role == Stonecutter ? "Stone Quarrier" : role == ClayMiner ? "Clay Miner" : role;
        public static readonly string[] Extractors = { Stonecutter, Woodcutter, ClayMiner };

        public static readonly string[] Specialists = { Toolmaker, Potter, Weaver, Carpenter, Builder };
        public static readonly string[] All = { Gatherer, Farmer, Craftsperson, Toolmaker, Potter, Weaver, Carpenter, Builder, Woodcutter, Stonecutter, ClayMiner };
    }

    public sealed partial class PopulationLedger
    {
        public void Assign(string role, int count, int residents) => Reassign(null, role, count, residents);
        public void Release(string role, int count, int residents) => Reassign(role, null, count, residents);
        public void Reassign(string from, string to, int count, int residents)
        {
            if (count < 0 || residents < 0 || from == to || from != null && string.IsNullOrWhiteSpace(from) || to != null && string.IsNullOrWhiteSpace(to)) throw new ArgumentException("Invalid workforce reassignment.");
            if (Assigned > residents) throw new InvalidOperationException("Workforce exceeds residents.");
            var source = Assignments.SingleOrDefault(a => a.Role == from);
            if (count > (from == null ? Gatherers(residents) : source?.Population ?? 0)) throw new InvalidOperationException("Not enough available workers.");
            if (count == 0) return;
            if (source != null) source.Population -= count;
            if (to != null)
            {
                var destination = Assignments.SingleOrDefault(a => a.Role == to);
                if (destination == null) Assignments.Add(new PopulationAssignment { Role = to, Population = count }); else destination.Population += count;
            }
            Normalize(residents);
        }
        public void ApplyCasualties(int residents, int deaths)
        {
            if (deaths < 0 || deaths > residents || Assigned > residents) throw new ArgumentException("Invalid resident casualties.");
            if (deaths == 0) return;
            var roles = Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).ToArray();
            var counts = new[] { Gatherers(residents) }.Concat(roles.Select(a => a.Population)).ToArray();
            var losses = counts.Select(n => (int)((long)deaths * n / residents)).ToArray();
            int remainder = deaths - losses.Sum();
            foreach (int i in Enumerable.Range(0, counts.Length).OrderByDescending(i => (long)deaths * counts[i] % residents).ThenBy(i => i))
                if (remainder > 0 && losses[i] < counts[i]) { losses[i]++; remainder--; }
            for (int i = 0; i < roles.Length; i++) roles[i].Population -= losses[i + 1];
            Normalize(residents - deaths);
        }
    }
}
