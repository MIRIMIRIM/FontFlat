using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(4, GenerateTryCreate = false, GenerateStorage = false)]
[OtField("Format", OtFieldKind.Byte, 0)]
[OtField("EntryFormat", OtFieldKind.Byte, 1)]
[OtField("MapCount", OtFieldKind.UInt16, 2)]
public readonly partial struct DeltaSetIndexMap
{
    private readonly TableSlice _table;
    private readonly int _offset;
    private readonly byte _entrySize;
    private readonly byte _innerIndexBitCount;
    private readonly int _mapDataOffset;

    private DeltaSetIndexMap(
        TableSlice table,
        int offset,
        byte entrySize,
        byte innerIndexBitCount,
        int mapDataOffset)
    {
        _table = table;
        _offset = offset;
        _entrySize = entrySize;
        _innerIndexBitCount = innerIndexBitCount;
        _mapDataOffset = mapDataOffset;
    }

    public static bool TryCreate(TableSlice table, int offset, out DeltaSetIndexMap map)
    {
        map = default;

        // format(1) + entryFormat(1) + mapCount(2)
        if ((uint)offset > (uint)table.Length - 4)
            return false;

        var data = table.Span;
        byte format = data[offset + 0];
        byte entryFormat = data[offset + 1];

        // Only format 0 is currently supported.
        if (format != 0)
            return false;

        ushort mapCount = BigEndian.ReadUInt16(data, offset + 2);

        // Spec: entrySize is stored in the high nibble, innerIndexBitCount in the low nibble (both +1).
        int entrySize = (entryFormat >> 4) + 1;
        int innerIndexBitCount = (entryFormat & 0x0F) + 1;

        if ((uint)entrySize is 0 or > 4)
            return false;

        int totalBits = entrySize * 8;
        if (innerIndexBitCount > totalBits)
            return false;

        long mapBytesLong = (long)mapCount * entrySize;
        if (mapBytesLong > int.MaxValue)
            return false;

        int mapBytes = (int)mapBytesLong;
        int mapDataOffset = offset + 4;
        if ((uint)mapDataOffset > (uint)table.Length - (uint)mapBytes)
            return false;

        map = new DeltaSetIndexMap(
            table,
            offset,
            entrySize: (byte)entrySize,
            innerIndexBitCount: (byte)innerIndexBitCount,
            mapDataOffset);
        return true;
    }

    public int EntrySize => _entrySize;
    public int InnerIndexBitCount => _innerIndexBitCount;

    public bool TryGetByteLength(out int byteLength)
    {
        byteLength = 0;

        if (Format != 0)
            return false;

        ushort mapCount = MapCount;
        long lenLong = 4L + ((long)mapCount * _entrySize);
        if (lenLong > int.MaxValue)
            return false;

        byteLength = (int)lenLong;
        return byteLength >= 0;
    }

    public bool TryGetVarIdx(int index, out VarIdx varIdx)
    {
        varIdx = default;

        ushort mapCount = MapCount;
        if ((uint)index >= mapCount)
            return false;

        int entryOffset = checked(_mapDataOffset + (index * _entrySize));
        if ((uint)entryOffset > (uint)_table.Length - (uint)_entrySize)
            return false;

        uint packed = ReadUIntBe(_table.Span, entryOffset, _entrySize);

        uint innerMask = (1u << _innerIndexBitCount) - 1u;
        uint inner = packed & innerMask;
        uint outer = packed >> _innerIndexBitCount;

        if (outer > ushort.MaxValue || inner > ushort.MaxValue)
            return false;

        varIdx = new VarIdx((ushort)outer, (ushort)inner);
        return true;
    }

    private static uint ReadUIntBe(ReadOnlySpan<byte> data, int offset, int size)
        => size switch
        {
            1 => data[offset],
            2 => BigEndian.ReadUInt16(data, offset),
            3 => (uint)(data[offset] << 16 | data[offset + 1] << 8 | data[offset + 2]),
            4 => BigEndian.ReadUInt32(data, offset),
            _ => 0
        };
}
