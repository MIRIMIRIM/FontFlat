using FontFlat.OpenType.DataTypes;
using FontFlat.OpenType.Helper;

namespace FontFlat.OpenType.FontTables;

public record struct Table_vmtx
{
    public LongVerMetric[] vMetrics;
    public short[]? topSideBearings;
}

public record struct LongVerMetric
{
    public ushort advanceHeight;
    public short topSideBearing;
}