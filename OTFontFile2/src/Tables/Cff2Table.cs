using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("CFF2", 5, GenerateTryCreate = false)]
[OtField("Major", OtFieldKind.Byte, 0)]
[OtField("Minor", OtFieldKind.Byte, 1)]
[OtField("HeaderSize", OtFieldKind.Byte, 2)]
[OtField("TopDictLength", OtFieldKind.UInt16, 3)]
public readonly partial struct Cff2Table
{
    public static bool TryCreate(TableSlice table, out Cff2Table cff2)
    {
        // major(1) + minor(1) + hdrSize(1) + topDictLength(2)
        if (table.Length < 5)
        {
            cff2 = default;
            return false;
        }

        byte hdrSize = table.Span[2];
        if (hdrSize < 5 || hdrSize > table.Length)
        {
            cff2 = default;
            return false;
        }

        ushort topDictLength = BigEndian.ReadUInt16(table.Span, 3);
        if ((uint)hdrSize > (uint)table.Length - topDictLength)
        {
            cff2 = default;
            return false;
        }

        cff2 = new Cff2Table(table);
        return true;
    }
    public bool TryGetTopDict(out Cff2TopDict topDict)
        => Cff2TopDict.TryCreate(_table, HeaderSize, TopDictLength, out topDict);

    public bool TryGetGlobalSubrIndex(out Cff2Index index)
    {
        index = default;

        int offset = checked(HeaderSize + TopDictLength);
        return Cff2Index.TryCreate(_table, offset, out index);
    }

    public bool TryGetCharStringsIndex(out Cff2Index index)
    {
        index = default;

        if (!TryGetTopDict(out var topDict))
            return false;

        int offset = topDict.CharStringsOffset;
        if (offset <= 0)
            return false;

        return Cff2Index.TryCreate(_table, offset, out index);
    }

    public bool TryGetFdArrayIndex(out Cff2Index index)
    {
        index = default;

        if (!TryGetTopDict(out var topDict))
            return false;

        int offset = topDict.FdArrayOffset;
        if (offset <= 0)
            return false;

        return Cff2Index.TryCreate(_table, offset, out index);
    }

    public bool TryGetFdSelect(out CffFdSelect fdSelect)
    {
        fdSelect = default;

        if (!TryGetTopDict(out var topDict))
            return false;

        int offset = topDict.FdSelectOffset;
        if (offset <= 0)
            return false;

        if (!TryGetCharStringsIndex(out var charStrings))
            return false;

        if (charStrings.Count > int.MaxValue)
            return false;

        return CffFdSelect.TryCreate(_table, offset, (int)charStrings.Count, out fdSelect);
    }

    public bool TryGetFontDict(int index, out CffFontDict fontDict)
    {
        fontDict = default;

        if (!TryGetFdArrayIndex(out var fdArray))
            return false;

        if (!fdArray.TryGetObjectBounds(index, out int dictOffset, out int dictLength))
            return false;

        return CffFontDict.TryCreate(_table, dictOffset, dictLength, out fontDict);
    }

    public bool TryGetVarStore(out ItemVariationStore store)
    {
        store = default;

        if (!TryGetTopDict(out var topDict))
            return false;

        if (!topDict.HasVarStore)
            return false;

        int offset = topDict.VarStoreOffset;
        if (offset <= 0)
            return false;

        return ItemVariationStore.TryCreate(_table, offset, out store);
    }
}
