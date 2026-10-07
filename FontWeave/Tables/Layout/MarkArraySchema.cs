using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MarkArrayLayout
{
    internal const uint Tag = GposLayout.Tag;
    internal const int MinimumHeader = 2, RecordStride = 4;
}

// The shared MarkArray (GPOS lookup types 4/5/6): the markCount plus the MarkRecord
// array (class + anchor offset per mark). The per-record anchors are Required - the
// spec marks the mark-record anchor with no NULL note, the explicit contrast to the
// Base/Component/Mark2 rows' "offsets may be NULL" (T4-06 section 2) - so the zero
// policy refuses a zero anchor offset natively. Offsets are MarkArray-relative
// (spec gpos.md:799), hence Origin=ViewStart on the per-item references. The class
// word stays a raw semantic value: its <classCount domain is the semantic layer's
// array-index check (OQ-T4-06-2), not a wire-level bound.
[BinaryTable(MarkArrayLayout.Tag, MarkArrayLayout.MinimumHeader, ContractVersion = 1)]
[BinaryScalar("MarkCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("MarkRecord", "MarkCount", MarkArrayLayout.MinimumHeader, MarkArrayLayout.RecordStride)]
[BinaryElement("MarkRecord", "Class", WireType.UInt16, 0, DataField.Payload)]
[BinaryElement("MarkRecord", "MarkAnchorOffset", WireType.UInt16, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = MarkArrayLayout.MinimumHeader,
    Count = "MarkCount", Stride = MarkArrayLayout.RecordStride)]
[BinaryReference("MarkAnchor", "MarkAnchorOffset", typeof(AnchorView), Array = "MarkRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "RecordsEnd")]
public readonly ref partial struct MarkArrayView { }
