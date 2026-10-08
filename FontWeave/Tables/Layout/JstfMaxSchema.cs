using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class JstfMaxLayout
{
    internal const int HeaderLength = 2, CountAt = 0, OffsetsAt = 2, OffsetStride = 2;
}

// One JstfMax (T4-09 section 2): the count plus the lookup offset array in design
// order (never checked - the spec names no order for this array). The per-item
// references are deliberately not declared (OQ-6): the targets are GPOS-shaped
// lookups, so the semantic layer opens them through the existing kind-aware
// LookupView (the LayoutListViews.TryOpenLookup consumption shape) instead of a
// second lookup declaration - the AGENTS one-layout rule and the generator's missing
// fixed-role-reference binding leave the opener handwritten, and the declaration
// stops at the counted offset array.
[BinaryTable(JstfLayout.Tag, JstfMaxLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("LookupCount", WireType.UInt16, JstfMaxLayout.CountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("LookupOffset", "LookupCount", JstfMaxLayout.OffsetsAt,
    JstfMaxLayout.OffsetStride)]
[BinaryElement("LookupOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = JstfMaxLayout.OffsetsAt,
    Count = "LookupCount", Stride = JstfMaxLayout.OffsetStride)]
public readonly ref partial struct JstfMaxView { }
