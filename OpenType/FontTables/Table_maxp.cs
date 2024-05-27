using FontFlat.OpenType.DataTypes;
using FontFlat.OpenType.Helper;

namespace FontFlat.OpenType.FontTables;

public record struct Table_maxp
{
    public Version16Dot16 version;
    public ushort numGlyphs;

    public ushort? maxPoints;
    public ushort? maxContours;
    public ushort? maxCompositePoints;
    public ushort? maxCompositeContours;
    public ushort? maxZones;
    public ushort? maxTwilightPoints;
    public ushort? maxStorage;
    public ushort? maxFunctionDefs;
    public ushort? maxInstructionDefs;
    public ushort? maxStackElements;
    public ushort? maxSizeOfInstructions;
    public ushort? maxComponentElements;
    public ushort? maxComponentDepth;
}