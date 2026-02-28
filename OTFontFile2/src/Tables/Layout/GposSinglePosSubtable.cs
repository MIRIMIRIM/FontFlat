using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(6)]
[OtField("PosFormat", OtFieldKind.UInt16, 0)]
[OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
[OtField("ValueFormat", OtFieldKind.UInt16, 4)]
[OtDiscriminant(nameof(PosFormat))]
[OtCase(1, typeof(GposSinglePosSubtable.Format1), Name = "Format1")]
[OtCase(2, typeof(GposSinglePosSubtable.Format2), Name = "Format2")]
[OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
public readonly partial struct GposSinglePosSubtable
{
    public bool TryGetValueRecordForGlyph(ushort glyphId, out bool positioned, out GposValueRecord value)
    {
        positioned = false;
        value = default;

        if (!TryGetCoverage(out var coverage))
            return false;

        if (!coverage.TryGetCoverage(glyphId, out bool covered, out ushort coverageIndex))
            return false;

        if (!covered)
            return true;

        if (TryGetFormat1(out var format1))
        {
            if (!format1.TryGetValue(out value))
                return false;

            positioned = true;
            return true;
        }

        if (TryGetFormat2(out var format2))
        {
            if (!format2.TryGetValue(coverageIndex, out value))
                return false;

            positioned = true;
            return true;
        }

        return false;
    }

    public bool TryGetFormat1Value(out GposValueRecord value)
    {
        value = default;
        return TryGetFormat1(out var format1) && format1.TryGetValue(out value);
    }

    public bool TryGetFormat2Value(int index, out GposValueRecord value)
    {
        value = default;
        return TryGetFormat2(out var format2) && format2.TryGetValue(index, out value);
    }

    [OtSubTable(6)]
    [OtField("PosFormat", OtFieldKind.UInt16, 0)]
    [OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
    [OtField("ValueFormat", OtFieldKind.UInt16, 4)]
    [OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
    public readonly partial struct Format1
    {
        public bool TryGetValue(out GposValueRecord value)
        {
            value = default;
            int offset = _offset + 6;
            return GposValueRecord.TryCreate(_table, offset, _offset, ValueFormat, out value);
        }
    }

    [OtSubTable(8)]
    [OtField("PosFormat", OtFieldKind.UInt16, 0)]
    [OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
    [OtField("ValueFormat", OtFieldKind.UInt16, 4)]
    [OtField("ValueCount", OtFieldKind.UInt16, 6)]
    [OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
    public readonly partial struct Format2
    {
        public bool TryGetValue(int index, out GposValueRecord value)
        {
            value = default;

            ushort valueCount = ValueCount;
            if ((uint)index >= valueCount)
                return false;

            int recordSize = GposValueRecord.GetByteLength(ValueFormat);
            long o = (long)_offset + 8 + ((long)index * recordSize);
            if (o < 0 || o > _table.Length - recordSize)
                return false;

            return GposValueRecord.TryCreate(_table, (int)o, _offset, ValueFormat, out value);
        }
    }
}
