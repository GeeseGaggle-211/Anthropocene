using System;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Job-based need coverage, never physical goods or shelter capacity.
    public static class SubsistenceNeeds
    {
        public const double GearBaseline = .80;
        public static double EligibleResidents(Camp c, double residents)
        {
            double other = c.Workforce?.Assignments.Where(a => a != null && a.Role != WorkforceRoles.Gatherer)
                .Sum(a => Math.Max(0, a.Population)) ?? 0;
            return Math.Max(0, residents - other);
        }
        public static double CoveredResidents(WorldState w, Camp c, double residents)
        {
            if (!w.Config.Neolithic.SubsistenceNeedsCovered || residents <= 0) return 0;
            return GearBaseline * EligibleResidents(c, residents);
        }
        public static double GoodsResidents(WorldState w, Camp c, double residents)
            => Math.Max(0, residents - CoveredResidents(w, c, residents));
        public static double Coverage(double people, double automaticallyCovered, double physicalPeople)
            => people > 0 ? Math.Min(1, (Math.Max(0, automaticallyCovered) + Math.Max(0, physicalPeople)) / people) : 1;
        public static double PhysicalClothingFraction(Camp c)
            => c.PhysicalClothingPerCapita ?? c.ClothPerCapita;
        // Manufactured coverage serves non-Gatherers first, then upgrades
        // self-made gear. Its unfilled part cannot receive an expectation-ramp
        // bonus above the subsistence ceiling. Other jobs retain the old ramp.
        public static double GearQuality(double people, double eligible, double physicalPeople,
            double coverage, double expectation)
        {
            if (people <= 0) return 1;
            double missingGear = Math.Max(0, (1 - GearBaseline) * eligible
                - Math.Max(0, physicalPeople - (people - eligible)));
            return Numbers.Clamp(1 - expectation * (1 - coverage)
                - (1 - expectation) * missingGear / people, 0, 1);
        }

        // Same stable worker loss apportionment as Immigration and Fission.
        // Arrivals get no assigned role, hence are Gatherers.
        public static PopulationLedger ProjectWorkforce(Camp c, int people, bool daughter = false)
        {
            var ledger = new PopulationLedger { Assignments = c.Workforce.Assignments
                .Select(a => new PopulationAssignment { Role = a.Role, Population = a.Population }).ToList() };
            if (daughter)
            {
                var remaining = ProjectWorkforce(c, Math.Max(0, c.Population - people));
                foreach (var a in ledger.Assignments)
                    a.Population -= remaining.Assignments.Where(r => r.Role == a.Role).Sum(r => r.Population);
                ledger.Assignments.RemoveAll(a => a.Role == WorkforceRoles.Farmer);
                ledger.Normalize(people);
            }
            else if (people < c.Population) ledger.ApplyCasualties(c.Population, c.Population - people);
            return ledger;
        }
        public static double ProjectCovered(WorldState w, Camp c, int people, bool daughter = false)
        {
            if (!w.Config.Neolithic.SubsistenceNeedsCovered) return 0;
            var ledger = ProjectWorkforce(c, people, daughter);
            return GearBaseline * Math.Max(0, people - ledger.Assignments.Where(a => a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population));
        }
    }
}
