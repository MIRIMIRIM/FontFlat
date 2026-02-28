using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GPOS lookup type 3: Cursive Attachment Positioning (format 1).
/// </summary>
[OtSubTable(6)]
[OtField("PosFormat", OtFieldKind.UInt16, 0)]
[OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
[OtField("EntryExitCount", OtFieldKind.UInt16, 4)]
[OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
[OtSequentialRecordArray("EntryExitRecord", 6, 4, CountPropertyName = nameof(EntryExitCount), OutParameterName = "record")]
public readonly partial struct GposCursivePosSubtable
{
    public readonly struct EntryExitRecord
    {
        private readonly TableSlice _table;
        private readonly int _subtableOffset;

        public ushort EntryAnchorOffset { get; }
        public ushort ExitAnchorOffset { get; }

        internal EntryExitRecord(
            [OtRecordContext("_table")] TableSlice table,
            [OtRecordContext("_offset")] int subtableOffset,
            ushort entryAnchorOffset,
            ushort exitAnchorOffset)
        {
            _table = table;
            _subtableOffset = subtableOffset;
            EntryAnchorOffset = entryAnchorOffset;
            ExitAnchorOffset = exitAnchorOffset;
        }

        public bool TryGetEntryAnchorTable(out bool present, out AnchorTable anchor)
        {
            anchor = default;

            ushort rel = EntryAnchorOffset;
            if (rel == 0)
            {
                present = false;
                return true;
            }

            int abs = checked(_subtableOffset + rel);
            present = AnchorTable.TryCreate(_table, abs, out anchor);
            return present;
        }

        public bool TryGetExitAnchorTable(out bool present, out AnchorTable anchor)
        {
            anchor = default;

            ushort rel = ExitAnchorOffset;
            if (rel == 0)
            {
                present = false;
                return true;
            }

            int abs = checked(_subtableOffset + rel);
            present = AnchorTable.TryCreate(_table, abs, out anchor);
            return present;
        }
    }

    public bool TryGetEntryExitRecordForGlyph(ushort glyphId, out bool covered, out ushort coverageIndex, out EntryExitRecord record)
    {
        record = default;
        covered = false;
        coverageIndex = 0;

        if (!TryGetCoverage(out var coverage))
            return false;

        if (!coverage.TryGetCoverage(glyphId, out covered, out coverageIndex))
            return false;

        if (!covered)
            return true;

        return TryGetEntryExitRecord(coverageIndex, out record);
    }
}
