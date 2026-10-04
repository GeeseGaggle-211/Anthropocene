using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // The automatic economic allocation strategy (Docs/EconomicControlContract.md §15): one dispatch point for
    // TurnPlanner.AutomaticIntent and EconomicIntents.Automatic. "Legacy" is the Value/Uniform planner exactly as before.
    public static class EconomicAllocationStrategy
    {
        public const string Legacy = "Legacy", DemandControl = "DemandControl";
        public static bool IsKnown(string name) => name == Legacy || name == DemandControl;
        public static bool IsDemandControl(SimulationConfig cfg) => cfg != null && cfg.EconomicAllocation == DemandControl;
        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, out ProductionOrder production)
        {
            if (IsDemandControl(w.Config)) return DemandController.Plan(w, c, settings, out production, out _);
            return EconomicPlanner.Plan(w, c, settings, EconomicPlanner.ConfiguredRule(w.Config), out _, out production);
        }
    }

    // DemandControl v0.1, slice 1 (Docs/EconomicControlContract.md with Addendum A).
    //
    // Pure and per camp: it reads the committed camp (a planning view) and returns a legal EconomicIntent plus a production
    // order whose ceilings the forecast and resolver honour. Single-service controllers make requests:
    //   Survival     herders for the current herd, operating farmers and food gatherers (the existing physical rules);
    //   Continuity   this turn's clothing consumption, shelter replacement (fixed Housing for farming camps, tents for
    //                herding ones), Farm and Herding Tools for the work staffed;
    //   Recovery     the clothing reserve, the shelter deficit, Pots for the storage gap.
    // Development (expansion, herd investment) is a fixed directive of none in this slice. Each request is a complete bundle:
    // craft time, made intermediates (Animal Fabric for tents, Housing Material for Housing), raw extraction beyond stock,
    // feedstock and its gathering, and tool service (toolmaking at the urgency of the work it enables). The Elder grants
    // whole classes in order, within a class an equal fulfilment fraction, freezing requests whose own input saturates; then
    // integer staffing is compiled per role with at most one repair pass. Cost: O(requests x bisection) per camp, one
    // physical demand pass, no production forecast and no Value search.
    internal static class DemandController
    {
        internal enum ServiceClass { Survival = 1, Continuity = 2, Recovery = 3 }
        private const double Epsilon = 1e-9;

        // Per-unit requirements of a request's complete bundle.
        internal sealed class Bundle
        {
            public readonly Dictionary<string, double> RoleYears = new Dictionary<string, double>();   // craft worker-years per unit, by role
            public readonly Dictionary<CraftGood, double> Outputs = new Dictionary<CraftGood, double>(); // goods made per unit, intermediates included
            public double ToolOutputs, Stone, Clay, Wood, Construction, Plant, Animal, Fabric, Material;
            public void Add(Bundle b, double scale)
            {
                foreach (var p in b.RoleYears) RoleYears[p.Key] = (RoleYears.TryGetValue(p.Key, out double v) ? v : 0) + p.Value * scale;
                foreach (var p in b.Outputs) Outputs[p.Key] = (Outputs.TryGetValue(p.Key, out double v) ? v : 0) + p.Value * scale;
                ToolOutputs += b.ToolOutputs * scale; Stone += b.Stone * scale; Clay += b.Clay * scale; Wood += b.Wood * scale; Construction += b.Construction * scale;
                Plant += b.Plant * scale; Animal += b.Animal * scale; Fabric += b.Fabric * scale; Material += b.Material * scale;
            }
        }
        // One service request and what the Elder granted (transient diagnostics, never saved).
        internal sealed class Request
        {
            public string Service; public ServiceClass Class; public CraftGood Good; public double Units, Granted; public string Unmet = "";
            internal Bundle Bundle; internal bool Frozen;
            public double Fraction => Units > Epsilon ? Granted / Units : 1;
        }
        internal sealed class Allocation
        {
            public readonly List<Request> Requests = new List<Request>();
            public readonly Dictionary<string, int> Staff = new Dictionary<string, int>();
            public int Herders, Farmers, FoodGatherers; public double FeedstockGatherers, MaterialGatherers, ToolsMade;
            public readonly Dictionary<CraftGood, double> Ceilings = new Dictionary<CraftGood, double>();
            public string FoodUnmet = ""; public int RepairPasses;
        }

        internal static EconomicIntent Plan(WorldState w, Camp c, EconomicIntent settings, out ProductionOrder production, out Allocation allocation)
        {
            production = null; var alloc = new Allocation(); allocation = alloc;
            var e = settings.Copy(); e.Assignments.Clear(); e.Labor = null;
            if (c.Population <= 0 || !CultivationEconomy.EconomyEnabled(c)) { e.AutomationTurns = 0; return e; }
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear, intervals = cfg.GenerationIntervals, perYear = cfg.DaysPerYear / cfg.BaseDays;
            var d = CampProductionDemand.BuildPhysical(w, c, e, false);
            var recipes = d.Recipes; var byGood = recipes.ToDictionary(r => r.Good);
            int residents = c.Population;
            double gatherRate = cfg.GatherPerPerson * intervals;  // food one gatherer brings in over the turn

            // ---- Survival: the current herd, field operation and food gathering (fixed directives: no expansion) ----
            int herders = Math.Min(residents, d.ReservedHerders);
            double animalReceipts = d.ManagedFood.AnimalFood;
            double foodTarget = Math.Min(d.ForageAfterHerds, Math.Max(0, d.Biological - animalReceipts));
            double terrestrial = Math.Min(d.TerrestrialAfterHerds, foodTarget);
            double farmRate = d.HarvestProductivity * cfg.GatherPerPerson * intervals;
            int requiredOperating = d.Established && farmRate > 0
                ? (int)Math.Ceiling(Math.Max(d.Upkeep, cfg.FarmerProductivity >= 1 ? terrestrial / farmRate : 0) - Epsilon) : 0;
            int farmers = Math.Min(Math.Max(0, residents - herders), Math.Max(0, requiredOperating));
            double harvest = Math.Min(terrestrial, farmers * farmRate);
            int wantedGatherers = gatherRate > 0 ? (int)Math.Ceiling(Math.Max(0, foodTarget - harvest) / gatherRate - Epsilon) : 0;
            int foodGatherers = Math.Min(Math.Max(0, residents - herders - farmers), wantedGatherers);
            if (farmers < requiredOperating || foodGatherers < wantedGatherers) alloc.FoodUnmet = "labor rationing";
            alloc.Herders = herders; alloc.Farmers = farmers; alloc.FoodGatherers = foodGatherers;
            double budget = Math.Max(0, residents - herders - farmers - foodGatherers);   // persons left for goods

            // The resolver-shaped candidate the tool demands read (herders and farmers staffed).
            var staffed = e.Copy();
            if (herders > 0) staffed.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = herders });
            if (farmers > 0) staffed.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = farmers });
            var candidate = EconomicPlanner.Preview(c, staffed, residents);
            NeolithicProduction.DepreciateHousing(w, candidate, years);

            // ---- Bundles ----
            string Role(CraftRecipe r) => c.People.HasTech(r.Track) ? r.Specialist : WorkforceRoles.Craftsperson;
            bool Usable(CraftRecipe r)
                => r != null && !(r.SpecialistOnly && !c.People.HasTech(r.Track)) && !(r.AgricultureOnly && !CultivationEconomy.Enabled(c))
                   && !(r.HusbandryOnly && !CultivationEconomy.HusbandryEnabled(c)) && SpecialistProductivity.CraftRate(w, c, r, Role(r)) > 0;
            double craftMultiplier = d.HousingMultiplier;
            Bundle Craft(CraftGood good)
            {
                var r = byGood[good]; var b = new Bundle();
                double rate = SpecialistProductivity.CraftRate(w, c, r, Role(r));
                double m = ToolCoverageBudget.IsHousingWork(good) ? craftMultiplier : 1;
                if (!(rate > 0) || !(m > 0)) return null;
                b.RoleYears[Role(r)] = 1 / (rate * perYear * m);
                b.Outputs[good] = 1;
                if (r.Equipment) b.ToolOutputs = 1;
                b.Stone = r.Stone; b.Clay = r.Clay; b.Wood = r.Wood; b.Construction = r.ConstructionMaterial;
                b.Plant = r.PlantFood; b.Animal = r.AnimalFood;
                return b;
            }
            // Stock of made intermediates credited once, before any is made.
            double freeFabric = Math.Max(0, c.Inventory.AnimalFabric - c.TradeExport(CraftGood.AnimalFabric));
            double freeMaterial = Math.Max(0, c.Inventory.HousingMaterial - c.TradeExport(CraftGood.HousingMaterial));
            Bundle WithInput(Bundle b, double perUnit, CraftGood input, double units, ref double stock, bool fabric)
            {
                if (b == null || !(perUnit > 0)) return b;
                double need = units * perUnit, fromStock = Math.Min(stock, need); stock -= fromStock;
                double madeShare = need > Epsilon ? (need - fromStock) / need : 0;
                if (fabric) b.Fabric += perUnit * (1 - madeShare); else b.Material += perUnit * (1 - madeShare);
                if (madeShare > Epsilon)
                {
                    var made = Usable(byGood[input]) ? Craft(input) : null;
                    if (made == null) return null;
                    b.Add(made, perUnit * madeShare);
                }
                return b;
            }

            // ---- Requests ----
            void Request(string service, ServiceClass cls, CraftGood good, double units)
            {
                if (!(units > Epsilon) || !byGood.ContainsKey(good)) return;
                var r = byGood[good];
                var req = new Request { Service = service, Class = cls, Good = good, Units = units };
                if (!Usable(r) || NeolithicProduction.Priority(candidate, good) <= 0) { req.Unmet = "missing capability"; alloc.Requests.Add(req); return; }
                var b = Craft(good);
                if (r.AnimalFabric > 0) b = WithInput(b, r.AnimalFabric, CraftGood.AnimalFabric, units, ref freeFabric, true);
                if (r.HousingMaterial > 0) b = WithInput(b, r.HousingMaterial, CraftGood.HousingMaterial, units, ref freeMaterial, false);
                if (b == null) { req.Unmet = "missing capability"; alloc.Requests.Add(req); return; }
                req.Bundle = b; alloc.Requests.Add(req);
            }
            // Clothing: one service, this turn's consumption (continuity) and the useful reserve (recovery). Route: Cloth
            // where agriculture allows it, Animal Fabric for herding camps.
            var clothingRoute = CultivationEconomy.Enabled(c) && Usable(byGood[CraftGood.Cloth]) && NeolithicProduction.Priority(candidate, CraftGood.Cloth) > 0 ? CraftGood.Cloth : CraftGood.AnimalFabric;
            double clothingUseful = Math.Max(0, NeolithicProduction.Demand(w, candidate, clothingRoute, years, residents, false, false, d.Sources));
            double clothingNow = Math.Min(clothingUseful, Math.Max(0, d.ClothingNeedUnits));
            Request("clothing", ServiceClass.Continuity, clothingRoute, clothingNow);
            Request("clothing reserve", ServiceClass.Recovery, clothingRoute, Math.Max(0, clothingUseful - clothingNow));
            // Fabric this turn's clothing consumption will use (beyond the Cloth held) is not free for tents.
            double consumption = residents * n.FabricConsumptionPerPersonTurn * years / n.ReferenceTurnYears;
            freeFabric = Math.Max(0, freeFabric - Math.Max(0, consumption - Math.Max(0, c.Inventory.Cloth - c.TradeExport(CraftGood.Cloth))));
            // Shelter: unhoused people, replacement of this turn's decay first. Route: fixed Housing for farming camps,
            // tents for herding ones; one route claims the whole shelter service.
            double endDays = w.Days + years * cfg.DaysPerYear;
            double gapPeople = NeolithicProduction.HousingGapPeople(w, candidate, residents, endDays);
            double decayed = Math.Max(0, NeolithicProduction.HousingCapacity(w, c, w.Days).Total - NeolithicProduction.HousingCapacity(w, candidate, endDays).Total);
            double replacementPeople = Math.Min(gapPeople, decayed);
            bool fixedRoute = CultivationEconomy.Enabled(c) && Usable(byGood[CraftGood.Housing]) && NeolithicProduction.Priority(candidate, CraftGood.Housing) > 0;
            CraftGood shelterGood = fixedRoute ? CraftGood.Housing : CraftGood.LightHousing;
            double peoplePerUnit = fixedRoute ? n.HousingPeoplePerUnit : n.LightHousingPeoplePerUnit;
            Request("shelter replacement", ServiceClass.Continuity, shelterGood, replacementPeople / peoplePerUnit);
            Request("shelter deficit", ServiceClass.Recovery, shelterGood, (gapPeople - replacementPeople) / peoplePerUnit);
            // Field and herd equipment for the work staffed (continuity of retained assets).
            Request("farm tools", ServiceClass.Continuity, CraftGood.FarmTools, Math.Max(0, NeolithicProduction.Demand(w, candidate, CraftGood.FarmTools, years, residents, false, false, d.Sources)));
            Request("herding tools", ServiceClass.Continuity, CraftGood.HerdingTools, Math.Max(0, NeolithicProduction.Demand(w, candidate, CraftGood.HerdingTools, years, residents, false, false, d.Sources)));
            // Storage: the Pots the food reserve needs beyond natural storage.
            Request("storage", ServiceClass.Recovery, CraftGood.Pots, Math.Max(0, NeolithicProduction.PotDemand(w, c, years, residents)));

            // ---- Toolmaking: the route with the least labour per tool output ----
            Bundle toolmaking = null; CraftGood toolGood = CraftGood.HandTools; double outputsPerTool = ToolCoverageBudget.OutputsPerHandTool;
            foreach (var (good, outputs) in new[] { (CraftGood.Tools, ToolCoverageBudget.OutputsPerTool), (CraftGood.HandTools, ToolCoverageBudget.OutputsPerHandTool) })
            {
                if (!Usable(byGood[good])) continue;
                var b = Craft(good); if (b == null) continue;
                double years1 = b.RoleYears.Values.Sum() / outputs;
                if (toolmaking == null || years1 < toolmaking.RoleYears.Values.Sum() / outputsPerTool) { toolmaking = b; toolGood = good; outputsPerTool = outputs; }
            }
            double coverage = Math.Max(0, c.Inventory.HandTools) * ToolCoverageBudget.OutputsPerHandTool + Math.Max(0, c.Inventory.Tools) * ToolCoverageBudget.OutputsPerTool;

            // ---- Resources ----
            double Inventory(MaterialId m) => Math.Max(0, MaterialEconomy.Inventory(c, m));
            double Accessible(MaterialId m) => d.Accessible.TryGetValue(m, out double a) ? Math.Max(0, a) : 0;
            string ExtractionRole(MaterialId m)
            {
                string role = MaterialEconomy.Specialist(m);
                return c.People.HasTech(EconomicIntents.RequiredTechnology(role)) ? role : WorkforceRoles.Gatherer;
            }
            double ExtractionYears(MaterialId m, double amount)
            {
                if (!(amount > Epsilon)) return 0;
                double rate = n.SpecialistMaterialExtractionRate > 0 ? SpecialistProductivity.ExtractionRate(w, c, m, ExtractionRole(m)) * perYear : 0;
                return rate > 0 ? amount / rate : double.PositiveInfinity;
            }

            // Totals for a set of grants: role worker-years (extraction and gathering included), and the binding check.
            string Binding(Func<Request, double> grant)
            {
                var total = new Bundle();
                foreach (var q in alloc.Requests) if (q.Bundle != null) total.Add(q.Bundle, grant(q));
                // Tool service beyond start coverage is made, at its own labour and stone/wood.
                double shortfall = Math.Max(0, total.ToolOutputs - coverage);
                if (shortfall > Epsilon) { if (toolmaking == null) return "equipment"; total.Add(toolmaking, shortfall / outputsPerTool); }
                // Construction material for Housing Material: Wood first, then Clay.
                double wood = total.Wood, clay = total.Clay;
                double woodRoom = Inventory(MaterialId.Wood) + Accessible(MaterialId.Wood);
                double constructionWood = Math.Min(total.Construction, Math.Max(0, woodRoom - wood));
                wood += constructionWood; clay += total.Construction - constructionWood;
                var raw = new Dictionary<MaterialId, double> { [MaterialId.Stone] = total.Stone, [MaterialId.Clay] = clay, [MaterialId.Wood] = wood };
                double persons = 0, gathererYears = 0;
                foreach (var m in MaterialEconomy.Materials)
                {
                    double extract = Math.Max(0, raw[m] - Inventory(m));
                    if (extract > Accessible(m) + Epsilon) return "material " + m;
                    double y = ExtractionYears(m, extract);
                    if (double.IsInfinity(y)) return "material " + m;
                    if (ExtractionRole(m) == WorkforceRoles.Gatherer) gathererYears += y; else persons += Math.Ceiling(y / years - Epsilon);
                }
                foreach (var role in total.RoleYears) persons += role.Value / years;
                // Clothing feedstock is funded before the reserve refill: the larger envelope bounds it.
                if (total.Plant > Math.Max(d.PlantEnvelope, d.PlantEnvelopeClothing) + Epsilon) return "plant feedstock";
                if (total.Animal > Math.Max(d.AnimalEnvelope, d.AnimalEnvelopeClothing) + Epsilon) return "animal feedstock";
                if (d.HusbandryEnabled && total.Plant + total.Animal > Math.Max(d.FoodRoomEnvelope, d.FoodRoomEnvelopeClothing) + Epsilon) return "food room";
                if (total.Fabric > Math.Max(0, c.Inventory.AnimalFabric) + Epsilon) return "fabric";
                if (total.Material > Math.Max(0, c.Inventory.HousingMaterial) + Epsilon) return "housing material";
                double feedstockGatherers = gatherRate > 0 ? total.Plant / gatherRate : 0;
                persons += gathererYears / years + feedstockGatherers;
                return persons > budget + Epsilon ? "labor" : null;
            }

            // ---- Elder arbitration: classes in order, an equal fraction within a class, freezing saturated inputs ----
            foreach (ServiceClass cls in new[] { ServiceClass.Continuity, ServiceClass.Recovery })
            {
                var active = alloc.Requests.Where(q => q.Class == cls && q.Bundle != null).ToList();
                for (int pass = 0; pass < 8 && active.Count > 0; pass++)
                {
                    double Grant(Request q, double f) => active.Contains(q) ? q.Units * f : q.Granted;
                    if (Binding(q => Grant(q, 1)) == null) { foreach (var q in active) q.Granted = q.Units; break; }
                    double lo = 0, hi = 1;
                    for (int i = 0; i < 40; i++) { double mid = (lo + hi) / 2; if (Binding(q => Grant(q, mid)) == null) lo = mid; else hi = mid; }
                    string binding = Binding(q => Grant(q, hi)) ?? "labor";
                    foreach (var q in active) q.Granted = q.Units * lo;
                    // Labour and equipment bind every request alike; an input binds only the requests that use it.
                    if (binding == "labor" || binding == "equipment") { foreach (var q in active) q.Unmet = binding == "labor" ? "labor rationing" : "equipment shortage"; break; }
                    var users = active.Where(q => Uses(q.Bundle, binding)).ToList();
                    if (users.Count == 0) users = active.ToList();
                    foreach (var q in users) { q.Unmet = binding.StartsWith("material") ? "material shortage" : binding; active.Remove(q); }
                }
            }

            // ---- Integer staffing, compiled per role after aggregation; at most one repair pass ----
            for (int repair = 0; repair < 2; repair++)
            {
                var total = new Bundle();
                foreach (var q in alloc.Requests) if (q.Bundle != null) total.Add(q.Bundle, q.Granted);
                double shortfall = Math.Max(0, total.ToolOutputs - coverage);
                double toolsMade = shortfall > Epsilon && toolmaking != null ? shortfall / outputsPerTool : 0;
                if (toolsMade > 0) total.Add(toolmaking, toolsMade);
                double wood = total.Wood, clay = total.Clay;
                double constructionWood = Math.Min(total.Construction, Math.Max(0, Inventory(MaterialId.Wood) + Accessible(MaterialId.Wood) - wood));
                wood += constructionWood; clay += total.Construction - constructionWood;
                var raw = new Dictionary<MaterialId, double> { [MaterialId.Stone] = total.Stone, [MaterialId.Clay] = clay, [MaterialId.Wood] = wood };
                var staff = new Dictionary<string, int>(); double materialGatherers = 0;
                foreach (var role in total.RoleYears.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    int people = (int)Math.Ceiling(role.Value / years - Epsilon);
                    if (people > 0) staff[role.Key] = (staff.TryGetValue(role.Key, out int s) ? s : 0) + people;
                }
                foreach (var m in MaterialEconomy.Materials)
                {
                    double y = ExtractionYears(m, Math.Max(0, raw[m] - Inventory(m)));
                    if (!(y > 0) || double.IsInfinity(y)) continue;
                    if (ExtractionRole(m) == WorkforceRoles.Gatherer) materialGatherers += y / years;
                    else { int people = (int)Math.Ceiling(y / years - Epsilon); staff[ExtractionRole(m)] = (staff.TryGetValue(ExtractionRole(m), out int s) ? s : 0) + people; }
                }
                double feedstockGatherers = gatherRate > 0 ? total.Plant / gatherRate : 0;
                double needed = staff.Values.Sum() + materialGatherers + feedstockGatherers;
                if (needed <= budget + Epsilon || repair == 1)
                {
                    foreach (var p in staff) alloc.Staff[p.Key] = p.Value;
                    alloc.MaterialGatherers = materialGatherers; alloc.FeedstockGatherers = feedstockGatherers; alloc.ToolsMade = toolsMade;
                    foreach (var p in total.Outputs) alloc.Ceilings[p.Key] = p.Value;
                    if (toolsMade > 0) alloc.Ceilings[toolGood] = (alloc.Ceilings.TryGetValue(toolGood, out double t) ? t : 0) + toolsMade;
                    // A final over-budget (rounding after the repair) drops the lowest-class work in stable order.
                    if (needed > budget + Epsilon) TrimToBudget(alloc, budget, needed);
                    break;
                }
                // Repair: rounding cost `needed - budget` persons; scale the lowest classes down by that share and recompile.
                alloc.RepairPasses++;
                double excess = needed - budget;
                foreach (var cls in new[] { ServiceClass.Recovery, ServiceClass.Continuity })
                {
                    var group = alloc.Requests.Where(q => q.Class == cls && q.Granted > Epsilon).ToList();
                    double groupPersons = group.Sum(q => q.Granted * q.Bundle.RoleYears.Values.Sum() / years);
                    if (!(groupPersons > Epsilon)) continue;
                    double cut = Math.Min(1, (excess + 1) / groupPersons);
                    foreach (var q in group) { q.Granted *= 1 - cut; q.Unmet = "integer staffing"; }
                    excess -= groupPersons * cut;
                    if (excess <= 0) break;
                }
            }

            // ---- Compile the intent ----
            var intent = e;
            intent.Labor = new EconomicLaborPlan();
            if (herders > 0) intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Herder, Population = herders });
            if (farmers > 0) intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.Farmer, Population = farmers });
            foreach (var p in alloc.Staff.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var existing = intent.Assignments.FirstOrDefault(a => a.Role == p.Key);
                if (existing != null) existing.Population += p.Value; else intent.Assignments.Add(new PopulationAssignment { Role = p.Key, Population = p.Value });
            }
            intent.Assignments = intent.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).ToList();
            int nonGatherers = intent.Assignments.Sum(a => a.Population);
            intent.Labor.MaterialGatherers = Math.Min(Math.Max(0, residents - nonGatherers), alloc.MaterialGatherers);
            intent.Labor.EquipmentTarget = Math.Min(residents, alloc.Staff.Where(p => !WorkforceRoles.Extractors.Contains(p.Key)).Sum(p => p.Value));
            intent.Labor.ExpansionFarmers = 0;
            // Trade's labour price (Addendum A §A2), lagged like the Value planner's: the value of a spare resident's
            // gathering. The marginal-bundle price under binding labour is a later slice.
            if (cfg.TradeEnabled)
            {
                double w0 = Math.Max(0, ExpansionValuation.FoodValue(cfg, c)) * cfg.GatherPerPerson * perYear;
                if (w0 > 0 && !double.IsInfinity(w0)) { intent.Labor.LabourPriceSet = true; intent.Labor.LabourPrice = w0; }
            }
            // Production order: toolmaking, then continuity and recovery goods with intermediates before their consumers.
            var order = new List<CraftGood> { CraftGood.Tools, CraftGood.HandTools };
            foreach (var q in alloc.Requests.OrderBy(q => q.Class))
            {
                if (q.Bundle == null) continue;
                foreach (var g in new[] { CraftGood.AnimalFabric, CraftGood.HousingMaterial, q.Good })
                    if (q.Bundle.Outputs.ContainsKey(g) && !order.Contains(g)) order.Add(g);
            }
            foreach (var r in recipes) if (!order.Contains(r.Good)) order.Add(r.Good);
            foreach (var r in recipes) if (!alloc.Ceilings.ContainsKey(r.Good)) alloc.Ceilings[r.Good] = 0;
            production = new ProductionOrder(order.ToArray(), alloc.Ceilings);
            return intent;
        }

        private static bool Uses(Bundle b, string binding)
        {
            if (b == null) return false;
            switch (binding)
            {
                case "plant feedstock": return b.Plant > 0;
                case "animal feedstock": return b.Animal > 0;
                case "food room": return b.Plant + b.Animal > 0;
                case "fabric": return b.Fabric > 0;
                case "housing material": return b.Material > 0;
                case "material Stone": return b.Stone > 0;
                case "material Clay": return b.Clay > 0 || b.Construction > 0;
                case "material Wood": return b.Wood > 0 || b.Construction > 0;
                default: return true;
            }
        }

        // Removes the lowest-class granted work, last request first, until the compiled staffing fits.
        private static void TrimToBudget(Allocation a, double budget, double needed)
        {
            foreach (var q in a.Requests.Where(q => q.Granted > Epsilon).OrderByDescending(q => q.Class).ThenByDescending(q => a.Requests.IndexOf(q)))
            {
                if (needed <= budget + Epsilon) break;
                foreach (var role in q.Bundle.RoleYears)
                    if (a.Staff.TryGetValue(role.Key, out int s) && s > 0) { a.Staff[role.Key] = s - 1; needed -= 1; if (a.Staff[role.Key] == 0) a.Staff.Remove(role.Key); }
                foreach (var g in q.Bundle.Outputs) if (a.Ceilings.ContainsKey(g.Key)) a.Ceilings[g.Key] = Math.Max(0, a.Ceilings[g.Key] - g.Value * q.Granted);
                q.Granted = 0; q.Unmet = "integer staffing";
            }
        }
    }
}
