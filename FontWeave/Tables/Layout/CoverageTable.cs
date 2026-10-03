using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Owned, ordered coverage keys. NumGlyphs is the enclosing font's glyph count.</summary>
public sealed class CoverageTable
{
    private readonly ushort[] _glyphs;

    internal CoverageTable(ushort[] glyphs, int numGlyphs, ushort format,
        DataLocation location, int consumedLength, int rangeCount)
    {
        _glyphs = glyphs;
        NumGlyphs = numGlyphs;
        Format = format;
        Location = location;
        ConsumedLength = consumedLength;
        RangeCount = rangeCount;
    }

    public ReadOnlySpan<ushort> Glyphs => _glyphs;
    public int NumGlyphs { get; }
    public ushort Format { get; }
    public DataLocation Location { get; }
    /// <summary>Bytes consumed by the source leaf, not the length of its enclosing read window.</summary>
    public int ConsumedLength { get; }
    internal int RangeCount { get; }

    public int FindGlyph(int glyphId)
    {
        if (glyphId is < 0 or > ushort.MaxValue) return -1;
        int index = Array.BinarySearch(_glyphs, (ushort)glyphId);
        return index >= 0 ? index : -1;
    }

    public ReadStatus TryWithGlyphs(ReadOnlySpan<ushort> glyphs, out CoverageTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        CoverageTableCompiler.TryCreate(glyphs, NumGlyphs, Format, out updated,
            out diagnostic, limits, Location);
}
