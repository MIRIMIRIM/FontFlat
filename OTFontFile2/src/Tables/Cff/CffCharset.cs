namespace OTFontFile2.Tables;

internal static class CffCharset
{
    public static bool TryGetByteLength(ReadOnlySpan<byte> cffData, int offset, ushort glyphCount, out int byteLength)
    {
        byteLength = 0;

        if (glyphCount == 0)
            return false;

        if ((uint)offset >= (uint)cffData.Length)
            return false;

        byte format = cffData[offset];
        int glyphsToCover = glyphCount - 1; // .notdef omitted

        if (format == 0)
        {
            long len = 1L + (glyphsToCover * 2L);
            if (len > int.MaxValue)
                return false;
            if ((uint)offset > (uint)cffData.Length - (uint)len)
                return false;

            byteLength = (int)len;
            return true;
        }

        if (format == 1)
        {
            int pos = offset + 1;
            int covered = 0;
            while (covered < glyphsToCover)
            {
                if ((uint)pos > (uint)cffData.Length - 3)
                    return false;

                // first SID (2) + nLeft(1)
                byte nLeft = cffData[pos + 2];
                covered = checked(covered + 1 + nLeft);
                pos += 3;
            }

            if (covered != glyphsToCover)
                return false;

            byteLength = pos - offset;
            return true;
        }

        if (format == 2)
        {
            int pos = offset + 1;
            int covered = 0;
            while (covered < glyphsToCover)
            {
                if ((uint)pos > (uint)cffData.Length - 4)
                    return false;

                // first SID (2) + nLeft(2)
                ushort nLeft = BigEndian.ReadUInt16(cffData, pos + 2);
                covered = checked(covered + 1 + nLeft);
                pos += 4;
            }

            if (covered != glyphsToCover)
                return false;

            byteLength = pos - offset;
            return true;
        }

        return false;
    }
}

