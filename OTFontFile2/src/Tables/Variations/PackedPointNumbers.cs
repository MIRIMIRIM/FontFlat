using System.Buffers;

namespace OTFontFile2.Tables;

internal static class PackedPointNumbers
{
    public static bool TryDecode(ReadOnlySpan<byte> data, int offset, int limit, out ushort[] points, out int bytesRead)
    {
        points = Array.Empty<ushort>();
        bytesRead = 0;

        if (offset < 0 || limit < 0)
            return false;
        if (offset > limit)
            return false;
        if ((uint)limit > (uint)data.Length)
            return false;
        if (offset == limit)
            return false;

        int pos = offset;
        byte first = data[pos++];

        int pointCount;
        if ((first & 0x80) != 0)
        {
            if (pos >= limit)
                return false;

            pointCount = ((first & 0x7F) << 8) | data[pos++];
        }
        else
        {
            pointCount = first;
        }

        if (pointCount < 0)
            return false;

        if (pointCount == 0)
        {
            bytesRead = pos - offset;
            points = Array.Empty<ushort>();
            return true;
        }

        var result = new ushort[pointCount];

        int remaining = pointCount;
        int prev = 0;
        int outIndex = 0;

        while (remaining > 0)
        {
            if (pos >= limit)
                return false;

            byte runHeader = data[pos++];
            bool isWord = (runHeader & 0x80) != 0;
            int runLength = (runHeader & 0x7F) + 1;
            if (runLength > remaining)
                return false;

            if (!isWord)
            {
                if (pos > limit - runLength)
                    return false;

                for (int i = 0; i < runLength; i++)
                {
                    prev = checked(prev + data[pos++]);
                    if (prev > ushort.MaxValue)
                        return false;
                    result[outIndex++] = (ushort)prev;
                }
            }
            else
            {
                int bytes = checked(runLength * 2);
                if (pos > limit - bytes)
                    return false;

                for (int i = 0; i < runLength; i++)
                {
                    ushort delta = BigEndian.ReadUInt16(data, pos);
                    pos += 2;
                    prev = checked(prev + delta);
                    if (prev > ushort.MaxValue)
                        return false;
                    result[outIndex++] = (ushort)prev;
                }
            }

            remaining -= runLength;
        }

        points = result;
        bytesRead = pos - offset;
        return true;
    }

    public static void Encode(ref ArrayBufferWriter<byte> w, ReadOnlySpan<ushort> points)
    {
        if (points.Length == 0)
        {
            w.GetSpan(1)[0] = 0;
            w.Advance(1);
            return;
        }

        ValidateStrictlyIncreasing(points);

        int count = points.Length;
        if (count <= 0x7F)
        {
            w.GetSpan(1)[0] = (byte)count;
            w.Advance(1);
        }
        else
        {
            if (count > 0x7FFF)
                throw new InvalidOperationException("Packed point numbers count must fit in 15 bits.");

            Span<byte> hdr = w.GetSpan(2);
            hdr[0] = (byte)(0x80 | (count >> 8));
            hdr[1] = (byte)count;
            w.Advance(2);
        }

        int prev = 0;
        int idx = 0;
        while (idx < points.Length)
        {
            int runStart = idx;

            bool runIsWord = false;
            int tmpPrev = prev;
            int maxRun = Math.Min(128, points.Length - idx);
            for (int i = 0; i < maxRun; i++)
            {
                int v = points[runStart + i];
                int delta = v - tmpPrev;
                if (delta < 0)
                    throw new InvalidOperationException("Packed point numbers must be strictly increasing.");

                if (delta > byte.MaxValue)
                {
                    runIsWord = true;
                    break;
                }

                tmpPrev = v;
            }

            // Extend run while delta size stays compatible (byte or word).
            int runLen = 0;
            tmpPrev = prev;
            while (idx + runLen < points.Length && runLen < 128)
            {
                int v = points[idx + runLen];
                int delta = v - tmpPrev;
                if (delta < 0)
                    throw new InvalidOperationException("Packed point numbers must be strictly increasing.");

                if (!runIsWord && delta > byte.MaxValue)
                    break;

                tmpPrev = v;
                runLen++;
            }

            byte header = (byte)((runLen - 1) & 0x7F);
            if (runIsWord)
                header |= 0x80;

            w.GetSpan(1)[0] = header;
            w.Advance(1);

            if (!runIsWord)
            {
                Span<byte> dest = w.GetSpan(runLen);
                for (int i = 0; i < runLen; i++)
                {
                    ushort p = points[idx + i];
                    dest[i] = (byte)(p - prev);
                    prev = p;
                }
                w.Advance(runLen);
            }
            else
            {
                Span<byte> dest = w.GetSpan(runLen * 2);
                for (int i = 0; i < runLen; i++)
                {
                    ushort p = points[idx + i];
                    ushort delta = (ushort)(p - prev);
                    BigEndian.WriteUInt16(dest, i * 2, delta);
                    prev = p;
                }
                w.Advance(runLen * 2);
            }

            idx += runLen;
        }
    }

    private static void ValidateStrictlyIncreasing(ReadOnlySpan<ushort> points)
    {
        ushort prev = 0;
        for (int i = 0; i < points.Length; i++)
        {
            ushort v = points[i];
            if (i != 0 && v <= prev)
                throw new InvalidOperationException("Packed point numbers must be strictly increasing.");
            prev = v;
        }
    }
}
