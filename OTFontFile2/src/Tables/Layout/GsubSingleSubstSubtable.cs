using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(6)]
[OtField("SubstFormat", OtFieldKind.UInt16, 0)]
[OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
[OtDiscriminant(nameof(SubstFormat))]
[OtCase(1, typeof(GsubSingleSubstSubtable.Format1), Name = "Format1")]
[OtCase(2, typeof(GsubSingleSubstSubtable.Format2), Name = "Format2")]
[OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
public readonly partial struct GsubSingleSubstSubtable
{
    public bool TrySubstituteGlyph(ushort glyphId, out bool substituted, out ushort substituteGlyphId)
    {
        substituted = false;
        substituteGlyphId = glyphId;

        if (!TryGetCoverage(out var coverage))
            return false;

        if (!coverage.TryGetCoverage(glyphId, out bool covered, out ushort coverageIndex))
            return false;

        if (!covered)
            return true;

        if (TryGetFormat1(out var format1))
        {
            substituteGlyphId = unchecked((ushort)(glyphId + format1.DeltaGlyphId));
            substituted = true;
            return true;
        }

        if (TryGetFormat2(out var format2))
        {
            if (!format2.TryGetSubstituteGlyphId(coverageIndex, out substituteGlyphId))
                return false;
            substituted = true;
            return true;
        }

        return false;
    }

    [OtSubTable(6)]
    [OtField("SubstFormat", OtFieldKind.UInt16, 0)]
    [OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
    [OtField("DeltaGlyphId", OtFieldKind.Int16, 4)]
    [OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
    public readonly partial struct Format1
    {
    }

    [OtSubTable(6)]
    [OtField("SubstFormat", OtFieldKind.UInt16, 0)]
    [OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
    [OtField("GlyphCount", OtFieldKind.UInt16, 4)]
    [OtUInt16Array("SubstituteGlyphId", 6, CountPropertyName = nameof(GlyphCount))]
    [OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
    public readonly partial struct Format2
    {
    }
}
