using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Owned GSUB type-8 subtable (ReverseChainSingleSubst format 1): the
/// coverage, the backtrack/lookahead coverage chains and the substitute glyph list.
/// The backtrack chain is the declared array; the lookahead chain, glyph count and
/// substitutes are the hand-walked computed-start tail (OQ-5's V1 boundary). An
/// empty backtrack or lookahead chain is legal (the gsub8 manual pin's
/// glyphCount=1 zero-chain subtable); the substitutes count equals the coverage
/// individual count (the SingleSubst format-2 identity, enforced at the walk).</summary>
public sealed class ReverseChainSubstTable
{
    private readonly CoverageTable[] _backtrackCoverages;
    private readonly CoverageTable[] _lookaheadCoverages;
    private readonly ushort[] _substitutes;

    internal ReverseChainSubstTable(CoverageTable coverage, CoverageTable[] backtrackCoverages,
        CoverageTable[] lookaheadCoverages, ushort[] substitutes, int numGlyphs,
        DataLocation location, int consumedLength)
    {
        Coverage = coverage;
        _backtrackCoverages = backtrackCoverages;
        _lookaheadCoverages = lookaheadCoverages;
        _substitutes = substitutes;
        NumGlyphs = numGlyphs; Location = location; ConsumedLength = consumedLength;
    }

    public CoverageTable Coverage { get; }

    public IReadOnlyList<CoverageTable> BacktrackCoverages => _backtrackCoverages;

    public IReadOnlyList<CoverageTable> LookaheadCoverages => _lookaheadCoverages;

    /// <summary>One substitute per coverage entry, in coverage order.</summary>
    public ReadOnlySpan<ushort> Substitutes => _substitutes;

    public int NumGlyphs { get; }

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}
