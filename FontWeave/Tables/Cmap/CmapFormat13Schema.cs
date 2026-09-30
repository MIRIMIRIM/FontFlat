using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat13Layout
{
    internal const int HeaderLength = 16, GroupStride = 12;
}

[BinaryTable(CmapLayout.Tag, CmapFormat13Layout.HeaderLength, VersionField = "Format", SupportedVersions = new uint[] { 13 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Reserved", WireType.UInt16, 2, DataField.Payload)]
[BinaryScalar("Length", WireType.UInt32, 4, DataField.Length)]
[BinaryScalar("Language", WireType.UInt32, 8, DataField.LanguageId)]
[BinaryScalar("NumGroups", WireType.UInt32, 12, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryRecord("Group", CmapFormat13Layout.GroupStride)]
[BinaryElement("Group", "StartCharCode", WireType.UInt32, 0, DataField.CharCode)]
[BinaryElement("Group", "EndCharCode", WireType.UInt32, 4, DataField.CharCode)]
[BinaryElement("Group", "StartGlyphID", WireType.UInt32, 8, DataField.GlyphId)]
public readonly ref partial struct CmapFormat13View { }
