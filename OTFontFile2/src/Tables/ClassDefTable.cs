using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(4)]
[OtField("ClassFormat", OtFieldKind.UInt16, 0)]
[OtDiscriminant(nameof(ClassFormat))]
[OtCase(1, typeof(ClassDefTable.Format1), Name = "Format1")]
[OtCase(2, typeof(ClassDefTable.Format2), Name = "Format2")]
public readonly partial struct ClassDefTable
{
    public bool TryGetClass(ushort glyphId, out ushort classValue)
    {
        classValue = 0;

        ushort format = ClassFormat;
        if (format == 1)
        {
            if ((uint)_offset > (uint)_table.Length - 6)
                return false;

            var data = _table.Span;
            ushort startGlyphId = BigEndian.ReadUInt16(data, _offset + 2);
            ushort glyphCount = BigEndian.ReadUInt16(data, _offset + 4);

            int required = 6 + (glyphCount * 2);
            if ((uint)_offset > (uint)_table.Length - (uint)required)
                return false;

            if (glyphId < startGlyphId)
                return true;

            uint index = (uint)(glyphId - startGlyphId);
            if (index >= glyphCount)
                return true;

            int o = _offset + 6 + ((int)index * 2);
            classValue = BigEndian.ReadUInt16(data, o);
            return true;
        }

        if (format == 2)
        {
            if ((uint)_offset > (uint)_table.Length - 4)
                return false;

            var data = _table.Span;
            ushort rangeCount = BigEndian.ReadUInt16(data, _offset + 2);

            int required = 4 + (rangeCount * 6);
            if ((uint)_offset > (uint)_table.Length - (uint)required)
                return false;

            int lo = 0;
            int hi = rangeCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int o = _offset + 4 + (mid * 6);

                ushort start = BigEndian.ReadUInt16(data, o);
                ushort end = BigEndian.ReadUInt16(data, o + 2);

                if (glyphId < start)
                {
                    hi = mid - 1;
                    continue;
                }

                if (glyphId > end)
                {
                    lo = mid + 1;
                    continue;
                }

                classValue = BigEndian.ReadUInt16(data, o + 4);
                return true;
            }

            return true;
        }

        return false;
    }

    public readonly struct RangeRecord
    {
        public ushort StartGlyphId { get; }
        public ushort EndGlyphId { get; }
        public ushort Class { get; }

        public RangeRecord(ushort startGlyphId, ushort endGlyphId, ushort @class)
        {
            StartGlyphId = startGlyphId;
            EndGlyphId = endGlyphId;
            Class = @class;
        }
    }

    public bool TryGetRangeCount(out ushort rangeCount)
    {
        rangeCount = 0;

        if (!TryGetFormat2(out var format2))
            return false;

        rangeCount = format2.RangeCount;
        return true;
    }

    public bool TryGetRangeRecord(int index, out RangeRecord record)
    {
        record = default;

        if (!TryGetFormat2(out var format2))
            return false;

        return format2.TryGetRangeRecord(index, out record);
    }

    [OtSubTable(6)]
    [OtField("ClassFormat", OtFieldKind.UInt16, 0)]
    [OtField("StartGlyphId", OtFieldKind.UInt16, 2)]
    [OtField("GlyphCount", OtFieldKind.UInt16, 4)]
    [OtUInt16Array("ClassValue", 6, CountPropertyName = nameof(GlyphCount))]
    public readonly partial struct Format1
    {
    }

    [OtSubTable(4)]
    [OtField("ClassFormat", OtFieldKind.UInt16, 0)]
    [OtField("RangeCount", OtFieldKind.UInt16, 2)]
    [OtSequentialRecordArray("RangeRecord", 4, 6, CountPropertyName = nameof(RangeCount), RecordTypeName = nameof(RangeRecord))]
    public readonly partial struct Format2
    {
    }
}
