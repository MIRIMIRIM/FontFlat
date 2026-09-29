using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class PostLayout
{
    internal const uint Tag = 0x706F7374;
    internal const uint Version10 = 0x00010000, Version20 = 0x00020000,
        Version25 = 0x00025000, Version30 = 0x00030000;
    internal const int HeaderLength = 32;
}

[BinaryTable(PostLayout.Tag, 4, VersionField = "Version", SupportedVersions = new uint[] {
    PostLayout.Version10, PostLayout.Version20, PostLayout.Version25, PostLayout.Version30 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("ItalicAngleRaw", WireType.Int32, 4, DataField.Payload, Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryScalar("UnderlinePosition", WireType.Int16, 8, DataField.Payload)]
[BinaryScalar("UnderlineThickness", WireType.Int16, 10, DataField.Payload)]
[BinaryScalar("IsFixedPitch", WireType.UInt32, 12, DataField.Payload)]
[BinaryScalar("MinMemType42", WireType.UInt32, 16, DataField.Payload)]
[BinaryScalar("MaxMemType42", WireType.UInt32, 20, DataField.Payload)]
[BinaryScalar("MinMemType1", WireType.UInt32, 24, DataField.Payload)]
[BinaryScalar("MaxMemType1", WireType.UInt32, 28, DataField.Payload)]
[BinaryScalar("NumGlyphs", WireType.UInt16, 32, DataField.NumGlyphs, Semantic = NumberSemantic.Count)]
[BinaryArray("GlyphNameIndex", "NumGlyphs", 34, 2)]
[BinaryElement("GlyphNameIndex", "Index", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryArray("GlyphOffset", "NumGlyphs", 34, 1)]
[BinaryElement("GlyphOffset", "Offset", WireType.Int8, 0, DataField.GlyphId)]
[BinaryDerived("StringDataStart", Constant = 34, Count = "NumGlyphs", Stride = 2)]
public readonly ref partial struct PostView { }
