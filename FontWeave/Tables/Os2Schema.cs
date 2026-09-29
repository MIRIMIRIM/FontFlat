using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class Os2Layout
{
    internal const uint Tag = 0x4F532F32;
    internal const int ShortLength = 68, V0Length = 78, V1Length = 86,
        V2Length = 96, V5Length = 100;
    internal static int Length(ushort version, bool shortV0 = false) => version switch
    {
        0 => shortV0 ? ShortLength : V0Length,
        1 => V1Length,
        2 or 3 or 4 => V2Length,
        5 => V5Length,
        _ => 0
    };
}

[BinaryTable(Os2Layout.Tag, 2, VersionField = "Version", SupportedVersions = new uint[] { 0, 1, 2, 3, 4, 5 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("XAvgCharWidth", WireType.Int16, 2, DataField.Payload)]
[BinaryScalar("WeightClass", WireType.UInt16, 4, DataField.Payload)]
[BinaryScalar("WidthClass", WireType.UInt16, 6, DataField.Payload)]
[BinaryScalar("FsType", WireType.UInt16, 8, DataField.Flags)]
[BinaryScalar("YSubscriptXSize", WireType.Int16, 10, DataField.Payload)]
[BinaryScalar("YSubscriptYSize", WireType.Int16, 12, DataField.Payload)]
[BinaryScalar("YSubscriptXOffset", WireType.Int16, 14, DataField.Payload)]
[BinaryScalar("YSubscriptYOffset", WireType.Int16, 16, DataField.Payload)]
[BinaryScalar("YSuperscriptXSize", WireType.Int16, 18, DataField.Payload)]
[BinaryScalar("YSuperscriptYSize", WireType.Int16, 20, DataField.Payload)]
[BinaryScalar("YSuperscriptXOffset", WireType.Int16, 22, DataField.Payload)]
[BinaryScalar("YSuperscriptYOffset", WireType.Int16, 24, DataField.Payload)]
[BinaryScalar("YStrikeoutSize", WireType.Int16, 26, DataField.Payload)]
[BinaryScalar("YStrikeoutPosition", WireType.Int16, 28, DataField.Payload)]
[BinaryScalar("SFamilyClass", WireType.Int16, 30, DataField.Payload)]
[BinaryScalar("Panose0", WireType.UInt8, 32, DataField.Payload)]
[BinaryScalar("Panose1", WireType.UInt8, 33, DataField.Payload)]
[BinaryScalar("Panose2", WireType.UInt8, 34, DataField.Payload)]
[BinaryScalar("Panose3", WireType.UInt8, 35, DataField.Payload)]
[BinaryScalar("Panose4", WireType.UInt8, 36, DataField.Payload)]
[BinaryScalar("Panose5", WireType.UInt8, 37, DataField.Payload)]
[BinaryScalar("Panose6", WireType.UInt8, 38, DataField.Payload)]
[BinaryScalar("Panose7", WireType.UInt8, 39, DataField.Payload)]
[BinaryScalar("Panose8", WireType.UInt8, 40, DataField.Payload)]
[BinaryScalar("Panose9", WireType.UInt8, 41, DataField.Payload)]
[BinaryScalar("UnicodeRange1", WireType.UInt32, 42, DataField.Payload)]
[BinaryScalar("UnicodeRange2", WireType.UInt32, 46, DataField.Payload)]
[BinaryScalar("UnicodeRange3", WireType.UInt32, 50, DataField.Payload)]
[BinaryScalar("UnicodeRange4", WireType.UInt32, 54, DataField.Payload)]
[BinaryScalar("VendorId", WireType.UInt32, 58, DataField.Tag)]
[BinaryScalar("FsSelection", WireType.UInt16, 62, DataField.Flags)]
[BinaryScalar("FirstCharIndex", WireType.UInt16, 64, DataField.Payload)]
[BinaryScalar("LastCharIndex", WireType.UInt16, 66, DataField.Payload)]
[BinaryScalar("TypoAscender", WireType.Int16, 68, DataField.Payload)]
[BinaryScalar("TypoDescender", WireType.Int16, 70, DataField.Payload)]
[BinaryScalar("TypoLineGap", WireType.Int16, 72, DataField.Payload)]
[BinaryScalar("WinAscent", WireType.UInt16, 74, DataField.Payload)]
[BinaryScalar("WinDescent", WireType.UInt16, 76, DataField.Payload)]
[BinaryScalar("CodePageRange1", WireType.UInt32, 78, DataField.Payload)]
[BinaryScalar("CodePageRange2", WireType.UInt32, 82, DataField.Payload)]
[BinaryScalar("XHeight", WireType.Int16, 86, DataField.Payload)]
[BinaryScalar("CapHeight", WireType.Int16, 88, DataField.Payload)]
[BinaryScalar("DefaultChar", WireType.UInt16, 90, DataField.Payload)]
[BinaryScalar("BreakChar", WireType.UInt16, 92, DataField.Payload)]
[BinaryScalar("MaxContext", WireType.UInt16, 94, DataField.Payload)]
[BinaryScalar("LowerOpticalPointSize", WireType.UInt16, 96, DataField.Payload)]
[BinaryScalar("UpperOpticalPointSize", WireType.UInt16, 98, DataField.Payload)]
public readonly ref partial struct Os2View { }
