namespace OTFontFile2.Tables;

public readonly struct Cff2PrivateDict
{
    private readonly TableSlice _cff;
    private readonly int _offset;
    private readonly int _length;

    private readonly int _subrsOffset;

    private Cff2PrivateDict(TableSlice cff, int offset, int length, int subrsOffset)
    {
        _cff = cff;
        _offset = offset;
        _length = length;
        _subrsOffset = subrsOffset;
    }

    public static bool TryCreate(TableSlice cff, int offset, int length, out Cff2PrivateDict dict)
    {
        dict = default;

        if (length <= 0)
            return false;
        if ((uint)offset > (uint)cff.Length - (uint)length)
            return false;

        var data = cff.Span.Slice(offset, length);

        int subrs = 0;

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

                    // Two-byte private dict operators are ignored for now.
                    operandCount = 0;
                    pos += 2;
                    continue;
                }

                if (b0 == 19) // Subrs
                {
                    if (TryGetIntFromEnd(0, operandCount, ringValues, ringIsInt, out int subrsValue))
                        subrs = subrsValue;
                }

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

        dict = new Cff2PrivateDict(cff, offset, length, subrs);
        return true;
    }

    public TableSlice Table => _cff;
    public int Offset => _offset;
    public int Length => _length;

    /// <summary>
    /// Offset to the local Subrs INDEX, relative to the start of the Private DICT data block.
    /// </summary>
    public int SubrsOffset => _subrsOffset;

    public bool TryGetSubrsIndex(out Cff2Index index)
    {
        index = default;

        int rel = _subrsOffset;
        if (rel <= 0)
            return false;

        int offset = _offset + rel;
        if (offset <= _offset)
            return false;

        return Cff2Index.TryCreate(_cff, offset, out index);
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
