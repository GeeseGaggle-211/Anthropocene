using System;
using System.Collections.Generic;
using System.Threading;

namespace Anthropocene.Simulation
{
    // Transient lookup of WorldState.FixedHousing by (camp, tile), so a camp's fixed shelter costs O(its own records) rather
    // than a scan of every camp's records (Docs/PlanningCostAttribution.md). Never saved, observed or fingerprinted.
    //
    // The simulation only appends records (AddFixedHousing, MigrateLegacyFixedHousing) and replaces the list when it normalizes
    // a loaded save; it updates Units and LastUpdatedDays in place but never a record's camp or tile. So an index snapshot is
    // current while the list object and its count are unchanged. Each hit is also checked against the record still sitting at
    // its recorded position with its key, which catches a direct edit that re-adds the same key. Any other direct edit that
    // keeps the count (removing one camp's record and adding another's) must call NeolithicProduction.FixedHousingChanged. An index is
    // immutable once built and published by reference, so parallel readers see a complete one. Positions ascend, so a sum
    // visits records in list order and equals the former linear scan exactly.
    internal sealed class FixedHousingIndex
    {
        private readonly List<FixedHousingStock> list; private readonly int count;
        private readonly Dictionary<long, int[]> positions;

        private FixedHousingIndex(List<FixedHousingStock> list)
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

        // Positions in WorldState.FixedHousing of the camp's records at the tile, ascending.
        internal static int[] Positions(WorldState w, int camp, int tile)
        {
            var list = w.FixedHousing;
            if (list == null || list.Count == 0) return Array.Empty<int>();
            PhaseProfile.Count("Housing.Lookups");
            for (int attempt = 0; ; attempt++)
            {
                var index = Volatile.Read(ref w.HousingIndex);
                if (attempt > 0 || index == null || !ReferenceEquals(index.list, list) || index.count != list.Count)
                {
                    index = new FixedHousingIndex(list); Volatile.Write(ref w.HousingIndex, index);
                    PhaseProfile.Count("Housing.IndexBuilds"); PhaseProfile.Count("Housing.RecordsScanned", list.Count);
                }
                if (!index.positions.TryGetValue(Key(camp, tile), out var found)) return Array.Empty<int>();
                bool current = true;
                foreach (int p in found) { var s = list[p]; if (s == null || s.CampId != camp || s.Tile != tile) { current = false; break; } }
                PhaseProfile.Count("Housing.RecordsRead", found.Length);
                if (current || attempt > 0) return found;
            }
        }
    }
}
