using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat12Layout
{
    internal const int HeaderLength = 16, GroupStride = 12;
}

// The group count lives on the wire, so the group rows are declared here (contract
// version 1: the generated accessor owns the ordinal gate and row addressing) and the
// ordering, glyph-bound and language rules are applied by the partial.
[BinaryTable(CmapLayout.Tag, CmapFormat12Layout.HeaderLength, ContractVersion = 1, VersionField = "Format", SupportedVersions = new uint[] { 12 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Reserved", WireType.UInt16, 2, DataField.Payload)]
[BinaryScalar("Length", WireType.UInt32, 4, DataField.Length)]
[BinaryScalar("Language", WireType.UInt32, 8, DataField.LanguageId)]
[BinaryScalar("NumGroups", WireType.UInt32, 12, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("Group", "NumGroups", CmapFormat12Layout.HeaderLength, CmapFormat12Layout.GroupStride)]
[BinaryElement("Group", "StartCharCode", WireType.UInt32, 0, DataField.CharCode)]
[BinaryElement("Group", "EndCharCode", WireType.UInt32, 4, DataField.CharCode)]
[BinaryElement("Group", "StartGlyphID", WireType.UInt32, 8, DataField.GlyphId)]
public readonly ref partial struct CmapFormat12View { }
