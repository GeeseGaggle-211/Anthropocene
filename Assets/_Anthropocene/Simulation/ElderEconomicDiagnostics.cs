using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Reporting data only. Primitive, bounded trees make fork copies independent of mutable turn records.
    [Serializable]
    public sealed class ElderEconomicDiagnostic
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion = CurrentSchemaVersion, CampId, SocietyId, DecisionTurn;
        public string TransitionId, TrajectoryId, ForkId, BranchId, ParentTransitionId;
        public double Years;
        public Dictionary<string, object> Domains = new Dictionary<string, object>(StringComparer.Ordinal);
        public Dictionary<string, string> Units = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> Unavailable = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, object> Conservation = new Dictionary<string, object>(StringComparer.Ordinal);
        public List<Dictionary<string, object>> Goods = new List<Dictionary<string, object>>(), Materials = new List<Dictionary<string, object>>(),
            Trades = new List<Dictionary<string, object>>(), Requests = new List<Dictionary<string, object>>(), Transfers = new List<Dictionary<string, object>>(),
            Herds = new List<Dictionary<string, object>>();
        public List<Dictionary<string, object>> Fields = new List<Dictionary<string, object>>();
        public ElderEconomicDiagnostic Copy() => new ElderEconomicDiagnostic
        {
            SchemaVersion = SchemaVersion, CampId = CampId, SocietyId = SocietyId, DecisionTurn = DecisionTurn,
            TransitionId = TransitionId, TrajectoryId = TrajectoryId, ForkId = ForkId, BranchId = BranchId,
            ParentTransitionId = ParentTransitionId, Years = Years,
            Domains = (Dictionary<string, object>)Clone(Domains), Conservation = (Dictionary<string, object>)Clone(Conservation),
            Units = new Dictionary<string, string>(Units), Unavailable = new Dictionary<string, string>(Unavailable),
            Goods = CopyRows(Goods), Materials = CopyRows(Materials), Trades = CopyRows(Trades), Requests = CopyRows(Requests),
            Transfers = CopyRows(Transfers), Herds = CopyRows(Herds), Fields = CopyRows(Fields)
        };
        static List<Dictionary<string, object>> CopyRows(List<Dictionary<string, object>> rows) => rows.Select(r => (Dictionary<string, object>)Clone(r)).ToList();
        static object Clone(object value)
        {
            if (value is Dictionary<string, object> d) return d.ToDictionary(p => p.Key, p => Clone(p.Value), StringComparer.Ordinal);
            if (value is List<Dictionary<string, object>> rows) return CopyRows(rows);
            if (value is string[] strings) return (string[])strings.Clone();
            return value;
        }
        internal void Missing(Dictionary<string, object> row, string key, string path, string reason)
        { row[key] = null; Unavailable[FieldPath(path) + "." + key] = reason; }
        static string FieldPath(string path) => new[] { "population", "cultivation", "herding", "manufacturing", "trade" }.Contains(path.Split('.')[0]) ? "domains." + path : path;
        internal void Put(Dictionary<string, object> row, string key, double value, string path, string unit)
        {
            Units[FieldPath(path) + "." + key] = unit;
            if (double.IsNaN(value)) throw new InvalidOperationException("NaN economic diagnostic " + path + "." + key);
            if (double.IsInfinity(value))
            { row[key] = 0.0; row[key + "BoundState"] = value > 0 ? "positive-unbounded" : "negative-unbounded"; }
            else row[key] = value;
        }
    }

    internal sealed class EconomicBoundary
    {
        internal int Society, Residents, Parties;
        internal double Food;
        internal double PlantFood, AnimalFood, AquaticFood;
        internal bool Cultivation, Herding, TradeEligible;
        internal Dictionary<CraftGood, double> Goods;
        internal Dictionary<MaterialId, double> Materials;
        internal Dictionary<string, object> PartyPopulations = new Dictionary<string, object>(StringComparer.Ordinal);
        internal static Dictionary<int, EconomicBoundary> Capture(WorldState world, bool stocks = true)
        {
            var result = world.Camps.ToDictionary(c => c.Id, c => new EconomicBoundary { Society = c.SocietyId, Residents = c.Population, Food = c.Food,
                PlantFood = c.PlantFood, AnimalFood = c.AnimalFood, AquaticFood = c.AquaticFood,
                Cultivation = stocks && CultivationEconomy.Enabled(c), Herding = stocks && CultivationEconomy.HusbandryEnabled(c), TradeEligible = stocks && Trade.Eligible(world, c),
                Goods = stocks ? Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().ToDictionary(g => g, g => NeolithicProduction.Stock(c, g)) : null,
                Materials = stocks ? new Dictionary<MaterialId, double> { [MaterialId.Stone] = c.Inventory.Stone, [MaterialId.Clay] = c.Inventory.Clay, [MaterialId.Wood] = c.Inventory.Wood } : null });
            foreach (var h in world.Hunters.OrderBy(h => h.Id))
                if (result.TryGetValue(h.SourceCamp, out var camp))
                { camp.Parties += h.Population; camp.PartyPopulations[h.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = h.Population; }
            return result;
        }
    }

    internal static class ElderEconomicCapture
    {
        static Dictionary<string, object> Row() => new Dictionary<string, object>(StringComparer.Ordinal);
        static ILookup<int, T> Index<T>(IEnumerable<T> source, Func<T, int> key) => (source ?? Enumerable.Empty<T>()).ToLookup(key);
        internal static Dictionary<int, ElderEconomicDiagnostic> Build(WorldState world, PreparedTurn prepared, TurnRecord record,
            ResolutionMetrics metrics, Dictionary<int, EconomicBoundary> opening, string trajectory, string fork, string branch, string parent,
            Dictionary<int, string> parentTransitions = null)
        {
            // Each authoritative list is indexed exactly once per turn. No world/tile/history scan per village.
            var demographics = record.Demographics.ToDictionary(d => d.Camp);
            var production = Index(record.Production, p => p.Camp); var material = Index(record.MaterialTransfers, p => p.Camp);
            var food = Index(record.Transfers, p => p.Entity); var herds = Index(record.ManagedHerds, p => p.Camp);
            var trade = new Dictionary<int, List<Tuple<int, TradeRecord>>>();
            for (int i = 0; i < record.Trades.Count; i++)
                foreach (int id in new[] { record.Trades[i].Requester, record.Trades[i].Responder })
                { if (!trade.TryGetValue(id, out var list)) trade[id] = list = new List<Tuple<int, TradeRecord>>(); list.Add(Tuple.Create(i, record.Trades[i])); }
            var closing = EconomicBoundary.Capture(world);
            var committedHerds = world.Herds.ToDictionary(h => h.Id);
            var migrationByCamp = new Dictionary<int, List<MigrationReport>>();
            foreach (var flow in record.Migrations ?? new List<MigrationReport>()) foreach (int endpoint in new[] { flow.SourceCamp, flow.DestinationCamp })
            { if (!migrationByCamp.TryGetValue(endpoint, out var flows)) migrationByCamp[endpoint] = flows = new List<MigrationReport>(); flows.Add(flow); }
            var fissionByCamp = Index(record.Fissions, f => f.ParentCamp);
            var transfersByCamp = new Dictionary<int, List<Dictionary<string, object>>>();
            foreach (var witness in metrics.EconomicTransfers) foreach (int endpoint in new[] { (int)witness["sourceCamp"], (int)witness["destinationCamp"] }.Distinct())
            { if (!transfersByCamp.TryGetValue(endpoint, out var flows)) transfersByCamp[endpoint] = flows = new List<Dictionary<string, object>>(); flows.Add(witness); }
            var logisticsByCamp = Index(record.Logistics, l => l.Camp);
            var result = new Dictionary<int, ElderEconomicDiagnostic>();
            double years = record.Duration / world.Config.DaysPerYear, intervalYears = world.Config.BaseDays / world.Config.DaysPerYear;
            foreach (var pair in opening.OrderBy(p => p.Key))
            {
                int id = pair.Key; var before = pair.Value; closing.TryGetValue(id, out var after); demographics.TryGetValue(id, out var demo);
                string rowParent = parentTransitions == null ? parent : parentTransitions.TryGetValue(id, out var priorTransition) ? priorTransition : null;
                var d = new ElderEconomicDiagnostic { CampId = id, SocietyId = before.Society, DecisionTurn = record.Turn,
                    TrajectoryId = trajectory, TransitionId = trajectory + "/" + record.Turn + "/" + id,
                    ForkId = fork, BranchId = branch, ParentTransitionId = rowParent, Years = years };
                result[id] = d;
                if (transfersByCamp.TryGetValue(id, out var witnesses)) d.Transfers.AddRange(witnesses.Select(w => new Dictionary<string, object>(w)));
                foreach (var supply in logisticsByCamp[id]) d.Transfers.Add(new Dictionary<string, object>
                { ["kind"] = "hunter-logistics", ["phase"] = "movement-and-economy", ["hunterId"] = supply.Hunter,
                    ["provisionsLoaded"] = supply.ProvisionsLoaded, ["deposited"] = supply.Deposited, ["inventoryLoss"] = supply.InventoryLoss,
                    ["overflow"] = supply.Overflow, ["harvestOverflow"] = supply.HarvestOverflow, ["endingCarriedFood"] = supply.EndingCarriedFood });
                if (fork == null) d.Unavailable["forkId"] = "root-transition-without-fork";
                if (branch == null) d.Unavailable["branchId"] = "root-transition-without-branch";
                if (rowParent == null) d.Unavailable["parentTransitionId"] = "no-preceding-fork-transition";
                if (migrationByCamp.TryGetValue(id, out var migrations)) foreach (var flow in migrations)
                    d.Transfers.Add(new Dictionary<string, object> { ["kind"] = "migration", ["phase"] = "close-after-demography", ["sourceCamp"] = flow.SourceCamp,
                        ["destinationCamp"] = flow.DestinationCamp, ["people"] = flow.Migrants, ["plantFood"] = flow.Plant, ["animalFood"] = flow.Animal,
                        ["aquaticFood"] = flow.Aquatic, ["carriedFood"] = flow.Carried });
                foreach (var flow in fissionByCamp[id])
                    d.Transfers.Add(new Dictionary<string, object> { ["kind"] = "fission", ["phase"] = "close-after-demography", ["sourceCamp"] = flow.ParentCamp,
                        ["destinationCamp"] = flow.DaughterCamp, ["people"] = flow.Daughter, ["animals"] = flow.AnimalsMoved });
                var pop = Row(); d.Domains["population"] = pop;
                void P(string key, double v, string unit = "people") => d.Put(pop, key, v, "population", unit);
                P("openingResidents", before.Residents); P("openingParties", before.Parties); P("openingLinked", before.Residents + before.Parties);
                P("endingResidents", after?.Residents ?? 0); P("endingParties", after?.Parties ?? 0); P("endingLinked", (after?.Residents ?? 0) + (after?.Parties ?? 0));
                P("decisionOpeningFood", before.Food, "food"); P("committedEndingFood", after?.Food ?? 0, "food");
                P("openingPlantFood", before.PlantFood, "food"); P("openingAnimalFood", before.AnimalFood, "food"); P("openingAquaticFood", before.AquaticFood, "food");
                P("endingPlantFood", after?.PlantFood ?? 0, "food"); P("endingAnimalFood", after?.AnimalFood ?? 0, "food"); P("endingAquaticFood", after?.AquaticFood ?? 0, "food");
                P("dispatchedPeople", d.Transfers.Where(t => (string)t["kind"] == "dispatch").Sum(t => (int)t["people"]));
                P("reintegratedPeople", d.Transfers.Where(t => (string)t["kind"] == "reintegration").Sum(t => (int)t["people"]));
                pop["openingPartyPopulations"] = before.PartyPopulations;
                foreach (var phase in new[] { "beforeDemography", "afterEconomy", "afterReintegration" })
                {
                    if (metrics.EconomicBoundaries.TryGetValue(phase, out var bound) && bound.TryGetValue(id, out var b))
                    { P(phase + "Residents", b.Residents); P(phase + "Parties", b.Parties); pop[phase + "PartyPopulations"] = b.PartyPopulations; }
                    else { d.Missing(pop, phase + "Residents", "population", "camp-absent-at-boundary"); d.Missing(pop, phase + "Parties", "population", "camp-absent-at-boundary"); }
                }
                if (demo != null)
                {
                    P("reportStartingLinked", demo.StartingPopulation); P("reportEndingLinked", demo.EndingPopulation);
                    P("grossBirths", demo.GrossBirths); P("grossDeaths", demo.GrossDeaths); P("splitOff", demo.SplitOff); P("emigrated", demo.Emigrated); P("immigrated", demo.Immigrated);
                    P("personYears", demo.PersonYears, "person-years"); P("qol", demo.QoL, "qol"); P("nutrition", demo.Nutrition, "fraction");
                    P("clothing", demo.ClothingCoverage, "fraction"); P("housing", demo.HousingCoverage, "fraction");
                    P("foodRequired", demo.FoodRequired, "food"); P("foodProduced", demo.FoodProduced, "food"); P("foodConsumed", demo.FoodConsumed, "food");
                    P("openingReserves", demo.InitialReserves, "food"); P("endingReserves", demo.FinalReserves, "food");
                    P("manufacturingFood", demo.ManufacturingFood, "food"); P("growthFood", demo.GrowthFoodAccepted, "food");
                    P("exposureDeaths", demo.ExposureDeaths); P("crowdingDeaths", demo.CrowdingDeaths); P("diseaseDeaths", demo.DiseaseDeaths); P("starvationDeaths", demo.StarvationDeaths);
                    pop["limitingFactor"] = demo.LimitingFactor;
                    d.Conservation["populationResidual"] = (after?.Residents ?? 0) + (after?.Parties ?? 0) - demo.EndingPopulation + demo.SplitOff + demo.Emigrated - demo.Immigrated;
                    d.Conservation["grossPopulationResidual"] = demo.EndingPopulation - demo.StartingPopulation - demo.GrossBirths + demo.GrossDeaths;
                    d.Conservation["integerCarryCorrection"] = null; d.Unavailable["conservation.integerCarryCorrection"] = "integer-carry-not-recorded-as-separate-linked-event";
                }
                else d.Missing(pop, "reportStartingLinked", "population", "no-demographic-report");
                foreach (var kind in new[] { "foodResidual", "materialResidual", "goodsResidual", "fieldResidual" })
                    d.Missing(d.Conservation, kind, "conservation", "complete-transfer-consumption-and-capital-witnesses-not-yet-recorded");
                prepared.ElderDecisions.TryGetValue(id, out var decision);
                var allocation = decision?.Allocation; if (allocation == null) prepared.DemandAllocations.TryGetValue(id, out allocation);
                var cultivation = Row(); d.Domains["cultivation"] = cultivation;
                d.Put(cultivation, "foodDelivered", food[id].Where(t => t.Kind == "Cultivation").Sum(t => t.Food), "cultivation", "food");
                if (metrics.CraftFieldWork != null && metrics.CraftFieldWork.TryGetValue(id, out var work))
                {
                    foreach (var p in new Dictionary<string, double> { ["assignedFarmerYears"] = work.AssignedFarmerWorkerYears,
                        ["maintenanceWorkerYears"] = work.MaintenanceWorkerYears, ["expansionWorkerYears"] = work.ExpansionWorkUsedWorkerYears,
                        ["farmToolCoveredWorkerYears"] = work.FarmToolCoveredWorkerYears, ["gatheringBridgeWorkerYears"] = work.GatheringBridgeCreditWorkerYears,
                        ["provisionalGatheringBridgeWorkerYears"] = work.ProvisionalGatheringBridgeCreditWorkerYears }) d.Put(cultivation, p.Key, p.Value, "cultivation", "worker-years");
                    double denominator = work.AssignedFarmerWorkerYears;
                    cultivation["foodPerFarmerYearDenominator"] = denominator;
                    if (denominator > 0) cultivation["foodPerFarmerYear"] = (double)cultivation["foodDelivered"] / denominator;
                    else d.Missing(cultivation, "foodPerFarmerYear", "cultivation", "zero-denominator");
                }
                else d.Missing(cultivation, "assignedFarmerYears", "cultivation", "no-active-field-work");
                if (metrics.EconomicFields.TryGetValue(id, out var fieldRows))
                {
                    d.Fields = fieldRows.Select(r => new Dictionary<string, object>(r)).ToList();
                    foreach (var key in new[] { "openingCapital", "meanCapital", "endingCapital" })
                        d.Put(cultivation, key, fieldRows.Sum(r => (double)r[key]), "cultivation", "field-capital");
                }
                else foreach (string key in new[] { "openingCapital", "meanCapital", "endingCapital" })
                    d.Missing(cultivation, key, "cultivation", "field-capital-horizon-snapshot-not-yet-retained");
                foreach (string key in new[] { "capitalAdded", "capitalLost" })
                    d.Missing(cultivation, key, "cultivation", "construction-versus-decay-components-not-retained");
                var herdRows = herds[id].OrderBy(h => h.Herd).ToArray(); var herding = Row(); d.Domains["herding"] = herding;
                herding["capable"] = before.Herding; cultivation["capable"] = before.Cultivation;
                foreach (var h in herdRows)
                    d.Herds.Add(new Dictionary<string, object> { ["herdId"] = h.Herd, ["startingAnimals"] = h.StartingAnimals, ["survivingAnimals"] = h.SurvivingAnimals,
                        ["newbornAnimals"] = h.NewbornAnimals, ["starvationDeaths"] = h.StarvationDeaths, ["slaughtered"] = h.Slaughtered,
                        ["foodNutrition"] = h.FoodNutrition, ["waterNutrition"] = h.WaterNutrition, ["nutrition"] = h.Nutrition, ["managementFulfilment"] = h.ManagementFulfillment,
                        ["potentialFood"] = h.PotentialFood, ["acceptedFood"] = h.AcceptedFood, ["slaughterFood"] = h.SlaughterFood,
                        ["coveredHerderYears"] = h.CoveredHerderYears, ["herdingToolsConsumed"] = h.HerdingToolsConsumed });
                foreach (var row in d.Herds)
                    row["committedEndingAnimals"] = committedHerds.TryGetValue((int)row["herdId"], out var owned) ? owned.Animals : 0;
                d.Conservation["herdResidual"] = herdRows.Sum(h => h.StartingAnimals - h.StarvationDeaths + h.NewbornAnimals - h.Slaughtered)
                    - fissionByCamp[id].Sum(f => f.AnimalsMoved) - d.Herds.Sum(h => (int)h["committedEndingAnimals"]);
                d.Conservation["herdOpeningBoundary"] = "managed-herd-report-start-after-resource-allocation";
                d.Conservation["herdScope"] = "reported-herd-ids-only-newly-acquired-groups-excluded";
                foreach (var p in new Dictionary<string, double> { ["startingAnimals"] = herdRows.Sum(h => h.StartingAnimals), ["newbornAnimals"] = herdRows.Sum(h => h.NewbornAnimals),
                    ["starvationDeaths"] = herdRows.Sum(h => h.StarvationDeaths), ["slaughtered"] = herdRows.Sum(h => h.Slaughtered),
                    ["coveredHerderYears"] = herdRows.Sum(h => h.CoveredHerderYears), ["potentialFood"] = herdRows.Sum(h => h.PotentialFood), ["acceptedFood"] = herdRows.Sum(h => h.AcceptedFood) })
                    d.Put(herding, p.Key, p.Value, "herding", p.Key.EndsWith("Food") ? "food" : p.Key.EndsWith("Years") ? "worker-years" : "animals");
                double animalDenominator = herdRows.Sum(h => h.StartingAnimals); herding["nutritionDenominator"] = animalDenominator;
                foreach (var p in new Dictionary<string, Func<ManagedHerdReport, double>> { ["foodNutrition"] = h => h.FoodNutrition, ["waterNutrition"] = h => h.WaterNutrition,
                    ["nutrition"] = h => h.Nutrition, ["managementFulfilment"] = h => h.ManagementFulfillment })
                    if (animalDenominator > 0) herding[p.Key] = herdRows.Sum(h => h.StartingAnimals * p.Value(h)) / animalDenominator;
                    else d.Missing(herding, p.Key, "herding", "zero-denominator");
                var manufacturing = Row(); d.Domains["manufacturing"] = manufacturing;
                var funnels = new List<Dictionary<string, object>>(); manufacturing["funnels"] = funnels;
                if (allocation != null)
                {
                    foreach (var p in new Dictionary<string, double> { ["fieldMarginalReturn"] = allocation.FieldMarginalReturn, ["selectedFieldBudget"] = allocation.SelectedFieldBudget, ["finalFieldBudget"] = allocation.FinalFieldBudget,
                        ["selectedDevelopmentUseful"] = allocation.SelectedDevelopmentUseful, ["finalDevelopmentUseful"] = allocation.FinalDevelopmentUseful, ["selectedDevelopmentWork"] = allocation.SelectedDevelopmentWork, ["finalDevelopmentWork"] = allocation.FinalDevelopmentWork })
                        d.Put(cultivation, p.Key, p.Value, "cultivation", p.Key.Contains("Return") ? "food/worker-year" : "planner-native");
                    d.Put(herding, "herdMarginalReturn", allocation.HerdMarginalReturn, "herding", "food/worker-year");
                    herding["selectedHerdBudget"] = allocation.SelectedHerdBudget; herding["finalHerdBudget"] = allocation.FinalHerdBudget; herding["developmentAnimals"] = allocation.HerdDevelopmentAnimals;
                    herding["plannedHerderYears"] = allocation.Herders * years;
                    string[] units = { "food-worker-headcount", "selected-route-clothing-units", "selected-route-shelter-units", "mixed-tool-good-units", "pot-units", "raw-extraction-worker-years" };
                    for (int s = 0; s < 6; s++)
                    {
                        var f = new Dictionary<string, object> { ["metric"] = ElderModel9Contract.GoodsServices[s], ["unit"] = units[s],
                            ["nativeDemand"] = allocation.ServiceRequested[s], ["compilerGrant"] = allocation.ServiceGranted[s], ["active"] = true,
                            ["constraintReasons"] = allocation.Requests.Where(q => ElderModel9Contract.ServiceOf(q.Service) == s && q.Unmet != "").Select(q => q.Unmet).Distinct().OrderBy(x => x).ToArray() };
                        f["actorCappedRequest"] = allocation.Requests.Where(q => ElderModel9Contract.ServiceOf(q.Service) == s).Sum(q => q.Units * (q.RawMaterial.HasValue ? q.ServiceEquivalentPerUnit : 1));
                        foreach (var key in new[] { "executedWork", "physicalReceipt", "finalService" })
                            d.Missing(f, key, "domains.manufacturing.funnels[" + s + "]", "compiler-control-unit-is-not-a-physical-good-or-work-unit");
                        funnels.Add(f);
                    }
                    for (int i = 0; i < allocation.Requests.Count; i++)
                    {
                        var q = allocation.Requests[i];
                        var req = new Dictionary<string, object> { ["requestId"] = q.RequestId ?? (d.TransitionId + "/request/" + i), ["ordinal"] = i,
                            ["service"] = q.Service, ["serviceClass"] = q.Class.ToString(), ["good"] = q.Good?.ToString(), ["role"] = q.Role,
                            ["rawMaterial"] = q.RawMaterial?.ToString(), ["nativeDemand"] = q.DiagnosticNativeUnits, ["actorCappedRequest"] = q.Units,
                            ["compilerGrant"] = q.Granted, ["unmetReason"] = q.Unmet, ["serviceEquivalentPerUnit"] = q.RawMaterial.HasValue ? q.ServiceEquivalentPerUnit : 1 };
                        req["nativeUnit"] = q.RawMaterial.HasValue ? "material-units" : q.Good.HasValue ? q.Good.Value + "-units" : "worker-headcount";
                        req["reservedFabric"] = q.ReservedFabric; req["reservedHousingMaterial"] = q.ReservedMaterial; req["reservedCloth"] = q.ReservedCloth;
                        if (q.Bundle != null)
                        {
                            req["roleYearsPerUnit"] = q.Bundle.RoleYears.ToDictionary(p => p.Key, p => (object)p.Value);
                            req["outputsPerUnit"] = q.Bundle.Outputs.ToDictionary(p => p.Key.ToString(), p => (object)p.Value);
                            req["workerYearsEquivalent"] = q.Bundle.RoleYears.Values.Sum() * q.Granted;
                            req["bundleInputsPerUnit"] = new Dictionary<string, object> { ["stone"] = q.Bundle.Stone, ["clay"] = q.Bundle.Clay, ["wood"] = q.Bundle.Wood,
                                ["plantFood"] = q.Bundle.Plant, ["animalFood"] = q.Bundle.Animal, ["animalFabric"] = q.Bundle.Fabric, ["housingMaterial"] = q.Bundle.Material, ["cloth"] = q.Bundle.ClothStock };
                        }
                        if (!q.DiagnosticNativeUnits.HasValue) d.Unavailable["requests[" + i + "].nativeDemand"] = "request-created-after-service-cap-phase";
                        foreach (string key in new[] { "good", "role", "rawMaterial" }) if (req[key] == null) d.Unavailable["requests[" + i + "]." + key] = "not-applicable-to-request-kind";
                        d.Requests.Add(req);
                    }
                }
                foreach (CraftGood good in Enum.GetValues(typeof(CraftGood)))
                {
                    var receipts = production[id].Where(p => p.Good == good).ToArray();
                    var g = new Dictionary<string, object> { ["good"] = good.ToString(), ["unit"] = "good-units", ["output"] = receipts.Sum(p => p.Output),
                        ["openingStock"] = before.Goods[good], ["endingStock"] = after?.Goods[good] ?? 0,
                        ["stockKind"] = good == CraftGood.Housing ? "legacy-carried-inventory-fixed-housing-excluded" : "camp-inventory",
                        ["workerIntervals"] = receipts.Sum(p => p.WorkerIntervals), ["workerYears"] = receipts.Sum(p => p.WorkerIntervals) * intervalYears,
                        ["equipmentCoveredWorkerYears"] = receipts.Sum(p => p.EquipmentWorkerIntervals) * intervalYears,
                        ["stone"] = receipts.Sum(p => p.Stone), ["clay"] = receipts.Sum(p => p.Clay), ["wood"] = receipts.Sum(p => p.Wood),
                        ["plantFood"] = receipts.Sum(p => p.PlantFood), ["animalFood"] = receipts.Sum(p => p.AnimalFood), ["animalFabric"] = receipts.Sum(p => p.AnimalFabric),
                        ["cloth"] = receipts.Sum(p => p.Cloth), ["housingMaterial"] = receipts.Sum(p => p.HousingMaterial), ["handToolsConsumed"] = receipts.Sum(p => p.HandToolsConsumed), ["toolsConsumed"] = receipts.Sum(p => p.ToolsConsumed) };
                    if (allocation != null) { g["plannedOutputCeiling"] = allocation.Ceilings.TryGetValue(good, out double ceiling) ? ceiling : (object)null;
                        if (g["plannedOutputCeiling"] == null) d.Unavailable["goods[" + d.Goods.Count + "].plannedOutputCeiling"] = "no-explicit-output-ceiling"; }
                    d.Goods.Add(g);
                }
                foreach (MaterialId m in new[] { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood })
                {
                    var receipts = material[id].Where(p => p.Material == m).ToArray(); var requests = allocation?.Requests.Where(q => q.RawMaterial == m).ToArray();
                    var row = new Dictionary<string, object> { ["material"] = m.ToString(), ["unit"] = "material-units", ["received"] = receipts.Sum(p => p.Amount),
                        ["openingStock"] = before.Materials[m], ["endingStock"] = after?.Materials[m] ?? 0,
                        ["workerIntervals"] = receipts.Sum(p => p.WorkerIntervals), ["workerYears"] = receipts.Sum(p => p.WorkerIntervals) * intervalYears };
                    if (requests != null && receipts.All(r => r.Phase == "elder-raw"))
                    { row["requested"] = requests.Sum(q => q.NativeUnits); row["granted"] = requests.Sum(q => q.Granted); }
                    else { d.Missing(row, "requested", "materials[" + d.Materials.Count + "]", "phase-a-authorization-not-retained-in-phase-b-material-request-units");
                        d.Missing(row, "granted", "materials[" + d.Materials.Count + "]", "phase-a-authorization-not-retained-in-phase-b-material-request-units"); }
                    if (requests != null) { row["phaseBRequested"] = requests.Sum(q => q.NativeUnits); row["phaseBGranted"] = requests.Sum(q => q.Granted); row["phaseBReceived"] = receipts.Where(r => r.Phase == "elder-raw").Sum(r => r.Amount); }
                    d.Materials.Add(row);
                }
                var trading = Row(); d.Domains["trade"] = trading;
                trading["eligible"] = before.TradeEligible;
                var transactions = trade.TryGetValue(id, out var ts) ? ts : new List<Tuple<int, TradeRecord>>();
                trading["settledTrades"] = transactions.Count; trading["realisedUtility"] = transactions.Sum(p => p.Item2.Requester == id ? p.Item2.RequesterRealizedUtility : p.Item2.ResponderRealizedUtility);
                foreach (var t in transactions)
                    d.Trades.Add(new Dictionary<string, object> { ["transactionId"] = trajectory + "/" + record.Turn + "/trade/" + t.Item1,
                        ["requester"] = t.Item2.Requester, ["responder"] = t.Item2.Responder, ["wantGood"] = t.Item2.Want.ToString(), ["giveGood"] = t.Item2.Give.ToString(),
                        ["wantQuantity"] = t.Item2.WantQuantity, ["giveQuantity"] = t.Item2.GiveQuantity, ["wantDeliveredFraction"] = t.Item2.Delivered, ["giveDeliveredFraction"] = t.Item2.GiveDelivered,
                        ["wantMadeToOrder"] = t.Item2.WantMadeToOrder, ["giveMadeToOrder"] = t.Item2.GiveMadeToOrder, ["distance"] = t.Item2.Distance,
                        ["requesterPlannedGain"] = t.Item2.RequesterGain, ["responderPlannedGain"] = t.Item2.ResponderGain,
                        ["requesterRealisedUtility"] = t.Item2.RequesterRealizedUtility, ["responderRealisedUtility"] = t.Item2.ResponderRealizedUtility });
                foreach (string key in new[] { "requisitions", "honoured", "countered", "ignored", "escrow", "refund", "storageRejection" })
                    d.Missing(trading, key, "trade", "only-world-handshake-counts-or-final-settlement-recorded");
                foreach (TradeGood good in Enum.GetValues(typeof(TradeGood)))
                {
                    trading["imported" + good] = transactions.Sum(p => p.Item2.Requester == id ? (p.Item2.Want == good ? p.Item2.WantQuantity : 0) : (p.Item2.Give == good ? p.Item2.GiveQuantity : 0));
                    trading["exported" + good] = transactions.Sum(p => p.Item2.Requester == id ? (p.Item2.Give == good ? p.Item2.GiveQuantity : 0) : (p.Item2.Want == good ? p.Item2.WantQuantity : 0));
                }
                Dictionary<string, object> Funnel(Dictionary<string, object> domain, string domainName, string name, string unit, object requested, object granted, object executed)
                {
                    bool active = decision != null && (domainName == "cultivation" ? before.Cultivation : domainName == "herding" ? before.Herding : before.TradeEligible);
                    var f = new Dictionary<string, object> { ["metric"] = name, ["unit"] = unit, ["active"] = active, ["constraintReasons"] = Array.Empty<string>() };
                    var fs = domain.TryGetValue("funnels", out var existing) ? (List<Dictionary<string, object>>)existing : new List<Dictionary<string, object>>();
                    domain["funnels"] = fs; int ordinal = fs.Count; fs.Add(f);
                    string path = "domains." + domainName + ".funnels[" + ordinal + "]";
                    foreach (var p in new Dictionary<string, object> { ["nativeDemand"] = null, ["actorCappedRequest"] = requested, ["compilerGrant"] = granted,
                        ["executedWork"] = executed, ["physicalReceipt"] = null, ["finalService"] = null })
                        if (active && p.Value != null) f[p.Key] = p.Value; else d.Missing(f, p.Key, path, !active ? "inactive-head" : "stage-not-recorded-in-this-control-unit");
                    return f;
                }
                Funnel(cultivation, "cultivation", "retainedFieldOperation", "operation-fraction", decision?.Requested?.RetainedFieldOperation,
                    decision?.Accepted?.RetainedFieldOperation, decision == null ? null : (object)decision.ResolvedRetainedFieldOperation);
                Funnel(cultivation, "cultivation", "fieldDevelopment", "worker-headcount", null,
                    decision == null ? null : (object)decision.AcceptedFieldDevelopmentPeople, decision == null ? null : (object)decision.ResolvedFieldDevelopmentPeople);
                Funnel(herding, "herding", "retainedHerdFraction", "animal-fraction", decision?.Requested?.RetainedHerdFraction,
                    decision?.Accepted?.RetainedHerdFraction, decision?.Realized?.RetainedHerdFraction);
                Funnel(herding, "herding", "herdDevelopment", "worker-headcount", null,
                    decision == null ? null : (object)decision.AcceptedHerdDevelopmentPeople, decision == null ? null : (object)decision.ResolvedHerdDevelopmentPeople);
                var tradeFunnel = Funnel(trading, "trade", "settledTransactions", "transactions", null, null, null);
                tradeFunnel["active"] = true; tradeFunnel["physicalReceipt"] = transactions.Count;
                d.Unavailable.Remove("domains.trade.funnels[0].physicalReceipt");
            }
            return result;
        }
    }
}
