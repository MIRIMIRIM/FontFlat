using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("VVAR", 24, GenerateTryCreate = false)]
[OtField("MajorVersion", OtFieldKind.UInt16, 0)]
[OtField("MinorVersion", OtFieldKind.UInt16, 2)]
[OtField("ItemVariationStoreOffset", OtFieldKind.UInt32, 4)]
[OtField("AdvanceHeightMappingOffset", OtFieldKind.UInt32, 8)]
[OtField("TsbMappingOffset", OtFieldKind.UInt32, 12)]
[OtField("BsbMappingOffset", OtFieldKind.UInt32, 16)]
[OtField("VorgMappingOffset", OtFieldKind.UInt32, 20)]
public readonly partial struct VvarTable
{
    public static bool TryCreate(TableSlice table, out VvarTable vvar)
    {
        vvar = default;

        // major(2) + minor(2) + 5 offsets32
        if (table.Length < 24)
            return false;

        var data = table.Span;
        uint storeOffsetU = BigEndian.ReadUInt32(data, 4);
        uint advMapOffsetU = BigEndian.ReadUInt32(data, 8);
        uint tsbMapOffsetU = BigEndian.ReadUInt32(data, 12);
        uint bsbMapOffsetU = BigEndian.ReadUInt32(data, 16);
        uint vorgMapOffsetU = BigEndian.ReadUInt32(data, 20);

        if (storeOffsetU > int.MaxValue || advMapOffsetU > int.MaxValue || tsbMapOffsetU > int.MaxValue || bsbMapOffsetU > int.MaxValue || vorgMapOffsetU > int.MaxValue)
            return false;

        int storeOffset = (int)storeOffsetU;
        int advMapOffset = (int)advMapOffsetU;
        int tsbMapOffset = (int)tsbMapOffsetU;
        int bsbMapOffset = (int)bsbMapOffsetU;
        int vorgMapOffset = (int)vorgMapOffsetU;

        if ((uint)storeOffset > (uint)table.Length - 8)
            return false;

        if (advMapOffset != 0 && (uint)advMapOffset > (uint)table.Length - 4)
            return false;

        if (tsbMapOffset != 0 && (uint)tsbMapOffset > (uint)table.Length - 4)
            return false;

        if (bsbMapOffset != 0 && (uint)bsbMapOffset > (uint)table.Length - 4)
            return false;

        if (vorgMapOffset != 0 && (uint)vorgMapOffset > (uint)table.Length - 4)
            return false;

        vvar = new VvarTable(table);
        return true;
    }

    public bool TryGetItemVariationStore(out ItemVariationStore store)
        => ItemVariationStore.TryCreate(_table, (int)ItemVariationStoreOffset, out store);

    public bool TryGetAdvanceHeightMapping(out DeltaSetIndexMap map)
    {
        map = default;
        uint offset = AdvanceHeightMappingOffset;
        return offset != 0 && DeltaSetIndexMap.TryCreate(_table, (int)offset, out map);
    }

    public bool TryGetTsbMapping(out DeltaSetIndexMap map)
    {
        map = default;
        uint offset = TsbMappingOffset;
        return offset != 0 && DeltaSetIndexMap.TryCreate(_table, (int)offset, out map);
    }

    public bool TryGetBsbMapping(out DeltaSetIndexMap map)
    {
        map = default;
        uint offset = BsbMappingOffset;
        return offset != 0 && DeltaSetIndexMap.TryCreate(_table, (int)offset, out map);
    }

    public bool TryGetVorgMapping(out DeltaSetIndexMap map)
    {
        map = default;
        uint offset = VorgMappingOffset;
        return offset != 0 && DeltaSetIndexMap.TryCreate(_table, (int)offset, out map);
    }
}
