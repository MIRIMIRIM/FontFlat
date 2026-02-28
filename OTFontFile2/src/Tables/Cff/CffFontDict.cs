namespace OTFontFile2.Tables;

public readonly struct CffFontDict
{
    private readonly TableSlice _cff;
    private readonly int _offset;
    private readonly int _length;

    private readonly int _privateSize;
    private readonly int _privateOffset;

    private readonly bool _hasFontNameSid;
    private readonly int _fontNameSid;

    private CffFontDict(TableSlice cff, int offset, int length, int privateSize, int privateOffset, bool hasFontNameSid, int fontNameSid)
    {
        _cff = cff;
        _offset = offset;
        _length = length;

        _privateSize = privateSize;
        _privateOffset = privateOffset;

        _hasFontNameSid = hasFontNameSid;
        _fontNameSid = fontNameSid;
    }

    public static bool TryCreate(TableSlice cff, int offset, int length, out CffFontDict fontDict)
    {
        fontDict = default;

        if (length <= 0)
            return false;
        if ((uint)offset > (uint)cff.Length - (uint)length)
            return false;

        var data = cff.Span.Slice(offset, length);

        int privateSize = 0;
        int privateOffset = 0;

        bool hasFontNameSid = false;
        int fontNameSid = 0;

        const int ringSize = 8; // must be power-of-two
        Span<int> ringValues = stackalloc int[ringSize];
        Span<byte> ringIsInt = stackalloc byte[ringSize];

        int operandCount = 0;
        int pos = 0;
        while ((uint)pos < (uint)data.Length)
        {
            byte b0 = data[pos];

            if (b0 <= 27)
            {
                if (b0 == 12)
                {
                    if ((uint)pos >= (uint)data.Length - 1)
                        return false;

                    byte op2 = data[pos + 1];
                    HandleTwoByteOperator(op2, operandCount, ringValues, ringIsInt, ref hasFontNameSid, ref fontNameSid);

                    operandCount = 0;
                    pos += 2;
                    continue;
                }

                HandleOneByteOperator(b0, operandCount, ringValues, ringIsInt, ref privateSize, ref privateOffset);

                operandCount = 0;
                pos++;
                continue;
            }

            if (!CffDictReader.TryReadOperand(data, ref pos, out int value, out CffDictOperandKind kind))
                return false;

            int slot = operandCount & (ringSize - 1);
            ringValues[slot] = value;
            ringIsInt[slot] = (byte)(kind == CffDictOperandKind.Integer ? 1 : 0);
            operandCount++;
        }

        fontDict = new CffFontDict(cff, offset, length, privateSize, privateOffset, hasFontNameSid, fontNameSid);
        return true;
    }

    public TableSlice Table => _cff;
    public int Offset => _offset;
    public int Length => _length;

    public int PrivateSize => _privateSize;
    public int PrivateOffset => _privateOffset;

    public bool HasFontNameSid => _hasFontNameSid;
    public int FontNameSid => _fontNameSid;

    public bool TryGetPrivateDict(out CffPrivateDict privateDict)
    {
        privateDict = default;

        int size = _privateSize;
        int offset = _privateOffset;
        if (size <= 0 || offset <= 0)
            return false;

        return CffPrivateDict.TryCreate(_cff, offset, size, out privateDict);
    }

    public bool TryGetPrivateDictCff2(out Cff2PrivateDict privateDict)
    {
        privateDict = default;

        int size = _privateSize;
        int offset = _privateOffset;
        if (size <= 0 || offset <= 0)
            return false;

        return Cff2PrivateDict.TryCreate(_cff, offset, size, out privateDict);
    }

    private static void HandleOneByteOperator(
        byte op,
        int operandCount,
        ReadOnlySpan<int> ringValues,
        ReadOnlySpan<byte> ringIsInt,
        ref int privateSize,
        ref int privateOffset)
    {
        switch (op)
        {
            case 18: // Private (size, offset)
                if (TryGetIntFromEnd(1, operandCount, ringValues, ringIsInt, out int size)
                    && TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int off))
                {
                    privateSize = size;
                    privateOffset = off;
                }
                break;
        }
    }

    private static void HandleTwoByteOperator(
        byte op2,
        int operandCount,
        ReadOnlySpan<int> ringValues,
        ReadOnlySpan<byte> ringIsInt,
        ref bool hasFontNameSid,
        ref int fontNameSid)
    {
        switch (op2)
        {
            case 38: // FontName
                if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int sidValue))
                {
                    hasFontNameSid = true;
                    fontNameSid = sidValue;
                }
                break;
        }
    }

    private static bool TryGetIntFromEnd(int fromEnd, int operandCount, ReadOnlySpan<int> ringValues, ReadOnlySpan<byte> ringIsInt, out int value)
    {
        value = 0;

        if (operandCount <= fromEnd)
            return false;

        int idx = (operandCount - 1 - fromEnd) & (ringValues.Length - 1);
        if (ringIsInt[idx] == 0)
            return false;

        value = ringValues[idx];
        return true;
    }
}
