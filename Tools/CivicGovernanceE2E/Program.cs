using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Anthropocene.CivicGovernanceE2E
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            var allTests = BuildCanonicalRegistry();

            string tierArg = "all";
            int? selectedTier = null;
            string filterArg = null;
            bool listOnly = false;
            bool verbose = false;
            bool showHelp = false;
            int jobs = 1;
            string resourcesRoot = null;
            string shardIndicesArg = null;
            string resultFileArg = null;

            bool seenTier = false;
            bool seenFilter = false;
            bool seenJobs = false;
            bool seenResourcesRoot = false;
            bool seenShardIndices = false;
            bool seenResultFile = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--list")
                {
                    listOnly = true;
                }
                else if (a == "--verbose")
                {
                    verbose = true;
                }
                else if (a == "--help" || a == "-h")
                {
                    showHelp = true;
                }
                else if (a.StartsWith("--tier=", StringComparison.Ordinal))
                {
                    if (seenTier)
                    {
                        Console.Error.WriteLine("Error: duplicate --tier option.");
                        return 2;
                    }
                    seenTier = true;
                    tierArg = a.Substring("--tier=".Length);
                    if (string.Equals(tierArg, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedTier = null;
                    }
                    else if (int.TryParse(tierArg, NumberStyles.None, CultureInfo.InvariantCulture, out int tVal) && tVal >= 1 && tVal <= 4)
                    {
                        selectedTier = tVal;
                    }
                    else
                    {
                        Console.Error.WriteLine($"Error: invalid --tier value '{tierArg}'. Expected 1, 2, 3, 4, or all.");
                        return 2;
                    }
                }
                else if (a.StartsWith("--filter=", StringComparison.Ordinal))
                {
                    if (seenFilter)
                    {
                        Console.Error.WriteLine("Error: duplicate --filter option.");
                        return 2;
                    }
                    seenFilter = true;
                    string raw = a.Substring("--filter=".Length);
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        Console.Error.WriteLine($"Error: invalid --filter value '{raw}'. Must be a non-empty substring.");
                        return 2;
                    }
                    filterArg = raw;
                }
                else if (a.StartsWith("--jobs=", StringComparison.Ordinal))
                {
                    if (seenJobs)
                    {
                        Console.Error.WriteLine("Error: duplicate --jobs option.");
                        return 2;
                    }
                    seenJobs = true;
                    string raw = a.Substring("--jobs=".Length);
                    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out jobs) || jobs < 1)
                    {
                        Console.Error.WriteLine($"Error: invalid --jobs value '{raw}'. Must be integer >= 1.");
                        return 2;
                    }
                }
                else if (a.StartsWith("--resources-root=", StringComparison.Ordinal))
                {
                    if (seenResourcesRoot)
                    {
                        Console.Error.WriteLine("Error: duplicate --resources-root option.");
                        return 2;
                    }
                    seenResourcesRoot = true;
                    string raw = a.Substring("--resources-root=".Length);
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        Console.Error.WriteLine($"Error: invalid --resources-root value '{raw}'. Must be a non-empty directory path.");
                        return 2;
                    }
                    resourcesRoot = raw;
                }
                else if (a.StartsWith("--shard-indices=", StringComparison.Ordinal))
                {
                    if (seenShardIndices)
                    {
                        Console.Error.WriteLine("Error: duplicate --shard-indices option.");
                        return 2;
                    }
                    seenShardIndices = true;
                    string raw = a.Substring("--shard-indices=".Length);
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        Console.Error.WriteLine($"Error: invalid --shard-indices value '{raw}'. Must be a non-empty comma-separated list of indices.");
                        return 2;
                    }
                    shardIndicesArg = raw;
                }
                else if (a.StartsWith("--result-file=", StringComparison.Ordinal))
                {
                    if (seenResultFile)
                    {
                        Console.Error.WriteLine("Error: duplicate --result-file option.");
                        return 2;
                    }
                    seenResultFile = true;
                    string raw = a.Substring("--result-file=".Length);
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        Console.Error.WriteLine($"Error: invalid --result-file value '{raw}'. Must be a non-empty file path.");
                        return 2;
                    }
                    resultFileArg = raw;
                }
                else
                {
                    Console.Error.WriteLine($"Error: unrecognized argument '{a}'.");
                    PrintUsage(Console.Error);
                    return 2;
                }
            }

            if (seenResourcesRoot)
            {
                if (File.Exists(resourcesRoot))
                {
                    Console.Error.WriteLine($"Error: --resources-root path is a file, not a directory: '{resourcesRoot}'.");
                    return 2;
                }
                if (!Directory.Exists(resourcesRoot))
                {
                    Console.Error.WriteLine($"Error: --resources-root directory does not exist: '{resourcesRoot}'.");
                    return 2;
                }
                ConfigLoader.CliResourcesRoot = resourcesRoot;
            }

            if (showHelp)
            {
                if (seenShardIndices || seenResultFile)
                {
                    Console.Error.WriteLine("Error: --help cannot be combined with internal worker flags (--shard-indices, --result-file).");
                    return 2;
                }
                PrintUsage(Console.Out);
                return 0;
            }

            // Internal isolated worker mode for process-sharded --jobs=N
            if (seenShardIndices || seenResultFile)
            {
                if (!seenShardIndices || !seenResultFile)
                {
                    Console.Error.WriteLine("Error: internal worker mode requires both --shard-indices and --result-file.");
                    return 2;
                }
                if (seenTier || seenFilter || seenJobs || listOnly || verbose)
                {
                    Console.Error.WriteLine("Error: internal worker mode cannot be combined with --tier, --filter, --jobs, --list, or --verbose.");
                    return 2;
                }
                return RunWorkerShard(allTests, shardIndicesArg, resultFileArg);
            }

            var selected = new List<(int GlobalIndex, E2ETestCase Test)>();
            for (int i = 0; i < allTests.Count; i++)
            {
                var tc = allTests[i];
                if (selectedTier.HasValue && tc.Tier != selectedTier.Value)
                    continue;
                if (!string.IsNullOrEmpty(filterArg))
                {
                    bool match =
                        tc.Id.IndexOf(filterArg, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tc.Feature.IndexOf(filterArg, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tc.Name.IndexOf(filterArg, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!match) continue;
                }
                selected.Add((i, tc));
            }

            if (selected.Count == 0)
            {
                Console.Error.WriteLine($"Error: zero tests matched (--tier={tierArg}, --filter={filterArg ?? "<none>"}).");
                return 2;
            }

            if (listOnly)
            {
                PrintTestList(selected);
                return 0;
            }

            var totalSw = Stopwatch.StartNew();
            List<TestResult> results = jobs == 1 || selected.Count == 1
                ? ExecuteSerial(selected)
                : ExecuteShardedProcesses(selected, Math.Min(jobs, selected.Count), resourcesRoot);
            totalSw.Stop();

            PrintReport(results, totalSw.Elapsed.TotalMilliseconds, jobs, verbose);

            bool anyBad = results.Any(r => r.Outcome != TestOutcome.Pass);
            return anyBad ? 1 : 0;
        }

        private static List<E2ETestCase> BuildCanonicalRegistry()
        {
            var list = new List<E2ETestCase>(160);
            list.AddRange(Tier1CoreTests.All());
            list.AddRange(Tier2BoundaryTests.All());
            list.AddRange(Tier3PairwiseTests.All());
            list.AddRange(Tier4ScenarioTests.All());

            // Verify registry uniqueness at startup
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tc in list)
            {
                if (!ids.Add(tc.Id))
                    throw new InvalidOperationException("Duplicate E2E test ID in registry: " + tc.Id);
            }
            return list;
        }

        private static TestResult RunOne(int index, E2ETestCase tc)
        {
            var sw = Stopwatch.StartNew();
            var res = new TestResult
            {
                Index = index,
                Id = tc.Id,
                Tier = tc.Tier,
                Feature = tc.Feature,
                Name = tc.Name,
                ExpectedOnM2 = tc.ExpectedOnM2
            };
            try
            {
                tc.Run();
                res.Outcome = TestOutcome.Pass;
                res.Message = "";
            }
            catch (E2EAssertionException ex)
            {
                res.Outcome = TestOutcome.Fail;
                res.Message = ex.Message;
            }
            catch (Exception ex)
            {
                res.Outcome = TestOutcome.Crash;
                res.Message = ex.GetType().Name + ": " + ex.Message + " | " + (ex.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim();
            }
            sw.Stop();
            res.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        private static List<TestResult> ExecuteSerial(List<(int GlobalIndex, E2ETestCase Test)> selected)
        {
            var results = new List<TestResult>(selected.Count);
            foreach (var item in selected)
                results.Add(RunOne(item.GlobalIndex, item.Test));
            return results;
        }

        private static int RunWorkerShard(List<E2ETestCase> allTests, string shardIndicesArg, string resultFile)
        {
            if (string.IsNullOrWhiteSpace(resultFile) || Directory.Exists(resultFile))
            {
                Console.Error.WriteLine($"Worker error: invalid --result-file path '{resultFile}'.");
                return 2;
            }
            string parentDir;
            try { parentDir = Path.GetDirectoryName(Path.GetFullPath(resultFile)); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                Console.Error.WriteLine($"Worker error: invalid --result-file path '{resultFile}': {ex.Message}");
                return 2;
            }
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                Console.Error.WriteLine($"Worker error: --result-file parent directory does not exist: '{parentDir}'.");
                return 2;
            }

            var indices = new List<int>();
            var seen = new HashSet<int>();
            foreach (string part in shardIndicesArg.Split(','))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int idx) || idx < 0 || idx >= allTests.Count)
                {
                    Console.Error.WriteLine($"Worker error: invalid shard index '{part}'.");
                    return 2;
                }
                if (!seen.Add(idx))
                {
                    Console.Error.WriteLine($"Worker error: duplicate shard index '{idx}'.");
                    return 2;
                }
                indices.Add(idx);
            }

            if (indices.Count == 0)
            {
                Console.Error.WriteLine("Worker error: --shard-indices produced zero indices.");
                return 2;
            }

            // Probe writability before running any test so unwritable targets fail fast with exit code 2.
            if (!TryWriteResultFile(resultFile, Array.Empty<string>())) return 2;

            var lines = new List<string>(indices.Count);
            foreach (int idx in indices)
            {
                var r = RunOne(idx, allTests[idx]);
                string b64Msg = Convert.ToBase64String(Encoding.UTF8.GetBytes(r.Message ?? ""));
                lines.Add(string.Join("\t",
                    r.Index.ToString(CultureInfo.InvariantCulture),
                    ((int)r.Outcome).ToString(CultureInfo.InvariantCulture),
                    r.ElapsedMs.ToString("R", CultureInfo.InvariantCulture),
                    b64Msg));
            }
            return TryWriteResultFile(resultFile, lines) ? 0 : 2;
        }

        private static bool TryWriteResultFile(string resultFile, IEnumerable<string> lines)
        {
            try
            {
                File.WriteAllLines(resultFile, lines, Encoding.UTF8);
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is ArgumentException ||
                ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                Console.Error.WriteLine($"Worker error: cannot write --result-file '{resultFile}': {ex.Message}");
                return false;
            }
        }

        private static void MarkShardCrash(
            Dictionary<int, TestResult> byGlobalIndex,
            Dictionary<int, E2ETestCase> metaByGlobalIndex,
            List<int> shard,
            string message)
        {
            foreach (int idx in shard)
            {
                var tc = metaByGlobalIndex[idx];
                byGlobalIndex[idx] = new TestResult
                {
                    Index = idx,
                    Id = tc.Id,
                    Tier = tc.Tier,
                    Feature = tc.Feature,
                    Name = tc.Name,
                    ExpectedOnM2 = tc.ExpectedOnM2,
                    Outcome = TestOutcome.Crash,
                    ElapsedMs = 0.0,
                    Message = message
                };
            }
        }

        private static List<TestResult> ExecuteShardedProcesses(
            List<(int GlobalIndex, E2ETestCase Test)> selected,
            int workerCount,
            string resourcesRoot)
        {
            var shards = new List<int>[workerCount];
            for (int w = 0; w < workerCount; w++) shards[w] = new List<int>();
            for (int i = 0; i < selected.Count; i++)
                shards[i % workerCount].Add(selected[i].GlobalIndex);

            string processPath = Environment.ProcessPath;
            string entryDll = Assembly.GetEntryAssembly()?.Location;
            bool isDotnetHost = !string.IsNullOrEmpty(processPath) &&
                Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(processPath))
                throw new InvalidOperationException("Unable to determine current executable path for process sharding.");
            if (isDotnetHost && string.IsNullOrEmpty(entryDll))
                throw new InvalidOperationException("Unable to determine entry assembly path under dotnet host.");

            string tempDir = Path.Combine(Path.GetTempPath(), "civic_e2e_shards_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var procs = new List<(Process Proc, Task<string> StdOut, Task<string> StdErr, string ResultFile, List<int> Shard)>();
            try
            {
                for (int w = 0; w < workerCount; w++)
                {
                    if (shards[w].Count == 0) continue;
                    string resultFile = Path.Combine(tempDir, $"shard_{w}.tsv");
                    string csv = string.Join(",", shards[w].Select(x => x.ToString(CultureInfo.InvariantCulture)));

                    var psi = new ProcessStartInfo
                    {
                        FileName = processPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    if (isDotnetHost)
                        psi.ArgumentList.Add(entryDll);
                    psi.ArgumentList.Add("--shard-indices=" + csv);
                    psi.ArgumentList.Add("--result-file=" + resultFile);
                    if (!string.IsNullOrEmpty(resourcesRoot))
                        psi.ArgumentList.Add("--resources-root=" + resourcesRoot);

                    var proc = Process.Start(psi);
                    if (proc == null)
                        throw new InvalidOperationException("Failed to start E2E worker child process.");
                    // Drain both pipes concurrently from spawn to avoid intra-worker and cross-worker pipe deadlocks.
                    var stdOutTask = proc.StandardOutput.ReadToEndAsync();
                    var stdErrTask = proc.StandardError.ReadToEndAsync();
                    procs.Add((proc, stdOutTask, stdErrTask, resultFile, shards[w]));
                }

                var byGlobalIndex = new Dictionary<int, TestResult>(selected.Count);
                var metaByGlobalIndex = selected.ToDictionary(x => x.GlobalIndex, x => x.Test);

                foreach (var entry in procs)
                {
                    Task.WaitAll(entry.StdOut, entry.StdErr);
                    entry.Proc.WaitForExit();
                    string stdout = entry.StdOut.Result;
                    string stderr = entry.StdErr.Result;

                    if (entry.Proc.ExitCode != 0 || !File.Exists(entry.ResultFile))
                    {
                        MarkShardCrash(byGlobalIndex, metaByGlobalIndex, entry.Shard,
                            $"Worker shard process exited {entry.Proc.ExitCode}: {stderr.Trim()} {stdout.Trim()}".Trim());
                        continue;
                    }

                    var expectedSet = new HashSet<int>(entry.Shard);
                    foreach (string line in File.ReadAllLines(entry.ResultFile, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string[] cols = line.Split('\t');
                        if (cols.Length != 4
                            || !int.TryParse(cols[0], NumberStyles.None, CultureInfo.InvariantCulture, out int idx)
                            || !expectedSet.Contains(idx)
                            || !int.TryParse(cols[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rawOutcome)
                            || !Enum.IsDefined(typeof(TestOutcome), rawOutcome)
                            || !double.TryParse(cols[2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double elapsedMs))
                        {
                            continue;
                        }
                        string msg;
                        try
                        {
                            msg = Encoding.UTF8.GetString(Convert.FromBase64String(cols[3]));
                        }
                        catch (FormatException)
                        {
                            continue;
                        }
                        var tc = metaByGlobalIndex[idx];
                        byGlobalIndex[idx] = new TestResult
                        {
                            Index = idx,
                            Id = tc.Id,
                            Tier = tc.Tier,
                            Feature = tc.Feature,
                            Name = tc.Name,
                            ExpectedOnM2 = tc.ExpectedOnM2,
                            Outcome = (TestOutcome)rawOutcome,
                            ElapsedMs = elapsedMs,
                            Message = msg
                        };
                    }

                    // Guard against truncated/malformed result files leaving missing shard entries.
                    foreach (int idx in entry.Shard)
                    {
                        if (!byGlobalIndex.ContainsKey(idx))
                        {
                            var tc = metaByGlobalIndex[idx];
                            byGlobalIndex[idx] = new TestResult
                            {
                                Index = idx,
                                Id = tc.Id,
                                Tier = tc.Tier,
                                Feature = tc.Feature,
                                Name = tc.Name,
                                ExpectedOnM2 = tc.ExpectedOnM2,
                                Outcome = TestOutcome.Crash,
                                ElapsedMs = 0.0,
                                Message = $"Worker shard did not emit a valid result row for index {idx}."
                            };
                        }
                    }
                }

                return selected.Select(s => byGlobalIndex[s.GlobalIndex]).ToList();
            }
            finally
            {
                foreach (var entry in procs)
                {
                    try { if (!entry.Proc.HasExited) entry.Proc.Kill(entireProcessTree: true); } catch { }
                    try { entry.Proc.Dispose(); } catch { }
                }
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { }
            }
        }

        private static void PrintTestList(List<(int GlobalIndex, E2ETestCase Test)> selected)
        {
            Console.WriteLine("ID        | Tier | Feature | M2Expected   | Name");
            Console.WriteLine("----------+------+---------+--------------+------------------------------------------------------------");
            foreach (var item in selected)
            {
                var tc = item.Test;
                Console.WriteLine($"{tc.Id,-9} | T{tc.Tier,-3} | {tc.Feature,-7} | {tc.ExpectedOnM2,-12} | {tc.Name}");
            }
            Console.WriteLine();
            Console.WriteLine("Tier Counts:");
            for (int t = 1; t <= 4; t++)
            {
                int n = selected.Count(x => x.Test.Tier == t);
                if (n > 0) Console.WriteLine($"  Tier {t}: {n}");
            }
            Console.WriteLine($"  TOTAL : {selected.Count}");

            Console.WriteLine();
            Console.WriteLine("Feature Counts:");
            foreach (var grp in selected.GroupBy(x => x.Test.Feature).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                int t1 = grp.Count(x => x.Test.Tier == 1);
                int t2 = grp.Count(x => x.Test.Tier == 2);
                int t3 = grp.Count(x => x.Test.Tier == 3);
                int t4 = grp.Count(x => x.Test.Tier == 4);
                Console.WriteLine($"  {grp.Key}: total={grp.Count(),2} (T1={t1}, T2={t2}, T3={t3}, T4={t4})");
            }

            Console.WriteLine();
            Console.WriteLine("M2 Baseline Expectation Counts:");
            foreach (M2Status st in new[] { M2Status.PassOnM2, M2Status.FailUntilM3, M2Status.FailUntilM4 })
                Console.WriteLine($"  {st,-12}: {selected.Count(x => x.Test.ExpectedOnM2 == st)}");
        }

        private static void PrintReport(List<TestResult> results, double wallMs, int jobs, bool verbose)
        {
            if (verbose)
            {
                foreach (var r in results)
                {
                    string tag = r.Outcome == TestOutcome.Pass ? "PASS" : (r.Outcome == TestOutcome.Fail ? "FAIL" : "CRASH");
                    Console.WriteLine($"[{tag,-5}] {r.Id,-9} (T{r.Tier}/{r.Feature}/{r.ExpectedOnM2,-11}) {r.ElapsedMs,7:F1} ms | {r.Name}");
                    if (r.Outcome != TestOutcome.Pass && !string.IsNullOrEmpty(r.Message))
                        Console.WriteLine($"        -> {r.Message}");
                }
                Console.WriteLine();
            }

            var failures = results.Where(r => r.Outcome != TestOutcome.Pass).ToList();
            if (failures.Count > 0 && !verbose)
            {
                Console.WriteLine("=== FAILURES & CRASHES ===");
                foreach (var r in failures)
                {
                    string tag = r.Outcome == TestOutcome.Fail ? "FAIL" : "CRASH";
                    Console.WriteLine($"[{tag,-5}] {r.Id,-9} (T{r.Tier}/{r.Feature}/{r.ExpectedOnM2,-11}) {r.ElapsedMs,6:F1} ms : {r.Message}");
                }
                Console.WriteLine();
            }

            Console.WriteLine("=== SUMMARY BY TIER ===");
            Console.WriteLine("Tier   | Total | PASS | FAIL | CRASH | Sum Time (ms)");
            Console.WriteLine("-------+-------+------+------+-------+--------------");
            for (int t = 1; t <= 4; t++)
            {
                var sub = results.Where(r => r.Tier == t).ToList();
                if (sub.Count == 0) continue;
                Console.WriteLine($"Tier {t} | {sub.Count,5} | {sub.Count(r => r.Outcome == TestOutcome.Pass),4} | {sub.Count(r => r.Outcome == TestOutcome.Fail),4} | {sub.Count(r => r.Outcome == TestOutcome.Crash),5} | {sub.Sum(r => r.ElapsedMs),13:F1}");
            }
            Console.WriteLine("-------+-------+------+------+-------+--------------");
            Console.WriteLine($"TOTAL  | {results.Count,5} | {results.Count(r => r.Outcome == TestOutcome.Pass),4} | {results.Count(r => r.Outcome == TestOutcome.Fail),4} | {results.Count(r => r.Outcome == TestOutcome.Crash),5} | {results.Sum(r => r.ElapsedMs),13:F1}");

            Console.WriteLine();
            Console.WriteLine("=== SUMMARY BY FEATURE ===");
            Console.WriteLine("Feature | Total | PASS | FAIL | CRASH");
            Console.WriteLine("--------+-------+------+------+------");
            foreach (var grp in results.GroupBy(r => r.Feature).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"{grp.Key,-7} | {grp.Count(),5} | {grp.Count(r => r.Outcome == TestOutcome.Pass),4} | {grp.Count(r => r.Outcome == TestOutcome.Fail),4} | {grp.Count(r => r.Outcome == TestOutcome.Crash),5}");
            }

            Console.WriteLine();
            Console.WriteLine("=== SUMMARY BY M2 EXPECTATION ===");
            Console.WriteLine("M2Expected   | Total | PASS | FAIL | CRASH");
            Console.WriteLine("-------------+-------+------+------+------");
            foreach (M2Status st in new[] { M2Status.PassOnM2, M2Status.FailUntilM3, M2Status.FailUntilM4 })
            {
                var sub = results.Where(r => r.ExpectedOnM2 == st).ToList();
                if (sub.Count == 0) continue;
                Console.WriteLine($"{st,-12} | {sub.Count,5} | {sub.Count(r => r.Outcome == TestOutcome.Pass),4} | {sub.Count(r => r.Outcome == TestOutcome.Fail),4} | {sub.Count(r => r.Outcome == TestOutcome.Crash),5}");
            }

            Console.WriteLine();
            Console.WriteLine($"Wall time: {wallMs:F1} ms ({wallMs / 1000.0:F2} s) with --jobs={jobs} | PASS={results.Count(r => r.Outcome == TestOutcome.Pass)} FAIL={results.Count(r => r.Outcome == TestOutcome.Fail)} CRASH={results.Count(r => r.Outcome == TestOutcome.Crash)} / {results.Count}");
        }

        private static void PrintUsage(TextWriter writer)
        {
            writer.WriteLine("Usage: Anthropocene.CivicGovernanceE2E [--tier=1|2|3|4|all] [--list] [--filter=<substr>] [--jobs=N] [--resources-root=<dir>] [--verbose]");
        }
    }
}
