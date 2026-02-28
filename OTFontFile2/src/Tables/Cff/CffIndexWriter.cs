using System.Buffers;

namespace OTFontFile2.Tables;

internal interface ICffIndexObjectSource
{
    int Count { get; }
    int GetLength(int index);
    void CopyObject(int index, Span<byte> destination);
}

internal static class CffIndexWriter
{
    public static byte[] Build<TSource>(ushort count, TSource source)
        where TSource : struct, ICffIndexObjectSource
    {
        if (count == 0)
            return new byte[] { 0, 0 };

        if (source.Count != count)
            throw new ArgumentException("Source.Count mismatch.", nameof(source));

        long dataLength = 0;
        for (int i = 0; i < count; i++)
            dataLength += source.GetLength(i);

        if (dataLength > int.MaxValue)
            throw new InvalidOperationException("CFF INDEX too large.");

        int dataLen = (int)dataLength;
        uint lastOffset1Based = checked((uint)dataLen + 1);
        int offSize = GetOffSize(lastOffset1Based);

        long byteLengthLong = 2L + 1 + ((long)count + 1) * offSize + dataLen;
        if (byteLengthLong > int.MaxValue)
            throw new InvalidOperationException("CFF INDEX too large.");

        byte[] bytes = new byte[(int)byteLengthLong];
        var span = bytes.AsSpan();

        BigEndian.WriteUInt16(span, 0, count);
        span[2] = (byte)offSize;

        int offsetsOffset = 3;
        int dataOffset = offsetsOffset + ((count + 1) * offSize);

        uint offset = 1;
        for (int i = 0; i <= count; i++)
        {
            WriteOffset(span, offsetsOffset + (i * offSize), offset, offSize);
            if (i != count)
                offset = checked(offset + (uint)source.GetLength(i));
        }

        int pos = dataOffset;
        for (int i = 0; i < count; i++)
        {
            int len = source.GetLength(i);
            source.CopyObject(i, span.Slice(pos, len));
            pos += len;
        }

        return bytes;
    }

    private static int GetOffSize(uint lastOffset1Based)
    {
        if (lastOffset1Based <= 0xFF) return 1;
        if (lastOffset1Based <= 0xFFFF) return 2;
        if (lastOffset1Based <= 0xFFFFFF) return 3;
        return 4;
    }

    private static void WriteOffset(Span<byte> data, int offset, uint value, int offSize)
    {
        switch (offSize)
        {
            case 1:
                data[offset] = (byte)value;
                return;
            case 2:
                BigEndian.WriteUInt16(data, offset, (ushort)value);
                return;
            case 3:
                data[offset] = (byte)(value >> 16);
                data[offset + 1] = (byte)(value >> 8);
                data[offset + 2] = (byte)value;
                return;
            case 4:
                BigEndian.WriteUInt32(data, offset, value);
                return;
            default:
                throw new InvalidOperationException("Invalid offSize.");
        }
    }
}

