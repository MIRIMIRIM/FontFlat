using FontFlat.OpenType.DataTypes;
using FontFlat.OpenType.FontTables;
using FontFlat.OpenType.Helper;
using System;

namespace FontFlat.OpenType;

public partial class OTFont(BigEndianBinaryReader _reader, int _offset, ReaderFlag _flag)
{
    private readonly BigEndianBinaryReader reader = _reader;
    private readonly int offset = _offset;
    private ReaderFlag flag = _flag;

    public TableDirectory Directory;
    public TableRecord[] Records = [];

    public Table_head? Head;
    public Table_name? Name;
    public Table_OS_2? OS_2;
    public Table_maxp? Maxp;
    public Table_hhea? Hhea;
    public Table_hmtx? Hmtx;
    public Table_post? Post;

    public void ReadPackets()
    {
        reader.BaseStream.Seek(offset, SeekOrigin.Begin);
        Directory = Read.ReadTableDirectory(reader);
        Records = new TableRecord[Directory.numTables];

        for (var i = 0; i < Directory.numTables; i++)
        {
            Records[i] = Read.ReadTableRecord(reader);
        }
    }

    public TableRecord GetTableRecord(ReadOnlySpan<byte> tag)
    {
        TableRecord? record = null;
        foreach (var rec in Records)
        {
            if (rec.tableTag.AsSpan().SequenceEqual(tag))
            {
                record = rec;
                break;
            }
        }
        if (record == null) { throw new Exception($"Not have table '{tag.ToString()}'"); }
        return (TableRecord)record;
    }

}
