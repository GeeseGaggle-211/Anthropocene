using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Anthropocene.Simulation
{
    public static class AutonomousElderHistoryContract
    {
        public const int ObservationVersion = 5, EnvironmentVersion = 5, ReplayVersion = 5;
        public const int HistoryWidth = 300, FeatureWidth = 1061, StorageVersion = 1;
        public static readonly (string Name, int Width)[] Groups = {
            ("decision_present", 1), ("decision_age_years", 1), ("decision_elapsed_years", 1),
            ("requested_workforce", 13), ("requested_activities", 195), ("requested_controls", 3),
            ("requested_movement", 8), ("quote_present", 1), ("quoted_partner_ordinal", 8),
            ("quoted_give", 11), ("quoted_want", 11), ("quoted_quantities", 2),
            ("compiled_staffing", 13), ("production_output", 11), ("material_extraction", 3),
            ("population_start", 1), ("population_end", 1), ("qol", 1), ("food_met", 1), ("water_met", 1),
            ("moved", 1), ("settled_trade_count", 1), ("settled_give", 1), ("settled_want", 1),
            ("immigrated", 1), ("emigrated", 1), ("daughters", 1),
            ("opening_animals", 1), ("ending_animals", 1),
            ("opening_field_capital", 1), ("ending_field_capital", 1),
            ("institution_present", 1), ("institution_age_years", 1)
        };
        public static readonly string[] FeatureNames = Groups.SelectMany(g =>
            Enumerable.Range(0, g.Width).Select(i => g.Width == 1 ? g.Name : g.Name + ":" + i)).ToArray();
    }

    [Serializable]
    public sealed class AutonomousElderReceipt
    {
        public int SchemaVersion = AutonomousElderHistoryContract.StorageVersion;
        public int Camp, Society, OpeningTile, EndingTile, OpeningPopulation, EndingPopulation;
        public bool HasDecision;
        public double DecisionDays, EndDays, InstitutionInitializedDays;
        public AutonomousElderAction Requested;
        public AutonomousElderObservation HistoricalMasks;
        public int[] CompiledStaffing = new int[13];
        public double[] Production = new double[11], Extraction = new double[3];
        public double QoL, FoodMet, WaterMet, SettledGive, SettledWant;
        public int SettledTrades, Immigrated, Emigrated, Daughters;
        public int OpeningAnimals, EndingAnimals;
        public double OpeningFieldCapital, EndingFieldCapital;
        [NonSerialized] internal int CounterpartyCamp = -1; // preparation-only routing

        public void Validate(WorldState world)
        {
            bool Finite(double x) => ElderRLContract.Finite(x) && x >= 0;
            bool Invalid(double[] xs, int n) => xs == null || xs.Length != n || xs.Any(x => !Finite(x));
            var camp = world.Camps.FirstOrDefault(c => c.Id == Camp);
            bool deadRecorded = HasDecision && EndingPopulation == 0 && world.History.Any(r =>
                Math.Abs(r.StartDays - DecisionDays) < 1e-8 && r.Demographics.Any(d => d.Camp == Camp));
            if (SchemaVersion != 1 || Camp < 0 || (camp == null && !deadRecorded) ||
                !world.Societies.Any(s => s.Id == Society) || camp != null && camp.SocietyId != Society ||
                !Finite(InstitutionInitializedDays) || InstitutionInitializedDays > world.Days + 1e-8)
                throw new InvalidDataException("Invalid autonomous institution identity/version/time.");
            if (Invalid(Production, 11) || Invalid(Extraction, 3) || CompiledStaffing?.Length != 13 ||
                CompiledStaffing.Any(x => x < 0))
                throw new InvalidDataException("Invalid autonomous receipt dimensions/amounts.");
            if (camp != null)
            {
                AutonomousElderHistory.ValidateMemory(world, camp, true);
                if (camp.ElderMemory.InitializedDays != InstitutionInitializedDays)
                    throw new InvalidDataException("Autonomous receipt and institution have different initialization times.");
            }
            if (!HasDecision)
            {
                if (Requested != null || HistoricalMasks != null)
                    throw new InvalidDataException("Absent autonomous decision contains an action.");
                if (DecisionDays != 0 || EndDays != 0 || OpeningTile != 0 || EndingTile != 0 ||
                    OpeningPopulation != 0 || EndingPopulation != 0 || OpeningAnimals != 0 || EndingAnimals != 0 ||
                    QoL != 0 || FoodMet != 0 || WaterMet != 0 || SettledGive != 0 || SettledWant != 0 ||
                    SettledTrades != 0 || Immigrated != 0 || Emigrated != 0 || Daughters != 0 ||
                    OpeningFieldCapital != 0 || EndingFieldCapital != 0 || CompiledStaffing.Any(x => x != 0) ||
                    Production.Any(x => x != 0) || Extraction.Any(x => x != 0))
                    throw new InvalidDataException("Absent autonomous decision contains outcome data.");
                return;
            }
            if (!Finite(DecisionDays) || !Finite(EndDays) || EndDays <= DecisionDays ||
                InstitutionInitializedDays > DecisionDays + 1e-8 || EndDays > world.Days + 1e-8 ||
                OpeningTile < 0 || OpeningTile >= world.Tiles.Length || EndingTile < -1 || EndingTile >= world.Tiles.Length ||
                OpeningPopulation < 1 || EndingPopulation < 0 || OpeningAnimals < 0 || EndingAnimals < 0 ||
                SettledTrades < 0 || Immigrated < 0 || Emigrated < 0 || Daughters < 0 ||
                !Finite(OpeningFieldCapital) || !Finite(EndingFieldCapital) || !Finite(SettledGive) || !Finite(SettledWant) ||
                !Finite(QoL) || QoL > 1 || !Finite(FoodMet) || FoodMet > 1 || !Finite(WaterMet) || WaterMet > 1 ||
                Invalid(Production, 11) || Invalid(Extraction, 3) || CompiledStaffing == null ||
                CompiledStaffing.Length != 13 || CompiledStaffing.Any(x => x < 0) ||
                CompiledStaffing.Sum(x => (long)x) != OpeningPopulation)
                throw new InvalidDataException("Invalid autonomous decision/outcome receipt.");
            var m = HistoricalMasks;
            if (m == null || m.SchemaVersion != 4 || m.WorkforceMask?.Length != 13 || m.ActivityMask?.Length != 13 ||
                m.ActivityMask.Any(row => row?.Length != 15) || m.ControlMask?.Length != 3 ||
                m.MovementMask?.Length != 8 || m.CounterpartyMask?.Length != 8 || m.GoodMask?.Length != 11)
                throw new InvalidDataException("Invalid historical autonomous legality masks.");
            try { AutonomousElderCompiler.Validate(Requested, m); }
            catch (ArgumentException e) { throw new InvalidDataException("Invalid historical autonomous action.", e); }
            if (!CompiledStaffing.SequenceEqual(AutonomousElderCompiler.Apportion(Requested.Workforce, OpeningPopulation)))
                throw new InvalidDataException("Autonomous receipt staffing does not match requested shares.");
        }
    }

    [Serializable]
    internal sealed class AutonomousElderHistorySave
    {
        public int SchemaVersion = 1;
        public List<AutonomousElderReceipt> Receipts = new List<AutonomousElderReceipt>();
    }

    public static class AutonomousElderHistory
    {
        internal static void ValidateMemory(WorldState w, Camp c, bool requireInitialized)
        {
            var m = c.ElderMemory;
            if (m == null)
            { if (requireInitialized) throw new InvalidDataException("Autonomous institution has no persistent memory."); return; }
            bool Time(double d) => ElderRLContract.Finite(d) && d >= 0 && d <= w.Days + 1e-8;
            if (m.SchemaVersion != 1 || requireInitialized && !m.Initialized ||
                !Time(m.InitializedDays) || !Time(m.LastIntegratedDays) || m.LastIntegratedDays < m.InitializedDays ||
                m.CurrentSignals?.Length != 11 || m.ExponentialMeans?.Length != 33 ||
                m.CurrentSignals.Any(x => !ElderRLContract.Finite(x)) || m.ExponentialMeans.Any(x => !ElderRLContract.Finite(x)) ||
                m.HasLastDecision && (!Time(m.LastDecisionDays) || !ElderRLContract.Finite(m.LastDecisionElapsedYears) || m.LastDecisionElapsedYears < 0) ||
                m.HasDeficitStart && !Time(m.DeficitStartDays) || m.HasLastMovement && !Time(m.LastMovementDays) ||
                m.HasLastFission && !Time(m.LastFissionDays) || m.HasCultivationFrontierChange && !Time(m.LastCultivationFrontierChangeDays) ||
                m.HasFishingFrontierChange && !Time(m.LastFishingFrontierChangeDays))
                throw new InvalidDataException("Invalid persistent autonomous institution memory.");
        }

        public static void Initialize(WorldState world, int[] controlled)
        {
            world.AutonomousReceipts ??= new Dictionary<int, AutonomousElderReceipt>();
            foreach (int id in controlled.OrderBy(id => id))
            {
                var camp = world.Camps.Single(c => c.Id == id);
                if (world.AutonomousReceipts.ContainsKey(id))
                { world.AutonomousReceipts[id].Validate(world); continue; }
                if (camp.ElderMemory?.Initialized == true) ValidateMemory(world, camp, true);
                // Current local state seeds the bank. No pre-opt-in interval flows
                // or fabricated action receipts are attributed to a new institution.
                var memory = ElderInstitutionMemorySystem.EnsureInitialized(world, camp, false);
                if (!world.AutonomousReceipts.ContainsKey(id))
                    world.AutonomousReceipts[id] = new AutonomousElderReceipt { Camp = id, Society = camp.SocietyId,
                        InstitutionInitializedDays = memory.InitializedDays };
            }
        }

        internal static Dictionary<int, AutonomousElderReceipt> PrepareReceipts(WorldState w,
            IReadOnlyDictionary<int, AutonomousElderAction> actions,
            IReadOnlyDictionary<int, AutonomousElderObservation> observations)
        {
            if (w.AutonomousReceipts == null) return null;
            var camps = w.Camps.ToDictionary(c => c.Id);
            var animals = w.Herds.Where(h => h.DomesticCamp >= 0).GroupBy(h => h.DomesticCamp)
                .ToDictionary(g => g.Key, g => g.Sum(h => h.Animals));
            var result = new Dictionary<int, AutonomousElderReceipt>();
            foreach (var pair in actions.OrderBy(p => p.Key))
            {
                if (!w.AutonomousReceipts.ContainsKey(pair.Key)) continue;
                var c = camps[pair.Key]; var a = pair.Value.Copy(); var o = observations[pair.Key];
                result[c.Id] = new AutonomousElderReceipt { Camp = c.Id, Society = c.SocietyId,
                    DecisionDays = w.Days, InstitutionInitializedDays = c.ElderMemory.InitializedDays,
                    OpeningTile = c.Tile, OpeningPopulation = c.Population, Requested = a,
                    HistoricalMasks = CopyMasks(o), CompiledStaffing = AutonomousElderCompiler.Apportion(a.Workforce, c.Population),
                    CounterpartyCamp = a.Quote == null ? -1 : o.CounterpartyIds[a.Quote.Counterparty],
                    OpeningAnimals = animals.TryGetValue(c.Id, out int n) ? n : 0,
                    OpeningFieldCapital = FieldCapital(w, c) };
            }
            return result;
        }

        internal static AutonomousElderObservation CopyMasks(AutonomousElderObservation o)
            => new AutonomousElderObservation { WorkforceMask = (bool[])o.WorkforceMask.Clone(),
                ActivityMask = o.ActivityMask.Select(row => (bool[])row.Clone()).ToArray(),
                ControlMask = (bool[])o.ControlMask.Clone(), MovementMask = (bool[])o.MovementMask.Clone(),
                CounterpartyMask = (bool[])o.CounterpartyMask.Clone(), GoodMask = (bool[])o.GoodMask.Clone() };

        // Capital in the village's current legal operation footprint, never a world-tile scan.
        internal static double FieldCapital(WorldState w, Camp c) => c == null ? 0 :
            CultivationEconomy.Footprint(w, c).Where(t => w.Tiles[t].CultivationOperator == c.Id)
                .Sum(t => Math.Max(0, w.Tiles[t].CultivationCapital));

        internal static void CloseBoundary(WorldState w, Dictionary<int, AutonomousElderReceipt> pending, TurnRecord record)
        {
            if (pending == null || pending.Count == 0) return;
            var camps = w.Camps.ToDictionary(c => c.Id);
            var animals = w.Herds.Where(h => h.DomesticCamp >= 0).GroupBy(h => h.DomesticCamp)
                .ToDictionary(g => g.Key, g => g.Sum(h => h.Animals));
            foreach (var p in record.Production)
                if (pending.TryGetValue(p.Camp, out var r)) r.Production[(int)p.Good] += p.Output;
            foreach (var p in record.MaterialTransfers)
                if (pending.TryGetValue(p.Camp, out var r))
                { int i = p.Material == MaterialId.Stone ? 0 : p.Material == MaterialId.Clay ? 1 : p.Material == MaterialId.Wood ? 2 : -1;
                    if (i >= 0) r.Extraction[i] += p.Amount; }
            foreach (var t in record.Trades)
                foreach (int id in new[] { t.Requester, t.Responder })
                    if (pending.TryGetValue(id, out var r))
                    {
                        r.SettledTrades++;
                        var q = r.Requested.Quote;
                        if (q == null || (id == t.Requester ? t.Responder : t.Requester) != r.CounterpartyCamp) continue;
                        bool requester = id == t.Requester;
                        if (q.Give != (requester ? t.Give : t.Want) || q.Want != (requester ? t.Want : t.Give)) continue;
                        // Native TradeRecord quantities already include delivery scales.
                        r.SettledGive += requester ? t.GiveQuantity : t.WantQuantity;
                        r.SettledWant += requester ? t.WantQuantity : t.GiveQuantity;
                    }
            foreach (var m in record.Migrations)
            { if (pending.TryGetValue(m.DestinationCamp, out var d)) d.Immigrated += m.Migrants;
                if (pending.TryGetValue(m.SourceCamp, out var s)) s.Emigrated += m.Migrants; }
            foreach (var f in record.Fissions)
                if (pending.TryGetValue(f.ParentCamp, out var r)) r.Daughters++;
            foreach (var pair in pending.OrderBy(p => p.Key))
            {
                var r = pair.Value; camps.TryGetValue(r.Camp, out var c);
                r.EndDays = record.StartDays + record.Duration; r.EndingTile = c?.Tile ?? -1;
                r.EndingPopulation = c?.Population ?? 0; r.QoL = ElderRLContract.Unit(c?.QoL ?? 0);
                r.FoodMet = ElderRLContract.Unit(c?.FoodNeedsMetRatio ?? 0); r.WaterMet = ElderRLContract.Unit(c?.WaterNeedsMetRatio ?? 0);
                r.EndingAnimals = animals.TryGetValue(r.Camp, out int n) ? n : 0;
                r.EndingFieldCapital = FieldCapital(w, c); r.HasDecision = true;
                w.AutonomousReceipts[r.Camp] = r;
            }
        }

        public static double[] Encode(WorldState w, AutonomousElderReceipt r)
        {
            var x = new double[AutonomousElderHistoryContract.HistoryWidth]; int i = 0;
            void Put(double v) { x[i++] = v; }
            void Many(IEnumerable<double> vs) { foreach (double v in vs) Put(v); }
            void OneHot(int n, int k) { for (int j = 0; j < n; j++) Put(j == k ? 1 : 0); }
            if (r.HasDecision)
            {
                var a = r.Requested; var q = a.Quote;
                Put(1); Put((w.Days - r.DecisionDays) / w.Config.DaysPerYear); Put((r.EndDays - r.DecisionDays) / w.Config.DaysPerYear);
                Many(a.Workforce); foreach (var row in a.Activities) Many(row);
                Many(new[] { a.FieldExpansionFraction, a.RetainedHerdFraction, a.Breeding }); OneHot(8, a.Movement);
                Put(q == null ? 0 : 1); OneHot(8, q?.Counterparty ?? -1); OneHot(11, q == null ? -1 : (int)q.Give);
                OneHot(11, q == null ? -1 : (int)q.Want); Put(q?.GiveQuantity ?? 0); Put(q?.WantQuantity ?? 0);
                Many(r.CompiledStaffing.Select(n => (double)n)); Many(r.Production); Many(r.Extraction);
                Put(r.OpeningPopulation); Put(r.EndingPopulation); Put(r.QoL); Put(r.FoodMet); Put(r.WaterMet);
                Put(r.EndingTile >= 0 && r.EndingTile != r.OpeningTile ? 1 : 0); Put(r.SettledTrades); Put(r.SettledGive); Put(r.SettledWant);
                Put(r.Immigrated); Put(r.Emigrated); Put(r.Daughters); Put(r.OpeningAnimals); Put(r.EndingAnimals);
                Put(r.OpeningFieldCapital); Put(r.EndingFieldCapital);
                if (i != x.Length - 2) throw new InvalidOperationException("Autonomous history layout mismatch.");
            }
            x[x.Length - 2] = 1;
            x[x.Length - 1] = (w.Days - r.InstitutionInitializedDays) / w.Config.DaysPerYear;
            return x;
        }
    }
}
