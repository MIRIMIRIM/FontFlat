using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>A static GSUB type-1 fragment, retaining the keyed replacement correspondence.</summary>
public sealed class SingleSubstTable
{
    private readonly ushort[] _substitutes;

    internal SingleSubstTable(CoverageTable coverage, ushort[] substitutes, ushort format,
        DataLocation location, int consumedLength)
    {
        Coverage = coverage;
        _substitutes = substitutes;
        Format = format;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public CoverageTable Coverage { get; }
    public ReadOnlySpan<ushort> Glyphs => Coverage.Glyphs;
    public ReadOnlySpan<ushort> Substitutes => _substitutes;
    public int NumGlyphs => Coverage.NumGlyphs;
    public ushort Format { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    public ReadStatus TryGetSubstitute(int glyphId, out ushort substitute, out FontDiagnostic diagnostic)
    {
        substitute = 0;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)NumGlyphs);
        int ordinal = Coverage.FindGlyph(glyphId);
        if (ordinal < 0)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Coverage.Location.AtLocal(DataField.GlyphId, 0), out diagnostic, (ulong)glyphId);
        substitute = _substitutes[ordinal];
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryWithMappings(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ushort> substitutes,
        out SingleSubstTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        SingleSubstTableCompiler.TryCreate(glyphs, substitutes, NumGlyphs, Format,
            out updated, out diagnostic, limits, Location, Coverage.Format);
}
