using FontFlat.OpenType.DataTypes;
using FontFlat.OpenType.Helper;
using System.Text;

namespace FontFlat.OpenType.FontTables;

public record struct Table_name
{
    public ushort version;
    public ushort count;
    public Offset16 storageOffset;
    public NameRecord[] nameRecords;
    public ushort? langTagCount;
    public LangTagRecord[]? langTagRecords;
}

public record struct NameRecord
{
    public ushort platformID;
    public ushort encodingID;
    public ushort languageID;
    public ushort nameID;
    public ushort length;
    public Offset16 stringOffset;
}

// langTag: IETF specification BCP 47
public record struct LangTagRecord
{
    public uint length;
    public Offset16 langTagOffset;
}
