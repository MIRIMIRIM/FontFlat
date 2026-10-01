using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat0Layout
{
    internal const int HeaderLength = 6, GlyphEntryStride = 1;
    internal const int GlyphCount = 256, SubtableLength = 262;
}

[BinaryTable(CmapLayout.Tag, CmapFormat0Layout.HeaderLength, VersionField = "Format", SupportedVersions = new uint[] { 0 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Length", WireType.UInt16, 2, DataField.Length)]
[BinaryScalar("Language", WireType.UInt16, 4, DataField.LanguageId)]
[BinaryRecord("GlyphEntry", CmapFormat0Layout.GlyphEntryStride)]
[BinaryElement("GlyphEntry", "Value", WireType.UInt8, 0, DataField.GlyphId)]
public readonly ref partial struct CmapFormat0View { }
