namespace OTFontFile2.Tables;

internal static class CffDictReader
{
    public static bool TryReadOperand(ReadOnlySpan<byte> data, ref int pos, out int value, out bool isInteger)
    {
        bool ok = TryReadOperand(data, ref pos, out value, out CffDictOperandKind kind);
        isInteger = ok && kind == CffDictOperandKind.Integer;
        return ok;
    }

    public static bool TryReadOperand(ReadOnlySpan<byte> data, ref int pos, out int value, out CffDictOperandKind kind)
    {
        value = 0;
        kind = default;

        if ((uint)pos >= (uint)data.Length)
            return false;

        byte b0 = data[pos];

        if (b0 is >= 32 and <= 246)
        {
            value = b0 - 139;
            kind = CffDictOperandKind.Integer;
            pos++;
            return true;
        }

        if (b0 is >= 247 and <= 250)
        {
            if ((uint)pos > (uint)data.Length - 2)
                return false;
            value = ((b0 - 247) * 256) + data[pos + 1] + 108;
            kind = CffDictOperandKind.Integer;
            pos += 2;
            return true;
        }

        if (b0 is >= 251 and <= 254)
        {
            if ((uint)pos > (uint)data.Length - 2)
                return false;
            value = -(((b0 - 251) * 256) + data[pos + 1] + 108);
            kind = CffDictOperandKind.Integer;
            pos += 2;
            return true;
        }

        switch (b0)
        {
            case 28:
                if ((uint)pos > (uint)data.Length - 3)
                    return false;
                value = BigEndian.ReadInt16(data, pos + 1);
                kind = CffDictOperandKind.Integer;
                pos += 3;
                return true;

            case 29:
                if ((uint)pos > (uint)data.Length - 5)
                    return false;
                value = BigEndian.ReadInt32(data, pos + 1);
                kind = CffDictOperandKind.Integer;
                pos += 5;
                return true;

            case 30:
                kind = CffDictOperandKind.Real;
                return TrySkipReal(data, ref pos);

            case 255:
                // 16.16 fixed-point (ignored by this reader, but reported via operand kind).
                if ((uint)pos > (uint)data.Length - 5)
                    return false;
                kind = CffDictOperandKind.Fixed1616;
                pos += 5;
                return true;

            default:
                return false;
        }
    }

    private static bool TrySkipReal(ReadOnlySpan<byte> data, ref int pos)
    {
        // Real number is encoded in nibbles, terminated by 0xF.
        // First byte is 30.
        if ((uint)pos >= (uint)data.Length || data[pos] != 30)
            return false;

        pos++;
        while ((uint)pos < (uint)data.Length)
        {
            byte b = data[pos++];
            int hi = (b >> 4) & 0xF;
            if (hi == 0xF)
                return true;

            int lo = b & 0xF;
            if (lo == 0xF)
                return true;
        }

        return false;
    }
}
