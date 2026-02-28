using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(6)]
[OtField("SubstFormat", OtFieldKind.UInt16, 0)]
[OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
[OtField("AlternateSetCount", OtFieldKind.UInt16, 4)]
[OtUInt16Array("AlternateSetOffset", 6, CountPropertyName = "AlternateSetCount")]
[OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
[OtSubTableOffsetArray("AlternateSet", "AlternateSetOffset", typeof(AlternateSet))]
public readonly partial struct GsubAlternateSubstSubtable
{
    public bool TryGetAlternateSetForGlyph(ushort glyphId, out bool substituted, out AlternateSet alternateSet)
    {
        substituted = false;
        alternateSet = default;

        if (!TryGetCoverage(out var coverage))
            return false;

        if (!coverage.TryGetCoverage(glyphId, out bool covered, out ushort coverageIndex))
            return false;

        if (!covered)
            return true;

        if (coverageIndex >= AlternateSetCount)
            return false;

        if (!TryGetAlternateSet(coverageIndex, out alternateSet))
            return false;

        substituted = true;
        return true;
    }

    [OtSubTable(2)]
    [OtField("GlyphCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("AlternateGlyphId", 2, CountPropertyName = "GlyphCount")]
    public readonly partial struct AlternateSet
    {
    }
}
