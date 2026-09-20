using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class EconomicIntent
    {
        public List<PopulationAssignment> Assignments = new List<PopulationAssignment>();
        public CraftingPriorities Priorities = new CraftingPriorities();
        public int Focus = -1;
        public double StonePriority = 1, ClayPriority = 1, WoodPriority = 1, PotsTarget, FarmToolsTarget;
        public EconomicIntent Copy() => new EconomicIntent { Assignments = Assignments?.Select(a => a == null ? null : new PopulationAssignment { Role = a.Role, Population = a.Population }).ToList(), Priorities = Priorities == null ? null : new CraftingPriorities { HandTools = Priorities.HandTools, Pots = Priorities.Pots, Cloth = Priorities.Cloth, FarmTools = Priorities.FarmTools, Housing = Priorities.Housing, Tools = Priorities.Tools }, Focus = Focus, StonePriority = StonePriority, ClayPriority = ClayPriority, WoodPriority = WoodPriority, PotsTarget = PotsTarget, FarmToolsTarget = FarmToolsTarget };
        public static EconomicIntent From(Camp c) => new EconomicIntent { Assignments = c.Workforce.Assignments, Priorities = c.CraftingPriorities, Focus = c.CultivationFocusTile, StonePriority = c.StonePriority, ClayPriority = c.ClayPriority, WoodPriority = c.WoodPriority, PotsTarget = c.PotsTarget, FarmToolsTarget = c.FarmToolsTarget }.Copy();
        public string Stamp => string.Join(",", (Assignments ?? new List<PopulationAssignment>()).OrderBy(a => a?.Role, StringComparer.Ordinal).Select(a => a == null ? "invalid" : a.Role + "=" + a.Population)) + ":" + Focus + ":" + string.Join(",", Values().Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
        public IEnumerable<double> Values() => Priorities == null ? Array.Empty<double>() : new[] { Priorities.HandTools, Priorities.Pots, Priorities.Cloth, Priorities.FarmTools, Priorities.Housing, Priorities.Tools, StonePriority, ClayPriority, WoodPriority, PotsTarget, FarmToolsTarget };
    }
    public static class EconomicIntents
    {
        public static string RequiredTechnology(string role) => role == WorkforceRoles.Toolmaker || role == WorkforceRoles.Stonecutter ? NeolithicTech.Toolmaking : role == WorkforceRoles.Potter ? NeolithicTech.Pottery : role == WorkforceRoles.Weaver ? NeolithicTech.Weaving : role == WorkforceRoles.Carpenter || role == WorkforceRoles.Woodcutter ? NeolithicTech.Carpentry : role == WorkforceRoles.Builder ? NeolithicTech.Construction : null;
        public static string Validate(AgentObservation o, SettlementObservation c, EconomicIntent e)
            => Validate(Constraints(o, c), e);
        public static EconomicConstraints Constraints(AgentObservation o, SettlementObservation c) => c == null ? null : new EconomicConstraints { Agriculture = c.Agriculture, Population = c.Population, Technologies = c.Technologies.ToArray(), FocusTiles = o.Tiles.Values.Where(t => t.Cultivable && o.Map.Distance(c.Tile, t.Id) <= 1).Select(t => t.Id).OrderBy(id => id).ToArray() };
        public static string Validate(EconomicConstraints c, EconomicIntent e)
        {
            if (c == null || !c.Agriculture || e == null || e.Assignments == null || e.Priorities == null) return "Agriculture and a complete economic plan are required.";
            if (e.Assignments.Any(a => a == null || !WorkforceRoles.All.Contains(a.Role) || a.Population <= 0) || e.Assignments.GroupBy(a => a.Role).Any(g => g.Count() > 1) || e.Assignments.Sum(a => (long)a.Population) > c.Population) return "Invalid workforce allocation.";
            if (e.Assignments.Any(a => RequiredTechnology(a.Role) != null && !c.Technologies.Contains(RequiredTechnology(a.Role)))) return "Specialist technology is not mastered.";
            if (e.Values().Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0)) return "Economic values must be finite and nonnegative.";
            if (e.Focus < -1 || e.Focus >= 0 && !c.FocusTiles.Contains(e.Focus)) return "Cultivation focus must be visible eligible radius-1 land.";
            return null;
        }
        public static void Apply(Camp c, EconomicIntent intent)
        {
            var e = intent.Copy(); c.Workforce.Assignments = e.Assignments; c.CraftingPriorities = e.Priorities;
            c.CultivationFocusTile = e.Focus; c.StonePriority = e.StonePriority; c.ClayPriority = e.ClayPriority; c.WoodPriority = e.WoodPriority; c.PotsTarget = e.PotsTarget; c.FarmToolsTarget = e.FarmToolsTarget;
        }
        // A policy proposal, copied into an ordinary validated order before use.
        public static EconomicIntent Automatic(WorldState w, Camp c, bool reset = false)
        {
            var e = EconomicIntent.From(c); var n = w.Config.Neolithic;
            if (!reset && e.Assignments.Count > 0) return e;
            e.Assignments.Clear();
            int foodWorkers = Math.Min(c.Population, (int)Math.Ceiling(c.Population * w.Config.Consumption / w.Config.GatherPerPerson));
            double turnFraction = w.Config.TurnDays / w.Config.DaysPerYear / n.ReferenceTurnYears;
            double textiles = c.Population * n.FabricConsumptionPerPersonTurn * turnFraction / (w.Config.GenerationIntervals * n.WeaverFabricOutputPerBaseDays * n.GeneralistTaskEfficiency);
            double housing = Math.Max(0, c.Population / n.HousingPeoplePerUnit - c.Inventory.HousingUnits * Math.Pow(1 - n.HousingDecayPerTurn, turnFraction)) / (w.Config.GenerationIntervals * n.BuilderHousingOutputPerBaseDays * n.GeneralistTaskEfficiency);
            int crafts = Math.Min(c.Population - foodWorkers, (int)Math.Ceiling(textiles + housing));
            int farmers = c.Population - foodWorkers - crafts;
            if (crafts > 0) e.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Craftsperson, Population = crafts });
            if (farmers > 0) e.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = farmers });
            return e;
        }
    }
    [Serializable]
    public sealed class EconomicConstraints
    {
        public bool Agriculture;
        public int Population;
        public string[] Technologies = Array.Empty<string>();
        public int[] FocusTiles = Array.Empty<int>();
    }
}
