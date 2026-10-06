using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>A static GSUB type-3 fragment: one Coverage plus the per-coverage-index
/// alternative sets. Alternates are functionally equivalent glyphs in arbitrary order;
/// an empty set is the card-adjudicated legal no-op (OQ-T4-03-2).</summary>
public sealed class AlternateSubstTable
{
    private readonly ushort[][] _alternateSets;

    internal AlternateSubstTable(CoverageTable coverage, ushort[][] alternateSets,
        DataLocation location, int consumedLength)
    {
        Coverage = coverage;
        _alternateSets = alternateSets;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public CoverageTable Coverage { get; }
    public int NumGlyphs => Coverage.NumGlyphs;
    public int AlternateSetCount => _alternateSets.Length;
    /// <summary>The alternate set for one Coverage index; may be empty (legal no-op).</summary>
    public ReadOnlySpan<ushort> GetAlternateSet(int ordinal) => _alternateSets[ordinal];
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    public ReadStatus TryGetAlternateSet(int glyphId, out ReadOnlySpan<ushort> alternates,
        out FontDiagnostic diagnostic)
    {
        alternates = default;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)NumGlyphs);
        int ordinal = Coverage.FindGlyph(glyphId);
        if (ordinal < 0)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Coverage.Location.AtLocal(DataField.GlyphId, 0), out diagnostic, (ulong)glyphId);
        alternates = _alternateSets[ordinal];
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryWithAlternateSets(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ushort[]> alternateSets,
        out AlternateSubstTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        AlternateSubstTableCompiler.TryCreate(glyphs, alternateSets, NumGlyphs, out updated,
            out diagnostic, limits, Location, Coverage.Format);
}
