using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GPOS lookup type 6: Mark-to-Mark Attachment Positioning (format 1).
/// </summary>
[OtSubTable(12)]
[OtField("PosFormat", OtFieldKind.UInt16, 0)]
[OtField("Mark1CoverageOffset", OtFieldKind.UInt16, 2)]
[OtField("Mark2CoverageOffset", OtFieldKind.UInt16, 4)]
[OtField("ClassCount", OtFieldKind.UInt16, 6)]
[OtField("Mark1ArrayOffset", OtFieldKind.UInt16, 8)]
[OtField("Mark2ArrayOffset", OtFieldKind.UInt16, 10)]
[OtSubTableOffset("Mark1Coverage", nameof(Mark1CoverageOffset), typeof(CoverageTable), OutParameterName = "coverage")]
[OtSubTableOffset("Mark2Coverage", nameof(Mark2CoverageOffset), typeof(CoverageTable), OutParameterName = "coverage")]
[OtSubTableOffset("Mark1Array", nameof(Mark1ArrayOffset), typeof(MarkArrayTable), OutParameterName = "markArray")]
public readonly partial struct GposMarkMarkPosSubtable
{
    public bool TryGetMark2Array(out AnchorMatrix mark2Array)
    {
        mark2Array = default;

        int rel = Mark2ArrayOffset;
        if (rel == 0)
            return false;

        ushort classCount = ClassCount;
        if (classCount == 0)
            return false;

        int abs = checked(_offset + rel);
        return AnchorMatrix.TryCreate(_table, abs, classCount, out mark2Array);
    }

    public bool TryGetMark1RecordForGlyph(ushort glyphId, out bool covered, out ushort mark1Index, out MarkArrayTable.MarkRecord record)
    {
        covered = false;
        mark1Index = 0;
        record = default;

        if (!TryGetMark1Coverage(out var coverage))
            return false;

        if (!coverage.TryGetCoverage(glyphId, out covered, out mark1Index))
            return false;

        if (!covered)
            return true;

        if (!TryGetMark1Array(out var markArray))
            return false;

        return markArray.TryGetMarkRecord(mark1Index, out record);
    }

    public bool TryGetMark2IndexForGlyph(ushort glyphId, out bool covered, out ushort mark2Index)
    {
        covered = false;
        mark2Index = 0;

        if (!TryGetMark2Coverage(out var coverage))
            return false;

        return coverage.TryGetCoverage(glyphId, out covered, out mark2Index);
    }

    public bool TryGetAnchorsForGlyphs(ushort mark1GlyphId, ushort mark2GlyphId, out bool positioned, out AnchorTable mark1Anchor, out AnchorTable mark2Anchor)
    {
        positioned = false;
        mark1Anchor = default;
        mark2Anchor = default;

        if (!TryGetMark1RecordForGlyph(mark1GlyphId, out bool mark1Covered, out _, out var mark1Record))
            return false;

        if (!mark1Covered)
            return true;

        if (!TryGetMark2IndexForGlyph(mark2GlyphId, out bool mark2Covered, out ushort mark2Index))
            return false;

        if (!mark2Covered)
            return true;

        ushort classCount = ClassCount;
        if (mark1Record.Class >= classCount)
            return false;

        if (!TryGetMark1Array(out var mark1Array))
            return false;

        if (!mark1Array.TryGetMarkAnchorTable(mark1Record, out bool hasMark1Anchor, out mark1Anchor))
            return false;

        if (!hasMark1Anchor)
            return true;

        if (!TryGetMark2Array(out var mark2Array))
            return false;

        if (!mark2Array.TryGetAnchorTable(mark2Index, mark1Record.Class, out bool hasMark2Anchor, out mark2Anchor))
            return false;

        if (!hasMark2Anchor)
            return true;

        positioned = true;
        return true;
    }
}
