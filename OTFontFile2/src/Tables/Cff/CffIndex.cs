namespace OTFontFile2.Tables;

public readonly struct CffIndex
{
    private readonly TableSlice _cff;
    private readonly int _offset;
    private readonly ushort _count;
    private readonly byte _offSize;
    private readonly int _dataOffset;
    private readonly int _byteLength;

    private CffIndex(TableSlice cff, int offset, ushort count, byte offSize, int dataOffset, int byteLength)
    {
        _cff = cff;
        _offset = offset;
        _count = count;
        _offSize = offSize;
        _dataOffset = dataOffset;
        _byteLength = byteLength;
    }

    public static bool TryCreate(TableSlice cff, int offset, out CffIndex index)
    {
        index = default;

        if ((uint)offset > (uint)cff.Length - 2)
            return false;

        var data = cff.Span;
        ushort count = BigEndian.ReadUInt16(data, offset);

        // Empty INDEX: only count(2).
        if (count == 0)
        {
            index = new CffIndex(cff, offset, count: 0, offSize: 0, dataOffset: offset + 2, byteLength: 2);
            return true;
        }

        if ((uint)offset > (uint)cff.Length - 3)
            return false;

        byte offSize = data[offset + 2];
        if (offSize is < 1 or > 4)
            return false;

        int offsetsOffset = offset + 3;
        int offsetsLength = (count + 1) * offSize;
        int dataOffset = offsetsOffset + offsetsLength;
        if ((uint)dataOffset > (uint)cff.Length)
            return false;

        // lastOffset = offsets[count]
        int lastOffsetPos = offsetsOffset + (count * offSize);
        if ((uint)lastOffsetPos > (uint)cff.Length - (uint)offSize)
            return false;

        uint lastOffset = ReadOffset(data, lastOffsetPos, offSize);
        if (lastOffset == 0)
            return false;

        if (lastOffset > int.MaxValue)
            return false;

        int dataLength = (int)lastOffset - 1;
        long byteLengthLong = 3L + offsetsLength + dataLength;
        if (byteLengthLong > int.MaxValue)
            return false;

        int byteLength = (int)byteLengthLong;
        if (byteLength > cff.Length - offset)
            return false;

        index = new CffIndex(cff, offset, count, offSize, dataOffset, byteLength);
        return true;
    }

    public TableSlice Table => _cff;
    public int Offset => _offset;
    public ushort Count => _count;
    public byte OffSize => _offSize;
    public int DataOffset => _dataOffset;
    public int ByteLength => _byteLength;

    public bool IsEmpty => _count == 0;

    public bool TryGetObjectSpan(int index, out ReadOnlySpan<byte> obj)
    {
        obj = default;

        if (_count == 0)
            return false;

        if ((uint)index >= (uint)_count)
            return false;

        if (!TryGetObjectBounds(index, out int start, out int length))
            return false;

        obj = _cff.Span.Slice(start, length);
        return true;
    }

    public bool TryGetObjectBounds(int index, out int start, out int length)
    {
        start = 0;
        length = 0;

        if (_count == 0)
            return false;

        if ((uint)index >= (uint)_count)
            return false;

        if (!TryGetOffset(index, out uint start1Based) || start1Based == 0)
            return false;
        if (!TryGetOffset(index + 1, out uint end1Based) || end1Based < start1Based)
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

    public bool TryGetOffset(int entryIndex, out uint offset1Based)
    {
        offset1Based = 0;

        if (_count == 0)
            return false;

        if ((uint)entryIndex > (uint)_count)
            return false;

        int offsetsOffset = _offset + 3;
        int pos = offsetsOffset + (entryIndex * _offSize);
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
            3 => BigEndian.ReadUInt24(data, offset),
            4 => BigEndian.ReadUInt32(data, offset),
            _ => 0
        };
    }
}
