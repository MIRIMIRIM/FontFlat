using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class KernSubtable2Layout
{
    internal const int HeaderLength = KernLayout.SubtableHeaderV0, RowWidthAt = 6,
        LeftClassOffsetAt = 8, RightClassOffsetAt = 10, ArrayOffsetAt = 12,
        BodyAt = KernLayout.SubtableBodyV0;
}

// One OT v0 format 2 subtable (class matrix): the shared three-word header, rowWidth
// and the three subtable-relative offsets (OT: "beginning of this subtable" - the
// jellee Apple witness proves byte-level that the same rule holds under the v1 8-byte
// header). The class tables are declared Required references (no NULL note); the
// kerning array has no declared reference at all - its extent is data-derived from the
// premultiplied class values (the card's OQ-7 semantic derivation, the cmap keep
// family), so the array window and the rowWidth/class-value relation stay in the
// semantic partial. The numGlyphs domain of firstGlyph+nGlyphs is the OQ-4 observation.
[BinaryTable(KernLayout.Tag, KernSubtable2Layout.BodyAt, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { KernLayout.Version0 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Length", WireType.UInt16, 2, DataField.Length)]
[BinaryScalar("Coverage", WireType.UInt16, 4, DataField.Flags)]
[BinaryScalar("RowWidth", WireType.UInt16, KernSubtable2Layout.RowWidthAt, DataField.Payload)]
[BinaryScalar("LeftClassOffset", WireType.UInt16, KernSubtable2Layout.LeftClassOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("RightClassOffset", WireType.UInt16, KernSubtable2Layout.RightClassOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("KerningArrayOffset", WireType.UInt16, KernSubtable2Layout.ArrayOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryDerived("BodyEnd", Constant = KernSubtable2Layout.BodyAt)]
[BinaryReference("LeftClassTable", "LeftClassOffset", typeof(KernClassTableView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("RightClassTable", "RightClassOffset", typeof(KernClassTableView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
public readonly ref partial struct KernSubtable2View { }
