using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace OTFontFile2;

internal static class BigEndian
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ReadInt16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset, 8));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadInt64(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset, 8));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt24(ReadOnlySpan<byte> data, int offset)
        => (uint)(data[offset] << 16 | data[offset + 1] << 8 | data[offset + 2]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16(Span<byte> data, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16BigEndian(data.Slice(offset, 2), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt16(Span<byte> data, int offset, short value)
        => BinaryPrimitives.WriteInt16BigEndian(data.Slice(offset, 2), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32(Span<byte> data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt32(Span<byte> data, int offset, int value)
        => BinaryPrimitives.WriteInt32BigEndian(data.Slice(offset, 4), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt64(Span<byte> data, int offset, ulong value)
        => BinaryPrimitives.WriteUInt64BigEndian(data.Slice(offset, 8), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt64(Span<byte> data, int offset, long value)
        => BinaryPrimitives.WriteInt64BigEndian(data.Slice(offset, 8), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt24(Span<byte> data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 16);
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)value;
    }
}
