using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Separate versioned surface: never reinterpret the original five-head directive.
    public static class AutonomousElderContract
    {
        public const int ActionVersion = 3, ObservationVersion = 4, EnvironmentVersion = 4, ReplayVersion = 4;
        public const int Roles = 13, Activities = 15, Contacts = 8;
        public const double QuoteMinimum = .000001, QuoteLimit = 1000000;
        public static readonly string[] ActivityNames = new[] { "Food", "Stone", "Clay", "Wood" }
            .Concat(Enum.GetNames(typeof(CraftGood))).ToArray();
    }

    public sealed class ElderStockQuote
    {
        public int Counterparty;
        public TradeGood Give, Want;
        public double GiveQuantity, WantQuantity;
        public ElderStockQuote Copy() => (ElderStockQuote)MemberwiseClone();
    }

    public sealed class AutonomousElderAction
    {
        public int SchemaVersion = AutonomousElderContract.ActionVersion;
        public double[] Workforce = new double[AutonomousElderContract.Roles];
        public double[][] Activities = Enumerable.Range(0, AutonomousElderContract.Roles)
            .Select(_ => new double[AutonomousElderContract.Activities]).ToArray();
        public double FieldExpansionFraction, RetainedHerdFraction, Breeding;
        public ElderStockQuote Quote;
        public int Movement; // 0 stay; 1..7 knowledge-limited site rows + 1
        public AutonomousElderAction Copy() => new AutonomousElderAction
        {
            SchemaVersion = SchemaVersion, Workforce = (double[])Workforce.Clone(),
            Activities = Activities.Select(row => (double[])row.Clone()).ToArray(),
            FieldExpansionFraction = FieldExpansionFraction, RetainedHerdFraction = RetainedHerdFraction,
            Breeding = Breeding, Quote = Quote?.Copy(), Movement = Movement
        };
    }

    public class AutonomousElderObservation
    {
        public int SchemaVersion = AutonomousElderContract.ObservationVersion;
        public double[] Context;
        public bool[] WorkforceMask;
        public bool[][] ActivityMask;
        public bool[] ControlMask;
        public bool[] CounterpartyMask = new bool[AutonomousElderContract.Contacts];
        public bool[] GoodMask;
        public bool[] MovementMask = new bool[8];
        internal int[] SiteTiles = Array.Empty<int>();
        public double[] Stocks;
        // Address tokens are server-only and are not serialized into actor observations.
        internal int[] CounterpartyIds = Array.Empty<int>();
        internal int[] CounterpartyTiles = Array.Empty<int>();
    }

    public static class AutonomousElderCompiler
    {
        public static AutonomousElderObservation Observe(WorldState w, Camp c)
        {
            var context = new ElderObservationBuilder().Build(w, c.Id, true, out var metadata);
            // Full owner knowledge is required for remembered contact slots.
            var game = new AgentObservationBuilder().Build(w, c.Player);
            var result = new AutonomousElderObservation
            {
                SiteTiles = metadata.SiteTiles,
                MovementMask = Enumerable.Range(0, 8).Select(i => context.Mask.AllowsMovement(i - 1)).ToArray(),
                Context = ElderObservationSchema.Encode(context).Values.Select(x => (double)x).ToArray(),
                WorkforceMask = WorkforceRoles.All.Select(role =>
                    (EconomicIntents.RequiredTechnology(role) == null || c.People.HasTech(EconomicIntents.RequiredTechnology(role))) &&
                    (role != WorkforceRoles.Farmer || CultivationEconomy.Enabled(c)) &&
                    (role != WorkforceRoles.Herder || CultivationEconomy.HusbandryEnabled(c)) &&
                    (role != WorkforceRoles.Fisher || FishingEconomy.FeatureEnabled(w, c))).ToArray(),
                ActivityMask = Enumerable.Range(0, AutonomousElderContract.Roles).Select(_ => new bool[AutonomousElderContract.Activities]).ToArray(),
                ControlMask = new[] { CultivationEconomy.Enabled(c), CultivationEconomy.HusbandryEnabled(c), CultivationEconomy.HusbandryEnabled(c) },
                GoodMask = Trade.Registry.Select(g => w.Config.TradeEnabled && g.Active(w.Config)).ToArray(),
                Stocks = Trade.Registry.Select(g => Trade.Held(c, g.Id)).ToArray()
            };
            for (int r = 0; r < WorkforceRoles.All.Length; r++)
            {
                if (!result.WorkforceMask[r]) continue;
                string role = WorkforceRoles.All[r];
                if (role == WorkforceRoles.Gatherer)
                    for (int a = 0; a < 4; a++) result.ActivityMask[r][a] = true;
                foreach (var recipe in NeolithicProduction.Recipes(w.Config))
                    if (NeolithicProduction.RecipeAvailable(w, c, recipe) &&
                        (role == recipe.Specialist && c.People.HasTech(recipe.Track) || role == WorkforceRoles.Craftsperson && !recipe.SpecialistOnly))
                        result.ActivityMask[r][4 + (int)recipe.Good] = true;
            }
            // Exactly the context contact-row ordering; quote slot n addresses row n.
            var contacts = game.KnownSettlements.Where(k => !k.Moved && k.Tile >= 0)
                .OrderBy(k => w.Map.Distance(c.Tile, k.Tile))
                .ThenByDescending(k => ElderRLContract.Finite(k.ObservedDays) ? k.ObservedDays :
                    Math.Min(w.Days, Math.Max(0, k.Turn) * w.Config.TurnDays))
                .Take(AutonomousElderContract.Contacts).ToArray();
            result.CounterpartyIds = contacts.Select(k => k.Camp).ToArray();
            result.CounterpartyTiles = contacts.Select(k => k.Tile).ToArray();
            for (int i = 0; i < contacts.Length; i++) result.CounterpartyMask[i] = w.Config.TradeEnabled && contacts[i].Camp != c.Id &&
                w.Map.Distance(c.Tile, contacts[i].Tile) <= w.Config.TradeRange &&
                w.Turn - contacts[i].Turn <= w.Config.ContactExpiryTurns;
            return result;
        }

        public static void Validate(AutonomousElderAction a, AutonomousElderObservation o)
        {
            if (a == null || a.SchemaVersion != AutonomousElderContract.ActionVersion) throw new ArgumentException("Unsupported autonomous action schema.");
            if (a.Movement < 0 || a.Movement >= o.MovementMask.Length || !o.MovementMask[a.Movement]) throw new ArgumentException("Illegal movement ordinal.");
            Simplex(a.Workforce, o.WorkforceMask);
            if (a.Activities == null || a.Activities.Length != AutonomousElderContract.Roles) throw new ArgumentException("Activity row count.");
            for (int i = 0; i < a.Activities.Length; i++) Simplex(a.Activities[i], o.ActivityMask[i]);
            var controls = new[] { a.FieldExpansionFraction, a.RetainedHerdFraction, a.Breeding };
            for (int i = 0; i < controls.Length; i++)
                if (!ElderRLContract.Finite(controls[i]) || controls[i] < 0 || controls[i] > 1 || !o.ControlMask[i] && controls[i] != 0)
                    throw new ArgumentException("Illegal capital/livestock control.");
            var q = a.Quote;
            if (q == null) return;
            if (q.Counterparty < 0 || q.Counterparty >= o.CounterpartyMask.Length || !o.CounterpartyMask[q.Counterparty] ||
                !Enum.IsDefined(typeof(TradeGood), q.Give) || !Enum.IsDefined(typeof(TradeGood), q.Want) || q.Give == q.Want ||
                !o.GoodMask[(int)q.Give] || !o.GoodMask[(int)q.Want] ||
                !ElderRLContract.Finite(q.GiveQuantity) || !ElderRLContract.Finite(q.WantQuantity) ||
                q.GiveQuantity < AutonomousElderContract.QuoteMinimum || q.WantQuantity < AutonomousElderContract.QuoteMinimum || q.GiveQuantity > AutonomousElderContract.QuoteLimit || q.WantQuantity > AutonomousElderContract.QuoteLimit)
                throw new ArgumentException("Illegal stock quote.");
        }

        private static void Simplex(double[] shares, bool[] mask)
        {
            if (shares == null || shares.Length != mask.Length || shares.Where((x, i) => !ElderRLContract.Finite(x) || x < 0 || !mask[i] && x != 0).Any())
                throw new ArgumentException("Illegal masked simplex.");
            double sum = shares.Sum();
            if (Math.Abs(sum - (mask.Any(x => x) ? 1 : 0)) > 1e-8) throw new ArgumentException("Simplex must sum to one (zero when absent).");
        }

        public static int[] Apportion(double[] shares, int residents)
        {
            if (residents < 0 || shares == null || shares.Length == 0 || shares.Any(x => !ElderRLContract.Finite(x) || x < 0) || Math.Abs(shares.Sum() - 1) > 1e-8)
                throw new ArgumentException("Invalid apportionment.");
            double sum = shares.Sum();
            var exact = shares.Select(x => x / sum * residents).ToArray();
            var counts = exact.Select(x => (int)Math.Floor(x)).ToArray();
            foreach (int i in Enumerable.Range(0, shares.Length).OrderByDescending(i => exact[i] - counts[i]).ThenBy(i => i).Take(residents - counts.Sum())) counts[i]++;
            return counts;
        }

        internal static AutomaticPlan Compile(WorldState w, Camp c, AutonomousElderAction a)
        {
            var counts = Apportion(a.Workforce, c.Population);
            var intent = new EconomicIntent { AutomationTurns = 0, ManagedBreedingIntensity = a.Breeding,
                HerdTargetSet = CultivationEconomy.HusbandryEnabled(c),
                HerdTarget = (int)Math.Floor(HerdingTooling.DomesticAnimals(w, c) * a.RetainedHerdFraction),
                Labor = new EconomicLaborPlan { PolicyAuthority = true, MaterialGatherers = counts[0] * (1 - a.Activities[0][0]), ExpansionFarmers = counts[1] * a.FieldExpansionFraction } };
            for (int r = 0; r < counts.Length; r++)
                if (counts[r] > 0) intent.Assignments.Add(new PopulationAssignment { Role = WorkforceRoles.All[r], Population = counts[r] });
            var production = ProductionOrder.Autonomous(a, counts);
            return new AutomaticPlan { Order = new Order(OrderKind.Economy, c.Id) { Economy = intent }, Production = production };
        }

        public static AutonomousElderAction Uniform(AutonomousElderObservation o)
        {
            double[] Shares(bool[] mask) { int n = mask.Count(x => x); return mask.Select(x => x ? 1d / n : 0).ToArray(); }
            return new AutonomousElderAction { Workforce = Shares(o.WorkforceMask), Activities = o.ActivityMask.Select(Shares).ToArray(), RetainedHerdFraction = o.ControlMask[1] ? 1 : 0 };
        }
    }
}
