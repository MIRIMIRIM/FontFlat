using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("head", 54, GenerateBuilder = true)]
[OtField("TableVersionNumber", OtFieldKind.Fixed1616, 0, HasDefaultValue = true, DefaultValue = 0x00010000u)]
[OtField("FontRevision", OtFieldKind.Fixed1616, 4, HasDefaultValue = true, DefaultValue = 0x00010000u)]
[OtField("CheckSumAdjustment", OtFieldKind.UInt32, 8, InBuilder = false)]
[OtField("MagicNumber", OtFieldKind.UInt32, 12, HasDefaultValue = true, DefaultValue = 0x5F0F3CF5u)]
[OtField("Flags", OtFieldKind.UInt16, 16)]
[OtField("UnitsPerEm", OtFieldKind.UInt16, 18, HasDefaultValue = true, DefaultValue = 1000)]
[OtField("Created", OtFieldKind.Int64, 20)]
[OtField("Modified", OtFieldKind.Int64, 28)]
[OtField("XMin", OtFieldKind.Int16, 36)]
[OtField("YMin", OtFieldKind.Int16, 38)]
[OtField("XMax", OtFieldKind.Int16, 40)]
[OtField("YMax", OtFieldKind.Int16, 42)]
[OtField("MacStyle", OtFieldKind.UInt16, 44)]
[OtField("LowestRecPPEM", OtFieldKind.UInt16, 46)]
[OtField("FontDirectionHint", OtFieldKind.Int16, 48)]
[OtField("IndexToLocFormat", OtFieldKind.Int16, 50)]
[OtField("GlyphDataFormat", OtFieldKind.Int16, 52)]
public readonly partial struct HeadTable
{
    public DateTime GetCreatedUtc() => LongDateTime.FromSecondsSince1904Utc(Created);
    public DateTime GetModifiedUtc() => LongDateTime.FromSecondsSince1904Utc(Modified);
}
