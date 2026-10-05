using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Anthropocene.Simulation
{
    // Opt-in cost attribution for planning and resolution (Docs/PlanningCostAttribution.md). Off by default: a disabled Scope is
    // one static read and an inert struct. Never an input to authoritative state, saves, observations or fingerprints.
    //
    // Each scope records its inclusive time, its exclusive time (inclusive minus the scopes opened inside it on the same thread)
    // and its calls, keyed by its path (parent path / name), so the same helper called from different phases is attributed to
    // each phase separately and nothing is counted twice: the exclusive times of all paths sum to the root scopes' inclusive time.
    // Work on parallel workers opens root paths on the worker threads; attribute with one worker (scalar execution) for a
    // complete tree. Counters record work counts (pairs compared, records scanned, camps valued) under plain names.
    public static class PhaseProfile
    {
        public static volatile bool Enabled;
        private sealed class Entry { public long Inclusive, Exclusive, Calls; }
        private static readonly ConcurrentDictionary<string, Entry> Entries = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, long[]> Counts = new ConcurrentDictionary<string, long[]>(StringComparer.Ordinal);
        [ThreadStatic] private static List<string> paths;
        [ThreadStatic] private static List<long> childTicks;
        [ThreadStatic] private static Dictionary<(string, string), string> joined;   // composed paths, so hot scopes do not allocate

        public readonly struct Handle : IDisposable
        {
            private readonly string path; private readonly long start;
            internal Handle(string path, long start) { this.path = path; this.start = start; }
            public void Dispose()
            {
                if (path == null) return;
                long elapsed = Stopwatch.GetTimestamp() - start;
                int top = paths.Count - 1;
                long children = childTicks[top];
                paths.RemoveAt(top); childTicks.RemoveAt(top);
                if (top > 0) childTicks[top - 1] += elapsed;
                var entry = Entries.GetOrAdd(path, _ => new Entry());
                Interlocked.Add(ref entry.Inclusive, elapsed); Interlocked.Add(ref entry.Exclusive, elapsed - children); Interlocked.Increment(ref entry.Calls);
            }
        }

        public static Handle Scope(string name)
        {
            if (!Enabled) return default;
            if (paths == null) { paths = new List<string>(); childTicks = new List<long>(); joined = new Dictionary<(string, string), string>(); }
            string parent = paths.Count == 0 ? "" : paths[paths.Count - 1];
            if (!joined.TryGetValue((parent, name), out string path)) joined[(parent, name)] = path = parent.Length == 0 ? name : parent + "/" + name;
            paths.Add(path); childTicks.Add(0);
            return new Handle(path, Stopwatch.GetTimestamp());
        }

        public static void Count(string name, long amount = 1)
        {
            if (!Enabled) return;
            Interlocked.Add(ref Counts.GetOrAdd(name, _ => new long[1])[0], amount);
        }

        public static void Reset() { Entries.Clear(); Counts.Clear(); }

        public readonly struct Row
        {
            public readonly string Path; public readonly double InclusiveMs, ExclusiveMs; public readonly long Calls;
            public Row(string path, double inclusiveMs, double exclusiveMs, long calls) { Path = path; InclusiveMs = inclusiveMs; ExclusiveMs = exclusiveMs; Calls = calls; }
        }

        // Rows in path order, so a parent precedes its children.
        public static Row[] Rows()
        {
            double perMs = 1000.0 / Stopwatch.Frequency;
            return Entries.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new Row(p.Key, p.Value.Inclusive * perMs, p.Value.Exclusive * perMs, p.Value.Calls)).ToArray();
        }

        public static KeyValuePair<string, long>[] Counters()
            => Counts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string, long>(p.Key, p.Value[0])).ToArray();
    }
}
