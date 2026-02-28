namespace OTFontFile2.Tables;

public readonly struct Cff2TopDict
{
    private readonly TableSlice _cff;
    private readonly int _offset;
    private readonly int _length;

    private readonly int _charStringsOffset;
    private readonly int _fdArrayOffset;
    private readonly int _fdSelectOffset;

    private readonly bool _hasVarStore;
    private readonly int _varStoreOffset;

    private readonly bool _hasMaxStack;
    private readonly int _maxStack;

    private Cff2TopDict(
        TableSlice cff,
        int offset,
        int length,
        int charStringsOffset,
        int fdArrayOffset,
        int fdSelectOffset,
        bool hasVarStore,
        int varStoreOffset,
        bool hasMaxStack,
        int maxStack)
    {
        _cff = cff;
        _offset = offset;
        _length = length;

        _charStringsOffset = charStringsOffset;
        _fdArrayOffset = fdArrayOffset;
        _fdSelectOffset = fdSelectOffset;

        _hasVarStore = hasVarStore;
        _varStoreOffset = varStoreOffset;

        _hasMaxStack = hasMaxStack;
        _maxStack = maxStack;
    }

    public static bool TryCreate(TableSlice cff, int offset, int length, out Cff2TopDict topDict)
    {
        topDict = default;

        if (length <= 0)
            return false;
        if ((uint)offset > (uint)cff.Length - (uint)length)
            return false;

        var data = cff.Span.Slice(offset, length);

        int charStrings = 0;
        int fdArray = 0;
        int fdSelect = 0;

        bool hasVarStore = false;
        int varStore = 0;

        bool hasMaxStack = false;
        int maxStack = 0;

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
                    HandleTwoByteOperator(op2, operandCount, ringValues, ringIsInt, ref fdArray, ref fdSelect);

                    operandCount = 0;
                    pos += 2;
                    continue;
                }

                HandleOneByteOperator(b0, operandCount, ringValues, ringIsInt, ref charStrings, ref hasVarStore, ref varStore, ref hasMaxStack, ref maxStack);

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

        topDict = new Cff2TopDict(
            cff,
            offset,
            length,
            charStrings,
            fdArray,
            fdSelect,
            hasVarStore,
            varStore,
            hasMaxStack,
            maxStack);
        return true;
    }

    public TableSlice Table => _cff;
    public int Offset => _offset;
    public int Length => _length;

    public int CharStringsOffset => _charStringsOffset;
    public int FdArrayOffset => _fdArrayOffset;
    public int FdSelectOffset => _fdSelectOffset;

    public bool HasVarStore => _hasVarStore;
    public int VarStoreOffset => _varStoreOffset;

    public bool HasMaxStack => _hasMaxStack;
    public int MaxStack => _maxStack;

    private static void HandleOneByteOperator(
        byte op,
        int operandCount,
        ReadOnlySpan<int> ringValues,
        ReadOnlySpan<byte> ringIsInt,
        ref int charStringsOffset,
        ref bool hasVarStore,
        ref int varStoreOffset,
        ref bool hasMaxStack,
        ref int maxStack)
    {
        switch (op)
        {
            case 17: // CharStrings
                if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int cs))
                    charStringsOffset = cs;
                break;

            case 24: // VarStore
                if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int vs))
                {
                    hasVarStore = true;
                    varStoreOffset = vs;
                }
                break;

            case 25: // maxstack
                if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int ms))
                {
                    hasMaxStack = true;
                    maxStack = ms;
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
        ref int fdSelect)
    {
        switch (op2)
        {
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
