using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat14Layout
{
    internal const int HeaderLength = 10, RecordStride = 11,
        DefaultRangeStride = 4, UvsMappingStride = 5;
}

// The u24 fields (varSelector, startUnicodeValue, unicodeValue) have no generator
// WireType and are read by the handwritten partial as three big-endian bytes; the
// records declare only their expressible u32/u16/u8 elements.
[BinaryTable(CmapLayout.Tag, CmapFormat14Layout.HeaderLength, VersionField = "Format", SupportedVersions = new uint[] { 14 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Length", WireType.UInt32, 2, DataField.Length)]
[BinaryScalar("NumVarSelectorRecords", WireType.UInt32, 6, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryRecord("VarSelector", CmapFormat14Layout.RecordStride)]
[BinaryElement("VarSelector", "DefaultUVSOffset", WireType.UInt32, 3, DataField.Offset)]
[BinaryElement("VarSelector", "NonDefaultUVSOffset", WireType.UInt32, 7, DataField.Offset)]
[BinaryRecord("DefaultRange", CmapFormat14Layout.DefaultRangeStride)]
[BinaryElement("DefaultRange", "AdditionalCount", WireType.UInt8, 3, DataField.Count)]
[BinaryRecord("UvsMapping", CmapFormat14Layout.UvsMappingStride)]
[BinaryElement("UvsMapping", "GlyphId", WireType.UInt16, 3, DataField.GlyphId)]
public readonly ref partial struct CmapFormat14View { }
