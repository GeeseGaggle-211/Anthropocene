using System;
using System.Linq;

namespace Anthropocene.Simulation
{
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
