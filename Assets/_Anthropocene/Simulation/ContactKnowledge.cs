using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    public enum ContactSource { Sighting, Trade, Kin, Migration }

    // A snapshot of where one foreign camp was last known. It carries location
    // only: no population, stores, orders or movement. Tile is -1 once the camp
    // was found to have left its recorded tile (Moved, location unknown).
    [Serializable]
    public sealed class SettlementContact
    {
        public int Camp, Owner, Tile, Turn;
        public ContactSource Source;
        public bool Moved;
        public SettlementContact Copy() => (SettlementContact)MemberwiseClone();
    }

    // Sparse explored-tile set. Tiles are grouped into 8x8 map blocks; a block
    // costs one sorted key plus one 64-bit word, and only blocks that contain an
    // explored tile exist, so memory follows explored area, not map size. Keys and
    // words are kept in ascending key order so saves, fingerprints and iteration
    // are deterministic.
    [Serializable]
    public sealed class ExploredTiles
    {
        public int Width;
        public List<int> Chunks = new List<int>();
        public List<ulong> Words = new List<ulong>();
        public ExploredTiles(int width) { Width = width; }
        private int Locate(int tile, out ulong mask)
        {
            int x = tile % Width, y = tile / Width;
            mask = 1UL << (((y & 7) << 3) | (x & 7));
            return (y >> 3) * ((Width + 7) >> 3) + (x >> 3);
        }
        public bool Contains(int tile)
        {
            int key = Locate(tile, out ulong mask), at = Chunks.BinarySearch(key);
            return at >= 0 && (Words[at] & mask) != 0;
        }
        public void Add(int tile)
        {
            int key = Locate(tile, out ulong mask), at = Chunks.BinarySearch(key);
            if (at >= 0) Words[at] |= mask; else { Chunks.Insert(~at, key); Words.Insert(~at, mask); }
        }
        public int Count
        {
            get
            {
                int total = 0;
                foreach (ulong word in Words) { ulong v = word; while (v != 0) { v &= v - 1; total++; } }
                return total;
            }
        }
        // Deterministic order: ascending block, then ascending bit within the block.
        public IEnumerable<int> Tiles()
        {
            int perRow = (Width + 7) >> 3;
            for (int i = 0; i < Chunks.Count; i++)
            {
                int cx = Chunks[i] % perRow, cy = Chunks[i] / perRow;
                for (int bit = 0; bit < 64; bit++)
                    if ((Words[i] & (1UL << bit)) != 0) yield return ((cy << 3) + (bit >> 3)) * Width + (cx << 3) + (bit & 7);
            }
        }
        public long ApproximateBytes => (long)Chunks.Count * 12;
        public ExploredTiles Clone() => new ExploredTiles(Width) { Chunks = new List<int>(Chunks), Words = new List<ulong>(Words) };
    }

    // Saved, optional, per-Society memory. Null on Societies that have not
    // passed their first breakthrough and on saves that predate it.
    [Serializable]
    public sealed class ContactMemory
    {
        public ExploredTiles Explored;
        public List<SettlementContact> Contacts = new List<SettlementContact>();
        public ContactMemory(int width) { Explored = new ExploredTiles(width); }
        public ContactMemory Clone() => new ContactMemory(Explored.Width) { Explored = Explored.Clone(), Contacts = Contacts.Select(c => c.Copy()).ToList() };
        public SettlementContact Find(int camp)
        {
            int at = Index(camp); return at >= 0 ? Contacts[at] : null;
        }
        private int Index(int camp)
        {
            int lo = 0, hi = Contacts.Count - 1;
            while (lo <= hi) { int mid = (lo + hi) >> 1, c = Contacts[mid].Camp.CompareTo(camp); if (c == 0) return mid; if (c < 0) lo = mid + 1; else hi = mid - 1; }
            return ~lo;
        }
        internal void Upsert(int camp, int owner, int tile, int turn, ContactSource source)
        {
            int at = Index(camp);
            if (at >= 0) { var c = Contacts[at]; c.Owner = owner; c.Tile = tile; c.Turn = turn; c.Source = source; c.Moved = false; }
            else Contacts.Insert(~at, new SettlementContact { Camp = camp, Owner = owner, Tile = tile, Turn = turn, Source = source });
        }
    }

    // A remembered tile: static geography only. It deliberately has no
    // vegetation state, stocks, herds, occupants or freshwater amounts.
    public sealed class RememberedTileObservation
    {
        public readonly int Id, ElevationLevel, RidgeMask;
        public readonly EnvironmentKind Environment;
        public readonly Relief Relief;
        public readonly float Elevation;
        public readonly WaterKind WaterBody;
        public readonly bool Land, Aquatic, River;
        public readonly IReadOnlyList<RiverObservation> Rivers;
        internal RememberedTileObservation(WorldState w, Tile tile, HexCorners corners)
        {
            Id = tile.Id; Environment = tile.Environment; Relief = tile.Relief; Elevation = tile.Elevation; ElevationLevel = tile.ElevationLevel; RidgeMask = tile.RidgeMask;
            WaterBody = tile.WaterBody; Land = tile.Land; Aquatic = tile.Aquatic; River = tile.River;
            Rivers = Array.AsReadOnly(tile.RiverEdges == null ? new RiverObservation[0] : tile.RiverEdges.Select((id, side) => new { id, side }).Where(x => x.id >= 0).Select(x => new RiverObservation(x.side, w.Rivers[x.id], tile.Id, corners)).ToArray());
        }
    }

    // A known settlement snapshot. Tile is -1 when the camp moved and its
    // location is unknown; Turn is when it was last learned or refreshed.
    public sealed class SettlementContactObservation
    {
        public readonly int Camp, Owner, Tile, Turn;
        public readonly ContactSource Source;
        public readonly bool Moved;
        internal SettlementContactObservation(SettlementContact c) { Camp = c.Camp; Owner = c.Owner; Tile = c.Moved ? -1 : c.Tile; Turn = c.Turn; Source = c.Source; Moved = c.Moved; }
    }

    // Remembered knowledge: explored static geography and known-settlement
    // snapshots gained after a Society's first breakthrough. The end-of-turn
    // update is pure over the frozen post-turn state (per-Society work may run on
    // bounded workers) followed by a canonical commit in Society-ID order.
    //
    // Cost: O(camps + hunters) indexing, O(vision tiles) per remembering Society,
    // O(log explored-blocks) per tile mark and O(contacts) per Society; nothing
    // scales with elapsed years or map size. Memory: 12 bytes per occupied 8x8
    // block plus ~32 bytes per contact, per Society.
    public static class ContactKnowledge
    {
        public static bool Remembers(Society s) => s.Progression.Era != Era.Paleolithic || s.Technology != Technology.None;
        public static ContactMemory MemoryOf(WorldState w, Society s) => s.Memory ?? (s.Memory = new ContactMemory(w.Config.Width));

        private sealed class Sighting { public int Camp, Owner, Tile; }
        private sealed class Update { public Society Society; public int[] Tiles; public HashSet<int> Vision; public Sighting[] Seen; }

        public static void EndTurn(WorldState w, PhaseExecution execution = null)
        {
            var societies = w.Societies.Where(Remembers).OrderBy(s => s.Id).ToArray();
            if (societies.Length == 0) return;
            execution = execution ?? new PhaseExecution();
            // Frozen post-turn sources, built once. A camp sees only while it has people, including its hunters.
            var hunterPeople = new Dictionary<int, int>();
            foreach (var h in w.Hunters) if (h.Population > 0) { hunterPeople.TryGetValue(h.SourceCamp, out int n); hunterPeople[h.SourceCamp] = n + h.Population; }
            var sources = new Dictionary<int, List<int>>();
            void AddSource(int society, int tile) { if (!sources.TryGetValue(society, out var list)) sources[society] = list = new List<int>(); list.Add(tile); }
            foreach (var c in w.Camps) { hunterPeople.TryGetValue(c.Id, out int n); if (c.Population + n > 0) AddSource(c.SocietyId, c.Tile); }
            foreach (var h in w.Hunters) if (h.Population > 0) AddSource(h.SocietyId, h.Tile);
            var campsByTile = new Dictionary<int, List<Camp>>();
            foreach (var c in w.Camps) { if (!campsByTile.TryGetValue(c.Tile, out var list)) campsByTile[c.Tile] = list = new List<Camp>(); list.Add(c); }
            int radius = w.Config.VisionRadius; var map = w.Map;
            var updates = execution.Map(societies, s =>
            {
                var vision = new HashSet<int>();
                if (sources.TryGetValue(s.Id, out var origins)) foreach (int origin in origins) vision.UnionWith(map.Radius(origin, radius));
                var seen = new List<Sighting>();
                foreach (int tile in vision)
                    if (campsByTile.TryGetValue(tile, out var here))
                        foreach (var c in here) if (c.SocietyId != s.Id) seen.Add(new Sighting { Camp = c.Id, Owner = c.Player, Tile = tile });
                seen.Sort((a, b) => a.Camp.CompareTo(b.Camp));
                return new Update { Society = s, Tiles = vision.ToArray(), Vision = vision, Seen = seen.ToArray() };
            });
            int turn = w.Turn, expiry = w.Config.ContactExpiryTurns;
            foreach (var u in updates)
            {
                var memory = MemoryOf(w, u.Society);
                foreach (int tile in u.Tiles) memory.Explored.Add(tile);
                foreach (var s in u.Seen) memory.Upsert(s.Camp, s.Owner, s.Tile, turn, ContactSource.Sighting);
                // A recorded tile now in view without its camp means the camp moved away.
                foreach (var c in memory.Contacts)
                    if (c.Turn != turn && !c.Moved && u.Vision.Contains(c.Tile)) { c.Moved = true; c.Tile = -1; }
                memory.Contacts.RemoveAll(c => turn - c.Turn >= expiry);
            }
        }

        // Trade hook (called by later trade work): society learns the partner camp's
        // current location. Call once per direction. Societies before their first breakthrough remember nothing.
        public static void RecordTrade(WorldState w, Society society, Camp partner)
        {
            if (!Remembers(society) || partner.SocietyId == society.Id) return;
            MemoryOf(w, society).Upsert(partner.Id, partner.Player, partner.Tile, w.Turn, ContactSource.Trade);
        }

        // Migration hook (WP10b): people moved between two known settlements, so each Society learns the other camp's
        // current location. Call once per direction. Societies before their first breakthrough remember nothing.
        public static void RecordMigration(WorldState w, Society society, Camp other)
        {
            if (!Remembers(society) || other.SocietyId == society.Id) return;
            MemoryOf(w, society).Upsert(other.Id, other.Player, other.Tile, w.Turn, ContactSource.Migration);
        }

        // Kin hook (called by later fission work). The daughter receives a plain copy of
        // the parent's explored tiles and contacts taken at fission (O(explored blocks +
        // contacts) once per fission; a copy-on-write share would save that memory but
        // needs ownership tracking for little gain at fission frequency), then parent and
        // daughter record each other's camps as kin.
        public static void RecordKin(WorldState w, Society parent, Society daughter, Camp parentCamp, Camp daughterCamp)
        {
            if (Remembers(parent))
            {
                var parentMemory = MemoryOf(w, parent);
                if (Remembers(daughter))
                {
                    var copy = parentMemory.Clone();
                    copy.Contacts.RemoveAll(c => c.Camp == daughterCamp.Id || c.Camp == parentCamp.Id);
                    daughter.Memory = copy;
                }
                parentMemory.Upsert(daughterCamp.Id, daughterCamp.Player, daughterCamp.Tile, w.Turn, ContactSource.Kin);
            }
            if (Remembers(daughter)) MemoryOf(w, daughter).Upsert(parentCamp.Id, parentCamp.Player, parentCamp.Tile, w.Turn, ContactSource.Kin);
        }

        // Read side for observations: remembered static geography outside current
        // vision, and the contact snapshots. Both are empty without memory.
        internal static RememberedTileObservation[] Remembered(WorldState w, Society s, HashSet<int> visible)
        {
            if (s.Memory == null) return Array.Empty<RememberedTileObservation>();
            var corners = new HexCorners(w.Config.Width, w.Config.Height);
            return s.Memory.Explored.Tiles().Where(t => !visible.Contains(t)).OrderBy(t => t).Select(t => new RememberedTileObservation(w, w.Tiles[t], corners)).ToArray();
        }
        internal static SettlementContactObservation[] Contacts(Society s)
            => s.Memory == null ? Array.Empty<SettlementContactObservation>() : s.Memory.Contacts.Select(c => new SettlementContactObservation(c)).ToArray();

        public struct Footprint { public int Societies, Blocks, Tiles, Contacts; public long Bytes; }
        // Transient measurement only; never saved or read by the simulation.
        public static Footprint Measure(WorldState w)
        {
            var f = new Footprint();
            foreach (var s in w.Societies.Where(s => s.Memory != null))
            { f.Societies++; f.Blocks += s.Memory.Explored.Chunks.Count; f.Tiles += s.Memory.Explored.Count; f.Contacts += s.Memory.Contacts.Count; f.Bytes += s.Memory.Explored.ApproximateBytes + 32L * s.Memory.Contacts.Count; }
            return f;
        }
    }
}
