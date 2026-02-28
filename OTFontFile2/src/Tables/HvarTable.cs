using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("HVAR", 20, GenerateTryCreate = false)]
[OtField("MajorVersion", OtFieldKind.UInt16, 0)]
[OtField("MinorVersion", OtFieldKind.UInt16, 2)]
[OtField("ItemVariationStoreOffset", OtFieldKind.UInt32, 4)]
[OtField("AdvanceWidthMappingOffset", OtFieldKind.UInt32, 8)]
[OtField("LsbMappingOffset", OtFieldKind.UInt32, 12)]
[OtField("RsbMappingOffset", OtFieldKind.UInt32, 16)]
public readonly partial struct HvarTable
{
    public static bool TryCreate(TableSlice table, out HvarTable hvar)
    {
        hvar = default;

        // major(2) + minor(2) + 4 offsets32
        if (table.Length < 20)
            return false;

        var data = table.Span;
        uint storeOffsetU = BigEndian.ReadUInt32(data, 4);
        uint advanceMapOffsetU = BigEndian.ReadUInt32(data, 8);
        uint lsbMapOffsetU = BigEndian.ReadUInt32(data, 12);
        uint rsbMapOffsetU = BigEndian.ReadUInt32(data, 16);

        if (storeOffsetU > int.MaxValue || advanceMapOffsetU > int.MaxValue || lsbMapOffsetU > int.MaxValue || rsbMapOffsetU > int.MaxValue)
            return false;

        int storeOffset = (int)storeOffsetU;
        int advanceMapOffset = (int)advanceMapOffsetU;
        int lsbMapOffset = (int)lsbMapOffsetU;
        int rsbMapOffset = (int)rsbMapOffsetU;

        if ((uint)storeOffset > (uint)table.Length - 8)
            return false;

        if (advanceMapOffset != 0 && (uint)advanceMapOffset > (uint)table.Length - 4)
            return false;

        if (lsbMapOffset != 0 && (uint)lsbMapOffset > (uint)table.Length - 4)
            return false;

        if (rsbMapOffset != 0 && (uint)rsbMapOffset > (uint)table.Length - 4)
            return false;

        hvar = new HvarTable(table);
        return true;
    }

    public bool TryGetItemVariationStore(out ItemVariationStore store)
        => ItemVariationStore.TryCreate(_table, (int)ItemVariationStoreOffset, out store);

    public bool TryGetAdvanceWidthMapping(out DeltaSetIndexMap map)
    {
        map = default;
        uint offset = AdvanceWidthMappingOffset;
        return offset != 0 && DeltaSetIndexMap.TryCreate(_table, (int)offset, out map);
    }

    public bool TryGetLsbMapping(out DeltaSetIndexMap map)
    {
        map = default;
        uint offset = LsbMappingOffset;
        return offset != 0 && DeltaSetIndexMap.TryCreate(_table, (int)offset, out map);
    }

    public bool TryGetRsbMapping(out DeltaSetIndexMap map)
    {
        map = default;
        uint offset = RsbMappingOffset;
        return offset != 0 && DeltaSetIndexMap.TryCreate(_table, (int)offset, out map);
    }
}
