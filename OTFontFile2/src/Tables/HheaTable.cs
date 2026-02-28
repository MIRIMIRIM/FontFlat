using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("hhea", 36, GenerateBuilder = true)]
[OtField("TableVersionNumber", OtFieldKind.Fixed1616, 0, HasDefaultValue = true, DefaultValue = 0x00010000u)]
[OtField("Ascender", OtFieldKind.Int16, 4)]
[OtField("Descender", OtFieldKind.Int16, 6)]
[OtField("LineGap", OtFieldKind.Int16, 8)]
[OtField("AdvanceWidthMax", OtFieldKind.UInt16, 10)]
[OtField("MinLeftSideBearing", OtFieldKind.Int16, 12)]
[OtField("MinRightSideBearing", OtFieldKind.Int16, 14)]
[OtField("XMaxExtent", OtFieldKind.Int16, 16)]
[OtField("CaretSlopeRise", OtFieldKind.Int16, 18, HasDefaultValue = true, DefaultValue = 1)]
[OtField("CaretSlopeRun", OtFieldKind.Int16, 20)]
[OtField("CaretOffset", OtFieldKind.Int16, 22)]
[OtField("Reserved1", OtFieldKind.Int16, 24)]
[OtField("Reserved2", OtFieldKind.Int16, 26)]
[OtField("Reserved3", OtFieldKind.Int16, 28)]
[OtField("Reserved4", OtFieldKind.Int16, 30)]
[OtField("MetricDataFormat", OtFieldKind.Int16, 32)]
[OtField("NumberOfHMetrics", OtFieldKind.UInt16, 34)]
public readonly partial struct HheaTable
{
}
