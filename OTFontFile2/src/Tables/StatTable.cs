using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("STAT", 20, GenerateTryCreate = false)]
[OtField("MajorVersion", OtFieldKind.UInt16, 0)]
[OtField("MinorVersion", OtFieldKind.UInt16, 2)]
[OtField("DesignAxisCount", OtFieldKind.UInt16, 6)]
[OtField("AxisValueCount", OtFieldKind.UInt16, 12)]
[OtField("ElidedFallbackNameId", OtFieldKind.UInt16, 18)]
public readonly partial struct StatTable
{
    public static bool TryCreate(TableSlice table, out StatTable stat)
    {
        stat = default;

        // Header is 20 bytes for STAT v1.x.
        if (table.Length < 20)
            return false;

        var data = table.Span;
        ushort major = BigEndian.ReadUInt16(data, 0);
        ushort minor = BigEndian.ReadUInt16(data, 2);

        // Unsupported major versions are still materialized so callers can read raw bytes + version.
        if (major != 1)
        {
            stat = new StatTable(table);
            return true;
        }

        ushort designAxisSize = BigEndian.ReadUInt16(data, 4);
        ushort designAxisCount = BigEndian.ReadUInt16(data, 6);
        uint designAxesOffsetU = BigEndian.ReadUInt32(data, 8);
        ushort axisValueCount = BigEndian.ReadUInt16(data, 12);
        uint axisValueOffsetU = BigEndian.ReadUInt32(data, 14);
        ushort fallback = BigEndian.ReadUInt16(data, 18);

        if (designAxisSize < 8)
            return false;

        if (designAxesOffsetU > int.MaxValue || axisValueOffsetU > int.MaxValue)
            return false;

        int designAxesOffset = (int)designAxesOffsetU;
        int axisValueOffset = (int)axisValueOffsetU;

        long designAxesBytesLong = (long)designAxisCount * designAxisSize;
        if (designAxesBytesLong > int.MaxValue)
            return false;

        int designAxesBytes = (int)designAxesBytesLong;
        if ((uint)designAxesOffset > (uint)table.Length - (uint)designAxesBytes)
            return false;

        long axisValueOffsetsBytesLong = (long)axisValueCount * 2;
        if (axisValueOffsetsBytesLong > int.MaxValue)
            return false;

        int axisValueOffsetsBytes = (int)axisValueOffsetsBytesLong;
        if ((uint)axisValueOffset > (uint)table.Length - (uint)axisValueOffsetsBytes)
            return false;

        stat = new StatTable(table);
        return true;
    }

    public bool IsSupported => MajorVersion == 1;

    public readonly struct DesignAxisRecord
    {
        public Tag AxisTag { get; }
        public ushort AxisNameId { get; }
        public ushort AxisOrdering { get; }

        public DesignAxisRecord(Tag axisTag, ushort axisNameId, ushort axisOrdering)
        {
            AxisTag = axisTag;
            AxisNameId = axisNameId;
            AxisOrdering = axisOrdering;
        }
    }

    public bool TryGetDesignAxisRecord(int designAxisIndex, out DesignAxisRecord record)
    {
        record = default;

        if (!IsSupported)
            return false;

        ushort designAxisCount = DesignAxisCount;
        if ((uint)designAxisIndex >= designAxisCount)
            return false;

        ushort designAxisSize = BigEndian.ReadUInt16(_table.Span, 4);
        int designAxesOffset = (int)BigEndian.ReadUInt32(_table.Span, 8);
        int offset = checked(designAxesOffset + (designAxisIndex * designAxisSize));
        if ((uint)offset > (uint)_table.Length - 8)
            return false;

        var data = _table.Span;
        record = new DesignAxisRecord(
            axisTag: new Tag(BigEndian.ReadUInt32(data, offset + 0)),
            axisNameId: BigEndian.ReadUInt16(data, offset + 4),
            axisOrdering: BigEndian.ReadUInt16(data, offset + 6));
        return true;
    }

    public bool TryGetAxisValueTable(int axisValueIndex, out AxisValueTable axisValueTable)
    {
        axisValueTable = default;

        if (!IsSupported)
            return false;

        ushort axisValueCount = AxisValueCount;
        if ((uint)axisValueIndex >= axisValueCount)
            return false;

        int axisValueOffset = (int)BigEndian.ReadUInt32(_table.Span, 14);
        int offsetEntry = checked(axisValueOffset + (axisValueIndex * 2));
        if ((uint)offsetEntry > (uint)_table.Length - 2)
            return false;

        ushort rel = BigEndian.ReadUInt16(_table.Span, offsetEntry);
        int abs = checked(axisValueOffset + rel);
        if ((uint)abs > (uint)_table.Length - 2)
            return false;

        return AxisValueTable.TryCreate(_table, abs, out axisValueTable);
    }

    [OtSubTable(2)]
    [OtField("Format", OtFieldKind.UInt16, 0)]
    [OtDiscriminant(nameof(Format))]
    [OtCase(1, typeof(AxisValueTable.Format1Table), Name = "Format1Table")]
    [OtCase(2, typeof(AxisValueTable.Format2Table), Name = "Format2Table")]
    [OtCase(3, typeof(AxisValueTable.Format3Table), Name = "Format3Table")]
    [OtCase(4, typeof(AxisValueTable.Format4Table), Name = "Format4Table")]
    public readonly partial struct AxisValueTable
    {
        public bool TryGetFormat1(out AxisValueFormat1 format1)
        {
            format1 = default;

            if (!TryGetFormat1Table(out var t))
                return false;

            format1 = new AxisValueFormat1(
                axisIndex: t.AxisIndex,
                flags: t.Flags,
                valueNameId: t.ValueNameId,
                value: t.Value);
            return true;
        }

        public bool TryGetFormat2(out AxisValueFormat2 format2)
        {
            format2 = default;

            if (!TryGetFormat2Table(out var t))
                return false;

            format2 = new AxisValueFormat2(
                axisIndex: t.AxisIndex,
                flags: t.Flags,
                valueNameId: t.ValueNameId,
                nominalValue: t.NominalValue,
                rangeMinValue: t.RangeMinValue,
                rangeMaxValue: t.RangeMaxValue);
            return true;
        }

        public bool TryGetFormat3(out AxisValueFormat3 format3)
        {
            format3 = default;

            if (!TryGetFormat3Table(out var t))
                return false;

            format3 = new AxisValueFormat3(
                axisIndex: t.AxisIndex,
                flags: t.Flags,
                valueNameId: t.ValueNameId,
                value: t.Value,
                linkedValue: t.LinkedValue);
            return true;
        }

        public bool TryGetFormat4(out AxisValueFormat4 format4)
        {
            format4 = default;

            if (!TryGetFormat4Table(out var t))
                return false;

            ushort axisCount = t.AxisCount;
            long recordsBytesLong = (long)axisCount * 6;
            if (recordsBytesLong > int.MaxValue)
                return false;

            int recordsBytes = (int)recordsBytesLong;
            int recordsOffset = t.Offset + 8;
            if ((uint)recordsOffset > (uint)_table.Length - (uint)recordsBytes)
                return false;

            format4 = new AxisValueFormat4(t);
            return true;
        }

        [OtSubTable(12)]
        [OtField("AxisIndex", OtFieldKind.UInt16, 2)]
        [OtField("Flags", OtFieldKind.UInt16, 4)]
        [OtField("ValueNameId", OtFieldKind.UInt16, 6)]
        [OtField("Value", OtFieldKind.Fixed1616, 8)]
        public readonly partial struct Format1Table
        {
        }

        [OtSubTable(20)]
        [OtField("AxisIndex", OtFieldKind.UInt16, 2)]
        [OtField("Flags", OtFieldKind.UInt16, 4)]
        [OtField("ValueNameId", OtFieldKind.UInt16, 6)]
        [OtField("NominalValue", OtFieldKind.Fixed1616, 8)]
        [OtField("RangeMinValue", OtFieldKind.Fixed1616, 12)]
        [OtField("RangeMaxValue", OtFieldKind.Fixed1616, 16)]
        public readonly partial struct Format2Table
        {
        }

        [OtSubTable(16)]
        [OtField("AxisIndex", OtFieldKind.UInt16, 2)]
        [OtField("Flags", OtFieldKind.UInt16, 4)]
        [OtField("ValueNameId", OtFieldKind.UInt16, 6)]
        [OtField("Value", OtFieldKind.Fixed1616, 8)]
        [OtField("LinkedValue", OtFieldKind.Fixed1616, 12)]
        public readonly partial struct Format3Table
        {
        }

        [OtSubTable(8)]
        [OtField("AxisCount", OtFieldKind.UInt16, 2)]
        [OtField("Flags", OtFieldKind.UInt16, 4)]
        [OtField("ValueNameId", OtFieldKind.UInt16, 6)]
        [OtSequentialRecordArray("AxisValueRecord", 8, 6, CountPropertyName = nameof(AxisCount), RecordTypeName = nameof(AxisValueRecord))]
        public readonly partial struct Format4Table
        {
        }
    }

    public readonly struct AxisValueFormat1
    {
        public ushort AxisIndex { get; }
        public ushort Flags { get; }
        public ushort ValueNameId { get; }
        public Fixed1616 Value { get; }

        public AxisValueFormat1(ushort axisIndex, ushort flags, ushort valueNameId, Fixed1616 value)
        {
            AxisIndex = axisIndex;
            Flags = flags;
            ValueNameId = valueNameId;
            Value = value;
        }
    }

    public readonly struct AxisValueFormat2
    {
        public ushort AxisIndex { get; }
        public ushort Flags { get; }
        public ushort ValueNameId { get; }
        public Fixed1616 NominalValue { get; }
        public Fixed1616 RangeMinValue { get; }
        public Fixed1616 RangeMaxValue { get; }

        public AxisValueFormat2(ushort axisIndex, ushort flags, ushort valueNameId, Fixed1616 nominalValue, Fixed1616 rangeMinValue, Fixed1616 rangeMaxValue)
        {
            AxisIndex = axisIndex;
            Flags = flags;
            ValueNameId = valueNameId;
            NominalValue = nominalValue;
            RangeMinValue = rangeMinValue;
            RangeMaxValue = rangeMaxValue;
        }
    }

    public readonly struct AxisValueFormat3
    {
        public ushort AxisIndex { get; }
        public ushort Flags { get; }
        public ushort ValueNameId { get; }
        public Fixed1616 Value { get; }
        public Fixed1616 LinkedValue { get; }

        public AxisValueFormat3(ushort axisIndex, ushort flags, ushort valueNameId, Fixed1616 value, Fixed1616 linkedValue)
        {
            AxisIndex = axisIndex;
            Flags = flags;
            ValueNameId = valueNameId;
            Value = value;
            LinkedValue = linkedValue;
        }
    }

    public readonly struct AxisValueFormat4
    {
        private readonly AxisValueTable.Format4Table _table;

        public ushort Flags { get; }
        public ushort ValueNameId { get; }

        internal AxisValueFormat4(AxisValueTable.Format4Table table)
        {
            _table = table;
            Flags = table.Flags;
            ValueNameId = table.ValueNameId;
        }

        public ushort AxisValueRecordCount => _table.AxisCount;

        public bool TryGetAxisValueRecord(int index, out AxisValueRecord record)
            => _table.TryGetAxisValueRecord(index, out record);
    }

    public readonly struct AxisValueRecord
    {
        public ushort AxisIndex { get; }
        public Fixed1616 Value { get; }

        public AxisValueRecord(ushort axisIndex, Fixed1616 value)
        {
            AxisIndex = axisIndex;
            Value = value;
        }
    }
}
