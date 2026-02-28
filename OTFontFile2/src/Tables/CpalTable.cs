using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("CPAL", 12, GenerateTryCreate = false, GenerateStorage = false)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("PaletteEntryCount", OtFieldKind.UInt16, 2)]
[OtField("PaletteCount", OtFieldKind.UInt16, 4)]
[OtField("ColorRecordCount", OtFieldKind.UInt16, 6)]
public readonly partial struct CpalTable
{
    public const ushort NoNameId = 0xFFFF;

    private readonly TableSlice _table;
    private readonly int _firstColorRecordOffset;

    // CPAL v1
    private readonly int _paletteTypeArrayOffset;
    private readonly int _paletteLabelArrayOffset;
    private readonly int _paletteEntryLabelArrayOffset;

    private CpalTable(
        TableSlice table,
        int firstColorRecordOffset,
        int paletteTypeArrayOffset,
        int paletteLabelArrayOffset,
        int paletteEntryLabelArrayOffset)
    {
        _table = table;
        _firstColorRecordOffset = firstColorRecordOffset;

        _paletteTypeArrayOffset = paletteTypeArrayOffset;
        _paletteLabelArrayOffset = paletteLabelArrayOffset;
        _paletteEntryLabelArrayOffset = paletteEntryLabelArrayOffset;
    }

    public static bool TryCreate(TableSlice table, out CpalTable cpal)
    {
        cpal = default;

        // header(12)
        if (table.Length < 12)
            return false;

        var data = table.Span;

        ushort version = BigEndian.ReadUInt16(data, 0);
        if (version > 1)
            return false;

        ushort numPaletteEntries = BigEndian.ReadUInt16(data, 2);
        ushort numPalettes = BigEndian.ReadUInt16(data, 4);
        ushort numColorRecords = BigEndian.ReadUInt16(data, 6);

        uint firstColorRecordOffsetU32 = BigEndian.ReadUInt32(data, 8);
        if (firstColorRecordOffsetU32 > int.MaxValue)
            return false;

        int firstColorRecordOffset = (int)firstColorRecordOffsetU32;
        if ((uint)firstColorRecordOffset > (uint)table.Length)
            return false;

        long colorRecordsByteLengthLong = (long)numColorRecords * 4;
        if (colorRecordsByteLengthLong > int.MaxValue)
            return false;

        int colorRecordsByteLength = (int)colorRecordsByteLengthLong;
        if ((uint)firstColorRecordOffset > (uint)table.Length - (uint)colorRecordsByteLength)
            return false;

        const int colorRecordIndicesOffset = 12;
        long afterIndicesLong = colorRecordIndicesOffset + (numPalettes * 2L);
        if (afterIndicesLong > table.Length)
            return false;

        int paletteTypeArrayOffset = 0;
        int paletteLabelArrayOffset = 0;
        int paletteEntryLabelArrayOffset = 0;

        if (version == 1)
        {
            if (afterIndicesLong > table.Length - 12)
                return false;

            int o = (int)afterIndicesLong;
            uint paletteTypeU = BigEndian.ReadUInt32(data, o + 0);
            uint paletteLabelU = BigEndian.ReadUInt32(data, o + 4);
            uint paletteEntryLabelU = BigEndian.ReadUInt32(data, o + 8);

            if (paletteTypeU > int.MaxValue || paletteLabelU > int.MaxValue || paletteEntryLabelU > int.MaxValue)
                return false;

            paletteTypeArrayOffset = (int)paletteTypeU;
            paletteLabelArrayOffset = (int)paletteLabelU;
            paletteEntryLabelArrayOffset = (int)paletteEntryLabelU;

            if (!ValidateOptionalArrayOffset(table.Length, paletteTypeArrayOffset, elementSize: 4, elementCount: numPalettes))
                return false;

            if (!ValidateOptionalArrayOffset(table.Length, paletteLabelArrayOffset, elementSize: 2, elementCount: numPalettes))
                return false;

            if (!ValidateOptionalArrayOffset(table.Length, paletteEntryLabelArrayOffset, elementSize: 2, elementCount: numPaletteEntries))
                return false;
        }

        // Validate palette start indices against numColorRecords.
        if (numPalettes != 0)
        {
            int indicesLen = checked(numPalettes * 2);
            if ((uint)colorRecordIndicesOffset > (uint)table.Length - (uint)indicesLen)
                return false;

            for (int i = 0; i < numPalettes; i++)
            {
                ushort startIndex = BigEndian.ReadUInt16(data, colorRecordIndicesOffset + (i * 2));
                uint end = (uint)startIndex + numPaletteEntries;
                if (end > numColorRecords)
                    return false;
            }
        }

        cpal = new CpalTable(
            table,
            firstColorRecordOffset,
            paletteTypeArrayOffset,
            paletteLabelArrayOffset,
            paletteEntryLabelArrayOffset);
        return true;
    }

    private const int ColorRecordIndicesOffset = 12;

    public bool IsVersion0 => Version == 0;
    public bool IsVersion1 => Version == 1;

    public int FirstColorRecordOffset => _firstColorRecordOffset;

    public readonly struct ColorRecord
    {
        public byte Blue { get; }
        public byte Green { get; }
        public byte Red { get; }
        public byte Alpha { get; }

        public ColorRecord(byte blue, byte green, byte red, byte alpha)
        {
            Blue = blue;
            Green = green;
            Red = red;
            Alpha = alpha;
        }
    }

    public bool TryGetPaletteStartIndex(int paletteIndex, out ushort startIndex)
    {
        startIndex = 0;

        ushort paletteCount = PaletteCount;
        if ((uint)paletteIndex >= paletteCount)
            return false;

        startIndex = BigEndian.ReadUInt16(_table.Span, ColorRecordIndicesOffset + (paletteIndex * 2));
        return true;
    }

    public bool TryGetColorRecord(int colorRecordIndex, out ColorRecord record)
    {
        record = default;

        ushort count = ColorRecordCount;
        if ((uint)colorRecordIndex >= count)
            return false;

        int offset = checked(_firstColorRecordOffset + (colorRecordIndex * 4));
        var data = _table.Span;

        record = new ColorRecord(
            blue: data[offset + 0],
            green: data[offset + 1],
            red: data[offset + 2],
            alpha: data[offset + 3]);
        return true;
    }

    public bool TryGetPaletteColor(int paletteIndex, int entryIndex, out ColorRecord record)
    {
        record = default;

        ushort entryCount = PaletteEntryCount;
        if ((uint)entryIndex >= entryCount)
            return false;

        if (!TryGetPaletteStartIndex(paletteIndex, out ushort start))
            return false;

        int recordIndex = start + entryIndex;
        return TryGetColorRecord(recordIndex, out record);
    }

    public bool TryGetPaletteType(int paletteIndex, out uint paletteType)
    {
        paletteType = 0;

        ushort paletteCount = PaletteCount;
        if ((uint)paletteIndex >= paletteCount)
            return false;

        int offset = _paletteTypeArrayOffset;
        if (!IsVersion1 || offset == 0)
            return true;

        paletteType = BigEndian.ReadUInt32(_table.Span, offset + (paletteIndex * 4));
        return true;
    }

    public bool TryGetPaletteLabelNameId(int paletteIndex, out ushort nameId)
    {
        nameId = NoNameId;

        ushort paletteCount = PaletteCount;
        if ((uint)paletteIndex >= paletteCount)
            return false;

        int offset = _paletteLabelArrayOffset;
        if (!IsVersion1 || offset == 0)
            return true;

        nameId = BigEndian.ReadUInt16(_table.Span, offset + (paletteIndex * 2));
        return true;
    }

    public bool TryGetPaletteEntryLabelNameId(int entryIndex, out ushort nameId)
    {
        nameId = NoNameId;

        ushort entryCount = PaletteEntryCount;
        if ((uint)entryIndex >= entryCount)
            return false;

        int offset = _paletteEntryLabelArrayOffset;
        if (!IsVersion1 || offset == 0)
            return true;

        nameId = BigEndian.ReadUInt16(_table.Span, offset + (entryIndex * 2));
        return true;
    }

    private static bool ValidateOptionalArrayOffset(int tableLength, int offset, int elementSize, int elementCount)
    {
        if (offset == 0)
            return true;

        long byteLengthLong = (long)elementSize * elementCount;
        if (byteLengthLong > int.MaxValue)
            return false;

        int byteLength = (int)byteLengthLong;
        if ((uint)offset > (uint)tableLength - (uint)byteLength)
            return false;

        return true;
    }
}
