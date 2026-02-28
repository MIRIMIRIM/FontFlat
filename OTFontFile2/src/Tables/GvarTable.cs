using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("gvar", 20, GenerateTryCreate = false, GenerateStorage = false)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("AxisCount", OtFieldKind.UInt16, 4)]
[OtField("SharedTupleCount", OtFieldKind.UInt16, 6)]
[OtField("GlyphCount", OtFieldKind.UInt16, 12)]
[OtField("Flags", OtFieldKind.UInt16, 14)]
public readonly partial struct GvarTable
{
    private readonly TableSlice _table;
    private readonly int _sharedTuplesOffset;
    private readonly int _glyphVariationDataArrayOffset;

    private GvarTable(TableSlice table, int sharedTuplesOffset, int glyphVariationDataArrayOffset)
    {
        _table = table;
        _sharedTuplesOffset = sharedTuplesOffset;
        _glyphVariationDataArrayOffset = glyphVariationDataArrayOffset;
    }

    public static bool TryCreate(TableSlice table, out GvarTable gvar)
    {
        gvar = default;

        // header(20)
        if (table.Length < 20)
            return false;

        var data = table.Span;
        uint version = BigEndian.ReadUInt32(data, 0);
        if ((version >> 16) != 1)
            return false;

        ushort axisCount = BigEndian.ReadUInt16(data, 4);
        ushort sharedTupleCount = BigEndian.ReadUInt16(data, 6);

        uint sharedTuplesOffsetU = BigEndian.ReadUInt32(data, 8);
        if (sharedTuplesOffsetU > int.MaxValue)
            return false;
        int sharedTuplesOffset = (int)sharedTuplesOffsetU;

        ushort glyphCount = BigEndian.ReadUInt16(data, 12);
        ushort flags = BigEndian.ReadUInt16(data, 14);

        uint glyphVariationDataArrayOffsetU = BigEndian.ReadUInt32(data, 16);
        if (glyphVariationDataArrayOffsetU > int.MaxValue)
            return false;
        int glyphVariationDataArrayOffset = (int)glyphVariationDataArrayOffsetU;

        bool offsetsAreLong = (flags & 1) != 0;
        int offsetEntrySize = offsetsAreLong ? 4 : 2;
        const int glyphVariationDataOffsetsOffset = 20;

        long offsetsBytesLong = (glyphCount + 1L) * offsetEntrySize;
        if (offsetsBytesLong > int.MaxValue)
            return false;
        int offsetsBytes = (int)offsetsBytesLong;

        if ((uint)glyphVariationDataOffsetsOffset > (uint)table.Length - (uint)offsetsBytes)
            return false;

        if ((uint)glyphVariationDataArrayOffset > (uint)table.Length)
            return false;

        // Validate shared tuples region.
        if (sharedTupleCount != 0)
        {
            if (axisCount == 0)
                return false;

            long tupleBytesLong = (long)sharedTupleCount * axisCount * 2;
            if (tupleBytesLong > int.MaxValue)
                return false;

            int tupleBytes = (int)tupleBytesLong;
            if ((uint)sharedTuplesOffset > (uint)table.Length - (uint)tupleBytes)
                return false;
        }

        // Validate last offset is in-bounds.
        if (!TryReadGlyphVariationDataOffset(data, glyphVariationDataOffsetsOffset, glyphCount, offsetEntrySize, offsetsAreLong, out int lastOffset))
            return false;

        if ((uint)lastOffset > (uint)(table.Length - glyphVariationDataArrayOffset))
            return false;

        gvar = new GvarTable(table, sharedTuplesOffset, glyphVariationDataArrayOffset);
        return true;
    }

    public int SharedTuplesOffset => _sharedTuplesOffset;

    public bool OffsetsAreLong => (Flags & 1) != 0;

    public int GlyphVariationDataArrayOffset => _glyphVariationDataArrayOffset;

    public bool TryGetSharedTuple(int index, out SharedTuple tuple)
    {
        tuple = default;

        ushort sharedTupleCount = SharedTupleCount;
        if ((uint)index >= sharedTupleCount)
            return false;

        if (sharedTupleCount == 0)
            return false;

        ushort axisCount = AxisCount;
        int tupleSize = checked(axisCount * 2);
        int offset = checked(_sharedTuplesOffset + (index * tupleSize));
        if ((uint)offset > (uint)_table.Length - (uint)tupleSize)
            return false;

        tuple = new SharedTuple(_table, offset, axisCount);
        return true;
    }

    public readonly struct SharedTuple
    {
        private readonly TableSlice _gvar;
        private readonly int _offset;
        private readonly ushort _axisCount;

        internal SharedTuple(TableSlice gvar, int offset, ushort axisCount)
        {
            _gvar = gvar;
            _offset = offset;
            _axisCount = axisCount;
        }

        public ushort AxisCount => _axisCount;

        public bool TryGetCoordinate(int axisIndex, out F2Dot14 coordinate)
        {
            coordinate = default;

            if ((uint)axisIndex >= _axisCount)
                return false;

            int offset = checked(_offset + (axisIndex * 2));
            if ((uint)offset > (uint)_gvar.Length - 2)
                return false;

            coordinate = new F2Dot14(BigEndian.ReadInt16(_gvar.Span, offset));
            return true;
        }
    }

    public bool TryGetGlyphVariationDataBounds(ushort glyphId, out int offset, out int length)
    {
        offset = 0;
        length = 0;

        ushort glyphCount = GlyphCount;
        if (glyphId >= glyphCount)
            return false;

        var data = _table.Span;
        bool offsetsAreLong = OffsetsAreLong;
        int offsetEntrySize = offsetsAreLong ? 4 : 2;
        const int glyphVariationDataOffsetsOffset = 20;

        if (!TryReadGlyphVariationDataOffset(data, glyphVariationDataOffsetsOffset, glyphId, offsetEntrySize, offsetsAreLong, out int startRel))
            return false;

        if (!TryReadGlyphVariationDataOffset(data, glyphVariationDataOffsetsOffset, glyphId + 1, offsetEntrySize, offsetsAreLong, out int endRel))
            return false;

        if (endRel < startRel)
            return false;

        int startAbs = checked(_glyphVariationDataArrayOffset + startRel);
        int len = endRel - startRel;

        if ((uint)startAbs > (uint)_table.Length - (uint)len)
            return false;

        offset = startAbs;
        length = len;
        return true;
    }

    public bool TryGetGlyphTupleVariationStore(ushort glyphId, out TupleVariationStore store)
    {
        store = default;

        if (!TryGetGlyphVariationDataBounds(glyphId, out int offset, out int length))
            return false;

        if (length == 0)
            return false;

        return TupleVariationStore.TryCreate(_table, offset, length, originOffset: offset, AxisCount, out store);
    }

    private static bool TryReadGlyphVariationDataOffset(
        ReadOnlySpan<byte> data,
        int offsetsArrayOffset,
        int index,
        int entrySize,
        bool offsetsAreLong,
        out int offset)
    {
        offset = 0;

        int entryOffset = checked(offsetsArrayOffset + (index * entrySize));
        if (offsetsAreLong)
        {
            if ((uint)entryOffset > (uint)data.Length - 4)
                return false;

            uint v = BigEndian.ReadUInt32(data, entryOffset);
            if (v > int.MaxValue)
                return false;

            offset = (int)v;
            return true;
        }

        if ((uint)entryOffset > (uint)data.Length - 2)
            return false;

        ushort words = BigEndian.ReadUInt16(data, entryOffset);
        offset = words * 2;
        return true;
    }
}
