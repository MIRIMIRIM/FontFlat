using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// OpenType Device table (and VariationIndex table when <see cref="DeltaFormat"/> is 0x8000).
/// </summary>
[OtSubTable(6)]
[OtField("StartSize", OtFieldKind.UInt16, 0)]
[OtField("EndSize", OtFieldKind.UInt16, 2)]
[OtField("DeltaFormat", OtFieldKind.UInt16, 4)]
public readonly partial struct DeviceTable
{
    public bool IsVariationIndex => DeltaFormat == 0x8000;

    public bool TryGetVarIdx(out VarIdx varIdx)
    {
        varIdx = default;

        if (!IsVariationIndex)
            return false;

        varIdx = new VarIdx(StartSize, EndSize);
        return true;
    }

    public bool TryGetDelta(ushort ppemSize, out sbyte delta)
    {
        delta = 0;

        ushort deltaFormat = DeltaFormat;
        if (deltaFormat == 0x8000)
            return false;

        int bitsPerValue = deltaFormat switch
        {
            1 => 2,
            2 => 4,
            3 => 8,
            _ => 0
        };

        if (bitsPerValue == 0)
            return false;

        ushort start = StartSize;
        ushort end = EndSize;
        if (start > end)
            return false;

        if (ppemSize < start || ppemSize > end)
            return true;

        int valuesPerWord = 16 / bitsPerValue;
        int index = ppemSize - start;
        int count = (end - start) + 1;
        int wordCount = (count + valuesPerWord - 1) / valuesPerWord;

        int requiredBytes = 6 + (wordCount * 2);
        if (_table.Length - _offset < requiredBytes)
            return false;

        int wordIndex = index / valuesPerWord;
        int withinWord = index - (wordIndex * valuesPerWord);

        int wordOffset = _offset + 6 + (wordIndex * 2);
        ushort word = BigEndian.ReadUInt16(_table.Span, wordOffset);

        int shift = (valuesPerWord - 1 - withinWord) * bitsPerValue;
        int mask = (1 << bitsPerValue) - 1;
        int raw = (word >> shift) & mask;

        int signed = (raw << (32 - bitsPerValue)) >> (32 - bitsPerValue);
        delta = (sbyte)signed;
        return true;
    }
}
