using BenchmarkDotNet.Running;

namespace FontWeave.Benchmarks;

/// <summary>Entry point.
///
///   (default | bench)   BenchmarkDotNet over the pinned corpus; forward any BDN
///                       arguments (--filter * --job short, ...).
///   smoke [files...]    single-shot validation of every scenario over the named
///                       corpus files (default: roboto + cambria-math.ttc); options
///                       --rounds N.
///   list                print the resolved corpus directory and the active labels.
///
/// Host options (consumed here, never forwarded to BDN):
///   --inputs &lt;dir&gt;     corpus directory override; default resolves bench-inputs/
///                       by walking up from the program directory.
///   --fonts a,b,c       replaces the sfnt parameter list for this run (file names
///                       inside the corpus directory; .ttc entries go to the TTC set).
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Dispatch(args);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FontWeave.Benchmarks: {error.Message}");
            return 1;
        }
    }

    private static int Dispatch(string[] args)
    {
        string? inputsDir = null;
        string[]? fontOverride = null;
        var forward = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--inputs")
            {
                if (++i >= args.Length) return Usage("--inputs requires a value.");
                inputsDir = args[i];
            }
            else if (arg.StartsWith("--inputs=", StringComparison.Ordinal))
            {
                inputsDir = arg["--inputs=".Length..];
            }
            else if (arg == "--fonts")
            {
                if (++i >= args.Length) return Usage("--fonts requires a value.");
                fontOverride = SplitLabels(args[i]);
            }
            else if (arg.StartsWith("--fonts=", StringComparison.Ordinal))
            {
                fontOverride = SplitLabels(arg["--fonts=".Length..]);
            }
            else
            {
                forward.Add(arg);
            }
        }

        string mode = "bench";
        if (forward.Count > 0 && forward[0] is "bench" or "smoke" or "list")
        {
            mode = forward[0];
            forward.RemoveAt(0);
        }

        BenchmarkHost.Initialize(inputsDir, SplitSfnt(fontOverride), SplitTtc(fontOverride));

        switch (mode)
        {
            case "list":
                Console.WriteLine($"corpus directory: {BenchmarkHost.InputsDir}");
                Console.WriteLine("sfnt scenarios: " + string.Join(", ", BenchmarkHost.SfntFontLabels));
                Console.WriteLine("ttc scenarios:  " + string.Join(", ", BenchmarkHost.TtcFontLabels));
                return 0;
            case "smoke":
                return SmokeRunner.Run(ResolveSmokeFiles(forward), ParseRounds(forward));
            default:
                BenchmarkDotNet.Running.BenchmarkSwitcher
                    .FromAssembly(typeof(Program).Assembly).Run([.. forward]);
                return 0;
        }
    }

    private static string[] ResolveSmokeFiles(List<string> forward)
    {
        var files = forward.Where(arg => !arg.StartsWith('-') && !int.TryParse(arg, out _)).ToList();
        if (files.Count > 0) return [.. files];
        return
        [
            Corpus.DefaultSfntFiles[0],       // overlay-roboto-foundation-layout-repaired.sfnt
            Corpus.DefaultTtcFiles[0],        // cambria-math.ttc
        ];
    }

    private static int? ParseRounds(List<string> forward)
    {
        for (int i = 0; i < forward.Count; i++)
        {
            if (forward[i] == "--rounds" && i + 1 < forward.Count && int.TryParse(forward[i + 1], out int rounds))
                return rounds;
            if (forward[i].StartsWith("--rounds=", StringComparison.Ordinal) &&
                int.TryParse(forward[i]["--rounds=".Length..], out int inline))
                return inline;
        }
        return null;
    }

    private static string[]? SplitSfnt(string[]? labels) => labels?
        .Where(label => !label.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)).ToArray();

    private static string[]? SplitTtc(string[]? labels) => labels?
        .Where(label => label.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)).ToArray();

    private static string[] SplitLabels(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(label => label.Length > 0).ToArray();

    private static int Usage(string message)
    {
        Console.Error.WriteLine($"FontWeave.Benchmarks: {message}");
        Console.Error.WriteLine("see the project README for the argument reference.");
        return 2;
    }
}
