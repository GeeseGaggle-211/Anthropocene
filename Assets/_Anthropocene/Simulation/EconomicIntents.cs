using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class EconomicLaborPlan
    {
        public double MaterialGatherers, ExpansionFarmers, EquipmentTarget;
        // Shared-allocation planner (Docs/SharedAllocationValidation.md, warm start):
        // the fulfilment-path position the last plan chose. Absent in older saves
        // and outside the shared planner (unset); not part of the intent stamp.
        [System.Runtime.Serialization.OptionalField] public bool FulfilmentPositionSet;
        [System.Runtime.Serialization.OptionalField] public double FulfilmentPosition;
        // Trade (Docs/Trade.md): the labour price w (person-QoL-years per worker-year) the last plan chose, the
        // lagged cost of producing to order. Recorded only while trade is enabled; absent in older saves (unset).
        [System.Runtime.Serialization.OptionalField] public bool LabourPriceSet;
        [System.Runtime.Serialization.OptionalField] public double LabourPrice;
        // Trade (phase V, Docs/PlannerTradeStorageDesign.md): the farm-labour price w_farm0 the last plan saw (the expansion
        // ladder's value after its staffed farmers), which prices labour-saving tools as the planner does. Recorded only
        // while trade is enabled; absent in older saves (unset).
        [System.Runtime.Serialization.OptionalField] public bool FarmLabourPriceSet;
        [System.Runtime.Serialization.OptionalField] public double FarmLabourPrice;
        // Trade: the goods values the last plan published (Docs/Trade.md, "Planner-published values"), at most one per
        // trade curve key, read lagged by the next handshake in place of the provisional curves. Recorded only while
        // trade is enabled and the fulfilment rule publishes; absent in older saves (null: every curve provisional).
        [System.Runtime.Serialization.OptionalField] public List<TradeValueSchedule> TradeValues;
        public EconomicLaborPlan Copy()
        {
            var copy = (EconomicLaborPlan)MemberwiseClone();
            copy.TradeValues = TradeValues?.Select(s => s?.Copy()).ToList();
            return copy;
        }
        public IEnumerable<double> Values() => new[] { MaterialGatherers, ExpansionFarmers, EquipmentTarget };
    }
    [Serializable]
    public sealed class EconomicIntent
    {
        public List<PopulationAssignment> Assignments = new List<PopulationAssignment>();
        public CraftingPriorities Priorities = new CraftingPriorities();
        public int Focus = -1;
        [System.Runtime.Serialization.OptionalField] public int AutomationTurns;
        [System.Runtime.Serialization.OptionalField] public EconomicLaborPlan Labor;
        [System.Runtime.Serialization.OptionalField] public double ManagedBreedingIntensity = .55;
        // Chosen managed-herd size (Docs/HerdLabour.md). Unset (the default, and absent in older saves) means no slaughter; set, animals above it are slaughtered after breeding.
        [System.Runtime.Serialization.OptionalField] public bool HerdTargetSet;
        [System.Runtime.Serialization.OptionalField] public int HerdTarget;
        public double StonePriority = 1, ClayPriority = 1, WoodPriority = 1, PotsTarget, FarmToolsTarget;
        public EconomicIntent Copy() => new EconomicIntent { Assignments = Assignments?.Select(a => a == null ? null : new PopulationAssignment { Role = a.Role, Population = a.Population }).ToList(), Priorities = Priorities == null ? null : new CraftingPriorities { HandTools = Priorities.HandTools, Pots = Priorities.Pots, Cloth = Priorities.Cloth, FarmTools = Priorities.FarmTools, Housing = Priorities.Housing, Tools = Priorities.Tools, AnimalFabric = Priorities.AnimalFabric, LightHousing = Priorities.LightHousing, HerdingTools = Priorities.HerdingTools, FishingTools = Priorities.FishingTools }, Focus = Focus, AutomationTurns = AutomationTurns, Labor = Labor?.Copy(), StonePriority = StonePriority, ClayPriority = ClayPriority, WoodPriority = WoodPriority, PotsTarget = PotsTarget, FarmToolsTarget = FarmToolsTarget, ManagedBreedingIntensity = ManagedBreedingIntensity, HerdTargetSet = HerdTargetSet, HerdTarget = HerdTarget };
        public static EconomicIntent From(Camp c) => new EconomicIntent { Assignments = c.Workforce.Assignments, Priorities = c.CraftingPriorities, Focus = c.CultivationFocusTile, AutomationTurns = c.EconomyAutomationTurns, Labor = c.EconomicLabor, StonePriority = c.StonePriority, ClayPriority = c.ClayPriority, WoodPriority = c.WoodPriority, PotsTarget = c.PotsTarget, FarmToolsTarget = c.FarmToolsTarget, ManagedBreedingIntensity = c.ManagedBreedingIntensity, HerdTargetSet = c.HerdTargetSet, HerdTarget = c.HerdTarget }.Copy();
        public string Stamp => string.Join(",", (Assignments ?? new List<PopulationAssignment>()).OrderBy(a => a?.Role, StringComparer.Ordinal).Select(a => a == null ? "invalid" : a.Role + "=" + a.Population)) + ":" + Focus + ":" + AutomationTurns + ":" + (Labor == null ? "manual" : string.Join(",", Labor.Values().Select(v => v.ToString("R", CultureInfo.InvariantCulture)))) + ":" + string.Join(",", Values().Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + ":" + (HerdTargetSet ? HerdTarget : -1);
        public IEnumerable<double> Values() => Priorities == null ? Array.Empty<double>() : new[] { Priorities.HandTools, Priorities.Pots, Priorities.Cloth, Priorities.FarmTools, Priorities.Housing, Priorities.Tools, Priorities.AnimalFabric, Priorities.LightHousing, Priorities.HerdingTools, Priorities.FishingTools, StonePriority, ClayPriority, WoodPriority, PotsTarget, FarmToolsTarget, ManagedBreedingIntensity };
    }
    public static class EconomicIntents
    {
        public static string RequiredTechnology(string role) => role == WorkforceRoles.Toolmaker || role == WorkforceRoles.Stonecutter ? NeolithicTech.Toolmaking : role == WorkforceRoles.Potter || role == WorkforceRoles.ClayMiner ? NeolithicTech.Pottery : role == WorkforceRoles.Weaver ? NeolithicTech.Weaving : role == WorkforceRoles.Carpenter || role == WorkforceRoles.Woodcutter ? NeolithicTech.Carpentry : role == WorkforceRoles.Builder ? NeolithicTech.Construction : null;
        public static string Validate(AgentObservation o, SettlementObservation c, EconomicIntent e)
            => Validate(Constraints(o, c), e);
        public static EconomicConstraints Constraints(AgentObservation o, SettlementObservation c) => c == null ? null : new EconomicConstraints { Agriculture = c.Agriculture, Husbandry = c.Husbandry, FishingEnabled = c.FishingEnabled, Population = c.Population, Technologies = c.Technologies.ToArray(), FocusTiles = o.Tiles.Values.Where(t => t.Cultivable && o.Map.Distance(c.Tile, t.Id) <= 1).Select(t => t.Id).OrderBy(id => id).ToArray() };
        public static string Validate(EconomicConstraints c, EconomicIntent e)
        {
            if (c == null) return "Economic capability information is unavailable.";
            if (!(c.Agriculture || c.Husbandry || c.FishingEnabled)) return "Economy requires Agriculture or Animal Husbandry.";
            if (e == null || e.Assignments == null || e.Priorities == null) return "Economic plan is incomplete; assignments and priorities are required.";
            if (e.Assignments.Any(a => a == null || !WorkforceRoles.All.Contains(a.Role) || a.Population <= 0) || e.Assignments.GroupBy(a => a.Role).Any(g => g.Count() > 1) || e.Assignments.Sum(a => (long)a.Population) > c.Population) return "Invalid workforce allocation.";
            if (e.Assignments.Any(a => RequiredTechnology(a.Role) != null && !c.Technologies.Contains(RequiredTechnology(a.Role)))) return "Specialist technology is not mastered.";
            if (e.Values().Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0)) return "Economic values must be finite and nonnegative.";
            if (e.AutomationTurns < -1) return "Automation must be off, continuous, or a positive number of turns.";
            if (e.Labor != null && (e.Labor.Values().Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0) ||
                e.Labor.MaterialGatherers > c.Population - e.Assignments.Where(a => a.Role != WorkforceRoles.Gatherer).Sum(a => a.Population) + 1e-8 ||
                e.Labor.ExpansionFarmers > e.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population) + 1e-8 || e.Labor.EquipmentTarget > c.Population)) return "Labor reservations exceed the real workforce.";
            if (e.Labor != null && TradeValueSchedule.Error(e.Labor.TradeValues) is string tradeValues) return "Invalid trade values: " + tradeValues + ".";
            if (e.Focus < -1 || e.Focus >= 0 && !c.FocusTiles.Contains(e.Focus)) return "Cultivation focus must be visible eligible radius-1 land.";
            if (!c.Agriculture && e.Assignments.Any(a => a.Role == WorkforceRoles.Farmer)) return "Farmer assignments require Cultivation.";
            if (!c.Husbandry && e.Assignments.Any(a => a.Role == WorkforceRoles.Herder)) return "Herder assignments require Animal Husbandry.";
            if (!c.FishingEnabled && e.Assignments.Any(a => a.Role == WorkforceRoles.Fisher)) return "Fisher assignments require feature-enabled Watercraft Fishing.";
            if (e.ManagedBreedingIntensity > 1) return "Managed breeding intensity must be at most one.";
            if (e.HerdTargetSet && (e.HerdTarget < 0 || !c.Husbandry)) return e.HerdTarget < 0 ? "Herd target cannot be negative." : "A herd target requires Animal Husbandry.";
            return null;
        }
        public static void Apply(Camp c, EconomicIntent intent)
        {
            var e = intent.Copy(); c.Workforce.Assignments = e.Assignments; c.CraftingPriorities = e.Priorities;
            c.EconomyAutomationTurns = e.AutomationTurns; c.EconomicLabor = e.Labor;
            c.CultivationFocusTile = e.Focus; c.StonePriority = e.StonePriority; c.ClayPriority = e.ClayPriority; c.WoodPriority = e.WoodPriority; c.PotsTarget = e.PotsTarget; c.FarmToolsTarget = e.FarmToolsTarget;
            c.ManagedBreedingIntensity = e.ManagedBreedingIntensity; c.ManagedBreedingPolicyInitialized = true;
            c.HerdTargetSet = e.HerdTargetSet; c.HerdTarget = e.HerdTargetSet ? e.HerdTarget : 0;
        }
        // A policy proposal, copied into an ordinary validated order before use.
        public static EconomicIntent Automatic(WorldState w, Camp c, bool reset = false)
        {
            if (!reset && c.EconomyAutomationTurns == 0 && c.Workforce.Assignments.Count > 0) return EconomicIntent.From(c);
            return EconomicAllocationStrategy.Plan(w, c, EconomicIntent.From(c), out _);
        }
    }
    [Serializable]
    public sealed class EconomicConstraints
    {
        public bool Agriculture;
        public bool Husbandry;
        public bool FishingEnabled;
        public int Population;
        public string[] Technologies = Array.Empty<string>();
        public int[] FocusTiles = Array.Empty<int>();
    }
}
