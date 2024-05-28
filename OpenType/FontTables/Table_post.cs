using FontFlat.OpenType.DataTypes;
using FontFlat.OpenType.Helper;

namespace FontFlat.OpenType.FontTables;

public record struct Table_post
{
    public Version16Dot16 version;
    public Fixed italicAngle;
    public FWORD underlinePosition;
    public FWORD underlineThickness;
    public uint isFixedPitch;
    public uint minMemType42;
    public uint maxMemType42;
    public uint minMemType1;
    public uint maxMemType1;

    public ushort? numGlyphs;
    public ushort[]? glyphNameIndex;

    public byte[]? offset;
}