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

    // The T4-04 edit API: one substitute slot, re-checked against the glyph domain
    // so the edit stays spec-conforming (the chain shape cannot change).
    public ReadStatus TryWithSubstitute(int index, ushort substitute, int numGlyphs,
        out ReverseChainSubstTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        if (index < 0 || index >= _substitutes.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)index), (ulong)_substitutes.Length);
        var budget = new BuildBudget(limits);
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (substitute >= numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, (ulong)index), out diagnostic,
                substitute, (ulong)numGlyphs);
        var copy = (ushort[])_substitutes.Clone();
        copy[index] = substitute;
        updated = new(Coverage, _backtrackCoverages, _lookaheadCoverages, copy, numGlyphs,
            Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
