using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GPOS lookup type 4: Mark-to-Base Attachment Positioning (format 1).
/// </summary>
[OtSubTable(12)]
[OtField("PosFormat", OtFieldKind.UInt16, 0)]
[OtField("MarkCoverageOffset", OtFieldKind.UInt16, 2)]
[OtField("BaseCoverageOffset", OtFieldKind.UInt16, 4)]
[OtField("ClassCount", OtFieldKind.UInt16, 6)]
[OtField("MarkArrayOffset", OtFieldKind.UInt16, 8)]
[OtField("BaseArrayOffset", OtFieldKind.UInt16, 10)]
[OtSubTableOffset("MarkCoverage", nameof(MarkCoverageOffset), typeof(CoverageTable), OutParameterName = "coverage")]
[OtSubTableOffset("BaseCoverage", nameof(BaseCoverageOffset), typeof(CoverageTable), OutParameterName = "coverage")]
[OtSubTableOffset("MarkArray", nameof(MarkArrayOffset), typeof(MarkArrayTable))]
public readonly partial struct GposMarkBasePosSubtable
{
    public bool TryGetBaseArray(out AnchorMatrix baseArray)
    {
        baseArray = default;

        int rel = BaseArrayOffset;
        if (rel == 0)
            return false;

        ushort classCount = ClassCount;
        if (classCount == 0)
            return false;

        int abs = checked(_offset + rel);
        return AnchorMatrix.TryCreate(_table, abs, classCount, out baseArray);
    }

    public bool TryGetMarkRecordForGlyph(ushort glyphId, out bool covered, out ushort markIndex, out MarkArrayTable.MarkRecord record)
    {
        covered = false;
        markIndex = 0;
        record = default;

        if (!TryGetMarkCoverage(out var coverage))
            return false;

        if (!coverage.TryGetCoverage(glyphId, out covered, out markIndex))
            return false;

        if (!covered)
            return true;

        if (!TryGetMarkArray(out var markArray))
            return false;

        return markArray.TryGetMarkRecord(markIndex, out record);
    }

    public bool TryGetBaseIndexForGlyph(ushort glyphId, out bool covered, out ushort baseIndex)
    {
        covered = false;
        baseIndex = 0;

        if (!TryGetBaseCoverage(out var coverage))
            return false;

        return coverage.TryGetCoverage(glyphId, out covered, out baseIndex);
    }

    public bool TryGetAnchorsForGlyphs(ushort markGlyphId, ushort baseGlyphId, out bool positioned, out AnchorTable markAnchor, out AnchorTable baseAnchor)
    {
        positioned = false;
        markAnchor = default;
        baseAnchor = default;

        if (!TryGetMarkRecordForGlyph(markGlyphId, out bool markCovered, out _, out var markRecord))
            return false;

        if (!markCovered)
            return true;

        if (!TryGetBaseIndexForGlyph(baseGlyphId, out bool baseCovered, out ushort baseIndex))
            return false;

        if (!baseCovered)
            return true;

        ushort classCount = ClassCount;
        if (markRecord.Class >= classCount)
            return false;

        if (!TryGetMarkArray(out var markArray))
            return false;
        if (!markArray.TryGetMarkAnchorTable(markRecord, out bool hasMarkAnchor, out markAnchor))
            return false;

        if (!hasMarkAnchor)
            return true;

        if (!TryGetBaseArray(out var baseArray))
            return false;
        if (!baseArray.TryGetAnchorTable(baseIndex, markRecord.Class, out bool hasBaseAnchor, out baseAnchor))
            return false;

        if (!hasBaseAnchor)
            return true;

        positioned = true;
        return true;
    }
}
