using System.Buffers;

namespace OTFontFile2.Tables;

internal static class PackedDeltas
{
    // Control byte:
    // - bits 0..5: runLength-1 (1..64)
    // - bit 7 (0x80): deltas are zero; no data follows
    // - bit 6 (0x40): deltas are int16; else int8
    public static bool TryDecode(ReadOnlySpan<byte> data, int offset, int limit, int deltaCount, Span<short> destination, out int bytesRead)
    {
        bytesRead = 0;

        if ((uint)deltaCount > (uint)destination.Length)
            return false;

        if (offset < 0 || limit < 0)
            return false;
        if (offset > limit)
            return false;
        if ((uint)limit > (uint)data.Length)
            return false;

        int pos = offset;
        int written = 0;

        while (written < deltaCount)
        {
            if (pos >= limit)
                return false;

            byte ctrl = data[pos++];
            int runLength = (ctrl & 0x3F) + 1;
            if (runLength > deltaCount - written)
                return false;

            bool isZero = (ctrl & 0x80) != 0;
            bool isWord = (ctrl & 0x40) != 0;

            if (isZero)
            {
                destination.Slice(written, runLength).Clear();
                written += runLength;
                continue;
            }

            if (!isWord)
            {
                if (pos > limit - runLength)
                    return false;

                for (int i = 0; i < runLength; i++)
                    destination[written++] = unchecked((sbyte)data[pos++]);
            }
            else
            {
                int bytes = checked(runLength * 2);
                if (pos > limit - bytes)
                    return false;

                for (int i = 0; i < runLength; i++)
                {
                    destination[written++] = BigEndian.ReadInt16(data, pos);
                    pos += 2;
                }
            }
        }

        bytesRead = pos - offset;
        return written == deltaCount;
    }

    public static void Encode(ref ArrayBufferWriter<byte> w, ReadOnlySpan<short> deltas)
    {
        int i = 0;
        while (i < deltas.Length)
        {
            // Zero run.
            if (deltas[i] == 0)
            {
                int len = 1;
                int max = Math.Min(64, deltas.Length - i);
                while (len < max && deltas[i + len] == 0)
                    len++;

                w.GetSpan(1)[0] = (byte)(0x80 | (len - 1));
                w.Advance(1);
                i += len;
                continue;
            }

            bool canBeByte = deltas[i] >= sbyte.MinValue && deltas[i] <= sbyte.MaxValue;
            bool isWord = !canBeByte;

            int runLen = 1;
            int maxRun = Math.Min(64, deltas.Length - i);
            while (runLen < maxRun)
            {
                short d = deltas[i + runLen];
                if (d == 0)
                    break; // start a zero run next

                if (!isWord)
                {
                    if (d < sbyte.MinValue || d > sbyte.MaxValue)
                        break;
                }

                runLen++;
            }

            byte ctrl = (byte)((runLen - 1) & 0x3F);
            if (isWord)
                ctrl |= 0x40;

            w.GetSpan(1)[0] = ctrl;
            w.Advance(1);

            if (!isWord)
            {
                Span<byte> dest = w.GetSpan(runLen);
                for (int j = 0; j < runLen; j++)
                    dest[j] = unchecked((byte)(sbyte)deltas[i + j]);
                w.Advance(runLen);
            }
            else
            {
                Span<byte> dest = w.GetSpan(runLen * 2);
                for (int j = 0; j < runLen; j++)
                    BigEndian.WriteInt16(dest, j * 2, deltas[i + j]);
                w.Advance(runLen * 2);
            }

            i += runLen;
        }
    }
}
