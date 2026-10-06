using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Owned AttachList: the required Coverage and one contour-point list per
/// covered glyph, in Coverage index order. Repeated point lists keep shared identity
/// with their source offsets.</summary>
public sealed class AttachListTable
{
    private readonly ushort[][] _points;
    private readonly uint[] _sourceOffsets;

    internal AttachListTable(CoverageTable coverage, ushort[][] points, int numGlyphs,
        DataLocation location, int consumedLength, uint[] sourceOffsets)
    {
        Coverage = coverage;
        _points = points;
        NumGlyphs = numGlyphs;
        Location = location;
        ConsumedLength = consumedLength;
        _sourceOffsets = sourceOffsets;
    }

    public CoverageTable Coverage { get; }
    public int Count => _points.Length;
    /// <summary>Point indices per covered glyph; identical lists repeat per glyph.</summary>
    public ReadOnlySpan<ushort> Points(int glyphIndex) => _points[glyphIndex];
    public int NumGlyphs { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
    /// <summary>Original parent-relative wire references; empty for caller-created models.</summary>
    public ReadOnlySpan<uint> SourceOffsets => _sourceOffsets;
}
