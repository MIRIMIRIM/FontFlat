using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GDEF MarkGlyphSetsDef table (v1.2+).
/// </summary>
[OtSubTable(4)]
[OtField("MarkGlyphSetsDefFormat", OtFieldKind.UInt16, 0)]
[OtField("MarkGlyphSetCount", OtFieldKind.UInt16, 2)]
[OtUInt32Array("CoverageOffset", 4, CountPropertyName = "MarkGlyphSetCount")]
[OtSubTableOffsetArray("CoverageTable", "CoverageOffset", typeof(CoverageTable), OutParameterName = "coverage")]
public readonly partial struct GdefMarkGlyphSetsDefTable
{
    public bool TryIsGlyphInSet(int markSetIndex, ushort glyphId, out bool covered)
    {
        covered = false;

        if (!TryGetCoverageTable(markSetIndex, out var coverageTable))
            return false;

        return coverageTable.TryGetCoverage(glyphId, out covered, out _);
    }
}
