using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GDEF AttachList table.
/// </summary>
[OtSubTable(4)]
[OtField("CoverageOffset", OtFieldKind.UInt16, 0)]
[OtField("GlyphCount", OtFieldKind.UInt16, 2)]
[OtUInt16Array("AttachPointOffset", 4, CountPropertyName = "GlyphCount")]
[OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
[OtSubTableOffsetArray("AttachPointTable", "AttachPointOffset", typeof(AttachPointTable), OutParameterName = "attachPoint")]
public readonly partial struct GdefAttachListTable
{
    public bool TryGetAttachPointTableForGlyph(ushort glyphId, out bool covered, out AttachPointTable attachPoint)
    {
        covered = false;
        attachPoint = default;

        if (!TryGetCoverage(out var coverageTable))
            return false;

        if (!coverageTable.TryGetCoverage(glyphId, out covered, out ushort index))
            return false;

        if (!covered)
            return true;

        return TryGetAttachPointTable(index, out attachPoint);
    }

    [OtSubTable(2)]
    [OtField("PointCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("PointIndex", 2, CountPropertyName = "PointCount")]
    public readonly partial struct AttachPointTable
    {
    }
}
