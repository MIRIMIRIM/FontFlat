using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat4Layout
{
    internal const int HeaderLength = 14, FieldStride = 2, FieldsStart = 16;
    // SegmentCount is SegCountX2/2 by contract hook and the four segment field arrays are
    // declared with computed count and derived starts; the trailing GlyphIdArray is still
    // addressed through self-relative IdRangeOffset values bound by the partial.
}

[BinaryTable(CmapLayout.Tag, CmapFormat4Layout.HeaderLength, ContractVersion = 1, VersionField = "Format", SupportedVersions = new uint[] { 4 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Length", WireType.UInt16, 2, DataField.Length)]
[BinaryScalar("Language", WireType.UInt16, 4, DataField.LanguageId)]
[BinaryScalar("SegCountX2", WireType.UInt16, 6, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("SearchRange", WireType.UInt16, 8, DataField.Payload)]
[BinaryScalar("EntrySelector", WireType.UInt16, 10, DataField.Payload)]
[BinaryScalar("RangeShift", WireType.UInt16, 12, DataField.Payload)]
[BinaryComputed("SegmentCount", "SegmentCountOf", Inputs = new[] { "SegCountX2" }, Maximum = 32767ul,
    Semantic = NumberSemantic.Count, DiagnosticField = DataField.Count, DiagnosticSource = "SegCountX2")]
[BinaryArray("EndCode", "SegmentCount", CmapFormat4Layout.HeaderLength, CmapFormat4Layout.FieldStride)]
[BinaryElement("EndCode", "Value", WireType.UInt16, 0, DataField.CharCode)]
[BinaryDerived("StartCodeStart", Constant = CmapFormat4Layout.FieldsStart, Count = "SegmentCount", Stride = 2)]
[BinaryArray("StartCode", "SegmentCount", 0, CmapFormat4Layout.FieldStride, Start = "StartCodeStart")]
[BinaryElement("StartCode", "Value", WireType.UInt16, 0, DataField.CharCode)]
[BinaryDerived("IdDeltaStart", Constant = CmapFormat4Layout.FieldsStart, Count = "SegmentCount", Stride = 4)]
[BinaryArray("IdDelta", "SegmentCount", 0, CmapFormat4Layout.FieldStride, Start = "IdDeltaStart")]
[BinaryElement("IdDelta", "Value", WireType.Int16, 0, DataField.Payload)]
[BinaryDerived("IdRangeOffsetStart", Constant = CmapFormat4Layout.FieldsStart, Count = "SegmentCount", Stride = 6)]
[BinaryArray("IdRangeOffset", "SegmentCount", 0, CmapFormat4Layout.FieldStride, Start = "IdRangeOffsetStart")]
[BinaryElement("IdRangeOffset", "Value", WireType.UInt16, 0, DataField.Offset)]
public readonly ref partial struct CmapFormat4View { }
