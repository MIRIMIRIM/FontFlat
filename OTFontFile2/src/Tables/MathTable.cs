using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// OpenType <c>MATH</c> table.
/// </summary>
[OtTable("MATH", 10)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("MathConstantsOffset", OtFieldKind.UInt16, 4)]
[OtField("MathGlyphInfoOffset", OtFieldKind.UInt16, 6)]
[OtField("MathVariantsOffset", OtFieldKind.UInt16, 8)]
public readonly partial struct MathTable
{
    public bool IsSupported => Version.RawValue == 0x00010000u;
}

