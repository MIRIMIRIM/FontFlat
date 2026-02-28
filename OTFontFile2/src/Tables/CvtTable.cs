using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("cvt ", 0, GenerateTryCreate = false)]
public readonly partial struct CvtTable
{
    public static bool TryCreate(TableSlice table, out CvtTable cvt)
    {
        if ((table.Length & 1) != 0)
        {
            cvt = default;
            return false;
        }

        cvt = new CvtTable(table);
        return true;
    }

    public int ValueCount => _table.Length / 2;

    public bool TryGetValue(int index, out short value)
    {
        value = 0;

        int count = ValueCount;
        if ((uint)index >= (uint)count)
            return false;

        int offset = index * 2;
        value = BigEndian.ReadInt16(_table.Span, offset);
        return true;
    }
}
