using System;
using System.Collections.Generic;
using System.Threading;

namespace Anthropocene.Simulation
{
    // Transient lookup of WorldState.FixedSanitation by (camp, tile), mirroring FixedHousingIndex so a camp's
    // fixed sanitation lookup costs O(its own records) rather than scanning every camp. Never saved, observed or fingerprinted.
    //
    // The simulation only appends records (AddFixedSanitation) and replaces the list when it normalizes a loaded save;
    // it updates Units and LastUpdatedDays in place but never a record's camp or tile. Any direct edit that keeps the
    // count must call NeolithicProduction.FixedSanitationChanged. Immutable once built and published by reference.
    internal sealed class FixedSanitationIndex
    {
        private readonly List<FixedSanitationStock> list; private readonly int count;
        private readonly Dictionary<long, int[]> positions;

        private FixedSanitationIndex(List<FixedSanitationStock> list)
        {
            this.list = list; count = list.Count;
            var build = new Dictionary<long, List<int>>();
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i]; if (s == null) continue;
                long key = Key(s.CampId, s.Tile);
                if (!build.TryGetValue(key, out var at)) build[key] = at = new List<int>(1);
                at.Add(i);
            }
            positions = new Dictionary<long, int[]>(build.Count);
            foreach (var pair in build) positions[pair.Key] = pair.Value.ToArray();
        }

        private static long Key(int camp, int tile) => ((long)camp << 32) | (uint)tile;

        // Positions in WorldState.FixedSanitation of the camp's records at the tile, ascending.
        internal static int[] Positions(WorldState w, int camp, int tile)
        {
            var list = w.FixedSanitation;
            if (list == null || list.Count == 0) return Array.Empty<int>();
            PhaseProfile.Count("Sanitation.Lookups");
            for (int attempt = 0; ; attempt++)
            {
                var index = Volatile.Read(ref w.SanitationIndex);
                if (attempt > 0 || index == null || !ReferenceEquals(index.list, list) || index.count != list.Count)
                {
                    index = new FixedSanitationIndex(list); Volatile.Write(ref w.SanitationIndex, index);
                    PhaseProfile.Count("Sanitation.IndexBuilds"); PhaseProfile.Count("Sanitation.RecordsScanned", list.Count);
                }
                if (!index.positions.TryGetValue(Key(camp, tile), out var found)) return Array.Empty<int>();
                bool current = true;
                foreach (int p in found) { var s = list[p]; if (s == null || s.CampId != camp || s.Tile != tile) { current = false; break; } }
                PhaseProfile.Count("Sanitation.RecordsRead", found.Length);
                if (current || attempt > 0) return found;
            }
        }
    }
}
