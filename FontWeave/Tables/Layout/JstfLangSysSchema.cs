using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class JstfLangSysLayout
{
    internal const int HeaderLength = 2, CountAt = 0, OffsetsAt = 2, OffsetStride = 2;
}

// One JstfLangSys (T4-09 section 2): the priority offset array in priority order
// (the array order is the semantic order; no value domain to check). The per-item
// Required references ride the LookupList Array="LookupOffset" shape; jstfPriorityCount
// zero is the OQ-5 legal empty form (no spec NULL note on the entries and no zero
// count prohibition - the T4-04 seqLookupCount=0 reasoning). This is a different
// layout from LayoutLangSysView (priority offsets, not feature indices), so a separate
// view is the one-layout rule's correct shape, not a parallel reader.
[BinaryTable(JstfLayout.Tag, JstfLangSysLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("PriorityCount", WireType.UInt16, JstfLangSysLayout.CountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("PriorityOffset", "PriorityCount", JstfLangSysLayout.OffsetsAt,
    JstfLangSysLayout.OffsetStride)]
[BinaryElement("PriorityOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = JstfLangSysLayout.OffsetsAt,
    Count = "PriorityCount", Stride = JstfLangSysLayout.OffsetStride)]
[BinaryReference("Priority", "Offset", typeof(JstfPriorityView), Array = "PriorityOffset",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "RecordsEnd")]
public readonly ref partial struct JstfLangSysView { }
