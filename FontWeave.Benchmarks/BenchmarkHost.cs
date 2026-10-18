namespace FontWeave.Benchmarks;

/// <summary>Process-wide benchmark host state: the resolved corpus directory and the
/// active font label lists. Set once from the command line before the runner starts;
/// every consumer is read-only afterwards. Corpus resolution is also lazy so a runner
/// that never dispatches through <see cref="Program"/> (or a fresh default run) still
/// finds the corpus by the documented upward walk.</summary>
public static class BenchmarkHost
{
    private static string? _inputsDir;
    private static string[] _sfntFontLabels = Corpus.DefaultSfntFiles;
    private static string[] _ttcFontLabels = Corpus.DefaultTtcFiles;

    public static string InputsDir => _inputsDir ??= Corpus.ResolveInputsDir(null);

    public static IReadOnlyList<string> SfntFontLabels => _sfntFontLabels;
    public static IReadOnlyList<string> TtcFontLabels => _ttcFontLabels;

    public static void Initialize(string? inputsDir, string[]? sfntOverride, string[]? ttcOverride)
    {
        _inputsDir = Corpus.ResolveInputsDir(inputsDir);
        // Publish for BenchmarkDotNet's child processes: GlobalSetup runs there, and a
        // non-default corpus location would otherwise be invisible to them.
        Environment.SetEnvironmentVariable(Corpus.InputsEnvironmentVariable, _inputsDir);
        if (sfntOverride is { Length: > 0 }) _sfntFontLabels = sfntOverride;
        if (ttcOverride is { Length: > 0 }) _ttcFontLabels = ttcOverride;
    }

    public static FontInput LoadSfnt(string label) => CorpusLoader.LoadSfnt(InputsDir, label);

    public static TtcInput LoadTtc(string label) => CorpusLoader.LoadTtc(InputsDir, label);
}
