using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Anthropocene.Simulation
{
    // Sparse, non-authoritative adjacency for the radius-one material economy.
    // Deposit geometry is immutable after generation/load; source states are not.
    internal sealed class MaterialAccessIndex
    {
        internal readonly struct SiteRef
        {
            internal readonly DepositBody Body;
            internal readonly DepositSite Site;
            internal SiteRef(DepositBody body, DepositSite site) { Body = body; Site = site; }
        }
        private readonly WorldResourceState resources;
        private readonly WorldState world;
        private readonly Dictionary<int, SiteRef[]> sitesByTile;
        private readonly Dictionary<(long, MaterialId), double> totals;
        private readonly Dictionary<string, MaterialSourceState> states = new Dictionary<string, MaterialSourceState>(StringComparer.Ordinal);
        private readonly HashSet<(int Society, string Source, int Tile)> knowledge = new HashSet<(int, string, int)>();
        private int stateCount;
        private int knowledgeCount;

        private MaterialAccessIndex(WorldState world)
        {
            this.world = world;
            resources = world.Resources;
            var sites = new Dictionary<int, List<SiteRef>>();
            totals = new Dictionary<(long, MaterialId), double>();
            if (resources != null)
                foreach (var body in resources.Bodies.OrderBy(b => b.Id))
                {
                    Interlocked.Increment(ref TurnDiagnostics.For(world).BodyScans);
                    foreach (var material in MaterialEconomy.Materials)
                        totals[(body.Id, material)] = body.Sites.Sum(site => resources.AccessibleAt(body, site, material));
                    foreach (var tileGroup in body.Sites.GroupBy(site => site.Tile))
                    {
                        if (!tileGroup.Any(site => site.Exposure > 0 && site.Burial != BurialDepth.Deep)) continue;
                        if (!sites.TryGetValue(tileGroup.Key, out var list)) sites[tileGroup.Key] = list = new List<SiteRef>();
                        list.Add(new SiteRef(body, tileGroup.First()));
                    }
                }
            sitesByTile = sites.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
            RefreshStates(world);
            RefreshKnowledge(world);
        }

        internal static MaterialAccessIndex For(WorldState world)
        {
            var index = world.MaterialIndex;
            if (index == null || !ReferenceEquals(index.resources, world.Resources))
            {
                var created = new MaterialAccessIndex(world);
                if (index == null) Interlocked.CompareExchange(ref world.MaterialIndex, created, null);
                else world.MaterialIndex = created;
                index = world.MaterialIndex;
            }
            if (index.stateCount != (world.MaterialSources?.Count ?? 0)) index.RefreshStates(world);
            if (index.knowledgeCount != (world.MaterialKnowledge?.Count ?? 0)) index.RefreshKnowledge(world);
            return index;
        }

        private void RefreshStates(WorldState world)
        {
            lock (states)
            {
                states.Clear();
                if (world.MaterialSources != null)
                    foreach (var state in world.MaterialSources) states[state.Id] = state;
                stateCount = world.MaterialSources?.Count ?? 0;
            }
        }
        internal void AddState(MaterialSourceState state)
        {
            lock (states) { states[state.Id] = state; stateCount++; }
        }
        private void RefreshKnowledge(WorldState world)
        {
            knowledge.Clear();
            if (world.MaterialKnowledge != null)
                foreach (var entry in world.MaterialKnowledge)
                    knowledge.Add((entry.Society, entry.Source, entry.Tile));
            knowledgeCount = world.MaterialKnowledge?.Count ?? 0;
        }
        internal bool Remember(int society, string source, int tile)
        {
            if (!knowledge.Add((society, source, tile))) return false;
            knowledgeCount++;
            return true;
        }
        internal MaterialSourceState State(string id)
        {
            if (ResolutionMetrics.CaptureDetailedCounters)
                Interlocked.Increment(ref TurnDiagnostics.For(world).SourceLookups);
            // Resolution reads a stable source map between explicit write barriers.
            // Parallel workers never add states while evaluating ecology tiles.
            states.TryGetValue(id, out var state); return state;
        }
        internal SiteRef[] Sites(int tile) => sitesByTile.TryGetValue(tile, out var sites) ? sites : Array.Empty<SiteRef>();
        internal double Total(DepositBody body, MaterialId material) => totals.TryGetValue((body.Id, material), out double total) ? total : 0;
    }
}
