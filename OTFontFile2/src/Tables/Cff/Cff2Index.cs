namespace OTFontFile2.Tables;

public readonly struct Cff2Index
{
    private readonly TableSlice _cff;
    private readonly int _offset;
    private readonly uint _count;
    private readonly byte _offSize;
    private readonly int _dataOffset;
    private readonly int _byteLength;

    private Cff2Index(TableSlice cff, int offset, uint count, byte offSize, int dataOffset, int byteLength)
    {
        _cff = cff;
        _offset = offset;
        _count = count;
        _offSize = offSize;
        _dataOffset = dataOffset;
        _byteLength = byteLength;
    }

    public static bool TryCreate(TableSlice cff, int offset, out Cff2Index index)
    {
        index = default;

        // count(4)
        if ((uint)offset > (uint)cff.Length - 4)
            return false;

        var data = cff.Span;
        uint count = BigEndian.ReadUInt32(data, offset);

        // Empty INDEX: only count(4).
        if (count == 0)
        {
            index = new Cff2Index(cff, offset, count: 0, offSize: 0, dataOffset: offset + 4, byteLength: 4);
            return true;
        }

        // count(4) + offSize(1)
        if ((uint)offset > (uint)cff.Length - 5)
            return false;

        byte offSize = data[offset + 4];
        if (offSize is < 1 or > 4)
            return false;

        // offsets start after count+offSize
        int offsetsOffset = offset + 5;

        long offsetsLengthLong = ((long)count + 1) * offSize;
        if (offsetsLengthLong > int.MaxValue)
            return false;

        int offsetsLength = (int)offsetsLengthLong;
        int dataOffset = offsetsOffset + offsetsLength;
        if ((uint)dataOffset > (uint)cff.Length)
            return false;

        long lastOffsetPosLong = offsetsOffset + ((long)count * offSize);
        if (lastOffsetPosLong > int.MaxValue)
            return false;

        int lastOffsetPos = (int)lastOffsetPosLong;
        if ((uint)lastOffsetPos > (uint)cff.Length - (uint)offSize)
            return false;

        uint lastOffset = ReadOffset(data, lastOffsetPos, offSize);
        if (lastOffset == 0)
            return false;
        if (lastOffset > int.MaxValue)
            return false;

        int dataLength = (int)lastOffset - 1;
        long byteLengthLong = 5L + offsetsLengthLong + dataLength;
        if (byteLengthLong > int.MaxValue)
            return false;

        int byteLength = (int)byteLengthLong;
        if (byteLength > cff.Length - offset)
            return false;

        index = new Cff2Index(cff, offset, count, offSize, dataOffset, byteLength);
        return true;
    }

    public TableSlice Table => _cff;
    public int Offset => _offset;
    public uint Count => _count;
    public byte OffSize => _offSize;
    public int DataOffset => _dataOffset;
    public int ByteLength => _byteLength;

    public bool IsEmpty => _count == 0;

    public bool TryGetObjectSpan(int index, out ReadOnlySpan<byte> obj)
    {
        obj = default;

        if (!TryGetObjectBounds(index, out int start, out int length))
            return false;

        obj = _cff.Span.Slice(start, length);
        return true;
    }

    public bool TryGetObjectBounds(int index, out int start, out int length)
    {
        start = 0;
        length = 0;

        uint count = _count;
        if (count == 0)
            return false;

        if (index < 0 || (uint)index >= count)
            return false;

        if (!TryGetOffset((uint)index, out uint start1Based) || start1Based == 0)
            return false;
        if (!TryGetOffset((uint)index + 1, out uint end1Based) || end1Based < start1Based)
            return false;

        if (start1Based > int.MaxValue || end1Based > int.MaxValue)
            return false;

        start = _dataOffset + (int)start1Based - 1;
        length = (int)(end1Based - start1Based);

        if (length < 0)
            return false;
        if ((uint)start > (uint)_cff.Length)
            return false;
        if (length > _cff.Length - start)
            return false;

        return true;
    }

    public bool TryGetOffset(uint entryIndex, out uint offset1Based)
    {
        offset1Based = 0;

        uint count = _count;
        if (count == 0)
            return false;

        if (entryIndex > count)
            return false;

        long posLong = _offset + 5L + (entryIndex * (long)_offSize);
        if (posLong > int.MaxValue)
            return false;

        int pos = (int)posLong;
        if ((uint)pos > (uint)_cff.Length - (uint)_offSize)
            return false;

        offset1Based = ReadOffset(_cff.Span, pos, _offSize);
        return true;
    }

    private static uint ReadOffset(ReadOnlySpan<byte> data, int offset, int offSize)
    {
        return offSize switch
        {
            1 => data[offset],
            2 => BigEndian.ReadUInt16(data, offset),
            3 => (uint)(data[offset] << 16 | data[offset + 1] << 8 | data[offset + 2]),
            4 => BigEndian.ReadUInt32(data, offset),
            _ => 0
        };
    }
}
