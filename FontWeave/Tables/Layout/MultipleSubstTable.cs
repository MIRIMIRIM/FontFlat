using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>A static GSUB type-2 fragment: one Coverage plus the per-coverage-index
/// substitution sequences. An empty sequence is the card-adjudicated legal deletion
/// no-op (OQ-T4-03-2, W9), keyed correspondence is Coverage index to sequence index.</summary>
public sealed class MultipleSubstTable
{
    private readonly ushort[][] _sequences;

    internal MultipleSubstTable(CoverageTable coverage, ushort[][] sequences,
        DataLocation location, int consumedLength)
    {
        Coverage = coverage;
        _sequences = sequences;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public CoverageTable Coverage { get; }
    public int NumGlyphs => Coverage.NumGlyphs;
    public int SequenceCount => _sequences.Length;
    /// <summary>The sequence for one Coverage index; may be empty (legal deletion).</summary>
    public ReadOnlySpan<ushort> GetSequence(int ordinal) => _sequences[ordinal];
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    public ReadStatus TryGetSequence(int glyphId, out ReadOnlySpan<ushort> substitutes,
        out FontDiagnostic diagnostic)
    {
        substitutes = default;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)NumGlyphs);
        int ordinal = Coverage.FindGlyph(glyphId);
        if (ordinal < 0)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Coverage.Location.AtLocal(DataField.GlyphId, 0), out diagnostic, (ulong)glyphId);
        substitutes = _sequences[ordinal];
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryWithSequences(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ushort[]> sequences,
        out MultipleSubstTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        MultipleSubstTableCompiler.TryCreate(glyphs, sequences, NumGlyphs, out updated,
            out diagnostic, limits, Location, Coverage.Format);
}
