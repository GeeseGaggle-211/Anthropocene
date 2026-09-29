using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

// Runs independent fixed-suite checks on a bounded number of lanes. Each check
// owns its worlds, so lanes cannot change simulation results. Console output is
// captured per check and printed in canonical order; the first failure in that
// order is rethrown, exactly as the sequential suite would report it.
internal static class ParallelChecks
{
    internal sealed class Entry
    {
        public string Name; public Action Run;
        // Measured seconds on the reference workstation; only schedules long checks first.
        public double Cost;
        internal string Output; internal Exception Error; internal double Seconds;
        internal readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
    }

    private sealed class RoutingWriter : TextWriter
    {
        internal static readonly AsyncLocal<StringWriter> Capture = new AsyncLocal<StringWriter>();
        private readonly TextWriter original;
        public RoutingWriter(TextWriter original) { this.original = original; }
        public override Encoding Encoding => original.Encoding;
        private TextWriter Target => (TextWriter)Capture.Value ?? original;
        public override void Write(char value) => Target.Write(value);
        public override void Write(string value) => Target.Write(value);
        public override void Write(char[] buffer, int index, int count) => Target.Write(buffer, index, count);
        public override void WriteLine(string value) => Target.WriteLine(value);
        public override void Flush() => original.Flush();
    }

    public static int DefaultJobs => Math.Max(1, Math.Min(3, Environment.ProcessorCount / 4));

    // --only=NameA,NameB runs the named fixed-suite entries and nothing else.
    public static bool Filter(string[] args, ref Entry[] entries)
    {
        var only = args.FirstOrDefault(a => a.StartsWith("--only=", StringComparison.Ordinal));
        if (only == null) return false;
        var names = only.Substring(7).Split(',');
        var all = entries;
        var unknown = names.Where(n => all.All(e => e.Name != n)).ToArray();
        if (unknown.Length > 0) throw new ArgumentException("Unknown fixed check: " + string.Join(", ", unknown) + ". Known: " + string.Join(", ", all.Select(e => e.Name)));
        entries = all.Where(e => names.Contains(e.Name)).ToArray();
        return true;
    }

    public static void Run(Entry[] entries, int jobs, bool timings)
    {
        var total = Stopwatch.StartNew();
        if (jobs <= 1)
        {
            foreach (var entry in entries)
            {
                var timer = Stopwatch.StartNew(); entry.Run(); entry.Seconds = timer.Elapsed.TotalSeconds;
            }
        }
        else
        {
            var original = Console.Out;
            Console.SetOut(new RoutingWriter(original));
            try
            {
                var queue = new ConcurrentQueue<Entry>(entries.OrderByDescending(e => e.Cost));
                for (int lane = 0; lane < Math.Min(jobs, entries.Length); lane++)
                    new Thread(() => Work(queue)) { IsBackground = true, Name = "check-lane-" + lane }.Start();
                foreach (var entry in entries)
                {
                    entry.Done.Wait();
                    original.Write(entry.Output);
                    if (entry.Error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(entry.Error).Throw();
                }
            }
            finally { Console.SetOut(original); }
        }
        if (timings)
        {
            foreach (var entry in entries.OrderByDescending(e => e.Seconds))
                Console.WriteLine("TIMING " + entry.Name + " " + entry.Seconds.ToString("F1") + "s");
            Console.WriteLine("TIMING fixed checks jobs=" + jobs + " wall=" + total.Elapsed.TotalSeconds.ToString("F1") + "s peakWorkingSetMiB=" + (Process.GetCurrentProcess().PeakWorkingSet64 / 1048576));
        }
    }

    private static void Work(ConcurrentQueue<Entry> queue)
    {
        while (queue.TryDequeue(out var entry))
        {
            var output = new StringWriter();
            RoutingWriter.Capture.Value = output;
            var timer = Stopwatch.StartNew();
            try { entry.Run(); }
            catch (Exception error) { entry.Error = error; }
            finally
            {
                RoutingWriter.Capture.Value = null;
                entry.Seconds = timer.Elapsed.TotalSeconds; entry.Output = output.ToString(); entry.Done.Set();
            }
        }
    }
}
