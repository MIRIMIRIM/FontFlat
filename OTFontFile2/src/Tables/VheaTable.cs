using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("vhea", 36, GenerateBuilder = true)]
[OtField("Version", OtFieldKind.Fixed1616, 0, HasDefaultValue = true, DefaultValue = 0x00010000u)]
[OtField("VertTypoAscender", OtFieldKind.Int16, 4)]
[OtField("VertTypoDescender", OtFieldKind.Int16, 6)]
[OtField("VertTypoLineGap", OtFieldKind.Int16, 8)]
[OtField("AdvanceHeightMax", OtFieldKind.UInt16, 10)]
[OtField("MinTopSideBearing", OtFieldKind.Int16, 12)]
[OtField("MinBottomSideBearing", OtFieldKind.Int16, 14)]
[OtField("YMaxExtent", OtFieldKind.Int16, 16)]
[OtField("CaretSlopeRise", OtFieldKind.Int16, 18, HasDefaultValue = true, DefaultValue = 1)]
[OtField("CaretSlopeRun", OtFieldKind.Int16, 20)]
[OtField("CaretOffset", OtFieldKind.Int16, 22)]
[OtField("Reserved1", OtFieldKind.Int16, 24)]
[OtField("Reserved2", OtFieldKind.Int16, 26)]
[OtField("Reserved3", OtFieldKind.Int16, 28)]
[OtField("Reserved4", OtFieldKind.Int16, 30)]
[OtField("MetricDataFormat", OtFieldKind.Int16, 32)]
[OtField("NumOfLongVerMetrics", OtFieldKind.UInt16, 34)]
public readonly partial struct VheaTable
{
}
