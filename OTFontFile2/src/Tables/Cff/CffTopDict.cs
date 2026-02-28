namespace OTFontFile2.Tables;

public readonly struct CffTopDict
{
    private readonly TableSlice _cff;
    private readonly int _offset;
    private readonly int _length;

    private readonly int _charStringsOffset;
    private readonly int _charsetIdOrOffset;
    private readonly int _encodingIdOrOffset;
    private readonly int _privateSize;
    private readonly int _privateOffset;
    private readonly int _fdArrayOffset;
    private readonly int _fdSelectOffset;

    private readonly bool _hasFullNameSid;
    private readonly int _fullNameSid;

    private readonly bool _hasFontNameSid;
    private readonly int _fontNameSid;

    private readonly bool _hasRos;
    private readonly int _rosRegistrySid;
    private readonly int _rosOrderingSid;
    private readonly int _rosSupplement;

    private CffTopDict(
        TableSlice cff,
        int offset,
        int length,
        int charStringsOffset,
        int charsetIdOrOffset,
        int encodingIdOrOffset,
        int privateSize,
        int privateOffset,
        int fdArrayOffset,
        int fdSelectOffset,
        bool hasFullNameSid,
        int fullNameSid,
        bool hasFontNameSid,
        int fontNameSid,
        bool hasRos,
        int rosRegistrySid,
        int rosOrderingSid,
        int rosSupplement)
    {
        _cff = cff;
        _offset = offset;
        _length = length;

        _charStringsOffset = charStringsOffset;
        _charsetIdOrOffset = charsetIdOrOffset;
        _encodingIdOrOffset = encodingIdOrOffset;
        _privateSize = privateSize;
        _privateOffset = privateOffset;
        _fdArrayOffset = fdArrayOffset;
        _fdSelectOffset = fdSelectOffset;

        _hasFullNameSid = hasFullNameSid;
        _fullNameSid = fullNameSid;

        _hasFontNameSid = hasFontNameSid;
        _fontNameSid = fontNameSid;

        _hasRos = hasRos;
        _rosRegistrySid = rosRegistrySid;
        _rosOrderingSid = rosOrderingSid;
        _rosSupplement = rosSupplement;
    }

    public static bool TryCreate(TableSlice cff, int offset, int length, out CffTopDict topDict)
    {
        if (length <= 0)
        {
            topDict = default;
            return false;
        }

        if ((uint)offset > (uint)cff.Length - (uint)length)
        {
            topDict = default;
            return false;
        }

        var data = cff.Span.Slice(offset, length);

        int charStringsOffset = 0;
        int charset = 0;
        int encoding = 0;
        int privateSize = 0;
        int privateOffset = 0;
        int fdArray = 0;
        int fdSelect = 0;

        bool hasFullNameSid = false;
        int fullNameSid = 0;

        bool hasFontNameSid = false;
        int fontNameSid = 0;

        bool hasRos = false;
        int rosRegistrySid = 0;
        int rosOrderingSid = 0;
        int rosSupplement = 0;

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
                // One-byte operator (including 12, handled here as operator).
                if (b0 == 12)
                {
                    if ((uint)pos >= (uint)data.Length - 1)
                    {
                        topDict = default;
                        return false;
                    }

                    byte b1 = data[pos + 1];
                    HandleTwoByteOperator(
                        b1,
                        operandCount,
                        ringValues,
                        ringIsInt,
                        ref fdArray,
                        ref fdSelect,
                        ref hasFontNameSid,
                        ref fontNameSid,
                        ref hasRos,
                        ref rosRegistrySid,
                        ref rosOrderingSid,
                        ref rosSupplement);

                    operandCount = 0;
                    pos += 2;
                    continue;
                }

                HandleOneByteOperator(
                    b0,
                    operandCount,
                    ringValues,
                    ringIsInt,
                    ref charset,
                    ref encoding,
                    ref charStringsOffset,
                    ref privateSize,
                    ref privateOffset,
                    ref hasFullNameSid,
                    ref fullNameSid);

                operandCount = 0;
                pos++;
                continue;
            }

            if (!CffDictReader.TryReadOperand(data, ref pos, out int value, out CffDictOperandKind kind))
            {
                topDict = default;
                return false;
            }

            int slot = operandCount & (ringSize - 1);
            ringValues[slot] = value;
            ringIsInt[slot] = (byte)(kind == CffDictOperandKind.Integer ? 1 : 0);
            operandCount++;
        }

        topDict = new CffTopDict(
            cff,
            offset,
            length,
            charStringsOffset,
            charset,
            encoding,
            privateSize,
            privateOffset,
            fdArray,
            fdSelect,
            hasFullNameSid,
            fullNameSid,
            hasFontNameSid,
            fontNameSid,
            hasRos,
            rosRegistrySid,
            rosOrderingSid,
            rosSupplement);
        return true;
    }

    public TableSlice Table => _cff;
    public int Offset => _offset;
    public int Length => _length;

    public int CharStringsOffset => _charStringsOffset;
    public int CharsetIdOrOffset => _charsetIdOrOffset;
    public int EncodingIdOrOffset => _encodingIdOrOffset;
    public int PrivateSize => _privateSize;
    public int PrivateOffset => _privateOffset;
    public int FdArrayOffset => _fdArrayOffset;
    public int FdSelectOffset => _fdSelectOffset;

    public bool HasFullNameSid => _hasFullNameSid;
    public int FullNameSid => _fullNameSid;

    public bool HasFontNameSid => _hasFontNameSid;
    public int FontNameSid => _fontNameSid;

    public bool HasRos => _hasRos;
    public int RosRegistrySid => _rosRegistrySid;
    public int RosOrderingSid => _rosOrderingSid;
    public int RosSupplement => _rosSupplement;

    public bool HasCustomCharset => _charsetIdOrOffset > 2;
    public bool HasCustomEncoding => _encodingIdOrOffset > 1;

    public bool TryGetCharStringsIndex(out CffIndex index)
    {
        index = default;
        int offset = _charStringsOffset;
        return offset > 0 && CffIndex.TryCreate(_cff, offset, out index);
    }

    public bool TryGetFdArrayIndex(out CffIndex index)
    {
        index = default;
        int offset = _fdArrayOffset;
        return offset > 0 && CffIndex.TryCreate(_cff, offset, out index);
    }

    public bool TryGetFdSelect(out CffFdSelect fdSelect)
    {
        fdSelect = default;

        int offset = _fdSelectOffset;
        if (offset <= 0)
            return false;

        if (!TryGetCharStringsIndex(out var charStrings))
            return false;

        return CffFdSelect.TryCreate(_cff, offset, charStrings.Count, out fdSelect);
    }

    public bool TryGetFontDict(int index, out CffFontDict fontDict)
    {
        fontDict = default;

        if (!TryGetFdArrayIndex(out var fdArray))
            return false;

        if (!fdArray.TryGetObjectBounds(index, out int dictOffset, out int dictLength))
            return false;

        return CffFontDict.TryCreate(_cff, dictOffset, dictLength, out fontDict);
    }

    public bool TryGetPrivateDict(out CffPrivateDict privateDict)
    {
        privateDict = default;

        int size = _privateSize;
        int offset = _privateOffset;
        if (size <= 0 || offset <= 0)
            return false;

        return CffPrivateDict.TryCreate(_cff, offset, size, out privateDict);
    }

    private static void HandleOneByteOperator(
        byte op,
        int operandCount,
        ReadOnlySpan<int> ringValues,
        ReadOnlySpan<byte> ringIsInt,
        ref int charset,
        ref int encoding,
        ref int charStringsOffset,
        ref int privateSize,
        ref int privateOffset,
        ref bool hasFullNameSid,
        ref int fullNameSid)
    {
        switch (op)
        {
            case 2: // FullName
                if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int sidValue))
                {
                    hasFullNameSid = true;
                    fullNameSid = sidValue;
                }
                break;

            case 15: // charset
                if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int charsetValue))
                    charset = charsetValue;
                break;

            case 16: // Encoding
                if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int encodingValue))
                    encoding = encodingValue;
                break;

            case 17: // CharStrings
                if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int cs))
                    charStringsOffset = cs;
                break;

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
        ref int fdArray,
        ref int fdSelect,
        ref bool hasFontNameSid,
        ref int fontNameSid,
        ref bool hasRos,
        ref int rosRegistrySid,
        ref int rosOrderingSid,
        ref int rosSupplement)
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

            case 30: // ROS
                if (TryGetIntFromEnd(2, operandCount, ringValues, ringIsInt, out int registry)
                    && TryGetIntFromEnd(1, operandCount, ringValues, ringIsInt, out int ordering)
                    && TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int supplement))
                {
                    hasRos = true;
                    rosRegistrySid = registry;
                    rosOrderingSid = ordering;
                    rosSupplement = supplement;
                }
                break;

            case 36: // FDArray
                if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int fdArrayValue))
                    fdArray = fdArrayValue;
                break;

            case 37: // FDSelect
                if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int fdSelectValue))
                    fdSelect = fdSelectValue;
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
