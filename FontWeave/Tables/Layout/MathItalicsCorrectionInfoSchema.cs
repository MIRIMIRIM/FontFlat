using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MathItalicsCorrectionInfoLayout
{
    internal const int HeaderLength = 4, CoverageOffsetAt = 0, CountAt = 2;
}

// MathItalicsCorrectionInfo (T4-10 section 2): one Optional Coverage over the counted
// MathValueRecord array (the BaseValues/JstfLangSys offset-array shape with the
// Anchor fmt3 per-item Optional device reference). The coverage/count coincidence is
// the spec's "Should" modality - the OQ-6 observation at the semantic layer, never a
// declaration face; the empty table (coverageOffset=0 + count=0, Partial3's real
// shape) stays the OQ-7 legal positive. The RecordsEnd floor keeps the coverage and
// every device edge strictly forward.
[BinaryTable(MathLayout.Tag, MathItalicsCorrectionInfoLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("CoverageOffset", WireType.UInt16, MathItalicsCorrectionInfoLayout.CoverageOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("ItalicsCorrectionCount", WireType.UInt16, MathItalicsCorrectionInfoLayout.CountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("ItalicsCorrection", "ItalicsCorrectionCount", MathItalicsCorrectionInfoLayout.HeaderLength,
    MathLayout.ValueRecordSize)]
[BinaryElement("ItalicsCorrection", "Value", WireType.Int16, MathLayout.ValueValueAt, DataField.Payload)]
[BinaryElement("ItalicsCorrection", "DeviceOffset", WireType.UInt16, MathLayout.ValueDeviceOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("RecordsEnd", Constant = MathItalicsCorrectionInfoLayout.HeaderLength,
    Count = "ItalicsCorrectionCount", Stride = MathLayout.ValueRecordSize)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("Device", "DeviceOffset", typeof(StaticDeviceView), Array = "ItalicsCorrection",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
public readonly ref partial struct MathItalicsCorrectionInfoView { }
