using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat6Layout
{
    internal const int HeaderLength = 10, GlyphEntryStride = 2;
}

[BinaryTable(CmapLayout.Tag, CmapFormat6Layout.HeaderLength, VersionField = "Format", SupportedVersions = new uint[] { 6 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Length", WireType.UInt16, 2, DataField.Length)]
[BinaryScalar("Language", WireType.UInt16, 4, DataField.LanguageId)]
[BinaryScalar("FirstCode", WireType.UInt16, 6, DataField.CharCode)]
[BinaryScalar("EntryCount", WireType.UInt16, 8, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryRecord("GlyphEntry", CmapFormat6Layout.GlyphEntryStride)]
[BinaryElement("GlyphEntry", "Value", WireType.UInt16, 0, DataField.GlyphId)]
public readonly ref partial struct CmapFormat6View { }
