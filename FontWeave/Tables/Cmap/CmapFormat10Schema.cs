using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat10Layout
{
    internal const int HeaderLength = 20, GlyphEntryStride = 2;
}

[BinaryTable(CmapLayout.Tag, CmapFormat10Layout.HeaderLength, VersionField = "Format", SupportedVersions = new uint[] { 10 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Reserved", WireType.UInt16, 2, DataField.Payload)]
[BinaryScalar("Length", WireType.UInt32, 4, DataField.Length)]
[BinaryScalar("Language", WireType.UInt32, 8, DataField.LanguageId)]
[BinaryScalar("StartCharCode", WireType.UInt32, 12, DataField.CharCode)]
[BinaryScalar("NumChars", WireType.UInt32, 16, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryRecord("GlyphEntry", CmapFormat10Layout.GlyphEntryStride)]
[BinaryElement("GlyphEntry", "Value", WireType.UInt16, 0, DataField.GlyphId)]
public readonly ref partial struct CmapFormat10View { }
