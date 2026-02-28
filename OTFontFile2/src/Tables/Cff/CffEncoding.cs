namespace OTFontFile2.Tables;

internal static class CffEncoding
{
    public static bool TryGetByteLength(ReadOnlySpan<byte> cffData, int offset, out int byteLength)
    {
        byteLength = 0;

        if ((uint)offset >= (uint)cffData.Length)
            return false;

        byte formatByte = cffData[offset];
        byte format = (byte)(formatByte & 0x7F);
        bool hasSupplement = (formatByte & 0x80) != 0;

        int baseLen;
        if (format == 0)
        {
            if ((uint)offset > (uint)cffData.Length - 2)
                return false;
            int nCodes = cffData[offset + 1];
            baseLen = 2 + nCodes;
        }
        else if (format == 1)
        {
            if ((uint)offset > (uint)cffData.Length - 2)
                return false;
            int nRanges = cffData[offset + 1];
            baseLen = 2 + (nRanges * 2);
        }
        else
        {
            return false;
        }

        if ((uint)offset > (uint)cffData.Length - (uint)baseLen)
            return false;

        int len = baseLen;
        if (hasSupplement)
        {
            int pos = offset + baseLen;
            if ((uint)pos >= (uint)cffData.Length)
                return false;

            int nSups = cffData[pos];
            long suppLenLong = 1L + (nSups * 3L);
            if (suppLenLong > int.MaxValue)
                return false;

            int suppLen = (int)suppLenLong;
            if ((uint)pos > (uint)cffData.Length - (uint)suppLen)
                return false;

            len = checked(len + suppLen);
        }

        byteLength = len;
        return true;
    }
}

