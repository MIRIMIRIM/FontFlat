using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("kern", 4)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("SubtableCount", OtFieldKind.UInt16, 2)]
public readonly partial struct KernTable
{
    public bool TryGetSubtable(int index, out KernSubtable subtable)
    {
        subtable = default;

        // v0 only for now.
        if (Version != 0)
            return false;

        ushort count = SubtableCount;
        if ((uint)index >= (uint)count)
            return false;

        int offset = 4;
        var data = _table.Span;
        for (int i = 0; i < index; i++)
        {
            if ((uint)offset > (uint)_table.Length - 6)
                return false;

            ushort length = BigEndian.ReadUInt16(data, offset + 2);
            if (length < 6)
                return false;

            int next = offset + length;
            if (next < offset || next > _table.Length)
                return false;

            offset = next;
        }

        if ((uint)offset > (uint)_table.Length - 6)
            return false;

        ushort stLengthU16 = BigEndian.ReadUInt16(data, offset + 2);
        if (stLengthU16 < 6)
            return false;

        int stLength = stLengthU16;
        if (offset + stLength > _table.Length)
            return false;

        subtable = new KernSubtable(_table, offset, stLength);
        return true;
    }

    [OtSubTable(6, GenerateTryCreate = false, GenerateStorage = false)]
    [OtField("Version", OtFieldKind.UInt16, 0)]
    [OtField("Coverage", OtFieldKind.UInt16, 4)]
    public readonly partial struct KernSubtable
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly int _length;

        internal KernSubtable(TableSlice kern, int offset, int length)
        {
            _table = kern;
            _offset = offset;
            _length = length;
        }

        public ushort Length => (ushort)_length;

        public bool IsHorizontal => (Coverage & 0x0001) != 0;
        public bool IsMinimum => (Coverage & 0x0002) != 0;
        public bool IsCrossStream => (Coverage & 0x0004) != 0;
        public bool IsOverride => (Coverage & 0x0008) != 0;

        public byte Format => (byte)(Coverage >> 8);

        public bool TryGetFormat0(out Format0Subtable format0)
        {
            format0 = default;
            if (Format != 0)
                return false;
            if (_length < 14)
                return false;

            format0 = new Format0Subtable(_table, _offset, _length);
            return true;
        }
    }

    [OtSubTable(14, GenerateTryCreate = false, GenerateStorage = false)]
    [OtField("PairCount", OtFieldKind.UInt16, 6)]
    [OtField("SearchRange", OtFieldKind.UInt16, 8)]
    [OtField("EntrySelector", OtFieldKind.UInt16, 10)]
    [OtField("RangeShift", OtFieldKind.UInt16, 12)]
    [OtSequentialRecordArray(
        "Pair",
        14,
        6,
        CountPropertyName = nameof(PairCount),
        RecordTypeName = nameof(KerningPair),
        BoundsLengthExpression = "_length")]
    public readonly partial struct Format0Subtable
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly int _length;

        internal Format0Subtable(TableSlice kern, int offset, int length)
        {
            _table = kern;
            _offset = offset;
            _length = length;
        }

        public readonly struct KerningPair
        {
            public ushort Left { get; }
            public ushort Right { get; }
            public short Value { get; }

            public KerningPair(ushort left, ushort right, short value)
            {
                Left = left;
                Right = right;
                Value = value;
            }
        }

        public bool TryFindKerningValue(ushort left, ushort right, out short value)
        {
            value = 0;

            ushort count = PairCount;
            if (count == 0)
                return false;

            uint key = ((uint)left << 16) | right;

            int lo = 0;
            int hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (!TryGetPair(mid, out var pair))
                    return false;

                uint midKey = ((uint)pair.Left << 16) | pair.Right;
                if (midKey == key)
                {
                    value = pair.Value;
                    return true;
                }

                if (midKey < key)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return false;
        }
    }
}
