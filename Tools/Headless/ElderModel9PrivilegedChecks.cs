using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Anthropocene.Simulation;

// Intervention A, simulator side: the versioned critic-only privileged view (ElderModel9Privileged).
// - layout: schema version, preregistered caps (fields 16, herds 8, unlocks 1, economy 1, each + one overflow row);
// - family 0 is the legacy ten-float vector, bit for bit;
// - every observed row carries the view and its schema version, captured at the observation's decision boundary;
// - perturbing hidden state that only the privileged view reads leaves every actor-visible tensor, legality mask and
//   actor-labor catalog byte-identical (while the view changes);
// - the privileged arrays never reach ElderObservation/ElderModel9Tensor/AgentObservation, and emitting them is pure
//   (world fingerprint and every player's filtered observation unchanged).
internal static class ElderModel9PrivilegedChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("ElderModel9Privileged: " + message); }

    public static int Run()
    {
        checks = 0;
        Layout();
        int herdingOnly = 1 << (int)Technology.Cultivation | 1 << (int)Technology.Watercraft;
        var snapshot = ElderModel9Environment.GenerateOpening(31, 8, out _, 40, herdingOnly);
        Emission(snapshot);
        Perturbation(snapshot);
        Overflow(snapshot);
        Filtering(snapshot);
        Console.WriteLine("PASS: " + checks + " model9 privileged-view assertions");
        return checks;
    }

    private static void Layout()
    {
        var f = ElderModel9Privileged.Families;
        Check(ElderModel9Contract.PrivilegedSchemaVersion == 1, "privileged schema version 1");
        Check(f.Select(x => x.Name).SequenceEqual(new[] { "core", "fields", "herds", "unlocks", "economy" }), "family order is preregistered");
        Check(f[0].Cap == 1 && !f[0].Overflow && f[0].Features.SequenceEqual(ElderModel9Contract.PrivilegedFeatures), "family 0 is the ten legacy floats");
        Check(f[1].Cap == 16 && f[2].Cap == 8 && f[3].Cap == 1 && f[4].Cap == 1 && f.Skip(1).All(x => x.Overflow && x.Rows == x.Cap + 1),
            "typed caps fields 16, herds 8, unlocks 1, economy 1, each with one overflow row");
        foreach (var family in f)
            Check(family.Features.Distinct().Count() == family.Width && (family.Name == "core" || family.Features[0] == "row-count"),
                family.Name + " features are unique and typed families start with row-count");
        Check(ElderModel9Contract.NextDecisionFields.Contains("PrivilegedView") && ElderModel9Contract.NextDecisionFields.Contains("Observation") &&
            ElderModel9Contract.ExecutedTransitionFields.Contains("Welfare") && !ElderModel9Contract.ExecutedTransitionFields.Contains("PrivilegedView"),
            "the row schema pairs privileged state with the observation, not with the reward");
        var villageFields = typeof(ElderModel9Village).GetFields().Select(x => x.Name).ToHashSet();
        Check(ElderModel9Contract.ExecutedTransitionFields.Concat(ElderModel9Contract.NextDecisionFields).All(villageFields.Contains),
            "every row-schema field exists on the village row");
    }

    // The legacy (pre-view) family-0 computation, with its global scans, as the bit-exact reference.
    private static float[] Legacy(WorldState w, Camp camp, int priorPopulation, int age)
    {
        var cfg = w.Config;
        double meals = Math.Max(1e-9, Math.Max(1, camp.Population) * cfg.Consumption * cfg.GenerationIntervals);
        var profile = HerdConsumptionProfile.Owned(w, camp);
        var site = SettlementSiteEvaluation.Evaluate(w, camp, camp.Tile, camp.Population, profile);
        double potential = 0, food = 0;
        foreach (int tile in w.Map.Radius(camp.Tile, 1))
        {
            var ecology = LazyEcology.ProjectEcology(w, tile, w.Days);
            potential += Math.Max(0, ecology.PotentialFood); food += Math.Max(0, ecology.Food);
        }
        double animals = Math.Max(1, profile.Animals);
        int daughters = w.Societies.Count(s => s.ParentSociety == camp.SocietyId);
        double housing = NeolithicProduction.FixedHousingAt(w, camp, w.Days) * cfg.Neolithic.HousingPeoplePerUnit / Math.Max(1, camp.Population);
        double growth = priorPopulation > 0 ? camp.Population / (double)priorPopulation - 1 : 0;
        return new[] { Math.Log(1 + Math.Max(0, camp.Population)), age, site.HumanFood / meals, potential / meals, food / meals,
            site.SustainableHerdCapacity / animals, site.AbsoluteHerdCapacity / animals, growth, daughters, housing }
            .Select(v => (float)(ElderRLContract.Finite(v) ? v : 0)).ToArray();
    }

    private static int Column(int family, string name) => Array.IndexOf(ElderModel9Privileged.Families[family].Features, name);

    private static void Emission(byte[] snapshot)
    {
        var environment = new ElderModel9Environment();
        var step = environment.Reset(snapshot, true);
        int startTurn = environment.World.Turn, herdRows = 0, fieldRows = 0, partnerRows = 0;
        var prior = new Dictionary<int, int>();
        for (int turn = 0; turn <= 6; turn++)
        {
            if (turn > 0) { prior = environment.World.Camps.ToDictionary(c => c.Id, c => c.Population); step = environment.Step(new Dictionary<int, ElderDirective>()); }
            var w = environment.World;
            var index = new ElderModel9Privileged.Index(w);
            foreach (var v in step.Villages.Where(v => v.Observation != null))
            {
                var camp = w.Camps.First(c => c.Id == v.Camp);
                var view = v.PrivilegedView;
                Check(view != null && view.SchemaVersion == ElderModel9Contract.PrivilegedSchemaVersion &&
                    v.PrivilegedSchemaVersion == ElderModel9Contract.PrivilegedSchemaVersion && v.RowSchemaVersion == ElderModel9Contract.RowSchemaVersion,
                    "every observed row carries the privileged view and its schema version");
                for (int f = 0; f < ElderModel9Privileged.Families.Length; f++)
                {
                    var family = ElderModel9Privileged.Families[f];
                    Check(view.Rows[f].Length == family.Rows * family.Width && view.Present[f].Length == family.Rows, family.Name + " has its fixed shape");
                    for (int r = 0; r < family.Rows; r++)
                        if (!view.Present[f][r]) Check(Enumerable.Range(0, family.Width).All(c => view.Rows[f][r * family.Width + c] == 0), "absent rows are zero");
                }
                int before = prior.TryGetValue(v.Camp, out int p) ? p : camp.Population;
                var legacy = Legacy(w, camp, turn == 0 ? camp.Population : before, w.Turn - startTurn);
                Check(legacy.SequenceEqual(v.Privileged) && legacy.SequenceEqual(view.Rows[0]), "family 0 is the legacy ten-float vector, bit for bit");
                // Captured at this boundary: rebuilding from the committed world gives the identical view.
                var again = ElderModel9Privileged.Build(w, camp, turn == 0 ? camp.Population : before, w.Turn - startTurn, index);
                Check(Enumerable.Range(0, view.Rows.Length).All(f => again.Rows[f].SequenceEqual(view.Rows[f]) && again.Present[f].SequenceEqual(view.Present[f])),
                    "the emitted view is the decision-boundary state (rebuilding it at the boundary is identical)");
                // Herds: exact counts and remainders in herd-ID order.
                var owned = w.Herds.Where(h => h.DomesticCamp == camp.Id).OrderBy(h => h.Id).ToArray();
                int width = ElderModel9Privileged.Families[2].Width;
                for (int r = 0; r < Math.Min(8, owned.Length); r++)
                {
                    herdRows++;
                    Check(view.Present[2][r] && view.Rows[2][r * width + Column(2, "animals")] == owned[r].Animals &&
                        view.Rows[2][r * width + Column(2, "growth-remainder")] == (float)owned[r].GrowthRemainder &&
                        view.Rows[2][r * width + Column(2, "death-remainder")] == (float)owned[r].DeathRemainder, "herd rows carry exact counts and remainders");
                }
                Check(view.Present[2].Take(8).Count(x => x) == Math.Min(8, owned.Length) && view.Present[2][8] == owned.Length > 8, "herd presence follows ownership");
                // Fields: the cultivable footprint is present; operated rows carry the tile's capital.
                int fw = ElderModel9Privileged.Families[1].Width;
                int footprint = CultivationEconomy.Footprint(w, camp).Length;
                Check(view.Present[1].Take(16).Count(x => x) >= footprint, "every footprint tile is a field row");
                for (int r = 0; r < 16 && view.Present[1][r]; r++)
                {
                    fieldRows++;
                    Check(view.Rows[1][r * fw] == 1 && view.Rows[1][r * fw + Column(1, "distance-hex")] <= 2, "field rows are local");
                }
                Check(view.Present[3][0] && !view.Present[3][1] && view.Present[4][0] && !view.Present[4][1], "unlock and economy rows are present without overflow");
                int ew = ElderModel9Privileged.Families[4].Width;
                Check(view.Rows[4][Column(4, "residents")] == camp.Population && view.Rows[4][Column(4, "food")] == (float)camp.Food,
                    "the economy row carries exact residents and food");
                if (view.Rows[4][Column(4, "trade-partners")] > 0) partnerRows++;
                int uw = ElderModel9Privileged.Families[3].Width;
                Check(view.Rows[3][Column(3, "era-neolithic")] == (camp.People.Progression.Era == Era.Neolithic ? 1 : 0), "the unlock row carries the era");
                _ = ew; _ = uw;
            }
        }
        Check(herdRows > 0 && fieldRows > 0, "the fixture exercises herd and field rows");
        Console.WriteLine("ElderModel9Privileged: " + herdRows + " herd rows, " + fieldRows + " field rows, " + partnerRows + " rows with trade partners");
    }

    private static bool SameTensor(ElderModel9Tensor a, ElderModel9Tensor b)
        => a.Core.SequenceEqual(b.Core) && a.CorePresent.SequenceEqual(b.CorePresent) &&
            a.Sets.Length == b.Sets.Length && Enumerable.Range(0, a.Sets.Length).All(s => a.Sets[s].SequenceEqual(b.Sets[s]) && a.RowPresent[s].SequenceEqual(b.RowPresent[s])) &&
            a.MovementMask.SequenceEqual(b.MovementMask) && a.ClothingRouteMask.SequenceEqual(b.ClothingRouteMask) &&
            a.ShelterRouteMask.SequenceEqual(b.ShelterRouteMask) && a.EconomyMixMask.SequenceEqual(b.EconomyMixMask) &&
            a.CommitmentHoldMask == b.CommitmentHoldMask && a.RegionMovementMask.SequenceEqual(b.RegionMovementMask);

    private static byte[] Bytes(ElderModel9Tensor t)
    {
        var bytes = new List<byte>();
        foreach (var x in t.Core) bytes.AddRange(BitConverter.GetBytes(x));
        foreach (var set in t.Sets) foreach (var x in set) bytes.AddRange(BitConverter.GetBytes(x));
        foreach (var mask in new[] { t.CorePresent, t.MovementMask, t.ClothingRouteMask, t.ShelterRouteMask, t.EconomyMixMask, t.RegionMovementMask, new[] { t.CommitmentHoldMask } }
            .Concat(t.RowPresent)) foreach (var x in mask) bytes.Add((byte)(x ? 1 : 0));
        return bytes.ToArray();
    }

    // Hidden state read only by the privileged view: owned-herd growth remainders (never observed) and the private
    // stocks of the remembered trade partners. Perturbing it changes the view, never the actor's inputs or legality.
    private static void Perturbation(byte[] snapshot)
    {
        var environment = new ElderModel9Environment { ActorLaborEnabled = true };
        environment.Reset(snapshot, true);
        for (int t = 0; t < 4; t++) environment.StepLabor(new Dictionary<int, ElderDirective>(), null);
        environment.Fork(out var clean);
        var w = environment.World;
        int perturbedHerds = 0, perturbedPartners = 0;
        var touched = new HashSet<int>();
        foreach (var herd in w.Herds.Where(h => h.DomesticCamp >= 0).OrderBy(h => h.Id)) { herd.GrowthRemainder = (herd.GrowthRemainder + .37) % 1; perturbedHerds++; touched.Add(herd.DomesticCamp); }
        var index = new ElderModel9Privileged.Index(w);
        var partnerOwners = new HashSet<int>();
        foreach (var camp in w.Camps.Where(c => c.Population > 0 && Trade.Eligible(w, c)).OrderBy(c => c.Id))
            foreach (var k in camp.People.Memory.Contacts.Where(k => !k.Moved && k.Tile >= 0 && index.Camps.ContainsKey(k.Camp)))
            {
                var partner = index.Camps[k.Camp];
                if (partner.SocietyId == camp.SocietyId || !Trade.Eligible(w, partner) || w.Map.Distance(camp.Tile, k.Tile) > w.Config.TradeRange) continue;
                partnerOwners.Add(partner.Id); touched.Add(camp.Id);
            }
        foreach (int id in partnerOwners.OrderBy(id => id)) { index.Camps[id].Inventory.Cloth += 5; perturbedPartners++; }
        environment.Fork(out var perturbed);
        Check(perturbedHerds > 0, "the perturbation fixture owns herds");
        var cleanRows = clean.Villages.Where(v => v.Observation != null).ToDictionary(v => v.Camp);
        int compared = 0, changed = 0;
        foreach (var v in perturbed.Villages.Where(v => v.Observation != null && touched.Contains(v.Camp) && !partnerOwners.Contains(v.Camp)))
        {
            var c = cleanRows[v.Camp];
            compared++;
            Check(SameTensor(c.Observation, v.Observation) && Bytes(c.Observation).SequenceEqual(Bytes(v.Observation)),
                "privileged-only perturbation leaves every actor-visible tensor and legality mask byte-identical (camp " + v.Camp + ")");
            Check(c.LaborCatalog != null && v.LaborCatalog != null && c.LaborCatalog.Digest == v.LaborCatalog.Digest &&
                c.LaborCatalog.SpecialtyMask.SequenceEqual(v.LaborCatalog.SpecialtyMask) &&
                c.LaborCatalog.ActivityMask.Zip(v.LaborCatalog.ActivityMask, (x, y) => x.SequenceEqual(y)).All(x => x),
                "privileged-only perturbation leaves the actor-labor catalog and its masks identical");
            if (Enumerable.Range(0, v.PrivilegedView.Rows.Length).Any(f => !v.PrivilegedView.Rows[f].SequenceEqual(c.PrivilegedView.Rows[f]))) changed++;
        }
        Check(compared > 0 && changed == compared, "the perturbation reaches the privileged view of every compared village");
        Console.WriteLine("ElderModel9Privileged: perturbed " + perturbedHerds + " herds and " + perturbedPartners + " partner stores; " + compared + " villages compared");
    }

    // More owned herds than the cap: the overflow row aggregates the rest (row-count = overflowing herds).
    private static void Overflow(byte[] snapshot)
    {
        var environment = new ElderModel9Environment();
        environment.Reset(snapshot, true);
        var w = environment.World;
        var camp = w.Camps.Where(c => c.Population > 0 && CultivationEconomy.EconomyEnabled(w, c)).OrderBy(c => c.Id).First();
        var species = w.Config.Species.First(s => s.Domesticable).Id;
        for (int i = 0; i < 11; i++)
            w.Herds.Add(new Herd { Id = w.NextId++, Tile = camp.Tile, DomesticCamp = camp.Id, OwnerSociety = camp.SocietyId, Species = species, Animals = 3 + i });
        var view = ElderModel9Privileged.Build(w, camp, camp.Population, 0, new ElderModel9Privileged.Index(w));
        var owned = w.Herds.Where(h => h.DomesticCamp == camp.Id).OrderBy(h => h.Id).ToArray();
        int width = ElderModel9Privileged.Families[2].Width;
        Check(owned.Length > 8 && view.Present[2].Take(8).All(x => x) && view.Present[2][8], "an overflowing herd family fills its cap and its overflow row");
        Check(view.Rows[2][8 * width] == owned.Length - 8 && view.Rows[2][8 * width + Column(2, "animals")] == owned.Skip(8).Sum(h => h.Animals),
            "the overflow row counts and sums the herds beyond the cap");
    }

    private static IEnumerable<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>(); var queue = new Queue<Type>(); queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (t == null || !seen.Add(t)) continue;
            yield return t;
            if (t.IsArray) { queue.Enqueue(t.GetElementType()); continue; }
            if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) queue.Enqueue(a);
            if (t.Namespace != "Anthropocene.Simulation") continue;
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) queue.Enqueue(f.FieldType);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) queue.Enqueue(p.PropertyType);
        }
    }

    private static void Filtering(byte[] snapshot)
    {
        // Structural: no observation type can carry the privileged view.
        foreach (var root in new[] { typeof(ElderObservation), typeof(ElderModel9Tensor), typeof(ElderActorLaborCatalog), typeof(AgentObservation), typeof(ObservationDto) })
        {
            var types = Reachable(root).ToArray();
            Check(!types.Contains(typeof(ElderModel9PrivilegedView)), root.Name + " cannot hold the privileged view");
            Check(types.Where(t => t.Namespace == "Anthropocene.Simulation").SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                .All(f => f.Name.IndexOf("privileged", StringComparison.OrdinalIgnoreCase) < 0), root.Name + " has no privileged field");
        }
        // Behavioural: emitting every village's view is pure, and every player's filtered observation is unchanged.
        var environment = new ElderModel9Environment();
        environment.Reset(snapshot, true);
        environment.Step(new Dictionary<int, ElderDirective>());
        var w = environment.World;
        string fingerprint = SaveCodec.Fingerprint(w);
        var builder = new AgentObservationBuilder();
        string Observations() => string.Join("\n", Enumerable.Range(0, w.LivePlayers).Select(p =>
        {
            var camp = w.Camps.FirstOrDefault(c => c.Player == p);
            return camp == null ? "" : ObservationContract.Serialize(builder.Build(w, p), camp.People.Mandate);
        }));
        string before = Observations();
        var index = new ElderModel9Privileged.Index(w);
        var views = environment.Villages().Select(id => w.Camps.First(c => c.Id == id))
            .Select(c => ElderModel9Privileged.Build(w, c, c.Population, 1, index)).ToArray();
        Check(views.Length > 0 && SaveCodec.Fingerprint(w) == fingerprint, "emitting the privileged view does not change the world");
        Check(Observations() == before, "every player/AI filtered observation is unchanged by emitting the privileged view");
        Check(before.IndexOf("privileged", StringComparison.OrdinalIgnoreCase) < 0, "no serialized player/AI observation names a privileged field");
    }
}
