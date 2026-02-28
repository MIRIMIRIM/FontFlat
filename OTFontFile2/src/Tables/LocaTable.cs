using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("loca", 0, GenerateTryCreate = false)]
public readonly partial struct LocaTable
{
    public static bool TryCreate(TableSlice table, out LocaTable loca)
    {
        loca = new LocaTable(table);
        return table.Length != 0;
    }

    public bool TryGetGlyphOffsetLength(ushort glyphId, short indexToLocFormat, ushort numGlyphs, out int offset, out int length)
    {
        offset = 0;
        length = 0;

        if (glyphId >= numGlyphs)
            return false;

        var data = _table.Span;
        int entryCount = numGlyphs + 1;

        if (indexToLocFormat == 0)
        {
            int bytes = entryCount * 2;
            if (data.Length < bytes)
                return false;

            int o0 = glyphId * 2;
            int o1 = o0 + 2;

            ushort off0 = BigEndian.ReadUInt16(data, o0);
            ushort off1 = BigEndian.ReadUInt16(data, o1);

            int start = off0 * 2;
            int end = off1 * 2;
            if (end < start)
                return false;

            offset = start;
            length = end - start;
            return true;
        }

        if (indexToLocFormat == 1)
        {
            int bytes = entryCount * 4;
            if (data.Length < bytes)
                return false;

            int o0 = glyphId * 4;
            int o1 = o0 + 4;

            uint off0 = BigEndian.ReadUInt32(data, o0);
            uint off1 = BigEndian.ReadUInt32(data, o1);

            if (off0 > int.MaxValue || off1 > int.MaxValue)
                return false;

            int start = (int)off0;
            int end = (int)off1;
            if (end < start)
                return false;

            offset = start;
            length = end - start;
            return true;
        }

        return false;
    }
}
