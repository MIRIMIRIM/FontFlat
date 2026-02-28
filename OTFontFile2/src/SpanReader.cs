using System.Runtime.CompilerServices;

namespace OTFontFile2;

internal readonly ref struct SpanReader
{
    private readonly ReadOnlySpan<byte> _data;

    public SpanReader(ReadOnlySpan<byte> data) => _data = data;

    public int Length => _data.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadUInt16(int offset, out ushort value)
    {
        if ((uint)offset > (uint)_data.Length - 2)
        {
            value = 0;
            return false;
        }

        value = BigEndian.ReadUInt16(_data, offset);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadInt16(int offset, out short value)
    {
        if ((uint)offset > (uint)_data.Length - 2)
        {
            value = 0;
            return false;
        }

        value = BigEndian.ReadInt16(_data, offset);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadUInt32(int offset, out uint value)
    {
        if ((uint)offset > (uint)_data.Length - 4)
        {
            value = 0;
            return false;
        }

        value = BigEndian.ReadUInt32(_data, offset);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadInt32(int offset, out int value)
    {
        if ((uint)offset > (uint)_data.Length - 4)
        {
            value = 0;
            return false;
        }

        value = BigEndian.ReadInt32(_data, offset);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadTag(int offset, out Tag tag)
    {
        if (!TryReadUInt32(offset, out uint value))
        {
            tag = default;
            return false;
        }

        tag = new Tag(value);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySlice(int offset, int length, out ReadOnlySpan<byte> slice)
    {
        slice = default;
        if ((uint)offset > (uint)_data.Length)
            return false;
        if ((uint)length > (uint)(_data.Length - offset))
            return false;

        slice = _data.Slice(offset, length);
        return true;
    }
}

