using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(4)]
[OtField("CoverageFormat", OtFieldKind.UInt16, 0)]
[OtDiscriminant(nameof(CoverageFormat))]
[OtCase(1, typeof(CoverageTable.Format1), Name = "Format1")]
[OtCase(2, typeof(CoverageTable.Format2), Name = "Format2")]
public readonly partial struct CoverageTable
{
    public bool TryGetCoverage(ushort glyphId, out bool covered, out ushort coverageIndex)
    {
        covered = false;
        coverageIndex = 0;

        ushort format = CoverageFormat;
        if (format == 1)
        {
            if ((uint)_offset > (uint)_table.Length - 4)
                return false;

            var data = _table.Span;
            ushort glyphCount = BigEndian.ReadUInt16(data, _offset + 2);

            int required = 4 + (glyphCount * 2);
            if ((uint)_offset > (uint)_table.Length - (uint)required)
                return false;

            int arrayOffset = _offset + 4;
            int lo = 0;
            int hi = glyphCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ushort midGlyph = BigEndian.ReadUInt16(data, arrayOffset + (mid * 2));

                if (glyphId < midGlyph)
                {
                    hi = mid - 1;
                    continue;
                }

                if (glyphId > midGlyph)
                {
                    lo = mid + 1;
                    continue;
                }

                covered = true;
                coverageIndex = (ushort)mid;
                return true;
            }

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

            int arrayOffset = _offset + 4;
            int lo = 0;
            int hi = rangeCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int o = arrayOffset + (mid * 6);

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

                ushort startCoverageIndex = BigEndian.ReadUInt16(data, o + 4);
                uint index = (uint)startCoverageIndex + (uint)(glyphId - start);
                if (index > ushort.MaxValue)
                    return false;
                covered = true;
                coverageIndex = (ushort)index;
                return true;
            }

            return true;
        }

        return false;
    }

    public bool TryGetFormat1GlyphCount(out ushort glyphCount)
    {
        glyphCount = 0;

        if (!TryGetFormat1(out var format1))
            return false;

        glyphCount = format1.GlyphCount;
        return true;
    }

    public bool TryGetFormat1GlyphId(int index, out ushort glyphId)
    {
        glyphId = 0;

        if (!TryGetFormat1(out var format1))
            return false;

        return format1.TryGetGlyphId(index, out glyphId);
    }

    public readonly struct RangeRecord
    {
        public ushort StartGlyphId { get; }
        public ushort EndGlyphId { get; }
        public ushort StartCoverageIndex { get; }

        public RangeRecord(ushort startGlyphId, ushort endGlyphId, ushort startCoverageIndex)
        {
            StartGlyphId = startGlyphId;
            EndGlyphId = endGlyphId;
            StartCoverageIndex = startCoverageIndex;
        }
    }

    public bool TryGetFormat2RangeCount(out ushort rangeCount)
    {
        rangeCount = 0;

        if (!TryGetFormat2(out var format2))
            return false;

        rangeCount = format2.RangeCount;
        return true;
    }

    public bool TryGetFormat2RangeRecord(int index, out RangeRecord range)
    {
        range = default;

        if (!TryGetFormat2(out var format2))
            return false;

        return format2.TryGetRangeRecord(index, out range);
    }

    [OtSubTable(4)]
    [OtField("CoverageFormat", OtFieldKind.UInt16, 0)]
    [OtField("GlyphCount", OtFieldKind.UInt16, 2)]
    [OtUInt16Array("GlyphId", 4, CountPropertyName = nameof(GlyphCount))]
    public readonly partial struct Format1
    {
    }

    [OtSubTable(4)]
    [OtField("CoverageFormat", OtFieldKind.UInt16, 0)]
    [OtField("RangeCount", OtFieldKind.UInt16, 2)]
    [OtSequentialRecordArray("RangeRecord", 4, 6, CountPropertyName = nameof(RangeCount), RecordTypeName = nameof(RangeRecord))]
    public readonly partial struct Format2
    {
    }
}
