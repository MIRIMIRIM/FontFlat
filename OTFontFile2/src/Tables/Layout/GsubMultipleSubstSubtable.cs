using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(6)]
[OtField("SubstFormat", OtFieldKind.UInt16, 0)]
[OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
[OtField("SequenceCount", OtFieldKind.UInt16, 4)]
[OtUInt16Array("SequenceOffset", 6, CountPropertyName = "SequenceCount")]
[OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
[OtSubTableOffsetArray("Sequence", "SequenceOffset", typeof(SequenceTable))]
public readonly partial struct GsubMultipleSubstSubtable
{
    public bool TryGetSequenceForGlyph(ushort glyphId, out bool substituted, out SequenceTable sequence)
    {
        substituted = false;
        sequence = default;

        if (!TryGetCoverage(out var coverage))
            return false;

        if (!coverage.TryGetCoverage(glyphId, out bool covered, out ushort coverageIndex))
            return false;

        if (!covered)
            return true;

        if (coverageIndex >= SequenceCount)
            return false;

        if (!TryGetSequence(coverageIndex, out sequence))
            return false;

        substituted = true;
        return true;
    }

    [OtSubTable(2)]
    [OtField("GlyphCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("SubstituteGlyphId", 2, CountPropertyName = "GlyphCount")]
    public readonly partial struct SequenceTable
    {
    }
}
