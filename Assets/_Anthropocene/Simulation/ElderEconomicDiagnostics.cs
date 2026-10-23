using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Reporting data only. Primitive, bounded trees make fork copies independent of mutable turn records.
    [Serializable]
    public sealed class ElderEconomicDiagnostic
    {
        public const int CurrentSchemaVersion = 2;
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
        public List<Dictionary<string, object>> Controls = new List<Dictionary<string, object>>();
        public List<Dictionary<string, object>> ConservationRows = new List<Dictionary<string, object>>();
        public List<Dictionary<string, object>> PhysicalEvents = new List<Dictionary<string, object>>();
        public ElderEconomicDiagnostic Copy() => new ElderEconomicDiagnostic
        {
            SchemaVersion = SchemaVersion, CampId = CampId, SocietyId = SocietyId, DecisionTurn = DecisionTurn,
            TransitionId = TransitionId, TrajectoryId = TrajectoryId, ForkId = ForkId, BranchId = BranchId,
            ParentTransitionId = ParentTransitionId, Years = Years,
            Domains = (Dictionary<string, object>)Clone(Domains), Conservation = (Dictionary<string, object>)Clone(Conservation),
            Units = new Dictionary<string, string>(Units), Unavailable = new Dictionary<string, string>(Unavailable),
            Goods = CopyRows(Goods), Materials = CopyRows(Materials), Trades = CopyRows(Trades), Requests = CopyRows(Requests),
            Transfers = CopyRows(Transfers), Herds = CopyRows(Herds), Fields = CopyRows(Fields),
            Controls = CopyRows(Controls), ConservationRows = CopyRows(ConservationRows), PhysicalEvents = CopyRows(PhysicalEvents)
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
        internal double Food, CarriedFood;
        internal double PlantFood, AnimalFood, AquaticFood;
        internal double ClothConsumed, AnimalFabricConsumed;
        internal bool Cultivation, Herding, TradeEligible;
        internal Dictionary<CraftGood, double> Goods;
        internal Dictionary<MaterialId, double> Materials;
        internal Dictionary<int, int> Herds = new Dictionary<int, int>();
        internal Dictionary<int, double> Fields = new Dictionary<int, double>();
        internal Dictionary<string, object> PartyPopulations = new Dictionary<string, object>(StringComparer.Ordinal);
        internal static Dictionary<int, EconomicBoundary> Capture(WorldState world, bool stocks = true,
            IReadOnlyDictionary<int, EconomicBoundary> openingIdentities = null)
        {
            var result = world.Camps.ToDictionary(c => c.Id, c => new EconomicBoundary { Society = c.SocietyId, Residents = c.Population, Food = c.Food,
                PlantFood = c.PlantFood, AnimalFood = c.AnimalFood, AquaticFood = c.AquaticFood,
                ClothConsumed = c.ResolvedClothConsumed, AnimalFabricConsumed = c.ResolvedAnimalFabricConsumed,
                Cultivation = stocks && CultivationEconomy.Enabled(c), Herding = stocks && CultivationEconomy.HusbandryEnabled(c), TradeEligible = stocks && Trade.Eligible(world, c),
                Goods = stocks ? Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().ToDictionary(g => g,
                    g => g == CraftGood.Housing ? NeolithicProduction.FixedHousingAt(world, c, world.Days) : NeolithicProduction.Stock(c, g)) : null,
                Materials = stocks ? new Dictionary<MaterialId, double> { [MaterialId.Stone] = c.Inventory.Stone, [MaterialId.Clay] = c.Inventory.Clay, [MaterialId.Wood] = c.Inventory.Wood } : null });
            foreach (var h in world.Hunters.OrderBy(h => h.Id))
                if (result.TryGetValue(h.SourceCamp, out var camp))
                {
                    camp.Parties += h.Population; camp.CarriedFood += Math.Max(0, h.CarriedFood);
                    camp.PartyPopulations[h.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = h.Population;
                }
            foreach (var camp in result.Values) camp.Food += camp.CarriedFood;
            if (stocks)
            {
                foreach (var herd in world.Herds.Where(h => h.DomesticCamp >= 0).OrderBy(h => h.Id))
                    if (result.TryGetValue(herd.DomesticCamp, out var camp)) camp.Herds[herd.Id] = herd.Animals;
                foreach (var camp in world.Camps.OrderBy(c => c.Id))
                    foreach (int field in CultivationEconomy.Footprint(world, camp))
                        if (world.Tiles[field].CultivationOperator == camp.Id)
                            result[camp.Id].Fields[field] = Math.Max(0, world.Tiles[field].CultivationCapital);
                if (openingIdentities != null)
                    foreach (var pair in openingIdentities)
                        if (result.TryGetValue(pair.Key, out var camp))
                            foreach (int field in pair.Value.Fields.Keys)
                                if (!camp.Fields.ContainsKey(field)) camp.Fields[field] = 0;
            }
            return result;
        }
    }

    internal static class ElderEconomicCapture
    {
        static Dictionary<string, object> Row() => new Dictionary<string, object>(StringComparer.Ordinal);
        static ILookup<int, T> Index<T>(IEnumerable<T> source, Func<T, int> key) => (source ?? Enumerable.Empty<T>()).ToLookup(key);
        static object CopyValue(object value)
        {
            if (value is Dictionary<string, object> dictionary)
                return dictionary.ToDictionary(p => p.Key, p => CopyValue(p.Value), StringComparer.Ordinal);
            if (value is List<Dictionary<string, object>> rows)
                return rows.Select(row => (Dictionary<string, object>)CopyValue(row)).ToList();
            if (value is string[] strings) return (string[])strings.Clone();
            return value;
        }
        static string InventoryName(CraftGood good)
            => good == CraftGood.Housing ? "HousingUnits"
            : good == CraftGood.LightHousing ? "LightHousingUnits"
            : good.ToString();
        static double TransferInventory(IEnumerable<Dictionary<string, object>> rows, int camp, string item, bool incoming)
        {
            double total = 0;
            foreach (var row in rows.Where(r => (string)r["kind"] == "fission-inventory" &&
                (int)r[incoming ? "destinationCamp" : "sourceCamp"] == camp))
                if (row.TryGetValue("inventory", out var raw) && raw is Dictionary<string, object> inventory &&
                    inventory.TryGetValue(item, out var value)) total += Convert.ToDouble(value);
            return total;
        }
        static double TransferFood(IEnumerable<Dictionary<string, object>> rows, int camp, bool incoming)
        {
            double total = 0;
            foreach (var row in rows)
            {
                string kind = Convert.ToString(row["kind"], System.Globalization.CultureInfo.InvariantCulture);
                if (kind != "migration" && kind != "fission-inventory") continue;
                if ((int)row[incoming ? "destinationCamp" : "sourceCamp"] != camp) continue;
                foreach (string key in new[] { "plantFood", "animalFood", "aquaticFood", "carriedFood" })
                    if (row.TryGetValue(key, out var value)) total += Math.Max(0, Convert.ToDouble(value));
            }
            return total;
        }
        static void Conservation(ElderEconomicDiagnostic diagnostic, string domain, string item, string unit,
            double opening, double production, double tradeIn, double transferIn, double consumption,
            double tradeOut, double transferOut, double discard, double closing)
        {
            double residual = opening + production + tradeIn + transferIn - consumption - tradeOut - transferOut - discard - closing;
            diagnostic.ConservationRows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            { ["domain"] = domain, ["item"] = item, ["unit"] = unit, ["opening"] = opening,
                ["production"] = production, ["tradeIn"] = tradeIn, ["transferIn"] = transferIn,
                ["consumption"] = consumption, ["tradeOut"] = tradeOut, ["transferOut"] = transferOut,
                ["discard"] = discard, ["closing"] = closing, ["residual"] = residual,
                ["complete"] = true, ["unavailableReason"] = null });
        }
        static void Control(ElderEconomicDiagnostic diagnostic, string id, string domain, string identity,
            string unit, string actorHead, object nativeDemand, object actorRequest, object grant,
            object executed, object receipt, object inventoryChange, object finalService,
            DemographicReport outcome, Dictionary<string, object> metadata = null,
            string executedUnit = null, string receiptUnit = null, string inventoryUnit = null,
            string finalServiceUnit = null, string classification = "active", string classificationReason = null,
            Dictionary<string, string> unavailableReasons = null)
        {
            var phases = new Dictionary<string, object>(StringComparer.Ordinal)
            { ["nativeDemand"] = nativeDemand, ["actorCappedRequest"] = actorRequest,
                ["compilerGrant"] = grant, ["executedWorkerOrInputClaim"] = executed,
                ["committedPhysicalReceipt"] = receipt, ["inventoryChange"] = inventoryChange,
                ["consumptionOrFinalService"] = finalService,
                ["endingPopulation"] = outcome == null ? (object)null : outcome.EndingPopulation,
                ["endingQol"] = outcome == null ? (object)null : outcome.QoL };
            var units = phases.Keys.ToDictionary(key => key, key =>
                key == "endingPopulation" ? "people" : key == "endingQol" ? "qol"
                : key == "executedWorkerOrInputClaim" ? executedUnit ?? unit
                : key == "committedPhysicalReceipt" ? receiptUnit ?? unit
                : key == "inventoryChange" ? inventoryUnit ?? receiptUnit ?? unit
                : key == "consumptionOrFinalService" ? finalServiceUnit ?? receiptUnit ?? unit
                : unit, StringComparer.Ordinal);
            var unavailable = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var phase in phases.Where(p => p.Value == null))
                unavailable[phase.Key] = unavailableReasons != null && unavailableReasons.TryGetValue(phase.Key, out var reason) ? reason
                    : phase.Key == "endingPopulation" || phase.Key == "endingQol"
                        ? "no-demographic-outcome-for-source-camp"
                        : "authoritative-phase-not-applicable-or-not-emitted";
            diagnostic.Controls.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            { ["controlId"] = id, ["domain"] = domain, ["identity"] = identity,
                ["nativeUnit"] = unit, ["actorHead"] = actorHead, ["phases"] = phases,
                ["phaseUnits"] = units, ["unavailable"] = unavailable,
                ["classification"] = classification, ["classificationReason"] = classificationReason,
                ["metadata"] = metadata ?? new Dictionary<string, object>(StringComparer.Ordinal) });
        }
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
            var closing = EconomicBoundary.Capture(world, true, opening);
            var tradeLifecycle = metrics.EconomicTradeLifecycle.Select(raw =>
            {
                var row = (Dictionary<string, object>)CopyValue(raw);
                foreach (string key in new[] { "eventId", "requestId", "transactionId" })
                    if (row.TryGetValue(key, out var id) && id is string text) row[key] = trajectory + "/" + text;
                if (row.TryGetValue("legs", out var rawLegs))
                    foreach (var leg in (List<Dictionary<string, object>>)rawLegs)
                        if (leg.TryGetValue("legId", out var id) && id is string text) leg["legId"] = trajectory + "/" + text;
                return row;
            }).ToList();
            var physicalByCamp = Index(metrics.EconomicPhysicalEvents, row => (int)row["campId"]);
            var tradeRowsByCamp = new Dictionary<int, List<Dictionary<string, object>>>();
            var eligibilityByCamp = tradeLifecycle.Where(row => (string)row["recordType"] == "eligibility")
                .ToDictionary(row => (int)row["campId"]);
            var lifecycleEndpoints = new Dictionary<int, HashSet<int>>();
            foreach (var row in tradeLifecycle)
            {
                string type = (string)row["recordType"];
                var endpoints = new HashSet<int>();
                if (type == "eligibility") endpoints.Add((int)row["campId"]);
                else { endpoints.Add((int)row["requester"]); endpoints.Add((int)row["responder"]); }
                foreach (int endpoint in endpoints)
                {
                    if (!tradeRowsByCamp.TryGetValue(endpoint, out var rows)) tradeRowsByCamp[endpoint] = rows = new List<Dictionary<string, object>>();
                    rows.Add(row);
                    if (!lifecycleEndpoints.TryGetValue(endpoint, out var related)) lifecycleEndpoints[endpoint] = related = new HashSet<int>();
                    foreach (int other in endpoints) related.Add(other);
                }
            }
            foreach (var pair in lifecycleEndpoints)
                if (tradeRowsByCamp.TryGetValue(pair.Key, out var rows))
                    foreach (int endpoint in pair.Value.OrderBy(value => value))
                        if (eligibilityByCamp.TryGetValue(endpoint, out var eligibility) && !rows.Contains(eligibility)) rows.Add(eligibility);
            var tradePartTotals = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var transaction in tradeLifecycle.Where(row => (string)row["recordType"] == "transaction"))
                foreach (var leg in (List<Dictionary<string, object>>)transaction["legs"])
                    foreach (var part in (List<Dictionary<string, object>>)leg["parts"])
                    {
                        string item = Convert.ToString(part["item"], System.Globalization.CultureInfo.InvariantCulture);
                        double delivered = Convert.ToDouble(part["deliveredQuantity"]);
                        foreach (var endpoint in new[]
                        {
                            Tuple.Create((int)leg["receiver"], true), Tuple.Create((int)leg["sender"], false)
                        })
                        {
                            string key = endpoint.Item1 + "\u001f" + item + "\u001f" + (endpoint.Item2 ? "in" : "out");
                            tradePartTotals.TryGetValue(key, out double prior); tradePartTotals[key] = prior + delivered;
                            string phaseKey = key + "\u001f" + Convert.ToString(leg["deliveryPhase"], System.Globalization.CultureInfo.InvariantCulture);
                            tradePartTotals.TryGetValue(phaseKey, out prior); tradePartTotals[phaseKey] = prior + delivered;
                        }
                    }
            var migrationByCamp = new Dictionary<int, List<MigrationReport>>();
            foreach (var flow in record.Migrations ?? new List<MigrationReport>()) foreach (int endpoint in new[] { flow.SourceCamp, flow.DestinationCamp })
            { if (!migrationByCamp.TryGetValue(endpoint, out var flows)) migrationByCamp[endpoint] = flows = new List<MigrationReport>(); flows.Add(flow); }
            var fissionByCamp = Index(record.Fissions, f => f.ParentCamp);
            var transfersByCamp = new Dictionary<int, List<Dictionary<string, object>>>();
            foreach (var witness in metrics.EconomicTransfers) foreach (int endpoint in new[] { (int)witness["sourceCamp"], (int)witness["destinationCamp"] }.Distinct())
            { if (!transfersByCamp.TryGetValue(endpoint, out var flows)) transfersByCamp[endpoint] = flows = new List<Dictionary<string, object>>(); flows.Add(witness); }
            metrics.EconomicIndexedPhysicalEvents = metrics.EconomicPhysicalEvents.Count;
            metrics.EconomicIndexedTradeRows = tradeLifecycle.Count;
            metrics.EconomicIndexedTransferRows = metrics.EconomicTransfers.Count;
            metrics.EconomicCapturedCampRows = opening.Count;
            var logisticsByCamp = Index(record.Logistics, l => l.Camp);
            var result = new Dictionary<int, ElderEconomicDiagnostic>();
            double years = record.Duration / world.Config.DaysPerYear, intervalYears = world.Config.BaseDays / world.Config.DaysPerYear;
            foreach (var pair in opening.OrderBy(p => p.Key))
            {
                int id = pair.Key; var before = pair.Value; closing.TryGetValue(id, out var after); demographics.TryGetValue(id, out var demo);
                metrics.EconomicBoundaries.TryGetValue("afterOpeningTrade", out var afterOpeningByCamp);
                metrics.EconomicBoundaries.TryGetValue("afterEconomy", out var afterEconomyByCamp);
                metrics.EconomicBoundaries.TryGetValue("afterFinalTrade", out var afterFinalTradeByCamp);
                EconomicBoundary afterOpening = null, afterEconomy = null, afterFinalTrade = null;
                afterOpeningByCamp?.TryGetValue(id, out afterOpening);
                afterEconomyByCamp?.TryGetValue(id, out afterEconomy);
                afterFinalTradeByCamp?.TryGetValue(id, out afterFinalTrade);
                double TradeQuantity(string item, bool incoming, string phase = null)
                {
                    string key = id + "\u001f" + item + "\u001f" + (incoming ? "in" : "out");
                    if (phase != null) key += "\u001f" + phase;
                    tradePartTotals.TryGetValue(key, out double value);
                    return value;
                }
                string rowParent = parentTransitions == null ? parent : parentTransitions.TryGetValue(id, out var priorTransition) ? priorTransition : null;
                var d = new ElderEconomicDiagnostic { CampId = id, SocietyId = before.Society, DecisionTurn = record.Turn,
                    TrajectoryId = trajectory, TransitionId = trajectory + "/" + record.Turn + "/" + id,
                    ForkId = fork, BranchId = branch, ParentTransitionId = rowParent, Years = years };
                result[id] = d;
                d.PhysicalEvents = physicalByCamp[id]
                    .Select(row => (Dictionary<string, object>)CopyValue(row))
                    .OrderBy(row => Convert.ToString(row["eventId"], System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)
                    .ToList();
                foreach (var physical in d.PhysicalEvents)
                    physical["eventId"] = trajectory + "/" + physical["eventId"];
                void BoundaryEvent(string category, string item, string unit, double quantity)
                {
                    d.PhysicalEvents.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["eventId"] = d.TransitionId + "/" + category + "/" + item,
                        ["campId"] = id,
                        ["category"] = category,
                        ["item"] = item,
                        ["unit"] = unit,
                        ["quantity"] = Math.Max(0, quantity),
                        ["direction"] = "boundary"
                    });
                }
                void MutationEvent(string category, string item, string unit, double quantity, string direction)
                {
                    if (!(quantity > 1e-12)) return;
                    d.PhysicalEvents.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["eventId"] = d.TransitionId + "/" + category + "/" + item,
                        ["campId"] = id, ["category"] = category, ["item"] = item,
                        ["unit"] = unit, ["quantity"] = quantity, ["direction"] = direction
                    });
                }
                if (transfersByCamp.TryGetValue(id, out var witnesses)) d.Transfers.AddRange(witnesses.Select(w => (Dictionary<string, object>)CopyValue(w)));
                if (tradeRowsByCamp.TryGetValue(id, out var lifecycleRows))
                    d.Trades.AddRange(lifecycleRows.OrderBy(row => (string)row["recordType"])
                        .ThenBy(row => Convert.ToString(row.TryGetValue("eventId", out var eventId) ? eventId
                            : row.TryGetValue("requestId", out var requestId) ? requestId : row["transactionId"], System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)
                        .Select(row => (Dictionary<string, object>)CopyValue(row)));
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
                    double committedLinked = (after?.Residents ?? 0) + (after?.Parties ?? 0);
                    double migrationIn = d.Transfers.Where(t => (string)t["kind"] == "migration" && (int)t["destinationCamp"] == id).Sum(t => Convert.ToDouble(t["people"]));
                    double migrationOut = d.Transfers.Where(t => (string)t["kind"] == "migration" && (int)t["sourceCamp"] == id).Sum(t => Convert.ToDouble(t["people"]));
                    double fissionOut = d.Transfers.Where(t => (string)t["kind"] == "fission" && (int)t["sourceCamp"] == id).Sum(t => Convert.ToDouble(t["people"]));
                    double grossPopulationResidual = demo.EndingPopulation - demo.StartingPopulation
                        - demo.GrossBirths + demo.GrossDeaths;
                    d.Conservation["integerExtinction"] = demo.IntegerExtinction;
                    d.Conservation["integerExtinctionContinuousResidual"] = demo.IntegerExtinctionResidual;
                    d.Conservation["grossPopulationResidual"] = grossPopulationResidual;
                    d.Conservation["populationResidual"] = before.Residents + before.Parties + demo.GrossBirths + migrationIn
                        - demo.GrossDeaths - migrationOut - fissionOut - committedLinked;
                    Conservation(d, "population", "linked-population", "people", before.Residents + before.Parties,
                        demo.GrossBirths, 0, migrationIn, demo.GrossDeaths, 0,
                        migrationOut + fissionOut, 0, committedLinked);
                    BoundaryEvent("opening-stock-boundary", "linked-population", "people", before.Residents + before.Parties);
                    BoundaryEvent("ending-stock-boundary", "linked-population", "people", committedLinked);
                    MutationEvent("population-birth", "linked-population", "people", demo.GrossBirths, "in");
                    MutationEvent("population-death", "linked-population", "people", demo.GrossDeaths, "out");
                    MutationEvent("transfer-in", "linked-population", "people", migrationIn, "in");
                    MutationEvent("transfer-out", "linked-population", "people", migrationOut + fissionOut, "out");
                    double maintenance = Math.Max(0, demo.FoodConsumed - demo.GrowthFoodAccepted);
                    pop["maintenanceFoodConsumed"] = maintenance;
                    double foodTradeIn = new[] { "PlantFood", "AnimalFood", "AquaticFood" }.Sum(item => TradeQuantity(item, true));
                    double foodTradeOut = new[] { "PlantFood", "AnimalFood", "AquaticFood" }.Sum(item => TradeQuantity(item, false));
                    double foodTransferIn = TransferFood(d.Transfers, id, true), foodTransferOut = TransferFood(d.Transfers, id, false);
                    double humanConsumption = maintenance + demo.GrowthFoodAccepted;
                    double foodConsumption = humanConsumption + demo.ManufacturingFood;
                    P("foodTradeIn", foodTradeIn, "food"); P("foodTradeOut", foodTradeOut, "food");
                    P("foodTransferIn", foodTransferIn, "food"); P("foodTransferOut", foodTransferOut, "food");
                    P("humanFoodConsumption", humanConsumption, "food"); P("foodDiscard", demo.DiscardedInventory, "food");
                    P("foodProductionReceipt", demo.FoodReceived, "food");
                    double foodResidual = before.Food + demo.FoodReceived + foodTradeIn + foodTransferIn - foodConsumption
                        - foodTradeOut - foodTransferOut - demo.DiscardedInventory - (after?.Food ?? 0);
                    d.Conservation["foodResidual"] = foodResidual;
                    Conservation(d, "food", "linked-food-reserves", "food", before.Food,
                        demo.FoodReceived, foodTradeIn, foodTransferIn, foodConsumption,
                        foodTradeOut, foodTransferOut, demo.DiscardedInventory, after?.Food ?? 0);
                    BoundaryEvent("opening-stock-boundary", "linked-food-reserves", "food", before.Food);
                    BoundaryEvent("ending-stock-boundary", "linked-food-reserves", "food", after?.Food ?? 0);
                    MutationEvent("food-production-receipt", "linked-food-reserves", "food", demo.FoodReceived, "in");
                    MutationEvent("human-food-consumption", "linked-food-reserves", "food", humanConsumption, "out");
                    MutationEvent("manufacturing-food-consumption", "linked-food-reserves", "food", demo.ManufacturingFood, "out");
                    MutationEvent("trade-in", "linked-food-reserves", "food", foodTradeIn, "in");
                    MutationEvent("trade-out", "linked-food-reserves", "food", foodTradeOut, "out");
                    MutationEvent("transfer-in", "linked-food-reserves", "food", foodTransferIn, "in");
                    MutationEvent("transfer-out", "linked-food-reserves", "food", foodTransferOut, "out");
                    MutationEvent("discard-loss", "linked-food-reserves", "food", demo.DiscardedInventory, "out");
                }
                else d.Missing(pop, "reportStartingLinked", "population", "no-demographic-report");
                prepared.ElderDecisions.TryGetValue(id, out var decision);
                var allocation = decision?.Allocation; if (allocation == null) prepared.DemandAllocations.TryGetValue(id, out allocation);
                var cultivation = Row(); d.Domains["cultivation"] = cultivation;
                cultivation["openingFieldIds"] = before.Fields.Keys.OrderBy(value => value).ToArray();
                cultivation["closingFieldIds"] = after == null ? Array.Empty<int>() : after.Fields.Keys.OrderBy(value => value).ToArray();
                d.Put(cultivation, "foodDelivered", food[id].Where(t => t.Kind == "Cultivation").Sum(t => t.Food), "cultivation", "food");
                CampFieldWorkDiagnostic work = null;
                if (metrics.CraftFieldWork != null && metrics.CraftFieldWork.TryGetValue(id, out work))
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
                var horizonFields = metrics.EconomicFields.TryGetValue(id, out var capturedFieldRows)
                    ? capturedFieldRows.ToDictionary(row => (int)row["fieldId"]) : new Dictionary<int, Dictionary<string, object>>();
                var fieldIdentities = new HashSet<int>(before.Fields.Keys);
                if (after != null) fieldIdentities.UnionWith(after.Fields.Keys);
                fieldIdentities.UnionWith(horizonFields.Keys);
                fieldIdentities.UnionWith(d.Transfers.Where(row => (string)row["kind"] == "field-abandonment")
                    .Select(row => (int)row["fieldId"]));
                cultivation["accountedFieldIds"] = fieldIdentities.OrderBy(value => value).ToArray();
                foreach (int fieldId in fieldIdentities.OrderBy(value => value))
                {
                    horizonFields.TryGetValue(fieldId, out var captured);
                    double openingCapital = before.Fields.TryGetValue(fieldId, out var openingField) ? openingField : 0;
                    double endingCapital = after != null && after.Fields.TryGetValue(fieldId, out var endingField) ? endingField : 0;
                    double added = captured == null ? 0 : (double)captured["capitalAdded"];
                    double lost = captured == null ? 0 : (double)captured["capitalLost"];
                    double transferOut = d.Transfers.Where(row => (string)row["kind"] == "field-abandonment" && (int)row["fieldId"] == fieldId)
                        .Sum(row => Convert.ToDouble(row["capital"]));
                    var field = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["fieldId"] = fieldId, ["openingCapital"] = openingCapital,
                        ["meanCapital"] = captured == null ? openingCapital : (double)captured["meanCapital"],
                        ["decayedCapital"] = captured == null ? openingCapital : (double)captured["decayedCapital"],
                        ["capitalAdded"] = added, ["capitalLost"] = lost,
                        ["transferIn"] = 0.0, ["transferOut"] = transferOut,
                        ["endingCapital"] = endingCapital,
                        ["residual"] = openingCapital + added - lost - transferOut - endingCapital
                    };
                    d.Fields.Add(field);
                    Conservation(d, "field", fieldId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "field-capital", openingCapital, added, 0, 0, lost, 0, transferOut, 0, endingCapital);
                    BoundaryEvent("opening-stock-boundary", "field:" + fieldId, "field-capital", openingCapital);
                    BoundaryEvent("ending-stock-boundary", "field:" + fieldId, "field-capital", endingCapital);
                    MutationEvent("field-capital-addition", "field:" + fieldId, "field-capital", added, "in");
                    MutationEvent("field-capital-loss", "field:" + fieldId, "field-capital", lost, "out");
                    MutationEvent("transfer-out", "field:" + fieldId, "field-capital", transferOut, "out");
                }
                foreach (var key in new[] { "openingCapital", "meanCapital", "capitalAdded", "capitalLost", "endingCapital" })
                    d.Put(cultivation, key, d.Fields.Sum(row => Convert.ToDouble(row[key])), "cultivation", "field-capital");
                d.Conservation["fieldResidual"] = d.Fields.Sum(row => Convert.ToDouble(row["residual"]));
                var herdRows = herds[id].OrderBy(h => h.Herd).ToArray(); var herding = Row(); d.Domains["herding"] = herding;
                herding["openingHerdIds"] = before.Herds.Keys.OrderBy(value => value).ToArray();
                herding["closingHerdIds"] = after == null ? Array.Empty<int>() : after.Herds.Keys.OrderBy(value => value).ToArray();
                herding["capable"] = before.Herding; cultivation["capable"] = before.Cultivation;
                var reportsByHerd = herdRows.ToDictionary(row => row.Herd);
                var herdIdentities = new HashSet<int>(before.Herds.Keys);
                if (after != null) herdIdentities.UnionWith(after.Herds.Keys);
                herdIdentities.UnionWith(reportsByHerd.Keys);
                herdIdentities.UnionWith(d.Transfers.Where(row => (string)row["kind"] == "fission-herd" && (int)row["sourceCamp"] == id)
                    .Select(row => (int)row["parentHerdId"]));
                foreach (int herdId in herdIdentities.OrderBy(value => value))
                {
                    reportsByHerd.TryGetValue(herdId, out var h);
                    double openingAnimals = before.Herds.TryGetValue(herdId, out int openingCount) ? openingCount : 0;
                    double endingAnimals = after != null && after.Herds.TryGetValue(herdId, out int endingCount) ? endingCount : 0;
                    double ownershipOut = d.Transfers.Where(t => (string)t["kind"] == "fission-herd" &&
                        (int)t["sourceCamp"] == id && (int)t["parentHerdId"] == herdId).Sum(t => Convert.ToDouble(t["animals"]));
                    var row = new Dictionary<string, object> { ["herdId"] = herdId, ["openingAnimals"] = openingAnimals,
                        ["startingAnimals"] = h == null ? openingAnimals : h.StartingAnimals,
                        ["survivingAnimals"] = h == null ? openingAnimals : h.SurvivingAnimals,
                        ["newbornAnimals"] = h == null ? 0 : h.NewbornAnimals,
                        ["starvationDeaths"] = h == null ? 0 : h.StarvationDeaths, ["slaughtered"] = h == null ? 0 : h.Slaughtered,
                        ["foodNutrition"] = h == null ? (object)null : h.FoodNutrition,
                        ["waterNutrition"] = h == null ? (object)null : h.WaterNutrition,
                        ["nutrition"] = h == null ? (object)null : h.Nutrition,
                        ["managementFulfilment"] = h == null ? (object)null : h.ManagementFulfillment,
                        ["potentialFood"] = h == null ? 0 : h.PotentialFood, ["acceptedFood"] = h == null ? 0 : h.AcceptedFood,
                        ["slaughterFood"] = h == null ? 0 : h.SlaughterFood,
                        ["coveredHerderYears"] = h == null ? 0 : h.CoveredHerderYears,
                        ["herdingToolsConsumed"] = h == null ? 0 : h.HerdingToolsConsumed,
                        ["ownershipInflow"] = 0.0, ["ownershipOutflow"] = ownershipOut,
                        ["committedEndingAnimals"] = endingAnimals, ["reportAvailable"] = h != null,
                        ["unavailableReason"] = h == null ? "no-managed-herd-report-for-opening-or-closing-identity" : null };
                    row["residual"] = openingAnimals + Convert.ToDouble(row["newbornAnimals"])
                        - Convert.ToDouble(row["starvationDeaths"]) - Convert.ToDouble(row["slaughtered"])
                        - ownershipOut - endingAnimals;
                    d.Herds.Add(row);
                    Conservation(d, "herd", herdId.ToString(System.Globalization.CultureInfo.InvariantCulture), "animals",
                        openingAnimals, Convert.ToDouble(row["newbornAnimals"]), 0, 0,
                        Convert.ToDouble(row["starvationDeaths"]) + Convert.ToDouble(row["slaughtered"]), 0,
                        ownershipOut, 0, endingAnimals);
                    BoundaryEvent("opening-stock-boundary", "herd:" + herdId, "animals", openingAnimals);
                    BoundaryEvent("ending-stock-boundary", "herd:" + herdId, "animals", endingAnimals);
                    MutationEvent("herd-birth", "herd:" + herdId, "animals", Convert.ToDouble(row["newbornAnimals"]), "in");
                    MutationEvent("herd-death-or-slaughter", "herd:" + herdId, "animals",
                        Convert.ToDouble(row["starvationDeaths"]) + Convert.ToDouble(row["slaughtered"]), "out");
                    MutationEvent("transfer-out", "herd:" + herdId, "animals", ownershipOut, "out");
                }
                double herdResidual = 0;
                foreach (var row in d.Herds) herdResidual += Convert.ToDouble(row["residual"]);
                d.Conservation["herdResidual"] = herdResidual;
                d.Conservation["herdOpeningBoundary"] = "decision-opening-owned-herd-identities";
                d.Conservation["herdScope"] = "union-of-opening-closing-and-managed-herd-identities";
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
                manufacturing["active"] = allocation != null;
                var funnels = new List<Dictionary<string, object>>(); manufacturing["funnels"] = funnels;
                if (allocation != null)
                {
                    var allGoods = Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>().ToArray();
                    var openingPools = allGoods.ToDictionary(g => g, g => Math.Max(0, before.Goods[g]));
                    var openingImportPools = allGoods.ToDictionary(g => g,
                        g => Math.Max(0, TradeQuantity(g.ToString(), true, "opening")));
                    var afterProductionImportPools = allGoods.ToDictionary(g => g,
                        g => Math.Max(0, TradeQuantity(g.ToString(), true, "after-production")));
                    var lateTransferIn = allGoods.ToDictionary(g => g, g => Math.Max(0,
                        g == CraftGood.Housing ? 0 : TransferInventory(d.Transfers, id, InventoryName(g), true)));
                    var receiptServicePools = allGoods.ToDictionary(g => g,
                        g => Math.Max(0, production[id].Where(p => p.Good == g).Sum(p => p.Output)));
                    var receiptAttributionPools = new Dictionary<CraftGood, double>(receiptServicePools);
                    var reservedIntermediate = allGoods.ToDictionary(g => g, g => d.PhysicalEvents
                        .Where(row => (string)row["category"] == "intermediate-recipe-consumption" && (string)row["item"] == g.ToString())
                        .Sum(row => Convert.ToDouble(row["quantity"])));
                    var reservedOpeningExport = allGoods.ToDictionary(g => g,
                        g => TradeQuantity(g.ToString(), false, "opening"));
                    var reservedAfterProductionExport = allGoods.ToDictionary(g => g,
                        g => TradeQuantity(g.ToString(), false, "after-production"));
                    var reservedExport = allGoods.ToDictionary(g => g,
                        g => reservedOpeningExport[g] + reservedAfterProductionExport[g]);
                    var reservedTransfer = allGoods.ToDictionary(g => g, g => g == CraftGood.Housing ? 0 : TransferInventory(d.Transfers, id, InventoryName(g), false));
                    var serviceEvents = d.PhysicalEvents.Where(row =>
                        (string)row["category"] == "final-service-fulfilment").ToArray();
                    foreach (var serviceEvent in serviceEvents)
                    {
                        string phase = serviceEvent.TryGetValue("phase", out var rawPhase)
                            ? Convert.ToString(rawPhase, System.Globalization.CultureInfo.InvariantCulture) : null;
                        if (phase != "work-before-production" && phase != "needs-after-production")
                            throw new InvalidOperationException("Economic final-service event is missing its authoritative phase.");
                    }
                    var earlyTerminalService = allGoods.ToDictionary(g => g, g => serviceEvents
                        .Where(row => (string)row["phase"] == "work-before-production"
                            && (string)row["item"] == g.ToString()).Sum(row => Convert.ToDouble(row["quantity"])));
                    var lateTerminalService = allGoods.ToDictionary(g => g, g => serviceEvents
                        .Where(row => (string)row["phase"] == "needs-after-production"
                            && (string)row["item"] == g.ToString()).Sum(row => Convert.ToDouble(row["quantity"])));
                    var hasTerminalWitness = allGoods.ToDictionary(g => g, g => serviceEvents.Any(row =>
                        (string)row["item"] == g.ToString()));
                    double DrawSupply(CraftGood good, double amount, bool allowReceipts,
                        bool allowAfterProductionImports, out double fromOpening,
                        out double fromImport, out double fromReceipt)
                    {
                        fromOpening = Math.Min(Math.Max(0, amount), openingPools[good]); openingPools[good] -= fromOpening;
                        double remaining = Math.Max(0, amount - fromOpening);
                        double fromOpeningImport = Math.Min(remaining, openingImportPools[good]);
                        openingImportPools[good] -= fromOpeningImport;
                        remaining = Math.Max(0, remaining - fromOpeningImport);
                        fromReceipt = allowReceipts ? Math.Min(remaining, receiptServicePools[good]) : 0;
                        receiptServicePools[good] -= fromReceipt;
                        remaining = Math.Max(0, remaining - fromReceipt);
                        double fromAfterProductionImport = allowAfterProductionImports
                            ? Math.Min(remaining, afterProductionImportPools[good]) : 0;
                        afterProductionImportPools[good] -= fromAfterProductionImport;
                        fromImport = fromOpeningImport + fromAfterProductionImport;
                        return fromOpening + fromImport + fromReceipt;
                    }
                    var unallocatedOpeningExport = allGoods.ToDictionary(g => g, g => 0.0);
                    var unallocatedIntermediate = allGoods.ToDictionary(g => g, g => 0.0);
                    var unallocatedAfterProductionExport = allGoods.ToDictionary(g => g, g => 0.0);
                    var unallocatedLateTransfer = allGoods.ToDictionary(g => g, g => 0.0);
                    foreach (CraftGood good in allGoods)
                    {
                        double drawn = DrawSupply(good, reservedOpeningExport[good], false, false, out _, out _, out _);
                        unallocatedOpeningExport[good] = Math.Max(0, reservedOpeningExport[good] - drawn);
                    }
                    var allocatedOpening = new Dictionary<int, double>();
                    var allocatedImport = new Dictionary<int, double>();
                    var allocatedTransferIn = new Dictionary<int, double>();
                    var allocatedReceipt = new Dictionary<int, double>();
                    var allocatedReceiptService = new Dictionary<int, double>();
                    var allocatedFinalService = new Dictionary<int, double>();
                    var orderedRequestIndices = Enumerable.Range(0, allocation.Requests.Count)
                        .OrderBy(index => allocation.Requests[index].Class).ThenBy(index => index).ToArray();
                    void AddServiceAllocation(int requestIndex, double fromOpening, double fromImport,
                        double fromReceipt)
                    {
                        allocatedOpening.TryGetValue(requestIndex, out double priorOpening);
                        allocatedImport.TryGetValue(requestIndex, out double priorImport);
                        allocatedReceiptService.TryGetValue(requestIndex, out double priorReceipt);
                        allocatedFinalService.TryGetValue(requestIndex, out double priorService);
                        allocatedOpening[requestIndex] = priorOpening + fromOpening;
                        allocatedImport[requestIndex] = priorImport + fromImport;
                        allocatedReceiptService[requestIndex] = priorReceipt + fromReceipt;
                        allocatedFinalService[requestIndex] = priorService + fromOpening + fromImport + fromReceipt;
                        allocatedTransferIn[requestIndex] = 0;
                    }
                    // Attribute production receipts independently of service. Work-time tool service occurs
                    // before production, so it may draw only decision-opening stock and opening-trade imports.
                    foreach (int requestIndex in orderedRequestIndices)
                    {
                        var request = allocation.Requests[requestIndex];
                        if (!request.Good.HasValue) continue;
                        CraftGood good = request.Good.Value;
                        double outputPerGrant = 1;
                        if (request.Bundle != null && request.Bundle.Outputs.TryGetValue(good, out double bundledOutput))
                            outputPerGrant = Math.Max(0, bundledOutput);
                        double attributableReceipt = Math.Min(receiptAttributionPools[good],
                            Math.Max(0, request.Granted) * outputPerGrant);
                        receiptAttributionPools[good] -= attributableReceipt;
                        allocatedReceipt[requestIndex] = attributableReceipt;
                        double wanted = Math.Min(Math.Max(0, request.Granted),
                            Math.Max(0, earlyTerminalService[good]));
                        DrawSupply(good, wanted, false, false, out double fromOpening,
                            out double fromImport, out double fromReceipt);
                        AddServiceAllocation(requestIndex, fromOpening, fromImport, fromReceipt);
                        earlyTerminalService[good] = Math.Max(0, earlyTerminalService[good]
                            - fromOpening - fromImport - fromReceipt);
                    }
                    // Production inputs precede final trade. Only after those reservations may
                    // after-production imports serve needs; close transfers remain later still.
                    foreach (CraftGood good in allGoods)
                    {
                        double drawn = DrawSupply(good, reservedIntermediate[good], true, false, out _, out _, out _);
                        unallocatedIntermediate[good] = Math.Max(0, reservedIntermediate[good] - drawn);
                        drawn = DrawSupply(good, reservedAfterProductionExport[good], true, true, out _, out _, out _);
                        unallocatedAfterProductionExport[good] = Math.Max(0, reservedAfterProductionExport[good] - drawn);
                    }
                    foreach (int requestIndex in orderedRequestIndices)
                    {
                        var request = allocation.Requests[requestIndex];
                        if (!request.Good.HasValue) continue;
                        CraftGood good = request.Good.Value;
                        allocatedFinalService.TryGetValue(requestIndex, out double priorService);
                        double requestCapacity = Math.Max(0, request.Granted - priorService);
                        double wanted = Math.Min(requestCapacity, Math.Max(0, lateTerminalService[good]));
                        DrawSupply(good, wanted, true, true, out double fromOpening,
                            out double fromImport, out double fromReceipt);
                        AddServiceAllocation(requestIndex, fromOpening, fromImport, fromReceipt);
                        lateTerminalService[good] = Math.Max(0, lateTerminalService[good]
                            - fromOpening - fromImport - fromReceipt);
                    }
                    foreach (CraftGood good in allGoods)
                    {
                        double drawn = DrawSupply(good, reservedTransfer[good], true, true, out _, out _, out _);
                        unallocatedLateTransfer[good] = Math.Max(0, reservedTransfer[good] - drawn);
                    }
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
                        string requestId = q.RequestId ?? (d.TransitionId + "/request/" + i);
                        var req = new Dictionary<string, object> { ["requestId"] = requestId, ["ordinal"] = i,
                            ["service"] = q.Service, ["serviceClass"] = q.Class.ToString(), ["good"] = q.Good?.ToString(), ["role"] = q.Role,
                            ["rawMaterial"] = q.RawMaterial?.ToString(), ["nativeDemand"] = q.DiagnosticNativeUnits, ["actorCappedRequest"] = q.Units,
                            ["compilerGrant"] = q.Granted, ["unmetReason"] = q.Unmet, ["serviceEquivalentPerUnit"] = q.RawMaterial.HasValue ? q.ServiceEquivalentPerUnit : 1 };
                        req["nativeUnit"] = q.RawMaterial.HasValue ? "material-units" : q.Good.HasValue ? "good-units" : "worker-headcount";
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
                        object physicalReceipt = null, executedWorkerYears = null;
                        object inventoryChange = null, finalService = null;
                        string receiptUnit = q.RawMaterial.HasValue ? "material-units" : "good-units";
                        var unavailableReasons = new Dictionary<string, string>(StringComparer.Ordinal);
                        if (q.RawMaterial.HasValue)
                        {
                            var boundReceipts = material[id].Where(receipt => receipt.RequestId == q.RequestId).ToArray();
                            physicalReceipt = boundReceipts.Sum(receipt => receipt.Amount);
                            executedWorkerYears = boundReceipts.Sum(receipt => receipt.WorkerIntervals) * intervalYears;
                            unavailableReasons["inventoryChange"] = "request-level-net-stock-mutation-is-not-authoritatively-attributed";
                            unavailableReasons["consumptionOrFinalService"] = "raw-extraction-request-has-no-terminal-service-link";
                        }
                        else if (q.Good.HasValue)
                        {
                            allocatedReceipt.TryGetValue(i, out double accountingReceiptAllocation);
                            allocatedOpening.TryGetValue(i, out double openingDraw);
                            allocatedImport.TryGetValue(i, out double importDraw);
                            allocatedTransferIn.TryGetValue(i, out double transferInDraw);
                            allocatedReceiptService.TryGetValue(i, out double receiptServiceDraw);
                            var boundReceipts = production[id].Where(receipt => receipt.Good == q.Good.Value &&
                                receipt.RequestId == q.RequestId).ToArray();
                            int authoritativeSources = allocation.Requests.Count(source => source.Granted > 1e-12 &&
                                source.Bundle != null && source.Bundle.Outputs.TryGetValue(q.Good.Value, out double output) && output > 1e-12);
                            if (authoritativeSources == 1)
                            {
                                physicalReceipt = boundReceipts.Sum(receipt => receipt.Output);
                                executedWorkerYears = boundReceipts.Sum(receipt => receipt.WorkerIntervals) * intervalYears;
                            }
                            else
                            {
                                unavailableReasons["committedPhysicalReceipt"] = "multiple-compiler-requests-share-good-output";
                                unavailableReasons["executedWorkerOrInputClaim"] = "multiple-compiler-requests-share-recipe-work";
                            }
                            inventoryChange = null;
                            unavailableReasons["inventoryChange"] = "request-level-net-stock-mutation-is-not-authoritatively-attributed";
                            if (hasTerminalWitness[q.Good.Value])
                            { allocatedFinalService.TryGetValue(i, out double resolvedService); finalService = resolvedService; }
                            else unavailableReasons["consumptionOrFinalService"] = "no-authoritative-terminal-service-witness-for-good";
                            req["openingStockDraw"] = openingDraw;
                            req["importedStockDraw"] = importDraw;
                            req["transferInStockDraw"] = transferInDraw;
                            req["productionServiceDraw"] = receiptServiceDraw;
                            req["productionReceiptAllocation"] = accountingReceiptAllocation;
                        }
                        else
                        {
                            unavailableReasons["committedPhysicalReceipt"] = "request-kind-has-no-bound-physical-receipt";
                            unavailableReasons["inventoryChange"] = "request-kind-has-no-bound-inventory-mutation";
                            unavailableReasons["consumptionOrFinalService"] = "request-kind-has-no-terminal-service-link";
                        }
                        req["executedWorkerYears"] = executedWorkerYears;
                        req["committedPhysicalReceipt"] = physicalReceipt;
                        req["inventoryChange"] = inventoryChange;
                        req["consumptionOrFinalService"] = finalService;
                        double stockDraw = q.Good.HasValue && allocatedOpening.TryGetValue(i, out double draw) ? draw : 0;
                        if (q.Good.HasValue && stockDraw > 1e-12)
                            d.PhysicalEvents.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                ["eventId"] = d.TransitionId + "/opening-stock-service-claim/" + i,
                                ["campId"] = id,
                                ["category"] = "opening-stock-service-claim",
                                ["item"] = q.Good.Value.ToString(),
                                ["unit"] = "good-units",
                                ["quantity"] = stockDraw,
                                ["direction"] = "claim",
                                ["receiptId"] = requestId
                            });
                        Control(d, d.TransitionId + "/control/request/" + i, "manufacturing", requestId,
                            (string)req["nativeUnit"], "service[" + ElderModel9Contract.ServiceOf(q.Service) + "]",
                            q.DiagnosticNativeUnits, q.Units, q.Granted, executedWorkerYears,
                            physicalReceipt,
                            inventoryChange, finalService, demo,
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            { ["service"] = q.Service, ["serviceClass"] = q.Class.ToString(),
                                ["good"] = q.Good?.ToString(), ["rawMaterial"] = q.RawMaterial?.ToString(),
                                ["role"] = q.Role, ["openingStockDraw"] = stockDraw,
                                ["importedStockDraw"] = q.Good.HasValue && allocatedImport.TryGetValue(i, out double importedDraw) ? importedDraw : 0,
                                ["transferInStockDraw"] = q.Good.HasValue && allocatedTransferIn.TryGetValue(i, out double inboundTransferDraw) ? inboundTransferDraw : 0,
                                ["productionServiceDraw"] = q.Good.HasValue && allocatedReceiptService.TryGetValue(i, out double productionServiceDraw) ? productionServiceDraw : 0,
                                ["workerYearsEquivalent"] = req.TryGetValue("workerYearsEquivalent", out var workEquivalent) ? workEquivalent : null,
                                ["recipeInputsPerUnit"] = req.TryGetValue("bundleInputsPerUnit", out var recipe) ? recipe : null },
                            "worker-years", receiptUnit, receiptUnit, receiptUnit, "active", null, unavailableReasons);
                    }
                    manufacturing["allocationSurplus"] = allGoods.ToDictionary(g => g.ToString(), g => (object)new Dictionary<string, object>
                    {
                        ["openingStock"] = openingPools[g], ["openingImportStock"] = openingImportPools[g],
                        ["afterProductionImportStock"] = afterProductionImportPools[g],
                        ["lateTransferInExcludedFromService"] = lateTransferIn[g],
                        ["productionServiceSupply"] = receiptServicePools[g],
                        ["unattributedProductionReceipts"] = receiptAttributionPools[g],
                        ["reservedIntermediate"] = reservedIntermediate[g], ["reservedExport"] = reservedExport[g],
                        ["reservedTransfer"] = reservedTransfer[g],
                        ["unallocatedOpeningExport"] = unallocatedOpeningExport[g],
                        ["unallocatedIntermediate"] = unallocatedIntermediate[g],
                        ["unallocatedAfterProductionExport"] = unallocatedAfterProductionExport[g],
                        ["unallocatedLateTransfer"] = unallocatedLateTransfer[g],
                        ["unallocatedEarlyService"] = earlyTerminalService[g],
                        ["unallocatedTerminalService"] = lateTerminalService[g]
                    }, StringComparer.Ordinal);
                }
                foreach (CraftGood good in Enum.GetValues(typeof(CraftGood)))
                {
                    var receipts = production[id].Where(p => p.Good == good).ToArray();
                    double output = receipts.Sum(p => p.Output);
                    double recipeConsumption = good == CraftGood.HandTools ? production[id].Sum(p => p.HandToolsConsumed)
                        : good == CraftGood.Tools ? production[id].Sum(p => p.ToolsConsumed)
                        : good == CraftGood.Cloth ? production[id].Sum(p => p.Cloth)
                        : good == CraftGood.AnimalFabric ? production[id].Sum(p => p.AnimalFabric)
                        : good == CraftGood.HousingMaterial ? production[id].Sum(p => p.HousingMaterial) : 0;
                    double openingTradeStock = afterOpening?.Goods?[good] ?? before.Goods[good];
                    double preFinalTradeStock = afterEconomy?.Goods?[good] ?? openingTradeStock + output;
                    double tradeIn = TradeQuantity(good.ToString(), true);
                    double tradeOut = TradeQuantity(good.ToString(), false);
                    double transferIn = good == CraftGood.Housing ? 0 : TransferInventory(d.Transfers, id, InventoryName(good), true);
                    double transferOut = good == CraftGood.Housing ? 0 : TransferInventory(d.Transfers, id, InventoryName(good), false);
                    double serviceConsumption = d.PhysicalEvents.Where(row =>
                        ((string)row["category"] == "final-service-fulfilment" || (string)row["category"] == "wear-depreciation") &&
                        (string)row["item"] == good.ToString()).Sum(row => Convert.ToDouble(row["quantity"]));
                    double discard = d.PhysicalEvents.Where(row => (string)row["category"] == "discard-loss" &&
                        (string)row["item"] == good.ToString()).Sum(row => Convert.ToDouble(row["quantity"]));
                    var g = new Dictionary<string, object> { ["good"] = good.ToString(), ["unit"] = "good-units", ["output"] = output,
                        ["openingStock"] = before.Goods[good], ["endingStock"] = after?.Goods[good] ?? 0,
                        ["stockKind"] = good == CraftGood.Housing ? "fixed-housing-tranche-capacity" : "camp-inventory",
                        ["afterOpeningTradeStock"] = openingTradeStock, ["beforeFinalTradeStock"] = preFinalTradeStock,
                        ["afterFinalTradeStock"] = afterFinalTrade?.Goods?[good] ?? preFinalTradeStock,
                        ["intermediateConsumption"] = recipeConsumption, ["serviceConsumptionOrDepreciation"] = serviceConsumption,
                        ["discardOrDestruction"] = discard,
                        ["actualTradeIn"] = tradeIn, ["actualTradeOut"] = tradeOut,
                        ["transferIn"] = transferIn, ["transferOut"] = transferOut,
                        ["workerIntervals"] = receipts.Sum(p => p.WorkerIntervals), ["workerYears"] = receipts.Sum(p => p.WorkerIntervals) * intervalYears,
                        ["equipmentCoveredWorkerYears"] = receipts.Sum(p => p.EquipmentWorkerIntervals) * intervalYears,
                        ["stone"] = receipts.Sum(p => p.Stone), ["clay"] = receipts.Sum(p => p.Clay), ["wood"] = receipts.Sum(p => p.Wood),
                        ["plantFood"] = receipts.Sum(p => p.PlantFood), ["animalFood"] = receipts.Sum(p => p.AnimalFood), ["animalFabric"] = receipts.Sum(p => p.AnimalFabric),
                        ["cloth"] = receipts.Sum(p => p.Cloth), ["housingMaterial"] = receipts.Sum(p => p.HousingMaterial), ["handToolsConsumed"] = receipts.Sum(p => p.HandToolsConsumed), ["toolsConsumed"] = receipts.Sum(p => p.ToolsConsumed) };
                    if (allocation != null) { g["plannedOutputCeiling"] = allocation.Ceilings.TryGetValue(good, out double ceiling) ? ceiling : (object)null;
                        if (g["plannedOutputCeiling"] == null) d.Unavailable["goods[" + d.Goods.Count + "].plannedOutputCeiling"] = "no-explicit-output-ceiling"; }
                    else d.Missing(g, "plannedOutputCeiling", "goods[" + d.Goods.Count + "]", "no-bound-demand-allocation");
                    d.Goods.Add(g);
                    BoundaryEvent("opening-stock-boundary", good.ToString(), "good-units", before.Goods[good]);
                    BoundaryEvent("ending-stock-boundary", good.ToString(), "good-units", after?.Goods[good] ?? 0);
                    Conservation(d, "craft-good", good.ToString(), "good-units", before.Goods[good], output,
                        tradeIn, transferIn, recipeConsumption + serviceConsumption, tradeOut, transferOut, discard, after?.Goods[good] ?? 0);
                    MutationEvent("trade-in", good.ToString(), "good-units", tradeIn, "in");
                    MutationEvent("trade-out", good.ToString(), "good-units", tradeOut, "out");
                    MutationEvent("transfer-in", good.ToString(), "good-units", transferIn, "in");
                    MutationEvent("transfer-out", good.ToString(), "good-units", transferOut, "out");
                }
                foreach (MaterialId m in new[] { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood })
                {
                    var receipts = material[id].Where(p => p.Material == m).ToArray(); var materialRequests = allocation?.Requests.Where(q => q.RawMaterial == m).ToArray();
                    double extracted = receipts.Sum(p => p.Amount);
                    double recipeUse = m == MaterialId.Stone ? production[id].Sum(p => p.Stone)
                        : m == MaterialId.Clay ? production[id].Sum(p => p.Clay) : production[id].Sum(p => p.Wood);
                    double tradeIn = TradeQuantity(m.ToString(), true), tradeOut = TradeQuantity(m.ToString(), false);
                    double transferIn = TransferInventory(d.Transfers, id, m.ToString(), true), transferOut = TransferInventory(d.Transfers, id, m.ToString(), false);
                    var row = new Dictionary<string, object> { ["material"] = m.ToString(), ["unit"] = "material-units", ["received"] = receipts.Sum(p => p.Amount),
                        ["openingStock"] = before.Materials[m], ["endingStock"] = after?.Materials[m] ?? 0,
                        ["recipeConsumption"] = recipeUse, ["actualTradeIn"] = tradeIn, ["actualTradeOut"] = tradeOut,
                        ["transferIn"] = transferIn, ["transferOut"] = transferOut,
                        ["workerIntervals"] = receipts.Sum(p => p.WorkerIntervals), ["workerYears"] = receipts.Sum(p => p.WorkerIntervals) * intervalYears };
                    if (materialRequests != null && receipts.All(r => r.Phase == "elder-raw"))
                    { row["requested"] = materialRequests.Sum(q => q.NativeUnits); row["granted"] = materialRequests.Sum(q => q.Granted); }
                    else { d.Missing(row, "requested", "materials[" + d.Materials.Count + "]", "phase-a-authorization-not-retained-in-phase-b-material-request-units");
                        d.Missing(row, "granted", "materials[" + d.Materials.Count + "]", "phase-a-authorization-not-retained-in-phase-b-material-request-units"); }
                    if (materialRequests != null) { row["phaseBRequested"] = materialRequests.Sum(q => q.NativeUnits); row["phaseBGranted"] = materialRequests.Sum(q => q.Granted); row["phaseBReceived"] = receipts.Where(r => r.Phase == "elder-raw").Sum(r => r.Amount); }
                    else
                    {
                        d.Missing(row, "phaseBRequested", "materials[" + d.Materials.Count + "]", "no-bound-raw-material-request");
                        d.Missing(row, "phaseBGranted", "materials[" + d.Materials.Count + "]", "no-bound-raw-material-request");
                        d.Missing(row, "phaseBReceived", "materials[" + d.Materials.Count + "]", "no-bound-raw-material-request");
                    }
                    d.Materials.Add(row);
                    BoundaryEvent("opening-stock-boundary", m.ToString(), "material-units", before.Materials[m]);
                    BoundaryEvent("ending-stock-boundary", m.ToString(), "material-units", after?.Materials[m] ?? 0);
                    Conservation(d, "material", m.ToString(), "material-units", before.Materials[m], extracted,
                        tradeIn, transferIn, recipeUse, tradeOut, transferOut, 0, after?.Materials[m] ?? 0);
                    MutationEvent("trade-in", m.ToString(), "material-units", tradeIn, "in");
                    MutationEvent("trade-out", m.ToString(), "material-units", tradeOut, "out");
                    MutationEvent("transfer-in", m.ToString(), "material-units", transferIn, "in");
                    MutationEvent("transfer-out", m.ToString(), "material-units", transferOut, "out");
                }
                var trading = Row(); d.Domains["trade"] = trading;
                trading["eligible"] = before.TradeEligible;
                var transactions = trade.TryGetValue(id, out var ts) ? ts : new List<Tuple<int, TradeRecord>>();
                trading["settledTrades"] = transactions.Count; trading["realisedUtility"] = transactions.Sum(p => p.Item2.Requester == id ? p.Item2.RequesterRealizedUtility : p.Item2.ResponderRealizedUtility);
                var requests = d.Trades.Where(row => row.TryGetValue("recordType", out var type) && (string)type == "requisition" && (int)row["requester"] == id).ToArray();
                trading["requisitions"] = requests.Length;
                trading["honoured"] = requests.Count(row => (string)row["outcome"] == "honoured");
                trading["countered"] = requests.Count(row => (string)row["outcome"] == "countered");
                trading["ignored"] = requests.Count(row => (string)row["outcome"] == "ignored");
                var transactionRows = d.Trades.Where(row => row.TryGetValue("recordType", out var type) && (string)type == "transaction").ToArray();
                var endpointLegs = transactionRows.SelectMany(row => (List<Dictionary<string, object>>)row["legs"])
                    .Where(leg => (int)leg["sender"] == id || (int)leg["receiver"] == id).ToArray();
                trading["escrow"] = endpointLegs.Sum(leg => Convert.ToDouble(leg["escrowedQuantity"]));
                trading["refund"] = endpointLegs.Sum(leg => Convert.ToDouble(leg["refundedOrReturnedQuantity"]));
                trading["storageRejection"] = endpointLegs.Sum(leg => Convert.ToDouble(leg["storageBlockedQuantity"]));
                foreach (var transaction in transactionRows)
                    foreach (var leg in ((List<Dictionary<string, object>>)transaction["legs"]).Where(leg => (int)leg["sender"] == id || (int)leg["receiver"] == id))
                    {
                        bool incoming = (int)leg["receiver"] == id;
                        double delivered = Convert.ToDouble(leg["deliveredQuantity"]);
                        object endpointUtility = transaction[id == (int)transaction["requester"]
                            ? "requesterImmediateUtility" : "responderImmediateUtility"];
                        var tradeUnavailable = new Dictionary<string, string>(StringComparer.Ordinal);
                        if (endpointUtility == null) tradeUnavailable["consumptionOrFinalService"] = Convert.ToString(
                            transaction[id == (int)transaction["requester"] ? "requesterImmediateUtilityUnavailableReason" : "responderImmediateUtilityUnavailableReason"],
                            System.Globalization.CultureInfo.InvariantCulture);
                        Control(d, d.TransitionId + "/control/trade/" + Convert.ToString(leg["legId"]), "trade",
                            Convert.ToString(leg["legId"]), Convert.ToString(leg["unit"]),
                            "tradeValue+tradeGate+tradeShare", leg["requestedQuantity"], leg["agreedQuantity"],
                            leg["agreedQuantity"], leg["supplyAvailableQuantity"], delivered,
                            incoming ? leg["receiverCreditQuantity"] : -Convert.ToDouble(leg["senderDebitQuantity"]), endpointUtility, demo,
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            { ["transactionId"] = transaction["transactionId"], ["good"] = leg["good"],
                                ["sender"] = leg["sender"], ["receiver"] = leg["receiver"], ["direction"] = incoming ? "import" : "export",
                                ["escrowedQuantity"] = leg["escrowedQuantity"], ["senderDebitQuantity"] = leg["senderDebitQuantity"],
                                ["receiverCreditQuantity"] = leg["receiverCreditQuantity"], ["refundedOrReturnedQuantity"] = leg["refundedOrReturnedQuantity"],
                                ["deliveryFraction"] = leg["deliveryFraction"], ["producerDeliveryFraction"] = leg["producerDeliveryFraction"] },
                            "trade-good-units", "trade-good-units", "trade-good-units", "utility",
                            before.TradeEligible ? "active" : "inactive", before.TradeEligible ? null : "decision-opening-trade-ineligible", tradeUnavailable);
                    }
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
                string Classification(bool capable) => decision == null ? "unsupported" : capable ? "active" : "inactive";
                string ClassificationReason(bool capable) => decision == null ? "no-bound-elder-decision" : capable ? null : "decision-opening-capability-inactive";
                bool fieldActive = decision != null && before.Cultivation;
                double fieldOpening = before.Fields.Values.Sum(), fieldEnding = after?.Fields.Values.Sum() ?? 0;
                double fieldAdded = d.Fields.Sum(row => Convert.ToDouble(row["capitalAdded"]));
                double fieldFood = Convert.ToDouble(cultivation["foodDelivered"]);
                var noFieldTerminal = new Dictionary<string, string>(StringComparer.Ordinal)
                { ["consumptionOrFinalService"] = "cultivation-output-is-not-attributed-to-terminal-human-consumption" };
                Control(d, d.TransitionId + "/control/cultivation/retained-field", "cultivation", "retainedFieldOperation",
                    "operation-fraction", "strategic[0]", decision?.Requested?.RetainedFieldOperation,
                    decision?.Requested?.RetainedFieldOperation, decision?.Accepted?.RetainedFieldOperation,
                    fieldActive && work != null ? (object)work.AssignedFarmerWorkerYears : null,
                    fieldActive ? (object)fieldFood : null, fieldActive ? (object)(fieldEnding - fieldOpening) : null,
                    null, demo, null, "worker-years", "food", "field-capital", "food",
                    Classification(before.Cultivation), ClassificationReason(before.Cultivation), noFieldTerminal);
                double? requestedFieldDevelopment = decision == null ? (double?)null
                    : decision.RequestedDevelopmentPeople * (1 - Numbers.Clamp(decision.Requested.FishingDevelopmentShare, 0, 1))
                        * Numbers.Clamp(decision.Requested.FieldDevelopmentShare, 0, 1);
                Control(d, d.TransitionId + "/control/cultivation/development", "cultivation", "fieldDevelopment",
                    "worker-headcount", "strategic[2]+strategic[3]", requestedFieldDevelopment,
                    requestedFieldDevelopment,
                    decision == null ? null : (object)decision.AcceptedFieldDevelopmentPeople,
                    fieldActive && work != null ? (object)work.ExpansionWorkUsedWorkerYears : null,
                    fieldActive ? (object)fieldAdded : null, fieldActive ? (object)(fieldEnding - fieldOpening) : null,
                    null, demo, null, "worker-years", "field-capital", "field-capital", "food",
                    Classification(before.Cultivation), ClassificationReason(before.Cultivation), noFieldTerminal);
                bool herdActive = decision != null && before.Herding;
                double herdOpening = d.Herds.Sum(row => Convert.ToDouble(row["openingAnimals"]));
                double herdEnding = d.Herds.Sum(row => Convert.ToDouble(row["committedEndingAnimals"]));
                double herderYears = herdRows.Sum(row => row.CoveredHerderYears);
                double herdFoodReceipt = herdRows.Sum(row => row.AcceptedFood + row.SlaughterFood);
                var noHerdTerminal = new Dictionary<string, string>(StringComparer.Ordinal)
                { ["consumptionOrFinalService"] = "herding-output-is-not-attributed-to-terminal-human-consumption" };
                Control(d, d.TransitionId + "/control/herding/retained-herd", "herding", "retainedHerdFraction",
                    "animal-fraction", "strategic[1]", decision?.Requested?.RetainedHerdFraction,
                    decision?.Requested?.RetainedHerdFraction, decision?.Accepted?.RetainedHerdFraction,
                    herdActive ? (object)herderYears : null, herdActive ? (object)herdFoodReceipt : null,
                    herdActive ? (object)(herdEnding - herdOpening) : null, null, demo, null,
                    "worker-years", "food", "animals", "food",
                    Classification(before.Herding), ClassificationReason(before.Herding), noHerdTerminal);
                double? requestedHerdDevelopment = decision == null ? (double?)null
                    : decision.RequestedDevelopmentPeople * (1 - Numbers.Clamp(decision.Requested.FishingDevelopmentShare, 0, 1))
                        * (1 - Numbers.Clamp(decision.Requested.FieldDevelopmentShare, 0, 1));
                Control(d, d.TransitionId + "/control/herding/development", "herding", "herdDevelopment",
                    "worker-headcount", "strategic[2]+strategic[3]", requestedHerdDevelopment,
                    requestedHerdDevelopment,
                    decision == null ? null : (object)decision.AcceptedHerdDevelopmentPeople,
                    herdActive ? (object)(decision.ResolvedHerdDevelopmentPeople * years) : null,
                    herdActive ? (object)herdRows.Sum(row => row.NewbornAnimals) : null,
                    herdActive ? (object)(herdEnding - herdOpening) : null, null, demo, null,
                    "worker-years", "animals", "animals", "food",
                    Classification(before.Herding), ClassificationReason(before.Herding),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["consumptionOrFinalService"] = "herd-development-has-no-terminal-service-link" });
                var tradeFunnel = Funnel(trading, "trade", "settledTransactions", "transactions", null, null, null);
                tradeFunnel["active"] = before.TradeEligible;
                if (before.TradeEligible) { tradeFunnel["physicalReceipt"] = transactions.Count; d.Unavailable.Remove("domains.trade.funnels[0].physicalReceipt"); }
                // Control outcomes are committed-closing witnesses.  DemographicReport
                // ends before migration/fission settlement, so copying its population
                // here would cross-bind a pre-transfer value to a closing boundary.
                // Rebind every applicable control only after the authoritative closing
                // camp/party identities above have been captured.
                foreach (var control in d.Controls)
                {
                    var phases = (Dictionary<string, object>)control["phases"];
                    if (phases["endingPopulation"] != null)
                        phases["endingPopulation"] = pop["endingLinked"];
                    if (phases["endingQol"] != null && pop.TryGetValue("qol", out var committedQol))
                        phases["endingQol"] = committedQol;
                }
                d.PhysicalEvents = d.PhysicalEvents.OrderBy(row =>
                    Convert.ToString(row["eventId"], System.Globalization.CultureInfo.InvariantCulture),
                    StringComparer.Ordinal).ToList();
            }
            return result;
        }
    }
}
