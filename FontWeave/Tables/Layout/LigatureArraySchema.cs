using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class LigatureArrayLayout
{
    internal const uint Tag = GposLayout.Tag;
    internal const int MinimumHeader = 2, OffsetStride = 2;
}

// The MarkLigPos ligature side (GPOS lookup type 5): one LigatureArray of Offset16
// attach bodies, each a LigatureAttach (componentCount plus the classCount-wide
// anchor rows). The array-to-attach hop is a declarable reference array with the
// Required null policy (no spec NULL note, T4-06 section 2). The attach body's
// ComponentRecord rows have the parent-driven dynamic stride (classCount x 2 bytes,
// OQ-T4-06-1's generator gap), so the view declares the count scalar only and the
// row walk plus the per-row Optional anchors stay the semantic partial's hand-written
// boundary (the PairSet value-row precedent) - zero real witnesses for NULL row
// anchors, the boundary is synthetic-pinned (facts R5).
[BinaryTable(LigatureArrayLayout.Tag, LigatureArrayLayout.MinimumHeader, ContractVersion = 1)]
[BinaryScalar("LigatureCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("LigatureAttachOffsetRecord", "LigatureCount", LigatureArrayLayout.MinimumHeader,
    LigatureArrayLayout.OffsetStride)]
[BinaryElement("LigatureAttachOffsetRecord", "LigatureAttachOffset", WireType.UInt16, 0,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = LigatureArrayLayout.MinimumHeader,
    Count = "LigatureCount", Stride = LigatureArrayLayout.OffsetStride)]
[BinaryReference("LigatureAttach", "LigatureAttachOffset", typeof(LigatureAttachView),
    Array = "LigatureAttachOffsetRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
public readonly ref partial struct LigatureArrayView { }

// One ligature's attach body (GPOS lookup type 5): the component count plus the
// component x class anchor-offset rows whose stride the owning subtable's classCount
// drives (OQ-T4-06-1: count declared, rows hand-walked). Offsets are
// LigatureAttach-relative and the row anchors are Optional/ZeroAllowed - spec's
// explicit "offsets may be NULL" (T4-06 section 2; zero real witnesses).
[BinaryTable(LigatureArrayLayout.Tag, LigatureArrayLayout.MinimumHeader, ContractVersion = 1)]
[BinaryScalar("ComponentCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
public readonly ref partial struct LigatureAttachView { }
