using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat4Layout
{
    internal const int HeaderLength = 14, FieldStride = 2;
    // SegmentCount is SegCountX2/2 and the trailing GlyphIdArray is addressed through
    // self-relative IdRangeOffset values; both are bound by the partial, not this schema.
}

[BinaryTable(CmapLayout.Tag, CmapFormat4Layout.HeaderLength, VersionField = "Format", SupportedVersions = new uint[] { 4 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Length", WireType.UInt16, 2, DataField.Length)]
[BinaryScalar("Language", WireType.UInt16, 4, DataField.LanguageId)]
[BinaryScalar("SegCountX2", WireType.UInt16, 6, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("SearchRange", WireType.UInt16, 8, DataField.Payload)]
[BinaryScalar("EntrySelector", WireType.UInt16, 10, DataField.Payload)]
[BinaryScalar("RangeShift", WireType.UInt16, 12, DataField.Payload)]
[BinaryRecord("EndCode", CmapFormat4Layout.FieldStride)]
[BinaryElement("EndCode", "Value", WireType.UInt16, 0, DataField.CharCode)]
[BinaryRecord("StartCode", CmapFormat4Layout.FieldStride)]
[BinaryElement("StartCode", "Value", WireType.UInt16, 0, DataField.CharCode)]
[BinaryRecord("IdDelta", CmapFormat4Layout.FieldStride)]
[BinaryElement("IdDelta", "Value", WireType.Int16, 0, DataField.Payload)]
[BinaryRecord("IdRangeOffset", CmapFormat4Layout.FieldStride)]
[BinaryElement("IdRangeOffset", "Value", WireType.UInt16, 0, DataField.Offset)]
public readonly ref partial struct CmapFormat4View { }
