using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("OS/2", 78)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("XAvgCharWidth", OtFieldKind.Int16, 2)]
[OtField("UsWeightClass", OtFieldKind.UInt16, 4)]
[OtField("UsWidthClass", OtFieldKind.UInt16, 6)]
[OtField("FsType", OtFieldKind.UInt16, 8)]
[OtField("YSubscriptXSize", OtFieldKind.Int16, 10)]
[OtField("YSubscriptYSize", OtFieldKind.Int16, 12)]
[OtField("YSubscriptXOffset", OtFieldKind.Int16, 14)]
[OtField("YSubscriptYOffset", OtFieldKind.Int16, 16)]
[OtField("YSuperscriptXSize", OtFieldKind.Int16, 18)]
[OtField("YSuperscriptYSize", OtFieldKind.Int16, 20)]
[OtField("YSuperscriptXOffset", OtFieldKind.Int16, 22)]
[OtField("YSuperscriptYOffset", OtFieldKind.Int16, 24)]
[OtField("YStrikeoutSize", OtFieldKind.Int16, 26)]
[OtField("YStrikeoutPosition", OtFieldKind.Int16, 28)]
[OtField("SFamilyClass", OtFieldKind.Int16, 30)]
[OtField("Panose1", OtFieldKind.Byte, 32)]
[OtField("Panose2", OtFieldKind.Byte, 33)]
[OtField("Panose3", OtFieldKind.Byte, 34)]
[OtField("Panose4", OtFieldKind.Byte, 35)]
[OtField("Panose5", OtFieldKind.Byte, 36)]
[OtField("Panose6", OtFieldKind.Byte, 37)]
[OtField("Panose7", OtFieldKind.Byte, 38)]
[OtField("Panose8", OtFieldKind.Byte, 39)]
[OtField("Panose9", OtFieldKind.Byte, 40)]
[OtField("Panose10", OtFieldKind.Byte, 41)]
[OtField("UlUnicodeRange1", OtFieldKind.UInt32, 42)]
[OtField("UlUnicodeRange2", OtFieldKind.UInt32, 46)]
[OtField("UlUnicodeRange3", OtFieldKind.UInt32, 50)]
[OtField("UlUnicodeRange4", OtFieldKind.UInt32, 54)]
[OtField("AchVendId", OtFieldKind.Tag, 58)]
[OtField("FsSelection", OtFieldKind.UInt16, 62)]
[OtField("UsFirstCharIndex", OtFieldKind.UInt16, 64)]
[OtField("UsLastCharIndex", OtFieldKind.UInt16, 66)]
[OtField("STypoAscender", OtFieldKind.Int16, 68)]
[OtField("STypoDescender", OtFieldKind.Int16, 70)]
[OtField("STypoLineGap", OtFieldKind.Int16, 72)]
[OtField("UsWinAscent", OtFieldKind.UInt16, 74)]
[OtField("UsWinDescent", OtFieldKind.UInt16, 76)]
public readonly partial struct Os2Table
{
    public bool TryGetCodePageRanges(out uint range1, out uint range2)
    {
        if (Version < 1 || _table.Length < 86)
        {
            range1 = 0;
            range2 = 0;
            return false;
        }

        range1 = BigEndian.ReadUInt32(_table.Span, 78);
        range2 = BigEndian.ReadUInt32(_table.Span, 82);
        return true;
    }

    public bool TryGetVersion2Fields(out short sxHeight, out short sCapHeight, out ushort usDefaultChar, out ushort usBreakChar, out ushort usMaxContext)
    {
        if (Version < 2 || _table.Length < 96)
        {
            sxHeight = 0;
            sCapHeight = 0;
            usDefaultChar = 0;
            usBreakChar = 0;
            usMaxContext = 0;
            return false;
        }

        sxHeight = BigEndian.ReadInt16(_table.Span, 86);
        sCapHeight = BigEndian.ReadInt16(_table.Span, 88);
        usDefaultChar = BigEndian.ReadUInt16(_table.Span, 90);
        usBreakChar = BigEndian.ReadUInt16(_table.Span, 92);
        usMaxContext = BigEndian.ReadUInt16(_table.Span, 94);
        return true;
    }

    public bool IsUnicodeRangeBitSet(byte bit)
    {
        if (bit > 127)
            throw new ArgumentOutOfRangeException(nameof(bit), "Valid bit indices are 0..127.");

        if (bit < 32)
            return (UlUnicodeRange1 & (1u << bit)) != 0;
        if (bit < 64)
            return (UlUnicodeRange2 & (1u << (bit - 32))) != 0;
        if (bit < 96)
            return (UlUnicodeRange3 & (1u << (bit - 64))) != 0;

        return (UlUnicodeRange4 & (1u << (bit - 96))) != 0;
    }
}
