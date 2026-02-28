using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("PCLT", 54, GenerateBuilder = true)]
[OtField("Version", OtFieldKind.Fixed1616, 0, HasDefaultValue = true, DefaultValue = 0x00010000u)]
[OtField("FontNumber", OtFieldKind.UInt32, 4)]
[OtField("Pitch", OtFieldKind.UInt16, 8)]
[OtField("XHeight", OtFieldKind.UInt16, 10)]
[OtField("Style", OtFieldKind.UInt16, 12)]
[OtField("TypeFamily", OtFieldKind.UInt16, 14)]
[OtField("CapHeight", OtFieldKind.UInt16, 16)]
[OtField("SymbolSet", OtFieldKind.UInt16, 18)]
[OtField("Typeface", OtFieldKind.Bytes, 20, Length = 16, PadByte = 0x20)]
[OtField("CharacterComplement", OtFieldKind.Bytes, 36, Length = 8, PadByte = 0x20)]
[OtField("FileName", OtFieldKind.Bytes, 44, Length = 6, PadByte = 0x20)]
[OtField("StrokeWeight", OtFieldKind.SByte, 50)]
[OtField("WidthType", OtFieldKind.SByte, 51)]
[OtField("SerifStyle", OtFieldKind.Byte, 52)]
[OtField("Reserved", OtFieldKind.Byte, 53)]
public readonly partial struct PcltTable
{
    public string GetTypefaceString() => DecodeAsciiTrim(Typeface);
    public string GetCharacterComplementString() => DecodeAsciiTrim(CharacterComplement);
    public string GetFileNameString() => DecodeAsciiTrim(FileName);

    private static string DecodeAsciiTrim(ReadOnlySpan<byte> bytes)
    {
        int len = bytes.IndexOf((byte)0);
        if (len < 0) len = bytes.Length;

        while (len > 0 && bytes[len - 1] == 0x20)
            len--;

        return len == 0 ? string.Empty : Encoding.ASCII.GetString(bytes.Slice(0, len));
    }
}
