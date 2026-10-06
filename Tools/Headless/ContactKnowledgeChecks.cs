using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// Contact knowledge (WP6): per-Society explored static geography and settlement contacts gained
// after the first breakthrough. Covers the sparse set, update/moved/expiry rules, trade and kin
// hooks, observation filtering, persistence and scalar/parallel replay. Owns its worlds and
// counters (lanes run checks concurrently).
internal static class ContactKnowledgeChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Contact knowledge: " + message); }

    // Camps at (column, 5) of a 24x16 wrapped fixture, one player each. Players in `remembering` are Neolithic.
    private static WorldState World(int[] columns, params int[] remembering)
    {
        var w = NeolithicChecks.Fixture(false); w.Camps.Clear();
        for (int i = 0; i < columns.Length; i++) w.Camps.Add(new Camp { Id = w.NextId++, Player = i, Tile = w.Map.At(columns[i], 5), Population = 100, PlantFood = 12000 });
        w.Config.Players = columns.Length; w.Config.MinimumCampSpacing = 1; w.Turn = 0;
        Ownership.Bind(w);
        foreach (int p in remembering) w.Societies.Single(s => s.Player == p).Progression.Era = Era.Neolithic;
        return w;
    }
    private static Society Soc(WorldState w, int player) => w.Societies.Where(s => s.Player == player).OrderBy(s => s.Id).First();
    private static int At(WorldState w, int q) => w.Map.At(q, 5);
    private static void Step(WorldState w, PhaseExecution e = null) { ContactKnowledge.EndTurn(w, e); w.Turn++; }
    private static HashSet<int> Vision(WorldState w, Camp c) => new HashSet<int>(w.Map.Radius(c.Tile, w.Config.VisionRadius));

    public static int Run()
    {
        checks = 0; Config(); Sparse(); Gating(); Sightings(); MovedAndExpiry(); Hooks(); Observations(); Persistence(); Replay(); Hinterland(); Decay(); HinterlandReplay();
        Console.WriteLine("PASS: " + checks + " contact knowledge assertions"); return checks;
    }

    private static void Config()
    {
        var config = new SimulationConfig();
        Check(config.ContactExpiryTurns == 5, "default expiry is five turns");
        config.ContactExpiryTurns = 0;
        bool rejected = false; try { config.Validate(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "expiry below one turn is rejected");
        var serializer = new DataContractSerializer(typeof(SimulationConfig)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { ContactExpiryTurns = 9 }); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<ContactExpiryTurns>9</ContactExpiryTurns>"), "expiry is serialized");
        string legacy = xml.Replace("<ContactExpiryTurns>9</ContactExpiryTurns>", "");
        Check(legacy != xml, "legacy fixture omits expiry");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) Check(((SimulationConfig)serializer.ReadObject(stream)).ContactExpiryTurns == 5, "older saves load the default expiry");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) Check(((SimulationConfig)serializer.ReadObject(stream)).ContactExpiryTurns == 9, "a saved expiry round trips");
        var json = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(FindConfig()), "\"ContactExpiryTurns\":\\s*(\\d+)");
        Check(json.Success && json.Groups[1].Value == "5", "OpeningConfig.json carries the default expiry");
        var d = new SimulationConfig();
        Check(d.HinterlandReferencePopulation == d.StartingPopulation && d.HinterlandRingsPerTurn == 1 && d.HinterlandMaxRadius == 6 && d.ExploredMemoryTurns == 5, "hinterland defaults: reference population = starting population, one ring per turn, radius cap 6, memory 5 turns");
        foreach (var bad in new Action<SimulationConfig>[] { c => c.HinterlandReferencePopulation = 0, c => c.HinterlandRingsPerTurn = -1, c => c.HinterlandRingsPerTurn = 9, c => c.HinterlandMaxRadius = 1, c => c.HinterlandMaxRadius = 9, c => c.ExploredMemoryTurns = 0 })
        { var c = new SimulationConfig(); bad(c); bool refused = false; try { c.Validate(); } catch (ArgumentException) { refused = true; } Check(refused, "an out-of-range hinterland or memory lever is rejected"); }
        string hxml; var hs = new DataContractSerializer(typeof(SimulationConfig));
        using (var stream = new MemoryStream()) { hs.WriteObject(stream, new SimulationConfig { HinterlandMaxRadius = 4, ExploredMemoryTurns = 9, HinterlandRingsPerTurn = 2, HinterlandReferencePopulation = 30 }); hxml = Encoding.UTF8.GetString(stream.ToArray()); }
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(hxml))) { var back = (SimulationConfig)hs.ReadObject(stream); Check(back.HinterlandMaxRadius == 4 && back.ExploredMemoryTurns == 9 && back.HinterlandRingsPerTurn == 2 && back.HinterlandReferencePopulation == 30, "hinterland levers round trip"); }
        string hlegacy = Regex.Replace(hxml, "<(HinterlandMaxRadius|ExploredMemoryTurns|HinterlandRingsPerTurn|HinterlandReferencePopulation)>\\d+</\\1>", "");
        Check(hlegacy != hxml, "legacy fixture omits the hinterland levers");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(hlegacy))) { var back = (SimulationConfig)hs.ReadObject(stream); Check(back.HinterlandMaxRadius == 6 && back.ExploredMemoryTurns == 5 && back.HinterlandRingsPerTurn == 1 && back.HinterlandReferencePopulation == 24, "older saves load the hinterland defaults"); }
        foreach (var key in new[] { "HinterlandReferencePopulation", "HinterlandRingsPerTurn", "HinterlandMaxRadius", "ExploredMemoryTurns" })
            Check(Regex.IsMatch(File.ReadAllText(FindConfig()), "\"" + key + "\":\\s*\\d+"), "OpeningConfig.json carries " + key);
    }
    private static string FindConfig()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        { string path = Path.Combine(dir.FullName, "Assets", "_Anthropocene", "Resources", "OpeningConfig.json"); if (File.Exists(path)) return path; }
        throw new FileNotFoundException("OpeningConfig.json");
    }

    private static void Sparse()
    {
        var set = new ExploredTiles(24);
        Check(set.Count == 0 && !set.Contains(0) && set.Chunks.Count == 0, "a new set is empty");
        foreach (int t in new[] { 5 * 24 + 23, 5 * 24 + 0, 0, 15 * 24 + 23, 5 * 24 + 0 }) set.Add(t);
        Check(set.Count == 4 && set.Contains(0) && set.Contains(5 * 24) && set.Contains(5 * 24 + 23) && set.Contains(15 * 24 + 23), "marked tiles are contained once, including the wrapped seam");
        Check(!set.Contains(1) && !set.Contains(5 * 24 + 1), "unmarked tiles are absent");
        Check(set.Chunks.SequenceEqual(set.Chunks.OrderBy(k => k)) && set.Chunks.Distinct().Count() == set.Chunks.Count, "block keys are sorted and unique");
        Check(set.Tiles().OrderBy(t => t).SequenceEqual(new[] { 0, 5 * 24, 5 * 24 + 23, 15 * 24 + 23 }), "iterated tile ids decode to the marked ids");
        var clone = set.Clone(); clone.Add(77);
        Check(set.Count == 4 && clone.Count == 5 && !set.Contains(77), "a clone is independent of the original");
        // Width not a multiple of eight, and a large map: memory follows explored area.
        var wide = new ExploredTiles(2048);
        for (int dr = 0; dr < 5; dr++) for (int dq = 0; dq < 5; dq++) wide.Add((300 + dr) * 2048 + 1000 + dq);
        Check(wide.Count == 25 && wide.Chunks.Count <= 4 && wide.ApproximateBytes <= 48, "25 tiles in a 2048-wide map occupy at most four blocks");
        var odd = new ExploredTiles(13); var all = Enumerable.Range(0, 13 * 11).ToArray();
        foreach (int t in all) odd.Add(t);
        Check(odd.Count == all.Length && odd.Tiles().OrderBy(t => t).SequenceEqual(all), "a width that is not a multiple of eight round trips every tile");
    }

    private static void Gating()
    {
        var w = World(new[] { 5, 9 }, 1);   // player 0 stays Paleolithic
        Check(!ContactKnowledge.Remembers(Soc(w, 0)) && ContactKnowledge.Remembers(Soc(w, 1)), "only the Neolithic Society remembers");
        Step(w);
        Check(Soc(w, 0).Memory == null, "a Paleolithic Society remembers nothing");
        Check(Soc(w, 1).Memory != null && Soc(w, 1).Memory.Explored.Count == Vision(w, w.Camps[1]).Count, "the Neolithic Society explores exactly its vision");
        var owner = new AgentObservationBuilder().Build(w, 0);
        Check(owner.RememberedTiles.Count == 0 && owner.KnownSettlements.Count == 0, "a Paleolithic observation has no remembered knowledge");
        // The first breakthrough (technology without the era flag) also starts memory.
        var w2 = World(new[] { 5 }); Soc(w2, 0).Technology = Technology.Cultivation;
        Step(w2); Check(Soc(w2, 0).Memory != null, "a Society with a first technology remembers");
        // A Society with nobody left to see anything explores nothing.
        var w3 = World(new[] { 5, 15 }, 0); w3.Camps[0].Population = 0; Step(w3);
        Check(Soc(w3, 0).Memory != null && Soc(w3, 0).Memory.Explored.Count == 0, "a depopulated Society marks no tiles");
    }

    private static void Sightings()
    {
        var w = World(new[] { 5, 10 }, 0, 1);  // distance 5: out of vision
        Step(w);
        var a = Soc(w, 0); var b = Soc(w, 1);
        Check(a.Memory.Contacts.Count == 0 && b.Memory.Contacts.Count == 0, "no contact without a sighting");
        Check(a.Memory.Explored.Contains(w.Camps[0].Tile) && !a.Memory.Explored.Contains(w.Camps[1].Tile), "exploration covers only what was in vision");
        w.Camps[1].Tile = At(w, 7);  // distance 2
        Step(w);
        var seen = a.Memory.Contacts.Single();
        Check(seen.Camp == w.Camps[1].Id && seen.Owner == 1 && seen.Tile == At(w, 7) && seen.Turn == 1 && seen.Source == ContactSource.Sighting && !seen.Moved, "a visible foreign camp is recorded with owner, tile, turn and source");
        Check(b.Memory.Contacts.Single().Camp == w.Camps[0].Id, "the sighting is mutual when both see");
        w.Camps[1].Tile = At(w, 6);  // still visible elsewhere: refreshed, not moved
        Step(w);
        seen = a.Memory.Contacts.Single();
        Check(seen.Tile == At(w, 6) && seen.Turn == 2 && !seen.Moved, "a camp seen at a new tile is refreshed there");
        // Own-Society camps (and Paleolithic bystanders) never become contacts.
        var own = World(new[] { 5, 7 }, 0); own.Camps[1].SocietyId = own.Camps[0].SocietyId; own.Camps[1].Bind(Soc(own, 0));
        Step(own); Check(Soc(own, 0).Memory.Contacts.Count == 0, "a camp of the same Society is not a foreign contact");
        // Hunter vision counts: a dispatched hunter group sees a distant camp.
        var h = World(new[] { 2, 12 }, 0);
        h.Hunters.Add(new Hunter { Id = h.NextId++, SourceCamp = h.Camps[0].Id, SocietyId = h.Camps[0].SocietyId, Player = 0, Population = 4, Tile = At(h, 10) });
        Step(h);
        Check(Soc(h, 0).Memory.Contacts.Single().Tile == At(h, 12), "a hunter group's vision records a distant camp");
        Check(Soc(h, 0).Memory.Explored.Contains(At(h, 10)) && Soc(h, 0).Memory.Explored.Contains(At(h, 11)), "hunter vision is explored");
    }

    private static void MovedAndExpiry()
    {
        var w = World(new[] { 5, 7 }, 0); w.Config.ContactExpiryTurns = 3;
        var a = Soc(w, 0); var far = w.Camps[1];
        Step(w);
        Check(a.Memory.Contacts.Single().Tile == At(w, 7), "the neighbour is recorded");
        far.Tile = At(w, 12); Step(w);  // left: its old tile is in view and empty
        var contact = a.Memory.Contacts.Single();
        Check(contact.Moved && contact.Tile == -1 && contact.Turn == 0, "a camp missing from its recorded visible tile is marked moved, location unknown, and not refreshed");
        Check(new AgentObservationBuilder().Build(w, 0).KnownSettlements.Single().Tile == -1, "the observation reports an unknown location for a moved camp");
        Step(w);  // update at turn 2: age 2 < 3
        Check(a.Memory.Contacts.Single().Turn == 0 && a.Memory.Contacts.Single().Moved, "a moved contact is never refreshed without a new sighting");
        Step(w);  // update at turn 3: age 3 >= ContactExpiryTurns
        Check(a.Memory.Contacts.Count == 0, "a contact not refreshed for ContactExpiryTurns is dropped");
        // Expiry timing for a contact outside vision: learned by trade at turn t, dropped exactly at t + N.
        var e = World(new[] { 5, 15 }, 0); e.Config.ContactExpiryTurns = 3; e.Turn = 10;
        ContactKnowledge.RecordTrade(e, Soc(e, 0), e.Camps[1]);
        var counts = new List<int>();
        for (int i = 0; i < 4; i++) { Step(e); counts.Add(Soc(e, 0).Memory.Contacts.Count); }
        Check(counts.SequenceEqual(new[] { 1, 1, 1, 0 }), "a traded contact outside vision survives three updates and is dropped at turn 10 + 3");
        // A refreshed contact keeps living.
        var r = World(new[] { 5, 7 }, 0); r.Config.ContactExpiryTurns = 2;
        for (int i = 0; i < 6; i++) Step(r);
        Check(Soc(r, 0).Memory.Contacts.Count == 1 && Soc(r, 0).Memory.Contacts[0].Turn == 5, "continuous sightings keep a contact alive");
        // A camp re-sighted after moving is found again.
        var m = World(new[] { 5, 7 }, 0); var moved = m.Camps[1];
        Step(m); moved.Tile = At(m, 12); Step(m); Check(Soc(m, 0).Memory.Contacts.Single().Moved, "moved (fixture)");
        moved.Tile = At(m, 6); Step(m);
        var back = Soc(m, 0).Memory.Contacts.Single();
        Check(!back.Moved && back.Tile == At(m, 6) && back.Turn == 2, "a moved camp sighted again is located again");
        // A destroyed camp in view is handled as moved.
        var d = World(new[] { 5, 7 }, 0); Step(d); d.Camps.RemoveAt(1); Step(d);
        Check(Soc(d, 0).Memory.Contacts.Single().Moved, "a camp that no longer exists reads as moved");
    }

    private static void Hooks()
    {
        var w = World(new[] { 5, 15, 20 }, 0, 1);  // player 2 is Paleolithic
        var a = Soc(w, 0); var far = w.Camps[1]; w.Turn = 4;
        ContactKnowledge.RecordTrade(w, a, far);
        var c = a.Memory.Contacts.Single();
        Check(c.Camp == far.Id && c.Owner == 1 && c.Tile == far.Tile && c.Turn == 4 && c.Source == ContactSource.Trade && !c.Moved, "a trade records the partner's location with its source");
        Step(w); Check(a.Memory.Contacts.Single().Tile == far.Tile && a.Memory.Contacts.Single().Turn == 4, "the update never overwrites a contact outside vision");
        ContactKnowledge.RecordTrade(w, Soc(w, 2), w.Camps[0]);
        Check(Soc(w, 2).Memory == null, "a Paleolithic Society ignores a trade record");
        ContactKnowledge.RecordTrade(w, a, w.Camps[0]);
        Check(a.Memory.Contacts.Count == 1, "a Society never records its own camp");

        // Kin: the daughter's explored set equals the parent's at fission; contacts add the mutual kin records.
        var k = World(new[] { 5, 15 }, 0); Step(k); k.Turn = 6;
        var parent = Soc(k, 0); var stranger = k.Camps[1];
        ContactKnowledge.RecordTrade(k, parent, stranger);
        var parentTiles = parent.Memory.Explored.Tiles().OrderBy(t => t).ToArray();
        var daughter = new Society { Id = k.NextId++, Player = 0, PolicySeed = 1 }; daughter.Progression.Era = Era.Neolithic;
        k.Societies.Add(daughter);
        var daughterCamp = new Camp { Id = k.NextId++, Player = 0, SocietyId = daughter.Id, Tile = At(k, 8), Population = 10 }; k.Camps.Add(daughterCamp); daughterCamp.Bind(daughter);
        Ownership.Bind(k);
        var parentCamp = k.Camps[0];
        ContactKnowledge.RecordKin(k, parent, daughter, parentCamp, daughterCamp);
        Check(daughter.Memory.Explored.Tiles().OrderBy(t => t).SequenceEqual(parentTiles), "the daughter's explored tiles equal the parent's at fission");
        Check(!ReferenceEquals(daughter.Memory.Explored, parent.Memory.Explored) && !ReferenceEquals(daughter.Memory.Contacts, parent.Memory.Contacts), "the daughter holds its own copy");
        var dc = daughter.Memory.Contacts;
        Check(dc.Count == 2 && dc.Single(x => x.Camp == stranger.Id).Source == ContactSource.Trade && dc.Single(x => x.Camp == parentCamp.Id).Source == ContactSource.Kin && dc.Single(x => x.Camp == parentCamp.Id).Tile == parentCamp.Tile, "the daughter inherits the parent's contacts and records the parent as kin");
        var pk = parent.Memory.Contacts.Single(x => x.Camp == daughterCamp.Id);
        Check(pk.Source == ContactSource.Kin && pk.Tile == daughterCamp.Tile && pk.Turn == 6 && parent.Memory.Contacts.Count == 2, "the parent records the daughter as kin");
        daughter.Memory.Explored.Add(1); Check(!parent.Memory.Explored.Contains(1), "later daughter exploration does not reach the parent");
        Check(dc.Select(x => x.Camp).SequenceEqual(dc.Select(x => x.Camp).OrderBy(x => x)), "contacts stay in camp-id order");
        // A daughter that has not passed its first breakthrough inherits nothing; the parent still records it.
        var k2 = World(new[] { 5 }, 0); Step(k2);
        var cold = new Society { Id = k2.NextId++, Player = 0, PolicySeed = 2 }; k2.Societies.Add(cold);
        var coldCamp = new Camp { Id = k2.NextId++, Player = 0, SocietyId = cold.Id, Tile = At(k2, 9), Population = 8 }; k2.Camps.Add(coldCamp); coldCamp.Bind(cold); Ownership.Bind(k2);
        ContactKnowledge.RecordKin(k2, Soc(k2, 0), cold, k2.Camps[0], coldCamp);
        Check(cold.Memory == null && Soc(k2, 0).Memory.Contacts.Any(x => x.Camp == coldCamp.Id), "a Paleolithic daughter remembers nothing");
    }

    private static readonly string[] Live = { "food", "vegetation", "freshwater", "biomass", "productivity", "moisture", "condition", "herd", "occupant", "cultivat", "fishery", "oasis", "gatherable", "saltwater", "cover", "service" };
    private static void Observations()
    {
        var w = World(new[] { 5, 13 }, 0, 1);
        Step(w);
        var own = w.Camps[0]; int old = own.Tile; own.Tile = At(w, 9);   // walks 4 tiles east
        var stranger = w.Camps[1]; stranger.Tile = At(w, 11);            // out of sight of the old position, in sight of the new one
        var herdTile = At(w, 4);
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = herdTile, Species = w.Config.Species[0].Id, Animals = 50, Capacity = 50 });
        Step(w);
        var o = new AgentObservationBuilder().Build(w, 0);
        Check(o.RememberedTiles.Count > 0 && o.RememberedTiles.Keys.All(t => !o.Tiles.ContainsKey(t)), "remembered tiles lie outside current vision");
        Check(o.RememberedTiles.ContainsKey(old) && o.Tiles.Keys.All(t => Soc(w, 0).Memory.Explored.Contains(t)), "the previous camp area is remembered and current vision is explored");
        Check(o.Tiles.Keys.SequenceEqual(Vision(w, own).OrderBy(t => t)), "live tiles remain exactly the current vision");
        var rememberedDto = typeof(RememberedTileObservation).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name.ToLowerInvariant()).ToArray();
        var dtoFields = typeof(ObservedRememberedTileDto).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name.ToLowerInvariant()).ToArray();
        Check(!rememberedDto.Concat(dtoFields).Any(n => Live.Any(n.Contains)), "remembered tile types declare no live fields");
        // Changing live state on remembered tiles changes nothing the player can see.
        string Remembered(AgentObservation x) => string.Join(";", x.RememberedTiles.OrderBy(p => p.Key).Select(p => { var t = p.Value; return t.Id + "," + t.Environment + "," + t.Relief + "," + t.Elevation + "," + t.ElevationLevel + "," + t.RidgeMask + "," + t.WaterBody + "," + t.Land + "," + t.River; }));
        string snapshot = Remembered(o), json = ObservationContract.Serialize(o, own.People.Mandate);
        var liveTile = w.Tiles[herdTile]; liveTile.Food = 1; liveTile.PotentialFood = 5; liveTile.Freshwater = 0; liveTile.CurrentVegetation = VegetationTier.High; liveTile.Cultivated = true;
        w.Camps.Add(new Camp { Id = w.NextId++, Player = 1, SocietyId = Soc(w, 1).Id, Tile = At(w, 3), Population = 5 }); w.Camps.Last().Bind(Soc(w, 1)); w.Herds.Last().Tile = At(w, 3);
        var o2 = new AgentObservationBuilder().Build(w, 0);
        Check(Remembered(o2) == snapshot, "live stock, vegetation, cultivation and occupant changes never alter remembered tiles");
        Check(o2.Herds.All(h => !o2.RememberedTiles.ContainsKey(h.Tile)) && o2.Rivals.All(r => !o2.RememberedTiles.ContainsKey(r.Tile)) && o2.Hunters.All(h => !o2.RememberedTiles.ContainsKey(h.Tile)), "no herd, rival or hunter is reported on a remembered tile");
        Check(TileDetails.Occupants(o2, herdTile).Length == 0 && TileDetails.IsRemembered(o2, herdTile), "a remembered tile has no occupants");
        string text = TileDetails.Describe(o2, herdTile, new Order[0]);
        Check(text.StartsWith("REMEMBERED") && !text.Contains("FOOD") && !text.Contains("VEGETATION") && !text.Contains("OCCUPANTS") && !text.Contains("HERD") && !text.Contains("Cultivated"), "the remembered description shows static geography only");
        Check(TileDetails.Describe(o2, At(w, 20), new Order[0]) == "", "an unexplored tile describes nothing");
        // Known settlements: the stranger seen from the new position is listed with its tile, turn and source.
        var known = o.KnownSettlements.Single();
        Check(known.Camp == stranger.Id && known.Owner == 1 && known.Tile == At(w, 11) && known.Turn == 1 && known.Source == ContactSource.Sighting && !known.Moved, "a known settlement carries camp, owner, tile, turn and source");
        // It walks out of vision: the snapshot stays at the old tile until that tile is seen empty.
        stranger.Tile = At(w, 19); own.Tile = At(w, 7);   // the recorded tile 11 is out of vision from tile 7
        Step(w);
        var o3 = new AgentObservationBuilder().Build(w, 0);
        var stale = o3.KnownSettlements.Single();
        Check(stale.Tile == At(w, 11) && !stale.Moved && stale.Turn == 1, "an out-of-vision snapshot keeps its last known tile and is not refreshed");
        Check(o3.Rivals.Count == 0 && TileDetails.KnownSettlementsAt(o3, At(w, 11)).Length == 1 && TileDetails.Describe(o3, At(w, 11), new Order[0]).Contains("KNOWN SETTLEMENT") && TileDetails.Describe(o3, At(w, 11), new Order[0]).Contains("current state unknown"), "a remembered settlement is labelled as a snapshot and is not a live rival");
        // DTO round trip at the current owner-knowledge schema.
        var dto = ObservationContract.Deserialize(ObservationContract.Serialize(o3, own.People.Mandate));
        Check(dto.Schema == 20 && AgentObservation.SchemaVersion == 20 && dto.RememberedTiles.Length == o3.RememberedTiles.Count && dto.KnownSettlements.Single().Camp == stranger.Id && dto.KnownSettlements.Single().Source == ContactSource.Sighting, "the serialized observation carries remembered tiles and known settlements at schema 20");
        Check(json.Contains("\"Schema\":20") && json.Contains("RememberedTiles") && json.Contains("KnownSettlements") && json.Contains("FishingGrounds") && json.Contains("FishingEnabled"), "schema 20 names remembered contacts and filtered Fishing observation blocks");
        // Another Society's knowledge is never visible: the Paleolithic fixture player sees none.
        var p = World(new[] { 5, 9 }, 1); Step(p);
        Check(new AgentObservationBuilder().Build(p, 0).RememberedTiles.Count == 0, "a Society's knowledge is private to it");
    }

    private static string Key(WorldState w) => SaveCodec.Fingerprint(w);
    private static string Memory(WorldState w) => string.Join("|", w.Societies.OrderBy(s => s.Id).Select(s => s.Id + ":" + (s.Memory == null ? "-" : string.Join(",", s.Memory.Explored.Chunks) + "/" + string.Join(",", s.Memory.Explored.Words) + "/" + (s.Memory.Explored.Turns == null ? "-" : string.Join(",", s.Memory.Explored.Turns)) + "/" + string.Join(",", s.Memory.Contacts.Select(c => c.Camp + "@" + c.Tile + "t" + c.Turn + c.Source + c.Moved)))));

    private static void Persistence()
    {
        var w = World(new[] { 5, 7, 18 }, 0, 1); Step(w); w.Camps[1].Tile = At(w, 12); Step(w);
        ContactKnowledge.RecordTrade(w, Soc(w, 0), w.Camps[2]);
        var loaded = SaveCodec.Load(SaveCodec.Save(w));
        Check(Memory(loaded) == Memory(w) && Memory(w).Contains("Trade"), "memory round trips exactly through save and load");
        Check(Key(loaded) == Key(w), "the fingerprint survives the round trip");
        Check(loaded.Societies.Single(s => s.Player == 2).Memory == null, "a Society without memory loads without any");
        var before = Key(w); Soc(w, 0).Memory.Explored.Add(200);
        Check(Key(w) != before, "the fingerprint covers remembered knowledge");
        // An older save has no memory block at all: the field is absent and loads empty.
        var serializer = new DataContractSerializer(typeof(Society)); string xml;
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, Soc(w, 0)); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<Memory>"), "a Society with memory serializes the block");
        string legacy = Regex.Replace(xml, "<Memory>.*</Memory>", "", RegexOptions.Singleline);
        Check(legacy != xml && !legacy.Contains("Memory"), "legacy fixture omits the block");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) Check(((Society)serializer.ReadObject(stream)).Memory == null, "a save without the block loads with no knowledge");
        // The legacy world keeps working: the next turn starts remembering.
        var old = World(new[] { 5, 7 }, 0, 1); Check(Soc(old, 0).Memory == null, "a fresh Society has no memory"); Step(old);
        Check(Soc(old, 0).Memory != null && Soc(old, 0).Memory.Contacts.Count == 1, "a Society without memory begins remembering on its next update");
        // Continuation: uninterrupted run equals save at turn 2 and continued (real resolution).
        var a = Scenario(); var b = Scenario();
        var resolver = new TurnResolver();
        for (int t = 0; t < 5; t++)
        {
            Advance(a, resolver, t);
            if (t == 1) b = SaveCodec.Load(SaveCodec.Save(a));
            else if (t > 1) Advance(b, resolver, t);
            if (t > 1) Check(Key(a) == Key(b) && Memory(a) == Memory(b), "continued run equals the uninterrupted run after turn " + t);
        }
        Check(Memory(a).Contains("Sighting"), "the continuation exercises resolved sightings");
    }

    // Four camps: players 0-2 Neolithic, 3 Paleolithic; movers walk toward and away from each other.
    private static WorldState Scenario() { var w = World(new[] { 2, 7, 12, 17 }, 0, 1, 2); w.Config.MinimumCampSpacing = 1; return w; }
    private static void Advance(WorldState w, TurnResolver resolver, int turn)
    {
        var camps = w.Camps.OrderBy(c => c.Id).ToArray(); var orders = new Dictionary<int, List<Order>>();
        // Columns per turn for camps 0-2: 0 and 1 close in and meet; 1 then walks away past 0's vision; 2 walks toward them.
        int[][] dq = { new[] { 1, -1, -1 }, new[] { 1, -1, -1 }, new[] { 0, 1, -1 }, new[] { 0, 1, -1 }, new[] { 0, 1, -1 }, new[] { 0, 1, -1 } };
        for (int k = 0; k < 3; k++)
        {
            if (dq[turn][k] == 0) continue;
            int dest = w.Map.At(camps[k].Tile % w.Map.Width + dq[turn][k], camps[k].Tile / w.Map.Width);
            orders[camps[k].Player] = new List<Order> { new Order(OrderKind.Move, camps[k].Id, destination: dest) };
        }
        resolver.Resolve(w, orders);
    }

    private static void Replay()
    {
        var worlds = new[] { Scenario(), Scenario() };
        worlds[1].Camps.Reverse(); worlds[1].Societies.Reverse();
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 3, true, 1) };
        for (int t = 0; t < 6; t++)
        {
            for (int i = 0; i < 2; i++) Advance(worlds[i], new TurnResolver(workers[i]), t);
            Check(Key(worlds[0]) == Key(worlds[1]) && Memory(worlds[0]) == Memory(worlds[1]), "scalar and reversed 4-worker replay with reordered collections agree after turn " + t);
        }
        Check(Memory(worlds[0]).Contains("Sighting") && Memory(worlds[0]).Contains("@-1"), "the replay exercises sightings and a moved contact");
        // The update alone, on a larger world, is worker- and order-independent.
        var cols = Enumerable.Range(0, 8).Select(i => i * 3).ToArray();
        WorldState Big() { var w = World(cols, Enumerable.Range(0, 8).ToArray()); return w; }
        var x = Big(); var y = Big(); y.Camps.Reverse(); y.Societies.Reverse(); y.Hunters.Reverse();
        for (int t = 0; t < 3; t++)
        {
            foreach (var w in new[] { x, y }) foreach (var c in w.Camps.Where(c => c.Player % 2 == 0)) c.Tile = w.Map.At((c.Tile % w.Map.Width + 1) % w.Map.Width, c.Tile / w.Map.Width);
            Step(x, new PhaseExecution()); Step(y, new PhaseExecution(4, 2, true, 1));
        }
        Check(Memory(x) == Memory(y) && Key(x) == Key(y) && x.Societies.All(s => s.Memory.Contacts.Count > 0), "the update is identical across workers and collection order");
    }

    private static HashSet<int> Explored(Society s) => new HashSet<int>(s.Memory.Explored.Tiles());
    private static HashSet<int> Disc(WorldState w, int tile, int r) => new HashSet<int>(w.Map.Radius(tile, r));

    // Hinterland: a stationary camp's explored radius grows by one ring per stationary turn up to the population cap and the maximum.
    private static void Hinterland()
    {
        var cfg = new SimulationConfig();
        int R(int turns, int people) => ContactKnowledge.HinterlandRadius(cfg, turns, people);
        Check(R(0, 24) == 2 && R(5, 24) == 2 && R(0, 1000) == 2, "the hinterland starts at the vision radius and a starting-size camp never outgrows it");
        Check(R(1, 96) == 3 && R(2, 96) == 4 && R(9, 96) == 4, "a stationary camp gains one ring per turn up to VisionRadius * sqrt(N / reference) (96 people: 4)");
        Check(R(3, 216) == 5 && R(4, 216) == 6 && R(30, 216) == 6 && R(30, 5000) == 6, "the cap is the smaller of the population cap and the maximum radius 6");
        cfg.HinterlandRingsPerTurn = 0; Check(R(9, 5000) == 2, "zero rings per turn disables growth");
        cfg.HinterlandRingsPerTurn = 3; cfg.HinterlandMaxRadius = 4; Check(R(1, 5000) == 4 && R(0, 5000) == 2, "the maximum radius and the ring rate are levers");
        // Ring growth in a world: the explored set is exactly the disc of the hinterland radius around the camp.
        var w = World(new[] { 12 }, 0); var camp = w.Camps[0]; camp.Population = 216; var society = Soc(w, 0); int home = camp.Tile;
        Step(w); Check(Explored(society).SetEquals(Disc(w, home, 2)), "a camp that just arrived explores only its vision");
        camp.StationaryTurns = 1; Step(w); Check(Explored(society).SetEquals(Disc(w, home, 3)), "after one stationary turn the hinterland is radius 3");
        camp.StationaryTurns = 2; Step(w); camp.StationaryTurns = 3; Step(w); Check(Explored(society).SetEquals(Disc(w, home, 5)), "radius 5 after three stationary turns");
        camp.StationaryTurns = 40; Step(w); Check(Explored(society).SetEquals(Disc(w, home, 6)), "the population and maximum caps stop it at 6 for 216 people");
        Check(Disc(w, home, 6).Count <= 3 * 6 * 6 + 3 * 6 + 1, "a radius-6 hinterland marks at most 127 tiles (the latitude clips this fixture), the bounded O(r^2) disc");
        var small = World(new[] { 12 }, 0); small.Camps[0].StationaryTurns = 40; small.Camps[0].Population = 24; Step(small);
        Check(Explored(Soc(small, 0)).SetEquals(Disc(small, small.Camps[0].Tile, 2)), "24 people never explore beyond their vision, however long they stay");
        // A move resets the hinterland to vision at the new site; the old area stays remembered until it decays.
        int away = At(w, 22); camp.Tile = away; camp.StationaryTurns = 0; Step(w);
        var ring3 = w.Map.Radius(away, 3).First(t => w.Map.Distance(away, t) == 3 && w.Map.Distance(home, t) > 6);
        Check(Explored(society).Contains(home) && Disc(w, away, 2).IsSubsetOf(Explored(society)) && !Explored(society).Contains(ring3), "after a move the camp explores only its vision at the new tile");
        // Hunters never get a hinterland.
        var h = World(new[] { 2 }, 0); h.Camps[0].Population = 216; h.Camps[0].StationaryTurns = 0;
        h.Hunters.Add(new Hunter { Id = h.NextId++, SourceCamp = h.Camps[0].Id, SocietyId = h.Camps[0].SocietyId, Player = 0, Population = 4, Tile = At(h, 14) }); Step(h);
        Check(Explored(Soc(h, 0)).SetEquals(Disc(h, h.Camps[0].Tile, 2).Union(Disc(h, At(h, 14), 2))), "hunter groups see only their vision radius");
        // The hinterland feeds fission candidates: a settled camp knows the distance-3 and -4 rings, a fresh one does not.
        var f = World(new[] { 12 }, 0); f.Camps[0].Population = 216; f.Camps[0].StationaryTurns = 2; Step(f);
        var rings = f.Map.Radius(f.Camps[0].Tile, 4).Where(t => f.Map.Distance(f.Camps[0].Tile, t) >= 3).ToArray();
        Check(rings.Length > 0 && rings.All(t => Soc(f, 0).Memory.Explored.Contains(t)), "a settled camp remembers the distance-3 and -4 rings a fission search needs");
    }

    // Decaying memory: blocks not refreshed for ExploredMemoryTurns are forgotten; revisits refresh; hinterlands refresh every turn; old saves start fresh.
    private static void Decay()
    {
        var w = World(new[] { 12 }, 0); w.Config.ExploredMemoryTurns = 3; var camp = w.Camps[0]; var s = Soc(w, 0); int home = camp.Tile, far = At(w, 2);
        Step(w);  // turn 0 marks home
        camp.Tile = far; for (int t = 1; t <= 2; t++) { Step(w); Check(s.Memory.Explored.Contains(home), "a trail is remembered while younger than ExploredMemoryTurns (turn " + t + ")"); }
        Step(w);  // turn 3: home was last refreshed at turn 0, age 3
        Check(!s.Memory.Explored.Contains(home) && s.Memory.Explored.Contains(far) && Disc(w, far, 2).IsSubsetOf(Explored(s)), "an old trail is forgotten after ExploredMemoryTurns and the current area stays");
        var o = new AgentObservationBuilder().Build(w, 0);
        Check(!o.RememberedTiles.ContainsKey(home) && o.RememberedTiles.Keys.All(t => s.Memory.Explored.Contains(t)), "forgotten tiles leave the observation's remembered tiles");
        // A revisit refreshes: go back at turn 4 and the block's clock restarts.
        camp.Tile = home; Step(w); Check(Explored(s).IsSupersetOf(Disc(w, home, 2)), "a revisit re-explores the area");
        camp.Tile = far; Step(w); Step(w); Check(s.Memory.Explored.Contains(home), "a revisited block starts its memory clock again");
        Step(w); Check(!s.Memory.Explored.Contains(home), "and is forgotten again three turns later");
        // Block granularity: any refresh within an 8x8 block keeps the whole block (documented approximation).
        var g = World(new[] { 12 }, 0); g.Config.ExploredMemoryTurns = 3; var gc = g.Camps[0]; Step(g);
        Check(Explored(Soc(g, 0)).Contains(g.Map.At(14, 5)), "fixture: (14,5) is explored in block (1, 0)");
        gc.Tile = g.Map.At(22, 5); Step(g); Step(g); gc.Tile = g.Map.At(9, 5); Step(g);  // turn 3 sees x 7..11, refreshing the block that holds x 8..15
        Check(Explored(Soc(g, 0)).Contains(g.Map.At(14, 5)), "a refresh of one tile keeps its whole block alive");
        // A settlement keeps its hinterland by refreshing it every turn.
        var v = World(new[] { 12 }, 0); v.Config.ExploredMemoryTurns = 2; var vc = v.Camps[0]; vc.Population = 216;
        for (int t = 0; t < 12; t++) { vc.StationaryTurns = t; Step(v); }
        Check(Explored(Soc(v, 0)).SetEquals(Disc(v, vc.Tile, 6)), "a stationary settlement keeps its full hinterland for as long as it stays");
        // Old saves: no stamps. Nothing is forgotten the moment they load; blocks are stamped at the next update and age from there.
        var old = World(new[] { 12 }, 0); old.Config.ExploredMemoryTurns = 3; Step(old);
        var oc = old.Camps[0]; var os = Soc(old, 0); oc.Tile = At(old, 2); os.Memory.Explored.Turns = null; old.Turn = 40;
        Check(Explored(os).Contains(At(old, 12)), "a memory without stamps still reads");
        Step(old); Check(Explored(os).Contains(At(old, 12)) && os.Memory.Explored.TurnOf(At(old, 12)) == 40, "an old save forgets nothing at its first update and stamps its blocks with that turn");
        Step(old); Step(old); Check(Explored(os).Contains(At(old, 12)), "and ages from the load turn");
        Step(old); Check(!Explored(os).Contains(At(old, 12)), "forgetting resumes after ExploredMemoryTurns");
        // Legacy XML: the Turns list is absent and loads null.
        var sx = new DataContractSerializer(typeof(Society)); string xml;
        var xs = World(new[] { 12 }, 0); Step(xs);
        using (var stream = new MemoryStream()) { sx.WriteObject(stream, Soc(xs, 0)); xml = Encoding.UTF8.GetString(stream.ToArray()); }
        Check(xml.Contains("<Turns"), "stamps are serialized");
        string legacy = Regex.Replace(xml, "<Turns[^>]*>.*?</Turns>", "", RegexOptions.Singleline);
        Check(legacy != xml, "legacy fixture omits the stamps");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) Check(((Society)sx.ReadObject(stream)).Memory.Explored.Turns == null, "a save without stamps loads with none");
        // Round trip keeps the stamps; the clone is independent.
        var rt = SaveCodec.Load(SaveCodec.Save(w)); Check(Memory(rt) == Memory(w) && Key(rt) == Key(w) && Soc(rt, 0).Memory.Explored.Turns.SequenceEqual(s.Memory.Explored.Turns), "stamps round trip exactly through save and load");
        var clone = s.Memory.Clone(); clone.Explored.Forget(1000, 1);
        Check(clone.Explored.Chunks.Count == 0 && s.Memory.Explored.Chunks.Count > 0 && s.Memory.Explored.Turns.Count == s.Memory.Explored.Chunks.Count, "a cloned memory is independent");
        // Paleolithic Societies still remember nothing.
        var p = World(new[] { 12 }); Step(p); Check(Soc(p, 0).Memory == null, "a Society before its first breakthrough has no memory, hinterland or not");
    }

    // The new rules are identical across workers and collection order.
    private static void HinterlandReplay()
    {
        var cols = Enumerable.Range(0, 6).Select(i => 2 + i * 4).ToArray();
        WorldState Make() { var w = World(cols, Enumerable.Range(0, 6).ToArray()); w.Config.ExploredMemoryTurns = 2; foreach (var c in w.Camps) c.Population = 60 + 40 * c.Player; return w; }
        var x = Make(); var y = Make(); y.Camps.Reverse(); y.Societies.Reverse();
        for (int t = 0; t < 8; t++)
            foreach (var w in new[] { x, y })
            {
                foreach (var c in w.Camps) { if (c.Player % 3 == 0) { c.Tile = w.Map.At((c.Tile % w.Map.Width + 1) % w.Map.Width, c.Tile / w.Map.Width); c.StationaryTurns = 0; } else c.StationaryTurns++; }
                ContactKnowledge.EndTurn(w, ReferenceEquals(w, x) ? new PhaseExecution() : new PhaseExecution(4, 2, true, 1)); w.Turn++;
            }
        Check(Memory(x) == Memory(y) && Key(x) == Key(y), "hinterland growth and forgetting are identical across workers and collection order");
        Check(x.Societies.Select(s => s.Memory.Explored.Count).Distinct().Count() > 1, "the replay mixes large settled and small moving camps");
    }
}
