using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GPOS MarkArray table used by mark attachment subtables.
/// MarkRecord anchor offsets are relative to the beginning of the MarkArray table.
/// </summary>
[OtSubTable(2, GenerateTryCreate = false)]
[OtField("MarkCount", OtFieldKind.UInt16, 0)]
[OtSequentialRecordArray("MarkRecord", 2, 4, CountPropertyName = "MarkCount")]
public readonly partial struct MarkArrayTable
{
    public static bool TryCreate(TableSlice gpos, int offset, out MarkArrayTable markArray)
    {
        markArray = default;

        if ((uint)offset > (uint)gpos.Length - 2)
            return false;

        ushort count = BigEndian.ReadUInt16(gpos.Span, offset);
        long bytesLong = 2L + (count * 4L);
        if (bytesLong > int.MaxValue)
            return false;

        int bytes = (int)bytesLong;
        if ((uint)offset > (uint)gpos.Length - (uint)bytes)
            return false;

        markArray = new MarkArrayTable(gpos, offset);
        return true;
    }

    public readonly struct MarkRecord
    {
        public ushort Class { get; }
        public ushort MarkAnchorOffset { get; }

        public MarkRecord(ushort @class, ushort markAnchorOffset)
        {
            Class = @class;
            MarkAnchorOffset = markAnchorOffset;
        }
    }

    public bool TryGetMarkAnchorTable(MarkRecord record, out bool present, out AnchorTable anchor)
    {
        anchor = default;

        ushort rel = record.MarkAnchorOffset;
        if (rel == 0)
        {
            present = false;
            return true;
        }

        int abs = checked(_offset + rel);
        present = AnchorTable.TryCreate(_table, abs, out anchor);
        return present;
    }
}
