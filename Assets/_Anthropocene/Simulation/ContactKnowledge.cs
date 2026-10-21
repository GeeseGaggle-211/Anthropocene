using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Anthropocene.Simulation
{
    public enum ContactSource { Sighting, Trade, Kin, Migration }

    // A snapshot of where one foreign camp was last known. It carries location and a coarse public population band
    // (knowledge-contract amendment Q6) taken when the contact was learned or refreshed: never live population,
    // stores, orders or movement. Tile is -1 once the camp was found to have left its recorded tile (Moved).
    [Serializable]
    public sealed class SettlementContact
    {
        public int Camp, Owner, Tile, Turn;
        [OptionalField] public double ObservedDays;
        public ContactSource Source;
        public bool Moved;
        // 1 + floor(log2(population)) when learned, 0 unknown (saves that predate the band). Snapshot, not live.
        [OptionalField] public int PopulationBand;
        [OnDeserializing]
        private void Defaults(StreamingContext context)
            => ObservedDays = double.NaN;
        public SettlementContact Copy() => (SettlementContact)MemberwiseClone();
    }

    // Sparse explored-tile set. Tiles are grouped into 8x8 map blocks; a block
    // costs one sorted key, one 64-bit word and one last-refreshed turn, and only
    // blocks that contain an explored tile exist, so memory follows explored area,
    // not map size. Keys and words are kept in ascending key order so saves,
    // fingerprints and iteration are deterministic.
    //
    // Decaying memory: Turns[i] is the last turn block i was refreshed (a tile in it seen or
    // inside a hinterland). A block not refreshed for ExploredMemoryTurns is forgotten whole,
    // so memory is block-granular: a tile can outlive its own last refresh by up to the block's
    // other tiles being refreshed, a deliberate approximation that keeps cost and memory per block.
    // A negative or absent stamp (saves that predate decay, or Add without a turn) is stamped at
    // the next end-of-turn update, so nothing is forgotten the moment a save is loaded.
    [Serializable]
    public sealed class ExploredTiles
    {
        public int Width;
        public List<int> Chunks = new List<int>();
        public List<ulong> Words = new List<ulong>();
#pragma warning disable SYSLIB0050
        [OptionalField] public List<int> Turns;
#pragma warning restore SYSLIB0050
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
        // Marks a tile without a refresh stamp: an existing block keeps its stamp, a new one is unstamped (-1).
        public void Add(int tile) => Add(tile, -1);
        // Marks a tile and refreshes its block's last-refreshed turn (never moves it backwards).
        public void Add(int tile, int turn)
        {
            EnsureTurns();
            int key = Locate(tile, out ulong mask), at = Chunks.BinarySearch(key);
            if (at >= 0) { Words[at] |= mask; if (turn > Turns[at]) Turns[at] = turn; }
            else { Chunks.Insert(~at, key); Words.Insert(~at, mask); Turns.Insert(~at, turn); }
        }
        private void EnsureTurns()
        {
            if (Turns == null) Turns = new List<int>();
            while (Turns.Count < Chunks.Count) Turns.Add(-1);
        }
        public int TurnOf(int tile)
        {
            int key = Locate(tile, out _), at = Chunks.BinarySearch(key);
            return at >= 0 && Turns != null && at < Turns.Count ? Turns[at] : -1;
        }
        // Stamps every unstamped block with the given turn.
        public void StampUnstamped(int turn)
        {
            EnsureTurns();
            for (int i = 0; i < Turns.Count; i++) if (Turns[i] < 0) Turns[i] = turn;
        }
        // Drops every block whose stamp is at least `memoryTurns` old; returns the number dropped. O(blocks).
        public int Forget(int turn, int memoryTurns)
        {
            EnsureTurns();
            int keep = 0, dropped = 0;
            for (int i = 0; i < Chunks.Count; i++)
            {
                if (Turns[i] >= 0 && turn - Turns[i] >= memoryTurns) { dropped++; continue; }
                if (keep != i) { Chunks[keep] = Chunks[i]; Words[keep] = Words[i]; Turns[keep] = Turns[i]; }
                keep++;
            }
            if (dropped > 0) { Chunks.RemoveRange(keep, dropped); Words.RemoveRange(keep, dropped); Turns.RemoveRange(keep, dropped); }
            return dropped;
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
        public long ApproximateBytes => (long)Chunks.Count * 16;
        public ExploredTiles Clone() { var c = new ExploredTiles(Width) { Chunks = new List<int>(Chunks), Words = new List<ulong>(Words) }; if (Turns != null) c.Turns = new List<int>(Turns); return c; }
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
        internal void Upsert(int camp, int owner, int tile, int turn,
            double observedDays, ContactSource source, int populationBand = 0)
        {
            int at = Index(camp);
            if (at >= 0) { var c = Contacts[at]; c.Owner = owner; c.Tile = tile; c.Turn = turn; c.ObservedDays = observedDays; c.Source = source; c.Moved = false; c.PopulationBand = populationBand; }
            else Contacts.Insert(~at, new SettlementContact { Camp = camp, Owner = owner, Tile = tile, Turn = turn, ObservedDays = observedDays, Source = source, PopulationBand = populationBand });
        }
        internal void Upsert(int camp, int owner, int tile, int turn,
            ContactSource source)
            => Upsert(camp, owner, tile, turn, double.NaN, source);
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
        public readonly double ObservedDays;
        public readonly ContactSource Source;
        public readonly bool Moved;
        public readonly int PopulationBand;
        internal SettlementContactObservation(SettlementContact c) { Camp = c.Camp; Owner = c.Owner; Tile = c.Moved ? -1 : c.Tile; Turn = c.Turn; ObservedDays = c.ObservedDays; Source = c.Source; Moved = c.Moved; PopulationBand = c.PopulationBand; }
    }

    // Remembered knowledge: explored static geography and known-settlement
    // snapshots gained after a Society's first breakthrough. The end-of-turn
    // update is pure over the frozen post-turn state (per-Society work may run on
    // bounded workers) followed by a canonical commit in Society-ID order.
    //
    // Cost: O(camps + hunters) indexing, O(sum of R_h^2) hinterland tiles per remembering Society (R_h <= HinterlandMaxRadius, so at most
    // 3 r^2 + 3 r + 1 per camp), O(log explored-blocks) per tile mark, O(explored blocks) for the forgetting sweep and O(contacts) per
    // Society; nothing scales with elapsed years or map size. Memory: 16 bytes per occupied 8x8 block plus ~32 bytes per contact, per Society.
    public static class ContactKnowledge
    {
        // Coarse public population band (Q6): 1 + floor(log2 N) for N >= 1, 0 for an empty or unknown camp.
        public static int PopulationBand(int population) => population <= 0 ? 0 : 1 + (int)Math.Floor(Math.Log(population, 2) + 1e-12);
        public static bool Remembers(Society s) => s.Progression.Era != Era.Paleolithic || s.Technology != Technology.None;
        public static ContactMemory MemoryOf(WorldState w, Society s) => s.Memory ?? (s.Memory = new ContactMemory(w.Config.Width));

        private sealed class Sighting { public int Camp, Owner, Tile, Band; }
        private sealed class Update { public Society Society; public int[] Tiles; public HashSet<int> Vision; public Sighting[] Seen; }

        // Hinterland radius of a camp that has stayed `stationaryTurns` turns with `population` residents: it grows by HinterlandRingsPerTurn
        // per stationary turn from the vision radius, up to VisionRadius * sqrt(N / HinterlandReferencePopulation) (area worked proportional
        // to people) and HinterlandMaxRadius, and never falls below the vision radius. A move (StationaryTurns = 0) resets it.
        public static int HinterlandRadius(SimulationConfig cfg, int stationaryTurns, int population)
        {
            int vision = cfg.VisionRadius;
            long grown = vision + (long)cfg.HinterlandRingsPerTurn * Math.Max(0, stationaryTurns);
            int cap = (int)Math.Floor(vision * Math.Sqrt(Math.Max(0, population) / (double)cfg.HinterlandReferencePopulation) + 1e-9);
            return Math.Max(vision, (int)Math.Min(Math.Min(grown, cap), cfg.HinterlandMaxRadius));
        }

        public static void EndTurn(WorldState w, PhaseExecution execution = null)
        {
            var societies = w.Societies.Where(Remembers).OrderBy(s => s.Id).ToArray();
            if (societies.Length == 0) return;
            execution = execution ?? new PhaseExecution();
            // Frozen post-turn sources, built once. A camp sees only while it has people, including its hunters.
            var hunterPeople = new Dictionary<int, int>();
            foreach (var h in w.Hunters) if (h.Population > 0) { hunterPeople.TryGetValue(h.SourceCamp, out int n); hunterPeople[h.SourceCamp] = n + h.Population; }
            var sources = new Dictionary<int, List<int[]>>();
            int radius = w.Config.VisionRadius; var map = w.Map;
            // A source is (tile, hinterland radius): a camp's radius grows with its stationary turns and people; hunters see only their vision.
            void AddSource(int society, int tile, int reach) { if (!sources.TryGetValue(society, out var list)) sources[society] = list = new List<int[]>(); list.Add(new[] { tile, reach }); }
            foreach (var c in w.Camps) { hunterPeople.TryGetValue(c.Id, out int n); if (c.Population + n > 0) AddSource(c.SocietyId, c.Tile, HinterlandRadius(w.Config, c.StationaryTurns, c.Population)); }
            foreach (var h in w.Hunters) if (h.Population > 0) AddSource(h.SocietyId, h.Tile, radius);
            var campsByTile = new Dictionary<int, List<Camp>>();
            foreach (var c in w.Camps) { if (!campsByTile.TryGetValue(c.Tile, out var list)) campsByTile[c.Tile] = list = new List<Camp>(); list.Add(c); }
            var updates = execution.Map(societies, s =>
            {
                var vision = new HashSet<int>();
                var explore = new HashSet<int>();
                if (sources.TryGetValue(s.Id, out var origins))
                    foreach (var origin in origins)
                    {
                        vision.UnionWith(map.Radius(origin[0], radius));
                        if (origin[1] > radius) explore.UnionWith(map.Radius(origin[0], origin[1]));
                    }
                explore.UnionWith(vision);
                var seen = new List<Sighting>();
                foreach (int tile in vision)
                    if (campsByTile.TryGetValue(tile, out var here))
                        foreach (var c in here) if (c.SocietyId != s.Id) seen.Add(new Sighting { Camp = c.Id, Owner = c.Player, Tile = tile, Band = PopulationBand(c.Population) });
                seen.Sort((a, b) => a.Camp.CompareTo(b.Camp));
                return new Update { Society = s, Tiles = explore.ToArray(), Vision = vision, Seen = seen.ToArray() };
            });
            int turn = w.Turn, expiry = w.Config.ContactExpiryTurns, forget = w.Config.ExploredMemoryTurns;
            foreach (var u in updates)
            {
                var memory = MemoryOf(w, u.Society);
                // Blocks stamped at the load turn (saves that predate decay) first, then this turn's marks, then forgetting.
                memory.Explored.StampUnstamped(turn);
                foreach (int tile in u.Tiles) memory.Explored.Add(tile, turn);
                foreach (var s in u.Seen) memory.Upsert(s.Camp, s.Owner,
                    s.Tile, turn, w.Days, ContactSource.Sighting, s.Band);
                // A recorded tile now in view without its camp means the camp moved away.
                foreach (var c in memory.Contacts)
                    if (c.Turn != turn && !c.Moved && u.Vision.Contains(c.Tile)) { c.Moved = true; c.Tile = -1; }
                memory.Contacts.RemoveAll(c => turn - c.Turn >= expiry);
                memory.Explored.Forget(turn, forget);
            }
        }

        // Trade hook (called by later trade work): society learns the partner camp's
        // current location. Call once per direction. Societies before their first breakthrough remember nothing.
        public static void RecordTrade(WorldState w, Society society, Camp partner)
        {
            if (!Remembers(society) || partner.SocietyId == society.Id) return;
            MemoryOf(w, society).Upsert(partner.Id, partner.Player,
                partner.Tile, w.Turn, w.Days, ContactSource.Trade, PopulationBand(partner.Population));
        }

        // Migration hook (WP10b): people moved between two known settlements, so each Society learns the other camp's
        // current location. Call once per direction. Societies before their first breakthrough remember nothing.
        public static void RecordMigration(WorldState w, Society society, Camp other)
        {
            if (!Remembers(society) || other.SocietyId == society.Id) return;
            MemoryOf(w, society).Upsert(other.Id, other.Player, other.Tile,
                w.Turn, w.Days, ContactSource.Migration, PopulationBand(other.Population));
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
                parentMemory.Upsert(daughterCamp.Id, daughterCamp.Player,
                    daughterCamp.Tile, w.Turn, w.Days, ContactSource.Kin, PopulationBand(daughterCamp.Population));
            }
            if (Remembers(daughter)) MemoryOf(w, daughter).Upsert(
                parentCamp.Id, parentCamp.Player, parentCamp.Tile, w.Turn,
                w.Days, ContactSource.Kin, PopulationBand(parentCamp.Population));
            if (Remembers(parent) && Remembers(daughter)) MarketMemorySystem.CopyAtFission(w, parent, daughter, parentCamp, daughterCamp);
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
