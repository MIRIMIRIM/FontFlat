using FontFlat.OpenType.DataTypes;
using ZLogger;

namespace FontFlat.OpenType.FontTables;

public record struct Table_head
{
    public ushort majorVersion;
    public ushort minorVersion;
    public Fixed fontRevision;          // Windows don’t use it
    public uint checksumAdjustment;
    public uint magicNumber;
    public ushort flags;
    public ushort unitsPerEm;
    public LONGDATETIME created;
    public LONGDATETIME modified;
    public short xMin;
    public short yMin;
    public short xMax;
    public short yMax;
    public ushort macStyle;             // == OS/2 table fsSelect (Windows)
    public ushort lowestRecPPEM;
    public short fontDirectionHint;     // Deprecated
    public short indexToLocFormat;
    public short glyphDataFormat;
}

public partial class Verify
{
    public void TableHead(Table_head head)
    {
        var tbl = "head";
        if (head.majorVersion != 1 || head.minorVersion != 0) { logger.ZLogError($"{tbl}: major version must be 1; minor version must be 0"); }
        if (head.magicNumber != 0x5F0F3CF5) { logger.ZLogError($"{tbl}: magicNumber must be 0x5F0F3CF5"); }
    }
}