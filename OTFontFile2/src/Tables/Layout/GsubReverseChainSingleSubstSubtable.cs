using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GSUB lookup type 8: Reverse Chaining Contextual Single Substitution (format 1).
/// </summary>
[OtSubTable(10)]
[OtField("SubstFormat", OtFieldKind.UInt16, 0)]
[OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
[OtField("BacktrackGlyphCount", OtFieldKind.UInt16, 4)]
[OtUInt16Array("BacktrackCoverageOffset", 6, CountPropertyName = "BacktrackGlyphCount")]
[OtUInt16Array(
    "LookaheadCoverageOffset",
    0,
    CountPropertyName = nameof(LookaheadGlyphCount),
    OutParameterName = "coverageOffset",
    ValuesOffsetExpression = "8 + (BacktrackGlyphCount * 2)")]
[OtUInt16Array(
    "SubstituteGlyphId",
    0,
    CountPropertyName = nameof(SubstituteGlyphCount),
    OutParameterName = "substituteGlyphId",
    ValuesOffsetExpression = "10 + (BacktrackGlyphCount * 2) + (LookaheadGlyphCount * 2)")]
[OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
[OtSubTableOffsetArray("BacktrackCoverage", "BacktrackCoverageOffset", typeof(CoverageTable), OutParameterName = "coverage")]
[OtSubTableOffsetArray("LookaheadCoverage", "LookaheadCoverageOffset", typeof(CoverageTable), OutParameterName = "coverage")]
public readonly partial struct GsubReverseChainSingleSubstSubtable
{
    private ushort LookaheadGlyphCount => TryGetLookaheadGlyphCount(out ushort count) ? count : (ushort)0;
    private ushort SubstituteGlyphCount => TryGetSubstituteGlyphCount(out ushort count) ? count : (ushort)0;

    public bool TryGetLookaheadGlyphCount(out ushort count)
    {
        count = 0;

        ushort backCount = BacktrackGlyphCount;
        int o = checked(_offset + 6 + (backCount * 2));
        if ((uint)o > (uint)_table.Length - 2)
            return false;

        count = BigEndian.ReadUInt16(_table.Span, o);
        return true;
    }

    public bool TryGetSubstituteGlyphCount(out ushort count)
    {
        count = 0;

        if (!TryGetLookaheadGlyphCount(out ushort lookaheadCount))
            return false;

        ushort backCount = BacktrackGlyphCount;
        int o = checked(_offset + 6 + (backCount * 2) + 2 + (lookaheadCount * 2));
        if ((uint)o > (uint)_table.Length - 2)
            return false;

        count = BigEndian.ReadUInt16(_table.Span, o);
        return true;
    }

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

        if (!TryGetSubstituteGlyphId(coverageIndex, out substituteGlyphId))
            return false;

        substituted = true;
        return true;
    }
}
