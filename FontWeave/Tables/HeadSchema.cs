using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class HeadLayout
{
    internal const uint Tag = 0x68656164;
    internal const uint Version = 0x00010000;
    internal const int MinimumHeader = 4;
    internal const int Length = 54;
    internal const int VersionAt = 0, FontRevisionAt = 4, ChecksumAdjustmentAt = 8,
        MagicNumberAt = 12, FlagsAt = 16, UnitsPerEmAt = 18, CreatedAt = 20,
        ModifiedAt = 28, XMinAt = 36, YMinAt = 38, XMaxAt = 40,
        YMaxAt = 42, MacStyleAt = 44, LowestRecPpemAt = 46,
        FontDirectionHintAt = 48, IndexToLocFormatAt = 50, GlyphDataFormatAt = 52;
}

[BinaryTable(HeadLayout.Tag, HeadLayout.MinimumHeader, VersionField = "Version", SupportedVersions = new uint[] { HeadLayout.Version })]
[BinaryScalar("Version", WireType.UInt32, HeadLayout.VersionAt, DataField.Version)]
[BinaryScalar("FontRevision", WireType.Int32, HeadLayout.FontRevisionAt, DataField.FontRevision, Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryScalar("ChecksumAdjustment", WireType.UInt32, HeadLayout.ChecksumAdjustmentAt, DataField.ChecksumAdjustment)]
[BinaryScalar("MagicNumber", WireType.UInt32, HeadLayout.MagicNumberAt, DataField.MagicNumber)]
[BinaryScalar("Flags", WireType.UInt16, HeadLayout.FlagsAt, DataField.Flags)]
[BinaryScalar("UnitsPerEm", WireType.UInt16, HeadLayout.UnitsPerEmAt, DataField.UnitsPerEm)]
[BinaryScalar("Created", WireType.Int64, HeadLayout.CreatedAt, DataField.Created)]
[BinaryScalar("Modified", WireType.Int64, HeadLayout.ModifiedAt, DataField.Modified)]
[BinaryScalar("XMin", WireType.Int16, HeadLayout.XMinAt, DataField.XMin)]
[BinaryScalar("YMin", WireType.Int16, HeadLayout.YMinAt, DataField.YMin)]
[BinaryScalar("XMax", WireType.Int16, HeadLayout.XMaxAt, DataField.XMax)]
[BinaryScalar("YMax", WireType.Int16, HeadLayout.YMaxAt, DataField.YMax)]
[BinaryScalar("MacStyle", WireType.UInt16, HeadLayout.MacStyleAt, DataField.MacStyle)]
[BinaryScalar("LowestRecPpem", WireType.UInt16, HeadLayout.LowestRecPpemAt, DataField.LowestRecPpem)]
[BinaryScalar("FontDirectionHint", WireType.Int16, HeadLayout.FontDirectionHintAt, DataField.FontDirectionHint)]
[BinaryScalar("IndexToLocFormat", WireType.Int16, HeadLayout.IndexToLocFormatAt, DataField.IndexToLocFormat)]
[BinaryScalar("GlyphDataFormat", WireType.Int16, HeadLayout.GlyphDataFormatAt, DataField.GlyphDataFormat)]
public readonly ref partial struct HeadView { }
