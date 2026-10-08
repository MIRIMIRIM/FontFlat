using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MathKernLayout
{
    internal const int HeaderLength = 2, HeightCountAt = 0, CorrectionHeightsAt = 2;
}

// MathKern (T4-10 section 2, OQ-3's lean ruling): the height-count header over the two
// MathValueRecord arrays. kernValues has no wire count - its length is the declared
// computed count KernValueCountOf(heightCount) = heightCount + 1 (the A2 computed-count
// precedent, StaticDevice's PackedWordCount shape; the Maximum=65536 arithmetic bound),
// so heightCount=0 legally yields exactly one kern value (STIX's two real zero-height
// bodies are the witness pin). The sorted correction heights are the spec's "sorted"
// wording at the semantic layer's observation face (the OQ-5 family; the T0 rejection
// list names no order refusal for MATH); the device slots ride the per-element
// Optional reference with the whole-body floor.
[BinaryTable(MathLayout.Tag, MathKernLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("HeightCount", WireType.UInt16, MathKernLayout.HeightCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryComputed("KernValueCount", "KernValueCountOf", Inputs = new[] { "HeightCount" },
    Maximum = 65536ul, Semantic = NumberSemantic.Count,
    DiagnosticField = DataField.Count, DiagnosticSource = "HeightCount")]
[BinaryArray("CorrectionHeight", "HeightCount", MathKernLayout.CorrectionHeightsAt,
    MathLayout.ValueRecordSize)]
[BinaryElement("CorrectionHeight", "Value", WireType.Int16, MathLayout.ValueValueAt, DataField.Payload)]
[BinaryElement("CorrectionHeight", "DeviceOffset", WireType.UInt16, MathLayout.ValueDeviceOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("CorrectionHeightsEnd", Constant = MathKernLayout.CorrectionHeightsAt,
    Count = "HeightCount", Stride = MathLayout.ValueRecordSize)]
[BinaryArray("KernValue", "KernValueCount", 0, MathLayout.ValueRecordSize, Start = "CorrectionHeightsEnd")]
[BinaryElement("KernValue", "Value", WireType.Int16, MathLayout.ValueValueAt, DataField.Payload)]
[BinaryElement("KernValue", "DeviceOffset", WireType.UInt16, MathLayout.ValueDeviceOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("BodyEnd", Base = "CorrectionHeightsEnd",
    Count = "KernValueCount", Stride = MathLayout.ValueRecordSize)]
[BinaryReference("CorrectionHeightDevice", "DeviceOffset", typeof(StaticDeviceView), Array = "CorrectionHeight",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
[BinaryReference("KernValueDevice", "DeviceOffset", typeof(StaticDeviceView), Array = "KernValue",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
public readonly ref partial struct MathKernView
{
    // The OQ-3 computed-count hook: kernValues is heightCount + 1 by the spec's own
    // declaration, so the derived length never overflows a u16 height count.
    private static ReadStatus KernValueCountOf(ushort heightCount, DataLocation diagnosticLocation,
        out ulong value, out FontDiagnostic diagnostic)
    {
        value = (ulong)heightCount + 1ul;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
