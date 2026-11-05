using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Anthropocene.Simulation;

internal static class FieldDomesticationChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Field domestication: " + message); }
    internal static WorldState Fixture(int distance = 1)
    {
        var w = NeolithicChecks.Fixture(); var c = w.Camps[0];
        w.Config.FieldDomesticationEnabled = true; w.Config.WanderChance = 0;
        w.Config.WildHerdFissionEnabled = false; w.Config.GetSpecies("goat").GrowthRate = 0;
        c.IsVillage = true; c.Population = 24; c.Workforce = new PopulationLedger();
        c.Workforce.Assign(WorkforceRoles.Farmer, 12, c.Population);
        foreach (var t in w.Tiles)
        {
            t.Food = 10000; t.PotentialFood = 100000; t.Freshwater = 100000;
            for (int i = 0; i < 4; i++) { t.Seasons[i].MaxFood = 10000; t.Seasons[i].MaxPotentialFood = 100000; t.Seasons[i].MaxFreshwater = 100000; }
        }
        Fields(w, c);
        int tile = w.Map.Radius(c.Tile, distance).Where(n => w.Map.Distance(c.Tile, n) == distance).OrderBy(n => n).First();
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = tile, Species = "goat", Animals = 8, Capacity = 100 });
        return w;
    }
    internal static void Fields(WorldState w, Camp c)
    {
        foreach (int n in w.Map.Radius(c.Tile, 1))
        {
            w.Tiles[n].CultivationCapital = 1;
            w.Tiles[n].CultivationOperator = c.Id;
        }
    }
    private static void Contact(WorldState w, bool increment = true)
    {
        new FieldDomestication(w, new PlanningIndex(w)).Resolve(w, new TurnRecord());
        if (increment) w.Turn++;
    }
    private static TurnRecord Resolve(WorldState w, PhaseExecution execution = null)
        => new TurnResolver(execution).Resolve(w, new Dictionary<int, List<Order>>());

    public static int Run()
    {
        checks = 0;
        EligibilityAndMovement(); ContactRules(); Competition(); BoundaryAndReplay(); LegacyAndConfiguration();
        Console.WriteLine("PASS: " + checks + " field-domestication assertions"); return checks;
    }
    private static void EligibilityAndMovement()
    {
        var w = Fixture(); var c = w.Camps[0]; var h = w.Herds[0];
        Check(w.Config.Species.Where(s => s.CultivationAffinity).Select(s => s.Id).SequenceEqual(new[] { "goat" }), "only represented historically domesticable crop-raiding goats are field-attracted by default");
        var fields = new FieldDomestication(w, new PlanningIndex(w));
        Check(fields.Score(w, h, c.Tile) > 0, "positive owned q attracts selected herd");
        var other = new Herd { Id = w.NextId++, Tile = h.Tile, Species = "goat", Animals = 8 }; w.Herds.Add(other);
        fields = new FieldDomestication(w, new PlanningIndex(w));
        Check(fields.Score(w, h, c.Tile) > 0 && fields.Score(w, other, c.Tile) == 0, "one herd per village with stable-ID tie");
        Check(TurnResolver.HerdDestination(w, h) == h.Tile, "attraction cannot bypass no-hunger/no-thirst/no-wander gate");
        w.Config.WanderChance = 1;
        int nearer = 0;
        for (int seed = 1; seed <= 100; seed++)
        {
            w.Seed = seed; w.Config.FieldDomesticationEnabled = false;
            int control = TurnResolver.HerdDestination(w, h);
            w.Config.FieldDomesticationEnabled = true;
            int attracted = TurnResolver.HerdDestination(w, h);
            if (fields.Score(w, h, attracted) > fields.Score(w, h, control)) nearer++;
        }
        Check(nearer > 0, "positive q changes actual admitted movement toward fields across matched keyed wander samples");
        w.Config.WanderChance = 0;
        w.Config.GetSpecies("goat").Domesticable = false;
        Check(new FieldDomestication(w, new PlanningIndex(w)).Score(w, h, c.Tile) == 0, "affinity does not imply domesticability");
        w.Config.GetSpecies("goat").Domesticable = true; w.Config.GetSpecies("goat").CultivationAffinity = false;
        Check(new FieldDomestication(w, new PlanningIndex(w)).Score(w, h, c.Tile) == 0, "domesticability does not imply affinity");
        w.Config.GetSpecies("goat").CultivationAffinity = true;
        foreach (var t in w.Tiles) t.CultivationCapital = 0;
        Check(new FieldDomestication(w, new PlanningIndex(w)).Score(w, h, c.Tile) == 0, "q=0 gives exactly zero attraction");
        Fields(w, c); c.IsVillage = false;
        Check(new FieldDomestication(w, new PlanningIndex(w)).Score(w, h, c.Tile) == 0, "mobile camps do not emit field attraction");
        c.IsVillage = true;
        h.Tile = w.Map.Radius(c.Tile, 7).First(n => w.Map.Distance(c.Tile, n) == 7);
        w.Herds.Remove(other);
        Check(new FieldDomestication(w, new PlanningIndex(w)).Score(w, h, c.Tile) == 0, "radius six excludes distance seven");
        w.Config.FieldDetectionRadius = 7;
        Check(new FieldDomestication(w, new PlanningIndex(w)).Score(w, h, c.Tile) > 0, "detection radius is configurable");
        h.Tile = c.Tile; w.Config.WanderChance = 1;
        w.Hunters.Add(new Hunter { Id = w.NextId++, SourceCamp = c.Id, SocietyId = c.SocietyId, Player = c.Player, Tile = h.Tile, Population = 4 });
        int threatened = TurnResolver.HerdDestination(w, h);
        w.Config.FieldDomesticationEnabled = false;
        Check(TurnResolver.HerdDestination(w, h) == threatened && threatened != h.Tile, "hunter flee response is unchanged by attraction");
        w.Hunters.Clear(); w.Config.FieldDomesticationEnabled = true;
        foreach (var t in w.Tiles) { t.Freshwater = 0; for (int i = 0; i < 4; i++) t.Seasons[i].MaxFreshwater = 0; }
        int wet = w.Map.Neighbors(h.Tile).First(); w.Tiles[wet].Freshwater = 100000;
        for (int i = 0; i < 4; i++) w.Tiles[wet].Seasons[i].MaxFreshwater = 100000;
        // Adjacent water is already accessible from the current tile. A prior
        // deficit makes this a thirst-response test instead of random wandering.
        h.UnmetFreshwater = 100;
        Check(FreshwaterAccess.Available(w, TurnResolver.HerdDestination(w, h)) > 0, "thirst still selects actual accessible water");
        foreach (var t in w.Tiles) { t.PotentialFood = 0; for (int i = 0; i < 4; i++) t.Seasons[i].MaxPotentialFood = 0; }
        h.UnmetFreshwater = 0; foreach (var t in w.Tiles) { t.Freshwater = 100000; for (int i = 0; i < 4; i++) t.Seasons[i].MaxFreshwater = 100000; }
        w.Tiles[wet].PotentialFood = 100000; for (int i = 0; i < 4; i++) w.Tiles[wet].Seasons[i].MaxPotentialFood = 100000;
        Check(TurnResolver.HerdDestination(w, h) == wet, "hungry herd still seeks forage");
        w.Tiles[wet].ElevationLevel = 10;
        Check(TurnResolver.HerdDestination(w, h) != wet, "attraction cannot enter blocked mountains");
    }
    private static void ContactRules()
    {
        var w = Fixture(6); var c = w.Camps[0]; var h = w.Herds[0];
        Check(new FieldDomestication(w, new PlanningIndex(w)).Score(w, h, c.Tile) > 0, "radius-six herd can sense cultivation outside vision");
        Contact(w); Check(h.FieldContacts == null && h.DomesticCamp < 0, "detection does not grant visibility or contact");
        w.Hunters.Add(new Hunter { Id = w.NextId++, SourceCamp = c.Id, SocietyId = c.SocietyId, Player = 0, Population = 1, Tile = h.Tile });
        Contact(w); Check(h.FieldContacts == null, "remote scout visibility cannot substitute for village contact");
        w.Hunters.Clear();
        h.Tile = c.Tile; Contact(w); Check(h.FieldContacts.Single().Turns == 1 && h.DomesticCamp < 0, "one visible encounter is not ownership");
        Contact(w, false); int count = h.FieldContacts.Single().Turns; Contact(w, false);
        Check(h.FieldContacts.Single().Turns == count, "same boundary never double counts");
        w.Turn++;
        h.Tile = w.Map.Radius(c.Tile, 6).First(n => w.Map.Distance(c.Tile, n) == 6); Contact(w);
        Check(h.FieldContacts == null, "a visibility break erases familiarity");
        h.Tile = c.Tile; Contact(w); Check(h.FieldContacts.Single().Turns == 1, "returning contact restarts at one");
        h.LastHostileTurnPlusOne = w.Turn + 1; Contact(w);
        Check(h.FieldContacts == null && h.DomesticCamp < 0, "any hostile attack blocks and resets contact");
        Contact(w); Check(h.FieldContacts.Single().Turns == 1, "peace must be sustained anew after attack");
        for (int i = 0; i < 8; i++) { h.Tile = i % 2 == 0 ? c.Tile : w.Map.Radius(c.Tile, 6).First(n => w.Map.Distance(c.Tile, n) == 6); Contact(w); }
        Check(h.DomesticCamp < 0 && !c.Technologies[(int)Technology.AnimalHusbandry], "repeated intermittent encounters cannot cause rapid false positives");
        h.Tile = c.Tile; Contact(w); c.IsVillage = false; Contact(w);
        Check(h.FieldContacts == null, "abandoning village breaks contact");
    }
    private static void Competition()
    {
        var w = Fixture(); var c = w.Camps[0]; var h = w.Herds[0];
        var second = new Camp { Id = w.NextId++, SocietyId = c.SocietyId, Player = 0, Tile = w.Map.Neighbors(c.Tile).First(), Population = 12, IsVillage = true };
        w.Camps.Add(second); Ownership.Bind(w); Fields(w, second); w.Tiles[c.Tile].CultivationOperator = c.Id;
        Contact(w); Check(h.FieldContacts.Count == 1 && h.FieldContacts.Single().Turns == 1, "multiple same-Society villages count one herd contact per turn");
        // A hostile group from the other village still blocks the same herd's contact.
        h.LastHostileTurnPlusOne = w.Turn + 1; Contact(w); Check(h.FieldContacts == null, "attack cannot be laundered through a second village");
        var rival = new Camp { Id = w.NextId++, Player = 1, Tile = w.Map.Neighbors(c.Tile).Last(), Population = 12, IsVillage = true };
        w.Camps.Add(rival); Ownership.Bind(w); rival.Technology = Technology.Cultivation; rival.Technologies[(int)Technology.Cultivation] = true; rival.People.Progression.Era = Era.Neolithic;
        Fields(w, rival); w.Tiles[c.Tile].CultivationOperator = c.Id; w.Tiles[second.Tile].CultivationOperator = second.Id;
        var copy = SaveCodec.Load(SaveCodec.Save(w)); copy.Camps.Reverse(); copy.Societies.Reverse(); copy.Herds.Reverse();
        for (int i = 0; i < w.Config.FieldContactTurns; i++) { Contact(w); Contact(copy); }
        Check(h.DomesticCamp == c.Id && h.OwnerSociety == c.SocietyId, "competing Societies resolve once by canonical camp ID");
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(copy), "contact ownership is collection-order independent");
        Check(!rival.Technologies[(int)Technology.AnimalHusbandry], "losing Society receives neither animals nor technology");
    }
    private static void BoundaryAndReplay()
    {
        foreach (int threshold in new[] { 5, 6 })
        foreach (bool knows in new[] { false, true })
        {
            var w = Fixture(); w.Config.FieldContactTurns = threshold; var c = w.Camps[0]; var h = w.Herds[0];
            c.Technologies[(int)Technology.AnimalHusbandry] = knows;
            int id = h.Id, animals = h.Animals, nextId = w.NextId;
            for (int i = 1; i < threshold; i++) { Resolve(w); Check(h.DomesticCamp < 0, "no transfer before configured sustained threshold"); }
            var loaded = SaveCodec.Load(SaveCodec.Save(w)); loaded.Herds.Reverse();
            var r = Resolve(w); Resolve(loaded, new PhaseExecution(4, 1, true, 1));
            Check(h.Id == id && h.Animals == animals && w.Herds.Count == 1 && w.NextId == nextId, "conversion conserves real animals, identity and IDs");
            Check(h.DomesticCamp == c.Id && h.OwnerSociety == c.SocietyId && c.Technologies[(int)Technology.AnimalHusbandry] && !h.OpeningCompatibility, "threshold acquires real herd whether or not Husbandry was known");
            Check(r.ManagedHerds.Count == 0 && !r.Transfers.Any(t => t.Kind == "Animal Product"), "conversion creates no retroactive same-turn production");
            Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "save continuation and scalar/parallel replay are exact through transfer");
            c.Workforce = new PopulationLedger(); c.Workforce.Assign(WorkforceRoles.Herder, 4, c.Population);
            var next = Resolve(w);
            Check(next.ManagedHerds.Single().Herd == id && next.ManagedHerds.Single().StartingAnimals == animals, "existing managed-herd economy accepts animals at next normal boundary");
        }
        // Exercise the actual Hunt hook, including a zero-kill attempted hunt.
        var hostile = Fixture(); hostile.Config.HuntEfficiency = 0;
        var hunter = new Hunter { Id = hostile.NextId++, SourceCamp = hostile.Camps[0].Id, Player = 0, SocietyId = hostile.Camps[0].SocietyId, Tile = hostile.Camps[0].Tile, Population = 4 };
        hostile.Hunters.Add(hunter); var herd = hostile.Herds[0];
        int destination = TurnResolver.HerdDestination(hostile, herd);
        var attack = new TurnResolver().Resolve(hostile, new Dictionary<int, List<Order>> { [0] = new List<Order> { new Order(OrderKind.Move, hunter.Id, destination: destination) } });
        Check(herd.LastHostileTurnPlusOne == hostile.Turn && herd.FieldContacts == null, "real zero-kill hunt marks hostile turn and blocks contact");
        Check(attack.Transfers.Any(t => t.Kind == "Hunting" && t.Food == 0), "zero-kill fixture actually attempted the hunt");
    }
    private static void LegacyAndConfiguration()
    {
        var w = Fixture(); Contact(w); var loaded = SaveCodec.Load(SaveCodec.Save(w));
        Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(loaded), "partial contact save round-trip is exact");
        var old = SaveCodec.Load(WithoutFields(w));
        Check(!old.Config.FieldDomesticationEnabled && old.Config.FieldDetectionRadius == 6 && old.Config.FieldContactTurns == 6
            && old.Herds[0].FieldContacts == null && old.Config.Species.All(s => !s.CultivationAffinity), "legacy saves retain old behavior and no fabricated contacts/species affinities");
        foreach (var pair in new[] { (0, 6), (13, 6), (6, 1), (6, 101) })
        {
            w.Config.FieldDetectionRadius = pair.Item1; w.Config.FieldContactTurns = pair.Item2;
            bool rejected = false; try { w.Config.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid/unbounded field configuration rejected");
        }
    }
    private static byte[] WithoutFields(WorldState world)
    {
        using var input = new MemoryStream(SaveCodec.Save(world)); using var unzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new BinaryReader(unzip); using var output = new MemoryStream();
        using (var zip = new GZipStream(output, CompressionLevel.Fastest, true))
        using (var writer = new BinaryWriter(zip))
        {
            writer.Write(reader.ReadString()); int header = reader.ReadInt32();
            using (var xml = XmlDictionaryReader.CreateBinaryReader(reader.ReadBytes(header), XmlDictionaryReaderQuotas.Max))
            using (var metadata = new MemoryStream())
            {
                var document = XDocument.Load(xml);
                string[] absent = { "FieldDomesticationEnabled", "FieldDetectionRadius", "FieldContactTurns", "CultivationAffinity", "FieldContacts", "LastHostileTurnPlusOne" };
                document.Descendants().Where(e => absent.Contains(e.Name.LocalName)).Remove();
                using (var binary = XmlDictionaryWriter.CreateBinaryWriter(metadata, null, null, false)) document.WriteTo(binary);
                writer.Write((int)metadata.Length); writer.Write(metadata.ToArray());
            }
            unzip.CopyTo(zip);
        }
        return output.ToArray();
    }
}
