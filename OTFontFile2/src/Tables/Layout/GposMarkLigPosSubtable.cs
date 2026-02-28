using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GPOS lookup type 5: Mark-to-Ligature Attachment Positioning (format 1).
/// </summary>
[OtSubTable(12)]
[OtField("PosFormat", OtFieldKind.UInt16, 0)]
[OtField("MarkCoverageOffset", OtFieldKind.UInt16, 2)]
[OtField("LigatureCoverageOffset", OtFieldKind.UInt16, 4)]
[OtField("ClassCount", OtFieldKind.UInt16, 6)]
[OtField("MarkArrayOffset", OtFieldKind.UInt16, 8)]
[OtField("LigatureArrayOffset", OtFieldKind.UInt16, 10)]
[OtSubTableOffset("MarkCoverage", nameof(MarkCoverageOffset), typeof(CoverageTable), OutParameterName = "coverage")]
[OtSubTableOffset("LigatureCoverage", nameof(LigatureCoverageOffset), typeof(CoverageTable), OutParameterName = "coverage")]
[OtSubTableOffset("MarkArray", nameof(MarkArrayOffset), typeof(MarkArrayTable))]
public readonly partial struct GposMarkLigPosSubtable
{
    [OtSubTable(2, GenerateTryCreate = false, GenerateStorage = false)]
    [OtField("LigatureCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("LigatureAttachOffset", 2, CountPropertyName = "LigatureCount")]
    public readonly partial struct LigatureArrayTable
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly ushort _classCount;

        internal LigatureArrayTable(TableSlice gpos, int offset, ushort classCount)
        {
            _table = gpos;
            _offset = offset;
            _classCount = classCount;
        }

        public bool TryGetLigatureAttach(int index, out AnchorMatrix attach)
        {
            attach = default;

            if (!TryGetLigatureAttachOffset(index, out ushort rel) || rel == 0)
                return false;

            int abs = checked(_offset + rel);
            return AnchorMatrix.TryCreate(_table, abs, _classCount, out attach);
        }
    }

    public bool TryGetLigatureArray(out LigatureArrayTable ligatureArray)
    {
        ligatureArray = default;

        int rel = LigatureArrayOffset;
        if (rel == 0)
            return false;

        ushort classCount = ClassCount;
        if (classCount == 0)
            return false;

        int abs = checked(_offset + rel);
        if ((uint)abs > (uint)_table.Length - 2)
            return false;

        ligatureArray = new LigatureArrayTable(_table, abs, classCount);
        return true;
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

    public bool TryGetLigatureIndexForGlyph(ushort glyphId, out bool covered, out ushort ligatureIndex)
    {
        covered = false;
        ligatureIndex = 0;

        if (!TryGetLigatureCoverage(out var coverage))
            return false;

        return coverage.TryGetCoverage(glyphId, out covered, out ligatureIndex);
    }

    public bool TryGetAnchorsForGlyphs(
        ushort markGlyphId,
        ushort ligatureGlyphId,
        ushort componentIndex,
        out bool positioned,
        out AnchorTable markAnchor,
        out AnchorTable ligatureAnchor)
    {
        positioned = false;
        markAnchor = default;
        ligatureAnchor = default;

        if (!TryGetMarkRecordForGlyph(markGlyphId, out bool markCovered, out _, out var markRecord))
            return false;

        if (!markCovered)
            return true;

        if (!TryGetLigatureIndexForGlyph(ligatureGlyphId, out bool ligCovered, out ushort ligIndex))
            return false;

        if (!ligCovered)
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

        if (!TryGetLigatureArray(out var ligArray))
            return false;

        if (!ligArray.TryGetLigatureAttach(ligIndex, out var attach))
            return false;

        if ((uint)componentIndex >= attach.RowCount)
            return false;

        if (!attach.TryGetAnchorTable(componentIndex, markRecord.Class, out bool hasLigAnchor, out ligatureAnchor))
            return false;

        if (!hasLigAnchor)
            return true;

        positioned = true;
        return true;
    }
}
