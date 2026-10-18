using System.Diagnostics;
using FontWeave;
using FontWeave.Core;

namespace FontWeave.Benchmarks;

/// <summary>Single-shot validation runner: one warmup plus N timed rounds per scenario
/// with Stopwatch, CPU time and GC.GetTotalAllocatedBytes deltas. This is a smoke gate
/// ("does every scenario execute over this input?") and prints per-round medians; it is
/// not the statistical benchmark (that is BenchmarkDotNet's role).</summary>
public static class SmokeRunner
{
    private const int DefaultRounds = 3;

    public static int Run(IReadOnlyList<string> fileLabels, int? roundsOverride)
    {
        int rounds = Math.Max(1, roundsOverride ?? DefaultRounds);
        if (fileLabels.Count == 0)
        {
            Console.Error.WriteLine("smoke: no input files given.");
            return 2;
        }
        Console.WriteLine($"FontWeave.Benchmarks smoke — {rounds} timed round(s) after one warmup, single thread");
        Console.WriteLine($"corpus: {BenchmarkHost.InputsDir}");
        Console.WriteLine();
        Console.WriteLine("file                                       scenario           outcome                        output-bytes      median-ms   alloc-bytes/round   cpu-ms");

        int failures = 0;
        foreach (string label in fileLabels)
        {
            string path = Path.Combine(BenchmarkHost.InputsDir, label);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"smoke: missing corpus file {path}");
                failures++;
                continue;
            }
            bool isCollection = label.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase);
            failures += isCollection ? SmokeTtc(label) : SmokeSfnt(label, rounds);
        }
        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "smoke: every scenario returned a defined outcome; refusals above are the product's finite decisions, not harness errors."
            : $"smoke: {failures} outcome row(s) missing (see errors above).");
        return failures == 0 ? 0 : 1;
    }

    private static int SmokeSfnt(string label, int rounds)
    {
        FontInput input = BenchmarkHost.LoadSfnt(label);
        Console.WriteLine($"# {label}: numGlyphs={input.NumGlyphs}, gidRoot={input.Gids512.Length}, " +
                          $"codepoints={input.Codepoints.Length}, editOrdinal={input.EditOrdinal}");
        var rows = new (string Name, Func<long> Scenario)[]
        {
            ("OpenFace", () => Scenarios.OpenFace(input)),
            ("ReadAll", () => Scenarios.ReadAll(input)),
            ("EmitRoundtrip", () => Scenarios.EmitRoundtrip(input)),
            ("SubsetRetain", () => Scenarios.SubsetRetain(input)),
            ("SubsetCompact", () => Scenarios.SubsetCompact(input)),
            ("SubsetCodepoints", () => Scenarios.SubsetCodepoints(input)),
            ("EditAndEmit", () => Scenarios.EditAndEmit(input)),
        };
        int failures = 0;
        foreach (var (name, scenario) in rows)
        {
            if (!SmokeRow(label, name, scenario, rounds)) failures++;
        }
        return failures;
    }

    private static int SmokeTtc(string label)
    {
        TtcInput input = BenchmarkHost.LoadTtc(label);
        Console.WriteLine($"# {label}: faces={input.FaceCount}, editOrdinals=[{string.Join(',', input.EditOrdinals)}]");
        var rows = new (string Name, Func<long> Scenario)[]
        {
            ("TtcOpen", () => Scenarios.TtcOpen(input)),
            ("TtcSplitFace0", () => Scenarios.TtcSplitFace0(input)),
            ("TtcSplitFace1", () => Scenarios.TtcSplitFace1(input)),
        };
        int failures = 0;
        foreach (var (name, scenario) in rows)
        {
            if (!SmokeRow(label, name, scenario, rounds: 1)) failures++;
        }
        return failures;
    }

    /// <summary>One warmup, then timed rounds. Returns false only when the scenario
    /// threw an unexpected exception type (a finite ReadStatus refusal is a defined
    /// outcome and prints as one).</summary>
    private static bool SmokeRow(string label, string name, Func<long> scenario, int rounds)
    {
        try
        {
            _ = scenario();
            var times = new List<double>(rounds);
            var allocations = new List<long>(rounds);
            var cpu = new List<double>(rounds);
            for (int round = 0; round < rounds; round++)
            {
                long cpuStart = Process.GetCurrentProcess().TotalProcessorTime.Ticks;
                long allocStart = GC.GetTotalAllocatedBytes(precise: true);
                Stopwatch watch = Stopwatch.StartNew();
                long result = scenario();
                watch.Stop();
                long allocDelta = GC.GetTotalAllocatedBytes(precise: true) - allocStart;
                cpu.Add((Process.GetCurrentProcess().TotalProcessorTime.Ticks - cpuStart) * 1000.0
                    / TimeSpan.TicksPerSecond);
                times.Add(watch.Elapsed.TotalMilliseconds);
                allocations.Add(allocDelta);
                RecordOutcome(label, name, result, Median(times), Median(allocations), Median(cpu));
            }
            return true;
        }
        catch (FontDataException error)
        {
            Console.WriteLine($"{label,-42} {name,-18} {error.Error}  (FontDataException — recorded, not retried)");
            return true;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{label}: {name} threw {error.GetType().Name}: {error.Message}");
            return false;
        }
    }

    private static void RecordOutcome(string label, string name, long result,
        double medianMs, long medianAlloc, double medianCpuMs)
    {
        string outcome;
        long outputBytes;
        if (result >= 0)
        {
            outcome = "Done";
            outputBytes = result;
        }
        else
        {
            FontDiagnostic diagnostic = Scenarios.LastRefusal;
            outcome = $"refused({(ReadStatus)(-result)}/{diagnostic.Code}({(ulong)diagnostic.Detail}))";
            outputBytes = 0;
        }
        Console.WriteLine(
            $"{label,-42} {name,-18} {outcome,-30} {outputBytes,12:N0} {medianMs,12:F3} {medianAlloc,17:N0} {medianCpuMs,8:F3}");
    }

    private static double Median(IReadOnlyList<double> values)
    {
        double[] sorted = [.. values];
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static long Median(IReadOnlyList<long> values)
    {
        long[] sorted = [.. values];
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
}
