using FontFlat.OpenType.DataTypes;
using FontFlat.OpenType.Helper;

namespace FontFlat.OpenType.FontTables;

public record struct Table_hhea
{
    public ushort majorVersion;
    public ushort minorVersion;
    public FWORD ascender;
    public FWORD descender;
    public FWORD lineGap;
    public UFWORD advanceWidthMax;
    public FWORD minLeftSideBearing;
    public FWORD minRightSideBearing;
    public FWORD xMaxExtent;
    public short caretSlopeRise;
    public short caretSlopeRun;
    public short caretOffset;
    public short[] reserveds;
    public short metricDataFormat;
    public ushort numberOfHMetrics;
}