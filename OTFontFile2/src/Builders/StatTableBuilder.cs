using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>STAT</c> table (Style Attributes).
/// Supports STAT v1 axis records and axis value table formats 1–4.
/// </summary>
[OtTableBuilder("STAT")]
public sealed partial class StatTableBuilder : ISfntTableSource
{
    private const ushort SupportedMajorVersion = 1;
    private const ushort DesignAxisRecordSize = 8;

    private readonly List<DesignAxisRecord> _designAxes = new();
    private readonly List<IAxisValueTable> _axisValues = new();

    private ushort _majorVersion = SupportedMajorVersion;
    private ushort _minorVersion;
    private ushort _elidedFallbackNameId;

    public ushort MajorVersion
    {
        get => _majorVersion;
        set
        {
            if (value == _majorVersion)
                return;

            _majorVersion = value;
            MarkDirty();
        }
    }

    public ushort MinorVersion
    {
        get => _minorVersion;
        set
        {
            if (value == _minorVersion)
                return;

            _minorVersion = value;
            MarkDirty();
        }
    }

    public ushort ElidedFallbackNameId
    {
        get => _elidedFallbackNameId;
        set
        {
            if (value == _elidedFallbackNameId)
                return;

            _elidedFallbackNameId = value;
            MarkDirty();
        }
    }

    public int DesignAxisCount => _designAxes.Count;

    public IReadOnlyList<DesignAxisRecord> DesignAxes => _designAxes;

    public int AxisValueCount => _axisValues.Count;

    public void ClearDesignAxes()
    {
        if (_designAxes.Count == 0)
            return;

        _designAxes.Clear();
        MarkDirty();
    }

    public void AddDesignAxis(Tag axisTag, ushort axisNameId, ushort axisOrdering)
    {
        _designAxes.Add(new DesignAxisRecord(axisTag, axisNameId, axisOrdering));
        MarkDirty();
    }

    public bool RemoveDesignAxisAt(int index)
    {
        if ((uint)index >= (uint)_designAxes.Count)
            return false;

        _designAxes.RemoveAt(index);
        MarkDirty();
        return true;
    }

    public void ClearAxisValues()
    {
        if (_axisValues.Count == 0)
            return;

        _axisValues.Clear();
        MarkDirty();
    }

    public bool RemoveAxisValueAt(int index)
    {
        if ((uint)index >= (uint)_axisValues.Count)
            return false;

        _axisValues.RemoveAt(index);
        MarkDirty();
        return true;
    }

    public void AddAxisValueFormat1(ushort axisIndex, ushort flags, ushort valueNameId, Fixed1616 value)
    {
        _axisValues.Add(new AxisValueFormat1(axisIndex, flags, valueNameId, value));
        MarkDirty();
    }

    public void AddAxisValueFormat2(ushort axisIndex, ushort flags, ushort valueNameId, Fixed1616 nominalValue, Fixed1616 rangeMinValue, Fixed1616 rangeMaxValue)
    {
        _axisValues.Add(new AxisValueFormat2(axisIndex, flags, valueNameId, nominalValue, rangeMinValue, rangeMaxValue));
        MarkDirty();
    }

    public void AddAxisValueFormat3(ushort axisIndex, ushort flags, ushort valueNameId, Fixed1616 value, Fixed1616 linkedValue)
    {
        _axisValues.Add(new AxisValueFormat3(axisIndex, flags, valueNameId, value, linkedValue));
        MarkDirty();
    }

    public void AddAxisValueFormat4(ushort flags, ushort valueNameId, ReadOnlySpan<AxisValueRecord> records)
    {
        if (records.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(records), "STAT format 4 record count must fit in uint16.");

        var copied = new AxisValueRecord[records.Length];
        for (int i = 0; i < records.Length; i++)
            copied[i] = records[i];

        _axisValues.Add(new AxisValueFormat4(flags, valueNameId, copied));
        MarkDirty();
    }

    public static bool TryFrom(StatTable stat, out StatTableBuilder builder)
    {
        builder = null!;

        if (!stat.IsSupported)
            return false;

        var b = new StatTableBuilder
        {
            MajorVersion = stat.MajorVersion,
            MinorVersion = stat.MinorVersion,
            ElidedFallbackNameId = stat.ElidedFallbackNameId
        };

        int axisCount = stat.DesignAxisCount;
        for (int i = 0; i < axisCount; i++)
        {
            if (!stat.TryGetDesignAxisRecord(i, out var axis))
                return false;

            b._designAxes.Add(new DesignAxisRecord(axis.AxisTag, axis.AxisNameId, axis.AxisOrdering));
        }

        int valueCount = stat.AxisValueCount;
        for (int i = 0; i < valueCount; i++)
        {
            if (!stat.TryGetAxisValueTable(i, out var axisValueTable))
                return false;

            switch (axisValueTable.Format)
            {
                case 1:
                    if (!axisValueTable.TryGetFormat1(out var f1))
                        return false;
                    b._axisValues.Add(new AxisValueFormat1(f1.AxisIndex, f1.Flags, f1.ValueNameId, f1.Value));
                    break;
                case 2:
                    if (!axisValueTable.TryGetFormat2(out var f2))
                        return false;
                    b._axisValues.Add(new AxisValueFormat2(f2.AxisIndex, f2.Flags, f2.ValueNameId, f2.NominalValue, f2.RangeMinValue, f2.RangeMaxValue));
                    break;
                case 3:
                    if (!axisValueTable.TryGetFormat3(out var f3))
                        return false;
                    b._axisValues.Add(new AxisValueFormat3(f3.AxisIndex, f3.Flags, f3.ValueNameId, f3.Value, f3.LinkedValue));
                    break;
                case 4:
                    if (!axisValueTable.TryGetFormat4(out var f4))
                        return false;

                    int recordCount = f4.AxisValueRecordCount;
                    var records = new AxisValueRecord[recordCount];
                    for (int r = 0; r < recordCount; r++)
                    {
                        if (!f4.TryGetAxisValueRecord(r, out var record))
                            return false;
                        records[r] = new AxisValueRecord(record.AxisIndex, record.Value);
                    }

                    b._axisValues.Add(new AxisValueFormat4(f4.Flags, f4.ValueNameId, records));
                    break;
                default:
                    return false;
            }
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (MajorVersion != SupportedMajorVersion)
            throw new InvalidOperationException("STAT major version must be 1.");

        if (_designAxes.Count > ushort.MaxValue)
            throw new InvalidOperationException("STAT design axis count must fit in uint16.");

        if (_axisValues.Count > ushort.MaxValue)
            throw new InvalidOperationException("STAT axis value count must fit in uint16.");

        int headerSize = 20;
        int designAxesOffset = headerSize;
        int designAxesBytes = checked(_designAxes.Count * DesignAxisRecordSize);
        int axisValueOffset = checked(designAxesOffset + designAxesBytes);

        int axisValueOffsetsBytes = checked(_axisValues.Count * 2);
        int pos = checked(axisValueOffset + axisValueOffsetsBytes);

        var relOffsets = new ushort[_axisValues.Count];
        for (int i = 0; i < _axisValues.Count; i++)
        {
            int rel = pos - axisValueOffset;
            if ((uint)rel > ushort.MaxValue)
                throw new InvalidOperationException("STAT axis value table offsets must fit in uint16.");

            relOffsets[i] = (ushort)rel;
            pos = checked(pos + _axisValues[i].ByteLength);
        }

        byte[] table = new byte[pos];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, MajorVersion);
        BigEndian.WriteUInt16(span, 2, MinorVersion);
        BigEndian.WriteUInt16(span, 4, DesignAxisRecordSize);
        BigEndian.WriteUInt16(span, 6, checked((ushort)_designAxes.Count));
        BigEndian.WriteUInt32(span, 8, (uint)designAxesOffset);
        BigEndian.WriteUInt16(span, 12, checked((ushort)_axisValues.Count));
        BigEndian.WriteUInt32(span, 14, (uint)axisValueOffset);
        BigEndian.WriteUInt16(span, 18, ElidedFallbackNameId);

        int aPos = designAxesOffset;
        for (int i = 0; i < _designAxes.Count; i++)
        {
            var a = _designAxes[i];
            BigEndian.WriteUInt32(span, aPos + 0, a.AxisTag.Value);
            BigEndian.WriteUInt16(span, aPos + 4, a.AxisNameId);
            BigEndian.WriteUInt16(span, aPos + 6, a.AxisOrdering);
            aPos += DesignAxisRecordSize;
        }

        int offsetsPos = axisValueOffset;
        for (int i = 0; i < relOffsets.Length; i++)
            BigEndian.WriteUInt16(span, offsetsPos + (i * 2), relOffsets[i]);

        int vPos = checked(axisValueOffset + axisValueOffsetsBytes);
        for (int i = 0; i < _axisValues.Count; i++)
        {
            _axisValues[i].Write(span, vPos);
            vPos = checked(vPos + _axisValues[i].ByteLength);
        }

        return table;
    }

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

    private interface IAxisValueTable
    {
        int ByteLength { get; }
        void Write(Span<byte> destination, int offset);
    }

    private sealed class AxisValueFormat1 : IAxisValueTable
    {
        private readonly ushort _axisIndex;
        private readonly ushort _flags;
        private readonly ushort _valueNameId;
        private readonly Fixed1616 _value;

        public AxisValueFormat1(ushort axisIndex, ushort flags, ushort valueNameId, Fixed1616 value)
        {
            _axisIndex = axisIndex;
            _flags = flags;
            _valueNameId = valueNameId;
            _value = value;
        }

        public int ByteLength => 12;

        public void Write(Span<byte> destination, int offset)
        {
            BigEndian.WriteUInt16(destination, offset + 0, 1);
            BigEndian.WriteUInt16(destination, offset + 2, _axisIndex);
            BigEndian.WriteUInt16(destination, offset + 4, _flags);
            BigEndian.WriteUInt16(destination, offset + 6, _valueNameId);
            BigEndian.WriteUInt32(destination, offset + 8, _value.RawValue);
        }
    }

    private sealed class AxisValueFormat2 : IAxisValueTable
    {
        private readonly ushort _axisIndex;
        private readonly ushort _flags;
        private readonly ushort _valueNameId;
        private readonly Fixed1616 _nominalValue;
        private readonly Fixed1616 _rangeMinValue;
        private readonly Fixed1616 _rangeMaxValue;

        public AxisValueFormat2(ushort axisIndex, ushort flags, ushort valueNameId, Fixed1616 nominalValue, Fixed1616 rangeMinValue, Fixed1616 rangeMaxValue)
        {
            _axisIndex = axisIndex;
            _flags = flags;
            _valueNameId = valueNameId;
            _nominalValue = nominalValue;
            _rangeMinValue = rangeMinValue;
            _rangeMaxValue = rangeMaxValue;
        }

        public int ByteLength => 20;

        public void Write(Span<byte> destination, int offset)
        {
            BigEndian.WriteUInt16(destination, offset + 0, 2);
            BigEndian.WriteUInt16(destination, offset + 2, _axisIndex);
            BigEndian.WriteUInt16(destination, offset + 4, _flags);
            BigEndian.WriteUInt16(destination, offset + 6, _valueNameId);
            BigEndian.WriteUInt32(destination, offset + 8, _nominalValue.RawValue);
            BigEndian.WriteUInt32(destination, offset + 12, _rangeMinValue.RawValue);
            BigEndian.WriteUInt32(destination, offset + 16, _rangeMaxValue.RawValue);
        }
    }

    private sealed class AxisValueFormat3 : IAxisValueTable
    {
        private readonly ushort _axisIndex;
        private readonly ushort _flags;
        private readonly ushort _valueNameId;
        private readonly Fixed1616 _value;
        private readonly Fixed1616 _linkedValue;

        public AxisValueFormat3(ushort axisIndex, ushort flags, ushort valueNameId, Fixed1616 value, Fixed1616 linkedValue)
        {
            _axisIndex = axisIndex;
            _flags = flags;
            _valueNameId = valueNameId;
            _value = value;
            _linkedValue = linkedValue;
        }

        public int ByteLength => 16;

        public void Write(Span<byte> destination, int offset)
        {
            BigEndian.WriteUInt16(destination, offset + 0, 3);
            BigEndian.WriteUInt16(destination, offset + 2, _axisIndex);
            BigEndian.WriteUInt16(destination, offset + 4, _flags);
            BigEndian.WriteUInt16(destination, offset + 6, _valueNameId);
            BigEndian.WriteUInt32(destination, offset + 8, _value.RawValue);
            BigEndian.WriteUInt32(destination, offset + 12, _linkedValue.RawValue);
        }
    }

    private sealed class AxisValueFormat4 : IAxisValueTable
    {
        private readonly ushort _flags;
        private readonly ushort _valueNameId;
        private readonly AxisValueRecord[] _records;

        public AxisValueFormat4(ushort flags, ushort valueNameId, AxisValueRecord[] records)
        {
            _flags = flags;
            _valueNameId = valueNameId;
            _records = records ?? throw new ArgumentNullException(nameof(records));
        }

        public int ByteLength => checked(8 + (_records.Length * 6));

        public void Write(Span<byte> destination, int offset)
        {
            if (_records.Length > ushort.MaxValue)
                throw new InvalidOperationException("STAT format 4 axis count must fit in uint16.");

            BigEndian.WriteUInt16(destination, offset + 0, 4);
            BigEndian.WriteUInt16(destination, offset + 2, checked((ushort)_records.Length));
            BigEndian.WriteUInt16(destination, offset + 4, _flags);
            BigEndian.WriteUInt16(destination, offset + 6, _valueNameId);

            int pos = offset + 8;
            for (int i = 0; i < _records.Length; i++)
            {
                var r = _records[i];
                BigEndian.WriteUInt16(destination, pos + 0, r.AxisIndex);
                BigEndian.WriteUInt32(destination, pos + 2, r.Value.RawValue);
                pos += 6;
            }
        }
    }
}
