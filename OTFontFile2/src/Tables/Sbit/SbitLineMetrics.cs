using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(12)]
[OtField("Ascender", OtFieldKind.SByte, 0)]
[OtField("Descender", OtFieldKind.SByte, 1)]
[OtField("WidthMax", OtFieldKind.Byte, 2)]
[OtField("CaretSlopeNumerator", OtFieldKind.SByte, 3)]
[OtField("CaretSlopeDenominator", OtFieldKind.SByte, 4)]
[OtField("CaretOffset", OtFieldKind.SByte, 5)]
[OtField("MinOriginSb", OtFieldKind.SByte, 6)]
[OtField("MinAdvanceSb", OtFieldKind.SByte, 7)]
[OtField("MaxBeforeBl", OtFieldKind.SByte, 8)]
[OtField("MinAfterBl", OtFieldKind.SByte, 9)]
[OtField("Pad1", OtFieldKind.SByte, 10)]
[OtField("Pad2", OtFieldKind.SByte, 11)]
public readonly partial struct SbitLineMetrics
{
    internal static SbitLineMetrics CreateUnchecked(TableSlice table, int offset) => new(table, offset);
}
