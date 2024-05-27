using FontFlat.OpenType.DataTypes;
using FontFlat.OpenType.Helper;

namespace FontFlat.OpenType.FontTables;

public record struct Table_hmtx
{
    public LongHorMetric[] hMetrics;
    public short[] leftSideBearings;
}
public record struct LongHorMetric
{
    public ushort advanceWidth;
    public short lsb;
}