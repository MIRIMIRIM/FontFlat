using FontFlat.OpenType.DataTypes;
using FontFlat.OpenType.Helper;
using System;

namespace FontFlat.OpenType.FontTables;

public record struct Table_vhea
{
    public Version16Dot16 version;
    public short ascent;                // v1.1 vertTypoAscender
    public short descent;               // v1.1 vertTypoDescender
    public short lineGap;               // v1.1 vertTypoLineGap
    public short advanceHeightMax;
    public short minTopSideBearing;
    public short minBottomSideBearing;
    public short yMaxExtent;
    public short caretSlopeRise;
    public short caretSlopeRun;
    public short caretOffset;
    public short[] reserveds;
    public short metricDataFormat;
    public ushort numOfLongVerMetrics;
}