using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MathKernInfoLayout
{
    internal const int HeaderLength = 4, CoverageOffsetAt = 0, CountAt = 2,
        RecordTopRightAt = 0, RecordTopLeftAt = 2, RecordBottomRightAt = 4, RecordBottomLeftAt = 6;
}

// MathKernInfo (T4-10 section 2): one Optional Coverage over the stride-8
// MathKernInfoRecord array, whose four corner offsets are each the spec's explicit
// "May be NULL" (Optional/ZeroAllowed per slot) resolved against the MathKernInfo
// table's own start - every record element carries the ViewStart origin like the
// BaseMinMax per-row shape. The four corner kern bodies are shared under the semantic
// layer's identity rule (STIX's 219 distinct bodies over 932 corners); the
// count==coverage coincidence is the OQ-6 hard face (Partial3's all-NULL corners
// record keeps its OQ-7 legal positive).
[BinaryTable(MathLayout.Tag, MathKernInfoLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("CoverageOffset", WireType.UInt16, MathKernInfoLayout.CoverageOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("MathKernCount", WireType.UInt16, MathKernInfoLayout.CountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("MathKernInfoRecord", "MathKernCount", MathKernInfoLayout.HeaderLength,
    MathLayout.KernInfoRecordSize)]
[BinaryElement("MathKernInfoRecord", "TopRightMathKernOffset", WireType.UInt16, MathKernInfoLayout.RecordTopRightAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryElement("MathKernInfoRecord", "TopLeftMathKernOffset", WireType.UInt16, MathKernInfoLayout.RecordTopLeftAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryElement("MathKernInfoRecord", "BottomRightMathKernOffset", WireType.UInt16, MathKernInfoLayout.RecordBottomRightAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryElement("MathKernInfoRecord", "BottomLeftMathKernOffset", WireType.UInt16, MathKernInfoLayout.RecordBottomLeftAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("RecordsEnd", Constant = MathKernInfoLayout.HeaderLength,
    Count = "MathKernCount", Stride = MathLayout.KernInfoRecordSize)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("TopRightMathKern", "TopRightMathKernOffset", typeof(MathKernView), Array = "MathKernInfoRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("TopLeftMathKern", "TopLeftMathKernOffset", typeof(MathKernView), Array = "MathKernInfoRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("BottomRightMathKern", "BottomRightMathKernOffset", typeof(MathKernView), Array = "MathKernInfoRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("BottomLeftMathKern", "BottomLeftMathKernOffset", typeof(MathKernView), Array = "MathKernInfoRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
public readonly ref partial struct MathKernInfoView { }
