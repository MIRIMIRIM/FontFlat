using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("cvar", 8, GenerateTryCreate = false)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("TupleVariationCountRaw", OtFieldKind.UInt16, 4)]
[OtField("OffsetToData", OtFieldKind.UInt16, 6)]
public readonly partial struct CvarTable
{
    public static bool TryCreate(TableSlice table, out CvarTable cvar)
    {
        cvar = default;

        // version(4) + tupleVariationCount(2) + offsetToData(2)
        if (table.Length < 8)
            return false;

        uint version = BigEndian.ReadUInt32(table.Span, 0);
        if ((version >> 16) != 1)
            return false;

        cvar = new CvarTable(table);
        return true;
    }
    public ushort TupleVariationCount => (ushort)(TupleVariationCountRaw & 0x0FFF);
    public bool HasSharedPointNumbers => (TupleVariationCountRaw & 0x8000) != 0;

    public bool TryGetTupleVariationStore(ushort axisCount, out TupleVariationStore store)
    {
        store = default;

        // Treat the tuple-variation record as starting at offset 4 (tupleVariationCount field),
        // but offsetToData is relative to the start of the cvar table (origin=0).
        int recordOffset = 4;
        int recordLength = _table.Length - recordOffset;
        if (recordLength < 4)
            return false;

        return TupleVariationStore.TryCreate(_table, recordOffset, recordLength, originOffset: 0, axisCount, out store);
    }
}
