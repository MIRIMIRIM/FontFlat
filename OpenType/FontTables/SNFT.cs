using FontFlat.OpenType.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FontFlat.OpenType.FontTables;

public record struct CollectionHeader
{
    public ushort majorVersion;
    public ushort minorVersion;
    public uint numFonts;
    public Offset32[] tableDirectoryOffsets;

    public uint? dsigTag;
    public uint? dsigLength;
    public uint? dsigOffset;
}

public record struct TableDirectory
{
    public uint sfntVersion;
    public ushort numTables;
    public ushort searchRange;
    public ushort entrySelector;
    public ushort rangeShift;
}

public record struct TableRecord
{
    public Tag tableTag;
    public uint checksum;
    public Offset32 offset;
    public uint length;
}