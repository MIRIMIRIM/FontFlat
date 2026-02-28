using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("fvar", 16, GenerateTryCreate = false)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("AxisCount", OtFieldKind.UInt16, 8)]
[OtField("AxisSize", OtFieldKind.UInt16, 10)]
[OtField("InstanceCount", OtFieldKind.UInt16, 12)]
[OtField("InstanceSize", OtFieldKind.UInt16, 14)]
public readonly partial struct FvarTable
{
    public static bool TryCreate(TableSlice table, out FvarTable fvar)
    {
        fvar = default;

        // version(4) + axesArrayOffset(2) + reserved(2) + axisCount(2) + axisSize(2) + instanceCount(2) + instanceSize(2)
        if (table.Length < 16)
            return false;

        var data = table.Span;
        uint version = BigEndian.ReadUInt32(data, 0);
        if ((version >> 16) != 1)
            return false;

        ushort axesArrayOffset = BigEndian.ReadUInt16(data, 4);
        ushort axisCount = BigEndian.ReadUInt16(data, 8);
        ushort axisSize = BigEndian.ReadUInt16(data, 10);
        ushort instanceCount = BigEndian.ReadUInt16(data, 12);
        ushort instanceSize = BigEndian.ReadUInt16(data, 14);

        if (axisSize < 20)
            return false;

        if (axesArrayOffset > table.Length)
            return false;

        long axesEndLong = axesArrayOffset + ((long)axisCount * axisSize);
        if (axesEndLong > table.Length)
            return false;

        long minInstanceSizeLong = 4 + ((long)axisCount * 4);
        if (instanceSize < minInstanceSizeLong)
            return false;

        long instancesOffsetLong = axesEndLong;
        long instancesEndLong = instancesOffsetLong + ((long)instanceCount * instanceSize);
        if (instancesEndLong > table.Length)
            return false;

        fvar = new FvarTable(table);
        return true;
    }

    public readonly struct AxisRecord
    {
        public Tag AxisTag { get; }
        public Fixed1616 MinValue { get; }
        public Fixed1616 DefaultValue { get; }
        public Fixed1616 MaxValue { get; }
        public ushort Flags { get; }
        public ushort AxisNameId { get; }

        public AxisRecord(Tag axisTag, Fixed1616 minValue, Fixed1616 defaultValue, Fixed1616 maxValue, ushort flags, ushort axisNameId)
        {
            AxisTag = axisTag;
            MinValue = minValue;
            DefaultValue = defaultValue;
            MaxValue = maxValue;
            Flags = flags;
            AxisNameId = axisNameId;
        }
    }

    public bool TryGetAxisRecord(int axisIndex, out AxisRecord record)
    {
        record = default;

        ushort axisCount = AxisCount;
        if ((uint)axisIndex >= axisCount)
            return false;

        ushort axisSize = AxisSize;
        ushort axesArrayOffset = BigEndian.ReadUInt16(_table.Span, 4);
        int offset = axesArrayOffset + (axisIndex * axisSize);
        if ((uint)offset > (uint)_table.Length - 20)
            return false;

        var data = _table.Span;
        record = new AxisRecord(
            axisTag: new Tag(BigEndian.ReadUInt32(data, offset + 0)),
            minValue: new Fixed1616(BigEndian.ReadUInt32(data, offset + 4)),
            defaultValue: new Fixed1616(BigEndian.ReadUInt32(data, offset + 8)),
            maxValue: new Fixed1616(BigEndian.ReadUInt32(data, offset + 12)),
            flags: BigEndian.ReadUInt16(data, offset + 16),
            axisNameId: BigEndian.ReadUInt16(data, offset + 18));
        return true;
    }

    public bool TryGetInstanceRecord(int instanceIndex, out InstanceRecord record)
    {
        record = default;

        ushort instanceCount = InstanceCount;
        if ((uint)instanceIndex >= instanceCount)
            return false;

        ushort axisCount = AxisCount;
        ushort axisSize = AxisSize;
        ushort axesArrayOffset = BigEndian.ReadUInt16(_table.Span, 4);
        int instancesArrayOffset = checked(axesArrayOffset + ((int)axisCount * axisSize));

        ushort instanceSize = InstanceSize;
        int offset = checked(instancesArrayOffset + (instanceIndex * instanceSize));
        if ((uint)offset > (uint)_table.Length - 4)
            return false;

        record = new InstanceRecord(_table, offset, axisCount, instanceSize);
        return true;
    }

    [OtSubTable(4, GenerateTryCreate = false, GenerateStorage = false)]
    [OtField("SubfamilyNameId", OtFieldKind.UInt16, 0)]
    [OtField("Flags", OtFieldKind.UInt16, 2)]
    public readonly partial struct InstanceRecord
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly ushort _axisCount;
        private readonly ushort _instanceSize;

        internal InstanceRecord(TableSlice fvar, int offset, ushort axisCount, ushort instanceSize)
        {
            _table = fvar;
            _offset = offset;
            _axisCount = axisCount;
            _instanceSize = instanceSize;
        }

        public bool HasPostScriptNameId => _instanceSize >= (4 + (_axisCount * 4) + 2);

        public bool TryGetCoordinate(int axisIndex, out Fixed1616 coordinate)
        {
            coordinate = default;

            if ((uint)axisIndex >= _axisCount)
                return false;

            int offset = checked(_offset + 4 + (axisIndex * 4));
            if ((uint)offset > (uint)_table.Length - 4)
                return false;

            coordinate = new Fixed1616(BigEndian.ReadUInt32(_table.Span, offset));
            return true;
        }

        public bool TryGetPostScriptNameId(out ushort postScriptNameId)
        {
            postScriptNameId = 0;

            if (!HasPostScriptNameId)
                return false;

            int offset = checked(_offset + 4 + (_axisCount * 4));
            if ((uint)offset > (uint)_table.Length - 2)
                return false;

            postScriptNameId = BigEndian.ReadUInt16(_table.Span, offset);
            return true;
        }
    }
}
