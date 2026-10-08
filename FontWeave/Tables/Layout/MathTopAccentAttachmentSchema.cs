using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MathTopAccentAttachmentLayout
{
    internal const int HeaderLength = 4, CoverageOffsetAt = 0, CountAt = 2;
}

// MathTopAccentAttachment (T4-10 section 2): the wire twin of MathItalicsCorrectionInfo
// with the spec's "Must be the same" modality - the count==coverage coincidence is the
// OQ-6 hard face at the semantic layer, while the declaration stays the same optional
// shape (Partial3's empty table keeps its OQ-7 legal positive).
[BinaryTable(MathLayout.Tag, MathTopAccentAttachmentLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("CoverageOffset", WireType.UInt16, MathTopAccentAttachmentLayout.CoverageOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("TopAccentAttachmentCount", WireType.UInt16, MathTopAccentAttachmentLayout.CountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("TopAccentAttachment", "TopAccentAttachmentCount", MathTopAccentAttachmentLayout.HeaderLength,
    MathLayout.ValueRecordSize)]
[BinaryElement("TopAccentAttachment", "Value", WireType.Int16, MathLayout.ValueValueAt, DataField.Payload)]
[BinaryElement("TopAccentAttachment", "DeviceOffset", WireType.UInt16, MathLayout.ValueDeviceOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("RecordsEnd", Constant = MathTopAccentAttachmentLayout.HeaderLength,
    Count = "TopAccentAttachmentCount", Stride = MathLayout.ValueRecordSize)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("Device", "DeviceOffset", typeof(StaticDeviceView), Array = "TopAccentAttachment",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
public readonly ref partial struct MathTopAccentAttachmentView { }
