using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class KernAppleSubtable2Layout
{
    internal const int HeaderLength = KernLayout.SubtableHeaderV1, RowWidthAt = 8,
        LeftClassOffsetAt = 10, RightClassOffsetAt = 12, ArrayOffsetAt = 14,
        BodyAt = KernLayout.SubtableBodyV1;
}

// The Apple v1.0 format 2 subtable (read-side typed depth, OQ-6): the v1 8-byte header
// (u32 length, coverage, tupleIndex) followed by the dialect-shared class-matrix body.
// The three offsets are subtable-relative including this header - the jellee witness's
// byte-level proof (kerningArrayOffset 16 = the body start under the 8-byte header).
// KerningArrayOffset is declared as an offset scalar without a reference: the array
// extent is derived from the premultiplied class values (OQ-7). jellee's bodies use
// the array-in-first legal non-canonical order (array 16, class tables later) - the
// W canonical re-layout is the compiler's outcome, never a read requirement.
[BinaryTable(KernLayout.Tag, KernAppleSubtable2Layout.BodyAt, ContractVersion = 1)]
[BinaryScalar("Length", WireType.UInt32, 0, DataField.Length)]
[BinaryScalar("Coverage", WireType.UInt16, 4, DataField.Flags)]
[BinaryScalar("TupleIndex", WireType.UInt16, 6, DataField.Payload)]
[BinaryScalar("RowWidth", WireType.UInt16, KernAppleSubtable2Layout.RowWidthAt, DataField.Payload)]
[BinaryScalar("LeftClassOffset", WireType.UInt16, KernAppleSubtable2Layout.LeftClassOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("RightClassOffset", WireType.UInt16, KernAppleSubtable2Layout.RightClassOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("KerningArrayOffset", WireType.UInt16, KernAppleSubtable2Layout.ArrayOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryDerived("BodyEnd", Constant = KernAppleSubtable2Layout.BodyAt)]
[BinaryReference("LeftClassTable", "LeftClassOffset", typeof(KernClassTableView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("RightClassTable", "RightClassOffset", typeof(KernClassTableView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
public readonly ref partial struct KernAppleSubtable2View { }
