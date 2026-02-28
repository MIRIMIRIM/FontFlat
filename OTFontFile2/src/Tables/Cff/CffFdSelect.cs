namespace OTFontFile2.Tables;

public readonly struct CffFdSelect
{
    private readonly TableSlice _cff;
    private readonly int _offset;
    private readonly int _glyphCount;

    private CffFdSelect(TableSlice cff, int offset, int glyphCount)
    {
        _cff = cff;
        _offset = offset;
        _glyphCount = glyphCount;
    }

    public static bool TryCreate(TableSlice cff, int offset, int glyphCount, out CffFdSelect fdSelect)
    {
        fdSelect = default;

        if (glyphCount <= 0)
            return false;

        if ((uint)offset >= (uint)cff.Length)
            return false;

        byte format = cff.Span[offset];
        switch (format)
        {
            case 0:
            {
                int needed = 1 + glyphCount;
                if ((uint)offset > (uint)cff.Length - (uint)needed)
                    return false;
                break;
            }
            case 3:
            {
                if ((uint)offset > (uint)cff.Length - 3)
                    return false;

                ushort nRanges = BigEndian.ReadUInt16(cff.Span, offset + 1);
                long needed = 1L + 2 + (nRanges * 3L) + 2;
                if (needed > int.MaxValue)
                    return false;
                if ((uint)offset > (uint)cff.Length - (uint)needed)
                    return false;
                break;
            }
            case 4:
            {
                if ((uint)offset > (uint)cff.Length - 5)
                    return false;

                uint nRanges = BigEndian.ReadUInt32(cff.Span, offset + 1);
                long needed = 1L + 4 + (nRanges * 6L) + 4;
                if (needed > int.MaxValue)
                    return false;
                if ((uint)offset > (uint)cff.Length - (uint)needed)
                    return false;
                break;
            }
            default:
                return false;
        }

        fdSelect = new CffFdSelect(cff, offset, glyphCount);
        return true;
    }

    public TableSlice Table => _cff;
    public int Offset => _offset;
    public int GlyphCount => _glyphCount;

    public byte Format => _cff.Span[_offset];

    public bool TryGetByteLength(out int byteLength)
    {
        byteLength = 0;

        var data = _cff.Span;
        if ((uint)_offset >= (uint)data.Length)
            return false;

        byte format = data[_offset];
        switch (format)
        {
            case 0:
            {
                int needed = 1 + _glyphCount;
                if ((uint)_offset > (uint)data.Length - (uint)needed)
                    return false;
                byteLength = needed;
                return true;
            }
            case 3:
            {
                if ((uint)_offset > (uint)data.Length - 3)
                    return false;

                ushort nRanges = BigEndian.ReadUInt16(data, _offset + 1);
                long needed = 1L + 2 + (nRanges * 3L) + 2;
                if (needed > int.MaxValue)
                    return false;
                if ((uint)_offset > (uint)data.Length - (uint)needed)
                    return false;

                byteLength = (int)needed;
                return true;
            }
            case 4:
            {
                if ((uint)_offset > (uint)data.Length - 5)
                    return false;

                uint nRanges = BigEndian.ReadUInt32(data, _offset + 1);
                long needed = 1L + 4 + (nRanges * 6L) + 4;
                if (needed > int.MaxValue)
                    return false;
                if ((uint)_offset > (uint)data.Length - (uint)needed)
                    return false;

                byteLength = (int)needed;
                return true;
            }
            default:
                return false;
        }
    }

    public bool TryGetFontDictIndex(int glyphId, out ushort fdIndex)
    {
        fdIndex = 0;

        if ((uint)glyphId >= (uint)_glyphCount)
            return false;

        var data = _cff.Span;
        byte format = data[_offset];
        switch (format)
        {
            case 0:
                fdIndex = data[_offset + 1 + glyphId];
                return true;

            case 3:
                return TryGetFontDictIndexFormat3(data, glyphId, out fdIndex);

            case 4:
                return TryGetFontDictIndexFormat4(data, glyphId, out fdIndex);

            default:
                return false;
        }
    }

    private bool TryGetFontDictIndexFormat3(ReadOnlySpan<byte> data, int glyphId, out ushort fdIndex)
    {
        fdIndex = 0;

        ushort nRanges = BigEndian.ReadUInt16(data, _offset + 1);
        if (nRanges == 0)
            return false;

        int rangesOffset = _offset + 3;
        int sentinelOffset = rangesOffset + (nRanges * 3);
        if ((uint)sentinelOffset > (uint)data.Length - 2)
            return false;

        ushort sentinel = BigEndian.ReadUInt16(data, sentinelOffset);
        if (sentinel == 0 || sentinel > _glyphCount)
            return false;

        int lo = 0;
        int hi = nRanges - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int recordOffset = rangesOffset + (mid * 3);
            ushort first = BigEndian.ReadUInt16(data, recordOffset);

            if (glyphId < first)
            {
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        int rangeIndex = hi;
        if (rangeIndex < 0)
            return false;

        int offset = rangesOffset + (rangeIndex * 3);
        ushort firstGlyph = BigEndian.ReadUInt16(data, offset);
        byte fd = data[offset + 2];

        ushort nextFirst = rangeIndex == nRanges - 1
            ? sentinel
            : BigEndian.ReadUInt16(data, rangesOffset + ((rangeIndex + 1) * 3));

        if (glyphId < firstGlyph || glyphId >= nextFirst)
            return false;

        fdIndex = fd;
        return true;
    }

    private bool TryGetFontDictIndexFormat4(ReadOnlySpan<byte> data, int glyphId, out ushort fdIndex)
    {
        fdIndex = 0;

        uint nRanges = BigEndian.ReadUInt32(data, _offset + 1);
        if (nRanges == 0 || nRanges > int.MaxValue)
            return false;

        int rangesOffset = _offset + 5;
        long sentinelOffsetLong = rangesOffset + (nRanges * 6L);
        if (sentinelOffsetLong > int.MaxValue)
            return false;

        int sentinelOffset = (int)sentinelOffsetLong;
        if ((uint)sentinelOffset > (uint)data.Length - 4)
            return false;

        uint sentinel = BigEndian.ReadUInt32(data, sentinelOffset);
        if (sentinel == 0 || sentinel > (uint)_glyphCount)
            return false;

        int lo = 0;
        int hi = (int)nRanges - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            long recordOffsetLong = (long)rangesOffset + ((long)mid * 6);
            if (recordOffsetLong > int.MaxValue)
                return false;

            int recordOffset = (int)recordOffsetLong;
            uint first = BigEndian.ReadUInt32(data, recordOffset);

            if ((uint)glyphId < first)
            {
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        int rangeIndex = hi;
        if (rangeIndex < 0)
            return false;

        int offset = rangesOffset + (rangeIndex * 6);
        uint firstGlyph = BigEndian.ReadUInt32(data, offset);
        ushort fd = BigEndian.ReadUInt16(data, offset + 4);

        uint nextFirst = rangeIndex == (int)nRanges - 1
            ? sentinel
            : BigEndian.ReadUInt32(data, rangesOffset + ((rangeIndex + 1) * 6));

        if ((uint)glyphId < firstGlyph || (uint)glyphId >= nextFirst)
            return false;

        fdIndex = fd;
        return true;
    }
}
