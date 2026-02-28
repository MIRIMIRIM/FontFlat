using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("MVAR", 12, GenerateTryCreate = false)]
[OtField("MajorVersion", OtFieldKind.UInt16, 0)]
[OtField("MinorVersion", OtFieldKind.UInt16, 2)]
[OtField("ItemVariationStoreOffset", OtFieldKind.UInt32, 4)]
[OtField("ValueRecordSize", OtFieldKind.UInt16, 8)]
[OtField("ValueRecordCount", OtFieldKind.UInt16, 10)]
[OtSequentialRecordArray(
    "ValueRecord",
    12,
    8,
    CountPropertyName = nameof(ValueRecordCount),
    RecordTypeName = nameof(ValueRecord),
    OutParameterName = "record",
    RecordStrideExpression = nameof(ValueRecordSize))]
public readonly partial struct MvarTable
{
    public static bool TryCreate(TableSlice table, out MvarTable mvar)
    {
        mvar = default;

        // major(2) + minor(2) + storeOffset(4) + valueRecordSize(2) + valueRecordCount(2)
        if (table.Length < 12)
            return false;

        var data = table.Span;
        ushort major = BigEndian.ReadUInt16(data, 0);
        ushort minor = BigEndian.ReadUInt16(data, 2);

        uint storeOffsetU = BigEndian.ReadUInt32(data, 4);
        if (storeOffsetU > int.MaxValue)
            return false;

        int storeOffset = (int)storeOffsetU;
        if ((uint)storeOffset > (uint)table.Length - 8)
            return false;

        ushort valueRecordSize = BigEndian.ReadUInt16(data, 8);
        ushort valueRecordCount = BigEndian.ReadUInt16(data, 10);

        if (valueRecordSize < 8)
            return false;

        long recordsBytesLong = (long)valueRecordCount * valueRecordSize;
        if (recordsBytesLong > int.MaxValue)
            return false;

        int recordsBytes = (int)recordsBytesLong;
        const int valueRecordsOffset = 12;
        if ((uint)valueRecordsOffset > (uint)table.Length - (uint)recordsBytes)
            return false;

        mvar = new MvarTable(table);
        return true;
    }

    public bool TryGetItemVariationStore(out ItemVariationStore store)
        => ItemVariationStore.TryCreate(_table, (int)ItemVariationStoreOffset, out store);

    public readonly struct ValueRecord
    {
        public Tag ValueTag { get; }
        public VarIdx DeltaSetIndex { get; }

        public ValueRecord(Tag valueTag, VarIdx deltaSetIndex)
        {
            ValueTag = valueTag;
            DeltaSetIndex = deltaSetIndex;
        }
    }
}
