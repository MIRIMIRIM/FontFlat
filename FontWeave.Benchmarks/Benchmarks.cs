using BenchmarkDotNet.Attributes;

namespace FontWeave.Benchmarks;

/// <summary>The seven sfnt scenarios over the pinned corpus. Each benchmark is the
/// whole per-invocation command work (open chain, operation, output copy); the corpus
/// file name is the parameter. GlobalSetup prepares the input roots/ordinals the way a
/// CLI caller fixes them before a command; it is outside the measured region.
/// BenchmarkDotNet's default child-process toolchain stays enabled; the corpus
/// directory travels to the child through FONTWEAVE_BENCH_INPUTS (see Corpus).</summary>
[MemoryDiagnoser]
public class FontScenarioBenchmarks
{
    [ParamsSource(nameof(FontLabels))]
    public string Font { get; set; } = "";

    public IEnumerable<string> FontLabels() => BenchmarkHost.SfntFontLabels;

    private FontInput _input = null!;

    [GlobalSetup]
    public void Setup() => _input = BenchmarkHost.LoadSfnt(Font);

    [Benchmark]
    public long OpenFace() => Scenarios.OpenFace(_input);

    [Benchmark]
    public long ReadAll() => Scenarios.ReadAll(_input);

    [Benchmark]
    public long EmitRoundtrip() => Scenarios.EmitRoundtrip(_input);

    [Benchmark]
    public long SubsetRetain() => Scenarios.SubsetRetain(_input);

    [Benchmark]
    public long SubsetCompact() => Scenarios.SubsetCompact(_input);

    [Benchmark]
    public long SubsetCodepoints() => Scenarios.SubsetCodepoints(_input);

    [Benchmark]
    public long EditAndEmit() => Scenarios.EditAndEmit(_input);
}

/// <summary>The three TTC scenarios over the pinned collection. The typed operation
/// chain refuses collection faces by design (SfntDirectoryView.Validate), so the split
/// scenarios run the legacy write chain (FontFile → NameEditor → SfntWriter), which is
/// the same chain the CLI's name-replace command executes for a --face argument.</summary>
[MemoryDiagnoser]
public class TtcScenarioBenchmarks
{
    [ParamsSource(nameof(TtcLabels))]
    public string Font { get; set; } = "";

    public IEnumerable<string> TtcLabels() => BenchmarkHost.TtcFontLabels;

    private TtcInput _input = null!;

    [GlobalSetup]
    public void Setup() => _input = BenchmarkHost.LoadTtc(Font);

    [Benchmark]
    public long TtcOpen() => Scenarios.TtcOpen(_input);

    [Benchmark]
    public long TtcSplitFace0() => Scenarios.TtcSplitFace0(_input);

    [Benchmark]
    public long TtcSplitFace1() => Scenarios.TtcSplitFace1(_input);
}
