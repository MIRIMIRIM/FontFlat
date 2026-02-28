using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(6)]
[OtField("SubstFormat", OtFieldKind.UInt16, 0)]
[OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
[OtField("LigatureSetCount", OtFieldKind.UInt16, 4)]
[OtUInt16Array("LigatureSetOffset", 6, CountPropertyName = "LigatureSetCount")]
[OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
[OtSubTableOffsetArray("LigatureSet", "LigatureSetOffset", typeof(LigatureSet))]
public readonly partial struct GsubLigatureSubstSubtable
{
    public bool TryGetLigatureSetForGlyph(ushort glyphId, out bool substituted, out LigatureSet ligatureSet)
    {
        substituted = false;
        ligatureSet = default;

        if (!TryGetCoverage(out var coverage))
            return false;

        if (!coverage.TryGetCoverage(glyphId, out bool covered, out ushort coverageIndex))
            return false;

        if (!covered)
            return true;

        if (coverageIndex >= LigatureSetCount)
            return false;

        if (!TryGetLigatureSet(coverageIndex, out ligatureSet))
            return false;

        substituted = true;
        return true;
    }

    [OtSubTable(2)]
    [OtField("LigatureCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("LigatureOffset", 2, CountPropertyName = "LigatureCount")]
    [OtSubTableOffsetArray("Ligature", "LigatureOffset", typeof(Ligature))]
    public readonly partial struct LigatureSet
    {
    }

    [OtSubTable(4)]
    [OtField("LigGlyph", OtFieldKind.UInt16, 0)]
    [OtField("ComponentCount", OtFieldKind.UInt16, 2)]
    [OtUInt16Array("ComponentGlyphId", 4, CountPropertyName = "ComponentCount", CountAdjustment = -1)]
    public readonly partial struct Ligature
    {
    }
}
